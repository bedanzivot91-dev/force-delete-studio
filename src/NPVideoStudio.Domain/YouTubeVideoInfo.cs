namespace NPVideoStudio.Domain;

/// <summary>Metadata read back from yt-dlp before a download, so the user can confirm it's their own video.</summary>
public sealed class YouTubeVideoInfo
{
    public required string Title { get; init; }
    public required string Uploader { get; init; }
    public required string VideoId { get; init; }
    public required TimeSpan Duration { get; init; }
}
