# Google Auto-Login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a separate, encrypted-vault-backed Google auto-login flow for the selected external Chrome profile without changing the existing manual Google-login action.

**Architecture:** Keep the domain rules and Google login state machine independent from WPF and Chrome. Store one profile-bound credential record per stable `ChromeProfile.Id` in a portable AES-256-GCM envelope whose random payload key is password-wrapped for export and optionally DPAPI-wrapped for same-Windows-user unlock. Launch the selected external Chrome profile with a loopback-only CDP endpoint, adapt CDP to a small browser interface, and let a WPF dialog coordinate vault unlock, record editing, import/export, and the bounded auto-login run.

**Tech Stack:** .NET 8 / C# / WPF, `System.Security.Cryptography` (`AesGcm`, `Rfc2898DeriveBytes.Pbkdf2`, `ProtectedData`), `HttpClient`, `ClientWebSocket`, xUnit, existing `ChromeLauncher`, `ChromeProfile`, `SettingsStore`, and existing WPF resource dictionaries.

**Spec:** `docs/superpowers/specs/2026-08-23-google-auto-login-design.md`

## Global Constraints

- One vault file stores the login records used by the application.
- One Chrome profile maps to exactly one Google account record.
- Records are keyed by the existing stable `ProfileId`, derived from the profile's stable user-data path and directory name; display names are not identity keys.
- Editing the email and choosing Save or Auto sign in is an explicit decision that the profile uses the new email. The new email replaces the previous record value. It is not a one-time override and does not implicitly sign out, delete cookies, or change the existing Chrome session.
- Vault import replaces the entire current vault, after confirmation, and creates a backup before replacement. It does not merge records.
- The vault password is never stored in plaintext. “Remember on this device” is opt-in and stores only a DPAPI-protected wrapping of a vault key outside the portable export; it is scoped to the current Windows user and device context.
- The existing external Chrome launch action stays unchanged. Auto sign-in uses Chrome started in app-managed mode with CDP bound only to `127.0.0.1` on a random local port.
- The app must not connect to an already-running Chrome profile that lacks CDP, must not force-close it, and must show actionable guidance to close and retry.
- The app must not use an embedded browser, clipboard, Chrome extension, UI Automation fallback, or a remote CDP endpoint for this feature.
- Do not store secrets in `settings.json`, logs, clipboard, crash reports, or unencrypted export metadata.
- Do not attempt to bypass CAPTCHA, passkeys, security keys, anti-bot controls, or additional verification.
- Do not silently fall back to less controlled UI automation.

## File map

Create focused files rather than adding cryptography, CDP protocol code, or dialog state to `MainViewModel`:

- `src/RouterPlus.Core/Security/GoogleLoginCredential.cs` — immutable profile-bound record and email validation.
- `src/RouterPlus.Core/Security/GoogleLoginVault.cs` — one-record-per-profile collection operations.
- `src/RouterPlus.Core/Security/GoogleTotpGenerator.cs` — Base32 decoding and RFC 6238-compatible six-digit TOTP generation.
- `src/RouterPlus.Core/Security/GoogleLoginAutomationModels.cs` — login states, field names, snapshots, results, and safe error categories.
- `src/RouterPlus.Infrastructure/Security/GoogleLoginVaultPaths.cs` — local vault and remembered-key paths.
- `src/RouterPlus.Infrastructure/Security/GoogleLoginVaultStore.cs` — versioned envelope, password key wrapping, AES-GCM, atomic writes, backup, import/export, and remembered-key handling.
- `src/RouterPlus.Infrastructure/Security/IGoogleLoginVaultStore.cs` — store/session boundary consumed by the app layer.
- `src/RouterPlus.Infrastructure/Chrome/ChromeManagedSession.cs` — managed process lifetime, random port, loopback endpoint polling, and profile-in-use detection.
- `src/RouterPlus.Infrastructure/Chrome/ChromeCdpClient.cs` — minimal CDP HTTP/WebSocket transport with request IDs and cancellation.
- `src/RouterPlus.Infrastructure/Chrome/GoogleLoginCdpBrowser.cs` — CDP adapter that exposes only the supported Google page operations.
- `src/RouterPlus.Infrastructure/Chrome/IGoogleLoginBrowser.cs` — testable browser boundary.
- `src/RouterPlus.Infrastructure/Chrome/GoogleLoginStateMachine.cs` — bounded email/password/TOTP/manual-intervention flow.
- `src/RouterPlus.App/ViewModels/GoogleAutoLoginViewModel.cs` — dialog state, vault commands, save semantics, and automation orchestration.
- `src/RouterPlus.App/Views/GoogleAutoLoginDialog.xaml` and `.xaml.cs` — WPF dialog and secure PasswordBox/file-picker event handling.
- `src/RouterPlus.App/Resources/Strings.vi.xaml` and `Strings.en.xaml` — localized dialog/menu/error strings.
- `src/RouterPlus.App/MainWindow.xaml` and `.xaml.cs` — separate context-menu item and dialog ownership only.
- `src/RouterPlus.App/ViewModels/MainViewModel.cs` — dependency seams and creation of the dialog view model; existing manual Google launch remains unchanged.
- `tests/RouterPlus.Core.Tests/GoogleLoginCredentialTests.cs`, `GoogleTotpGeneratorTests.cs`, `GoogleLoginVaultTests.cs` — domain and cryptographic-adjacent deterministic tests.
- `tests/RouterPlus.Core.Tests/GoogleLoginVaultStoreTests.cs` — temporary-file envelope, backup, import, tamper, and remembered-key tests.
- `tests/RouterPlus.Core.Tests/GoogleLoginStateMachineTests.cs` — fake-browser state-machine tests.
- `tests/RouterPlus.Core.Tests/GoogleAutoLoginViewModelTests.cs` — dialog orchestration, save semantics, and redacted-status tests.

---

### Task 1: Add profile-bound credential and TOTP domain models

**Files:**
- Create: `src/RouterPlus.Core/Security/GoogleLoginCredential.cs`
- Create: `src/RouterPlus.Core/Security/GoogleLoginVault.cs`
- Create: `src/RouterPlus.Core/Security/GoogleTotpGenerator.cs`
- Create: `src/RouterPlus.Core/Security/GoogleLoginAutomationModels.cs`
- Test: `tests/RouterPlus.Core.Tests/GoogleLoginCredentialTests.cs`
- Test: `tests/RouterPlus.Core.Tests/GoogleTotpGeneratorTests.cs`
- Test: `tests/RouterPlus.Core.Tests/GoogleLoginVaultTests.cs`

**Interfaces:**
- Produces `GoogleLoginCredential`, `GoogleLoginVault`, `GoogleTotpGenerator`, `GoogleLoginPageState`, `GoogleLoginField`, and `GoogleLoginResult` for later tasks.
- `GoogleLoginCredential` constructor signature: `GoogleLoginCredential(string profileId, string email, string password, string totpSecret)`.
- `GoogleLoginVault` exposes `IReadOnlyList<GoogleLoginCredential> Records`, `GoogleLoginCredential? Find(string profileId)`, and `GoogleLoginVault Upsert(GoogleLoginCredential credential)`.
- `GoogleTotpGenerator.Generate(string secret, DateTimeOffset utcNow, int digits = 6, int periodSeconds = 30)` returns the numeric code as a string.

- [ ] **Step 1: Write failing credential and vault tests**

```csharp
[Fact]
public void Credential_rejects_blank_profile_id_and_invalid_email()
{
    Assert.Throws<ArgumentException>(() => new GoogleLoginCredential("", "user@example.com", "p", "s"));
    Assert.Throws<FormatException>(() => new GoogleLoginCredential("profile-1", "not-an-email", "p", "s"));
}

[Fact]
public void Vault_upsert_replaces_the_existing_record_for_the_same_profile_id()
{
    var vault = new GoogleLoginVault();
    var first = new GoogleLoginCredential("profile-1", "first@example.com", "p1", "s1");
    var second = new GoogleLoginCredential("profile-1", "second@example.com", "p2", "s2");

    var updated = vault.Upsert(first).Upsert(second);

    var record = Assert.Single(updated.Records);
    Assert.Equal("second@example.com", record.Email);
}
```

- [ ] **Step 2: Run the focused tests and verify they fail for missing types**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~GoogleLoginCredentialTests|FullyQualifiedName~GoogleLoginVaultTests"`

Expected: FAIL because the new domain types do not exist.

- [ ] **Step 3: Implement the minimal immutable models**

Use `StringComparer.Ordinal` for `ProfileId` identity, trim the profile ID and email before validation, require a non-empty password and TOTP secret only when constructing a complete credential, and make `GoogleLoginVault.Upsert` return a new vault with at most one record per profile. Do not add display-name lookup to the domain model; the app layer supplies the default email.

- [ ] **Step 4: Add deterministic RFC 6238 TOTP tests before implementation**

Use the standard SHA-1 vector at Unix time `59`, with secret `12345678901234567890` encoded as Base32 `GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ`, and assert six-digit output `287082`. Add tests for spaces/hyphens in a secret, an invalid Base32 character, and rejection of a non-positive period.

- [ ] **Step 5: Implement TOTP without persistence or logging**

Decode Base32 case-insensitively, calculate the moving counter from Unix seconds divided by `periodSeconds`, use HMAC-SHA1 dynamic truncation, and left-pad to `digits`. Return only the generated code; never write it to a file, clipboard, or diagnostic message.

- [ ] **Step 6: Run all Task 1 tests**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~GoogleLoginCredentialTests|FullyQualifiedName~GoogleTotpGeneratorTests|FullyQualifiedName~GoogleLoginVaultTests"`

Expected: PASS.

- [ ] **Step 7: Commit the domain slice**

```bash
git add src/RouterPlus.Core/Security/GoogleLoginCredential.cs src/RouterPlus.Core/Security/GoogleLoginVault.cs src/RouterPlus.Core/Security/GoogleTotpGenerator.cs src/RouterPlus.Core/Security/GoogleLoginAutomationModels.cs tests/RouterPlus.Core.Tests/GoogleLoginCredentialTests.cs tests/RouterPlus.Core.Tests/GoogleTotpGeneratorTests.cs tests/RouterPlus.Core.Tests/GoogleLoginVaultTests.cs
git commit -m "feat: add Google login domain models"
```

### Task 2: Implement the encrypted portable vault and remembered unlock

**Files:**
- Create: `src/RouterPlus.Infrastructure/Security/IGoogleLoginVaultStore.cs`
- Create: `src/RouterPlus.Infrastructure/Security/GoogleLoginVaultPaths.cs`
- Create: `src/RouterPlus.Infrastructure/Security/GoogleLoginVaultStore.cs`
- Test: `tests/RouterPlus.Core.Tests/GoogleLoginVaultStoreTests.cs`
- Modify: `src/RouterPlus.Infrastructure/RouterPlus.Infrastructure.csproj` only if a package is required by the implementation; use the existing `System.Security.Cryptography.ProtectedData` package and no additional package for this task.

**Interfaces:**
- `IGoogleLoginVaultStore.OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default)` returns `Task<GoogleLoginVaultSession>`.
- `IGoogleLoginVaultStore.TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default)` returns `Task<GoogleLoginVaultSession?>`.
- `IGoogleLoginVaultStore.SaveAsync(GoogleLoginVaultSession session, CancellationToken cancellationToken = default)` persists the current record set using the session's payload key.
- `IGoogleLoginVaultStore.ExportAsync(GoogleLoginVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default)` writes a portable envelope with the supplied export password.
- `IGoogleLoginVaultStore.ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default)` validates/decrypts the source first, copies the current file to `currentPath + ".bak"`, atomically replaces the current file, and removes the remembered key for the previous vault.
- `GoogleLoginVaultSession` exposes `GoogleLoginVault Vault`, `string VaultId`, `void Replace(GoogleLoginVault vault)`, `Task RememberAsync()`, `Task RemoveRememberedAsync()`, and `ValueTask DisposeAsync()`; disposal zeroes owned key buffers.
- `GoogleLoginVaultPaths` returns the default local paths `%LOCALAPPDATA%\9RouterPlus\google-login-vault.gvault` and `%LOCALAPPDATA%\9RouterPlus\google-login-vault.remembered` and accepts an optional root for tests.

- [ ] **Step 1: Write failing envelope and round-trip tests**

```csharp
[Fact]
public async Task Save_and_open_round_trip_the_profile_record()
{
    var paths = new GoogleLoginVaultPaths(CreateTempDirectory());
    var store = new GoogleLoginVaultStore(paths);
    await using var session = await store.CreateAsync(paths.VaultPath, "vault-password");
    session.Replace(new GoogleLoginVault().Upsert(
        new GoogleLoginCredential("profile-1", "user@example.com", "password", "JBSWY3DPEHPK3PXP")));
    await store.SaveAsync(session);

    await using var reopened = await store.OpenAsync(paths.VaultPath, "vault-password");

    Assert.Equal("user@example.com", reopened.Vault.Find("profile-1")!.Email);
}
```

- [ ] **Step 2: Add failure tests for wrong password and tampering**

Write the vault file, change one byte in its JSON/base64 content, and assert `CryptographicException` for both a wrong password and tampered ciphertext. Assert that no exception exposes the password, TOTP secret, or full serialized payload in its message.

- [ ] **Step 3: Implement the versioned envelope**

Use a JSON document with these exact non-secret fields: `Version` (`1`), `VaultId` (16 random bytes, Base64), `KdfAlgorithm` (`PBKDF2-HMAC-SHA256`), `KdfIterations` (`600000`), `KdfSalt` (16 random bytes, Base64), `KeyWrapNonce` (12 random bytes, Base64), `KeyWrapTag` (16 bytes, Base64), `WrappedPayloadKey` (32-byte ciphertext, Base64), `PayloadNonce` (12 random bytes, Base64), `PayloadTag` (16 bytes, Base64), and `PayloadCiphertext` (authenticated JSON payload, Base64). Generate a random 32-byte payload key; wrap it with AES-256-GCM using the PBKDF2-derived key and encrypt the serialized records with the payload key. Authenticate `Version`, `VaultId`, and algorithm identifiers as associated data. Reject unknown version/algorithm values before decrypting.

- [ ] **Step 4: Implement atomic save and backup semantics**

Create the parent directory, write to a unique `.tmp` file, flush and close it, then replace the destination. For import, decrypt and validate the source into memory before touching the current file; copy the existing file to `google-login-vault.gvault.bak` (or `currentPath + ".bak"`) and use a second temporary file plus `File.Move(..., overwrite: true)` for replacement. If backup or replacement fails, leave the original current file untouched and do not clear the remembered key.

- [ ] **Step 5: Implement DPAPI remembered unlock**

Protect the random payload key with `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` and entropy derived from the literal `9RouterPlus.GoogleLoginVault.v1` plus the UTF-8 `VaultId`. Store only a version, VaultId, and Base64 protected key in `google-login-vault.remembered`; never include this file in export. `TryOpenRememberedAsync` must reject a missing/mismatched VaultId and delete stale remembered material. `RemoveRememberedAsync` deletes only the remembered file.

- [ ] **Step 6: Add import/export and remembered-key tests**

Cover: export/import round trip into a second temporary root; import replaces rather than merges; backup exists after successful import; a malformed source leaves the old vault and backup state unchanged; `RememberAsync` reopens on the same user; `RemoveRememberedAsync` prevents remembered reopening; and importing a second vault invalidates the first remembered key. Use synthetic credentials only.

- [ ] **Step 7: Run the focused storage tests**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~GoogleLoginVaultStoreTests"`

Expected: PASS.

- [ ] **Step 8: Commit the vault slice**

```bash
git add src/RouterPlus.Infrastructure/Security/IGoogleLoginVaultStore.cs src/RouterPlus.Infrastructure/Security/GoogleLoginVaultPaths.cs src/RouterPlus.Infrastructure/Security/GoogleLoginVaultStore.cs tests/RouterPlus.Core.Tests/GoogleLoginVaultStoreTests.cs
git commit -m "feat: add encrypted Google login vault"
```

### Task 3: Build and test the bounded Google login state machine

**Files:**
- Create: `src/RouterPlus.Infrastructure/Chrome/IGoogleLoginBrowser.cs`
- Create: `src/RouterPlus.Infrastructure/Chrome/GoogleLoginStateMachine.cs`
- Test: `tests/RouterPlus.Core.Tests/GoogleLoginStateMachineTests.cs`

**Interfaces:**
- `IGoogleLoginBrowser` exposes `Task<GoogleLoginPageState> ReadStateAsync(CancellationToken)`, `Task FillAsync(GoogleLoginField field, string value, CancellationToken)`, `Task SubmitAsync(GoogleLoginField field, CancellationToken)`, and `ValueTask DisposeAsync()`.
- `GoogleLoginStateMachine.RunAsync(IGoogleLoginBrowser browser, GoogleLoginCredential credential, CancellationToken cancellationToken)` returns `Task<GoogleLoginResult>`.
- `GoogleLoginPageState` includes `Uri PageUri`, `bool HasEmailField`, `bool HasPasswordField`, `bool HasTotpField`, `bool HasCompletionSignal`, and `bool HasManualChallenge`.
- `GoogleLoginResult` uses the categories `Success`, `ManualInterventionRequired`, `InvalidCredentials`, `Timeout`, `Cancelled`, `BrowserDisconnected`, and `UnsupportedPage`; it carries only a safe category/message, never a secret or DOM snapshot.

- [ ] **Step 1: Write fake-browser tests for every allowed transition**

Create a fake browser that returns scripted page states and records only field names and submission counts, not field values. Test email → password → TOTP → completion, email-only completion for an already partially authenticated flow, manual challenge stop, unsupported origin stop, timeout, cancellation between every step, browser disconnect, and a wrong-origin tab. Assert that success is returned only when `HasCompletionSignal` is true.

- [ ] **Step 2: Run the state-machine tests and verify they fail**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~GoogleLoginStateMachineTests"`

Expected: FAIL because the browser boundary and state machine do not exist.

- [ ] **Step 3: Implement strict origin and transition guards**

Accept an entry page whose host is exactly `accounts.google.com`; accept completion only on `accounts.google.com`, `myaccount.google.com`, or `www.google.com` with `HasCompletionSignal`. Return `ManualInterventionRequired` immediately for `HasManualChallenge`, return `UnsupportedPage` for any other origin or unrecognized field combination, and enforce a 30-second per-step timeout plus a five-minute total timeout. Never retry a submitted credential automatically.

- [ ] **Step 4: Implement just-in-time field handling**

Fill and submit email first, then password, then generate the TOTP code with `GoogleTotpGenerator.Generate` immediately before filling the TOTP field. Do not persist or include the generated code in `GoogleLoginResult`; dispose/clear local buffers after each operation as far as managed memory permits. Pass cancellation through every browser call.

- [ ] **Step 5: Run the state-machine tests and commit**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~GoogleLoginStateMachineTests"`

Expected: PASS.

```bash
git add src/RouterPlus.Infrastructure/Chrome/IGoogleLoginBrowser.cs src/RouterPlus.Infrastructure/Chrome/GoogleLoginStateMachine.cs tests/RouterPlus.Core.Tests/GoogleLoginStateMachineTests.cs
git commit -m "feat: add bounded Google login state machine"
```

### Task 4: Add managed Chrome launch and a loopback CDP adapter

**Files:**
- Create: `src/RouterPlus.Infrastructure/Chrome/ChromeManagedSession.cs`
- Create: `src/RouterPlus.Infrastructure/Chrome/ChromeCdpClient.cs`
- Create: `src/RouterPlus.Infrastructure/Chrome/GoogleLoginCdpBrowser.cs`
- Modify: `src/RouterPlus.Infrastructure/Chrome/ChromeLauncher.cs` to preserve `Launch(...)` and add `LaunchManagedAsync(...)`.
- Test: `tests/RouterPlus.Core.Tests/ChromeManagedSessionTests.cs`
- Test: `tests/RouterPlus.Core.Tests/GoogleLoginCdpBrowserTests.cs`

**Interfaces:**
- `ChromeLauncher.LaunchManagedAsync(ChromeInstallation installation, ChromeProfile profile, Uri startUri, CancellationToken cancellationToken)` returns `Task<ChromeManagedSession>`.
- `ChromeManagedSession` exposes `Process Process`, `Uri DevToolsBaseUri`, `Task<IGoogleLoginBrowser> ConnectGoogleLoginAsync(CancellationToken)`, and `ValueTask DisposeAsync()`.
- `ChromeCdpClient` uses `ClientWebSocket` and `HttpClient` only; it exposes `Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken)` and `IAsyncEnumerable<JsonElement> EventsAsync(CancellationToken)` internally to the adapter. It must reject non-loopback endpoints.
- `GoogleLoginCdpBrowser` implements `IGoogleLoginBrowser` and is constructed with a connected `ChromeCdpClient` plus the target ID created for this run.

- [ ] **Step 1: Add process/endpoint test seams**

Add an internal constructor or factory seam for endpoint polling and process creation so tests can use a fake process-start result and a local fake `/json/version`/WebSocket server. Do not make the production API accept arbitrary remote URLs.

- [ ] **Step 2: Write failing managed-session tests**

Test that launch arguments contain the selected installation user-data directory, selected profile directory, `--remote-debugging-address=127.0.0.1`, a non-zero random `--remote-debugging-port`, and the Google accounts URL. Test that a loopback endpoint is accepted, a non-loopback endpoint is rejected, endpoint polling times out with a safe message, and a process that forwards to an already-running profile is reported as profile-in-use rather than force-closed.

- [ ] **Step 3: Implement managed launch and endpoint validation**

Choose an available TCP port on loopback, start Chrome with the existing `--user-data-dir` and `--profile-directory` arguments plus the managed CDP flags, poll `http://127.0.0.1:{port}/json/version` for up to 15 seconds, verify the returned WebSocket URL host is `127.0.0.1` or `localhost`, and associate the endpoint with the started process. If Chrome exits or the endpoint never appears, return a typed safe error. Do not kill an unrelated existing Chrome process.

- [ ] **Step 4: Implement the minimal CDP transport**

Use monotonically increasing request IDs, a concurrent pending-request map, one receive loop, cancellation-aware request completion, and disposal that closes the socket and fails pending requests. Redact all CDP error descriptions before they reach `StatusText` or logs. Allow only the CDP methods required by the adapter: `Target.getTargets`, `Target.attachToTarget`, `Runtime.evaluate`, `Runtime.callFunctionOn`, `Input.dispatchKeyEvent`, `Input.insertText`, and `Page.bringToFront`.

- [ ] **Step 5: Implement the Google page adapter**

Select exactly one page target created by the managed run whose URL host is `accounts.google.com`; attach to it and bring it to front. Use `Runtime.evaluate` with a static script and JSON-encoded arguments to return only booleans/field metadata for `ReadStateAsync`. Use `Input.insertText` after focusing the selected semantic input and dispatch the submit action through the matching button/form; never construct JavaScript by string-concatenating a secret. Reject a target change or wrong origin before filling.

- [ ] **Step 6: Add adapter tests with synthetic CDP responses**

Assert that state inspection exposes only booleans, that field fills target the correct semantic field, that no clipboard command is emitted, that a second tab is not selected, that wrong origins are rejected, and that CDP errors become safe categories without including the fake password/TOTP value.

- [ ] **Step 7: Run the focused Chrome tests and commit**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~ChromeManagedSessionTests|FullyQualifiedName~GoogleLoginCdpBrowserTests"`

Expected: PASS. A live manual Chrome check is deferred to Task 7 and must use a dedicated test account.

```bash
git add src/RouterPlus.Infrastructure/Chrome/ChromeLauncher.cs src/RouterPlus.Infrastructure/Chrome/ChromeManagedSession.cs src/RouterPlus.Infrastructure/Chrome/ChromeCdpClient.cs src/RouterPlus.Infrastructure/Chrome/GoogleLoginCdpBrowser.cs tests/RouterPlus.Core.Tests/ChromeManagedSessionTests.cs tests/RouterPlus.Core.Tests/GoogleLoginCdpBrowserTests.cs
git commit -m "feat: control managed Chrome through loopback CDP"
```

### Task 5: Add the WPF auto-login dialog and vault controls

**Files:**
- Create: `src/RouterPlus.App/ViewModels/GoogleAutoLoginViewModel.cs`
- Create: `src/RouterPlus.App/Views/GoogleAutoLoginDialog.xaml`
- Create: `src/RouterPlus.App/Views/GoogleAutoLoginDialog.xaml.cs`
- Modify: `src/RouterPlus.App/Resources/Strings.vi.xaml`
- Modify: `src/RouterPlus.App/Resources/Strings.en.xaml`
- Test: `tests/RouterPlus.Core.Tests/GoogleAutoLoginViewModelTests.cs`

**Interfaces:**
- `GoogleAutoLoginViewModel(ChromeProfile profile, IGoogleLoginVaultStore vaultStore, Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> runAutomation)` is the dialog state owner.
- Public properties: `ProfileName`, `Email`, `VaultPasswordStatus`, `PasswordStatus`, `TotpStatus`, `StatusText`, `IsVaultUnlocked`, `IsBusy`, `RememberOnDevice`, and `CanAutoLogin`.
- Public methods: `Task UnlockVaultAsync(string vaultPassword, bool remember, CancellationToken)`, `Task SaveInformationAsync(string email, string password, string totpSecret, CancellationToken)`, `Task<GoogleLoginResult> AutoLoginAsync(string email, string password, string totpSecret, CancellationToken)`, `Task ImportAsync(string sourcePath, string sourcePassword, CancellationToken)`, `Task ExportAsync(string destinationPath, string exportPassword, CancellationToken)`, `Task LockVaultAsync(CancellationToken)`, and `Task RemoveRememberedUnlockAsync(CancellationToken)`.
- The dialog code-behind reads `PasswordBox.Password` only on button events, passes it to the view model, then sets the controls to empty immediately; it does not two-way-bind password/TOTP/vault-password values to a string property.

- [ ] **Step 1: Write failing view-model tests for save semantics**

Use an in-memory fake vault store and fake automation delegate. Test that a new record defaults `Email` to `profile.Name`; invalid email blocks both Save and AutoLogin; `SaveInformationAsync` persists email/password/TOTP; `AutoLoginAsync` persists a changed email but does not persist newly entered password/TOTP; and the automation delegate receives the current fields exactly once.

- [ ] **Step 2: Add tests for unlock, lock, import, export, and redacted status**

Test wrong vault password returns a safe status, remember-on-device is passed to the store, lock clears `IsVaultUnlocked`, import calls the replacement operation only after successful source validation, export uses the selected destination, and `StatusText`/`LogText` never contains synthetic password/TOTP/OTP values.

- [ ] **Step 3: Implement the view model**

On initialization, load the one record for `profile.Id`; if absent, set `Email = profile.Name`. Require a valid email before calling the automation delegate. Keep the unlocked session private, update only non-sensitive status strings, and map typed results to localized resource keys/messages. When `RememberOnDevice` is true, call `session.RememberAsync()` after successful unlock; when false, do not create or modify remembered material.

- [ ] **Step 4: Build the dialog layout**

Follow `ChromeSelectionDialog` styling and `CenterOwner` behavior. Include profile name/linkage status, vault status and vault-password PasswordBox with Unlock, Remember on this device checkbox, Import, Export, Lock vault now, Gmail email TextBox, password PasswordBox, TOTP secret PasswordBox, Save information, Auto sign in, Cancel, and a non-sensitive status TextBlock. Disable credential fields/actions until the vault is unlocked; disable all actions while busy except Cancel.

- [ ] **Step 5: Implement secure code-behind event handling**

Use `Microsoft.Win32.OpenFileDialog`/`SaveFileDialog` for `.gvault` files. Confirm import with a WPF Yes/No message box that states it replaces the current vault and creates a backup. Read and clear PasswordBox values in the same handler; never use clipboard APIs. Set `DialogResult` only on Cancel or after a completed Save/AutoLogin operation, and leave Chrome open for manual intervention results.

- [ ] **Step 6: Add Vietnamese and English resources**

Add keys for the menu item, dialog title/labels, vault unlock/remember/import/export/lock actions, supported state messages, profile-in-use guidance, manual intervention, timeout, cancellation, invalid email, and generic safe failure. Do not interpolate secrets into resource strings.

- [ ] **Step 7: Run the focused dialog view-model tests and commit**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~GoogleAutoLoginViewModelTests"`

Expected: PASS.

```bash
git add src/RouterPlus.App/ViewModels/GoogleAutoLoginViewModel.cs src/RouterPlus.App/Views/GoogleAutoLoginDialog.xaml src/RouterPlus.App/Views/GoogleAutoLoginDialog.xaml.cs src/RouterPlus.App/Resources/Strings.vi.xaml src/RouterPlus.App/Resources/Strings.en.xaml tests/RouterPlus.Core.Tests/GoogleAutoLoginViewModelTests.cs
git commit -m "feat: add encrypted Google auto-login dialog"
```

### Task 6: Integrate the new action without changing manual Google launch

**Files:**
- Modify: `src/RouterPlus.App/MainWindow.xaml:805-813` to add a distinct localized `Auto sign in to Google` menu item beside the existing manual item.
- Modify: `src/RouterPlus.App/MainWindow.xaml.cs:249-252` to open `GoogleAutoLoginDialog` with `Owner = this` and the selected profile.
- Modify: `src/RouterPlus.App/ViewModels/MainViewModel.cs:31-99,1937-1954` to inject/create the vault store and automation service while leaving `OpenSelectedGoogleLoginAsync` behavior unchanged.
- Test: `tests/RouterPlus.Core.Tests/MainViewModelProfileContextMenuTests.cs` and `GoogleAutoLoginViewModelTests.cs` for selected-profile routing.

**Interfaces:**
- Add optional constructor seams to `MainViewModel` for `IGoogleLoginVaultStore` and `Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>` so unit tests never launch Chrome.
- Add `MainViewModel.CreateGoogleAutoLoginViewModel()` returning a `GoogleAutoLoginViewModel` for `SelectedProfile`; when no profile is selected, set the existing safe status text and return `null`.
- Add `MainWindow.ProfileGoogleAutoLogin_Click` that obtains the view model, creates `new GoogleAutoLoginDialog(dialogViewModel) { Owner = this }`, and calls `ShowDialog()`.

- [ ] **Step 1: Write the routing regression test**

Create a selected `ChromeProfile`, call `CreateGoogleAutoLoginViewModel`, and assert the view model uses that profile's stable ID and display name. Call the existing `OpenSelectedGoogleLoginAsync` test with a fake launcher/seam and assert it still requests `https://accounts.google.com/` through the old launch path.

- [ ] **Step 2: Add the menu item and event handler**

Place the new command directly below `Đăng nhập Google bằng Chrome`; preserve the existing item's header and click handler. Use only the new handler for the CDP flow.

- [ ] **Step 3: Wire service creation and selected-profile guards**

Use the configured Chrome locator/installation in the automation delegate, pass the selected profile unchanged, and map a null selected profile to the existing Vietnamese selection status. Do not move cryptography or CDP details into `MainViewModel`.

- [ ] **Step 4: Run integration-focused tests and build the app**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~MainViewModelProfileContextMenuTests|GoogleAutoLoginViewModelTests"`

Then run: `dotnet build RouterPlus.sln --configuration Debug`

Expected: PASS and a successful WPF build; the manual Google action remains covered by its existing behavior.

- [ ] **Step 5: Commit the integration slice**

```bash
git add src/RouterPlus.App/MainWindow.xaml src/RouterPlus.App/MainWindow.xaml.cs src/RouterPlus.App/ViewModels/MainViewModel.cs tests/RouterPlus.Core.Tests/MainViewModelProfileContextMenuTests.cs tests/RouterPlus.Core.Tests/GoogleAutoLoginViewModelTests.cs
git commit -m "feat: add Google auto-login profile action"
```

### Task 7: Verify security, regression coverage, and live acceptance

**Files:**
- Modify: `tests/RouterPlus.Core.Tests/MainViewModelErrorLoggingTests.cs` only if a redaction regression test needs an existing test seam.
- Create: `docs/superpowers/plans/2026-08-23-google-auto-login-acceptance.md` containing a manual checklist with synthetic placeholders only.
- No production code changes unless a failing verification test identifies a defect; fix the smallest responsible file and rerun its focused test.

- [ ] **Step 1: Run the complete automated test suite**

Run: `dotnet test RouterPlus.sln --configuration Debug --no-restore`

Expected: all existing and new tests pass. If a test fails, reproduce only that failure, make the minimal fix, and rerun the affected test before the full suite.

- [ ] **Step 2: Inspect generated files and logs for secret leakage**

Run: `git diff --check` and inspect the temporary test roots for unencrypted credential values. Confirm the application vault JSON contains only the documented envelope metadata plus Base64 ciphertext, the remembered file is not in the export, and no new code writes password/TOTP/OTP values to `StatusText`, `LogText`, clipboard, settings, or exception messages.

- [ ] **Step 3: Run the C# security/code review**

Invoke the repository's `ecc:csharp-reviewer` on the changed C# files, then address only verified findings. Pay special attention to AES-GCM nonce reuse, PBKDF2 parameter enforcement, DPAPI entropy binding, atomic replacement, CDP origin/target validation, cancellation, and secret redaction.

- [ ] **Step 4: Run the manual acceptance checklist with a dedicated test account**

Use a disposable Google test account supplied outside the repository. Verify: right-click selected profile opens the new dialog; first-use unlock and save; email defaults to profile display name; editing email persists the new profile linkage; Save information persists password/TOTP; Auto sign in uses unsaved current fields without persisting new password/TOTP; Chrome opens outside the app with the selected profile; email/password/TOTP supported steps complete; cookies/session remain in that Chrome profile; CAPTCHA/passkey/challenge stops with Chrome available for manual continuation; import confirms, backs up, and replaces; export imports on a second temporary root; lock and remembered unlock behave as designed.

- [ ] **Step 5: Record the final verification result**

Add the command outputs and manual pass/fail results to `docs/superpowers/plans/2026-08-23-google-auto-login-acceptance.md` without recording any email address, password, TOTP secret, OTP, cookie, token, or full sensitive URL.

- [ ] **Step 6: Commit verification documentation**

```bash
git add docs/superpowers/plans/2026-08-23-google-auto-login-acceptance.md
git commit -m "docs: record Google auto-login acceptance checks"
```

## Plan self-review

- **Spec coverage:** The domain task covers stable profile mapping, one record per profile, default/edited email, and TOTP. The vault task covers versioning, PBKDF2-HMAC-SHA256, AES-256-GCM, password-protected portable export, atomic backup-and-replace import, and DPAPI remembered unlock. Tasks 3–4 cover the bounded CDP state machine, loopback-only external Chrome, origin/tab checks, manual handoff, cancellation, timeout, and completion-only success. Tasks 5–6 cover the WPF dialog, menu entry, save semantics, localization, and preservation of the existing manual launch. Task 7 covers regression, security, and live acceptance without real credentials in source or automated tests.
- **Placeholder scan:** No `TBD`, `TODO`, or unspecified implementation step is used. KDF iterations, envelope fields, file names, paths, method signatures, timeout values, and test commands are explicit.
- **Type consistency:** Later tasks consume `GoogleLoginCredential`, `GoogleLoginVault`, `GoogleLoginResult`, `IGoogleLoginBrowser`, `GoogleLoginStateMachine`, `IGoogleLoginVaultStore`, `GoogleLoginVaultSession`, and `GoogleAutoLoginViewModel` exactly as introduced in earlier tasks.
- **Scope check:** The work remains one feature with independently testable domain, storage, browser automation, UI, integration, and verification slices; no unrelated refactoring is included.
