using NPVideoStudio.Core.Services;
using NPVideoStudio.Domain;

namespace NPVideoStudio.Media;

/// <summary>
/// Prefers <see cref="EasyOcrVideoLayoutAnalysisService"/> when it's actually installed (better on
/// stylized/decorative on-screen text - see that class's doc comment for the real, verified evidence),
/// falls back to <see cref="TesseractOcrService"/> otherwise - Tesseract is the lighter dependency and
/// stays available with nothing extra to install, so a machine without the optional EasyOCR/Python
/// setup still gets working OCR, just less accurate on decorative fonts.
/// </summary>
public sealed class CompositeVideoLayoutAnalysisService : IVideoLayoutAnalysisService
{
    private readonly IVideoLayoutAnalysisService _preferred;
    private readonly IVideoLayoutAnalysisService _fallback;
    private bool? _preferredAvailable;

    public CompositeVideoLayoutAnalysisService(IVideoLayoutAnalysisService preferred, IVideoLayoutAnalysisService fallback)
    {
        _preferred = preferred;
        _fallback = fallback;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (await _preferred.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return await _fallback.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<VideoLayoutAnalysisResult> AnalyzeAsync(
        string videoFilePath, int sampleFrameCount = 5, CancellationToken cancellationToken = default)
    {
        _preferredAvailable ??= await _preferred.IsAvailableAsync(cancellationToken).ConfigureAwait(false);

        if (_preferredAvailable == true)
        {
            try
            {
                return await _preferred.AnalyzeAsync(videoFilePath, sampleFrameCount, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The availability probe only checks that EasyOCR is importable, not that this exact
                // run will succeed (a corrupt model cache, an OOM on a huge frame, etc.) - Tesseract is
                // the safety net so one bad run doesn't take the whole feature down.
            }
        }

        return await _fallback.AnalyzeAsync(videoFilePath, sampleFrameCount, cancellationToken).ConfigureAwait(false);
    }
}
