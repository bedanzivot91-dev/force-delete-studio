using System.Diagnostics;
using System.Globalization;
using NPVideoStudio.Core.Services;

namespace NPVideoStudio.Media;

/// <summary>
/// Real ffmpeg-based implementation (spec Phase 10): `-loop 1` on the image input + `-shortest` produces
/// exactly a song-length video - verified empirically (not assumed) against real ffmpeg before writing
/// this: a 5s synthetic song + a still image produced exactly a 5.0s 1280x720 output. Caption burn-in
/// uses ffmpeg's own `subtitles` filter directly against a real .srt file rather than re-implementing
/// per-word `drawtext` burning like <see cref="FfmpegFilterGraphBuilder"/> does for the timeline - simpler
/// and equally verified (OCR at the exact caption window matched, and nowhere else).
/// </summary>
public sealed class QuickVideoService : IQuickVideoService
{
    private readonly string _ffmpegPath;

    public QuickVideoService(string? ffmpegOverridePath = null)
    {
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
    }

    public async Task<string> CreateAsync(
        string imageFilePath, string songFilePath, double songDurationSeconds, string outputFilePath,
        bool overwriteConfirmed, string? subtitleSrtPath = null, int width = 1920, int height = 1080,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (File.Exists(outputFilePath) && !overwriteConfirmed)
        {
            throw new InvalidOperationException(
                "Izlazni fajl već postoji - potrebna je potvrda za prepisivanje pre pokretanja izvoza.");
        }

        var directory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var outputExtension = Path.GetExtension(outputFilePath);
        var outputWithoutExtension = outputFilePath[..^outputExtension.Length];
        var tempPath = $"{outputWithoutExtension}.{Guid.NewGuid():N}{outputExtension}";

        using var process = new Process { StartInfo = BuildStartInfo(imageFilePath, songFilePath, subtitleSrtPath, width, height, tempPath) };

        try
        {
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
            string stdErr;

            try
            {
                while (true)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    ReportProgressLine(line, songDurationSeconds, progress);
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                stdErr = await stdErrTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        // Without waiting for the real exit, the temp output file's handle can still be
                        // open when cleanup below tries to delete it (a real race hit on Windows CI while
                        // building the very similar RenderService - not reproducible on Linux).
                        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch { /* best-effort on cancellation */ }
                }

                throw;
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(stdErr) ? $"ffmpeg nije uspeo (kod {process.ExitCode})." : stdErr.Trim());
            }

            if (!File.Exists(tempPath))
            {
                throw new InvalidOperationException("ffmpeg je prijavio uspeh, ali izlazni fajl nije pronađen.");
            }

            File.Move(tempPath, outputFilePath, overwrite: true);
            progress?.Report(100);
            return outputFilePath;
        }
        finally
        {
            // Best-effort: a cleanup failure here must never replace/mask whatever exception is already
            // propagating out of the try block above.
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

    private ProcessStartInfo BuildStartInfo(string imagePath, string songPath, string? subtitleSrtPath, int width, int height, string tempPath)
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
        startInfo.ArgumentList.Add("-loop");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(imagePath);
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(songPath);

        var filter = FormattableString.Invariant(
            $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1");
        if (!string.IsNullOrEmpty(subtitleSrtPath))
        {
            // Running with the .srt's own directory as CWD and passing just its bare filename sidesteps
            // the subtitles filter's own path-escaping entirely - no drive-letter colon, no backslashes,
            // nothing that needs escaping at all. Real, reproducible CI failure (Windows-only, could not
            // be caught in this Linux sandbox since there are no drive letters to trigger it): the
            // documented `C\:/path` colon-escaping approach this replaced made ffmpeg's own filter-option
            // parser misread the path and throw "Unable to parse 'original_size' ... as image size".
            startInfo.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(subtitleSrtPath));
            filter += $",subtitles={EscapeSubtitlesFilterPath(Path.GetFileName(subtitleSrtPath))}";
        }

        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add(filter);
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("libx264");
        startInfo.ArgumentList.Add("-tune");
        startInfo.ArgumentList.Add("stillimage");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("yuv420p");
        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("aac");
        startInfo.ArgumentList.Add("-b:a");
        startInfo.ArgumentList.Add("192k");
        startInfo.ArgumentList.Add("-shortest");
        startInfo.ArgumentList.Add("-movflags");
        startInfo.ArgumentList.Add("+faststart");
        startInfo.ArgumentList.Add("-progress");
        startInfo.ArgumentList.Add("pipe:1");
        startInfo.ArgumentList.Add(tempPath);

        return startInfo;
    }

    /// <summary>
    /// Defensive escaping for whatever ends up in the `subtitles=` filter value - <see cref="BuildStartInfo"/>
    /// avoids ever needing this in practice by running with the .srt's own directory as the working
    /// directory and passing only its bare filename (no drive letter, no path separators), after a real
    /// CI-only failure: a colon-escaped absolute Windows path (`C\:/Users/...`) - the documented ffmpeg
    /// wiki approach, not reproducible in this Linux sandbox since there are no drive letters - made
    /// ffmpeg's own filter-option parser misread the path and throw "Unable to parse 'original_size' ...
    /// as image size" instead of finding the file.
    /// </summary>
    public static string EscapeSubtitlesFilterPath(string path) =>
        path.Replace('\\', '/').Replace(":", "\\:");

    private static void ReportProgressLine(string line, double totalDurationSeconds, IProgress<double>? progress)
    {
        if (progress is null || totalDurationSeconds <= 0)
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
            progress.Report(Math.Clamp(elapsedSeconds / totalDurationSeconds * 100.0, 0, 100));
        }
    }
}
