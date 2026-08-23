using NPVideoStudio.AI;
using NPVideoStudio.Domain;
using Xunit;

namespace NPVideoStudio.UnitTests;

/// <summary>
/// Real integration tests for AiWorkerClient against a fake worker process (tests/FakeAiWorker) - same
/// "mock process, not a fake in test code alone" pattern as YouTubeDownloadServiceTests/
/// SongRecognitionServiceTests. Exercises the actual process launch, request-file JSON serialization,
/// JSONL event parsing, non-zero exit handling, and cancellation without needing Python or any ML
/// package installed.
/// </summary>
public class AiWorkerClientTests
{
    private readonly string _fakeWorkerPath;

    public AiWorkerClientTests()
    {
        var exeName = OperatingSystem.IsWindows() ? "FakeAiWorker.exe" : "FakeAiWorker";
        _fakeWorkerPath = Path.Combine(AppContext.BaseDirectory, exeName);
        Assert.True(File.Exists(_fakeWorkerPath), $"Fake AI worker nije pronađen na {_fakeWorkerPath} - proveriti ProjectReference ka tests/FakeAiWorker.");
    }

    private AiWorkerClient CreateClient() => new(workerCommandOverride: _fakeWorkerPath);

    [Fact]
    public async Task CheckCapabilitiesAsync_RealProcessCall_ParsesCapabilityEvents()
    {
        var client = CreateClient();

        var capabilities = await client.CheckCapabilitiesAsync();

        Assert.True(capabilities.WorkerReachable);
        Assert.Equal("3.11.0-fake", capabilities.PythonVersion);
        Assert.True(capabilities.FasterWhisperAvailable);
        Assert.False(capabilities.WhisperXAvailable);
        Assert.False(capabilities.DemucsAvailable);
        Assert.True(capabilities.LyricAlignAvailable);
    }

    [Fact]
    public async Task RunAsync_SuccessfulJob_YieldsProgressThenResultThenDone()
    {
        var client = CreateClient();
        var request = new AiWorkerRequest
        {
            JobKind = AiWorkerJobKind.UnknownSongTranscription,
            Profile = AiProcessingProfile.Balanced,
            AudioFilePath = "/tmp/whatever.wav"
        };

        var events = new List<AiWorkerEvent>();
        await foreach (var evt in client.RunAsync(request))
        {
            events.Add(evt);
        }

        Assert.Collection(events,
            e => Assert.Equal(AiWorkerEventType.Progress, e.Type),
            e => Assert.Equal(AiWorkerEventType.Result, e.Type),
            e => Assert.Equal(AiWorkerEventType.Done, e.Type));

        var result = events[1];
        Assert.NotNull(result.Words);
        var word = Assert.Single(result.Words!);
        Assert.Equal("reč", word.Text);
        Assert.Equal(TimeSpan.FromSeconds(1.0), word.Start);
        Assert.Equal(TimeSpan.FromSeconds(1.5), word.End);
        Assert.Equal(0.9, word.Confidence);
    }

    [Fact]
    public async Task RunAsync_WorkerReportsError_YieldsErrorEvent()
    {
        var client = CreateClient();
        var request = new AiWorkerRequest
        {
            JobKind = AiWorkerJobKind.UnknownSongTranscription,
            Profile = AiProcessingProfile.Fast,
            AudioFilePath = "TRIGGER_ERROR"
        };

        var events = new List<AiWorkerEvent>();
        await foreach (var evt in client.RunAsync(request))
        {
            events.Add(evt);
        }

        var error = Assert.Single(events);
        Assert.Equal(AiWorkerEventType.Error, error.Type);
        Assert.Contains("simulated failure", error.Message);
    }

    [Fact]
    public async Task RunAsync_NonZeroExitWithoutErrorEvent_SynthesizesErrorFromStderr()
    {
        var client = CreateClient();
        var request = new AiWorkerRequest
        {
            JobKind = AiWorkerJobKind.UnknownSongTranscription,
            Profile = AiProcessingProfile.Fast,
            AudioFilePath = "TRIGGER_MALFORMED_EXIT"
        };

        var events = new List<AiWorkerEvent>();
        await foreach (var evt in client.RunAsync(request))
        {
            events.Add(evt);
        }

        var error = Assert.Single(events);
        Assert.Equal(AiWorkerEventType.Error, error.Type);
        Assert.Contains("crashed without a proper Error event", error.Message);
    }

    [Fact]
    public async Task RunAsync_Cancelled_KillsProcessQuickly()
    {
        var client = CreateClient();
        var request = new AiWorkerRequest
        {
            JobKind = AiWorkerJobKind.UnknownSongTranscription,
            Profile = AiProcessingProfile.MostAccurate,
            AudioFilePath = "TRIGGER_SLOW"
        };

        using var cts = new CancellationTokenSource();
        var events = new List<AiWorkerEvent>();
        var firstProgress = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in client.RunAsync(request, cts.Token))
                {
                    events.Add(evt);
                    if (evt.Type == AiWorkerEventType.Progress)
                    {
                        firstProgress.TrySetResult(true);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected: cancelling mid-stream throws from the enumerator after process teardown.
            }
        });

        // Synchronize on the worker's real first event instead of assuming every Windows runner can
        // launch a subprocess within one second. If no event arrives, that is still a real test failure.
        var progressCompleted = await Task.WhenAny(firstProgress.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(firstProgress.Task, progressCompleted);
        cts.Cancel();

        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(runTask, completed);
        Assert.Contains(events, e => e.Type == AiWorkerEventType.Progress);
    }
}
