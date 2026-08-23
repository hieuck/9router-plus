namespace RouterPlus.Core.Providers;

public static class QuotaAutoDisablePolicy
{
    public static bool CanAutoDisable(ProviderConnection connection) =>
        connection.Provider switch
        {
            ProviderKind.Codex or ProviderKind.Ollama => connection.IsOverLimit,
            ProviderKind.Kiro => CanAutoDisableKiro(connection),
            _ => false
        };

    public static bool HasRecovered(ProviderConnection connection) =>
        connection.Provider == ProviderKind.Kiro
            ? HasRecoveredKiro(connection)
            : !connection.IsOverLimit;

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
}
