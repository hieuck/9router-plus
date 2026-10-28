using System;
using RouterPlus.App.Tests.TestHelpers;
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
            new ProviderDefinition(ProviderKind.Codex, "Codex", "/dashboard", "https://codex.example", WorkflowKind.OAuth, false, "codex", "Codex", "✨", "CX"),
            new ProviderDefinition(ProviderKind.Kiro, "Kiro", "/dashboard", "https://kiro.example", WorkflowKind.DeviceCode, false, "kiro", "Kiro", "🚀", "KI"),
            new ProviderDefinition(ProviderKind.OpenRouter, "OpenRouter", "/dashboard", "https://openrouter.example", WorkflowKind.ApiKey, false, "openrouter", "OpenR", "🧠", "OR"),
            new ProviderDefinition(ProviderKind.Ollama, "Ollama Cloud", "/dashboard", "https://ollama.example", WorkflowKind.ApiKey, false, "ollama", "Ollama", "🦙", "OL"),
            new ProviderDefinition(ProviderKind.Nous, "Nous", "/dashboard", "https://nous.example", WorkflowKind.DeviceCode, false, "nous", "Nous", "🔭", "NS"),
            new ProviderDefinition(ProviderKind.GitHub, "GitHub", "/dashboard", "https://github.example", WorkflowKind.OAuth, false, "github", "GitHub", "🐙", "GH"),
            new ProviderDefinition(ProviderKind.Agnes, "Agnes AI", "/dashboard", "https://agnes.example", WorkflowKind.ApiKey, false, "agnes", "Agnes", "✨", "AG")
        };
    }

    private static ProviderConnection MakeConnection(
        string id,
        ProviderKind provider,
        string? name,
        int priority,
        bool isActive = true,
        string? email = null)
        => new ProviderConnection(id, provider, name, priority, isActive, email);

    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);

        // Assert
        Assert.Equal(_testProfile, viewModel.Profile);
        Assert.Equal("Profile 1", viewModel.Name);
        Assert.Equal("Default", viewModel.DirectoryName);
        Assert.Equal(7, viewModel.ProviderStatuses.Count);
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
            TestData.CreateConnection(ProviderKind.Codex)
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
            TestData.CreateConnection(ProviderKind.Codex)
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
            TestData.CreateConnection(ProviderKind.Codex)
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
    public void HasLostConnection_FirstEmptyAfterConnection_IsPendingNotLost()
    {
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var connections = new[] { TestData.CreateConnection(ProviderKind.Codex) };
        viewModel.UpdateConnections(connections);
        viewModel.UpdateConnections(Array.Empty<ProviderConnection>());

        var status = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Codex);
        Assert.False(status.HasLostConnection);
    }

    [Fact]
    public void HasLostConnection_SecondConsecutiveEmpty_IsLost()
    {
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var connections = new[] { TestData.CreateConnection(ProviderKind.Codex) };
        viewModel.UpdateConnections(connections);
        viewModel.UpdateConnections(Array.Empty<ProviderConnection>());
        viewModel.UpdateConnections(Array.Empty<ProviderConnection>());

        var status = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Codex);
        Assert.True(status.HasLostConnection);
        Assert.Contains("Mất", viewModel.ConnectionSummary);
    }

    [Fact]
    public void HasLostConnection_ReAddClears()
    {
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var connections = new[] { TestData.CreateConnection(ProviderKind.Codex) };
        viewModel.UpdateConnections(connections);
        viewModel.UpdateConnections(Array.Empty<ProviderConnection>());
        viewModel.UpdateConnections(Array.Empty<ProviderConnection>());
        viewModel.UpdateConnections(connections);

        var status = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Codex);
        Assert.False(status.HasLostConnection);
    }

    [Fact]
    public void HasLostConnection_TransientEmptyThenConnection_NoLost()
    {
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var connections = new[] { TestData.CreateConnection(ProviderKind.Codex) };
        viewModel.UpdateConnections(connections);
        viewModel.UpdateConnections(Array.Empty<ProviderConnection>());
        viewModel.UpdateConnections(connections);

        var status = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Codex);
        Assert.False(status.HasLostConnection);
    }

    [Fact]
    public void HasHealthIssues_InitiallyFalse()
    {
        // Arrange & Act
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);

        // Assert
        Assert.False(viewModel.HasHealthIssues);
    }

    // ----- UpdateConnections tests -----

    [Fact]
    public void UpdateConnections_OnlyMatchingConnectionsAssigned_SortedOrder()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var connections = new[]
        {
            // Codex connections - should match
            MakeConnection("c1", ProviderKind.Codex, "Profile 1", 2),
            MakeConnection("c2", ProviderKind.Codex, "Profile 1", 1),
            // OpenRouter connection - different name, should not match
            MakeConnection("c3", ProviderKind.OpenRouter, "Other Profile", 1),
            // Kiro connection - should match
            MakeConnection("c4", ProviderKind.Kiro, "Profile 1", 1),
        };

        // Act
        viewModel.UpdateConnections(connections);

        // Assert
        var codexStatus = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Codex);
        var openRouterStatus = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.OpenRouter);
        var kiroStatus = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Kiro);

        // Only matching connections should be assigned
        Assert.Equal(2, codexStatus.Connections.Count);
        Assert.Empty(openRouterStatus.Connections);
        Assert.Single(kiroStatus.Connections);

        // Sorted by Priority asc
        Assert.Equal(1, codexStatus.Connections[0].Priority);
        Assert.Equal(2, codexStatus.Connections[1].Priority);
    }

    [Fact]
    public void UpdateConnections_AmbiguousProfileName_ReturnsEmpty()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var ambiguousProfile = new ChromeProfile("other-id", "Profile 1", "Profile 2", "C:\\UserData2", false);
        var allProfiles = new[] { _testProfile, ambiguousProfile };

        // Mark name as ambiguous (simulating what MainViewModel would do)
        viewModel.IsNameAmbiguous = true;

        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "Profile 1", 1),
        };

        // Act
        viewModel.UpdateConnections(connections, allProfiles);

        // Assert
        Assert.All(viewModel.ProviderStatuses, status => Assert.Empty(status.Connections));
        Assert.Equal(0, viewModel.ConnectedProviderCount);
    }

    [Fact]
    public void UpdateConnections_ConnectionEmailMatchesProfileEmailCaseInsensitive_Matches()
    {
        // Arrange
        var emailProfile = new ChromeProfile("email-id", "user@Example.COM", "Default", "C:\\UserData", false);
        var providers = new List<ProviderDefinition>
        {
            new ProviderDefinition(ProviderKind.Codex, "Codex", "/dashboard", "https://codex.example", WorkflowKind.OAuth, false, "codex", "Codex", "✨", "CX"),
        };
        var viewModel = new ProfileRowViewModel(emailProfile, providers);
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "user@Example.COM", 1, email: "user@example.com"),
        };

        // Act
        viewModel.UpdateConnections(connections);

        // Assert
        var codexStatus = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Codex);
        Assert.Single(codexStatus.Connections);
    }

    [Fact]
    public void UpdateConnections_ConnectionEmailDiffersFromProfileEmail_NoMatch()
    {
        // Arrange
        var emailProfile = new ChromeProfile("email-id", "user@Example.COM", "Default", "C:\\UserData", false);
        var providers = new List<ProviderDefinition>
        {
            new ProviderDefinition(ProviderKind.Codex, "Codex", "/dashboard", "https://codex.example", WorkflowKind.OAuth, false, "codex", "Codex", "✨", "CX"),
        };
        var viewModel = new ProfileRowViewModel(emailProfile, providers);
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "user@Example.COM", 1, email: "other@example.com"),
        };

        // Act
        viewModel.UpdateConnections(connections);

        // Assert
        var codexStatus = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Codex);
        Assert.Empty(codexStatus.Connections);
    }

    [Fact]
    public void UpdateConnections_ProfileNameNotEmailLike_EmailCheckSkipped()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "Profile 1", 1, email: "different@example.com"),
        };

        // Act
        viewModel.UpdateConnections(connections);

        // Assert
        var codexStatus = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Codex);
        Assert.Single(codexStatus.Connections);
    }

    [Fact]
    public void UpdateConnections_EmptyConnectionEmail_FallsBackToNameMatch()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var connections = new[]
        {
            MakeConnection("c1", ProviderKind.Codex, "Profile 1", 1, email: ""),
            MakeConnection("c2", ProviderKind.Codex, "Profile 1", 2, email: null),
        };

        // Act
        viewModel.UpdateConnections(connections);

        // Assert
        var codexStatus = viewModel.ProviderStatuses.First(s => s.Definition.Kind == ProviderKind.Codex);
        Assert.Equal(2, codexStatus.Connections.Count);
    }

    [Fact]
    public void ConnectedProviderSummary_InitialValue_IsZeroOverTotal()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);

        // Act
        var summary = viewModel.ConnectedProviderSummary;

        // Assert
        Assert.Equal($"0 / {_providers.Count}", summary);
    }

    [Fact]
    public void ConnectedProviderSummary_AfterUpdateConnections_RaisesPropertyChangedAndUpdatesValue()
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
            TestData.CreateConnection(ProviderKind.Codex)
        };

        // Act
        viewModel.UpdateConnections(connections);

        // Assert
        Assert.Contains(nameof(ProfileRowViewModel.ConnectedProviderSummary), propertyChangedNames);
        Assert.Equal($"1 / {_providers.Count}", viewModel.ConnectedProviderSummary);
    }

    [Fact]
    public void ConnectedProviderSummary_MarkStatusUnknown_RaisesPropertyChangedAndResetsValue()
    {
        // Arrange
        var viewModel = new ProfileRowViewModel(_testProfile, _providers);
        var connections = new[]
        {
            TestData.CreateConnection(ProviderKind.Codex)
        };
        viewModel.UpdateConnections(connections);

        var propertyChangedNames = new List<string>();
        viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName != null)
                propertyChangedNames.Add(e.PropertyName);
        };

        // Act
        viewModel.MarkStatusUnknown();

        // Assert
        Assert.Contains(nameof(ProfileRowViewModel.ConnectedProviderSummary), propertyChangedNames);
        Assert.Equal($"0 / {_providers.Count}", viewModel.ConnectedProviderSummary);
    }
}
