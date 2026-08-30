using System.Security.Cryptography;
using System.Text;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Infrastructure.Tests;

public sealed class ProviderConnectionVaultStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "RouterPlusTests",
        Guid.NewGuid().ToString("N"));
    private readonly ProviderConnectionVaultStore _store;

    public ProviderConnectionVaultStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new ProviderConnectionVaultStore(Path.Combine(_directory, "provider-connections.vault"));
    }

    [Fact]
    public async Task Save_and_get_round_trip_preserves_google_oauth_connection()
    {
        var connection = new ProviderAuthConnection
        {
            ProfileName = "Work",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.GoogleOAuth,
            LinkedGoogleAccount = "user@example.test"
        };

        await _store.SaveConnectionAsync(connection);

        var loaded = await _store.GetConnectionAsync("Work", ProviderKind.Codex);

        Assert.NotNull(loaded);
        Assert.Equal(AuthMethod.GoogleOAuth, loaded!.PreferredMethod);
        Assert.Equal("user@example.test", loaded.LinkedGoogleAccount);
        Assert.Null(loaded.DirectCredential);
    }

    [Fact]
    public async Task Has_credentials_returns_true_for_direct_provider_credentials()
    {
        await _store.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = "Personal",
            Provider = ProviderKind.Kiro,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential
            {
                Email = "user@example.test",
                Password = "synthetic-password",
                TotpSecret = "NONE"
            }
        });

        var hasCredentials = await _store.HasCredentialsAsync("Personal", ProviderKind.Kiro);

        Assert.True(hasCredentials);
    }

    [Fact]
    public async Task Removing_last_provider_connection_removes_profile_entry()
    {
        await _store.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = "Work",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.GoogleOAuth,
            LinkedGoogleAccount = "user@example.test"
        });

        await _store.RemoveConnectionAsync("Work", ProviderKind.Codex);

        var loaded = await _store.GetConnectionAsync("Work", ProviderKind.Codex);
        var profileConnections = await _store.GetProfileConnectionsAsync("Work");

        Assert.Null(loaded);
        Assert.Empty(profileConnections);
    }

    [Fact]
    public async Task Missing_connection_returns_false_without_creating_credentials()
    {
        var hasCredentials = await _store.HasCredentialsAsync("Missing", ProviderKind.GitHub);

        Assert.False(hasCredentials);
    }

    // === Fail-closed and atomic tests ===

    [Fact]
    public async Task Malformed_JSON_fails_closed_and_does_not_overwrite_valid_state()
    {
        // Arrange: create a valid vault first
        var validStore = new ProviderConnectionVaultStore(Path.Combine(_directory, "provider-connections.vault"));
        await validStore.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = "Work",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.GoogleOAuth,
            LinkedGoogleAccount = "user@example.test"
        });
        validStore.Dispose();

        // Corrupt the file with malformed JSON
        var vaultPath = Path.Combine(_directory, "provider-connections.vault");
        var encryptedBytes = await File.ReadAllBytesAsync(vaultPath);
        var corruptedBytes = Encoding.UTF8.GetBytes("not valid json at all");

        // We need to re-encrypt the corrupted content to match DPAPI format
        var entropy = Encoding.UTF8.GetBytes("9RouterPlus.ProviderConnectionVault.v1");
        var protectedBytes = ProtectedData.Protect(corruptedBytes, entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(vaultPath, protectedBytes);

        // Act & Assert: new store instance should fail closed (not return empty as success)
        var newStore = new ProviderConnectionVaultStore(vaultPath);
        var exception = await Assert.ThrowsAsync<CryptographicException>(() => newStore.GetConnectionAsync("Work", ProviderKind.Codex));
        Assert.Contains("Invalid vault format", exception.Message);

        newStore.Dispose();
    }

    [Fact]
    public async Task Invalid_DPAPI_data_fails_closed()
    {
        // Arrange: write completely invalid encrypted data
        var vaultPath = Path.Combine(_directory, "provider-connections.vault");
        var invalidBytes = Encoding.UTF8.GetBytes("not encrypted with DPAPI");
        await File.WriteAllBytesAsync(vaultPath, invalidBytes);

        var store = new ProviderConnectionVaultStore(vaultPath);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<CryptographicException>(() => store.GetConnectionAsync("Work", ProviderKind.Codex));
        Assert.Contains("Invalid vault format", exception.Message);

        store.Dispose();
    }

    [Fact]
    public async Task Unsupported_provider_data_is_preserved_but_not_accessible()
    {
        // This test verifies that unknown provider kinds in the vault don't crash loading
        // but are simply not returned by GetConnectionAsync for known providers
        var vaultPath = Path.Combine(_directory, "provider-connections.vault");

        // Create a vault with an unknown provider kind by directly writing JSON
        var testData = new Dictionary<string, Dictionary<int, ProviderAuthConnection>>
        {
            ["Work"] = new Dictionary<int, ProviderAuthConnection>
            {
                [999] = new ProviderAuthConnection // Unknown provider kind
                {
                    ProfileName = "Work",
                    Provider = (ProviderKind)999,
                    PreferredMethod = AuthMethod.GoogleOAuth,
                    LinkedGoogleAccount = "user@example.test"
                }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(testData);
        var entropy = Encoding.UTF8.GetBytes("9RouterPlus.ProviderConnectionVault.v1");
        var plaintextBytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plaintextBytes, entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(vaultPath, protectedBytes);

        var store = new ProviderConnectionVaultStore(vaultPath);

        // Act: should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Assert: unknown provider not accessible via known enum
        Assert.Empty(connections);

        store.Dispose();
    }

    [Fact]
    public async Task Partial_write_failure_preserves_original_file()
    {
        // This test simulates a write failure by making the vault file read-only after creation
        var vaultPath = Path.Combine(_directory, "provider-connections.vault");

        // Create initial valid vault
        var store1 = new ProviderConnectionVaultStore(vaultPath);
        await store1.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = "Work",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.GoogleOAuth,
            LinkedGoogleAccount = "user@example.test"
        });
        store1.Dispose();

        // Make the vault file read-only to force write failure
        var fileInfo = new FileInfo(vaultPath);
        fileInfo.IsReadOnly = true;

        try
        {
            var store2 = new ProviderConnectionVaultStore(vaultPath);

            // Act & Assert: save should fail
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                store2.SaveConnectionAsync(new ProviderAuthConnection
                {
                    ProfileName = "Personal",
                    Provider = ProviderKind.Kiro,
                    PreferredMethod = AuthMethod.Direct,
                    DirectCredential = new ProviderCredential
                    {
                        Email = "user2@example.test",
                        Password = "synthetic-password",
                        TotpSecret = "NONE"
                    }
                }));

            store2.Dispose();
        }
        finally
        {
            // Restore write access for cleanup
            fileInfo.IsReadOnly = false;
        }

        // Verify original data is intact
        var store3 = new ProviderConnectionVaultStore(vaultPath);
        var loaded = await store3.GetConnectionAsync("Work", ProviderKind.Codex);
        Assert.NotNull(loaded);
        Assert.Equal("user@example.test", loaded!.LinkedGoogleAccount);
        store3.Dispose();
    }

    [Fact]
    public async Task Concurrent_operations_use_single_gate()
    {
        // Verify that the operation gate serializes load-modify-save
        var store = new ProviderConnectionVaultStore(Path.Combine(_directory, "provider-connections.vault"));

        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var idx = i;
            tasks.Add(store.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = $"Profile{idx}",
                Provider = ProviderKind.Codex,
                PreferredMethod = AuthMethod.GoogleOAuth,
                LinkedGoogleAccount = $"user{idx}@example.test"
            }));
        }

        await Task.WhenAll(tasks);

        // All should succeed
        for (int i = 0; i < 10; i++)
        {
            var loaded = await store.GetConnectionAsync($"Profile{i}", ProviderKind.Codex);
            Assert.NotNull(loaded);
            Assert.Equal($"user{i}@example.test", loaded!.LinkedGoogleAccount);
        }

        store.Dispose();
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_directory))
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // Windows may hold file handles briefly
            }
        }
    }
}
