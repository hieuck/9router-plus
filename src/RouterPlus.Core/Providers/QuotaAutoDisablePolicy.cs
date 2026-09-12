namespace RouterPlus.Core.Providers;

public static class QuotaAutoDisablePolicy
{
    public static bool CanAutoDisable(ProviderConnection connection) =>
        connection.Provider switch
        {
            ProviderKind.Codex or ProviderKind.Ollama => connection.IsOverLimit,
            ProviderKind.Kiro => CanAutoDisableKiro(connection),
            ProviderKind.OpenRouter => IsOpenRouterRateLimitExceeded(connection),
            _ => false
        };

    public static bool HasRecovered(ProviderConnection connection) =>
        connection.Provider switch
        {
            ProviderKind.Kiro => HasRecoveredKiro(connection),
            ProviderKind.OpenRouter => IsOpenRouterRateLimitRecovered(connection),
            _ => !connection.IsOverLimit
        };

    private static bool CanAutoDisableKiro(ProviderConnection connection)
    {
        var quotas = connection.QuotaRows;
        return quotas.Count > 0
            && quotas.All(quota => quota.Total is > 0
                && quota.IsOverLimit
                && quota.ResetAt.HasValue);
    }

    private static bool HasRecoveredKiro(ProviderConnection connection)
    {
        var quotas = connection.QuotaRows;
        return quotas.Count > 0
            && quotas.All(quota => quota.Total is > 0 && !quota.IsOverLimit);
    }

    private static bool IsOpenRouterRateLimitExceeded(ProviderConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.LastError))
            return false;

        return connection.LastError.Contains("Rate limit exceeded: free-models-per-day", StringComparison.OrdinalIgnoreCase)
            || connection.LastError.Contains("Inference is blocked on this account", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenRouterRateLimitRecovered(ProviderConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.LastError))
            return true;

        return !connection.LastError.Contains("Rate limit exceeded: free-models-per-day", StringComparison.OrdinalIgnoreCase)
            && !connection.LastError.Contains("Inference is blocked on this account", StringComparison.OrdinalIgnoreCase);
    }
}
