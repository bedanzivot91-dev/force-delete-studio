namespace NPVideoStudio.Domain;

/// <summary>Application-wide settings, persisted locally. No telemetry, no remote sync.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.DarkCinematic;
    public string Language { get; set; } = "sr-Latn";

    public string ProjectsFolder { get; set; } = DefaultProjectsFolder();
    public string CacheFolder { get; set; } = DefaultCacheFolder();

    public int AutoSaveIntervalSeconds { get; set; } = 60;
    public bool AutoSaveEnabled { get; set; } = true;

    public string? FfmpegPath { get; set; }
    public string? FfprobePath { get; set; }
    public string? YtDlpPath { get; set; }

    public int LogRetentionDays { get; set; } = 30;
    public ToolUpdatePolicy ToolUpdatePolicy { get; set; } = ToolUpdatePolicy.NotifyOnly;
    public int ToolUpdateIntervalDays { get; set; } = 7;
    public DateTimeOffset? LastToolUpdateUtc { get; set; }

    public static string DefaultProjectsFolder()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "NP Video Studio", "Projects");
    }

    public static string DefaultCacheFolder()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "NP Video Studio", "Cache");
    }

    public static string AppDataRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "NP Video Studio");
    }

    public static string LogsFolder() => Path.Combine(AppDataRoot(), "Logs");

    public static string DatabasePath() => Path.Combine(AppDataRoot(), "npvideostudio.db");

    public static string AutoSaveFolder() => Path.Combine(AppDataRoot(), "AutoSave");

    public static string ModelsFolder() => Path.Combine(AppDataRoot(), "Models");

    /// <summary>Where the real-audio-video-preview render (workspace "Pravi pregled sa zvukom") writes
    /// its temporary output file - separate from <see cref="DefaultCacheFolder"/>/<see cref="CacheFolder"/>
    /// (proxy/thumbnail cache) since this is regenerated per-render, not a long-lived cache.</summary>
    public static string PreviewCacheFolder() => Path.Combine(AppDataRoot(), "PreviewCache");
}
