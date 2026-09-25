# Security & tamper resistance

This explains, honestly, how hard it is for a child to bypass Galiluna Shield, what protects the saved
evidence, and the limits. Read it before you rely on the product's "can't turn it off" claims.

## The realistic model

Galiluna Shield must capture the child's **audio and screen**, which are per-Windows-session things. A
background Windows service running in the isolated "Session 0" **cannot** capture a user's audio or take a
screenshot of their desktop. So Galiluna Shield deliberately runs **in the child's own session**, and
makes itself hard to stop rather than impossible:

1. **Parent PIN.** Stopping monitoring, exiting the app, changing settings, the word list, the topics, or
   opening encrypted evidence all require the parent PIN. A child cannot use the app's own controls to
   turn it off.
2. **Start hidden with Windows.** The app can start with no window and no tray icon, so there is nothing
   obvious to close.
3. **Keep-alive watchdog.** With keep-alive on, a Windows Scheduled Task running as SYSTEM relaunches the
   app at logon and every few minutes. If a child kills it from Task Manager, it comes back within a few
   minutes.
4. **Everything is logged.** Every stop, every unexpected end (a kill, a crash, a power-off), every
   Windows shutdown, and deletion of the output folder is recorded in the program-event log and shown in
   the report. **A bypass is always visible to the parent**, even if it briefly succeeds.
5. **Encrypted evidence.** With encryption on, the saved clips, recordings and screenshots can only be
   opened inside the app on that Windows account (and only with the parent PIN if one is set). Copying the
   files to another PC or reading the disk from another account does not reveal them.

## The one setting that makes it strong

**Give your child a standard (non-administrator) Windows account, and keep the administrator password to
yourself.** Then:

- the child cannot delete the SYSTEM keep-alive task, so the watchdog always restarts the app;
- the child cannot uninstall the program or stop a protected process they do not own;
- the child cannot read the encrypted evidence, which is bound to the account and the PIN;
- the child cannot turn off "start with Windows" for all users.

On a shared **administrator** account, a determined, technical child can still remove the task, kill the
app repeatedly, or uninstall the product. No consumer software can fully prevent that without an
enterprise device-management setup or a signed kernel driver, which are out of scope here. What Galiluna
Shield guarantees on any account is that **every attempt is recorded** so you know it happened.

## What would make it bulletproof (future / enterprise)

- Code-signing the binaries and installer (also removes SmartScreen warnings) — do this before selling.
- A signed, Protected-Process-Light helper or kernel driver (large undertaking, Microsoft attestation).
- Enterprise MDM / Windows policies to lock down accounts and prevent uninstall.
- A remote component so tamper alerts reach the parent's phone immediately (planned; today alerts are on
  the child's PC).

## Reporting a security issue

Email `[SECURITY EMAIL]`. Please do not post exploit details publicly before we can address them.
