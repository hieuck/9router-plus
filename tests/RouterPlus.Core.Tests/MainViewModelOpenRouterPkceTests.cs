using System.Net;
using System.Text;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelOpenRouterPkceTests
{
    [Fact]
    public async Task ConnectOpenRouterOAuth_when_flow_succeeds_saves_key()
    {
        var profile = Profile();
        var handler = new SaveKeyHandler();
        using var httpClient = new HttpClient(handler);
        var vm = new MainViewModel(httpClient: httpClient)
        {
            DashboardBaseUrl = "http://router.test"
        };
        vm.Profiles.Add(profile);
        vm.ProfileRows.Add(new ProfileRowViewModel(profile, vm.Providers));
        vm.SelectedProfile = profile;
        vm.OpenRouterPkceFlow = _ => Task.FromResult(OpenRouterPkceResult.Succeeded("sk-or-v1-oauth"));

        try
        {
            var ok = await vm.ConnectOpenRouterOAuthAsync();

            Assert.True(ok);
            Assert.Contains("POST /api/providers", handler.Requests);
        }
        finally
        {
            await new DpapiSecretVault().RemoveAsync(ProfileSecretKey.Create(profile, ProviderKind.OpenRouter));
        }
    }

    [Fact]
    public async Task ConnectOpenRouterOAuth_when_flow_fails_does_not_save()
    {
        var profile = Profile();
        var handler = new SaveKeyHandler();
        using var httpClient = new HttpClient(handler);
        var vm = new MainViewModel(httpClient: httpClient)
        {
            DashboardBaseUrl = "http://router.test"
        };
        vm.Profiles.Add(profile);
        vm.ProfileRows.Add(new ProfileRowViewModel(profile, vm.Providers));
        vm.SelectedProfile = profile;
        vm.OpenRouterPkceFlow = _ => Task.FromResult(OpenRouterPkceResult.Failed("Invalid code"));

        var ok = await vm.ConnectOpenRouterOAuthAsync();

        Assert.False(ok);
        Assert.Empty(handler.Requests);
        Assert.False(vm.IsWorkflowInProgress);
        Assert.True(vm.ConnectOpenRouterOAuthCommand.CanExecute(null));
        Assert.False(vm.ProviderCards.Single(card => card.Kind == ProviderKind.OpenRouter).IsWorkflowInProgress);
    }

    [Fact]
    public async Task ConnectOpenRouterOAuth_when_no_profile_selected_returns_false()
    {
        using var httpClient = new HttpClient(new SaveKeyHandler());
        var vm = new MainViewModel(httpClient: httpClient);
        var ok = await vm.ConnectOpenRouterOAuthAsync();
        Assert.False(ok);
    }

    [Fact]
    public void ConnectOpenRouterOAuthCommand_canExecute_follows_SelectedProfile()
    {
        var profile = Profile();
        var vm = new MainViewModel(httpClient: new HttpClient(new SaveKeyHandler()));
        vm.Profiles.Add(profile);
        vm.ProfileRows.Add(new ProfileRowViewModel(profile, vm.Providers));
        Assert.False(vm.ConnectOpenRouterOAuthCommand.CanExecute(null));
        vm.SelectedProfile = profile;
        Assert.True(vm.ConnectOpenRouterOAuthCommand.CanExecute(null));
    }

    private static ChromeProfile Profile() =>
        new(
            "profile-id",
            "Work",
            "Default",
            Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N")),
            IsDefault: true);

    private sealed class SaveKeyHandler : HttpMessageHandler
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
                "/api/providers" when request.Method == HttpMethod.Get =>
                    "{\"connections\":[]}",
                "/api/providers" when request.Method == HttpMethod.Post =>
                    "{\"connection\":{\"id\":\"openrouter-1\",\"provider\":\"openrouter\",\"name\":\"Work\",\"priority\":1,\"isActive\":true,\"testStatus\":\"unknown\"}}",
                "/api/providers/openrouter-1/test" when request.Method == HttpMethod.Post =>
                    "{\"valid\":true}",
                "/api/usage/openrouter-1" when request.Method == HttpMethod.Get =>
                    "{}",
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {path}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
