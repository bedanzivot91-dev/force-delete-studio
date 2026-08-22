$ErrorActionPreference = 'Stop'

function Read-Utf8([string]$Path) {
    return [System.IO.File]::ReadAllText((Resolve-Path $Path), [System.Text.Encoding]::UTF8)
}
function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText((Resolve-Path $Path), $Text, (New-Object System.Text.UTF8Encoding($false)))
}
function Replace-Required([string]$Path, [string]$Old, [string]$New, [string]$Label) {
    $text = Read-Utf8 $Path
    if (-not $text.Contains($Old)) { throw "Missing audit anchor: $Label ($Path)" }
    $text = $text.Replace($Old, $New)
    Write-Utf8 $Path $text
}

# Global typography: modern does not mean tiny. Keep compact editor density but raise all user-facing text.
$app = 'src/NPVideoStudio.App/App.axaml'
Replace-Required $app '<Setter Property="FontSize" Value="13.5" />' '<Setter Property="FontSize" Value="14.5" />' 'window base font'
Replace-Required $app '<Setter Property="FontSize" Value="12.5" />' '<Setter Property="FontSize" Value="13.5" />' 'first subtle/button sizes'
# The replacement above intentionally touches every 12.5 global style (subtle/buttons/topnav/checkbox).
Replace-Required $app '<Setter Property="FontSize" Value="9.5" />' '<Setter Property="FontSize" Value="12.5" />' 'micro text'
Replace-Required $app '<Setter Property="FontSize" Value="22" />' '<Setter Property="FontSize" Value="24" />' 'heading'
Replace-Required $app '<Setter Property="FontSize" Value="15.5" />' '<Setter Property="FontSize" Value="17" />' 'section'
Replace-Required $app '<Setter Property="FontSize" Value="10" />' '<Setter Property="FontSize" Value="12.5" />' 'eyebrow'

# Settings had stale user-visible copy: there are eight actual theme enum values/dictionaries, not three.
$settings = 'src/NPVideoStudio.App/Views/SettingsView.axaml'
Replace-Required $settings 'Text="Trenutno su dostupne 3 od planiranih 10 tema. Ostale dolaze u narednim fazama."' 'Text="Izaberite temu interfejsa. Promena teme menja boje i površine kroz ceo program; Dark Cinematic je podrazumevana tema."' 'theme availability copy'

# Remove tiny metadata text that overrode the global readable typography.
$viewFiles = @(
    'src/NPVideoStudio.App/Views/CaptionEditorView.axaml',
    'src/NPVideoStudio.App/Views/CaptionStyleGalleryView.axaml',
    'src/NPVideoStudio.App/Views/DependencyManagerView.axaml',
    'src/NPVideoStudio.App/Views/RenderQueueView.axaml',
    'src/NPVideoStudio.App/Views/StartScreenView.axaml',
    'src/NPVideoStudio.App/Views/WorkspaceView.axaml'
)
foreach ($path in $viewFiles) {
    $text = Read-Utf8 $path
    $text = $text.Replace('FontSize="9.5"', 'FontSize="12.5"')
    $text = $text.Replace('FontSize="10"', 'FontSize="12.5"')
    $text = $text.Replace('FontSize="11"', 'FontSize="13"')
    $text = $text.Replace('FontSize="12"', 'FontSize="13"')
    $text = $text.Replace('FontSize="12.5"', 'FontSize="13.5"')
    Write-Utf8 $path $text
}

# Theme consistency: these were fixed dark islands even when Minimal Light / Arctic Glass / Ocean Glass was active.
Replace-Required 'src/NPVideoStudio.App/Views/CaptionStyleGalleryView.axaml' 'Background="#1A1A1A"' 'Background="{DynamicResource ThemeInputBrush}"' 'caption preset preview surface'
Replace-Required 'src/NPVideoStudio.App/Views/WorkspaceView.axaml' '<Canvas Height="52" Background="#1A1A1A" />' '<Canvas Height="52" Background="{DynamicResource ThemeInputBrush}" />' 'timeline lane theme surface'

# Audit every XAML page. User-facing TextBlock font overrides below 12.5px are forbidden.
$bad = New-Object System.Collections.Generic.List[string]
Get-ChildItem 'src/NPVideoStudio.App/Views' -Filter '*.axaml' | ForEach-Object {
    $content = Read-Utf8 $_.FullName
    $matches = [regex]::Matches($content, '<TextBlock\b[^>]*?FontSize="([0-9]+(?:\.[0-9]+)?)"[^>]*?>')
    foreach ($m in $matches) {
        $size = [double]::Parse($m.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
        if ($size -lt 12.5) { $bad.Add("$($_.Name): TextBlock FontSize=$size") }
    }
}
if ($bad.Count -gt 0) {
    throw "Unreadably small TextBlock overrides remain:`n$($bad -join "`n")"
}

Write-Host 'UI readability/theme audit patch applied; no TextBlock override below 12.5px remains.'
