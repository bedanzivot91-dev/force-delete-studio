namespace NPVideoStudio.Core.Diagnostics;

public interface IDiagnosticsService
{
    Task<IReadOnlyList<DiagnosticCheckResult>> RunAllChecksAsync(CancellationToken cancellationToken = default);

    /// <summary>Attempts an automatic fix for a check that reported <see cref="DiagnosticCheckResult.CanAutoFix"/>. Returns the updated result.</summary>
    Task<DiagnosticCheckResult> TryAutoFixAsync(string checkName, CancellationToken cancellationToken = default);

    /// <summary>Builds a support package (zipped logs + system info) at the given destination path. Never includes project/media files.</summary>
    Task<string> CreateSupportPackageAsync(string destinationFolder, CancellationToken cancellationToken = default);
}
