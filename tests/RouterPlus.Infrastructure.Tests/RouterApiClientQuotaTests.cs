using System.Net;
using System.Text;
using System.Threading;
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
        Assert.NotNull(result.Quota);
        Assert.Equal(2, result.Quota!.QuotaRows.Count);

        var requestsQuota = result.Quota!.QuotaRows.First(q => q.Name == "requests");
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
        Assert.Null(result.Quota);
        Assert.False(result.Failed);
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
        Assert.Null(result.Quota);
        Assert.False(result.Failed);
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
        Assert.Null(result.Quota);
        Assert.False(result.Failed);
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
        Assert.Null(result.Quota);
        Assert.False(result.Failed);
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
        Assert.NotNull(result.Quota);
        var quota = result.Quota!.QuotaRows.First();
        Assert.Equal(500m, quota.Used);
        Assert.Equal(1000m, quota.Total);
        Assert.Null(quota.Remaining);
        Assert.Null(quota.ResetAt);
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsFailure_WhenHttpExceptionThrown()
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
        Assert.Null(result.Quota);
        Assert.True(result.Failed);
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
        Assert.NotNull(result.Quota);
        Assert.Equal(3, result.Quota!.QuotaRows.Count);

        Assert.Contains(result.Quota!.QuotaRows, q => q.Name == "requests");
        Assert.Contains(result.Quota!.QuotaRows, q => q.Name == "tokens");
        Assert.Contains(result.Quota!.QuotaRows, q => q.Name == "credits");
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
        Assert.NotNull(result.Quota);
        var quota = result.Quota!.QuotaRows.First();
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

    private async Task<RouterApiClient.QuotaFetchResult> InvokeFetchQuotaAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        // Use reflection to call private method
        var method = typeof(RouterApiClient).GetMethod(
            "FetchQuotaAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = (Task<RouterApiClient.QuotaFetchResult>)method!.Invoke(_client, new object[] { connectionId, cancellationToken })!;
        return await task;
    }

    [Fact]
    public async Task ListAllConnectionsAsync_WithIncludeQuotasFalse_DoesNotCallUsageEndpoint()
    {
        // Arrange
        var connectionsJson = """
            {
                "connections": [
                    {
                        "id": "conn-1",
                        "provider": "codex",
                        "name": "Test Connection",
                        "priority": 1
                    },
                    {
                        "id": "conn-2",
                        "provider": "kiro",
                        "name": "Test Connection 2",
                        "priority": 2
                    }
                ]
            }
            """;

        var requestPaths = new List<string>();
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                requestPaths.Add(request.RequestUri!.AbsolutePath);
                if (request.RequestUri!.AbsolutePath == "/api/providers")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(connectionsJson, Encoding.UTF8, "application/json")
                    };
                }
                // Should not be called when includeQuotas=false
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });

        // Act
        var connections = await _client.ListAllConnectionsAsync(CancellationToken.None, includeQuotas: false);

        // Assert
        Assert.Equal(2, connections.Count);
        Assert.DoesNotContain(requestPaths, path => path.StartsWith("/api/usage/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListAllConnectionsAsync_WithIncludeQuotasTrue_CallsUsageEndpoint()
    {
        // Arrange
        var connectionsJson = """
            {
                "connections": [
                    {
                        "id": "conn-1",
                        "provider": "codex",
                        "name": "Test Connection",
                        "priority": 1
                    }
                ]
            }
            """;

        var quotaJson = """
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

        var requestPaths = new List<string>();
        var responseQueue = new Queue<(HttpStatusCode, string)>();
        responseQueue.Enqueue((HttpStatusCode.OK, connectionsJson));
        responseQueue.Enqueue((HttpStatusCode.OK, quotaJson));

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                requestPaths.Add(request.RequestUri!.AbsolutePath);
                var response = responseQueue.Dequeue();
                return new HttpResponseMessage(response.Item1)
                {
                    Content = new StringContent(response.Item2, Encoding.UTF8, "application/json")
                };
            });

        // Act
        var connections = await _client.ListAllConnectionsAsync(CancellationToken.None, includeQuotas: true);

        // Assert
        Assert.Single(connections);
        Assert.Contains(requestPaths, path => path == "/api/usage/conn-1");
    }

    [Fact]
    public async Task FetchAllQuotasAsync_ConcurrencyIsBoundedTo64()
    {
        // Arrange - use a custom handler that supports true async concurrency
        var connectionCount = 128;
        var connectionElements = Enumerable.Range(1, connectionCount)
            .Select(i => $"{{\"id\":\"conn-{i}\",\"provider\":\"codex\",\"name\":\"Test {i}\",\"priority\":1}}");
        var connectionsJson = $$"""
            {
                "connections": [
                    {{string.Join(",", connectionElements)}}
                ]
            }
            """;

        var quotaJson = """
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

        var requestStarted = new SemaphoreSlim(0, connectionCount);
        var allowCompletion = new SemaphoreSlim(0, connectionCount);
        int peakConcurrency = 0;
        object peakLock = new();

        var handler = new ConcurrencyTrackingHandler(
            connectionsJson,
            quotaJson,
            requestStarted,
            allowCompletion,
            peak => { lock (peakLock) { if (peak > peakConcurrency) peakConcurrency = peak; } });

        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://api.example.com");

        // Act
        var listTask = client.ListAllConnectionsAsync(CancellationToken.None, includeQuotas: true);

        // Wait for the first 64 requests to start (fills the cap) without releasing completions.
        for (var i = 0; i < 64; i++)
        {
            await requestStarted.WaitAsync(TimeSpan.FromSeconds(10));
        }

        // Release completions one at a time to let the remaining 64 start, keeping peak at cap.
        for (var i = 64; i < connectionCount; i++)
        {
            allowCompletion.Release();
            await requestStarted.WaitAsync(TimeSpan.FromSeconds(10));
        }

        // Release the remaining in-flight completions so the run can finish.
        for (var i = 0; i < 64; i++)
        {
            allowCompletion.Release();
        }

        var connections = await listTask;

        // Assert
        Assert.Equal(connectionCount, connections.Count);
        // Concurrency is no longer capped at 16 and is bounded at 64
        Assert.True(peakConcurrency > 16, $"Expected concurrency > 16, got {peakConcurrency}");
        Assert.True(peakConcurrency <= 64, $"Expected concurrency <= 64, got {peakConcurrency}");
    }

    private sealed class ConcurrencyTrackingHandler(
        string connectionsJson,
        string quotaJson,
        SemaphoreSlim requestStarted,
        SemaphoreSlim allowCompletion,
        Action<int> updatePeak) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/providers")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(connectionsJson, Encoding.UTF8, "application/json")
                };
            }

            // Track concurrency - use Interlocked for thread-safe increments
            var current = Interlocked.Increment(ref _concurrentCount);
            updatePeak(current);

            // Signal that this request has started
            requestStarted.Release();

            try
            {
                // Wait for test to allow completion (this allows other requests to start concurrently)
                await allowCompletion.WaitAsync(cancellationToken);
            }
            finally
            {
                // Always decrement in finally so cancellation cannot make counter negative
                Interlocked.Decrement(ref _concurrentCount);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(quotaJson, Encoding.UTF8, "application/json")
            };
        }

        private int _concurrentCount;
    }

    [Fact]
    public async Task FetchAllQuotasAsync_RealFailureIsCounted_404AndEmptyAreNot()
    {
        // Arrange - 5xx = failure, 404/empty/message = normal no-data
        var connectionsJson = """
            {
                "connections": [
                    {"id":"conn-500","provider":"codex","name":"Test 500","priority":1},
                    {"id":"conn-404","provider":"codex","name":"Test 404","priority":2},
                    {"id":"conn-empty","provider":"codex","name":"Test Empty","priority":3},
                    {"id":"conn-message","provider":"codex","name":"Test Message","priority":4},
                    {"id":"conn-success","provider":"codex","name":"Test Success","priority":5}
                ]
            }
            """;

        var quotaSuccessJson = """
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

        var quotaEmptyJson = """
            {
                "quotas": {}
            }
            """;

        var quotaMessageJson = """
            {
                "message": "Connection not found"
            }
            """;

        var requestCount = 0;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/api/providers")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(connectionsJson, Encoding.UTF8, "application/json")
                    };
                }

                requestCount++;
                // conn-500: 500 = real failure
                if (path == "/api/usage/conn-500")
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }
                // conn-404: 404 = normal no-data
                if (path == "/api/usage/conn-404")
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }
                // conn-empty: 200 with empty quotas = normal no-data
                if (path == "/api/usage/conn-empty")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(quotaEmptyJson, Encoding.UTF8, "application/json")
                    };
                }
                // conn-message: 200 with message = normal no-data
                if (path == "/api/usage/conn-message")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(quotaMessageJson, Encoding.UTF8, "application/json")
                    };
                }
                // conn-success: 200 with quota data = success
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(quotaSuccessJson, Encoding.UTF8, "application/json")
                };
            });

        // Act - should not throw
        var connections = await _client.ListAllConnectionsAsync(CancellationToken.None, includeQuotas: true);

        // Assert
        Assert.Equal(5, connections.Count);
        
        // Only conn-500 should be a failure; others should have quota data or be normal no-data
        var conn500 = connections.First(c => c.Id == "conn-500");
        var conn404 = connections.First(c => c.Id == "conn-404");
        var connEmpty = connections.First(c => c.Id == "conn-empty");
        var connMessage = connections.First(c => c.Id == "conn-message");
        var connSuccess = connections.First(c => c.Id == "conn-success");

        // conn-500: 5xx failure -> no quota data
        Assert.Empty(conn500.QuotaRows);
        
        // conn-404: 404 -> normal no-data, no quota data
        Assert.Empty(conn404.QuotaRows);
        
        // conn-empty: empty quotas -> normal no-data, no quota data
        Assert.Empty(connEmpty.QuotaRows);
        
        // conn-message: message property -> normal no-data, no quota data
        Assert.Empty(connMessage.QuotaRows);
        
        // conn-success: has quota data
        Assert.NotEmpty(connSuccess.QuotaRows);
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsNoData_When404()
    {
        // Arrange
        var connectionId = "conn-123";
        SetupHttpResponse(HttpStatusCode.NotFound, "{}");

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert - 404 is normal no-data, not a failure
        Assert.Null(result.Quota);
        Assert.False(result.Failed); // Should not be failed
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsNoData_WhenEmptyQuotas()
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

        // Assert - empty quotas is normal no-data
        Assert.Null(result.Quota);
        Assert.False(result.Failed);
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsNoData_WhenMessageProperty()
    {
        // Arrange
        var connectionId = "conn-123";
        var json = """
            {
                "message": "Connection not found"
            }
            """;
        SetupHttpResponse(HttpStatusCode.OK, json);

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert - message property is normal no-data
        Assert.Null(result.Quota);
        Assert.False(result.Failed);
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsFailure_When5xx()
    {
        // Arrange
        var connectionId = "conn-123";
        SetupHttpResponse(HttpStatusCode.InternalServerError, "{}");

        // Act
        var result = await InvokeFetchQuotaAsync(connectionId);

        // Assert - 5xx is a real failure
        Assert.Null(result.Quota);
        Assert.True(result.Failed);
    }

    [Fact]
    public async Task FetchQuotaAsync_ReturnsFailure_WhenTransportException()
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

        // Assert - transport exception is a real failure
        Assert.Null(result.Quota);
        Assert.True(result.Failed);
    }

    [Fact]
    public async Task WaitForNewConnectionAsync_PollingUsesIncludeQuotasFalse()
    {
        // Arrange - fake handler returns a new connection on first poll
        var connectionsJsonFirstPoll = """
            {
                "connections": [
                    {"id":"conn-old","provider":"codex","name":"Old","priority":1},
                    {"id":"conn-new","provider":"codex","name":"Test Profile","priority":2}
                ]
            }
            """;

        var requestPaths = new List<string>();
        var callCount = 0;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                callCount++;
                requestPaths.Add(request.RequestUri!.AbsolutePath);
                
                if (request.RequestUri!.AbsolutePath == "/api/providers")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(connectionsJsonFirstPoll, Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            });

        var existingConnections = new Dictionary<string, ProviderConnection>
        {
            ["conn-old"] = new ProviderConnection("conn-old", ProviderKind.Codex, "Old", 1, true, null, null, null, null, null, null, null, null, null, null, null, null, Array.Empty<ProviderQuota>())
        };

        var api = new RouterApiClient(_httpClient, "https://api.example.com");

        // Act - should find the new connection on first poll (2s timeout, 1s poll interval)
        var connection = await api.WaitForNewConnectionAsync(
            ProviderKind.Codex,
            existingConnections,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        // Assert
        Assert.Equal("conn-new", connection.Id);
        Assert.Equal("Test Profile", connection.Name);
        
        // Verify no /api/usage calls were made during polling
        Assert.DoesNotContain(requestPaths, path => path.StartsWith("/api/usage/", StringComparison.Ordinal));
    }
}
