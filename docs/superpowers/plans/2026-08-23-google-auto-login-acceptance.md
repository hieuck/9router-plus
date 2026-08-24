# Google Auto-Login Acceptance

Date: 2026-08-24

## Automated verification

| Check | Result | Evidence |
|---|---|---|
| Full solution tests | PASS | `dotnet test RouterPlus.sln --configuration Debug --no-restore` — RouterPlus.Core.Tests: 286 passed; RouterPlus.Updater.Tests: 5 passed; 0 failed, 0 skipped. |
| Solution build | PASS | `dotnet build RouterPlus.sln --configuration Debug --no-restore` — 0 warnings, 0 errors. |
| Vault storage tests | PASS | 13 focused GoogleLoginVaultStore tests passed after lifecycle synchronization fixes. |
| MainViewModel/integration tests | PASS | 18 focused tests passed, including manual-handoff disposal and selected-profile routing. |
| CDP/session tests | PASS | 21 focused managed-session and CDP adapter tests passed. |
| State-machine tests | PASS | 18 focused bounded state-machine tests passed. |
| Domain/TOTP tests | PASS | 23 focused domain tests passed. |
| Diff/whitespace check | PASS | `git diff --check` completed without findings. |
| Secret scan | PASS | Vault tests confirmed credential fields are absent from plaintext envelope metadata; reviewed Google auto-login status, log, exception, clipboard, and settings paths for password/TOTP/OTP writes. |

## Manual acceptance checklist

Status: **NOT RUN — requires a disposable Google test account supplied outside the repository.**

The following checks remain for a real Windows/Chrome environment. Use synthetic labels in notes; never record an email address, password, TOTP secret, OTP, cookie, token, or full sensitive URL here.

- [ ] Right-clicking a selected profile opens the separate auto-login dialog.
- [ ] The existing manual `Đăng nhập Google bằng Chrome` action still opens external Chrome without CDP automation.
- [ ] First-use vault unlock creates the local encrypted vault.
- [ ] A new profile defaults the email field to the profile display name and rejects an invalid address.
- [ ] Save information persists the email, password, and TOTP secret in the encrypted vault.
- [ ] Editing an existing profile email persists only the linkage change during Auto sign in.
- [ ] Auto sign in for a new profile uses current fields for the run without persisting password/TOTP until Save information is selected.
- [ ] Managed Chrome launches outside the app with the selected user-data directory and profile directory.
- [ ] The CDP endpoint is loopback-only and the managed target is the target marked for this run.
- [ ] Supported email → password → TOTP steps complete on a disposable account.
- [ ] Successful login leaves cookies/session state in the selected Chrome profile.
- [ ] CAPTCHA, passkey, security-key, anti-bot, or unusual-login challenge stops automation and leaves Chrome open for manual continuation.
- [ ] Timeout, cancellation, browser disconnect, and unsupported origin return safe status without secret values.
- [ ] Import confirms replacement, creates a `.bak` backup, replaces rather than merges, and invalidates remembered unlock.
- [ ] Export can be imported into a second temporary vault root with the export password.
- [ ] Remember-on-device unlock, Lock vault now, and Remove remembered unlock behave as designed.

## Safety notes

- No real account data was used in automated tests or committed files.
- Live acceptance must be performed only with an account owned by or authorized for the operator.
- The feature does not bypass CAPTCHA, passkeys, security keys, anti-bot checks, or additional verification.
- Manual acceptance results must be appended without sensitive values before any release decision.
