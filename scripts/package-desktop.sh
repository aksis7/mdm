#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$ROOT/artifacts/desktop}"
if [[ $# -ge 2 ]]; then RIDS=("$2"); else RIDS=(win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64); fi
mkdir -p "$OUT"

for rid in "${RIDS[@]}"; do
  bundle="$OUT/mini-pdm-$rid"
  rm -rf "$bundle"
  mkdir -p "$bundle/backend" "$bundle/desktop"
  rsync -a --exclude='.git/' --exclude='**/bin/' --exclude='**/obj/' --include='.env.example' --exclude='.env' --exclude='.env.*' --exclude='cad-imports/*' --exclude='artifacts/' "$ROOT/" "$bundle/backend/"
  dotnet publish "$ROOT/src/MiniPdm.Desktop/MiniPdm.Desktop.csproj" -c Release -r "$rid" --self-contained true -m:1 -o "$bundle/desktop"
  if [[ "$rid" == osx-* ]]; then
    app="$bundle/Mini-PDM.app/Contents"
    mkdir -p "$app/MacOS" "$app/Resources"
    mv "$bundle/backend" "$app/Resources/backend"
    mv "$bundle/desktop" "$app/Resources/desktop"
    cp "$ROOT/scripts/launch-native-unix.sh" "$app/Resources/launch-native-unix.sh"
    cat > "$app/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict><key>CFBundleExecutable</key><string>Mini-PDM</string><key>CFBundleIdentifier</key><string>local.minipdm.desktop</string><key>CFBundleName</key><string>Mini-PDM</string><key>CFBundlePackageType</key><string>APPL</string></dict></plist>
PLIST
    cat > "$app/MacOS/Mini-PDM" <<'WRAPPER'
#!/bin/sh
RESOURCES="$(cd -- "$(dirname -- "$0")/../Resources" && pwd)"
PDM_BACKEND_DIR="$RESOURCES/backend" exec "$RESOURCES/launch-native-unix.sh" "$@"
WRAPPER
    chmod +x "$app/MacOS/Mini-PDM" "$app/Resources/launch-native-unix.sh"
  else
    cp "$ROOT/scripts/launch-native-unix.sh" "$bundle/launch-native.sh"
    cp "$ROOT/scripts/launch-native.ps1" "$bundle/launch-native.ps1"
    cp "$ROOT/scripts/launch-native.cmd" "$bundle/launch-native.cmd"
    chmod +x "$bundle/launch-native.sh"
  fi
  if [[ "$rid" == win-* ]]; then
    rm -f "$OUT/mini-pdm-$rid.zip"
    (cd "$OUT" && zip -qr "mini-pdm-$rid.zip" "$(basename "$bundle")")
  else
    tar -C "$OUT" -czf "$OUT/mini-pdm-$rid.tar.gz" "$(basename "$bundle")"
  fi
done
