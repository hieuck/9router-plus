using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Storage;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// TDD tests for RecentProfileRowViewModel
/// ViewModel for recent profile entries with pinning support
/// </summary>
public sealed class RecentProfileRowViewModelTests
{
    private readonly ChromeProfile _testProfile;
    private readonly RecentProfile _testRecent;

    public RecentProfileRowViewModelTests()
    {
        _testProfile = new ChromeProfile(
            "test-id",
            "Test Profile",
            "Profile 1",
            "C:\\UserData",
            false);
        _testRecent = new RecentProfile(
            _testProfile.Id,
            _testProfile.Name,
            _testProfile.UserDataDirectory,
            DateTime.UtcNow.AddHours(-2),
            5,
            false);
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        var viewModel = new RecentProfileRowViewModel(_testRecent, _testProfile, 3);

        // Assert
        Assert.Equal(_testRecent, viewModel.Recent);
        Assert.Equal(_testProfile, viewModel.Profile);
        Assert.Equal(3, viewModel.SlotIndex);
        Assert.False(viewModel.IsPinned);
    }

    [Fact]
    public void Constructor_ThrowsWhenRecentIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RecentProfileRowViewModel(null!, _testProfile, 0));
    }

    [Fact]
    public void Constructor_ThrowsWhenProfileIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RecentProfileRowViewModel(_testRecent, null!, 0));
    }

    [Fact]
    public void Constructor_InitializesPinnedState()
    {
        // Arrange
        var pinnedRecent = new RecentProfile(_testProfile.Id, _testProfile.Name, _testProfile.UserDataDirectory, DateTime.UtcNow, 1, true);

        // Act
        var viewModel = new RecentProfileRowViewModel(pinnedRecent, _testProfile, 0);

        // Assert
        Assert.True(viewModel.IsPinned);
    }

    [Fact]
    public void Name_ReturnsProfileName()
    {
        // Arrange
        var viewModel = new RecentProfileRowViewModel(_testRecent, _testProfile, 0);

        // Act
        var name = viewModel.Name;

        // Assert
        Assert.Equal("Test Profile", name);
    }

    [Fact]
    public void LaunchCountText_ReturnsSingularForOne()
    {
        // Arrange
        var recent = new RecentProfile(_testProfile.Id, _testProfile.Name, _testProfile.UserDataDirectory, DateTime.UtcNow, 1, false);
        var viewModel = new RecentProfileRowViewModel(recent, _testProfile, 0);

        // Act
        var text = viewModel.LaunchCountText;

        // Assert
        Assert.Equal("1 lần", text);
    }

    [Fact]
    public void LaunchCountText_ReturnsPluralForMultiple()
    {
        // Arrange
        var recent = new RecentProfile(_testProfile.Id, _testProfile.Name, _testProfile.UserDataDirectory, DateTime.UtcNow, 5, false);
        var viewModel = new RecentProfileRowViewModel(recent, _testProfile, 0);

        // Act
        var text = viewModel.LaunchCountText;

        // Assert
        Assert.Equal("5 lần", text);
    }

    [Fact]
    public void LastUsedText_ReturnsJustNowForRecent()
    {
        // Arrange
        var recent = new RecentProfile(_testProfile.Id, _testProfile.Name, _testProfile.UserDataDirectory, DateTime.UtcNow.AddSeconds(-30), 1, false);
        var viewModel = new RecentProfileRowViewModel(recent, _testProfile, 0);

        // Act
        var text = viewModel.LastUsedText;

        // Assert
        Assert.Equal("vừa xong", text);
    }

    [Fact]
    public void LastUsedText_ReturnsMinutesAgoForRecentMinutes()
    {
        // Arrange
        var recent = new RecentProfile(_testProfile.Id, _testProfile.Name, _testProfile.UserDataDirectory, DateTime.UtcNow.AddMinutes(-15), 1, false);
        var viewModel = new RecentProfileRowViewModel(recent, _testProfile, 0);

        // Act
        var text = viewModel.LastUsedText;

        // Assert
        Assert.Contains("phút trước", text);
        Assert.Contains("15", text);
    }

    [Fact]
    public void LastUsedText_ReturnsHoursAgoForRecentHours()
    {
        // Arrange
        var recent = new RecentProfile(_testProfile.Id, _testProfile.Name, _testProfile.UserDataDirectory, DateTime.UtcNow.AddHours(-3), 1, false);
        var viewModel = new RecentProfileRowViewModel(recent, _testProfile, 0);

        // Act
        var text = viewModel.LastUsedText;

        // Assert
        Assert.Contains("giờ trước", text);
        Assert.Contains("3", text);
    }

    [Fact]
    public void LastUsedText_ReturnsDaysAgoForRecentDays()
    {
        // Arrange
        var recent = new RecentProfile(_testProfile.Id, _testProfile.Name, _testProfile.UserDataDirectory, DateTime.UtcNow.AddDays(-3), 1, false);
        var viewModel = new RecentProfileRowViewModel(recent, _testProfile, 0);

        // Act
        var text = viewModel.LastUsedText;

        // Assert
        Assert.Contains("ngày trước", text);
        Assert.Contains("3", text);
    }

    [Fact]
    public void LastUsedText_ReturnsFormattedDateForOld()
    {
        // Arrange
        var recent = new RecentProfile(_testProfile.Id, _testProfile.Name, _testProfile.UserDataDirectory, DateTime.UtcNow.AddDays(-10), 1, false);
        var viewModel = new RecentProfileRowViewModel(recent, _testProfile, 0);

        // Act
        var text = viewModel.LastUsedText;

        // Assert
        Assert.Matches(@"\d{2}/\d{2} \d{2}:\d{2}", text); // Format: dd/MM HH:mm
    }

    [Fact]
    public void IsPinned_UpdatesAndRaisesPropertyChanged()
    {
        // Arrange
        var viewModel = new RecentProfileRowViewModel(_testRecent, _testProfile, 0);
        var propertyChanged = false;
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(RecentProfileRowViewModel.IsPinned))
                propertyChanged = true;
        };

        // Act
        viewModel.IsPinned = true;

        // Assert
        Assert.True(viewModel.IsPinned);
        Assert.True(propertyChanged);
    }

    [Fact]
    public void IsPinned_UpdatesPinGlyph()
    {
        // Arrange
        var viewModel = new RecentProfileRowViewModel(_testRecent, _testProfile, 0);
        var pinGlyphChanged = false;
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(RecentProfileRowViewModel.PinGlyph))
                pinGlyphChanged = true;
        };

        // Act
        viewModel.IsPinned = true;

        // Assert
        Assert.True(pinGlyphChanged);
        Assert.Equal("📌", viewModel.PinGlyph);
    }

    [Fact]
    public void PinGlyph_ReturnsUnpinnedGlyphByDefault()
    {
        // Arrange
        var viewModel = new RecentProfileRowViewModel(_testRecent, _testProfile, 0);

        // Act
        var glyph = viewModel.PinGlyph;

        // Assert
        Assert.Equal("📍", glyph);
    }

    [Fact]
    public void PinGlyph_ReturnsPinnedGlyphWhenPinned()
    {
        // Arrange
        var pinnedRecent = new RecentProfile(_testProfile.Id, _testProfile.Name, _testProfile.UserDataDirectory, DateTime.UtcNow, 1, true);
        var viewModel = new RecentProfileRowViewModel(pinnedRecent, _testProfile, 0);

        // Act
        var glyph = viewModel.PinGlyph;

        // Assert
        Assert.Equal("📌", glyph);
    }

    [Fact]
    public void IsPinned_WhenSetToSameValue_DoesNotRaisePropertyChanged()
    {
        // Arrange
        var viewModel = new RecentProfileRowViewModel(_testRecent, _testProfile, 0);
        viewModel.IsPinned = true;

        var propertyChangedCount = 0;
        viewModel.PropertyChanged += (s, e) => propertyChangedCount++;

        // Act
        viewModel.IsPinned = true; // Same value

        // Assert
        Assert.Equal(0, propertyChangedCount);
    }
}
