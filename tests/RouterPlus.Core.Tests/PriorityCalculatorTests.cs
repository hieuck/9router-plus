using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class PriorityCalculatorTests
{
    [Fact]
    public void Next_returns_one_for_an_empty_provider()
    {
        Assert.Equal(1, PriorityCalculator.Next(Array.Empty<ProviderConnection>()));
    }

    [Fact]
    public void Next_appends_after_the_largest_existing_priority()
    {
        var connections = new[]
        {
            new ProviderConnection("a", ProviderKind.OpenRouter, "A", 4, true),
            new ProviderConnection("b", ProviderKind.OpenRouter, "B", 12, true),
            new ProviderConnection("c", ProviderKind.OpenRouter, "C", 7, true)
        };

        Assert.Equal(13, PriorityCalculator.Next(connections));
    }
}
