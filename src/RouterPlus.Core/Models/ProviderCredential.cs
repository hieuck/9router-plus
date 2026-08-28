namespace RouterPlus.Core.Models;

/// <summary>
/// Provider-specific credentials for direct login (not via Google OAuth).
///
/// Callers:
/// - ProviderConnectionVaultStore: Stores/loads from provider-connections.vault
/// - AutoLoginOrchestrator: Uses for direct login automation
/// - DirectLoginAutomation subclasses: Consume email/password/TOTP for automation
///
/// Schema: { Email: string, Password: string, TotpSecret?: string }
/// User request: "commit và bắt đầu" - implementing Phase 1 Step 1.1 (Create Models)
/// </summary>
public class ProviderCredential
{
    /// <summary>
    /// Email or username for the provider account.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Password for the provider account (DPAPI encrypted in vault).
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// TOTP secret for 2FA (DPAPI encrypted in vault), if configured.
    /// </summary>
    public string? TotpSecret { get; init; }
}
