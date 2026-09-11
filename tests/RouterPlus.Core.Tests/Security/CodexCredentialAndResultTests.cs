using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.Security;

public sealed class CodexCredentialAndResultTests
{
    [Fact]
    public void FromGoogleOAuth_populates_google_credentials()
    {
        // Arrange
        const string profileId = "synthetic-profile";
        const string email = "synthetic-google@example.test";

        // Act
        var credential = CodexLoginCredential.FromGoogleOAuth(profileId, email);

        // Assert
        Assert.Equal(profileId, credential.ProfileId);
        Assert.Equal(AuthMethod.GoogleOAuth, credential.Method);
        Assert.Equal(email, credential.LinkedGoogleEmail);
        Assert.Null(credential.Email);
        Assert.Null(credential.Password);
        Assert.Null(credential.TotpSecret);
    }

    [Fact]
    public void FromGoogleOAuthWithTotp_preserves_totp_secret()
    {
        // Arrange
        const string totpSecret = "SYNTHETIC-TOTP";

        // Act
        var credential = CodexLoginCredential.FromGoogleOAuthWithTotp(
            "synthetic-profile",
            "synthetic-google@example.test",
            totpSecret);

        // Assert
        Assert.Equal(AuthMethod.GoogleOAuth, credential.Method);
        Assert.Equal(totpSecret, credential.TotpSecret);
    }

    [Fact]
    public void FromDirect_populates_direct_credentials()
    {
        // Arrange
        const string email = "synthetic-direct@example.test";
        const string password = "synthetic-password";
        const string totpSecret = "SYNTHETIC-TOTP";

        // Act
        var credential = CodexLoginCredential.FromDirect(
            "synthetic-profile",
            email,
            password,
            totpSecret);

        // Assert
        Assert.Equal(AuthMethod.Direct, credential.Method);
        Assert.Equal(email, credential.Email);
        Assert.Equal(password, credential.Password);
        Assert.Equal(totpSecret, credential.TotpSecret);
        Assert.Null(credential.LinkedGoogleEmail);
    }

    [Fact]
    public void Constructor_rejects_null_profile_id()
    {
        // Arrange
        var action = () => new CodexLoginCredential(null!, AuthMethod.Direct);

        // Act and Assert
        Assert.Throws<ArgumentNullException>(action);
    }

    [Fact]
    public void Result_factories_assign_category_and_message()
    {
        // Arrange and Act
        var results = new[]
        {
            CodexLoginResult.Success(),
            CodexLoginResult.ManualInterventionRequired("synthetic manual step"),
            CodexLoginResult.Timeout(),
            CodexLoginResult.Cancelled(),
            CodexLoginResult.Failed("synthetic failure")
        };

        // Assert
        Assert.Equal(CodexLoginResultCategory.Success, results[0].Category);
        Assert.Equal("Codex login successful", results[0].Message);
        Assert.Equal(CodexLoginResultCategory.ManualInterventionRequired, results[1].Category);
        Assert.Equal("synthetic manual step", results[1].Message);
        Assert.Equal(CodexLoginResultCategory.Timeout, results[2].Category);
        Assert.Equal("Codex login timed out", results[2].Message);
        Assert.Equal(CodexLoginResultCategory.Cancelled, results[3].Category);
        Assert.Equal("Codex login cancelled", results[3].Message);
        Assert.Equal(CodexLoginResultCategory.Failed, results[4].Category);
        Assert.Equal("synthetic failure", results[4].Message);
    }
}
