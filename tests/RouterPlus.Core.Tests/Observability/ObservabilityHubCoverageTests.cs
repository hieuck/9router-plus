using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

public sealed class ObservabilityHubCoverageTests
{
    [Fact]
    public async Task Flush_writes_events_and_snapshots_with_scrubbed_context()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;
        var writer = new RecordingWriter();
        hub.SetWriter(writer);

        // Act
        hub.LogEvent(LogLevel.Info, "Coverage", "EventUnderTest", "synthetic message", new
        {
            password = "synthetic-password",
            value = 7
        });
        hub.LogError("Coverage", "ErrorUnderTest", new InvalidOperationException("synthetic error"));
        hub.CaptureSnapshot("CoverageComponent", new Dictionary<string, object?>
        {
            ["value"] = 7
        }, SnapshotTrigger.OnDemand, "synthetic context");
        await hub.FlushAsync();

        // Assert
        var loggedEvent = Assert.Single(writer.Events, e => e.Event == "EventUnderTest");
        var context = Assert.IsType<Dictionary<string, object?>>(loggedEvent.Context);
        Assert.Equal("[REDACTED]", context["password"]);
        Assert.Equal(7, context["value"]);

        var error = Assert.Single(writer.Events, e => e.Event == "ErrorUnderTest");
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Equal("InvalidOperationException", error.ErrorType);
        Assert.Equal("synthetic error", error.Message);

        var snapshot = Assert.Single(writer.Snapshots, s => s.Component == "CoverageComponent");
        Assert.Equal(SnapshotTrigger.OnDemand, snapshot.Trigger);
        Assert.Equal("synthetic context", snapshot.ErrorContext);
    }

    [Fact]
    public void SetWriter_rejects_null()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;

        // Act and Assert
        Assert.Throws<ArgumentNullException>(() => hub.SetWriter(null!));
    }

    private sealed class RecordingWriter : IObservabilityWriter
    {
        private readonly object _lock = new();
        private readonly List<LogEvent> _events = new();
        private readonly List<StateSnapshot> _snapshots = new();

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_lock)
                {
                    return _events.ToArray();
                }
            }
        }

        public IReadOnlyList<StateSnapshot> Snapshots
        {
            get
            {
                lock (_lock)
                {
                    return _snapshots.ToArray();
                }
            }
        }

        public Task WriteEventsAsync(IEnumerable<LogEvent> events)
        {
            lock (_lock)
            {
                _events.AddRange(events);
            }

            return Task.CompletedTask;
        }

        public Task WriteSnapshotsAsync(IEnumerable<StateSnapshot> snapshots)
        {
            lock (_lock)
            {
                _snapshots.AddRange(snapshots);
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
