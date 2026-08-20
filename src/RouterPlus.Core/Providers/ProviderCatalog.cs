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
                true),
            [ProviderKind.Kiro] = new(
                ProviderKind.Kiro,
                "Kiro",
                "/dashboard/providers/kiro",
                "https://kiro.dev",
                WorkflowKind.DeviceCode,
                true),
            [ProviderKind.OpenRouter] = new(
                ProviderKind.OpenRouter,
                "OpenRouter",
                "/dashboard/providers/openrouter",
                "https://openrouter.ai/settings/keys",
                WorkflowKind.ApiKey,
                true),
            [ProviderKind.Ollama] = new(
                ProviderKind.Ollama,
                "Ollama Cloud",
                "/dashboard/providers/ollama",
                "https://ollama.com/settings/keys",
                WorkflowKind.ApiKey,
                true),
            [ProviderKind.Kimchi] = new(
                ProviderKind.Kimchi,
                "Kimchi",
                "/dashboard/providers/kimchi",
                "https://app.kimchi.dev/",
                WorkflowKind.OAuth,
                true)
        };

    public static IReadOnlyList<ProviderDefinition> All { get; } =
        Definitions.Values.OrderBy(definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    public static ProviderDefinition Get(ProviderKind kind) =>
        Definitions.TryGetValue(kind, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown provider.");
}
