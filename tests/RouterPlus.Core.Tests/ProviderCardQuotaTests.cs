using System.Net;
using System.Text;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Core.Tests;

public sealed class ProviderCardQuotaTests
{
    [Fact]
    public async Task ListAllConnections_preserves_all_decimal_quota_rows()
    {
        using var httpClient = new HttpClient(new QuotaApiHandler());
        var client = new RouterApiClient(httpClient, "http://localhost:20128");

        var connections = await client.ListConnectionsAsync(ProviderKind.Kiro);

        var connection = Assert.Single(connections);
        Assert.Equal(2, connection.QuotaRows.Count);
        Assert.Equal("credit", connection.QuotaRows[0].Name);
        Assert.Equal(49.54m, connection.QuotaRows[0].Used);
        Assert.Equal(0.46m, connection.QuotaRows[0].Remaining);
        Assert.Equal("credit_freetrial", connection.QuotaRows[1].Name);
        Assert.Equal(500m, connection.QuotaRows[1].Total);
    }

    [Fact]
    public async Task ListAllConnections_preserves_codex_session_quota()
    {
        using var httpClient = new HttpClient(new QuotaApiHandler());
        var client = new RouterApiClient(httpClient, "http://localhost:20128");

        var connections = await client.ListConnectionsAsync(ProviderKind.Codex);

        var connection = Assert.Single(connections);
        var quota = Assert.Single(connection.QuotaRows);
        Assert.Equal("session", quota.Name);
        Assert.Equal(100m, quota.Used);
        Assert.Equal(100m, quota.Total);
        Assert.Equal(0m, quota.Remaining);
    }

    [Fact]
    public void Connection_is_over_limit_when_any_quota_window_is_exhausted()
    {
        var connection = new ProviderConnection(
            "codex-1",
            ProviderKind.Codex,
            "Work",
            1,
            true,
            Quotas:
            [
                new ProviderQuota("daily", 1m, 10m, 9m, null),
                new ProviderQuota("monthly", null, null, 0m, null)
            ]);

        Assert.True(connection.IsOverLimit);
    }

    [Fact]
    public void Connection_is_over_limit_when_remaining_is_zero_without_percentage()
    {
        var connection = new ProviderConnection(
            "ollama-1",
            ProviderKind.Ollama,
            "Work",
            1,
            true,
            Quotas: [new ProviderQuota("session", null, null, 0m, null)]);

        Assert.True(connection.IsOverLimit);
    }

    [Fact]
    public void Connection_is_not_over_limit_when_all_quota_windows_have_remaining_capacity()
    {
        var connection = new ProviderConnection(
            "codex-1",
            ProviderKind.Codex,
            "Work",
            1,
            true,
            Quotas:
            [
                new ProviderQuota("daily", 1m, 10m, 9m, null),
                new ProviderQuota("monthly", 5m, 10m, 5m, null)
            ]);

        Assert.False(connection.IsOverLimit);
    }

    [Fact]
    public void Provider_card_exposes_selected_profile_quota_rows()
    {
        var card = new ProviderCardViewModel(ProviderCatalog.Get(ProviderKind.Kiro));
        var profile = new ChromeProfile(
            "profile-id",
            "Work",
            "Default",
            "C:\\Chrome",
            false);
        var row = new ProfileRowViewModel(
            profile,
            [ProviderCatalog.Get(ProviderKind.Kiro)]);
        row.UpdateConnections(
        [
            new ProviderConnection(
                "kiro-1",
                ProviderKind.Kiro,
                "Work",
                1,
                true,
                Quotas: [new ProviderQuota("credit", 49.54m, 50m, 0.46m, null)])
        ]);

        card.UpdateProviderStatus(row.ProviderStatuses[0]);

        Assert.Single(card.QuotaRows);
        Assert.Equal(49.54m, card.QuotaRows[0].Used);
    }

    [Fact]
    public void Profile_provider_status_exposes_quotas_for_matching_connections()
    {
        var profile = new ChromeProfile(
            "profile-id",
            "Work",
            "Default",
            "C:\\Chrome",
            false);
        var row = new ProfileRowViewModel(
            profile,
            [ProviderCatalog.Get(ProviderKind.Kiro)]);
        var connection = new ProviderConnection(
            "kiro-1",
            ProviderKind.Kiro,
            "Work",
            1,
            true,
            Quotas:
            [
                new ProviderQuota("credit", 49.54m, 50m, 0.46m, null),
                new ProviderQuota("credit_freetrial", 500m, 500m, 0m, null)
            ]);

        row.UpdateConnections([connection]);

        var status = Assert.Single(row.ProviderStatuses);
        var matchedConnection = Assert.Single(status.Connections);
        Assert.Equal("kiro-1", matchedConnection.Id);
        Assert.Equal(2, status.QuotaRows.Count);
        Assert.Equal(49.54m, status.QuotaRows[0].Used);
        Assert.Equal(500m, status.QuotaRows[1].Total);
    }

    [Fact]
    public void Provider_card_workflow_state_is_scoped_to_each_card()
    {
        var codexCard = new ProviderCardViewModel(ProviderCatalog.Get(ProviderKind.Codex));
        var kiroCard = new ProviderCardViewModel(ProviderCatalog.Get(ProviderKind.Kiro));

        codexCard.SetWorkflowInProgress(true);

        Assert.True(codexCard.IsWorkflowInProgress);
        Assert.False(kiroCard.IsWorkflowInProgress);
    }

    private sealed class QuotaApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.RequestUri?.AbsolutePath switch
            {
                "/api/providers" => """
                    {
                      "connections": [
                        {
                          "id": "kiro-1",
                          "provider": "kiro",
                          "name": "Work",
                          "priority": 1,
                          "isActive": true,
                          "testStatus": "active"
                        },
                        {
                          "id": "codex-1",
                          "provider": "codex",
                          "name": "Work",
                          "priority": 1,
                          "isActive": true,
                          "testStatus": "active"
                        }
                      ]
                    }
                    """,
                "/api/usage/codex-1" => """
                    {
                      "quotas": {
                        "session": {
                          "used": 100,
                          "total": 100,
                          "remaining": 0,
                          "resetAt": "2026-09-17T20:20:32.000Z"
                        }
                      }
                    }
                    """,
                "/api/usage/kiro-1" => """
                    {
                      "plan": "KIRO FREE",
                      "quotas": {
                        "credit": {
                          "used": 49.54,
                          "total": 50,
                          "remaining": 0.46,
                          "resetAt": "2026-09-01T00:00:00.000Z",
                          "unlimited": false
                        },
                        "credit_freetrial": {
                          "used": 500,
                          "total": 500,
                          "remaining": 0,
                          "resetAt": "2026-09-01T00:00:00.000Z",
                          "unlimited": false
                        }
                      }
                    }
                    """,
                _ => "{}"
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
