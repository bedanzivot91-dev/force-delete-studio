$ErrorActionPreference = 'Stop'

function UpdateText([string]$Path, [string]$Old, [string]$New, [string]$Label) {
    $full = Resolve-Path $Path
    $text = [System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8)
    if (-not $text.Contains($Old)) { throw "Anchor not found: $Label" }
    $text = $text.Replace($Old, $New)
    [System.IO.File]::WriteAllText($full, $text, (New-Object System.Text.UTF8Encoding($false)))
}

# Renderer treats every Video track after the first as an overlay. Mirror that rule in the VM,
# otherwise secondary video layers cannot reach PIP/chroma controls even though export supports them.
$oldCtor = '            OnLayerPlacementChanged, track.Kind == TimelineTrackKind.ImageOverlay, OnEffectsChanged, OnTransformChanged)'
$newCtor = '            OnLayerPlacementChanged, track.Kind == TimelineTrackKind.ImageOverlay || (track.Kind == TimelineTrackKind.Video && _session.Tracks.Where(t => t.Kind == TimelineTrackKind.Video).FirstOrDefault()?.Id != track.Id), OnEffectsChanged, OnTransformChanged)'
UpdateText 'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs' $oldCtor $newCtor 'secondary video overlay classification'

# Chroma key has meaning only while compositing a layer over the base video. Hide it for the base layer
# so there is no button that silently does nothing.
$oldChroma = @'
              <TextBlock Text="GREEN SCREEN / CHROMA KEY" Classes="eyebrow" Margin="0,8,0,0" />
              <ToggleButton Content="Uključi Chroma Key" IsChecked="{Binding ChromaKeyEnabled}"/>
              <TextBlock Text="Boja (#RRGGBB)" Classes="subtle"/><TextBox Text="{Binding ChromaKeyColor}" Watermark="#00FF00"/>
              <TextBlock Text="Sličnost" Classes="subtle"/><Slider Minimum="0.01" Maximum="1" Value="{Binding ChromaKeySimilarity}"/>
              <TextBlock Text="Feather / blend" Classes="subtle"/><Slider Minimum="0" Maximum="1" Value="{Binding ChromaKeyBlend}"/>
'@
$newChroma = @'
              <StackPanel Spacing="4" IsVisible="{Binding IsOverlayClip}">
                <TextBlock Text="GREEN SCREEN / CHROMA KEY" Classes="eyebrow" Margin="0,8,0,0" />
                <ToggleButton Content="Uključi Chroma Key" IsChecked="{Binding ChromaKeyEnabled}"/>
                <TextBlock Text="Boja (#RRGGBB)" Classes="subtle"/><TextBox Text="{Binding ChromaKeyColor}" Watermark="#00FF00"/>
                <TextBlock Text="Sličnost" Classes="subtle"/><Slider Minimum="0.01" Maximum="1" Value="{Binding ChromaKeySimilarity}"/>
                <TextBlock Text="Feather / blend" Classes="subtle"/><Slider Minimum="0" Maximum="1" Value="{Binding ChromaKeyBlend}"/>
              </StackPanel>
'@
UpdateText 'src/NPVideoStudio.App/Views/WorkspaceView.axaml' $oldChroma $newChroma 'chroma visibility'

Write-Host 'Overlay classification and chroma visibility fixed.'
