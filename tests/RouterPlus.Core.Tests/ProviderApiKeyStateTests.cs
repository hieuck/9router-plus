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
}
