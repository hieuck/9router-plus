# Google Auto-Login Design

**Date:** 2026-08-23  
**Status:** Approved in conversation; implementation not started

## Goal

Add a separate **Auto sign in to Google** action to the selected Chrome profile's right-click menu. The existing action that opens Google in external Chrome remains unchanged. The new action uses the selected profile's external Chrome session, a portable encrypted vault, and a local Chrome DevTools Protocol (CDP) connection to fill and submit supported Google sign-in steps.

This feature is intended for accounts the user owns or is authorized to use. It must stop and return control to the user for CAPTCHA, passkeys/security keys, anti-bot checks, unusual-login challenges, or unsupported Google UI states.

## Scope and decisions

- One vault file stores the login records used by the application.
- One Chrome profile maps to exactly one Google account record.
- Records are keyed by the existing stable `ProfileId`, derived from the profile's stable user-data path and directory name; display names are not identity keys.
- A record contains `ProfileId`, `Email`, `Password`, and `TotpSecret`.
- If no email has been saved for a profile, the dialog defaults the email field to the profile display name. The user must correct it before continuing if it is not a valid email address.
- Editing the email and choosing Save or Auto sign in is an explicit decision that the profile uses the new email. The new email replaces the previous record value. It is not a one-time override and does not implicitly sign out, delete cookies, or change the existing Chrome session.
- Vault import replaces the entire current vault, after confirmation, and creates a backup before replacement. It does not merge records.
- Vault export produces a portable encrypted file that can be imported on another machine when its vault password is known.
- The vault password is never stored in plaintext. “Remember on this device” is opt-in and stores only a DPAPI-protected wrapping of a vault key outside the portable export; it is scoped to the current Windows user and device context.
- The existing external Chrome launch action stays unchanged. Auto sign-in uses Chrome started in app-managed mode with CDP bound only to `127.0.0.1` on a random local port.
- The app must not connect to an already-running Chrome profile that lacks CDP, must not force-close it, and must show actionable guidance to close and retry.
- The app must not use an embedded browser, clipboard, Chrome extension, UI Automation fallback, or a remote CDP endpoint for this feature.

## Architecture

### UI entry point

Add a distinct context-menu command beside the existing Google-login command. The command opens a WPF dialog owned by the main window and supplied with the selected `ChromeProfile`.

The dialog shows:

- selected profile identity and linkage status;
- Gmail email field;
- password field;
- TOTP secret field;
- Save information;
- Auto sign in;
- Cancel;
- non-sensitive progress/status text.

The dialog must never display or log secrets. It should follow the existing WPF dialog/resource/localization patterns.

### Vault service

Add a focused vault abstraction in the Infrastructure/Security or Storage boundary. It owns file format, encryption/decryption, atomic writes, backup creation, import/export, and device-key wrapping. The view model should depend on the abstraction rather than implementing cryptography or file format logic itself.

A versioned vault file contains a non-secret header and an authenticated encrypted payload. The payload contains the profile records. The proposed primitives are:

- PBKDF2-HMAC-SHA256 with a per-file random salt to derive a vault key from the user-supplied vault password;
- AES-256-GCM with a random nonce and authentication tag to encrypt and authenticate the payload;
- explicit version and algorithm identifiers to support future migration.

Exact KDF iteration parameters must be selected from current .NET guidance during implementation and covered by tests; they must not be silently weakened for convenience.

The portable file must not contain a plaintext vault password, DPAPI-wrapped device key, password, TOTP secret, cookie, or session token outside the authenticated encrypted payload.

### Device unlock

Support three unlock scopes:

1. **This operation:** plaintext/decryption material exists only for the operation's lifetime where possible.
2. **This app session:** the unlocked key is retained in memory until app exit or explicit “Lock vault now”.
3. **Remember on this device:** a random vault key is wrapped with Windows DPAPI `CurrentUser` and stored separately from the portable vault file. The default is off. Provide “Lock vault now” and “Remove remembered unlock”. Removing remembered unlock does not delete the vault.

Importing a different vault invalidates the remembered unlock for the previous vault. The implementation must bind the remembered wrapping to the imported vault identity/version or otherwise refuse to use stale material.

### Chrome/CDP service

Add a small abstraction around process launch, CDP endpoint discovery, target/tab selection, navigation state inspection, field input, button submission, cancellation, and disposal. The implementation must launch the selected `user-data-dir` and `profile-directory`, open the Google sign-in URL, and expose only a loopback CDP endpoint.

Use a bounded state machine for supported states:

1. email entry;
2. password entry;
3. TOTP entry;
4. completed/redirected account state;
5. manual intervention required or unsupported state.

At every step:

- verify the controlled target is the intended Google origin/tab;
- locate controls using stable semantic attributes rather than screen coordinates;
- send values directly through CDP, never through clipboard;
- avoid logging field values, DOM snapshots, cookies, tokens, full sensitive URLs, or page contents;
- enforce per-step and total timeouts;
- stop on unsupported challenges instead of retrying indefinitely.

The service must not claim success merely because Chrome starts. It reports success only after a defined completion signal is observed. If Chrome closes or the CDP connection is lost, report interrupted/cancelled and do not continue sending secrets.

### TOTP handling

Decrypt the TOTP secret only when needed, generate the current code using the standard TOTP algorithm and local clock, submit it to the intended field, and do not persist generated codes. On rejection or suspected clock skew, stop with an actionable error. Minimize the lifetime and copies of plaintext values; do not claim that managed .NET strings can be guaranteed to be wiped from all memory.

## Data flow

1. User opens the selected profile's context menu and chooses Auto sign in to Google.
2. App resolves the stable profile ID and loads/unlocks the vault.
3. App loads the single record for that profile, or creates a draft with the display name as the default email.
4. User reviews/edits email, password, and TOTP secret.
5. **Save information** explicitly persists the complete record (email, password, and TOTP secret) in the encrypted vault. **Auto sign in** uses the current fields for the run and persists the edited email as the profile's account linkage, but does not persist newly entered password/TOTP values unless the user also chooses Save information.
6. App starts the selected external Chrome profile with loopback CDP, or reports that the profile must be closed and relaunched by the app if it is locked by an unmanaged running instance.
7. CDP state machine performs supported email, password, and TOTP steps.
8. On completion, Chrome retains cookies/session in its own profile directory; the vault does not receive cookies or tokens.
9. On success, failure, timeout, cancellation, or manual intervention, CDP is disposed and in-memory secret material is released as early as practical.

## Error handling and user-visible states

Use actionable, non-sensitive messages for:

- vault locked or wrong vault password;
- no record for the selected profile;
- invalid/missing email;
- malformed or rejected vault file;
- import confirmation/cancel;
- backup failure or atomic replacement failure;
- profile already in use by Chrome;
- CDP launch/connect failure;
- unsupported/manual verification required;
- invalid credentials or rejected TOTP;
- timeout, cancellation, Chrome exit, or lost CDP connection.

Never show or log passwords, TOTP secrets, generated OTP values, cookies, tokens, page DOM, or full sensitive URLs. Do not retry credentials indefinitely.

## Testing strategy

### Vault tests

- create, save, reopen, and round-trip records;
- reject wrong password;
- detect one-byte file tampering through authenticated decryption;
- export/import round trip using temporary directories;
- backup before replacement and preserve the old vault if import fails;
- replace rather than merge on successful import;
- enforce one record per `ProfileId` and update the existing record;
- remember/unlock/lock/remove remembered unlock behavior;
- invalidate stale remembered unlock after import;
- reject invalid email before Chrome work begins.

Tests must use synthetic credentials only.

### State-machine/CDP tests

Use an interface and fake CDP server/driver; do not use real Google credentials in automated tests. Cover email/password/TOTP/completed states, wrong origin/tab, manual intervention states, cancellation at every step, timeout, Chrome exit, lost connection, no cross-tab secret submission, and success only after completion signal.

### Manual acceptance test

Use a dedicated test account supplied by the user, never commit its data, and verify the external Chrome profile, session retention, supported flow, and manual handoff for challenges. Automated tests must not depend on Google's live UI.

## Security constraints

- DPAPI scope is `CurrentUser`; document that same-user malware or an unlocked Windows session is in scope for the “remember” convenience tradeoff.
- Do not store secrets in `settings.json`, logs, clipboard, crash reports, or unencrypted export metadata.
- Bind all CDP communication to loopback and use a random port; dispose the endpoint after the run.
- Keep external Chrome session state in the selected Chrome profile directory.
- Do not attempt to bypass CAPTCHA, passkeys, security keys, anti-bot controls, or additional verification.
- Do not silently fall back to less controlled UI automation.

## Acceptance criteria

1. A separate context-menu command opens the auto-login dialog for the selected profile.
2. One encrypted vault supports one Google record per profile and password-protected import/export.
3. Import confirms, backs up, and replaces the complete current vault.
4. Remember-on-device, lock-now, and remove-remembered-unlock work through DPAPI without exporting the device wrapping.
5. The selected external Chrome profile is used, not a WebView or temporary profile.
6. Supported email → password → TOTP steps can be filled/submitted and completion is detected.
7. Unsupported challenges hand control back to the user without bypass attempts or infinite retries.
8. No secret appears in logs, clipboard, settings, or unencrypted export metadata.
9. Vault and state-machine tests pass, and manual acceptance is documented separately.
