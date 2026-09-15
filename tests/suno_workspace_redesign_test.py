from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "app" / "web" / "suno_workspace_redesign_extension.js").read_text(encoding="utf-8")
BACKEND = (ROOT / "app" / "workspace_backend.py").read_text(encoding="utf-8")


def main() -> None:
    assert 'single-owner Suno workspace redesign' in BACKEND
    assert 'suno_workspace_redesign_extension.js' in BACKEND
    assert "['home', '⌂', 'Početna']" in SOURCE
    assert "['library', '♫', 'Moje Suno pesme']" in SOURCE
    assert "['import', '↻', 'Poveži i sinhronizuj']" in SOURCE
    assert "['recognition', '⌕', 'Proveri klip']" in SOURCE
    assert "['tools', '▶', 'Moji YouTube kanali']" in SOURCE
    assert "['settings', '⚙', 'Podešavanja']" in SOURCE
    assert "nav?.replaceChildren()" in SOURCE
    assert "allButtons.forEach(button => button.remove())" in SOURCE
    assert "Video Studija, timeline-a i NP funkcija" in SOURCE
    assert "view === 'home'" in SOURCE
    assert "document.querySelectorAll('.view').forEach" in SOURCE
    assert "legacyVideoWords" in SOURCE
    assert "data-open-view=\"download\"" in SOURCE
    assert "data-open-view=\"folders\"" in SOURCE
    assert "data-open-view=\"audio\"" in SOURCE
    assert 'data-sps-skin="signal-grid"' in SOURCE
    assert 'height:62px!important' in SOURCE
    assert 'flex-direction:row!important' in SOURCE
    assert 'overflow-y:hidden!important' in SOURCE
    assert '.suno-tools-drawer[open] .suno-tools-grid' in SOURCE
    print('suno_workspace_redesign_test: PASS — one menu owner, six core destinations, contextual tools, Suno-only copy')


if __name__ == '__main__':
    main()
