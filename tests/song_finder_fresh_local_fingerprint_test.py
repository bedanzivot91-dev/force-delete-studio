from __future__ import annotations

import tempfile
from pathlib import Path
from types import SimpleNamespace
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
from song_finder_runtime_fix import apply


def main() -> None:
    calls = []
    with tempfile.TemporaryDirectory() as tmp:
        local = Path(tmp) / 'ZIVOT JE TO Remastered.mp3'
        local.write_bytes(b'local-audio-placeholder')

        def original_candidates(upload_signatures, songs):
            return ([{'song_id': songs[0]['id'], 'status': 'confirmed'}], 1)

        def source_cheap(song):
            return local, False

        def signature_for_source(source_type, source_id, source, task=None, label='', force=False):
            calls.append((source_type, source_id, Path(source), force))
            return {'chromaprint': [1, 2, 3]}

        core = SimpleNamespace(
            _song_finder_candidates=original_candidates,
            _song_finder_source_cheap=source_cheap,
            _signature_for_source=signature_for_source,
            runtime_log=lambda *args, **kwargs: None,
        )
        apply(core)
        result, checked = core._song_finder_candidates(
            {'duration': 26.4},
            [{'id': 'song-1', 'title': 'ZIVOT JE TO Remastered'}],
        )
        assert checked == 1
        assert result and result[0]['song_id'] == 'song-1'
        assert calls == [('suno', 'song-1', local, False)], calls

    print('song_finder_fresh_local_fingerprint_test: PASS')


if __name__ == '__main__':
    main()
