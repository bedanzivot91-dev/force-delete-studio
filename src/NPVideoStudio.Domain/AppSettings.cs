namespace NPVideoStudio.Domain;

/// <summary>Application-wide settings, persisted locally. No telemetry, no remote sync.</summary>
public sealed class AppSettings
{
    private static readonly object RuntimePathLock = new();
    private static string? _runtimeCacheFolder;

    public AppTheme Theme { get; set; } = AppTheme.Studio2026;
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

    /// <summary>
    /// Updates the process-wide cache root after persisted settings are loaded or saved. Centralising this
    /// here prevents proxy generation, proxy-folder navigation and ownership cleanup from silently using
    /// different roots after the user changes Settings -> Cache folder. An invalid manually typed path is
    /// never allowed to crash Settings save/startup; in that case the safe factory cache root is used.
    /// </summary>
    public static void ConfigureRuntimeCacheFolder(string? cacheFolder)
    {
        var normalized = DefaultCacheFolder();
        if (!string.IsNullOrWhiteSpace(cacheFolder))
        {
            try
            {
                normalized = Path.GetFullPath(cacheFolder.Trim());
            }
            catch (ArgumentException)
            {
                normalized = DefaultCacheFolder();
            }
            catch (NotSupportedException)
            {
                normalized = DefaultCacheFolder();
            }
            catch (PathTooLongException)
            {
                normalized = DefaultCacheFolder();
            }
        }

        lock (RuntimePathLock)
        {
            _runtimeCacheFolder = normalized;
        }
    }

    public static string ActiveCacheFolder()
    {
        lock (RuntimePathLock)
        {
            return string.IsNullOrWhiteSpace(_runtimeCacheFolder) ? DefaultCacheFolder() : _runtimeCacheFolder;
        }
    }

    /// <summary>App-owned lower-resolution media proxies. Proxies are disposable cache data and never
    /// replace the original source path stored in a project. This follows the active user-selected cache
    /// root rather than always falling back to the factory default.</summary>
    public static string ProxyCacheFolder() => Path.Combine(ActiveCacheFolder(), "Proxies");

    /// <summary>Where the real-audio-video-preview render (workspace "Pravi pregled sa zvukom") writes
    /// its temporary output file - separate from the configurable long-lived cache because this is
    /// regenerated per render.</summary>
    public static string PreviewCacheFolder() => Path.Combine(AppDataRoot(), "PreviewCache");
}
