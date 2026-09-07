using System;
using System.Collections.Generic;
using System.Reflection;
using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

public sealed class SnapshotSchedulerTests
{
    [Fact]
    public void Constructor_and_register_provider_reject_null_arguments()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;

        // Act and Assert
        Assert.Throws<ArgumentNullException>(() => new SnapshotScheduler(null!, TimeSpan.FromHours(1)));
        using var scheduler = new SnapshotScheduler(hub, TimeSpan.FromHours(1));
        Assert.Throws<ArgumentNullException>(() => scheduler.RegisterProvider(null!));
    }

    [Fact]
    public void HasStateChanged_returns_true_for_initial_and_different_states()
    {
        // Arrange
        using var scheduler = new SnapshotScheduler(ObservabilityHub.Instance, TimeSpan.FromHours(1));
        var hasStateChanged = GetHasStateChangedMethod();
        var initialState = new Dictionary<string, object?> { ["status"] = "ready" };
        var differentValue = new Dictionary<string, object?> { ["status"] = "busy" };
        var differentCount = new Dictionary<string, object?>
        {
            ["status"] = "ready",
            ["attempt"] = 1
        };
        var missingKey = new Dictionary<string, object?> { ["other"] = "ready" };

        // Act and Assert
        Assert.True(InvokeHasStateChanged(hasStateChanged, scheduler, initialState));
        SetLastState(scheduler, initialState);
        Assert.True(InvokeHasStateChanged(hasStateChanged, scheduler, differentValue));
        Assert.True(InvokeHasStateChanged(hasStateChanged, scheduler, differentCount));
        Assert.True(InvokeHasStateChanged(hasStateChanged, scheduler, missingKey));
    }

    [Fact]
    public void HasStateChanged_returns_false_for_equal_states()
    {
        // Arrange
        using var scheduler = new SnapshotScheduler(ObservabilityHub.Instance, TimeSpan.FromHours(1));
        var hasStateChanged = GetHasStateChangedMethod();
        var state = new Dictionary<string, object?> { ["status"] = "ready", ["count"] = 2 };
        SetLastState(scheduler, state);

        // Act
        var result = InvokeHasStateChanged(hasStateChanged, scheduler,
            new Dictionary<string, object?> { ["status"] = "ready", ["count"] = 2 });

        // Assert
        Assert.False(result);
    }

    private static MethodInfo GetHasStateChangedMethod() =>
        typeof(SnapshotScheduler).GetMethod("HasStateChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static bool InvokeHasStateChanged(MethodInfo method, SnapshotScheduler scheduler,
        Dictionary<string, object?> state) =>
        (bool)method.Invoke(scheduler, new object[] { state })!;

    private static void SetLastState(SnapshotScheduler scheduler, Dictionary<string, object?> state) =>
        typeof(SnapshotScheduler).GetField("_lastState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(scheduler, state);
}
