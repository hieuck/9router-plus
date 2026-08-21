import sys

with open('src/RouterPlus.App/ViewModels/MainViewModel.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Remove allOption line
content = content.replace(
    '        var allOption = new ProfileProviderFilterOption(null, "Tất cả", "⌀", "Hiển tất cả profile");' + chr(10),
    ''
)

# Change options initialization
content = content.replace(
    'var options = new[] { allOption }.Concat(providerOptions).ToArray();',
    'var options = providerOptions.ToArray();'
)

with open('src/RouterPlus.App/ViewModels/MainViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print('Removed Tất cả button')
