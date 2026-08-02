# Phase Status

Read this before starting any work. Update the row when a phase finishes. Details for each phase are
in `docs/MASTER_SPEC.md` — read only that phase's section, not the whole file, and never the original
giant prompt again.

| Phase | Name | Status | Finished commit |
|---|---|---|---|
| 0 | Audit | DONE | (this commit) |
| 1 | Build, dependencies, release foundation | NOT_STARTED | — |
| 2 | Existing-feature hardening | NOT_STARTED | — |
| 3 | Five new themes | NOT_STARTED | — |
| 4 | Song library + fingerprinting | NOT_STARTED | — |
| 5 | AI pipeline (worker, faster-whisper, Demucs, WhisperX) | NOT_STARTED | — |
| 6 | Caption/word data model + editor | NOT_STARTED | — |
| 7 | Caption styling + video layout/OCR | NOT_STARTED | — |
| 8 | Timeline + player | NOT_STARTED | — |
| 9 | Render pipeline | NOT_STARTED | — |
| 10 | Finish or remove planned-feature tiles | NOT_STARTED | — |
| 11 | Final QA + distribution | NOT_STARTED | — |

## Baseline at end of Phase 0

- Build: clean, 0 warnings/errors (8/8 projects).
- Tests: 70/70 passing total — 66 run locally in the dev sandbox, 4 Whisper-model-download integration
  tests verified separately on Windows CI (sandbox proxy blocks huggingface.co).
- Function matrix: 19 `WORKING_VERIFIED`, 33 `IMPLEMENTED_NOT_RUNTIME_VERIFIED`, 0 `BROKEN`,
  0 `PLACEHOLDER`, 9 `NOT_PRESENT`, 0 `BLOCKED_BY_DEPENDENCY` (61 rows total — see
  `function-contracts.json`).
- 10/10 of the master prompt's "known problems" claims verified true against real source/CI evidence
  (see `BASELINE_AUDIT.md` §6) — none rejected, none assumed.
- No behavior changed this phase; only `CLAUDE.md` and `docs/*` were added.

## Next action

Start Phase 1 (build/dependencies/release foundation) only when told to proceed — Phase 0's master
prompt explicitly says not to auto-advance.
