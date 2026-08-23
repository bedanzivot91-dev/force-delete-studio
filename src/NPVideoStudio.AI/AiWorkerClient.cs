using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
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
    private string _pythonPath;
    private readonly string _workerScriptPath;
    private readonly string _installerScriptPath;

    public AiWorkerClient(
        string? workerCommandOverride = null,
        string? pythonOverridePath = null,
        string? workerScriptOverridePath = null,
        string? installerScriptOverridePath = null)
    {
        _workerCommandOverride = workerCommandOverride;
        _pythonPath = !string.IsNullOrWhiteSpace(pythonOverridePath)
            ? pythonOverridePath
            : ResolveAppPythonPath();
        _workerScriptPath = workerScriptOverridePath
            ?? Path.Combine(AppContext.BaseDirectory, "Tools", "ai-worker", "ai_worker.py");
        _installerScriptPath = installerScriptOverridePath
            ?? Path.Combine(AppContext.BaseDirectory, "Tools", "ai-worker", "install-song-ai.ps1");
    }

    private static string ResolveAppPythonPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var managedPython = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NPVideoStudio", "ai-runtime", "Scripts", "python.exe");
            if (File.Exists(managedPython))
            {
                return managedPython;
            }
            return "python";
        }
        return "python3";
    }

    public async Task InstallSongAiAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Automatska AI instalacija je trenutno namenjena Windows verziji programa.");
        }

        if (!File.Exists(_installerScriptPath))
        {
            throw new FileNotFoundException("Installer za AI nije pronađen u programu.", _installerScriptPath);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(_installerScriptPath);
        process.StartInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

        process.Start();
        try
        {
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line)) progress?.Report(line.Trim());
            }

            await process.WaitForExitAsync(cancellationToken);
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? $"AI instalacija nije uspela (kod {process.ExitCode})."
                    : error.Trim());
            }

            _pythonPath = ResolveAppPythonPath();
        }
        finally
        {
            // Cancelling the async reads/wait is not enough: PowerShell can keep winget/pip/python child
            // processes alive. Terminate the whole tree and wait for the actual exit before returning so
            // the UI's "Otkaži" cannot report completion while installation continues in the background.
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch { /* best-effort during teardown */ }
            }
        }
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
        var lyricAlign = false;
        var openCv = false;
        var backgroundRemoval = false;
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
                    case "lyric_align":
                        lyricAlign = evt.EngineAvailable == true;
                        break;
                    case "opencv":
                        openCv = evt.EngineAvailable == true;
                        break;
                    case "rembg":
                        backgroundRemoval = evt.EngineAvailable == true;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            return new AiWorkerCapabilities { WorkerReachable = false, Error = ex.Message };
        }

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
            DemucsAvailable = demucs,
            LyricAlignAvailable = lyricAlign,
            OpenCvAvailable = openCv,
            BackgroundRemovalAvailable = backgroundRemoval
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
                    continue;
                }

                if (evt is not null)
                {
                    sawErrorEvent |= evt.Type == AiWorkerEventType.Error;
                    yield return evt;
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdErr = await stdErrTask.ConfigureAwait(false);

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
                try { process.Kill(entireProcessTree: true); } catch { }
            }

            if (File.Exists(requestPath))
            {
                File.Delete(requestPath);
            }
        }
    }

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private ProcessStartInfo BuildStartInfo(string requestPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _workerCommandOverride ?? _pythonPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (_workerCommandOverride is null)
        {
            startInfo.ArgumentList.Add(_workerScriptPath);
            startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            var bundledFfmpegDirectory = Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg");
            if (Directory.Exists(bundledFfmpegDirectory))
            {
                var existingPath = startInfo.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? "";
                startInfo.EnvironmentVariables["PATH"] = bundledFfmpegDirectory + Path.PathSeparator + existingPath;
            }

            var modelCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NPVideoStudio", "ai-models");
            Directory.CreateDirectory(modelCache);
            startInfo.EnvironmentVariables["HF_HOME"] = modelCache;
        }

        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(requestPath);
        return startInfo;
    }

    private sealed class TimeSpanSecondsConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TimeSpan.FromSeconds(reader.GetDouble());

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.TotalSeconds);
    }
}
