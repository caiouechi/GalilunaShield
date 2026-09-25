# galiluna – command-line engine

The same engine as the desktop app, without a window. Useful for headless machines, scripts, support and
testing. It shares settings, the word list and the output folder with the desktop app.

```bash
galiluna [--start] [--minimized] [--mode microphone|system|both]
         [--record off|microphone|system|both|smart] [--config <appsettings.json>]
galiluna --report [today|week|YYYY-MM-DD]
```

| Option | Meaning |
|---|---|
| `--start` | Begin monitoring immediately (also honours `StartMonitoringOnLaunch` in settings). |
| `--minimized` | Start with the console window minimized (for a Windows startup shortcut). |
| `--mode` | Override the listening mode for this run. |
| `--record` | Override the recording mode for this run. |
| `--report` | Build the report(s), print the path and exit. |
| `--config` | Use a different settings file. |

Inside the window: **Space** toggles monitoring, **M** cycles the listening mode, **R** cycles the
recording mode, **P** rebuilds the reports, **Q** or Ctrl+C quits. The global hotkey (default
**Ctrl+Alt+S**) toggles from any window.

Settings live in `%APPDATA%\Galiluna Shield\appsettings.json` (all options are commented) and the word
list in `redflags.txt` next to it. Output goes to `Documents\Galiluna Shield`. See the
[Parent's Guide](../docs/PARENTS-GUIDE.md) for what the modes, alerts, recording and reports mean, and
[BUILDING.md](../docs/BUILDING.md) for the architecture.
