import sys

with open('src/RouterPlus.App/MainWindow.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Remove the event handler we added
handler = '''
    private void ProviderFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is ProfileProviderFilterOption option)
        {
            ViewModel.ToggleProvider(option.Kind);
        }
    }
'''

content = content.replace(handler, '')

with open('src/RouterPlus.App/MainWindow.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print('Removed event handler')
