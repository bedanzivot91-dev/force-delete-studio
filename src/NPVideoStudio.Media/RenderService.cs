using System.Diagnostics;
using System.Globalization;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Real ffmpeg-based render pipeline. One graph is shared by preview/export; this service owns container,
/// codec, progress, cancellation, hardware fallback and atomic temp-file replacement.
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
        ValidateSettings(settings);

        if (File.Exists(settings.OutputFilePath) && !settings.OverwriteConfirmed)
        {
            throw new InvalidOperationException(
                "Izlazni fajl već postoji - potrebna je potvrda za prepisivanje pre pokretanja izvoza.");
        }

        job.Status = RenderJobStatus.Running;
        job.StartedAt = DateTimeOffset.Now;

        using var stabilization = await VideoStabilizationPrepass.PrepareAsync(project, _ffmpegPath, cancellationToken)
            .ConfigureAwait(false);
        var plan = FfmpegFilterGraphBuilder.Build(
            project.Timeline, project.MediaLibrary, project.Format.Width, project.Format.Height, project.Format.Fps,
            stabilization.TransformFiles);

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

            // Re-apply the user's overwrite decision at the atomic publish step. A file can appear at
            // this path while a long render is running; overwrite:false protects that new user file from
            // being silently destroyed when the user never approved replacement.
            File.Move(tempPath, settings.OutputFilePath, overwrite: settings.OverwriteConfirmed);
            job.Status = RenderJobStatus.Completed;
            job.ProgressPercent = 100;
            job.CompletedAt = DateTimeOffset.Now;
            return settings.OutputFilePath;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException) { }
        }
    }

    private static void ValidateSettings(RenderSettings settings)
    {
        var expectedExtension = settings.Format.Extension();
        var actualExtension = Path.GetExtension(settings.OutputFilePath);
        if (!string.Equals(actualExtension, expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Format {settings.Format} zahteva izlazni fajl sa ekstenzijom {expectedExtension}. " +
                $"Izabrana putanja ima „{actualExtension}“.");
        }

        if (!settings.Format.IsAudioOnly() && !settings.Format.SupportsVideoCodec(settings.Codec))
        {
            throw new InvalidOperationException(
                $"Kodek {settings.Codec} nije kompatibilan sa formatom {settings.Format}.");
        }
    }

    private async Task<(int ExitCode, string StdErr)> RunWithFallbackAsync(
        FfmpegRenderPlan plan, RenderJob job, string tempPath, CancellationToken cancellationToken)
    {
        var requestedCodec = job.Settings.Codec;
        var (exitCode, stdErr) = await RunOnceAsync(plan, requestedCodec, job, tempPath, cancellationToken).ConfigureAwait(false);

        var hardwareH264 = requestedCodec is VideoCodec.H264Nvenc or VideoCodec.H264Qsv or VideoCodec.H264Amf;
        if (exitCode != 0 && hardwareH264 && job.Settings.Format.SupportsVideoCodec(VideoCodec.Libx264) &&
            !cancellationToken.IsCancellationRequested)
        {
            (exitCode, stdErr) = await RunOnceAsync(plan, VideoCodec.Libx264, job, tempPath, cancellationToken).ConfigureAwait(false);
        }

        return (exitCode, stdErr);
    }

    private async Task<(int ExitCode, string StdErr)> RunOnceAsync(
        FfmpegRenderPlan plan, VideoCodec codec, RenderJob job, string tempPath, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = BuildStartInfo(plan, codec, job.Settings, tempPath) };
        job.FfmpegCommandLogged = $"{_ffmpegPath} {string.Join(' ', process.StartInfo.ArgumentList)}";

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException(
                "ffmpeg nije pronađen. Pokrenite Dijagnostiku i proverite FFmpeg putanju.", ex);
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
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch { }
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

        const string key = "out_time_ms=";
        if (!line.StartsWith(key, StringComparison.Ordinal))
        {
            return;
        }

        if (long.TryParse(line.AsSpan(key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
        {
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
        // The shared timeline graph always produces both final video and audio outputs. For audio-only
        // containers the video output still has to be consumed inside the graph or FFmpeg rejects the
        // entire graph as "output ... unconnected" before -vn can take effect. nullsink consumes that
        // final video branch without encoding or writing it, while the audio branch is mapped normally.
        var filterGraph = settings.Format.IsAudioOnly()
            ? $"{plan.FilterComplexArgument};{plan.VideoMapLabel}nullsink"
            : plan.FilterComplexArgument;
        startInfo.ArgumentList.Add(filterGraph);

        if (settings.Format.IsAudioOnly())
        {
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add(plan.AudioMapLabel);
            startInfo.ArgumentList.Add("-vn");
            AppendAudioCodecArguments(startInfo, settings);
        }
        else
        {
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add(plan.VideoMapLabel);
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add(plan.AudioMapLabel);

            startInfo.ArgumentList.Add("-c:v");
            startInfo.ArgumentList.Add(CodecName(codec));
            AppendVideoQualityArguments(startInfo, codec, settings);

            startInfo.ArgumentList.Add("-c:a");
            startInfo.ArgumentList.Add(settings.Format == ExportFormat.WebM ? "libopus" : "aac");
            startInfo.ArgumentList.Add("-b:a");
            startInfo.ArgumentList.Add($"{settings.AudioBitrateKbps}k");

            if (settings.Format is ExportFormat.Mp4 or ExportFormat.Mov)
            {
                startInfo.ArgumentList.Add("-movflags");
                startInfo.ArgumentList.Add("+faststart");
            }
        }

        startInfo.ArgumentList.Add("-progress");
        startInfo.ArgumentList.Add("pipe:1");
        startInfo.ArgumentList.Add(tempPath);

        return startInfo;
    }

    private static void AppendVideoQualityArguments(ProcessStartInfo startInfo, VideoCodec codec, RenderSettings settings)
    {
        switch (codec)
        {
            case VideoCodec.Libx264:
            case VideoCodec.Libx265:
                startInfo.ArgumentList.Add("-crf");
                startInfo.ArgumentList.Add(settings.Crf.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add("-preset");
                startInfo.ArgumentList.Add(settings.Preset);
                break;
            case VideoCodec.LibvpxVp9:
                startInfo.ArgumentList.Add("-crf");
                startInfo.ArgumentList.Add(settings.Crf.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add("-b:v");
                startInfo.ArgumentList.Add("0");
                break;
            case VideoCodec.LibaomAv1:
                startInfo.ArgumentList.Add("-crf");
                startInfo.ArgumentList.Add(settings.Crf.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add("-b:v");
                startInfo.ArgumentList.Add("0");
                startInfo.ArgumentList.Add("-cpu-used");
                startInfo.ArgumentList.Add("6");
                break;
        }
    }

    private static void AppendAudioCodecArguments(ProcessStartInfo startInfo, RenderSettings settings)
    {
        var codec = settings.Format switch
        {
            ExportFormat.M4a => "aac",
            ExportFormat.Mp3 => "libmp3lame",
            ExportFormat.Wav => "pcm_s16le",
            ExportFormat.Flac => "flac",
            _ => throw new InvalidOperationException($"{settings.Format} nije audio-only format.")
        };

        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add(codec);

        if (settings.Format is ExportFormat.M4a or ExportFormat.Mp3)
        {
            startInfo.ArgumentList.Add("-b:a");
            startInfo.ArgumentList.Add($"{settings.AudioBitrateKbps}k");
        }
    }

    private static string CodecName(VideoCodec codec) => codec switch
    {
        VideoCodec.H264Nvenc => "h264_nvenc",
        VideoCodec.H264Qsv => "h264_qsv",
        VideoCodec.H264Amf => "h264_amf",
        VideoCodec.Libx265 => "libx265",
        VideoCodec.LibvpxVp9 => "libvpx-vp9",
        VideoCodec.LibaomAv1 => "libaom-av1",
        _ => "libx264"
    };
}
