from __future__ import annotations

"""Accuracy recovery for owned YouTube/Shorts audio matching.

The fast fingerprint shortlist is intentionally the first pass, but it must not
be allowed to turn a performance optimisation into a false negative.  This
module is installed *after* the unbounded YouTube patch and adds three rules:

1. A short owned YouTube video is compared with the same proportional 1-4 s
   minimum used by the short-clip finder.  The previous owned-channel path still
   called compare_signatures() with its 12 s default, which made a clean 8-10 s
   Short impossible to confirm.
2. scan_mode=new retries old uncertain/different-audio rows instead of treating
   any historical audio_checked_at value as permanently finished.
3. If the normal top-20 fingerprint/metadata shortlist is weak, retry with an
   expanded shortlist.  For a stubborn Short only, fall back to the complete
   library.  Cached Suno fingerprints are reused by youtube_reconcile_fixes, so
   this is expensive only when accuracy genuinely needs it; it does not re-fetch
   thousands of Suno audio files.
"""

import threading
from typing import Any


_RECOVERY_LOCK = threading.RLock()
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
                    # compare_signatures is also a completeness analyser.  For a
                    # clip shorter than 10 s its full-song status normally stays
                    # different_audio even when the identity match is excellent.
                    # Promote only above the measured short-clip false-positive
                    # danger zone; this mirrors the dedicated short finder.
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

            def run_pass(shortlist_size: int | None = None, exhaustive: bool = False) -> dict[str, Any] | None:
                core.compare_signatures = owned_compare
                if exhaustive:
                    core._song_finder_shortlist = lambda _sig, library: (list(library), False)
                    core.SONG_FINDER_SHORTLIST = max(original_limit, len(songs))
                elif shortlist_size:
                    core._song_finder_shortlist = original_shortlist
                    core.SONG_FINDER_SHORTLIST = max(original_limit, min(int(shortlist_size), len(songs)))
                else:
                    core._song_finder_shortlist = original_shortlist
                    core.SONG_FINDER_SHORTLIST = original_limit
                return original_analyse(task, video, songs, owned_ids, options)

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

                # Full-library fallback is intentionally limited to Shorts/
                # short-form videos.  This is the case where title and duration
                # are least useful and where missing the real song is worse than
                # spending extra CPU once. Long videos keep the expanded pass so
                # one unrelated upload cannot force 3000 comparisons every time.
                if 0 < video_duration <= 70.0 and len(songs) > expanded:
                    task.log(
                        f"„{video.get('title') or video.get('video_id')}“: Short nije pouzdano pronađen u brzom indeksu; "
                        f"radim završnu proveru svih {len(songs)} pesama iz Biblioteke koristeći sačuvane otiske.",
                        "warning",
                    )
                    recovered = run_pass(exhaustive=True)
                    best = _better(best, recovered)
                    if recovered and _is_reliable(recovered):
                        keep_song_id = str(recovered.get("song_id") or "")
                        video_id = str(video.get("video_id") or "")
                        if keep_song_id and video_id:
                            try:
                                with core.DB._lock, core.DB._connect() as conn:
                                    conn.execute(
                                        """DELETE FROM youtube_matches
                                           WHERE video_id=? AND song_id<>?
                                             AND audio_checked_at IS NOT NULL AND audio_checked_at<>''
                                             AND (COALESCE(completeness_status,'') IN ('','different_audio')
                                                  OR COALESCE(confidence,'')='low'
                                                  OR COALESCE(audio_score,0)<48)""",
                                        (video_id, keep_song_id),
                                    )
                            except Exception as exc:
                                core.runtime_log(f"Nije očišćen stari false-negative YouTube zapis za {video_id}: {exc}", "warning")
                    return best
                return best
            finally:
                core.compare_signatures = original_compare
                core._song_finder_shortlist = original_shortlist
                core.SONG_FINDER_SHORTLIST = original_limit

    def analyze_owned_youtube_audio(task: Any, options: dict[str, Any]) -> None:
        patched = dict(options or {})
        # "new" must mean new + previously uncertain.  Treating every old
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


def apply(core: Any) -> dict[str, Any]:
    if getattr(core, "_youtube_match_recovery_v1_installed", False):
        return {}
    exports = _install_owned_youtube_accuracy_recovery(core)
    core._youtube_match_recovery_v1_installed = True
    core.runtime_log(
        "YouTube match recovery aktivan: kratki Shorts koriste 1-4 s minimum, stari sumnjivi rezultati se ponavljaju, a slab shortlist dobija accuracy fallback.",
        "info",
    )
    return exports
