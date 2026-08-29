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
