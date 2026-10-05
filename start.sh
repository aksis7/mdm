#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
export PDM_BACKEND_DIR="$ROOT"
export PDM_BUILD_DESKTOP_IN_DOCKER=1
exec "$ROOT/scripts/launch-native-unix.sh" "$@"
