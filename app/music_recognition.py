from __future__ import annotations
import base64, json, mimetypes, os, tempfile, urllib.error, urllib.request, uuid
from pathlib import Path
from typing import Any

AUDD_ENDPOINT = 'https://api.audd.io/'
MAX_AUDIO_BYTES = 10 * 1024 * 1024

class MusicRecognitionError(RuntimeError):
    pass

def decode_audio_base64(value: str, suffix: str = '.mp3') -> Path:
    raw = value.split(',',1)[-1].strip()
    try: data = base64.b64decode(raw, validate=True)
    except Exception as exc: raise MusicRecognitionError('Audio fajl nije ispravno prenet.') from exc
    if not data: raise MusicRecognitionError('Audio fajl je prazan.')
    if len(data) > MAX_AUDIO_BYTES: raise MusicRecognitionError('Audio isečak mora biti manji od 10 MB.')
    suffix = suffix if suffix.startswith('.') else '.' + suffix
    fd, name = tempfile.mkstemp(prefix='suno-recognition-', suffix=suffix[:10])
    os.close(fd); path=Path(name); path.write_bytes(data); return path

def _multipart(fields: dict[str,str], file_path: Path) -> tuple[bytes,str]:
    boundary='----SunoStudio'+uuid.uuid4().hex
    chunks=[]
    for k,v in fields.items():
        chunks += [f'--{boundary}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode()]
    ctype=mimetypes.guess_type(file_path.name)[0] or 'application/octet-stream'
    chunks += [f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{file_path.name}"\r\nContent-Type: {ctype}\r\n\r\n'.encode(), file_path.read_bytes(), b'\r\n', f'--{boundary}--\r\n'.encode()]
    return b''.join(chunks), f'multipart/form-data; boundary={boundary}'

def recognize_audd(file_path: Path, token: str) -> dict[str,Any]:
    token=token.strip()
    if not token: raise MusicRecognitionError('Unesi AudD API token u podešavanjima pre prepoznavanja.')
    body,ctype=_multipart({'api_token':token,'return':'apple_music,spotify'},file_path)
    req=urllib.request.Request(AUDD_ENDPOINT,data=body,headers={'Content-Type':ctype,'User-Agent':'Suno-Pesme-Studio/3.1'},method='POST')
    try:
        with urllib.request.urlopen(req,timeout=60) as r: payload=json.loads(r.read().decode('utf-8'))
    except urllib.error.HTTPError as exc:
        detail=exc.read().decode('utf-8','replace')[:500]
        raise MusicRecognitionError(f'AudD je vratio grešku {exc.code}: {detail}') from exc
    except Exception as exc: raise MusicRecognitionError(f'Internet prepoznavanje nije uspelo: {exc}') from exc
    if payload.get('status')!='success':
        err=payload.get('error') or {}; raise MusicRecognitionError(str(err.get('error_message') or err.get('message') or 'AudD nije prihvatio zahtev.'))
    result=payload.get('result')
    if not result: return {'found':False,'provider':'AudD','message':'Pesma nije prepoznata iz ovog isečka.'}
    return {'found':True,'provider':'AudD','title':result.get('title') or '', 'artist':result.get('artist') or '', 'album':result.get('album') or '', 'release_date':result.get('release_date') or '', 'label':result.get('label') or '', 'timecode':result.get('timecode') or '', 'song_link':result.get('song_link') or '', 'spotify':result.get('spotify') or {}, 'apple_music':result.get('apple_music') or {}, 'raw':result}
