using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.AI;

/// <summary>Runs the bundled OpenCV CSRT tracker through the app-owned Python 3.12 environment.
/// The request is a temporary JSON file, the result is one JSON object, and cancellation kills the whole
/// Python process tree. No network service and no fabricated fallback coordinates.</summary>
public sealed class MotionTrackingService : IMotionTrackingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _pythonPath;
    private readonly string _scriptPath;

    public MotionTrackingService(string? pythonOverridePath = null, string? scriptOverridePath = null)
    {
        _pythonPath = !string.IsNullOrWhiteSpace(pythonOverridePath)
            ? pythonOverridePath
            : ResolveAppPythonPath();
        _scriptPath = scriptOverridePath
            ?? Path.Combine(AppContext.BaseDirectory, "Tools", "ai-worker", "motion_tracker.py");
    }

    public async Task<IReadOnlyList<MotionTrackingPoint>> TrackAsync(
        MotionTrackingRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.MediaFilePath) || !File.Exists(request.MediaFilePath))
        {
            throw new FileNotFoundException("Video za Motion Tracking ne postoji.", request.MediaFilePath);
        }
        if (!File.Exists(_scriptPath))
        {
            throw new FileNotFoundException("Motion Tracking worker nije pronađen u programu.", _scriptPath);
        }
        if (request.SourceEndSeconds <= request.SourceStartSeconds + 0.05)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Motion Tracking opseg je prekratak.");
        }

        var normalized = new MotionTrackingRequest
        {
            MediaFilePath = Path.GetFullPath(request.MediaFilePath),
            SourceStartSeconds = Math.Max(0, request.SourceStartSeconds),
            SourceEndSeconds = request.SourceEndSeconds,
            InitialRegion = request.InitialRegion.Clamp(),
            SampleIntervalSeconds = Math.Clamp(request.SampleIntervalSeconds, 0.04, 1.0)
        };

        var requestPath = Path.Combine(Path.GetTempPath(), $"npvs_tracking_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(normalized, JsonOptions), cancellationToken)
            .ConfigureAwait(false);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add(_scriptPath);
        process.StartInfo.ArgumentList.Add("--request");
        process.StartInfo.ArgumentList.Add(requestPath);
        process.StartInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

        try
        {
            try
            {
                process.Start();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                throw new InvalidOperationException(
                    "Motion Tracking Python runtime nije pronađen. Otvorite Alati i modeli i instalirajte/ažurirajte AI alate.", ex);
            }

            progress?.Report(5);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            TrackingResponse? response = null;
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                try { response = JsonSerializer.Deserialize<TrackingResponse>(stdout.Trim(), JsonOptions); }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException("Motion Tracking worker je vratio nevažeći JSON rezultat.", ex);
                }
            }

            if (process.ExitCode != 0 || response?.Error is not null)
            {
                throw new InvalidOperationException(
                    response?.Error ?? (string.IsNullOrWhiteSpace(stderr)
                        ? $"Motion Tracking nije uspeo (kod {process.ExitCode})."
                        : stderr.Trim()));
            }

            var points = response?.TrackingPoints ?? new List<MotionTrackingPoint>();
            if (points.Count < 2)
            {
                throw new InvalidOperationException("Motion Tracking nije vratio dovoljno tačaka za putanju.");
            }

            progress?.Report(100);
            return points
                .OrderBy(point => point.SourceTimeSeconds)
                .Select(point => new MotionTrackingPoint
                {
                    SourceTimeSeconds = point.SourceTimeSeconds,
                    CenterX = Math.Clamp(point.CenterX, 0, 1),
                    CenterY = Math.Clamp(point.CenterY, 0, 1),
                    Width = Math.Clamp(point.Width, 0.001, 1),
                    Height = Math.Clamp(point.Height, 0.001, 1),
                    Confidence = Math.Clamp(point.Confidence, 0, 1)
                })
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch { }
            }
            try { File.Delete(requestPath); } catch { }
        }
    }

    private static string ResolveAppPythonPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var managedPython = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NPVideoStudio", "ai-runtime", "Scripts", "python.exe");
            if (File.Exists(managedPython)) return managedPython;
            return "python";
        }
        return "python3";
    }

    private sealed class TrackingResponse
    {
        public List<MotionTrackingPoint>? TrackingPoints { get; init; }
        public string? Tracker { get; init; }
        public string? Error { get; init; }
    }
}
