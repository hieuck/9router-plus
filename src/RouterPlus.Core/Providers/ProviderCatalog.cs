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
                "CX"),
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
                "KI"),
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
                "OR"),
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
            [ProviderKind.Kimchi] = new(
                ProviderKind.Kimchi,
                "Kimchi",
                "/dashboard/providers/kimchi",
                "https://app.kimchi.dev/",
                WorkflowKind.OAuth,
                true,
                "kimchi",
                "Kimchi",
                "🌿",
                "KM"),
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
                "NS")
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
}
