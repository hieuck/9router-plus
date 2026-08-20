# Localization

RouterPlus keeps user-facing labels in UTF-8 resource dictionaries so the UI can add languages without putting translated text in control templates.

- `src/RouterPlus.App/Resources/Strings.vi.xaml` is the default Vietnamese dictionary.
- `src/RouterPlus.App/Resources/Strings.en.xaml` is the English dictionary template for the future language switcher.
- Use resource keys such as `ProviderDashboardText` from XAML instead of duplicating translated labels in provider cards.
- Keep source files in UTF-8 and do not rewrite whole XAML files with a shell command that uses the system ANSI code page.
