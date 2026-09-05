using RouterPlus.Core.Models;

namespace RouterPlus.Core.Providers;

/// <summary>
/// Credentials for Codex login automation.
/// Supports both Google OAuth (linkedGoogleEmail) and Direct (email/password/totp).
/// </summary>
public sealed class CodexLoginCredential
{
    public string ProfileId { get; }
    public AuthMethod Method { get; }

    // Google OAuth fields
    public string? LinkedGoogleEmail { get; }

    // Direct login fields
    public string? Email { get; }
    public string? Password { get; }
    public string? TotpSecret { get; }

    public CodexLoginCredential(
        string profileId,
        AuthMethod method,
        string? linkedGoogleEmail = null,
        string? email = null,
        string? password = null,
        string? totpSecret = null)
    {
        ProfileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
        Method = method;
        LinkedGoogleEmail = linkedGoogleEmail;
        Email = email;
        Password = password;
        TotpSecret = totpSecret;
    }

    public static CodexLoginCredential FromGoogleOAuth(string profileId, string googleEmail) =>
        new(profileId, AuthMethod.GoogleOAuth, linkedGoogleEmail: googleEmail);

    public static CodexLoginCredential FromGoogleOAuthWithTotp(string profileId, string googleEmail, string totpSecret) =>
        new(profileId, AuthMethod.GoogleOAuth, linkedGoogleEmail: googleEmail, totpSecret: totpSecret);

    public static CodexLoginCredential FromDirect(string profileId, string email, string password, string? totpSecret = null) =>
        new(profileId, AuthMethod.Direct, email: email, password: password, totpSecret: totpSecret);
}
