# Practice Timer: Spec (v0.1 draft)

A simple Windows app for kids and parents to track how long a student has actually been practicing an instrument. It is a stopwatch that can optionally run itself by listening to the microphone.

## Stack
- C# / .NET 8, WPF, MVVM-light (no heavy framework)
- NAudio for mic enumeration (WASAPI) and capture
- Settings and history stored as JSON in `%AppData%\PracticeTimer\`
- Publish as a self-contained single-file `.exe` (win-x64)

## Main window
- Large time display `HH:MM:SS` (kid-friendly, high contrast)
- Buttons: **Start**, **Pause**, **Reset**. Start and Pause share one toggle button.
- **Mode toggle**: Manual or Auto-listen
  - Manual is a plain stopwatch.
  - Auto: Start arms the listener. The clock runs only while sound is detected.
- Status indicator: `Stopped` / `Listening... waiting for sound` / `Playing` / `Paused (quiet)` in Auto mode, `Running` / `Paused` in Manual mode. The same line carries warnings and results (microphone problems, learn progress and outcomes, save failures).
- Mic dropdown (all active capture devices). The choice is remembered, and the list refreshes when devices are plugged in or removed.
- Live level meter with a draggable threshold line. The sensitivity slider (margin above the room) is in Settings; dragging the line adjusts the same thing.
- Goal row: target minutes (optional) and a progress bar. A chime and visual cue fire once when the goal is reached.
- Window options: always-on-top, minimize to tray (tray tooltip shows the elapsed time)
- Today's total of **saved** sessions (the running one is not included until saved), shown small under the timer

## Auto-listen detection (v1: loudness only)
1. Capture audio at the device's own sample rate (shared-mode WASAPI; 8/16/24/32-bit PCM and 32/64-bit float are handled), mix to mono, and compute RMS in dBFS per 25 ms frame. Non-finite samples read as silence.
2. **Noise floor**: adaptive. A rolling estimate (the 10th percentile of the last 400 quiet frames, ~10 s), frozen while sound is active (the detector's 0.5 s hangover counts as active). If the room gets steadily louder (a fan switched on) the floor would otherwise never follow, so after 30 s of unbroken loudness that varies by less than 6 dB over the last 10 s, that level is treated as noise and fed to the floor, which then rises past it. A calibration is kept for the microphone it was made on, until the app exits, and is restored when that microphone is reopened.
3. **Loud frame** means level > noise floor + margin, where the margin is set by the sensitivity slider. There is also an absolute minimum floor so total silence doesn't trigger on a cough.
3a. **Metronome rejection** (see the next section). A loud frame only counts as **active** if it is not part of a metronome pattern.
4. State machine with hysteresis:
   - Idle → Playing: sound is active continuously for **start delay** (default 1 s).
   - Playing → Paused: no active sound for **stop delay** (default 5 s). The clock stops and the trailing quiet is **removed from the total** (the clock is rolled back to the last moment of active sound).
   - Short gaps below the stop delay (breaths, rests between phrases) keep the clock running.
   - Brief spikes shorter than the start delay (door slam, clap) are ignored.
5. Use separate enter and exit thresholds (about 3 dB apart) so the level doesn't flap at the edge.
6. All timings and sensitivity are adjustable in Settings. Add a one-click **Calibrate** button that records ~3 s of room quiet and sets the floor at that stretch's 90th percentile. The gap between its 90th and 10th percentiles is kept as a bias, so later frames from the same room don't pull the floor back down. Starting calibration (like starting a guided learn) ends any auto run at its last active moment, and nothing is counted while it runs.
7. **Parents and examples count.** A parent playing or demonstrating, or a video example playing, is treated as practice. There is no speaker or source discrimination, and none is wanted. Speech alone is not specifically filtered in v1.
8. **Design for a later tonal detector**: put detection behind an `IActivityDetector` interface that takes audio frames and outputs `bool active`. A future version can add a spectral or pitched-content check without touching the timer logic.

## Metronome filtering
Goal: a metronome ticking alone (mechanical click or digital beep, ~30–250 BPM) must not start or sustain the clock. A metronome running while the student plays must not get in the way.

Approach (rhythm and shape, not pitch):
- **Burst shape.** Group consecutive loud frames into bursts. A metronome tick is a short burst (roughly < 150 ms) followed by a clear gap of near-floor level. Music and speech produce longer or denser energy.
- **Duty cycle.** Over a sliding ~2–3 s window, compute the fraction of time that is loud. Ticks alone give a low duty cycle (a few percent). Playing gives far more. Only a window above the duty threshold (default ~20%, tunable) counts as active.
- **Periodicity check.** Track onset times of short bursts. If at least 4 consecutive short bursts have near-constant intervals (±12%, in the 0.24–2 s range), flag them as a **metronome pattern** and exclude them from the active signal. Learn the interval, and keep ignoring bursts that fall on that grid for a few seconds after the pattern is lost, so a missed beat doesn't leak through.
- **Playing over it.** When sustained energy appears on top of the ticks, the duty cycle rises and the window becomes active. The ticks being present while the student rests does not hold the clock open, because the stop delay is measured against non-metronome activity only.
- Known limits: pizzicato, staccato, or a single plucked note repeated very regularly looks like a metronome and may be ignored. A loud mechanical metronome next to the mic raises the noise floor margin slightly. Both can be tuned in Settings, and there is a **Metronome filter** checkbox (on by default) to disable it.
- The level meter shows metronome-flagged frames in a different color so tuning is easy to see.
- Saved-metronome tests: round-trip save/load, corrupt file, a saved fingerprint still matching after a restart, and a duplicate learn merging into the existing entry.
- Unit tests include synthetic click trains at several BPMs (short clicks, 50 ms beeps), clicks with a played note over them, and irregular staccato.
- To be validated with real recordings of both metronome types.

### Per-session metronome learning
The metronome often runs for long stretches, may start before the student plays or after the session starts, and may be off for parts of the session. The detector therefore learns the metronome's **sound** (a fingerprint) during the session, so it can recognize it later regardless of tempo or timing.

- **What is learned.** For each metronome-flagged tick: a spectral fingerprint (e.g. ~24-band log-spectrum averaged over the burst), burst duration, and level range. Interval and tempo are *not* part of the fingerprint, so a tempo change doesn't need re-learning.
- **When it learns.** Only from **clean ticks**: short bursts with quiet gaps (near the noise floor) on both sides. That is the natural case when the student pauses and lets it run. Frames that overlap sustained playing are never used, so the student's instrument can't contaminate the template.
- **Bootstrap.** Until a fingerprint exists, the periodicity rule above carries the load. After ~4–6 regular clean ticks, a fingerprint is stored. Confidence grows with more clean ticks.
- **Multiple clusters.** Keep up to ~4 fingerprint clusters, merging similar ones, to cover a mechanical tick/tock pair, an accented beat 1, or a second metronome (e.g. switching from a digital app to a mechanical one). Least recently matched clusters are evicted first.
- **Matching.** A short burst is ignored if its fingerprint is close to a learned cluster (cosine similarity above a threshold, level within a tolerance band) and it is short enough. It does **not** need to fit the tempo grid, so tempo changes, a dropped beat, or a restarted metronome are handled instantly. Bursts that don't match (a played note, a bump, speech) count as normal activity.
- **Tempo changes during the session.** Matching is by sound, not by interval, so a student changing the metronome mid-session (e.g. 60 → 80 BPM, or a gradual speed-up) is handled without re-learning. Specifically:
  - Ticks are matched individually, so an interval jump doesn't break anything.
  - The periodicity tracker follows the interval with a short moving window (last ~4 ticks) instead of a fixed learned tempo. It re-locks within a few ticks after a change, and the current BPM is shown in the status.
  - The supported range is ~30–250 BPM, intervals from ~0.24 s to 2 s. Subdivision settings (clicks at 2× or 3× the beat) fall inside that.
  - While the tempo is changing, or right after, ticks that match the fingerprint are still ignored, so there is no gap where the metronome leaks into the count.
  - Tempo changes never reset the learned profile.
- **Level changes.** The level tolerance is a wide band (±18 dB) around each cluster's learned peak level, so the student moving the metronome or the mic is tolerated. Spectral shape matters more than loudness. Fingerprints also record the upper band edge they were taken with (it only differs for sample rates below ~16.7 kHz, e.g. Bluetooth headsets); profiles with a different layout neither match nor merge.
- **Playing over the metronome.** Sustained playing raises the duty cycle as before. Learned ticks inside that sound are not subtracted, because they don't need to be: the sustained sound is active on its own. Learned ticks still never reset the stop timer.
- **Noise floor.** Learned ticks are excluded from the noise-floor estimate, so a long-running metronome doesn't push the threshold up or make the app think the room is noisy.
- **Lifetime.** Learned metronomes are held in memory for the session. **Locked** (guided-learn) metronomes are also **saved across sessions** (see below). **Tentative** (auto-learned) clusters are session-only and are cleared on exit. Reset (the timer) does not clear any metronome profile. The status area shows `Metronomes: N saved`, plus `+ Metronome (auto)` when tentative ones exist.

#### Saved metronomes (across sessions)
A family usually has one or two metronomes, so a metronome only has to be taught once.
- **Saving.** A successful guided learn saves the profile automatically to `%AppData%\PracticeTimer\metronomes.json`. The user can give it a name (default `Metronome 1`, `Metronome 2`, …) in a small prompt that can be skipped.
- **Loading.** All saved metronomes are loaded at startup and active immediately, with no button press needed. The status shows `Metronomes: 2 saved`. Matching works like a locked cluster.
- **Manage list.** A Settings section lists saved metronomes with name, learned BPM range and date. The user can rename, delete, enable or disable each, or **Forget all**. Disabled ones stay stored but are not used.
- **Limit.** Up to 4 saved metronomes. Saving a fifth asks which to replace.
- **Re-learning a known one.** If a guided learn matches a saved fingerprint, the app says `Already known: Metronome 1` and refines it slightly (averaged in) instead of adding a duplicate.
- **Drift.** The same metronome can sound a bit different with a different mic, room or distance. If a saved metronome is clearly not matching (the guided learn finds a similar but separate sound), the user can simply learn again and delete the old one. Fingerprints are stored per mic device, and a saved metronome is flagged `learned on <mic>` when a different mic is selected. It is still used, but the user is nudged to re-learn if it doesn't seem to match.
- **Stored data.** Only the numeric spectral fingerprint and summary statistics are stored (no audio). The file is small, human-readable JSON with a version field. A corrupt file is moved aside (`*.corrupt-<time>.json`) and ignored rather than crashing. A file from a newer build is moved aside intact (`*.newer-v<N>-<time>.json`). An older version is migrated, or read as-is when the change was additive. A file that is merely locked by another program is never treated as corrupt: it is retried, and if it stays unreadable the app starts with defaults but **refuses to save over it** (a message says so) until restarted. Entries with an unusable shape (wrong spectrum length, missing lists) are dropped on load. The same applies to `settings.json` and `history.json`.
- **Forget auto** (main window) clears only the session's tentative clusters and un-loads nothing from disk. Deleting a saved metronome is done from the manage list.
- **Guided "Learn metronome" flow** (the primary way to learn, to avoid mis-learning):
  1. The student or parent starts the metronome, then presses **Learn metronome**.
  2. State `Learning metronome…` shows with a progress ring and a live tick counter. The level meter colours ticks the filter recognises as the metronome (known fingerprint, or a locked-in pulse); the ring fills as clean ticks are counted.
  3. The app listens until it has enough clean ticks (≥8, minimum ~4 s, timeout ~20 s) with consistent fingerprints and clean gaps. During learning it doesn't count time and ignores any sustained sound, and it asks the user to keep still ("Let the metronome run on its own").
  4. **Success signal:** a short confirmation chime, a green check, and the text `Metronome learned (BPM ~72)`. The learned profile is marked **locked** (high confidence).
  5. **Failure signals:** `Didn't hear a steady metronome` if it times out, and `Too much other sound, try again` if sustained sound was heard. Nothing is stored on failure.
  6. Cancel is always available: the button reads **Cancel learning** while learning. Learning needs the Metronome filter on, so the button is disabled when it is off. Pressing **Learn metronome** again afterwards adds a cluster, which is useful for a second metronome.
  On success the metronome is saved (see Saved metronomes). A **Forget auto** button clears the session's auto-learned clusters.
- **Automatic learning is a fallback.** A setting, **Auto-learn metronome** (default on), lets the bootstrap above learn without the button. Auto-learned clusters are marked **tentative** and shown as `Metronome (auto)` in the status. They need more clean ticks than a guided learn (12 versus 8), never alter or merge into a locked profile (only a guided learn refines saved metronomes), are evicted before locked clusters, and are dropped if they match long sustained sound. If it is off, only the guided flow learns.
- **Locked clusters** are never evicted by auto-learned ones and are only removed by the user from the saved-metronomes list.
- **Failure modes to watch.** If the first "regular" sound the app hears is actually something else (e.g. repeated pizzicato at a steady tempo), it could learn a wrong fingerprint. The confidence threshold, the clean-gap requirement and the Forget button limit the damage. A learned cluster that matches a lot of long sustained sound is discarded.
- **Tests.** Guided learn succeeds on a clean click train, times out with silence, and rejects sustained sound; tempo steps (60→90→45 BPM), a continuous accelerando, and a restart at a different tempo with no re-learning. Replay scenarios: metronome first then play; play then metronome; metronome off and on at a different tempo; tick/tock mechanical; accented digital beep; two different metronomes in one session; sustained or long plucked notes at a steady tempo are never learned. Very short regular plucks (< ~150 ms) *are* indistinguishable from ticks and are learned; this is the documented limit above and is pinned by a test.

## Timer rules
- Elapsed time is monotonic (use `Stopwatch`, not wall clock) and survives the UI thread being busy. Manual mode reads the `Stopwatch`; Auto mode is driven by audio sample timestamps, which are equally immune to clock changes. A new audio pipeline restarts its timestamps at zero, so the clock ends the current run (at its last active moment) before any pipeline swap, and treats a timestamp that runs backwards as the same event.
- Pausing or switching to manual while Playing also drops the trailing quiet, and the goal chime never fires on time that would later be rolled back.
- Manual Start and Pause always override. If the user pauses in Auto mode, the listener is disarmed until Start.
- Reset stops the clock and clears the elapsed time. If elapsed time is more than a few seconds, ask the user to confirm and offer to save it to history.
- Mic lost or unplugged: pause, show a visible warning, and don't crash. The warning stays until the user presses Start, picks a microphone, or starts learning/calibration, even if the app falls back to another microphone. A microphone that silently stops delivering audio (no data for 2.5 s) is treated the same way. The remembered microphone is switched back to when it returns.
- Sleep pauses a running timer; resume reopens the microphone (the old stream is usually dead). Manual mode is not paused by microphone problems, since it doesn't use the microphone.

## Session log / history
- A session of at least 3 s is saved when the user confirms it on Reset, and **automatically** on app close or Windows logoff/shutdown (no prompt).
- Fields: date, start time, duration, mode (manual or auto), optional instrument label, optional note (both entered on the main window).
- History window: list by day with daily and weekly totals, delete an entry, and export to CSV (UTF-8 with BOM; durations don't wrap at 24 h; cells starting with `=`, `+`, `-` or `@` are prefixed with `'`). If a write fails the session stays in memory, a message is shown, and the next successful save includes it.
- The instrument label is a free-text combo box that remembers previous entries (violin, piano, etc.).

## Settings
- Mic device (on the main window)
- Start delay, stop delay, sensitivity, and the Calibrate button
- Chime on/off (the goal itself is set on the main window)
- Always on top, minimize to tray, start minimized
- Theme: light or dark, plus a larger-text option

## Implementation notes
- **Threads.** The audio callback thread runs the pipeline (clock updates, detection, learning). The UI thread polls the pipeline every 40 ms through `TakeSnapshot()`, which returns the peak level and the strongest frame tag since the last poll, so a one- or two-frame click still shows on the meter. `FingerprintBank` locks internally and returns snapshot copies; edits go through `ApplyEdits`, `SetMic` and `Remove`. `PracticeClock` raises its events after releasing its lock.
- **Settings window.** Metronome renames, enable flags and deletions (including Forget all) are staged and applied only on OK.
- **Saving.** Writes go to `<file>.tmp`, are flushed to disk, then swapped in with `File.Replace`. Settings changed continuously (dragging the trigger line, typing a goal) are written about a second after the last change and on exit. Save failures show a message and never crash the app. Loaded settings are clamped to valid ranges.
- **Startup failure.** If the main window can't be created, the error is logged, shown, and the process exits; a windowless instance would otherwise block every later launch.
- **Theme.** Each window sets its own Background/Foreground from the theme resources (implicit `Window` styles don't apply to derived window classes); text inherits the foreground.
- **Single instance.** A second launch asks the running instance to show its window and exits, so two copies can't overwrite each other's history.
- **Errors.** Unhandled UI exceptions are appended to `%AppData%\PracticeTimer\error.log`; the user is told once and the app keeps running.
- **Start minimized** hides to the tray only when "Minimize to tray" is on; otherwise the window starts minimized to the taskbar.
- **Known limits.** Very short, very regular notes (staccato/pizzicato under ~150 ms) are treated as a metronome by design (see above). `LoudnessDetector` is not yet fully hidden behind `IActivityDetector`: the pipeline also reads its floor, periodicity and learning hooks.

## Privacy
- Audio is processed in memory only, never recorded, saved, or sent anywhere. Only levels and a numeric spectral fingerprint of the metronome are computed, and the fingerprint (not audio) is what gets saved. State this in the UI (e.g. in About).
- No network access. No account.

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
tests/PracticeTimer.Core.Tests/   xUnit, synthetic audio (Signals/SignalGen.cs)
```

## Testing
- The clock state machine and loudness detector are unit-tested with synthetic level sequences (silence, steady tone, bursts, gaps shorter and longer than the stop delay).
- Manual test checklist: device switching, unplug during run, noisy room, quiet instrument (e.g. a soft violin), speech nearby.

## Decisions
- Trailing quiet is removed from the total on auto-pause.
- Single student. Parent playing or video examples count as practice.
- Metronomes (mechanical and digital) are filtered out.
- Windows 11 only, plain portable `.exe`.

## Open questions
1. Any app name, icon or colour preferences? Placeholder name: "Practice Timer".
2. Can you record 20–30 s samples of your actual metronomes (mechanical and digital), and the instrument being played with and without them? They'd let me tune the filter against reality. If not, I'll use synthetic signals and sensible defaults.

## Milestones
1. Skeleton WPF app with manual stopwatch, plus tests for the clock
2. Mic enumeration, level meter, loudness detector, auto mode
2b. Metronome filter (burst shape, duty cycle, periodicity), tuned against recordings
2c. Per-session metronome learning (fingerprints, guided Learn flow with success signal, Forget button, tempo-change handling)
2d. Saved metronomes (MetronomeStore, auto-load at startup, manage list, per-mic flag)
3. Settings, calibration, and persistence
4. Goal, tray, and always-on-top
5. History, CSV export, and packaging
