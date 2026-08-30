using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class ProviderOAuthAdapterRegistryTests
{
    [Theory]
    [InlineData(ProviderKind.Codex, typeof(CodexOAuthAdapter))]
    [InlineData(ProviderKind.GitHub, typeof(GitHubOAuthAdapter))]
    [InlineData(ProviderKind.OpenRouter, typeof(OpenRouterOAuthAdapter))]
    [InlineData(ProviderKind.Kiro, typeof(AwsBuilderIdOAuthAdapter))]
    public void Get_ReturnsAdapterRegisteredForProvider(
        ProviderKind provider,
        Type expectedAdapterType)
    {
        var registry = new ProviderOAuthAdapterRegistry();

        var adapter = registry.Get(provider);

        Assert.IsType(expectedAdapterType, adapter);
        Assert.Equal(provider, adapter.Provider);
    }

    [Theory]
    [InlineData(ProviderKind.Ollama)]
    [InlineData(ProviderKind.Kimchi)]
    public void Get_UnsupportedProviderThrowsWithoutFallback(ProviderKind provider)
    {
        var registry = new ProviderOAuthAdapterRegistry();

        var exception = Assert.Throws<NotSupportedException>(() => registry.Get(provider));

        Assert.Contains(provider.ToString(), exception.Message);
        Assert.Contains("not supported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_DuplicateProviderAdaptersThrows()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ProviderOAuthAdapterRegistry(new CodexOAuthAdapter(), new CodexOAuthAdapter()));

        Assert.Contains("Codex", exception.Message);
    }
}
