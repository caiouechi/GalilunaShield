# Operator notes: consent records, DPIA, and compliance checklist

> Internal guidance for `[COMPANY]` and for operators. Not legal advice; confirm with counsel.

## What the Software records as consent

Before monitoring can run, Galiluna Shield requires the operator to accept an on-screen consent flow. When
they accept, the Software writes a **consent record** to `%APPDATA%\Galiluna Shield\consent.json`
containing:

- the versions of the EULA, Privacy Policy and Acceptable Use Policy that were shown and accepted;
- the exact attestations the operator ticked (guardian status, ownership of the computer, responsibility
  for recording law, lawful-use commitment, acknowledgement that third parties may be recorded);
- the timestamp, the Windows user name and the machine name;
- the app version.

If any policy version changes, the operator is asked to review and accept again before continuing. The
operator can view and export this record from the app's **Legal** page. This gives you a defensible,
timestamped record that the operator affirmed authority and accepted the terms.

## Recommended pre-sale legal steps

1. Have a lawyer in each target market review the EULA, Privacy Policy, Acceptable Use Policy and Cookie
   Policy, and the consent wording, and adapt them (especially recording-consent and children's-data
   rules).
2. Decide your markets. Recording-consent law differs sharply (e.g. "one-party" vs "all-party" consent
   US states; strict EU/UK rules). You may need to disable stored recording by default in some regions.
3. Prepare a **Data Protection Impact Assessment (DPIA)** template for operators in GDPR/UK-GDPR regions,
   since monitoring a child's communications is high-risk processing.
4. Confirm your **COPPA** position (US): the operator, as the child's parent, is generally the one
   consenting; make sure your marketing does not position the product as directed at children.
5. Add an **age/# of the child** acknowledgement and a clear statement that the tool is for the operator's
   **own** child on the operator's **own** computer.
6. Provide a clear **in-app and website route to withdraw** and to delete all data (uninstall + delete
   folder), and document retention.
7. If you add any cloud/remote-notification feature later, you become a **data processor/controller** for
   that data and this whole analysis changes — treat that as a separate, larger compliance project.

## Compliance checklist (high level)

- [ ] EULA / Privacy / AUP / Cookie policies reviewed by counsel per market
- [ ] Consent flow wording reviewed by counsel
- [ ] Recording-consent defaults set correctly per market
- [ ] DPIA template prepared (EU/UK)
- [ ] COPPA position confirmed (US)
- [ ] Age acknowledgement present
- [ ] Data deletion & retention documented and honoured
- [ ] Security review of encryption-at-rest and tamper protection
- [ ] Installer and binaries code-signed
- [ ] Support and privacy contact routes live
