using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.AI;

/// <summary>
/// Launches the local AI worker as a subprocess and speaks its versioned JSON-in/JSONL-out protocol
/// (spec Phase 5). The request is always written to a temp file and passed as a path argument - audio
/// bytes and the request/response payloads never travel through stdin/JSON directly.
///
/// Two run modes, mirroring <see cref="Media.YouTubeDownloadService"/>'s override pattern:
/// - Real usage: <c>python(3) &lt;bundled ai_worker.py&gt; --request &lt;path&gt;</c>.
/// - Tests: <paramref name="workerCommandOverride"/> in the constructor replaces the whole command with
///   a single executable (e.g. the FakeAiWorker test fixture), so process orchestration and JSONL
///   parsing are exercised against a real process without needing Python or any ML package installed.
/// </summary>
public sealed class AiWorkerClient : IAiWorkerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(), new TimeSpanSecondsConverter() }
    };

    private readonly string? _workerCommandOverride;
    private readonly string _pythonPath;
    private readonly string _workerScriptPath;

    public AiWorkerClient(
        string? workerCommandOverride = null,
        string? pythonOverridePath = null,
        string? workerScriptOverridePath = null)
    {
        _workerCommandOverride = workerCommandOverride;
        _pythonPath = !string.IsNullOrWhiteSpace(pythonOverridePath)
            ? pythonOverridePath
            : OperatingSystem.IsWindows() ? "python" : "python3";
        _workerScriptPath = workerScriptOverridePath
            ?? Path.Combine(AppContext.BaseDirectory, "Tools", "ai-worker", "ai_worker.py");
    }

    public async Task<AiWorkerCapabilities> CheckCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var request = new AiWorkerRequest
        {
            JobKind = AiWorkerJobKind.CapabilityCheck,
            Profile = AiProcessingProfile.Fast
        };

        var pythonVersion = (string?)null;
        var fasterWhisper = false;
        var whisperX = false;
        var demucs = false;
        var errorMessage = (string?)null;

        try
        {
            await foreach (var evt in RunAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (evt.Type == AiWorkerEventType.Error)
                {
                    errorMessage = evt.Message;
                    continue;
                }

                if (evt.Type != AiWorkerEventType.CapabilityCheck)
                {
                    continue;
                }

                switch (evt.Engine)
                {
                    case "python":
                        pythonVersion = evt.Message;
                        break;
                    case "faster_whisper":
                        fasterWhisper = evt.EngineAvailable == true;
                        break;
                    case "whisperx":
                        whisperX = evt.EngineAvailable == true;
                        break;
                    case "demucs":
                        demucs = evt.EngineAvailable == true;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            return new AiWorkerCapabilities { WorkerReachable = false, Error = ex.Message };
        }

        // Only a real "python" capability line proves the script actually ran end to end - a process
        // that merely launched (e.g. python found, but the bundled script itself missing) and exited
        // non-zero must not be reported as reachable just because nothing threw.
        if (pythonVersion is null)
        {
            return new AiWorkerCapabilities
            {
                WorkerReachable = false,
                Error = errorMessage ?? "AI worker nije vratio očekivan odgovor."
            };
        }

        return new AiWorkerCapabilities
        {
            WorkerReachable = true,
            PythonVersion = pythonVersion,
            FasterWhisperAvailable = fasterWhisper,
            WhisperXAvailable = whisperX,
            DemucsAvailable = demucs
        };
    }

    public async IAsyncEnumerable<AiWorkerEvent> RunAsync(
        AiWorkerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestPath = Path.Combine(Path.GetTempPath(), $"npvs_ai_request_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, JsonOptions), cancellationToken)
            .ConfigureAwait(false);

        using var process = new Process { StartInfo = BuildStartInfo(requestPath) };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            File.Delete(requestPath);
            throw new InvalidOperationException(
                "AI worker nije pronađen (Python i ai_worker.py). Ovo je opcioni deo aplikacije - " +
                "Whisper.net i dalje radi bez njega.", ex);
        }

        try
        {
            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var sawErrorEvent = false;

            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                AiWorkerEvent? evt;
                try
                {
                    evt = JsonSerializer.Deserialize<AiWorkerEvent>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    continue; // Ignore any stray non-JSON line rather than aborting the whole run.
                }

                if (evt is not null)
                {
                    sawErrorEvent |= evt.Type == AiWorkerEventType.Error;
                    yield return evt;
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdErr = await stdErrTask.ConfigureAwait(false);

            // Only synthesize a fallback Error from stderr/exit code if the worker didn't already
            // explain itself via a proper Error event - otherwise every honest error would double up.
            if (process.ExitCode != 0 && !sawErrorEvent)
            {
                yield return new AiWorkerEvent
                {
                    Type = AiWorkerEventType.Error,
                    Message = string.IsNullOrWhiteSpace(stdErr)
                        ? $"AI worker se završio sa greškom (kod {process.ExitCode})."
                        : stdErr.Trim()
                };
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort on cancellation */ }
            }

            if (File.Exists(requestPath))
            {
                File.Delete(requestPath);
            }
        }
    }

    private ProcessStartInfo BuildStartInfo(string requestPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _workerCommandOverride ?? _pythonPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (_workerCommandOverride is null)
        {
            startInfo.ArgumentList.Add(_workerScriptPath);
        }

        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(requestPath);
        return startInfo;
    }

    /// <summary>Wire format for word timing is fractional seconds, not .NET's TimeSpan string format, so the Python side only ever writes/reads plain numbers.</summary>
    private sealed class TimeSpanSecondsConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TimeSpan.FromSeconds(reader.GetDouble());

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.TotalSeconds);
    }
}
