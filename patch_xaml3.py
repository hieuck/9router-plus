path = r'E:\GitHub\9router-plus\src\RouterPlus.App\MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    s = f.read()

# Replace the ComboBox filter with ItemsControl of ToggleButtons
old_block = '''                    <Border Grid.Row="4" Style="{StaticResource SidebarSearchStyle}" Padding="10,0" Margin="0,0,0,10">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="&#x2325;" FontSize="14" Foreground="{DynamicResource MutedTextBrush}" VerticalAlignment="Center" />
                            <ComboBox Grid.Column="1"
                                      ItemsSource="{Binding ProviderFilterOptions}"
                                      SelectedItem="{Binding ProfileProviderFilter}"
                                      DisplayMemberPath="DisplayName"
                                      ToolTip="L&#x1ecb;c profile theo provider"
                                      Background="Transparent"
                                      BorderThickness="0"
                                      Padding="8,5"
                                      Margin="0"
                                      Foreground="{DynamicResource TextBrush}" />
                        </Grid>
                    </Border>'''

new_block = '''                    <Border Grid.Row="4" Style="{StaticResource SidebarSearchStyle}" Padding="10,8" Margin="0,0,0,10">
                        <ItemsControl ItemsSource="{Binding ProviderFilterOptions}">
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
                    </Border>'''
assert old_block in s, 'old block not found'
s = s.replace(old_block, new_block, 1)
with open(path, 'w', encoding='utf-8') as f:
    f.write(s)
print('Done, length:', len(s))
