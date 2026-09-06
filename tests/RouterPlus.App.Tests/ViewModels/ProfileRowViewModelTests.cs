using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// TDD tests for ProfileRowViewModel core functionality
/// Complements ProfileRowViewModelHealthTests with property and method tests
/// </summary>
public sealed class ProfileRowViewModelTests
{
    private readonly ChromeProfile _testProfile;
    private readonly List<ProviderDefinition> _providers;

    public ProfileRowViewModelTests()
    {
        _testProfile = new ChromeProfile("test-id", "Profile 1", "Default", "C:\\UserData", false);
        _providers = new List<ProviderDefinition>
        {
            new ProviderDefinition(ProviderKind.Codex, "Codex", "/dashboard", "https://codex.example", WorkflowKind.OAuth, false),
            new ProviderDefinition(ProviderKind.OpenRouter, "OpenRouter", "/dashboard", "https://openrouter.example", WorkflowKind.ApiKey, false),
            new ProviderDefinition(ProviderKind.Kiro, "Kiro", "/dashboard", "https://kiro.example", WorkflowKind.DeviceCode, false)
        };
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);

        // Assert
        Assert.Equal(_testProfile, viewModel.Profile);
        Assert.Equal("Profile 1", viewModel.Name);
        Assert.Equal("Default", viewModel.DirectoryName);
        Assert.Equal(3, viewModel.ProviderStatuses.Count);
    }

    [Fact]
    public void Constructor_ThrowsWhenProfileIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProfileRowViewModel(null!, _providers));
    }

    [Fact]
    public void Constructor_ThrowsWhenProvidersIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProfileRowViewModel(_testProfile, null!));
    }

    [Fact]
    public void Initial_ReturnsFirstCharacterUppercase()
    {
        // Arrange
        var profile = new ChromeProfile("test-id", "Profile 1", "Default", "C:\\UserData", false);
        var viewModel = new ProfileRowViewModel(profile, _providers);

        // Act
        var initial = viewModel.Initial;

        // Assert
        Assert.Equal("P", initial);
    }

    [Fact]
    public void Initial_ReturnsQuestionMarkForEmptyName()
    {
        // Arrange
        var profile = new ChromeProfile("test-id", "", "Default", "C:\\UserData", false);
        var viewModel = new ProfileRowViewModel(profile, _providers);

        // Act
        var initial = viewModel.Initial;

        // Assert
        Assert.Equal("?", initial);
    }

    [Fact]
    public void IsSelected_DefaultsFalse()
    {
        // Arrange & Act
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);

        // Assert
        Assert.False(viewModel.IsSelected);
    }

    [Fact]
    public void IsSelected_WhenChanged_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var propertyChangedRaised = false;
        viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(ProfileRowViewModel.IsSelected))
                propertyChangedRaised = true;
        };

        // Act
        viewModel.IsSelected = true;

        // Assert
        Assert.True(propertyChangedRaised);
        Assert.True(viewModel.IsSelected);
    }

    [Fact]
    public void IsSelected_WhenChanged_RaisesSelectionChanged()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var selectionChangedRaised = false;
        viewModel.SelectionChanged += (sender, e) => selectionChangedRaised = true;

        // Act
        viewModel.IsSelected = true;

        // Assert
        Assert.True(selectionChangedRaised);
    }

    [Fact]
    public void IsSelected_WhenSetToSameValue_DoesNotRaiseEvents()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        viewModel.IsSelected = true;

        var propertyChangedCount = 0;
        var selectionChangedCount = 0;
        viewModel.PropertyChanged += (sender, e) => propertyChangedCount++;
        viewModel.SelectionChanged += (sender, e) => selectionChangedCount++;

        // Act
        viewModel.IsSelected = true; // Same value

        // Assert
        Assert.Equal(0, propertyChangedCount);
        Assert.Equal(0, selectionChangedCount);
    }

    [Fact]
    public void SetDisplayIndex_UpdatesDisplayIndex()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);

        // Act
        viewModel.SetDisplayIndex(5);

        // Assert
        Assert.Equal(5, viewModel.DisplayIndex);
    }

    [Fact]
    public void SetDisplayIndex_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var propertyChangedRaised = false;
        viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(ProfileRowViewModel.DisplayIndex))
                propertyChangedRaised = true;
        };

        // Act
        viewModel.SetDisplayIndex(3);

        // Assert
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void ConnectedProviderCount_InitiallyZero()
    {
        // Arrange & Act
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);

        // Assert
        Assert.Equal(0, viewModel.ConnectedProviderCount);
    }

    [Fact]
    public void UpdateConnections_UpdatesProviderStatuses()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var connections = new[]
        {
            new ProviderConnection("conn-id", ProviderKind.Codex, "Profile 1", 1, true)
        };

        // Act
        viewModel.UpdateConnections(connections);

        // Assert
        var codexStatus = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Codex);
        Assert.True(codexStatus.IsConnected);
        Assert.Equal(1, viewModel.ConnectedProviderCount);
    }

    [Fact]
    public void UpdateConnections_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var propertyChangedNames = new List<string>();
        viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName != null)
                propertyChangedNames.Add(e.PropertyName);
        };

        var connections = new[]
        {
            new ProviderConnection("conn-id", ProviderKind.Codex, "Profile 1", 1, true)
        };

        // Act
        viewModel.UpdateConnections(connections);

        // Assert
        Assert.Contains(nameof(ProfileRowViewModel.ConnectedProviderCount), propertyChangedNames);
        Assert.Contains(nameof(ProfileRowViewModel.ConnectionSummary), propertyChangedNames);
    }

    [Fact]
    public void MarkStatusUnknown_ResetsAllProviders()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var connections = new[]
        {
            new ProviderConnection("conn-3", ProviderKind.Codex, "Profile 1", 1, true)
        };
        viewModel.UpdateConnections(connections);

        // Act
        viewModel.MarkStatusUnknown();

        // Assert
        Assert.All(viewModel.ProviderStatuses, status => Assert.False(status.IsKnown));
        Assert.Equal(0, viewModel.ConnectedProviderCount);
    }

    [Fact]
    public void ConnectionSummary_NoConnections_ReturnsNotConnected()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        viewModel.UpdateConnections(Array.Empty<ProviderConnection>());

        // Act
        var summary = viewModel.ConnectionSummary;

        // Assert
        Assert.Equal("Chưa có provider được gán", summary);
    }

    [Fact]
    public void IsCheckingHealth_DefaultsFalse()
    {
        // Arrange & Act
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);

        // Assert
        Assert.False(viewModel.IsCheckingHealth);
    }

    [Fact]
    public void HasHealthIssues_InitiallyFalse()
    {
        // Arrange & Act
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);

        // Assert
        Assert.False(viewModel.HasHealthIssues);
    }
}
