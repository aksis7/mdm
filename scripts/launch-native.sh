#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
PDM_BACKEND_DIR="${PDM_BACKEND_DIR:-$ROOT}"
export PDM_BACKEND_DIR
PDM_BUILD_DESKTOP_IN_DOCKER=1
export PDM_BUILD_DESKTOP_IN_DOCKER
exec "$ROOT/scripts/launch-native-unix.sh" "$@"
