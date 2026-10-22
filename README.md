# Practice Timer

A Windows 11 stopwatch for kids and parents that can time *actual* practice. In Auto-listen mode it hears the
instrument through a chosen microphone, runs the clock only while someone is playing, and ignores metronomes.
Spec: [SPEC.md](SPEC.md).

## Build and test (from WSL2)

```
dotnet test                      # logic tests (Core), runs on Linux
dotnet build src/PracticeTimer   # compiles the WPF app on Linux too (it can't be run there)
scripts/publish-win.sh           # self-contained single-file exe -> publish/win-x64/PracticeTimer.exe
scripts/publish-win.sh --framework   # smaller, needs the .NET 8 Desktop Runtime on Windows
```

SDK 10 is pinned in `global.json`; the app targets `net8.0-windows` and is cross-built from Linux.

## Layout

- `src/PracticeTimer.Core` - all logic, no Windows dependencies: clock state machine, audio analysis, burst and
  metronome detection and learning, persistence.
- `src/PracticeTimer` - WPF app: NAudio (WASAPI) capture, view models, windows.
- `tests/PracticeTimer.Core.Tests` - xUnit tests driven by synthetic audio (`Signals/SignalGen.cs`) and by recordings in `Fixtures/`.
- `scripts/smoke-win.sh` - launches the published exe on Windows in a temporary data folder and checks it starts, lists microphones, ticks and logs no errors.
- `tools/Replay` - replays a .wav through the real pipeline and prints what it decided (`dotnet run --project tools/Replay -- <file.wav> --auto`).

## Behaviour at a glance

- Auto mode times from the audio itself (sample timestamps), so a busy UI or system clock change can't skew it.
  Trailing quiet is never counted; pipeline restarts, learning and calibration end the current run cleanly.
- Metronomes are recognised by sound (a numeric fingerprint), not tempo. Teach one with **Learn metronome**; up to
  four are saved. Auto-learned ones are tentative, session-only, and can't change a saved one.
- Closing the window or logging off saves the session automatically. Reset asks first.
- Microphone loss, sleep, or a mic that stops sending audio pauses the timer and leaves a warning until you act.
- Steady room noise that starts mid-session (a fan) stops counting as playing after about 30 s; Calibrate in Settings
  does it immediately.
- Files are written atomically; corrupt or newer-format files are set aside, never deleted, and a file that is locked
  at startup is never overwritten with defaults. See
  [SPEC.md](SPEC.md#reliability) for details and known limits.
- Only one copy runs at a time. Unexpected errors are logged to `%AppData%\PracticeTimer\error.log`.

Data lives in `%AppData%\PracticeTimer` (`settings.json`, `history.json`, `metronomes.json`). Audio is never stored.
