import sys

with open('src/RouterPlus.App/ViewModels/MainViewModel.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Change back to non-nullable
content = content.replace(
    'public AsyncRelayCommand<ProviderKind?> ToggleProviderCommand',
    'public AsyncRelayCommand<ProviderKind> ToggleProviderCommand'
)

content = content.replace(
    'ToggleProviderCommand = new AsyncRelayCommand<ProviderKind?>(kind => { ToggleProvider(kind); return Task.CompletedTask; });',
    'ToggleProviderCommand = new AsyncRelayCommand<ProviderKind>(kind => { ToggleProvider(kind); return Task.CompletedTask; });'
)

content = content.replace(
    'public void ToggleProvider(ProviderKind? kind)',
    'public void ToggleProvider(ProviderKind kind)'
)

# Remove the null check since we don't have "Tất cả" button anymore
old_body = '''    public void ToggleProvider(ProviderKind kind)
    {
        if (kind == null)
        {
            ClearProviderFilter();
            return;
        }

        if (SelectedProviderKinds.Contains(kind.Value))
        {
            SelectedProviderKinds.Remove(kind.Value);
        }
        else
        {
            SelectedProviderKinds.Add(kind.Value);
        }
        if (_providerOptionByKind.TryGetValue(kind.Value, out var option))
        {
            option.IsSelected = SelectedProviderKinds.Contains(kind.Value);
        }'''

new_body = '''    public void ToggleProvider(ProviderKind kind)
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
        }'''

content = content.replace(old_body, new_body)

with open('src/RouterPlus.App/ViewModels/MainViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print('Reverted to non-nullable')
