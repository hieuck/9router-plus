using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using System.Net;
using System.Text;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelDashboardCommandTests
{
    [Fact]
    public void Dashboard_command_is_disabled_until_a_profile_is_selected()
    {
        var viewModel = new MainViewModel();

        Assert.False(viewModel.OpenProviderDashboardCommand.CanExecute(ProviderKind.Kiro));
    }

    [Fact]
    public async Task Manual_status_sync_appends_a_log_entry_even_when_result_is_unchanged()
    {
        var viewModel = new MainViewModel();

        await viewModel.RefreshConnectionStatusesAsync();
        var firstLog = viewModel.LogText;

        await viewModel.RefreshConnectionStatusesAsync();
        var secondLog = viewModel.LogText;

        Assert.Contains("[SYNC]", secondLog, StringComparison.Ordinal);
        Assert.True(secondLog.Length > firstLog.Length);
    }

    [Fact]
    public async Task Test_connection_does_not_refresh_the_full_connection_list()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var viewModel = new MainViewModel(httpClient: httpClient)
        {
            DashboardBaseUrl = "http://router.test"
        };
        var profile = new ChromeProfile(
            "profile-id",
            "Work",
            "Default",
            Path.Combine(Path.GetTempPath(), "RouterPlusTests"),
            IsDefault: true);
        viewModel.Profiles.Add(profile);
        viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
        viewModel.SelectedProfile = profile;

        viewModel.TestConnectionCommand.Execute(ProviderKind.Codex);

        await WaitForAsync(() => handler.Requests.Count >= 2 && viewModel.StatusText.Contains("succeeded", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, handler.Requests.Count);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.True(condition());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var body = request.Method == HttpMethod.Post
                ? "{\"valid\":true}"
                : "{\"connections\":[{\"id\":\"codex-1\",\"provider\":\"codex\",\"name\":\"Work\",\"priority\":1,\"isActive\":true,\"testStatus\":\"active\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
