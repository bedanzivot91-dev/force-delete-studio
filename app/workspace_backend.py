from __future__ import annotations

"""Backend bridge for the production/timeline workspace.

The mature renderer in v3_features already supports text/outline colours and an
optional waveform, but the old one-button v3 task never forwarded those
arguments.  The workspace uses the same renderer and same subtitle database;
this patch only exposes options the renderer genuinely implements.
"""

import re
from pathlib import Path
from typing import Any


def _ass_color(value: Any, default: str) -> str:
    raw = str(value or "").strip()
    if re.fullmatch(r"&H[0-9A-Fa-f]{8}", raw):
        return raw.upper()
    match = re.fullmatch(r"#?([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})", raw)
    if not match:
        return default
    red, green, blue = match.groups()
    return f"&H00{blue}{green}{red}".upper()


def apply(core: Any) -> dict[str, Any]:
    if getattr(core, "_workspace_backend_v1_installed", False):
        return {}

    def v3_lyric_video_task(task: Any, options: dict[str, Any]) -> None:
        song_id = str(options.get("id") or "")
        song = core.DB.get_song(song_id)
        if not song:
            raise RuntimeError("Pesma nije pronađena.")

        source = core._v3_audio_path(song)
        cues = core.load_subtitle_cues(song, core.DB)
        if not cues:
            raise RuntimeError(
                "Pesma nema sačuvane LRC/SRT titlove. Otvori radnu površinu, dodaj ili učitaj titlove i sačuvaj ih pre rendera."
            )

        aspect = str(options.get("aspect") or "16:9")
        if aspect not in {"16:9", "9:16"}:
            aspect = "16:9"
        suffix = "vertikalni" if aspect == "9:16" else "youtube"
        target_dir = Path(
            str(
                options.get("target")
                or (
                    core.get_download_dir()
                    / "Lyric_video"
                    / core.sanitize_filename(str(song.get("title") or song_id), 100)
                )
            )
        ).expanduser()
        target_dir.mkdir(parents=True, exist_ok=True)
        output = target_dir / f"{core.sanitize_filename(str(song.get('title') or song_id), 100)}-{suffix}.mp4"

        background_raw = str(options.get("background") or "").strip()
        background = Path(background_raw).expanduser() if background_raw else None
        font = str(options.get("font") or "Arial").strip() or "Arial"
        font_size = max(24, min(140, int(options.get("font_size") or (64 if aspect == "9:16" else 54))))
        text_color = _ass_color(options.get("text_color"), "&H00FFFFFF")
        outline_color = _ass_color(options.get("outline_color"), "&H00000000")
        waveform = bool(options.get("waveform", False))

        task.set_progress(5, 100, "Priprema timeline titlova i pozadine")
        result = core.render_lyric_video(
            song,
            source,
            cues,
            output,
            background_path=background,
            aspect=aspect,
            font=font,
            font_size=font_size,
            text_color=text_color,
            outline_color=outline_color,
            waveform=waveform,
        )
        core.DB.add_derived_file(
            song_id,
            "lyric_video",
            f"Lyric video {aspect}",
            result["path"],
            "mp4",
            float(result.get("duration") or song.get("duration") or 0),
            {
                "aspect": aspect,
                "font": font,
                "font_size": font_size,
                "text_color": text_color,
                "outline_color": outline_color,
                "waveform": waveform,
                "background": background_raw,
                "subtitle_cues": len(cues),
                "workspace": True,
            },
        )
        task.set_progress(100, 100, output.name)
        task.finish(f"Lyric video je napravljen iz radne površine: {output.name}")

    core.v3_lyric_video_task = v3_lyric_video_task
    core._workspace_backend_v1_installed = True
    return {"v3_lyric_video_task": v3_lyric_video_task}
