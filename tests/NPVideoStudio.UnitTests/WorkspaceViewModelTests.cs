using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NPVideoStudio.App.Services;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using Serilog;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Fakes so workspace/timeline tests don't need real disk I/O or ffprobe.</summary>
public sealed class FakeProjectRepository : IProjectRepository
{
    public Task<Project> LoadAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public int SaveCallCount { get; private set; }

    public Task SaveAsync(Project project, string projectFilePath, CancellationToken cancellationToken = default)
    {
        SaveCallCount++;
        return Task.CompletedTask;
    }

    public Task BackupAsync(string projectFilePath, int maxBackups = 10, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class FakeMediaProbeService : IMediaProbeService
{
    public Func<string, MediaAsset>? Handler { get; set; }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<MediaAsset> ProbeAsync(string filePath, CancellationToken cancellationToken = default) =>
        Handler is null ? throw new NotSupportedException() : Task.FromResult(Handler(filePath));
}

/// <summary>Fake so workspace/timeline tests don't need real ffmpeg for preview-frame extraction -
/// FramePreviewService's own real-process behavior is covered by FramePreviewServiceTests.cs.</summary>
public sealed class FakeFramePreviewService : IFramePreviewService
{
    public Func<string, double, byte[]?>? Handler { get; set; }

    public Task<byte[]?> ExtractFrameAsync(string sourceFilePath, double timestampSeconds, CancellationToken cancellationToken = default) =>
        Task.FromResult(Handler?.Invoke(sourceFilePath, timestampSeconds));
}

/// <summary>
/// Uses [AvaloniaFact] (not a plain [Fact]) because PlayerViewModel constructs a real Avalonia
/// DispatcherTimer, which needs a running Dispatcher - same reason AppSmokeTests.cs uses it.
/// </summary>
public class WorkspaceViewModelTests
{
    private static WorkspaceViewModel CreateWorkspace(Project? project = null, ISubtitleGeneratorService? subtitleGeneratorService = null,
        IMediaProbeService? mediaProbeService = null, IStorageService? storageService = null, IRenderService? renderService = null)
    {
        project ??= new Project { Name = "Test projekat" };
        return new WorkspaceViewModel(
            project,
            new FakeProjectRepository(),
            mediaProbeService ?? new FakeMediaProbeService(),
            storageService ?? new FakeStorageService(),
            new FakeFramePreviewService(),
            subtitleGeneratorService ?? new FakeSubtitleGeneratorService(),
            renderService ?? new FakeRenderService(),
            new LoggerConfiguration().CreateLogger());
    }

    [AvaloniaFact]
    public void Construction_WithEmptyTimeline_StartsWithNoTracksAndZeroDuration()
    {
        var workspace = CreateWorkspace();

        Assert.Empty(workspace.Timeline.Tracks);
        Assert.Equal(0, workspace.Player.TotalDurationSeconds);
    }

    [AvaloniaFact]
    public void AddingTrackAndClip_UpdatesPlayerTotalDuration()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(8) };
        var project = new Project { Name = "Test projekat", MediaLibrary = { asset } };
        var workspace = CreateWorkspace(project);
        workspace.MediaLibrary.Add(new MediaAssetViewModel(asset));

        workspace.Timeline.AddVideoTrackCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(workspace.Timeline.Tracks);

        workspace.Timeline.SelectedMediaAsset = workspace.MediaLibrary[0];
        workspace.Timeline.Tracks[0].AddClipAtPlayheadCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(workspace.Timeline.Tracks[0].Clips);
        Assert.Equal(8, workspace.Player.TotalDurationSeconds);
    }

    [AvaloniaFact]
    public void SplitClipAtPlayhead_ProducesTwoClips()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(10) };
        var project = new Project { Name = "Test projekat", MediaLibrary = { asset } };
        var workspace = CreateWorkspace(project);
        workspace.MediaLibrary.Add(new MediaAssetViewModel(asset));
        workspace.Timeline.AddVideoTrackCommand.Execute(null);
        workspace.Timeline.SelectedMediaAsset = workspace.MediaLibrary[0];
        workspace.Timeline.Tracks[0].AddClipAtPlayheadCommand.Execute(null);

        workspace.Player.Seek(4);
        workspace.Timeline.Tracks[0].Clips[0].SplitAtPlayheadCommand.Execute(null);

        Assert.Equal(2, workspace.Timeline.Tracks[0].Clips.Count);
    }

    [AvaloniaFact]
    public void Undo_AfterAddingTrack_RemovesIt()
    {
        var workspace = CreateWorkspace();

        workspace.Timeline.AddAudioTrackCommand.Execute(null);
        Assert.Single(workspace.Timeline.Tracks);

        workspace.Timeline.UndoCommand.Execute(null);

        Assert.Empty(workspace.Timeline.Tracks);
    }

    [AvaloniaFact]
    public void SaveToProject_WritesCurrentTracksBackOntoProjectTimeline()
    {
        var project = new Project { Name = "Test projekat" };
        var workspace = CreateWorkspace(project);

        workspace.Timeline.AddCaptionTrackCommand.Execute(null);
        workspace.Timeline.SaveToProject();

        Assert.Single(project.Timeline.Tracks);
        Assert.Equal(TimelineTrackKind.Caption, project.Timeline.Tracks[0].Kind);
    }

    [AvaloniaFact]
    public void PlayerTransport_PlayPauseStop_ChangesStateCorrectly()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(5) };
        var project = new Project { Name = "Test projekat", MediaLibrary = { asset } };
        var workspace = CreateWorkspace(project);
        workspace.MediaLibrary.Add(new MediaAssetViewModel(asset));
        workspace.Timeline.AddVideoTrackCommand.Execute(null);
        workspace.Timeline.SelectedMediaAsset = workspace.MediaLibrary[0];
        workspace.Timeline.Tracks[0].AddClipAtPlayheadCommand.Execute(null);

        workspace.Player.PlayCommand.Execute(null);
        Assert.True(workspace.Player.IsPlaying);

        workspace.Player.PauseCommand.Execute(null);
        Assert.False(workspace.Player.IsPlaying);

        workspace.Player.StopCommand.Execute(null);
        Assert.Equal(0, workspace.Player.CurrentTimeSeconds);
    }

    /// <summary>Regression test for a real bug found via a user's real-machine screenshot: when a clip
    /// genuinely existed under the playhead but frame extraction still failed (e.g. ffmpeg not found on
    /// their PC), the player showed the exact same "add a clip" message as when there was no clip at all -
    /// completely indistinguishable from the user's side, even though these are very different problems.</summary>
    [AvaloniaFact]
    public void RefreshPreviewFrame_ClipExistsButExtractionFails_ShowsDifferentMessageThanNoClip()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(5) };
        var project = new Project { Name = "Test projekat", MediaLibrary = { asset } };
        var framePreview = new FakeFramePreviewService { Handler = (_, _) => null };
        var workspace = new WorkspaceViewModel(
            project, new FakeProjectRepository(), new FakeMediaProbeService(), new FakeStorageService(),
            framePreview, new FakeSubtitleGeneratorService(), new FakeRenderService(), new LoggerConfiguration().CreateLogger());
        workspace.MediaLibrary.Add(new MediaAssetViewModel(asset));

        var noClipMessage = workspace.Player.PreviewStatusMessage;
        Assert.Null(workspace.Player.CurrentFrameBitmap);

        workspace.Timeline.AddVideoTrackCommand.Execute(null);
        workspace.Timeline.SelectedMediaAsset = workspace.MediaLibrary[0];
        workspace.Timeline.Tracks[0].AddClipAtPlayheadCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(workspace.Player.CurrentFrameBitmap);
        Assert.NotEqual(noClipMessage, workspace.Player.PreviewStatusMessage);
        Assert.Contains("FFmpeg", workspace.Player.PreviewStatusMessage);
    }

    /// <summary>Regression test for a real reported point of confusion: a user imported their video via
    /// "Dodaj medije" and expected the player to show it immediately, not understanding that a fresh
    /// import only lands in the media library - it still needed a manually added video track, a clip
    /// placed on it, and the playhead moved onto that clip before the player would show anything. Now the
    /// very first video import on an empty timeline places itself on a new video track at time 0
    /// automatically, so the player shows a frame right after import with no further clicks.</summary>
    [AvaloniaFact]
    public async Task ImportFilesAsync_FirstVideoOnEmptyTimeline_AutoPlacesClipSoPlayerShowsIt()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(6), HasVideoStream = true };
        var probe = new FakeMediaProbeService { Handler = _ => asset };
        var framePreview = new FakeFramePreviewService { Handler = (_, _) => new byte[] { 1, 2, 3 } };
        var project = new Project { Name = "Test projekat" };
        var workspace = new WorkspaceViewModel(
            project, new FakeProjectRepository(), probe, new FakeStorageService(),
            framePreview, new FakeSubtitleGeneratorService(), new FakeRenderService(), new LoggerConfiguration().CreateLogger());

        await workspace.ImportFilesAsync(new[] { "/tmp/fake.mp4" });
        Dispatcher.UIThread.RunJobs();

        Assert.Single(workspace.Timeline.Tracks);
        Assert.Equal(TimelineTrackKind.Video, workspace.Timeline.Tracks[0].Track.Kind);
        Assert.Single(workspace.Timeline.Tracks[0].Clips);
        Assert.Equal(0, workspace.Timeline.Tracks[0].Clips[0].Clip.TimelineStartSeconds);
        Assert.NotNull(workspace.Player.CurrentFrameBitmap);
    }

    /// <summary>Real user report: a portrait (1080x1920) video imported into a project created with the
    /// default horizontal (1920x1080) canvas showed up tiny with black bars on the sides - correct given
    /// the mismatch, but confusing since nothing told the user the two were even a related choice. The
    /// very first video import on an empty timeline now resizes the project canvas to match it.</summary>
    [AvaloniaFact]
    public async Task ImportFilesAsync_FirstVideoOrientationMismatchesProject_AdjustsProjectFormatToMatch()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(6), HasVideoStream = true, Width = 1080, Height = 1920 };
        var probe = new FakeMediaProbeService { Handler = _ => asset };
        var project = new Project { Name = "Test projekat" }; // defaults to 1920x1080 horizontal
        var workspace = new WorkspaceViewModel(
            project, new FakeProjectRepository(), probe, new FakeStorageService(),
            new FakeFramePreviewService(), new FakeSubtitleGeneratorService(), new FakeRenderService(), new LoggerConfiguration().CreateLogger());

        await workspace.ImportFilesAsync(new[] { "/tmp/fake.mp4" });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1080, project.Format.Width);
        Assert.Equal(1920, project.Format.Height);
        Assert.Contains("prilagođen", workspace.StatusMessage);
        // Project/ProjectFormat are plain non-observable domain classes - the header UI binds to this
        // ViewModel-level label, not straight to Project.Format.Width, specifically so it actually
        // refreshes when the format is adjusted (a real bug caught via a live run: the header kept
        // showing the old 1920x1080 text after this exact adjustment before this label existed).
        Assert.Contains("1080×1920", workspace.FormatSummaryLabel);
    }

    [AvaloniaFact]
    public async Task ImportFilesAsync_FirstVideoOrientationAlreadyMatchesProject_DoesNotTouchFormat()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(6), HasVideoStream = true, Width = 1280, Height = 720 };
        var probe = new FakeMediaProbeService { Handler = _ => asset };
        var project = new Project { Name = "Test projekat" }; // 1920x1080 horizontal - same orientation as a 1280x720 video
        var workspace = new WorkspaceViewModel(
            project, new FakeProjectRepository(), probe, new FakeStorageService(),
            new FakeFramePreviewService(), new FakeSubtitleGeneratorService(), new FakeRenderService(), new LoggerConfiguration().CreateLogger());

        await workspace.ImportFilesAsync(new[] { "/tmp/fake.mp4" });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1920, project.Format.Width);
        Assert.Equal(1080, project.Format.Height);
    }

    /// <summary>Home-screen "Dodaj tekst u video" shortcut - real end-to-end proof that picking a video
    /// through this entry point results in a ready-to-edit workspace: the video on a video track AND a
    /// Text track with a starter clip, not just the video import half of the flow.</summary>
    [AvaloniaFact]
    public async Task StartAddTextToVideoFlowAsync_VideoPicked_ImportsItAndAddsTextTrackWithStarterClip()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(6), HasVideoStream = true };
        var probe = new FakeMediaProbeService { Handler = _ => asset };
        var storage = new FakeStorageService { FilesToReturn = new[] { "/tmp/fake.mp4" } };
        var workspace = CreateWorkspace(mediaProbeService: probe, storageService: storage);

        await workspace.StartAddTextToVideoFlowAsync();
        Dispatcher.UIThread.RunJobs();

        var videoTrack = Assert.Single(workspace.Timeline.Tracks, t => t.Track.Kind == TimelineTrackKind.Video);
        Assert.Single(videoTrack.Clips);

        var textTrack = Assert.Single(workspace.Timeline.Tracks, t => t.Track.Kind == TimelineTrackKind.Text);
        Assert.Single(textTrack.Clips);
        Assert.True(textTrack.Clips[0].IsTextClip);
    }

    /// <summary>Cancelling the video picker (empty file list) must leave the workspace untouched -
    /// no phantom empty Text track left behind from a shortcut the user backed out of.</summary>
    [AvaloniaFact]
    public async Task StartAddTextToVideoFlowAsync_PickerCancelled_AddsNoTracks()
    {
        var storage = new FakeStorageService { FilesToReturn = Array.Empty<string>() };
        var workspace = CreateWorkspace(storageService: storage);

        await workspace.StartAddTextToVideoFlowAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(workspace.Timeline.Tracks);
    }

    /// <summary>A second import after the timeline already has a clip on it must NOT rearrange the user's
    /// edit in progress - it should only land in the media library, same as before this feature existed.</summary>
    [AvaloniaFact]
    public async Task ImportFilesAsync_SecondImportAfterTimelineAlreadyHasAClip_DoesNotAutoPlace()
    {
        var firstAsset = new MediaAsset { FilePath = "/tmp/first.mp4", Duration = TimeSpan.FromSeconds(6), HasVideoStream = true };
        var secondAsset = new MediaAsset { FilePath = "/tmp/second.mp4", Duration = TimeSpan.FromSeconds(4), HasVideoStream = true };
        var probe = new FakeMediaProbeService { Handler = path => path == "/tmp/first.mp4" ? firstAsset : secondAsset };
        var project = new Project { Name = "Test projekat" };
        var workspace = new WorkspaceViewModel(
            project, new FakeProjectRepository(), probe, new FakeStorageService(),
            new FakeFramePreviewService(), new FakeSubtitleGeneratorService(), new FakeRenderService(), new LoggerConfiguration().CreateLogger());

        await workspace.ImportFilesAsync(new[] { "/tmp/first.mp4" });
        Dispatcher.UIThread.RunJobs();
        await workspace.ImportFilesAsync(new[] { "/tmp/second.mp4" });
        Dispatcher.UIThread.RunJobs();

        Assert.Single(workspace.Timeline.Tracks);
        Assert.Single(workspace.Timeline.Tracks[0].Clips);
    }

    /// <summary>Regression test for a real bug: ExportVideo() used to fire ExportRequested without
    /// syncing the live timeline edit session back onto Project first, so the render queue (which reads
    /// Project.Timeline.Tracks directly) could silently render a stale/empty timeline even though the
    /// user could see clips on screen.</summary>
    [AvaloniaFact]
    public void ExportVideo_UnsavedTimelineEdit_SyncsToProjectBeforeRaisingExportRequested()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(5) };
        var project = new Project { Name = "Test projekat", MediaLibrary = { asset } };
        var workspace = CreateWorkspace(project);
        workspace.MediaLibrary.Add(new MediaAssetViewModel(asset));
        workspace.Timeline.AddVideoTrackCommand.Execute(null);
        workspace.Timeline.SelectedMediaAsset = workspace.MediaLibrary[0];
        workspace.Timeline.Tracks[0].AddClipAtPlayheadCommand.Execute(null);

        // Not calling Timeline.SaveToProject() manually - ExportVideo() itself must do it.
        Assert.Empty(project.Timeline.Tracks);

        var exportRequestedFired = false;
        workspace.ExportRequested += () => exportRequestedFired = true;
        workspace.ExportVideoCommand.Execute(null);

        Assert.True(exportRequestedFired);
        Assert.Single(project.Timeline.Tracks);
        Assert.Single(project.Timeline.Tracks[0].Clips);
    }

    /// <summary>The clip-existence check must run independently of whether the real (LibVLC) player is
    /// available on this machine - a user with nothing on the timeline yet should see "add a clip first"
    /// regardless of platform, not a native-library error that has nothing to do with their actual
    /// mistake.</summary>
    [AvaloniaFact]
    public async Task RenderRealPreviewAsync_NoClipsOnTimeline_DoesNotCallRenderService()
    {
        var renderCalled = false;
        var renderService = new FakeRenderService { Handler = (_, job, _) => { renderCalled = true; return Task.FromResult(job.Settings.OutputFilePath); } };
        var workspace = CreateWorkspace(renderService: renderService);

        await workspace.RenderRealPreviewCommand.ExecuteAsync(null);

        Assert.False(renderCalled);
        Assert.Contains("Dodajte bar jedan klip", workspace.RealPreviewStatusMessage);
    }

    /// <summary>On any machine without libvlc's native library available - this project's own Linux dev
    /// sandbox included, since only the win-x64 build bundles libvlc.dll - RealPreview.IsAvailable is
    /// false by construction (see RealPreviewViewModel's constructor try/catch around
    /// LibVLCSharp.Shared.Core.Initialize()). The command must degrade gracefully with a clear message
    /// instead of throwing, and must never call the render service for a render nothing could play back
    /// anyway.</summary>
    [AvaloniaFact]
    public async Task RenderRealPreviewAsync_ClipExistsButRealPlayerUnavailableOnThisMachine_DoesNotCallRenderService()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(5) };
        var project = new Project { Name = "Test projekat", MediaLibrary = { asset } };
        var renderCalled = false;
        var renderService = new FakeRenderService { Handler = (_, job, _) => { renderCalled = true; return Task.FromResult(job.Settings.OutputFilePath); } };
        var workspace = CreateWorkspace(project, renderService: renderService);
        workspace.MediaLibrary.Add(new MediaAssetViewModel(asset));
        workspace.Timeline.AddVideoTrackCommand.Execute(null);
        workspace.Timeline.SelectedMediaAsset = workspace.MediaLibrary[0];
        workspace.Timeline.Tracks[0].AddClipAtPlayheadCommand.Execute(null);

        // This sandbox never has libvlc's native library available - a real, disclosed environment
        // constraint (same category as huggingface.co being blocked for the Whisper model), not assumed.
        Assert.False(workspace.RealPreview.IsAvailable);

        await workspace.RenderRealPreviewCommand.ExecuteAsync(null);

        Assert.False(renderCalled);
        Assert.Equal(workspace.RealPreview.UnavailableReason, workspace.RealPreviewStatusMessage);
    }

    /// <summary>Real feature request from a user: "automatically add text from the video" - this drives
    /// the same local Whisper transcription the standalone "Generiši titlove (SRT)" tool uses, but lands
    /// the result directly on the timeline as real caption clips instead of only a standalone .srt file.</summary>
    [AvaloniaFact]
    public async Task GenerateCaptionsForVideoAsync_VideoOnTimelineAndModelReady_AddsCaptionTrackWithClips()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(10), HasVideoStream = true };
        var project = new Project { Name = "Test projekat", MediaLibrary = { asset } };
        var subtitleService = new FakeSubtitleGeneratorService
        {
            IsModelReady = true,
            SegmentsToReturn = new[]
            {
                new TranscribedCaptionSegment(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), "Zdravo"),
                new TranscribedCaptionSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "svima")
            }
        };
        var workspace = CreateWorkspace(project, subtitleService);
        workspace.MediaLibrary.Add(new MediaAssetViewModel(asset));
        workspace.Timeline.AddVideoTrackCommand.Execute(null);
        workspace.Timeline.SelectedMediaAsset = workspace.MediaLibrary[0];
        workspace.Timeline.Tracks[0].AddClipAtPlayheadCommand.Execute(null);

        await workspace.GenerateCaptionsForVideoCommand.ExecuteAsync(null);

        var captionTrack = Assert.Single(workspace.Timeline.Tracks, t => t.Track.Kind == TimelineTrackKind.Caption);
        Assert.Equal(2, captionTrack.Clips.Count);
        Assert.Equal("Zdravo", captionTrack.Clips[0].Clip.TextContent);
        Assert.Equal(2, captionTrack.Clips[1].Clip.TimelineStartSeconds);
        Assert.Contains("2", workspace.CaptionsStatusMessage);
    }

    /// <summary>Real feature request: karaoke-style captions where each spoken word appears on screen
    /// individually, timed to when it's actually said - drives word-level Whisper transcription
    /// (TranscribeWordsAsync) instead of line-level, but reuses the exact same timeline-placement path.</summary>
    [AvaloniaFact]
    public async Task GenerateKaraokeCaptionsForVideoAsync_VideoOnTimelineAndModelReady_AddsOneClipPerWord()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(10), HasVideoStream = true };
        var project = new Project { Name = "Test projekat", MediaLibrary = { asset } };
        var subtitleService = new FakeSubtitleGeneratorService
        {
            IsModelReady = true,
            WordSegmentsToReturn = new[]
            {
                new TranscribedCaptionSegment(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.4), "Zdravo"),
                new TranscribedCaptionSegment(TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(0.7), "svima"),
                new TranscribedCaptionSegment(TimeSpan.FromSeconds(0.7), TimeSpan.FromSeconds(1.0), "danas")
            }
        };
        var workspace = CreateWorkspace(project, subtitleService);
        workspace.MediaLibrary.Add(new MediaAssetViewModel(asset));
        workspace.Timeline.AddVideoTrackCommand.Execute(null);
        workspace.Timeline.SelectedMediaAsset = workspace.MediaLibrary[0];
        workspace.Timeline.Tracks[0].AddClipAtPlayheadCommand.Execute(null);

        await workspace.GenerateKaraokeCaptionsForVideoCommand.ExecuteAsync(null);

        var captionTrack = Assert.Single(workspace.Timeline.Tracks, t => t.Track.Kind == TimelineTrackKind.Caption);
        Assert.Equal(3, captionTrack.Clips.Count);
        Assert.Equal("Zdravo", captionTrack.Clips[0].Clip.TextContent);
        Assert.Equal("svima", captionTrack.Clips[1].Clip.TextContent);
        Assert.Equal("danas", captionTrack.Clips[2].Clip.TextContent);
        Assert.Equal(0.4, captionTrack.Clips[1].Clip.TimelineStartSeconds, precision: 5);
        Assert.Contains("karaoke", workspace.CaptionsStatusMessage);
    }

    [AvaloniaFact]
    public async Task GenerateCaptionsForVideoAsync_NoVideoOnTimeline_DoesNotCallTranscribeAndExplainsWhy()
    {
        var workspace = CreateWorkspace(subtitleGeneratorService: new FakeSubtitleGeneratorService { IsModelReady = true });

        await workspace.GenerateCaptionsForVideoCommand.ExecuteAsync(null);

        Assert.Empty(workspace.Timeline.Tracks);
        Assert.Contains("Dodajte video", workspace.CaptionsStatusMessage);
    }

    [AvaloniaFact]
    public async Task GenerateCaptionsForVideoAsync_ModelNotReady_DoesNotAddTrackAndPointsToSrtTool()
    {
        var asset = new MediaAsset { FilePath = "/tmp/fake.mp4", Duration = TimeSpan.FromSeconds(10), HasVideoStream = true };
        var project = new Project { Name = "Test projekat", MediaLibrary = { asset } };
        var subtitleService = new FakeSubtitleGeneratorService { IsModelReady = false };
        var workspace = CreateWorkspace(project, subtitleService);
        workspace.MediaLibrary.Add(new MediaAssetViewModel(asset));
        workspace.Timeline.AddVideoTrackCommand.Execute(null);
        workspace.Timeline.SelectedMediaAsset = workspace.MediaLibrary[0];
        workspace.Timeline.Tracks[0].AddClipAtPlayheadCommand.Execute(null);

        await workspace.GenerateCaptionsForVideoCommand.ExecuteAsync(null);

        Assert.Single(workspace.Timeline.Tracks); // still just the video track, no caption track added
        Assert.Contains("Generiši titlove (SRT)", workspace.CaptionsStatusMessage);
    }
}
