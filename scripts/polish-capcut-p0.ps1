$ErrorActionPreference = 'Stop'

function UpdateText([string]$Path, [string]$Old, [string]$New, [string]$Label) {
    $full = Resolve-Path $Path
    $text = [System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8)
    if (-not $text.Contains($Old)) { throw "Anchor not found: $Label" }
    $text = $text.Replace($Old, $New)
    [System.IO.File]::WriteAllText($full, $text, (New-Object System.Text.UTF8Encoding($false)))
}

UpdateText 'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs' `
'    private readonly Action<string, ClipVideoEffect, double, double, double, double>? _onEffectsChanged;    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;' `
"    private readonly Action<string, ClipVideoEffect, double, double, double, double>? _onEffectsChanged;`r`n    private readonly Action<string, ClipTransformSettings>? _onTransformChanged;" `
'clip VM callback formatting'

UpdateText 'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs' `
'    public bool IsOverlayClip { get; }' `
"    public bool IsOverlayClip { get; }`r`n    public bool IsPictureClip => IsVideoClip || IsOverlayClip;" `
'picture clip property'

UpdateText 'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs' `
'        _onEffectsChanged = onEffectsChanged;        _onTransformChanged = onTransformChanged;' `
"        _onEffectsChanged = onEffectsChanged;`r`n        _onTransformChanged = onTransformChanged;" `
'constructor formatting'

UpdateText 'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs' `
'        }        void OnTransformChanged(string clipId, ClipTransformSettings settings)' `
"        }`r`n`r`n        void OnTransformChanged(string clipId, ClipTransformSettings settings)" `
'view model callback formatting'

UpdateText 'src/NPVideoStudio.App/Views/WorkspaceView.axaml' `
'            <StackPanel Spacing="8" IsVisible="{Binding !IsTextClip}">' `
'            <StackPanel Spacing="8" IsVisible="{Binding IsPictureClip}">' `
'picture inspector visibility'

UpdateText 'src/NPVideoStudio.App/Views/WorkspaceView.axaml' `
'              <TextBlock Text="Brzina" Classes="subtle"/><NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/>              <TextBlock Text="TRANSFORMACIJA" Classes="eyebrow" Margin="0,8,0,0" />' `
"              <TextBlock Text=\"Brzina\" Classes=\"subtle\"/><NumericUpDown Value=\"{Binding SpeedMultiplier}\" Minimum=\"0.25\" Maximum=\"4\" Increment=\"0.25\"/>`r`n              <TextBlock Text=\"TRANSFORMACIJA\" Classes=\"eyebrow\" Margin=\"0,8,0,0\" />" `
'workspace formatting'

Write-Host 'CapCut P0 inspector visibility and formatting polished.'
