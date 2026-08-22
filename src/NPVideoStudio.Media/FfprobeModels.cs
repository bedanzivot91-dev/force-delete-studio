using System.Text.Json.Serialization;

namespace NPVideoStudio.Media;

internal sealed class FfprobeOutput
{
    [JsonPropertyName("streams")]
    public List<FfprobeStream> Streams { get; set; } = new();

    [JsonPropertyName("format")]
    public FfprobeFormat? Format { get; set; }
}

internal sealed class FfprobeStream
{
    [JsonPropertyName("codec_type")]
    public string? CodecType { get; set; }

    [JsonPropertyName("codec_name")]
    public string? CodecName { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("r_frame_rate")]
    public string? RFrameRate { get; set; }

    [JsonPropertyName("avg_frame_rate")]
    public string? AvgFrameRate { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }
}

internal sealed class FfprobeFormat
{
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("size")]
    public string? Size { get; set; }

    [JsonPropertyName("format_name")]
    public string? FormatName { get; set; }
}
