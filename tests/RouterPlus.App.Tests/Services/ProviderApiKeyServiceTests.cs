using Moq;
using RouterPlus.App.Services;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.App.Tests.Services;

public sealed class ProviderApiKeyServiceTests
{
    private static ChromeProfile Profile() =>
        new(
            "profile-id",
            "Work",
            "Default",
            Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N")),
            IsDefault: true);

    private static ProviderConnection MakeConnection(string id, string name, int priority = 1) =>
        new(id, ProviderKind.OpenRouter, name, priority, true);

    private sealed class InMemorySecretVault : ISecretVault
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default)
        {
            _values[key] = secret;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public bool Contains(string key) => _values.ContainsKey(key);
        public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;
    }

    [Fact]
    public async Task CreatePath_CallsAdd_Store_Test_AndReturnsCreated()
    {
        var profile = Profile();
        var connection = MakeConnection("openrouter-1", profile.Name);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListConnectionsAsync(ProviderKind.OpenRouter, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Array.Empty<ProviderConnection>());
        mock.Setup(m => m.AddApiKeyConnectionAsync(ProviderKind.OpenRouter, profile.Name, "test-key", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        mock.Setup(m => m.TestConnectionAsync("openrouter-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderConnectionTestResult(true, null));

        var vault = new InMemorySecretVault();
        var service = new ProviderApiKeyService(vault);

        var result = await service.AddOrUpdateAsync(
            ProviderKind.OpenRouter,
            "test-key",
            profile,
            () => Task.FromResult(mock.Object),
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal("openrouter-1", result.Connection.Id);
        Assert.True(result.TestResult.Valid);
        Assert.Equal("test-key", vault.Get(ProfileSecretKey.Create(profile, ProviderKind.OpenRouter)));
        mock.Verify(m => m.UpdateConnectionAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(), It.IsAny<bool?>()), Times.Never);
        mock.Verify(m => m.AddApiKeyConnectionAsync(ProviderKind.OpenRouter, profile.Name, "test-key", 1, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.TestConnectionAsync("openrouter-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePath_WhenNameMatches_UpdatesAndReturnsNotCreated()
    {
        var profile = Profile();
        var existing = MakeConnection("existing-1", profile.Name);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListConnectionsAsync(ProviderKind.OpenRouter, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(new[] { existing });
        mock.Setup(m => m.UpdateConnectionAsync("existing-1", profile.Name, null, "new-key", It.IsAny<CancellationToken>(), null))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.TestConnectionAsync("existing-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderConnectionTestResult(true, null));

        var vault = new InMemorySecretVault();
        var service = new ProviderApiKeyService(vault);

        var result = await service.AddOrUpdateAsync(
            ProviderKind.OpenRouter,
            "new-key",
            profile,
            () => Task.FromResult(mock.Object),
            CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal("existing-1", result.Connection.Id);
        Assert.Equal("new-key", vault.Get(ProfileSecretKey.Create(profile, ProviderKind.OpenRouter)));
        mock.Verify(m => m.UpdateConnectionAsync("existing-1", profile.Name, null, "new-key", It.IsAny<CancellationToken>(), null), Times.Once);
        mock.Verify(m => m.AddApiKeyConnectionAsync(
            It.IsAny<ProviderKind>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TestFailureResult_Returned()
    {
        var profile = Profile();
        var connection = MakeConnection("openrouter-1", profile.Name);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListConnectionsAsync(ProviderKind.OpenRouter, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Array.Empty<ProviderConnection>());
        mock.Setup(m => m.AddApiKeyConnectionAsync(ProviderKind.OpenRouter, profile.Name, "test-key", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        mock.Setup(m => m.TestConnectionAsync("openrouter-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderConnectionTestResult(false, "bad"));

        var vault = new InMemorySecretVault();
        var service = new ProviderApiKeyService(vault);

        var result = await service.AddOrUpdateAsync(
            ProviderKind.OpenRouter,
            "test-key",
            profile,
            () => Task.FromResult(mock.Object),
            CancellationToken.None);

        Assert.False(result.TestResult.Valid);
        Assert.Equal("bad", result.TestResult.Error);
    }

    [Fact]
    public async Task Exception_Propagates()
    {
        var profile = Profile();
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListConnectionsAsync(ProviderKind.OpenRouter, It.IsAny<CancellationToken>(), false))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var service = new ProviderApiKeyService(new InMemorySecretVault());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddOrUpdateAsync(
                ProviderKind.OpenRouter,
                "test-key",
                profile,
                () => Task.FromResult(mock.Object),
                CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var profile = Profile();
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListConnectionsAsync(ProviderKind.OpenRouter, It.IsAny<CancellationToken>(), false))
            .Returns((ProviderKind p, CancellationToken ct, bool iq) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<ProviderConnection>>(Array.Empty<ProviderConnection>());
            });

        var service = new ProviderApiKeyService(new InMemorySecretVault());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.AddOrUpdateAsync(
                ProviderKind.OpenRouter,
                "test-key",
                profile,
                () => Task.FromResult(mock.Object),
                cts.Token));
    }

    [Fact]
    public async Task VaultKey_Stored_OnCreate()
    {
        var profile = Profile();
        var connection = MakeConnection("openrouter-1", profile.Name);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListConnectionsAsync(ProviderKind.OpenRouter, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Array.Empty<ProviderConnection>());
        mock.Setup(m => m.AddApiKeyConnectionAsync(ProviderKind.OpenRouter, profile.Name, "secret", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        mock.Setup(m => m.TestConnectionAsync("openrouter-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderConnectionTestResult(true, null));

        var vault = new InMemorySecretVault();
        var service = new ProviderApiKeyService(vault);

        await service.AddOrUpdateAsync(
            ProviderKind.OpenRouter,
            "secret",
            profile,
            () => Task.FromResult(mock.Object),
            CancellationToken.None);

        Assert.Equal("secret", vault.Get(ProfileSecretKey.Create(profile, ProviderKind.OpenRouter)));
    }

    [Fact]
    public async Task Agnes_Create_Asserts_TestConnection_Called_And_Returned_Provider_Agnes()
    {
        var profile = Profile();
        var connection = new ProviderConnection("agnes-1", ProviderKind.Agnes, profile.Name, 1, true);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListConnectionsAsync(ProviderKind.Agnes, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(Array.Empty<ProviderConnection>());
        mock.Setup(m => m.AddApiKeyConnectionAsync(ProviderKind.Agnes, profile.Name, "agnes-secret", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        mock.Setup(m => m.TestConnectionAsync("agnes-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderConnectionTestResult(true, null));

        var vault = new InMemorySecretVault();
        var service = new ProviderApiKeyService(vault);

        var result = await service.AddOrUpdateAsync(
            ProviderKind.Agnes,
            "agnes-secret",
            profile,
            () => Task.FromResult(mock.Object),
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal("agnes-1", result.Connection.Id);
        Assert.Equal(ProviderKind.Agnes, result.Connection.Provider);
        Assert.True(result.TestResult.Valid);
        mock.Verify(m => m.TestConnectionAsync("agnes-1", It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(m => m.AddApiKeyConnectionAsync(ProviderKind.Agnes, profile.Name, "agnes-secret", 1, It.IsAny<CancellationToken>()), Times.Once);
    }
}
