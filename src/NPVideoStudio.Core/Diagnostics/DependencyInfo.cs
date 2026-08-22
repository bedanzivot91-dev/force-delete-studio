namespace NPVideoStudio.Core.Diagnostics;

/// <summary>
/// Health state for one external dependency. UpdateAvailable is intentionally only used when an
/// authoritative newer version is known; NP never guesses that state from age or a failed command.
/// Checking/Downloading are transient UI states and are included in the shared vocabulary so the
/// dependency screen does not need a second incompatible status model.
/// </summary>
public enum DependencyStatus
{
    Installed,
    NotInstalled,
    UpdateAvailable,
    Corrupt,
    Incompatible,
    Checking,
    Downloading
}

/// <summary>
/// Evidence-backed status of one external tool/model used by the "Alati i modeli" screen. A green
/// Installed state requires the real executable/module check to succeed. Corrupt means a concrete file
/// exists but cannot execute its version/capability check. Incompatible means it executes but is below a
/// version floor required by the NP features that consume it.
/// </summary>
public sealed class DependencyInfo
{
    public required string Name { get; init; }
    public required DependencyStatus Status { get; init; }
    public string? Version { get; init; }
    public string? ExpectedVersion { get; init; }
    public string? Path { get; init; }
    public string? Sha256 { get; init; }
    public string? License { get; init; }
    public DateTimeOffset LastCheckedUtc { get; init; } = DateTimeOffset.UtcNow;
    public required string WhyItMatters { get; init; }
    public bool CanDownload { get; init; }
    public bool CanRepair { get; init; }
    public bool CanOpenFolder { get; init; }
    public string? TechnicalDetails { get; init; }

    public bool IsUsable => Status is DependencyStatus.Installed or DependencyStatus.UpdateAvailable;
    public bool NeedsAttention => !IsUsable && Status is not DependencyStatus.Checking and not DependencyStatus.Downloading;
}
