using RouterPlus.Core.Models;
using Xunit;

namespace RouterPlus.Core.Tests.Models;

/// <summary>
/// TDD tests for ProviderCredential model
/// Validates provider-specific credential data structure
/// </summary>
public sealed class ProviderCredentialTests
{
    [Fact]
    public void DefaultConstructor_InitializesWithEmptyStrings()
    {
        // Arrange & Act
        var credential = new ProviderCredential();

        // Assert
        Assert.Equal(string.Empty, credential.Email);
        Assert.Equal(string.Empty, credential.Password);
        Assert.Null(credential.TotpSecret);
    }

    [Fact]
    public void InitProperties_SetsEmailAndPassword()
    {
        // Arrange & Act
        var credential = new ProviderCredential
        {
            Email = "user@example.com",
            Password = "SecurePass123"
        };

        // Assert
        Assert.Equal("user@example.com", credential.Email);
        Assert.Equal("SecurePass123", credential.Password);
        Assert.Null(credential.TotpSecret);
    }

    [Fact]
    public void InitProperties_SetsAllThreeProperties()
    {
        // Arrange & Act
        var credential = new ProviderCredential
        {
            Email = "user@example.com",
            Password = "SecurePass123",
            TotpSecret = "JBSWY3DPEHPK3PXP"
        };

        // Assert
        Assert.Equal("user@example.com", credential.Email);
        Assert.Equal("SecurePass123", credential.Password);
        Assert.Equal("JBSWY3DPEHPK3PXP", credential.TotpSecret);
    }

    [Fact]
    public void TotpSecret_CanBeNull()
    {
        // Arrange & Act
        var credential = new ProviderCredential
        {
            Email = "user@example.com",
            Password = "password",
            TotpSecret = null
        };

        // Assert
        Assert.Null(credential.TotpSecret);
    }

    [Fact]
    public void TotpSecret_CanBeEmptyString()
    {
        // Arrange & Act
        var credential = new ProviderCredential
        {
            Email = "user@example.com",
            Password = "password",
            TotpSecret = string.Empty
        };

        // Assert
        Assert.Equal(string.Empty, credential.TotpSecret);
    }

    [Fact]
    public void Email_CanBeUsername()
    {
        // Arrange & Act
        var credential = new ProviderCredential
        {
            Email = "username123",
            Password = "password"
        };

        // Assert
        Assert.Equal("username123", credential.Email);
    }

    [Fact]
    public void Email_CanBeEmailAddress()
    {
        // Arrange & Act
        var credential = new ProviderCredential
        {
            Email = "user@provider.com",
            Password = "password"
        };

        // Assert
        Assert.Equal("user@provider.com", credential.Email);
    }

    [Fact]
    public void Password_CanContainSpecialCharacters()
    {
        // Arrange & Act
        var credential = new ProviderCredential
        {
            Email = "user@example.com",
            Password = "P@ssw0rd!#$%^&*()"
        };

        // Assert
        Assert.Equal("P@ssw0rd!#$%^&*()", credential.Password);
    }
}
