#!/usr/bin/env bash
# Launches the published Windows exe on the Windows desktop with its own data folder, waits for it to come up, and checks
# that it started, found a microphone list, is ticking and logged no errors. Saves a screenshot of the app window.
# Usage: scripts/smoke-win.sh [--no-publish] [seconds-to-wait]
set -euo pipefail
cd "$(dirname "$0")/.."
PUBLISH=true; [[ "${1:-}" == "--no-publish" ]] && { PUBLISH=false; shift; }
WAIT="${1:-8}"
EXE="publish/win-x64/PracticeTimer.exe"
SHOT="${SMOKE_SHOT:-/tmp/practice-timer-smoke.png}"

if powershell.exe -NoProfile -Command "if (Get-Process PracticeTimer -ErrorAction SilentlyContinue) { exit 1 }" ; then :; else
  echo "PracticeTimer is already running; close it first (the smoke test uses a single-instance app)." >&2; exit 2
fi
$PUBLISH && scripts/publish-win.sh >/dev/null
[[ -f "$EXE" ]] || { echo "No $EXE; run without --no-publish." >&2; exit 2; }

WEXE="$(wslpath -w "$PWD/$EXE")"
WDATA="$(powershell.exe -NoProfile -Command '[IO.Path]::Combine([IO.Path]::GetTempPath(), "practice-timer-smoke")' | tr -d '\r')"
WSHOT="$(wslpath -w "$SHOT")"
rm -rf "$(wslpath -u "$WDATA")"

powershell.exe -NoProfile -Command "
\$ErrorActionPreference = 'Stop'
\$env:PRACTICETIMER_DATA = '$WDATA'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public class W { [DllImport(\"user32.dll\")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport(\"user32.dll\")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  public struct RECT { public int L, T, R, B; } }
'@
\$p = Start-Process -FilePath '$WEXE' -PassThru
Start-Sleep -Seconds $WAIT
\$p.Refresh()
if (\$p.HasExited) { Write-Output 'RESULT exited'; exit 0 }
Write-Output ('RESULT responding=' + \$p.Responding + ' window=' + (\$p.MainWindowHandle -ne 0))
if (\$p.MainWindowHandle -ne 0) {
  \$r = New-Object W+RECT; [W]::GetWindowRect(\$p.MainWindowHandle, [ref]\$r) | Out-Null
  \$bmp = New-Object System.Drawing.Bitmap (\$r.R - \$r.L), (\$r.B - \$r.T)
  \$g = [System.Drawing.Graphics]::FromImage(\$bmp); \$dc = \$g.GetHdc()
  [W]::PrintWindow(\$p.MainWindowHandle, \$dc, 2) | Out-Null
  \$g.ReleaseHdc(\$dc); \$g.Dispose(); \$bmp.Save('$WSHOT'); \$bmp.Dispose()
}
Stop-Process -Id \$p.Id -Force
" | tr -d '\r' > /tmp/smoke.out

cat /tmp/smoke.out
DATA="$(wslpath -u "$WDATA")"
echo "--- trace.log"; cat "$DATA/trace.log" 2>/dev/null || echo "(missing)"
echo "--- error.log"; cat "$DATA/error.log" 2>/dev/null || echo "(none)"
fail=0
grep -q "RESULT responding=True window=True" /tmp/smoke.out || { echo "FAIL: window missing or not responding"; fail=1; }
grep -q "devices listed: [1-9]" "$DATA/trace.log" 2>/dev/null || { echo "FAIL: no microphones listed"; fail=1; }
grep -q "first tick" "$DATA/trace.log" 2>/dev/null || { echo "FAIL: timer never ticked"; fail=1; }
[[ -f "$DATA/error.log" ]] && { echo "FAIL: error.log was written"; fail=1; }
[[ $fail == 0 ]] && echo "SMOKE OK (screenshot: $SHOT)"
exit $fail
