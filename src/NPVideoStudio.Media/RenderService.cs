using System.Diagnostics;
using System.Globalization;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Real ffmpeg-based render pipeline (spec Phase 9): builds the filter graph via
/// <see cref="FfmpegFilterGraphBuilder"/>, runs ffmpeg with real progress reporting (`-progress pipe:1`),
/// automatic codec fallback to libx264, and atomic temp-file-then-rename output - same "never leave a
/// half-written file looking valid" rule as every other real generation path in this codebase.
/// </summary>
public sealed class RenderService : IRenderService
{
    private readonly string _ffmpegPath;

    public RenderService(string? ffmpegOverridePath = null)
    {
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
    }

    public async Task<string> RenderAsync(Project project, RenderJob job, CancellationToken cancellationToken = default)
    {
        var settings = job.Settings;

        if (File.Exists(settings.OutputFilePath) && !settings.OverwriteConfirmed)
        {
            throw new InvalidOperationException(
                "Izlazni fajl već postoji - potrebna je potvrda za prepisivanje pre pokretanja izvoza.");
        }

        job.Status = RenderJobStatus.Running;
        job.StartedAt = DateTimeOffset.Now;

        var plan = FfmpegFilterGraphBuilder.Build(
            project.Timeline, project.MediaLibrary, project.Format.Width, project.Format.Height, project.Format.Fps);

        var directory = Path.GetDirectoryName(settings.OutputFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var outputExtension = Path.GetExtension(settings.OutputFilePath);
        var outputWithoutExtension = settings.OutputFilePath[..^outputExtension.Length];
        var tempPath = $"{outputWithoutExtension}.{Guid.NewGuid():N}{outputExtension}";

        try
        {
            int exitCode;
            string stdErr;
            try
            {
                (exitCode, stdErr) = await RunWithFallbackAsync(plan, job, tempPath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                job.Status = RenderJobStatus.Cancelled;
                throw;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                job.Status = RenderJobStatus.Cancelled;
                throw new OperationCanceledException(cancellationToken);
            }

            if (exitCode != 0)
            {
                job.Status = RenderJobStatus.Failed;
                job.ErrorMessage = string.IsNullOrWhiteSpace(stdErr) ? $"Render nije uspeo (kod {exitCode})." : stdErr.Trim();
                throw new InvalidOperationException(job.ErrorMessage);
            }

            if (!File.Exists(tempPath))
            {
                job.Status = RenderJobStatus.Failed;
                job.ErrorMessage = "ffmpeg je prijavio uspeh, ali izlazni fajl nije pronađen.";
                throw new InvalidOperationException(job.ErrorMessage);
            }

            File.Move(tempPath, settings.OutputFilePath, overwrite: true);
            job.Status = RenderJobStatus.Completed;
            job.ProgressPercent = 100;
            job.CompletedAt = DateTimeOffset.Now;
            return settings.OutputFilePath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private async Task<(int ExitCode, string StdErr)> RunWithFallbackAsync(
        FfmpegRenderPlan plan, RenderJob job, string tempPath, CancellationToken cancellationToken)
    {
        var (exitCode, stdErr) = await RunOnceAsync(plan, job.Settings.Codec, job, tempPath, cancellationToken).ConfigureAwait(false);

        if (exitCode != 0 && job.Settings.Codec != VideoCodec.Libx264 && !cancellationToken.IsCancellationRequested)
        {
            // Automatic fallback (spec): a hardware encoder that isn't present/working falls back to
            // the software encoder rather than failing the whole export.
            (exitCode, stdErr) = await RunOnceAsync(plan, VideoCodec.Libx264, job, tempPath, cancellationToken).ConfigureAwait(false);
        }

        return (exitCode, stdErr);
    }

    private async Task<(int ExitCode, string StdErr)> RunOnceAsync(
        FfmpegRenderPlan plan, VideoCodec codec, RenderJob job, string tempPath, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = BuildStartInfo(plan, codec, job.Settings, tempPath) };

        // Logged in full (no secrets - every argument is a local file path or a plain encoding
        // setting), overwritten if the automatic hardware-encoder fallback below runs a second attempt.
        job.FfmpegCommandLogged = $"{_ffmpegPath} {string.Join(' ', process.StartInfo.ArgumentList)}";

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException(
                "ffmpeg nije pronađen. Pokrenite scripts/check-dependencies.ps1 ili ga instalirajte i dodajte u PATH.", ex);
        }

        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                ReportProgressLine(line, plan.TotalDurationSeconds, job);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort on cancellation */ }
            }
        }

        var stdErr = await stdErrTask.ConfigureAwait(false);
        return (process.ExitCode, stdErr);
    }

    private static void ReportProgressLine(string line, double totalDurationSeconds, RenderJob job)
    {
        if (totalDurationSeconds <= 0)
        {
            return;
        }

        // ffmpeg's `-progress pipe:1` emits plain key=value lines; out_time_ms is the one we need.
        const string key = "out_time_ms=";
        if (!line.StartsWith(key, StringComparison.Ordinal))
        {
            return;
        }

        if (long.TryParse(line.AsSpan(key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
        {
            // Despite the "_ms" name, ffmpeg's progress output is actually in microseconds.
            var elapsedSeconds = microseconds / 1_000_000.0;
            job.ProgressPercent = Math.Clamp(elapsedSeconds / totalDurationSeconds * 100.0, 0, 100);
        }
    }

    private ProcessStartInfo BuildStartInfo(FfmpegRenderPlan plan, VideoCodec codec, RenderSettings settings, string tempPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-y");
        foreach (var input in plan.InputFilePaths)
        {
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(input);
        }

        startInfo.ArgumentList.Add("-filter_complex");
        startInfo.ArgumentList.Add(plan.FilterComplexArgument);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add(plan.VideoMapLabel);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add(plan.AudioMapLabel);

        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add(CodecName(codec));

        if (codec == VideoCodec.Libx264)
        {
            // CRF/preset are libx264-specific quality controls - hardware encoders use their own
            // quality knobs (not implemented this pass, they use each encoder's own defaults).
            startInfo.ArgumentList.Add("-crf");
            startInfo.ArgumentList.Add(settings.Crf.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-preset");
            startInfo.ArgumentList.Add(settings.Preset);
        }

        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("aac");
        startInfo.ArgumentList.Add("-b:a");
        startInfo.ArgumentList.Add($"{settings.AudioBitrateKbps}k");
        startInfo.ArgumentList.Add("-movflags");
        startInfo.ArgumentList.Add("+faststart");
        startInfo.ArgumentList.Add("-progress");
        startInfo.ArgumentList.Add("pipe:1");
        startInfo.ArgumentList.Add(tempPath);

        return startInfo;
    }

    private static string CodecName(VideoCodec codec) => codec switch
    {
        VideoCodec.H264Nvenc => "h264_nvenc",
        VideoCodec.H264Qsv => "h264_qsv",
        VideoCodec.H264Amf => "h264_amf",
        _ => "libx264"
    };
}
