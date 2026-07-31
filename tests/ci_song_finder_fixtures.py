"""CI-only helper (not part of the pytest-less tests/*.py suite, so it is
excluded from the "Run the existing test suite" step by name): builds two
distinct synthetic "songs" as real WAV files and inserts one of them into a
fresh library DB, so the Windows CI job can drive the real song-finder HTTP
API end-to-end with real FFmpeg/chromaprint output -- this sandbox has no
FFmpeg to do that locally.

Usage: python ci_song_finder_fixtures.py <app_dir> <fixtures_dir> <data_dir>
  app_dir:      package\\Program\\app (so database.py can be imported)
  fixtures_dir: where song_a.wav / song_b.wav are written
  data_dir:     SUNO_STUDIO_DATA_DIR the server will be started with;
                suno_biblioteka.db is created directly inside it
"""
from __future__ import annotations

import math
import struct
import sys
import wave
from pathlib import Path

SAMPLE_RATE = 44100
NOTE_SECONDS = 0.4
SCALE = [261.63, 293.66, 329.63, 349.23, 392.00, 440.00, 493.88, 523.25]


def make_song(path: Path, seed: int, duration: float) -> None:
    """A deterministic little "melody" with enough spectral movement for
    both the chromaprint and the dependency-free spectral fallback matcher
    to have real, non-degenerate content to work with -- a flat single-tone
    sine wave would not exercise either matcher realistically."""
    note_count = int(duration / NOTE_SECONDS)
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(SAMPLE_RATE)
        data = bytearray()
        idx = seed
        for note in range(note_count):
            idx = (idx * 7 + 3 + seed) % len(SCALE)
            freq = SCALE[idx] * (1.0 + 0.5 * ((seed + note) % 3))
            for i in range(int(SAMPLE_RATE * NOTE_SECONDS)):
                t = i / SAMPLE_RATE
                tremolo = 1.0 + 0.15 * math.sin(2 * math.pi * 5 * t)
                value = int(11000 * tremolo * math.sin(2 * math.pi * freq * t))
                value = max(-32000, min(32000, value))
                data += struct.pack("<h", value)
        handle.writeframes(bytes(data))


def main() -> None:
    app_dir, fixtures_dir, data_dir = sys.argv[1], sys.argv[2], sys.argv[3]
    sys.path.insert(0, app_dir)
    from database import LibraryDB  # noqa: E402

    fixtures = Path(fixtures_dir)
    fixtures.mkdir(parents=True, exist_ok=True)
    song_a = fixtures / "song_a.wav"
    song_b = fixtures / "song_b.wav"
    make_song(song_a, seed=1, duration=75.0)
    make_song(song_b, seed=5, duration=75.0)

    data_path = Path(data_dir)
    data_path.mkdir(parents=True, exist_ok=True)
    db = LibraryDB(data_path / "suno_biblioteka.db")
    db.upsert_song({
        "id": "ci-song-finder-a", "title": "CI Test Song A", "display_name": "CI Autor",
        "duration": 75.0, "created_at": "2026-07-31T00:00:00Z",
    })
    db.update_song_files("ci-song-finder-a", local_wav=str(song_a))
    print(f"song_finder e2e fixtures ready: {song_a} (indexed), {song_b} (not indexed, for the negative case)")


if __name__ == "__main__":
    main()
