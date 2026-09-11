using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.Security;

public sealed class CodexLoginModelsTests
{
    [Fact]
    public void FromGoogleOAuth_creates_google_credential_without_totp()
    {
        // Act
        var credential = CodexLoginCredential.FromGoogleOAuth("profile-synthetic", "google@example.test");

        // Assert
        Assert.Equal("profile-synthetic", credential.ProfileId);
        Assert.Equal(AuthMethod.GoogleOAuth, credential.Method);
        Assert.Equal("google@example.test", credential.LinkedGoogleEmail);
        Assert.Null(credential.Email);
        Assert.Null(credential.Password);
        Assert.Null(credential.TotpSecret);
    }

    [Fact]
    public void FromGoogleOAuthWithTotp_preserves_totp_secret()
    {
        // Act
        var credential = CodexLoginCredential.FromGoogleOAuthWithTotp(
            "profile-synthetic",
            "google@example.test",
            "SYNTHETIC-TOTP");

        // Assert
        Assert.Equal(AuthMethod.GoogleOAuth, credential.Method);
        Assert.Equal("google@example.test", credential.LinkedGoogleEmail);
        Assert.Equal("SYNTHETIC-TOTP", credential.TotpSecret);
    }

    [Fact]
    public void FromDirect_creates_direct_credential_with_optional_totp()
    {
        // Act
        var credential = CodexLoginCredential.FromDirect(
            "profile-synthetic",
            "direct@example.test",
            "synthetic-password",
            "SYNTHETIC-TOTP");

        // Assert
        Assert.Equal(AuthMethod.Direct, credential.Method);
        Assert.Equal("direct@example.test", credential.Email);
        Assert.Equal("synthetic-password", credential.Password);
        Assert.Equal("SYNTHETIC-TOTP", credential.TotpSecret);
        Assert.Null(credential.LinkedGoogleEmail);
    }

    [Fact]
    public void Constructor_rejects_null_profile_id()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => new CodexLoginCredential(
            null!,
            AuthMethod.Direct,
            email: "direct@example.test",
            password: "synthetic-password"));
    }

    [Theory]
    [InlineData(CodexLoginResultCategory.Success, "Codex login successful")]
    [InlineData(CodexLoginResultCategory.Timeout, "Codex login timed out")]
    [InlineData(CodexLoginResultCategory.Cancelled, "Codex login cancelled")]
    public void Result_factories_create_expected_defaults(
        CodexLoginResultCategory expectedCategory,
        string expectedMessage)
    {
        // Act
        var result = expectedCategory switch
        {
            CodexLoginResultCategory.Success => CodexLoginResult.Success(),
            CodexLoginResultCategory.Timeout => CodexLoginResult.Timeout(),
            CodexLoginResultCategory.Cancelled => CodexLoginResult.Cancelled(),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedCategory))
        };

        // Assert
        Assert.Equal(expectedCategory, result.Category);
        Assert.Equal(expectedMessage, result.Message);
    }

    [Fact]
    public void Result_factories_preserve_custom_reasons()
    {
        // Arrange
        const string reason = "Synthetic manual action required.";

        // Act
        var manual = CodexLoginResult.ManualInterventionRequired(reason);
        var failed = CodexLoginResult.Failed(reason);

        // Assert
        Assert.Equal(reason, manual.Message);
        Assert.Equal(reason, failed.Message);
    }
}
