from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKIN = ROOT / "app" / "web" / "modern_2026_skin_extension.js"
COMPAT = ROOT / "app" / "web" / "modern_2026_compat_extension.js"
BACKEND = ROOT / "app" / "workspace_backend.py"


def main() -> None:
    skin = SKIN.read_text(encoding="utf-8")
    compat = COMPAT.read_text(encoding="utf-8")
    backend = BACKEND.read_text(encoding="utf-8")

    # Global application chrome and shared components: every page inherits one
    # design system instead of falling back to historical per-view styling.
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

    # Primary user surfaces must all receive explicit modern treatment in
    # addition to the global panel/input rules.
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

    # Old theme/export controls are not deleted. They are moved, unchanged,
    # into a collapsed compatibility section and therefore keep their listeners.
    assert "modernLegacyThemes" in compat
    assert "details.appendChild(legacy)" in compat
    assert ".legacy-theme-settings{display:block!important" in compat

    # The skin is served after layout cleanup, and compatibility restoration is
    # served after the skin. That ordering is what lets the new CSS win while
    # retaining every old settings control.
    skin_ref = 'core.WEB_DIR / "modern_2026_skin_extension.js"'
    compat_ref = 'core.WEB_DIR / "modern_2026_compat_extension.js"'
    cleanup_ref = 'core.WEB_DIR / "workflow_cleanup_extension.js"'
    assert skin_ref in backend and compat_ref in backend
    assert backend.index(skin_ref) > backend.index(cleanup_ref)
    assert backend.index(compat_ref) > backend.index(skin_ref)
    assert "_workspace_complete_bundle_v6" in backend

    print("modern 2026 UI skin: OK")


if __name__ == "__main__":
    main()
