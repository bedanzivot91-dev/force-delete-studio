namespace NPVideoStudio.Media;

/// <summary>
/// Pure, network-free logic used by <see cref="YouTubeDownloadService"/> - kept separate so it can be
/// unit tested directly without needing yt-dlp installed or a real network connection.
/// </summary>
public static class YouTubeDownloadHelpers
{
    private static readonly string[] AllowedHosts =
    {
        "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be"
    };

    public static bool IsYouTubeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        AllowedHosts.Contains(uri.Host.ToLowerInvariant());

    public static void ValidateYouTubeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Unesite ispravan YouTube link.", nameof(url));
        }

        if (!AllowedHosts.Contains(uri.Host.ToLowerInvariant()))
        {
            throw new ArgumentException("Ovaj alat radi samo sa YouTube linkovima - za sadržaj koji je vaš.", nameof(url));
        }
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "preuzeta_pesma" : cleaned;
    }

    public static string MakeUnique(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        var i = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            i++;
        } while (File.Exists(candidate));

        return candidate;
    }
}
