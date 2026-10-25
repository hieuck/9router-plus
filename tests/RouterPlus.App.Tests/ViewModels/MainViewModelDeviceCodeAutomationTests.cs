using System.Net;
using System.Reflection;
using System.Text;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.App.Tests.ViewModels;

public sealed class MainViewModelDeviceCodeAutomationTests
{
    [Fact]
    public async Task OpenProvider_kiro_uses_manual_fallback_when_automation_is_unavailable()
    {
        // Arrange
        var profile = CreateProfile();
        var handler = new DeviceCodeHandler();
        var launchedUrls = new List<string>();
        using var httpClient = new HttpClient(handler);
        var viewModel = CreateViewModel(profile, httpClient, launchedUrls);

        // Act
        await viewModel.OpenProviderForTestAsync(ProviderKind.Kiro);

        // Assert
        Assert.Equal(["https://aws.example/device?user_code=ABCD-EFGH"], launchedUrls);
        Assert.Contains("GET /api/oauth/kiro/device-code?auth_method=idc", handler.Requests);
        Assert.Contains("POST /api/oauth/kiro/poll", handler.Requests);
        Assert.Contains("PUT /api/providers/kiro-1", handler.Requests);
        Assert.False(viewModel.IsWorkflowInProgress);
        Assert.Equal("Đã kết nối Kiro với profile Work.", viewModel.StatusText);
    }

    [Fact]
    public async Task OpenProvider_kiro_continues_polling_when_automation_fails()
    {
        // Arrange
        var profile = CreateProfile();
        var handler = new DeviceCodeHandler();
        using var httpClient = new HttpClient(handler);
        var automationCalls = 0;
        var viewModel = CreateViewModel(
            profile,
            httpClient,
            launchedUrls: [],
            launchManagedChrome: (_, _, _, _) =>
            {
                automationCalls++;
                throw new InvalidOperationException("synthetic automation failure");
            });
        SetInstallation(viewModel, profile);

        // Act
        await viewModel.OpenProviderForTestAsync(ProviderKind.Kiro);

        // Assert
        Assert.Equal(1, automationCalls);
        Assert.Contains("POST /api/oauth/kiro/poll", handler.Requests);
        Assert.Contains("PUT /api/providers/kiro-1", handler.Requests);
        Assert.False(viewModel.IsWorkflowInProgress);
        Assert.Equal("Đã kết nối Kiro với profile Work.", viewModel.StatusText);
    }

    [Fact]
    public async Task OpenProvider_nous_uses_automation_branch_and_falls_back_on_failure()
    {
        // Arrange
        var profile = CreateProfile();
        var handler = new NousDeviceCodeHandler();
        var launchedUrls = new List<string>();
        int chromeLaunchCount = 0;
        using var httpClient = new HttpClient(handler);
        var viewModel = CreateViewModel(
            profile,
            httpClient,
            launchedUrls,
            launchManagedChrome: (_, _, _, _) =>
            {
                chromeLaunchCount++;
                throw new InvalidOperationException("synthetic automation failure");
            });
        SetInstallation(viewModel, profile);

        // Act
        await viewModel.OpenProviderForTestAsync(ProviderKind.Nous);

        // Assert
        // Automation was attempted (chrome launched once before throwing)
        Assert.Equal(1, chromeLaunchCount);
        // No manual URL launch when automation is attempted (even if it throws)
        Assert.Empty(launchedUrls);
        Assert.Contains("GET /api/oauth/nous/device-code", handler.Requests);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("nous/device-code?auth_method="));
        Assert.Contains("POST /api/oauth/nous/poll", handler.Requests);
        Assert.Contains("PUT /api/providers/nous-1", handler.Requests);
        Assert.False(viewModel.IsWorkflowInProgress);
        Assert.Equal("Đã kết nối Nous với profile Work.", viewModel.StatusText);
    }

    private static MainViewModel CreateViewModel(
        ChromeProfile profile,
        HttpClient httpClient,
        List<string> launchedUrls,
        Func<ChromeInstallation, ChromeProfile, Uri, CancellationToken, Task<ChromeManagedSession>>? launchManagedChrome = null)
    {
        var viewModel = new MainViewModel(
            httpClient: httpClient,
            launchUrl: (_, url) =>
            {
                launchedUrls.Add(url);
                return Task.CompletedTask;
            },
            launchManagedChrome: launchManagedChrome)
        {
            DashboardBaseUrl = "http://router.test"
        };
        viewModel.Profiles.Add(profile);
        viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
        viewModel.SelectedProfile = profile;
        return viewModel;
    }

    private static void SetInstallation(MainViewModel viewModel, ChromeProfile profile)
    {
        var field = typeof(MainViewModel).GetField("_installation", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(viewModel, new ChromeInstallation("Z:\\synthetic\\chrome.exe", profile.UserDataDirectory));
    }

    private static ChromeProfile CreateProfile() => new(
        "profile-id",
        "Work",
        "Default",
        Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N")),
        IsDefault: true);

    private sealed class DeviceCodeHandler : HttpMessageHandler
    {
        private int _providerListCount;

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add($"{request.Method} {path}");
            var body = path switch
            {
                "/api/providers" => ProviderList(++_providerListCount),
                "/api/oauth/kiro/device-code?auth_method=idc" =>
                    "{\"device_code\":\"device-1\",\"user_code\":\"ABCD-EFGH\",\"verification_uri\":\"https://aws.example/device\",\"verification_uri_complete\":\"https://aws.example/device?user_code=ABCD-EFGH\",\"expires_in\":60,\"interval\":1}",
                "/api/oauth/kiro/poll" => "{\"success\":true}",
                "/api/usage/kiro-1" => "{}",
                _ when request.Method == HttpMethod.Put && path == "/api/providers/kiro-1" => "{}",
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {path}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static string ProviderList(int requestNumber) => requestNumber == 1
            ? "{\"connections\":[]}"
            : "{\"connections\":[{\"id\":\"kiro-1\",\"provider\":\"kiro\",\"name\":\"Work\",\"priority\":1,\"isActive\":true,\"email\":\"work@example.com\"}]}";
    }

    private sealed class NousDeviceCodeHandler : HttpMessageHandler
    {
        private int _providerListCount;

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add($"{request.Method} {path}");
            var body = path switch
            {
                "/api/providers" => ProviderList(++_providerListCount),
                "/api/oauth/nous/device-code" =>
                    "{\"device_code\":\"device-1\",\"user_code\":\"ABCD-EFGH\",\"verification_uri\":\"https://portal.nousresearch.com/device\",\"verification_uri_complete\":\"https://portal.nousresearch.com/device?user_code=ABCD-EFGH\",\"expires_in\":60,\"interval\":1}",
                "/api/oauth/nous/poll" => "{\"success\":true}",
                "/api/usage/nous-1" => "{}",
                _ when request.Method == HttpMethod.Put && path == "/api/providers/nous-1" => "{}",
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {path}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static string ProviderList(int requestNumber) => requestNumber == 1
            ? "{\"connections\":[]}"
            : "{\"connections\":[{\"id\":\"nous-1\",\"provider\":\"nous\",\"name\":\"Work\",\"priority\":1,\"isActive\":true,\"email\":\"work@example.com\"}]}";
    }

    private sealed class NousDeviceCodePollRetryHandler : HttpMessageHandler
    {
        private int _providerListCount;
        private int _pollCount;

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add($"{request.Method} {path}");
            var body = path switch
            {
                "/api/providers" => ProviderList(++_providerListCount),
                "/api/oauth/nous/device-code" =>
                    "{\"device_code\":\"device-1\",\"user_code\":\"ABCD-EFGH\",\"verification_uri\":\"https://portal.nousresearch.com/device\",\"verification_uri_complete\":\"https://portal.nousresearch.com/device?user_code=ABCD-EFGH\",\"expires_in\":60,\"interval\":1}",
                "/api/oauth/nous/poll" when _pollCount++ == 0 =>
                    throw new HttpRequestException("Transient 500", null, HttpStatusCode.InternalServerError),
                "/api/oauth/nous/poll" => "{\"success\":true}",
                "/api/usage/nous-1" => "{}",
                _ when request.Method == HttpMethod.Put && path == "/api/providers/nous-1" => "{}",
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {path}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static string ProviderList(int requestNumber) => requestNumber == 1
            ? "{\"connections\":[]}"
            : "{\"connections\":[{\"id\":\"nous-1\",\"provider\":\"nous\",\"name\":\"Work\",\"priority\":1,\"isActive\":true,\"email\":\"work@example.com\"}]}";
    }

    [Fact]
    public async Task OpenProvider_nous_retries_poll_on_transient_failure_and_renames_connection()
    {
        // Arrange
        var profile = CreateProfile();
        var handler = new NousDeviceCodePollRetryHandler();
        var launchedUrls = new List<string>();
        using var httpClient = new HttpClient(handler);
        var viewModel = CreateViewModel(
            profile,
            httpClient,
            launchedUrls,
            launchManagedChrome: (_, _, _, _) => throw new InvalidOperationException("synthetic automation failure"));

        // Act
        await viewModel.OpenProviderForTestAsync(ProviderKind.Nous);

        // Assert
        // Poll was retried (2 calls: first 500, second success)
        Assert.Equal(2, handler.Requests.Count(r => r.Contains("POST /api/oauth/nous/poll")));
        // PUT rename happened
        Assert.Contains("PUT /api/providers/nous-1", handler.Requests);
        // Final status is the rename status
        Assert.Equal("Đã kết nối Nous với profile Work.", viewModel.StatusText);
        Assert.False(viewModel.IsWorkflowInProgress);
    }
}
