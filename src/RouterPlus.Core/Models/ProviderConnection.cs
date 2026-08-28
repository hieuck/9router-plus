using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Models;

/// <summary>
/// Maps a Chrome profile to a provider with authentication configuration.
///
/// Callers:
/// - ProviderConnectionVaultStore: Stores/loads from provider-connections.vault
/// - AutoLoginOrchestrator: Reads to determine auth method and credentials
/// - MainViewModel: Checks HasCredentialsAsync for batch login eligibility
/// - ProfileRowViewModel: Displays per-provider credential indicators
///
/// Schema: {
///   ProfileName: string,
///   Provider: ProviderKind,
///   PreferredMethod: AuthMethod,
///   LinkedGoogleAccount?: string,
///   DirectCredential?: ProviderCredential
/// }
///
/// User request: "commit và bắt đầu" - implementing Phase 1 Step 1.1 (Create Models)
/// </summary>
public class ProviderConnection
{
    /// <summary>
    /// Chrome profile name this connection belongs to.
    /// </summary>
    public string ProfileName { get; init; } = string.Empty;

    /// <summary>
    /// Provider this connection is for (Codex, Kiro, GitHub, OpenRouter, etc.).
    /// </summary>
    public ProviderKind Provider { get; init; }

    /// <summary>
    /// Preferred authentication method (auto-detected if not explicitly set).
    /// Auto-detect logic: GoogleOAuth if LinkedGoogleAccount exists, otherwise Direct.
    /// </summary>
    public AuthMethod PreferredMethod { get; init; }

    /// <summary>
    /// Google account email linked for Google OAuth login (if using GoogleOAuth method).
    /// References an entry in google-accounts.vault.
    /// </summary>
    public string? LinkedGoogleAccount { get; init; }

    /// <summary>
    /// Provider-specific credentials for direct login (if using Direct method or as fallback).
    /// </summary>
    public ProviderCredential? DirectCredential { get; init; }
}
