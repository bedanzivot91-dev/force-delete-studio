using System.Collections.ObjectModel;
using NPVideoStudio.AI;
using NPVideoStudio.App.Services;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class CaptionStyleGalleryIntegrationTests
{
    [Fact]
    public void TimelineSession_AppliesPresetAsOneUndoableRealEdit()
    {
        var clip = new TimelineClip
        {
            TextContent = "Nedostaješ mi",
            SourceTrimOutSeconds = 3,
            TextColor = "#FFFFFF",
            TextOutlineColor = null,
            HasTextBackground = false
        };
        var track = new TimelineTrack { Kind = TimelineTrackKind.Caption, Clips = { clip } };
        var session = new TimelineEditSession(new[] { track });
        var preset = CaptionStylePresetCatalog.All.First(p => p.PanelColorHex is not null);

        Assert.True(session.ApplyCaptionStylePreset(clip.Id, preset));
        var edited = Assert.Single(Assert.Single(session.Tracks).Clips);
        Assert.Equal(preset.TextColorHex, edited.TextColor);
        Assert.True(edited.HasTextBackground);
        Assert.StartsWith("#", edited.TextBackgroundColor);
        Assert.Equal(7, edited.TextBackgroundColor.Length);
        Assert.InRange(edited.TextBackgroundOpacity, 0.01, 1.0);

        session.Undo();
        var undone = Assert.Single(Assert.Single(session.Tracks).Clips);
        Assert.Equal("#FFFFFF", undone.TextColor);
        Assert.Null(undone.TextOutlineColor);
        Assert.False(undone.HasTextBackground);
    }

    [Fact]
    public void NormalTimelineSelection_AppliesPresetToSelectedCaption_AndUndoRestoresIt()
    {
        var clip = new TimelineClip
        {
            TextContent = "Još te čekam",
            SourceTrimOutSeconds = 2,
            TextColor = "#ABCDEF"
        };
        var project = new Project { Name = "Caption preset UI path" };
        project.Timeline.Tracks.Add(new TimelineTrack { Kind = TimelineTrackKind.Caption, Clips = { clip } });
        var timeline = new TimelineViewModel(project, new ObservableCollection<MediaAssetViewModel>(), () => 0);
        timeline.SelectedClipId = clip.Id;
        var preset = CaptionStylePresetCatalog.All.First(p => p.Animation == CaptionAnimationKind.Shadow);

        Assert.True(timeline.ApplyCaptionStylePresetToSelected(preset));
        var edited = Assert.Single(Assert.Single(timeline.CurrentTracks).Clips);
        Assert.Equal(preset.TextColorHex, edited.TextColor);
        Assert.Equal(preset.OutlineOrShadowColorHex, edited.TextShadowColor);
        Assert.Null(edited.TextOutlineColor);

        timeline.SaveToProject();
        Assert.Equal(preset.TextColorHex, Assert.Single(Assert.Single(project.Timeline.Tracks).Clips).TextColor);

        timeline.UndoCommand.Execute(null);
        var undone = Assert.Single(Assert.Single(timeline.CurrentTracks).Clips);
        Assert.Equal("#ABCDEF", undone.TextColor);
    }

    [Fact]
    public async Task GalleryProjectMode_CardApplyCommand_InvokesRealProjectCallback()
    {
        CaptionStylePreset? received = null;
        var gallery = new CaptionStyleGalleryViewModel(preset =>
        {
            received = preset;
            return Task.FromResult($"primenjen:{preset.Name}");
        });

        Assert.True(gallery.CanApplyToProject);
        var card = Assert.IsType<CaptionStylePresetItemViewModel>(gallery.Presets.First());
        Assert.True(card.CanApply);
        Assert.NotNull(card.ApplyCommand);

        card.ApplyCommand!.Execute(null);
        for (var i = 0; i < 50 && received is null; i++)
        {
            await Task.Delay(10);
        }

        Assert.Same(card.Preset, received);
        Assert.Equal($"primenjen:{card.Preset.Name}", gallery.StatusMessage);
    }

    [Fact]
    public void FfmpegFinalGraph_ContainsPresetRenderableStyle()
    {
        var preset = CaptionStylePresetCatalog.All.First(p => p.Animation == CaptionAnimationKind.Outline && p.PanelColorHex is null);
        var caption = new TimelineClip
        {
            TextContent = "ČĆŽŠĐ",
            SourceTrimOutSeconds = 2
        };
        var session = new TimelineEditSession(new[]
        {
            new TimelineTrack { Kind = TimelineTrackKind.Caption, Clips = { caption } }
        });
        Assert.True(session.ApplyCaptionStylePreset(caption.Id, preset));
        var styledCaption = Assert.Single(Assert.Single(session.Tracks).Clips);

        var asset = new MediaAsset
        {
            Id = "video",
            FilePath = "input-with-audio.mp4",
            Duration = TimeSpan.FromSeconds(2),
            HasVideoStream = true,
            HasAudioStream = true
        };
        var timeline = new Timeline();
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips = { new TimelineClip { MediaAssetId = asset.Id, SourceTrimOutSeconds = 2 } }
        });
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Caption,
            Clips = { styledCaption }
        });

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { asset }, 640, 360, 30);

        Assert.Contains("drawtext=", plan.FilterComplexArgument);
        Assert.Contains($"fontcolor={preset.TextColorHex}", plan.FilterComplexArgument);
        Assert.Contains($"bordercolor={preset.OutlineOrShadowColorHex}", plan.FilterComplexArgument);
    }
}
