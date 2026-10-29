namespace RouterPlus.Core.Providers;

public static class ProviderCatalog
{
    private static readonly IReadOnlyDictionary<ProviderKind, ProviderDefinition> Definitions =
        new Dictionary<ProviderKind, ProviderDefinition>
        {
            [ProviderKind.Codex] = new(
                ProviderKind.Codex,
                "Codex",
                "/dashboard/providers/codex",
                "https://chatgpt.com/codex",
                WorkflowKind.OAuth,
                true,
                "codex",
                "Codex",
                "✨",
                "CX",
                "https://chatgpt.com/"),
            [ProviderKind.Kiro] = new(
                ProviderKind.Kiro,
                "Kiro",
                "/dashboard/providers/kiro",
                "https://kiro.dev",
                WorkflowKind.DeviceCode,
                true,
                "kiro",
                "Kiro",
                "🚀",
                "KI",
                "https://kiro.dev/"),
            [ProviderKind.OpenRouter] = new(
                ProviderKind.OpenRouter,
                "OpenRouter",
                "/dashboard/providers/openrouter",
                "https://openrouter.ai/settings/keys",
                WorkflowKind.ApiKey,
                true,
                "openrouter",
                "OpenR",
                "🧠",
                "OR",
                "https://openrouter.ai/"),
            [ProviderKind.Ollama] = new(
                ProviderKind.Ollama,
                "Ollama Cloud",
                "/dashboard/providers/ollama",
                "https://ollama.com/settings/keys",
                WorkflowKind.ApiKey,
                true,
                "ollama",
                "Ollama",
                "🦙",
                "OL"),
            [ProviderKind.Nous] = new(
                ProviderKind.Nous,
                "Nous",
                "/dashboard/providers/nous",
                "https://portal.nousresearch.com",
                WorkflowKind.DeviceCode,
                true,
                "nous",
                "Nous",
                "🔭",
                "NS",
                "https://portal.nousresearch.com/"),
            [ProviderKind.GitHub] = new(
                ProviderKind.GitHub,
                "GitHub",
                "/dashboard/providers/github",
                "https://github.com",
                WorkflowKind.OAuth,
                true,
                "github",
                "GitHub",
                "🐙",
                "GH",
                "https://github.com/login"),
            [ProviderKind.Agnes] = new(
                ProviderKind.Agnes,
                "Agnes AI",
                "/dashboard/providers/agnes",
                "https://platform.agnes-ai.com/settings/apiKeys",
                WorkflowKind.ApiKey,
                true,
                "agnes",
                "Agnes",
                "✨",
                "AG",
                "https://platform.agnes-ai.com/settings/apiKeys"),
            [ProviderKind.Poolside] = new(
                ProviderKind.Poolside,
                "Poolside",
                "/dashboard/providers/poolside",
                "https://platform.poolside.ai/api-keys",
                WorkflowKind.ApiKey,
                true,
                "poolside",
                "Poolside",
                "🏊",
                "PS",
                "https://platform.poolside.ai/api-keys"),
            [ProviderKind.Bazaarlink] = new(
                ProviderKind.Bazaarlink,
                "Bazaarlink",
                "http://localhost:20128/dashboard/providers/bazaarlink",
                "https://bazaarlink.ai/keys",
                WorkflowKind.ApiKey,
                true,
                "bazaarlink",
                "Bazaarlink",
                "🛒",
                "BZ",
                "https://bazaarlink.ai/keys")
        };

    private static readonly IReadOnlyDictionary<string, ProviderKind> ApiAliases =
        Definitions.ToDictionary(
            definition => definition.Value.ApiAlias,
            definition => definition.Value.Kind,
            StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ProviderDefinition> All { get; } =
        Definitions.Values.OrderBy(definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    public static ProviderDefinition Get(ProviderKind kind) =>
        Definitions.TryGetValue(kind, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown provider.");

    public static bool TryGetByApiAlias(string? alias, out ProviderKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        return ApiAliases.TryGetValue(alias.Trim(), out kind);
    }

    public static Uri? GetDirectLoginStartUri(ProviderKind kind)
    {
        if (Definitions.TryGetValue(kind, out var definition) &&
            !string.IsNullOrWhiteSpace(definition.DirectLoginStartUrl) &&
            Uri.TryCreate(definition.DirectLoginStartUrl, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        return null;
    }
}