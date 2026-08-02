from __future__ import annotations

import base64
import hashlib
import json
import math
import os
import re
import shutil
import statistics
import subprocess
import tempfile
import time
import urllib.request
import urllib.parse
import zipfile
import zlib
from array import array
from pathlib import Path
from typing import Any, Callable, Iterable

from audio_tools import ffmpeg_path, probe_audio


ROOT = Path(__file__).resolve().parent.parent
TOOLS_DIR = ROOT / "tools" / "yt-dlp"
DENO_DIR = ROOT / "tools" / "deno"
DENO_EXE = DENO_DIR / ("deno.exe" if os.name == "nt" else "deno")
DENO_ASSET_NAME = "deno-x86_64-pc-windows-msvc.zip"
DENO_LATEST_URL = f"https://github.com/denoland/deno/releases/latest/download/{DENO_ASSET_NAME}"
DENO_SHA_URL = DENO_LATEST_URL + ".sha256sum"
YTDLP_EXE = TOOLS_DIR / ("yt-dlp.exe" if os.name == "nt" else "yt-dlp")
YTDLP_ASSET_NAME = "yt-dlp.exe" if os.name == "nt" else "yt-dlp"
YTDLP_LATEST_URL = f"https://github.com/yt-dlp/yt-dlp/releases/latest/download/{YTDLP_ASSET_NAME}"
YTDLP_SUMS_URL = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS"
DATA_DIR = Path(os.environ.get("SUNO_STUDIO_DATA_DIR") or (ROOT / "data")).expanduser().resolve()
CACHE_DIR = DATA_DIR / "youtube_audio_cache"
ALGORITHM_VERSION = "sps-spectral-v3"
FRAME_SECONDS = 0.5
SAMPLE_RATE = 2000


class AudioMatchCancelled(RuntimeError):
    pass


class AudioMatchError(RuntimeError):
    pass


def _creationflags() -> int:
    if os.name == "nt":
        return int(getattr(subprocess, "CREATE_NO_WINDOW", 0))
    return 0


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def ytdlp_path() -> str | None:
    if YTDLP_EXE.exists() and YTDLP_EXE.is_file():
        return str(YTDLP_EXE)
    found = shutil.which("yt-dlp")
    return found or None


def deno_path() -> str | None:
    if DENO_EXE.exists() and DENO_EXE.is_file():
        return str(DENO_EXE)
    found = shutil.which("deno")
    return found or None


def deno_status() -> dict[str, Any]:
    path = deno_path()
    version = ""
    if path:
        try:
            result = subprocess.run([path, "--version"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace", timeout=12, creationflags=_creationflags())
            if result.returncode == 0:
                version = (result.stdout or "").strip().splitlines()[0]
        except Exception:
            pass
    return {"ready": bool(path and version), "path": path or "", "version": version, "portable_dir": str(DENO_DIR)}


def ensure_deno(progress: Callable[[str, int], None] | None = None, force_update: bool = False) -> dict[str, Any]:
    current = deno_status()
    if current["ready"] and not force_update:
        return current
    if os.name != "nt":
        if current["ready"]:
            return current
        raise AudioMatchError("Deno nije pronađen. Instaliraj Deno 2.3 ili noviji.")
    DENO_DIR.mkdir(parents=True, exist_ok=True)
    archive = DENO_DIR / (DENO_ASSET_NAME + ".part")
    checksum = DENO_DIR / (DENO_ASSET_NAME + ".sha256sum")
    try:
        if progress:
            progress("Preuzimam Deno JavaScript runtime...", 0)
        _download(DENO_LATEST_URL, archive, progress)
        _download(DENO_SHA_URL, checksum)
        expected = checksum.read_text(encoding="utf-8", errors="replace").strip().split()[0].lower()
        if not re.fullmatch(r"[0-9a-f]{64}", expected):
            raise AudioMatchError("Deno SHA-256 zapis nije ispravan.")
        if _sha256(archive).lower() != expected:
            raise AudioMatchError("Deno kontrolni zbir nije ispravan.")
        extract = DENO_DIR / "extract"
        shutil.rmtree(extract, ignore_errors=True)
        extract.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(archive) as zf:
            bad = zf.testzip()
            if bad:
                raise AudioMatchError(f"Deno ZIP je oštećen: {bad}")
            zf.extractall(extract)
        found = next(extract.rglob("deno.exe"), None)
        if not found:
            raise AudioMatchError("deno.exe nije pronađen u preuzetom paketu.")
        shutil.copy2(found, DENO_EXE)
        shutil.rmtree(extract, ignore_errors=True)
        archive.unlink(missing_ok=True)
        if progress:
            progress("Deno je spreman.", 100)
    except Exception:
        archive.unlink(missing_ok=True)
        raise
    current = deno_status()
    if not current["ready"]:
        raise AudioMatchError("Deno je preuzet, ali ne može da se pokrene.")
    return current


def ytdlp_status() -> dict[str, Any]:
    path = ytdlp_path()
    version = ""
    if path:
        try:
            result = subprocess.run(
                [path, "--version"], stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                text=True, encoding="utf-8", errors="replace", timeout=12,
                creationflags=_creationflags(),
            )
            if result.returncode == 0:
                version = (result.stdout or "").strip().splitlines()[0]
        except Exception:
            pass
    deno = deno_status()
    return {
        "ready": bool(path and version), "path": path or "", "version": version,
        "portable_dir": str(TOOLS_DIR), "download_mb": 35,
        "deno": deno,
        "purpose": "YouTube pretraga, čitanje metapodataka i audio analiza uz ugrađeni JavaScript runtime.",
    }


def _download(url: str, target: Path, progress: Callable[[str, int], None] | None = None) -> None:
    req = urllib.request.Request(url, headers={"User-Agent": "Suno-Pesme-Studio/1.7"})
    with urllib.request.urlopen(req, timeout=180) as response, target.open("wb") as out:
        total = int(response.headers.get("Content-Length") or 0)
        done = 0
        while True:
            chunk = response.read(1024 * 512)
            if not chunk:
                break
            out.write(chunk)
            done += len(chunk)
            if progress:
                pct = int(done * 100 / total) if total else 0
                progress(f"Preuzimam yt-dlp: {pct}%", min(95, pct))


def ensure_ytdlp(progress: Callable[[str, int], None] | None = None, force_update: bool = False) -> dict[str, Any]:
    current = ytdlp_status()
    if current["ready"] and not force_update:
        ensure_deno(progress)
        return ytdlp_status()
    if os.name != "nt" and current["ready"] and force_update:
        try:
            subprocess.run([str(current["path"]), "-U"], timeout=180, check=False, creationflags=_creationflags())
        except Exception:
            pass
        return ytdlp_status()
    TOOLS_DIR.mkdir(parents=True, exist_ok=True)
    part = TOOLS_DIR / f"{YTDLP_ASSET_NAME}.part"
    sums = TOOLS_DIR / "SHA2-256SUMS"
    try:
        if progress:
            progress("Preuzimam yt-dlp alat (oko 20 MB)...", 0)
        _download(YTDLP_LATEST_URL, part, progress)
        try:
            _download(YTDLP_SUMS_URL, sums)
        except Exception as exc:
            raise AudioMatchError("Ne mogu da preuzmem zvanične SHA-256 zbirove za yt-dlp. Alat nije instaliran jer integritet ne može da se potvrdi.") from exc
        expected = ""
        for line in sums.read_text(encoding="utf-8", errors="replace").splitlines():
            pieces = line.strip().split()
            if len(pieces) >= 2 and pieces[-1].lstrip("*") == YTDLP_ASSET_NAME and re.fullmatch(r"[0-9a-fA-F]{64}", pieces[0]):
                expected = pieces[0].lower()
                break
        if not expected:
            raise AudioMatchError(f"Zvanični SHA-256 za {YTDLP_ASSET_NAME} nije pronađen.")
        if _sha256(part).lower() != expected:
            raise AudioMatchError(f"Kontrolni zbir {YTDLP_ASSET_NAME} nije ispravan. Preuzimanje je obrisano.")
        os.replace(part, YTDLP_EXE)
        if os.name != "nt":
            YTDLP_EXE.chmod(0o755)
        if progress:
            progress("yt-dlp je spreman.", 100)
    except Exception:
        part.unlink(missing_ok=True)
        raise
    current = ytdlp_status()
    if not current["ready"]:
        raise AudioMatchError("yt-dlp je preuzet, ali program ne može da ga pokrene.")
    ensure_deno(progress)
    return ytdlp_status()




def _validated_youtube_url(value: str) -> str:
    raw = str(value or "").strip()
    parsed = urllib.parse.urlparse(raw)
    host = (parsed.hostname or "").lower()
    allowed = host in {"youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be"} or host.endswith(".youtube.com")
    if parsed.scheme not in ("http", "https") or not allowed:
        raise AudioMatchError("Dozvoljen je samo YouTube link (youtube.com ili youtu.be).")
    return raw

def _extract_video_id(url: str) -> str:
    raw = str(url or "")
    patterns = (
        r"[?&]v=([A-Za-z0-9_-]{11})",
        r"youtu\.be/([A-Za-z0-9_-]{11})",
        r"youtube\.com/(?:shorts|embed)/([A-Za-z0-9_-]{11})",
    )
    for pattern in patterns:
        match = re.search(pattern, raw)
        if match:
            return match.group(1)
    digest = hashlib.sha1(raw.encode("utf-8", errors="ignore")).hexdigest()[:12]
    return "video-" + digest


def _cookie_args(cookie_browser: str = "") -> list[str]:
    value = str(cookie_browser or "").strip().lower()
    allowed = {"chrome", "edge", "brave", "firefox", "opera", "vivaldi"}
    return ["--cookies-from-browser", value] if value in allowed else []


def _youtube_runtime_args(cookie_browser: str = "") -> list[str]:
    deno = ensure_deno().get("path")
    args: list[str] = []
    if deno:
        args += ["--js-runtimes", f"deno:{deno}"]
    args += _cookie_args(cookie_browser)
    return args


def _yt_error(lines: list[str], fallback: str) -> str:
    cleaned = [line.strip() for line in lines if line.strip()]
    useful = cleaned[-12:]
    return fallback + ("\n" + "\n".join(useful) if useful else "")


def search_youtube_with_ytdlp(query: str, max_results: int = 20, cookie_browser: str = "") -> list[dict[str, Any]]:
    tool = ensure_ytdlp().get("path")
    if not tool:
        raise AudioMatchError("yt-dlp nije spreman.")
    count = max(1, min(int(max_results or 20), 50))
    args = [
        str(tool), "--flat-playlist", "--dump-single-json", "--skip-download",
        "--socket-timeout", "30", "--retries", "3",
        *_youtube_runtime_args(cookie_browser), f"ytsearch{count}:{str(query or '').strip()}",
    ]
    result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", errors="replace", timeout=240, creationflags=_creationflags())
    if result.returncode != 0:
        raise AudioMatchError(_yt_error((result.stderr or "").splitlines(), "YouTube pretraga preko yt-dlp nije uspela."))
    try:
        payload = json.loads(result.stdout or "{}")
    except Exception as exc:
        raise AudioMatchError("yt-dlp nije vratio ispravan JSON za YouTube pretragu.") from exc
    entries = payload.get("entries") if isinstance(payload, dict) and isinstance(payload.get("entries"), list) else []
    videos: list[dict[str, Any]] = []
    for item in entries:
        if not isinstance(item, dict):
            continue
        vid = str(item.get("id") or "")
        if not vid:
            continue
        raw_url = str(item.get("webpage_url") or item.get("url") or "").strip()
        if not raw_url.startswith(("http://", "https://")):
            raw_url = f"https://www.youtube.com/watch?v={vid}"
        videos.append({
            "video_id": vid,
            "channel_id": str(item.get("channel_id") or item.get("uploader_id") or ""),
            "channel_title": str(item.get("channel") or item.get("uploader") or ""),
            "title": str(item.get("title") or vid),
            "description": str(item.get("description") or ""),
            "published_at": "",
            "url": raw_url,
            "thumbnail_url": str(item.get("thumbnail") or ""),
            "duration": float(item.get("duration") or 0),
            "view_count": int(item.get("view_count") or 0),
            "like_count": int(item.get("like_count") or 0),
            "comment_count": int(item.get("comment_count") or 0),
            "privacy_status": "public",
            "raw_json": item,
        })
    return videos


def download_youtube_audio(
    video_url: str,
    progress: Callable[[str, int], None] | None = None,
    cancel_check: Callable[[], bool] | None = None,
    reuse_cache: bool = True,
    cookie_browser: str = "",
) -> Path:
    """Download audio from a user-owned/public YouTube upload into a local cache."""
    video_url = _validated_youtube_url(video_url)
    tool = ensure_ytdlp(progress).get("path")
    if not tool:
        raise AudioMatchError("yt-dlp nije spreman.")
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    video_id = _extract_video_id(video_url)
    existing = sorted(CACHE_DIR.glob(f"{video_id}.*"))
    existing = [p for p in existing if p.is_file() and p.suffix.lower() not in (".part", ".json") and p.stat().st_size > 1024]
    if reuse_cache and existing:
        return existing[0]
    if not reuse_cache:
        for stale in CACHE_DIR.glob(f"{video_id}.*"):
            if stale.is_file():
                stale.unlink(missing_ok=True)
    template = str(CACHE_DIR / f"{video_id}.%(ext)s")
    args = [
        str(tool), "--no-playlist", "--newline", "--continue", "--force-overwrites",
        "--retries", "7", "--fragment-retries", "7", "--socket-timeout", "30",
        "--sleep-requests", "1", "--sleep-interval", "1", "--max-sleep-interval", "3",
        "--format", "bestaudio/best", "--output", template,
        "--print", "after_move:filepath", *_youtube_runtime_args(cookie_browser), str(video_url),
    ]
    proc = subprocess.Popen(
        args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        text=True, encoding="utf-8", errors="replace", bufsize=1,
        creationflags=_creationflags(),
    )
    printed_path = ""
    lines: list[str] = []
    try:
        if proc.stdout is not None:
            for raw in proc.stdout:
                if cancel_check and cancel_check():
                    proc.terminate()
                    try:
                        proc.wait(timeout=3)
                    except subprocess.TimeoutExpired:
                        proc.kill()
                    raise AudioMatchCancelled("YouTube audio analiza je zaustavljena.")
                line = raw.strip()
                if not line:
                    continue
                lines.append(line)
                lines = lines[-30:]
                pct_match = re.search(r"\[download\]\s+([0-9.]+)%", line)
                if pct_match and progress:
                    progress(f"Preuzimam YouTube audio: {pct_match.group(1)}%", min(90, int(float(pct_match.group(1)) * 0.9)))
                candidate = Path(line.strip('"'))
                if candidate.exists() and candidate.is_file():
                    printed_path = str(candidate)
        code = proc.wait(timeout=600)
    except Exception:
        if proc.poll() is None:
            proc.kill()
        raise
    if code != 0:
        raise AudioMatchError(_yt_error(lines, f"yt-dlp nije preuzeo zvuk (kod {code})."))
    if printed_path:
        path = Path(printed_path)
        if path.exists() and path.stat().st_size > 1024:
            return path
    existing = sorted(CACHE_DIR.glob(f"{video_id}.*"))
    existing = [p for p in existing if p.is_file() and p.suffix.lower() not in (".part", ".json") and p.stat().st_size > 1024]
    if not existing:
        raise AudioMatchError("YouTube audio je navodno preuzet, ali fajl nije pronađen.")
    return existing[0]



def inspect_youtube_video(video_url: str, cancel_check: Callable[[], bool] | None = None, cookie_browser: str = "") -> dict[str, Any]:
    """Read public YouTube metadata with yt-dlp without downloading the media."""
    video_url = _validated_youtube_url(video_url)
    tool = ensure_ytdlp().get("path")
    if not tool:
        raise AudioMatchError("yt-dlp nije spreman.")
    args = [
        str(tool), "--no-playlist", "--skip-download",
        "--dump-single-json", *_youtube_runtime_args(cookie_browser), str(video_url),
    ]
    proc = subprocess.Popen(
        args, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        text=True, encoding="utf-8", errors="replace",
        creationflags=_creationflags(),
    )
    try:
        while proc.poll() is None:
            if cancel_check and cancel_check():
                proc.terminate()
                raise AudioMatchCancelled("YouTube provera je zaustavljena.")
            time.sleep(0.1)
        stdout, stderr = proc.communicate(timeout=30)
    except Exception:
        if proc.poll() is None:
            proc.kill()
        raise
    if proc.returncode != 0:
        raise AudioMatchError((stderr or "Ne mogu da pročitam YouTube video.").strip().splitlines()[-1])
    try:
        data = json.loads(stdout)
    except Exception as exc:
        raise AudioMatchError("yt-dlp nije vratio ispravne podatke o videu.") from exc
    upload_date = str(data.get("upload_date") or "")
    published_at = ""
    if len(upload_date) == 8 and upload_date.isdigit():
        published_at = f"{upload_date[:4]}-{upload_date[4:6]}-{upload_date[6:8]}T00:00:00Z"
    video_id = str(data.get("id") or _extract_video_id(video_url))
    channel_id = str(data.get("channel_id") or data.get("uploader_id") or "")
    channel_title = str(data.get("channel") or data.get("uploader") or "")
    thumbnail = str(data.get("thumbnail") or "")
    return {
        "video_id": video_id,
        "title": str(data.get("title") or video_id),
        "description": str(data.get("description") or ""),
        "channel_id": channel_id,
        "channel_title": channel_title,
        "published_at": published_at,
        "duration": float(data.get("duration") or 0),
        "view_count": int(data.get("view_count") or 0),
        "like_count": int(data.get("like_count") or 0),
        "comment_count": int(data.get("comment_count") or 0),
        "thumbnail_url": thumbnail,
        "url": str(data.get("webpage_url") or video_url),
        "is_live": bool(data.get("is_live")),
        "availability": str(data.get("availability") or ""),
        "extractor": str(data.get("extractor_key") or data.get("extractor") or "youtube"),
    }

def _median(values: list[float]) -> float:
    return statistics.median(values) if values else 0.0


def _normalize_columns(frames: list[list[float]]) -> list[list[float]]:
    if not frames:
        return []
    cols = len(frames[0])
    medians: list[float] = []
    scales: list[float] = []
    for col in range(cols):
        values = [row[col] for row in frames]
        med = _median(values)
        deviations = [abs(v - med) for v in values]
        mad = _median(deviations)
        if mad < 1e-6:
            mean = sum(values) / len(values)
            variance = sum((v - mean) ** 2 for v in values) / max(1, len(values) - 1)
            mad = math.sqrt(variance) or 1.0
            med = mean
        medians.append(med)
        scales.append(max(1e-6, mad * 1.4826))
    return [[max(-5.0, min(5.0, (row[c] - medians[c]) / scales[c])) for c in range(cols)] for row in frames]


def _goertzel_log_power(frame: array, frequency: float, sample_rate: int) -> float:
    """Small dependency-free spectral probe used by the local fingerprint."""
    omega = 2.0 * math.pi * frequency / sample_rate
    coeff = 2.0 * math.cos(omega)
    s0 = s1 = s2 = 0.0
    for raw in frame:
        s0 = float(raw) + coeff * s1 - s2
        s2 = s1
        s1 = s0
    power = max(0.0, s1 * s1 + s2 * s2 - coeff * s1 * s2)
    return math.log1p(power)


def _frame_features(samples: array, frame_size: int) -> list[list[float]]:
    frames: list[list[float]] = []
    for start in range(0, len(samples) - frame_size + 1, frame_size):
        frame = samples[start:start + frame_size]
        if not frame:
            continue
        squares = 0.0
        diffs = 0.0
        crossings = 0
        peak = 0
        prev = int(frame[0])
        quarters = [0.0, 0.0, 0.0, 0.0]
        qsize = max(1, frame_size // 4)
        for idx, value_raw in enumerate(frame):
            value = int(value_raw)
            av = abs(value)
            peak = max(peak, av)
            squares += value * value
            if idx:
                diffs += abs(value - prev)
                if (value >= 0) != (prev >= 0):
                    crossings += 1
            quarters[min(3, idx // qsize)] += value * value
            prev = value
        rms = math.sqrt(squares / len(frame)) + 1.0
        rough = diffs / max(1, len(frame) - 1) + 1.0
        zcr = crossings / max(1, len(frame) - 1)
        crest = peak / rms
        q_rms = [math.sqrt(v / qsize + 1.0) / rms for v in quarters]
        # Coarse spectral landmarks make the signature far less likely to confuse
        # two songs with a similar loudness envelope or beat pattern.
        spectral_frequencies = (70.0, 110.0, 170.0, 250.0, 360.0, 500.0, 700.0, 900.0)
        spectral = [_goertzel_log_power(frame, freq, SAMPLE_RATE) for freq in spectral_frequencies]
        frames.append([math.log(rms), math.log(rough), zcr * 20.0, crest, *q_rms, *spectral])
    normalized = _normalize_columns(frames)
    # Delta information is more robust to loudness normalization than absolute levels.
    result: list[list[float]] = []
    previous = normalized[0] if normalized else []
    for row in normalized:
        deltas = [row[i] - previous[i] for i in range(len(row))]
        result.append(row + deltas)
        previous = row
    return result


def _extract_chromaprint(ffmpeg: str, source: str) -> list[int]:
    """Use FFmpeg's bundled Chromaprint muxer when available."""
    try:
        result = subprocess.run(
            [ffmpeg, "-hide_banner", "-loglevel", "error", "-i", source, "-vn", "-f", "chromaprint", "-fp_format", "raw", "pipe:1"],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=180,
            creationflags=_creationflags(),
        )
        if result.returncode != 0 or len(result.stdout) < 16:
            return []
        payload = result.stdout[: len(result.stdout) // 4 * 4]
        values = array("I")
        values.frombytes(payload)
        if os.sys.byteorder != "little":
            values.byteswap()
        return [int(v) for v in values]
    except Exception:
        return []


def _chromaprint_bit_similarity(a: int, b: int) -> float:
    return 1.0 - ((int(a) ^ int(b)).bit_count() / 32.0)


def _resample_int_sequence(seq: list[int], new_len: int) -> list[int]:
    """Nearest-neighbor resample, used to test a global speed/tempo
    hypothesis: a clip played back at T times the original speed has its
    fingerprint sequence compressed/expanded by T relative to the source,
    which a fixed 1:1 frame-offset search can't compensate for on its own."""
    if new_len <= 0 or not seq:
        return []
    if new_len == len(seq):
        return list(seq)
    scale = len(seq) / new_len
    return [seq[min(len(seq) - 1, int(i * scale))] for i in range(new_len)]


# A modest global speed change is common (Shorts/reels sped up or slowed
# down for pacing) and would otherwise silently defeat the fixed 1:1
# frame-offset comparison below. 1.0 (no change) is tried first and alone
# covers the overwhelming majority of real matches cheaply; the other
# hypotheses only run when that pass didn't already find a solid match, to
# avoid paying this ~5x search cost on every ordinary comparison.
_TEMPO_HYPOTHESES = (1.0, 1.03, 0.97, 1.08, 0.93)
_TEMPO_HYPOTHESIS_GOOD_ENOUGH_RANK = 0.60


def _chromaprint_offset_search(src: list[int], vid: list[int], src_step: float, vid_step: float, min_frames: int, tempo_hint: float) -> list[dict[str, Any]]:
    candidates: list[dict[str, Any]] = []
    for offset in range(-len(src) + min_frames, len(vid) - min_frames + 1):
        src_start = max(0, -offset)
        vid_start = max(0, offset)
        overlap = min(len(src) - src_start, len(vid) - vid_start)
        if overlap < min_frames:
            continue
        sims = [_chromaprint_bit_similarity(src[src_start + i], vid[vid_start + i]) for i in range(overlap)]
        seg_start, seg_end, seg_quality = _best_segment(sims, threshold=0.68, gap_tolerance=10)
        seg_len = seg_end - seg_start
        if seg_len < min_frames:
            continue
        mean_all = sum(sims[seg_start:seg_end]) / seg_len
        length_factor = min(1.0, math.sqrt(seg_len / max(min_frames, len(src) * 0.55)))
        rank = mean_all * (0.72 + 0.28 * length_factor)
        source_index = src_start + seg_start
        video_index = vid_start + seg_start
        duration_sec = min(seg_len * src_step, seg_len * vid_step)
        candidates.append({
            "rank": rank, "quality": mean_all, "segment_quality": seg_quality,
            "source_frame_start": source_index, "video_frame_start": video_index,
            "frames": seg_len, "source_start": source_index * src_step,
            "source_end": source_index * src_step + duration_sec,
            "video_start": video_index * vid_step, "video_end": video_index * vid_step + duration_sec,
            "matched_seconds": duration_sec, "metric": "chromaprint", "tempo_hint": tempo_hint,
        })
    return candidates


def _chromaprint_candidates(source: dict[str, Any], video: dict[str, Any]) -> list[dict[str, Any]]:
    src = [int(x) for x in (source.get("chromaprint") or [])]
    vid_original = [int(x) for x in (video.get("chromaprint") or [])]
    if len(src) < 80 or len(vid_original) < 80:
        return []
    source_duration = float(source.get("duration") or 0)
    video_duration = float(video.get("duration") or 0)
    src_step = source_duration / len(src) if source_duration > 0 else 0.128
    min_frames = max(60, int(10.0 / max(src_step, 0.05)))

    baseline_vid_step = video_duration / len(vid_original) if video_duration > 0 else src_step
    candidates = _chromaprint_offset_search(src, vid_original, src_step, baseline_vid_step, min_frames, 1.0)
    if candidates and max(float(c["rank"]) for c in candidates) >= _TEMPO_HYPOTHESIS_GOOD_ENOUGH_RANK:
        return candidates

    for hypothesis in _TEMPO_HYPOTHESES:
        if hypothesis == 1.0:
            continue
        resampled = _resample_int_sequence(vid_original, max(80, round(len(vid_original) * hypothesis)))
        if len(resampled) < 80:
            continue
        vid_step = video_duration / len(resampled) if video_duration > 0 else src_step
        candidates.extend(_chromaprint_offset_search(src, resampled, src_step, vid_step, min_frames, hypothesis))
    return candidates


def extract_signature(
    source: str | Path,
    progress: Callable[[str, int], None] | None = None,
    cancel_check: Callable[[], bool] | None = None,
) -> dict[str, Any]:
    ffmpeg = ffmpeg_path()
    if not ffmpeg:
        raise AudioMatchError("FFmpeg nije spreman za audio prepoznavanje.")
    source_str = str(source)
    args = [ffmpeg, "-hide_banner", "-loglevel", "error", "-i", source_str, "-vn", "-ac", "1", "-ar", str(SAMPLE_RATE), "-f", "s16le", "pipe:1"]
    proc = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=_creationflags())
    chunks: list[bytes] = []
    total = 0
    last_report = 0
    try:
        if proc.stdout is None:
            raise AudioMatchError("FFmpeg nije otvorio audio tok.")
        while True:
            if cancel_check and cancel_check():
                proc.terminate()
                raise AudioMatchCancelled("Audio prepoznavanje je zaustavljeno.")
            chunk = proc.stdout.read(1024 * 256)
            if not chunk:
                break
            chunks.append(chunk)
            total += len(chunk)
            if progress and total - last_report >= 1024 * 512:
                last_report = total
                seconds = total / 2 / SAMPLE_RATE
                progress(f"Pravim audio otisak: {seconds:.0f} s", min(85, int(seconds / 300 * 85)))
        stderr = (proc.stderr.read() if proc.stderr else b"").decode("utf-8", errors="replace")
        code = proc.wait(timeout=60)
    except Exception:
        if proc.poll() is None:
            proc.kill()
        raise
    if code != 0:
        raise AudioMatchError((stderr or "FFmpeg nije mogao da pročita audio.").strip())
    raw = b"".join(chunks)
    if len(raw) < SAMPLE_RATE * 2 * 8:
        raise AudioMatchError("Audio je prekratak za pouzdano prepoznavanje.")
    if len(raw) % 2:
        raw = raw[:-1]
    samples = array("h")
    samples.frombytes(raw)
    if os.sys.byteorder != "little":
        samples.byteswap()
    frame_size = int(SAMPLE_RATE * FRAME_SECONDS)
    features = _frame_features(samples, frame_size)
    duration = len(samples) / SAMPLE_RATE
    if len(features) < 16:
        raise AudioMatchError("Nema dovoljno audio okvira za poređenje.")
    chromaprint = _extract_chromaprint(ffmpeg, source_str)
    if progress:
        progress("Audio otisak je spreman.", 100)
    return {
        "algorithm": ALGORITHM_VERSION,
        "duration": duration,
        "interval": FRAME_SECONDS,
        "sample_rate": SAMPLE_RATE,
        "features": features,
        "frame_count": len(features),
        "chromaprint": chromaprint,
        "chromaprint_count": len(chromaprint),
        "chromaprint_interval": (duration / len(chromaprint)) if chromaprint else 0.0,
    }


def pack_signature(signature: dict[str, Any]) -> bytes:
    raw = json.dumps(signature, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    return zlib.compress(raw, level=9)


def unpack_signature(payload: bytes | str) -> dict[str, Any]:
    if isinstance(payload, str):
        try:
            payload_b = base64.b64decode(payload)
        except Exception:
            payload_b = payload.encode("latin1", errors="ignore")
    else:
        payload_b = payload
    try:
        raw = zlib.decompress(payload_b)
    except zlib.error:
        raw = payload_b
    data = json.loads(raw.decode("utf-8"))
    if not isinstance(data, dict) or not isinstance(data.get("features"), list):
        raise AudioMatchError("Sačuvani audio otisak nije ispravan.")
    return data


def _frame_similarity(a: list[float], b: list[float]) -> float:
    n = min(len(a), len(b))
    if n == 0:
        return 0.0
    # Base vector: 4 envelope values, 4 quarter-energy values and 8 coarse
    # spectral probes. The second half contains their deltas.
    base = [0.9, 0.8, 0.25, 0.25, 0.45, 0.45, 0.45, 0.45] + [1.45] * 8
    weights = base + [w * 1.18 for w in base]
    weights = (weights + [1.0] * n)[:n]
    distance = sum(w * abs(a[i] - b[i]) for i, w in enumerate(weights)) / max(1e-9, sum(weights))
    return math.exp(-distance / 1.55)


def _smooth(values: list[float], radius: int = 2) -> list[float]:
    if not values:
        return []
    prefix = [0.0]
    for value in values:
        prefix.append(prefix[-1] + value)
    out: list[float] = []
    for i in range(len(values)):
        left = max(0, i - radius)
        right = min(len(values), i + radius + 1)
        out.append((prefix[right] - prefix[left]) / (right - left))
    return out


def _best_segment(similarities: list[float], threshold: float = 0.54, gap_tolerance: int = 4) -> tuple[int, int, float]:
    smoothed = _smooth(similarities, 2)
    best_start = best_end = 0
    best_quality = 0.0
    start: int | None = None
    last_good = -1
    for idx, value in enumerate(smoothed):
        if value >= threshold:
            if start is None:
                start = idx
            last_good = idx
        elif start is not None and idx - last_good > gap_tolerance:
            end = last_good + 1
            quality = sum(smoothed[start:end]) / max(1, end - start)
            if (end - start, quality) > (best_end - best_start, best_quality):
                best_start, best_end, best_quality = start, end, quality
            start = None
    if start is not None:
        end = last_good + 1
        quality = sum(smoothed[start:end]) / max(1, end - start)
        if (end - start, quality) > (best_end - best_start, best_quality):
            best_start, best_end, best_quality = start, end, quality
    return best_start, best_end, best_quality


def _interval_overlap_ratio(a0: float, a1: float, b0: float, b1: float) -> float:
    inter = max(0.0, min(a1, b1) - max(a0, b0))
    base = min(max(0.0, a1 - a0), max(0.0, b1 - b0))
    return inter / base if base > 1e-9 else 0.0


def _merge_intervals(intervals: list[tuple[float, float]], gap_tolerance: float = 1.5) -> list[tuple[float, float]]:
    cleaned = sorted((max(0.0, float(a)), max(0.0, float(b))) for a, b in intervals if float(b) > float(a))
    merged: list[list[float]] = []
    for start, end in cleaned:
        if not merged or start > merged[-1][1] + gap_tolerance:
            merged.append([start, end])
        else:
            merged[-1][1] = max(merged[-1][1], end)
    return [(round(a, 3), round(b, 3)) for a, b in merged]


def _missing_intervals(covered: list[tuple[float, float]], duration: float, min_gap: float = 0.75) -> list[tuple[float, float]]:
    gaps: list[tuple[float, float]] = []
    cursor = 0.0
    for start, end in covered:
        if start - cursor >= min_gap:
            gaps.append((round(cursor, 3), round(start, 3)))
        cursor = max(cursor, end)
    if duration - cursor >= min_gap:
        gaps.append((round(cursor, 3), round(duration, 3)))
    return gaps


def compare_signatures(source: dict[str, Any], video: dict[str, Any]) -> dict[str, Any]:
    """Compare two compact signatures and report every reliable matching segment.

    v2 keeps the previous best-match behaviour, but also returns non-overlapping
    segments. This allows the program to recognise mixes, repeated songs and a
    full song assembled from several excerpts inside one YouTube video.
    """
    src = source.get("features") or []
    vid = video.get("features") or []
    interval = float(source.get("interval") or FRAME_SECONDS)
    if not src or not vid:
        raise AudioMatchError("Nedostaje audio otisak za poređenje.")
    min_frames = max(20, int(12 / interval))
    raw_candidates: list[dict[str, Any]] = _chromaprint_candidates(source, video)
    if not raw_candidates:
        # Dependency-free fallback for FFmpeg builds without Chromaprint.
        # offset = video_index - source_index
        for offset in range(-len(src) + min_frames, len(vid) - min_frames + 1):
            src_start = max(0, -offset)
            vid_start = max(0, offset)
            overlap = min(len(src) - src_start, len(vid) - vid_start)
            if overlap < min_frames:
                continue
            sims = [_frame_similarity(src[src_start + i], vid[vid_start + i]) for i in range(overlap)]
            seg_start, seg_end, seg_quality = _best_segment(sims)
            seg_len = seg_end - seg_start
            if seg_len < min_frames:
                continue
            mean_all = sum(sims[seg_start:seg_end]) / seg_len
            length_factor = min(1.0, math.sqrt(seg_len / max(min_frames, len(src) * 0.6)))
            rank = mean_all * (0.72 + 0.28 * length_factor)
            source_frame_start = src_start + seg_start
            video_frame_start = vid_start + seg_start
            source_sec = source_frame_start * interval
            video_sec = video_frame_start * interval
            duration_sec = seg_len * interval
            raw_candidates.append({
                "rank": rank, "quality": mean_all, "segment_quality": seg_quality,
                "source_frame_start": source_frame_start, "video_frame_start": video_frame_start,
                "frames": seg_len, "source_start": source_sec, "source_end": source_sec + duration_sec,
                "video_start": video_sec, "video_end": video_sec + duration_sec,
                "matched_seconds": duration_sec, "metric": "envelope",
            })

    source_duration = float(source.get("duration") or len(src) * interval)
    video_duration = float(video.get("duration") or len(vid) * interval)
    if not raw_candidates:
        return {
            "audio_score": 0.0, "coverage_percent": 0.0, "matched_seconds": 0.0,
            "total_matched_seconds": 0.0, "covered_seconds": 0.0,
            "completeness_status": "different_audio", "confidence": "low",
            "source_start": 0.0, "source_end": 0.0, "video_start": 0.0, "video_end": 0.0,
            "missing_start": source_duration, "missing_end": 0.0, "internal_gap_seconds": 0.0,
            "has_intro": False, "has_outro": False, "segments": [], "missing_intervals": [[0.0, round(source_duration, 2)]],
            "segment_count": 0, "occurrence_count": 0,
            "reason": "Nije pronađen dovoljno dug podudarni audio segment.",
            "analysis_version": ALGORITHM_VERSION,
        }

    raw_candidates.sort(key=lambda c: (float(c["rank"]), int(c["frames"])), reverse=True)
    selected: list[dict[str, Any]] = []
    for candidate in raw_candidates:
        # Convert similarity into a conservative percentage before accepting it.
        if str(candidate.get("metric") or "") == "chromaprint":
            score = max(0.0, min(100.0, (float(candidate["quality"]) - 0.80) / 0.18 * 100.0))
        else:
            score = max(0.0, min(100.0, (float(candidate["quality"]) - 0.34) / 0.48 * 100.0))
        if score < 46 or float(candidate["matched_seconds"]) < 12:
            continue
        conflicting_alignment = False
        for prior in selected:
            video_overlap = _interval_overlap_ratio(
                float(candidate["video_start"]), float(candidate["video_end"]),
                float(prior["video_start"]), float(prior["video_end"]),
            )
            # The same seconds from the YouTube video must never be reused to
            # claim several different parts of the Suno original. Repetitive
            # choruses can otherwise make a 30-second Short look like a full
            # song. Separate YouTube occurrences are still accepted, so repeated
            # songs and mixes continue to work.
            if video_overlap >= 0.48:
                conflicting_alignment = True
                break
        if conflicting_alignment:
            continue
        item = dict(candidate)
        item["audio_score"] = round(score, 1)
        selected.append(item)
        if len(selected) >= 12:
            break

    if not selected:
        selected = [dict(raw_candidates[0])]
        if str(selected[0].get("metric") or "") == "chromaprint":
            selected[0]["audio_score"] = max(0.0, min(100.0, (float(selected[0]["quality"]) - 0.80) / 0.18 * 100.0))
        else:
            selected[0]["audio_score"] = max(0.0, min(100.0, (float(selected[0]["quality"]) - 0.34) / 0.48 * 100.0))

    reliable = [x for x in selected if float(x.get("audio_score") or 0) >= 48 and float(x.get("matched_seconds") or 0) >= 10]
    if not reliable:
        reliable = selected[:1]

    covered = _merge_intervals([
        (min(source_duration, float(x["source_start"])), min(source_duration, float(x["source_end"])))
        for x in reliable
    ])
    covered_seconds = sum(max(0.0, end - start) for start, end in covered)
    gaps = _missing_intervals(covered, source_duration)
    missing_start = gaps[0][1] - gaps[0][0] if gaps and gaps[0][0] <= 0.01 else 0.0
    missing_end = gaps[-1][1] - gaps[-1][0] if gaps and gaps[-1][1] >= source_duration - 0.01 else 0.0
    internal_gap_seconds = sum(end - start for start, end in gaps if start > 0.01 and end < source_duration - 0.01)
    coverage = min(100.0, covered_seconds / max(interval, source_duration) * 100.0)
    primary = max(reliable, key=lambda x: (float(x.get("rank") or 0), float(x.get("matched_seconds") or 0)))
    audio_score = max(float(x.get("audio_score") or 0) for x in reliable)
    source_start = covered[0][0] if covered else 0.0
    source_end = covered[-1][1] if covered else 0.0
    # Use the earliest and latest reliable video positions for intro/outro detection.
    video_start = min(float(x["video_start"]) for x in reliable)
    video_end = max(float(x["video_end"]) for x in reliable)
    has_intro = video_start >= 4.0
    has_outro = video_duration - video_end >= 4.0
    total_matched_seconds = sum(float(x.get("matched_seconds") or 0) for x in reliable)

    # Count repeated occurrences: similar source range, separate YouTube range.
    occurrence_count = 1
    primary_src = (float(primary["source_start"]), float(primary["source_end"]))
    repeat_count = 0
    for item in reliable:
        if _interval_overlap_ratio(primary_src[0], primary_src[1], float(item["source_start"]), float(item["source_end"])) >= 0.75:
            repeat_count += 1
    occurrence_count = max(1, repeat_count)

    if audio_score < 48 or covered_seconds < 10:
        status = "different_audio"
    elif coverage >= 92 and missing_start <= max(6.0, source_duration * 0.04) and missing_end <= max(8.0, source_duration * 0.06) and internal_gap_seconds <= max(4.0, source_duration * 0.03):
        status = "complete"
    elif coverage >= 80:
        status = "almost_complete"
    elif covered_seconds <= 70 or coverage < 35:
        status = "short_clip"
    else:
        status = "partial"
    if audio_score >= 78 and covered_seconds >= 45:
        confidence = "high"
    elif audio_score >= 60 and covered_seconds >= 25:
        confidence = "medium"
    else:
        confidence = "low"

    labels = {
        "complete": "kompletna pesma",
        "almost_complete": "skoro kompletna pesma",
        "partial": "delimično objavljena pesma",
        "short_clip": "kratak isečak",
        "different_audio": "drugačiji ili nepotvrđen audio",
    }
    reason = (
        f"Audio: {audio_score:.0f}%, pokrivenost originala: {coverage:.1f}%, "
        f"pokriveno: {covered_seconds:.1f} s kroz {len(reliable)} segment(a), "
        f"rezultat: {labels.get(status, status)}."
    )
    segment_payload = []
    for item in reliable:
        segment_payload.append({
            "source_start": round(float(item["source_start"]), 2),
            "source_end": round(min(source_duration, float(item["source_end"])), 2),
            "video_start": round(float(item["video_start"]), 2),
            "video_end": round(min(video_duration, float(item["video_end"])), 2),
            "matched_seconds": round(float(item["matched_seconds"]), 2),
            "audio_score": round(float(item.get("audio_score") or 0), 1),
        })
    return {
        "audio_score": round(audio_score, 1), "coverage_percent": round(coverage, 1),
        "matched_seconds": round(covered_seconds, 2), "total_matched_seconds": round(total_matched_seconds, 2),
        "covered_seconds": round(covered_seconds, 2), "completeness_status": status,
        "confidence": confidence, "source_start": round(source_start, 2), "source_end": round(source_end, 2),
        "video_start": round(video_start, 2), "video_end": round(video_end, 2),
        "missing_start": round(missing_start, 2), "missing_end": round(missing_end, 2),
        "internal_gap_seconds": round(internal_gap_seconds, 2),
        "has_intro": has_intro, "has_outro": has_outro,
        "segments": segment_payload,
        "missing_intervals": [[round(a, 2), round(b, 2)] for a, b in gaps],
        "segment_count": len(segment_payload), "occurrence_count": occurrence_count,
        "reason": reason, "analysis_version": ALGORITHM_VERSION,
    }

def source_identity(source: str | Path) -> dict[str, Any]:
    raw = str(source)
    if raw.startswith("http://") or raw.startswith("https://"):
        return {"mtime": 0.0, "size": 0, "identity": hashlib.sha256(raw.encode("utf-8")).hexdigest()}
    path = Path(raw)
    stat = path.stat()
    return {"mtime": stat.st_mtime, "size": stat.st_size, "identity": _sha256(path)}


def analyze_audio_pair(
    source: str | Path,
    video_audio: str | Path,
    progress: Callable[[str, int], None] | None = None,
    cancel_check: Callable[[], bool] | None = None,
    source_signature: dict[str, Any] | None = None,
    video_signature: dict[str, Any] | None = None,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    if source_signature is None:
        source_signature = extract_signature(source, lambda m, p: progress(f"Suno original: {m}", int(p * 0.45)) if progress else None, cancel_check)
    if video_signature is None:
        video_signature = extract_signature(video_audio, lambda m, p: progress(f"YouTube video: {m}", 45 + int(p * 0.45)) if progress else None, cancel_check)
    if progress:
        progress("Upoređujem audio otiske...", 93)
    result = compare_signatures(source_signature, video_signature)
    if progress:
        progress("Audio poređenje je završeno.", 100)
    return result, source_signature, video_signature


def closest_duration_candidates(songs: Iterable[dict[str, Any]], video_duration: float, limit: int = 12) -> list[dict[str, Any]]:
    rows = list(songs)
    if video_duration <= 0:
        return rows[:limit]
    def key(song: dict[str, Any]) -> tuple[float, float]:
        duration = float(song.get("duration") or 0)
        if duration <= 0:
            return (999.0, 999.0)
        ratio_penalty = abs(math.log(max(1.0, video_duration) / max(1.0, duration)))
        return ratio_penalty, abs(video_duration - duration)
    return sorted(rows, key=key)[:max(1, limit)]


def cleanup_youtube_audio_cache(max_age_days: int = 30, max_size_gb: float = 10.0) -> dict[str, Any]:
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    files = [p for p in CACHE_DIR.iterdir() if p.is_file()]
    now = time.time()
    removed = 0
    freed = 0
    for path in files:
        try:
            if now - path.stat().st_mtime > max_age_days * 86400:
                freed += path.stat().st_size
                path.unlink()
                removed += 1
        except OSError:
            pass
    remaining = sorted([p for p in CACHE_DIR.iterdir() if p.is_file()], key=lambda p: p.stat().st_mtime)
    total = sum(p.stat().st_size for p in remaining)
    limit = int(max_size_gb * 1024 ** 3)
    for path in remaining:
        if total <= limit:
            break
        try:
            size = path.stat().st_size
            path.unlink()
            total -= size
            freed += size
            removed += 1
        except OSError:
            pass
    return {"removed": removed, "freed_bytes": freed, "remaining_bytes": max(0, total), "folder": str(CACHE_DIR)}
