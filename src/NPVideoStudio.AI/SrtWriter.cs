using System.Text;

namespace NPVideoStudio.AI;

/// <summary>
/// Formats transcribed segments as a standard .srt subtitle file. Pure and network-free, kept separate
/// from <see cref="SubtitleGeneratorService"/> so the format itself can be unit tested directly.
/// </summary>
public static class SrtWriter
{
    public static string Write(IReadOnlyList<TranscribedSegment> segments)
    {
        var builder = new StringBuilder();
        var index = 1;

        foreach (var segment in segments)
        {
            var text = segment.Text.Trim();
            if (text.Length == 0 || segment.End <= segment.Start)
            {
                continue;
            }

            builder.Append(index).Append('\n');
            builder.Append(FormatTimestamp(segment.Start)).Append(" --> ").Append(FormatTimestamp(segment.End)).Append('\n');
            builder.Append(text).Append('\n');
            builder.Append('\n');
            index++;
        }

        return builder.ToString();
    }

    public static string FormatTimestamp(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2},{time.Milliseconds:D3}";
    }
}
