using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;
using System.Net;
using System.Text;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelErrorLoggingTests
{
    [Fact]
    public void Profile_action_errors_do_not_expose_exception_details_in_status_or_log()
    {
        var viewModel = new MainViewModel();
        const string sensitiveDetails = "C:\\Users\\private-user\\profile\\token=sk-live-example";

        viewModel.MarkProfileActionFailed(
            new InvalidOperationException(sensitiveDetails),
            "OpenProfileFolder");

        Assert.Contains("OpenProfileFolder", viewModel.LogText, StringComparison.Ordinal);
        Assert.Contains("[ERROR]", viewModel.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveDetails, viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveDetails, viewModel.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-example", viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-example", viewModel.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void Router_api_errors_do_not_expose_server_details_in_status_or_log()
    {
        var viewModel = new MainViewModel();
        const string sensitiveDetails = "provider response included token=sk-live-example";

        viewModel.MarkProfileActionFailed(
            new RouterApiException(sensitiveDetails, HttpStatusCode.BadGateway),
            "RefreshConnections");

        Assert.DoesNotContain(sensitiveDetails, viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveDetails, viewModel.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-example", viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-example", viewModel.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_key_paste_errors_do_not_expose_clipboard_details_in_status_or_log()
    {
        var viewModel = new MainViewModel();
        const string sensitiveDetails = "clipboard path C:\\Users\\private-user\\token=sk-live-example";

        viewModel.MarkApiKeyPasteFailed(ProviderKind.OpenRouter, sensitiveDetails);

        Assert.Contains("Không thể dán API key", viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveDetails, viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveDetails, viewModel.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-example", viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-example", viewModel.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_connection_errors_do_not_expose_server_details_in_status_or_log()
    {
        const string sensitiveDetails = "provider response included token=sk-live-example";
        using var httpClient = new HttpClient(new SensitiveConnectionHandler(sensitiveDetails));
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

        await WaitForAsync(() => viewModel.StatusText.Contains("failed", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(sensitiveDetails, viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveDetails, viewModel.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-example", viewModel.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-example", viewModel.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_status_tooltip_does_not_expose_server_details()
    {
        const string sensitiveDetails = "provider response included token=sk-live-example";
        var status = new ProfileProviderStatusViewModel(ProviderCatalog.Get(ProviderKind.Codex));

        status.SetConnectionCount(
            1,
            ProviderHealthState.Error,
            testStatus: "failed",
            errorCode: "401",
            lastError: sensitiveDetails);

        Assert.DoesNotContain(sensitiveDetails, status.ToolTip, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-example", status.ToolTip, StringComparison.Ordinal);
        Assert.Contains("401", status.ToolTip, StringComparison.Ordinal);
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

    private sealed class SensitiveConnectionHandler(string error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Method == HttpMethod.Post
                ? $"{{\"valid\":false,\"error\":\"{error}\"}}"
                : "{\"connections\":[{\"id\":\"codex-1\",\"provider\":\"codex\",\"name\":\"Work\",\"priority\":1,\"isActive\":true,\"testStatus\":\"active\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
