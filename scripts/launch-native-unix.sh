#!/usr/bin/env bash
set -euo pipefail

HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ "$(uname -s)" == Darwin ]]; then
  mkdir -p "$HOME/Library/Logs/MiniPdm"
  exec > >(tee -a "$HOME/Library/Logs/MiniPdm/launcher.log") 2>&1
fi
printf 'Mini-PDM: preparing launch. The first build can take several minutes.\n'
BACKEND_DIR="${PDM_BACKEND_DIR:-$HERE/backend}"
API_PORT="${PDM_API_PORT:-5000}"
export PDM_API_PORT="$API_PORT"
API_URL="http://127.0.0.1:${API_PORT}"
PROJECT_NAME="${PDM_COMPOSE_PROJECT_NAME:-cad}"
STARTUP_TIMEOUT="${PDM_STARTUP_TIMEOUT_SECONDS:-180}"

fail() {
  printf 'Mini-PDM: %s\n' "$*" >&2
  if [[ "$(uname -s)" == Darwin ]] && command -v osascript >/dev/null 2>&1; then
    osascript -e 'display dialog "Mini-PDM could not start. See ~/Library/Logs/MiniPdm/launcher.log for details." buttons {"OK"} with title "Mini-PDM"' >/dev/null 2>&1 || true
  fi
  exit 1
}
cleanup_native_build() {
  if [[ -n "${PDM_NATIVE_BUILD_DIR:-}" && -d "$PDM_NATIVE_BUILD_DIR" ]]; then
    rm -rf -- "$PDM_NATIVE_BUILD_DIR"
  fi
}
if ! command -v docker >/dev/null 2>&1 && [[ "$(uname -s)" == Darwin ]]; then
  for candidate in /usr/local/bin/docker /opt/homebrew/bin/docker /Applications/Docker.app/Contents/Resources/bin/docker; do
    if [[ -x "$candidate" ]]; then PATH="$(dirname "$candidate"):$PATH"; export PATH; break; fi
  done
fi
command -v docker >/dev/null 2>&1 || fail 'Docker is required. Install Docker Desktop or Docker Engine, then retry.'
command -v curl >/dev/null 2>&1 || fail 'curl is required by this launcher.'
if ! docker info >/dev/null 2>&1 && [[ "$(uname -s)" == Darwin ]]; then
  [[ -d /Applications/Docker.app ]] && open -a Docker || true
  for _ in {1..45}; do docker info >/dev/null 2>&1 && break; sleep 2; done
fi
docker info >/dev/null 2>&1 || fail 'Docker is installed but its daemon is unavailable. Start Docker and retry.'
[[ -f "$BACKEND_DIR/docker-compose.yml" ]] || fail "Backend sources are missing: $BACKEND_DIR"

build_inputs=(src docker .config Dockerfile Directory.Build.props global.json MiniPdm.sln docker-compose.yml compose.native.yml)
source_stamp=''
if command -v git >/dev/null 2>&1; then
  revision_refs=()
  for path in "${build_inputs[@]}"; do revision_refs+=("HEAD:$path"); done
  if revisions="$(git -C "$BACKEND_DIR" rev-parse "${revision_refs[@]}" 2>/dev/null)" &&
      git -C "$BACKEND_DIR" diff --quiet HEAD -- "${build_inputs[@]}" &&
      [[ -z "$(git -C "$BACKEND_DIR" ls-files --others --exclude-standard -- "${build_inputs[@]}")" ]]; then
    source_stamp="$(printf '%s\n' "$revisions" | git hash-object --stdin)"
  fi
fi
force_rebuild="${PDM_REBUILD:-0}"
mkdir -p "$BACKEND_DIR/artifacts/native"

if [[ "${PDM_BUILD_DESKTOP_IN_DOCKER:-}" == 1 ]]; then
  case "$(uname -s):$(uname -m)" in
    Linux:x86_64|Linux:amd64) rid=linux-x64 ;;
    Linux:aarch64|Linux:arm64) rid=linux-arm64 ;;
    Darwin:x86_64) rid=osx-x64 ;;
    Darwin:arm64|Darwin:aarch64) rid=osx-arm64 ;;
    *) fail "Unsupported host architecture: $(uname -s) $(uname -m). Supported: Linux/macOS x64 and arm64." ;;
  esac
  command -v docker >/dev/null 2>&1 && docker buildx version >/dev/null 2>&1 || fail 'Docker Buildx is required to build the native Desktop without installing the .NET SDK.'
  cache_dir="$BACKEND_DIR/artifacts/native/$rid"
  if [[ "$force_rebuild" == 1 || -z "$source_stamp" || ! -x "$cache_dir/MiniPdm.Desktop" || ! -f "$cache_dir/.source-stamp" || "$(cat "$cache_dir/.source-stamp")" != "$source_stamp" ]]; then
    native_dir="$(mktemp -d "$BACKEND_DIR/artifacts/native/$rid.XXXXXX")" || fail 'Could not create a temporary native build output directory.'
    PDM_NATIVE_BUILD_DIR="$native_dir"
    trap cleanup_native_build EXIT
    printf 'Mini-PDM: building Desktop for %s in Docker...\n' "$rid"
    docker buildx build --target native-export --build-arg "PDM_DESKTOP_RID=$rid" --output "type=local,dest=$native_dir" "$BACKEND_DIR" || fail 'Docker could not build the native Desktop. Existing backend services were not stopped.'
    [[ -f "$native_dir/MiniPdm.Desktop" ]] || fail "Docker export did not produce the native executable for $rid."
    chmod +x "$native_dir/MiniPdm.Desktop"
    rm -rf -- "$cache_dir"
    mv "$native_dir" "$cache_dir"
    PDM_NATIVE_BUILD_DIR=''
    [[ -z "$source_stamp" ]] || printf '%s\n' "$source_stamp" > "$cache_dir/.source-stamp"
  else
    printf 'Mini-PDM: using built Desktop for %s.\n' "$rid"
  fi
  PDM_DESKTOP_EXECUTABLE="$cache_dir/MiniPdm.Desktop"
  export PDM_DESKTOP_EXECUTABLE
fi

compose_args=(--project-directory "$BACKEND_DIR" -p "$PROJECT_NAME" -f "$BACKEND_DIR/docker-compose.yml" -f "$BACKEND_DIR/compose.native.yml")
[[ -f "$BACKEND_DIR/.env" ]] && compose_args+=(--env-file "$BACKEND_DIR/.env")
backend_stamp="$BACKEND_DIR/artifacts/native/backend-$PROJECT_NAME.source-stamp"
backend_rebuilt=0
if [[ "$force_rebuild" == 1 || -z "$source_stamp" || ! -f "$backend_stamp" || "$(cat "$backend_stamp")" != "$source_stamp" ]]; then
  printf 'Mini-PDM: building and starting API and database...\n'
  docker compose "${compose_args[@]}" up -d --build api || fail 'Could not start the API and its database/migration dependencies. Check Docker output above.'
  backend_rebuilt=1
else
  printf 'Mini-PDM: starting existing API and database...\n'
  if ! docker compose "${compose_args[@]}" up -d --no-build api; then
    docker compose "${compose_args[@]}" up -d --build api || fail 'Could not start the API and its database/migration dependencies. Check Docker output above.'
    backend_rebuilt=1
  fi
fi

ready=0
deadline=$((SECONDS + STARTUP_TIMEOUT))
while (( SECONDS < deadline )); do
  if curl --silent --show-error --fail --max-time 2 "$API_URL/health" >/dev/null 2>&1; then ready=1; break; fi
  sleep 1
done
[[ "$ready" == 1 ]] || fail "API did not become healthy within ${STARTUP_TIMEOUT} seconds at $API_URL/health. Check Docker logs with: docker compose --project-directory '$BACKEND_DIR' -p '$PROJECT_NAME' -f '$BACKEND_DIR/docker-compose.yml' -f '$BACKEND_DIR/compose.native.yml' logs api"
if [[ "$backend_rebuilt" == 1 && -n "$source_stamp" ]]; then printf '%s\n' "$source_stamp" > "$backend_stamp"; fi

unset PDM_BROWSER_PICKER
export PDM_API_BASE_URL="$API_URL"
printf 'Mini-PDM: API ready at %s. Opening Desktop...\n' "$API_URL"
if [[ -n "${PDM_DESKTOP_EXECUTABLE:-}" && -x "$PDM_DESKTOP_EXECUTABLE" ]]; then
  "$PDM_DESKTOP_EXECUTABLE" "$@"
  exit $?
elif [[ -x "$HERE/desktop/MiniPdm.Desktop" ]]; then
  exec "$HERE/desktop/MiniPdm.Desktop" "$@"
elif [[ -x "$HERE/../Resources/desktop/MiniPdm.Desktop" ]]; then
  exec "$HERE/../Resources/desktop/MiniPdm.Desktop" "$@"
else
  fail 'Desktop application executable was not found in this distribution.'
fi
