path = r'E:\GitHub\9router-plus\src\RouterPlus.Core\Providers\ProviderDefinition.cs'
with open(path, 'r', encoding='utf-8') as f:
    s = f.read()

# Add ShortDisplayName + Glyph members to ProviderDefinition
old = '''public sealed record ProviderDefinition(
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
}'''
new = '''public sealed record ProviderDefinition(
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
        ProviderKind.Codex => "\u2728",
        ProviderKind.Kiro => "\u{1F680}",
        ProviderKind.OpenRouter => "\u{1F9E0}",
        ProviderKind.Ollama => "\u{1F999}",
        ProviderKind.Kimchi => "\u{1F33F}",
        _ => "\u25CF"
    };

    public string BuildDashboardUrl(string dashboardBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dashboardBaseUrl);
        return $"{dashboardBaseUrl.TrimEnd('/')}{DashboardPath}";
    }
}'''
assert old in s, 'anchor not found'
s = s.replace(old, new, 1)
with open(path, 'w', encoding='utf-8') as f:
    f.write(s)
print('Done')
