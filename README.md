# Practice Timer

A Windows 11 stopwatch for kids and parents that can time *actual* practice. In Auto-listen mode it hears the
instrument through a chosen microphone, runs the clock only while someone is playing, and ignores metronomes.
Spec: [SPEC.md](SPEC.md).

## Build and test (from WSL2)

```
dotnet test                      # logic tests (Core), runs on Linux
scripts/publish-win.sh           # self-contained single-file exe -> publish/win-x64/PracticeTimer.exe
scripts/publish-win.sh --framework   # smaller, needs the .NET 8 Desktop Runtime on Windows
```

SDK 10 is pinned in `global.json`; the app targets `net8.0-windows` and is cross-built from Linux.

## Layout

- `src/PracticeTimer.Core` - all logic, no Windows dependencies: clock state machine, audio analysis, burst and
  metronome detection and learning, persistence.
- `src/PracticeTimer` - WPF app: NAudio (WASAPI) capture, view models, windows.
- `tests/PracticeTimer.Core.Tests` - xUnit tests driven by synthetic audio (`Signals/SignalGen.cs`).

Data lives in `%AppData%\PracticeTimer` (`settings.json`, `history.json`, `metronomes.json`). Audio is never stored.
