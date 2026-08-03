#!/usr/bin/env python3
"""NP Video Studio local AI worker (Phase 5).

Protocol: one JSON request file (path given via --request), one JSONL event per line on stdout,
protocol_version 1. No HTTP server, no audio bytes over stdin/stdout - only file paths.

Heavier engines (faster-whisper, WhisperX, Demucs) are optional local dependencies. This script never
pretends they are installed: CapabilityCheck honestly reports what's importable right now, and any job
that needs an engine which isn't installed fails with a clear error event instead of a fabricated
result. Whisper.net (the .NET side) is the always-available offline fallback and does not go through
this worker at all.

Install the optional engines with: pip install -r requirements.txt
"""
import argparse
import json
import sys
import platform

# Force UTF-8 stdout/stderr regardless of the OS locale/codepage. Matters most on Windows: a redirected
# (piped) stream there defaults to the locale's codepage, not UTF-8, which silently mangles Serbian
# č/ć/š/ž/đ in emitted JSON. AiWorkerClient also sets PYTHONIOENCODING=utf-8 when launching this script
# and decodes with a matching encoding on its side - this reconfigure is the belt to that suspenders,
# so the script is still correct if ever invoked without that env var.
sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")

PROTOCOL_VERSION = 1


def emit(event: dict) -> None:
    event.setdefault("protocolVersion", PROTOCOL_VERSION)
    print(json.dumps(event, ensure_ascii=False), flush=True)


def check_engine(module_name: str, event_name: str) -> bool:
    try:
        __import__(module_name)
        emit({"type": "CapabilityCheck", "engine": event_name, "engineAvailable": True})
        return True
    except ImportError as ex:
        emit({"type": "CapabilityCheck", "engine": event_name, "engineAvailable": False, "message": str(ex)})
        return False


def run_capability_check() -> int:
    emit({
        "type": "CapabilityCheck",
        "engine": "python",
        "engineAvailable": True,
        "message": platform.python_version(),
    })
    check_engine("faster_whisper", "faster_whisper")
    check_engine("whisperx", "whisperx")
    check_engine("demucs", "demucs")
    emit({"type": "Done"})
    return 0


def run_transcription(job_kind: str, profile: str) -> int:
    # Balanced/MostAccurate need faster-whisper at minimum; Demucs/WhisperX are used opportunistically
    # by those profiles for vocals-only ASR / forced alignment where installed. None are bundled or
    # auto-installed - see requirements.txt and docs/PHASE_STATUS.md for the honest current gap.
    try:
        import faster_whisper  # noqa: F401
    except ImportError:
        emit({
            "type": "Error",
            "message": (
                "faster-whisper nije instaliran u Python okruženju AI worker-a. "
                "Instalirajte ga sa 'pip install -r ai-worker/requirements.txt' ili koristite "
                "profil 'Fast' (Whisper.net), koji radi bez ove instalacije."
            ),
        })
        return 1

    # Real faster-whisper/Demucs/WhisperX orchestration is not implemented yet (spec Phase 5 tracks
    # this as the next real gap once the above import succeeds in a real deployment) - emitting a
    # fabricated transcript here would violate the "never guess" rule the rest of this codebase follows.
    emit({
        "type": "Error",
        "message": f"Obrada profila '{profile}' za '{job_kind}' još nije implementirana u AI worker-u.",
    })
    return 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--request", required=True, help="Path to the JSON request file")
    args = parser.parse_args()

    try:
        with open(args.request, "r", encoding="utf-8") as f:
            request = json.load(f)
    except (OSError, json.JSONDecodeError) as ex:
        emit({"type": "Error", "message": f"Ne mogu da pročitam zahtev: {ex}"})
        return 1

    if request.get("protocolVersion") != PROTOCOL_VERSION:
        emit({
            "type": "Error",
            "message": f"Nepodržana verzija protokola: {request.get('protocolVersion')!r} (očekivano {PROTOCOL_VERSION}).",
        })
        return 1

    job_kind = request.get("jobKind")
    profile = request.get("profile", "Fast")

    if job_kind == "CapabilityCheck":
        return run_capability_check()
    if job_kind in ("KnownSongAlignment", "UnknownSongTranscription"):
        return run_transcription(job_kind, profile)

    emit({"type": "Error", "message": f"Nepoznat tip posla: {job_kind!r}"})
    return 1


if __name__ == "__main__":
    sys.exit(main())
