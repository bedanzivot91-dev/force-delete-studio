from __future__ import annotations

import ast
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPORT = ROOT / "deep-repository-audit-report.json"

EXCLUDED_PARTS = {
    ".git", ".gradle", ".idea", ".vscode", "__pycache__", "node_modules",
    "build", "dist", "out", "target", ".venv", "venv", "artifact-download",
    "coverage", ".coverage", "htmlcov",
}

TEXT_SUFFIXES = {
    ".py", ".js", ".html", ".css", ".go", ".kt", ".kts", ".xml", ".json",
    ".yml", ".yaml", ".ps1", ".bat", ".cmd", ".sh", ".gradle", ".properties",
    ".toml", ".ini", ".cfg", ".txt", ".md",
}

PRODUCTION_ROOTS = {"app", "plugins", "windows_build", "android", "ci_scripts"}


class HtmlAudit(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.ids: list[str] = []

    def handle_starttag(self, tag: str, attrs_raw) -> None:
        attrs = dict(attrs_raw)
        element_id = attrs.get("id")
        if element_id:
            self.ids.append(str(element_id))


def relative(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def wanted(path: Path) -> bool:
    rel = path.relative_to(ROOT)
    return not any(part in EXCLUDED_PARTS for part in rel.parts)


def all_files() -> list[Path]:
    return sorted(p for p in ROOT.rglob("*") if p.is_file() and wanted(p))


def source_files(files: list[Path]) -> list[Path]:
    return [p for p in files if p.suffix.lower() in TEXT_SUFFIXES or p.name in {"gradlew", "Dockerfile"}]


def strict_text(path: Path, issues: list[str]) -> str | None:
    try:
        return path.read_text(encoding="utf-8")
    except UnicodeDecodeError as exc:
        issues.append(f"UTF-8 decode failed: {relative(path)}: {exc}")
    except OSError as exc:
        issues.append(f"Cannot read source/config file: {relative(path)}: {exc}")
    return None


def python_scope_duplicates(tree: ast.AST, file_label: str, issues: list[str]) -> None:
    def visit_scope(node: ast.AST, scope_name: str) -> None:
        body = getattr(node, "body", [])
        names: list[tuple[str, int, str]] = []
        for child in body:
            if isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                names.append((child.name, child.lineno, type(child).__name__))
        counts = Counter(name for name, _line, _kind in names)
        for name, count in counts.items():
            if count > 1:
                locations = [str(line) for n, line, _kind in names if n == name]
                issues.append(
                    f"Duplicate Python definition in one scope: {file_label}:{scope_name}:{name} lines {','.join(locations)}"
                )
        for child in body:
            if isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                visit_scope(child, f"{scope_name}.{child.name}")

    visit_scope(tree, "<module>")


def is_obvious_stub(node: ast.AST) -> bool:
    body = getattr(node, "body", None)
    if not isinstance(body, list) or not body:
        return False
    meaningful = [stmt for stmt in body if not (
        isinstance(stmt, ast.Expr) and isinstance(getattr(stmt, "value", None), ast.Constant)
        and isinstance(stmt.value.value, str)
    )]
    if len(meaningful) != 1:
        return False
    stmt = meaningful[0]
    if isinstance(stmt, ast.Pass):
        return True
    if isinstance(stmt, ast.Expr) and isinstance(stmt.value, ast.Constant) and stmt.value.value is Ellipsis:
        return True
    if isinstance(stmt, ast.Raise):
        exc = stmt.exc
        if isinstance(exc, ast.Name) and exc.id == "NotImplementedError":
            return True
        if isinstance(exc, ast.Call) and isinstance(exc.func, ast.Name) and exc.func.id == "NotImplementedError":
            return True
    return False


def check_python(paths: list[Path], issues: list[str], warnings: list[str], stats: dict) -> None:
    function_count = 0
    class_count = 0
    parsed = 0
    for path in paths:
        text = strict_text(path, issues)
        if text is None:
            continue
        try:
            tree = ast.parse(text, filename=relative(path))
            compile(tree, relative(path), "exec")
            parsed += 1
        except SyntaxError as exc:
            issues.append(f"Python syntax/compile failed: {relative(path)}: {exc}")
            continue
        python_scope_duplicates(tree, relative(path), issues)
        top = path.relative_to(ROOT).parts[0] if path.relative_to(ROOT).parts else ""
        production = top in PRODUCTION_ROOTS
        for node in ast.walk(tree):
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                function_count += 1
                if production and is_obvious_stub(node):
                    issues.append(f"Production Python function is an obvious stub: {relative(path)}:{node.lineno}:{node.name}")
            elif isinstance(node, ast.ClassDef):
                class_count += 1
            elif isinstance(node, ast.ExceptHandler):
                if production and len(node.body) == 1 and isinstance(node.body[0], ast.Pass):
                    warnings.append(f"Silent except/pass: {relative(path)}:{node.lineno}")
    stats["python_files_parsed"] = parsed
    stats["python_functions"] = function_count
    stats["python_classes"] = class_count


def check_html(paths: list[Path], issues: list[str], stats: dict) -> None:
    parsed = 0
    ids = 0
    for path in paths:
        text = strict_text(path, issues)
        if text is None:
            continue
        parser = HtmlAudit()
        try:
            parser.feed(text)
            parser.close()
            parsed += 1
        except Exception as exc:
            issues.append(f"HTML parse failed: {relative(path)}: {exc}")
            continue
        dupes = sorted(k for k, v in Counter(parser.ids).items() if v > 1)
        if dupes:
            issues.append(f"Duplicate HTML ids: {relative(path)}: {', '.join(dupes)}")
        ids += len(parser.ids)
    stats["html_files_parsed"] = parsed
    stats["html_ids"] = ids


def check_json(paths: list[Path], issues: list[str], stats: dict) -> None:
    parsed = 0
    for path in paths:
        text = strict_text(path, issues)
        if text is None:
            continue
        try:
            json.loads(text)
            parsed += 1
        except Exception as exc:
            issues.append(f"JSON parse failed: {relative(path)}: {exc}")
    stats["json_files_parsed"] = parsed


def check_xml(paths: list[Path], issues: list[str], stats: dict) -> None:
    parsed = 0
    for path in paths:
        text = strict_text(path, issues)
        if text is None:
            continue
        try:
            ET.fromstring(text)
            parsed += 1
        except Exception as exc:
            issues.append(f"XML parse failed: {relative(path)}: {exc}")
    stats["xml_files_parsed"] = parsed


def check_requirements(files: list[Path], issues: list[str], stats: dict) -> None:
    reqs = [p for p in files if p.name.startswith("requirements") and p.suffix == ".txt"]
    for path in reqs:
        text = strict_text(path, issues)
        if text is None:
            continue
        for lineno, raw in enumerate(text.splitlines(), 1):
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith(("-r ", "--requirement ")):
                target = line.split(maxsplit=1)[1].strip()
                resolved = (path.parent / target).resolve()
                if not resolved.exists():
                    issues.append(f"Requirements include missing file: {relative(path)}:{lineno}: {target}")
    stats["requirements_files"] = len(reqs)


def check_obvious_placeholders(paths: list[Path], issues: list[str], warnings: list[str], stats: dict) -> None:
    fail_patterns = {
        ".go": [r"panic\(\s*[\"'](?:TODO|not implemented|placeholder)", r"IMPLEMENT_ME"],
        ".kt": [r"\bTODO\s*\(", r"NotImplementedError\s*\("],
        ".kts": [r"\bTODO\s*\(", r"NotImplementedError\s*\("],
        ".js": [r"throw\s+new\s+Error\(\s*[\"'](?:TODO|not implemented|placeholder)"],
    }
    warning_pattern = re.compile(r"\b(TODO|FIXME|XXX)\b", re.IGNORECASE)
    checked = 0
    for path in paths:
        top = path.relative_to(ROOT).parts[0] if path.relative_to(ROOT).parts else ""
        if top not in PRODUCTION_ROOTS:
            continue
        if path.suffix.lower() not in {".go", ".kt", ".kts", ".js", ".py", ".ps1"}:
            continue
        text = strict_text(path, issues)
        if text is None:
            continue
        checked += 1
        for pat in fail_patterns.get(path.suffix.lower(), []):
            if re.search(pat, text, re.IGNORECASE):
                issues.append(f"Obvious production placeholder found: {relative(path)} pattern={pat}")
        for lineno, line in enumerate(text.splitlines(), 1):
            if warning_pattern.search(line):
                warnings.append(f"TODO/FIXME marker: {relative(path)}:{lineno}: {line.strip()[:180]}")
    stats["production_placeholder_files_scanned"] = checked


def check_local_workflow_refs(paths: list[Path], issues: list[str], stats: dict) -> None:
    workflows = [p for p in paths if p.parent == ROOT / ".github" / "workflows" and p.suffix.lower() in {".yml", ".yaml"}]
    checked_refs = 0
    # Conservative: only validate obvious repository-relative script paths that are quoted or bare in run commands.
    path_re = re.compile(r"(?<![A-Za-z0-9_./-])((?:tests|app|plugins|ci_scripts|windows_build|android|docs)/[A-Za-z0-9_./-]+\.(?:py|ps1|js|sh|bat|cmd|kt|kts|xml|md))")
    for path in workflows:
        text = strict_text(path, issues)
        if text is None:
            continue
        for ref in sorted(set(path_re.findall(text))):
            # references can target files generated during build; only fail when parent exists in source and filename is source-like
            candidate = ROOT / ref
            checked_refs += 1
            if not candidate.exists():
                issues.append(f"Workflow references missing repository file: {relative(path)} -> {ref}")
    stats["workflow_local_refs_checked"] = checked_refs
    stats["workflow_files"] = len(workflows)


def inventory(files: list[Path], text_paths: list[Path]) -> dict:
    suffix_counts = Counter((p.suffix.lower() or "<no-ext>") for p in files)
    top_counts = Counter((p.relative_to(ROOT).parts[0] if p.relative_to(ROOT).parts else "<root>") for p in files)
    manifest = []
    for path in text_paths:
        try:
            data = path.read_bytes()
        except OSError:
            continue
        manifest.append({
            "path": relative(path),
            "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(),
        })
    return {
        "total_files": len(files),
        "text_source_config_files": len(text_paths),
        "suffix_counts": dict(sorted(suffix_counts.items())),
        "top_level_counts": dict(sorted(top_counts.items())),
        "source_manifest": manifest,
    }


def main() -> None:
    issues: list[str] = []
    warnings: list[str] = []
    stats: dict[str, object] = {}
    files = all_files()
    text_paths = source_files(files)

    # Strict UTF-8 pass over every source/config file counted by this audit.
    decoded = 0
    for path in text_paths:
        if strict_text(path, issues) is not None:
            decoded += 1
    stats["utf8_source_files"] = decoded

    check_python([p for p in files if p.suffix.lower() == ".py"], issues, warnings, stats)
    check_html([p for p in files if p.suffix.lower() == ".html"], issues, stats)
    check_json([p for p in files if p.suffix.lower() == ".json"], issues, stats)
    check_xml([p for p in files if p.suffix.lower() == ".xml"], issues, stats)
    check_requirements(files, issues, stats)
    check_obvious_placeholders(text_paths, issues, warnings, stats)
    check_local_workflow_refs(text_paths, issues, stats)

    inv = inventory(files, text_paths)
    result = {
        "result": "FAIL" if issues else "PASS",
        "inventory": inv,
        "stats": stats,
        "issues": issues,
        "warnings": warnings,
    }
    REPORT.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")

    print("DEEP REPOSITORY AUDIT")
    print(f"Files enumerated: {inv['total_files']}")
    print(f"Source/config files strict-decoded and hashed: {inv['text_source_config_files']}")
    for key, value in stats.items():
        print(f"{key}: {value}")
    print(f"Warnings requiring review: {len(warnings)}")
    for warning in warnings[:100]:
        print("WARNING:", warning)
    if len(warnings) > 100:
        print(f"... {len(warnings) - 100} additional warnings are in {REPORT.name}")
    if issues:
        print("\nFAILURES:")
        for issue in issues:
            print("-", issue)
        print(f"\nFull machine-readable report: {REPORT}")
        raise SystemExit(1)
    print("\nRESULT: PASS — every enumerated repository source/config file passed the applicable static integrity checks.")
    print(f"Machine-readable report: {REPORT}")


if __name__ == "__main__":
    main()
