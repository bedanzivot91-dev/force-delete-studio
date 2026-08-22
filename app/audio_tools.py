from __future__ import annotations

import hashlib
from array import array
import json
import math
import os
import re
import shutil
import subprocess
import threading
import time
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path
from typing import Any, Callable


ROOT = Path(__file__).resolve().parent.parent
TOOLS_DIR = ROOT / "tools" / "ffmpeg"
BIN_DIR = TOOLS_DIR / "bin"
# The "full" build, NOT "essentials". Song recognition depends on FFmpeg's
# Chromaprint muxer (-f chromaprint), and gyan.dev's essentials build is
# compiled WITHOUT libchromaprint. With essentials, _extract_chromaprint()
# silently returns [] for every song, every comparison falls back to the
# envelope metric, and that metric collapses as soon as the audio has been
# re-encoded -- which a Shorts clip always has. The result was that a Short
# genuinely containing the user's own song could never be recognised.
FFMPEG_URL = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-full.zip"
FFMPEG_SHA_URL = FFMPEG_URL + ".sha256"
USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Suno-Pesme-Studio/1.2"


class AudioCancelled(RuntimeError):
    pass


def _exe(name: str) -> str:
    return name + (".exe" if os.name == "nt" else "")


def ffmpeg_path() -> str | None:
    local = BIN_DIR / _exe("ffmpeg")
    if local.exists():
        return str(local)
    return shutil.which("ffmpeg")


def ffprobe_path() -> str | None:
    local = BIN_DIR / _exe("ffprobe")
    if local.exists():
        return str(local)
    return shutil.which("ffprobe")


def status() -> dict[str, Any]:
    ffmpeg = ffmpeg_path()
    ffprobe = ffprobe_path()
    version = ""
    if ffmpeg:
        try:
            result = _run([ffmpeg, "-version"], timeout=10)
            version = (result.stdout or "").splitlines()[0]
        except Exception:
            version = ""
    ffprobe_ok = False
    if ffprobe:
        try:
            probe_result = _run([ffprobe, "-version"], timeout=10)
            ffprobe_ok = probe_result.returncode == 0
        except Exception:
            ffprobe_ok = False
    return {
        "ready": bool(ffmpeg and ffprobe and version and ffprobe_ok),
        "ffmpeg": ffmpeg or "",
        "ffprobe": ffprobe or "",
        "version": version,
        "portable_dir": str(BIN_DIR),
        "download_mb": 145,
        "fast_cut": True,
        "cached_waveform": True,
        "direct_remote_processing": True,
        "chromaprint": has_chromaprint(),
    }


_CHROMAPRINT_CACHE: dict[str, bool] = {}


def has_chromaprint(ffmpeg: str | None = None) -> bool:
    """True when this FFmpeg build actually has the Chromaprint muxer.

    Song recognition is only reliable with it. Without it every fingerprint
    falls back to a loudness-envelope metric that does not survive the
    re-encoding a Shorts clip always went through, so a Short containing the
    user's own song silently never matches. Detecting it explicitly is what
    lets the UI say so instead of just reporting "not found" forever.
    """
    exe = ffmpeg or ffmpeg_path()
    if not exe:
        return False
    cached = _CHROMAPRINT_CACHE.get(exe)
    if cached is not None:
        return cached
    ok = False
    try:
        result = _run([exe, "-hide_banner", "-muxers"], timeout=15)
        ok = bool(re.search(r"^\s*\S*E\S*\s+chromaprint\b", result.stdout or "", re.MULTILINE))
    except Exception:
        ok = False
    _CHROMAPRINT_CACHE[exe] = ok
    return ok


def _download(url: str, target: Path, progress: Callable[[int, int], None] | None = None) -> None:
    req = urllib.request.Request(url, headers={"User-Agent": "Suno Pesme Studio/1.2"})
    with urllib.request.urlopen(req, timeout=120) as response, target.open("wb") as out:
        total = int(response.headers.get("Content-Length") or 0)
        done = 0
        while True:
            chunk = response.read(1024 * 512)
            if not chunk:
                break
            out.write(chunk)
            done += len(chunk)
            if progress:
                progress(done, total)


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def ensure_ffmpeg(progress: Callable[[str, int], None] | None = None, force_update: bool = False) -> dict[str, Any]:
    current = status()
    # An already-installed FFmpeg that runs fine but has NO Chromaprint muxer
    # must still be replaced: song recognition silently degrades to a
    # loudness-envelope match that cannot identify a re-encoded Shorts clip.
    # Measured on a real 28s Shorts against the same music inside a 3-minute
    # original: without Chromaprint 48.8 points, 6s matched, located 45s off
    # (rejected); with Chromaprint 76.8 points, 25.6s matched, located
    # correctly (confirmed). Without this upgrade path every existing
    # installation would keep its old essentials build forever, because
    # "ready" only means the binaries execute.
    needs_chromaprint_upgrade = current["ready"] and not current.get("chromaprint")
    if current["ready"] and not force_update and not needs_chromaprint_upgrade:
        return current
    if needs_chromaprint_upgrade and progress:
        progress("Postojeci FFmpeg nema Chromaprint modul za prepoznavanje pesama. Preuzimam pun FFmpeg...", 0)
    if os.name != "nt":
        raise RuntimeError("FFmpeg nije pronađen. Na ovom sistemu ga instaliraj kroz sistemski paket menadžer.")

    TOOLS_DIR.mkdir(parents=True, exist_ok=True)
    BIN_DIR.mkdir(parents=True, exist_ok=True)
    archive = TOOLS_DIR / "ffmpeg-release-full.zip.part"
    checksum_file = TOOLS_DIR / "ffmpeg-release-full.zip.sha256"

    if progress:
        progress("Preuzimam FFmpeg audio alate (oko 145 MB)...", 0)

    def dl_progress(done: int, total: int) -> None:
        percent = int(done * 100 / total) if total else 0
        if progress:
            progress(f"Preuzimam FFmpeg: {percent}%", percent)

    try:
        _download(FFMPEG_URL, archive, dl_progress)
        try:
            _download(FFMPEG_SHA_URL, checksum_file)
        except Exception as exc:
            raise RuntimeError("Ne mogu da preuzmem zvanični SHA-256 za FFmpeg. Paket nije instaliran jer integritet ne može da se potvrdi.") from exc
        pieces = checksum_file.read_text(encoding="utf-8", errors="replace").strip().split()
        expected = pieces[0].lower() if pieces else ""
        if not re.fullmatch(r"[0-9a-f]{64}", expected):
            raise RuntimeError("Zvanični FFmpeg SHA-256 fajl nema ispravan format. Paket nije instaliran.")
        actual = _sha256(archive).lower()
        if actual != expected:
            raise RuntimeError("Kontrolni zbir FFmpeg paketa nije ispravan. Preuzimanje je obrisano.")

        if progress:
            progress("Raspakujem FFmpeg...", 95)
        extract_dir = TOOLS_DIR / "extract"
        if extract_dir.exists():
            shutil.rmtree(extract_dir, ignore_errors=True)
        extract_dir.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(archive) as zf:
            bad = zf.testzip()
            if bad:
                raise RuntimeError(f"FFmpeg ZIP je oštećen: {bad}")
            zf.extractall(extract_dir)

        found_ffmpeg = next(extract_dir.rglob("ffmpeg.exe"), None)
        found_ffprobe = next(extract_dir.rglob("ffprobe.exe"), None)
        if not found_ffmpeg or not found_ffprobe:
            raise RuntimeError("U preuzetom paketu nisu pronađeni ffmpeg.exe i ffprobe.exe.")
        shutil.copy2(found_ffmpeg, BIN_DIR / "ffmpeg.exe")
        shutil.copy2(found_ffprobe, BIN_DIR / "ffprobe.exe")
        found_ffplay = next(extract_dir.rglob("ffplay.exe"), None)
        if found_ffplay:
            shutil.copy2(found_ffplay, BIN_DIR / "ffplay.exe")
        shutil.rmtree(extract_dir, ignore_errors=True)
        archive.unlink(missing_ok=True)
        if progress:
            progress("FFmpeg je spreman.", 100)
    except Exception:
        archive.unlink(missing_ok=True)
        raise

    current = status()
    if not current["ready"]:
        raise RuntimeError("FFmpeg je preuzet, ali program ne može da ga pokrene.")
    return current


def _run(args: list[str], timeout: float | None = None) -> subprocess.CompletedProcess[str]:
    kwargs: dict[str, Any] = {
        "stdout": subprocess.PIPE,
        "stderr": subprocess.PIPE,
        "text": True,
        "encoding": "utf-8",
        "errors": "replace",
        "timeout": timeout,
    }
    if os.name == "nt":
        kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW  # type: ignore[attr-defined]
    return subprocess.run(args, **kwargs)


def _is_url(value: str | Path) -> bool:
    raw = str(value)
    return raw.startswith("http://") or raw.startswith("https://")


def _input_args(source: str | Path) -> list[str]:
    if not _is_url(source):
        return ["-i", str(source)]
    headers = "Referer: https://suno.com/\r\n"
    return ["-user_agent", USER_AGENT, "-headers", headers, "-rw_timeout", "30000000", "-i", str(source)]


def probe_audio(source: str | Path) -> dict[str, Any]:
    probe = ffprobe_path()
    if not probe:
        raise RuntimeError("FFmpeg audio alati nisu spremni.")
    args = [probe, "-v", "error"]
    if _is_url(source):
        args += ["-user_agent", USER_AGENT, "-headers", "Referer: https://suno.com/\r\n", "-rw_timeout", "30000000"]
    args += [
        "-show_entries",
        "format=duration,size,bit_rate,format_name:format_tags=title,artist,album,genre,date,year,track,copyright,comment:stream=codec_name,codec_type,sample_rate,channels,channel_layout,bit_rate",
        "-of", "json", str(source),
    ]
    result = _run(args, timeout=45)
    if result.returncode != 0:
        raise RuntimeError((result.stderr or "FFprobe nije mogao da pročita fajl.").strip())
    data = json.loads(result.stdout or "{}")
    fmt = data.get("format") or {}
    audio_stream = next((s for s in data.get("streams", []) if s.get("codec_type") == "audio"), {})
    duration = float(fmt.get("duration") or 0)
    bitrate = int(float(audio_stream.get("bit_rate") or fmt.get("bit_rate") or 0))
    tags = fmt.get("tags") if isinstance(fmt.get("tags"), dict) else {}
    size_raw = fmt.get("size") or 0
    if not size_raw and not _is_url(source):
        try:
            size_raw = Path(source).stat().st_size
        except OSError:
            size_raw = 0
    return {
        "duration": duration,
        "size": int(size_raw or 0),
        "bitrate": bitrate,
        "bitrate_kbps": round(bitrate / 1000) if bitrate else 0,
        "format": fmt.get("format_name") or Path(urllib.parse.urlparse(str(source)).path).suffix.lstrip("."),
        "codec": audio_stream.get("codec_name") or "",
        "sample_rate": int(audio_stream.get("sample_rate") or 0),
        "channels": int(audio_stream.get("channels") or 0),
        "channel_layout": audio_stream.get("channel_layout") or "",
        "tags": {str(k).lower(): str(v) for k, v in tags.items()},
    }


def _safe_float(value: Any, default: float = 0.0) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def _source_extension(source: str | Path) -> str:
    path = urllib.parse.urlparse(str(source)).path if _is_url(source) else str(source)
    return Path(path).suffix.lower()


def _has_effects(options: dict[str, Any]) -> bool:
    return any([
        bool(options.get("normalize")),
        bool(options.get("remove_silence")),
        abs(_safe_float(options.get("volume_db"), 0.0)) >= 0.05,
        abs(_safe_float(options.get("speed"), 1.0) - 1.0) >= 0.001,
        _safe_float(options.get("fade_in"), 0.0) > 0,
        _safe_float(options.get("fade_out"), 0.0) > 0,
        int(_safe_float(options.get("sample_rate"), 0)) > 0,
        int(_safe_float(options.get("channels"), 0)) > 0,
    ])


def _copy_compatible(source: str | Path, fmt: str, info: dict[str, Any]) -> bool:
    codec = str(info.get("codec") or "").lower()
    ext = _source_extension(source)
    if fmt == "mp3":
        return codec == "mp3" or ext == ".mp3"
    if fmt in ("m4a", "aac"):
        return codec == "aac" or ext in (".m4a", ".aac", ".mp4")
    if fmt == "flac":
        return codec == "flac" or ext == ".flac"
    if fmt == "wav":
        return codec.startswith("pcm_") or ext == ".wav"
    return False


def _run_ffmpeg_progress(
    args: list[str],
    expected_duration: float,
    progress: Callable[[str, int], None] | None,
    cancel_check: Callable[[], bool] | None,
    timeout: float,
) -> tuple[int, str, float]:
    kwargs: dict[str, Any] = {
        "stdout": subprocess.PIPE,
        "stderr": subprocess.PIPE,
        "text": True,
        "encoding": "utf-8",
        "errors": "replace",
        "bufsize": 1,
    }
    if os.name == "nt":
        kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW  # type: ignore[attr-defined]
    started = time.monotonic()
    proc = subprocess.Popen(args, **kwargs)
    stderr_lines: list[str] = []

    def read_stderr() -> None:
        if proc.stderr is None:
            return
        for line in proc.stderr:
            stderr_lines.append(line.rstrip())
            if len(stderr_lines) > 80:
                del stderr_lines[:-80]

    stderr_thread = threading.Thread(target=read_stderr, daemon=True)
    stderr_thread.start()
    last_percent = -1
    try:
        if proc.stdout is not None:
            for raw in proc.stdout:
                if cancel_check and cancel_check():
                    proc.terminate()
                    try:
                        proc.wait(timeout=3)
                    except subprocess.TimeoutExpired:
                        proc.kill()
                    raise AudioCancelled("Audio obrada je zaustavljena.")
                line = raw.strip()
                out_seconds: float | None = None
                if line.startswith("out_time_us="):
                    out_seconds = _safe_float(line.split("=", 1)[1], 0) / 1_000_000
                elif line.startswith("out_time_ms="):
                    out_seconds = _safe_float(line.split("=", 1)[1], 0) / 1_000_000
                if out_seconds is not None and expected_duration > 0:
                    pct = max(1, min(96, int(out_seconds / expected_duration * 96)))
                    if pct != last_percent:
                        last_percent = pct
                        if progress:
                            progress(f"Obrada audio-fajla: {pct}%", pct)
                if time.monotonic() - started > timeout:
                    proc.kill()
                    raise RuntimeError("Audio obrada je prekoračila dozvoljeno vreme.")
        return_code = proc.wait(timeout=max(5, timeout - (time.monotonic() - started)))
    except subprocess.TimeoutExpired:
        proc.kill()
        raise RuntimeError("Audio obrada je prekoračila dozvoljeno vreme.")
    finally:
        stderr_thread.join(timeout=2)
    return return_code, "\n".join(stderr_lines[-20:]), time.monotonic() - started


def process_audio(
    input_source: str | Path,
    output_path: Path,
    options: dict[str, Any],
    progress: Callable[[str, int], None] | None = None,
    cancel_check: Callable[[], bool] | None = None,
    known_info: dict[str, Any] | None = None,
) -> dict[str, Any]:
    ffmpeg = ffmpeg_path()
    if not ffmpeg:
        raise RuntimeError("FFmpeg audio alati nisu spremni.")

    info = dict(known_info or {})
    full_duration = max(0.0, _safe_float(info.get("duration"), 0.0))
    if full_duration <= 0:
        info = probe_audio(input_source)
        full_duration = max(0.0, _safe_float(info.get("duration"), 0.0))

    start = max(0.0, _safe_float(options.get("start"), 0.0))
    end_raw = options.get("end")
    end = _safe_float(end_raw, full_duration) if end_raw not in (None, "") else full_duration
    if full_duration:
        end = min(max(0.0, end), full_duration)
    if end <= start:
        raise ValueError("Kraj mora da bude posle početka isečka.")
    source_clip_duration = end - start

    output_path.parent.mkdir(parents=True, exist_ok=True)
    temp_path = output_path.with_suffix(output_path.suffix + ".part")
    temp_path.unlink(missing_ok=True)
    fmt = str(options.get("format") or output_path.suffix.lstrip(".") or "mp3").lower()
    mode = str(options.get("processing_mode") or "auto").lower()
    effects = _has_effects(options)
    stream_copy = mode in ("quick", "copy", "lossless") or (mode == "auto" and not effects and _copy_compatible(input_source, fmt, info))
    if stream_copy and effects:
        raise ValueError("Brzo isecanje ne može da koristi fade, normalizaciju, promenu brzine, glasnoće, kanala ili sample rate-a.")
    if stream_copy and not _copy_compatible(input_source, fmt, info):
        if mode in ("quick", "copy", "lossless"):
            raise ValueError("Brzo isecanje zahteva isti audio-format kao izvor. Izaberi MP3 za Suno MP3 pesmu ili koristi preciznu obradu.")
        stream_copy = False

    filters: list[str] = []
    output_duration = source_clip_duration
    if not stream_copy:
        if options.get("remove_silence"):
            filters.append("silenceremove=start_periods=1:start_duration=0.08:start_threshold=-50dB:stop_periods=-1:stop_duration=0.12:stop_threshold=-50dB")
        volume_db = max(-30.0, min(30.0, _safe_float(options.get("volume_db"), 0.0)))
        if abs(volume_db) >= 0.05:
            filters.append(f"volume={volume_db:g}dB")
        if options.get("normalize"):
            target = max(-23.0, min(-5.0, _safe_float(options.get("loudness_target"), -14.0)))
            filters.append(f"loudnorm=I={target:g}:TP=-1.5:LRA=11:linear=true")
        speed = max(0.5, min(2.0, _safe_float(options.get("speed"), 1.0)))
        if abs(speed - 1.0) >= 0.001:
            filters.append(f"atempo={speed:g}")
            output_duration /= speed
        fade_in = max(0.0, min(30.0, _safe_float(options.get("fade_in"), 0.0)))
        fade_out = max(0.0, min(30.0, _safe_float(options.get("fade_out"), 0.0)))
        if fade_in > 0:
            filters.append(f"afade=t=in:st=0:d={min(fade_in, output_duration):g}")
        if fade_out > 0 and output_duration > 0:
            fade_start = max(0.0, output_duration - min(fade_out, output_duration))
            filters.append(f"afade=t=out:st={fade_start:g}:d={min(fade_out, output_duration):g}")

    args = [ffmpeg, "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-ss", f"{start:.3f}"]
    args += _input_args(input_source)
    args += ["-t", f"{source_clip_duration:.3f}", "-map", "0:a:0", "-vn", "-map_metadata", "0", "-avoid_negative_ts", "make_zero"]

    if stream_copy:
        args += ["-c:a", "copy"]
    else:
        if filters:
            args += ["-af", ",".join(filters)]
        sample_rate = int(_safe_float(options.get("sample_rate"), 0))
        channels = int(_safe_float(options.get("channels"), 0))
        if sample_rate in (22050, 32000, 44100, 48000):
            args += ["-ar", str(sample_rate)]
        if channels in (1, 2):
            args += ["-ac", str(channels)]
        args += ["-threads", "0"]

    if fmt == "wav":
        if not stream_copy:
            args += ["-c:a", "pcm_s16le"]
        args += ["-f", "wav"]
    elif fmt == "flac":
        if not stream_copy:
            args += ["-c:a", "flac", "-compression_level", "5"]
        args += ["-f", "flac"]
    elif fmt in ("m4a", "aac"):
        if not stream_copy:
            bitrate = str(options.get("bitrate") or "256k")
            args += ["-c:a", "aac", "-b:a", bitrate]
        args += ["-movflags", "+faststart", "-f", "ipod"]
    else:
        if not stream_copy:
            bitrate = str(options.get("bitrate") or "320k")
            if bitrate not in {"128k", "160k", "192k", "224k", "256k", "320k"}:
                bitrate = "320k"
            args += ["-c:a", "libmp3lame", "-b:a", bitrate, "-id3v2_version", "3"]
        args += ["-f", "mp3"]

    args += ["-progress", "pipe:1", "-nostats", str(temp_path)]
    if progress:
        progress("Brzo isecam bez ponovnog kodiranja..." if stream_copy else "Obrađujem audio sa efektima...", 1)
    timeout = max(90.0, source_clip_duration * (1.2 if stream_copy else 3.0) + 60.0)
    return_code, stderr_text, elapsed = _run_ffmpeg_progress(args, output_duration, progress, cancel_check, timeout)
    if return_code != 0 or not temp_path.exists() or temp_path.stat().st_size < 1024:
        temp_path.unlink(missing_ok=True)
        error = stderr_text.strip().splitlines()[-10:] if stderr_text.strip() else ["FFmpeg obrada nije uspela."]
        raise RuntimeError("\n".join(error))
    os.replace(temp_path, output_path)
    if progress:
        progress("Audio obrada je završena.", 100)

    codec_by_format = {"mp3": "mp3", "wav": "pcm_s16le", "flac": "flac", "m4a": "aac", "aac": "aac"}
    return {
        "path": str(output_path),
        "duration": output_duration,
        "size": output_path.stat().st_size,
        "bitrate": int(info.get("bitrate") or 0),
        "bitrate_kbps": int(info.get("bitrate_kbps") or 0),
        "format": fmt,
        "codec": info.get("codec") if stream_copy else codec_by_format.get(fmt, ""),
        "sample_rate": int(info.get("sample_rate") or 0),
        "channels": int(info.get("channels") or 0),
        "options": options,
        "method": "stream_copy" if stream_copy else "reencode",
        "elapsed_seconds": round(elapsed, 3),
    }


def waveform_peaks(
    input_source: str | Path,
    width: int = 1000,
    progress: Callable[[str, int], None] | None = None,
    cancel_check: Callable[[], bool] | None = None,
) -> dict[str, Any]:
    ffmpeg = ffmpeg_path()
    if not ffmpeg:
        raise RuntimeError("FFmpeg audio alati nisu spremni.")
    width = max(200, min(int(width), 3000))
    args = [ffmpeg, "-hide_banner", "-nostdin", "-loglevel", "error"]
    args += _input_args(input_source)
    args += ["-map", "0:a:0", "-vn", "-ac", "1", "-ar", "2000", "-f", "s16le", "pipe:1"]
    kwargs: dict[str, Any] = {"stdout": subprocess.PIPE, "stderr": subprocess.PIPE}
    if os.name == "nt":
        kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW  # type: ignore[attr-defined]
    if progress:
        progress("Pravim brzi talasni prikaz...", 10)
    proc = subprocess.Popen(args, **kwargs)
    chunks: list[bytes] = []
    assert proc.stdout is not None
    while True:
        if cancel_check and cancel_check():
            proc.kill()
            raise AudioCancelled("Učitavanje talasnog prikaza je zaustavljeno.")
        block = proc.stdout.read(65536)
        if not block:
            break
        chunks.append(block)
    stderr = (proc.stderr.read() if proc.stderr else b"").decode("utf-8", errors="replace")
    code = proc.wait(timeout=20)
    raw = b"".join(chunks)
    if code != 0 or len(raw) < 4:
        raise RuntimeError((stderr or "Talasni prikaz nije mogao da se napravi.").strip())
    samples = array("h")
    samples.frombytes(raw[: (len(raw) // 2) * 2])
    if os.sys.byteorder != "little":
        samples.byteswap()
    sample_count = len(samples)
    bucket = max(1, math.ceil(sample_count / width))
    peaks: list[list[float]] = []
    for index in range(width):
        start = index * bucket
        if start >= sample_count:
            peaks.append([0.0, 0.0])
            continue
        section = samples[start:min(sample_count, start + bucket)]
        peaks.append([round(min(section) / 32768.0, 4), round(max(section) / 32768.0, 4)])
    if progress:
        progress("Talasni prikaz je spreman.", 100)
    return {"width": width, "peaks": peaks, "sample_rate": 2000, "samples": sample_count}
