import sys

with open('src/RouterPlus.App/MainWindow.xaml', 'r', encoding='utf-8') as f:
    xaml = f.read()

# Revert back to Command binding
xaml = xaml.replace(
    'Click=\"ProviderFilterButton_Click\"' + chr(10) + '                                                  Tag=\"{Binding}\"',
    'Command=\"{Binding DataContext.ToggleProviderCommand, RelativeSource={RelativeSource AncestorType=Window}}\"' + chr(10) + '                                                  CommandParameter=\"{Binding Kind}\"'
)

with open('src/RouterPlus.App/MainWindow.xaml', 'w', encoding='utf-8') as f:
    f.write(xaml)

print('Reverted XAML')
