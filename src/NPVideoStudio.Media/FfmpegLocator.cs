using System.Diagnostics;

namespace NPVideoStudio.Media;

/// <summary>
/// Finds the ffmpeg/ffprobe executables to use: an explicit override, then a copy bundled next to the
/// app (Tools/ffmpeg), then whatever is on PATH. Never assumes a hardcoded absolute path (spec §51).
/// </summary>
public static class FfmpegLocator
{
    public static string ResolveFfprobePath(string? overridePath) => Resolve(overridePath, "ffprobe", "ffmpeg");

    public static string ResolveFfmpegPath(string? overridePath) => Resolve(overridePath, "ffmpeg", "ffmpeg");

    public static string ResolveYtDlpPath(string? overridePath) => Resolve(overridePath, "yt-dlp", "yt-dlp");

    public static string ResolveFpcalcPath(string? overridePath) => Resolve(overridePath, "fpcalc", "fpcalc");

    public static string ResolveTesseractPath(string? overridePath) => Resolve(overridePath, "tesseract", "tesseract");

    private static string Resolve(string? overridePath, string toolName, string bundledSubfolder)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var exeName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;

        var bundled = Path.Combine(AppContext.BaseDirectory, "Tools", bundledSubfolder, exeName);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        // Fall back to PATH; the actual executable name is resolved by the OS at process start.
        return exeName;
    }

    public static Task<(bool Found, string? Version)> TryGetVersionAsync(string executablePath, CancellationToken cancellationToken = default)
        => TryGetVersionAsync(executablePath, "-version", cancellationToken);

    public static async Task<(bool Found, string? Version)> TryGetVersionAsync(string executablePath, string versionArgument, CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = versionArgument,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return process.ExitCode == 0 && output is not null
                ? (true, output)
                : (false, null);
        }
        catch (Exception)
        {
            return (false, null);
        }
    }
}
