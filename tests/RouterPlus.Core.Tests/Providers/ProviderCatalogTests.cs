using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.Providers;

public sealed class ProviderCatalogTests
{
    [Theory]
    [InlineData(ProviderKind.Codex, "/dashboard/providers/codex", "https://chatgpt.com/codex")]
    [InlineData(ProviderKind.Kiro, "/dashboard/providers/kiro", "https://kiro.dev")]
    [InlineData(ProviderKind.OpenRouter, "/dashboard/providers/openrouter", "https://openrouter.ai/settings/keys")]
    [InlineData(ProviderKind.Ollama, "/dashboard/providers/ollama", "https://ollama.com/settings/keys")]
    [InlineData(ProviderKind.Kimchi, "/dashboard/providers/kimchi", "https://app.kimchi.dev/")]
    public void Catalog_exposes_dashboard_and_quick_link_urls(
        ProviderKind kind,
        string dashboardPath,
        string quickLink)
    {
        var definition = ProviderCatalog.Get(kind);

        Assert.Equal(dashboardPath, definition.DashboardPath);
        Assert.Equal(quickLink, definition.QuickLink);
        Assert.Equal("http://localhost:20128" + dashboardPath, definition.BuildDashboardUrl("http://localhost:20128"));
    }

    [Fact]
    public void Catalog_marks_api_key_providers()
    {
        Assert.Equal(WorkflowKind.ApiKey, ProviderCatalog.Get(ProviderKind.OpenRouter).Workflow);
        Assert.Equal(WorkflowKind.ApiKey, ProviderCatalog.Get(ProviderKind.Ollama).Workflow);
        Assert.Equal(WorkflowKind.OAuth, ProviderCatalog.Get(ProviderKind.Codex).Workflow);
        Assert.Equal(WorkflowKind.DeviceCode, ProviderCatalog.Get(ProviderKind.Kiro).Workflow);
    }

    [Theory]
    [InlineData(ProviderKind.Codex, "Codex", "✨")]
    [InlineData(ProviderKind.Kiro, "Kiro", "🚀")]
    [InlineData(ProviderKind.OpenRouter, "OpenR", "🧠")]
    [InlineData(ProviderKind.Ollama, "Ollama", "🦙")]
    [InlineData(ProviderKind.Kimchi, "Kimchi", "🌿")]
    public void Catalog_exposes_provider_short_names_and_glyphs(
        ProviderKind kind,
        string expectedShortName,
        string expectedGlyph)
    {
        // Arrange
        var definition = ProviderCatalog.Get(kind);

        // Act
        var shortName = definition.ShortDisplayName;
        var glyph = definition.Glyph;

        // Assert
        Assert.Equal(expectedShortName, shortName);
        Assert.Equal(expectedGlyph, glyph);
    }

    [Fact]
    public void BuildDashboardUrl_trims_trailing_slashes_from_base_url()
    {
        // Arrange
        var definition = ProviderCatalog.Get(ProviderKind.Codex);

        // Act
        var url = definition.BuildDashboardUrl("https://router.example///");

        // Assert
        Assert.Equal("https://router.example/dashboard/providers/codex", url);
    }

    [Fact]
    public void BuildDashboardUrl_rejects_null_base_url()
    {
        // Arrange
        var definition = ProviderCatalog.Get(ProviderKind.Codex);

        // Act
        var action = () => definition.BuildDashboardUrl(null!);

        // Assert
        Assert.Throws<ArgumentNullException>(action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildDashboardUrl_rejects_blank_base_urls(string baseUrl)
    {
        // Arrange
        var definition = ProviderCatalog.Get(ProviderKind.Codex);

        // Act
        var action = () => definition.BuildDashboardUrl(baseUrl);

        // Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Get_rejects_unknown_provider_kind()
    {
        // Arrange
        var unknownKind = (ProviderKind)999;

        // Act
        var action = () => ProviderCatalog.Get(unknownKind);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(action);
    }
}
