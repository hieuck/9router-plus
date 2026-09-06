using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// TDD tests for BatchLoginProgressRow
/// Tracks login progress for a single profile in batch auto-login
/// </summary>
public sealed class BatchLoginProgressRowTests
{
    private readonly ChromeProfile _testProfile;

    public BatchLoginProgressRowTests()
    {
        _testProfile = new ChromeProfile("profile-1-id", "Profile 1", "Profile 1", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", false);
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        var row = new BatchLoginProgressRow(_testProfile);

        // Assert
        Assert.Equal(_testProfile, row.Profile);
        Assert.Equal("Profile 1", row.ProfileName);
        Assert.Equal(BatchLoginState.Waiting, row.State);
        Assert.Equal("Đang chờ", row.StatusMessage);
        Assert.Equal(TimeSpan.Zero, row.Duration);
        Assert.Equal("", row.DurationText);
        Assert.Equal("⏸", row.StatusIcon);
    }

    [Fact]
    public void Constructor_ThrowsWhenProfileIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BatchLoginProgressRow(null!));
    }

    [Fact]
    public void State_UpdatesAndRaisesPropertyChanged()
    {
        // Arrange
        var row = new BatchLoginProgressRow(_testProfile);
        var changedProperties = new List<string>();
        row.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
                changedProperties.Add(e.PropertyName);
        };

        // Act
        row.State = BatchLoginState.InProgress;

        // Assert
        Assert.Equal(BatchLoginState.InProgress, row.State);
        Assert.Contains(nameof(BatchLoginProgressRow.State), changedProperties);
        Assert.Contains(nameof(BatchLoginProgressRow.StatusIcon), changedProperties);
    }

    [Fact]
    public void State_DoesNotRaisePropertyChangedWhenSameValue()
    {
        // Arrange
        var row = new BatchLoginProgressRow(_testProfile);
        var eventRaisedCount = 0;
        row.PropertyChanged += (s, e) => eventRaisedCount++;

        // Act
        row.State = BatchLoginState.Waiting; // Same as initial value

        // Assert
        Assert.Equal(0, eventRaisedCount);
    }

    [Fact]
    public void StatusMessage_UpdatesAndRaisesPropertyChanged()
    {
        // Arrange
        var row = new BatchLoginProgressRow(_testProfile);
        var propertyChanged = false;
        row.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(BatchLoginProgressRow.StatusMessage))
                propertyChanged = true;
        };

        // Act
        row.StatusMessage = "Đang đăng nhập";

        // Assert
        Assert.Equal("Đang đăng nhập", row.StatusMessage);
        Assert.True(propertyChanged);
    }

    [Fact]
    public void StatusMessage_DoesNotRaisePropertyChangedWhenSameValue()
    {
        // Arrange
        var row = new BatchLoginProgressRow(_testProfile);
        row.StatusMessage = "Test message";
        var eventRaisedCount = 0;
        row.PropertyChanged += (s, e) => eventRaisedCount++;

        // Act
        row.StatusMessage = "Test message"; // Same value

        // Assert
        Assert.Equal(0, eventRaisedCount);
    }

    [Fact]
    public void Duration_UpdatesAndRaisesPropertyChanged()
    {
        // Arrange
        var row = new BatchLoginProgressRow(_testProfile);
        var changedProperties = new List<string>();
        row.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
                changedProperties.Add(e.PropertyName);
        };

        // Act
        row.Duration = TimeSpan.FromSeconds(5.3);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5.3), row.Duration);
        Assert.Contains(nameof(BatchLoginProgressRow.Duration), changedProperties);
        Assert.Contains(nameof(BatchLoginProgressRow.DurationText), changedProperties);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1.5, "1,5s")]
    [InlineData(10.234, "10,2s")]
    [InlineData(60.0, "60,0s")]
    public void DurationText_FormatsCorrectly(double seconds, string expected)
    {
        // Arrange
        var row = new BatchLoginProgressRow(_testProfile);

        // Act
        row.Duration = TimeSpan.FromSeconds(seconds);

        // Assert
        Assert.Equal(expected, row.DurationText);
    }

    [Theory]
    [InlineData(BatchLoginState.Waiting, "⏸")]
    [InlineData(BatchLoginState.InProgress, "⏳")]
    [InlineData(BatchLoginState.Success, "✅")]
    [InlineData(BatchLoginState.Failed, "❌")]
    [InlineData(BatchLoginState.Skipped, "⊘")]
    public void StatusIcon_ReturnsCorrectIconForState(BatchLoginState state, string expectedIcon)
    {
        // Arrange
        var row = new BatchLoginProgressRow(_testProfile);

        // Act
        row.State = state;

        // Assert
        Assert.Equal(expectedIcon, row.StatusIcon);
    }

    [Fact]
    public void ProfileName_ReturnsProfileName()
    {
        // Arrange
        var profile = new ChromeProfile("custom-id", "Custom Profile", "Custom Profile", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", false);
        var row = new BatchLoginProgressRow(profile);

        // Act & Assert
        Assert.Equal("Custom Profile", row.ProfileName);
    }

    [Fact]
    public void CompleteWorkflow_UpdatesStateAndProperties()
    {
        // Arrange
        var row = new BatchLoginProgressRow(_testProfile);

        // Act - Simulate complete workflow
        row.State = BatchLoginState.InProgress;
        row.StatusMessage = "Đang đăng nhập";
        row.Duration = TimeSpan.FromSeconds(2.5);

        row.State = BatchLoginState.Success;
        row.StatusMessage = "Thành công";
        row.Duration = TimeSpan.FromSeconds(5.2);

        // Assert
        Assert.Equal(BatchLoginState.Success, row.State);
        Assert.Equal("Thành công", row.StatusMessage);
        Assert.Equal("5,2s", row.DurationText);
        Assert.Equal("✅", row.StatusIcon);
    }
}
