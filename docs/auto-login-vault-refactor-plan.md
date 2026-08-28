# Auto-Login & Vault Architecture Refactor Plan

**Created:** 2026-08-28  
**Updated:** 2026-08-28  
**User Request:** "rất tốt, viết chi tiết refactor plan vào file docs luôn"  
**Status:** Ready to Implement - All decisions finalized  
**Estimate:** 22-31 hours (full foundation)

---

## Context

**Affected Components:**
- `MainViewModel` - Uses GoogleLoginVaultStore (rename to GoogleAccountVaultStore), will use ProviderConnectionVaultStore
- `GoogleAutoLoginDialog.xaml.cs` - Uses GoogleLoginVaultStore
- `AwsBuilderIdOAuthAutomation.cs` - Will inherit from GoogleOAuthFlowAutomation base
- `CodexOAuthAutomation.cs` - Will inherit from GoogleOAuthFlowAutomation base

**New Components:**
- `GoogleOAuthFlowAutomation` - Base class for all Google OAuth automations
- `ProviderConnectionVaultStore` - Maps profile → provider → auth config
- `AutoLoginOrchestrator` - Unified login with fallback support

**Data Schemas:**
- `GoogleCredential`: { Email, Password, TotpSecret }
- `ProviderConnection`: { ProfileName, Provider, PreferredMethod, LinkedGoogleAccount, DirectCredential }
- `ProviderCredential`: { Email, Password, TotpSecret }
- `AuthMethod` enum: GoogleOAuth | Direct

---

### Current Situation

**Existing auto-login implementations:**
1. `GoogleLoginCdpBrowser.cs` - Full Google login (email/password/TOTP)
2. `CodexOAuthAutomation.cs` - Codex with Google OAuth
3. `AwsBuilderIdOAuthAutomation.cs` - Kiro/AWS Builder ID with Google OAuth

**Problems:**
- **Code duplication** - Google OAuth logic repeated in 3 places
- **Limited scope** - Only supports Google OAuth, no direct login
- **Vault naming** - `GoogleLoginVaultStore` used by non-Google providers
- **No fallback** - Can't fallback from Google OAuth to direct login

### Future Providers

All providers support **2 authentication methods:**

| Provider | Method 1: Direct | Method 2: Google OAuth |
|----------|-------------------|------------------------|
| Codex | ✅ Email/password/TOTP | ✅ Link with Google |
| Kiro/AWS | ✅ Email/password/TOTP | ✅ Link with Google (current) |
| GitHub | ✅ Username/password/TOTP | ✅ Link with Google |
| OpenRouter | ✅ Email/password/TOTP | ✅ Link with Google |
| Claude (future) | ✅ Email/password/TOTP | ✅ Link with Google |
| Gemini (future) | ✅ Email/password/TOTP | ✅ Link with Google |

→ **Need architecture that supports both methods + fallback**

---

## Goals

1. **Eliminate code duplication** - Google OAuth automation shared across providers
2. **Support multiple auth methods** - Google OAuth + Direct login per provider
3. **Flexible credential storage** - Map profile → provider → auth config
4. **Enable batch auto-login** - Sequential login with fallback support
5. **Clear vault structure** - Separate Google accounts from provider connections
6. **Future-proof** - Easy to add new providers

---

## New Architecture

### Vault Structure

**Two vault files:**

#### 1. Google Account Vault
```
File: google-accounts.vault (DPAPI encrypted)
Purpose: Store Google account credentials
Format: Dictionary<email, GoogleCredential>
```

```csharp
public class GoogleCredential
{
    public string Email { get; init; }
    public string Password { get; init; }
    public string? TotpSecret { get; init; }
}

// Example data:
{
  "demo.user1@example.com": {
    "email": "demo.user1@example.com",
    "password": "encrypted_password",
    "totpSecret": "encrypted_totp_secret"
  },
  "demo.user2@example.com": {
    "email": "demo.user2@example.com",
    "password": "encrypted_password",
    "totpSecret": null
  }
}
```

#### 2. Provider Connection Vault
```
File: provider-connections.vault (DPAPI encrypted)
Purpose: Map Chrome profile → provider → auth configuration
Format: Dictionary<profileName, Dictionary<providerKind, ProviderConnection>>
```

```csharp
public class ProviderConnection
{
    public string ProfileName { get; init; }
    public ProviderKind Provider { get; init; }
    
    // Preferred authentication method
    public AuthMethod PreferredMethod { get; init; }
    
    // Google OAuth credentials (if using Google)
    public string? LinkedGoogleAccount { get; init; }
    
    // Direct credentials (if using direct login)
    public ProviderCredential? DirectCredential { get; init; }
}

public class ProviderCredential
{
    public string Email { get; init; }
    public string Password { get; init; }
    public string? TotpSecret { get; init; }
}

public enum AuthMethod
{
    GoogleOAuth,  // "Continue with Google"
    Direct        // Provider's own email/password/TOTP
}

// Example data:
{
  "demo.user1@example.com": { // Chrome profile name
    "Codex": {
      "profileName": "demo.user1@example.com",
      "provider": "Codex",
      "preferredMethod": "GoogleOAuth",
      "linkedGoogleAccount": "demo.user1@example.com",
      "directCredential": null
    },
    "Kiro": {
      "profileName": "demo.user1@example.com",
      "provider": "Kiro",
      "preferredMethod": "GoogleOAuth",
      "linkedGoogleAccount": "demo.user1@example.com",
      "directCredential": null
    },
    "GitHub": {
      "profileName": "demo.user1@example.com",
      "provider": "GitHub",
      "preferredMethod": "Direct",
      "linkedGoogleAccount": null,
      "directCredential": {
        "email": "loandev@github.com",
        "password": "encrypted_password",
        "totpSecret": "encrypted_totp_secret"
      }
    },
    "OpenRouter": {
      "profileName": "demo.user1@example.com",
      "provider": "OpenRouter",
      "preferredMethod": "GoogleOAuth",
      "linkedGoogleAccount": "demo.user1@example.com",
      "directCredential": {
        // Fallback if Google OAuth fails
        "email": "loan@openrouter.com",
        "password": "encrypted_password",
        "totpSecret": "encrypted_totp_secret"
      }
    }
  },
  
  "work-profile": { // Another Chrome profile
    "Codex": {
      "profileName": "work-profile",
      "provider": "Codex",
      "preferredMethod": "Direct",
      "linkedGoogleAccount": null,
      "directCredential": {
        "email": "work@company.com",
        "password": "encrypted_password",
        "totpSecret": "encrypted_totp_secret"
      }
    }
  }
}
```

---

## Automation Classes

### Base Classes

#### GoogleOAuthFlowAutomation (Base)
```csharp
public abstract class GoogleOAuthFlowAutomation : IAsyncDisposable
{
    protected readonly CdpSession _session;
    protected readonly string _googleEmail;
    protected readonly string _googlePassword;
    protected readonly ITotpGenerator? _totpGenerator;
    
    protected GoogleOAuthFlowAutomation(
        string webSocketUrl,
        string googleEmail,
        string googlePassword,
        ITotpGenerator? totpGenerator)
    {
        _session = new CdpSession(webSocketUrl);
        _googleEmail = googleEmail;
        _googlePassword = googlePassword;
        _totpGenerator = totpGenerator;
    }
    
    // Main flow
    public async Task RunAsync(CancellationToken ct)
    {
        await _session.ConnectAsync(ct);
        
        // Shared Google OAuth steps
        await WaitForPageLoadAsync(ct);
        await TryClickContinueWithGoogleAsync(ct);
        await TryClickAccountAsync(ct);
        await TryFillTotpAsync(ct);
        await TryClickGoogleConsentAsync(ct);
        
        // Provider-specific consent/navigation
        await OnAfterGoogleConsentAsync(ct);
        
        // Wait for completion
        await WaitForCompletionAsync(ct);
    }
    
    // Shared methods
    protected async Task<bool> TryClickContinueWithGoogleAsync(CancellationToken ct)
    {
        // Shared logic for "Continue with Google" button
    }
    
    protected async Task<bool> TryClickAccountAsync(CancellationToken ct)
    {
        // Shared account picker logic
        // Skip AWS SSO identity, find matching email
    }
    
    protected async Task<bool> TryFillTotpAsync(CancellationToken ct)
    {
        // Shared TOTP auto-fill logic
    }
    
    protected async Task<bool> TryClickGoogleConsentAsync(CancellationToken ct)
    {
        // Shared Google consent button logic
    }
    
    // Provider-specific hooks (must override)
    protected abstract Task OnAfterGoogleConsentAsync(CancellationToken ct);
    protected abstract Task WaitForCompletionAsync(CancellationToken ct);
    
    public async ValueTask DisposeAsync()
    {
        await _session.DisconnectAsync();
    }
}
```

#### Provider Implementations

```csharp
// Kiro/AWS Builder ID
public class AwsBuilderIdOAuthAutomation : GoogleOAuthFlowAutomation
{
    protected override async Task OnAfterGoogleConsentAsync(CancellationToken ct)
    {
        // Click AWS Builder ID consent
        await TryClickAwsConsentButtonAsync(ct);
    }
    
    protected override async Task WaitForCompletionAsync(CancellationToken ct)
    {
        // Wait for completion page or redirect
        await WaitForUrlAsync("view.awsapps.com/start/#/complete", ct);
    }
}

// Codex
public class CodexOAuthAutomation : GoogleOAuthFlowAutomation
{
    protected override async Task OnAfterGoogleConsentAsync(CancellationToken ct)
    {
        // Navigate to chatgpt.com
        await NavigateToAsync("https://chatgpt.com/", ct);
    }
    
    protected override async Task WaitForCompletionAsync(CancellationToken ct)
    {
        // Wait for successful navigation
        await WaitForUrlAsync("chatgpt.com", ct);
    }
}

// GitHub
public class GitHubOAuthAutomation : GoogleOAuthFlowAutomation
{
    protected override async Task OnAfterGoogleConsentAsync(CancellationToken ct)
    {
        // GitHub may have additional consent screen
        await TryClickGitHubConsentAsync(ct);
    }
    
    protected override async Task WaitForCompletionAsync(CancellationToken ct)
    {
        await WaitForUrlAsync("github.com", ct);
    }
}

// OpenRouter
public class OpenRouterOAuthAutomation : GoogleOAuthFlowAutomation
{
    protected override async Task OnAfterGoogleConsentAsync(CancellationToken ct)
    {
        // OpenRouter-specific consent if any
    }
    
    protected override async Task WaitForCompletionAsync(CancellationToken ct)
    {
        await WaitForUrlAsync("openrouter.ai", ct);
    }
}
```

### Direct Login Automation

```csharp
public abstract class DirectLoginAutomation : IAsyncDisposable
{
    protected readonly CdpSession _session;
    protected readonly string _email;
    protected readonly string _password;
    protected readonly ITotpGenerator? _totpGenerator;
    
    public async Task RunAsync(CancellationToken ct)
    {
        await _session.ConnectAsync(ct);
        
        // Provider-specific login flow
        await FillEmailAsync(ct);
        await FillPasswordAsync(ct);
        await FillTotpIfNeededAsync(ct);
        await SubmitLoginAsync(ct);
        await WaitForCompletionAsync(ct);
    }
    
    protected abstract Task FillEmailAsync(CancellationToken ct);
    protected abstract Task FillPasswordAsync(CancellationToken ct);
    protected abstract Task FillTotpIfNeededAsync(CancellationToken ct);
    protected abstract Task SubmitLoginAsync(CancellationToken ct);
    protected abstract Task WaitForCompletionAsync(CancellationToken ct);
}

// Example: GitHub direct login
public class GitHubDirectLoginAutomation : DirectLoginAutomation
{
    protected override async Task FillEmailAsync(CancellationToken ct)
    {
        await FillInputAsync("input[name='login']", _email, ct);
    }
    
    protected override async Task FillPasswordAsync(CancellationToken ct)
    {
        await FillInputAsync("input[name='password']", _password, ct);
    }
    
    protected override async Task FillTotpIfNeededAsync(CancellationToken ct)
    {
        if (_totpGenerator != null)
        {
            var code = _totpGenerator.Generate();
            await FillInputAsync("input[name='otp']", code, ct);
        }
    }
    
    protected override async Task SubmitLoginAsync(CancellationToken ct)
    {
        await ClickAsync("input[type='submit']", ct);
    }
    
    protected override async Task WaitForCompletionAsync(CancellationToken ct)
    {
        await WaitForUrlAsync("github.com", ct);
    }
}
```

---

## Auto-Login Orchestrator

```csharp
public class AutoLoginOrchestrator
{
    private readonly GoogleAccountVaultStore _googleVault;
    private readonly ProviderConnectionVaultStore _connectionVault;
    private readonly IChromeLauncher _chromeLauncher;
    private readonly ILogger<AutoLoginOrchestrator> _logger;
    
    public async Task<AutoLoginResult> LoginAsync(
        ChromeProfile profile,
        ProviderKind provider,
        CancellationToken ct)
    {
        // Get connection config
        var connection = await _connectionVault.GetConnectionAsync(
            profile.Name,
            provider,
            ct);
        
        if (connection == null)
        {
            return AutoLoginResult.NoCredentials();
        }
        
        // Try preferred method
        try
        {
            if (connection.PreferredMethod == AuthMethod.GoogleOAuth)
            {
                return await LoginViaGoogleOAuthAsync(profile, provider, connection, ct);
            }
            else
            {
                return await LoginDirectAsync(profile, provider, connection, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary login method failed for {Provider}", provider);
            
            // Try fallback if available
            if (connection.PreferredMethod == AuthMethod.GoogleOAuth &&
                connection.DirectCredential != null)
            {
                _logger.LogInformation("Attempting fallback to direct login");
                return await LoginDirectAsync(profile, provider, connection, ct);
            }
            else if (connection.PreferredMethod == AuthMethod.Direct &&
                     connection.LinkedGoogleAccount != null)
            {
                _logger.LogInformation("Attempting fallback to Google OAuth");
                return await LoginViaGoogleOAuthAsync(profile, provider, connection, ct);
            }
            
            return AutoLoginResult.Failed(ex.Message);
        }
    }
    
    private async Task<AutoLoginResult> LoginViaGoogleOAuthAsync(
        ChromeProfile profile,
        ProviderKind provider,
        ProviderConnection connection,
        CancellationToken ct)
    {
        // Get Google account credentials
        var googleAccount = await _googleVault.GetAsync(
            connection.LinkedGoogleAccount!,
            ct);
        
        if (googleAccount == null)
        {
            throw new InvalidOperationException(
                $"Google account '{connection.LinkedGoogleAccount}' not found in vault");
        }
        
        // Launch browser
        var loginUrl = GetProviderLoginUrl(provider);
        var session = await _chromeLauncher.LaunchManagedAsync(
            profile,
            loginUrl,
            ct,
            useOriginalProfile: true);
        
        try
        {
            // Create TOTP generator
            ITotpGenerator? totpGenerator = null;
            if (!string.IsNullOrEmpty(googleAccount.TotpSecret))
            {
                totpGenerator = new GoogleTotpGenerator(googleAccount.TotpSecret);
            }
            
            // Create automation
            await using var automation = CreateGoogleOAuthAutomation(
                provider,
                session.WebSocketUrl!,
                googleAccount.Email,
                googleAccount.Password,
                totpGenerator);
            
            // Run automation
            await automation.RunAsync(ct);
            
            return AutoLoginResult.Success(AuthMethod.GoogleOAuth);
        }
        finally
        {
            await session.CleanupAsync();
        }
    }
    
    private async Task<AutoLoginResult> LoginDirectAsync(
        ChromeProfile profile,
        ProviderKind provider,
        ProviderConnection connection,
        CancellationToken ct)
    {
        var creds = connection.DirectCredential!;
        
        // Launch browser
        var loginUrl = GetProviderLoginUrl(provider);
        var session = await _chromeLauncher.LaunchManagedAsync(
            profile,
            loginUrl,
            ct,
            useOriginalProfile: true);
        
        try
        {
            // Create TOTP generator
            ITotpGenerator? totpGenerator = null;
            if (!string.IsNullOrEmpty(creds.TotpSecret))
            {
                totpGenerator = new GoogleTotpGenerator(creds.TotpSecret);
            }
            
            // Create automation
            await using var automation = CreateDirectLoginAutomation(
                provider,
                session.WebSocketUrl!,
                creds.Email,
                creds.Password,
                totpGenerator);
            
            // Run automation
            await automation.RunAsync(ct);
            
            return AutoLoginResult.Success(AuthMethod.Direct);
        }
        finally
        {
            await session.CleanupAsync();
        }
    }
    
    private GoogleOAuthFlowAutomation CreateGoogleOAuthAutomation(
        ProviderKind provider,
        string webSocketUrl,
        string googleEmail,
        string googlePassword,
        ITotpGenerator? totpGenerator)
    {
        return provider switch
        {
            ProviderKind.Codex => new CodexOAuthAutomation(
                webSocketUrl, googleEmail, googlePassword, totpGenerator),
            ProviderKind.Kiro => new AwsBuilderIdOAuthAutomation(
                webSocketUrl, googleEmail, googlePassword, totpGenerator),
            ProviderKind.GitHub => new GitHubOAuthAutomation(
                webSocketUrl, googleEmail, googlePassword, totpGenerator),
            ProviderKind.OpenRouter => new OpenRouterOAuthAutomation(
                webSocketUrl, googleEmail, googlePassword, totpGenerator),
            _ => throw new NotSupportedException($"Provider {provider} not supported")
        };
    }
    
    private DirectLoginAutomation CreateDirectLoginAutomation(
        ProviderKind provider,
        string webSocketUrl,
        string email,
        string password,
        ITotpGenerator? totpGenerator)
    {
        return provider switch
        {
            ProviderKind.GitHub => new GitHubDirectLoginAutomation(
                webSocketUrl, email, password, totpGenerator),
            ProviderKind.OpenRouter => new OpenRouterDirectLoginAutomation(
                webSocketUrl, email, password, totpGenerator),
            // Add more as implemented
            _ => throw new NotSupportedException(
                $"Direct login for {provider} not implemented yet")
        };
    }
}

public class AutoLoginResult
{
    public bool Success { get; init; }
    public AuthMethod? UsedMethod { get; init; }
    public string? ErrorMessage { get; init; }
    
    public static AutoLoginResult Success(AuthMethod method) =>
        new() { Success = true, UsedMethod = method };
    
    public static AutoLoginResult Failed(string error) =>
        new() { Success = false, ErrorMessage = error };
    
    public static AutoLoginResult NoCredentials() =>
        new() { Success = false, ErrorMessage = "No credentials configured" };
}
```

---

## Implementation Phases

### Phase 1: Vault Architecture (3-4 hours)

**Goal:** Create new vault structure, separate Google accounts from provider connections

#### Step 1.1: Create Models (1h)
**New files:**
- `src/RouterPlus.Core/Models/AuthMethod.cs`
- `src/RouterPlus.Core/Models/ProviderCredential.cs`
- `src/RouterPlus.Core/Models/ProviderConnection.cs`
- `src/RouterPlus.Core/Models/GoogleCredential.cs` (move from existing)

```csharp
// AuthMethod.cs
public enum AuthMethod
{
    GoogleOAuth,
    Direct
}

// ProviderCredential.cs
public class ProviderCredential
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? TotpSecret { get; init; }
}

// ProviderConnection.cs
public class ProviderConnection
{
    public string ProfileName { get; init; } = string.Empty;
    public ProviderKind Provider { get; init; }
    public AuthMethod PreferredMethod { get; init; }
    public string? LinkedGoogleAccount { get; init; }
    public ProviderCredential? DirectCredential { get; init; }
}
```

#### Step 1.2: Create Vault Stores (2h)
**Rename existing:**
- `GoogleLoginVaultStore.cs` → `GoogleAccountVaultStore.cs`
- Update file path: `google-login.vault` → `google-accounts.vault`

**New file:**
- `src/RouterPlus.Infrastructure/Vault/ProviderConnectionVaultStore.cs`

```csharp
public class ProviderConnectionVaultStore
{
    private readonly string _vaultPath;
    private readonly ILogger<ProviderConnectionVaultStore> _logger;
    
    // In-memory cache
    private Dictionary<string, Dictionary<ProviderKind, ProviderConnection>>? _connections;
    
    public async Task<ProviderConnection?> GetConnectionAsync(
        string profileName,
        ProviderKind provider,
        CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        
        if (_connections!.TryGetValue(profileName, out var profileConnections) &&
            profileConnections.TryGetValue(provider, out var connection))
        {
            return connection;
        }
        
        return null;
    }
    
    public async Task SaveConnectionAsync(
        ProviderConnection connection,
        CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        
        if (!_connections!.ContainsKey(connection.ProfileName))
        {
            _connections[connection.ProfileName] = new();
        }
        
        _connections[connection.ProfileName][connection.Provider] = connection;
        
        await SaveAsync(ct);
    }
    
    public async Task<bool> HasCredentialsAsync(
        string profileName,
        ProviderKind provider,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(profileName, provider, ct);
        
        if (connection == null)
            return false;
        
        // Check if has either Google OAuth or direct credentials
        return !string.IsNullOrEmpty(connection.LinkedGoogleAccount) ||
               connection.DirectCredential != null;
    }
    
    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_connections != null)
            return;
        
        await LoadAsync(ct);
    }
    
    private async Task LoadAsync(CancellationToken ct)
    {
        // Load from DPAPI-encrypted file
        // Similar to GoogleAccountVaultStore implementation
    }
    
    private async Task SaveAsync(CancellationToken ct)
    {
        // Save to DPAPI-encrypted file
        // Similar to GoogleAccountVaultStore implementation
    }
}
```

#### Step 1.3: Update Callers (1h)
**Files to update:**
- `MainViewModel.cs` - Use new vault stores
- `GoogleAutoLoginDialog.xaml.cs` - Update to use GoogleAccountVaultStore

**Test:**
- Vault files created correctly
- DPAPI encryption/decryption works
- New vaults integrate with existing UI

---

### Phase 2: Google OAuth Consolidation (3-4 hours)

**Goal:** Extract shared Google OAuth logic into base class

#### Step 2.1: Create Base Class (2h)
**New file:**
- `src/RouterPlus.Infrastructure/Chrome/GoogleOAuthFlowAutomation.cs`

Extract shared logic from:
- `AwsBuilderIdOAuthAutomation.cs`
- `CodexOAuthAutomation.cs`

Methods to extract:
- `TryClickContinueWithGoogleAsync()`
- `TryClickAccountAsync()`
- `TryFillTotpAsync()`
- `TryClickGoogleConsentAsync()`

#### Step 2.2: Refactor Existing Automations (1h)
**Modify:**
- `AwsBuilderIdOAuthAutomation.cs` - Inherit from base, implement hooks
- `CodexOAuthAutomation.cs` - Inherit from base, implement hooks

#### Step 2.3: Create New Provider Automations (1h)
**New files:**
- `GitHubOAuthAutomation.cs`
- `OpenRouterOAuthAutomation.cs`

**Test:**
- Existing Codex and Kiro automation still works
- New providers can be tested manually (if accounts available)

---

### Phase 3: Direct Login Automation (4-6 hours per provider)

**Goal:** Implement provider-specific direct login automation

#### Step 3.1: Create Base Class (2h)
**New file:**
- `src/RouterPlus.Infrastructure/Chrome/DirectLoginAutomation.cs`

#### Step 3.2: Implement for One Provider (4-6h)
Pick one provider to start (recommend GitHub):

**New file:**
- `GitHubDirectLoginAutomation.cs`

**Implementation steps:**
1. Research GitHub login flow (selectors, steps)
2. Implement fill email/password/TOTP
3. Handle error cases (wrong password, TOTP required)
4. Test with real account

**Test:**
- Successful login
- Wrong password handling
- TOTP required/optional
- Account 2FA variations

#### Step 3.3: Repeat for Other Providers (incremental)
- OpenRouter
- Codex
- Kiro/AWS Builder ID

---

### Phase 4: Auto-Login Orchestrator (3-4 hours)

**Goal:** Unified orchestrator with fallback support

#### Step 4.1: Create Orchestrator (2h)
**New file:**
- `src/RouterPlus.Infrastructure/Services/AutoLoginOrchestrator.cs`

Implements:
- Get connection config from vault
- Try preferred method
- Fallback to alternative if available
- Return structured result

#### Step 4.2: Integrate with UI (1-2h)
**Modify:**
- `MainViewModel.cs` - Use orchestrator instead of direct automation calls
- Update device code workflow
- Update batch login (when implemented)

**Test:**
- Google OAuth login works
- Direct login works (if implemented)
- Fallback triggers correctly
- Error handling

---

### Phase 5: UI Updates (3-4 hours)

**Goal:** Update UI to support new vault structure

#### Step 5.1: Vault Indicator per Provider (1h)
**Modify:**
- `ProfileRowViewModel.cs` - Add per-provider credential indicators
- `MainWindow.xaml` - Show icons for each provider with credentials

Display:
```
Profile Name              G C K O H
└─ Has credentials for: Google, Codex, Kiro, OpenRouter, GitHub
```

#### Step 5.2: Credential Management UI (2-3h)
**Options:**

**Option A: Per-provider dialogs** (simpler, less change)
- Keep existing context menu structure
- "Google Auto Login" opens Google vault dialog
- "Codex Auto Login" opens Codex credential dialog
- Each dialog manages both Google OAuth + Direct for that provider

**Option B: Unified Credentials Manager** (better UX, more work)
- New toolbar button: "🔐 Credentials"
- Opens centralized dialog showing all providers
- Tab per provider with both auth methods
- Preferred method selection

Recommend: **Start with Option A**, migrate to Option B later

**Test:**
- Can add/edit/remove Google accounts
- Can configure provider connections
- Can select preferred auth method
- Indicators update correctly

---

### Phase 6: Batch Auto-Login Integration (2-3 hours)

**Goal:** Integrate orchestrator with batch login

**Modify:**
- `MainViewModel.cs` - Batch login logic
- Use `AutoLoginOrchestrator` instead of direct automation

**Changes:**
```csharp
private async Task RunSingleAutoLoginAsync(
    ChromeProfile profile,
    ProviderKind provider,
    BatchLoginProgressRow progressRow,
    CancellationToken ct)
{
    // Use orchestrator
    var result = await _autoLoginOrchestrator.LoginAsync(
        profile,
        provider,
        ct);
    
    if (result.Success)
    {
        progressRow.State = BatchLoginState.Success;
        progressRow.StatusMessage = result.UsedMethod == AuthMethod.GoogleOAuth
            ? "Login successful via Google OAuth"
            : "Login successful via direct login";
    }
    else
    {
        progressRow.State = result.ErrorMessage == "No credentials configured"
            ? BatchLoginState.Skipped
            : BatchLoginState.Failed;
        progressRow.StatusMessage = result.ErrorMessage!;
    }
}
```

**Test:**
- Batch with Google OAuth profiles
- Batch with Direct profiles
- Batch with mixed auth methods
- Fallback works in batch context

---

## Finalized Decisions

### 1. Vault UI Location
**Decision:** ✅ **Dedicated "Credentials Manager" toolbar button**

- New toolbar button: "🔐 Credentials"
- Opens centralized dialog showing all providers
- Tab or section per provider with both auth methods
- Preferred method selection per provider

**Rationale:** Centralized management easier for multiple providers, better UX for batch operations

---

### 2. Credentials Scope
**Decision:** ✅ **Each Chrome profile has independent credentials**

- Profile A can use Google account X for Codex
- Profile B can use Google account Y for Codex
- Profile C can use direct Codex credentials (no Google)
- Same Google account can be used by multiple profiles

**Rationale:** Maximum flexibility, supports different personas/work contexts

---

### 3. Preferred Method Selection
**Decision:** ✅ **Auto-detect based on available credentials**

**Logic:**
```
if (hasLinkedGoogleAccount && googleAccountHasCredentials)
    use GoogleOAuth
else if (hasDirectCredentials)
    use Direct
else
    show "No credentials configured"
```

**Fallback:**
- If preferred method fails AND alternative exists → try fallback
- If both fail → show error to user

**Rationale:** Simple UX, no additional configuration needed, intelligent fallback

---

### 4. Implementation Priority
**Decision:** ✅ **Full Foundation (22-31 hours)**

```
Phase 1: Vault Architecture (no migration)    3-4h
Phase 2: Google OAuth Consolidation          3-4h
Phase 3: Direct Login (1-2 providers)        8-12h
Phase 4: Orchestrator                         3-4h
Phase 5: UI Updates                           3-4h
Phase 6: Batch Integration                    2-3h
─────────────────────────────────────────────────
Total:                                       22-31h
```

**Rationale:** 
- Build complete solution once, no need to refactor later
- Support both Google OAuth and Direct login from start
- Proper foundation for batch auto-login feature

---

### 5. Migration Strategy
**Decision:** ✅ **No migration needed - vault is currently empty**

**Implementation:**
- Start fresh with new vault architecture
- No backward compatibility needed
- Simpler implementation (skip Phase 1.3)

**Rationale:** No existing data to migrate, clean start with proper architecture

---

## File Structure

```
src/RouterPlus.Core/
├─ Models/
│  ├─ AuthMethod.cs (NEW)
│  ├─ GoogleCredential.cs (MOVED)
│  ├─ ProviderCredential.cs (NEW)
│  └─ ProviderConnection.cs (NEW)

src/RouterPlus.Infrastructure/
├─ Chrome/
│  ├─ GoogleOAuthFlowAutomation.cs (NEW - base class)
│  ├─ DirectLoginAutomation.cs (NEW - base class)
│  ├─ AwsBuilderIdOAuthAutomation.cs (MODIFIED - inherit from base)
│  ├─ CodexOAuthAutomation.cs (MODIFIED - inherit from base)
│  ├─ GitHubOAuthAutomation.cs (NEW)
│  ├─ OpenRouterOAuthAutomation.cs (NEW)
│  ├─ GitHubDirectLoginAutomation.cs (NEW)
│  ├─ OpenRouterDirectLoginAutomation.cs (NEW)
│  └─ GoogleLoginCdpBrowser.cs (KEEP - for full Google login)
│
├─ Vault/
│  ├─ GoogleAccountVaultStore.cs (RENAMED from GoogleLoginVaultStore)
│  ├─ ProviderConnectionVaultStore.cs (NEW)
│  └─ VaultMigration.cs (NEW)
│
└─ Services/
   └─ AutoLoginOrchestrator.cs (NEW)

src/RouterPlus.App/
├─ ViewModels/
│  ├─ MainViewModel.cs (MODIFIED - use new vaults + orchestrator)
│  └─ ProfileRowViewModel.cs (MODIFIED - per-provider indicators)
│
└─ Views/
   ├─ GoogleAutoLoginDialog.xaml (MODIFIED - use GoogleAccountVaultStore)
   └─ ProviderCredentialsDialog.xaml (NEW - optional, for Option B UI)
```

---

## Testing Strategy

### Unit Tests
- [ ] `GoogleAccountVaultStore` - Save/load/encryption
- [ ] `ProviderConnectionVaultStore` - CRUD operations
- [ ] `AutoLoginOrchestrator` - Method selection + fallback logic

### Integration Tests
- [ ] Google OAuth automation (each provider)
- [ ] Direct login automation (each provider)
- [ ] Orchestrator with real vaults
- [ ] Fallback scenarios
- [ ] Batch login with mixed auth methods

### Manual Tests
- [ ] Add Google account via UI
- [ ] Configure provider connection (Google OAuth)
- [ ] Configure provider connection (Direct)
- [ ] Single auto-login via context menu
- [ ] Batch auto-login (Google OAuth profiles)
- [ ] Batch auto-login (Direct profiles)
- [ ] Batch auto-login (mixed)
- [ ] Fallback when Google OAuth fails

---

## Risks & Mitigations

### Risk 1: Provider Login Flow Changes
**Risk:** Provider changes login UI/selectors, automation breaks

**Mitigation:**
- Abstract selectors to configuration
- Add logging for each automation step
- Graceful fallback to manual login
- Version detection for known flows

### Risk 2: DPAPI Encryption Issues
**Risk:** Vault cannot be decrypted on different machine

**Mitigation:**
- DPAPI is machine+user specific (expected)
- Document export/import workflow
- Consider optional password-based encryption for portability

### Risk 3: Credential Sync Complexity
**Risk:** User changes password, must update in multiple places

**Mitigation:**
- Google accounts centralized in one vault
- Clear UI showing which providers use which Google account
- Bulk update feature?

### Risk 4: Fallback Loop
**Risk:** Both methods fail repeatedly, causing infinite retry

**Mitigation:**
- Limit retry attempts (max 1 fallback per login attempt)
- Log failures to prevent repeated attempts
- Show clear error to user after fallback fails

---

## Success Metrics

- [ ] Code duplication eliminated (3 files → 1 base + N providers)
- [ ] New provider can be added in 4-6 hours
- [ ] Batch auto-login success rate >90% (with valid credentials)
- [ ] Fallback works when primary method fails
- [ ] Vault structure supports future providers without refactor
- [ ] No breaking changes to existing working features

---

## Timeline Summary

### Full Foundation Approach (22-31 hours)
```
Week 1:
  Phase 1: Vault Architecture (no migration)    3-4h
  Phase 2: Google OAuth Consolidation          3-4h
  Phase 3: Direct Login (GitHub)               4-6h

Week 2:
  Phase 4: Orchestrator                         3-4h
  Phase 5: UI Updates                           3-4h

Week 3:
  Phase 6: Batch Integration                    2-3h
  Phase 3: Direct Login (other providers)      4-6h per provider

Total: 22-31h over 3 weeks
```

---

## Next Steps

1. ✅ **All decisions finalized** (see Finalized Decisions section above)
2. **Ready to implement Phase 1** (Vault Architecture - 3-4h)
3. **Create GitHub issues/tasks** for each phase (optional)
4. **Begin implementation** with models and vault stores

---

## References

- Related: `batch-auto-login-plan.md` - Batch feature details
- Existing code: `GoogleLoginCdpBrowser.cs`, `AwsBuilderIdOAuthAutomation.cs`, `CodexOAuthAutomation.cs`
- Vault: `GoogleLoginVaultStore.cs` (to be renamed)
