using System.Collections.ObjectModel;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Exercises the real UI-facing chain (TimelineViewModel -> TimelineTrackItemViewModel ->
/// TimelineClipItemViewModel), not just the underlying TimelineEditSession - this is what actually
/// backs the "Tekst traka" button and the per-clip "Tekst:" edit box in WorkspaceView.axaml.
/// </summary>
public class TimelineViewModelTests
{
    private static TimelineViewModel CreateTimeline(Project project) =>
        new(project, new ObservableCollection<MediaAssetViewModel>(), getPlayhead: () => 0);

    [Fact]
    public void AddTextTrackCommand_ThenAddClip_ProducesATextClipItemWithEditableTextContent()
    {
        var project = new Project { Name = "Test" };
        var timeline = CreateTimeline(project);

        timeline.AddTextTrackCommand.Execute(null);

        Assert.Single(timeline.Tracks);
        Assert.Equal(TimelineTrackKind.Text, timeline.Tracks[0].Track.Kind);

        // AddClipAtPlayheadCommand triggers RefreshFromSession, which rebuilds every track/clip item
        // (not just the edited one) - so the pre-call trackItem reference is stale immediately after
        // Execute() returns. Real callers (WorkspaceView's bound ObservableCollection) always observe
        // the rebuilt collection, never the stale instance, so the test does the same.
        timeline.Tracks[0].AddClipAtPlayheadCommand.Execute(null);

        Assert.Single(timeline.Tracks[0].Clips);
        var clipItem = timeline.Tracks[0].Clips[0];
        Assert.True(clipItem.IsTextClip);
        Assert.Equal("Novi tekst", clipItem.TextContent);

        clipItem.TextContent = "Ispravljen tekst";

        // The clip row is rebuilt after every edit (RefreshFromSession), so re-read it via Tracks/Clips
        // rather than trusting the stale clipItem instance - exactly what the real WorkspaceView does.
        var refreshedClipItem = timeline.Tracks[0].Clips[0];
        Assert.Equal("Ispravljen tekst", refreshedClipItem.TextContent);

        timeline.SaveToProject();
        Assert.Equal("Ispravljen tekst", project.Timeline.Tracks[0].Clips[0].TextContent);
    }

    [Fact]
    public void AddClipToVideoTrack_TextContentIsNotATextClip()
    {
        var project = new Project { Name = "Test" };
        var timeline = CreateTimeline(project);

        timeline.AddVideoTrackCommand.Execute(null);
        var trackItem = timeline.Tracks[0];

        // No media selected -> AddClipAtPlayhead is a no-op for a video track, confirming the command
        // doesn't blow up and doesn't fabricate a clip out of nothing.
        trackItem.AddClipAtPlayheadCommand.Execute(null);

        Assert.Empty(trackItem.Clips);
    }

    [Fact]
    public void AddTextAtPlayhead_IsOneClickAndSelectsTheNewEditableClip()
    {
        var project = new Project { Name = "Test" };
        var timeline = new TimelineViewModel(project, new ObservableCollection<MediaAssetViewModel>(), () => 7.5);

        timeline.AddTextAtPlayheadCommand.Execute(null);

        var track = Assert.Single(timeline.Tracks);
        Assert.Equal(TimelineTrackKind.Text, track.Track.Kind);
        var clip = Assert.Single(track.Clips);
        Assert.Equal(7.5, clip.StartSeconds);
        Assert.True(clip.IsSelected);
        Assert.Equal("Novi tekst", clip.TextContent);
    }

    [Fact]
    public void TimelineZoom_ChangesClipGeometryAndCreatesScrollableLaneWidth()
    {
        var asset = new MediaAsset { FilePath = "/tmp/video.mp4", Duration = TimeSpan.FromSeconds(30), HasVideoStream = true };
        var project = new Project { Name = "Test", MediaLibrary = { asset } };
        var media = new ObservableCollection<MediaAssetViewModel> { new(asset) };
        var timeline = new TimelineViewModel(project, media, () => 0) { SelectedMediaAsset = media[0] };
        timeline.AddVideoTrackCommand.Execute(null);
        timeline.Tracks[0].AddClipAtPlayheadCommand.Execute(null);
        var oldWidth = timeline.Tracks[0].Clips[0].PixelWidth;

        timeline.ZoomInCommand.Execute(null);

        Assert.True(timeline.Tracks[0].Clips[0].PixelWidth > oldWidth);
        Assert.True(timeline.Tracks[0].LaneWidth > 1200);
        Assert.Equal(timeline.ZoomPixelsPerSecond, project.Timeline.ZoomPixelsPerSecond);
    }
}
