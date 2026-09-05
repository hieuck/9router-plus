using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using Xunit;

namespace RouterPlus.Core.Tests.Security;

/// <summary>
/// Tests for vault lookup fallback logic: profile.Id (new) → profile.Name (legacy)
/// </summary>
public sealed class GoogleAccountVaultFallbackTests
{
    [Fact]
    public void Find_by_id_returns_credential_when_exists()
    {
        // Arrange
        var credential = new GoogleLoginCredential("profile-123", "user@example.com", "password", "TOTP123");
        var vault = new GoogleAccountVault(new[] { credential });

        // Act
        var result = vault.Find("profile-123");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("user@example.com", result.Email);
    }

    [Fact]
    public void Find_by_id_returns_null_when_not_exists()
    {
        // Arrange
        var credential = new GoogleLoginCredential("profile-123", "user@example.com", "password", "TOTP123");
        var vault = new GoogleAccountVault(new[] { credential });

        // Act
        var result = vault.Find("profile-456");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Find_by_name_returns_credential_when_stored_with_name_key()
    {
        // Arrange - legacy credential stored with profile name as key
        var credential = new GoogleLoginCredential("Profile 1", "user@example.com", "password", "TOTP123");
        var vault = new GoogleAccountVault(new[] { credential });

        // Act
        var result = vault.Find("Profile 1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("user@example.com", result.Email);
    }

    [Fact]
    public void Fallback_pattern_finds_credential_by_name_when_id_fails()
    {
        // Arrange - simulate legacy credential stored with name as key
        var legacyCredential = new GoogleLoginCredential("Profile 1", "legacy@example.com", "password", "TOTP123");
        var vault = new GoogleAccountVault(new[] { legacyCredential });

        // Simulate MainViewModel fallback logic
        var profile = new ChromeProfile(
            Id: "profile-new-id-123",
            Name: "Profile 1",
            DirectoryName: "Profile 1",
            UserDataDirectory: "C:\\fake",
            IsDefault: false);

        // Act - try Id first, then fallback to Name
        var credential = vault.Find(profile.Id);
        if (credential == null)
        {
            credential = vault.Find(profile.Name);
        }

        // Assert - fallback should succeed
        Assert.NotNull(credential);
        Assert.Equal("legacy@example.com", credential.Email);
    }

    [Fact]
    public void Fallback_pattern_uses_id_when_both_exist()
    {
        // Arrange - both old (name-keyed) and new (id-keyed) credentials exist
        var legacyCredential = new GoogleLoginCredential("Profile 1", "legacy@example.com", "oldpass", "OLDTOTP");
        var newCredential = new GoogleLoginCredential("profile-123", "new@example.com", "newpass", "NEWTOTP");

        // Vault will keep only one per key due to Dictionary deduplication
        var vault = new GoogleAccountVault(new[] { legacyCredential, newCredential });

        var profile = new ChromeProfile(
            Id: "profile-123",
            Name: "Profile 1",
            DirectoryName: "Profile 1",
            UserDataDirectory: "C:\\fake",
            IsDefault: false);

        // Act - try Id first (should succeed, no fallback needed)
        var credential = vault.Find(profile.Id);

        // Assert - should use ID-keyed credential
        Assert.NotNull(credential);
        Assert.Equal("new@example.com", credential.Email);
    }

    [Fact]
    public void Fallback_pattern_returns_null_when_neither_id_nor_name_exists()
    {
        // Arrange
        var credential = new GoogleLoginCredential("other-profile", "other@example.com", "password", "TOTP123");
        var vault = new GoogleAccountVault(new[] { credential });

        var profile = new ChromeProfile(
            Id: "profile-123",
            Name: "Profile 1",
            DirectoryName: "Profile 1",
            UserDataDirectory: "C:\\fake",
            IsDefault: false);

        // Act - try Id first, then fallback to Name
        var result = vault.Find(profile.Id);
        if (result == null)
        {
            result = vault.Find(profile.Name);
        }

        // Assert - both should fail
        Assert.Null(result);
    }
}
