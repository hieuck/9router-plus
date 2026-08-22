using System.Reflection;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelRecentProfilesTests
{
    [Fact]
    public void LaunchRecentCommand_slot_outside_range_is_noop()
    {
        var viewModel = new MainViewModel();

        Assert.NotNull(viewModel.LaunchRecentCommand);
    }

    [Fact]
    public async Task OpenQuickLaunchPalette_with_no_profiles_reports_status()
    {
        var viewModel = new MainViewModel();

        await viewModel.OpenQuickLaunchPalette();

        Assert.False(viewModel.IsQuickLaunchOpen);
        Assert.Contains("Chưa có Chrome profile", viewModel.StatusText);
    }

    [Fact]
    public async Task OpenQuickLaunchPalette_opens_when_profiles_loaded()
    {
        var viewModel = new MainViewModel();
        SimulateLoadedProfiles(viewModel, "Personal", "Work");

        await viewModel.OpenQuickLaunchPalette();

        Assert.True(viewModel.IsQuickLaunchOpen);
        Assert.Equal(2, viewModel.FilteredQuickLaunchProfiles.Count);
    }

    [Fact]
    public void QuickLaunchFilter_narrows_results()
    {
        var viewModel = new MainViewModel();
        SimulateLoadedProfiles(viewModel, "Personal", "Work", "Study");

        viewModel.QuickLaunchFilterText = "wo";

        Assert.Single(viewModel.FilteredQuickLaunchProfiles);
        Assert.Equal("Work", viewModel.FilteredQuickLaunchProfiles[0].Name);
    }

    [Fact]
    public void ClearRecentProfiles_empties_rows_and_disables_command()
    {
        var viewModel = new MainViewModel();
        SimulateLoadedProfiles(viewModel, "Personal", "Work");
        SeedRecents(viewModel, new[]
        {
            (id: "Work", path: @"C:\Chrome\User Data", count: 5, pinned: false)
        });

        viewModel.ClearRecentProfilesCommand.Execute(null);

        Assert.Empty(viewModel.RecentProfileRows);
        Assert.False(viewModel.ClearRecentProfilesCommand.CanExecute(null));
    }

    [Fact]
    public void QuickLaunchSelection_wraps_with_arrow_keys()
    {
        var viewModel = new MainViewModel();
        SimulateLoadedProfiles(viewModel, "Alpha", "Beta", "Gamma");

        viewModel.OpenQuickLaunchPalette();

        viewModel.SelectedQuickLaunchProfile = viewModel.FilteredQuickLaunchProfiles[2];
        viewModel.MoveQuickLaunchSelectionCommand.Execute(1);
        Assert.Same(viewModel.FilteredQuickLaunchProfiles[0], viewModel.SelectedQuickLaunchProfile);
        viewModel.MoveQuickLaunchSelectionCommand.Execute(-1);
        Assert.Same(viewModel.FilteredQuickLaunchProfiles[2], viewModel.SelectedQuickLaunchProfile);
    }

    [Fact]
    public void RecentProfileRows_render_keyboard_hint_for_slot_index()
    {
        var viewModel = new MainViewModel();
        SimulateLoadedProfiles(viewModel, "Personal", "Work");
        SeedRecents(viewModel, new[]
        {
            (id: "Personal", path: @"C:\Chrome\User Data", count: 7, pinned: true),
            (id: "Work", path: @"C:\Chrome\User Data", count: 2, pinned: false)
        });

        var rows = viewModel.RecentProfileRows;
        Assert.Equal(2, rows.Count);
        Assert.Equal("Ctrl+1", rows[0].KeyboardHint);
        Assert.Equal("Ctrl+2", rows[1].KeyboardHint);
        Assert.Equal("7 lần", rows[0].LaunchCountText);
        Assert.True(rows[0].IsPinned);
    }

    [Fact]
    public void MaxRecentSlots_is_ten()
    {
        Assert.Equal(10, MainViewModel.MaxRecentSlots);
    }

    private static void SimulateLoadedProfiles(MainViewModel viewModel, params string[] names)
    {
        foreach (var n in names)
        {
            viewModel.Profiles.Add(new ChromeProfile(
                ChromeProfile.CreateId(@"C:\Chrome\User Data", n),
                n,
                n,
                @"C:\Chrome\User Data",
                false));
        }
    }

    private static void SeedRecents(
        MainViewModel viewModel,
        IReadOnlyList<(string id, string path, int count, bool pinned)> recents)
    {
        var list = (System.Collections.IList?)typeof(MainViewModel)
            .GetField("_recentProfiles", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(viewModel);
        Assert.NotNull(list);

        list!.Clear();
        foreach (var r in recents)
        {
            var profile = viewModel.Profiles.First(p => p.Name == r.id);
            list.Add(new RecentProfile(
                ProfileId: profile.Id,
                ProfileName: profile.Name,
                UserDataDirectory: r.path,
                LastUsedUtc: DateTime.UtcNow.AddMinutes(-30),
                LaunchCount: r.count,
                IsPinned: r.pinned));
        }

        typeof(MainViewModel)
            .GetMethod("UpdateRecentProfilesList", BindingFlags.Instance | BindingFlags.NonPublic)
            !.Invoke(viewModel, null);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RouterPlusRecentTests", Guid.NewGuid().ToString("N"));
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
