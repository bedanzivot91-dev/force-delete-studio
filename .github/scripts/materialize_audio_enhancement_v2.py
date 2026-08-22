from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8")


def write(rel, text):
    (ROOT / rel).write_text(text, encoding="utf-8")


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)

# 1) Domain persistence -------------------------------------------------------
p = "src/NPVideoStudio.Domain/Timeline.cs"
s = read(p)
s = replace_once(s,
'''public readonly record struct ClipColorGradingSettings(
    double ExposureStops,
    double Highlights,
    double Shadows,
    double Temperature,
    double Tint);

public enum ClipMaskType''',
'''public readonly record struct ClipColorGradingSettings(
    double ExposureStops,
    double Highlights,
    double Shadows,
    double Temperature,
    double Tint);

/// <summary>Persisted, non-destructive audio cleanup controls. These values are applied by the real
/// FFmpeg render/preview chain to source audio; the original media file is never modified.</summary>
public readonly record struct ClipAudioEnhancementSettings(
    bool NoiseReductionEnabled,
    double NoiseReductionStrength,
    bool EnhanceVoiceEnabled,
    bool LoudnessNormalizationEnabled);

public enum ClipMaskType''', "domain settings record")
s = replace_once(s,
'''    public bool IsMuted { get; set; }
    public double Volume { get; set; } = 1.0;

    // --- Layer compositing''',
'''    public bool IsMuted { get; set; }
    public double Volume { get; set; } = 1.0;

    // --- Audio enhancement ---------------------------------------------------------------
    public bool AudioNoiseReductionEnabled { get; set; }
    /// <summary>0..1 strength mapped to a conservative FFmpeg afftdn reduction amount.</summary>
    public double AudioNoiseReductionStrength { get; set; } = 0.5;
    public bool AudioEnhanceVoiceEnabled { get; set; }
    public bool AudioLoudnessNormalizationEnabled { get; set; }

    // --- Layer compositing''', "domain audio fields")
write(p, s)

# 2) Undo/Redo session --------------------------------------------------------
p = "src/NPVideoStudio.AI/TimelineEditSession.cs"
s = read(p)
s = replace_once(s,
'''        live.Tint = tint;
    }

    public void SetSpeedCurvePreset''',
'''        live.Tint = tint;
    }

    public void SetClipAudioEnhancement(string clipId, ClipAudioEnhancementSettings settings)
    {
        var (_, clip) = FindClipWithTrack(clipId);
        if (clip is null || clip.MediaAssetId is null) return;

        var strength = Math.Clamp(settings.NoiseReductionStrength, 0, 1);
        if (clip.AudioNoiseReductionEnabled == settings.NoiseReductionEnabled &&
            Math.Abs(clip.AudioNoiseReductionStrength - strength) < 1e-9 &&
            clip.AudioEnhanceVoiceEnabled == settings.EnhanceVoiceEnabled &&
            clip.AudioLoudnessNormalizationEnabled == settings.LoudnessNormalizationEnabled)
            return;

        SaveSnapshot();
        var live = FindClipWithTrack(clipId).Clip!;
        live.AudioNoiseReductionEnabled = settings.NoiseReductionEnabled;
        live.AudioNoiseReductionStrength = strength;
        live.AudioEnhanceVoiceEnabled = settings.EnhanceVoiceEnabled;
        live.AudioLoudnessNormalizationEnabled = settings.LoudnessNormalizationEnabled;
    }

    public void SetSpeedCurvePreset''', "session audio setter")
s = replace_once(s,
'''        IsMuted = clip.IsMuted,
        Volume = clip.Volume,
        ScalePercent = clip.ScalePercent,''',
'''        IsMuted = clip.IsMuted,
        Volume = clip.Volume,
        AudioNoiseReductionEnabled = clip.AudioNoiseReductionEnabled,
        AudioNoiseReductionStrength = clip.AudioNoiseReductionStrength,
        AudioEnhanceVoiceEnabled = clip.AudioEnhanceVoiceEnabled,
        AudioLoudnessNormalizationEnabled = clip.AudioLoudnessNormalizationEnabled,
        ScalePercent = clip.ScalePercent,''', "session clone audio")
write(p, s)

# 3) Renderer ---------------------------------------------------------------
p = "src/NPVideoStudio.Media/FfmpegFilterGraphBuilder.cs"
s = read(p)
s = replace_once(s,
'''            audioFilter.Append(FormattableString.Invariant(
                $"[{inputIndex}:a]atrim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},asetpts=PTS-STARTPTS,volume={(clip.IsFreezeFrame ? 0 : volume)}"));
            if (clip.IsReversed && !clip.IsFreezeFrame)
            {
                audioFilter.Append(",areverse");
            }            audioFilter.Append(BuildAudioSpeedFilter(clip));
            if (clip.FadeInSeconds > 0)''',
'''            audioFilter.Append(FormattableString.Invariant(
                $"[{inputIndex}:a]atrim=start={clip.SourceTrimInSeconds}:end={clip.SourceTrimOutSeconds},asetpts=PTS-STARTPTS"));
            if (clip.IsReversed && !clip.IsFreezeFrame)
            {
                audioFilter.Append(",areverse");
            }
            audioFilter.Append(BuildAudioSpeedFilter(clip));
            audioFilter.Append(BuildAudioEnhancementFilters(clip));
            audioFilter.Append(FormattableString.Invariant($",volume={(clip.IsFreezeFrame ? 0 : volume)}"));
            if (clip.FadeInSeconds > 0)''', "base video audio chain")
s = replace_once(s,
'''                chain.Append(BuildAudioSpeedFilter(clip));
                chain.Append(FormattableString.Invariant($",volume={volume}"));''',
'''                chain.Append(BuildAudioSpeedFilter(clip));
                chain.Append(BuildAudioEnhancementFilters(clip));
                chain.Append(FormattableString.Invariant($",volume={volume}"));''', "standalone audio chain")
s = replace_once(s,
'''        IsMuted = clip.IsMuted,
        Volume = clip.Volume,
        ScalePercent = clip.ScalePercent,''',
'''        IsMuted = clip.IsMuted,
        Volume = clip.Volume,
        AudioNoiseReductionEnabled = clip.AudioNoiseReductionEnabled,
        AudioNoiseReductionStrength = clip.AudioNoiseReductionStrength,
        AudioEnhanceVoiceEnabled = clip.AudioEnhanceVoiceEnabled,
        AudioLoudnessNormalizationEnabled = clip.AudioLoudnessNormalizationEnabled,
        ScalePercent = clip.ScalePercent,''', "range clone audio")
s = replace_once(s,
'''        return stages.Count == 0
            ? string.Empty
            : "," + string.Join(",", stages.Select(s => FormattableString.Invariant($"atempo={s}")));
    }
    private static string TransitionName''',
'''        return stages.Count == 0
            ? string.Empty
            : "," + string.Join(",", stages.Select(s => FormattableString.Invariant($"atempo={s}")));
    }

    /// <summary>Real per-clip audio cleanup. The chain intentionally uses only filters present in the
    /// bundled Windows FFmpeg build and returns an empty string for neutral settings.</summary>
    public static string BuildAudioEnhancementFilters(TimelineClip clip)
    {
        var parts = new List<string>();
        if (clip.AudioNoiseReductionEnabled)
        {
            var strength = Math.Clamp(clip.AudioNoiseReductionStrength, 0, 1);
            var reductionDb = 6 + strength * 24; // afftdn nr: 6..30 dB, conservative and stable.
            parts.Add($"afftdn=nr={F(reductionDb)}:nf=-50:tn=1");
        }

        if (clip.AudioEnhanceVoiceEnabled)
        {
            parts.Add("highpass=f=80");
            parts.Add("lowpass=f=12000");
            parts.Add("equalizer=f=2500:t=q:w=1:g=3");
            parts.Add("acompressor=threshold=0.125:ratio=3:attack=20:release=250:makeup=1.5");
        }

        if (clip.AudioLoudnessNormalizationEnabled)
        {
            parts.Add("loudnorm=I=-16:TP=-1.5:LRA=11");
        }

        return parts.Count == 0 ? string.Empty : "," + string.Join(",", parts);
    }

    private static string TransitionName''', "audio filter builder")
write(p, s)

# 4) Clip ViewModel ----------------------------------------------------------
p = "src/NPVideoStudio.App/ViewModels/TimelineClipItemViewModel.cs"
s = read(p)
s = replace_once(s,
'''    private readonly Action<string, ClipColorGradingSettings>? _onColorGradingChanged;
    private readonly Action<string, SpeedCurvePreset>? _onSpeedCurvePresetChanged;''',
'''    private readonly Action<string, ClipColorGradingSettings>? _onColorGradingChanged;
    private readonly Action<string, ClipAudioEnhancementSettings>? _onAudioEnhancementChanged;
    private readonly Action<string, SpeedCurvePreset>? _onSpeedCurvePresetChanged;''', "vm callback field")
s = replace_once(s,
'''    public bool IsOverlayClip { get; }
    public bool IsPictureClip => IsVideoClip || IsOverlayClip;
    public bool IsAudioClip { get; }
    public bool SupportsKeyframes''',
'''    public bool IsOverlayClip { get; }
    public bool IsPictureClip => IsVideoClip || IsOverlayClip;
    public bool IsAudioClip { get; }
    /// <summary>True for both standalone audio and video media that actually contains an audio stream.</summary>
    public bool HasAudioStream { get; }
    public bool CanUseAudioEnhancement => HasAudioStream && !Clip.IsFreezeFrame;
    public bool SupportsKeyframes''', "vm audio capability")
s = replace_once(s,
'''    public double Tint
    {
        get => Clip.Tint;
        set { if (Math.Abs(Clip.Tint - value) < 1e-6) return; PushColorGrading(s => s with { Tint = value }); }
    }

    /// <summary>0.5 = slow motion''',
'''    public double Tint
    {
        get => Clip.Tint;
        set { if (Math.Abs(Clip.Tint - value) < 1e-6) return; PushColorGrading(s => s with { Tint = value }); }
    }

    private void PushAudioEnhancement(Func<ClipAudioEnhancementSettings, ClipAudioEnhancementSettings> mutate)
    {
        var current = new ClipAudioEnhancementSettings(
            AudioNoiseReductionEnabled, AudioNoiseReductionStrength,
            AudioEnhanceVoiceEnabled, AudioLoudnessNormalizationEnabled);
        _onAudioEnhancementChanged?.Invoke(Clip.Id, mutate(current));
    }

    public bool AudioNoiseReductionEnabled
    {
        get => Clip.AudioNoiseReductionEnabled;
        set { if (Clip.AudioNoiseReductionEnabled == value) return; PushAudioEnhancement(s => s with { NoiseReductionEnabled = value }); }
    }
    public double AudioNoiseReductionStrength
    {
        get => Clip.AudioNoiseReductionStrength;
        set { if (Math.Abs(Clip.AudioNoiseReductionStrength - value) < 1e-6) return; PushAudioEnhancement(s => s with { NoiseReductionStrength = value }); }
    }
    public bool AudioEnhanceVoiceEnabled
    {
        get => Clip.AudioEnhanceVoiceEnabled;
        set { if (Clip.AudioEnhanceVoiceEnabled == value) return; PushAudioEnhancement(s => s with { EnhanceVoiceEnabled = value }); }
    }
    public bool AudioLoudnessNormalizationEnabled
    {
        get => Clip.AudioLoudnessNormalizationEnabled;
        set { if (Clip.AudioLoudnessNormalizationEnabled == value) return; PushAudioEnhancement(s => s with { LoudnessNormalizationEnabled = value }); }
    }

    /// <summary>0.5 = slow motion''', "vm audio properties")
s = replace_once(s,
'''    public IReadOnlyList<SpeedCurvePreset> AvailableSpeedCurvePresets { get; } = Enum.GetValues<SpeedCurvePreset>();
    public bool CanUseSpeedCurve => HasSourceMedia && !IsTextClip && !Clip.IsReversed && !Clip.IsFreezeFrame;''',
'''    public IReadOnlyList<SpeedCurvePreset> AvailableSpeedCurvePresets { get; } = Enum.GetValues<SpeedCurvePreset>();
    public bool CanUseSpeedCurve => HasSourceMedia && (IsVideoClip || IsAudioClip) && !Clip.IsReversed && !Clip.IsFreezeFrame;''', "speed curve image guard")
s = replace_once(s,
'''        Action<string, bool>? onAutoReframeChanged = null,
        Action<string, ClipColorGradingSettings>? onColorGradingChanged = null)''',
'''        Action<string, bool>? onAutoReframeChanged = null,
        Action<string, ClipColorGradingSettings>? onColorGradingChanged = null,
        bool hasAudioStream = false,
        Action<string, ClipAudioEnhancementSettings>? onAudioEnhancementChanged = null)''', "vm constructor params")
s = replace_once(s,
'''        _onEffectsChanged = onEffectsChanged;
        _onColorGradingChanged = onColorGradingChanged;
        _onSpeedCurvePresetChanged''',
'''        _onEffectsChanged = onEffectsChanged;
        _onColorGradingChanged = onColorGradingChanged;
        _onAudioEnhancementChanged = onAudioEnhancementChanged;
        _onSpeedCurvePresetChanged''', "vm constructor callback assign")
s = replace_once(s,
'''        IsVideoClip = isVideoClip;
        IsAudioClip = isAudioClip;
        SplitAtPlayheadCommand''',
'''        IsVideoClip = isVideoClip;
        IsAudioClip = isAudioClip;
        HasAudioStream = hasAudioStream;
        SplitAtPlayheadCommand''', "vm constructor audio assign")
write(p, s)

# 5) Timeline ViewModel wiring ----------------------------------------------
p = "src/NPVideoStudio.App/ViewModels/TimelineViewModel.cs"
s = read(p)
s = replace_once(s,
'''        void OnColorGradingChanged(string clipId, ClipColorGradingSettings settings)
        {
            _session.SetColorGrading(clipId, settings);
            RefreshFromSession();
        }
        void OnSpeedCurvePresetChanged''',
'''        void OnColorGradingChanged(string clipId, ClipColorGradingSettings settings)
        {
            _session.SetColorGrading(clipId, settings);
            RefreshFromSession();
        }
        void OnAudioEnhancementChanged(string clipId, ClipAudioEnhancementSettings settings)
        {
            _session.SetClipAudioEnhancement(clipId, settings);
            RefreshFromSession();
        }
        void OnSpeedCurvePresetChanged''', "timeline vm audio callback")
s = replace_once(s,
'''        var sourceDurationSeconds = clip.MediaAssetId is null
            ? 0
            : AvailableMedia.FirstOrDefault(m => m.Asset.Id == clip.MediaAssetId)?.Asset.Duration.TotalSeconds
              ?? Math.Max(clip.SourceTrimOutSeconds, 0);

        return new TimelineClipItemViewModel''',
'''        var sourceAsset = clip.MediaAssetId is null
            ? null
            : AvailableMedia.FirstOrDefault(m => m.Asset.Id == clip.MediaAssetId)?.Asset;
        var sourceDurationSeconds = sourceAsset?.Duration.TotalSeconds
            ?? (clip.MediaAssetId is null ? 0 : Math.Max(clip.SourceTrimOutSeconds, 0));
        var hasAudioStream = sourceAsset?.HasAudioStream == true;

        return new TimelineClipItemViewModel''', "timeline vm source asset")
s = replace_once(s,
'''            OnTrackingRegionChanged, OnMotionTrackingRequested, OnAutoReframeChanged, OnColorGradingChanged)
        {''',
'''            OnTrackingRegionChanged, OnMotionTrackingRequested, OnAutoReframeChanged, OnColorGradingChanged,
            hasAudioStream, OnAudioEnhancementChanged)
        {''', "timeline vm ctor args")
write(p, s)

# 6) Active Studio 2026 inspector -------------------------------------------
p = "src/NPVideoStudio.App/Views/ModernInspectorView.axaml"
s = read(p)
start = s.index('      <TabItem Header="Audio" IsVisible="{Binding IsAudioClip}">')
end = s.index('      <TabItem Header="Transform"', start)
new_audio = '''      <TabItem Header="Audio" IsVisible="{Binding HasAudioStream}">
        <ScrollViewer>
          <StackPanel Spacing="10" Margin="2,10,2,2">
            <Border Classes="inspectorSection" IsVisible="{Binding IsAudioClip}">
              <StackPanel Spacing="8">
                <TextBlock Text="Audio klip" Classes="section" />
                <TextBlock Text="Brzina" Classes="subtle"/>
                <NumericUpDown Value="{Binding SpeedMultiplier}" Minimum="0.25" Maximum="4" Increment="0.25"/>
                <Border Name="ModernAudioSpeedCurvePanel" Classes="inspectorSection" IsVisible="{Binding CanUseSpeedCurve}" Margin="0,4,0,0">
                  <StackPanel Spacing="5">
                    <TextBlock Text="Velocity / Speed Curve" Classes="section"/>
                    <ComboBox Name="ModernAudioSpeedCurve" ItemsSource="{Binding AvailableSpeedCurvePresets}" SelectedItem="{Binding SpeedCurvePreset}"/>
                    <TextBlock Text="Kriva menja tempo uz očuvanje visine tona. Ručna Brzina iznad isključuje aktivnu krivu." Classes="subtle" TextWrapping="Wrap"/>
                  </StackPanel>
                </Border>
              </StackPanel>
            </Border>
            <Border Name="ModernAudioEnhancementPanel" Classes="inspectorSection" IsVisible="{Binding CanUseAudioEnhancement}">
              <StackPanel Spacing="8">
                <TextBlock Text="Poboljšanje zvuka" Classes="section" />
                <ToggleButton Name="ModernAudioNoiseReduction" Content="Smanji šum" IsChecked="{Binding AudioNoiseReductionEnabled}" HorizontalAlignment="Left"/>
                <StackPanel Spacing="4" IsVisible="{Binding AudioNoiseReductionEnabled}">
                  <TextBlock Text="Jačina redukcije šuma" Classes="subtle"/>
                  <Slider Minimum="0" Maximum="1" Value="{Binding AudioNoiseReductionStrength}" TickFrequency="0.1"/>
                </StackPanel>
                <ToggleButton Name="ModernAudioEnhanceVoice" Content="Istakni govor" IsChecked="{Binding AudioEnhanceVoiceEnabled}" HorizontalAlignment="Left"/>
                <ToggleButton Name="ModernAudioLoudnessNormalization" Content="Normalizuj glasnoću (-16 LUFS)" IsChecked="{Binding AudioLoudnessNormalizationEnabled}" HorizontalAlignment="Left"/>
                <TextBlock Text="Radi i na zvuku video klipa i na zasebnoj audio traci. Originalni fajl se ne menja; obrada ulazi u pravi FFmpeg preview/export." Classes="subtle" TextWrapping="Wrap"/>
              </StackPanel>
            </Border>
            <TextBlock Text="Utišavanje i fade kontrole su uvek dostupne u zaglavlju inspektora." Classes="subtle" TextWrapping="Wrap"/>
          </StackPanel>
        </ScrollViewer>
      </TabItem>

'''
s = s[:start] + new_audio + s[end:]
write(p, s)

# 7) Tests ------------------------------------------------------------------
test = r'''using System.Diagnostics;
using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class AudioEnhancementIntegrationTests
{
    [Fact]
    public void AudioEnhancement_IsUndoRedoSafeAndClamped()
    {
        var clip = new TimelineClip { MediaAssetId = "m", SourceTrimOutSeconds = 2 };
        var session = new TimelineEditSession(new[]
        {
            new TimelineTrack { Kind = TimelineTrackKind.Audio, Clips = new List<TimelineClip> { clip } }
        });

        session.SetClipAudioEnhancement(clip.Id, new ClipAudioEnhancementSettings(true, 7, true, true));
        var edited = session.Tracks.Single().Clips.Single();
        Assert.True(edited.AudioNoiseReductionEnabled);
        Assert.Equal(1, edited.AudioNoiseReductionStrength, 6);
        Assert.True(edited.AudioEnhanceVoiceEnabled);
        Assert.True(edited.AudioLoudnessNormalizationEnabled);

        session.Undo();
        var undone = session.Tracks.Single().Clips.Single();
        Assert.False(undone.AudioNoiseReductionEnabled);
        Assert.Equal(.5, undone.AudioNoiseReductionStrength, 6);
        Assert.False(undone.AudioEnhanceVoiceEnabled);
        Assert.False(undone.AudioLoudnessNormalizationEnabled);

        session.Redo();
        Assert.True(session.Tracks.Single().Clips.Single().AudioLoudnessNormalizationEnabled);
    }

    [Fact]
    public async Task AudioEnhancement_RoundTripsThroughProjectRepository()
    {
        var dir = Path.Combine(Path.GetTempPath(), "npvs-audio-enhance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "audio.npvsproject");
            var project = new Project { Name = "Audio" };
            project.Timeline.Tracks.Add(new TimelineTrack
            {
                Kind = TimelineTrackKind.Audio,
                Clips = new List<TimelineClip>
                {
                    new() { MediaAssetId = "a", SourceTrimOutSeconds = 1, AudioNoiseReductionEnabled = true, AudioNoiseReductionStrength = .7, AudioEnhanceVoiceEnabled = true, AudioLoudnessNormalizationEnabled = true }
                }
            });
            var repo = new ProjectRepository();
            await repo.SaveAsync(project, path);
            var loaded = await repo.LoadAsync(path);
            var clip = loaded.Timeline.Tracks.Single().Clips.Single();
            Assert.True(clip.AudioNoiseReductionEnabled);
            Assert.Equal(.7, clip.AudioNoiseReductionStrength, 6);
            Assert.True(clip.AudioEnhanceVoiceEnabled);
            Assert.True(clip.AudioLoudnessNormalizationEnabled);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AudioEnhancement_EmitsRealFfmpegFilters()
    {
        var clip = new TimelineClip { AudioNoiseReductionEnabled = true, AudioNoiseReductionStrength = .6, AudioEnhanceVoiceEnabled = true, AudioLoudnessNormalizationEnabled = true };
        var filters = FfmpegFilterGraphBuilder.BuildAudioEnhancementFilters(clip);
        Assert.Contains("afftdn=", filters);
        Assert.Contains("highpass=", filters);
        Assert.Contains("lowpass=", filters);
        Assert.Contains("equalizer=", filters);
        Assert.Contains("acompressor=", filters);
        Assert.Contains("loudnorm=", filters);
    }

    [Fact]
    public void RangeExtraction_PreservesAudioEnhancement()
    {
        var clip = new TimelineClip
        {
            MediaAssetId = "a", SourceTrimOutSeconds = 4,
            AudioNoiseReductionEnabled = true, AudioNoiseReductionStrength = .8,
            AudioEnhanceVoiceEnabled = true, AudioLoudnessNormalizationEnabled = true
        };
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new() { Kind = TimelineTrackKind.Audio, Clips = new List<TimelineClip> { clip } }
            }
        };
        var sliced = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, 1, 3).Tracks.Single().Clips.Single();
        Assert.True(sliced.AudioNoiseReductionEnabled);
        Assert.Equal(.8, sliced.AudioNoiseReductionStrength, 6);
        Assert.True(sliced.AudioEnhanceVoiceEnabled);
        Assert.True(sliced.AudioLoudnessNormalizationEnabled);
    }

    [Fact]
    public void Studio2026Inspector_ExposesAudioEnhancementForAnyAudioStream()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernInspectorView.axaml"));
        Assert.Contains("Header=\"Audio\" IsVisible=\"{Binding HasAudioStream}\"", xaml);
        Assert.Contains("ModernAudioEnhancementPanel", xaml);
        foreach (var binding in new[] { "AudioNoiseReductionEnabled", "AudioNoiseReductionStrength", "AudioEnhanceVoiceEnabled", "AudioLoudnessNormalizationEnabled" })
            Assert.Contains($"Binding {binding}", xaml);
    }

    [Fact]
    public void SpeedCurve_IsNotOfferedToImageOnlyClip()
    {
        var clip = new TimelineClip { MediaAssetId = "img", SourceTrimOutSeconds = 3 };
        var vm = BuildMinimalViewModel(clip, isVideo: false, isAudio: false, hasAudio: false);
        Assert.False(vm.CanUseSpeedCurve);
    }

    [Fact]
    public async Task RealFfmpeg_ExecutesCompleteAudioEnhancementChain()
    {
        var ffmpeg = FfmpegLocator.ResolveFfmpegPath(null);
        var dir = Path.Combine(Path.GetTempPath(), "npvs-audio-ffmpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var output = Path.Combine(dir, "enhanced.wav");
            var clip = new TimelineClip { AudioNoiseReductionEnabled = true, AudioNoiseReductionStrength = .65, AudioEnhanceVoiceEnabled = true, AudioLoudnessNormalizationEnabled = true };
            var filter = FfmpegFilterGraphBuilder.BuildAudioEnhancementFilters(clip).TrimStart(',');
            using var process = new Process { StartInfo = new ProcessStartInfo { FileName = ffmpeg, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true } };
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1", "-af", filter, "-c:a", "pcm_s16le", output })
                process.StartInfo.ArgumentList.Add(arg);
            Assert.True(process.Start());
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stderr = await stderrTask;
            _ = await stdoutTask;
            Assert.True(process.ExitCode == 0, stderr);
            Assert.True(File.Exists(output) && new FileInfo(output).Length > 1000);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static NPVideoStudio.App.ViewModels.TimelineClipItemViewModel BuildMinimalViewModel(TimelineClip clip, bool isVideo, bool isAudio, bool hasAudio)
    {
        var noOp = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        return new NPVideoStudio.App.ViewModels.TimelineClipItemViewModel(
            clip, "t", "clip", isVideo,
            noOp, noOp, noOp, noOp, noOp, noOp, noOp, noOp, noOp,
            isAudioClip: isAudio, sourceMediaDurationSeconds: 3, hasAudioStream: hasAudio);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("NPVideoStudio.sln nije pronađen iz test output foldera.");
    }
}
'''
write("tests/NPVideoStudio.UnitTests/AudioEnhancementIntegrationTests.cs", test)

# Self-delete so the feature diff contains only production/test code.
for rel in [".github/scripts/materialize_audio_enhancement_v2.py", ".github/workflows/materialize-audio-enhancement-v2.yml"]:
    target = ROOT / rel
    if target.exists():
        target.unlink()

print("Audio enhancement v2 materialized successfully.")
