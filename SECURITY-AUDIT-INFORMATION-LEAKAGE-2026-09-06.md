# Security Audit Report - Information Leakage Analysis
**Project:** 9router-plus  
**Date:** 2026-09-06  
**Auditor:** Security Review  
**Scope:** Comprehensive audit for information leakage vulnerabilities in code execution paths

---

## Executive Summary

This audit examined the 9router-plus codebase for potential information leakage vulnerabilities through logging, error handling, diagnostic output, and network communication. The project demonstrates **strong security practices** overall, with encrypted credential storage, privacy scrubbing in logs, and proper secret handling. However, several **diagnostic logging points** were identified that could expose sensitive information in development/debug scenarios.

**Risk Level:** LOW to MEDIUM  
**Critical Issues:** 0  
**High Priority Issues:** 2  
**Medium Priority Issues:** 3  
**Low Priority Issues:** 4

---

## Findings Summary

### ✅ Strong Security Practices Identified

1. **Credential Encryption**
   - Vault uses AES-256-GCM with authenticated encryption
   - PBKDF2-HMAC-SHA256 with 600,000 iterations for key derivation
   - Windows DPAPI (CurrentUser scope) for remembered keys
   - File: `GoogleAccountVaultStore.cs`

2. **Privacy Scrubbing**
   - `PrivacyScrubber` sanitizes sensitive property names (Password, ApiKey, Token, TotpSecret)
   - Regex patterns redact credentials in strings
   - Applied automatically in `ObservabilityHub` before logging
   - Files: `PrivacyScrubber.cs`, `ObservabilityHub.cs`

3. **Error Message Sanitization**
   - Vault errors don't expose passwords or secrets
   - Generic messages used for cryptographic failures
   - Test coverage verifies no secret leakage in exceptions
   - File: `GoogleAccountVaultStoreTests.cs` (lines 36-53, 398-463)

4. **Secure Network Communication**
   - GitHub release checker validates HTTPS-only URLs
   - Allowed host whitelist for asset downloads
   - No personal data sent in update checks (only version in User-Agent)
   - Files: `GitHubReleaseClient.cs`

5. **Build & CI Security**
   - `.gitignore` excludes `secrets.json`, `.env*`, `*.key`, `*.pem`
   - GitHub workflows don't expose secrets
   - No credentials in test fixtures (synthetic data only)
   - Files: `.gitignore`, `.github/workflows/`

---

## 🔴 HIGH PRIORITY ISSUES

### H-1: Credential Email Exposed in Debug Console Output
**File:** `src/RouterPlus.App/Diagnostics/DebugAutoLoginRunner.cs`  
**Line:** 115

**Issue:**
```csharp
Console.WriteLine($"Credential found: email={credential.Email}");
```

**Risk:** Email addresses are PII and should not be logged to console in plaintext. If debug output is captured or shared (logs, screenshots, issue reports), this exposes user identity.

**Recommendation:**
```csharp
Console.WriteLine($"Credential found: email={PrivacyScrubber.ScrubString(credential.Email)}");
// Or redact entirely:
Console.WriteLine("Credential found and validated.");
```

**Impact:** Medium - Only affects debug builds/harness mode, but could be captured in bug reports

---

### H-2: DebugLogger Writes Sensitive Data to Disk
**File:** `src/RouterPlus.App/Diagnostics/DebugLogger.cs`  
**Lines:** 23-52

**Issue:**
```csharp
private static readonly string DebugLogPath = Path.Combine(
    AppContext.BaseDirectory,
    "app-debug.log");

WriteToFile($"  Message: {ex.Message}");
WriteToFile($"  StackTrace: {ex.StackTrace}");
```

**Risk:** Exception messages and stack traces may contain sensitive data (file paths with usernames, connection strings, API responses). Written to `app-debug.log` without scrubbing.

**Recommendation:**
1. Apply `PrivacyScrubber.ScrubString()` before writing exception messages
2. Add warning comment that file may contain sensitive data
3. Document that `app-debug.log` should be excluded from bug reports
4. Consider encrypting or sanitizing before persisting

**Impact:** Medium - Debug-only, but persistent on disk

---

## 🟡 MEDIUM PRIORITY ISSUES

### M-1: Chrome User Data Path Exposure in Diagnostics
**File:** `src/RouterPlus.App/Diagnostics/DebugAutoLoginRunner.cs`  
**Lines:** 8-9

**Issue:**
```csharp
Console.WriteLine($"UserData: {settings.ChromeUserDataDirectory}");
```

**Risk:** Full path includes Windows username (e.g., `C:\Users\[USERNAME]\AppData\...`). Exposed in debug console output.

**Recommendation:**
```csharp
var sanitizedPath = settings.ChromeUserDataDirectory
    .Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
             "%USERPROFILE%");
Console.WriteLine($"UserData: {sanitizedPath}");
```

**Impact:** Low-Medium - Windows username is minor PII, but should be scrubbed

---

### M-2: Exception Context in ObservabilityHub
**File:** `src/RouterPlus.App/ViewModels/CredentialsManagerViewModel.cs`  
**Lines:** 666-671

**Issue:**
```csharp
ObservabilityHub.Instance.LogEvent(LogLevel.Error, "CredentialsManager", 
    "VaultUnlockFailed", "Failed to unlock vault", 
    new { error = ex.Message, error_type = ex.GetType().Name });
```

**Risk:** While vault password isn't logged, exception messages could theoretically contain password-related info if exception handling changes. Defense in depth suggests additional scrubbing.

**Recommendation:**
Ensure all context objects passed to ObservabilityHub are explicitly scrubbed:
```csharp
var scrubbedContext = PrivacyScrubber.Scrub(new { 
    error = ex.Message, 
    error_type = ex.GetType().Name 
});
ObservabilityHub.Instance.LogEvent(LogLevel.Error, "CredentialsManager", 
    "VaultUnlockFailed", "Failed to unlock vault", scrubbedContext);
```

**Impact:** Low - Already has good practices, this is defense in depth

---

### M-3: API Key Visibility in UI State
**Files:** `src/RouterPlus.App/MainWindow.xaml.cs`, `src/RouterPlus.App/ViewModels/ProviderCardViewModel.cs`

**Issue:** API keys are stored in ViewModel properties (`ApiKeyValue`) and toggled between visible/hidden states. While encrypted at rest, they exist in memory as plaintext strings.

**Risk:** Memory dumps, debugging sessions, or UI automation tools could capture plaintext keys.

**Recommendation:**
1. Document that API keys are encrypted at rest but plaintext in memory during use
2. Clear API key strings from memory after use (consider zero-fill)
3. For high-security scenarios, consider `SecureString` (though .NET Core has limited support)

**Impact:** Low - Standard practice for UI applications, worth documenting

---

## 🟢 LOW PRIORITY ISSUES

### L-1: Test Constants Contain Synthetic Secrets
**File:** `tests/RouterPlus.Core.Tests/GoogleAccountVaultStoreTests.cs`  
**Lines:** 11-14

**Issue:**
```csharp
private const string VaultPassword = "vault-password";
private const string MarkerEmail = "integrity@example.test";
private const string MarkerPassword = "integrity-synthetic-password";
private const string MarkerTotp = "HXQWIIQFDUIJZA3Q";
```

**Risk:** None - these are clearly synthetic test values, not real credentials.

**Recommendation:** No action needed. Test data is appropriate and well-marked.

**Impact:** None - Informational only

---

### L-2: ObservabilityHub May Serialize Large Objects
**File:** `src/RouterPlus.Core/Observability/ObservabilityHub.cs`  
**Lines:** 58-59

**Issue:** Large context objects passed to logging could cause performance issues during scrubbing and serialization.

**Risk:** Performance concern, not security issue.

**Recommendation:** Document recommended context size limits in ObservabilityHub documentation.

**Impact:** Low - Performance/design consideration

---

### L-3: Multiple Console.WriteLine in DebugAutoLoginRunner
**File:** `src/RouterPlus.App/Diagnostics/DebugAutoLoginRunner.cs`  
**Multiple lines**

**Issue:** Various diagnostic outputs that may be captured in debug sessions:
- Profile names
- Chrome executable paths
- Profile directory names
- Credential status messages

**Risk:** Low - Diagnostic tool for developers, but output could be shared in bug reports.

**Recommendation:**
1. Add header warning that output contains PII
2. Scrub Windows usernames from paths
3. Provide "sanitized output" option for bug reports

**Impact:** Low - Development tool, but should follow privacy best practices

---

### L-4: GitHub Repository Hardcoded in Release Client
**File:** `src/RouterPlus.Infrastructure/Updates/GitHubReleaseClient.cs`  
**Line:** 9

**Issue:**
```csharp
private static readonly Uri ReleasesUri = 
    new("https://api.github.com/repos/hieuck/9router-plus/releases?per_page=100");
```

**Risk:** None - This is the official repository and must be hardcoded for security.

**Recommendation:** No action needed. This is correct implementation to prevent update hijacking.

**Impact:** None - Expected behavior

---

## Detailed Security Controls Verification

### ✅ Credential Storage
- ✅ Passwords encrypted with AES-256-GCM
- ✅ PBKDF2 with 600K iterations (OWASP recommended ≥600K for 2023+)
- ✅ Random salt per vault (16 bytes)
- ✅ Authenticated encryption (GCM mode prevents tampering)
- ✅ DPAPI for remembered keys (user-scoped, survives reboots)
- ✅ Vault integrity checks on open
- ✅ Atomic file writes (temp file + move)
- ✅ No plaintext secrets in vault JSON envelope
- ✅ Test coverage verifies error messages don't leak secrets

### ✅ Logging & Diagnostics
- ✅ PrivacyScrubber removes sensitive property names
- ✅ Regex patterns for password/token/key patterns in strings
- ✅ ObservabilityHub applies scrubbing automatically to context
- ⚠️ DebugLogger writes to file without scrubbing (H-2)
- ⚠️ Console.WriteLine in debug harness exposes email (H-1)
- ⚠️ File paths contain Windows usernames (M-1)

### ✅ Network Communication
- ✅ HTTPS-only for GitHub releases
- ✅ Host whitelist for asset downloads
- ✅ No personal data in update requests
- ✅ User-Agent contains only product name + version
- ✅ API keys sent only to configured localhost 9Router instance
- ✅ No credentials in query strings
- ✅ Redirect validation prevents MITM attacks

### ✅ Source Code Hygiene
- ✅ `.gitignore` excludes secrets, keys, env files
- ✅ No hardcoded real credentials found
- ✅ Test data uses example.com and synthetic values
- ✅ GitHub workflows don't expose secrets
- ✅ No credentials in configuration files
- ✅ No secrets in git history

### ✅ Error Handling
- ✅ Generic error messages for crypto failures
- ✅ No secret material in exception messages
- ✅ Test coverage for error message safety
- ✅ Safe error message helper in CredentialsManagerViewModel
- ⚠️ Exception context could be scrubbed more defensively (M-2)

---

## Files Audited

### Core Security Components (✅ Reviewed)
- `src/RouterPlus.Infrastructure/Security/GoogleAccountVaultStore.cs`
- `src/RouterPlus.Infrastructure/Security/DpapiSecretVault.cs`
- `src/RouterPlus.Core/Security/GoogleAccountVault.cs`
- `src/RouterPlus.Core/Security/GoogleLoginCredential.cs`
- `src/RouterPlus.Core/Observability/PrivacyScrubber.cs`
- `src/RouterPlus.Core/Observability/ObservabilityHub.cs`

### Application Layer (✅ Reviewed)
- `src/RouterPlus.App/ViewModels/CredentialsManagerViewModel.cs`
- `src/RouterPlus.App/ViewModels/MainViewModel.cs`
- `src/RouterPlus.App/MainWindow.xaml.cs`
- `src/RouterPlus.App/Diagnostics/DebugLogger.cs` ⚠️
- `src/RouterPlus.App/Diagnostics/DebugAutoLoginRunner.cs` ⚠️

### Network & Updates (✅ Reviewed)
- `src/RouterPlus.Infrastructure/Updates/GitHubReleaseClient.cs`
- `src/RouterPlus.Infrastructure/Router/RouterApiClient.cs`
- `src/RouterPlus.Infrastructure/Diagnostics/DiagnosticRedactor.cs`

### Tests (✅ Reviewed)
- `tests/RouterPlus.Core.Tests/GoogleAccountVaultStoreTests.cs`
- `tests/RouterPlus.Infrastructure.Tests/Security/DpapiSecretVaultTests.cs`
- `tests/RouterPlus.Infrastructure.Tests/Security/GoogleAccountVaultStoreTests.cs`
- `tests/RouterPlus.Infrastructure.Tests/DiagnosticRedactorTests.cs`
- `tests/RouterPlus.Core.Tests/GoogleTotpGeneratorTests.cs`

### Configuration & Build (✅ Reviewed)
- `.gitignore`
- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`
- `README.md`
- `SECURITY.md`

---

## Recommendations Summary

### Immediate Actions (High Priority)
1. **Apply PrivacyScrubber to DebugAutoLoginRunner console output**
   - Scrub or remove email logging at line 115
   - File: `src/RouterPlus.App/Diagnostics/DebugAutoLoginRunner.cs`
   
2. **Sanitize DebugLogger file output**
   - Apply `PrivacyScrubber.ScrubString()` before persisting exception details
   - File: `src/RouterPlus.App/Diagnostics/DebugLogger.cs`

### Short-term Actions (Medium Priority)
3. **Sanitize file paths in diagnostic output**
   - Replace usernames with `%USERPROFILE%` placeholder
   - File: `src/RouterPlus.App/Diagnostics/DebugAutoLoginRunner.cs`

4. **Review all ObservabilityHub.LogEvent calls**
   - Ensure context is explicitly scrubbed before logging
   - Files: All ViewModels that log error context

5. **Add documentation warnings**
   - Document that `app-debug.log` may contain PII
   - Add to SECURITY.md and developer documentation

### Long-term Improvements (Low Priority)
6. **Add privacy header to debug output**
   - Warn developers that output contains PII
   - Provide "sanitize for sharing" utility

7. **Consider SecureString for in-memory API keys**
   - Defense in depth for memory dump scenarios
   - Document current in-memory plaintext limitation

8. **Add context size limits to ObservabilityHub**
   - Prevent performance issues with large objects
   - Document recommended context size

---

## Compliance Notes

### GDPR / Privacy Considerations
- ✅ Email addresses stored locally only (no telemetry)
- ✅ No analytics or tracking to third parties
- ✅ Update checks don't transmit personal data
- ⚠️ Debug logs may contain PII (email, file paths with usernames)
- ⚠️ Should be documented in privacy policy and SECURITY.md

### Security Best Practices (OWASP Top 10 2021)
- ✅ **A02:2021 – Cryptographic Failures:** Strong encryption with authenticated modes
- ✅ **A03:2021 – Injection:** No SQL injection vectors identified, parameterized queries
- ✅ **A05:2021 – Security Misconfiguration:** Secure defaults, proper .gitignore
- ✅ **A07:2021 – Identification and Authentication Failures:** PBKDF2 with proper iterations
- ⚠️ **A09:2021 – Security Logging and Monitoring Failures:** Need to document debug logs may contain sensitive data

### Microsoft Security Development Lifecycle (SDL)
- ✅ Secure by Design: DPAPI usage, encrypted storage
- ✅ Secure by Default: No plaintext secrets at rest
- ⚠️ Secure in Deployment: Debug builds should not be distributed

---

## Conclusion

The 9router-plus project demonstrates **strong security fundamentals** with encrypted credential storage, privacy-aware logging infrastructure, and secure network communication. The identified issues are primarily in **diagnostic/development code paths** that are not exposed in production release builds.

### Primary Risks Identified
1. Debug console output exposing email addresses
2. Debug log files persisting unscrubbed exception data
3. File paths with Windows usernames in diagnostic output

### All Risks Are Mitigatable
Both high-priority issues can be mitigated quickly by applying existing `PrivacyScrubber` utilities to diagnostic output paths.

### Overall Security Assessment

**Security Grade: B+ (Good)**

**Strengths:**
- ✅ Excellent credential storage with AES-256-GCM
- ✅ Strong privacy scrubbing infrastructure in production paths
- ✅ Comprehensive test coverage for security controls
- ✅ Secure update mechanism with HTTPS and host validation
- ✅ No critical vulnerabilities identified

**Areas for Improvement:**
- ⚠️ Apply existing privacy controls to debug/diagnostic code
- ⚠️ Document PII exposure risk in debug builds
- ⚠️ Add warnings for developers about sensitive data in logs

**No Critical Vulnerabilities Found**

---

## Verification Testing Performed

### ✅ Completed Security Tests
1. ✅ **Vault Tampering** - Verified tampered ciphertexts rejected (test suite passes)
2. ✅ **Wrong Password** - Verified error messages don't leak secrets (test suite passes)
3. ✅ **Secret Scrubbing** - Verified PrivacyScrubber removes sensitive patterns
4. ✅ **Encryption Strength** - Verified AES-256-GCM with proper key derivation
5. ✅ **DPAPI Protection** - Verified remembered keys use CurrentUser scope

### Recommended Additional Testing
6. ⚠️ **Debug Log Inspection** - Manual review of `app-debug.log` contents
7. ⚠️ **Console Output Capture** - Review DebugAutoLoginRunner output for PII
8. ⚠️ **Memory Dump Analysis** - Check for in-memory key exposure (advanced)
9. ⚠️ **Vault File Forensics** - Verify deleted vaults cannot be recovered

---

## Appendix: Code Examples

### Example 1: Current Issue (H-1)
```csharp
// ❌ Current - Exposes email
Console.WriteLine($"Credential found: email={credential.Email}");
```

### Example 1: Recommended Fix
```csharp
// ✅ Fixed - Email scrubbed
var scrubbedEmail = credential.Email.Length > 0 
    ? $"{credential.Email[0]}***@{credential.Email.Split('@').Last()}"
    : "[redacted]";
Console.WriteLine($"Credential found: email={scrubbedEmail}");

// Or simply:
Console.WriteLine("Credential found and validated.");
```

### Example 2: Current Issue (M-1)
```csharp
// ❌ Current - Exposes username
Console.WriteLine($"UserData: {settings.ChromeUserDataDirectory}");
// Output: UserData: C:\Users\JohnDoe\AppData\Local\Google\Chrome\User Data
```

### Example 2: Recommended Fix
```csharp
// ✅ Fixed - Username redacted
var sanitizedPath = settings.ChromeUserDataDirectory
    .Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
             "%USERPROFILE%");
Console.WriteLine($"UserData: {sanitizedPath}");
// Output: UserData: %USERPROFILE%\AppData\Local\Google\Chrome\User Data
```

---

**Report Version:** 1.0  
**Report Date:** 2026-09-06  
**Next Review:** Recommended after implementing fixes or before next major release  
**Auditor Contact:** Security review team

**Related Documents:**
- `SECURITY.md` - Security policy and vulnerability reporting
- `SECURITY-AUDIT-REPORT.md` - Previous audit (2026-08-28) - credential exposure check
- `docs/privacy.md` - Privacy policy and data handling
