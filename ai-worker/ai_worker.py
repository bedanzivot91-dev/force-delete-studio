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
import os
import subprocess
import tempfile
from pathlib import Path

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


def _find_vocals(root: str) -> str | None:
    matches = list(Path(root).rglob("vocals.wav"))
    return str(matches[0]) if matches else None


def _separate_vocals(source: str, work_dir: str) -> str:
    """Use Demucs' supported two-stem mode. If separation fails, keep the original audio usable."""
    try:
        __import__("demucs")
    except ImportError:
        emit({"type": "Warning", "message": "Demucs nije instaliran; prepoznajem originalni miks bez izdvajanja vokala."})
        return source

    emit({"type": "Progress", "progressPercent": 5, "message": "Izdvajam pevanje od instrumentalne muzike (Demucs)..."})
    completed = subprocess.run(
        [sys.executable, "-m", "demucs", "--two-stems=vocals", "-n", "htdemucs", "--out", work_dir, source],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    vocals = _find_vocals(work_dir)
    if completed.returncode == 0 and vocals:
        emit({"type": "Progress", "progressPercent": 40, "message": "Vokal je izdvojen. Prepoznajem stihove..."})
        return vocals

    detail = completed.stderr.strip().splitlines()[-1] if completed.stderr.strip() else "nepoznata greška"
    emit({"type": "Warning", "message": f"Demucs nije uspeo ({detail}); nastavljam sa originalnim miksom."})
    return source


def run_transcription(request: dict, job_kind: str, profile: str) -> int:
    # Balanced/MostAccurate need faster-whisper at minimum; Demucs/WhisperX are used opportunistically
    # by those profiles for vocals-only ASR / forced alignment where installed. None are bundled or
    # auto-installed - see requirements.txt and docs/PHASE_STATUS.md for the honest current gap.
    try:
        from faster_whisper import WhisperModel
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

    source = request.get("audioFilePath")
    if not source or not os.path.isfile(source):
        emit({"type": "Error", "message": "Audio/video fajl za prepoznavanje ne postoji."})
        return 1

    model_name = "large-v3" if profile == "MostAccurate" else "medium"
    language = request.get("languageHint") or "sr"
    verified = (request.get("verifiedLyrics") or "").strip()

    try:
        with tempfile.TemporaryDirectory(prefix="npvs_song_") as work_dir:
            recognition_source = _separate_vocals(source, work_dir)
            emit({"type": "Progress", "progressPercent": 45, "message": f"Učitavam Whisper model {model_name}..."})

            # int8 works on ordinary Windows CPUs; CUDA users can opt into GPU through the environment
            # without making the application unusable on machines that do not have an NVIDIA card.
            device = os.environ.get("NPVS_WHISPER_DEVICE", "cpu")
            compute_type = "float16" if device == "cuda" else "int8"
            model = WhisperModel(model_name, device=device, compute_type=compute_type)
            segments, info = model.transcribe(
                recognition_source,
                language=language,
                task="transcribe",
                beam_size=5,
                best_of=5,
                temperature=0,
                word_timestamps=True,
                # Speech VAD often removes sustained sung vowels. Demucs has already reduced the
                # accompaniment, so preserving the complete vocal is the safer choice for lyrics.
                vad_filter=False,
                condition_on_previous_text=True,
                initial_prompt=verified or "Srpska pesma. Prepiši tačno otpevane stihove sa dijakriticima č ć š ž đ.",
            )

            words = []
            raw_parts = []
            for segment in segments:
                raw_parts.append(segment.text.strip())
                for word in segment.words or []:
                    text = word.word.strip()
                    if not text or word.start is None or word.end is None:
                        continue
                    words.append({
                        "text": text,
                        "start": float(word.start),
                        "end": max(float(word.end), float(word.start) + 0.05),
                        "confidence": float(word.probability or 0),
                    })

            emit({"type": "Progress", "progressPercent": 95, "message": "Grupišem prepoznate reči u stihove..."})
            emit({"type": "Result", "words": words, "rawText": " ".join(raw_parts).strip()})
            emit({"type": "Done", "message": f"Prepoznato {len(words)} reči; jezik {info.language}."})
            return 0
    except Exception as ex:
        emit({"type": "Error", "message": f"Prepoznavanje stihova nije uspelo: {ex}"})
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
        return run_transcription(request, job_kind, profile)

    emit({"type": "Error", "message": f"Nepoznat tip posla: {job_kind!r}"})
    return 1


if __name__ == "__main__":
    sys.exit(main())
