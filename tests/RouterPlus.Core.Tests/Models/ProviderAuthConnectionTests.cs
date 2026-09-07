using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using Xunit;

namespace RouterPlus.Core.Tests.Models;

/// <summary>
/// TDD tests for ProviderAuthConnection model
/// Validates provider-profile connection configuration
/// </summary>
public sealed class ProviderAuthConnectionTests
{
    [Fact]
    public void DefaultConstructor_InitializesWithDefaults()
    {
        // Arrange & Act
        var connection = new ProviderAuthConnection();

        // Assert
        Assert.Equal(string.Empty, connection.ProfileName);
        Assert.Equal(ProviderKind.Codex, connection.Provider); // enum default is first value
        Assert.Equal(AuthMethod.GoogleOAuth, connection.PreferredMethod); // enum default
        Assert.Null(connection.LinkedGoogleAccount);
        Assert.Null(connection.DirectCredential);
    }

    [Fact]
    public void InitProperties_SetsGoogleOAuthConnection()
    {
        // Arrange & Act
        var connection = new ProviderAuthConnection
        {
            ProfileName = "Work Profile",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.GoogleOAuth,
            LinkedGoogleAccount = "work@gmail.com"
        };

        // Assert
        Assert.Equal("Work Profile", connection.ProfileName);
        Assert.Equal(ProviderKind.Codex, connection.Provider);
        Assert.Equal(AuthMethod.GoogleOAuth, connection.PreferredMethod);
        Assert.Equal("work@gmail.com", connection.LinkedGoogleAccount);
        Assert.Null(connection.DirectCredential);
    }

    [Fact]
    public void InitProperties_SetsDirectCredentialConnection()
    {
        // Arrange
        var credential = new ProviderCredential
        {
            Email = "user@provider.com",
            Password = "password123",
            TotpSecret = "TOTP123"
        };

        // Act
        var connection = new ProviderAuthConnection
        {
            ProfileName = "Personal",
            Provider = ProviderKind.Kiro,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = credential
        };

        // Assert
        Assert.Equal("Personal", connection.ProfileName);
        Assert.Equal(ProviderKind.Kiro, connection.Provider);
        Assert.Equal(AuthMethod.Direct, connection.PreferredMethod);
        Assert.Null(connection.LinkedGoogleAccount);
        Assert.NotNull(connection.DirectCredential);
        Assert.Equal("user@provider.com", connection.DirectCredential.Email);
    }

    [Fact]
    public void InitProperties_CanHaveBothAuthMethods()
    {
        // Arrange
        var credential = new ProviderCredential
        {
            Email = "fallback@provider.com",
            Password = "fallback123"
        };

        // Act
        var connection = new ProviderAuthConnection
        {
            ProfileName = "Hybrid",
            Provider = ProviderKind.OpenRouter,
            PreferredMethod = AuthMethod.GoogleOAuth,
            LinkedGoogleAccount = "primary@gmail.com",
            DirectCredential = credential
        };

        // Assert
        Assert.Equal(AuthMethod.GoogleOAuth, connection.PreferredMethod);
        Assert.Equal("primary@gmail.com", connection.LinkedGoogleAccount);
        Assert.NotNull(connection.DirectCredential);
    }

    [Theory]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Kiro)]
    [InlineData(ProviderKind.OpenRouter)]
    [InlineData(ProviderKind.GitHub)]
    [InlineData(ProviderKind.Ollama)]
    [InlineData(ProviderKind.Kimchi)]
    public void Provider_CanBeAnyProviderKind(ProviderKind provider)
    {
        // Arrange & Act
        var connection = new ProviderAuthConnection
        {
            ProfileName = "Test",
            Provider = provider
        };

        // Assert
        Assert.Equal(provider, connection.Provider);
    }

    [Theory]
    [InlineData(AuthMethod.GoogleOAuth)]
    [InlineData(AuthMethod.Direct)]
    public void PreferredMethod_CanBeAnyAuthMethod(AuthMethod method)
    {
        // Arrange & Act
        var connection = new ProviderAuthConnection
        {
            ProfileName = "Test",
            Provider = ProviderKind.Codex,
            PreferredMethod = method
        };

        // Assert
        Assert.Equal(method, connection.PreferredMethod);
    }

    [Fact]
    public void LinkedGoogleAccount_CanBeNull()
    {
        // Arrange & Act
        var connection = new ProviderAuthConnection
        {
            ProfileName = "Test",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            LinkedGoogleAccount = null
        };

        // Assert
        Assert.Null(connection.LinkedGoogleAccount);
    }

    [Fact]
    public void DirectCredential_CanBeNull()
    {
        // Arrange & Act
        var connection = new ProviderAuthConnection
        {
            ProfileName = "Test",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.GoogleOAuth,
            DirectCredential = null
        };

        // Assert
        Assert.Null(connection.DirectCredential);
    }
}
