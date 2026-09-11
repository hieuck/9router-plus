using System.Net;
using System.Text;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Core.Tests.ViewModels;

public sealed class MainViewModelApiKeyTests
{
    [Fact]
    public async Task AddApiKey_preserves_selected_profile_when_refresh_rebuilds_filtered_rows()
    {
        var profile = new ChromeProfile(
            "profile-id",
            "Work",
            "Default",
            Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N")),
            IsDefault: true);
        var handler = new ApiKeyAddHandler();
        using var httpClient = new HttpClient(handler);
        var viewModel = new MainViewModel(httpClient: httpClient, secretVault: new InMemorySecretVault())
        {
            DashboardBaseUrl = "http://router.test"
        };
        viewModel.Profiles.Add(profile);
        viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
        viewModel.SelectedProfile = profile;
        viewModel.ProfileSearchText = profile.Name;
        viewModel.FilteredProfileRows.CollectionChanged += (_, eventArgs) =>
        {
            if (eventArgs.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                viewModel.SelectedProfile = null;
            }
        };

        var added = await viewModel.AddApiKeyAsync(ProviderKind.OpenRouter, "test-key");

        Assert.True(added);
        Assert.Equal(profile.Id, viewModel.SelectedProfile?.Id);
    }

    [Fact]
    public async Task AddApiKey_rejects_provider_without_api_key_workflow_without_http_call()
    {
        var handler = new ApiKeyAddHandler();
        using var httpClient = new HttpClient(handler);
        var viewModel = new MainViewModel(httpClient: httpClient);

        var added = await viewModel.AddApiKeyAsync(ProviderKind.Kimchi, "test-key");

        Assert.False(added);
        Assert.Equal("Kimchi không dùng API key.", viewModel.StatusText);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AddApiKey_requires_selected_profile_without_http_call()
    {
        var handler = new ApiKeyAddHandler();
        using var httpClient = new HttpClient(handler);
        var viewModel = new MainViewModel(httpClient: httpClient);

        var added = await viewModel.AddApiKeyAsync(ProviderKind.OpenRouter, "test-key");

        Assert.False(added);
        Assert.Equal("Hãy chọn Chrome profile trước.", viewModel.StatusText);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AddApiKey_rejects_blank_key_without_http_call()
    {
        var profile = new ChromeProfile(
            "profile-id",
            "Work",
            "Default",
            Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N")),
            IsDefault: true);
        var handler = new ApiKeyAddHandler();
        using var httpClient = new HttpClient(handler);
        var viewModel = new MainViewModel(httpClient: httpClient, secretVault: new InMemorySecretVault())
        {
            SelectedProfile = profile
        };

        var added = await viewModel.AddApiKeyAsync(ProviderKind.OpenRouter, "  \t ");

        Assert.False(added);
        Assert.Equal("Hãy dán API key vào ô bảo mật.", viewModel.StatusText);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AddApiKey_tests_created_connection_before_refreshing_status()
    {
        var profile = new ChromeProfile(
            "profile-id",
            "Work",
            "Default",
            Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N")),
            IsDefault: true);
        var handler = new ApiKeyAddHandler();
        using var httpClient = new HttpClient(handler);
        var viewModel = new MainViewModel(httpClient: httpClient, secretVault: new InMemorySecretVault())
        {
            DashboardBaseUrl = "http://router.test"
        };
        viewModel.Profiles.Add(profile);
        viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
        viewModel.SelectedProfile = profile;

        var added = await viewModel.AddApiKeyAsync(ProviderKind.OpenRouter, "test-key");

        Assert.True(added);
        Assert.Equal(
            [
                "GET /api/providers",
                "POST /api/providers",
                "POST /api/providers/openrouter-1/test",
                "GET /api/providers",
                "GET /api/usage/openrouter-1"
            ],
            handler.Requests);
        Assert.Equal(
            ProviderHealthState.Healthy,
            viewModel.ProviderCards.Single(card => card.Kind == ProviderKind.OpenRouter).HealthState);
    }

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
    }

    private sealed class ApiKeyAddHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");
            var body = path switch
            {
                "/api/providers" when request.Method == HttpMethod.Get => Requests.Count == 1
                    ? "{\"connections\":[]}"
                    : "{\"connections\":[{\"id\":\"openrouter-1\",\"provider\":\"openrouter\",\"name\":\"Work\",\"priority\":1,\"isActive\":true,\"testStatus\":\"active\"}]}",
                "/api/providers" when request.Method == HttpMethod.Post => "{\"connection\":{\"id\":\"openrouter-1\",\"provider\":\"openrouter\",\"name\":\"Work\",\"priority\":1,\"isActive\":true,\"testStatus\":\"unknown\"}}",
                "/api/providers/openrouter-1/test" when request.Method == HttpMethod.Post => "{\"valid\":true}",
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {path}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}