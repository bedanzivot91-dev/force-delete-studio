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
NOTE_SECONDS = 0.6
# Kept within roughly one octave and close to the 70-900 Hz spectral probe
# band audio_match.py's dependency-free fallback matcher actually samples
# (_frame_features()'s Goertzel bins are fixed at 70/110/170/250/360/500/
# 700/900 Hz) -- a first version of this fixture multiplied notes by up to
# 2x, pushing a lot of energy above 900 Hz where neither that fallback nor
# a real song's typical fundamental range has much to grab onto, and a real
# CI run (this sandbox has no FFmpeg to catch it locally) showed even a
# pristine same-song clip only barely reached "possible", with a 3% tempo
# shift enough to drop it to "not_found" entirely.
SCALE = [130.81, 146.83, 164.81, 174.61, 196.00, 220.00, 246.94, 261.63]


def make_song(path: Path, seed: int, duration: float) -> None:
    """A deterministic little "melody" with enough spectral movement for
    both the chromaprint and the dependency-free spectral fallback matcher
    to have real, non-degenerate content to work with -- a flat single-tone
    sine wave would not exercise either matcher realistically. Each note is
    a fundamental plus two quieter harmonics (not a bare sine), closer to
    how a real instrument/voice actually distributes energy across the
    fixed spectral probe band, so the fingerprint has more than one thin
    frequency line to survive small speed/pitch perturbations with."""
    note_count = int(duration / NOTE_SECONDS)
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(SAMPLE_RATE)
        data = bytearray()
        idx = seed
        for note in range(note_count):
            idx = (idx * 7 + 3 + seed) % len(SCALE)
            freq = SCALE[idx]
            for i in range(int(SAMPLE_RATE * NOTE_SECONDS)):
                t = i / SAMPLE_RATE
                tremolo = 1.0 + 0.1 * math.sin(2 * math.pi * 4 * t)
                fundamental = math.sin(2 * math.pi * freq * t)
                second_harmonic = 0.45 * math.sin(2 * math.pi * freq * 2 * t)
                third_harmonic = 0.2 * math.sin(2 * math.pi * freq * 3 * t)
                value = int(9500 * tremolo * (fundamental + second_harmonic + third_harmonic))
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
