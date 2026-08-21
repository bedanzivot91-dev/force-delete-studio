from __future__ import annotations

import ast
import re
from collections import Counter
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "app"
WEB = APP / "web"
INDEX = WEB / "index.html"
BACKEND = APP / "workspace_backend.py"


class AuditHtml(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.ids: list[str] = []
        self.views: set[str] = set()
        self.nav_targets: set[str] = set()
        self.buttons: list[dict[str, str]] = []

    def handle_starttag(self, tag: str, attrs_raw) -> None:
        attrs = dict(attrs_raw)
        element_id = attrs.get("id", "")
        if element_id:
            self.ids.append(element_id)
            if element_id.startswith("view-"):
                self.views.add(element_id[5:])
        if attrs.get("data-view"):
            self.nav_targets.add(attrs["data-view"])
        if tag == "button":
            self.buttons.append({k: str(v or "") for k, v in attrs.items()})


def js_sources() -> list[Path]:
    return [WEB / "app.js", *sorted(WEB.glob("*_extension.js"))]


def all_js_text() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in js_sources())


def all_python_text() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="replace") for path in sorted(APP.rglob("*.py")))


def check_python_syntax(issues: list[str]) -> None:
    for path in sorted(APP.rglob("*.py")):
        try:
            ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        except SyntaxError as exc:
            issues.append(f"Python syntax: {path.relative_to(ROOT)}: {exc}")


def check_html_and_navigation(issues: list[str], notes: list[str]) -> AuditHtml:
    parser = AuditHtml()
    parser.feed(INDEX.read_text(encoding="utf-8"))
    duplicates = sorted(key for key, count in Counter(parser.ids).items() if count > 1)
    if duplicates:
        issues.append("Dupli HTML ID-jevi: " + ", ".join(duplicates))
    missing_views = sorted(parser.nav_targets - parser.views)
    if missing_views:
        issues.append("Navigacija vodi na nepostojeće stranice: " + ", ".join(missing_views))
    missing_nav = sorted(parser.views - parser.nav_targets)
    if missing_nav:
        issues.append("Stranice bez navigacije: " + ", ".join(missing_nav))
    notes.append(f"HTML: {len(parser.ids)} ID kandidata, {len(parser.views)} stranica, {len(parser.buttons)} dugmadi.")
    return parser


def check_button_wiring(parser: AuditHtml, js: str, issues: list[str], notes: list[str]) -> None:
    orphaned: list[str] = []
    for attrs in parser.buttons:
        button_id = attrs.get("id", "")
        if not button_id:
            if not any(key.startswith("data-") for key in attrs):
                classes = set(attrs.get("class", "").split())
                known_delegated = {"export-all", "tool-audio-preset", "tool-folder", "lyric-download"}
                if not (classes & known_delegated):
                    orphaned.append("<button bez id/data akcije>")
            continue
        if button_id not in js and not any(key.startswith("data-") for key in attrs):
            orphaned.append(button_id)
    if orphaned:
        issues.append("Dugmad bez pronađenog JS povezivanja: " + ", ".join(sorted(set(orphaned))))
    notes.append(f"Kontrole: provereno {len(parser.buttons)} HTML dugmadi prema app.js + svim aktivnim ekstenzijama.")


def check_dom_references(parser: AuditHtml, js: str, issues: list[str], notes: list[str]) -> None:
    dynamic_ids = set(re.findall(r"\.id\s*=\s*['\"]([A-Za-z0-9_-]+)['\"]", js))
    dynamic_ids.update(re.findall(r"id=[\\\"']([A-Za-z0-9_-]+)[\\\"']", js))
    # bulk_download_extension creates real buttons through one shared helper;
    # capture the literal first argument rather than falsely reporting those
    # controls as missing DOM nodes.
    dynamic_ids.update(re.findall(r"_bulkButton\(\s*['\"]([A-Za-z0-9_-]+)['\"]", js))
    known = set(parser.ids) | dynamic_ids
    refs = set(re.findall(r"\$\(['\"]([A-Za-z0-9_-]+)['\"]\)", js))
    refs.update(re.findall(r"getElementById\(['\"]([A-Za-z0-9_-]+)['\"]\)", js))
    missing = sorted(refs - known)
    allow = {
        "modernLegacyThemes", "spsIa2026Marker", "spsModern2026Legibility",
        "spsModern2026Style", "spsModern2026CompatStyle", "spsModern2026IsolationStyle",
        "spsModern2026SurfaceCoverage", "spsIa2026Style",
    }
    missing = [item for item in missing if item not in allow]
    if missing:
        issues.append("JS traži DOM ID koji audit ne nalazi u HTML/dinamičkom UI-ju: " + ", ".join(missing))
    notes.append(f"DOM reference: provereno {len(refs)} direktnih ID referenci i {len(dynamic_ids)} dinamičkih ID-jeva.")


def check_api_routes(js: str, py: str, issues: list[str], notes: list[str]) -> None:
    endpoints = set(re.findall(r"[\"'`](/api/[A-Za-z0-9_./-]+)", js))
    endpoints = {ep.rstrip("/") or "/api" for ep in endpoints}
    missing = sorted(ep for ep in endpoints if ep not in py)
    allowed_prefixes = (
        "/api/files/", "/api/download/", "/api/export/", "/api/audio/stream", "/api/cover",
    )
    missing = [ep for ep in missing if not ep.startswith(allowed_prefixes)]
    if missing:
        issues.append("Frontend API putanje bez pronađenog backend stringa: " + ", ".join(missing))
    notes.append(f"API: statički provereno {len(endpoints)} frontend /api putanja prema Python runtime-u.")


def check_information_architecture(issues: list[str], notes: list[str]) -> None:
    ia = (WEB / "information_architecture_2026_extension.js").read_text(encoding="utf-8")
    organized = (WEB / "organized_ui_extension.js").read_text(encoding="utf-8")
    cleanup = (WEB / "workflow_cleanup_extension.js").read_text(encoding="utf-8")
    required_moves = {
        "Suno": ["Nove Suno pesme", "ia2026SunoOperations", "importView.appendChild"],
        "Audio": ["Brza audio obrada", "ia2026AudioBatchOperations", "audio.appendChild"],
        "Biblioteka": ["Favoriti i ocene", "Oznake i status", "Brze kolekcije", "Izvoz", "Izveštaji", "library.appendChild"],
        "Podešavanja": ["Backup i održavanje", "ia2026SystemOperations", "settings.appendChild"],
    }
    for group, tokens in required_moves.items():
        for token in tokens:
            if token not in ia:
                issues.append(f"IA 2026: {group} nema očekivano grupisanje: {token}")
    for title in (
        "Zaključavanje programa", "Instalacija, rollback i potpis", "Integritet i duplikati",
        "Automatska organizacija foldera", "Zaštita pesama i rollback",
        "Panako (opcioni dodatni fingerprint motor)",
    ):
        if title not in organized:
            issues.append(f"Video Studio maintenance nije eksplicitno premešten: {title}")
    if "advanced-systems-panel" not in cleanup or "body.appendChild(advanced)" not in cleanup:
        issues.append("Napredni sistemski panel nije premešten iz YouTube centra u Podešavanja.")
    if "oldLyric.classList.add('hidden')" not in organized:
        issues.append("Stari duplirani lyric-video panel nije sakriven iz Video Studija.")
    if "['library-tools', 'system']" not in ia:
        issues.append("Stari YouTube tabovi za Library/System nisu uklonjeni iz primarnog YouTube toka.")
    if "count.id = 'recognitionHistoryCount'" not in ia:
        issues.append("Pronalazač ima kod za broj istorije, ali vidljivi recognitionHistoryCount element nije napravljen.")
    notes.append("Raspored: proverene istorijski razbacane Suno/Audio/Library/System/Video funkcije, YouTube tabovi i brojač istorije Pronalazača.")


def check_modern_ui_and_legibility(parser: AuditHtml, issues: list[str], notes: list[str]) -> None:
    skin = (WEB / "modern_2026_skin_extension.js").read_text(encoding="utf-8")
    surfaces = (WEB / "modern_2026_surface_coverage_extension.js").read_text(encoding="utf-8")
    isolation = (WEB / "modern_2026_isolation_extension.js").read_text(encoding="utf-8")
    legibility = (WEB / "modern_2026_legibility_extension.js").read_text(encoding="utf-8")
    backend = BACKEND.read_text(encoding="utf-8")
    for token in (
        "document.querySelectorAll('.view').forEach", ".sidebar", ".topbar", ".panel", ".songs-grid",
        "#productionWorkspace", ".youtube-action-card", ".settings-grid",
        "Aurora Studio", "Graphite Pro", "Midnight Signal",
    ):
        if token not in skin:
            issues.append(f"Moderni skin ne pokriva očekivanu globalnu površinu/token: {token}")

    # Page-specific components intentionally live in the later surface layer,
    # not in the global skin. Verify their actual owning layer instead of
    # duplicating CSS merely to satisfy the test.
    for token in (
        "#view-logs .logs-table",
        "#view-recognition .youtube-action-card",
        "#view-release .release-row",
        "#view-smart .smart-rule-row",
        "#view-versions .version-member",
    ):
        if token not in surfaces:
            issues.append(f"2026 page surface ne pokriva očekivani token: {token}")

    # No routed page may rely only on generic old CSS. Each has an explicit rule.
    for view in sorted(parser.views):
        selector = f"#view-{view}"
        if selector not in surfaces:
            issues.append(f"Stranica nema eksplicitni 2026 surface stil: {selector}")

    for token in (
        "document.body.dataset.theme = 'default'", "MutationObserver", ".brand::after{content:none!important}",
        ".nav-item::before{content:none!important}",
    ):
        if token not in isolation:
            issues.append(f"Izolacija starih tema nedostaje: {token}")
    for token in (
        "body.sps-modern-2026{font-size:15px", ".nav-item{font-size:14px", ".btn.small{font-size:13px",
        "input,body.sps-modern-2026 select,body.sps-modern-2026 textarea{font-size:14px",
        ".muted{font-size:13.5px", ".pws-cue{font-size:12.5px", ".matrix-status", ".youtube-channel-meta i",
    ):
        if token not in legibility:
            issues.append(f"Finalni čitljivi font override nedostaje: {token}")
    sizes = [float(x) for x in re.findall(r"font-size\s*:\s*([0-9]+(?:\.[0-9]+)?)px", legibility)]
    if sizes and min(sizes) < 12.5:
        issues.append(f"Finalni legibility sloj još sadrži font manji od 12.5px: minimum {min(sizes)}px")
    order = [
        "workflow_cleanup_extension.js", "information_architecture_2026_extension.js",
        "modern_2026_skin_extension.js", "modern_2026_surface_coverage_extension.js",
        "modern_2026_compat_extension.js", "modern_2026_isolation_extension.js",
        "modern_2026_legibility_extension.js",
    ]
    positions = [backend.find(name) for name in order]
    if any(pos < 0 for pos in positions) or positions != sorted(positions):
        issues.append("Finalni UI bundle redosled nije cleanup -> IA -> skin -> surfaces -> compatibility -> isolation -> legibility.")
    if "_workspace_complete_bundle_v9" not in backend:
        issues.append("Backend nije prebačen na bundle v9 sa page-by-page UI pokrivenošću.")
    notes.append(f"Tipografija: finalni override ima {len(sizes)} eksplicitnih veličina; minimum {min(sizes) if sizes else 'n/a'}px. Eksplicitno stilizovano {len(parser.views)} routed stranica.")


def main() -> None:
    issues: list[str] = []
    notes: list[str] = []
    check_python_syntax(issues)
    parser = check_html_and_navigation(issues, notes)
    js = all_js_text()
    py = all_python_text()
    check_button_wiring(parser, js, issues, notes)
    check_dom_references(parser, js, issues, notes)
    check_api_routes(js, py, issues, notes)
    check_information_architecture(issues, notes)
    check_modern_ui_and_legibility(parser, issues, notes)

    print("FULL PROGRAM STATIC AUDIT")
    for note in notes:
        print("OK:", note)
    if issues:
        print("\nPROBLEMI:")
        for issue in issues:
            print("-", issue)
        raise SystemExit(1)
    print("\nRESULT: PASS — statički integritet UI/funkcija/ruta/rasporeda/čitljivosti je prošao.")


if __name__ == "__main__":
    main()
