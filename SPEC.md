# Practice Timer: Spec

A simple Windows app for kids and parents to track how long a student has actually been practicing an instrument. It is a stopwatch that can optionally run itself by listening to the microphone.

**Status:** implemented and covered by synthetic-audio tests. Not yet tuned or validated against real recordings (see [Validation](#validation-and-acceptance-targets)). Statements below describe what the code does; where the code deliberately falls short of the original intent it is listed under [Known deviations and limits](#known-deviations-and-limits).

## Contents
- [Stack](#stack) · [Main window](#main-window) · [Auto-listen detection](#auto-listen-detection) · [Metronome filtering](#metronome-filtering) · [Timer rules](#timer-rules)
- [Reliability](#reliability) · [Threading and data ownership](#threading-and-data-ownership) · [Session log / history](#session-log--history) · [Settings](#settings)
- [Parameters](#parameters) · [Privacy](#privacy) · [Known deviations and limits](#known-deviations-and-limits) · [Non-goals](#non-goals-v1) · [Project layout](#project-layout) · [Testing](#testing) · [Validation](#validation-and-acceptance-targets) · [Decisions](#decisions) · [Open items](#open-items)

## Stack
- C# / .NET 8, WPF, MVVM-light (no heavy framework)
- NAudio for mic enumeration (WASAPI) and capture
- Settings, history and saved metronomes stored as JSON in `%AppData%\PracticeTimer\`
- Publish as a self-contained single-file `.exe` (win-x64). The WPF project also compiles on Linux (`EnableWindowsTargeting`), but can only run on Windows.

## Main window
- Large time display `HH:MM:SS` (kid-friendly, high contrast)
- Buttons: **Start**, **Pause**, **Reset**. Start and Pause share one toggle button.
- **Mode toggle**: Manual or Auto-listen
  - Manual is a plain stopwatch.
  - Auto: Start arms the listener. The clock runs only while sound is detected.
- Status line: `Stopped` / `Listening... waiting for sound` / `Playing` / `Paused (quiet)` in Auto mode, `Running` / `Paused` in Manual mode. The same line carries warnings and results (microphone problems, calibration and learn progress and outcomes, save failures).
- Mic dropdown (all active capture devices). The choice is remembered, and the list refreshes when devices are plugged in or removed.
- Live level meter with a draggable threshold line. The sensitivity slider (margin above the room) is in Settings; dragging the line adjusts the same thing.
- Goal row: target minutes (optional) and a progress bar. A chime and visual cue fire once when the goal is reached.
- Instrument (free text, remembers entries) and Note fields for the session being timed.
- Window options: always-on-top, minimize to tray (tray tooltip shows the elapsed time).
- Today's total of **saved** sessions (the running one is not included until saved), shown small under the timer.

## Auto-listen detection
Loudness-based (v1). Detection sits behind `IActivityDetector` (frame in, activity sample out) so a tonal detector can be added later without touching the timer logic.

1. **Capture.** Shared-mode WASAPI at the device's own sample rate (8/16/24/32-bit PCM and 32/64-bit float are handled), mixed to mono, sliced into 25 ms frames, RMS in dBFS. Non-finite samples read as silence.
2. **Noise floor.** Adaptive: the 10th percentile of the last 400 quiet frames (~10 s), frozen while sound is active (the detector's 0.5 s hangover counts as active). Because only quiet frames feed it, a room that gets steadily louder (a fan switched on) would never be learned; so after 30 s of unbroken loudness that varies by less than 6 dB over the last 10 s, that level is treated as noise and fed to the floor, which then rises past it. Real playing has rests and dynamics beyond that.
3. **Loud frame** means level > noise floor + margin (sensitivity), and never below an absolute minimum, so total silence doesn't trigger on a cough. Enter and exit thresholds are 3 dB apart so the level doesn't flap.
4. **Metronome rejection** (next section): a loud frame only counts as **active** if it is not part of a metronome pattern.
5. **State machine:**
   - Listening → Playing: sound is active continuously for the **start delay** (default 1 s).
   - Playing → Paused (quiet): no active sound for the **stop delay** (default 5 s). The clock stops and the trailing quiet is **removed from the total** (rolled back to the last moment of active sound).
   - Gaps shorter than the stop delay (breaths, rests between phrases) keep the clock running.
   - Spikes shorter than the start delay (door slam, clap) are ignored.
6. **Calibrate.** One click records ~3 s of room quiet and sets the floor at that stretch's 90th percentile. The gap between its 90th and 10th percentiles is kept as a bias, so later frames from the same room don't pull the floor back down. The calibration is kept for that microphone until the app exits and is restored when the microphone is reopened. Starting calibration (like starting a guided learn) ends any auto run at its last active moment, and nothing is counted while it runs.
7. **Parents and examples count.** A parent playing or demonstrating, or a video example playing, is treated as practice. There is no speaker or source discrimination, and none is wanted. Speech alone is not specifically filtered.

## Metronome filtering
Goal: a metronome ticking alone (mechanical click or digital beep, ~30–250 BPM) must not start or sustain the clock. A metronome running while the student plays must not get in the way.

Approach (rhythm and shape, not pitch):
- **Burst shape.** Consecutive loud frames form a burst. A tick is a burst whose level is **held** near its peak (within 10 dB) for less than 150 ms, followed by a clear gap near the floor. A click or clap that rings down for a few hundred milliseconds is still a tick; a note that stays up is not. Music and speech produce longer or denser energy.
- **Duty cycle.** Over a sliding 2.5 s window, the fraction of time that is loud from short unmatched bursts. Ticks alone give a few percent; playing gives far more. Only a window above the duty threshold (default 20%, tunable) counts as active.
- **Periodicity.** At least 4 consecutive short bursts with near-constant intervals (±12%, 0.24–2 s) that also sound alike (spectral cosine ≥ 0.95 between neighbours, ≥ 0.90 to follow a locked pulse) are flagged as a **metronome pattern** and excluded from the active signal. The tracker follows tempo changes with a short moving window and keeps ignoring bursts on the grid for 3 s after the pattern is lost, so a missed beat doesn't leak through. The current BPM is shown, and clears when the pulse stops.
- **Playing over it.** Sustained energy on top of the ticks raises the duty cycle and the window becomes active. Ticks while the student rests do not hold the clock open: the stop delay is measured against non-metronome activity only.
- A **Metronome filter** checkbox (on by default) disables all of this. The level meter shows metronome-flagged frames in a different colour so tuning is easy to see.

### Per-session metronome learning
The metronome often runs for long stretches, may start before the student plays, and may be off for parts of the session. The detector therefore learns the metronome's **sound** (a fingerprint), so it can recognise it regardless of tempo or timing.

- **What is learned.** For each clean tick: a 24-band log-spectrum (100 Hz up to 7.5 kHz) taken over the whole burst, mean-subtracted and normalised, plus peak level and duration. Interval and tempo are *not* part of the fingerprint, so a tempo change needs no re-learning.
- **When it learns.** Only from **clean ticks**: short bursts with ≥ 80 ms of quiet on both sides. Frames overlapping sustained playing are never used, so the instrument can't contaminate the template.
- **Clusters.** Up to 3 spectral clusters per profile (cosine ≥ 0.9 merges), covering a tick/tock pair or an accented beat. Up to 4 profiles in the bank; the least recently matched tentative one is evicted first.
- **Matching.** A short burst is ignored if its spectrum has cosine similarity ≥ 0.92 to a cluster of an enabled profile and its peak level is within ±18 dB of the cluster's learned peak. It does **not** need to fit the tempo grid, so tempo changes, a dropped beat or a restarted metronome are handled instantly. Non-matching bursts (a played note, a bump, speech) count as normal activity. Spectral shape matters more than loudness, so moving the metronome or the mic is tolerated.
- **Band layout.** Fingerprints record the upper band edge they were taken with. It differs from the default only for sample rates below ~16.7 kHz (e.g. Bluetooth headsets); profiles with a different layout neither match nor merge.
- **Noise floor.** Learned ticks are loud frames, so they never enter the noise-floor estimate; a long-running metronome doesn't make the app think the room is noisy.
- **Lifetime.** **Locked** (guided-learn) profiles are saved across sessions. **Tentative** (auto-learned) profiles are session-only. Reset (the timer) clears no profile.
- **Status.** `Metronomes: N saved`, plus `+ Metronome (auto)` when tentative ones exist, plus `(k learned on another mic)` when relevant.

#### Saved metronomes (across sessions)
A family usually has one or two metronomes, so each only has to be taught once.
- **Saving.** A successful guided learn saves the profile to `metronomes.json` and offers a name prompt (default `Metronome N`, lowest unused N) that can be skipped.
- **Loading.** All saved metronomes are active immediately at startup.
- **Manage list** (Settings). Name, learned BPM range, date, mic. Rename, enable/disable, delete, or **Forget all**. Edits are staged and applied only on OK. Disabled profiles stay stored but are not used.
- **Limit.** 4 saved. When a fifth is taught the app asks which saved metronome to replace, or to discard the new one.
- **Re-learning a known one.** If a guided learn matches a saved fingerprint the app says `Already known: <name>`, refines it (averaged in) and enables it if it was disabled.
- **Per-mic.** Each profile records the mic it was learned on; when a different mic is selected it is flagged `learned on <mic>`. It is still used, but the user can re-learn and delete the old one if it stops matching.
- **Forget auto** (main window) clears only the session's tentative profiles; nothing on disk changes.

#### Guided "Learn metronome" flow (the primary way to learn)
1. The student or parent starts the metronome, then presses **Learn metronome** (needs the Metronome filter on).
2. `Learning metronome... n/8 ticks. Let it run on its own.` shows with a progress ring. The meter colours ticks the filter recognises. Time is not counted while learning, and sustained sound is rejected.
3. The app listens until it has ≥ 8 clean ticks over ≥ 4 s with consistent fingerprints and a steady interval (±15%), or times out at 20 s. While learning, the detector is more sensitive (8 dB over the larger of the noise floor and the last two seconds' background level, down to −70 dBFS, and never less strict than the normal trigger), so a steady hum just under the normal trigger is not read as one endless sound, and it accepts ticks up to 400 ms long, because a phone speaker is quiet and its click rings on. One unbroken second of sustained sound ends it with `Too much other sound`.
4. **Success:** chime, green check, `Metronome learned (BPM ~72)`; the profile is **locked** and saved.
5. **Failure:** `Didn't hear a steady metronome` (timeout) or `Too much other sound. Try again.` Nothing is stored.
6. The button reads **Cancel learning** while learning and is always usable then. Learning again afterwards adds a profile, which is how a second metronome is taught. A mic change or loss mid-learn discards the learn with a message.

#### Auto-learn (fallback)
A setting, **Auto-learn metronomes** (default on), lets the bootstrap learn without the button. Auto-learned profiles are **tentative**, shown as `Metronome (auto)`. They need 12 steady clean ticks (more than a guided learn's 8), never alter or merge into a locked profile, are evicted before locked ones, and are dropped if they keep matching long sustained sound. If it is off, only the guided flow learns. Locked profiles are removed only by the user.

## Timer rules
- Elapsed time is monotonic (`Stopwatch`, not wall clock) and survives a busy UI thread. Manual mode reads the stopwatch; Auto mode is driven by audio sample timestamps, which are equally immune to clock changes.
- A new audio pipeline restarts its timestamps at zero. The clock therefore ends the current run (at its last active moment) before any pipeline swap, learn or calibration, and treats a timestamp that runs backwards as the same event. The gap is never credited.
- Pausing, switching to manual, saving and the goal all use the **credited** time, which excludes trailing quiet. The goal chime and "Goal reached" never fire on time that would later be rolled back.
- Manual Start and Pause always override. If the user pauses in Auto mode, the listener is disarmed until Start.
- Reset stops the clock and clears the time. If more than a few seconds elapsed, it asks whether to save the session (Yes / No / Cancel).
- Sleep pauses a running timer; resume reopens the microphone (the old stream is usually dead).

## Reliability
Things the app must keep doing, because earlier versions got them wrong:
- **Microphone problems.** If the active device is removed, errors out, or stops delivering audio (2.5 s after data was flowing; 10 s allowed for a device's first block), an Auto timer **pauses** with a visible warning that stays until the user presses Start, picks a microphone, or starts learning or calibration, even if the app has fallen back to another microphone. A Manual timer keeps running (it doesn't use the mic), but the warning still shows. The remembered microphone is switched back to when it returns. Re-pressing Start retries a mic that wouldn't open.
- **Never overwrite what couldn't be read.** If a data file is locked at startup, the app runs with defaults and **refuses to save over it** (a message says so) until restarted. Startup also does not rewrite settings.
- **Never destroy unusable files.** A corrupt file is moved aside (`*.corrupt-<time>.json`); a file from a newer build is moved aside intact (`*.newer-v<N>-<time>.json`); an older version is migrated, or read as-is when the change was additive. Entries with an unusable shape (wrong spectrum length, missing lists) are dropped on load, and loaded settings are clamped to valid ranges (including NaN).
- **Atomic, durable writes.** Write to `<file>.tmp`, flush to disk, swap in with `File.Replace`. Settings that change continuously (dragging the trigger line, typing a goal) are written about a second after the last change and on exit.
- **Save failures are not fatal.** A failed write shows a message; history keeps the session in memory and the next successful save includes it. CSV export failures show a message.
- **Closing saves.** A session of at least 3 s is saved automatically on window close and Windows logoff or shutdown (no prompt), then the clock is reset so it can't be saved twice.
- **Single instance.** A second launch asks the running instance to show its window and exits (two copies would overwrite each other's history). The second launch builds no window of its own.
- **Startup failure.** If the main window can't be created the error is logged and shown and the process exits; a windowless instance would block every later launch.
- **Startup trace.** `%AppData%\PracticeTimer\trace.log` is rewritten on each launch with a few lines (startup, timer started, microphones found, first tick, and the outcome of each guided learn). It exists to diagnose a window that comes up dead. The app does not depend on the window's `Loaded` event, which was seen not to arrive; the timer starts from the constructor.
- **Unhandled errors.** UI exceptions are appended to `%AppData%\PracticeTimer\error.log`; the user is told once and the app keeps running. Capture-thread errors are logged too.

## Threading and data ownership
- The **audio callback thread** owns the `AudioPipeline`: frame slicing, detection, learning and clock updates all run there under the pipeline's lock.
- The **UI thread** polls every 40 ms through `TakeSnapshot()`, which returns the peak level and strongest frame tag since the previous poll, so a one-frame click still reaches the meter. It never reads detector internals directly.
- **`FingerprintBank`** is shared by both threads: it locks internally, hands out snapshot copies (`Profiles`, `Summary`), and takes edits only through `ApplyEdits`, `SetMic`, `Remove` and `ClearTentative`.
- **`PracticeClock`** is called from both threads under its own lock and raises `StateChanged` after releasing it, so a subscriber may call back in. Lock order is pipeline → clock → bank; the UI thread never takes them in reverse.
- `MicCapture` raises `Stopped` only for the *current* capture, never for one the app ended itself, so a stale event can't hit a newer capture.
- A pipeline replaced by a mic change is discarded; learn results from it are ignored.

## Session log / history
- Fields: date, start time, duration, mode (manual or auto), optional instrument, optional note.
- A session shorter than 3 s is not saved. Saving happens on Reset (after confirmation) and automatically on close (see Reliability).
- History window: list by day with daily and weekly totals (Monday-start weeks), delete an entry, export to CSV. Columns sort by value.
- CSV: UTF-8 with BOM; durations don't wrap at 24 h; cells starting with `=`, `+`, `-` or `@` are prefixed with `'`.
- The instrument box is a free-text combo that remembers previous entries (most recent first).

## Settings
- Start delay (0.3–5 s), stop delay (1–30 s), sensitivity margin (4–30 dB), sparse-playing duty threshold (5–60%), Calibrate
- Ignore metronome ticks (filter), Auto-learn metronomes, saved-metronome list
- Chime on/off (the goal itself and the microphone are on the main window)
- Always on top, minimize to tray, start minimized (hides only if the tray icon is on; otherwise minimizes to the taskbar)
- Theme: System (default), Light or Dark; larger-text option (window minimum size scales with it)
- Privacy statement
- OK applies and saves; Cancel discards everything including metronome edits.

## Parameters
Defaults live in code; "User" means adjustable in the UI.

| Parameter | Value | User | Where |
|---|---|---|---|
| Frame length | 25 ms | no | `AudioPipeline` |
| Noise floor window / percentile | 400 frames (~10 s), 10th | no | `NoiseFloorEstimator` |
| Floor before 20 frames | −60 dBFS | no | `NoiseFloorEstimator` |
| Calibration length / percentile | 3 s, 90th (+ bias) | no | `AudioPipeline` |
| Sensitivity margin | 12 dB (4–30) | yes | `DetectorSettings` |
| Absolute minimum trigger | −55 dBFS | via trigger line | `DetectorSettings` |
| Hysteresis | 3 dB | no | `LoudnessGate` |
| Start / stop delay | 1 s / 5 s | yes | `PracticeClock` |
| Detector hangover | 500 ms | no | `DetectorSettings` |
| Short-burst (held-level) limit / hold window / close gap | 150 ms / 10 dB / 50 ms | no | `BurstTracker` |
| Duty window / threshold | 2.5 s / 20% | threshold only | `DetectorSettings` |
| Periodicity: ticks / tolerance / range / hold / spectral similarity (lock, follow) | 4 / ±12% / 0.24–2 s / 3 s / 0.95, 0.90 | no | `PeriodicityTracker` |
| Clean gap | 80 ms | no | `LoudnessDetector` |
| Steady-noise rule | 30 s run, ≤ 6 dB over 10 s | no | `LoudnessDetector` |
| Fingerprint | 24 bands, 100 Hz–7.5 kHz | no | `BandSpectrum` |
| Match / merge / cluster cosine | 0.92 / 0.90 / 0.90 | no | `FingerprintBank`, `TickClusterer` |
| Level tolerance | ±18 dB | no | `FingerprintBank` |
| Profiles / clusters per profile | 4 / 3 | no | `FingerprintBank` |
| Sustained matches before a tentative profile is dropped | 3 | no | `FingerprintBank` |
| Guided learn: ticks / min / timeout / max sustained | 8 / 4 s / 20 s / 1 s | no | `GuidedLearnSession` |
| Auto-learn ticks | 12 | no | `AutoLearner` |
| Mic watchdog | 2.5 s (10 s before first data) | no | `MainViewModel` |
| Settings save debounce | 1 s | no | `MainViewModel` |
| Minimum saved session | 3 s | no | `SessionRecorder` |
| UI poll | 40 ms | no | `MainViewModel` |

## Privacy
- Audio is processed in memory only, never recorded, saved, or sent anywhere. Only levels and a numeric spectral fingerprint of each taught metronome are computed, and the fingerprint (not audio) is what gets saved.
- The Settings > Window tab states this in the UI. No network access. No account.

## Known deviations and limits
- **Short regular notes look like a metronome.** Staccato or pizzicato held under ~150 ms at a very steady tempo and with a near-identical sound is treated as ticks (and can even be auto-learned as a tentative metronome). Long or ringing notes are not. The Metronome filter checkbox is the escape hatch; a test pins this behaviour.
- **A loud mechanical metronome** next to the mic raises the noise floor margin slightly.
- **Steady loud sustained sound** (a drone held 30 s with < 6 dB variation) is eventually treated as room noise.
- **`IActivityDetector` is not fully respected:** the pipeline also reads `LoudnessDetector`'s floor, periodicity and learning hooks.
- **Sensitivity slider** is in Settings, not on the main window (the draggable trigger line is).
- **Fingerprints are not portable across sample rates below ~16.7 kHz** or, in practice, across very different mics or rooms; re-learn.
- Today's total counts saved sessions only.
- Speech is not filtered, by decision.

## Non-goals (v1)
- Telling pieces, notes, or instruments apart, or judging quality
- Multi-student profiles (single student only)
- Installer (plain portable `.exe`), Windows 10 support (Windows 11 only)
- Cloud sync, mobile
- Tonal or music-vs-speech classification (designed for, not built)

## Project layout
```
PracticeTimer.sln
src/PracticeTimer.Core/      no Windows dependencies
  Analysis/    FrameSlicer, Dsp, NoiseFloorEstimator, LoudnessGate, BandSpectrum
  Detection/   LoudnessDetector (IActivityDetector), BurstTracker, DetectorSettings
  Metronome/   FingerprintBank, AutoLearner, GuidedLearnSession, PeriodicityTracker, TickClusterer, MetronomeProfile
  Pipeline/    AudioPipeline (slicer -> detector -> clock/learners, meter snapshots)
  Persistence/ JsonStore, SettingsStore (AppSettings), MetronomeStore
  Sessions/    HistoryStore, SessionRecorder, aggregation, CSV
  Timing/      PracticeClock (state machine); Time/ monotonic clock
src/PracticeTimer/           WPF app
  Platform/    MicCapture, MicWatcher, TrayIcon
  ViewModels/  MainViewModel
  Views/       MainWindow, SettingsWindow, HistoryWindow, LevelMeter, ProgressRing, Dialogs
tests/PracticeTimer.Core.Tests/   xUnit; synthetic audio (Signals/SignalGen.cs) and recordings (Fixtures/*.wav)
  Support/     WavReader, ReplayRunner (a recording through the real pipeline), Harness
tools/Replay/                    console tool: replay a .wav and print what the detector decided
```

## Testing
- **Automated (Core, runs on Linux):** the clock state machine (manual and auto, including backwards time, interrupts, trailing-quiet rollback, goal); the detector and noise floor (synthetic silence, tones, bursts, gaps either side of the stop delay, steady noise, varied-level calibration); click trains at several BPMs and 50 ms beeps; metronome learning (guided and auto, tempo steps, accelerando, restart at a new tempo, tick/tock, accent, two metronomes, off/on, level change, disabled/blank edits, locked profiles untouched by auto-learn, band-layout mismatch); bank concurrency; persistence (round trip, corrupt, newer/older version, locked file, unusable entries, envelope-key collisions, sanitising, failed writes); history aggregation and CSV.
- **Recordings.** `Fixtures/metronome.wav` (a phone metronome app at 60 BPM, a clicking sound that rings for a few hundred milliseconds) and `Fixtures/claps.wav` (irregular hand claps standing in for music) are replayed through the real pipeline at their own 48 kHz. Tests pin that the metronome is learned at 60 BPM, is not credited with or without being learned, and that claps, alone or laid over the metronome, are credited. `tools/Replay` shows the same runs: `dotnet run --project tools/Replay -- tests/PracticeTimer.Core.Tests/Fixtures/claps.wav --auto [--bank <metronome.wav>] [--learn-at s] [--ticks] [--rows]`. New recordings (an instrument, a different metronome, a noisy room) go in `Fixtures/` with a test beside these.
- **Manual checklist (Windows):** device switching, unplug during a run, sleep and resume, noisy room, a quiet instrument (soft violin), speech nearby, two copies launched, locked data file, dark theme, mixed-DPI monitors, start minimized with and without the tray.
- The WPF layer is compiled in CI-style builds but has no automated UI tests.

## Validation and acceptance targets
The thresholds were chosen with synthetic audio only. These **targets** define "good enough" once real recordings (mechanical and digital metronomes, and the actual instrument with and without them) are available; none has been measured yet.

| Scenario | Target |
|---|---|
| Quiet room, Auto armed, 30 min | No false start; ≤ 2 s credited |
| Metronome alone, 10 min (taught) | 0 s credited |
| Metronome alone, 10 min (never taught, auto-learn on) | ≤ 5 s credited before the fingerprint locks, then 0 |
| Quiet playing 15 dB above the room | ≥ 95% of playing time credited |
| Rest of 5 s or more during playing | Excluded to within ±1 s |
| Metronome running while resting between phrases | Rests not credited (±1 s per rest) |
| Metronome over playing | No more than 2% extra credited vs. no metronome |
| Room noise steps up 15 dB | Stops crediting within 45 s without recalibrating |

## Decisions
- Trailing quiet is removed from the total on auto-pause.
- Single student. Parent playing or video examples count as practice.
- Metronomes (mechanical and digital) are filtered out.
- Windows 11 only, plain portable `.exe`.
- Closing saves the session automatically; Reset asks.
- Locked metronomes change only through a guided learn or the user's own edits.

## Open items
1. **Real recordings.** 20–30 s samples of the actual metronomes (mechanical and digital), and the instrument played with and without them, to tune the filter and check the acceptance targets above.
2. **App name and icon.** Placeholder name "Practice Timer"; no icon.
3. **Candidate features** are tracked outside this spec; anything adopted gets added here with its behaviour.
