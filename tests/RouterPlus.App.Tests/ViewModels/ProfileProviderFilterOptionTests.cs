using RouterPlus.App.ViewModels;
using RouterPlus.Core.Providers;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// TDD tests for ProfileProviderFilterOption
/// Filter option for profiles by provider presence (Has/NotHas/Off)
/// </summary>
public sealed class ProfileProviderFilterOptionTests
{
    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter by Codex");

        // Assert
        Assert.Equal(ProviderKind.Codex, option.Kind);
        Assert.Equal("Codex", option.DisplayName);
        Assert.Equal("CX", option.Glyph);
        Assert.Equal("Filter by Codex", option.Tooltip);
        Assert.Equal(0, option.ProfileCount);
        Assert.Equal("Codex", option.DisplayNameWithCount);
        Assert.False(option.IsSelected);
        Assert.Equal(ProviderFilterState.Off, option.FilterState);
    }

    [Fact]
    public void Constructor_AcceptsNullKind()
    {
        // Arrange & Act
        var option = new ProfileProviderFilterOption(null, "All", "*", "Show all");

        // Assert
        Assert.Null(option.Kind);
        Assert.Equal("All", option.DisplayName);
    }

    [Fact]
    public void SetProfileCount_UpdatesProfileCount()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");

        // Act
        option.SetProfileCount(5);

        // Assert
        Assert.Equal(5, option.ProfileCount);
    }

    [Fact]
    public void SetProfileCount_ClampsNegativeToZero()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");

        // Act
        option.SetProfileCount(-1);

        // Assert
        Assert.Equal(0, option.ProfileCount);
    }

    [Fact]
    public void SetProfileCount_RaisesPropertyChanged()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        var changedProperties = new List<string>();
        option.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
                changedProperties.Add(e.PropertyName);
        };

        // Act
        option.SetProfileCount(3);

        // Assert
        Assert.Contains(nameof(ProfileProviderFilterOption.ProfileCount), changedProperties);
        Assert.Contains(nameof(ProfileProviderFilterOption.DisplayNameWithCount), changedProperties);
    }

    [Fact]
    public void SetProfileCounts_UpdatesDisplayNameWithCount()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");

        // Act
        option.SetProfileCounts(hasCount: 5, notHasCount: 3);
        option.FilterState = ProviderFilterState.Has;

        // Assert
        Assert.Equal("Codex (5)", option.DisplayNameWithCount);
    }

    [Fact]
    public void SetProfileCounts_ClampsNegativesToZero()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");

        // Act
        option.SetProfileCounts(hasCount: -1, notHasCount: -2);
        option.FilterState = ProviderFilterState.Has;

        // Assert
        Assert.Equal("Codex", option.DisplayNameWithCount); // 0 count shows no count
    }

    [Fact]
    public void DisplayNameWithCount_ShowsHasCountWhenStateIsHas()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        option.SetProfileCounts(hasCount: 5, notHasCount: 3);

        // Act
        option.FilterState = ProviderFilterState.Has;

        // Assert
        Assert.Equal("Codex (5)", option.DisplayNameWithCount);
    }

    [Fact]
    public void DisplayNameWithCount_ShowsNotHasCountWhenStateIsNotHas()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        option.SetProfileCounts(hasCount: 5, notHasCount: 3);

        // Act
        option.FilterState = ProviderFilterState.NotHas;

        // Assert
        Assert.Equal("Codex (3)", option.DisplayNameWithCount);
    }

    [Fact]
    public void DisplayNameWithCount_ShowsHasCountWhenStateIsOff()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        option.SetProfileCounts(hasCount: 5, notHasCount: 3);

        // Act
        option.FilterState = ProviderFilterState.Off;

        // Assert
        Assert.Equal("Codex (5)", option.DisplayNameWithCount); // Default Off shows Has count
    }

    [Fact]
    public void DisplayNameWithCount_ShowsNoCountWhenZero()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        option.SetProfileCounts(hasCount: 0, notHasCount: 0);

        // Act
        option.FilterState = ProviderFilterState.Has;

        // Assert
        Assert.Equal("Codex", option.DisplayNameWithCount);
    }

    [Fact]
    public void IsSelected_UpdatesAndRaisesPropertyChanged()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        var propertyChanged = false;
        option.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ProfileProviderFilterOption.IsSelected))
                propertyChanged = true;
        };

        // Act
        option.IsSelected = true;

        // Assert
        Assert.True(option.IsSelected);
        Assert.True(propertyChanged);
    }

    [Fact]
    public void IsSelected_DoesNotRaisePropertyChangedWhenSameValue()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        option.IsSelected = true;
        var eventRaisedCount = 0;
        option.PropertyChanged += (s, e) => eventRaisedCount++;

        // Act
        option.IsSelected = true; // Same value

        // Assert
        Assert.Equal(0, eventRaisedCount);
    }

    [Fact]
    public void FilterState_UpdatesAndRaisesMultiplePropertyChanged()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        var changedProperties = new List<string>();
        option.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
                changedProperties.Add(e.PropertyName);
        };

        // Act
        option.FilterState = ProviderFilterState.Has;

        // Assert
        Assert.Equal(ProviderFilterState.Has, option.FilterState);
        Assert.Contains(nameof(ProfileProviderFilterOption.FilterState), changedProperties);
        Assert.Contains(nameof(ProfileProviderFilterOption.IsSelected), changedProperties);
        Assert.Contains(nameof(ProfileProviderFilterOption.ButtonBackground), changedProperties);
        Assert.Contains(nameof(ProfileProviderFilterOption.ButtonBorderBrush), changedProperties);
        Assert.Contains(nameof(ProfileProviderFilterOption.ButtonForeground), changedProperties);
        Assert.Contains(nameof(ProfileProviderFilterOption.DisplayNameWithCount), changedProperties);
    }

    [Fact]
    public void FilterState_UpdatesIsSelectedToTrue_WhenNotOff()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");

        // Act
        option.FilterState = ProviderFilterState.Has;

        // Assert
        Assert.True(option.IsSelected);
    }

    [Fact]
    public void FilterState_UpdatesIsSelectedToFalse_WhenOff()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        option.FilterState = ProviderFilterState.Has;

        // Act
        option.FilterState = ProviderFilterState.Off;

        // Assert
        Assert.False(option.IsSelected);
    }

    [Fact]
    public void FilterState_DoesNotRaisePropertyChangedWhenSameValue()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        option.FilterState = ProviderFilterState.Has;
        var eventRaisedCount = 0;
        option.PropertyChanged += (s, e) => eventRaisedCount++;

        // Act
        option.FilterState = ProviderFilterState.Has; // Same value

        // Assert
        Assert.Equal(0, eventRaisedCount);
    }

    [Fact]
    public void CycleFilterState_CyclesFromOffToHas()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");

        // Act
        option.CycleFilterState();

        // Assert
        Assert.Equal(ProviderFilterState.Has, option.FilterState);
    }

    [Fact]
    public void CycleFilterState_CyclesFromHasToNotHas()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        option.FilterState = ProviderFilterState.Has;

        // Act
        option.CycleFilterState();

        // Assert
        Assert.Equal(ProviderFilterState.NotHas, option.FilterState);
    }

    [Fact]
    public void CycleFilterState_CyclesFromNotHasToOff()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        option.FilterState = ProviderFilterState.NotHas;

        // Act
        option.CycleFilterState();

        // Assert
        Assert.Equal(ProviderFilterState.Off, option.FilterState);
    }

    [Fact]
    public void CycleFilterState_CompletesCycle()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");

        // Act - Complete full cycle
        option.CycleFilterState(); // Off → Has
        option.CycleFilterState(); // Has → NotHas
        option.CycleFilterState(); // NotHas → Off
        option.CycleFilterState(); // Off → Has again

        // Assert
        Assert.Equal(ProviderFilterState.Has, option.FilterState);
    }

    [Fact]
    public void SetProfileCount_DoesNotRaisePropertyChangedWhenValueIsUnchanged()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        var eventRaisedCount = 0;
        option.PropertyChanged += (s, e) => eventRaisedCount++;

        // Act
        option.SetProfileCount(0);

        // Assert
        Assert.Equal(0, eventRaisedCount);
    }

    [Fact]
    public void SetProfileCounts_DoesNotRaisePropertyChangedWhenValuesAreUnchanged()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        var eventRaisedCount = 0;
        option.PropertyChanged += (s, e) => eventRaisedCount++;

        // Act
        option.SetProfileCounts(hasCount: 0, notHasCount: 0);

        // Assert
        Assert.Equal(0, eventRaisedCount);
    }

    [Fact]
    public void CycleFilterState_ResetsUnknownStateToOff()
    {
        // Arrange
        var option = new ProfileProviderFilterOption(ProviderKind.Codex, "Codex", "CX", "Filter");
        option.FilterState = (ProviderFilterState)99;

        // Act
        option.CycleFilterState();

        // Assert
        Assert.Equal(ProviderFilterState.Off, option.FilterState);
    }
}
