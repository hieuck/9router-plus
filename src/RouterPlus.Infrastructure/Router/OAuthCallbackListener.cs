using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RouterPlus.Infrastructure.Router;

public sealed class OAuthCallbackListener : IAsyncDisposable
{
    private readonly TcpListener _listener;

    private OAuthCallbackListener(TcpListener listener, Uri redirectUri)
    {
        _listener = listener;
        RedirectUri = redirectUri;
    }

    public Uri RedirectUri { get; }

    public static Task<OAuthCallbackListener> StartAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var redirectUri = new Uri($"http://127.0.0.1:{endpoint.Port}/callback");
        return Task.FromResult(new OAuthCallbackListener(listener, redirectUri));
    }

    public async Task<OAuthCallbackData> WaitForCallbackAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(timeoutSource.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(timeoutSource.Token);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                throw new InvalidOperationException("OAuth callback request was empty.");
            }

            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeoutSource.Token)))
            {
            }

            var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length < 2)
            {
                throw new InvalidOperationException("OAuth callback request was invalid.");
            }

            var callbackUri = new Uri(new Uri("http://127.0.0.1"), requestParts[1]);
            await WriteResponseAsync(stream, timeoutSource.Token);
            return ParseCallbackUri(callbackUri);
        }
        catch (OperationCanceledException exception)
            when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for the OAuth callback.", exception);
        }
    }

    public static OAuthCallbackData ParseCallbackUri(Uri callbackUri)
    {
        ArgumentNullException.ThrowIfNull(callbackUri);
        var values = ParseQuery(callbackUri.Query);
        if (!string.IsNullOrWhiteSpace(callbackUri.Fragment))
        {
            foreach (var pair in ParseQuery(callbackUri.Fragment.TrimStart('#', '?')))
            {
                values[pair.Key] = pair.Value;
            }
        }

        values.TryGetValue("code", out var code);
        values.TryGetValue("token", out var token);
        values.TryGetValue("state", out var state);
        values.TryGetValue("error", out var error);
        values.TryGetValue("error_description", out var errorDescription);
        return new OAuthCallbackData(code, token, state, error, errorDescription);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalizedQuery = query.TrimStart('?', '#');
        foreach (var pair in normalizedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator >= 0 ? pair[..separator] : pair;
            var value = separator >= 0 ? pair[(separator + 1)..] : string.Empty;
            values[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return values;
    }

    private static async Task WriteResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        const string body = "<html><body><h3>Đã nhận callback OAuth</h3><p>Bạn có thể quay lại 9Router Profile Tool.</p></body></html>";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
    }
}
