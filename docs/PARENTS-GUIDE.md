# Galiluna Shield – Parent's Guide

Galiluna Shield is a quiet guardian for the computer your child uses. It listens to what is said and
heard, understands it **on your computer** (nothing is uploaded), and speaks up when it hears something a
parent should know about.

## Installing

1. Run `GalilunaShield-<version>-x64.msi`. Windows will ask for permission to install.
2. Accept the licence, choose a folder (the default is fine) and click **Install**.
3. Leave **Open Galiluna Shield now** ticked and click **Finish**.

The first time it opens, Galiluna Shield downloads its speech-recognition model once (about 150 MB).
After that it works completely offline.

Requirements: 64-bit Windows 10 (version 2004 or newer) or Windows 11, and a microphone or headset.
You do not need to install anything else.

## The first five minutes

1. **Start monitoring** with the big green button. The status light in the bottom-left turns green.
2. Say a test sentence near the microphone, e.g. *"Today we learned about dinosaurs."* It appears in
   **Live transcript** within a few seconds.
3. Say a test red flag, e.g. *"This is our little secret, don't tell mommy."* A red alert appears on
   the Dashboard and under **Alerts**, with a **Play clip** button.
4. Open **Reports** and click **Open reports home** to see the report in your browser.

Press **Ctrl+Alt+S** from any window to start or stop monitoring. Closing the window keeps Galiluna
Shield running in the system tray while monitoring is on; double-click the shield icon to bring it back.

## What to listen to

| Choice | Hears | Use it when |
|---|---|---|
| **Microphone** (default) | What your child says | You mainly want to know what your child says out loud, in games, calls or to the room. |
| **System audio** | What your child hears in calls and games. YouTube, Netflix, Spotify and other browsers/players are skipped. | You are worried about who is talking *to* your child online. |
| **Both** | Both of the above | Most complete picture. |

Galiluna Shield follows the microphone that actually has sound, so a headset works even if Windows'
default device is the laptop microphone. It also hears games and calls playing through a headset that is
not the default speaker.

## Alerts and severity

Every alert shows the sentence, which words matched, the program or microphone it came from, how
confident the recognizer was, what was said just before, and a **Play clip** button. Listen before you
act: speech recognition is good but not perfect.

| Severity | Categories | What happens |
|---|---|---|
| **Critical** | Body safety, strangers/abduction, online grooming, self-harm | Tray notification, and in Smart recording mode a proof recording starts immediately. |
| **High** | Violence/threats, sexual content, personal information | Tray notification. |
| **Medium** | Drugs/alcohol, bullying, scams | Logged and shown. |
| **Low** | Profanity | Logged and shown. |
| **Possible** (purple) | A close mis-hearing of a red flag, e.g. "kill my sale" | Shown; worth a listen. |

## Where it was heard, and a picture of the screen

Every alert tells you **where** the words came from and **when**:

- The **program name** (Discord, Roblox, a specific game) for anything heard in a call or game, or "the
  microphone" for what your child said or someone in the room said.
- The **exact date and time**.
- A **screenshot of every monitor** taken the instant the red flag happened, so you can see what your
  child was looking at. Two or three monitors are all captured in one picture. Turn this off, or limit it
  to more serious flags, under **Settings → Screenshots at a red flag**.

## Who is talking?

Galiluna Shield can warn you when a voice that **sounds like an adult** is talking to your child in a call
or game. It judges by pitch, so it cannot name a person and it can be fooled; treat it as a nudge to
listen to the clip. Children's voices sit high; adult voices sit lower. Configure it under
**Settings → Who is talking?**.

## Topic packs

Beyond the main word list, **Topic packs** are ready-made lists you switch on with one click:
heavy cursing, a specific religion (Christianity, Islam, Judaism, Buddhism/Hinduism, or any faith),
politics, gambling, dating, horror, weapons, alcohol, drugs slang, body image, gaming toxicity, occult,
online-safety phrases and more. Tick one and its words protect your child immediately; untick to remove
them. Drop your own `.txt` list into the topic-packs folder to add a custom pack.

## Keeping the shield on

- **Start hidden with Windows.** Under Settings, turn on *Start with Windows* and *Start completely
  hidden*. When the computer restarts, Galiluna Shield runs with no window and no tray icon. Bring it back
  any time with the **show-window hotkey** (default Ctrl+Alt+G), or by launching the app again.
- **The program log.** Every start, stop, Windows shutdown, and any run that ended without closing
  properly is recorded and shown in the report under *Program events*. If a child turns the shield off,
  you will see exactly when. Set a **parent PIN** so stopping it needs your PIN.

## Performance on older computers

If your child's computer is not powerful, set **Settings → Performance** to **Light**: the app uses a
smaller speech model and less CPU, staying responsive at the cost of some accuracy. **Balanced** suits
most computers; **Accurate** is for a fast PC. The Settings page suggests what your machine can handle.

## Auto-delete (keeping the disk from filling up)

Under **Settings → Auto-delete saved audio** you can have Galiluna Shield delete audio older than a
number of days you choose. It is **off by default**, because deleting is permanent. You can keep the
written record (alert text, transcripts, reports) even after the audio is removed.

## Recording (proof)

Alerts always save a short clip. On top of that you can record:

- **Smart** (default): records nothing until three red flags occur within five minutes, or one critical
  one appears. Then it records everything being listened to, *starting with the minute before the
  trigger*, and keeps going until a **calm-down margin** passes with no new flag. You set that margin
  (default 15 minutes) under Settings, so the shield keeps capturing while things are tense and stops once
  they settle. Good for evidence without recording all day.
- **Microphone / System audio / Everything**: records continuously. The app warns you that this uses a
  lot of disk (roughly 110 MB per hour, per source); pair it with auto-delete.
- **Off**: only the alert clips.

Recordings are 16 kHz mono WAV files, about 1.9 MB per minute, split into files of a length you choose.

## Reports and evidence

Reports open in your browser and work offline. They show alerts by severity, category, hour and source,
the full timeline with the audio embedded, unclear speech worth a listen, and recordings. Every clip
carries a SHA-256 fingerprint, which lets anyone verify later that the file was not altered. A **CSV**
file sits next to each report for spreadsheets or professionals.

If you need to hand evidence to a school, a counsellor or the police, copy the whole
**Documents\Galiluna Shield** folder: the report links point inside it.

## Reviewing alerts (so you're not flooded)

A normal gamer will trigger words like "kill" or "shoot". On the **Alerts** page you can keep the noise
down:

- **Reviewed** / **Not a concern** mark an alert so you know you've dealt with it. The sidebar badge counts
  only *new* alerts.
- **Mute this word** stops a specific word from ever alerting again (you can un-mute it in Settings).
- **Mute this app** marks a program "fine": it is still transcribed, but stops raising alerts.
- **Show dismissed** brings back the ones you set aside.

## Keeping the evidence private (encryption)

Under **Settings → Files & reports**, turn on **Encrypt saved audio, recordings and screenshots**. The
files are then locked to this Windows account and can only be opened inside Galiluna Shield (with the
parent PIN if you set one). Copying them to another computer, or opening them from another account, reveals
nothing. Reports link to the app to play encrypted media instead of showing it in the browser. Set a parent
PIN first to make the encryption stronger.

## Making it hard to switch off

- Set a **parent PIN** (Settings). Then stopping monitoring, exiting, changing settings, topics or the word
  list, and opening encrypted evidence all need the PIN.
- Turn on **Keep-alive** (Settings → Monitoring). Windows will restart Galiluna Shield within a few minutes
  if it is closed or killed. This needs administrator approval once.
- **Give your child a standard (non-administrator) Windows account.** This is the single most important
  step: it stops a tech-savvy child removing the keep-alive task, uninstalling the app, or reading the
  encrypted evidence. See SECURITY.md in the install folder.
- Whatever happens, every time monitoring is stopped or the app is killed is **recorded in the report**, so
  a bypass is always visible to you.

## Please use it lawfully

When you first open Galiluna Shield you must accept the licence, privacy and acceptable-use terms and
confirm you are the parent/guardian of the child and own the computer. Recording people, especially other
people who talk to your child in a call, is regulated and the rules differ by country and state. Some
places require everyone in a conversation to agree before recording. **You are responsible for following
the law where you live.** If you cannot, use microphone-only mode or turn recording off and rely on live
alerts. See the Acceptable Use Policy (About page in the app, or the `legal` folder).

## The red-flag word list

Under **Red-flag words** you can add, remove or change words and phrases. One per line, grouped under
`[Category | severity]` headers. Add `*` to the end of a word to match any ending (`kill*` matches kill,
killed, killing). Changes apply as soon as you save, no restart needed. Consider adding names of people
or places you are specifically worried about, and words in the languages spoken at home.

## Parent PIN

In **Settings**, set a 4–8 digit PIN. Stopping monitoring, exiting the app, and changing settings or the
word list then require it, so an older child cannot quietly switch the shield off.

## Unclear speech

Mumbling, shouting, another language or heavy game noise sometimes cannot be transcribed. Instead of
dropping it, Galiluna Shield saves the audio in the **unclear** folder and lists it in the report, so you
can listen if you have a specific worry.

## Where things are

| What | Where |
|---|---|
| Alerts, transcripts, recordings, reports, unclear clips | `Documents\Galiluna Shield` |
| Settings and the word list | `%APPDATA%\Galiluna Shield` (type that into the Explorer address bar) |
| Speech model | `%LOCALAPPDATA%\Galiluna Shield\models` |

## Good to know

- Galiluna Shield helps you notice and investigate. It does not replace talking with your child.
- It can miss things and it can raise false alarms. Always listen to the clip.
- Recording conversations is regulated differently in different places, especially conversations that
  include other people's children. Check the rules where you live.
- Nothing is ever uploaded. If you uninstall the app, your alerts and recordings stay in your Documents
  folder until you delete them.
