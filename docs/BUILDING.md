# Building Galiluna Shield

## Prerequisites

- Windows 10/11 x64
- .NET 9 SDK
- (Installer only) nothing else: WiX 5 is a project-local .NET tool restored automatically.

## Solution layout

| Project | What |
|---|---|
| `GalilunaShield.Core` | The engine: audio capture (NAudio), offline speech-to-text (Whisper.net), red-flag detection, Smart recording, alert store, HTML/CSV reports. No UI. |
| `GalilunaShield.App` | WPF desktop app (`GalilunaShield.exe`): dashboard, alerts, live transcript, reports, word-list editor, settings, tray icon, parent PIN. |
| `GalilunaShield` | Console engine (`galiluna.exe`) for headless use, scripting and support. |
| `GalilunaShield.Tests` | xUnit tests for detection, parsing, chunking, PCM conversion, alert store and reports. |
| `installer/` | WiX 5 MSI definition, licence, artwork. |

## Everyday commands

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project GalilunaShield.App
```

```bash
dotnet run --project GalilunaShield -- --start --mode both --record smart
```

Render every page of the desktop app to PNG (used for the docs):

```bash
dotnet run --project GalilunaShield.App -- --screenshots docs\screenshots
```

## Building the installer

```powershell
.\build-installer.ps1
```

This runs the tests, publishes both executables **self-contained** for `win-x64` (the target machine
needs no .NET), and compiles `installer\out\GalilunaShield-<version>-x64.msi`. The version comes from
`<Version>` in `GalilunaShield.App.csproj`, or pass `-Version 1.2.0`.

The MSI installs to `Program Files\Galiluna Shield`, adds Start-menu and desktop shortcuts, offers to
launch the app, closes a running copy on upgrade/uninstall, and upgrades in place (same UpgradeCode).

### Code signing

Unsigned installers trigger Windows SmartScreen warnings on customers' machines. Before distribution,
sign `GalilunaShield.exe`, `galiluna.exe` and the MSI with an Authenticode certificate (an EV
certificate avoids the SmartScreen reputation ramp-up), e.g.:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a installer\out\GalilunaShield-1.0.0-x64.msi
```

## Runtime file locations

| What | Where |
|---|---|
| Program | `%ProgramFiles%\Galiluna Shield` (read-only for users) |
| User config (`appsettings.json`, `redflags.txt`) | `%APPDATA%\Galiluna Shield` (copied from the program folder on first run, never overwritten) |
| Speech models | `%LOCALAPPDATA%\Galiluna Shield\models` |
| Output (alerts, transcripts, recordings, reports, unclear) | `Documents\Galiluna Shield` (configurable) |
| Crash logs | `%APPDATA%\Galiluna Shield\logs` |

## Architecture notes

- **Capture**: `MicrophoneSource` opens every active capture endpoint via WASAPI and forwards the one with
  sound. `SystemAudioCaptureManager` enumerates audio sessions on all render endpoints and opens a
  per-process loopback (`ProcessLoopbackSource`) for each program that passes the ignore/focus filters,
  so browsers and media players never reach the transcriber. All sources normalise to 16 kHz mono float.
- **Chunking**: `SpeechChunker` cuts speech at pauses (energy-based), with an idle-flush timer because
  loopback delivers no buffers while nothing plays.
- **Transcription**: one `WhisperTranscriber` (whisper.cpp, beam search, per-segment probabilities) fed
  from a single channel. Segments Whisper emits as noise tags are dropped; chunks with no words at all
  become "unclear" clips.
- **Detection**: `RedFlagDetector` builds whole-word regexes per phrase, plus a Levenshtein-based fuzzy
  pass over word windows for near mis-hearings. Rules hot-reload from `redflags.txt`.
- **Recording**: `RecordingController` decides when recorders write; `SourceRecorder` keeps a pre-roll
  ring buffer while idle and flushes it at the head of the file when Smart mode triggers.
- **Persistence**: `AlertStore` writes per-alert text + WAV (SHA-256), `alerts.jsonl`, daily transcripts,
  unclear clips. `ReportBuilder` renders self-contained HTML (no scripts) and CSV from the JSONL log.
- **UI**: `MonitorService` is event-driven (`Logged`, `Transcribed`, `AlertRaised`, `StateChanged`);
  the console and the WPF `ShellViewModel` are thin subscribers.
