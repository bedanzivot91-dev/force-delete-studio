# test-data/

## `local/` (gitignored - never commit anything here)

Phase 11 (final QA, see `docs/MASTER_SPEC.md`) calls for a real regression pass against the user's own
Shorts clip: ~9:16, 1080x1920, ~24s, ~30fps, contains singing, may already have on-screen text/logo/CTA
burned in. This is real, user-owned footage - it must never be committed to this repository (privacy,
copyright, repo size), which is why `test-data/local/` is listed in `.gitignore`.

To run the Phase 11 regression pass:

1. Drop your own clip into `test-data/local/` (any filename).
2. Run it through the real pipelines this phase is meant to validate end-to-end: import into a project,
   song-highlight/lyric-search/subtitle-generation as applicable, timeline placement, and a real render
   via the export screen.
3. Verify a real human check of the output: correct orientation/resolution, audio in sync, no crashes,
   captions (if used) burned in at the right position/timing for a 9:16 frame.

This step has not been run in this session - there is no such file in this sandbox, and this pipeline
step genuinely requires the user's own footage (fabricating a substitute clip here would defeat the
purpose: this check exists specifically to catch real-world edge cases synthetic lavfi test clips can't,
like actual singing audio, aspect-ratio-specific caption safe zones, and pre-existing on-screen text).
