from __future__ import annotations
import json, sys
from pathlib import Path

def main() -> int:
    if len(sys.argv) < 4:
        raise SystemExit('Upotreba: stems_worker.py AUDIO OUTPUT_DIR MODEL')
    audio = Path(sys.argv[1]).resolve()
    output = Path(sys.argv[2]).resolve()
    model = sys.argv[3]
    if not audio.is_file():
        raise FileNotFoundError(audio)
    output.mkdir(parents=True, exist_ok=True)
    try:
        from audio_separator.separator import Separator
    except Exception as exc:
        raise RuntimeError('audio-separator nije instaliran u stems_env.') from exc
    separator = Separator(output_dir=str(output), output_format='FLAC')
    separator.load_model(model_filename=model)
    names = separator.separate(str(audio)) or []
    files = {}
    for name in names:
        path = Path(name)
        if not path.is_absolute():
            path = output / path
        if path.exists():
            label = path.stem.split('(')[-1].rstrip(')').strip() or path.stem
            files[label] = str(path.resolve())
    print(json.dumps({'ok': True, 'model': model, 'files': files}, ensure_ascii=False))
    return 0

if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(json.dumps({'ok': False, 'error': str(exc)}, ensure_ascii=False))
        raise
