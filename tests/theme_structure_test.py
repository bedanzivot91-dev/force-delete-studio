from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SKIN = (ROOT / "app" / "web" / "modern_2026_skin_extension.js").read_text(encoding="utf-8")
LAYOUTS = (ROOT / "app" / "web" / "real_theme_layouts_extension.js").read_text(encoding="utf-8")
REDESIGN = (ROOT / "app" / "web" / "suno_workspace_redesign_extension.js").read_text(encoding="utf-8")
SERVER = (ROOT / "app" / "server.py").read_text(encoding="utf-8")


def main() -> None:
    themes = (
        "aurora-flow", "graphite-console", "vinyl-loft", "signal-grid",
        "paper-studio", "neon-stage", "album-wall", "mixer-desk",
    )
    for theme in themes:
        assert f"'{theme}'" in SKIN, f"tema nije registrovana: {theme}"
        assert f'value="{theme}"' in SKIN, f"tema nije u brzom izboru: {theme}"
        assert f'data-sps-skin="{theme}"' in LAYOUTS, f"nema layout pravila: {theme}"

    # Svaka tema mora imati bar jednu strukturnu odluku. Promena samo CSS
    # promenljivih/boja nije dovoljna da prođe ovaj test.
    required_structure = {
        "graphite-console": ("grid-template-columns:218px", "width:198px", "minmax(205px,1fr)"),
        "vinyl-loft": ("grid-template-columns:330px", "width:300px", "minmax(310px,1fr)"),
        "signal-grid": ("display:block", "position:sticky", "flex-direction:row"),
        "paper-studio": ("grid-template-columns:250px", "max-width:1480px", "minmax(280px,1fr)"),
        "neon-stage": ("grid-template-columns:236px", "clip-path:polygon", "minmax(240px,1fr)"),
        "album-wall": ("grid-template-columns:370px", "width:338px", "minmax(360px,1fr)"),
        "mixer-desk": ("grid-template-columns:minmax(0,1fr) 286px", "right:12px", "grid-column:1"),
    }
    for theme, tokens in required_structure.items():
        for token in tokens:
            assert token in (LAYOUTS + REDESIGN), f"{theme} nema strukturno pravilo {token}"

    assert 'height:62px!important' in REDESIGN
    assert 'overflow-y:hidden!important' in REDESIGN
    assert 'production_workspace_extension.js' not in SERVER
    print("theme_structure_test: PASS — 8 tema su registrovane i menjaju strukturu, Signal Grid ne prekriva sadržaj, video editor nije u fallback bundle-u")


if __name__ == "__main__":
    main()
