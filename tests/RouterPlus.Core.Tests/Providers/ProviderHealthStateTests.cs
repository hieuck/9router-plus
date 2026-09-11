using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.Providers;

public sealed class ProviderHealthStateTests
{
    [Fact]
    public void Resolve_returns_unknown_when_connections_are_not_synced()
    {
        // Arrange
        var connections = Array.Empty<ProviderConnection>();

        // Act
        var state = ProviderHealthStateResolver.Resolve(false, connections);

        // Assert
        Assert.Equal(ProviderHealthState.Unknown, state);
    }

    [Fact]
    public void Resolve_returns_missing_when_profile_has_no_connection()
    {
        // Arrange
        var connections = Array.Empty<ProviderConnection>();

        // Act
        var state = ProviderHealthStateResolver.Resolve(true, connections);

        // Assert
        Assert.Equal(ProviderHealthState.Missing, state);
    }

    [Fact]
    public void Resolve_returns_disabled_when_all_connections_are_inactive()
    {
        // Arrange
        var connections = new[]
        {
            new ProviderConnection("ollama-1", ProviderKind.Ollama, "Work", 1, false)
        };

        // Act
        var state = ProviderHealthStateResolver.Resolve(true, connections);

        // Assert
        Assert.Equal(ProviderHealthState.Disabled, state);
    }

    [Fact]
    public void Resolve_returns_error_when_an_active_connection_is_unavailable()
    {
        // Arrange
        var connections = new[]
        {
            new ProviderConnection(
                "ollama-1",
                ProviderKind.Ollama,
                "Work",
                1,
                true,
                TestStatus: "unavailable")
        };

        // Act
        var state = ProviderHealthStateResolver.Resolve(true, connections);

        // Assert
        Assert.Equal(ProviderHealthState.Error, state);
    }

    [Fact]
    public void Resolve_returns_unknown_when_an_active_connection_has_not_been_tested()
    {
        // Arrange
        var connections = new[]
        {
            new ProviderConnection(
                "openrouter-1",
                ProviderKind.OpenRouter,
                "Work",
                1,
                true,
                TestStatus: "unknown")
        };

        // Act
        var state = ProviderHealthStateResolver.Resolve(true, connections);

        // Assert
        Assert.Equal(ProviderHealthState.Unknown, state);
    }

    [Fact]
    public void Resolve_returns_unknown_when_active_connection_has_null_test_status()
    {
        // Arrange
        var connections = new[]
        {
            new ProviderConnection(
                "codex-1",
                ProviderKind.Codex,
                "Work",
                1,
                true,
                TestStatus: null)
        };

        // Act
        var state = ProviderHealthStateResolver.Resolve(true, connections);

        // Assert
        Assert.Equal(ProviderHealthState.Unknown, state);
    }

    [Fact]
    public void Resolve_trusts_active_test_status_over_stale_error_metadata()
    {
        // Arrange
        var connections = new[]
        {
            new ProviderConnection(
                "ollama-1",
                ProviderKind.Ollama,
                "Work",
                1,
                true,
                TestStatus: "active",
                ErrorCode: "400",
                LastError: "stale error from an earlier test")
        };

        // Act
        var state = ProviderHealthStateResolver.Resolve(true, connections);

        // Assert
        Assert.Equal(ProviderHealthState.Healthy, state);
    }

    [Fact]
    public void Resolve_returns_disabled_when_9router_marks_all_connections_inactive()
    {
        // Arrange
        var connections = new[]
        {
            new ProviderConnection(
                "ollama-1",
                ProviderKind.Ollama,
                "Work",
                1,
                false,
                TestStatus: "unavailable",
                ErrorCode: "401")
        };

        // Act
        var state = ProviderHealthStateResolver.Resolve(true, connections);

        // Assert
        Assert.Equal(ProviderHealthState.Disabled, state);
    }

    [Fact]
    public void Resolve_prioritizes_error_over_disabled_and_healthy()
    {
        // Arrange
        var connections = new[]
        {
            new ProviderConnection("codex-1", ProviderKind.Codex, "Work", 1, false),
            new ProviderConnection(
                "codex-2",
                ProviderKind.Codex,
                "Work",
                2,
                true,
                TestStatus: "unavailable",
                ErrorCode: "401",
                LastError: "Usage API temporarily unavailable (401)")
        };

        // Act
        var state = ProviderHealthStateResolver.Resolve(true, connections);

        // Assert
        Assert.Equal(ProviderHealthState.Error, state);
    }
}
