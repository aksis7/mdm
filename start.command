#!/bin/bash
ROOT="$(cd -- "$(dirname -- "$0")" && pwd)"
exec "$ROOT/start.sh" "$@"
