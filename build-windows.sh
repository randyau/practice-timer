#!/usr/bin/env bash
# Runs the Windows-side build from WSL. Usage: ./build-windows.sh [build|test|run|publish]
cd "$(dirname "$0")" || exit 1
exec cmd.exe /c "$(wslpath -w "$PWD/build.cmd")" "${1:-build}"
