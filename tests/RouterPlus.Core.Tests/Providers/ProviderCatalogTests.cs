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
    [InlineData(ProviderKind.Nous, "/dashboard/providers/nous", "https://portal.nousresearch.com")]
    [InlineData(ProviderKind.GitHub, "/dashboard/providers/github", "https://github.com")]
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
        Assert.Equal(WorkflowKind.DeviceCode, ProviderCatalog.Get(ProviderKind.Nous).Workflow);
    }

    [Theory]
    [InlineData(ProviderKind.Codex, "Codex", "✨")]
    [InlineData(ProviderKind.Kiro, "Kiro", "🚀")]
    [InlineData(ProviderKind.OpenRouter, "OpenR", "🧠")]
    [InlineData(ProviderKind.Ollama, "Ollama", "🦙")]
    [InlineData(ProviderKind.Kimchi, "Kimchi", "🌿")]
    [InlineData(ProviderKind.Nous, "Nous", "🔭")]
    [InlineData(ProviderKind.GitHub, "GitHub", "🐙")]
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

    [Fact]
    public void Every_definition_has_non_empty_metadata()
    {
        foreach (var definition in ProviderCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.ApiAlias));
            Assert.False(string.IsNullOrWhiteSpace(definition.ShortDisplayName));
            Assert.False(string.IsNullOrWhiteSpace(definition.Glyph));
            Assert.False(string.IsNullOrWhiteSpace(definition.ShortCode));
        }
    }

    [Fact]
    public void Api_aliases_are_unique()
    {
        var aliases = ProviderCatalog.All.Select(d => d.ApiAlias).ToArray();
        Assert.Equal(aliases.Length, aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("kiro")]
    [InlineData("openrouter")]
    [InlineData("ollama")]
    [InlineData("kimchi")]
    [InlineData("nous")]
    public void TryGetByApiAlias_round_trips_case_insensitive(string alias)
    {
        Assert.True(ProviderCatalog.TryGetByApiAlias(alias, out var kind));
        Assert.Equal(alias, ProviderCatalog.Get(kind).ApiAlias, StringComparer.OrdinalIgnoreCase);
        Assert.True(ProviderCatalog.TryGetByApiAlias(alias.ToUpperInvariant(), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    public void TryGetByApiAlias_returns_false_for_unknown(string? alias)
    {
        Assert.False(ProviderCatalog.TryGetByApiAlias(alias, out _));
    }

    [Theory]
    [InlineData(ProviderKind.Codex, "codex", "Codex", "✨", "CX")]
    [InlineData(ProviderKind.Kiro, "kiro", "Kiro", "🚀", "KI")]
    [InlineData(ProviderKind.OpenRouter, "openrouter", "OpenR", "🧠", "OR")]
    [InlineData(ProviderKind.Ollama, "ollama", "Ollama", "🦙", "OL")]
    [InlineData(ProviderKind.Kimchi, "kimchi", "Kimchi", "🌿", "KM")]
    [InlineData(ProviderKind.Nous, "nous", "Nous", "🔭", "NS")]
    [InlineData(ProviderKind.GitHub, "github", "GitHub", "🐙", "GH")]
    public void Catalog_exposes_exact_provider_metadata(
        ProviderKind kind,
        string expectedApiAlias,
        string expectedShortDisplayName,
        string expectedGlyph,
        string expectedShortCode)
    {
        var definition = ProviderCatalog.Get(kind);

        Assert.Equal(expectedApiAlias, definition.ApiAlias);
        Assert.Equal(expectedShortDisplayName, definition.ShortDisplayName);
        Assert.Equal(expectedGlyph, definition.Glyph);
        Assert.Equal(expectedShortCode, definition.ShortCode);
    }

    [Fact]
    public void TryGetByApiAlias_trims_padded_input()
    {
        Assert.True(ProviderCatalog.TryGetByApiAlias("  codex  ", out var kind));
        Assert.Equal(ProviderKind.Codex, kind);
    }

    [Theory]
    [InlineData(ProviderKind.Codex, "https://chatgpt.com/")]
    [InlineData(ProviderKind.Kiro, "https://kiro.dev/")]
    [InlineData(ProviderKind.OpenRouter, "https://openrouter.ai/")]
    [InlineData(ProviderKind.Nous, "https://portal.nousresearch.com/")]
    [InlineData(ProviderKind.GitHub, "https://github.com/login")]
    public void GetDirectLoginStartUri_returns_expected_url(ProviderKind kind, string expected)
    {
        var uri = ProviderCatalog.GetDirectLoginStartUri(kind);

        Assert.NotNull(uri);
        Assert.Equal(expected, uri!.ToString());
    }

    [Theory]
    [InlineData(ProviderKind.Ollama)]
    [InlineData(ProviderKind.Kimchi)]
    public void GetDirectLoginStartUri_returns_null_for_non_direct_login_providers(ProviderKind kind)
    {
        Assert.Null(ProviderCatalog.GetDirectLoginStartUri(kind));
    }

    [Fact]
    public void GetDirectLoginStartUri_does_not_throw_for_unknown_provider_kind()
    {
        var unknownKind = (ProviderKind)999;

        var uri = ProviderCatalog.GetDirectLoginStartUri(unknownKind);

        Assert.Null(uri);
    }

    [Fact]
    public void All_contains_GitHub()
    {
        Assert.Contains(ProviderCatalog.All, definition => definition.Kind == ProviderKind.GitHub);
    }

    [Fact]
    public void TryGetByApiAlias_returns_GitHub_for_github()
    {
        Assert.True(ProviderCatalog.TryGetByApiAlias("github", out var kind));
        Assert.Equal(ProviderKind.GitHub, kind);
    }
}
