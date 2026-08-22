from __future__ import annotations

import base64
import csv
import ctypes
import difflib
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
import time
import urllib.parse
import urllib.request
import zipfile
from ctypes import wintypes
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Iterable

import math
from array import array

from audio_tools import ensure_ffmpeg, ffmpeg_path, ffprobe_path, probe_audio
from suno_client import format_lrc, format_srt, format_vtt, sanitize_filename


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def sha256_file(path: Path, chunk_size: int = 1024 * 1024) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        while True:
            chunk = fh.read(chunk_size)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()



def unique_timestamp_path(folder: Path, prefix: str, suffix: str) -> Path:
    """Return a collision-free timestamped path, even for two operations in the same microsecond."""
    folder = folder.expanduser().resolve()
    folder.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    candidate = folder / f"{prefix}{stamp}{suffix}"
    counter = 1
    while candidate.exists():
        candidate = folder / f"{prefix}{stamp}-{counter}{suffix}"
        counter += 1
    return candidate


def iter_song_files(song: dict[str, Any]) -> Iterable[tuple[str, Path]]:
    for field in ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"):
        raw = str(song.get(field) or "").strip()
        if raw:
            yield field, Path(raw)
    for item in song.get("derived_files") or []:
        raw = str(item.get("path") or "").strip()
        if raw:
            yield f"derived:{item.get('id')}", Path(raw)


def create_incremental_backup(db: Any, target_root: Path, *, include_files: bool = True, progress: Callable[[str, int, int], None] | None = None) -> dict[str, Any]:
    target_root = target_root.expanduser().resolve()
    target_root.mkdir(parents=True, exist_ok=True)
    objects = target_root / "objects"
    snapshots = target_root / "snapshots"
    objects.mkdir(exist_ok=True)
    snapshots.mkdir(exist_ok=True)
    snapshot_dir = unique_timestamp_path(snapshots, "", "")
    backup_id = snapshot_dir.name
    snapshot_dir.mkdir(parents=True, exist_ok=False)
    db_snapshot = snapshot_dir / "suno_biblioteka.db"
    db.backup_to(db_snapshot)
    songs = db.export_rows()
    manifest: dict[str, Any] = {
        "format": "suno-pesme-incremental-v1",
        "backup_id": backup_id,
        "created_at": now_iso(),
        "database": "suno_biblioteka.db",
        "items": [],
    }
    candidates: list[tuple[str, str, Path]] = []
    for song in songs:
        full = db.get_song(str(song.get("id") or "")) or song
        for field, path in iter_song_files(full):
            if path.exists() and path.is_file():
                candidates.append((str(song.get("id") or ""), field, path))
    total = len(candidates)
    copied = 0
    reused = 0
    bytes_new = 0
    if include_files:
        for idx, (song_id, field, path) in enumerate(candidates, 1):
            digest = sha256_file(path)
            object_path = objects / digest[:2] / digest
            object_path.parent.mkdir(parents=True, exist_ok=True)
            if object_path.exists() and object_path.stat().st_size == path.stat().st_size:
                reused += 1
            else:
                temp = object_path.with_suffix(".part")
                shutil.copy2(path, temp)
                os.replace(temp, object_path)
                copied += 1
                bytes_new += object_path.stat().st_size
            manifest["items"].append({
                "song_id": song_id,
                "field": field,
                "original_path": str(path.resolve()),
                "sha256": digest,
                "size": path.stat().st_size,
                "mtime": path.stat().st_mtime,
                "object": str(object_path.relative_to(target_root)).replace("\\", "/"),
            })
            if progress:
                progress(path.name, idx, total)
    (snapshot_dir / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    (target_root / "latest.txt").write_text(backup_id, encoding="utf-8")
    return {"backup_id": backup_id, "path": str(snapshot_dir), "files": len(manifest["items"]), "candidates": total, "copied": copied, "reused": reused, "new_bytes": bytes_new}


def list_incremental_backups(target_root: Path) -> list[dict[str, Any]]:
    snapshots = target_root.expanduser() / "snapshots"
    if not snapshots.exists():
        return []
    result = []
    for path in sorted((p for p in snapshots.iterdir() if p.is_dir()), reverse=True):
        manifest_path = path / "manifest.json"
        if not manifest_path.exists():
            continue
        try:
            data = json.loads(manifest_path.read_text(encoding="utf-8"))
            result.append({"backup_id": data.get("backup_id") or path.name, "created_at": data.get("created_at") or "", "files": len(data.get("items") or []), "path": str(path)})
        except Exception:
            continue
    return result


def restore_incremental_item(db: Any, snapshot_dir: Path, *, song_id: str, field: str = "") -> dict[str, Any]:
    snapshot_dir = snapshot_dir.expanduser().resolve()
    manifest = json.loads((snapshot_dir / "manifest.json").read_text(encoding="utf-8"))
    root = snapshot_dir.parent.parent
    items = [x for x in manifest.get("items") or [] if str(x.get("song_id") or "") == song_id and (not field or str(x.get("field") or "") == field)]
    if not items:
        raise RuntimeError("U izabranom backup-u nema tražene pesme ili fajla.")
    restored: list[dict[str, Any]] = []
    file_updates: dict[str, str] = {}
    for item in items:
        source = (root / str(item.get("object") or "")).resolve()
        target = Path(str(item.get("original_path") or "")).expanduser().resolve()
        if not source.exists():
            raise RuntimeError(f"Backup objekat nedostaje: {source.name}")
        if sha256_file(source) != str(item.get("sha256") or ""):
            raise RuntimeError(f"Hash backup fajla nije ispravan: {source.name}")
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        item_field = str(item.get("field") or "")
        if item_field.startswith("derived:"):
            pass
        else:
            file_updates[item_field] = str(target)
        restored.append({"field": item_field, "path": str(target)})
    if file_updates:
        db.update_song_files(song_id, **file_updates)
    return {"song_id": song_id, "restored": restored, "count": len(restored)}


def relocate_missing_files(db: Any, search_roots: list[Path], progress: Callable[[str, int, int], None] | None = None) -> dict[str, Any]:
    roots = [p.expanduser().resolve() for p in search_roots if p.exists() and p.is_dir()]
    if not roots:
        raise RuntimeError("Izaberi bar jedan postojeći folder za pretragu.")
    songs = db.export_rows()
    missing: list[tuple[str, str, str]] = []
    for song in songs:
        for field in ("local_audio", "local_wav", "local_video", "local_cover", "local_lyrics", "local_lrc", "local_srt"):
            raw = str(song.get(field) or "")
            if raw and not Path(raw).exists():
                missing.append((str(song.get("id") or ""), field, raw))
    by_name: dict[str, list[Path]] = {}
    all_files: list[Path] = []
    for root in roots:
        for path in root.rglob("*"):
            if path.is_file():
                all_files.append(path)
                by_name.setdefault(path.name.casefold(), []).append(path)
    fixed = 0
    unresolved: list[dict[str, str]] = []
    for idx, (song_id, field, old) in enumerate(missing, 1):
        old_path = Path(old)
        candidates = by_name.get(old_path.name.casefold(), [])
        song = db.get_song(song_id) or {}
        wanted_hash = str(song.get("content_sha256") or song.get("file_sha256") or "") if field in ("local_audio", "local_wav") else ""
        chosen: Path | None = None
        if len(candidates) == 1:
            chosen = candidates[0]
        elif candidates:
            if wanted_hash:
                for candidate in candidates:
                    try:
                        if sha256_file(candidate) == wanted_hash:
                            chosen = candidate
                            break
                    except Exception:
                        pass
            # Never guess between same-named files when the old file is missing and no hash proves identity.
            if not chosen and old_path.exists():
                old_size = old_path.stat().st_size
                size_matches = [candidate for candidate in candidates if candidate.stat().st_size == old_size]
                if len(size_matches) == 1:
                    chosen = size_matches[0]
        elif wanted_hash:
            for candidate in all_files:
                try:
                    if candidate.suffix.lower() == old_path.suffix.lower() and sha256_file(candidate) == wanted_hash:
                        chosen = candidate; break
                except Exception:
                    pass
        if chosen:
            db.update_song_files(song_id, **{field: str(chosen.resolve())})
            fixed += 1
        else:
            unresolved.append({"song_id": song_id, "field": field, "old_path": old})
        if progress:
            progress(old_path.name, idx, len(missing))
    return {"missing": len(missing), "fixed": fixed, "unresolved": unresolved, "indexed_files": len(all_files)}


def _run_ffmpeg_capture(args: list[str], timeout: int = 600) -> str:
    proc = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", timeout=timeout, creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0))
    return proc.stdout or ""


# -- BPM (tempo) and musical key: honest FFmpeg-only estimates, not a
# librosa/essentia-grade analyzer. Both are clearly labelled "procena"
# (estimate) wherever they reach the UI. --

_BEAT_SAMPLE_RATE = 8000
_BEAT_FRAME_SAMPLES = 400  # 8000/400 = 50ms per frame (20fps), fine enough to autocorrelate 60-180 BPM
_BEAT_FRAME_SECONDS = _BEAT_FRAME_SAMPLES / _BEAT_SAMPLE_RATE


def _beat_envelope(audio_path: Path, temp_dir: Path) -> list[float]:
    ensure_ffmpeg()
    metadata = temp_dir / "beat.txt"
    escaped_metadata = str(metadata).replace(":", "\\:").replace("'", "\\'")
    filt = f"aresample={_BEAT_SAMPLE_RATE},asetnsamples=n={_BEAT_FRAME_SAMPLES}:p=0,astats=metadata=1:reset=1,ametadata=print:file='{escaped_metadata}'"
    cmd = [str(ffmpeg_path()), "-hide_banner", "-nostats", "-y", "-i", str(audio_path), "-vn", "-af", filt, "-f", "null", "-"]
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=900, creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0))
    if not metadata.exists():
        return []
    values: list[float] = []
    for line in metadata.read_text(encoding="utf-8", errors="replace").splitlines():
        if "lavfi.astats.Overall.RMS_level=" in line:
            raw = line.rsplit("=", 1)[-1].strip()
            try:
                value = float(raw)
            except ValueError:
                value = -100.0
            values.append(value if math.isfinite(value) else -100.0)
    return values


def estimate_bpm(values: list[float], min_bpm: float = 60.0, max_bpm: float = 180.0) -> dict[str, Any] | None:
    """Autocorrelate the half-wave-rectified onset flux of an RMS envelope
    to find the strongest periodicity in the 60-180 BPM range."""
    if len(values) < 40:
        return None
    onset = [max(0.0, values[i] - values[i - 1]) for i in range(1, len(values))]
    mean = sum(onset) / len(onset)
    onset = [v - mean for v in onset]
    energy = sum(v * v for v in onset)
    if energy <= 0:
        return None
    min_lag = max(1, int(round((60.0 / max_bpm) / _BEAT_FRAME_SECONDS)))
    max_lag = min(len(onset) - 1, int(round((60.0 / min_bpm) / _BEAT_FRAME_SECONDS)))
    if max_lag <= min_lag:
        return None
    best_lag = 0
    best_score = 0.0
    for lag in range(min_lag, max_lag + 1):
        score = sum(onset[i] * onset[i - lag] for i in range(lag, len(onset)))
        if score > best_score:
            best_lag, best_score = lag, score
    if best_lag <= 0 or best_score <= 0:
        return None
    bpm = 60.0 / (best_lag * _BEAT_FRAME_SECONDS)
    confidence = round(min(100.0, max(0.0, (best_score / energy) * 100)), 1)
    return {"bpm": round(bpm, 1), "confidence": confidence}


_KEY_SAMPLE_RATE = 2000  # Nyquist 1000Hz still covers C2..B4 pitch classes used below
_KEY_FRAME_SECONDS = 2.0
_KS_MAJOR_PROFILE = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88]
_KS_MINOR_PROFILE = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17]
_NOTE_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]


def _goertzel_power(frame: "array[int]", frequency: float, sample_rate: int) -> float:
    n = len(frame)
    if n == 0:
        return 0.0
    k = int(0.5 + n * frequency / sample_rate)
    omega = 2.0 * math.pi * k / n
    coeff = 2.0 * math.cos(omega)
    s_prev = 0.0
    s_prev2 = 0.0
    for sample in frame:
        s = sample + coeff * s_prev - s_prev2
        s_prev2 = s_prev
        s_prev = s
    power = s_prev2 * s_prev2 + s_prev * s_prev - coeff * s_prev * s_prev2
    return power / n


def estimate_musical_key(audio_path: Path, max_seconds: int = 60) -> dict[str, Any] | None:
    """Krumhansl-Schmuckler key estimate from a Goertzel-built chroma vector.
    Not a substitute for a real chroma/HPCP analyzer, but real signal
    processing rather than a guess -- always labelled as an estimate."""
    ensure_ffmpeg()
    cmd = [
        str(ffmpeg_path()), "-hide_banner", "-nostats", "-y", "-i", str(audio_path), "-vn",
        "-ac", "1", "-ar", str(_KEY_SAMPLE_RATE), "-t", str(max_seconds), "-f", "s16le", "pipe:1",
    ]
    proc = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=180, creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0))
    raw = proc.stdout or b""
    usable = len(raw) - (len(raw) % 2)
    if usable < _KEY_SAMPLE_RATE * 4 * 2:
        return None
    samples: "array[int]" = array("h")
    samples.frombytes(raw[:usable])
    frame_size = int(_KEY_SAMPLE_RATE * _KEY_FRAME_SECONDS)
    chroma = [0.0] * 12
    frames_used = 0
    for start in range(0, len(samples) - frame_size, frame_size):
        frame = samples[start:start + frame_size]
        frames_used += 1
        for octave in range(2, 5):
            for pitch_class in range(12):
                freq = 16.3516 * (2 ** octave) * (2 ** (pitch_class / 12.0))
                if freq >= _KEY_SAMPLE_RATE / 2:
                    continue
                power = _goertzel_power(frame, freq, _KEY_SAMPLE_RATE)
                chroma[pitch_class] += max(0.0, math.log10(power + 1e-9) + 9)
    if frames_used == 0:
        return None
    total = sum(chroma)
    if total <= 0:
        return None
    chroma = [c / total for c in chroma]
    best_tonic = 0
    best_mode = "major"
    best_score = float("-inf")
    for tonic in range(12):
        for mode, profile in (("major", _KS_MAJOR_PROFILE), ("minor", _KS_MINOR_PROFILE)):
            score = sum(chroma[pc] * profile[(pc - tonic) % 12] for pc in range(12))
            if score > best_score:
                best_score, best_tonic, best_mode = score, tonic, mode
    mode_label = "dur" if best_mode == "major" else "mol"
    return {
        "key": _NOTE_NAMES[best_tonic], "mode": best_mode,
        "label": f"{_NOTE_NAMES[best_tonic]} {mode_label}",
        "confidence": round(min(100.0, max(0.0, best_score * 100)), 1),
    }


def analyze_audio_quality(path: Path) -> dict[str, Any]:
    path = path.expanduser().resolve()
    if not path.exists():
        raise RuntimeError("Audio fajl ne postoji.")
    ensure_ffmpeg()
    info = probe_audio(path)
    ffmpeg = str(ffmpeg_path())
    ebur = _run_ffmpeg_capture([ffmpeg, "-hide_banner", "-nostats", "-i", str(path), "-filter_complex", "ebur128=peak=true", "-f", "null", "-"])
    integrated = None; true_peak = None; lra = None
    for match in re.finditer(r"I:\s*(-?[0-9.]+)\s*LUFS", ebur): integrated = float(match.group(1))
    for match in re.finditer(r"LRA:\s*([0-9.]+)\s*LU", ebur): lra = float(match.group(1))
    for match in re.finditer(r"Peak:\s*(-?[0-9.]+)\s*dBFS", ebur): true_peak = float(match.group(1))
    silence = _run_ffmpeg_capture([ffmpeg, "-hide_banner", "-nostats", "-i", str(path), "-af", "silencedetect=noise=-45dB:d=0.5", "-f", "null", "-"])
    starts = [float(x) for x in re.findall(r"silence_start:\s*([0-9.]+)", silence)]
    ends = [float(x) for x in re.findall(r"silence_end:\s*([0-9.]+)", silence)]
    duration = float(info.get("duration") or 0)
    leading_silence = ends[0] if starts and starts[0] <= 0.05 and ends else 0.0
    trailing_silence = 0.0
    if starts and duration and starts[-1] > duration - 15:
        trailing_silence = max(0.0, duration - starts[-1])
    astats = _run_ffmpeg_capture([ffmpeg, "-hide_banner", "-nostats", "-i", str(path), "-af", "astats=metadata=1:reset=0", "-f", "null", "-"])
    nan_samples = sum(int(m.group(1)) for m in re.finditer(r"Number of NaNs:\s*(\d+)", astats))
    inf_samples = sum(int(m.group(1)) for m in re.finditer(r"Number of Infs:\s*(\d+)", astats))
    clipping_risk = bool(true_peak is not None and true_peak >= -0.1)
    # Heuristic score aimed at common YouTube music delivery targets, not a mastering certificate.
    score = 100
    warnings: list[str] = []
    if integrated is None:
        score -= 15; warnings.append("LUFS nije mogao da se izmeri")
    else:
        delta = abs(integrated - (-14.0))
        if delta > 6: score -= 20; warnings.append(f"glasnoća je daleko od -14 LUFS ({integrated:.1f})")
        elif delta > 3: score -= 10; warnings.append(f"glasnoća odstupa od -14 LUFS ({integrated:.1f})")
    if true_peak is not None and true_peak > -1.0:
        score -= 20; warnings.append(f"true peak je previsok ({true_peak:.1f} dBFS)")
    if leading_silence > 2:
        score -= 8; warnings.append(f"duga tišina na početku ({leading_silence:.1f}s)")
    if trailing_silence > 4:
        score -= 8; warnings.append(f"duga tišina na kraju ({trailing_silence:.1f}s)")
    if int(info.get("sample_rate") or 0) < 44100:
        score -= 8; warnings.append("sample rate je ispod 44.1 kHz")
    bitrate = int(info.get("bit_rate") or info.get("bitrate") or 0)
    if bitrate and bitrate < 192000 and path.suffix.lower() == ".mp3":
        score -= 8; warnings.append("MP3 bitrate je ispod 192 kbps")
    if int(info.get("channels") or 0) == 1:
        warnings.append("audio je mono")
    score = max(0, min(100, score))
    bpm_info = None
    try:
        with tempfile.TemporaryDirectory(prefix="suno-beat-") as tmp:
            bpm_info = estimate_bpm(_beat_envelope(path, Path(tmp)))
    except Exception:
        bpm_info = None
    try:
        key_info = estimate_musical_key(path)
    except Exception:
        key_info = None
    return {**info, "path": str(path), "integrated_lufs": integrated, "loudness_range_lu": lra, "true_peak_dbfs": true_peak, "leading_silence": round(leading_silence, 3), "trailing_silence": round(trailing_silence, 3), "nan_samples": nan_samples, "infinite_samples": inf_samples, "clipping_risk": clipping_risk, "bpm_estimate": bpm_info, "key_estimate": key_info, "score": score, "ready": score >= 80, "warnings": warnings}


_LRC_RE = re.compile(r"^\[(\d+):(\d+(?:\.\d+)?)\](.*)$")
_SRT_TIME_RE = re.compile(r"(\d+):(\d+):(\d+)[,.](\d+)\s*-->\s*(\d+):(\d+):(\d+)[,.](\d+)")


def parse_lrc(text: str) -> list[dict[str, Any]]:
    cues = []
    for line in str(text or "").splitlines():
        m = _LRC_RE.match(line.strip())
        if not m: continue
        start = int(m.group(1)) * 60 + float(m.group(2))
        cues.append({"start_s": start, "end_s": start + 3.0, "text": m.group(3).strip()})
    for i in range(len(cues) - 1):
        cues[i]["end_s"] = max(cues[i]["start_s"] + 0.1, cues[i + 1]["start_s"] - 0.02)
    return cues


def parse_srt(text: str) -> list[dict[str, Any]]:
    cues = []
    blocks = re.split(r"\n\s*\n", str(text or "").strip())
    for block in blocks:
        lines = block.replace("\r", "").splitlines()
        timing_index = next((i for i, line in enumerate(lines) if "-->" in line), -1)
        if timing_index < 0: continue
        m = _SRT_TIME_RE.search(lines[timing_index])
        if not m: continue
        raw_vals = list(m.groups())
        vals = [int(x) for x in raw_vals]
        start_fraction = int(raw_vals[3]) / (10 ** len(raw_vals[3]))
        end_fraction = int(raw_vals[7]) / (10 ** len(raw_vals[7]))
        start = vals[0] * 3600 + vals[1] * 60 + vals[2] + start_fraction
        end = vals[4] * 3600 + vals[5] * 60 + vals[6] + end_fraction
        cues.append({"start_s": start, "end_s": end, "text": "\n".join(lines[timing_index + 1:]).strip()})
    return cues


def load_subtitle_cues(song: dict[str, Any], db: Any) -> list[dict[str, Any]]:
    stored = db.list_subtitle_cues(str(song.get("id") or ""))
    if stored: return stored
    for field, parser in (("local_lrc", parse_lrc), ("local_srt", parse_srt)):
        raw = str(song.get(field) or "")
        if raw and Path(raw).exists():
            cues = parser(Path(raw).read_text(encoding="utf-8", errors="replace"))
            if cues:
                db.save_subtitle_cues(str(song.get("id") or ""), cues, source=field)
                return db.list_subtitle_cues(str(song.get("id") or ""))
    lyrics = [line.strip() for line in str(song.get("lyrics") or "").splitlines() if line.strip()]
    duration = float(song.get("duration") or 0)
    step = duration / max(1, len(lyrics)) if duration else 4.0
    return [{"cue_index": i + 1, "start_s": i * step, "end_s": (i + 1) * step, "text": line, "source": "generated-draft"} for i, line in enumerate(lyrics)]


def save_subtitle_files(song: dict[str, Any], cues: list[dict[str, Any]], db: Any, target_dir: Path | None = None, fallback_dir: Path | None = None, source: str = "manual") -> dict[str, str]:
    song_id = str(song.get("id") or "")
    normalized = []
    for cue in cues:
        start = float(cue.get("start_s", cue.get("start", 0)) or 0)
        end = float(cue.get("end_s", cue.get("end", start + 0.1)) or (start + 0.1))
        normalized.append({"start_s": max(0.0, start), "end_s": max(start + 0.05, end), "text": str(cue.get("text") or "").strip()})
    cues = [cue for cue in normalized if cue["text"]]
    db.save_subtitle_cues(song_id, cues, source=source)
    local_raw = str(song.get("local_audio") or song.get("local_wav") or "").strip()
    if target_dir is not None:
        base_dir = target_dir.expanduser().resolve()
    elif local_raw:
        base_dir = Path(local_raw).expanduser().resolve().parent
    elif fallback_dir is not None:
        base_dir = fallback_dir.expanduser().resolve()
    else:
        base_dir = Path.cwd().resolve() / "Tekstovi"
    base_dir.mkdir(parents=True, exist_ok=True)
    base = sanitize_filename(str(song.get("title") or song_id), 100)
    lrc_path = base_dir / f"{base}.lrc"
    srt_path = base_dir / f"{base}.srt"
    vtt_path = base_dir / f"{base}.vtt"
    lrc_path.write_text(format_lrc(cues), encoding="utf-8")
    srt_path.write_text(format_srt(cues), encoding="utf-8")
    vtt_path.write_text(format_vtt(cues), encoding="utf-8")
    db.update_song_files(song_id, local_lrc=str(lrc_path.resolve()), local_srt=str(srt_path.resolve()))
    return {"lrc": str(lrc_path), "srt": str(srt_path), "vtt": str(vtt_path)}


def has_manual_subtitle_cues(db: Any, song_id: str) -> bool:
    """True if the user already saved hand-edited timing/text for this song
    via the subtitle segment editor -- used to stop an automatic
    transcription re-run from silently overwriting manual corrections."""
    return any(str(cue.get("source") or "") == "manual" for cue in db.list_subtitle_cues(song_id))


def normalize_text_lines(text: str) -> list[str]:
    result = []
    for line in str(text or "").splitlines():
        clean = re.sub(r"\[[^]]+\]", "", line).strip().casefold()
        clean = re.sub(r"[^\wčćžšđ]+", " ", clean, flags=re.UNICODE)
        clean = " ".join(clean.split())
        if clean: result.append(clean)
    return result


def compare_lyrics_transcript(lyrics: str, transcript: str) -> dict[str, Any]:
    original = normalize_text_lines(lyrics)
    observed = normalize_text_lines(transcript)
    matcher = difflib.SequenceMatcher(a=original, b=observed, autojunk=False)
    missing: list[str] = []
    changed: list[dict[str, Any]] = []
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag == "delete": missing.extend(original[i1:i2])
        elif tag == "replace": changed.append({"suno": original[i1:i2], "youtube": observed[j1:j2]})
    return {"similarity": round(matcher.ratio() * 100, 1), "missing_lines": missing, "changed_lines": changed, "suno_lines": len(original), "transcript_lines": len(observed), "transcript_text": transcript}


class DATA_BLOB(ctypes.Structure):
    _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_byte))]


def _dpapi(data: bytes, protect: bool) -> bytes:
    if os.name != "nt":
        raise RuntimeError("DPAPI enkripcija je dostupna samo na Windowsu.")
    blob_in = DATA_BLOB(len(data), ctypes.cast(ctypes.create_string_buffer(data), ctypes.POINTER(ctypes.c_byte)))
    blob_out = DATA_BLOB()
    fn = ctypes.windll.crypt32.CryptProtectData if protect else ctypes.windll.crypt32.CryptUnprotectData
    if protect:
        ok = fn(ctypes.byref(blob_in), "Suno Pesme Studio cloud backup", None, None, None, 0, ctypes.byref(blob_out))
    else:
        ok = fn(ctypes.byref(blob_in), None, None, None, None, 0, ctypes.byref(blob_out))
    if not ok:
        raise ctypes.WinError()
    try:
        return ctypes.string_at(blob_out.pbData, blob_out.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(blob_out.pbData)


def create_cloud_backup(db: Any, sync_folder: Path, *, include_files: bool = False) -> dict[str, Any]:
    sync_folder = sync_folder.expanduser().resolve()
    sync_folder.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="suno_cloud_") as tmp:
        snapshot = Path(tmp) / "suno_biblioteka.db"
        db.backup_to(snapshot)
        encrypted = _dpapi(snapshot.read_bytes(), True) if os.name == "nt" else snapshot.read_bytes()
        package = unique_timestamp_path(sync_folder, "Suno-Pesme-Cloud-", ".zip")
        manifest: dict[str, Any] = {
            "format": "suno-pesme-cloud-v2", "created_at": now_iso(), "encrypted": os.name == "nt",
            "encryption": "Windows DPAPI CurrentUser" if os.name == "nt" else "none", "include_files": include_files,
            "database": "data/suno_biblioteka.db.dpapi" if os.name == "nt" else "data/suno_biblioteka.db", "files": [],
        }
        if include_files:
            for song in db.export_rows():
                full = db.get_song(str(song.get("id") or "")) or song
                for field, path in iter_song_files(full):
                    if path.exists() and path.is_file():
                        arc = f"files/{song.get('id')}/{field}/{path.name}"
                        manifest["files"].append({"song_id": str(song.get("id") or ""), "field": field, "arcname": arc, "original_path": str(path.resolve()), "sha256": sha256_file(path), "size": path.stat().st_size})
        with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED, allowZip64=True) as z:
            z.writestr(manifest["database"], encrypted)
            for item in manifest["files"]:
                z.write(Path(item["original_path"]), arcname=item["arcname"])
            z.writestr("manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2))
    return {"path": str(package), "size": package.stat().st_size, "encrypted": os.name == "nt", "files": len(manifest["files"])}


def restore_cloud_backup(db: Any, package: Path, *, restore_files: bool = True, restore_root: Path | None = None, preserve_original_paths: bool = False) -> dict[str, Any]:
    package = package.expanduser().resolve()
    if not package.is_file():
        raise RuntimeError("Cloud backup ZIP ne postoji.")
    controlled_root = (restore_root or (package.parent / "Vraceni_cloud_backup" / package.stem)).expanduser().resolve()
    with tempfile.TemporaryDirectory(prefix="suno_cloud_restore_") as tmp_raw:
        tmp = Path(tmp_raw)
        with zipfile.ZipFile(package, "r") as z:
            names = set(z.namelist())
            if "manifest.json" not in names:
                raise RuntimeError("Backup nema manifest.json.")
            manifest = json.loads(z.read("manifest.json").decode("utf-8"))
            db_name = str(manifest.get("database") or ("data/suno_biblioteka.db.dpapi" if manifest.get("encrypted") else "data/suno_biblioteka.db"))
            if db_name not in names:
                raise RuntimeError("Backup ne sadrži bazu.")
            raw = z.read(db_name)
            if manifest.get("encrypted"):
                if os.name != "nt":
                    raise RuntimeError("Ovaj backup je zaštićen Windows DPAPI sistemom i može da se vrati samo na Windows nalogu koji ga je napravio.")
                raw = _dpapi(raw, False)
            db_file = tmp / "suno_biblioteka.db"
            db_file.write_bytes(raw)
            db.restore_from(db_file)
            restored = 0
            skipped: list[dict[str, str]] = []
            controlled_root.mkdir(parents=True, exist_ok=True)
            for item in (manifest.get("files") or []) if restore_files else []:
                arc = str(item.get("arcname") or "")
                target_raw = str(item.get("original_path") or "")
                song_id = str(item.get("song_id") or "")
                field = str(item.get("field") or "")
                if not arc or arc not in names:
                    skipped.append({"arcname": arc, "target": target_raw, "error": "Fajl ne postoji u ZIP-u"})
                    continue
                safe_name = sanitize_filename(Path(target_raw or arc).name, 140)
                if preserve_original_paths and target_raw:
                    target = Path(target_raw).expanduser().resolve()
                else:
                    safe_field = sanitize_filename(field.replace(":", "-"), 50) or "fajl"
                    target = controlled_root / (sanitize_filename(song_id, 80) or "bez-id") / safe_field / safe_name
                try:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    temp = target.with_suffix(target.suffix + ".restore-part")
                    with z.open(arc) as src, temp.open("wb") as out:
                        shutil.copyfileobj(src, out)
                    expected = str(item.get("sha256") or "")
                    if expected and sha256_file(temp) != expected:
                        temp.unlink(missing_ok=True)
                        raise RuntimeError("SHA-256 se ne poklapa")
                    os.replace(temp, target)
                    if field.startswith("derived:"):
                        try:
                            db.update_derived_file_path(int(field.split(":", 1)[1]), str(target.resolve()))
                        except Exception:
                            pass
                    elif song_id and field:
                        db.update_song_files(song_id, **{field: str(target.resolve())})
                    restored += 1
                except Exception as exc:
                    skipped.append({"arcname": arc, "target": str(target), "error": str(exc)})
    return {"path": str(package), "database_restored": True, "files_restored": restored, "restore_root": str(controlled_root), "skipped": skipped}


# component -> (pip packages to install, a top-level package dir that only
# exists once the install actually succeeded -- used as the "installed?"
# check instead of a separate interpreter, see install_ai_plugin() below).
_AI_COMPONENTS: dict[str, tuple[list[str], str]] = {
    "stems": (["audio-separator[cpu]==0.44.5"], "audio_separator"),
    "transcription": (["faster-whisper==1.2.1", "ctranslate2==4.8.1"], "faster_whisper"),
}


def _bundled_python(root: Path) -> Path:
    return root / "python" / ("python.exe" if os.name == "nt" else "python")


def plugin_status(root: Path) -> dict[str, Any]:
    plugins = root / "plugins"
    panako_candidates = [
        root / "tools" / "panako" / ("panako.bat" if os.name == "nt" else "panako"),
        plugins / "panako" / ("panako.bat" if os.name == "nt" else "panako"),
    ]
    panako = next((candidate for candidate in panako_candidates if candidate.exists()), panako_candidates[0])
    fpcalc = plugins / "chromaprint" / ("fpcalc.exe" if os.name == "nt" else "fpcalc")
    result: dict[str, Any] = {
        "panako": {"installed": panako.exists(), "path": str(panako)},
        "chromaprint": {"installed": fpcalc.exists() or bool(shutil.which("fpcalc")), "path": str(fpcalc if fpcalc.exists() else shutil.which("fpcalc") or "")},
    }
    for component, (_, marker_pkg) in _AI_COMPONENTS.items():
        target = plugins / f"{component}_env"
        result[component] = {"installed": (target / marker_pkg).is_dir(), "target": str(target)}
    return result


def install_ai_plugin(root: Path, component: str, progress: Callable[[str], None] | None = None) -> dict[str, Any]:
    """Real installation of an optional AI component (stems|transcription).

    The embeddable Python distribution this app ships has no venv/ensurepip
    support, so a real isolated virtualenv (what the old, since-removed
    INSTALIRAJ_STEMOVE.bat / INSTALIRAJ_TRANSKRIPCIJU.bat scripts implied)
    isn't practical here. `pip install --target <dir>` with the bundled
    interpreter is the real, working equivalent: it actually downloads and
    installs the packages (verified against real PyPI in this project's
    CI), keeps them out of the main interpreter's site-packages, and
    run_stem_separation()/run_transcription() below load them via
    PYTHONPATH instead of a separate interpreter.
    """
    if component not in _AI_COMPONENTS:
        raise ValueError(f"Nepoznata AI komponenta: {component}")
    packages, _ = _AI_COMPONENTS[component]
    python_exe = _bundled_python(root)
    if not python_exe.exists():
        raise RuntimeError("Ugrađeni Python interpreter nije pronađen.")
    target = root / "plugins" / f"{component}_env"
    target.mkdir(parents=True, exist_ok=True)
    if progress:
        progress(f"Instaliram: {', '.join(packages)} (ovo može potrajati nekoliko minuta)")
    cmd = [str(python_exe), "-m", "pip", "install", "--no-warn-script-location", "--target", str(target), *packages]
    proc = subprocess.run(
        cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1800,
        creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0),
    )
    if proc.returncode != 0:
        raise RuntimeError((proc.stderr or proc.stdout or "Instalacija nije uspela.")[-2000:])
    if progress:
        progress(f"{component} je instaliran.")
    return {"ok": True, "component": component, "target": str(target)}


def _run_ai_worker(root: Path, component: str, worker_name: str, args: list[str]) -> dict[str, Any]:
    status = plugin_status(root)[component]
    if not status["installed"]:
        raise RuntimeError(
            "Ovaj AI dodatak nije instaliran. Otvori karticu „Pametni alati“ i klikni „Instaliraj“ "
            f"za {('izdvajanje vokala' if component == 'stems' else 'transkripciju')}."
        )
    worker = root / "plugins" / worker_name
    env = os.environ.copy()
    env["PYTHONPATH"] = status["target"] + (os.pathsep + env["PYTHONPATH"] if env.get("PYTHONPATH") else "")
    cmd = [str(_bundled_python(root)), str(worker), *args]
    proc = subprocess.run(
        cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=7200, env=env,
        creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0),
    )
    if proc.returncode != 0:
        raise RuntimeError((proc.stderr or proc.stdout or "Obrada nije uspela.")[-2000:])
    return json.loads((proc.stdout or "{}").strip().splitlines()[-1])


def run_stem_separation(root: Path, audio_path: Path, output_dir: Path, model: str = "htdemucs") -> dict[str, Any]:
    output_dir.mkdir(parents=True, exist_ok=True)
    return _run_ai_worker(root, "stems", "stems_worker.py", [str(audio_path), str(output_dir), model])


def run_transcription(root: Path, audio_path: Path, language: str = "sr", model: str = "small") -> dict[str, Any]:
    return _run_ai_worker(root, "transcription", "transcribe_worker.py", [str(audio_path), language, model])


def _version_tuple(value: str) -> tuple[int, ...]:
    parts = re.findall(r"\d+", str(value or ""))
    return tuple(int(x) for x in parts[:4]) or (0,)


def check_update(manifest_url: str, current_version: str) -> dict[str, Any]:
    url = str(manifest_url or "").strip()
    if not url:
        return {"configured": False, "available": False, "message": "URL manifesta za ažuriranje nije podešen.", "current": current_version}
    if not (url.startswith("https://") or url.startswith("file://")):
        raise RuntimeError("Update manifest mora koristiti HTTPS adresu.")
    with urllib.request.urlopen(url, timeout=20) as response:
        data = json.loads(response.read().decode("utf-8"))
    latest = str(data.get("version") or "")
    available = bool(latest and _version_tuple(latest) > _version_tuple(current_version))
    return {"configured": True, "available": available, "current": current_version, "latest": latest, "download_url": str(data.get("download_url") or ""), "sha256": str(data.get("sha256") or ""), "notes": str(data.get("notes") or ""), "message": "Nova verzija je dostupna." if available else "Koristiš najnoviju verziju."}


def download_update(root: Path, manifest_url: str, current_version: str) -> dict[str, Any]:
    info = check_update(manifest_url, current_version)
    if not info.get("available"):
        raise RuntimeError(info.get("message") or "Nova verzija nije pronađena.")
    url = str(info.get("download_url") or "").strip()
    expected = str(info.get("sha256") or "").strip().lower()
    local_test = str(manifest_url or "").startswith("file://")
    if not url or not re.fullmatch(r"[0-9a-f]{64}", expected):
        raise RuntimeError("Manifest nema ispravan download_url ili SHA-256.")
    if not (url.startswith("https://") or (local_test and url.startswith("file://"))):
        raise RuntimeError("Ažuriranje mora da se preuzme preko HTTPS adrese.")
    updates = root / "Azuriranja"
    updates.mkdir(parents=True, exist_ok=True)
    target = updates / f"Suno-Pesme-Studio-{info['latest']}.zip"
    part = target.with_suffix(target.suffix + ".part")
    part.unlink(missing_ok=True)
    try:
        with urllib.request.urlopen(url, timeout=120) as response, part.open("wb") as out:
            total = 0
            while True:
                chunk = response.read(1024 * 1024)
                if not chunk:
                    break
                total += len(chunk)
                if total > 2 * 1024 * 1024 * 1024:
                    raise RuntimeError("Paket ažuriranja je neočekivano velik.")
                out.write(chunk)
        if sha256_file(part).lower() != expected:
            raise RuntimeError("SHA-256 preuzetog ažuriranja nije ispravan.")
        with zipfile.ZipFile(part, "r") as archive:
            bad = archive.testzip()
            if bad:
                raise RuntimeError(f"ZIP ažuriranja je oštećen: {bad}")
        os.replace(part, target)
    except Exception:
        part.unlink(missing_ok=True)
        raise
    return {**info, "path": str(target), "verified": True}
