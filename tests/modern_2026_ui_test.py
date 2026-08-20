from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKIN = ROOT / "app" / "web" / "modern_2026_skin_extension.js"
BACKEND = ROOT / "app" / "workspace_backend.py"


def main() -> None:
    skin = SKIN.read_text(encoding="utf-8")
    backend = BACKEND.read_text(encoding="utf-8")

    # Global application chrome and shared components: if these selectors are
    # present, every page inherits the same design system instead of each view
    # falling back to the old historical styling.
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

    # The Settings page offers only 2026 skins in the new primary theme panel.
    for token in (
        "Aurora Studio",
        "Graphite Pro",
        "Midnight Signal",
        "legacy-theme-settings{display:none!important}",
        "modernSkinQuickSelect",
        "sps-modern-2026-skin",
    ):
        assert token in skin, token

    # It must be served LAST so it can override all older inline/theme CSS
    # without replacing functional DOM nodes or their event listeners.
    assert 'core.WEB_DIR / "modern_2026_skin_extension.js"' in backend
    assert backend.index('core.WEB_DIR / "modern_2026_skin_extension.js"') > backend.index('core.WEB_DIR / "workflow_cleanup_extension.js"')
    assert "_workspace_complete_bundle_v5" in backend

    print("modern 2026 UI skin: OK")


if __name__ == "__main__":
    main()
