using System.Net;
using System.Text;
using Moq;
using Moq.Protected;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Infrastructure.Tests;

public class RouterApiClientQuotaTests
{
    private readonly Mock<HttpMessageHandler> _httpHandler;
    private readonly HttpClient _httpClient;
    private readonly RouterApiClient _client;

    public RouterApiClientQuotaTests()
    {
        _httpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandler.Object);
        _client = new RouterApiClient(_httpClient, "https://api.example.com");
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsQuotaData_WhenApiReturnsValidData()
    {
        // Arrange
        var connectionId = "conn-123";
        var quotaJson = """
            {
                "quotas": {
                    "requests": {
                        "used": 750,
                        "total": 1000,
                        "remaining": 250,
                        "resetAt": "2026-10-01T00:00:00Z"
                    },
                    "tokens": {
                        "used": 50000,
                        "total": 100000,
                        "remaining": 50000,
                        "resetAt": "2026-10-01T00:00:00Z"
                    }
                }
            }
            """;

        SetupHttpResponse(HttpStatusCode.OK, quotaJson);

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.QuotaRows.Count);

        var requestsQuota = result.QuotaRows.First(q => q.Name == "requests");
        Assert.Equal(750m, requestsQuota.Used);
        Assert.Equal(1000m, requestsQuota.Total);
        Assert.Equal(250m, requestsQuota.Remaining);
        Assert.NotNull(requestsQuota.ResetAt);
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsNull_WhenApiReturnsErrorMessage()
    {
        // Arrange
        var connectionId = "conn-123";
        var errorJson = """
            {
                "message": "Connection not found"
            }
            """;

        SetupHttpResponse(HttpStatusCode.OK, errorJson);

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsNull_When404NotFound()
    {
        // Arrange
        var connectionId = "conn-123";
        SetupHttpResponse(HttpStatusCode.NotFound, "{}");

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsNull_WhenNoQuotasProperty()
    {
        // Arrange
        var connectionId = "conn-123";
        var json = """
            {
                "data": {}
            }
            """;

        SetupHttpResponse(HttpStatusCode.OK, json);

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsNull_WhenQuotasIsEmpty()
    {
        // Arrange
        var connectionId = "conn-123";
        var json = """
            {
                "quotas": {}
            }
            """;

        SetupHttpResponse(HttpStatusCode.OK, json);

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchQuotaAsync_HandlesPartialQuotaData()
    {
        // Arrange
        var connectionId = "conn-123";
        var quotaJson = """
            {
                "quotas": {
                    "requests": {
                        "used": 500,
                        "total": 1000
                    }
                }
            }
            """;

        SetupHttpResponse(HttpStatusCode.OK, quotaJson);

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert
        Assert.NotNull(result);
        var quota = result.QuotaRows.First();
        Assert.Equal(500m, quota.Used);
        Assert.Equal(1000m, quota.Total);
        Assert.Null(quota.Remaining);
        Assert.Null(quota.ResetAt);
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsNull_WhenHttpExceptionThrown()
    {
        // Arrange
        var connectionId = "conn-123";
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchAllQuotasAsync_FetchesQuotaForAllConnections()
    {
        // Arrange
        var connectionsJson = """
            {
                "connections": [
                    {
                        "id": "conn-1",
                        "provider": "codex",
                        "name": "Connection 1",
                        "priority": 1
                    },
                    {
                        "id": "conn-2",
                        "provider": "kiro",
                        "name": "Connection 2",
                        "priority": 2
                    }
                ]
            }
            """;

        var quota1Json = """
            {
                "quotas": {
                    "requests": {
                        "used": 100,
                        "total": 1000,
                        "remaining": 900
                    }
                }
            }
            """;

        var quota2Json = """
            {
                "quotas": {
                    "requests": {
                        "used": 500,
                        "total": 1000,
                        "remaining": 500
                    }
                }
            }
            """;

        SetupMultipleHttpResponses(new[]
        {
            (HttpStatusCode.OK, connectionsJson, "api/providers"),
            (HttpStatusCode.OK, quota1Json, "api/usage/conn-1"),
            (HttpStatusCode.OK, quota2Json, "api/usage/conn-2")
        });

        // Act
        var connections = await _client.ListAllConnectionsAsync();

        // Assert
        Assert.Equal(2, connections.Count);

        var conn1 = connections.First(c => c.Id == "conn-1");
        Assert.Single(conn1.QuotaRows);
        Assert.Equal(100m, conn1.QuotaRows[0].Used);

        var conn2 = connections.First(c => c.Id == "conn-2");
        Assert.Single(conn2.QuotaRows);
        Assert.Equal(500m, conn2.QuotaRows[0].Used);
    }

    [Fact]
    public async Task ListAllConnectionsAsync_IncludesQuotaData_WhenAvailable()
    {
        // Arrange
        var connectionsJson = """
            {
                "connections": [
                    {
                        "id": "conn-1",
                        "provider": "codex",
                        "name": "Test Connection",
                        "priority": 1,
                        "isActive": true
                    }
                ]
            }
            """;

        var quotaJson = """
            {
                "quotas": {
                    "requests": {
                        "used": 800,
                        "total": 1000,
                        "remaining": 200,
                        "resetAt": "2026-10-01T00:00:00Z"
                    }
                }
            }
            """;

        SetupMultipleHttpResponses(new[]
        {
            (HttpStatusCode.OK, connectionsJson, "api/providers"),
            (HttpStatusCode.OK, quotaJson, "api/usage/conn-1")
        });

        // Act
        var connections = await _client.ListAllConnectionsAsync();

        // Assert
        var connection = connections.Single();
        Assert.True(connection.HasUsageData);
        Assert.True(connection.IsNearLimit); // 80% usage
        Assert.False(connection.IsOverLimit);
        Assert.Single(connection.QuotaRows);
    }

    [Fact]
    public async Task ListAllConnectionsAsync_FallsBackToInference_WhenQuotaUnavailable()
    {
        // Arrange
        var connectionsJson = """
            {
                "connections": [
                    {
                        "id": "conn-1",
                        "provider": "codex",
                        "name": "Test Connection",
                        "priority": 1,
                        "errorCode": "429",
                        "lastError": "[429]: The usage limit has been reached",
                        "lastErrorAt": "2026-09-06T06:00:00Z"
                    }
                ]
            }
            """;

        SetupMultipleHttpResponses(new[]
        {
            (HttpStatusCode.OK, connectionsJson, "api/providers"),
            (HttpStatusCode.NotFound, "{}", "api/usage/conn-1")
        });

        // Act
        var connections = await _client.ListAllConnectionsAsync();

        // Assert
        var connection = connections.Single();
        Assert.True(connection.HasUsageData);
        Assert.Equal(100, connection.UsageCount);
        Assert.Equal(100, connection.LimitCount);
        Assert.NotNull(connection.UsageResetAt);
    }

    [Fact]
    public async Task FetchQuotaAsync_HandlesMultipleQuotaTypes()
    {
        // Arrange
        var connectionId = "conn-123";
        var quotaJson = """
            {
                "quotas": {
                    "requests": {
                        "used": 750,
                        "total": 1000,
                        "remaining": 250
                    },
                    "tokens": {
                        "used": 45000,
                        "total": 50000,
                        "remaining": 5000
                    },
                    "credits": {
                        "used": 9.50,
                        "total": 10.00,
                        "remaining": 0.50
                    }
                }
            }
            """;

        SetupHttpResponse(HttpStatusCode.OK, quotaJson);

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.QuotaRows.Count);

        Assert.Contains(result.QuotaRows, q => q.Name == "requests");
        Assert.Contains(result.QuotaRows, q => q.Name == "tokens");
        Assert.Contains(result.QuotaRows, q => q.Name == "credits");
    }

    [Fact]
    public async Task FetchQuotaAsync_ParsesResetAtCorrectly()
    {
        // Arrange
        var connectionId = "conn-123";
        var quotaJson = """
            {
                "quotas": {
                    "requests": {
                        "used": 500,
                        "total": 1000,
                        "remaining": 500,
                        "resetAt": "2026-10-15T12:30:45Z"
                    }
                }
            }
            """;

        SetupHttpResponse(HttpStatusCode.OK, quotaJson);

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert
        Assert.NotNull(result);
        var quota = result.QuotaRows.First();
        Assert.NotNull(quota.ResetAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 12, 30, 45, TimeSpan.Zero), quota.ResetAt.Value);
    }

    [Fact]
    public async Task FetchQuotaAsync_ThrowsOperationCanceledException_WhenCancelled()
    {
        // Arrange
        var connectionId = "conn-123";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => InvokeFetchQuotaAsync(connectionId, cts.Token));
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
    }

    private void SetupMultipleHttpResponses(IEnumerable<(HttpStatusCode StatusCode, string Content, string Path)> responses)
    {
        var responseQueue = new Queue<(HttpStatusCode, string, string)>(responses);

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var response = responseQueue.Dequeue();
                return new HttpResponseMessage
                {
                    StatusCode = response.Item1,
                    Content = new StringContent(response.Item2, Encoding.UTF8, "application/json")
                };
            });
    }

    private async Task<RouterApiClient.QuotaData?> InvokeFetchQuotaAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        // Use reflection to call private method
        var method = typeof(RouterApiClient).GetMethod(
            "FetchQuotaAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = (Task<RouterApiClient.QuotaData?>)method!.Invoke(_client, new object[] { connectionId, cancellationToken })!;
        return await task;
    }
}
