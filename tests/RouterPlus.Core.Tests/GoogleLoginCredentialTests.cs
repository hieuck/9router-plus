using RouterPlus.Core.Security;
using Xunit;

namespace RouterPlus.Core.Tests;

public sealed class GoogleLoginCredentialTests
{
    [Fact]
    public void Credential_rejects_blank_profile_id_and_invalid_email()
    {
        Assert.Throws<ArgumentException>(() => new GoogleLoginCredential("", "user@example.com", "p", "s"));
        Assert.Throws<ArgumentException>(() => new GoogleLoginCredential("   ", "user@example.com", "p", "s"));
        Assert.Throws<FormatException>(() => new GoogleLoginCredential("profile-1", "not-an-email", "p", "s"));
        Assert.Throws<FormatException>(() => new GoogleLoginCredential("profile-1", "no-at-sign", "p", "s"));
        Assert.Throws<FormatException>(() => new GoogleLoginCredential("profile-1", "@nodomain.com", "p", "s"));
    }

    [Fact]
    public void Credential_trims_profile_id_and_email()
    {
        var credential = new GoogleLoginCredential("  profile-1  ", "  user@example.com  ", "password", "secret");

        Assert.Equal("profile-1", credential.ProfileId);
        Assert.Equal("user@example.com", credential.Email);
    }

    [Fact]
    public void Credential_requires_non_empty_password_and_totp_secret()
    {
        Assert.Throws<ArgumentException>(() => new GoogleLoginCredential("profile-1", "user@example.com", "", "s"));
        Assert.Throws<ArgumentException>(() => new GoogleLoginCredential("profile-1", "user@example.com", "p", ""));
    }

    [Fact]
    public void Credential_accepts_valid_email_formats()
    {
        var credential1 = new GoogleLoginCredential("profile-1", "user@example.com", "p", "s");
        var credential2 = new GoogleLoginCredential("profile-2", "user.name+tag@example.co.uk", "p", "s");

        Assert.Equal("user@example.com", credential1.Email);
        Assert.Equal("user.name+tag@example.co.uk", credential2.Email);
    }

    [Fact]
    public void Credential_is_immutable_record()
    {
        var credential1 = new GoogleLoginCredential("profile-1", "user@example.com", "password", "secret");
        var credential2 = new GoogleLoginCredential("profile-1", "user@example.com", "password", "secret");

        Assert.Equal(credential1, credential2);
        Assert.True(credential1 == credential2);
    }
}
