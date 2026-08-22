using System.Collections.ObjectModel;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class TrackVolumeUiTests
{
    [Fact]
    public void AudioTrackVolume_FromVisibleTrackViewModel_PersistsAndUndoRestoresIt()
    {
        var project = new Project { Name = "Track volume" };
        project.Timeline.Tracks.Add(new TimelineTrack
        {
            Id = "audio-track",
            Kind = TimelineTrackKind.Audio,
            Name = "Muzika",
            Volume = 1.0
        });

        var timeline = new TimelineViewModel(project, new ObservableCollection<MediaAssetViewModel>(), () => 0);
        var audioTrack = Assert.Single(timeline.Tracks);
        Assert.True(audioTrack.HasVolumeControl);
        Assert.Equal(100, audioTrack.VolumePercent, 6);

        audioTrack.VolumePercent = 35;

        Assert.Equal(0.35, Assert.Single(timeline.CurrentTracks).Volume, 6);
        Assert.Equal(35, Assert.Single(timeline.Tracks).VolumePercent, 6);

        timeline.UndoCommand.Execute(null);
        Assert.Equal(1.0, Assert.Single(timeline.CurrentTracks).Volume, 6);
        Assert.Equal(100, Assert.Single(timeline.Tracks).VolumePercent, 6);

        timeline.Tracks[0].VolumePercent = 150;
        timeline.SaveToProject();
        Assert.Equal(1.5, Assert.Single(project.Timeline.Tracks).Volume, 6);
    }

    [Fact]
    public void NonAudioTracks_DoNotAdvertiseTrackVolumeControl()
    {
        foreach (var kind in new[]
        {
            TimelineTrackKind.Video,
            TimelineTrackKind.Caption,
            TimelineTrackKind.Text,
            TimelineTrackKind.ImageOverlay
        })
        {
            var track = new TimelineTrack { Kind = kind };
            var noop = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            var vm = new TimelineTrackItemViewModel(track, noop, noop, noop, noop, noop, noop);
            Assert.False(vm.HasVolumeControl);
        }
    }

    [Fact]
    public void RenderGraph_AudioTrackVolume_IsActuallyMultipliedIntoAudioGain()
    {
        var video = new MediaAsset { Id = "video", FilePath = "video.mp4" };
        var music = new MediaAsset { Id = "music", FilePath = "music.wav" };
        var timeline = new Timeline();
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips =
            {
                new TimelineClip
                {
                    MediaAssetId = video.Id,
                    SourceTrimInSeconds = 0,
                    SourceTrimOutSeconds = 5,
                    TimelineStartSeconds = 0
                }
            }
        });
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Audio,
            Volume = 0.35,
            Clips =
            {
                new TimelineClip
                {
                    MediaAssetId = music.Id,
                    SourceTrimInSeconds = 0,
                    SourceTrimOutSeconds = 5,
                    TimelineStartSeconds = 0,
                    Volume = 1.0
                }
            }
        });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { video, music }, 640, 360, 30);
        Assert.Contains("volume=0.35", plan.FilterComplexArgument, StringComparison.Ordinal);
    }
}
