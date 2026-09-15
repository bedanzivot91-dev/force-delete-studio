from __future__ import annotations

import json
import sys
import traceback
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "app"))

from audio_tools import ensure_ffmpeg, status as ffmpeg_status
from audio_match import ensure_deno, ensure_ytdlp, deno_status, ytdlp_status


def progress(message: str, percent: int) -> None:
    print(f"[{max(0, min(100, int(percent))):3d}%] {message}", flush=True)


def main() -> int:
    force = "--force" in sys.argv[1:]
    mode = "prinudno osvežavanje" if force else "provera i samopopravka"
    print(f"Suno Pesme Studio 3.0.0 — {mode} ugrađenih alata", flush=True)
    results = {}
    results["ffmpeg"] = ensure_ffmpeg(progress, force_update=force)
    results["deno"] = ensure_deno(progress, force_update=force)
    results["yt_dlp"] = ensure_ytdlp(progress, force_update=force)
    final = {"ffmpeg": ffmpeg_status(), "deno": deno_status(), "yt_dlp": ytdlp_status()}
    missing = [name for name, value in final.items() if not value.get("ready")]
    (ROOT / "data").mkdir(parents=True, exist_ok=True)
    (ROOT / "data" / "tools_status.json").write_text(json.dumps(final, ensure_ascii=False, indent=2), encoding="utf-8")
    if missing:
        raise RuntimeError("Nisu spremni alati: " + ", ".join(missing))
    print("Svi osnovni alati su spremni.", flush=True)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception as exc:
        print("GREŠKA PRI PRIPREMI ALATA:", exc, file=sys.stderr, flush=True)
        traceback.print_exc()
        raise SystemExit(1)
