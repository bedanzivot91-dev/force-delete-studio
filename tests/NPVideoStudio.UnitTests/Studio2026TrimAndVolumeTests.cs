using System.Collections.ObjectModel;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class Studio2026TrimAndVolumeTests
{
    [Fact]
    public void ModernInspectorTrim_UsesSessionUndo_AndRealSourceDuration()
    {
        var asset = new MediaAsset
        {
            Id = "video-source",
            FilePath = "source.mp4",
            Duration = TimeSpan.FromSeconds(10),
            HasVideoStream = true,
            Kind = MediaKind.Video
        };
        var clip = new TimelineClip
        {
            MediaAssetId = asset.Id,
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 10,
            TimelineStartSeconds = 0
        };
        var project = new Project { Name = "Trim UI" };
        project.Timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { clip } });
        var media = new ObservableCollection<MediaAssetViewModel> { new(asset) };
        var vm = new TimelineViewModel(project, media, () => 4);

        var item = Assert.Single(Assert.Single(vm.Tracks).Clips);
        Assert.True(item.HasSourceMedia);
        Assert.Equal(10, item.SourceDurationSeconds, 6);

        item.TrimInSeconds = 2;
        Assert.Equal(2, Assert.Single(Assert.Single(vm.CurrentTracks).Clips).SourceTrimInSeconds, 6);
        vm.UndoCommand.Execute(null);
        Assert.Equal(0, Assert.Single(Assert.Single(vm.CurrentTracks).Clips).SourceTrimInSeconds, 6);

        item = Assert.Single(Assert.Single(vm.Tracks).Clips);
        item.TrimOutSeconds = 7.5;
        Assert.Equal(7.5, Assert.Single(Assert.Single(vm.CurrentTracks).Clips).SourceTrimOutSeconds, 6);
        vm.UndoCommand.Execute(null);
        Assert.Equal(10, Assert.Single(Assert.Single(vm.CurrentTracks).Clips).SourceTrimOutSeconds, 6);

        item = Assert.Single(Assert.Single(vm.Tracks).Clips);
        item.TrimOutSeconds = 99;
        Assert.Equal(10, Assert.Single(Assert.Single(vm.CurrentTracks).Clips).SourceTrimOutSeconds, 6);
    }

    [Fact]
    public void ModernTimelineAudioVolume_UsesSessionAndUndo()
    {
        var project = new Project { Name = "Audio volume UI" };
        project.Timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Audio, Volume = 1.0 });
        var vm = new TimelineViewModel(project, new ObservableCollection<MediaAssetViewModel>(), () => 0);

        var track = Assert.Single(vm.Tracks);
        Assert.True(track.HasVolumeControl);
        track.VolumePercent = 135;
        Assert.Equal(1.35, Assert.Single(vm.CurrentTracks).Volume, 6);

        vm.UndoCommand.Execute(null);
        Assert.Equal(1.0, Assert.Single(vm.CurrentTracks).Volume, 6);
    }

    [Fact]
    public void ModernChrome_ExposesRealTrimAndTrackVolumeBindings()
    {
        var root = FindRepositoryRoot();
        var inspector = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernInspectorView.axaml"));
        var timeline = File.ReadAllText(Path.Combine(root, "src", "NPVideoStudio.App", "Views", "ModernTimelineView.axaml"));

        Assert.Contains("IsVisible=\"{Binding HasSourceMedia}\"", inspector);
        Assert.Contains("Value=\"{Binding TrimInSeconds}\"", inspector);
        Assert.Contains("Value=\"{Binding TrimOutSeconds}\"", inspector);
        Assert.Contains("Value=\"{Binding VolumePercent, Mode=TwoWay}\"", timeline);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NPVideoStudio.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
