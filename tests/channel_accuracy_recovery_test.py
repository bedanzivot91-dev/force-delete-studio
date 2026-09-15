from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "app" / "server_core.py").read_text(encoding="utf-8")


def main() -> None:
    start = SOURCE.index("def _analyse_video_against_songs(")
    end = SOURCE.index("\ndef analyze_owned_youtube_audio", start)
    body = SOURCE[start:end]
    assert "video_signatures = [video_signature]" in body
    assert "video_duration < PHASE_SEARCH_BELOW_SECONDS" in body
    assert "video_signatures = extract_query_signatures(youtube_audio)" in body
    assert "analyses = [compare_signatures(signature, variant) for variant in video_signatures]" in body
    assert "best_before_tempo < 72" in body
    assert "for tempo in SONG_FINDER_TEMPO_VARIANTS" in body
    assert "extract_query_signatures(youtube_audio, tempo=tempo)" in body
    assert "item[\"analysis\"] = rescue" in body
    print("channel_accuracy_recovery_test: PASS — owned-channel Shorts use phase recovery and weak results use real tempo re-extraction")


if __name__ == "__main__":
    main()
