using System.Collections.ObjectModel;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class CaptionPresetTimelineIntegrationTests
{
    [Fact]
    public void ApplySelectedCaptionPreset_CommandPersistsVisualStyleIntoProjectTimeline()
    {
        var caption = new TimelineClip
        {
            TextContent = "Nedostaješ mi",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 3,
            TimelineStartSeconds = 0
        };
        var project = new Project
        {
            Name = "Preset test",
            Timeline = new Timeline
            {
                Tracks = new List<TimelineTrack>
                {
                    new()
                    {
                        Kind = TimelineTrackKind.Caption,
                        Name = "Titlovi",
                        Clips = new List<TimelineClip> { caption }
                    }
                }
            }
        };

        var vm = new TimelineViewModel(project, new ObservableCollection<MediaAssetViewModel>(), () => 0);
        vm.SelectedClipId = caption.Id;
        vm.SelectedCaptionPresetChoice = vm.CaptionPresetChoices.Single(x => x.Preset.Name == "Stakleni panel");

        Assert.NotNull(vm.ApplySelectedCaptionPresetCommand);
        vm.ApplySelectedCaptionPresetCommand.Execute(null);
        vm.SaveToProject();

        var saved = project.Timeline.Tracks.Single().Clips.Single();
        Assert.Equal("#10233D", saved.TextColor);
        Assert.Equal("#FFFFFF", saved.TextOutlineColor);
        Assert.True(saved.HasTextBackground);
        Assert.Equal("#1677E8", saved.TextBackgroundColor);
        Assert.InRange(saved.TextBackgroundOpacity, 0.19, 0.21);
        Assert.Contains("ide u export", vm.CaptionPresetStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplySelectedCaptionPreset_CommandRefusesNonTextSelection()
    {
        var video = new TimelineClip
        {
            MediaAssetId = "video-1",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 3,
            TimelineStartSeconds = 0
        };
        var project = new Project
        {
            Name = "Video preset guard",
            Timeline = new Timeline
            {
                Tracks = new List<TimelineTrack>
                {
                    new()
                    {
                        Kind = TimelineTrackKind.Video,
                        Name = "Video",
                        Clips = new List<TimelineClip> { video }
                    }
                }
            }
        };

        var vm = new TimelineViewModel(project, new ObservableCollection<MediaAssetViewModel>(), () => 0);
        vm.SelectedClipId = video.Id;

        vm.ApplySelectedCaptionPresetCommand.Execute(null);

        Assert.Contains("tekst/titl", vm.CaptionPresetStatusMessage, StringComparison.OrdinalIgnoreCase);
    }
}
