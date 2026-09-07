using RouterPlus.Core.Security;

namespace RouterPlus.Core.Tests;

public sealed class ProviderApiKeyStateTests
{
    [Fact]
    public void State_starts_masked_and_tracks_saved_value()
    {
        var state = new ProviderApiKeyState();

        Assert.False(state.IsVisible);
        Assert.Equal("Hiện key", state.ToggleText);
        Assert.False(state.HasSavedKey);

        state.SetValue("or-key");
        Assert.False(state.HasSavedKey);

        state.MarkSaved();
        Assert.True(state.HasSavedKey);
        Assert.Equal("Đã lưu cục bộ", state.StatusText);
    }

    [Fact]
    public void Loading_saved_value_resets_visibility_and_detects_changes()
    {
        var state = new ProviderApiKeyState();
        state.ToggleVisibility();
        state.LoadSaved("ollama-key");

        Assert.Equal("ollama-key", state.Value);
        Assert.False(state.IsVisible);
        Assert.Equal("Hiện key", state.ToggleText);
        Assert.True(state.HasSavedKey);

        state.SetValue("changed-key");

        Assert.False(state.HasSavedKey);
        Assert.Equal("Key chưa lưu", state.StatusText);
    }

    [Fact]
    public void LoadSaved_null_clears_key_and_reports_missing_key()
    {
        // Arrange
        var state = new ProviderApiKeyState();
        state.SetValue("temporary-key");
        state.ToggleVisibility();

        // Act
        state.LoadSaved(null);

        // Assert
        Assert.Equal(string.Empty, state.Value);
        Assert.False(state.IsVisible);
        Assert.False(state.HasSavedKey);
        Assert.Equal("Hiện key", state.ToggleText);
        Assert.Equal("Chưa có key", state.StatusText);
    }

    [Fact]
    public void SetValue_null_clears_unsaved_key_and_notifies_dependent_properties()
    {
        // Arrange
        var state = new ProviderApiKeyState();
        state.LoadSaved("saved-key");
        var changedProperties = new List<string?>();
        state.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        // Act
        state.SetValue(null);

        // Assert
        Assert.Equal(string.Empty, state.Value);
        Assert.False(state.HasSavedKey);
        Assert.Equal("Chưa có key", state.StatusText);
        Assert.Equal(
            new[] { nameof(state.Value), nameof(state.HasSavedKey), nameof(state.StatusText) },
            changedProperties);
    }

    [Fact]
    public void SetValue_same_value_does_not_raise_property_changed()
    {
        // Arrange
        var state = new ProviderApiKeyState();
        state.SetValue("same-key");
        var changed = false;
        state.PropertyChanged += (_, _) => changed = true;

        // Act
        state.SetValue("same-key");

        // Assert
        Assert.False(changed);
        Assert.Equal("same-key", state.Value);
        Assert.Equal("Key chưa lưu", state.StatusText);
    }

    [Fact]
    public void MarkSaved_when_already_saved_does_not_raise_property_changed()
    {
        // Arrange
        var state = new ProviderApiKeyState();
        state.LoadSaved("saved-key");
        var changed = false;
        state.PropertyChanged += (_, _) => changed = true;

        // Act
        state.MarkSaved();

        // Assert
        Assert.False(changed);
        Assert.True(state.HasSavedKey);
        Assert.Equal("Đã lưu cục bộ", state.StatusText);
    }

    [Fact]
    public void ToggleVisibility_notifies_visibility_and_toggle_text_each_time()
    {
        // Arrange
        var state = new ProviderApiKeyState();
        var changedProperties = new List<string?>();
        state.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        // Act
        state.ToggleVisibility();
        state.ToggleVisibility();

        // Assert
        Assert.False(state.IsVisible);
        Assert.Equal("Hiện key", state.ToggleText);
        Assert.Equal(
            new[]
            {
                nameof(state.IsVisible), nameof(state.ToggleText),
                nameof(state.IsVisible), nameof(state.ToggleText)
            },
            changedProperties);
    }
}
