// Minimal stand-in for the real yt-dlp CLI: understands exactly the subset of arguments
// YouTubeDownloadService actually sends, so tests can exercise the real process-orchestration code
// (argument construction, JSON parsing, output file resolution) without a network call or the real tool.
var argsList = args.ToList();

string? GetOption(string name)
{
    var idx = argsList.IndexOf(name);
    return idx >= 0 && idx + 1 < argsList.Count ? argsList[idx + 1] : null;
}

var url = argsList.LastOrDefault(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase));
if (url is null)
{
    Console.Error.WriteLine("fake-yt-dlp: no URL argument found");
    return 1;
}

if (url.Contains("faildownload", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("fake-yt-dlp: simulated failure for test");
    return 1;
}

string ExtractId(string videoUrl)
{
    var uri = new Uri(videoUrl);
    var vParam = uri.Query.TrimStart('?').Split('&')
        .Select(p => p.Split('=', 2))
        .FirstOrDefault(kv => kv.Length == 2 && kv[0] == "v");
    if (vParam is not null)
    {
        return vParam[1];
    }

    return uri.Segments.LastOrDefault()?.Trim('/') ?? "unknown";
}

var id = ExtractId(url);

if (argsList.Contains("--dump-json"))
{
    Console.WriteLine($$"""{"id":"{{id}}","title":"Fake Test Song {{id}}","uploader":"Fake Channel","duration":12.5}""");
    return 0;
}

if (argsList.Contains("-x"))
{
    var outputTemplate = GetOption("-o");
    if (outputTemplate is null)
    {
        Console.Error.WriteLine("fake-yt-dlp: missing -o argument");
        return 1;
    }

    var outputPath = outputTemplate.Replace("%(id)s", id).Replace("%(ext)s", "mp3");
    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    File.WriteAllBytes(outputPath, "fake-mp3-content"u8.ToArray());
    Console.WriteLine($"[download] Destination: {outputPath}");
    return 0;
}

Console.Error.WriteLine("fake-yt-dlp: unrecognized invocation");
return 1;
