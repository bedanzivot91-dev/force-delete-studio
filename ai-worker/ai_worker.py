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
    check_engine("lyric_align", "lyric_align")
    emit({"type": "Done"})
    return 0


def _find_vocals(root: str) -> str | None:
    matches = list(Path(root).rglob("vocals.wav"))
    return str(matches[0]) if matches else None


def _separate_vocals(source: str, work_dir: str) -> str:
    """Use Demucs' two-stem mode. Never silently feed the full music mix to song ASR."""
    try:
        __import__("demucs")
    except ImportError:
        raise RuntimeError("Demucs nije instaliran; bez izdvojenog vokala rezultat pesme nije pouzdan.")

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
    raise RuntimeError(f"Demucs nije uspeo da izdvoji vokal ({detail}). Prepoznavanje je zaustavljeno da program ne bi upisao izmišljene reči.")


def _usable_word(text: str, probability: float) -> bool:
    """Reject music markers, common boilerplate and very uncertain tokens."""
    normalized = text.strip().lower().strip(".,!?;:-_()[]{}")
    blocked = {
        "muzika", "music", "applause", "aplauz", "instrumental", "instrumentalno",
        "hvala na gledanju", "subscribe", "titlovi", "subtitles"
    }
    return bool(normalized) and normalized not in blocked and probability >= 0.35


_SECTION_NAMES = {
    "intro", "verse", "strofa", "pre-chorus", "pre chorus", "pred-refren", "pred refren",
    "chorus", "refren", "post-chorus", "post chorus", "bridge", "most", "hook", "refrain",
    "outro", "instrumental", "instrumentalno",
}


def _is_structural_lyrics_tag(line: str) -> bool:
    """Skip only explicit section labels such as [Verse 2] or [Refren].

    Older code discarded *any* first ALL-CAPS line with four words or fewer because it guessed that
    the line was a song title. That can delete a perfectly valid lyric such as "JOŠ TE VOLIM". Casing
    is never metadata. Only an explicit bracketed structural tag is allowed to be omitted.
    """
    stripped = line.strip()
    if len(stripped) < 3 or not (stripped.startswith("[") and stripped.endswith("]")):
        return False
    inner = stripped[1:-1].strip().lower()
    if inner in _SECTION_NAMES:
        return True
    # Accept numbered section labels: [Verse 2], [Refren 3], [Bridge 1].
    parts = inner.rsplit(" ", 1)
    return len(parts) == 2 and parts[1].isdigit() and parts[0] in _SECTION_NAMES


def _verified_lyric_lines(verified: str) -> list[str]:
    """Return every user-supplied lyric line except explicit structural section labels.

    The exact spelling/casing/diacritics are intentionally preserved. Alignment is allowed to assign
    timing to these lines; it is never allowed to rewrite or silently drop them.
    """
    return [
        line.strip()
        for line in verified.splitlines()
        if line.strip() and not _is_structural_lyrics_tag(line)
    ]


def _lossless_verified_words(aligned, lyric_lines: list[str]) -> list[dict]:
    """Convert lyric-align output only when every verified lyric line has a real time range.

    A partial alignment is not a successful result. The previous 60% threshold could return a Done
    event while silently losing almost half of the user's verified lyrics. This helper deliberately
    fails the whole operation instead. The UI can then keep the original verified text and ask the
    user to retry/manual-align rather than pretending a partial transcript is complete.
    """
    missing: list[tuple[int, str]] = []
    for index, line in enumerate(lyric_lines):
        if index >= len(aligned):
            missing.append((index + 1, line))
            continue
        item = aligned[index]
        if item.start is None or item.end is None:
            missing.append((index + 1, line))

    if len(aligned) != len(lyric_lines) or missing:
        missing_indices = {number for number, _ in missing}
        if len(aligned) < len(lyric_lines):
            for index in range(len(aligned), len(lyric_lines)):
                missing_indices.add(index + 1)
        missing_preview = "; ".join(
            f"{number}: {lyric_lines[number - 1]}"
            for number in sorted(missing_indices)[:5]
        )
        extra = "" if len(missing_indices) <= 5 else f"; + još {len(missing_indices) - 5}"
        raise RuntimeError(
            f"Tačan tekst nije kompletno poravnat: {len(lyric_lines) - len(missing_indices)}/{len(lyric_lines)} redova ima timing. "
            f"Nedostaju redovi {missing_preview}{extra}. Nijedan red nije obrisan; pokušajte ponovo ili ručno podesite timing."
        )

    result: list[dict] = []
    for index, line in enumerate(lyric_lines):
        item = aligned[index]
        start = float(item.start)
        end = max(float(item.end), start + 0.25)
        result.append({
            # Preserve the user's exact verified text instead of trusting a library-normalized copy.
            "text": line,
            "start": start,
            "end": end,
            "confidence": float(item.score if item.matched else 0.25),
        })
    return result


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
                # Slow sung vowels are frequently cut as silence by VAD. With known lyrics we can
                # safely keep the full separated vocal and use fuzzy matching to locate each line.
                vad_filter=not bool(verified),
                vad_parameters={"min_silence_duration_ms": 500, "speech_pad_ms": 300},
                condition_on_previous_text=False,
                compression_ratio_threshold=2.2,
                log_prob_threshold=-0.8,
                no_speech_threshold=0.55,
                hallucination_silence_threshold=1.5,
                initial_prompt=verified or "Srpska pesma. Prepiši tačno otpevane stihove sa dijakriticima č ć š ž đ.",
            )

            words = []
            raw_parts = []
            aligner_segments = []
            for segment in segments:
                if not verified and (getattr(segment, "no_speech_prob", 0) > 0.55 or getattr(segment, "avg_logprob", 0) < -0.8):
                    continue
                raw_parts.append(segment.text.strip())
                segment_words = []
                for word in segment.words or []:
                    text = word.word.strip()
                    probability = float(word.probability or 0)
                    if ((not verified and not _usable_word(text, probability)) or not text or
                            word.start is None or word.end is None):
                        continue
                    words.append({
                        "text": text,
                        "start": float(word.start),
                        "end": max(float(word.end), float(word.start) + 0.05),
                        "confidence": probability,
                    })
                    segment_words.append({"word": text, "start": float(word.start), "end": float(word.end)})
                aligner_segments.append({
                    "start": float(segment.start), "end": float(segment.end),
                    "text": segment.text.strip(), "words": segment_words
                })

            if verified:
                try:
                    from lyric_align import Segment, align, interpolate_gaps
                except ImportError as ex:
                    raise RuntimeError(
                        "lyric-align nije instaliran. Ponovo pokrenite instalaciju AI alata u Podešavanjima."
                    ) from ex

                lyric_lines = _verified_lyric_lines(verified)
                if not lyric_lines:
                    raise RuntimeError("Provereni tekst ne sadrži nijedan stih za poravnanje.")
                aligned = align(
                    [Segment.from_dict(item) for item in aligner_segments],
                    lyric_lines, pairing="auto", karaoke=False
                )
                aligned = interpolate_gaps(aligned)
                words = _lossless_verified_words(aligned, lyric_lines)
                raw_parts = lyric_lines

            emit({"type": "Progress", "progressPercent": 95, "message": "Grupišem prepoznate reči u stihove..."})
            emit({"type": "Result", "words": words, "rawText": " ".join(raw_parts).strip()})
            result_name = "redova proverenog teksta" if verified else "reči"
            emit({"type": "Done", "message": f"Postavljeno {len(words)} {result_name}; jezik {info.language}."})
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
