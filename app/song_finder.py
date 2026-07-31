from __future__ import annotations

from pathlib import Path
from typing import Any

# Formats a user can drop into the local finder (section 2 of the request:
# Shorts/video/audio, no token, no internet).
SUPPORTED_EXTENSIONS = {
    ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".webm", ".mp4", ".mov", ".mkv",
}

# Thresholds for interpreting audio_match.compare_signatures()'s output
# specifically for short clips (5-180s Shorts), which legitimately have
# less matchable audio available than a full-length YouTube upload.
# compare_signatures() itself is shared with the existing YouTube<->Suno
# completeness feature and is NOT modified here (its own internal
# candidate floor stays at 12s/score 46, tuned for that use case) --
# these constants only decide how THIS feature reads its output.
# Calibrated against tests/song_finder_test.py's synthetic cases and the
# real-FFmpeg CI end-to-end step (this sandbox has no FFmpeg to calibrate
# against real audio directly).
CONFIRMED_MIN_SCORE = 68.0
CONFIRMED_MIN_SECONDS = 6.0
POSSIBLE_MIN_SCORE = 48.0
POSSIBLE_MIN_SECONDS = 4.0

STATUS_CONFIRMED = "confirmed"
STATUS_POSSIBLE = "possible"
STATUS_NOT_FOUND = "not_found"

_STATUS_ORDER = {STATUS_CONFIRMED: 0, STATUS_POSSIBLE: 1, STATUS_NOT_FOUND: 2}

STATUS_LABELS_SR = {
    STATUS_CONFIRMED: "POTVRĐENO",
    STATUS_POSSIBLE: "MOGUĆE PODUDARANJE",
    STATUS_NOT_FOUND: "NIJE PRONAĐENA MOJA PESMA",
}


def is_supported_file(path: Path) -> bool:
    return path.suffix.lower() in SUPPORTED_EXTENSIONS


def classify_match(analysis: dict[str, Any]) -> str:
    """Turn a compare_signatures() result into confirmed/possible/not_found."""
    score = float(analysis.get("audio_score") or 0)
    seconds = float(analysis.get("matched_seconds") or analysis.get("covered_seconds") or 0)
    if score >= CONFIRMED_MIN_SCORE and seconds >= CONFIRMED_MIN_SECONDS:
        return STATUS_CONFIRMED
    if score >= POSSIBLE_MIN_SCORE and seconds >= POSSIBLE_MIN_SECONDS:
        return STATUS_POSSIBLE
    return STATUS_NOT_FOUND


def confidence_percent(analysis: dict[str, Any]) -> int:
    return max(0, min(100, round(float(analysis.get("audio_score") or 0))))


def rank_song_candidates(candidates: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Best match first: confirmed > possible > not_found, then by score."""
    return sorted(
        candidates,
        key=lambda c: (_STATUS_ORDER.get(str(c.get("status")), 3), -float(c.get("audio_score") or 0)),
    )


def select_distinct_matches(ranked: list[dict[str, Any]], overlap_ratio: float = 0.5) -> list[dict[str, Any]]:
    """Pick out matches that occupy genuinely DIFFERENT parts of the clip
    (e.g. two of the user's own songs mixed into one Shorts), instead of
    just "1 primary + alternatives". Greedily walks the already-ranked list
    (best first) and only rejects a candidate when its clip_start/clip_end
    window really overlaps an already-picked one -- two different songs
    matched at two different timestamps both survive."""
    selected: list[dict[str, Any]] = []
    for candidate in ranked:
        if str(candidate.get("status")) == STATUS_NOT_FOUND:
            continue
        c_start = float(candidate.get("clip_start") or 0)
        c_end = float(candidate.get("clip_end") or c_start)
        conflict = False
        for kept in selected:
            k_start = float(kept.get("clip_start") or 0)
            k_end = float(kept.get("clip_end") or k_start)
            overlap = max(0.0, min(c_end, k_end) - max(c_start, k_start))
            base = min(max(0.0, c_end - c_start), max(0.0, k_end - k_start))
            if base > 0 and overlap / base >= overlap_ratio:
                conflict = True
                break
        if not conflict:
            selected.append(candidate)
    return selected
