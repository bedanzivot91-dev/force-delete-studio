from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKIN = ROOT / "app" / "web" / "modern_2026_skin_extension.js"
COMPAT = ROOT / "app" / "web" / "modern_2026_compat_extension.js"
LEGIBILITY = ROOT / "app" / "web" / "modern_2026_legibility_extension.js"
IA = ROOT / "app" / "web" / "information_architecture_2026_extension.js"
BACKEND = ROOT / "app" / "workspace_backend.py"


def main() -> None:
    skin = SKIN.read_text(encoding="utf-8")
    compat = COMPAT.read_text(encoding="utf-8")
    legibility = LEGIBILITY.read_text(encoding="utf-8")
    ia = IA.read_text(encoding="utf-8")
    backend = BACKEND.read_text(encoding="utf-8")

    required_global = (
        "body.sps-modern-2026 .app-shell",
        "body.sps-modern-2026 .sidebar",
        "body.sps-modern-2026 .topbar",
        "body.sps-modern-2026 .panel",
        "body.sps-modern-2026 .btn",
        "body.sps-modern-2026 .search-wrap",
        "body.sps-modern-2026 .modal-card",
        "body.sps-modern-2026 .player",
        "body.sps-modern-2026 .toast",
    )
    for token in required_global:
        assert token in skin, token

    required_surfaces = (
        ".songs-grid",
        ".settings-grid",
        ".tools-tabs",
        ".youtube-oauth-connect",
        ".youtube-action-card",
        ".organized-workflow",
        "#productionWorkspace",
        ".pws-timeline-panel",
        ".stat-card",
        ".log-row",
    )
    for token in required_surfaces:
        assert token in skin, token

    for token in (
        "Aurora Studio",
        "Graphite Pro",
        "Midnight Signal",
        "modernSkinQuickSelect",
        "sps-modern-2026-skin",
    ):
        assert token in skin, token

    assert "modernLegacyThemes" in compat
    assert "details.appendChild(legacy)" in compat
    assert ".legacy-theme-settings{display:block!important" in compat

    # Final pass must explicitly override the tiny historical typography.
    for token in (
        "body.sps-modern-2026{font-size:15px",
        ".nav-item{font-size:14px",
        ".btn.small{font-size:13px",
        ".muted{font-size:13.5px",
        ".pws-cue{font-size:12.5px",
    ):
        assert token in legibility, token

    # General-purpose tools no longer live in the YouTube surface.
    for token in (
        "Nove Suno pesme",
        "Brza audio obrada",
        "Favoriti i ocene",
        "Oznake i status",
        "Brze kolekcije",
        "Backup i održavanje",
        "importView.appendChild",
        "audio.appendChild",
        "library.appendChild",
        "settings.appendChild",
    ):
        assert token in ia, token

    cleanup_ref = 'core.WEB_DIR / "workflow_cleanup_extension.js"'
    ia_ref = 'core.WEB_DIR / "information_architecture_2026_extension.js"'
    skin_ref = 'core.WEB_DIR / "modern_2026_skin_extension.js"'
    compat_ref = 'core.WEB_DIR / "modern_2026_compat_extension.js"'
    legibility_ref = 'core.WEB_DIR / "modern_2026_legibility_extension.js"'
    for ref in (cleanup_ref, ia_ref, skin_ref, compat_ref, legibility_ref):
        assert ref in backend, ref
    assert backend.index(ia_ref) > backend.index(cleanup_ref)
    assert backend.index(skin_ref) > backend.index(ia_ref)
    assert backend.index(compat_ref) > backend.index(skin_ref)
    assert backend.index(legibility_ref) > backend.index(compat_ref)
    assert "_workspace_complete_bundle_v7" in backend

    print("modern 2026 UI + layout + legibility: OK")


if __name__ == "__main__":
    main()