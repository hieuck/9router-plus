import re
path = r'E:\GitHub\9router-plus\src\RouterPlus.App\ViewModels\MainViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    s = f.read()

# 1. Add HashSet field + observable SelectedProviderKinds + ToggleProviderCommand right after CanClearProfileSearch
anchor = '    public bool CanClearProfileSearch => !string.IsNullOrEmpty(ProfileSearchText);\n'
assert s.count(anchor) == 1
insert = (
    '    public bool CanClearProfileSearch => !string.IsNullOrEmpty(ProfileSearchText);\n'
    '\n'
    '    public HashSet<ProviderKind> SelectedProviderKinds { get; } = new();\n'
    '\n'
    '    public RelayCommand<ProviderKind> ToggleProviderCommand { get; }\n'
    '\n'
    '    public void ToggleProvider(ProviderKind kind)\n'
    '    {\n'
    '        if (SelectedProviderKinds.Contains(kind))\n'
    '        {\n'
    '            SelectedProviderKinds.Remove(kind);\n'
    '        }\n'
    '        else\n'
    '        {\n'
    '            SelectedProviderKinds.Add(kind);\n'
    '        }\n'
    '        OnPropertyChanged(nameof(SelectedProviderKinds));\n'
    '        OnPropertyChanged(nameof(IsProviderFilterActive));\n'
    '        OnPropertyChanged(nameof(FilteredProfileCountLabel));\n'
    '        ApplyProfileFilter();\n'
    '    }\n'
    '\n'
    '    public void ClearProviderFilter()\n'
    '    {\n'
    '        if (SelectedProviderKinds.Count == 0)\n'
    '        {\n'
    '            return;\n'
    '        }\n'
    '\n'
    '        SelectedProviderKinds.Clear();\n'
    '        OnPropertyChanged(nameof(SelectedProviderKinds));\n'
    '        OnPropertyChanged(nameof(IsProviderFilterActive));\n'
    '        OnPropertyChanged(nameof(FilteredProfileCountLabel));\n'
    '        ApplyProfileFilter();\n'
    '    }\n'
    '\n'
    '    public bool IsProviderFilterActive => SelectedProviderKinds.Count > 0;\n'
    '\n'
    '    public int FilteredProfileCount => FilteredProfileRows.Count;\n'
    '\n'
    '    public string FilteredProfileCountLabel\n'
    '    {\n'
    '        get\n'
    '        {\n'
    '            var hasFilter = IsProviderFilterActive || !string.IsNullOrWhiteSpace(ProfileSearchText);\n'
    '            return hasFilter\n'
    '                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} đang hiển thị", FilteredProfileCount)\n'
    '                : string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} profile", FilteredProfileCount);\n'
    '        }\n'
    '    }\n'
)
s = s.replace(anchor, insert, 1)

# 2. Initialize ToggleProviderCommand in constructor right after other Command initializers
# Find a known anchor - the AsyncRelayCommand<ProviderKind> declarations exist earlier. We'll add at end of ctor.
# Simpler: add ToggleProviderCommand right after _profileProviderFilter assignment was previously (which we just removed)
# But after revert, that line is gone. Let's add right after ProviderCatalog.All usage block.
# Search for Providers = ProviderCatalog.All;
anchor2 = '        Providers = ProviderCatalog.All;\n'
assert s.count(anchor2) == 1
s = s.replace(anchor2, anchor2 + '        ToggleProviderCommand = new RelayCommand<ProviderKind>(ToggleProvider);\n', 1)

# 3. Replace body of ApplyProfileFilter
old_body = (
    '    private void ApplyProfileFilter()\n'
    '    {\n'
    '        FilteredProfiles.Clear();\n'
    '        FilteredProfileRows.Clear();\n'
    '        var rowsByProfileId = ProfileRows.ToDictionary(row => row.Profile.Id, StringComparer.Ordinal);\n'
    '        var displayIndex = 1;\n'
    '        foreach (var profile in ChromeProfileFilter.Filter(Profiles, ProfileSearchText))\n'
    '        {\n'
    '            FilteredProfiles.Add(profile);\n'
    '            if (rowsByProfileId.TryGetValue(profile.Id, out var row))\n'
    '            {\n'
    '                row.SetDisplayIndex(displayIndex++);\n'
    '                FilteredProfileRows.Add(row);\n'
    '            }\n'
    '        }\n'
    '    }'
)
new_body = (
    '    private void ApplyProfileFilter()\n'
    '    {\n'
    '        FilteredProfiles.Clear();\n'
    '        FilteredProfileRows.Clear();\n'
    '        var rowsByProfileId = ProfileRows.ToDictionary(row => row.Profile.Id, StringComparer.Ordinal);\n'
    '        var selectedProviders = SelectedProviderKinds;\n'
    '        var hasProviderFilter = selectedProviders.Count > 0;\n'
    '        var displayIndex = 1;\n'
    '        foreach (var profile in ChromeProfileFilter.Filter(Profiles, ProfileSearchText))\n'
    '        {\n'
    '            if (!rowsByProfileId.TryGetValue(profile.Id, out var row))\n'
    '            {\n'
    '                continue;\n'
    '            }\n'
    '            if (hasProviderFilter && !row.ProviderStatuses.Any(status => selectedProviders.Contains(status.Definition.Kind) && status.IsConnected))\n'
    '            {\n'
    '                continue;\n'
    '            }\n'
    '            FilteredProfiles.Add(profile);\n'
    '            row.SetDisplayIndex(displayIndex++);\n'
    '            FilteredProfileRows.Add(row);\n'
    '        }\n'
    '        OnPropertyChanged(nameof(FilteredProfileCount));\n'
    '        OnPropertyChanged(nameof(FilteredProfileCountLabel));\n'
    '    }'
)
assert old_body in s, 'old body not found'
s = s.replace(old_body, new_body, 1)

with open(path, 'w', encoding='utf-8') as f:
    f.write(s)
print('Done, length:', len(s))
