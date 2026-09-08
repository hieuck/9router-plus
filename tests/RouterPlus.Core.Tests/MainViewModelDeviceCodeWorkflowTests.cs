using System.Net;
using System.Text;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelDeviceCodeWorkflowTests
{
    [Fact]
    public async Task OpenProvider_kiro_device_code_success_renames_and_refreshes_connection()
    {
        // Arrange
        var profile = Profile();
        var handler = new DeviceCodeHandler(terminalError: null);
        var launchedUrls = new List<string>();
        using var httpClient = new HttpClient(handler);
        var viewModel = new MainViewModel(
            httpClient: httpClient,
            launchUrl: (_, url) =>
            {
                launchedUrls.Add(url);
                return Task.CompletedTask;
            })
        {
            DashboardBaseUrl = "http://router.test"
        };
        viewModel.Profiles.Add(profile);
        viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
        viewModel.SelectedProfile = profile;

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
    public async Task OpenProvider_kiro_device_code_terminal_error_stops_workflow_without_renaming()
    {
        // Arrange
        var profile = Profile();
        var handler = new DeviceCodeHandler(terminalError: "access_denied");
        var launchedUrls = new List<string>();
        using var httpClient = new HttpClient(handler);
        var viewModel = new MainViewModel(
            httpClient: httpClient,
            launchUrl: (_, url) =>
            {
                launchedUrls.Add(url);
                return Task.CompletedTask;
            });
        viewModel.Profiles.Add(profile);
        viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
        viewModel.SelectedProfile = profile;

        // Act
        await viewModel.OpenProviderForTestAsync(ProviderKind.Kiro);

        // Assert
        Assert.Single(launchedUrls);
        Assert.Contains("POST /api/oauth/kiro/poll", handler.Requests);
        Assert.DoesNotContain("PUT /api/providers/kiro-1", handler.Requests);
        Assert.False(viewModel.IsWorkflowInProgress);
        Assert.Equal("Thao tác thất bại. Kiểm tra cài đặt rồi thử lại.", viewModel.StatusText);
    }

    private static ChromeProfile Profile() => new(
        "profile-id",
        "Work",
        "Default",
        Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N")),
        IsDefault: true);

    private sealed class DeviceCodeHandler(string? terminalError) : HttpMessageHandler
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
                "/api/oauth/kiro/poll" => terminalError is null
                    ? "{\"success\":true}"
                    : $"{{\"success\":false,\"error\":\"{terminalError}\",\"errorDescription\":\"User denied authorization\"}}",
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
}
