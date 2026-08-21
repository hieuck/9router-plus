path = r'E:\GitHub\9router-plus\src\RouterPlus.App\ViewModels\MainViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    s = f.read()

# 1. Add ProviderFilterOptions + init helper right before SelectedProviderKinds line
old = '''    public HashSet<ProviderKind> SelectedProviderKinds { get; } = new();'''
new = '''    public IReadOnlyList<ProfileProviderFilterOption> ProviderFilterOptions { get; } = Array.Empty<ProfileProviderFilterOption>();

    private readonly Dictionary<ProviderKind, ProfileProviderFilterOption> _providerOptionByKind = new();

    private void InitializeProviderFilterOptions()
    {
        var allOption = new ProfileProviderFilterOption(null, "T\u1ea5t c\u1ea3", "\u2300", "Hi\u1ec3n t\u1ea5t c\u1ea3 profile");
        var providerOptions = ProviderCatalog.All.Select(definition => new ProfileProviderFilterOption(
            definition.Kind,
            definition.ShortDisplayName,
            definition.Glyph,
            $"Ch\u1ec9 hi\u1ec3n profile c\u00f3 k\u1ebft n\u1ed1i {definition.DisplayName}"));
        var options = new[] { allOption }.Concat(providerOptions).ToArray();
        ProviderFilterOptions = options;
        foreach (var option in options)
        {
            if (option.Kind is { } kind)
            {
                _providerOptionByKind[kind] = option;
            }
        }
    }

    public HashSet<ProviderKind> SelectedProviderKinds { get; } = new();'''
assert old in s, 'anchor not found'
s = s.replace(old, new, 1)

# 2. Call InitializeProviderFilterOptions after Providers = ProviderCatalog.All;
anchor2 = '        Providers = ProviderCatalog.All;\n'
assert s.count(anchor2) == 1
s = s.replace(anchor2, anchor2 + '        InitializeProviderFilterOptions();\n', 1)

# 3. Update ToggleProvider to sync IsSelected
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
assert old_toggle in s, 'toggle anchor not found'
s = s.replace(old_toggle, new_toggle, 1)

# 4. Update ClearProviderFilter to reset IsSelected on all options
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
assert old_clear in s, 'clear anchor not found'
s = s.replace(old_clear, new_clear, 1)

with open(path, 'w', encoding='utf-8') as f:
    f.write(s)
print('Done, length:', len(s))
