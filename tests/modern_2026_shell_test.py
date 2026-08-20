from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SHELL = ROOT / "app" / "web" / "modern_2026_shell_extension.js"
BACKEND = ROOT / "app" / "workspace_backend.py"


def main() -> None:
    shell = SHELL.read_text(encoding="utf-8")
    backend = BACKEND.read_text(encoding="utf-8")

    assert "sps-shell-2026" in shell
    assert "2026 WORKSPACE · MATCH RECOVERY" in shell
    assert "data.uiGeneration" in shell or "dataset.uiGeneration" in shell
    assert "SUNO I BIBLIOTEKA" in shell
    assert "AUDIO I VIDEO" in shell
    assert "YOUTUBE" in shell
    assert "SISTEM" in shell
    assert "section.appendChild(button)" in shell, "navigation must MOVE existing controls, not clone them"
    assert "stage.appendChild(view)" in shell, "views must be moved as the same DOM nodes"
    assert "Segoe UI Variable" in shell
    assert "#productionWorkspace" in shell
    assert "#view-tools" in shell and "#view-recognition" in shell

    sizes = [float(value) for value in re.findall(r"font-size\s*:\s*([0-9]+(?:\.[0-9]+)?)px", shell)]
    assert sizes, "no font sizes found in shell"
    assert min(sizes) >= 12.5, f"2026 shell contains unreadable font size: {min(sizes)}px"
    assert max(sizes) >= 28.0, "workspace heading is not visually distinct"

    legibility_pos = backend.index('modern_2026_legibility_extension.js')
    shell_pos = backend.index('modern_2026_shell_extension.js')
    assert shell_pos > legibility_pos, "2026 shell must load after every legacy/legibility layer"

    unbounded_pos = backend.index('apply_unbounded_operations(core)')
    recovery_pos = backend.index('apply_youtube_match_recovery(core)')
    assert recovery_pos > unbounded_pos, "YouTube match recovery must wrap the final unbounded scanner"

    print("modern_2026_shell_test: PASS — final shell + readable type + organized navigation")


if __name__ == "__main__":
    main()
