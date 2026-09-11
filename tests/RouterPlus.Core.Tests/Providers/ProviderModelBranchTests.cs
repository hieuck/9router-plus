using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.Providers;

public sealed class ProviderModelBranchTests
{
    [Theory]
    [InlineData(ProviderKind.Codex, "Codex", "✨")]
    [InlineData(ProviderKind.Kiro, "Kiro", "🚀")]
    [InlineData(ProviderKind.OpenRouter, "OpenR", "🧠")]
    [InlineData(ProviderKind.Ollama, "Ollama", "🦙")]
    [InlineData(ProviderKind.Kimchi, "Kimchi", "🌿")]
    public void Definition_exposes_kind_specific_short_name_and_glyph(
        ProviderKind kind,
        string expectedShortName,
        string expectedGlyph)
    {
        // Act
        var definition = ProviderCatalog.Get(kind);

        // Assert
        Assert.Equal(expectedShortName, definition.ShortDisplayName);
        Assert.Equal(expectedGlyph, definition.Glyph);
    }

    [Fact]
    public void Definition_falls_back_to_display_name_and_default_glyph_for_unknown_kind()
    {
        // Arrange
        var definition = new ProviderDefinition(
            (ProviderKind)999,
            "Synthetic Provider",
            "/synthetic",
            "https://example.test",
            WorkflowKind.ApiKey,
            false);

        // Act and assert
        Assert.Equal("Synthetic Provider", definition.ShortDisplayName);
        Assert.Equal("●", definition.Glyph);
    }

    [Theory]
    [InlineData("https://example.test", "https://example.test/synthetic")]
    [InlineData("https://example.test/", "https://example.test/synthetic")]
    [InlineData("https://example.test///", "https://example.test/synthetic")]
    public void BuildDashboardUrl_trims_base_url_trailing_slashes(
        string baseUrl,
        string expectedUrl)
    {
        // Arrange
        var definition = new ProviderDefinition(
            ProviderKind.Codex,
            "Synthetic",
            "/synthetic",
            "https://example.test",
            WorkflowKind.OAuth,
            false);

        // Act
        var url = definition.BuildDashboardUrl(baseUrl);

        // Assert
        Assert.Equal(expectedUrl, url);
    }

    [Fact]
    public void ProviderConnection_reports_status_branches_and_disabled_state()
    {
        // Arrange
        var connection = new ProviderConnection(
            "synthetic-connection",
            ProviderKind.Codex,
            "Synthetic",
            1,
            false,
            TestStatus: " READY ");

        // Act and assert
        Assert.True(connection.IsDisabled);
        Assert.True(connection.HasSuccessfulTestStatus);
        Assert.False(connection.HasUnknownTestStatus);
        Assert.False(connection.HasError);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("healthy")]
    [InlineData("available")]
    [InlineData("ready")]
    [InlineData("success")]
    [InlineData("connected")]
    public void ProviderConnection_accepts_all_success_test_statuses(string status)
    {
        // Arrange
        var connection = new ProviderConnection(
            "synthetic-connection",
            ProviderKind.OpenRouter,
            "Synthetic",
            1,
            true,
            TestStatus: $"  {status.ToUpperInvariant()}  ");

        // Act and assert
        Assert.True(connection.HasSuccessfulTestStatus);
        Assert.False(connection.HasUnknownTestStatus);
        Assert.False(connection.HasError);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("expired")]
    [InlineData("invalid")]
    [InlineData("failed")]
    public void ProviderConnection_recognizes_error_test_statuses(string status)
    {
        // Arrange
        var connection = new ProviderConnection(
            "synthetic-connection",
            ProviderKind.Codex,
            "Synthetic",
            1,
            true,
            TestStatus: status);

        // Act and assert
        Assert.True(connection.HasError);
        Assert.False(connection.HasUnknownTestStatus);
    }

    [Fact]
    public void ProviderConnection_treats_unrecognized_status_as_unknown()
    {
        // Arrange
        var connection = new ProviderConnection(
            "synthetic-connection",
            ProviderKind.Kiro,
            "Synthetic",
            1,
            true,
            TestStatus: "pending");

        // Act and assert
        Assert.False(connection.HasError);
        Assert.True(connection.HasUnknownTestStatus);
    }
}
