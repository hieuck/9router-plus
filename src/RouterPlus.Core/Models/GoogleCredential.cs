namespace RouterPlus.Core.Models;

/// <summary>
/// Google account credentials for Google OAuth login flows.
///
/// Callers:
/// - GoogleAccountVaultStore: Stores/loads from google-accounts.vault
/// - AutoLoginOrchestrator: Loads credentials for Google OAuth automation
/// - GoogleOAuthFlowAutomation: Uses email/password/TOTP for automation
/// - MainViewModel: Manages via credentials UI
///
/// Schema: { Email: string, Password: string, TotpSecret?: string }
///
/// User request: "commit và bắt đầu" - implementing Phase 1 Step 1.1 (Create Models)
/// </summary>
public class GoogleCredential
{
    /// <summary>
    /// Google account email address.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Google account password (DPAPI encrypted in vault).
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// TOTP secret for Google 2FA (DPAPI encrypted in vault), if configured.
    /// </summary>
    public string? TotpSecret { get; init; }
}
