from __future__ import annotations
import json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
from v3_features import find_lyrics_timestamp, teaser_clip_from_text, TEASER_MIN_DURATION, TEASER_MAX_DURATION


def main():
    checks = []

    cues = [
        {"start_s": 0.0, "end_s": 4.0, "text": "Prvi stih pesme"},
        {"start_s": 4.0, "end_s": 8.5, "text": "Ovde počinje refren, o volim te"},
        {"start_s": 8.5, "end_s": 12.0, "text": "Drugi deo refrena"},
        {"start_s": 40.0, "end_s": 44.0, "text": "Ovde počinje refren, o volim te"},
    ]

    # -- exact substring match, case/diacritics-insensitive --
    hit = find_lyrics_timestamp(cues, "volim te")
    assert hit is not None and hit["start"] == 4.0
    checks.append('find_lyrics_timestamp: plain substring match found')

    hit_upper = find_lyrics_timestamp(cues, "VOLIM TE")
    assert hit_upper is not None and hit_upper["start"] == 4.0
    checks.append('find_lyrics_timestamp: case-insensitive')

    hit_diacritics = find_lyrics_timestamp(cues, "pocinje refren")  # no dijakritika u upitu
    assert hit_diacritics is not None and hit_diacritics["start"] == 4.0
    checks.append('find_lyrics_timestamp: diacritics-insensitive (počinje vs pocinje)')

    # -- repeated line: must return the EARLIEST occurrence --
    hit_repeat = find_lyrics_timestamp(cues, "refren")
    assert hit_repeat is not None and hit_repeat["start"] == 4.0
    checks.append('find_lyrics_timestamp: repeated line resolves to earliest occurrence')

    # -- no match --
    assert find_lyrics_timestamp(cues, "ovaj tekst ne postoji u pesmi") is None
    checks.append('find_lyrics_timestamp: unmatched text returns None')

    # -- empty query --
    assert find_lyrics_timestamp(cues, "   ") is None
    checks.append('find_lyrics_timestamp: blank query returns None, not a false match')

    # -- teaser_clip_from_text: window starts a bit before the match, sized to `duration` --
    clip = teaser_clip_from_text(cues, "volim te", total_duration=180.0, duration=30.0, pre_roll=2.0)
    assert clip is not None
    assert clip["start"] == 2.0  # 4.0 - 2.0 pre_roll
    assert clip["duration"] == 30.0
    assert clip["end"] == 32.0
    assert clip["matched_at"] == 4.0
    assert not clip["approximate"]
    checks.append('teaser_clip_from_text: correct pre-roll window and duration')

    # -- clip must stay inside [0, total_duration] even near the very start --
    clip_at_start = teaser_clip_from_text(cues, "prvi stih", total_duration=180.0, duration=30.0, pre_roll=2.0)
    assert clip_at_start is not None and clip_at_start["start"] == 0.0 and clip_at_start["end"] == 30.0
    checks.append('teaser_clip_from_text: clamps to song start instead of going negative')

    # -- clip must stay inside [0, total_duration] near the very end --
    end_cues = [{"start_s": 175.0, "end_s": 179.0, "text": "Poslednji stih"}]
    clip_at_end = teaser_clip_from_text(end_cues, "poslednji stih", total_duration=180.0, duration=30.0, pre_roll=2.0)
    assert clip_at_end is not None and clip_at_end["end"] == 180.0 and clip_at_end["start"] == 150.0
    checks.append('teaser_clip_from_text: clamps to song end without exceeding total duration')

    # -- unmatched text returns None (caller must show a clear "not found" error) --
    assert teaser_clip_from_text(cues, "nepostojeći tekst", total_duration=180.0, duration=30.0) is None
    checks.append('teaser_clip_from_text: unmatched text returns None')

    # -- approximate flag surfaces the generated-draft (no real LRC/SRT) case --
    draft_cues = [{"start_s": 10.0, "end_s": 14.0, "text": "Neki stih", "source": "generated-draft"}]
    approx_clip = teaser_clip_from_text(draft_cues, "neki stih", total_duration=180.0, duration=30.0)
    assert approx_clip is not None and approx_clip["approximate"] is True
    checks.append('teaser_clip_from_text: flags approximate (generated-draft) timing so the UI can warn the user')

    # -- teaser duration range sanity (30-50s per the user's request) --
    assert TEASER_MIN_DURATION == 30.0 and TEASER_MAX_DURATION == 50.0
    checks.append('teaser duration constants match the requested 30-50s range')

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
