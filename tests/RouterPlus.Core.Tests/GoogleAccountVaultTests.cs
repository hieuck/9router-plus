using RouterPlus.Core.Security;
using Xunit;

namespace RouterPlus.Core.Tests;

public sealed class GoogleAccountVaultTests
{
    [Fact]
    public void Vault_starts_empty()
    {
        var vault = new GoogleAccountVault();

        Assert.Empty(vault.Records);
    }

    [Fact]
    public void Vault_upsert_replaces_the_existing_record_for_the_same_profile_id()
    {
        var vault = new GoogleAccountVault();
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
        var vault = new GoogleAccountVault();
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
        var vault = new GoogleAccountVault();
        var credential = new GoogleLoginCredential("profile-1", "user@example.com", "p", "s");
        var updated = vault.Upsert(credential);

        Assert.Null(updated.Find("profile-2"));
        Assert.Null(updated.Find("unknown"));
    }

    [Fact]
    public void Vault_find_is_case_sensitive_by_profile_id()
    {
        var vault = new GoogleAccountVault();
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

        var vault = new GoogleAccountVault(records);

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

        var vault = new GoogleAccountVault(records);

        var record = Assert.Single(vault.Records);
        Assert.Equal("first@example.com", record.Email);
    }

    [Fact]
    public void Vault_is_immutable()
    {
        var vault = new GoogleAccountVault();
        var credential = new GoogleLoginCredential("profile-1", "user@example.com", "p", "s");

        var updated = vault.Upsert(credential);

        Assert.Empty(vault.Records);
        Assert.Single(updated.Records);
    }

    [Fact]
    public void Vault_keeps_records_with_distinct_profile_ids_even_when_display_names_collide()
    {
        // Two current profiles may share a display name; the vault keys strictly
        // by the stable profile Id so both records survive independently.
        const string firstId = "AAAAAAAAAAAAAAAA";
        const string secondId = "BBBBBBBBBBBBBBBB";
        var vault = new GoogleAccountVault();
        var first = new GoogleLoginCredential(firstId, "first@example.com", "p1", "s1");
        var second = new GoogleLoginCredential(secondId, "second@example.com", "p2", "s2");

        var updated = vault.Upsert(first).Upsert(second);

        Assert.Equal(2, updated.Records.Count);
        var firstRecord = updated.Find(firstId);
        var secondRecord = updated.Find(secondId);
        Assert.NotNull(firstRecord);
        Assert.NotNull(secondRecord);
        Assert.NotEqual(firstRecord, secondRecord);
        Assert.Equal("first@example.com", firstRecord!.Email);
        Assert.Equal("second@example.com", secondRecord!.Email);
        Assert.Null(updated.Find("Display Name")); // display names are not vault keys
    }

    [Fact]
    public void Vault_record_survives_display_name_rename_under_unchanged_profile_id()
    {
        // A profile rename changes only the display name, never the stable Id.
        // Re-saving under the unchanged Id replaces the prior record rather than
        // orphaning or duplicating it.
        var vault = new GoogleAccountVault();
        var original = new GoogleLoginCredential("stable-id", "user@example.com", "p", "s");

        var updated = vault.Upsert(original);

        // Re-save with new values under the unchanged stable Id.
        var reSaved = updated.Upsert(new GoogleLoginCredential("stable-id", "user@example.com", "newpass", "s2"));

        var record = Assert.Single(reSaved.Records);
        Assert.Equal("stable-id", record.ProfileId);
        Assert.Equal("stable-id", reSaved.Find("stable-id")!.ProfileId);
        Assert.Equal("newpass", record.Password);
        Assert.Equal("s2", record.TotpSecret);
    }
}
