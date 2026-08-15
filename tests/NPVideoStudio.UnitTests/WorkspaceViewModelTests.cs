using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<MediaAsset> ProbeAsync(string filePath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
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
    private static WorkspaceViewModel CreateWorkspace(Project? project = null)
    {
        project ??= new Project { Name = "Test projekat" };
        return new WorkspaceViewModel(
            project,
            new FakeProjectRepository(),
            new FakeMediaProbeService(),
            new FakeStorageService(),
            new FakeFramePreviewService(),
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
            framePreview, new LoggerConfiguration().CreateLogger());
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
}
