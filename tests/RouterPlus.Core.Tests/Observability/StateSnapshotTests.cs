using System.Reflection;
using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

[Collection("Observability")]
public class StateSnapshotTests
{
    [Fact]
    public async Task CaptureSnapshot_enqueues_snapshot_with_periodic_trigger()
    {
        // Arrange - create isolated hub instance to avoid singleton interference
        var hub = CreateHub();
        var writer = new RecordingWriter();
        hub.SetWriter(writer);
        var state = new Dictionary<string, object?>
        {
            ["ProfileCount"] = 5,
            ["SelectedProfile"] = "Profile1"
        };

        // Act
        hub.CaptureSnapshot("MainViewModel", state, SnapshotTrigger.Periodic);
        await hub.FlushAsync();

        // Assert - snapshot is enqueued and written with expected data
        var snapshot = Assert.Single(writer.Snapshots);
        Assert.Equal("MainViewModel", snapshot.Component);
        Assert.Equal(SnapshotTrigger.Periodic, snapshot.Trigger);
        Assert.NotNull(snapshot.State);
        Assert.Equal(5, snapshot.State["ProfileCount"]);
        Assert.Equal("Profile1", snapshot.State["SelectedProfile"]);
    }

    [Fact]
    public async Task CaptureSnapshot_with_error_context_includes_error_details()
    {
        // Arrange
        var hub = CreateHub();
        var writer = new RecordingWriter();
        hub.SetWriter(writer);
        var state = new Dictionary<string, object?>
        {
            ["LastAction"] = "AutoLogin",
            ["ErrorMessage"] = "Vault locked"
        };

        // Act
        hub.CaptureSnapshot("GoogleAutoLoginViewModel", state, SnapshotTrigger.Error, "Vault unlock failed");
        await hub.FlushAsync();

        // Assert - snapshot includes error context
        var snapshot = Assert.Single(writer.Snapshots);
        Assert.Equal("GoogleAutoLoginViewModel", snapshot.Component);
        Assert.Equal(SnapshotTrigger.Error, snapshot.Trigger);
        Assert.Equal("Vault unlock failed", snapshot.ErrorContext);
        Assert.NotNull(snapshot.State);
        Assert.Equal("AutoLogin", snapshot.State["LastAction"]);
        Assert.Equal("Vault locked", snapshot.State["ErrorMessage"]);
    }

    [Fact]
    public async Task CaptureSnapshot_scrubs_sensitive_data_from_state()
    {
        // Arrange
        var hub = CreateHub();
        var writer = new RecordingWriter();
        hub.SetWriter(writer);
        var state = new Dictionary<string, object?>
        {
            ["Username"] = "user@example.com",
            ["Password"] = "secret123",
            ["ProfileId"] = "profile_abc"
        };

        // Act
        hub.CaptureSnapshot("TestComponent", state, SnapshotTrigger.OnDemand);
        await hub.FlushAsync();

        // Assert - Password should be scrubbed by PrivacyScrubber
        var snapshot = Assert.Single(writer.Snapshots);
        Assert.Equal("TestComponent", snapshot.Component);
        Assert.Equal(SnapshotTrigger.OnDemand, snapshot.Trigger);
        Assert.NotNull(snapshot.State);
        Assert.Equal("user@example.com", snapshot.State["Username"]);
        Assert.Equal("profile_abc", snapshot.State["ProfileId"]);
        Assert.Equal("[REDACTED]", snapshot.State["Password"]);
    }

    private static ObservabilityHub CreateHub()
    {
        var constructor = typeof(ObservabilityHub).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        return (ObservabilityHub)constructor!.Invoke(null);
    }

    private sealed class RecordingWriter : IObservabilityWriter
    {
        public List<LogEvent> Events { get; } = [];
        public List<StateSnapshot> Snapshots { get; } = [];

        public Task WriteEventsAsync(IEnumerable<LogEvent> events)
        {
            Events.AddRange(events);
            return Task.CompletedTask;
        }

        public Task WriteSnapshotsAsync(IEnumerable<StateSnapshot> snapshots)
        {
            Snapshots.AddRange(snapshots);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}
