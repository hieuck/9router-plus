using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.Providers;

public sealed class PriorityCalculatorAdditionalTests
{
    [Fact]
    public void Next_returns_one_when_all_existing_priorities_are_negative()
    {
        // Arrange
        var connections = new[]
        {
            new ProviderConnection("synthetic-a", ProviderKind.Codex, "A", -5, true),
            new ProviderConnection("synthetic-b", ProviderKind.Kiro, "B", -1, true)
        };

        // Act
        var nextPriority = PriorityCalculator.Next(connections);

        // Assert
        Assert.Equal(1, nextPriority);
    }

    [Fact]
    public void Next_throws_when_connections_is_null()
    {
        // Arrange
        IEnumerable<ProviderConnection>? connections = null;

        // Act
        Action action = () => _ = PriorityCalculator.Next(connections!);

        // Assert
        Assert.Throws<ArgumentNullException>(action);
    }
}
