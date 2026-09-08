using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class ProviderDefinitionTests
{
    [Theory]
    [InlineData(ProviderKind.Codex, "Codex")]
    [InlineData(ProviderKind.Kiro, "Kiro")]
    [InlineData(ProviderKind.OpenRouter, "OpenR")]
    [InlineData(ProviderKind.Ollama, "Ollama")]
    [InlineData(ProviderKind.Kimchi, "Kimchi")]
    [InlineData(ProviderKind.GitHub, "GitHub display")]
    public void ShortDisplayName_uses_provider_specific_name_or_display_name(
        ProviderKind kind,
        string expected)
    {
        var definition = new ProviderDefinition(
            kind,
            "GitHub display",
            "/dashboard",
            "https://example.test",
            WorkflowKind.ApiKey,
            RenamesConnection: false);

        Assert.Equal(expected, definition.ShortDisplayName);
    }

    [Theory]
    [InlineData(ProviderKind.Codex, "✨")]
    [InlineData(ProviderKind.Kiro, "🚀")]
    [InlineData(ProviderKind.OpenRouter, "🧠")]
    [InlineData(ProviderKind.Ollama, "🦙")]
    [InlineData(ProviderKind.Kimchi, "🌿")]
    [InlineData(ProviderKind.GitHub, "●")]
    public void Glyph_uses_provider_specific_glyph_or_default(
        ProviderKind kind,
        string expected)
    {
        var definition = new ProviderDefinition(
            kind,
            "Display",
            "/dashboard",
            "https://example.test",
            WorkflowKind.ApiKey,
            RenamesConnection: false);

        Assert.Equal(expected, definition.Glyph);
    }

    [Fact]
    public void BuildDashboardUrl_trims_trailing_slashes_from_base_url()
    {
        var definition = new ProviderDefinition(
            ProviderKind.Codex,
            "Codex",
            "/dashboard/providers/codex",
            "https://example.test",
            WorkflowKind.OAuth,
            RenamesConnection: true);

        Assert.Equal(
            "https://localhost:20128/dashboard/providers/codex",
            definition.BuildDashboardUrl("https://localhost:20128///"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildDashboardUrl_rejects_missing_base_url(string? baseUrl)
    {
        var definition = new ProviderDefinition(
            ProviderKind.Codex,
            "Codex",
            "/dashboard",
            "https://example.test",
            WorkflowKind.OAuth,
            RenamesConnection: true);

        Assert.ThrowsAny<ArgumentException>(() => definition.BuildDashboardUrl(baseUrl!));
    }
}
