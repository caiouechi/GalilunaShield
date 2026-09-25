# Privacy

Galiluna Shield is built so that a parent never has to trust a server with their child's voice.

## What stays on the computer

Everything. Audio capture, speech recognition, red-flag matching, recordings, alerts and reports all run
and are stored locally:

| Data | Location | Retention |
|---|---|---|
| Alert clips, details, `alerts.jsonl` | `Documents\Galiluna Shield\alerts` | Until you delete them |
| Daily transcripts | `Documents\Galiluna Shield\transcripts` | Until you delete them |
| Unclear-speech clips | `Documents\Galiluna Shield\unclear` | Until you delete them (capped at 40 new clips per hour) |
| Recordings | `Documents\Galiluna Shield\recordings` | Until you delete them |
| Reports (HTML, CSV) | `Documents\Galiluna Shield\reports` | Regenerated; until you delete them |
| Settings, word list | `%APPDATA%\Galiluna Shield` | Until uninstall/deletion |
| Speech model | `%LOCALAPPDATA%\Galiluna Shield\models` | Until deletion |

## Network access

The only network request the software makes is the one-time download of the speech-recognition model
from `huggingface.co` on first run (or after changing the model size in Settings). No telemetry, no
crash reporting, no update checks, no account.

## What is not collected

The publisher receives nothing: no audio, no text, no usage statistics, no identifiers.

## Uninstalling

Uninstalling removes the program. Your alerts, recordings, reports, settings and the model are left in
place so that evidence is never destroyed by accident; delete the folders above if you want them gone.

## Legal note

Monitoring and recording conversations is regulated differently around the world, and conversations
often include other people's children. The software gives you the tools; you are responsible for using
them lawfully and proportionately.
