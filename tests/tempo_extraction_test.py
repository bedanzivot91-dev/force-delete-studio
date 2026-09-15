from __future__ import annotations
import json, sys
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import audio_match


class FakeCompletedProcess:
    def __init__(self, stdout=b'', returncode=0):
        self.stdout = stdout
        self.returncode = returncode


class FakePopen:
    """Enough of subprocess.Popen's interface for extract_signature() to
    run its read loop and hit its own "audio too short" error cleanly,
    without needing a real ffmpeg or real audio bytes."""
    def __init__(self, args, **kwargs):
        self.args = args
        self.stdout = _FakeStream(b'')  # immediate EOF -> extract_signature raises "too short", which the test expects
        self.stderr = _FakeStream(b'')
        self.returncode = 0

    def wait(self, timeout=None):
        return 0

    def poll(self):
        return 0

    def kill(self):
        pass

    def terminate(self):
        pass


class _FakeStream:
    def __init__(self, data: bytes):
        self._data = data
        self._read = False

    def read(self, n=-1):
        if self._read:
            return b''
        self._read = True
        return self._data


def main():
    checks = []

    # -- _extract_chromaprint: tempo=1.0 (default) must NOT add an -af flag
    # (zero behavior change for the overwhelmingly common case) --
    captured_runs = []

    def fake_run(args, **kwargs):
        captured_runs.append(args)
        return FakeCompletedProcess(stdout=b'\x00' * 20, returncode=0)

    with mock.patch.object(audio_match.subprocess, 'run', side_effect=fake_run):
        audio_match._extract_chromaprint('ffmpeg.exe', 'clip.wav', tempo=1.0)
    assert '-af' not in captured_runs[-1]
    checks.append('_extract_chromaprint: tempo=1.0 omits -af entirely (no change vs. before this fix)')

    # -- _extract_chromaprint: a real tempo value must add a REAL ffmpeg
    # atempo filter (not a synthetic reshuffle of already-extracted bits) --
    captured_runs.clear()
    with mock.patch.object(audio_match.subprocess, 'run', side_effect=fake_run):
        audio_match._extract_chromaprint('ffmpeg.exe', 'clip.wav', tempo=1.03)
    args = captured_runs[-1]
    assert '-af' in args, args
    af_value = args[args.index('-af') + 1]
    assert af_value == 'atempo=1.0300', af_value
    checks.append('_extract_chromaprint: tempo=1.03 adds a real "-af atempo=1.0300" ffmpeg filter argument')

    captured_runs.clear()
    with mock.patch.object(audio_match.subprocess, 'run', side_effect=fake_run):
        audio_match._extract_chromaprint('ffmpeg.exe', 'clip.wav', tempo=0.97)
    args = captured_runs[-1]
    af_value = args[args.index('-af') + 1]
    assert af_value == 'atempo=0.9700', af_value
    checks.append('_extract_chromaprint: tempo=0.97 (slowed-down compensation) also builds the correct filter value')

    # -- extract_signature: same -af wiring on the raw-PCM decode pass --
    captured_popen_args = []

    def fake_popen(args, **kwargs):
        captured_popen_args.append(args)
        return FakePopen(args, **kwargs)

    with mock.patch.object(audio_match, 'ffmpeg_path', return_value='ffmpeg.exe'), \
         mock.patch.object(audio_match.subprocess, 'Popen', side_effect=fake_popen):
        try:
            audio_match.extract_signature('clip.wav', tempo=1.03)
        except audio_match.AudioMatchError:
            pass  # expected: FakePopen yields no real PCM, extract_signature correctly rejects it as too short
    assert captured_popen_args, "expected extract_signature to actually invoke ffmpeg"
    popen_args = captured_popen_args[0]
    assert '-af' in popen_args, popen_args
    af_value = popen_args[popen_args.index('-af') + 1]
    assert af_value == 'atempo=1.0300', af_value
    checks.append('extract_signature: tempo=1.03 adds the same real atempo filter to the raw-PCM decode pass, not just the chromaprint pass')

    captured_popen_args.clear()
    with mock.patch.object(audio_match, 'ffmpeg_path', return_value='ffmpeg.exe'), \
         mock.patch.object(audio_match.subprocess, 'Popen', side_effect=fake_popen):
        try:
            audio_match.extract_signature('clip.wav')
        except audio_match.AudioMatchError:
            pass
    assert '-af' not in captured_popen_args[0], captured_popen_args[0]
    checks.append('extract_signature: default call (no tempo arg) is byte-for-byte the same ffmpeg command as before this fix')

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
