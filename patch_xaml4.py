path = r'E:\GitHub\9router-plus\src\RouterPlus.App\MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    s = f.read()

# Insert ItemsControl of ToggleButtons right at the top of StackPanel Row=4 (before the Add profile button)
old = '''                    <StackPanel Grid.Row="4" Style="{StaticResource SidebarPanelStyle}" Margin="0,0,0,10">
                        <Button Content="{Binding ProfileAddButtonText}"'''
new = '''                    <StackPanel Grid.Row="4" Style="{StaticResource SidebarPanelStyle}" Margin="0,0,0,10">
                        <ItemsControl ItemsSource="{Binding ProviderFilterOptions}" Margin="0,0,0,8">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <WrapPanel Orientation="Horizontal" />
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <ToggleButton Margin="0,0,6,6"
                                                  Padding="8,5"
                                                  MinWidth="0"
                                                  MinHeight="0"
                                                  Command="{Binding DataContext.ToggleProviderCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                  CommandParameter="{Binding Kind}"
                                                  IsChecked="{Binding IsSelected, Mode=OneWay}"
                                                  ToolTip="{Binding Tooltip}">
                                        <ToggleButton.Style>
                                            <Style TargetType="ToggleButton" BasedOn="{StaticResource {x:Type ToggleButton}}">
                                                <Setter Property="Background" Value="{DynamicResource PanelLightBrush}" />
                                                <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
                                                <Setter Property="Foreground" Value="{DynamicResource TextBrush}" />
                                                <Style.Triggers>
                                                    <Trigger Property="IsChecked" Value="True">
                                                        <Setter Property="Background" Value="{DynamicResource AccentSoftBrush}" />
                                                        <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
                                                        <Setter Property="Foreground" Value="{DynamicResource AccentContentBrush}" />
                                                    </Trigger>
                                                </Style.Triggers>
                                            </Style>
                                        </ToggleButton.Style>
                                        <StackPanel Orientation="Horizontal">
                                            <TextBlock Text="{Binding Glyph}" FontSize="13" VerticalAlignment="Center" Margin="0,0,6,0" />
                                            <TextBlock Text="{Binding DisplayName}" FontSize="11" VerticalAlignment="Center" />
                                        </StackPanel>
                                    </ToggleButton>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                        <Button Content="{Binding ProfileAddButtonText}"'''
assert old in s, 'anchor not found'
s = s.replace(old, new, 1)

# Update total count label to FilteredProfileCountLabel
old_count = '<TextBlock Grid.Column="1" Text="{Binding Profiles.Count, StringFormat={}{0} total}"'
new_count = '<TextBlock Grid.Column="1" Text="{Binding FilteredProfileCountLabel}"'
assert old_count in s, 'count not found'
s = s.replace(old_count, new_count, 1)

with open(path, 'w', encoding='utf-8') as f:
    f.write(s)
print('Done, length:', len(s))
