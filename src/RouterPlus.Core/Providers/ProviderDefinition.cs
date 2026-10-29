namespace RouterPlus.Core.Providers;

public enum ProviderKind
{
    Codex,
    Kiro,
    OpenRouter,
    GitHub,
    Ollama,
    // Value 5 is reserved; never reuse.
    Nous = 6,
    Agnes = 7,
    Poolside = 8,
    Bazaarlink = 9
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
    bool RenamesConnection,
    string ApiAlias,
    string ShortDisplayName,
    string Glyph,
    string ShortCode,
    string? DirectLoginStartUrl = null)
{
    public string BuildDashboardUrl(string dashboardBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dashboardBaseUrl);
        return $"{dashboardBaseUrl.TrimEnd('/')}{DashboardPath}";
    }
}