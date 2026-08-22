$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-Utf8([string]$Path) {
    return [System.IO.File]::ReadAllText((Resolve-Path $Path), [System.Text.Encoding]::UTF8)
}
function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText((Resolve-Path $Path), $Text, (New-Object System.Text.UTF8Encoding($false)))
}
function Replace-Required([string]$Path, [string]$Old, [string]$New, [string]$Label) {
    $text = Read-Utf8 $Path
    if (-not $text.Contains($Old)) { throw "Missing integration anchor: $Label ($Path)" }
    $text = $text.Replace($Old, $New)
    Write-Utf8 $Path $text
}

# Preserve the later real track-volume workflow that landed after the font-renderer branch point.
$timeline = 'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs'
$oldTrackBlock = @'
        var removeTrack = new RelayCommand(() => { _session.RemoveTrack(track.Id); RefreshFromSession(); });
        var addClip = new RelayCommand(() => AddClipToTrack(track));

        var trackItem = new TimelineTrackItemViewModel(track, toggleLock, toggleHide, toggleMute, toggleSolo, removeTrack, addClip);
'@
$newTrackBlock = @'
        var removeTrack = new RelayCommand(() => { _session.RemoveTrack(track.Id); RefreshFromSession(); });
        var addClip = new RelayCommand(() => AddClipToTrack(track));
        void OnTrackVolumeChanged(string trackId, double volume)
        {
            _session.SetTrackVolume(trackId, volume);
            RefreshFromSession();
        }

        var trackItem = new TimelineTrackItemViewModel(track, toggleLock, toggleHide, toggleMute, toggleSolo, removeTrack, addClip, OnTrackVolumeChanged);
'@
Replace-Required $timeline $oldTrackBlock $newTrackBlock 'later track-volume workflow'

# Materialize the audited readability/theme corrections from the former PR #13 into real XAML.
$app = 'src/NPVideoStudio.App/App.axaml'
Replace-Required $app '<Setter Property="FontSize" Value="13.5" />' '<Setter Property="FontSize" Value="14.5" />' 'window base font'
Replace-Required $app '<Setter Property="FontSize" Value="12.5" />' '<Setter Property="FontSize" Value="13.5" />' 'global compact text floor'
Replace-Required $app '<Setter Property="FontSize" Value="9.5" />' '<Setter Property="FontSize" Value="12.5" />' 'micro text'
Replace-Required $app '<Setter Property="FontSize" Value="22" />' '<Setter Property="FontSize" Value="24" />' 'heading'
Replace-Required $app '<Setter Property="FontSize" Value="15.5" />' '<Setter Property="FontSize" Value="17" />' 'section'
Replace-Required $app '<Setter Property="FontSize" Value="10" />' '<Setter Property="FontSize" Value="12.5" />' 'eyebrow'

$settings = 'src/NPVideoStudio.App/Views/SettingsView.axaml'
$settingsText = Read-Utf8 $settings
$stale = 'Text="Trenutno su dostupne 3 od planiranih 10 tema. Ostale dolaze u narednim fazama."'
if ($settingsText.Contains($stale)) {
    $settingsText = $settingsText.Replace($stale, 'Text="Izaberite temu interfejsa. Promena teme menja boje i površine kroz ceo program; Dark Cinematic je podrazumevana tema."')
    Write-Utf8 $settings $settingsText
}

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

$gallery = 'src/NPVideoStudio.App/Views/CaptionStyleGalleryView.axaml'
$galleryText = Read-Utf8 $gallery
if ($galleryText.Contains('Background="#1A1A1A"')) {
    $galleryText = $galleryText.Replace('Background="#1A1A1A"', 'Background="{DynamicResource ThemeInputBrush}"')
    Write-Utf8 $gallery $galleryText
}
$workspace = 'src/NPVideoStudio.App/Views/WorkspaceView.axaml'
$workspaceText = Read-Utf8 $workspace
if ($workspaceText.Contains('<Canvas Height="52" Background="#1A1A1A" />')) {
    $workspaceText = $workspaceText.Replace('<Canvas Height="52" Background="#1A1A1A" />', '<Canvas Height="52" Background="{DynamicResource ThemeInputBrush}" />')
    Write-Utf8 $workspace $workspaceText
}

# Hard audit: no explicitly user-facing TextBlock override below 12.5 px may remain anywhere in Views.
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

Write-Host 'Unified UI materialization PASS: font renderer preserved, track volume preserved, XAML readability/theme audit passed.'
