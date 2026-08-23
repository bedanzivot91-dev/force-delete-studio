using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

/// <summary>
/// Analyzes a video for existing on-screen content that captions shouldn't cover (spec Phase 7). See
/// <see cref="VideoLayoutAnalysisResult"/>'s doc comment for exactly what's real today (text via OCR)
/// vs. not yet implemented (face/logo/CTA detection).
/// </summary>
public interface IVideoLayoutAnalysisService
{
    /// <summary>True if the OCR engine this service shells out to is actually installed.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<VideoLayoutAnalysisResult> AnalyzeAsync(
        string videoFilePath, int sampleFrameCount = 5, CancellationToken cancellationToken = default);
}
