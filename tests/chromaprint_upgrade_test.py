"""An existing install whose FFmpeg has no Chromaprint muxer must be upgraded.

Without this, ensure_ffmpeg() saw a working FFmpeg, reported "ready", and
returned early forever -- so every existing installation kept the old
essentials build that cannot recognise a re-encoded Shorts clip, no matter
how many times the user clicked "prepare tools".
"""
from __future__ import annotations
import json, sys
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import audio_tools
import audio_match


def main():
    checks = []

    assert "full" in audio_tools.FFMPEG_URL and "essentials" not in audio_tools.FFMPEG_URL
    checks.append("FFMPEG_URL points at the full build (has Chromaprint), not essentials")
    assert audio_tools.FFMPEG_SHA_URL == audio_tools.FFMPEG_URL + ".sha256"
    checks.append("the SHA-256 URL still tracks the archive URL, so integrity checking is unchanged")

    # -- a working FFmpeg WITHOUT chromaprint must trigger a re-download --
    ready_no_chroma = {"ready": True, "chromaprint": False, "ffmpeg": "x", "ffprobe": "y", "version": "v"}
    downloaded = {"n": 0}

    def fake_download(url, target, progress=None):
        downloaded["n"] += 1
        raise RuntimeError("stop-after-download-attempt")

    with patch.object(audio_tools, "status", return_value=ready_no_chroma), \
         patch.object(audio_tools.os, "name", "nt"), \
         patch.object(audio_tools, "_download", side_effect=fake_download):
        try:
            audio_tools.ensure_ffmpeg()
        except RuntimeError:
            pass
    assert downloaded["n"] == 1, "a chromaprint-less FFmpeg must be re-downloaded, not silently kept"
    checks.append("ensure_ffmpeg() DOES re-download when the installed FFmpeg lacks the Chromaprint muxer")

    # -- a working FFmpeg WITH chromaprint must NOT be re-downloaded --
    ready_with_chroma = {"ready": True, "chromaprint": True, "ffmpeg": "x", "ffprobe": "y", "version": "v"}
    downloaded["n"] = 0
    with patch.object(audio_tools, "status", return_value=ready_with_chroma), \
         patch.object(audio_tools, "_download", side_effect=fake_download):
        result = audio_tools.ensure_ffmpeg()
    assert downloaded["n"] == 0, "a good FFmpeg must not be re-downloaded on every call"
    assert result is ready_with_chroma
    checks.append("a Chromaprint-capable FFmpeg is left alone (no pointless 145 MB re-download)")

    # -- has_chromaprint() must parse real `ffmpeg -muxers` output --
    class R:
        def __init__(self, out): self.stdout = out; self.returncode = 0
    real_line = " E chromaprint     Chromaprint"
    with patch.object(audio_tools, "_run", return_value=R("Muxers:\n" + real_line + "\n E matroska  Matroska\n")):
        audio_tools._CHROMAPRINT_CACHE.clear()
        assert audio_tools.has_chromaprint("/fake/ffmpeg") is True
    checks.append("has_chromaprint() detects the real 'E chromaprint' muxer line")
    with patch.object(audio_tools, "_run", return_value=R("Muxers:\n E matroska  Matroska\n E mp4  MP4\n")):
        audio_tools._CHROMAPRINT_CACHE.clear()
        assert audio_tools.has_chromaprint("/fake/ffmpeg") is False
    checks.append("has_chromaprint() correctly reports False for a build without it")
    audio_tools._CHROMAPRINT_CACHE.clear()

    # -- old fingerprints must be invalidated so they get rebuilt with chromaprint --
    assert audio_match.ALGORITHM_VERSION == "sps-spectral-v4", audio_match.ALGORITHM_VERSION
    checks.append("ALGORITHM_VERSION bumped to v4 so chromaprint-less fingerprints are re-indexed, not reused")

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
