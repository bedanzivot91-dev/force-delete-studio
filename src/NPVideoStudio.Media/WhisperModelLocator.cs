namespace NPVideoStudio.Media;

/// <summary>
/// Finds the Whisper tiny model file to use: an explicit override, then a copy bundled next to the app
/// (Tools/whisper-models - see build-release.ps1, same convention as <see cref="FfmpegLocator"/>), then
/// the per-user AppData location the app downloads to after explicit user consent (spec: never download
/// without asking). Bundling it lets a build made with real internet access skip that consent click
/// entirely for whoever installs it; a build made without internet (or a user who deletes/replaces the
/// bundled copy) still falls back to the exact same on-demand download flow as before this existed.
/// </summary>
public static class WhisperModelLocator
{
    public static string ResolveModelPath(string? overridePath, string defaultAppDataPath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "Tools", "whisper-models", "ggml-tiny.bin");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        return defaultAppDataPath;
    }
}
