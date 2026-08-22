using System.Net;
using System.Text;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelApiKeyTests
{
    [Fact]
    public async Task AddApiKey_tests_created_connection_before_refreshing_status()
    {
        var profile = new ChromeProfile(
            "profile-id",
            "Work",
            "Default",
            Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N")),
            IsDefault: true);
        var secretKey = ProfileSecretKey.Create(profile, ProviderKind.OpenRouter);
        var handler = new ApiKeyAddHandler();
        using var httpClient = new HttpClient(handler);
        var viewModel = new MainViewModel(httpClient: httpClient)
        {
            DashboardBaseUrl = "http://router.test"
        };
        viewModel.Profiles.Add(profile);
        viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
        viewModel.SelectedProfile = profile;

        try
        {
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
        finally
        {
            await new DpapiSecretVault().RemoveAsync(secretKey);
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