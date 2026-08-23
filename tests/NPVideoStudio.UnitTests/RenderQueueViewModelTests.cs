using Avalonia.Headless.XUnit;
using NPVideoStudio.App.ViewModels;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;
using Serilog;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>Fake so render-queue ViewModel tests don't need real ffmpeg - <see cref="RenderService"/>'s
/// own real-process behavior is already covered by RenderServiceTests.cs.</summary>
public sealed class FakeRenderService : IRenderService
{
    public Func<Project, RenderJob, CancellationToken, Task<string>>? Handler { get; set; }

    public Task<string> RenderAsync(Project project, RenderJob job, CancellationToken cancellationToken = default) =>
        Handler is null ? Task.FromResult(job.Settings.OutputFilePath) : Handler(project, job, cancellationToken);
}

/// <summary>
/// [AvaloniaFact] (not [Fact]) because RenderQueueViewModel constructs a real Avalonia DispatcherTimer to
/// poll job progress - same reason WorkspaceViewModelTests.cs needs it for PlayerViewModel.
/// </summary>
public class RenderQueueViewModelTests
{
    private static RenderQueueViewModel Create(Project? project = null, IRenderService? renderService = null, FakeStorageService? storage = null) =>
        new(project ?? new Project { Name = "Moj Projekat" },
            renderService ?? new FakeRenderService(),
            storage ?? new FakeStorageService(),
            new LoggerConfiguration().CreateLogger());

    [AvaloniaFact]
    public void Construction_DefaultsOutputFileName_FromProjectName()
    {
        var vm = Create(new Project { Name = "Moj Projekat" });

        Assert.NotNull(vm.OutputFilePath);
        Assert.Contains("Moj Projekat_captioned.mp4", vm.OutputFilePath);
    }

    [AvaloniaFact]
    public void StartRender_NoOutputPath_ShowsValidationMessageAndQueuesNothing()
    {
        var vm = Create();
        vm.OutputFilePath = "   ";

        vm.StartRenderCommand.Execute(null);

        Assert.Empty(vm.Jobs);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [AvaloniaFact]
    public void StartRender_ExistingFileNotConfirmedViaPicker_RefusesToQueue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var vm = Create();
            vm.OutputFilePath = tempFile;

            vm.StartRenderCommand.Execute(null);

            Assert.Empty(vm.Jobs);
            Assert.Contains("već postoji", vm.StatusMessage);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [AvaloniaFact]
    public async Task PickOutputFile_ThenStartRender_OnExistingFile_ProceedsWithoutBlocking()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var storage = new FakeStorageService { SaveFileToReturn = tempFile };
            var fake = new FakeRenderService { Handler = (_, job, _) => { job.Status = RenderJobStatus.Completed; return Task.FromResult(job.Settings.OutputFilePath); } };
            var vm = Create(renderService: fake, storage: storage);

            await vm.PickOutputFileCommand.ExecuteAsync(null);
            Assert.Equal(tempFile, vm.OutputFilePath);

            vm.StartRenderCommand.Execute(null);

            Assert.Single(vm.Jobs);
            Assert.Equal(RenderJobStatus.Completed, vm.Jobs[0].Status);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [AvaloniaFact]
    public void StartRender_HandlerSucceeds_JobEndsCompletedAndStatusMessageReportsPath()
    {
        var fake = new FakeRenderService
        {
            Handler = (_, job, _) =>
            {
                job.Status = RenderJobStatus.Completed;
                job.ProgressPercent = 100;
                return Task.FromResult(job.Settings.OutputFilePath);
            }
        };
        var vm = Create(renderService: fake);
        vm.OutputFilePath = Path.Combine(Path.GetTempPath(), $"npvs-render-{Guid.NewGuid():N}.mp4");

        vm.StartRenderCommand.Execute(null);

        Assert.Single(vm.Jobs);
        Assert.Equal(RenderJobStatus.Completed, vm.Jobs[0].Status);
        Assert.Equal(100, vm.Jobs[0].ProgressPercent);
        Assert.Contains(vm.OutputFilePath, vm.StatusMessage);
    }

    [AvaloniaFact]
    public void StartRender_HandlerThrows_JobEndsFailedWithErrorMessage()
    {
        var fake = new FakeRenderService { Handler = (_, _, _) => throw new InvalidOperationException("ffmpeg nije uspeo") };
        var vm = Create(renderService: fake);
        vm.OutputFilePath = Path.Combine(Path.GetTempPath(), $"npvs-render-{Guid.NewGuid():N}.mp4");

        vm.StartRenderCommand.Execute(null);

        Assert.Single(vm.Jobs);
        Assert.Equal(RenderJobStatus.Failed, vm.Jobs[0].Status);
        Assert.Equal("ffmpeg nije uspeo", vm.Jobs[0].ErrorMessage);
        Assert.True(vm.Jobs[0].HasError);
    }

    [AvaloniaFact]
    public void MultipleStartRenderCalls_QueueSeparateJobsIndependently()
    {
        var fake = new FakeRenderService
        {
            Handler = (_, job, _) => { job.Status = RenderJobStatus.Completed; return Task.FromResult(job.Settings.OutputFilePath); }
        };
        var vm = Create(renderService: fake);

        vm.OutputFilePath = Path.Combine(Path.GetTempPath(), $"npvs-render-{Guid.NewGuid():N}.mp4");
        vm.StartRenderCommand.Execute(null);
        vm.OutputFilePath = Path.Combine(Path.GetTempPath(), $"npvs-render-{Guid.NewGuid():N}.mp4");
        vm.StartRenderCommand.Execute(null);

        Assert.Equal(2, vm.Jobs.Count);
        Assert.All(vm.Jobs, j => Assert.Equal(RenderJobStatus.Completed, j.Status));
    }

    [AvaloniaFact]
    public void CancelCommand_OnRunningJob_CancelsTheTokenPassedToRenderService()
    {
        var tcs = new TaskCompletionSource<string>();
        CancellationToken capturedToken = default;
        var fake = new FakeRenderService
        {
            Handler = (_, job, ct) =>
            {
                capturedToken = ct;
                job.Status = RenderJobStatus.Running;
                return tcs.Task;
            }
        };
        var vm = Create(renderService: fake);
        vm.OutputFilePath = Path.Combine(Path.GetTempPath(), $"npvs-render-{Guid.NewGuid():N}.mp4");

        vm.StartRenderCommand.Execute(null);

        var item = Assert.Single(vm.Jobs);
        Assert.True(item.CancelCommand.CanExecute(null));

        item.CancelCommand.Execute(null);

        Assert.True(capturedToken.IsCancellationRequested);
        tcs.TrySetCanceled();
    }

    [AvaloniaFact]
    public void CancelCommand_OnCompletedJob_CannotExecute()
    {
        var fake = new FakeRenderService
        {
            Handler = (_, job, _) => { job.Status = RenderJobStatus.Completed; return Task.FromResult(job.Settings.OutputFilePath); }
        };
        var vm = Create(renderService: fake);
        vm.OutputFilePath = Path.Combine(Path.GetTempPath(), $"npvs-render-{Guid.NewGuid():N}.mp4");

        vm.StartRenderCommand.Execute(null);

        Assert.False(vm.Jobs[0].CancelCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void BackCommand_RaisesBackRequested()
    {
        var vm = Create();
        var raised = false;
        vm.BackRequested += () => raised = true;

        vm.BackCommand.Execute(null);

        Assert.True(raised);
    }
}
