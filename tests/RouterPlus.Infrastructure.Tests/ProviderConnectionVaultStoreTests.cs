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

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
