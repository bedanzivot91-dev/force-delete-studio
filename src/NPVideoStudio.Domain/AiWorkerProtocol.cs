namespace NPVideoStudio.Domain;

/// <summary>
/// The three processing profiles the AI worker offers, trading speed for accuracy (spec Phase 5):
/// Fast keeps using the existing Whisper.net path, Balanced/MostAccurate ask the local Python worker
/// for faster-whisper/Demucs/WhisperX. Whisper.net is never removed - it stays the offline/low-end
/// fallback regardless of which profile is selected.
/// </summary>
public enum AiProcessingProfile
{
    Fast,
    Balanced,
    MostAccurate
}

/// <summary>What the worker is being asked to do for one request.</summary>
public enum AiWorkerJobKind
{
    /// <summary>No audio job at all - just report which optional engines are importable right now.</summary>
    CapabilityCheck,

    /// <summary>Song already recognized in the library: align its verified lyrics to this clip, don't guess new text.</summary>
    KnownSongAlignment,

    /// <summary>Song not recognized: transcribe from scratch (raw audio, then vocals-only if available).</summary>
    UnknownSongTranscription
}

/// <summary>
/// One request written to a temp JSON file and passed to the worker as a file path argument - never as
/// audio bytes through JSON (spec: "no binary audio through JSON (temp files + absolute paths)").
/// </summary>
public sealed class AiWorkerRequest
{
    public const int CurrentProtocolVersion = 1;

    public int ProtocolVersion { get; init; } = CurrentProtocolVersion;
    public required AiWorkerJobKind JobKind { get; init; }
    public required AiProcessingProfile Profile { get; init; }

    /// <summary>Absolute path. Required for the two transcription job kinds, unused for CapabilityCheck.</summary>
    public string? AudioFilePath { get; init; }

    /// <summary>Verified lyrics text from the song library - required for KnownSongAlignment only.</summary>
    public string? VerifiedLyrics { get; init; }

    /// <summary>BCP-47-ish hint (e.g. "sr"), or null to let the worker auto-detect.</summary>
    public string? LanguageHint { get; init; }
}

/// <summary>One line of the worker's JSONL event stream.</summary>
public enum AiWorkerEventType
{
    CapabilityCheck,
    Progress,
    Warning,
    PartialResult,
    Result,
    Error,
    Done
}

/// <summary>
/// One recognized/aligned word. Deliberately minimal (no verification-status/source enum, no editor
/// concerns) - Phase 6 owns the full word-level caption data model and editor; this is just what the
/// worker protocol carries across the process boundary.
/// </summary>
public sealed class AiWorkerWord
{
    public required string Text { get; init; }
    public required TimeSpan Start { get; init; }
    public required TimeSpan End { get; init; }
    public double Confidence { get; init; }
}

/// <summary>
/// One JSONL line from the worker. Fields not relevant to <see cref="Type"/> stay null - this is a
/// tagged union flattened into one class for simple System.Text.Json round-tripping, same shape the
/// worker's Python side emits field-for-field.
/// </summary>
public sealed class AiWorkerEvent
{
    public int ProtocolVersion { get; init; } = AiWorkerRequest.CurrentProtocolVersion;
    public required AiWorkerEventType Type { get; init; }
    public string? Message { get; init; }
    public double? ProgressPercent { get; init; }

    /// <summary>Set on CapabilityCheck events: which engine ("faster_whisper"/"whisperx"/"demucs") this line describes.</summary>
    public string? Engine { get; init; }
    public bool? EngineAvailable { get; init; }

    public IReadOnlyList<AiWorkerWord>? Words { get; init; }
    public string? RawText { get; init; }
}

/// <summary>
/// Aggregated result of a CapabilityCheck run, for the "Alati i modeli" screen - honest per-engine
/// status, never a single "AI available" boolean, since each engine is optional independently.
/// </summary>
public sealed class AiWorkerCapabilities
{
    public bool WorkerReachable { get; init; }
    public string? PythonVersion { get; init; }
    public bool FasterWhisperAvailable { get; init; }
    public bool WhisperXAvailable { get; init; }
    public bool DemucsAvailable { get; init; }
    public string? Error { get; init; }
}
