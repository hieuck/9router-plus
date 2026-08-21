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
    public string ShortDisplayName => Kind switch
    {
        ProviderKind.Codex => "Codex",
        ProviderKind.Kiro => "Kiro",
        ProviderKind.OpenRouter => "OpenR",
        ProviderKind.Ollama => "Ollama",
        ProviderKind.Kimchi => "Kimchi",
        _ => DisplayName
    };

    public string Glyph => Kind switch
    {
        ProviderKind.Codex => "✨",
        ProviderKind.Kiro => "🚀",
        ProviderKind.OpenRouter => "🧠",
        ProviderKind.Ollama => "🦙",
        ProviderKind.Kimchi => "🌿",
        _ => "●"
    };

    public string BuildDashboardUrl(string dashboardBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dashboardBaseUrl);
        return $"{dashboardBaseUrl.TrimEnd('/')}{DashboardPath}";
    }
}
