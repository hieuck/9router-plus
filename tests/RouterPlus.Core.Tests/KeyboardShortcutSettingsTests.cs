using RouterPlus.App.ViewModels;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests;

public sealed class KeyboardShortcutSettingsTests
{
    [Fact]
    public void RouterSettings_defaults_to_disabled_shortcuts()
    {
        var settings = new RouterSettings();

        Assert.False(settings.EnableKeyboardShortcuts);
        Assert.Null(settings.KeyboardShortcuts);
    }

    [Fact]
    public async Task SettingsStore_round_trips_shortcut_settings()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new SettingsStore(path);
            await store.SaveAsync(new RouterSettings(
                DashboardBaseUrl: "http://127.0.0.1:1",
                EnableKeyboardShortcuts: true,
                KeyboardShortcuts: new Dictionary<string, string>
                {
                    ["OpenQuickLaunch"] = "Alt+Q",
                    ["OpenProviderCodex"] = "Ctrl+Alt+1"
                }));

            var loaded = await store.LoadAsync();

            Assert.True(loaded.EnableKeyboardShortcuts);
            Assert.Equal("Alt+Q", loaded.KeyboardShortcuts!["OpenQuickLaunch"]);
            Assert.Equal("Ctrl+Alt+1", loaded.KeyboardShortcuts!["OpenProviderCodex"]);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task SettingsStore_round_trips_quota_auto_disable_markers()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var marker = new QuotaAutoDisableMarker(
                "codex-1",
                ProviderKind.Codex,
                "Work",
                DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
            var store = new SettingsStore(path);

            await store.SaveAsync(new RouterSettings(QuotaAutoDisableMarkers: [marker]));

            var loaded = await store.LoadAsync();

            var restored = Assert.Single(loaded.QuotaAutoDisableMarkers!);
            Assert.Equal(marker, restored);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void ShortcutService_parses_modifiers_and_key()
    {
        Assert.True(KeyboardShortcutService.TryParse("Ctrl+Alt+1", out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal("1", parsed!.KeyName);
        Assert.Equal(3, parsed.ModifierFlags); // Ctrl(1) + Alt(2)
        Assert.Equal("Ctrl+Alt+1", parsed.DisplayValue);
    }

    [Fact]
    public void ShortcutService_rejects_invalid_gestures()
    {
        Assert.False(KeyboardShortcutService.TryParse("", out _));
        Assert.False(KeyboardShortcutService.TryParse("Ctrl+", out _));
        Assert.False(KeyboardShortcutService.TryParse("NonExistentKey", out _));
        Assert.False(KeyboardShortcutService.TryParse("Ctrl+Alt+1+2", out _));
    }

    [Fact]
    public void ShortcutService_accepts_function_and_number_keys()
    {
        Assert.True(KeyboardShortcutService.TryParse("F5", out var f5));
        Assert.Equal("F5", f5!.KeyName);

        Assert.True(KeyboardShortcutService.TryParse("Ctrl+0", out var ctrl0));
        Assert.Equal("0", ctrl0!.KeyName);
    }

    [Fact]
    public void ShortcutBindingsViewModel_detects_duplicate_gestures()
    {
        var bindings = new ShortcutBindingsViewModel();
        Assert.Null(bindings.ValidateAndApply("OpenQuickLaunch", "Alt+Q"));

        var duplicateError = bindings.ValidateAndApply("SaveSettings", "Alt+Q");

        Assert.NotNull(duplicateError);
        Assert.Contains("đã được dùng", duplicateError);
    }

    [Fact]
    public void ShortcutBindingsViewModel_rejects_invalid_gesture()
    {
        var bindings = new ShortcutBindingsViewModel();

        var error = bindings.ValidateAndApply("OpenQuickLaunch", "BadGesture!");

        Assert.NotNull(error);
        Assert.Contains("không hợp lệ", error);
    }

    [Fact]
    public void ShortcutBindingsViewModel_reset_restores_defaults()
    {
        var bindings = new ShortcutBindingsViewModel();
        bindings.ValidateAndApply("OpenQuickLaunch", "Alt+Q");
        Assert.Equal("Alt+Q", bindings.Rows.First(r => r.ActionId == "OpenQuickLaunch").Gesture);

        bindings.ResetAll();

        Assert.Equal("Ctrl+Shift+K", bindings.Rows.First(r => r.ActionId == "OpenQuickLaunch").Gesture);
        Assert.Empty(bindings.Overrides);
    }

    [Fact]
    public void MainViewModel_toggle_is_false_by_default()
    {
        var viewModel = new MainViewModel();

        Assert.False(viewModel.IsKeyboardShortcutsEnabled);
    }

    [Fact]
    public async Task MainViewModel_apply_shortcut_updates_row_and_persists()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var viewModel = new MainViewModel(new SettingsStore(path));

            var row = viewModel.ShortcutRows.Single(r => r.ActionId == "OpenQuickLaunch");
            row.Gesture = "Alt+Q";
            viewModel.ApplyShortcutCommand.Execute("OpenQuickLaunch");

            Assert.Equal("Alt+Q", row.Gesture);

            RouterSettings? persisted = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (persisted?.KeyboardShortcuts?.GetValueOrDefault("OpenQuickLaunch") != "Alt+Q"
                   && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
                persisted = new SettingsStore(path).Load();
            }

            Assert.NotNull(persisted);
            Assert.Equal("Alt+Q", persisted!.KeyboardShortcuts!["OpenQuickLaunch"]);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RouterPlusShortcutTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

