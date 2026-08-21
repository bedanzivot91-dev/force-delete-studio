from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LEGIBILITY = ROOT / "app" / "web" / "modern_2026_legibility_extension.js"


def main() -> None:
    text = LEGIBILITY.read_text(encoding="utf-8")
    sizes = [float(value) for value in re.findall(r"font-size\s*:\s*([0-9]+(?:\.[0-9]+)?)px", text)]
    assert sizes, "Nisu pronađene eksplicitne veličine fonta u finalnom 2026 sloju."
    assert min(sizes) >= 12.5, f"Finalni 2026 sloj ima sitan tekst: minimum {min(sizes)}px"
    for token in (
        "body.sps-modern-2026{font-size:15px",
        ".nav-item{font-size:14px",
        ".btn{font-size:14px",
        "input,body.sps-modern-2026 select,body.sps-modern-2026 textarea{font-size:14px",
        ".pws-track-label",
        ".matrix-status",
    ):
        assert token in text, token
    print(f"readability floor: OK — {len(sizes)} explicit font sizes, minimum {min(sizes)}px")


if __name__ == "__main__":
    main()
