using CommunityToolkit.Mvvm.Input;
using NPVideoStudio.AI;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Domain;
using NPVideoStudio.Infrastructure.Persistence;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class KeyframeUiPathTests
{
    [Fact]
    public void InspectorAddAndRemoveAtPlayhead_AreRealUndoableSessionEdits()
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
                session.RemoveKeyframe(clipId, property, localTime));

        vm.SelectedKeyframeProperty = ClipKeyframeProperty.Scale;
        vm.KeyframeValue = 135;
        vm.SelectedKeyframeEasing = ClipKeyframeEasing.EaseOut;
        vm.AddKeyframeAtPlayheadCommand.Execute(null);

        var persisted = Assert.Single(session.Tracks[0].Clips[0].Keyframes);
        Assert.Equal(ClipKeyframeProperty.Scale, persisted.Property);
        Assert.Equal(1, persisted.TimeSeconds, 6);
        Assert.Equal(135, persisted.Value, 6);
        Assert.Equal(ClipKeyframeEasing.EaseOut, persisted.Easing);

        vm.RemoveKeyframeAtPlayheadCommand.Execute(null);
        Assert.Empty(session.Tracks[0].Clips[0].Keyframes);

        session.Undo();
        persisted = Assert.Single(session.Tracks[0].Clips[0].Keyframes);
        Assert.Equal(135, persisted.Value, 6);

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

    [Fact]
    public void TrimInAndTrimOut_RebaseAndDiscardKeyframes_AndUndoRestoresThem()
    {
        var clip = new TimelineClip
        {
            Id = "trim-keyframe",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 10,
            TimelineStartSeconds = 0,
            Keyframes =
            {
                new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 1, Value = 10 },
                new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 4, Value = 40 },
                new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 8, Value = 80 }
            }
        };
        var session = new TimelineEditSession(new[]
        {
            new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { clip } }
        });

        session.TrimIn("trim-keyframe", 3);
        var trimmedIn = session.Tracks[0].Clips[0];
        Assert.Equal(3, trimmedIn.TimelineStartSeconds, 6);
        Assert.Equal(new[] { 0d, 1d, 5d }, trimmedIn.Keyframes.Select(k => k.TimeSeconds).OrderBy(x => x).ToArray());
        Assert.Equal(30, trimmedIn.Keyframes.Single(k => Math.Abs(k.TimeSeconds) < 0.001).Value, 6);

        session.TrimOut("trim-keyframe", 7);
        var trimmedOut = session.Tracks[0].Clips[0];
        Assert.Equal(4, trimmedOut.TimelineDurationSeconds, 6);
        Assert.Equal(new[] { 0d, 1d, 4d }, trimmedOut.Keyframes.Select(k => k.TimeSeconds).OrderBy(x => x).ToArray());
        Assert.Equal(70, trimmedOut.Keyframes.Single(k => Math.Abs(k.TimeSeconds - 4) < 0.001).Value, 6);
        Assert.DoesNotContain(trimmedOut.Keyframes, k => k.TimeSeconds > 4.0001);

        session.Undo();
        Assert.Equal(new[] { 0d, 1d, 5d }, session.Tracks[0].Clips[0].Keyframes.Select(k => k.TimeSeconds).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task ProjectRepository_RoundTripsKeyframesLosslessly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "npvs-keyframe-persistence-" + Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(tempDir, "keyframes.npvsproject");
        Directory.CreateDirectory(tempDir);

        try
        {
            var project = new Project
            {
                Name = "Keyframe persistence",
                Timeline = new Timeline
                {
                    Tracks =
                    {
                        new TimelineTrack
                        {
                            Kind = TimelineTrackKind.Video,
                            Clips =
                            {
                                new TimelineClip
                                {
                                    Id = "persisted-clip",
                                    SourceTrimOutSeconds = 5,
                                    Keyframes =
                                    {
                                        new ClipKeyframe
                                        {
                                            Id = "persisted-keyframe",
                                            Property = ClipKeyframeProperty.Opacity,
                                            TimeSeconds = 2.25,
                                            Value = 0.42,
                                            Easing = ClipKeyframeEasing.Hold
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var repository = new ProjectRepository();
            await repository.SaveAsync(project, projectPath);
            var loaded = await repository.LoadAsync(projectPath);

            var keyframe = Assert.Single(Assert.Single(Assert.Single(loaded.Timeline.Tracks).Clips).Keyframes);
            Assert.Equal("persisted-keyframe", keyframe.Id);
            Assert.Equal(ClipKeyframeProperty.Opacity, keyframe.Property);
            Assert.Equal(2.25, keyframe.TimeSeconds, 6);
            Assert.Equal(0.42, keyframe.Value, 6);
            Assert.Equal(ClipKeyframeEasing.Hold, keyframe.Easing);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
