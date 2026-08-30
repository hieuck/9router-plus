using System.Net;
using System.Text;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelAutoGetKeyTests
{
    [Fact]
    public async Task AutoGetKey_when_flow_succeeds_saves_key_to_9router()
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
        vm.OpenRouterKeyFlow = (localProfile, credential, ct) =>
            Task.FromResult(Success("sk-or-v1-created"));
        vm.AutoGetKeyCredentials = (localProfile, ct) =>
            Task.FromResult<GoogleLoginCredential?>(new GoogleLoginCredential(
                profileId: profile.Id, email: "user@example.com", password: "pw", totpSecret: "JBSWY3DPEHPK3PXP"));

        try
        {
            var ok = await vm.AutoGetKeyAsync();

            Assert.True(ok);
            Assert.Contains("POST /api/providers", handler.Requests);
        }
        finally
        {
            await new DpapiSecretVault().RemoveAsync(ProfileSecretKey.Create(profile, ProviderKind.OpenRouter));
        }
    }

    [Fact]
    public async Task AutoGetKey_when_flow_fails_does_not_save()
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
        vm.OpenRouterKeyFlow = (localProfile, credential, ct) =>
            Task.FromResult(Failure("No 'Sign in with Google' button was found."));

        var ok = await vm.AutoGetKeyAsync();

        Assert.False(ok);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AutoGetKey_when_key_already_shown_does_not_run_browser_flow()
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
        var flowRuns = 0;
        vm.OpenRouterKeyFlow = (localProfile, credential, ct) =>
        {
            flowRuns++;
            return Task.FromResult(Success("sk-or-v1-skipped"));
        };

        // Mirror of _googleLoginAutomation: still called exactly once by the VM
        // for the picked profile, without the browser flow variant (fast path).
        vm.AutoGetKeyCredentials = (profile, ct) =>
            Task.FromResult<GoogleLoginCredential?>(new GoogleLoginCredential(
                profileId: profile.Id, email: "user@example.com", password: "pw", totpSecret: "JBSWY3DPEHPK3PXP"));

        try
        {
            var ok = await vm.AutoGetKeyAsync();

            Assert.True(ok);
            Assert.Equal(1, flowRuns);
            Assert.Contains("POST /api/providers", handler.Requests);
        }
        finally
        {
            await new DpapiSecretVault().RemoveAsync(ProfileSecretKey.Create(profile, ProviderKind.OpenRouter));
        }
    }

    [Fact]
    public async Task AutoGetKey_when_no_profile_selected_returns_false()
    {
        using var httpClient = new HttpClient(new SaveKeyHandler());
        var vm = new MainViewModel(httpClient: httpClient);
        var ok = await vm.AutoGetKeyAsync();
        Assert.False(ok);
    }

    [Fact]
    public async Task AutoGetKey_when_no_vault_credential_returns_false()
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
        vm.OpenRouterKeyFlow = (localProfile, credential, ct) =>
        { throw new InvalidOperationException("Flow should not run when no credential is available."); };
        var ok = await vm.AutoGetKeyAsync();
        Assert.False(ok);
    }

    [Fact]
    public void AutoGetKeyCommand_canExecute_follows_SelectedProfile()
    {
        var profile = Profile();
        var vm = new MainViewModel(httpClient: new HttpClient(new SaveKeyHandler()));
        vm.Profiles.Add(profile);
        vm.ProfileRows.Add(new ProfileRowViewModel(profile, vm.Providers));
        Assert.False(vm.AutoGetKeyCommand.CanExecute(null));
        vm.SelectedProfile = profile;
        Assert.True(vm.AutoGetKeyCommand.CanExecute(null));
    }

    [Fact]
    public void AutoGetKeyCommand_raises_CanExecuteChanged_when_SelectedProfile_changes()
    {
        var profile = Profile();
        var vm = new MainViewModel(httpClient: new HttpClient(new SaveKeyHandler()));
        vm.Profiles.Add(profile);
        vm.ProfileRows.Add(new ProfileRowViewModel(profile, vm.Providers));

        var raised = 0;
        vm.AutoGetKeyCommand.CanExecuteChanged += (_, _) => raised++;
        vm.SelectedProfile = profile;

        // WPF only re-queries CanExecute after CanExecuteChanged, so the Button
        // stays grey unless the profile setter notifies this command.
        Assert.True(raised > 0);
    }

    private static ChromeProfile Profile() =>
        new(
            "profile-id",
            "Work",
            "Default",
            Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N")),
            IsDefault: true);

    private static OpenRouterKeyFlowOrchestrator.OpenRouterKeyFlowResult Success(string apiKey) =>
        new(Success: true, apiKey, null);

    private static OpenRouterKeyFlowOrchestrator.OpenRouterKeyFlowResult Failure(string message) =>
        new(Success: false, null, message);

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