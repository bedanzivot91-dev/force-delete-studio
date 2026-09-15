from __future__ import annotations

"""Accuracy recovery for owned YouTube/Shorts audio matching.

The fast fingerprint shortlist is intentionally the first pass, but it must not
be allowed to turn a performance optimisation into a false negative.  This
module is installed *after* the unbounded YouTube patch and adds four rules:

1. A short owned YouTube video is compared with the same proportional 1-4 s
   minimum used by the short-clip finder.  The previous owned-channel path still
   called compare_signatures() with its 12 s default, which made a clean 8-10 s
   Short impossible to confirm.
2. scan_mode=new retries old uncertain/different-audio rows instead of treating
   any historical audio_checked_at value as permanently finished.
3. If the normal top-20 fingerprint/metadata shortlist is weak, retry with an
   expanded shortlist.  For a stubborn Short only, fall back to the complete
   library.  Cached Suno fingerprints are reused by youtube_reconcile_fixes, so
   this does not re-fetch thousands of Suno audio files.
4. Connecting a YouTube account no longer stops after metadata.  Once the fast
   metadata job is finished and the task slot is idle, a Shorts-only audio
   recovery job starts automatically for all new/uncertain Shorts.  A manual
   user task always wins and suppresses this automatic follow-up.
"""

import threading
import time
from typing import Any


_RECOVERY_LOCK = threading.RLock()
_AUTO_LOCK = threading.RLock()
_AUTO_GENERATION = 0
_PROCESSED = {"complete", "almost_complete", "partial", "short_clip"}


def _score(result: dict[str, Any] | None) -> float:
    try:
        return float((result or {}).get("audio_score") or 0.0)
    except (TypeError, ValueError):
        return 0.0


def _matched_seconds(result: dict[str, Any] | None) -> float:
    try:
        return float((result or {}).get("matched_seconds") or (result or {}).get("covered_seconds") or 0.0)
    except (TypeError, ValueError):
        return 0.0


def _is_reliable(result: dict[str, Any] | None) -> bool:
    if not result:
        return False
    status = str(result.get("completeness_status") or "")
    score = _score(result)
    matched = _matched_seconds(result)
    if status not in _PROCESSED:
        return False
    if status == "short_clip":
        # Scores <=71 have been measured on wrong short clips; a short match is
        # accepted only above that danger zone unless it already carries >=10 s
        # of matched material.
        return (score >= 78.0 and matched >= 1.0) or (score >= 60.0 and matched >= 10.0)
    return score >= 48.0 and matched >= 10.0


def _better(left: dict[str, Any] | None, right: dict[str, Any] | None) -> dict[str, Any] | None:
    if left is None:
        return right
    if right is None:
        return left
    rank = {"complete": 5, "almost_complete": 4, "partial": 3, "short_clip": 2, "different_audio": 1, "": 0}
    lk = (rank.get(str(left.get("completeness_status") or ""), 0), _score(left), _matched_seconds(left))
    rk = (rank.get(str(right.get("completeness_status") or ""), 0), _score(right), _matched_seconds(right))
    return right if rk > lk else left


def _cleanup_stale_negative_rows(core: Any, video_id: str, keep_song_id: str = "") -> None:
    if not video_id:
        return
    try:
        with core.DB._lock, core.DB._connect() as conn:
            if keep_song_id:
                conn.execute(
                    """DELETE FROM youtube_matches
                       WHERE video_id=? AND song_id<>?
                         AND audio_checked_at IS NOT NULL AND audio_checked_at<>''
                         AND (COALESCE(completeness_status,'') IN ('','different_audio')
                              OR COALESCE(confidence,'')='low'
                              OR COALESCE(audio_score,0)<48)""",
                    (video_id, keep_song_id),
                )
            else:
                # Do not erase a good historical match.  This branch is used
                # only to prune duplicate weak rows left by old matchers.
                conn.execute(
                    """DELETE FROM youtube_matches
                       WHERE video_id=?
                         AND audio_checked_at IS NOT NULL AND audio_checked_at<>''
                         AND COALESCE(completeness_status,'') IN ('','different_audio')
                         AND rowid NOT IN (
                           SELECT rowid FROM youtube_matches
                           WHERE video_id=?
                           ORDER BY COALESCE(audio_score,0) DESC, COALESCE(score,0) DESC
                           LIMIT 1
                         )""",
                    (video_id, video_id),
                )
    except Exception as exc:
        core.runtime_log(f"Nisu očišćeni stari false-negative YouTube zapisi za {video_id}: {exc}", "warning")


def _install_owned_youtube_accuracy_recovery(core: Any) -> dict[str, Any]:
    original_analyse = core._analyse_video_against_songs
    original_scan = core.analyze_owned_youtube_audio

    def analyse_video_against_songs(
        task: Any,
        video: dict[str, Any],
        songs: list[dict[str, Any]],
        owned_ids: set[str],
        options: dict[str, Any],
    ) -> dict[str, Any] | None:
        if not songs:
            return original_analyse(task, video, songs, owned_ids, options)

        video_duration = float(video.get("duration") or 0.0)
        with _RECOVERY_LOCK:
            original_compare = core.compare_signatures
            original_shortlist = core._song_finder_shortlist
            original_limit = int(getattr(core, "SONG_FINDER_SHORTLIST", 20) or 20)

            def owned_compare(source: dict[str, Any], target: dict[str, Any], min_match_seconds: float | None = None) -> dict[str, Any]:
                duration = float(target.get("duration") or video_duration or 0.0)
                if min_match_seconds is None and 0 < duration <= 70.0:
                    needed = float(core.required_match_seconds(duration))
                    result = dict(original_compare(source, target, min_match_seconds=needed))
                    score = float(result.get("audio_score") or 0.0)
                    matched = float(result.get("matched_seconds") or result.get("covered_seconds") or 0.0)
                    # compare_signatures is also a completeness analyser. For a
                    # clip shorter than 10 s its full-song status normally stays
                    # different_audio even when identity is excellent. Promote
                    # only above the measured short false-positive danger zone.
                    if (
                        str(result.get("completeness_status") or "") == "different_audio"
                        and score >= 78.0
                        and matched >= needed
                    ):
                        result["completeness_status"] = "short_clip"
                        result["confidence"] = "medium" if score >= 86.0 else "low"
                        result["reason"] = (
                            str(result.get("reason") or "").rstrip()
                            + f" Kratak YouTube klip je audio-identifikovan sa {score:.0f}% preko {matched:.1f} s."
                        ).strip()
                    return result
                if min_match_seconds is None:
                    return original_compare(source, target)
                return original_compare(source, target, min_match_seconds=min_match_seconds)

            def run_pass(
                shortlist_size: int | None = None,
                exhaustive: bool = False,
                library: list[dict[str, Any]] | None = None,
            ) -> dict[str, Any] | None:
                pass_library = library if library is not None else songs
                core.compare_signatures = owned_compare
                if exhaustive:
                    core._song_finder_shortlist = lambda _sig, current: (list(current), False)
                    core.SONG_FINDER_SHORTLIST = max(original_limit, len(pass_library))
                elif shortlist_size:
                    core._song_finder_shortlist = original_shortlist
                    core.SONG_FINDER_SHORTLIST = max(original_limit, min(int(shortlist_size), len(pass_library)))
                else:
                    core._song_finder_shortlist = original_shortlist
                    core.SONG_FINDER_SHORTLIST = original_limit
                return original_analyse(task, video, pass_library, owned_ids, options)

            try:
                best = run_pass()
                if _is_reliable(best):
                    return best

                expanded = min(len(songs), max(128, original_limit * 6))
                if expanded > original_limit:
                    task.log(
                        f"„{video.get('title') or video.get('video_id')}“: prvi brzi shortlist nije dao pouzdan audio pogodak; "
                        f"proširujem proveru na {expanded} fingerprint kandidata.",
                        "info",
                    )
                    best = _better(best, run_pass(shortlist_size=expanded))
                    if _is_reliable(best):
                        return best

                # A full-library fallback is needed only for short-form videos,
                # where the title is often a quote and metadata has almost no
                # value. Process the library in bounded batches and stop as soon
                # as a reliable audio identity is found. This preserves recall
                # without forcing all 3000 comparisons after the correct song was
                # already found in an earlier batch.
                if 0 < video_duration <= 70.0 and len(songs) > expanded:
                    batch_size = 256
                    task.log(
                        f"„{video.get('title') or video.get('video_id')}“: Short nije pouzdano pronađen u brzom indeksu; "
                        f"pokrećem završni fallback kroz Biblioteku u paketima od {batch_size}, koristeći sačuvane otiske.",
                        "warning",
                    )
                    recovered: dict[str, Any] | None = None
                    for start in range(0, len(songs), batch_size):
                        if task.cancel_event.is_set():
                            break
                        batch = songs[start:start + batch_size]
                        candidate = run_pass(exhaustive=True, library=batch)
                        best = _better(best, candidate)
                        recovered = _better(recovered, candidate)
                        task.log(
                            f"Fallback Shortsa: provereno do {min(start + len(batch), len(songs))}/{len(songs)} pesama.",
                            "info",
                        )
                        if _is_reliable(candidate):
                            recovered = candidate
                            break

                    video_id = str(video.get("video_id") or "")
                    if recovered and _is_reliable(recovered):
                        keep_song_id = str(recovered.get("song_id") or "")
                        _cleanup_stale_negative_rows(core, video_id, keep_song_id)
                    else:
                        _cleanup_stale_negative_rows(core, video_id)
                    return best
                return best
            finally:
                core.compare_signatures = original_compare
                core._song_finder_shortlist = original_shortlist
                core.SONG_FINDER_SHORTLIST = original_limit

    def analyze_owned_youtube_audio(task: Any, options: dict[str, Any]) -> None:
        patched = dict(options or {})
        # "new" must mean new + previously uncertain. Treating every old
        # audio_checked_at as final preserved false negatives made by older
        # matchers forever after an upgrade.
        if str(patched.get("scan_mode") or "new").strip().lower() == "new":
            patched["scan_mode"] = "uncertain"
        return original_scan(task, patched)

    core._analyse_video_against_songs = analyse_video_against_songs
    core.analyze_owned_youtube_audio = analyze_owned_youtube_audio
    return {
        "_analyse_video_against_songs": analyse_video_against_songs,
        "analyze_owned_youtube_audio": analyze_owned_youtube_audio,
    }


def _install_automatic_shorts_audio_followup(core: Any) -> dict[str, Any]:
    original_start = core.start_automatic_youtube_pipeline

    def start_automatic_youtube_pipeline(delay_seconds: float = 1.5) -> None:
        global _AUTO_GENERATION
        # Preserve the fast metadata job first; it populates channel/video rows
        # and is useful even when audio download is temporarily unavailable.
        original_start(delay_seconds)
        with _AUTO_LOCK:
            _AUTO_GENERATION += 1
            generation = _AUTO_GENERATION

        def follow_up() -> None:
            time.sleep(max(0.0, float(delay_seconds)) + 0.75)
            deadline = time.monotonic() + 15 * 60
            while time.monotonic() < deadline:
                with _AUTO_LOCK:
                    if generation != _AUTO_GENERATION:
                        return
                with core.STATE_LOCK:
                    active = core.ACTIVE_TASK
                    status = str(getattr(active, "status", "")) if active is not None else ""
                    task_type = str(getattr(active, "type", "")) if active is not None else ""
                if active is not None and status == "running":
                    # Wait only for the automatic metadata job. If the user has
                    # started another job, user intent wins and auto recovery
                    # quietly yields instead of recreating the old task lockout.
                    if task_type not in {"youtube_owned"}:
                        core.runtime_log(
                            "Automatska Shorts audio provera je preskočena jer je korisnik pokrenuo drugi posao.",
                            "info",
                        )
                        return
                    time.sleep(1.0)
                    continue
                break
            else:
                core.runtime_log("Automatska Shorts audio provera nije pokrenuta jer metadata posao nije završio u očekivanom vremenu.", "warning")
                return

            with _AUTO_LOCK:
                if generation != _AUTO_GENERATION:
                    return
            options = {
                "scan_mode": "new",  # recovery wrapper converts this to new + uncertain
                "max_videos_per_channel": 0,  # 0 = all, no application-side cap
                "shorts_only": True,
                "reuse_cache": True,
                "include_private_unlisted": True,
            }
            try:
                core.start_task(
                    "youtube_audio_owned",
                    "Automatska audio provera novih i sumnjivih Shorts videa",
                    lambda task: core.analyze_owned_youtube_audio(task, options),
                    persistent_payload=options,
                )
                core.runtime_log(
                    "Posle povezivanja kanala pokrenuta je automatska audio provera svih novih i ranije sumnjivih Shorts videa; 0 znači bez limita programa.",
                    "info",
                )
            except Exception as exc:
                core.runtime_log(f"Automatska Shorts audio provera nije pokrenuta: {exc}", "warning")

        threading.Thread(target=follow_up, daemon=True, name="youtube-auto-shorts-audio").start()

    core.start_automatic_youtube_pipeline = start_automatic_youtube_pipeline
    return {"start_automatic_youtube_pipeline": start_automatic_youtube_pipeline}


def apply(core: Any) -> dict[str, Any]:
    if getattr(core, "_youtube_match_recovery_v2_installed", False):
        return {}
    exports = _install_owned_youtube_accuracy_recovery(core)
    exports.update(_install_automatic_shorts_audio_followup(core))
    core._youtube_match_recovery_v2_installed = True
    core.runtime_log(
        "YouTube match recovery v2 aktivan: Shorts koriste 1-4 s minimum, stari sumnjivi rezultati se ponavljaju, slab shortlist dobija accuracy fallback, a posle povezivanja sledi neblokirajuća Shorts audio provera.",
        "info",
    )
    return exports