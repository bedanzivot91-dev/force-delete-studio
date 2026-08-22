namespace NPVideoStudio.Core.Services;

/// <summary>
/// Generates a lower-resolution proxy file for smooth playback of codecs that don't decode well in
/// real time (spec Phase 8: "Auto-proxy (720p or configurable) ... keep link to original, never render
/// the proxy as final"). The original file is never modified or deleted.
/// </summary>
public interface IProxyGeneratorService
{
    Task<string> GenerateProxyAsync(
        string sourceFilePath, string outputFilePath, int targetHeight = 720, CancellationToken cancellationToken = default);
}
