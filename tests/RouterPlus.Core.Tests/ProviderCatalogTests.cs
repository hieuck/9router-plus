using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

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
}
