#!/usr/bin/env bash
# Cross-publishes the WPF app from WSL2 as a self-contained single-file win-x64 exe (no SDK needed on Windows).
# Usage: scripts/publish-win.sh [--framework]   (--framework needs the .NET 8 Desktop Runtime on Windows)
set -euo pipefail
cd "$(dirname "$0")/.."
SELF=true; [[ "${1:-}" == "--framework" ]] && SELF=false
OUT=publish/win-x64
rm -rf "$OUT"
dotnet publish src/PracticeTimer/PracticeTimer.csproj -c Release -r win-x64 --self-contained "$SELF" \
  -p:PublishSingleFile=true -p:EnableWindowsTargeting=true -o "$OUT" --nologo -v q
echo "Built: $(wslpath -w "$PWD/$OUT")\\PracticeTimer.exe"
