using RouterPlus.App.ViewModels;
using RouterPlus.Core.Providers;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// TDD tests for ProviderCardViewModel
/// ViewModel for provider connection cards with API key management
/// </summary>
public sealed class ProviderCardViewModelTests
{
    private readonly ProviderDefinition _testDefinition;

    public ProviderCardViewModelTests()
    {
        _testDefinition = new ProviderDefinition(
            ProviderKind.OpenRouter,
            "OpenRouter",
            "/dashboard",
            "https://openrouter.ai",
            WorkflowKind.ApiKey,
            false);
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        var viewModel = new ProviderCardViewModel(_testDefinition);

        // Assert
        Assert.Equal(_testDefinition, viewModel.Definition);
        Assert.Equal(ProviderKind.OpenRouter, viewModel.Kind);
        Assert.Equal("OpenRouter", viewModel.DisplayName);
        Assert.Equal(WorkflowKind.ApiKey, viewModel.Workflow);
        Assert.Equal(ProviderHealthState.Unknown, viewModel.HealthState);
        Assert.False(viewModel.IsWorkflowInProgress);
    }

    [Fact]
    public void Constructor_ThrowsWhenDefinitionIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ProviderCardViewModel(null!));
    }

    [Theory]
    [InlineData(ProviderKind.Codex, "CX")]
    [InlineData(ProviderKind.Kiro, "KI")]
    [InlineData(ProviderKind.OpenRouter, "OR")]
    [InlineData(ProviderKind.Ollama, "OL")]
    [InlineData(ProviderKind.Kimchi, "KM")]
    public void ShortCode_ReturnsCorrectCodeForProvider(ProviderKind kind, string expectedCode)
    {
        // Arrange
        var definition = new ProviderDefinition(kind, "Test", "/dash", "https://test.com", WorkflowKind.OAuth, false);
        var viewModel = new ProviderCardViewModel(definition);

        // Act
        var shortCode = viewModel.ShortCode;

        // Assert
        Assert.Equal(expectedCode, shortCode);
    }

    [Fact]
    public void IsOpenRouter_ReturnsTrueForOpenRouter()
    {
        // Arrange
        var definition = new ProviderDefinition(ProviderKind.OpenRouter, "OpenRouter", "/d", "https://or.ai", WorkflowKind.ApiKey, false);
        var viewModel = new ProviderCardViewModel(definition);

        // Act & Assert
        Assert.True(viewModel.IsOpenRouter);
    }

    [Fact]
    public void IsOpenRouter_ReturnsFalseForOtherProviders()
    {
        // Arrange
        var definition = new ProviderDefinition(ProviderKind.Codex, "Codex", "/d", "https://cx.ai", WorkflowKind.OAuth, false);
        var viewModel = new ProviderCardViewModel(definition);

        // Act & Assert
        Assert.False(viewModel.IsOpenRouter);
    }

    [Fact]
    public void ApiKeyValue_CanBeSetAndRetrieved()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);

        // Act
        viewModel.ApiKeyValue = "test-api-key";

        // Assert
        Assert.Equal("test-api-key", viewModel.ApiKeyValue);
    }

    [Fact]
    public void ApiKeyValue_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);
        var propertyChanged = false;
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ProviderCardViewModel.ApiKeyValue))
                propertyChanged = true;
        };

        // Act
        viewModel.ApiKeyValue = "test-key";

        // Assert
        Assert.True(propertyChanged);
    }

    [Fact]
    public void LoadSavedApiKey_LoadsValue()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);

        // Act
        viewModel.LoadSavedApiKey("saved-key");

        // Assert
        Assert.True(viewModel.HasSavedApiKey);
    }

    [Fact]
    public void LoadSavedApiKey_WithNull_DoesNotSetSavedKey()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);

        // Act
        viewModel.LoadSavedApiKey(null);

        // Assert
        Assert.False(viewModel.HasSavedApiKey);
    }

    [Fact]
    public void MarkApiKeySaved_UpdatesHasSavedApiKey()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);
        viewModel.ApiKeyValue = "test-key";

        // Act
        viewModel.MarkApiKeySaved();

        // Assert
        Assert.True(viewModel.HasSavedApiKey);
    }

    [Fact]
    public void ToggleApiKeyVisibility_TogglesVisibility()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);
        var initialVisibility = viewModel.IsApiKeyVisible;

        // Act
        viewModel.ToggleApiKeyVisibility();

        // Assert
        Assert.NotEqual(initialVisibility, viewModel.IsApiKeyVisible);
    }

    [Fact]
    public void UpdateProviderStatus_UpdatesHealthState()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);
        var status = new ProfileProviderStatusViewModel(_testDefinition);
        status.SetConnectionCount(1, ProviderHealthState.Healthy, null, null);

        // Act
        viewModel.UpdateProviderStatus(status);

        // Assert
        Assert.Equal(ProviderHealthState.Healthy, viewModel.HealthState);
    }

    [Fact]
    public void UpdateProviderStatus_RaisesMultiplePropertyChanged()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);
        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
                changedProperties.Add(e.PropertyName);
        };

        var status = new ProfileProviderStatusViewModel(_testDefinition);
        status.SetConnectionCount(1, ProviderHealthState.Healthy, null, null);

        // Act
        viewModel.UpdateProviderStatus(status);

        // Assert
        Assert.Contains(nameof(ProviderCardViewModel.HealthState), changedProperties);
        Assert.Contains(nameof(ProviderCardViewModel.StatusLabel), changedProperties);
        Assert.Contains(nameof(ProviderCardViewModel.StatusTooltip), changedProperties);
        Assert.Contains(nameof(ProviderCardViewModel.IsHealthy), changedProperties);
    }

    [Fact]
    public void IsHealthy_ReturnsTrueWhenHealthy()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);
        var status = new ProfileProviderStatusViewModel(_testDefinition);
        status.SetConnectionCount(1, ProviderHealthState.Healthy, null, null);

        // Act
        viewModel.UpdateProviderStatus(status);

        // Assert
        Assert.True(viewModel.IsHealthy);
    }

    [Fact]
    public void HasError_ReturnsTrueWhenError()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);
        var status = new ProfileProviderStatusViewModel(_testDefinition);
        status.SetConnectionCount(1, ProviderHealthState.Error, null, "Test error");

        // Act
        viewModel.UpdateProviderStatus(status);

        // Assert
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public void IsDisabled_ReturnsTrueWhenDisabled()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);
        var status = new ProfileProviderStatusViewModel(_testDefinition);
        status.SetConnectionCount(0, ProviderHealthState.Disabled, null, null);

        // Act
        viewModel.UpdateProviderStatus(status);

        // Assert
        Assert.True(viewModel.IsDisabled);
    }

    [Fact]
    public void IsMissing_ReturnsTrueWhenMissing()
    {
        // Arrange
        var viewModel = new ProviderCardViewModel(_testDefinition);
        var status = new ProfileProviderStatusViewModel(_testDefinition);
        status.SetConnectionCount(0, ProviderHealthState.Missing, null, null);

        // Act
        viewModel.UpdateProviderStatus(status);

        // Assert
        Assert.True(viewModel.IsMissing);
    }

    [Fact]
    public void IsUnknown_ReturnsTrueByDefault()
    {
        // Arrange & Act
        var viewModel = new ProviderCardViewModel(_testDefinition);

        // Assert
        Assert.True(viewModel.IsUnknown);
    }

    [Fact]
    public void Connections_DefaultsToEmpty()
    {
        // Arrange & Act
        var viewModel = new ProviderCardViewModel(_testDefinition);

        // Assert
        Assert.Empty(viewModel.Connections);
    }

    [Fact]
    public void QuotaRows_DefaultsToEmpty()
    {
        // Arrange & Act
        var viewModel = new ProviderCardViewModel(_testDefinition);

        // Assert
        Assert.Empty(viewModel.QuotaRows);
    }

    [Fact]
    public void IsWorkflowInProgress_DefaultsToFalse()
    {
        // Arrange & Act
        var viewModel = new ProviderCardViewModel(_testDefinition);

        // Assert
        Assert.False(viewModel.IsWorkflowInProgress);
    }
}
