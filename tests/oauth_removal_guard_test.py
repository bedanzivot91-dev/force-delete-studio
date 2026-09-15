from __future__ import annotations

import sys
import tempfile
import types
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'app'))

from song_finder_runtime_fix import _install_oauth_removal_guard

class Manager:
    def __init__(self,root:Path):
        self.config_path=root/'oauth.json'; self.tokens_path=root/'tokens.bin'
        self.config_path.write_text('x'); self.tokens_path.write_text('y')
    def remove_client_config(self):
        self.config_path.unlink(missing_ok=True)
        self.tokens_path.unlink(missing_ok=True)

class BrokenManager:
    def __init__(self,root:Path):
        self.config_path=root/'oauth-broken.json'; self.tokens_path=root/'tokens-broken.bin'
        self.config_path.write_text('x'); self.tokens_path.write_text('y')
    def remove_client_config(self):
        # Legacy false-success simulation: files remain.
        return None

def main():
    with tempfile.TemporaryDirectory(prefix='oauth-guard-') as raw:
        root=Path(raw)
        ok=Manager(root); core=types.SimpleNamespace(YOUTUBE_OAUTH=ok,YouTubeOAuthError=RuntimeError)
        _install_oauth_removal_guard(core); ok.remove_client_config()
        assert not ok.config_path.exists() and not ok.tokens_path.exists()

        broken=BrokenManager(root); core2=types.SimpleNamespace(YOUTUBE_OAUTH=broken,YouTubeOAuthError=RuntimeError)
        _install_oauth_removal_guard(core2)
        try:
            broken.remove_client_config()
        except RuntimeError as exc:
            assert 'nisu potpuno uklonjeni' in str(exc)
        else:
            raise AssertionError('guard must reject false-success OAuth removal')
    print('oauth_removal_guard_test: PASS — local OAuth removal cannot report success while files remain')

if __name__=='__main__': main()
