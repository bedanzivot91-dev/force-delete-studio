using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using NPVideoStudio.Media;
using Xunit;

namespace NPVideoStudio.UnitTests;

public sealed class KeyframeEngineTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"npvs_keyframes_{Guid.NewGuid():N}");

    public KeyframeEngineTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void VolumeEnvelope_IsClampedEvaluatedAndRendered()
    {
        var clip = new TimelineClip { Volume = 1, SourceTrimOutSeconds = 4 };
        clip.Keyframes.Add(new ClipKeyframe { Property = ClipKeyframeProperty.Volume, TimeSeconds = 0, Value = 0 });
        clip.Keyframes.Add(new ClipKeyframe { Property = ClipKeyframeProperty.Volume, TimeSeconds = 2, Value = 3, Easing = ClipKeyframeEasing.Linear });

        Assert.Equal(1, ClipKeyframeEvaluator.Evaluate(clip, ClipKeyframeProperty.Volume, 1), 6);
        Assert.Equal(2, ClipKeyframeEvaluator.Evaluate(clip, ClipKeyframeProperty.Volume, 2), 6);

        var method = typeof(FfmpegFilterGraphBuilder).GetMethod(
            "BuildAnimatedVolumeFilter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var filter = (string)method.Invoke(null, new object[] { clip, 1d })!;
        Assert.Contains("eval=frame", filter);
        Assert.Contains("volume='", filter);
    }

    [Fact]
    public void Evaluator_LinearEaseAndHold_AreDeterministic()
    {
        var keys = new[]
        {
            new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 0, Value = 0 },
            new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 2, Value = 100, Easing = ClipKeyframeEasing.Linear }
        };
        Assert.Equal(50, ClipKeyframeEvaluator.Evaluate(keys, ClipKeyframeProperty.PositionX, 1, 50), 6);

        keys[1].Easing = ClipKeyframeEasing.EaseIn;
        Assert.Equal(25, ClipKeyframeEvaluator.Evaluate(keys, ClipKeyframeProperty.PositionX, 1, 50), 6);

        keys[1].Easing = ClipKeyframeEasing.Hold;
        Assert.Equal(0, ClipKeyframeEvaluator.Evaluate(keys, ClipKeyframeProperty.PositionX, 1.9, 50), 6);
        Assert.Equal(100, ClipKeyframeEvaluator.Evaluate(keys, ClipKeyframeProperty.PositionX, 2, 50), 6);
    }

    [Fact]
    public void Session_KeyframesAreUndoableAndDeepCloned()
    {
        var clip = new TimelineClip
        {
            Id = "clip",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 4,
            TimelineStartSeconds = 0
        };
        var session = new TimelineEditSession(new[]
        {
            new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { clip } }
        });

        session.UpsertKeyframe("clip", ClipKeyframeProperty.Scale, 1, 80, ClipKeyframeEasing.EaseInOut);
        Assert.Single(session.Tracks[0].Clips[0].Keyframes);
        session.UpsertKeyframe("clip", ClipKeyframeProperty.Scale, 3, 140, ClipKeyframeEasing.EaseOut);
        Assert.Equal(2, session.Tracks[0].Clips[0].Keyframes.Count);

        session.Undo();
        Assert.Single(session.Tracks[0].Clips[0].Keyframes);
        Assert.Equal(80, session.Tracks[0].Clips[0].Keyframes[0].Value);

        session.Redo();
        Assert.Equal(2, session.Tracks[0].Clips[0].Keyframes.Count);
    }

    [Fact]
    public void Split_PreservesAnimatedValueAtBothNewEdges()
    {
        var clip = new TimelineClip
        {
            Id = "clip",
            SourceTrimInSeconds = 0,
            SourceTrimOutSeconds = 4,
            TimelineStartSeconds = 0,
            Keyframes =
            {
                new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 0, Value = 10 },
                new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 4, Value = 90, Easing = ClipKeyframeEasing.Linear }
            }
        };
        var session = new TimelineEditSession(new[]
        {
            new TimelineTrack { Kind = TimelineTrackKind.Video, Clips = { clip } }
        });

        session.SplitClip("clip", 2);
        var split = session.Tracks[0].Clips.OrderBy(c => c.TimelineStartSeconds).ToArray();
        Assert.Equal(2, split.Length);
        Assert.Equal(50, ClipKeyframeEvaluator.Evaluate(split[0], ClipKeyframeProperty.PositionX, split[0].TimelineDurationSeconds), 6);
        Assert.Equal(50, ClipKeyframeEvaluator.Evaluate(split[1], ClipKeyframeProperty.PositionX, 0), 6);
    }

    [Fact]
    public void RangePreview_ShiftsKeyframesAndPreservesBoundaryValues()
    {
        var timeline = new Timeline();
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips =
            {
                new TimelineClip
                {
                    Id = "v", MediaAssetId = "asset", SourceTrimInSeconds = 0, SourceTrimOutSeconds = 10, TimelineStartSeconds = 0,
                    Keyframes =
                    {
                        new ClipKeyframe { Property = ClipKeyframeProperty.Opacity, TimeSeconds = 0, Value = 0 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Opacity, TimeSeconds = 10, Value = 1, Easing = ClipKeyframeEasing.Linear }
                    }
                }
            }
        });

        var range = FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, 3, 7);
        var sliced = Assert.Single(Assert.Single(range.Tracks).Clips);
        Assert.Equal(0.3, ClipKeyframeEvaluator.Evaluate(sliced, ClipKeyframeProperty.Opacity, 0), 6);
        Assert.Equal(0.7, ClipKeyframeEvaluator.Evaluate(sliced, ClipKeyframeProperty.Opacity, 4), 6);
    }

    [Fact]
    public void FilterGraph_UsesRealDynamicExpressionsForBaseOverlayAndText()
    {
        var baseAsset = new MediaAsset { Id = "base", FilePath = "base.mp4", Width = 320, Height = 240, Duration = TimeSpan.FromSeconds(2) };
        var overlayAsset = new MediaAsset { Id = "ovl", FilePath = "ovl.mp4", Width = 320, Height = 240, Duration = TimeSpan.FromSeconds(2) };
        var timeline = BuildAnimatedTimeline(baseAsset, overlayAsset, 2);

        var plan = FfmpegFilterGraphBuilder.Build(timeline, new[] { baseAsset, overlayAsset }, 640, 360, 10);

        Assert.Contains("eval=frame", plan.FilterComplexArgument);
        Assert.Contains("rotate=angle=", plan.FilterComplexArgument);
        Assert.Contains("geq=r='r(X,Y)'", plan.FilterComplexArgument);
        Assert.Contains("fontsize='", plan.FilterComplexArgument);
        Assert.Contains("if(lt(", plan.FilterComplexArgument);
        Assert.Contains("overlay=x='", plan.FilterComplexArgument);
    }

    [Fact]
    public async Task RenderAsync_AnimatedBaseOverlayAndText_CompletesWithRealFfmpeg()
    {
        var basePath = await CreateSolidColorClipAsync("base.mp4", "#CC2233", 1.2, 440);
        var overlayPath = await CreateSolidColorClipAsync("overlay.mp4", "#2266CC", 1.2, 660);
        var baseAsset = new MediaAsset { Id = "base", FilePath = basePath, Width = 320, Height = 240, Duration = TimeSpan.FromSeconds(1.2) };
        var overlayAsset = new MediaAsset { Id = "ovl", FilePath = overlayPath, Width = 320, Height = 240, Duration = TimeSpan.FromSeconds(1.2) };

        var project = new Project { Name = "Animated render", MediaLibrary = { baseAsset, overlayAsset } };
        project.Format.Width = 640;
        project.Format.Height = 360;
        project.Format.Fps = 10;
        project.Timeline = BuildAnimatedTimeline(baseAsset, overlayAsset, 1.2);

        var output = Path.Combine(_tempDir, "animated.mp4");
        var job = new RenderJob
        {
            ProjectName = project.Name,
            Settings = new RenderSettings { OutputFilePath = output, OverwriteConfirmed = true, Preset = "ultrafast", Crf = 30 }
        };

        var service = new RenderService();
        var rendered = await service.RenderAsync(project, job);
        Assert.Equal(RenderJobStatus.Completed, job.Status);
        Assert.True(File.Exists(rendered));
        Assert.True(new FileInfo(rendered).Length > 1_000);
        Assert.Contains("if(lt(", job.FfmpegCommandLogged);
    }

    private static Timeline BuildAnimatedTimeline(MediaAsset baseAsset, MediaAsset overlayAsset, double duration)
    {
        var timeline = new Timeline();
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips =
            {
                new TimelineClip
                {
                    MediaAssetId = baseAsset.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = duration, TimelineStartSeconds = 0,
                    Keyframes =
                    {
                        new ClipKeyframe { Property = ClipKeyframeProperty.Scale, TimeSeconds = 0, Value = 42 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Scale, TimeSeconds = duration, Value = 55, Easing = ClipKeyframeEasing.EaseInOut },
                        new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 0, Value = 25 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = duration, Value = 75, Easing = ClipKeyframeEasing.EaseInOut },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Rotation, TimeSeconds = 0, Value = -8 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Rotation, TimeSeconds = duration, Value = 8, Easing = ClipKeyframeEasing.EaseInOut },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Opacity, TimeSeconds = 0, Value = 0.7 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Opacity, TimeSeconds = duration, Value = 1, Easing = ClipKeyframeEasing.EaseOut }
                    }
                }
            }
        });
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Video,
            Clips =
            {
                new TimelineClip
                {
                    MediaAssetId = overlayAsset.Id, SourceTrimInSeconds = 0, SourceTrimOutSeconds = duration, TimelineStartSeconds = 0,
                    ScalePercent = 25,
                    Keyframes =
                    {
                        new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 0, Value = 20 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = duration, Value = 80, Easing = ClipKeyframeEasing.EaseInOut },
                        new ClipKeyframe { Property = ClipKeyframeProperty.PositionY, TimeSeconds = 0, Value = 20 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.PositionY, TimeSeconds = duration, Value = 80, Easing = ClipKeyframeEasing.EaseInOut },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Scale, TimeSeconds = 0, Value = 20 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Scale, TimeSeconds = duration, Value = 35, Easing = ClipKeyframeEasing.EaseOut },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Rotation, TimeSeconds = 0, Value = 0 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Rotation, TimeSeconds = duration, Value = 120, Easing = ClipKeyframeEasing.Linear },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Opacity, TimeSeconds = 0, Value = 0.2 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Opacity, TimeSeconds = duration, Value = 1, Easing = ClipKeyframeEasing.EaseIn }
                    }
                }
            }
        });
        timeline.Tracks.Add(new TimelineTrack
        {
            Kind = TimelineTrackKind.Text,
            Clips =
            {
                new TimelineClip
                {
                    TextContent = "KEYFRAME", SourceTrimInSeconds = 0, SourceTrimOutSeconds = duration, TimelineStartSeconds = 0,
                    FontSizePx = 28, HasTextBackground = false,
                    Keyframes =
                    {
                        new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = 0, Value = 20 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.PositionX, TimeSeconds = duration, Value = 80, Easing = ClipKeyframeEasing.EaseInOut },
                        new ClipKeyframe { Property = ClipKeyframeProperty.PositionY, TimeSeconds = 0, Value = 20 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.PositionY, TimeSeconds = duration, Value = 75, Easing = ClipKeyframeEasing.EaseInOut },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Scale, TimeSeconds = 0, Value = 80 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Scale, TimeSeconds = duration, Value = 130, Easing = ClipKeyframeEasing.EaseOut },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Opacity, TimeSeconds = 0, Value = 0 },
                        new ClipKeyframe { Property = ClipKeyframeProperty.Opacity, TimeSeconds = duration, Value = 1, Easing = ClipKeyframeEasing.EaseIn }
                    }
                }
            }
        });
        return timeline;
    }

    private async Task<string> CreateSolidColorClipAsync(string name, string color, double durationSeconds, int frequency)
    {
        var path = Path.Combine(_tempDir, name);
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var arg in new[]
        {
            "-y", "-f", "lavfi", "-i", $"color=c={color}:s=320x240:d={durationSeconds}:r=10",
            "-f", "lavfi", "-i", $"sine=frequency={frequency}:duration={durationSeconds}",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", path
        }) process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0 && File.Exists(path), error);
        return path;
    }
}
