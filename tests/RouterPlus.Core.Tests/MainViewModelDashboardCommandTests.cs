using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Storage;
using System.Net;
using System.Text;
using System.Text.Json;

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

    [Theory]
    [InlineData(ProviderKind.Codex, "codex-1")]
    [InlineData(ProviderKind.Ollama, "ollama-1")]
    [InlineData(ProviderKind.Kiro, "kiro-1")]
    public async Task Refresh_statuses_disables_active_connection_when_quota_is_exhausted(
        ProviderKind provider,
        string connectionId)
    {
        var handler = new ExhaustedQuotaHandler(provider, connectionId);
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

        await viewModel.RefreshConnectionStatusesAsync();

        Assert.Contains(handler.DisableRequests, id => id == connectionId);
        Assert.Equal(
            ProviderHealthState.Disabled,
            viewModel.ProviderCards.Single(card => card.Kind == provider).HealthState);
    }

    [Fact]
    public async Task Refresh_statuses_suggests_reenable_after_marked_quota_reset()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusQuotaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            await new SettingsStore(settingsPath).SaveAsync(new RouterSettings(
                QuotaAutoDisableMarkers:
                [
                    new QuotaAutoDisableMarker(
                        "codex-1",
                        ProviderKind.Codex,
                        "Work",
                        DateTimeOffset.UtcNow.AddMinutes(-1))
                ]));
            var handler = new ReenableSuggestionHandler();
            using var httpClient = new HttpClient(handler);
            var viewModel = new MainViewModel(new SettingsStore(settingsPath), httpClient: httpClient)
            {
                DashboardBaseUrl = "http://router.test"
            };
            var profile = new ChromeProfile("profile-id", "Work", "Default", Path.Combine(directory, "profile"), true);
            viewModel.Profiles.Add(profile);
            viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
            viewModel.SelectedProfile = profile;
            await viewModel.InitializeQuotaAutoDisableMarkersForTestAsync();

            await viewModel.RefreshConnectionStatusesAsync();

            var suggestion = Assert.Single(viewModel.QuotaResetSuggestions);
            Assert.Equal("codex-1", suggestion.ConnectionId);
            Assert.Empty(handler.EnableRequests);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Confirmed_reenable_enables_connection_and_removes_marker()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusQuotaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var marker = new QuotaAutoDisableMarker(
                "codex-1",
                ProviderKind.Codex,
                "Work",
                DateTimeOffset.UtcNow.AddMinutes(-1));
            await new SettingsStore(settingsPath).SaveAsync(new RouterSettings(QuotaAutoDisableMarkers: [marker]));
            var handler = new ReenableSuggestionHandler();
            using var httpClient = new HttpClient(handler);
            var viewModel = new MainViewModel(new SettingsStore(settingsPath), httpClient: httpClient)
            {
                DashboardBaseUrl = "http://router.test"
            };
            var profile = new ChromeProfile("profile-id", "Work", "Default", Path.Combine(directory, "profile"), true);
            viewModel.Profiles.Add(profile);
            viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
            viewModel.SelectedProfile = profile;
            await viewModel.InitializeQuotaAutoDisableMarkersForTestAsync();
            await viewModel.RefreshConnectionStatusesAsync();

            await viewModel.ReenableQuotaConnectionAsync("codex-1", confirmedByUser: true);

            Assert.Contains("codex-1", handler.EnableRequests);
            Assert.Equal(
                ProviderHealthState.Healthy,
                viewModel.ProviderCards.Single(card => card.Kind == ProviderKind.Codex).HealthState);
            Assert.Empty(viewModel.QuotaResetSuggestions);
            Assert.Empty(new SettingsStore(settingsPath).Load().QuotaAutoDisableMarkers!);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Unconfirmed_reenable_does_not_enable_connection()
    {
        var handler = new ReenableSuggestionHandler();
        using var httpClient = new HttpClient(handler);
        var viewModel = new MainViewModel(httpClient: httpClient)
        {
            DashboardBaseUrl = "http://router.test"
        };

        await viewModel.ReenableQuotaConnectionAsync("codex-1", confirmedByUser: false);

        Assert.Empty(handler.EnableRequests);
    }

    [Fact]
    public async Task Reenable_rejects_stale_marker_when_quota_is_exhausted_again()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusQuotaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            await new SettingsStore(settingsPath).SaveAsync(new RouterSettings(
                QuotaAutoDisableMarkers:
                [new QuotaAutoDisableMarker("codex-1", ProviderKind.Codex, "Work", DateTimeOffset.UtcNow.AddMinutes(-1))]));
            var handler = new ExhaustedReenableSuggestionHandler();
            using var httpClient = new HttpClient(handler);
            var viewModel = new MainViewModel(new SettingsStore(settingsPath), httpClient: httpClient)
            {
                DashboardBaseUrl = "http://router.test"
            };
            await viewModel.InitializeQuotaAutoDisableMarkersForTestAsync();

            var enabled = await viewModel.ReenableQuotaConnectionAsync("codex-1", confirmedByUser: true);

            Assert.False(enabled);
            Assert.Empty(handler.EnableRequests);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Auto_disable_persists_marker_when_follow_up_refresh_fails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusQuotaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var handler = new FollowUpRefreshFailureHandler();
            using var httpClient = new HttpClient(handler);
            var viewModel = new MainViewModel(new SettingsStore(settingsPath), httpClient: httpClient)
            {
                DashboardBaseUrl = "http://router.test"
            };
            var profile = new ChromeProfile("profile-id", "Work", "Default", Path.Combine(directory, "profile"), true);
            viewModel.Profiles.Add(profile);
            viewModel.ProfileRows.Add(new ProfileRowViewModel(profile, viewModel.Providers));
            viewModel.SelectedProfile = profile;

            await viewModel.RefreshConnectionStatusesAsync();

            var marker = Assert.Single(new SettingsStore(settingsPath).Load().QuotaAutoDisableMarkers!);
            Assert.Equal("codex-1", marker.ConnectionId);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Refresh_statuses_propagates_cancellation_during_quota_fetch()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = new HttpClient(new RefreshCancellationHandler(cancellation));
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => viewModel.RefreshConnectionStatusesAsync(cancellation.Token));
    }

    [Fact]
    public async Task Refresh_statuses_continues_disabling_after_one_connection_update_fails()
    {
        var handler = new PartialDisableFailureHandler();
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

        await viewModel.RefreshConnectionStatusesAsync();

        Assert.Contains("codex-1", handler.DisableRequests);
        Assert.Contains("codex-2", handler.DisableRequests);
        Assert.NotEqual(ProviderHealthState.Unknown, viewModel.ProviderCards.Single(card => card.Kind == ProviderKind.Codex).HealthState);
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

        Assert.True(handler.Requests.Count >= 2, $"Expected at least 2 requests, got {handler.Requests.Count}");
    }

    private sealed class ReenableSuggestionHandler : HttpMessageHandler
    {
        private bool _enabled;

        public List<string> EnableRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path == "/api/providers/codex-1")
            {
                EnableRequests.Add("codex-1");
                _enabled = true;
                return Task.FromResult(Json(HttpStatusCode.OK, "{}"));
            }

            if (request.Method == HttpMethod.Get && path == "/api/providers")
            {
                var active = _enabled.ToString().ToLowerInvariant();
                return Task.FromResult(Json(HttpStatusCode.OK, $"{{\"connections\":[{{\"id\":\"codex-1\",\"provider\":\"codex\",\"name\":\"Work\",\"priority\":1,\"isActive\":{active},\"testStatus\":\"active\"}}]}}"));
            }

            if (request.Method == HttpMethod.Get && path == "/api/usage/codex-1")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"quotas\":{\"session\":{\"used\":0,\"total\":100,\"remaining\":100,\"resetAt\":\"2026-10-01T00:00:00Z\"}}}"));
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class ExhaustedReenableSuggestionHandler : HttpMessageHandler
    {
        public List<string> EnableRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                EnableRequests.Add(path);
                return Task.FromResult(Json(HttpStatusCode.OK, "{}"));
            }

            if (request.Method == HttpMethod.Get && path == "/api/providers")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"connections\":[{\"id\":\"codex-1\",\"provider\":\"codex\",\"name\":\"Work\",\"priority\":1,\"isActive\":true,\"testStatus\":\"active\"}]}"));
            }

            if (request.Method == HttpMethod.Get && path == "/api/usage/codex-1")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"quotas\":{\"session\":{\"used\":100,\"total\":100,\"remaining\":0,\"resetAt\":\"2026-10-01T00:00:00Z\"}}}"));
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class FollowUpRefreshFailureHandler : HttpMessageHandler
    {
        private int _providerRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path == "/api/providers/codex-1")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{}"));
            }

            if (request.Method == HttpMethod.Get && path == "/api/providers")
            {
                _providerRequests++;
                if (_providerRequests > 1)
                {
                    return Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}"));
                }

                return Task.FromResult(Json(HttpStatusCode.OK, "{\"connections\":[{\"id\":\"codex-1\",\"provider\":\"codex\",\"name\":\"Work\",\"priority\":1,\"isActive\":true,\"testStatus\":\"active\"}]}"));
            }

            if (request.Method == HttpMethod.Get && path == "/api/usage/codex-1")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"quotas\":{\"session\":{\"used\":100,\"total\":100,\"remaining\":0,\"resetAt\":\"2026-10-01T00:00:00Z\"}}}"));
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class PartialDisableFailureHandler : HttpMessageHandler
    {
        private readonly HashSet<string> _disabled = new(StringComparer.Ordinal);

        public List<string> DisableRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.StartsWith("/api/providers/", StringComparison.Ordinal))
            {
                var id = path["/api/providers/".Length..];
                DisableRequests.Add(id);
                if (id == "codex-1")
                {
                    return Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}"));
                }

                _disabled.Add(id);
                return Task.FromResult(Json(HttpStatusCode.OK, "{}"));
            }

            if (request.Method == HttpMethod.Get && path == "/api/providers")
            {
                var active1 = !_disabled.Contains("codex-1");
                var active2 = !_disabled.Contains("codex-2");
                return Task.FromResult(Json(HttpStatusCode.OK, $$"""
                    {"connections":[
                      {"id":"codex-1","provider":"codex","name":"Work","priority":1,"isActive":{{active1.ToString().ToLowerInvariant()}},"testStatus":"active"},
                      {"id":"codex-2","provider":"codex","name":"Work","priority":2,"isActive":{{active2.ToString().ToLowerInvariant()}},"testStatus":"active"}
                    ]}
                    """));
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/api/usage/", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"quotas\":{\"session\":{\"used\":100,\"total\":100,\"remaining\":0,\"resetAt\":\"2026-09-01T00:00:00Z\"}}}"));
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RefreshCancellationHandler(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/api/providers")
            {
                return Json(HttpStatusCode.OK, "{\"connections\":[{\"id\":\"codex-1\",\"provider\":\"codex\",\"name\":\"Work\",\"priority\":1,\"isActive\":true}]}");
            }

            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation was not propagated.");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class ExhaustedQuotaHandler(ProviderKind provider, string connectionId) : HttpMessageHandler
    {
        private bool _disabled;

        public List<string> DisableRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path == $"/api/providers/{connectionId}")
            {
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (!document.RootElement.GetProperty("isActive").GetBoolean())
                {
                    _disabled = true;
                    DisableRequests.Add(connectionId);
                }

                return Json(HttpStatusCode.OK, "{}");
            }

            if (request.Method == HttpMethod.Get && path == "/api/providers")
            {
                var active = !_disabled;
                return Json(HttpStatusCode.OK, $$"""
                    {"connections":[{"id":"{{connectionId}}","provider":"{{provider.ToString().ToLowerInvariant()}}","name":"Work","priority":1,"isActive":{{active.ToString().ToLowerInvariant()}},"testStatus":"active"}]}
                    """);
            }

            if (request.Method == HttpMethod.Get && path == $"/api/usage/{connectionId}")
            {
                return Json(HttpStatusCode.OK, "{\"quotas\":{\"session\":{\"used\":100,\"total\":100,\"remaining\":0,\"resetAt\":\"2026-09-01T00:00:00Z\"}}}");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
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
