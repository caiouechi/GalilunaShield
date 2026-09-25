# Galiluna Shield — Privacy Policy

**Version 1.0 — effective 2026-09-23**

> **IMPORTANT — TEMPLATE FOR LEGAL REVIEW.** This document is a good-faith draft written to reflect how
> Galiluna Shield actually works. It is **not legal advice** and has not been reviewed by a lawyer. Before
> you distribute or sell Galiluna Shield you must have a qualified attorney review and adapt it for every
> country and state you sell in, and insert your real company name, address, contact details and data-
> protection representative. Placeholders are marked like `[COMPANY]`.

`[COMPANY]` ("we", "us") makes Galiluna Shield ("the Software"), a parental-safety application that a
parent or legal guardian installs on a computer used by a child in their care. This policy explains what
the Software does with data.

## 1. The short version

- **Everything stays on your computer.** The Software captures audio, converts speech to text, checks it
  against a word list, and saves alerts, transcripts, recordings, screenshots and reports **locally, on
  the computer where it is installed.**
- **We do not receive your data.** No audio, transcripts, alerts, screenshots, recordings or personal
  data are transmitted to us or to any third party by the Software.
- **The only network request** the Software makes is a one-time download of the speech-recognition model
  from a third-party host (see §5). No personal data is sent in that request.
- **You are the data controller.** Because the data never reaches us, **you** decide what is collected,
  how long it is kept, and who may see it. That also means you carry the legal responsibility for using
  the Software lawfully (see the EULA and Acceptable Use Policy).

## 2. Who this policy is for

The **purchaser/operator** is an adult parent or legal guardian. The **monitored person** is a minor
child in that adult's care. The Software is **not** intended to monitor other adults, employees, or
people the operator has no legal authority over. Because monitoring involves a child's personal data, and
often the personal data of third parties who speak to the child (for example other participants in a
voice call), special legal rules apply — see §8 and the Acceptable Use Policy.

## 3. What the Software processes, and where it is stored

All of the following is created and stored **only on the local computer**, by default under the
operator's `Documents\Galiluna Shield` folder and the per-user application-data folder. None of it is
sent to us.

| Data | Purpose | Default location | Default retention |
|---|---|---|---|
| Microphone and system (call/game) audio | Detect red-flag words and, optionally, keep proof | Processed in memory; saved only as clips/recordings below | n/a |
| Speech transcripts | Let the operator review what was heard | `…\transcripts` | Until deleted by the operator |
| Alert records (word matched, category, time, program, context) | Notify and evidence | `…\alerts` (and `alerts.jsonl`) | Until deleted |
| Short alert audio clips + SHA-256 fingerprint | Let the operator hear what triggered an alert | `…\alerts` | Until deleted, or per auto-delete setting |
| Proof recordings | Evidence during a red-flag episode | `…\recordings` | Until deleted, or per auto-delete setting |
| Screenshots of all monitors at an alert | Show what was on screen | `…\screenshots` | Until deleted, or per auto-delete setting |
| "Unclear" audio clips | Speech the recognizer could not make out | `…\unclear` | Until deleted, or per auto-delete setting |
| HTML/CSV reports | Human-readable summaries | `…\reports` | Until deleted |
| Program event log | Record of start/stop/shutdown/tamper | `…\events` | Until deleted |
| Settings and word lists | Configuration | `%APPDATA%\Galiluna Shield` | Until changed/uninstalled |
| Consent record | Proof the operator accepted the terms | `%APPDATA%\Galiluna Shield\consent.json` | Until uninstalled |
| Speech-recognition model | Offline recognition | `%LOCALAPPDATA%\Galiluna Shield\models` | Until deleted |
| Diagnostic/crash logs | Fix problems | `%APPDATA%\Galiluna Shield\logs` | Until deleted |

The Software can **encrypt** the sensitive items at rest (audio clips, recordings, screenshots), so that
they can only be opened inside the Software after the parent PIN is entered. See §7.

## 4. Special-category and children's data

Voice recordings and their transcripts can reveal **special-category personal data** (for example about
health, religion, sexuality or beliefs) and concern a **child**. Under the EU/UK GDPR, the COPPA rules in
the United States, and similar laws, this data is treated as especially sensitive. Because we never
receive it, the operator is the controller and is responsible for the lawful basis (typically the
guardian's exercise of parental responsibility) and for handling the data proportionately. The Software
is designed to minimise this data (it discards silence, lets you narrow what is listened to, and can
auto-delete old audio), but the operator decides how it is used.

## 5. The one network request (speech model download)

On first run, and when you change the recognition model, the Software downloads a speech-recognition
model file from a third-party content host (currently `huggingface.co`, operated by Hugging Face, Inc.).
This request:

- sends only what any file download sends (your IP address and a standard user-agent to that host);
- sends **no** audio, transcripts, alerts or other personal data from the Software;
- happens once per model and can be avoided by pre-installing the model file offline.

That host has its own privacy policy, which governs the connection to it. After the model is downloaded,
the Software works fully offline.

## 6. What we (the vendor) collect

Through the Software itself: **nothing.** The Software contains no analytics, no telemetry, no
advertising, no crash reporting to us, and no account system.

If you contact our support, buy a licence, or use our website, **those** interactions may involve
personal data (your email, payment details handled by our payment processor, website cookies). Those are
covered by the separate website privacy notice and the Cookie Policy, not by the Software.

## 7. Security

- Sensitive artifacts (alert clips, recordings, screenshots) can be **encrypted at rest** with AES-256
  using a key derived from the parent PIN combined with a machine-bound secret, so a person who copies the
  files or reads the disk from another account cannot open them. Encryption requires a parent PIN to be
  set.
- Access to the Software's controls, settings, word lists and (when encryption is on) the saved media is
  protected by the **parent PIN**.
- The Software can run as a protected Windows service under a system account so that a child using a
  **standard (non-administrator) Windows account cannot stop it or read its protected files**. This
  protection depends on the child not having administrator rights and on the installer being genuine and
  code-signed.
- No software is perfectly secure. Keep the operating system updated, use a strong parent PIN, and give
  the child a standard (non-administrator) account.

## 8. Third parties who are recorded

When the Software records system audio (a call or game), it may capture the voices of **other people**,
including other children, who did not install the Software and have not consented. Laws in many places
(all-party/two-party consent statutes, wiretapping and eavesdropping laws, GDPR) restrict recording other
people. **The operator is solely responsible** for complying with those laws — for example by only using
microphone-only mode, by informing call participants, or by not recording where the law forbids it. The
Software provides controls (microphone-only mode, per-program filters, recording off) to help, but the
legal responsibility is the operator's. See the Acceptable Use Policy.

## 9. Your rights and controls

Because the data is on your computer and under your control, you can at any time:

- see everything in the output folder;
- delete any or all of it (delete the files, or use the auto-delete setting);
- turn off any kind of collection (microphone, system audio, recording, screenshots, voice analysis);
- export a report or the machine-readable `alerts.jsonl`;
- uninstall the Software (which stops all collection; your saved files remain until you delete them).

If a data-protection law gives the **monitored child** rights (access, erasure, objection), those rights
are exercised against **you** as the controller, not against us, because we do not hold the data.

## 10. International use

The Software runs locally, so no international data transfer of the monitored data occurs through us. The
model-download request in §5 may reach servers outside your country; it carries no personal monitoring
data.

## 11. Changes

If we change this policy, the Software will show the new version and ask the operator to review and accept
it again before continuing. The current version and effective date are shown at the top.

## 12. Contact

`[COMPANY]`, `[ADDRESS]`. Privacy enquiries: `[PRIVACY EMAIL]`. If required in your market, our data-
protection officer / EU or UK representative is `[REPRESENTATIVE]`.
