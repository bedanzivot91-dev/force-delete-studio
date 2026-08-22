"""Imports every app/ and plugins/ module individually and records the real
result (this is what section 9 of the build spec calls for: prove each
module actually imports from the embedded interpreter, not just that the
file exists on disk).

Run with: python import_smoke_test.py
Exit code is non-zero if any REQUIRED module fails to import.
"""
from __future__ import annotations

import importlib
import json
import sys
import traceback
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP_DIR = ROOT / "app"
PLUGINS_DIR = ROOT / "plugins"

sys.path.insert(0, str(APP_DIR))
sys.path.insert(0, str(PLUGINS_DIR))

# plugins are optional AI workers; a missing faster-whisper/audio-separator
# install must not fail the whole smoke test, it should be reported as such.
OPTIONAL = {"transcribe_worker", "stems_worker", "cdp"}

MODULES = sorted(p.stem for p in APP_DIR.glob("*.py")) + sorted(p.stem for p in PLUGINS_DIR.glob("*.py"))

results = {}
required_failed = []
for name in MODULES:
    try:
        importlib.import_module(name)
        results[name] = {"status": "OK", "optional": name in OPTIONAL}
    except Exception as exc:  # noqa: BLE001 - intentionally broad, this is a diagnostic sweep
        results[name] = {
            "status": "FAIL",
            "optional": name in OPTIONAL,
            "error": f"{type(exc).__name__}: {exc}",
            "traceback": traceback.format_exc(),
        }
        if name not in OPTIONAL:
            required_failed.append(name)

print(json.dumps({
    "python_version": sys.version,
    "modules_checked": len(MODULES),
    "results": {k: {kk: vv for kk, vv in v.items() if kk != "traceback"} for k, v in results.items()},
    "required_failed": required_failed,
}, indent=2, ensure_ascii=False))

if required_failed:
    sys.exit(1)
