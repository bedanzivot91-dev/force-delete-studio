namespace NPVideoStudio.Core.Diagnostics;

public enum DiagnosticStatus
{
    Ok,
    Warning,
    Error
}

/// <summary>Result of a single diagnostic check, written so a non-technical user can understand and act on it (see spec §42).</summary>
public sealed class DiagnosticCheckResult
{
    public required string CheckName { get; init; }
    public DiagnosticStatus Status { get; init; }

    /// <summary>What is wrong, in plain language.</summary>
    public required string Description { get; init; }

    /// <summary>Why it matters.</summary>
    public string? WhyItMatters { get; init; }

    /// <summary>How the user can fix it.</summary>
    public string? HowToFix { get; init; }

    /// <summary>Raw technical detail (paths, versions, exceptions) the user can copy for a support request.</summary>
    public string? TechnicalDetails { get; init; }

    public bool CanAutoFix { get; init; }
}
