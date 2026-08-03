using NPVideoStudio.Domain;

namespace NPVideoStudio.Core.Services;

/// <summary>
/// Orchestrates the local Python AI worker subprocess (spec Phase 5): JSON request file in, JSONL
/// events out, versioned protocol, never over HTTP. Whisper.net (<see cref="ILyricSearchService"/> /
/// <see cref="ISubtitleGeneratorService"/>) remains the separate lightweight/offline fallback and is
/// not routed through this interface.
/// </summary>
public interface IAiWorkerClient
{
    /// <summary>
    /// Never throws - reports whatever is actually importable right now (python missing, worker script
    /// missing, or any subset of faster-whisper/WhisperX/Demucs), for the "Alati i modeli" screen.
    /// </summary>
    Task<AiWorkerCapabilities> CheckCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs one job and streams its events as they arrive. Cancelling kills the worker process.
    /// Throws <see cref="InvalidOperationException"/> if the process can't be started at all.
    /// </summary>
    IAsyncEnumerable<AiWorkerEvent> RunAsync(AiWorkerRequest request, CancellationToken cancellationToken = default);
}
