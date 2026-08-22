import re

filepath = r'E:\GitHub\9router-plus\src\RouterPlus.Infrastructure\Router\RouterApiClient.cs'
with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Add quotaByConnection fetch
old1 = '        var tokenExpirationByConnection = _usageReader.GetTokenExpirationByConnection();'
new1 = '''        var tokenExpirationByConnection = _usageReader.GetTokenExpirationByConnection();

        // Fetch real quota data from 9Router API (same as Quota Tracker dashboard)
        var quotaByConnection = await FetchAllQuotasAsync(connections, cancellationToken);'''
content = content.replace(old1, new1)

# 2. Update Select
content = content.replace(
    '.Select(element => ParseConnection(element, usageByConnection, tokenExpirationByConnection))',
    '.Select(element => ParseConnection(element, usageByConnection, tokenExpirationByConnection, quotaByConnection))')

# 3. Update ParseConnection signature
content = content.replace(
    'private static ProviderConnection? ParseConnection(JsonElement element, Dictionary<string, UsageData> usageByConnection, Dictionary<string, TokenExpirationData> tokenExpirationByConnection)',
    'private static ProviderConnection? ParseConnection(JsonElement element, Dictionary<string, UsageData> usageByConnection, Dictionary<string, TokenExpirationData> tokenExpirationByConnection, Dictionary<string, QuotaData> quotaByConnection)')

# 4. Update fallback calls
content = content.replace(
    'return ParseConnection(connection, new Dictionary<string, UsageData>(), new Dictionary<string, TokenExpirationData>());',
    'return ParseConnection(connection, new Dictionary<string, UsageData>(), new Dictionary<string, TokenExpirationData>(), new Dictionary<string, QuotaData>());')
content = content.replace(
    'return root.TryGetProperty("id", out _) ? ParseConnection(root, new Dictionary<string, UsageData>(), new Dictionary<string, TokenExpirationData>()) : null;',
    'return root.TryGetProperty("id", out _) ? ParseConnection(root, new Dictionary<string, UsageData>(), new Dictionary<string, TokenExpirationData>(), new Dictionary<string, QuotaData>()) : null;')

# 5. Add quota override before return
old5 = 'return new ProviderConnection(id, provider, name, priority, isActive, email, createdAt, testStatus, errorCode, lastError, lastErrorAt, usageCount, limitCount, usageResetAt, expiresAt, expiresIn, lastRefreshAt);'
new5 = '''        // HIGHEST PRIORITY: Apply real quota data from 9Router Quota Tracker API
        if (quotaByConnection.TryGetValue(id, out var quota))
        {
            if (quota.Used.HasValue) usageCount = quota.Used.Value;
            if (quota.Total.HasValue) limitCount = quota.Total.Value;
            if (quota.ResetAt.HasValue) usageResetAt = quota.ResetAt.Value;
        }

return new ProviderConnection(id, provider, name, priority, isActive, email, createdAt, testStatus, errorCode, lastError, lastErrorAt, usageCount, limitCount, usageResetAt, expiresAt, expiresIn, lastRefreshAt);'''
content = content.replace(old5, new5)

# 6. Add new methods before last closing brace
new_methods = '''
    public record QuotaData(long? Used, long? Total, long? Remaining, DateTimeOffset? ResetAt);

    private async Task<Dictionary<string, QuotaData>> FetchAllQuotasAsync(
        JsonElement connections, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, QuotaData>();
        var fetchTasks = new List<(string Id, Task<QuotaData?> Task)>();
        foreach (var element in connections.EnumerateArray())
        {
            if (!element.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
            var id = idEl.GetString()!;
            fetchTasks.Add((id, FetchQuotaAsync(id, cancellationToken)));
        }
        var tasks = fetchTasks.Select(t => t.Task).ToArray();
        await Task.WhenAll(tasks);
        for (int i = 0; i < fetchTasks.Count; i++)
        {
            if (tasks[i].Result is QuotaData quota) result[fetchTasks[i].Id] = quota;
        }
        return result;
    }

    private async Task<QuotaData?> FetchQuotaAsync(string connectionId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                CreateUri($"api/usage/{Uri.EscapeDataString(connectionId)}"), cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = await ReadDocumentAsync(response, cancellationToken);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out _)) return null;
            long? used = null, total = null, remaining = null;
            DateTimeOffset? resetAt = null;
            if (root.TryGetProperty("quotas", out var quotas))
            {
                foreach (var quotaType in quotas.EnumerateObject())
                {
                    var q = quotaType.Value;
                    used = q.TryGetProperty("used", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt64() : (long?)null;
                    total = q.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt64() : (long?)null;
                    remaining = q.TryGetProperty("remaining", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt64() : (long?)null;
                    if (q.TryGetProperty("resetAt", out var ra) && ra.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ra.GetString(), out var parsedReset))
                        resetAt = parsedReset;
                    break;
                }
            }
            return new QuotaData(used, total, remaining, resetAt);
        }
        catch { return null; }
    }
'''
last_brace = content.rfind('}')
content = content[:last_brace] + new_methods + content[last_brace:]

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(content)
print(f'Done. Lines: {content.count(chr(10)) + 1}')
