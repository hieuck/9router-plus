using RouterPlus.Core.Security;
using Xunit;

namespace RouterPlus.Core.Tests;

public sealed class GoogleLoginVaultTests
{
    [Fact]
    public void Vault_starts_empty()
    {
        var vault = new GoogleLoginVault();

        Assert.Empty(vault.Records);
    }

    [Fact]
    public void Vault_upsert_replaces_the_existing_record_for_the_same_profile_id()
    {
        var vault = new GoogleLoginVault();
        var first = new GoogleLoginCredential("profile-1", "first@example.com", "p1", "s1");
        var second = new GoogleLoginCredential("profile-1", "second@example.com", "p2", "s2");

        var updated = vault.Upsert(first).Upsert(second);

        var record = Assert.Single(updated.Records);
        Assert.Equal("second@example.com", record.Email);
        Assert.Equal("p2", record.Password);
        Assert.Equal("s2", record.TotpSecret);
    }

    [Fact]
    public void Vault_upsert_adds_new_profile_without_affecting_existing()
    {
        var vault = new GoogleLoginVault();
        var first = new GoogleLoginCredential("profile-1", "first@example.com", "p1", "s1");
        var second = new GoogleLoginCredential("profile-2", "second@example.com", "p2", "s2");

        var updated = vault.Upsert(first).Upsert(second);

        Assert.Equal(2, updated.Records.Count);
        Assert.Equal("first@example.com", updated.Find("profile-1")!.Email);
        Assert.Equal("second@example.com", updated.Find("profile-2")!.Email);
    }

    [Fact]
    public void Vault_find_returns_null_for_unknown_profile()
    {
        var vault = new GoogleLoginVault();
        var credential = new GoogleLoginCredential("profile-1", "user@example.com", "p", "s");
        var updated = vault.Upsert(credential);

        Assert.Null(updated.Find("profile-2"));
        Assert.Null(updated.Find("unknown"));
    }

    [Fact]
    public void Vault_find_is_case_sensitive_by_profile_id()
    {
        var vault = new GoogleLoginVault();
        var credential = new GoogleLoginCredential("profile-1", "user@example.com", "p", "s");
        var updated = vault.Upsert(credential);

        Assert.NotNull(updated.Find("profile-1"));
        Assert.Null(updated.Find("Profile-1"));
        Assert.Null(updated.Find("PROFILE-1"));
    }

    [Fact]
    public void Vault_constructor_accepts_initial_records()
    {
        var records = new[]
        {
            new GoogleLoginCredential("profile-1", "first@example.com", "p1", "s1"),
            new GoogleLoginCredential("profile-2", "second@example.com", "p2", "s2")
        };

        var vault = new GoogleLoginVault(records);

        Assert.Equal(2, vault.Records.Count);
        Assert.Equal("first@example.com", vault.Find("profile-1")!.Email);
        Assert.Equal("second@example.com", vault.Find("profile-2")!.Email);
    }

    [Fact]
    public void Vault_constructor_deduplicates_by_profile_id()
    {
        var records = new[]
        {
            new GoogleLoginCredential("profile-1", "first@example.com", "p1", "s1"),
            new GoogleLoginCredential("profile-1", "second@example.com", "p2", "s2")
        };

        var vault = new GoogleLoginVault(records);

        var record = Assert.Single(vault.Records);
        Assert.Equal("first@example.com", record.Email);
    }

    [Fact]
    public void Vault_is_immutable()
    {
        var vault = new GoogleLoginVault();
        var credential = new GoogleLoginCredential("profile-1", "user@example.com", "p", "s");

        var updated = vault.Upsert(credential);

        Assert.Empty(vault.Records);
        Assert.Single(updated.Records);
    }
}
