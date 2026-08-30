using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// Integration tests for Credentials Manager vault operations - Phase 5 Step 5.2
/// Tests vault integration without UI dependencies
/// </summary>
public sealed class CredentialsManagerVaultIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GoogleAccountVaultPaths _vaultPaths;
    private readonly IGoogleAccountVaultStore _googleVaultStore;

    public CredentialsManagerVaultIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"creds-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _vaultPaths = new GoogleAccountVaultPaths(_tempDir);
        _googleVaultStore = new GoogleAccountVaultStore(_vaultPaths);
    }

    [Fact]
    public async Task VaultSession_CreateAndLoad_ReturnsCredentials()
    {
        // Arrange - Create vault with test data
        var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "test-password",
            CancellationToken.None);

        var credential = new GoogleLoginCredential(
            "test-profile",
            "test@example.com",
            "test-password",
            "test-totp-secret");

        var vault = session.Vault.Upsert(credential);
        session.Replace(vault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);
        await session.RememberAsync(CancellationToken.None);
        await session.DisposeAsync();

        // Act - Reopen with remembered session
        var reopened = await _googleVaultStore.TryOpenRememberedAsync(
            _vaultPaths.VaultPath,
            CancellationToken.None);

        // Assert
        Assert.NotNull(reopened);
        Assert.Single(reopened.Vault.Records);
        Assert.Equal("test@example.com", reopened.Vault.Records.First().Email);
        Assert.Equal("test-totp-secret", reopened.Vault.Records.First().TotpSecret);

        await reopened.DisposeAsync();
    }

    [Fact]
    public async Task VaultSession_RemoveCredential_ImmutablePattern()
    {
        // Arrange - Create vault with multiple credentials
        var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "test-password",
            CancellationToken.None);

        var cred1 = new GoogleLoginCredential("profile1", "user1@example.com", "pass1", "totp1");
        var cred2 = new GoogleLoginCredential("profile2", "user2@example.com", "pass2", "totp2");

        var vault = session.Vault.Upsert(cred1).Upsert(cred2);
        session.Replace(vault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Act - Remove one credential using immutable pattern
        var filtered = vault.Records.Where(r => r.Email != "user1@example.com");
        var newVault = new GoogleAccountVault(filtered);
        session.Replace(newVault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Assert
        Assert.Single(session.Vault.Records);
        Assert.Equal("user2@example.com", session.Vault.Records.First().Email);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task VaultSession_UpdateCredential_ImmutablePattern()
    {
        // Arrange - Create vault with one credential
        var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "test-password",
            CancellationToken.None);

        var original = new GoogleLoginCredential("profile1", "user@example.com", "oldpass", "oldtotp");
        var vault = session.Vault.Upsert(original);
        session.Replace(vault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Act - Update using Upsert
        var updated = new GoogleLoginCredential("profile1", "user@example.com", "newpass", "newtotp");
        var newVault = session.Vault.Upsert(updated);
        session.Replace(newVault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Assert
        Assert.Single(session.Vault.Records);
        var record = session.Vault.Records.First();
        Assert.Equal("newpass", record.Password);
        Assert.Equal("newtotp", record.TotpSecret);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task VaultSession_EmptyVault_ReturnsNoRecords()
    {
        // Arrange - Create empty vault
        var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "test-password",
            CancellationToken.None);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);
        await session.RememberAsync(CancellationToken.None);
        await session.DisposeAsync();

        // Act - Reopen empty vault
        var reopened = await _googleVaultStore.TryOpenRememberedAsync(
            _vaultPaths.VaultPath,
            CancellationToken.None);

        // Assert
        Assert.NotNull(reopened);
        Assert.Empty(reopened.Vault.Records);

        await reopened.DisposeAsync();
    }

    [Fact]
    public async Task VaultSession_MultipleProfiles_MaintainsSeparateCredentials()
    {
        // Arrange - Create vault with multiple profiles
        var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "test-password",
            CancellationToken.None);

        var cred1 = new GoogleLoginCredential("Work", "work@example.com", "workpass", "worktotp");
        var cred2 = new GoogleLoginCredential("Personal", "personal@example.com", "personalpass", "NONE");
        var cred3 = new GoogleLoginCredential("Gaming", "gaming@example.com", "gamingpass", "gamingtotp");

        var vault = session.Vault
            .Upsert(cred1)
            .Upsert(cred2)
            .Upsert(cred3);
        session.Replace(vault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Act - Query by profile
        var workCred = vault.Records.FirstOrDefault(r => r.ProfileId == "Work");
        var personalCred = vault.Records.FirstOrDefault(r => r.ProfileId == "Personal");

        // Assert
        Assert.Equal(3, vault.Records.Count());
        Assert.NotNull(workCred);
        Assert.Equal("work@example.com", workCred.Email);
        Assert.Equal("worktotp", workCred.TotpSecret);
        Assert.NotNull(personalCred);
        Assert.Equal("NONE", personalCred.TotpSecret); // NONE placeholder for no TOTP

        await session.DisposeAsync();
    }

    [Fact]
    public async Task VaultSession_RemoveNonExistentCredential_NoEffect()
    {
        // Arrange - Create vault with one credential
        var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "test-password",
            CancellationToken.None);

        var cred = new GoogleLoginCredential("profile1", "user@example.com", "pass", "totp");
        var vault = session.Vault.Upsert(cred);
        session.Replace(vault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Act - Try to remove non-existent credential
        var filtered = vault.Records.Where(r => r.Email != "nonexistent@example.com");
        var newVault = new GoogleAccountVault(filtered);
        session.Replace(newVault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Assert - Original credential still exists
        Assert.Single(session.Vault.Records);
        Assert.Equal("user@example.com", session.Vault.Records.First().Email);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task VaultSession_UpsertSameProfileId_ReplacesOldCredential()
    {
        // Arrange - Create vault with one credential
        var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "test-password",
            CancellationToken.None);

        var original = new GoogleLoginCredential("profile1", "old@example.com", "oldpass", "oldtotp");
        var vault = session.Vault.Upsert(original);
        session.Replace(vault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Act - Upsert with same ProfileId but different email
        var updated = new GoogleLoginCredential("profile1", "new@example.com", "newpass", "newtotp");
        var newVault = session.Vault.Upsert(updated);
        session.Replace(newVault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Assert - Only one credential exists with new values
        Assert.Single(session.Vault.Records);
        var record = session.Vault.Records.First();
        Assert.Equal("new@example.com", record.Email);
        Assert.Equal("newpass", record.Password);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task VaultSession_RemoveAllCredentials_EmptyVault()
    {
        // Arrange - Create vault with multiple credentials
        var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "test-password",
            CancellationToken.None);

        var cred1 = new GoogleLoginCredential("profile1", "user1@example.com", "pass1", "NONE");
        var cred2 = new GoogleLoginCredential("profile2", "user2@example.com", "pass2", "NONE");

        var vault = session.Vault.Upsert(cred1).Upsert(cred2);
        session.Replace(vault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Act - Remove all credentials
        var newVault = new GoogleAccountVault(Enumerable.Empty<GoogleLoginCredential>());
        session.Replace(newVault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Assert
        Assert.Empty(session.Vault.Records);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task VaultSession_StableProfileId_RoundTripsThroughStore()
    {
        var root = Path.Combine(_tempDir, "profiles");
        var profileDir = Path.Combine(root, "Default");
        Directory.CreateDirectory(profileDir);
        var profile = new ChromeProfile(
            ChromeProfile.CreateId(root, "Default"),
            "Display Name",
            "Default",
            root,
            true);

        // Arrange - Create vault with a record keyed by the stable profile Id.
        var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "test-password",
            CancellationToken.None);
        var credential = new GoogleLoginCredential(profile.Id, "stable@example.com", "pass", "NONE");
        var vault = session.Vault.Upsert(credential);
        session.Replace(vault);
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);
        await session.RememberAsync(CancellationToken.None);
        await session.DisposeAsync();

        // Act - Reopen with remembered session.
        var reopened = await _googleVaultStore.TryOpenRememberedAsync(
            _vaultPaths.VaultPath,
            CancellationToken.None);

        // Assert - Lookup by stable Id round-trips; display name is not a key.
        Assert.NotNull(reopened);
        Assert.Single(reopened!.Vault.Records);
        Assert.NotNull(reopened.Vault.Find(profile.Id));
        Assert.Equal("stable@example.com", reopened.Vault.Find(profile.Id)!.Email);
        Assert.Null(reopened.Vault.Find(profile.Name));

        // Act - Remove by stable Id through the immutable filter pattern.
        var filtered = reopened.Vault.Records.Where(r => r.ProfileId != profile.Id);
        reopened.Replace(new GoogleAccountVault(filtered));
        await _googleVaultStore.SaveAsync(reopened, CancellationToken.None);

        // Assert
        Assert.Empty(reopened.Vault.Records);

        await reopened.DisposeAsync();
    }

    [Fact]
    public async Task VaultSession_LegacyNameKeyedRecord_IsPairedByResolverWhenNameIsUnique()
    {
        var root = Path.Combine(_tempDir, "profiles-legacy");
        var profileDir = Path.Combine(root, "Default");
        Directory.CreateDirectory(profileDir);
        var profile = new ChromeProfile(
            ChromeProfile.CreateId(root, "Default"),
            "Unique Name",
            "Default",
            root,
            true);

        // Arrange - A legacy vault record keyed by the display name.
        var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "test-password",
            CancellationToken.None);
        var legacy = new GoogleLoginCredential("Unique Name", "legacy@example.com", "pass", "NONE");
        session.Replace(new GoogleAccountVault(new[] { legacy }));
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Act - Resolve same way the Credentials Manager load path does: stable
        // Id first, then unambiguous display-name fallback.
        var byId = session.Vault.Find(profile.Id);
        var byUniqueName = session.Vault.Find(profile.Name);

        // Assert - The record is found by display name (the legacy key) and NOT
        // by the stable Id yet (still name-keyed on disk, no load migration).
        Assert.Null(byId);
        Assert.NotNull(byUniqueName);
        Assert.Equal("legacy@example.com", byUniqueName.Email);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task VaultSession_AmbiguousNameKeyedRecord_IsNotAdopted()
    {
        var root = Path.Combine(_tempDir, "profiles-ambiguous");
        var firstDir = Path.Combine(root, "First");
        var secondDir = Path.Combine(root, "Second");
        Directory.CreateDirectory(firstDir);
        Directory.CreateDirectory(secondDir);
        var first = new ChromeProfile(ChromeProfile.CreateId(root, "First"), "Shared Name", "First", root, true);
        var second = new ChromeProfile(ChromeProfile.CreateId(root, "Second"), "Shared Name", "Second", root, true);

        // Ensure the sibling profile Ids differ while the display names collide.
        Assert.Equal("Shared Name", first.Name);
        Assert.Equal("Shared Name", second.Name);
        Assert.NotEqual(first.Id, second.Id);

        // Arrange - A single legacy record keyed by the shared display name.
        var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "test-password",
            CancellationToken.None);
        var legacy = new GoogleLoginCredential("Shared Name", "ambiguous@example.com", "pass", "NONE");
        session.Replace(new GoogleAccountVault(new[] { legacy }));
        await _googleVaultStore.SaveAsync(session, CancellationToken.None);

        // Act - Resolve for each profile the same way the load path does.
        var discovered = Enumerable.Empty<ChromeProfile>().Append(first).Append(second);
        var uniqueNameMatches = discovered
            .Where(p => discovered.Count(other => other.Name == p.Name) == 1);
        var firstCredential = uniqueNameMatches.Contains(first) ? session.Vault.Find(first.Name) : null;
        var secondCredential = uniqueNameMatches.Contains(second) ? session.Vault.Find(second.Name) : null;

        // Assert - Neither profile may adopt the shared-name record: there is no
        // silent merge of ambiguous names. The record stays on disk, orphaned.
        Assert.Empty(uniqueNameMatches);
        Assert.Null(firstCredential);
        Assert.Null(secondCredential);
        Assert.Single(session.Vault.Records);
        Assert.Equal("Shared Name", session.Vault.Records.Single().ProfileId);

        await session.DisposeAsync();
    }

    [Fact]
    public void GoogleLoginCredential_RequiresAllParameters()
    {
        // Arrange & Act & Assert - Empty email throws
        Assert.Throws<ArgumentException>(() =>
            new GoogleLoginCredential("profile", "", "password", "totp"));

        // Empty password throws
        Assert.Throws<ArgumentException>(() =>
            new GoogleLoginCredential("profile", "email@example.com", "", "totp"));

        // Empty ProfileId throws
        Assert.Throws<ArgumentException>(() =>
            new GoogleLoginCredential("", "email@example.com", "password", "totp"));

        // Empty TOTP throws (TOTP is required)
        Assert.Throws<ArgumentException>(() =>
            new GoogleLoginCredential("profile", "email@example.com", "password", ""));

        // Valid credential with TOTP
        var validWithTotp = new GoogleLoginCredential("profile", "email@example.com", "password", "JBSWY3DPEHPK3PXP");
        Assert.Equal("JBSWY3DPEHPK3PXP", validWithTotp.TotpSecret);

        // Valid credential with NONE placeholder (for accounts without 2FA)
        var validWithoutTotp = new GoogleLoginCredential("profile", "email@example.com", "password", "NONE");
        Assert.Equal("NONE", validWithoutTotp.TotpSecret);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Cleanup best effort
        }
    }
}
