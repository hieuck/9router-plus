using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.Providers;

public sealed class ProviderDefinitionTests
{
    [Fact]
    public void Constructor_stores_metadata_fields()
    {
        var definition = new ProviderDefinition(
            ProviderKind.Codex,
            "Codex",
            "/dashboard",
            "https://example.test",
            WorkflowKind.OAuth,
            RenamesConnection: true,
            ApiAlias: "codex",
            ShortDisplayName: "Codex",
            Glyph: "✨",
            ShortCode: "CX");

        Assert.Equal("codex", definition.ApiAlias);
        Assert.Equal("Codex", definition.ShortDisplayName);
        Assert.Equal("✨", definition.Glyph);
        Assert.Equal("CX", definition.ShortCode);
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
            RenamesConnection: true,
            ApiAlias: "codex",
            ShortDisplayName: "Codex",
            Glyph: "✨",
            ShortCode: "CX");

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
            RenamesConnection: true,
            ApiAlias: "codex",
            ShortDisplayName: "Codex",
            Glyph: "✨",
            ShortCode: "CX");

        Assert.ThrowsAny<ArgumentException>(() => definition.BuildDashboardUrl(baseUrl!));
    }
}
