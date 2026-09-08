using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class CodexLoginCredentialTests
{
    [Fact]
    public void FromGoogleOAuth_sets_linked_google_account()
    {
        var result = CodexLoginCredential.FromGoogleOAuth("profile-1", "user@example.com");

        Assert.Equal("profile-1", result.ProfileId);
        Assert.Equal(AuthMethod.GoogleOAuth, result.Method);
        Assert.Equal("user@example.com", result.LinkedGoogleEmail);
        Assert.Null(result.Email);
        Assert.Null(result.Password);
        Assert.Null(result.TotpSecret);
    }

    [Fact]
    public void FromGoogleOAuthWithTotp_sets_linked_account_and_totp()
    {
        var result = CodexLoginCredential.FromGoogleOAuthWithTotp(
            "profile-1", "user@example.com", "totp-secret");

        Assert.Equal(AuthMethod.GoogleOAuth, result.Method);
        Assert.Equal("user@example.com", result.LinkedGoogleEmail);
        Assert.Equal("totp-secret", result.TotpSecret);
    }

    [Fact]
    public void FromDirect_sets_direct_login_fields()
    {
        var result = CodexLoginCredential.FromDirect(
            "profile-1", "codex@example.com", "password", "totp-secret");

        Assert.Equal("profile-1", result.ProfileId);
        Assert.Equal(AuthMethod.Direct, result.Method);
        Assert.Equal("codex@example.com", result.Email);
        Assert.Equal("password", result.Password);
        Assert.Equal("totp-secret", result.TotpSecret);
        Assert.Null(result.LinkedGoogleEmail);
    }

    [Fact]
    public void Constructor_throws_when_profile_id_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new CodexLoginCredential(
            null!, AuthMethod.Direct));
    }
}
