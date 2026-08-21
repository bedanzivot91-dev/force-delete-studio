using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.AI;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class KeyframeUiPathTests
{
    [Fact]
    public void InspectorAddAtPlayhead_PersistsThroughSession_AndUndoRemovesIt()
    {
        var clip = new TimelineClip
        {
            Id = "ui-keyframe",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 8,
            TimelineStartSeconds = 5,
            ScalePercent = 100
        };
        var session = new TimelineEditSession(new[]
        {
            new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { clip } }
        });
        var live = session.Tracks[0].Clips[0];
        var noop = new RelayCommand(() => { });
        var vm = new TimelineClipItemViewModel(
            live,
            session.Tracks[0].Id,
            "video.mp4",
            true,
            noop, noop, noop, noop, noop, noop, noop, noop, noop,
            getPlayheadSeconds: () => 6,
            onKeyframeUpsert: (clipId, property, localTime, value, easing) =>
                session.UpsertKeyframe(clipId, property, localTime, value, easing),
            onKeyframeRemove: (clipId, property, localTime) =>
                session.RemoveKeyframeNear(clipId, property, localTime));

        vm.SelectedKeyframeProperty = ClipKeyframeProperty.Scale;
        vm.KeyframeValue = 135;
        vm.SelectedKeyframeEasing = ClipKeyframeEasing.EaseOut;
        vm.AddKeyframeAtPlayheadCommand.Execute(null);

        var persisted = Assert.Single(session.Tracks[0].Clips[0].Keyframes);
        Assert.Equal(ClipKeyframeProperty.Scale, persisted.Property);
        Assert.Equal(1, persisted.TimeSeconds, 6);
        Assert.Equal(135, persisted.Value, 6);
        Assert.Equal(ClipKeyframeEasing.EaseOut, persisted.Easing);

        session.Undo();
        Assert.Empty(session.Tracks[0].Clips[0].Keyframes);
    }

    [Fact]
    public void SpeedChange_RescalesKeyframeTimesProportionally_AndUndoRestoresThem()
    {
        var clip = new TimelineClip
        {
            Id = "speed-keyframe",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 8,
            TimelineStartSeconds = 0,
            SpeedMultiplier = 1,
            Keyframes =
            {
                new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 2, Value = 20 },
                new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 6, Value = 80 }
            }
        };
        var session = new TimelineEditSession(new[]
        {
            new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { clip } }
        });

        session.SetClipEffects("speed-keyframe", ClipVideoEffect.None, 0, 1, 1, 2);

        var sped = session.Tracks[0].Clips[0];
        Assert.Equal(4, sped.TimelineDurationSeconds, 6);
        Assert.Equal(new[] { 1d, 3d }, sped.Keyframes.Select(k => k.TimeSeconds).OrderBy(x => x).ToArray());

        session.Undo();
        var restored = session.Tracks[0].Clips[0];
        Assert.Equal(1, restored.SpeedMultiplier, 6);
        Assert.Equal(8, restored.TimelineDurationSeconds, 6);
        Assert.Equal(new[] { 2d, 6d }, restored.Keyframes.Select(k => k.TimeSeconds).OrderBy(x => x).ToArray());
    }
}
