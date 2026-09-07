using System.Collections.Concurrent;
using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

[CollectionDefinition("Observability", DisableParallelization = true)]
public sealed class ObservabilityCollectionDefinition
{
}

[Collection("Observability")]
public sealed class ObservabilityHubTests
{
    [Fact]
    public void SetWriter_rejects_null_writer()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => hub.SetWriter(null!));

        // Assert
        Assert.Equal("writer", exception.ParamName);
    }

    [Fact]
    public async Task LogEvent_flushes_synthetic_event_with_scrubbed_context()
    {
        // Arrange
        var (hub, writer) = await CreateIsolatedHubAsync();
        var context = new
        {
            Profile = "Synthetic Profile",
            Password = "secret-password",
            Nested = new { ApiKey = "secret-api-key" }
        };

        // Act
        hub.LogEvent(LogLevel.Warning, "Synthetic", "SyntheticEvent", "Synthetic message", context);
        await hub.FlushAsync();

        // Assert
        var loggedEvent = Assert.Single(writer.Events);
        Assert.Equal(LogLevel.Warning, loggedEvent.Level);
        Assert.Equal("Synthetic", loggedEvent.Category);
        Assert.Equal("SyntheticEvent", loggedEvent.Event);
        Assert.Equal("Synthetic message", loggedEvent.Message);
        Assert.NotEqual(default, loggedEvent.Timestamp);

        var scrubbedContext = Assert.IsType<Dictionary<string, object?>>(loggedEvent.Context);
        Assert.Equal("Synthetic Profile", scrubbedContext["Profile"]);
        Assert.Equal("[REDACTED]", scrubbedContext["Password"]);

        var nestedContext = Assert.IsType<Dictionary<string, object?>>(scrubbedContext["Nested"]);
        Assert.Equal("[REDACTED]", nestedContext["ApiKey"]);

        writer.Dispose();
    }

    [Fact]
    public async Task LogError_flushes_exception_details_and_scrubbed_context()
    {
        // Arrange
        var (hub, writer) = await CreateIsolatedHubAsync();
        InvalidOperationException exception;
        try
        {
            throw new InvalidOperationException("Synthetic failure");
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        // Act
        hub.LogError("Synthetic", "SyntheticFailure", exception, new { Attempt = 2, Token = "secret-token" });
        await hub.FlushAsync();

        // Assert
        var errorEvent = Assert.Single(writer.Events);
        Assert.Equal(LogLevel.Error, errorEvent.Level);
        Assert.Equal("Synthetic", errorEvent.Category);
        Assert.Equal("SyntheticFailure", errorEvent.Event);
        Assert.Equal("Synthetic failure", errorEvent.Message);
        Assert.Equal(nameof(InvalidOperationException), errorEvent.ErrorType);
        Assert.NotNull(errorEvent.StackTrace);
        Assert.Contains(nameof(LogError_flushes_exception_details_and_scrubbed_context), errorEvent.StackTrace);

        var scrubbedContext = Assert.IsType<Dictionary<string, object?>>(errorEvent.Context);
        Assert.Equal(2, scrubbedContext["Attempt"]);
        Assert.Equal("[REDACTED]", scrubbedContext["Token"]);

        writer.Dispose();
    }

    [Fact]
    public async Task CaptureSnapshot_flushes_synthetic_snapshot_metadata()
    {
        // Arrange
        var (hub, writer) = await CreateIsolatedHubAsync();
        var state = new Dictionary<string, object?>
        {
            ["Status"] = "Synthetic state"
        };

        // Act
        hub.CaptureSnapshot("SyntheticComponent", state, SnapshotTrigger.Error, "Synthetic error context");
        await hub.FlushAsync();

        // Assert
        var snapshot = Assert.Single(writer.Snapshots);
        Assert.Equal("SyntheticComponent", snapshot.Component);
        Assert.Equal(SnapshotTrigger.Error, snapshot.Trigger);
        Assert.Equal("Synthetic error context", snapshot.ErrorContext);
        Assert.NotEqual(default, snapshot.Timestamp);

        writer.Dispose();
    }

    private static async Task<(ObservabilityHub hub, InMemoryWriter writer)> CreateIsolatedHubAsync()
    {
        var hub = ObservabilityHub.Instance;
        var writer = new InMemoryWriter();
        hub.SetWriter(writer);

        // Drain events left by other singleton consumers before this test begins.
        await hub.FlushAsync();
        writer.Clear();

        return (hub, writer);
    }

    private sealed class InMemoryWriter : IObservabilityWriter
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();
        private readonly ConcurrentQueue<StateSnapshot> _snapshots = new();

        public IReadOnlyCollection<LogEvent> Events => _events.ToArray();
        public IReadOnlyCollection<StateSnapshot> Snapshots => _snapshots.ToArray();

        public Task WriteEventsAsync(IEnumerable<LogEvent> events)
        {
            foreach (var logEvent in events)
            {
                _events.Enqueue(logEvent);
            }

            return Task.CompletedTask;
        }

        public Task WriteSnapshotsAsync(IEnumerable<StateSnapshot> snapshots)
        {
            foreach (var snapshot in snapshots)
            {
                _snapshots.Enqueue(snapshot);
            }

            return Task.CompletedTask;
        }

        public void Clear()
        {
            while (_events.TryDequeue(out _)) { }
            while (_snapshots.TryDequeue(out _)) { }
        }

        public void Dispose()
        {
        }
    }
}
