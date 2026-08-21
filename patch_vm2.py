path = r'E:\GitHub\9router-plus\src\RouterPlus.App\ViewModels\MainViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    s = f.read()

# 1. Replace the simple ProviderFilterOptions line with a proper build
old_options = '''    public IReadOnlyList<ProfileProviderFilterOption> ProviderFilterOptions { get; } =
        new[] { new ProfileProviderFilterOption(null, "T\u1ea5t c\u1ea3 provider") }
            .Concat(ProviderCatalog.All.Select(definition => new ProfileProviderFilterOption(definition.Kind, definition.DisplayName)))
            .ToArray();'''
new_options = '''    public IReadOnlyList<ProfileProviderFilterOption> ProviderFilterOptions { get; }

    private readonly Dictionary<ProviderKind, ProfileProviderFilterOption> _providerOptionByKind = new();

    private void InitializeProviderFilterOptions()
    {
        var allOption = new ProfileProviderFilterOption(null, "T\u1ea5t c\u1ea3", "\u232b", "Hi\u1ec7n t\u1ea5t c\u1ea3 profile");
        var providerOptions = ProviderCatalog.All.Select(definition => new ProfileProviderFilterOption(
            definition.Kind,
            definition.ShortDisplayName,
            definition.Glyph,
            $"Ch\u1ec9 hi\u1ec7n profile c\u00f3 k\u1ebft n\u1ed1i {definition.DisplayName}"));
        ProviderFilterOptions = new[] { allOption }.Concat(providerOptions).ToArray();
        foreach (var option in ProviderFilterOptions)
        {
            if (option.Kind is { } kind)
            {
                _providerOptionByKind[kind] = option;
            }
        }
    }'''
assert old_options in s, 'old options not found'
s = s.replace(old_options, new_options, 1)

# 2. Add initialization call right after Providers = ProviderCatalog.All;
anchor = '        Providers = ProviderCatalog.All;\n'
assert s.count(anchor) == 1
s = s.replace(anchor, anchor + '        InitializeProviderFilterOptions();\n        ToggleProviderCommand = new AsyncRelayCommand<ProviderKind>(kind => { ToggleProvider(kind); return Task.CompletedTask; });\n', 1)

# 3. Remove the previously inserted ToggleProviderCommand line (was injected earlier from old patch)
old_cmd_line = '        ToggleProviderCommand = new AsyncRelayCommand<ProviderKind>(kind => { ToggleProvider(kind); return Task.CompletedTask; });\n'
# Only remove the duplicated one if it appears more than once after step 2
count = s.count(old_cmd_line)
if count > 1:
    # Remove the second occurrence
    idx = s.find(old_cmd_line)
    idx = s.find(old_cmd_line, idx + len(old_cmd_line))
    s = s[:idx] + s[idx + len(old_cmd_line):]

# 4. Update ToggleProvider to sync IsSelected on the corresponding option
old_toggle = '''    public void ToggleProvider(ProviderKind kind)
    {
        if (SelectedProviderKinds.Contains(kind))
        {
            SelectedProviderKinds.Remove(kind);
        }
        else
        {
            SelectedProviderKinds.Add(kind);
        }
        OnPropertyChanged(nameof(SelectedProviderKinds));
        OnPropertyChanged(nameof(IsProviderFilterActive));
        OnPropertyChanged(nameof(FilteredProfileCountLabel));
        ApplyProfileFilter();
    }'''
new_toggle = '''    public void ToggleProvider(ProviderKind kind)
    {
        if (SelectedProviderKinds.Contains(kind))
        {
            SelectedProviderKinds.Remove(kind);
        }
        else
        {
            SelectedProviderKinds.Add(kind);
        }
        if (_providerOptionByKind.TryGetValue(kind, out var option))
        {
            option.IsSelected = SelectedProviderKinds.Contains(kind);
        }
        OnPropertyChanged(nameof(SelectedProviderKinds));
        OnPropertyChanged(nameof(IsProviderFilterActive));
        OnPropertyChanged(nameof(FilteredProfileCountLabel));
        ApplyProfileFilter();
    }'''
assert old_toggle in s, 'old toggle not found'
s = s.replace(old_toggle, new_toggle, 1)

# 5. Update ClearProviderFilter to also reset option IsSelected
old_clear = '''    public void ClearProviderFilter()
    {
        if (SelectedProviderKinds.Count == 0)
        {
            return;
        }

        SelectedProviderKinds.Clear();
        OnPropertyChanged(nameof(SelectedProviderKinds));
        OnPropertyChanged(nameof(IsProviderFilterActive));
        OnPropertyChanged(nameof(FilteredProfileCountLabel));
        ApplyProfileFilter();
    }'''
new_clear = '''    public void ClearProviderFilter()
    {
        if (SelectedProviderKinds.Count == 0)
        {
            return;
        }

        SelectedProviderKinds.Clear();
        foreach (var option in ProviderFilterOptions)
        {
            option.IsSelected = false;
        }
        OnPropertyChanged(nameof(SelectedProviderKinds));
        OnPropertyChanged(nameof(IsProviderFilterActive));
        OnPropertyChanged(nameof(FilteredProfileCountLabel));
        ApplyProfileFilter();
    }'''
assert old_clear in s, 'old clear not found'
s = s.replace(old_clear, new_clear, 1)

with open(path, 'w', encoding='utf-8') as f:
    f.write(s)
print('Done, length:', len(s))
