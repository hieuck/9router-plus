using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Minimal CDP transport with loopback-only validation and allowed method filtering.
/// </summary>
public sealed class ChromeCdpClient : IAsyncDisposable
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
    {
        "Target.getTargets",
        "Target.attachToTarget",
        "Runtime.evaluate",
        "Runtime.callFunctionOn",
        "Input.dispatchKeyEvent",
        "Input.dispatchMouseEvent",
        "Input.insertText",
        "Page.bringToFront",
        "Page.navigate",
        "Page.enable",
        "Network.enable"
    };

    private readonly Uri _baseUri;
    private readonly HttpClient _httpClient;
    private ClientWebSocket? _webSocket;
    private int _nextRequestId;
    private readonly ConcurrentDictionary<int, PendingRequest> _pendingRequests = new();
    private Task? _receiveTask;

    private sealed record PendingRequest(
        string Method,
        TaskCompletionSource<JsonElement> Completion);
    private readonly CancellationTokenSource _disposalCts = new();
    private int _disposed;

    public ChromeCdpClient(Uri baseUri)
    {
        if (baseUri.Host != "127.0.0.1" && baseUri.Host != "localhost")
        {
            throw new ArgumentException("Only loopback endpoints are allowed.", nameof(baseUri));
        }

        _baseUri = baseUri;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var versionUrl = new Uri(_baseUri, "/json/version");
        var json = await _httpClient.GetStringAsync(versionUrl, cancellationToken);
        var doc = JsonDocument.Parse(json);
        var wsUrl = doc.RootElement.GetProperty("webSocketDebuggerUrl").GetString()!;

        var wsUri = new Uri(wsUrl);
        if (wsUri.Host != "127.0.0.1" && wsUri.Host != "localhost")
        {
            throw new InvalidOperationException("WebSocket URL must be loopback.");
        }

        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(wsUri, cancellationToken);

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_disposalCts.Token), CancellationToken.None);
    }

    public async Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken cancellationToken, string? sessionId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (!AllowedMethods.Contains(method))
        {
            throw new InvalidOperationException($"CDP method '{method}' is not allowed.");
        }

        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected.");
        }

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = new PendingRequest(method, tcs);

        try
        {
            var request = new Dictionary<string, object?>
            {
                ["id"] = requestId,
                ["method"] = method
            };

            if (parameters != null)
            {
                request["params"] = parameters;
            }

            if (sessionId != null)
            {
                request["sessionId"] = sessionId;
            }

            var json = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(json);

            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);

            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposalCts.Token);
            combinedCts.Token.Register(() => tcs.TrySetCanceled(cancellationToken));

            return await tcs.Task;
        }
        catch
        {
            _pendingRequests.TryRemove(requestId, out _);
            throw;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuilder = new List<byte>();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                // Accumulate fragments until EndOfMessage
                for (int i = 0; i < result.Count; i++)
                {
                    messageBuilder.Add(buffer[i]);
                }

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(messageBuilder.ToArray());
                messageBuilder.Clear();

                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
                {
                    if (_pendingRequests.TryRemove(id, out var pendingRequest))
                    {
                        if (root.TryGetProperty("error", out var errorProp))
                        {
                            var errorMessage = errorProp.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
                            var errorCode = errorProp.TryGetProperty("code", out var code) ? code.GetInt32() : 0;
                            DebugConsole.WriteLine($"[ChromeCdpClient] CDP error for {pendingRequest.Method}: code={errorCode}, message={errorMessage}");
                            pendingRequest.Completion.SetException(
                                new InvalidOperationException($"CDP method '{pendingRequest.Method}' failed: {errorMessage} (code {errorCode})"));
                        }
                        else if (root.TryGetProperty("result", out var result2))
                        {
                            pendingRequest.Completion.SetResult(result2.Clone());
                        }
                        else
                        {
                            pendingRequest.Completion.SetException(
                                new InvalidOperationException("CDP response missing result and error."));
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }
        catch
        {
            // Connection lost
        }
        finally
        {
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.Completion.TrySetException(new InvalidOperationException("CDP connection closed."));
            }
            _pendingRequests.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _disposalCts.Cancel();

        if (_webSocket != null)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
            }
            catch
            {
                // Best effort
            }

            _webSocket.Dispose();
        }

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch
            {
                // Expected
            }
        }

        _httpClient.Dispose();
        _disposalCts.Dispose();
    }
}
