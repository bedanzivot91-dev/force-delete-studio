from __future__ import annotations

import ast
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from collections import Counter
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPORT = ROOT / "deep-repository-audit-report.json"
EXCLUDED = {".git", ".gradle", ".idea", ".vscode", "__pycache__", "node_modules", "build", "dist", "out", "target", ".venv", "venv", "artifact-download", "coverage", "htmlcov"}
TEXT_SUFFIXES = {".py", ".js", ".html", ".css", ".go", ".kt", ".kts", ".xml", ".json", ".yml", ".yaml", ".ps1", ".bat", ".cmd", ".sh", ".gradle", ".properties", ".toml", ".ini", ".cfg", ".txt", ".md"}
PRODUCTION_ROOTS = {"app", "plugins", "windows_build", "android", "ci_scripts"}


class HtmlAudit(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.ids: list[str] = []

    def handle_starttag(self, tag: str, attrs_raw) -> None:
        element_id = dict(attrs_raw).get("id")
        if element_id:
            self.ids.append(str(element_id))


def rel(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def wanted(path: Path) -> bool:
    return not any(part in EXCLUDED for part in path.relative_to(ROOT).parts)


def files() -> list[Path]:
    return sorted(p for p in ROOT.rglob("*") if p.is_file() and wanted(p))


def source_files(items: list[Path]) -> list[Path]:
    return [p for p in items if p.suffix.lower() in TEXT_SUFFIXES or p.name in {"gradlew", "Dockerfile"}]


def read_text(path: Path, issues: list[str]) -> str | None:
    try:
        return path.read_text(encoding="utf-8")
    except UnicodeDecodeError as exc:
        issues.append(f"UTF-8 decode failed: {rel(path)}: {exc}")
    except OSError as exc:
        issues.append(f"Cannot read source/config file: {rel(path)}: {exc}")
    return None


def duplicate_defs(tree: ast.AST, label: str, issues: list[str]) -> None:
    def walk_scope(node: ast.AST, scope: str) -> None:
        body = getattr(node, "body", [])
        defs = [(x.name, x.lineno) for x in body if isinstance(x, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef))]
        counts = Counter(name for name, _ in defs)
        for name, count in counts.items():
            if count > 1:
                lines = ",".join(str(line) for n, line in defs if n == name)
                issues.append(f"Duplicate Python definition: {label}:{scope}:{name} lines {lines}")
        for child in body:
            if isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                walk_scope(child, f"{scope}.{child.name}")
    walk_scope(tree, "<module>")


def obvious_stub(node: ast.AST) -> bool:
    body = getattr(node, "body", [])
    meaningful = [s for s in body if not (isinstance(s, ast.Expr) and isinstance(getattr(s, "value", None), ast.Constant) and isinstance(s.value.value, str))]
    if len(meaningful) != 1:
        return False
    stmt = meaningful[0]
    if isinstance(stmt, ast.Pass):
        return True
    if isinstance(stmt, ast.Expr) and isinstance(stmt.value, ast.Constant) and stmt.value.value is Ellipsis:
        return True
    if isinstance(stmt, ast.Raise):
        exc = stmt.exc
        return (isinstance(exc, ast.Name) and exc.id == "NotImplementedError") or (isinstance(exc, ast.Call) and isinstance(exc.func, ast.Name) and exc.func.id == "NotImplementedError")
    return False


def check_python(paths: list[Path], issues: list[str], warnings: list[str], stats: dict) -> None:
    parsed = funcs = classes = 0
    for path in paths:
        text = read_text(path, issues)
        if text is None:
            continue
        try:
            tree = ast.parse(text, filename=rel(path))
            compile(tree, rel(path), "exec")
            parsed += 1
        except SyntaxError as exc:
            issues.append(f"Python syntax/compile failed: {rel(path)}: {exc}")
            continue
        duplicate_defs(tree, rel(path), issues)
        production = path.relative_to(ROOT).parts[0] in PRODUCTION_ROOTS
        for node in ast.walk(tree):
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                funcs += 1
                if production and obvious_stub(node):
                    issues.append(f"Production Python function is an obvious stub: {rel(path)}:{node.lineno}:{node.name}")
            elif isinstance(node, ast.ClassDef):
                classes += 1
            elif production and isinstance(node, ast.ExceptHandler) and len(node.body) == 1 and isinstance(node.body[0], ast.Pass):
                warnings.append(f"Silent except/pass: {rel(path)}:{node.lineno}")
    stats.update(python_files_parsed=parsed, python_functions=funcs, python_classes=classes)


def check_html(paths: list[Path], issues: list[str], stats: dict) -> None:
    parsed = total_ids = 0
    for path in paths:
        text = read_text(path, issues)
        if text is None:
            continue
        parser = HtmlAudit()
        try:
            parser.feed(text); parser.close(); parsed += 1
        except Exception as exc:
            issues.append(f"HTML parse failed: {rel(path)}: {exc}")
            continue
        dupes = sorted(k for k, v in Counter(parser.ids).items() if v > 1)
        if dupes:
            issues.append(f"Duplicate HTML ids: {rel(path)}: {', '.join(dupes)}")
        total_ids += len(parser.ids)
    stats.update(html_files_parsed=parsed, html_ids=total_ids)


def check_structured(paths: list[Path], suffix: str, parser, label: str, issues: list[str], stats: dict) -> None:
    parsed = 0
    for path in paths:
        if path.suffix.lower() != suffix:
            continue
        text = read_text(path, issues)
        if text is None:
            continue
        try:
            parser(text); parsed += 1
        except Exception as exc:
            issues.append(f"{label} parse failed: {rel(path)}: {exc}")
    stats[f"{label.lower()}_files_parsed"] = parsed


def check_requirements(items: list[Path], issues: list[str], stats: dict) -> None:
    reqs = [p for p in items if p.name.startswith("requirements") and p.suffix == ".txt"]
    for path in reqs:
        text = read_text(path, issues)
        if text is None:
            continue
        for line_no, raw in enumerate(text.splitlines(), 1):
            line = raw.strip()
            if line.startswith(("-r ", "--requirement ")):
                target = line.split(maxsplit=1)[1].strip()
                if not (path.parent / target).resolve().exists():
                    issues.append(f"Requirements include missing file: {rel(path)}:{line_no}: {target}")
    stats["requirements_files"] = len(reqs)


def check_placeholders(paths: list[Path], issues: list[str], warnings: list[str], stats: dict) -> None:
    fatal = {
        ".go": [r"panic\(\s*[\"'](?:TODO|not implemented|placeholder)", r"IMPLEMENT_ME"],
        ".kt": [r"\bTODO\s*\(", r"NotImplementedError\s*\("],
        ".kts": [r"\bTODO\s*\(", r"NotImplementedError\s*\("],
        ".js": [r"throw\s+new\s+Error\(\s*[\"'](?:TODO|not implemented|placeholder)"],
    }
    marker = re.compile(r"\b(TODO|FIXME|XXX)\b", re.I)
    checked = 0
    for path in paths:
        if path.relative_to(ROOT).parts[0] not in PRODUCTION_ROOTS or path.suffix.lower() not in {".go", ".kt", ".kts", ".js", ".py", ".ps1"}:
            continue
        text = read_text(path, issues)
        if text is None:
            continue
        checked += 1
        for pattern in fatal.get(path.suffix.lower(), []):
            if re.search(pattern, text, re.I):
                issues.append(f"Obvious production placeholder: {rel(path)} pattern={pattern}")
        for line_no, line in enumerate(text.splitlines(), 1):
            if marker.search(line):
                warnings.append(f"TODO/FIXME marker: {rel(path)}:{line_no}: {line.strip()[:180]}")
    stats["production_placeholder_files_scanned"] = checked


def check_workflow_refs(paths: list[Path], issues: list[str], stats: dict) -> None:
    workflows = [p for p in paths if p.parent == ROOT / ".github" / "workflows" and p.suffix.lower() in {".yml", ".yaml"}]
    # Longest extensions first + explicit right boundary prevents .kts -> .kt and .json -> .js truncation.
    path_re = re.compile(r"(?<![A-Za-z0-9_./-])((?:tests|app|plugins|ci_scripts|windows_build|android|docs)/[A-Za-z0-9_./-]+\.(?:json|yaml|kts|ps1|py|js|sh|bat|cmd|xml|yml|kt|md|txt))(?![A-Za-z0-9_.-])")
    checked = 0
    for path in workflows:
        text = read_text(path, issues)
        if text is None:
            continue
        for ref in sorted(set(path_re.findall(text))):
            checked += 1
            if not (ROOT / ref).exists():
                issues.append(f"Workflow references missing repository file: {rel(path)} -> {ref}")
    stats.update(workflow_local_refs_checked=checked, workflow_files=len(workflows))


def make_inventory(items: list[Path], sources: list[Path]) -> dict:
    manifest = []
    for path in sources:
        data = path.read_bytes()
        manifest.append({"path": rel(path), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
    return {
        "total_files": len(items),
        "text_source_config_files": len(sources),
        "suffix_counts": dict(sorted(Counter(p.suffix.lower() or "<no-ext>" for p in items).items())),
        "top_level_counts": dict(sorted(Counter(p.relative_to(ROOT).parts[0] for p in items).items())),
        "source_manifest": manifest,
    }


def main() -> None:
    issues: list[str] = []
    warnings: list[str] = []
    stats: dict[str, object] = {}
    items = files(); sources = source_files(items)
    stats["utf8_source_files"] = sum(read_text(p, issues) is not None for p in sources)
    check_python([p for p in items if p.suffix.lower() == ".py"], issues, warnings, stats)
    check_html([p for p in items if p.suffix.lower() == ".html"], issues, stats)
    check_structured(items, ".json", json.loads, "JSON", issues, stats)
    check_structured(items, ".xml", ET.fromstring, "XML", issues, stats)
    check_requirements(items, issues, stats)
    check_placeholders(sources, issues, warnings, stats)
    check_workflow_refs(sources, issues, stats)
    inv = make_inventory(items, sources)
    result = {"result": "FAIL" if issues else "PASS", "inventory": inv, "stats": stats, "issues": issues, "warnings": warnings}
    REPORT.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print("DEEP REPOSITORY AUDIT")
    print("Files enumerated:", inv["total_files"])
    print("Source/config files strict-decoded and hashed:", inv["text_source_config_files"])
    for key, value in stats.items(): print(f"{key}: {value}")
    print("Warnings requiring review:", len(warnings))
    for warning in warnings[:100]: print("WARNING:", warning)
    if issues:
        print("\nFAILURES:")
        for issue in issues: print("-", issue)
        raise SystemExit(1)
    print("\nRESULT: PASS — every enumerated repository source/config file passed the applicable static integrity checks.")


if __name__ == "__main__":
    main()
