from __future__ import annotations

"""Final guard separating cheap YouTube preflight from real audio indexing.

The real owned-channel audio scan explicitly passes ``required_for_youtube``
when missing Suno fingerprints must be repaired.  A lightweight preflight uses
``finish_task=False`` without that flag and must never refresh thousands of
Suno songs merely because its task type happens to be ``youtube_audio_owned``.
"""

from typing import Any


def apply(core: Any) -> dict[str, Any]:
    if getattr(core, "_youtube_preflight_final_fix_v1", False):
        return {"youtube_preflight_final_fix_installed": True}

    previous = core.song_finder_index_task
    youtube_task_types = {"youtube_audio_owned", "youtube_audio_url", "youtube_audio_one"}

    def guarded_index(task: Any, options: dict[str, Any] | None = None) -> Any:
        opts = dict(options or {})
        explicit_required = bool(opts.get("required_for_youtube")) or bool(opts.get("required_for_recognition"))
        cheap_preflight = (
            getattr(task, "type", "") in youtube_task_types
            and not explicit_required
            and not bool(opts.get("finish_task", True))
            and not bool(opts.get("force"))
        )
        if not cheap_preflight:
            return previous(task, opts)

        # recognition_final_fixes historically inferred "required" from the
        # task type. Temporarily mask only that routing hint so its own
        # not-required branch delegates to the mature lightweight preflight.
        original_type = getattr(task, "type", "")
        try:
            task.type = "youtube_audio_preflight"
            return previous(task, opts)
        finally:
            task.type = original_type

    core.song_finder_index_task = guarded_index
    core._youtube_preflight_final_fix_v1 = True
    return {
        "song_finder_index_task": guarded_index,
        "youtube_preflight_final_fix_installed": True,
    }
