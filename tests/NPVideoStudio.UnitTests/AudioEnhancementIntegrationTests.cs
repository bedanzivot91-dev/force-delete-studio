using System.Diagnostics;
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
    public void SilentBaseVideo_GeneratesSilenceInsteadOfReferencingMissingAudioPad()
    {
        var video = new MediaAsset { Id = "v", FilePath = "silent.mp4", HasVideoStream = true, HasAudioStream = false, Duration = TimeSpan.FromSeconds(1) };
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { new() { MediaAssetId = "v", SourceTrimOutSeconds = 1 } } }
            }
        };
        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { video }, 320, 180, 30);
        Assert.Contains("anullsrc=r=44100:cl=stereo:d=1[a0]", plan.FilterComplexArgument);
        Assert.DoesNotContain("[0:a]", plan.FilterComplexArgument);
    }

    [Fact]
    public void StandaloneAudioTrack_RejectsMediaWithoutAudioStreamClearly()
    {
        var baseVideo = new MediaAsset { Id = "v", FilePath = "base.mp4", HasVideoStream = true, HasAudioStream = false, Duration = TimeSpan.FromSeconds(1) };
        var silent = new MediaAsset { Id = "s", FilePath = "not-audio.mp4", HasVideoStream = true, HasAudioStream = false, Duration = TimeSpan.FromSeconds(1) };
        var timeline = new Timeline
        {
            Tracks = new List<TimelineTrack>
            {
                new() { Kind = TimelineTrackKind.Video, Clips = new List<TimelineClip> { new() { MediaAssetId = "v", SourceTrimOutSeconds = 1 } } },
                new() { Kind = TimelineTrackKind.Audio, Clips = new List<TimelineClip> { new() { MediaAssetId = "s", SourceTrimOutSeconds = 1 } } }
            }
        };
        var ex = Assert.Throws<InvalidOperationException>(() => FfmpegFilterGraphBuilder.Build(timeline, new[] { baseVideo, silent }, 320, 180, 30));
        Assert.Contains("bez audio stream-a", ex.Message);
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
