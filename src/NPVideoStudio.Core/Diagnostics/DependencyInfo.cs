namespace NPVideoStudio.Core.Diagnostics;

public enum DependencyStatus
{
    Installed,
    NotInstalled
}

/// <summary>
/// Status of one external tool/model the app depends on, for the "Alati i modeli" screen. Deliberately
/// only tracks what can actually be checked today (found + version via a real version-command exit
/// code) - no "Oštećeno"/"Nekompatibilno"/"Ažuriranje dostupno" states, since there is no checksum or
/// expected-version pinning system yet to back those up honestly.
/// </summary>
public sealed class DependencyInfo
{
    public required string Name { get; init; }
    public required DependencyStatus Status { get; init; }
    public string? Version { get; init; }
    public string? Path { get; init; }
    public required string WhyItMatters { get; init; }
    public bool CanDownload { get; init; }
    public bool CanOpenFolder { get; init; }
    public string? TechnicalDetails { get; init; }
}
