using RouterPlus.Core.Models;
using Xunit;

namespace RouterPlus.Core.Tests.Models;

/// <summary>
/// TDD tests for GoogleCredential model
/// Validates Google account credential data structure
/// </summary>
public sealed class GoogleCredentialTests
{
    [Fact]
    public void DefaultConstructor_InitializesWithEmptyStrings()
    {
        // Arrange & Act
        var credential = new GoogleCredential();

        // Assert
        Assert.Equal(string.Empty, credential.Email);
        Assert.Equal(string.Empty, credential.Password);
        Assert.Null(credential.TotpSecret);
    }

    [Fact]
    public void InitProperties_SetsEmailAndPassword()
    {
        // Arrange & Act
        var credential = new GoogleCredential
        {
            Email = "test@gmail.com",
            Password = "SecurePassword123"
        };

        // Assert
        Assert.Equal("test@gmail.com", credential.Email);
        Assert.Equal("SecurePassword123", credential.Password);
        Assert.Null(credential.TotpSecret);
    }

    [Fact]
    public void InitProperties_SetsAllThreeProperties()
    {
        // Arrange & Act
        var credential = new GoogleCredential
        {
            Email = "test@gmail.com",
            Password = "SecurePassword123",
            TotpSecret = "JBSWY3DPEHPK3PXP"
        };

        // Assert
        Assert.Equal("test@gmail.com", credential.Email);
        Assert.Equal("SecurePassword123", credential.Password);
        Assert.Equal("JBSWY3DPEHPK3PXP", credential.TotpSecret);
    }

    [Fact]
    public void TotpSecret_CanBeNull()
    {
        // Arrange & Act
        var credential = new GoogleCredential
        {
            Email = "test@gmail.com",
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
        var credential = new GoogleCredential
        {
            Email = "test@gmail.com",
            Password = "password",
            TotpSecret = string.Empty
        };

        // Assert
        Assert.Equal(string.Empty, credential.TotpSecret);
    }

    [Fact]
    public void Email_CanContainVariousFormats()
    {
        // Arrange & Act
        var credential1 = new GoogleCredential { Email = "simple@gmail.com" };
        var credential2 = new GoogleCredential { Email = "user.name+tag@domain.co.uk" };
        var credential3 = new GoogleCredential { Email = "123@test.com" };

        // Assert
        Assert.Equal("simple@gmail.com", credential1.Email);
        Assert.Equal("user.name+tag@domain.co.uk", credential2.Email);
        Assert.Equal("123@test.com", credential3.Email);
    }

    [Fact]
    public void Password_CanContainSpecialCharacters()
    {
        // Arrange & Act
        var credential = new GoogleCredential
        {
            Email = "test@gmail.com",
            Password = "P@ssw0rd!#$%^&*()"
        };

        // Assert
        Assert.Equal("P@ssw0rd!#$%^&*()", credential.Password);
    }

}
