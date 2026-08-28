namespace RouterPlus.Core.Models;

/// <summary>
/// Authentication method for provider login.
/// Used by ProviderConnection to specify preferred login method.
/// Consumed by AutoLoginOrchestrator to determine which automation to use.
/// </summary>
public enum AuthMethod
{
    /// <summary>
    /// Login via Google OAuth ("Continue with Google").
    /// </summary>
    GoogleOAuth,

    /// <summary>
    /// Login via provider's own credentials (email/password/TOTP).
    /// </summary>
    Direct
}
