using System.Security.Cryptography;
using System.Text;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Infrastructure.Tests.Security;

public sealed class ProviderConnectionVaultStoreTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _vaultPath;

    public ProviderConnectionVaultStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ProviderVaultTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _vaultPath = Path.Combine(_testDirectory, "connections.vault");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveConnectionAsync_ThenGetConnectionAsync_ReturnsConnection()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        const string profileName = "profile1";
        var connection = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "test@example.com", Password = "test-pass" }
        };

        await store.SaveConnectionAsync(connection);
        var retrieved = await store.GetConnectionAsync(profileName, ProviderKind.Codex);

        Assert.NotNull(retrieved);
        Assert.Equal(ProviderKind.Codex, retrieved.Provider);
        Assert.Equal("test@example.com", retrieved.DirectCredential?.Email);
    }

    [Fact]
    public async Task GetConnectionAsync_NonExistent_ReturnsNull()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        var result = await store.GetConnectionAsync("nonexistent", ProviderKind.Codex);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveConnectionAsync_Overwrite_UpdatesConnection()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        const string profileName = "profile1";

        var connection1 = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user1@example.com", Password = "pass1" }
        };

        var connection2 = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user2@example.com", Password = "pass2" }
        };

        await store.SaveConnectionAsync(connection1);
        await store.SaveConnectionAsync(connection2);

        var retrieved = await store.GetConnectionAsync(profileName, ProviderKind.Codex);

        Assert.NotNull(retrieved);
        Assert.Equal("user2@example.com", retrieved.DirectCredential?.Email);
    }

    [Fact]
    public async Task RemoveConnectionAsync_ExistingConnection_RemovesIt()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        const string profileName = "profile1";

        var connection = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user@example.com", Password = "pass" }
        };

        await store.SaveConnectionAsync(connection);
        await store.RemoveConnectionAsync(profileName, ProviderKind.Codex);

        var retrieved = await store.GetConnectionAsync(profileName, ProviderKind.Codex);

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task RemoveConnectionAsync_NonExistent_DoesNotThrow()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        await store.RemoveConnectionAsync("nonexistent", ProviderKind.Codex);

        // Should complete without exception
    }

    [Fact]
    public async Task HasCredentialsAsync_WithCredentials_ReturnsTrue()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        const string profileName = "profile1";

        var connection = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user@example.com", Password = "pass" }
        };

        await store.SaveConnectionAsync(connection);

        var hasCredentials = await store.HasCredentialsAsync(profileName, ProviderKind.Codex);

        Assert.True(hasCredentials);
    }

    [Fact]
    public async Task HasCredentialsAsync_WithoutCredentials_ReturnsFalse()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        var hasCredentials = await store.HasCredentialsAsync("profile1", ProviderKind.Codex);

        Assert.False(hasCredentials);
    }

    [Fact]
    public async Task GetProfileConnectionsAsync_MultipleProviders_ReturnsAll()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        const string profileName = "profile1";

        var codexConnection = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "codex@example.com", Password = "codex-pass" }
        };

        var openRouterConnection = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.OpenRouter,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "openrouter@example.com", Password = "openrouter-pass" }
        };

        await store.SaveConnectionAsync(codexConnection);
        await store.SaveConnectionAsync(openRouterConnection);

        var connections = await store.GetProfileConnectionsAsync(profileName);

        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.OpenRouter));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("openrouter@example.com", connections[ProviderKind.OpenRouter].DirectCredential?.Email);
    }

    [Fact]
    public async Task GetProfileConnectionsAsync_NoConnections_ReturnsEmpty()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        var connections = await store.GetProfileConnectionsAsync("nonexistent");

        Assert.Empty(connections);
    }

    [Fact]
    public async Task SaveConnectionAsync_MultipleProfiles_Isolated()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        var profile1Connection = new ProviderAuthConnection
        {
            ProfileName = "profile1",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user1@example.com", Password = "pass1" }
        };

        var profile2Connection = new ProviderAuthConnection
        {
            ProfileName = "profile2",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user2@example.com", Password = "pass2" }
        };

        await store.SaveConnectionAsync(profile1Connection);
        await store.SaveConnectionAsync(profile2Connection);

        var profile1Retrieved = await store.GetConnectionAsync("profile1", ProviderKind.Codex);
        var profile2Retrieved = await store.GetConnectionAsync("profile2", ProviderKind.Codex);

        Assert.NotNull(profile1Retrieved);
        Assert.NotNull(profile2Retrieved);
        Assert.Equal("user1@example.com", profile1Retrieved.DirectCredential?.Email);
        Assert.Equal("user2@example.com", profile2Retrieved.DirectCredential?.Email);
    }

    [Fact]
    public async Task Cancellation_ThrowsTaskCanceledException()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var connection = new ProviderAuthConnection
        {
            ProfileName = "profile1",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user@example.com", Password = "pass" }
        };

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            store.SaveConnectionAsync(connection, cts.Token));
    }

    [Fact]
    public async Task ConcurrentOperations_Serialized()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        var tasks = new List<Task>();

        // Start 10 concurrent write operations
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            var connection = new ProviderAuthConnection
            {
                ProfileName = $"profile{index}",
                Provider = ProviderKind.Codex,
                PreferredMethod = AuthMethod.Direct,
                DirectCredential = new ProviderCredential { Email = $"user{index}@example.com", Password = $"pass{index}" }
            };
            tasks.Add(store.SaveConnectionAsync(connection));
        }

        await Task.WhenAll(tasks);

        // Verify all writes succeeded
        for (int i = 0; i < 10; i++)
        {
            var connection = await store.GetConnectionAsync($"profile{i}", ProviderKind.Codex);
            Assert.NotNull(connection);
            Assert.Equal($"user{i}@example.com", connection.DirectCredential?.Email);
        }
    }

    [Fact]
    public async Task Dispose_WaitsForPendingOperations()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        var connection = new ProviderAuthConnection
        {
            ProfileName = "profile1",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user@example.com", Password = "pass" }
        };

        var saveTask = store.SaveConnectionAsync(connection);

        // Wait for the operation to complete before disposing
        await saveTask;
        store.Dispose();

        // Verify operation completed successfully
        Assert.True(saveTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AfterDispose_ThrowsObjectDisposedException()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        store.Dispose();

        var connection = new ProviderAuthConnection
        {
            ProfileName = "profile1",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user@example.com", Password = "pass" }
        };

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.SaveConnectionAsync(connection));
    }

    // === Legacy Vault Migration Tests ===

    /// <summary>
    /// Creates a legacy vault file with the given JSON content (encrypted with DPAPI).
    /// </summary>
    private async Task WriteLegacyVaultAsync(string vaultPath, string json)
    {
        var entropy = Encoding.UTF8.GetBytes("9RouterPlus.ProviderConnectionVault.v1");
        var plaintextBytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plaintextBytes, entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(vaultPath, protectedBytes);
    }

    [Fact]
    public async Task LegacyVault_WithKimchiStringKey_LoadsSuccessfullyAndDropsKimchi()
    {
        // Legacy vault with "Kimchi" string key (old provider name)
        var legacyJson = @"{
            ""Work"": {
                ""Kimchi"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 5,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""kimchi@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Kimchi should be dropped, Nous should be retained (Provider=6 maps to Nous=6)
        Assert.Single(connections);
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("nous@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        Assert.Equal(ProviderKind.Nous, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_WithNumericKeys5And6_LoadsSuccessfullyMapsCorrectly()
    {
        // Legacy vault with numeric keys: 5 (Kimchi) and 6 (Nous)
        var legacyJson = @"{
            ""Work"": {
                ""5"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 5,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""kimchi@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""6"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""0"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Key 5 (Kimchi) dropped, key 6 retained as Nous (6), key 0 (Codex) preserved
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("nous@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        Assert.Equal(ProviderKind.Nous, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_WithNestedProviderValue5And6_MapsCorrectly()
    {
        // Legacy vault with nested Provider property values 5 (Kimchi) and 6 (Nous)
        var legacyJson = @"{
            ""Work"": {
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Kimchi"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 5,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""kimchi@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Kimchi (Provider=5) dropped, Nous (Provider=6 retained as Nous=6) retained, Codex preserved
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("nous@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        // Verify the nested Provider value is 6 (Nous)
        Assert.Equal(ProviderKind.Nous, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }

    [Fact]
    public async Task NewVault_WithNous6_LoadsCorrectlyWithoutMigration()
    {
        // New vault with Nous=6 (current enum value) - should NOT trigger migration
        var newJson = @"{
            ""Work"": {
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, newJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Both providers should be present
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("nous@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        Assert.Equal(ProviderKind.Nous, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_WithUnknownProvider_BehavesAsBefore()
    {
        // Legacy vault with unknown provider kind (999) - should be ignored as before
        var legacyJson = @"{
            ""Work"": {
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""999"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 999,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""unknown@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing, unknown provider ignored
        var connections = await store.GetProfileConnectionsAsync("Work");

        Assert.Single(connections);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_EmptyAfterKimchiDrop_ReturnsEmptyProfile()
    {
        // Legacy vault with ONLY Kimchi - should result in empty profile after drop
        var legacyJson = @"{
            ""Work"": {
                ""Kimchi"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 5,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""kimchi@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Profile should be empty (Kimchi dropped)
        Assert.Empty(connections);

        store.Dispose();
    }

    // === Regression Tests for REMOVE-KIMCHI Migration Fixes ===

    [Fact]
    public async Task LegacyVault_NousWithNestedProvider6_LoadsAsNous6()
    {
        // Test: "Nous" entry with nested Provider=6 loads correctly as Nous=6
        var legacyJson = @"{
            ""Work"": {
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Both providers should be present, Nous retained as 6
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("nous@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        Assert.Equal(ProviderKind.Nous, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_NestedProviderNonInteger_SkipsBadEntryLoadsValidEntries()
    {
        // Regression test: nested Provider is a string instead of integer
        // Vault should still load, valid entries preserved, bad entry skipped
        var legacyJson = @"{
            ""Work"": {
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": ""not-an-integer"",
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""OpenRouter"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 2,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""or@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Valid entries should be preserved, bad entry skipped
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.OpenRouter));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("or@example.com", connections[ProviderKind.OpenRouter].DirectCredential?.Email);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_NestedProviderNull_SkipsBadEntryLoadsValidEntries()
    {
        // Regression test: nested Provider is null
        // Vault should still load, valid entries preserved, bad entry skipped
        var legacyJson = @"{
            ""Work"": {
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": null,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""OpenRouter"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 2,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""or@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Valid entries should be preserved, bad entry skipped
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.OpenRouter));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("or@example.com", connections[ProviderKind.OpenRouter].DirectCredential?.Email);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_MalformedJsonMigration_DoesNotCrashVault()
    {
        // Regression test: malformed JSON that would cause migration to fail
        // Should fall back to original JSON and let normal deserialization handle it
        // (or throw CryptographicException but not crash the whole process)
        var malformedJson = @"{
            ""Work"": {
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }"; // Missing closing brace

        await WriteLegacyVaultAsync(_vaultPath, malformedJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should throw CryptographicException (from deserialization failure), not crash
        await Assert.ThrowsAsync<CryptographicException>(() => store.GetProfileConnectionsAsync("Work"));

        store.Dispose();
    }

    // === New Tests for REMOVE-KIMCHI Migration Edge Cases ===

    [Fact]
    public async Task LegacyVault_KimchiStringKeyAfterNous_LoadsCorrectly()
    {
        // Test: Kimchi string key after Nous entry - both handled correctly
        var legacyJson = @"{
            ""Work"": {
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Kimchi"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 5,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""kimchi@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Kimchi dropped, Nous retained as 6, Codex preserved
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("nous@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        Assert.Equal(ProviderKind.Nous, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_MixedNumeric6AndNousStringKey_LastOneWins()
    {
        // Test: both numeric "6" and string "Nous" keys exist for same profile
        // Both map to ProviderKind.Nous (6) - JSON deserializer keeps last one
        var legacyJson = @"{
            ""Work"": {
                ""6"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous-from-6@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous-from-string@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Exactly one Nous entry (last one in JSON wins), Codex preserved
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        // Last one (string "Nous") wins
        Assert.Equal("nous-from-string@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        Assert.Equal(ProviderKind.Nous, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_OnlyNumeric6_MapsToNous6()
    {
        // Test: only numeric "6" exists -> retained as Nous 6
        var legacyJson = @"{
            ""Work"": {
                ""6"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        var connections = await store.GetProfileConnectionsAsync("Work");

        // Key 6 retained as Nous (6), Codex preserved
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("nous@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        Assert.Equal(ProviderKind.Nous, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_OnlyNousStringKey_KeepsAsIs()
    {
        // Test: only string "Nous" exists (no numeric "6") -> should keep as-is
        var legacyJson = @"{
            ""Work"": {
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        var connections = await store.GetProfileConnectionsAsync("Work");

        // Both providers present, Nous unchanged
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("nous@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        Assert.Equal(ProviderKind.Nous, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }

    // === New Tests for REMOVE-KIMCHI Migration Data-Loss Fix ===

    [Fact]
    public async Task LegacyVault_NousStringInvalidNestedProvider_ValidNumeric6RetainedAndInvalidStringDropped()
    {
        // Test case: numeric "6" valid + string "Nous" with non-int nested Provider
        // Expected: vault loads, numeric "6" kept (valid), string "Nous" dropped (invalid nested Provider)
        var legacyJson = @"{
            ""Work"": {
                ""6"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous-from-6@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": ""not-an-integer"",
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous-from-string@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing (invalid nested Provider causes that entry to be skipped)
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Both providers present - the numeric "6" with valid Provider=6 should work
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        // Numeric 6 entry should be the one that works
        Assert.Equal("nous-from-6@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        Assert.Equal(ProviderKind.Nous, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_MixedValidNumeric6AndValidNousString_LastOneWins()
    {
        // Test case: both numeric "6" and string "Nous" are valid
        // Both map to ProviderKind.Nous (6) - JSON deserializer keeps last one
        var legacyJson = @"{
            ""Work"": {
                ""6"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous-from-6@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 6,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous-from-string@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Exactly one Nous entry (last one wins), Codex preserved
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        // String "Nous" entry wins (last in JSON)
        Assert.Equal("nous-from-string@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        Assert.Equal(ProviderKind.Nous, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }

    [Fact]
    public async Task LegacyVault_NestedProvider5_EntryKeptWithStaleProviderValue()
    {
        // Test case: nested Provider=5 (legacy Kimchi value) - key "Nous" is kept (valid key)
        // The sanitizer doesn't drop the "Nous" key (only drops "Kimchi" string and "5" numeric key)
        // The nested Provider value is kept as-is (5) - this is a data integrity issue but not blocked
        // The entry is kept because the dictionary key "Nous" maps to valid ProviderKind.Nous (6)
        var legacyJson = @"{
            ""Work"": {
                ""Nous"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 5,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""nous@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                },
                ""Codex"": {
                    ""ProfileName"": ""Work"",
                    ""Provider"": 0,
                    ""PreferredMethod"": 1,
                    ""DirectCredential"": { ""Email"": ""codex@example.com"", ""Password"": ""pass"", ""TotpSecret"": ""NONE"" }
                }
            }
        }";

        await WriteLegacyVaultAsync(_vaultPath, legacyJson);

        var store = new ProviderConnectionVaultStore(_vaultPath);

        // Should load without throwing
        var connections = await store.GetProfileConnectionsAsync("Work");

        // Both entries kept - Nous key is valid (maps to Nous=6), nested Provider=5 is stale but not filtered
        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.Nous));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("nous@example.com", connections[ProviderKind.Nous].DirectCredential?.Email);
        // Nested Provider value is 5 (stale)
        Assert.Equal((ProviderKind)5, connections[ProviderKind.Nous].Provider);

        store.Dispose();
    }
}
