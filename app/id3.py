from __future__ import annotations

import os
import shutil
import struct
import tempfile
from pathlib import Path


def _synchsafe(value: int) -> bytes:
    return bytes([
        (value >> 21) & 0x7F,
        (value >> 14) & 0x7F,
        (value >> 7) & 0x7F,
        value & 0x7F,
    ])


def _decode_synchsafe(data: bytes) -> int:
    if len(data) != 4:
        return 0
    return ((data[0] & 0x7F) << 21) | ((data[1] & 0x7F) << 14) | ((data[2] & 0x7F) << 7) | (data[3] & 0x7F)


def _text_payload(text: str) -> bytes:
    # ID3v2.3 encoding 1 = UTF-16 with BOM
    return b"\x01" + (text or "").encode("utf-16")


def _frame(frame_id: str, payload: bytes) -> bytes:
    return frame_id.encode("ascii") + struct.pack(">I", len(payload)) + b"\x00\x00" + payload


def _text_frame(frame_id: str, text: str) -> bytes:
    return _frame(frame_id, _text_payload(text)) if text else b""


def _comment_frame(text: str) -> bytes:
    if not text:
        return b""
    payload = b"\x01eng" + "Suno prompt".encode("utf-16") + b"\x00\x00" + text.encode("utf-16")
    return _frame("COMM", payload)


def _lyrics_frame(text: str) -> bytes:
    if not text:
        return b""
    payload = b"\x01eng" + b"\xff\xfe\x00\x00" + text.encode("utf-16")
    return _frame("USLT", payload)


def _picture_frame(image: bytes, mime: str = "image/jpeg") -> bytes:
    if not image:
        return b""
    payload = b"\x00" + mime.encode("ascii", errors="ignore") + b"\x00" + b"\x03" + b"\x00" + image
    return _frame("APIC", payload)


def embed_mp3_metadata(
    mp3_path: Path,
    *,
    title: str = "",
    artist: str = "",
    album: str = "Suno",
    genre: str = "",
    year: str = "",
    lyrics: str = "",
    comment: str = "",
    cover_bytes: bytes | None = None,
    cover_mime: str = "image/jpeg",
    track_number: str = "",
    copyright_text: str = "",
    website: str = "",
    bpm: str = "",
    key_signature: str = "",
) -> None:
    frames = b"".join([
        _text_frame("TIT2", title),
        _text_frame("TPE1", artist),
        _text_frame("TALB", album),
        _text_frame("TCON", genre),
        _text_frame("TYER", year[:4] if year else ""),
        _text_frame("TRCK", track_number),
        _text_frame("TCOP", copyright_text),
        _text_frame("TBPM", bpm),
        _text_frame("TKEY", key_signature),
        _frame("WOAR", website.encode("latin-1", errors="ignore")) if website else b"",
        _comment_frame(comment),
        _lyrics_frame(lyrics),
        _picture_frame(cover_bytes or b"", cover_mime),
    ])
    if not frames:
        return
    padding = b"\x00" * 1024
    tag = b"ID3" + bytes([3, 0, 0]) + _synchsafe(len(frames) + len(padding)) + frames + padding

    mp3_path = Path(mp3_path)
    fd, temp_name = tempfile.mkstemp(prefix="suno_tag_", suffix=".mp3", dir=str(mp3_path.parent))
    os.close(fd)
    try:
        # The source handle must be fully closed before os.replace() below:
        # on Windows (unlike POSIX), replacing a file that this process
        # still has open for reading fails with "Access is denied"
        # (WinError 5) even though it's a read-only handle in the same
        # process -- the whole write happens inside this `with` block so
        # `source` is guaranteed closed by the time we get to the replace.
        with mp3_path.open("rb") as source:
            start = source.read(10)
            old_tag_size = 0
            if len(start) == 10 and start[:3] == b"ID3":
                old_tag_size = 10 + _decode_synchsafe(start[6:10]) + (10 if (start[5] & 0x10) else 0)
            source.seek(old_tag_size)
            with open(temp_name, "wb") as target:
                target.write(tag)
                shutil.copyfileobj(source, target, length=1024 * 1024)
        os.replace(temp_name, mp3_path)
    finally:
        if os.path.exists(temp_name):
            os.unlink(temp_name)
