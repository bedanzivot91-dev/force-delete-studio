from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKIN = ROOT / "app" / "web" / "modern_2026_skin_extension.js"
SURFACES = ROOT / "app" / "web" / "modern_2026_surface_coverage_extension.js"
COMPAT = ROOT / "app" / "web" / "modern_2026_compat_extension.js"
ISOLATION = ROOT / "app" / "web" / "modern_2026_isolation_extension.js"
LEGIBILITY = ROOT / "app" / "web" / "modern_2026_legibility_extension.js"
IA = ROOT / "app" / "web" / "information_architecture_2026_extension.js"
BACKEND = ROOT / "app" / "workspace_backend.py"


def main() -> None:
    skin = SKIN.read_text(encoding="utf-8")
    surfaces = SURFACES.read_text(encoding="utf-8")
    compat = COMPAT.read_text(encoding="utf-8")
    isolation = ISOLATION.read_text(encoding="utf-8")
    legibility = LEGIBILITY.read_text(encoding="utf-8")
    ia = IA.read_text(encoding="utf-8")
    backend = BACKEND.read_text(encoding="utf-8")

    required_global = (
        "body.sps-modern-2026 .app-shell", "body.sps-modern-2026 .sidebar",
        "body.sps-modern-2026 .topbar", "body.sps-modern-2026 .panel",
        "body.sps-modern-2026 .btn", "body.sps-modern-2026 .search-wrap",
        "body.sps-modern-2026 .modal-card", "body.sps-modern-2026 .player",
        "body.sps-modern-2026 .toast",
    )
    for token in required_global:
        assert token in skin, token

    for token in (
        ".songs-grid", ".settings-grid", ".tools-tabs", ".youtube-oauth-connect",
        ".youtube-action-card", ".organized-workflow", "#productionWorkspace",
        ".pws-timeline-panel", ".stat-card", ".log-row",
    ):
        assert token in skin, token

    # Every HTML routed page has an explicit page-specific 2026 rule group.
    for view_id in (
        "#view-library", "#view-folders", "#view-audio", "#view-download", "#view-import",
        "#view-recognition", "#view-smart", "#view-versions", "#view-release", "#view-tools",
        "#view-production", "#view-stats", "#view-logs", "#view-settings",
    ):
        assert view_id in surfaces, view_id

    for token in ("Aurora Studio", "Graphite Pro", "Midnight Signal", "modernSkinQuickSelect", "sps-modern-2026-skin"):
        assert token in skin, token

    assert "modernLegacyThemes" in compat
    assert "details.appendChild(legacy)" in compat
    assert ".legacy-theme-settings{display:block!important" in compat

    for token in (
        "document.body.dataset.theme = 'default'", "MutationObserver", "spsLegacyTheme",
        ".brand::after{content:none!important}", ".nav-item::before{content:none!important}",
    ):
        assert token in isolation, token

    for token in (
        "body.sps-modern-2026{font-size:15px", ".nav-item{font-size:14px",
        ".btn.small{font-size:13px", ".muted{font-size:13.5px",
        ".pws-cue{font-size:12.5px", ".matrix-status", ".youtube-channel-meta i",
    ):
        assert token in legibility, token

    for token in (
        "Nove Suno pesme", "Brza audio obrada", "Favoriti i ocene", "Oznake i status",
        "Brze kolekcije", "Backup i održavanje", "importView.appendChild",
        "audio.appendChild", "library.appendChild", "settings.appendChild",
    ):
        assert token in ia, token

    ordered = (
        'core.WEB_DIR / "workflow_cleanup_extension.js"',
        'core.WEB_DIR / "information_architecture_2026_extension.js"',
        'core.WEB_DIR / "modern_2026_skin_extension.js"',
        'core.WEB_DIR / "modern_2026_surface_coverage_extension.js"',
        'core.WEB_DIR / "modern_2026_compat_extension.js"',
        'core.WEB_DIR / "modern_2026_isolation_extension.js"',
        'core.WEB_DIR / "modern_2026_legibility_extension.js"',
    )
    for ref in ordered:
        assert ref in backend, ref
    positions = [backend.index(ref) for ref in ordered]
    assert positions == sorted(positions), positions
    assert "_workspace_complete_bundle_v9" in backend

    print("modern 2026 UI + every-page coverage + layout + isolation + legibility: OK")


if __name__ == "__main__":
    main()