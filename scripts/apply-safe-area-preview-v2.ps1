$ErrorActionPreference = 'Stop'

function Read-Utf8([string]$Path) {
    return [System.IO.File]::ReadAllText((Resolve-Path $Path), [System.Text.Encoding]::UTF8)
}
function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText((Resolve-Path $Path), $Text, (New-Object System.Text.UTF8Encoding($false)))
}
function Replace-Once([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $i = $Text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($i -lt 0) { throw "Anchor not found: $Label" }
    if ($Text.IndexOf($Old, $i + $Old.Length, [StringComparison]::Ordinal) -ge 0) { throw "Anchor not unique: $Label" }
    return $Text.Substring(0, $i) + $New + $Text.Substring($i + $Old.Length)
}

$path = 'src/NPVideoStudio.App/ViewModels/WorkspaceViewModel.cs'
$t = Read-Utf8 $path
$old = @'
    [ObservableProperty]
    private double _playerAspectRatio = 16.0 / 9.0;

    public string PreviewCaptionText => Timeline.SelectedClip is { IsTextClip: true } clip ? clip.TextContent : string.Empty;
'@
$new = @'
    [ObservableProperty]
    private double _playerAspectRatio = 16.0 / 9.0;

    /// <summary>Preview-only platform safe-area guide. It is never burned into export; it only shows the
    /// usable rectangle from SafeAreaPreset over the player so text/logos stay clear of Shorts/Reels/
    /// TikTok chrome.</summary>
    [ObservableProperty]
    private bool _showSafeArea;

    public SafeAreaPreset CurrentSafeAreaPreset => SafeAreaPreset.ForFrame(Project.Format.Width, Project.Format.Height);
    public string SafeAreaGuideLabel => $"SAFE AREA {CurrentSafeAreaPreset.FormatLabel}";
    public bool IsVerticalSafeArea => CurrentSafeAreaPreset == SafeAreaPreset.Vertical9By16;
    public bool IsSquareSafeArea => CurrentSafeAreaPreset == SafeAreaPreset.Square1By1;
    public bool IsHorizontalSafeArea => !IsVerticalSafeArea && !IsSquareSafeArea;

    public string PreviewCaptionText => Timeline.SelectedClip is { IsTextClip: true } clip ? clip.TextContent : string.Empty;
'@
$t = Replace-Once $t $old $new 'Workspace safe-area properties'

$old = @'
    private void RefreshFormatSummaryLabel()
    {
        FormatSummaryLabel = $"{Project.Format.Width}×{Project.Format.Height}  ·  {Project.Format.Fps:0.##} fps  ·  {Project.Format.Orientation}";
        OnPropertyChanged(nameof(ProjectAspectRatio));
    }
'@
$new = @'
    private void RefreshFormatSummaryLabel()
    {
        FormatSummaryLabel = $"{Project.Format.Width}×{Project.Format.Height}  ·  {Project.Format.Fps:0.##} fps  ·  {Project.Format.Orientation}";
        OnPropertyChanged(nameof(ProjectAspectRatio));
        OnPropertyChanged(nameof(CurrentSafeAreaPreset));
        OnPropertyChanged(nameof(SafeAreaGuideLabel));
        OnPropertyChanged(nameof(IsVerticalSafeArea));
        OnPropertyChanged(nameof(IsSquareSafeArea));
        OnPropertyChanged(nameof(IsHorizontalSafeArea));
    }
'@
$t = Replace-Once $t $old $new 'Refresh safe-area derived properties'
Write-Utf8 $path $t

$path = 'src/NPVideoStudio.App/Views/WorkspaceView.axaml'
$t = Read-Utf8 $path
$old = @'
            <Button Name="FullScreenButton" Classes="cta" Content="⛶ Ceo ekran"
                    ToolTip.Tip="Preko celog ekrana. Esc ili dupli klik za izlaz." />
          </WrapPanel>
'@
$new = @'
            <Button Name="FullScreenButton" Classes="cta" Content="⛶ Ceo ekran"
                    ToolTip.Tip="Preko celog ekrana. Esc ili dupli klik za izlaz." />
            <ToggleButton Content="Safe area" IsChecked="{Binding ShowSafeArea}"
                          ToolTip.Tip="Prikaži bezbednu zonu finalnog 16:9 / 9:16 / 1:1 projekta. Vodič se ne izvozi u video." />
          </WrapPanel>
'@
$t = Replace-Once $t $old $new 'Safe-area toggle'

$old = @'
              <Grid>
                <views:VideoSurface Name="PlayerSurface" StaticImage="{Binding Player.CurrentFrameBitmap}" />
                <Border IsVisible="{Binding IsPreviewCaptionVisible}"
'@
$new = @'
              <Grid>
                <views:VideoSurface Name="PlayerSurface" StaticImage="{Binding Player.CurrentFrameBitmap}" />

                <!-- Preview-only safe-area guides. Star-sized rows/columns preserve the exact normalized
                     margins from SafeAreaPreset at any on-screen player size. -->
                <Grid IsVisible="{Binding ShowSafeArea}" IsHitTestVisible="False">
                  <Grid IsVisible="{Binding IsHorizontalSafeArea}" ColumnDefinitions="8*,84*,8*" RowDefinitions="8*,82*,10*">
                    <Border Grid.Column="1" Grid.Row="1" BorderBrush="#E6FFD54F" BorderThickness="2"/>
                  </Grid>
                  <Grid IsVisible="{Binding IsVerticalSafeArea}" ColumnDefinitions="8*,84*,8*" RowDefinitions="12*,72*,16*">
                    <Border Grid.Column="1" Grid.Row="1" BorderBrush="#E6FFD54F" BorderThickness="2"/>
                  </Grid>
                  <Grid IsVisible="{Binding IsSquareSafeArea}" ColumnDefinitions="8*,84*,8*" RowDefinitions="10*,80*,10*">
                    <Border Grid.Column="1" Grid.Row="1" BorderBrush="#E6FFD54F" BorderThickness="2"/>
                  </Grid>
                  <Border Background="#99000000" CornerRadius="4" Padding="6,3" Margin="8"
                          HorizontalAlignment="Left" VerticalAlignment="Top">
                    <TextBlock Text="{Binding SafeAreaGuideLabel}" Foreground="#FFFFD54F" FontSize="11" FontWeight="Bold"/>
                  </Border>
                </Grid>
                <Border IsVisible="{Binding IsPreviewCaptionVisible}"
'@
$t = Replace-Once $t $old $new 'Safe-area player overlay'
Write-Utf8 $path $t

$testPath = 'tests/NPVideoStudio.UnitTests/SafeAreaPreviewTests.cs'
$test = @'
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class SafeAreaPreviewTests
{
    [Theory]
    [InlineData(1920, 1080, "16:9", 0.08, 0.08, 0.08, 0.10)]
    [InlineData(1080, 1920, "9:16", 0.08, 0.08, 0.12, 0.16)]
    [InlineData(1080, 1080, "1:1", 0.08, 0.08, 0.10, 0.10)]
    public void ForFrame_ReturnsGuideMarginsUsedByPreview(int width, int height, string label, double left, double right, double top, double bottom)
    {
        var p = SafeAreaPreset.ForFrame(width, height);
        Assert.Equal(label, p.FormatLabel);
        Assert.Equal(left, p.Left, 6);
        Assert.Equal(right, p.Right, 6);
        Assert.Equal(top, p.Top, 6);
        Assert.Equal(bottom, p.Bottom, 6);
    }

    [Fact]
    public void VerticalGuide_PixelRectMatchesNormalizedMargins()
    {
        var r = SafeAreaPreset.Vertical9By16.ToPixelRect(1080, 1920);
        Assert.Equal(86, r.X);
        Assert.Equal(230, r.Y);
        Assert.Equal(907, r.Width);
        Assert.Equal(1382, r.Height);
    }
}
'@
[System.IO.File]::WriteAllText($testPath, $test, (New-Object System.Text.UTF8Encoding($false)))
