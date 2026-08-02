using System.Diagnostics;
using System.Text.Json;
using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Wraps the yt-dlp CLI to fetch metadata and download audio from a YouTube URL. Only ever used for
/// content the user confirms is their own (Suno songs posted to their own channels) - the ownership
/// confirmation is a required parameter, not a UI afterthought, and the URL is restricted to YouTube
/// hosts so this can't turn into a general-purpose downloader for other people's videos.
/// </summary>
public sealed class YouTubeDownloadService : IYouTubeDownloadService
{
    private readonly string _ytDlpPath;
    private readonly string _ffmpegPath;

    public YouTubeDownloadService(string? ffmpegOverridePath = null, string? ytDlpOverridePath = null)
    {
        _ffmpegPath = FfmpegLocator.ResolveFfmpegPath(ffmpegOverridePath);
        _ytDlpPath = FfmpegLocator.ResolveYtDlpPath(ytDlpOverridePath);
    }

    public async Task<YouTubeVideoInfo> GetVideoInfoAsync(string url, CancellationToken cancellationToken = default)
    {
        YouTubeDownloadHelpers.ValidateYouTubeUrl(url);

        var (exitCode, stdOut, stdErr) = await RunYtDlpAsync(
            new[] { "--no-playlist", "--skip-download", "--dump-json", "--no-warnings", url },
            cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                ? $"Nije moguće učitati podatke o videu (kod {exitCode})."
                : stdErr.Trim());
        }

        using var doc = JsonDocument.Parse(stdOut);
        var root = doc.RootElement;

        var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "Nepoznat naslov" : "Nepoznat naslov";
        var uploader = root.TryGetProperty("uploader", out var uploaderEl) ? uploaderEl.GetString() ?? "" : "";
        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        var durationSeconds = root.TryGetProperty("duration", out var durationEl) && durationEl.ValueKind == JsonValueKind.Number
            ? durationEl.GetDouble()
            : 0;

        return new YouTubeVideoInfo
        {
            Title = title,
            Uploader = uploader,
            VideoId = id,
            Duration = TimeSpan.FromSeconds(durationSeconds)
        };
    }

    public async Task<string> DownloadAudioAsync(
        string url,
        string outputDirectory,
        bool confirmedOwnContent,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!confirmedOwnContent)
        {
            throw new InvalidOperationException(
                "Preuzimanje je dozvoljeno samo za sadržaj koji je vaš - potvrdite to pre preuzimanja.");
        }

        YouTubeDownloadHelpers.ValidateYouTubeUrl(url);
        Directory.CreateDirectory(outputDirectory);

        var info = await GetVideoInfoAsync(url, cancellationToken);
        progress?.Report($"Preuzimanje: {info.Title}...");

        var outputTemplate = Path.Combine(outputDirectory, "%(id)s.%(ext)s");
        var args = new List<string> { "--no-playlist", "--no-warnings", "-x", "--audio-format", "mp3", "--audio-quality", "0" };

        var ffmpegDirectory = Path.GetDirectoryName(Path.GetFullPath(_ffmpegPath));
        if (!string.IsNullOrEmpty(ffmpegDirectory) && Directory.Exists(ffmpegDirectory))
        {
            args.Add("--ffmpeg-location");
            args.Add(ffmpegDirectory);
        }

        args.Add("-o");
        args.Add(outputTemplate);
        args.Add(url);

        var (exitCode, _, stdErr) = await RunYtDlpAsync(args, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stdErr)
                ? $"Preuzimanje nije uspelo (kod {exitCode})."
                : stdErr.Trim());
        }

        var downloadedPath = Path.Combine(outputDirectory, $"{info.VideoId}.mp3");
        if (!File.Exists(downloadedPath))
        {
            throw new InvalidOperationException("Preuzimanje je prijavljeno kao uspešno, ali izlazni audio fajl nije pronađen.");
        }

        var finalPath = YouTubeDownloadHelpers.MakeUnique(
            Path.Combine(outputDirectory, $"{YouTubeDownloadHelpers.SanitizeFileName(info.Title)}.mp3"));
        File.Move(downloadedPath, finalPath);

        progress?.Report("Preuzimanje završeno.");
        return finalPath;
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunYtDlpAsync(
        IEnumerable<string> args, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException(
                "yt-dlp nije pronađen. Pokrenite scripts/check-dependencies.ps1 ili ga instalirajte i dodajte u PATH.", ex);
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdOutTask, await stdErrTask);
    }
}
