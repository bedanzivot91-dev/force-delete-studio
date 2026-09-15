from __future__ import annotations
import json, os, sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) < 4:
        raise SystemExit('Upotreba: transcribe_worker.py AUDIO LANGUAGE MODEL')
    audio = Path(sys.argv[1]).resolve()
    language_raw = (sys.argv[2] or 'auto').strip().lower()
    language = None if language_raw in {'auto','detect',''} else language_raw
    model_name = sys.argv[3] or 'small'
    if not audio.is_file():
        raise FileNotFoundError(audio)
    try:
        from faster_whisper import WhisperModel
        import ctranslate2
    except Exception as exc:
        raise RuntimeError('faster-whisper nije instaliran u transcription_env.') from exc
    cuda_count = 0
    try: cuda_count = int(ctranslate2.get_cuda_device_count())
    except Exception: pass
    prefer_gpu = os.environ.get('SUNO_WHISPER_DEVICE','auto').lower()
    device = 'cuda' if (prefer_gpu in {'auto','cuda'} and cuda_count > 0) else 'cpu'
    compute_type = 'float16' if device == 'cuda' else 'int8'
    try:
        model = WhisperModel(model_name, device=device, compute_type=compute_type)
    except Exception:
        device='cpu'; compute_type='int8'
        model = WhisperModel(model_name, device=device, compute_type=compute_type)
    segments, info = model.transcribe(
        str(audio), language=language, vad_filter=True, beam_size=5,
        word_timestamps=True, condition_on_previous_text=False,
    )
    rows=[]; words=[]
    for seg in segments:
        text=str(seg.text or '').strip()
        segment_words=[]
        for word in list(getattr(seg,'words',None) or []):
            item={'start':float(word.start or seg.start),'end':float(word.end or seg.end),'word':str(word.word or '').strip()}
            if item['word']:
                words.append(item); segment_words.append(item)
        if text:
            rows.append({'start':float(seg.start),'end':float(seg.end),'text':text,'words':segment_words})
    print(json.dumps({
        'ok': True, 'language': getattr(info,'language',language_raw),
        'language_probability': float(getattr(info,'language_probability',0.0) or 0.0),
        'device':device,'compute_type':compute_type,'model':model_name,
        'text':'\n'.join(r['text'] for r in rows),'segments':rows,'words':words,
    }, ensure_ascii=False))
    return 0

if __name__ == '__main__':
    try: raise SystemExit(main())
    except Exception as exc:
        print(json.dumps({'ok': False, 'error': str(exc)}, ensure_ascii=False))
        raise
