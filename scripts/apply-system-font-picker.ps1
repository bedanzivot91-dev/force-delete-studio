$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Replace-Exact([string]$path, [string]$old, [string]$new) {
    $text = Get-Content -Raw -Path $path
    if ($text.Contains($new)) { return }
    if (-not $text.Contains($old)) { throw "Anchor nije pronađen u $path`n---`n$old" }
    Set-Content -Path $path -Value ($text.Replace($old, $new)) -Encoding UTF8
}

# 1. Persist a concrete system-font file per text/caption clip.
$timeline = 'src/NPVideoStudio.Domain/Timeline.cs'
Replace-Exact $timeline @'
    public CaptionFontChoice FontChoice { get; set; } = CaptionFontChoice.Default;
    public int FontSizePx { get; set; } = 36;
'@ @'
    public CaptionFontChoice FontChoice { get; set; } = CaptionFontChoice.Default;

    /// <summary>Optional concrete font file selected from the operating system. The renderer prefers this
    /// over <see cref="FontChoice"/> when it still exists; the enum remains a portable fallback when a
    /// project is opened on a machine that does not have the chosen font installed.</summary>
    public string? CustomFontFilePath { get; set; }
    public string? CustomFontFamilyName { get; set; }

    public int FontSizePx { get; set; } = 36;
'@

# 2. Make installed-font selection an undo-safe timeline edit, batch-copy it, and preserve it in snapshots.
$session = 'src/NPVideoStudio.AI/TimelineEditSession.cs'
Replace-Exact $session @'
    public void ApplyTextStyleToAllClipsOnTrack(string trackId, string sourceClipId)
'@ @'
    public void SetSystemFont(string clipId, string? fontFilePath, string? familyName)
    {
        var found = FindClipWithTrack(clipId);
        if (found.Clip is null || found.Clip.TextContent is null)
        {
            return;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(fontFilePath) ? null : fontFilePath.Trim();
        var normalizedFamily = string.IsNullOrWhiteSpace(familyName) ? null : familyName.Trim();
        if (string.Equals(found.Clip.CustomFontFilePath, normalizedPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(found.Clip.CustomFontFamilyName, normalizedFamily, StringComparison.Ordinal))
        {
            return;
        }

        SaveSnapshot();
        var liveClip = FindClipWithTrack(clipId).Clip!;
        liveClip.CustomFontFilePath = normalizedPath;
        liveClip.CustomFontFamilyName = normalizedFamily;
    }

    public void ApplyTextStyleToAllClipsOnTrack(string trackId, string sourceClipId)
'@
Replace-Exact $session @'
            target.FontChoice = source.FontChoice;
            target.FontSizePx = source.FontSizePx;
'@ @'
            target.FontChoice = source.FontChoice;
            target.CustomFontFilePath = source.CustomFontFilePath;
            target.CustomFontFamilyName = source.CustomFontFamilyName;
            target.FontSizePx = source.FontSizePx;
'@
Replace-Exact $session @'
        FontChoice = clip.FontChoice,
        FontSizePx = clip.FontSizePx,
'@ @'
        FontChoice = clip.FontChoice,
        CustomFontFilePath = clip.CustomFontFilePath,
        CustomFontFamilyName = clip.CustomFontFamilyName,
        FontSizePx = clip.FontSizePx,
'@

# 3. Prefer a real selected system font, with graceful fallback to the existing portable font enum.
$resolver = 'src/NPVideoStudio.Media/CaptionFontResolver.cs'
Replace-Exact $resolver @'
    public static string? ResolveFontFilePath(CaptionFontChoice choice, bool isBold = false, bool isItalic = false)
    {
        var (family, choiceIsBold) = choice switch
'@ @'
    public static string? ResolveFontFilePath(
        CaptionFontChoice choice,
        bool isBold = false,
        bool isItalic = false,
        string? customFontFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customFontFilePath) && File.Exists(customFontFilePath))
        {
            return Path.GetFullPath(customFontFilePath);
        }

        var (family, choiceIsBold) = choice switch
'@

# 4. Keep custom fonts in preview-range clones and use them in drawtext export.
$ffmpeg = 'src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs'
Replace-Exact $ffmpeg @'
            var fontFilePath = CaptionFontResolver.ResolveFontFilePath(clip.FontChoice, clip.IsTextBold, clip.IsTextItalic);
'@ @'
            var fontFilePath = CaptionFontResolver.ResolveFontFilePath(
                clip.FontChoice, clip.IsTextBold, clip.IsTextItalic, clip.CustomFontFilePath);
'@
Replace-Exact $ffmpeg @'
        FontChoice = clip.FontChoice,
        FontSizePx = clip.FontSizePx,
'@ @'
        FontChoice = clip.FontChoice,
        CustomFontFilePath = clip.CustomFontFilePath,
        CustomFontFamilyName = clip.CustomFontFamilyName,
        FontSizePx = clip.FontSizePx,
'@

# 5. Make InstalledFont render naturally in a ComboBox.
$catalog = 'src/NPVideoStudio.Media/SystemFontCatalog.cs'
Replace-Exact $catalog @'
    public string DisplayLabel => (IsBold, IsItalic) switch
    {
        (true, true) => $"{FamilyName} (podebljano, kurziv)",
        (true, false) => $"{FamilyName} (podebljano)",
        (false, true) => $"{FamilyName} (kurziv)",
        _ => FamilyName
    };
}
'@ @'
    public string DisplayLabel => (IsBold, IsItalic) switch
    {
        (true, true) => $"{FamilyName} (podebljano, kurziv)",
        (true, false) => $"{FamilyName} (podebljano)",
        (false, true) => $"{FamilyName} (kurziv)",
        _ => FamilyName
    };

    public override string ToString() => DisplayLabel;
}
'@

# 6. Wire the real catalog into each selected text/caption clip without bypassing undo.
$clipVm = 'src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs'
Replace-Exact $clipVm @'
using NPVideoStudio.Domain;
'@ @'
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
'@
Replace-Exact $clipVm @'
    private readonly Action<string, ClipKeyframeProperty, double>? _onKeyframeRemove;

    public TimelineClip Clip { get; }
'@ @'
    private readonly Action<string, ClipKeyframeProperty, double>? _onKeyframeRemove;
    private readonly Action<string, string?, string?>? _onSystemFontChanged;
    private static readonly Lazy<IReadOnlyList<InstalledFont>> SerbianSystemFonts =
        new(() => SystemFontCatalog.ListFontsUsableForSerbian());

    public TimelineClip Clip { get; }
'@
Replace-Exact $clipVm @'
    public IReadOnlyList<CaptionFontChoice> AvailableFontChoices { get; } = Enum.GetValues<CaptionFontChoice>();
    public IReadOnlyList<CaptionTextPosition> AvailablePositions { get; } = Enum.GetValues<CaptionTextPosition>();
'@ @'
    public IReadOnlyList<CaptionFontChoice> AvailableFontChoices { get; } = Enum.GetValues<CaptionFontChoice>();
    public IReadOnlyList<InstalledFont> AvailableSystemFonts => SerbianSystemFonts.Value;
    public InstalledFont? SelectedSystemFont
    {
        get => string.IsNullOrWhiteSpace(Clip.CustomFontFilePath)
            ? null
            : AvailableSystemFonts.FirstOrDefault(font =>
                string.Equals(font.FilePath, Clip.CustomFontFilePath, StringComparison.OrdinalIgnoreCase));
        set
        {
            var path = value?.FilePath;
            var family = value?.FamilyName;
            if (string.Equals(path, Clip.CustomFontFilePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(family, Clip.CustomFontFamilyName, StringComparison.Ordinal))
            {
                return;
            }
            _onSystemFontChanged?.Invoke(Clip.Id, path, family);
        }
    }
    public string SystemFontStatus => string.IsNullOrWhiteSpace(Clip.CustomFontFamilyName)
        ? "Koristi fallback font"
        : $"Sistemski font: {Clip.CustomFontFamilyName}";
    public ICommand ClearSystemFontCommand { get; }
    public IReadOnlyList<CaptionTextPosition> AvailablePositions { get; } = Enum.GetValues<CaptionTextPosition>();
'@
Replace-Exact $clipVm @'
        Action<string, ClipKeyframeProperty, double>? onKeyframeRemove = null)
'@ @'
        Action<string, ClipKeyframeProperty, double>? onKeyframeRemove = null,
        Action<string, string?, string?>? onSystemFontChanged = null)
'@
Replace-Exact $clipVm @'
        _onKeyframeRemove = onKeyframeRemove;
        IsOverlayClip = isOverlayClip;
'@ @'
        _onKeyframeRemove = onKeyframeRemove;
        _onSystemFontChanged = onSystemFontChanged;
        ClearSystemFontCommand = new RelayCommand(() => _onSystemFontChanged?.Invoke(Clip.Id, null, null));
        IsOverlayClip = isOverlayClip;
'@

# 7. Parent VM routes the picker through TimelineEditSession so persistence/undo remain correct.
$timelineVm = 'src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs'
Replace-Exact $timelineVm @'
        void OnKeyframeRemove(string clipId, ClipKeyframeProperty property, double localTime)
        {
            _session.RemoveKeyframe(clipId, property, localTime);
            TimelineChanged?.Invoke();
        }
        return new TimelineClipItemViewModel(clip, track.Id, ResolveClipLabel(clip), track.Kind == TimelineTrackKind.Video,
'@ @'
        void OnKeyframeRemove(string clipId, ClipKeyframeProperty property, double localTime)
        {
            _session.RemoveKeyframe(clipId, property, localTime);
            TimelineChanged?.Invoke();
        }
        void OnSystemFontChanged(string clipId, string? fontFilePath, string? familyName)
        {
            _session.SetSystemFont(clipId, fontFilePath, familyName);
            RefreshFromSession();
        }
        return new TimelineClipItemViewModel(clip, track.Id, ResolveClipLabel(clip), track.Kind == TimelineTrackKind.Video,
'@
Replace-Exact $timelineVm @'
            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove)
'@ @'
            _getPlayhead, OnKeyframeUpsert, OnKeyframeRemove, OnSystemFontChanged)
'@

# 8. Visible modern inspector: actual Serbian-safe installed fonts + explicit fallback reset.
$workspace = 'src/NPVideoStudio.App/Views/WorkspaceView.axaml'
Replace-Exact $workspace @'
              <TextBlock Text="Font" Classes="subtle" />
              <ComboBox ItemsSource="{Binding AvailableFontChoices}" SelectedItem="{Binding FontChoice}" />
'@ @'
              <TextBlock Text="Fallback font" Classes="subtle" />
              <ComboBox ItemsSource="{Binding AvailableFontChoices}" SelectedItem="{Binding FontChoice}" />
              <TextBlock Text="Instalirani font (proveren za srpska slova)" Classes="subtle" />
              <Grid ColumnDefinitions="*,Auto" ColumnSpacing="8">
                <ComboBox Name="InspectorSystemFont" ItemsSource="{Binding AvailableSystemFonts}"
                          SelectedItem="{Binding SelectedSystemFont}" MinWidth="190"
                          ToolTip.Tip="Prikazuje samo instalirane fontove koji imaju sva srpska latinična slova. Izabrani font se koristi i u finalnom FFmpeg exportu." />
                <Button Grid.Column="1" Name="InspectorClearSystemFont" Classes="ghost" Content="Fallback"
                        Command="{Binding ClearSystemFontCommand}" ToolTip.Tip="Vrati projekat na prenosivi fallback font." />
              </Grid>
              <TextBlock Text="{Binding SystemFontStatus}" Classes="micro" />
'@

# 9. Regression suite: persistence/undo, batch copy, actual renderer graph, fallback, and visible UI controls.
$tests = 'tests/NPVideoStudio.UnitTests/SystemFontPickerTests.cs'
@'
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NPVideoStudio.AI;
using NPVideoStudio.App.Views;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public class SystemFontPickerTests
{
    private static TimelineClip TextClip(string text = "ŠĐČĆŽ") => new()
    {
        TextContent = text,
        TimelineStartSeconds = 0,
        SourceTrimInSeconds = 0,
        SourceTrimOutSeconds = 2
    };

    [Fact]
    public void SetSystemFont_IsPersistedInSessionAndUndoSafe()
    {
        var clip = TextClip();
        var track = new TimelineTrack { Kind = TimelineTrackKind.Caption, Clips = { clip } };
        var session = new TimelineEditSession(new[] { track });

        session.SetSystemFont(clip.Id, @"C:\Windows\Fonts\arial.ttf", "Arial");

        var live = Assert.Single(Assert.Single(session.Tracks).Clips);
        Assert.Equal(@"C:\Windows\Fonts\arial.ttf", live.CustomFontFilePath);
        Assert.Equal("Arial", live.CustomFontFamilyName);

        session.Undo();
        live = Assert.Single(Assert.Single(session.Tracks).Clips);
        Assert.Null(live.CustomFontFilePath);
        Assert.Null(live.CustomFontFamilyName);
    }

    [Fact]
    public void ApplyTextStyleToAllClips_CopiesSelectedSystemFont()
    {
        var source = TextClip("Prvi");
        source.CustomFontFilePath = @"C:\Windows\Fonts\arial.ttf";
        source.CustomFontFamilyName = "Arial";
        var target = TextClip("Drugi");
        target.TimelineStartSeconds = 2;
        var track = new TimelineTrack { Kind = TimelineTrackKind.Caption, Clips = { source, target } };
        var session = new TimelineEditSession(new[] { track });

        session.ApplyTextStyleToAllClipsOnTrack(track.Id, source.Id);

        var copied = session.Tracks.Single().Clips.Single(c => c.Id == target.Id);
        Assert.Equal(source.CustomFontFilePath, copied.CustomFontFilePath);
        Assert.Equal(source.CustomFontFamilyName, copied.CustomFontFamilyName);
    }

    [Fact]
    public void CaptionFontResolver_PrefersExistingCustomFile_AndMissingFileFallsBack()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"npvs-font-{Guid.NewGuid():N}.ttf");
        File.WriteAllText(temp, "test-only");
        try
        {
            Assert.Equal(Path.GetFullPath(temp), CaptionFontResolver.ResolveFontFilePath(CaptionFontChoice.Default, customFontFilePath: temp));
            Assert.Null(CaptionFontResolver.ResolveFontFilePath(CaptionFontChoice.Default, customFontFilePath: temp + ".missing"));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void FilterGraph_UsesRealSerbianSafeInstalledFontFile()
    {
        var font = Assert.Single(SystemFontCatalog.ListFontsUsableForSerbian().Take(1));
        Assert.True(File.Exists(font.FilePath));
        Assert.True(font.SupportsSerbianLatin);

        var asset = new MediaAsset { Id = "video", FilePath = @"C:\media\video.mp4" };
        var timeline = new Timeline();
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips = { new TimelineClip { MediaAssetId = asset.Id, TimelineStartSeconds = 0, SourceTrimInSeconds = 0, SourceTrimOutSeconds = 3 } }
        });
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Caption,
            Clips = { new TimelineClip
            {
                TextContent = "ŠĐČĆŽ",
                TimelineStartSeconds = 0,
                SourceTrimInSeconds = 0,
                SourceTrimOutSeconds = 2,
                CustomFontFilePath = font.FilePath,
                CustomFontFamilyName = font.FamilyName
            } }
        });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset });

        Assert.Contains($"fontfile='{FfmpegFilterGraphBuilder.EscapeDrawtext(Path.GetFullPath(font.FilePath))}'", plan.FilterComplexArgument);
    }

    [AvaloniaFact]
    public void ModernInspector_ExposesSystemFontPickerAndFallbackButton()
    {
        var view = new WorkspaceView();
        var window = new Window { Width = 1600, Height = 1000, Content = view };
        window.Show();

        Assert.NotNull(view.FindControl<ComboBox>("InspectorSystemFont"));
        Assert.NotNull(view.FindControl<Button>("InspectorClearSystemFont"));

        window.Close();
    }
}
'@ | Set-Content -Path $tests -Encoding UTF8

Write-Host 'System-font production patch applied.'
