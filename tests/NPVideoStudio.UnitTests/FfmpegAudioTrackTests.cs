using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Covers mixing standalone Audio-kind tracks (music, voice-over) into the render.
///
/// This was the app's most damaging gap for its actual purpose: the "Muzički spot" template creates an
/// Audio track and the UI offers "+ Audio traka", but the exported video contained no music at all - the
/// track was silently skipped. These tests exist so that can never silently regress.
/// </summary>
public class FfmpegAudioTrackTests
{
    private static MediaAsset Asset(string id, string path, MediaKind kind = MediaKind.Video) => new()
    {
        Id = id,
        FilePath = path,
        Kind = kind,
        Duration = TimeSpan.FromSeconds(30),
        Width = kind == MediaKind.Video ? 1920 : 0,
        Height = kind == MediaKind.Video ? 1080 : 0
    };

    private static TimelineClip Clip(string assetId, double start, double trimOut) => new()
    {
        MediaAssetId = assetId,
        TimelineStartSeconds = start,
        SourceTrimInSeconds = 0,
        SourceTrimOutSeconds = trimOut
    };

    private static (Timeline Timeline, List<MediaAsset> Library) VideoOnly()
    {
        var timeline = new Timeline
        {
            Tracks = { new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { Clip("v", 0, 10) } } }
        };
        return (timeline, new List<MediaAsset> { Asset("v", "/tmp/v.mp4") });
    }

    private static (Timeline Timeline, List<MediaAsset> Library) WithMusic(
        Action<TimelineTrack>? configureTrack = null, Action<TimelineClip>? configureClip = null, double startSeconds = 0)
    {
        var (timeline, library) = VideoOnly();
        library.Add(Asset("m", "/tmp/pesma.mp3", MediaKind.Audio));

        var clip = Clip("m", startSeconds, 8);
        configureClip?.Invoke(clip);

        var track = new TimelineTrack { Kind = TimelineTrackKind.Audio, Name = "Muzika", Clips = { clip } };
        configureTrack?.Invoke(track);
        timeline.Tracks.Add(track);

        return (timeline, library);
    }

    [Fact]
    public void NoAudioTrack_GraphIsUnchanged_SoExistingProjectsRenderExactlyAsBefore()
    {
        var (timeline, library) = VideoOnly();

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.DoesNotContain("amix=", plan.FilterComplexArgument);
    }

    [Fact]
    public void MusicTrack_IsActuallyMixedIntoTheExportedAudio()
    {
        var (timeline, library) = WithMusic();

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.Contains("amix=", plan.FilterComplexArgument);
        Assert.Contains("/tmp/pesma.mp3", plan.InputFilePaths);
        // The mapped audio must be the MIX, not the video's own audio - otherwise the music is built and
        // then thrown away, which is exactly the bug this fixes.
        Assert.Equal("[amixed]", plan.AudioMapLabel);
    }

    [Fact]
    public void MusicPlacedLaterOnTheTimeline_IsDelayedToThatPositionOnEveryChannel()
    {
        var (timeline, library) = WithMusic(startSeconds: 12);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        // all=1 matters: without it adelay delays only the first channel and the music comes out lopsided.
        Assert.Contains("adelay=12000:all=1", plan.FilterComplexArgument);
    }

    [Fact]
    public void MusicAtTheVeryStart_IsNotDelayedAtAll()
    {
        var (timeline, library) = WithMusic(startSeconds: 0);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.DoesNotContain("adelay=", plan.FilterComplexArgument);
    }

    [Fact]
    public void TrackVolumeAndClipVolume_AreCombined()
    {
        var (timeline, library) = WithMusic(
            configureTrack: t => t.Volume = 0.5,
            configureClip: c => c.Volume = 0.5);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.Contains("volume=0.25", plan.FilterComplexArgument);
    }

    [Fact]
    public void MutedMusicTrack_IsLeftOutOfTheMixEntirely()
    {
        var (timeline, library) = WithMusic(configureTrack: t => t.IsMuted = true);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.DoesNotContain("amix=", plan.FilterComplexArgument);
    }

    [Fact]
    public void HiddenMusicTrack_IsLeftOutToo()
    {
        var (timeline, library) = WithMusic(configureTrack: t => t.IsHidden = true);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.DoesNotContain("amix=", plan.FilterComplexArgument);
    }

    [Fact]
    public void SoloOnOneAudioTrack_SilencesTheOtherAudioTracks()
    {
        var (timeline, library) = WithMusic();
        library.Add(Asset("drugi", "/tmp/drugi.mp3", MediaKind.Audio));
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Audio,
            IsSolo = true,
            Clips = { Clip("drugi", 0, 5) }
        });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.Contains("/tmp/drugi.mp3", plan.InputFilePaths);
        Assert.DoesNotContain("/tmp/pesma.mp3", plan.InputFilePaths);
    }

    [Fact]
    public void MusicFades_AreAppliedToTheMusicItself()
    {
        var (timeline, library) = WithMusic(configureClip: c =>
        {
            c.FadeInSeconds = 2;
            c.FadeOutSeconds = 3;
        });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.Contains("afade=t=in:st=0:d=2", plan.FilterComplexArgument);
        Assert.Contains("afade=t=out:st=5:d=3", plan.FilterComplexArgument);
    }

    [Fact]
    public void Mix_FollowsTheVideosLength_SoALongerSongDoesNotStretchTheExport()
    {
        var (timeline, library) = WithMusic();

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        Assert.Contains("duration=first", plan.FilterComplexArgument);
        // normalize=0 keeps each source at the volume the user actually set, instead of ffmpeg quietly
        // halving everything to avoid clipping.
        Assert.Contains("normalize=0", plan.FilterComplexArgument);
    }

    [Fact]
    public void SeveralMusicClips_AreAllMixedIn()
    {
        var (timeline, library) = WithMusic();
        library.Add(Asset("drugi", "/tmp/drugi.mp3", MediaKind.Audio));
        timeline.Tracks[^1].Clips.Add(Clip("drugi", 20, 5));

        var plan = FfmpegFilterGraphBuilder.Build(timeline, library);

        // Video audio + 2 music clips.
        Assert.Contains("amix=inputs=3", plan.FilterComplexArgument);
    }

    [Fact]
    public void MusicReferencingAMissingAsset_FailsLoudlyInSerbian()
    {
        var (timeline, library) = VideoOnly();
        timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Audio, Clips = { Clip("nema-ga", 0, 5) } });

        var ex = Assert.Throws<InvalidOperationException>(() => FfmpegFilterGraphBuilder.Build(timeline, library));

        Assert.Contains("Audio traka referencira medij koji ne postoji", ex.Message);
    }
}
