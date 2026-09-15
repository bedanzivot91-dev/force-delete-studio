from __future__ import annotations
import json, sys, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import server as server_module


def main():
    checks = []

    with tempfile.TemporaryDirectory(prefix='sps-songfinder-src-') as raw:
        local_file = Path(raw) / 'song.wav'
        local_file.write_bytes(b'RIFF....WAVEfmt ')

        # -- local file present: preferred over audio_url, never remote --
        song_with_local = {"id": "a", "local_wav": str(local_file), "audio_url": "https://cdn.suno.com/a.mp3"}
        source, is_remote = server_module._song_finder_source(song_with_local)
        assert source == str(local_file) or Path(source) == local_file
        assert is_remote is False
        checks.append('_song_finder_source: an existing local file is preferred, never treated as remote')

        # -- no local file, but a remote audio_url: usable, marked remote (Shazam-style, no permanent download needed) --
        song_remote_only = {"id": "b", "local_wav": "", "local_audio": "", "audio_url": "https://cdn.suno.com/b.mp3"}
        source, is_remote = server_module._song_finder_source(song_remote_only)
        assert source == "https://cdn.suno.com/b.mp3"
        assert is_remote is True
        checks.append('_song_finder_source: falls back to audio_url when no local file exists, marked remote')

        # -- local_wav points at a file that no longer exists on disk: must fall back to audio_url, not fail silently --
        song_stale_local = {"id": "c", "local_wav": str(Path(raw) / "deleted.wav"), "audio_url": "https://cdn.suno.com/c.mp3"}
        source, is_remote = server_module._song_finder_source(song_stale_local)
        assert source == "https://cdn.suno.com/c.mp3" and is_remote is True
        checks.append('_song_finder_source: a stale/missing local_wav path falls back to the remote URL instead of being unusable')

        # -- neither local file nor audio_url: genuinely unusable --
        song_nothing = {"id": "d", "local_wav": "", "local_audio": "", "audio_url": ""}
        source, is_remote = server_module._song_finder_source(song_nothing)
        assert source is None and is_remote is False
        checks.append('_song_finder_source: no local file and no audio_url -> None, correctly unusable')

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
