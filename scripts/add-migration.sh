#!/bin/sh
set -eu

if [ "${2:-}" = "--as-repository-owner" ]; then
  if [ "$#" -ne 2 ]; then
    echo "Invalid helper invocation." >&2
    exit 2
  fi
  as_repository_owner=true
elif [ "$#" -eq 1 ]; then
  as_repository_owner=false
else
  echo "Usage: docker compose run --rm migration-add <MigrationName>" >&2
  exit 2
fi

migration_name=$1
case "$migration_name" in
  ''|[-]*|*[!A-Za-z0-9_]*)
    echo "Migration name must contain only letters, digits, and underscores, and cannot start with a hyphen." >&2
    exit 2
    ;;
  [0-9]*)
    echo "Migration name must start with a letter or underscore." >&2
    exit 2
    ;;
esac

# The repository is bind-mounted. On Linux, write generated files as the
# repository owner's numeric user so Docker does not leave root-owned files.
if [ "$as_repository_owner" = false ] && [ "$(id -u)" -eq 0 ]; then
  source_uid=$(stat -c '%u' /src)
  source_gid=$(stat -c '%g' /src)
  if [ "$source_uid" -ne 0 ]; then
    if ! command -v setpriv >/dev/null 2>&1; then
      echo "setpriv is required to create migration files with the repository owner's permissions." >&2
      exit 1
    fi
    exec setpriv --reuid="$source_uid" --regid="$source_gid" --clear-groups \
      env HOME=/tmp DOTNET_CLI_HOME=/tmp/dotnet NUGET_PACKAGES=/tmp/nuget \
      DOTNET_CLI_TELEMETRY_OPTOUT=1 \
      /bin/sh "$0" "$migration_name" --as-repository-owner
  fi
fi

dotnet tool restore
dotnet restore src/MiniPdm.Storage/MiniPdm.Storage.csproj

dotnet ef migrations add "$migration_name" \
  --project src/MiniPdm.Storage/MiniPdm.Storage.csproj \
  --startup-project src/MiniPdm.Storage/MiniPdm.Storage.csproj \
  --context PdmDbContext
