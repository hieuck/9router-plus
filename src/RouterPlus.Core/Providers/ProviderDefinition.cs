namespace RouterPlus.Core.Providers;

public enum ProviderKind
{
    Codex,
    Kiro,
    OpenRouter,
    Ollama,
    Kimchi
}

public enum WorkflowKind
{
    OAuth,
    DeviceCode,
    ApiKey
}

public sealed record ProviderDefinition(
    ProviderKind Kind,
    string DisplayName,
    string DashboardPath,
    string QuickLink,
    WorkflowKind Workflow,
    bool RenamesConnection)
{
    public string BuildDashboardUrl(string dashboardBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dashboardBaseUrl);
        return $"{dashboardBaseUrl.TrimEnd('/')}{DashboardPath}";
    }
}
