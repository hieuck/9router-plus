using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RouterPlus.Core.Providers;

namespace RouterPlus.Infrastructure.Router;

public sealed class RouterApiClient : IRouterApiClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly UsageDatabaseReader _usageReader;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public RouterApiClient(HttpClient httpClient, string dashboardBaseUrl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(dashboardBaseUrl);
        _baseUri = new Uri(dashboardBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _usageReader = new UsageDatabaseReader();
    }

    public async Task<IReadOnlyList<ProviderConnection>> ListAllConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(CreateUri("api/providers"), cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("connections", out var connections) ||
            connections.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ProviderConnection>();
        }

        
        // Get usage data from database
        var usageByConnection = _usageReader.GetTodayUsageByConnection();

        return connections.EnumerateArray()
            .Select(element => ParseConnection(element, usageByConnection))
            .Where(connection => connection is not null)
            .Cast<ProviderConnection>()
            .OrderBy(connection => connection.Provider)
            .ThenBy(connection => connection.Priority)
            .ThenBy(connection => connection.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ProviderConnection>> ListConnectionsAsync(
        ProviderKind provider,
        CancellationToken cancellationToken = default)
    {
        var connections = await ListAllConnectionsAsync(cancellationToken);
        return connections
            .Where(connection => connection.Provider == provider)
            .OrderBy(connection => connection.Priority)
            .ThenBy(connection => connection.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ProviderConnectionTestResult> TestConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        using var response = await _httpClient.PostAsync(
            CreateUri($"api/providers/{Uri.EscapeDataString(connectionId)}/test"),
            null,
            cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var root = document.RootElement;
        return new ProviderConnectionTestResult(
            GetBoolean(root, "valid"),
            GetString(root, "error"));
    }

    public async Task<OAuthAuthorizationSession> StartOAuthAuthorizationAsync(
        ProviderKind provider,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        var path = $"api/oauth/{ToApiAlias(provider)}/authorize?redirect_uri={Uri.EscapeDataString(redirectUri)}";
        using var response = await _httpClient.GetAsync(CreateUri(path), cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var root = document.RootElement;
        return new OAuthAuthorizationSession(
            GetRequiredString(root, "authUrl"),
            GetRequiredString(root, "state"),
            GetRequiredString(root, "codeVerifier"),
            GetString(root, "redirectUri") ?? redirectUri,
            GetString(root, "flowType") ?? "browser",
            GetBoolean(root, "fixedPort"),
            GetString(root, "callbackPath"));
    }

    public async Task<OAuthProxyStartResult> StartOAuthProxyAsync(
        ProviderKind provider,
        int appPort,
        OAuthAuthorizationSession session,
        CancellationToken cancellationToken = default)
    {
        if (appPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(appPort));
        }

        ArgumentNullException.ThrowIfNull(session);
        var path = $"api/oauth/{ToApiAlias(provider)}/start-proxy" +
                   $"?app_port={appPort}" +
                   $"&state={Uri.EscapeDataString(session.State)}" +
                   $"&code_verifier={Uri.EscapeDataString(session.CodeVerifier)}" +
                   $"&redirect_uri={Uri.EscapeDataString(session.RedirectUri)}";
        using var response = await _httpClient.GetAsync(CreateUri(path), cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var root = document.RootElement;
        var success = GetBoolean(root, "success");
        if (!success)
        {
            throw new RouterApiException(
                GetString(root, "reason") ?? GetString(root, "error") ?? "9Router could not start the OAuth proxy.",
                response.StatusCode);
        }

        return new OAuthProxyStartResult(success, GetBoolean(root, "serverSide"));
    }

    public async Task<OAuthProxyStatus> GetOAuthProxyStatusAsync(
        ProviderKind provider,
        string state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        var path = $"api/oauth/{ToApiAlias(provider)}/poll-status?state={Uri.EscapeDataString(state)}";
        using var response = await _httpClient.GetAsync(CreateUri(path), cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var root = document.RootElement;
        return new OAuthProxyStatus(
            GetString(root, "status") ?? "pending",
            GetString(root, "error"));
    }

    public async Task ExchangeOAuthCodeAsync(
        ProviderKind provider,
        string code,
        string redirectUri,
        string? codeVerifier,
        string? state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        var request = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["redirectUri"] = redirectUri,
            ["codeVerifier"] = codeVerifier,
            ["state"] = state
        };
        using var response = await _httpClient.PostAsJsonAsync(
            CreateUri($"api/oauth/{ToApiAlias(provider)}/exchange"),
            request,
            _jsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<DeviceCodeSession> StartDeviceCodeAsync(
        ProviderKind provider,
        string? authMethod = null,
        CancellationToken cancellationToken = default)
    {
        var path = $"api/oauth/{ToApiAlias(provider)}/device-code";
        if (!string.IsNullOrWhiteSpace(authMethod))
        {
            path += $"?auth_method={Uri.EscapeDataString(authMethod)}";
        }

        using var response = await _httpClient.GetAsync(CreateUri(path), cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var root = document.RootElement;
        return new DeviceCodeSession(
            GetRequiredString(root, "device_code"),
            GetString(root, "user_code"),
            GetRequiredString(root, "verification_uri"),
            GetString(root, "verification_uri_complete"),
            GetInteger(root, "expires_in", 600),
            GetInteger(root, "interval", 5),
            GetString(root, "_clientId"),
            GetString(root, "_clientSecret"),
            GetString(root, "_region"),
            GetString(root, "_authMethod"),
            GetString(root, "_startUrl"),
            GetString(root, "codeVerifier"));
    }

    public async Task<DeviceCodePollResult> PollDeviceCodeAsync(
        ProviderKind provider,
        DeviceCodeSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var extraData = new Dictionary<string, string?>
        {
            ["_clientId"] = session.ClientId,
            ["_clientSecret"] = session.ClientSecret,
            ["_region"] = session.Region,
            ["_authMethod"] = session.AuthMethod,
            ["_startUrl"] = session.StartUrl
        };
        var request = new
        {
            deviceCode = session.DeviceCode,
            codeVerifier = session.CodeVerifier,
            extraData
        };
        using var response = await _httpClient.PostAsJsonAsync(
            CreateUri($"api/oauth/{ToApiAlias(provider)}/poll"),
            request,
            _jsonOptions,
            cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var root = document.RootElement;
        return new DeviceCodePollResult(
            GetBoolean(root, "success"),
            GetString(root, "error"),
            GetString(root, "errorDescription"));
    }

    public async Task<ProviderConnection> AddApiKeyConnectionAsync(
        ProviderKind provider,
        string name,
        string apiKey,
        int priority,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        if (priority < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority must be positive.");
        }

        var request = new
        {
            provider = ToApiAlias(provider),
            apiKey,
            name,
            priority,
            testStatus = "unknown"
        };
        using var response = await _httpClient.PostAsJsonAsync(CreateUri("api/providers"), request, _jsonOptions, cancellationToken);
        using var document = await ReadDocumentAsync(response, cancellationToken);
        var created = ParseConnectionFromResponse(document.RootElement);
        if (created is not null)
        {
            return created;
        }

        var connections = await ListConnectionsAsync(provider, cancellationToken);
        return connections.LastOrDefault(connection =>
                   string.Equals(connection.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? throw new RouterApiException("9Router did not return the created connection.", HttpStatusCode.OK);
    }

    public async Task UpdateConnectionAsync(
        string connectionId,
        string? name = null,
        int? priority = null,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (string.IsNullOrWhiteSpace(name) && priority is null && string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var request = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            request["name"] = name.Trim();
        }

        if (priority is not null)
        {
            if (priority < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority must be positive.");
            }

            request["priority"] = priority.Value;
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request["apiKey"] = apiKey.Trim();
        }

        using var response = await _httpClient.PutAsJsonAsync(CreateUri($"api/providers/{Uri.EscapeDataString(connectionId)}"), request, _jsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<ProviderConnection> WaitForNewConnectionAsync(
        ProviderKind provider,
        IReadOnlySet<string> existingConnectionIds,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(existingConnectionIds);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var current = await ListConnectionsAsync(provider, cancellationToken);
            var newConnection = current.FirstOrDefault(connection => !existingConnectionIds.Contains(connection.Id));
            if (newConnection is not null)
            {
                return newConnection;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining < pollInterval ? remaining : pollInterval, cancellationToken);
            }
        }

        throw new TimeoutException($"Timed out waiting for a new {ProviderCatalog.Get(provider).DisplayName} connection.");
    }

    private async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private Uri CreateUri(string relativePath) => new(_baseUri, relativePath);

    private static string GetRequiredString(JsonElement root, string propertyName) =>
        GetString(root, propertyName) ?? throw new RouterApiException(
            $"9Router response did not include '{propertyName}'.",
            HttpStatusCode.OK);

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetBoolean(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    private static int GetInteger(JsonElement root, string propertyName, int fallback) =>
        root.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : fallback;

    private static Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (response.IsSuccessStatusCode)
        {
            return Task.CompletedTask;
        }

        throw new RouterApiException(
            $"9Router API request failed with HTTP {(int)response.StatusCode}.",
            response.StatusCode);
    }

    private static ProviderConnection? ParseConnectionFromResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("connection", out var connection))
        {
            return ParseConnection(connection, new Dictionary<string, UsageData>());
        }

        return root.TryGetProperty("id", out _) ? ParseConnection(root, new Dictionary<string, UsageData>()) : null;
    }

    private static ProviderConnection? ParseConnection(JsonElement element, Dictionary<string, UsageData> usageByConnection)
    {
        if (!element.TryGetProperty("id", out var idElement) ||
            !element.TryGetProperty("provider", out var providerElement))
        {
            return null;
        }

        var id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
        var providerValue = providerElement.ValueKind == JsonValueKind.String ? providerElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(id) || !TryParseProvider(providerValue, out var provider))
        {
            return null;
        }

        var name = GetString(element, "name");
        var email = GetString(element, "email");
        var testStatus = GetString(element, "testStatus");
        var errorCode = GetStringOrNumber(element, "errorCode");
        var lastError = GetString(element, "lastError");
        var priority = element.TryGetProperty("priority", out var priorityElement) &&
                       priorityElement.ValueKind == JsonValueKind.Number &&
                       priorityElement.TryGetInt32(out var parsedPriority)
            ? parsedPriority
            : 0;
        var isActive = !element.TryGetProperty("isActive", out var activeElement) || activeElement.ValueKind != JsonValueKind.False;
        var createdAt = element.TryGetProperty("createdAt", out var createdElement) &&
                        createdElement.ValueKind == JsonValueKind.String &&
                        createdElement.TryGetDateTimeOffset(out var parsedCreatedAt)
            ? parsedCreatedAt
            : (DateTimeOffset?)null;
        var lastErrorAt = element.TryGetProperty("lastErrorAt", out var lastErrorAtElement) &&
                          lastErrorAtElement.ValueKind == JsonValueKind.String &&
                          lastErrorAtElement.TryGetDateTimeOffset(out var parsedLastErrorAt)
            ? parsedLastErrorAt
            : (DateTimeOffset?)null;

        var usageCount = element.TryGetProperty("usageCount", out var usageCountElement) &&
                         usageCountElement.ValueKind == JsonValueKind.Number &&
                         usageCountElement.TryGetInt64(out var parsedUsageCount)
            ? parsedUsageCount
            : (long?)null;
        var limitCount = element.TryGetProperty("limitCount", out var limitCountElement) &&
                         limitCountElement.ValueKind == JsonValueKind.Number &&
                         limitCountElement.TryGetInt64(out var parsedLimitCount)
            ? parsedLimitCount
            : (long?)null;
        var usageResetAt = element.TryGetProperty("usageResetAt", out var usageResetAtElement) &&
                           usageResetAtElement.ValueKind == JsonValueKind.String &&
                           usageResetAtElement.TryGetDateTimeOffset(out var parsedUsageResetAt)
            ? parsedUsageResetAt
            : (DateTimeOffset?)null;


        var expiresAt = element.TryGetProperty("expiresAt", out var expiresAtElement) &&
                        expiresAtElement.ValueKind == JsonValueKind.String &&
                        expiresAtElement.TryGetDateTimeOffset(out var parsedExpiresAt)
            ? parsedExpiresAt
            : (DateTimeOffset?)null;

        var expiresIn = element.TryGetProperty("expiresIn", out var expiresInElement) &&
                        expiresInElement.ValueKind == JsonValueKind.Number &&
                        expiresInElement.TryGetInt32(out var parsedExpiresIn)
            ? parsedExpiresIn
            : (int?)null;

        var lastRefreshAt = element.TryGetProperty("lastRefreshAt", out var lastRefreshAtElement) &&
                            lastRefreshAtElement.ValueKind == JsonValueKind.String &&
                            lastRefreshAtElement.TryGetDateTimeOffset(out var parsedLastRefreshAt)
            ? parsedLastRefreshAt
            : (DateTimeOffset?)null;

        // Use expiresAt for OAuth providers to show token expiration time
        // This is different from usage quota - it's when the OAuth token expires
        if (expiresAt.HasValue && expiresAt.Value > DateTimeOffset.Now && !usageResetAt.HasValue)
        {
            usageResetAt = expiresAt.Value;
        }
        
        // ALWAYS prefer database for usageCount (most accurate, real-time local data)
        // Database is the source of truth for usage tracking, NOT API response
        if (usageByConnection.TryGetValue(id, out var dbUsage))
        {
            // Override API usageCount with real database value (accurate, real-time)
            usageCount = dbUsage.Requests;
            
            // Database doesn't have limit, so we leave limitCount null
            // (API limitCount is preserved if available)
            
            // Set reset time to end of today ONLY if not already set from expiresAt
            if (!usageResetAt.HasValue || usageResetAt.Value.Date != DateTimeOffset.UtcNow.Date.AddDays(1).Date)
            {
                usageResetAt = DateTimeOffset.UtcNow.Date.AddDays(1);
            }
        }
        
        // If backend doesn't provide usage data and database doesn't have it, try to infer from error messages
        if (!usageCount.HasValue && !limitCount.HasValue)
        {
            var inferred = UsageInferenceService.InferUsageFromError(provider, errorCode, lastError, lastErrorAt);
            if (inferred is not null)
            {
                usageCount = inferred.UsageCount;
                limitCount = inferred.LimitCount;
                // Only use inferred resetAt if we don't have expiresAt
                if (!usageResetAt.HasValue)
                {
                    usageResetAt = inferred.UsageResetAt;
                }
            }
        }
return new ProviderConnection(id, provider, name, priority, isActive, email, createdAt, testStatus, errorCode, lastError, lastErrorAt, usageCount, limitCount, usageResetAt, expiresAt, expiresIn, lastRefreshAt);
}
    private static string? GetStringOrNumber(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static bool TryParseProvider(string? value, out ProviderKind provider)
    {
        provider = value?.Trim().ToLowerInvariant() switch
        {
            "codex" => ProviderKind.Codex,
            "kiro" => ProviderKind.Kiro,
            "openrouter" => ProviderKind.OpenRouter,
            "ollama" => ProviderKind.Ollama,
            "kimchi" => ProviderKind.Kimchi,
            _ => default
        };
        return value?.Trim().ToLowerInvariant() is "codex" or "kiro" or "openrouter" or "ollama" or "kimchi";
    }

    private static string ToApiAlias(ProviderKind provider) => provider switch
    {
        ProviderKind.Codex => "codex",
        ProviderKind.Kiro => "kiro",
        ProviderKind.OpenRouter => "openrouter",
        ProviderKind.Ollama => "ollama",
        ProviderKind.Kimchi => "kimchi",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider.")
    };
}
