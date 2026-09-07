using System.Collections;
using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

[Collection("Observability")]
public sealed class ObservabilityHubFailureHandlingTests
{
    [Fact]
    public void LogEvent_swallows_context_scrubbing_failure()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;
        var context = new ThrowingEnumerable();

        // Act
        var exception = Record.Exception(() => hub.LogEvent(
            LogLevel.Warning,
            "Synthetic",
            "ScrubbingFailure",
            "Synthetic logging failure",
            context));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void LogError_swallows_context_scrubbing_failure()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;
        var context = new ThrowingEnumerable();

        // Act
        var exception = Record.Exception(() => hub.LogError(
            "Synthetic",
            "ScrubbingFailure",
            new InvalidOperationException("Synthetic exception"),
            context));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void CaptureSnapshot_swallows_state_scrubbing_failure()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;
        var state = new ThrowingStateDictionary();

        // Act
        var exception = Record.Exception(() => hub.CaptureSnapshot(
            "Synthetic",
            state,
            SnapshotTrigger.Error,
            "Synthetic snapshot failure"));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void IncrementCounter_swallows_invalid_metric_key_failure()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;

        // Act
        var exception = Record.Exception(() => hub.IncrementCounter(null!));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void RecordGauge_swallows_invalid_metric_key_failure()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;

        // Act
        var exception = Record.Exception(() => hub.RecordGauge(null!, 1.0));

        // Assert
        Assert.Null(exception);
    }

    private sealed class ThrowingEnumerable : IEnumerable
    {
        public IEnumerator GetEnumerator() => throw new InvalidOperationException("Synthetic enumeration failure");
    }

    private sealed class ThrowingStateDictionary : Dictionary<string, object?>, IEnumerable
    {
        IEnumerator IEnumerable.GetEnumerator() => throw new InvalidOperationException("Synthetic state enumeration failure");
    }
}
