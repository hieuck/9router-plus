using RouterPlus.Core.Observability;

namespace RouterPlus.Core.Tests.Observability;

[Collection("Observability")]
public sealed class TraceScopeTests
{
    [Fact]
    public async Task Begin_logs_start_and_completion_and_dispose_is_idempotent()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;
        var writer = new CapturingWriter();
        hub.SetWriter(writer);
        await hub.FlushAsync();
        writer.Clear();
        var operationName = "TraceScope_DeterministicCompletion";
        var metricName = $"TestCategory.{operationName}.duration";
        var beforeCount = hub.GetMetricSnapshots().histograms.TryGetValue(metricName, out var previous)
            ? previous.count
            : 0;

        // Act
        var trace = TraceScope.Begin("TestCategory", operationName, new { test_id = 123 });
        trace.Dispose();
        trace.Dispose();
        await hub.FlushAsync();

        // Assert
        var events = writer.Events;
        var startedEvent = Assert.Single(events, logEvent => logEvent.Event == $"{operationName}Started");
        var completedEvent = Assert.Single(events, logEvent => logEvent.Event == $"{operationName}Completed");
        var startedContext = Assert.IsType<Dictionary<string, object?>>(startedEvent.Context);
        var completedContext = Assert.IsType<Dictionary<string, object?>>(completedEvent.Context);
        Assert.Equal(123, startedContext["test_id"]);
        Assert.True(completedContext.ContainsKey("duration_ms"));
        Assert.Equal(beforeCount + 1, hub.GetMetricSnapshots().histograms[metricName].count);
    }

    [Fact]
    public async Task LogCheckpoint_includes_elapsed_time_and_both_contexts()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;
        var writer = new CapturingWriter();
        hub.SetWriter(writer);
        await hub.FlushAsync();
        writer.Clear();
        var operationName = "TraceScope_DeterministicCheckpoint";

        // Act
        using (var trace = TraceScope.Begin("TestCategory", operationName, new { request_id = "request-1" }))
        {
            trace.LogCheckpoint("Loaded", new { items = 2 });
        }
        await hub.FlushAsync();

        // Assert
        var checkpoint = Assert.Single(writer.Events, logEvent => logEvent.Event == $"{operationName}Checkpoint");
        var context = Assert.IsType<Dictionary<string, object?>>(checkpoint.Context);
        var parentContext = Assert.IsType<Dictionary<string, object?>>(context["parent_context"]);
        var checkpointContext = Assert.IsType<Dictionary<string, object?>>(context["checkpoint_context"]);
        Assert.Equal("Loaded", context["checkpoint"]);
        Assert.IsType<long>(context["elapsed_ms"]);
        Assert.Equal("request-1", parentContext["request_id"]);
        Assert.Equal(2, checkpointContext["items"]);
    }

    [Fact]
    public void Current_restores_parent_scope_after_nested_scope_is_disposed()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;
        hub.SetWriter(new CapturingWriter());
        Assert.Null(TraceScope.Current);

        // Act
        using (var outer = TraceScope.Begin("TestCategory", "TraceScope_Outer"))
        {
            using (var inner = TraceScope.Begin("TestCategory", "TraceScope_Inner"))
            {
                // Assert
                Assert.Same(inner, TraceScope.Current);
            }

            // Assert
            Assert.Same(outer, TraceScope.Current);
        }

        // Assert
        Assert.Null(TraceScope.Current);
    }

    private sealed class CapturingWriter : IObservabilityWriter
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return _events.ToArray();
                }
            }
        }

        public Task WriteEventsAsync(IEnumerable<LogEvent> events)
        {
            lock (_events)
            {
                _events.AddRange(events);
            }

            return Task.CompletedTask;
        }

        public Task WriteSnapshotsAsync(IEnumerable<StateSnapshot> snapshots) => Task.CompletedTask;

        public void Clear()
        {
            lock (_events)
            {
                _events.Clear();
            }
        }

        public void Dispose()
        {
        }
    }
}
