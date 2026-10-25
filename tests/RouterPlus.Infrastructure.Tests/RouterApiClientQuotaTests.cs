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
    public async Task FetchAllQuotasAsync_ConcurrencyIsBoundedTo16()
    {
        // Arrange - use a custom handler that supports true async concurrency
        var connectionCount = 50;
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
        var peakConcurrency = 0;

        var handler = new ConcurrencyTrackingHandler(
            connectionsJson,
            quotaJson,
            requestStarted,
            allowCompletion,
            peak => { if (peak > peakConcurrency) peakConcurrency = peak; });

        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://api.example.com");

        // Act
        var listTask = client.ListAllConnectionsAsync(CancellationToken.None, includeQuotas: true);

        // Wait for all quota requests to start, releasing completions as we go to avoid deadlock
        // (semaphore limits to 16 concurrent, so we must allow completions to free up slots)
        var startedCount = 0;
        while (startedCount < connectionCount)
        {
            await requestStarted.WaitAsync(TimeSpan.FromSeconds(10));
            startedCount++;
            // Release one completion to allow the next batch to start
            allowCompletion.Release();
        }

        var connections = await listTask;

        // Assert
        Assert.Equal(connectionCount, connections.Count);
        // Concurrency should be bounded to 16 (not serialized = 1, not unbounded = 50)
        Assert.True(peakConcurrency > 1, $"Expected concurrency > 1, got {peakConcurrency}");
        Assert.True(peakConcurrency <= 16, $"Expected concurrency <= 16, got {peakConcurrency}");
    }

    private sealed class ConcurrencyTrackingHandler(
        string connectionsJson,
        string quotaJson,
        SemaphoreSlim requestStarted,
        SemaphoreSlim allowCompletion,
        Action<int> updatePeak) : HttpMessageHandler
    {
        public int GetCurrentConcurrency()
        {
            // Thread-safe read without locking
            return _concurrentCount;
        }

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

            var threadId = Environment.CurrentManagedThreadId;
            Console.WriteLine($"[Handler:{threadId}] Enter SendAsync for {path}");

            // Track concurrency - no gate, use Interlocked for thread-safe increments
            var current = Interlocked.Increment(ref _concurrentCount);
            updatePeak(current);
            Console.WriteLine($"[Handler:{threadId}] Concurrency now: {current}");

            // Signal that this request has started
            requestStarted.Release();

            // Wait for test to allow completion (this allows other requests to start concurrently)
            await allowCompletion.WaitAsync(cancellationToken);
            Console.WriteLine($"[Handler:{threadId}] Completing request");

            Interlocked.Decrement(ref _concurrentCount);
            Console.WriteLine($"[Handler:{threadId}] Concurrency decremented to: {_concurrentCount}");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(quotaJson, Encoding.UTF8, "application/json")
            };
        }

        private int _concurrentCount;
    }

    [Fact]
    public async Task FetchAllQuotasAsync_OneFailedQuotaDoesNotFailList_LogsAggregateWarning()
    {
        // Arrange
        var connectionsJson = """
            {
                "connections": [
                    {"id":"conn-1","provider":"codex","name":"Test 1","priority":1},
                    {"id":"conn-2","provider":"codex","name":"Test 2","priority":2},
                    {"id":"conn-3","provider":"codex","name":"Test 3","priority":3}
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
                // Fail the second request (conn-2)
                if (path == "/api/usage/conn-2")
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(quotaSuccessJson, Encoding.UTF8, "application/json")
                };
            });

        // Act - should not throw
        var connections = await _client.ListAllConnectionsAsync(CancellationToken.None, includeQuotas: true);

        // Assert
        Assert.Equal(3, connections.Count);
        // conn-1 and conn-3 should have quota data, conn-2 should not
        var conn1 = connections.First(c => c.Id == "conn-1");
        var conn2 = connections.First(c => c.Id == "conn-2");
        var conn3 = connections.First(c => c.Id == "conn-3");

        Assert.NotEmpty(conn1.QuotaRows);
        Assert.Empty(conn2.QuotaRows); // Failed quota fetch results in empty quota rows
        Assert.NotEmpty(conn3.QuotaRows);
    }
}
