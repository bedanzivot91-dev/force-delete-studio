from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    if text.count(old) != 1:
        raise RuntimeError(f'{path}: expected one anchor, got {text.count(old)} for {old!r}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')

app = 'src/NPVideoStudio.App/App.axaml'
replacements = {
    '<Setter Property="FontSize" Value="13.5" />': '<Setter Property="FontSize" Value="14.5" />',
    '<Setter Property="FontSize" Value="12.5" />\n    </Style>\n    <Style Selector="TextBlock.micro">': '<Setter Property="FontSize" Value="13.5" />\n    </Style>\n    <Style Selector="TextBlock.micro">',
    '<Setter Property="FontSize" Value="9.5" />': '<Setter Property="FontSize" Value="12.5" />',
    '<Setter Property="FontSize" Value="15.5" />': '<Setter Property="FontSize" Value="16.5" />',
    '<Setter Property="FontSize" Value="10" />': '<Setter Property="FontSize" Value="13" />',
}
for old, new in replacements.items():
    replace_once(app, old, new)

# Buttons/topnav/check boxes are normal interactive text, not secondary captions.
p = Path(app)
text = p.read_text(encoding='utf-8')
text = text.replace('<Setter Property="FontSize" Value="12.5" />\n      <Setter Property="Cursor" Value="Hand" />',
                    '<Setter Property="FontSize" Value="13.5" />\n      <Setter Property="MinHeight" Value="36" />\n      <Setter Property="Cursor" Value="Hand" />', 1)
text = text.replace('<Setter Property="FontSize" Value="12.5" />\n      <Setter Property="CornerRadius" Value="6" />',
                    '<Setter Property="FontSize" Value="13.5" />\n      <Setter Property="CornerRadius" Value="6" />', 1)
text = text.replace('<Setter Property="FontSize" Value="12.5" />\n    </Style>\n    <Style Selector="CheckBox /template/',
                    '<Setter Property="FontSize" Value="13.5" />\n    </Style>\n    <Style Selector="CheckBox /template/', 1)
p.write_text(text, encoding='utf-8')

# The safe-area chip is user-facing text, so it must not be 11px.
replace_once('src/NPVideoStudio.App/Views/WorkspaceView.axaml',
             'Foreground="#FFFFD54F" FontSize="11" FontWeight="Bold"',
             'Foreground="#FFFFD54F" FontSize="12.5" FontWeight="Bold"')

# Remove the stale hard-coded "3 of 10" claim and derive it from the real enum list.
replace_once('src/NPVideoStudio.App/ViewModels/SettingsViewModel.cs',
             'public IReadOnlyList<AppTheme> AvailableThemes { get; } = Enum.GetValues<AppTheme>();',
             'public IReadOnlyList<AppTheme> AvailableThemes { get; } = Enum.GetValues<AppTheme>();\n    public string ThemeAvailabilityLabel => $"Dostupno je {AvailableThemes.Count} tema. Sve ponuđene teme koriste isti skup semantičkih UI resursa.";')
replace_once('src/NPVideoStudio.App/Views/SettingsView.axaml',
             'Text="Trenutno su dostupne 3 od planiranih 10 tema. Ostale dolaze u narednim fazama."',
             'Text="{Binding ThemeAvailabilityLabel}"')

print('Readability production changes materialized.')
