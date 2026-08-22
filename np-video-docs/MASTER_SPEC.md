# Master Spec — condensed, phase-indexed

Source: user's full master prompt (2026-08-02), preserved verbatim in this session's transcript. This
file is the condensed working reference — read only the phase you're currently on, not the whole file,
and never re-read the original giant prompt from scratch. Global rules that apply to every phase are in
`CLAUDE.md`. Phase completion report format is fixed — see bottom of this file.

## Non-negotiable ground rules (apply to every phase)

- Extend the existing NP Video Studio (.NET 8 + Avalonia), never fork/rewrite in another stack.
- Don't duplicate existing services — check `docs/FUNCTION_MATRIX.md` / `ARCHITECTURE.md` first.
- A feature that isn't finished must not look active in the UI (disabled tile + "Uskoro", not a live
  button that no-ops). Already enforced today — keep enforcing it.
- No destructive git ops, no deleting user projects, no un-migrated `.npvsproject` format changes, no
  silent DB deletion.
- Never run ffmpeg/ffprobe/yt-dlp/Whisper/any long job on the UI thread; always cancellable.
- Max ~8-12 source files changed per phase (excluding tests/theme resources); full test suite only at
  end of phase; end each phase with the fixed report format below, then stop — don't auto-continue.

## Phase 0 — Audit (COMPLETE, this session)

Deliverables: `CLAUDE.md`, `docs/BASELINE_AUDIT.md`, `docs/FUNCTION_MATRIX.md`,
`docs/function-contracts.json`, `docs/ARCHITECTURE.md`, `docs/MASTER_SPEC.md` (this file),
`docs/PHASE_STATUS.md`. No behavior changed.

## Phase 1 — Build, dependencies, release foundation

- `DependencyManager`: central class managing ffmpeg/ffprobe/yt-dlp/fpcalc(Chromaprint)/AI
  worker/Demucs/Whisper/alignment/OCR models. Resolution order: user setting → `Tools/<tool>/` →
  app folder → system PATH. Track name/expected+actual version/path/status/checksum/license/last-
  checked/dependents.
- New "Alati i modeli" screen: Installed / Not installed / Update available / Corrupt / Incompatible /
  Checking / Downloading / Repair / Open folder. Downloads need: confirmation, size shown, progress,
  cancel, `.part` temp file, checksum verify, atomic rename, retry, clear error — never leave a
  half-downloaded file looking valid.
- Wire `AppSettings.FfmpegPath/FfprobePath/YtDlpPath` into the actual Settings UI (they exist in the
  model today, not exposed — see BASELINE_AUDIT §8).
- ffmpeg/ffprobe/yt-dlp version checks must run the tool's version command and check exit code, not
  just check file existence (already true for FFmpeg/FFprobe/yt-dlp in `FfmpegLocator`/
  `DiagnosticsService` — extend the same pattern to any new tool).
- Portable structure: real `Tools/ffmpeg/`, `Tools/yt-dlp/`, `Tools/chromaprint/` with bundled
  LICENSE.txt files. Fix Portable "ZIP inside ZIP" (root cause: `upload-artifact` re-zipping an already-
  zipped file — see BASELINE_AUDIT §6 row 8). Release cleanup: strip PDBs (separate symbols zip if
  needed), strip non-win-x64 Whisper native libs, strip `obj`/test assets/logs/user data from the
  published output. Fix version mismatch (installer says 0.1.0, compiled assembly reports 1.0.0.0 —
  BASELINE_AUDIT §6 row 10): set `<Version>` centrally (e.g. `Directory.Build.props`) and keep the
  installer/README/UI in sync with it.

## Phase 2 — Existing features hardening

Verify every row currently `IMPLEMENTED_NOT_RUNTIME_VERIFIED` in `FUNCTION_MATRIX.md` with a real test
or a committed (not ad-hoc) UI smoke test: projects save/load/autosave/recovery/recent list, media
import (file picker + drag-and-drop), FFprobe, favorites/remove, settings save/reset, diagnostics
run/auto-fix/support-package, YouTube fetch-info/download (currently has **no** automated test for the
actual yt-dlp process call — flagged in FUNCTION_MATRIX as a real gap), song highlights,
lyric search, subtitle generator.

## Phase 3 — Five new themes

Add `ObsidianNeon.axaml`, `ArcticGlass.axaml`, `CrimsonCyber.axaml`, `MidnightPro.axaml`,
`OceanGlass.axaml` alongside the existing 3 (total 8). Exact palettes and semantic resource key list are
in the original prompt (colors given per theme, ~22 semantic brush keys required:
Background/Surface/Panel/Input/Hover/Pressed/Accent/AccentHover/AccentSubtle/Text/SubtleText/Border/
Success/Warning/Error/Info/Timeline/Track/Waveform/Playhead/Selection/CaptionActive/CaptionInactive).
No hardcoded colors in Views — already the existing convention, keep it. Add a theme-gallery screen
with live preview cards, WCAG-AA-ish contrast, hover/pressed/disabled states, "Reduce motion" toggle,
runtime switching without restart (already supported by `ApplyTheme`). Add a test that loads every
`ResourceDictionary` and flags missing semantic keys.

## Phase 4 — Song library + fingerprinting

New "Moje pesme" screen + SQLite table (with migration + backup-before-migration): id, title, artist,
album?, original audio path, analysis WAV, duration/sample-rate/channels, fingerprint, full lyrics,
per-line lyrics, optional LRC/word timestamps, added-date, verification status, language, script,
notes, linked Shorts projects. CRUD + import (WAV/MP3/FLAC/text/LRC/paste), duplicate check, re-analyze,
"find projects using this song", delete-record-without-deleting-file (file delete needs separate
confirm). `ISongRecognitionService`: Chromaprint/fpcalc multi-window fingerprinting (start/quarter/
mid/three-quarter/end + high-energy windows, 5-15s configurable) → candidates with confidence/offset/
agreeing-vs-conflicting windows/tempo-ratio/pitch-shift/warnings. Auto-accept only when ≥2 windows agree
+ offsets consistent + confidence over threshold + tempo/pitch sane; otherwise show top 3, require user
pick or "none of these" — never guess. Check AGPL licensing before adding any matcher library; document
in `THIRD_PARTY_NOTICES.md` (Phase 11).

## Phase 5 — AI pipeline (known song → verified lyrics; unknown song → ASR)

Local AI worker (Python OK only for faster-whisper/WhisperX/Demucs/OCR/advanced audio analysis /
forced alignment) as a CLI subprocess, JSON-in / JSONL-events-out, versioned protocol (schema given in
original prompt), no HTTP server unless unavoidable, no binary audio through JSON (temp files + absolute
paths). Known song: use verified lyrics, not ASR guesses; ASR only helps alignment; keep raw ASR for
QA; locate the relevant excerpt via fingerprint offset + clip time + envelope + ASR keywords + fuzzy
matching + DP + existing LRC/word timestamps. Unknown song: extract audio → normalized WAV → ASR on raw
→ Demucs vocals → ASR on vocals → consensus → flag uncertain words → allow manual fix. Profiles: Fast
(Whisper.net/faster-whisper small, CPU int8), Balanced (faster-whisper large-v3-turbo + VAD + word
timestamps + Demucs if music detected), Most-accurate (Demucs + large-v3 + multi-pass + WhisperX
alignment + quality report, no auto-export at low confidence). Whisper.net stays as the lightweight/
offline/low-end fallback — do not remove it. Serbian: Latin+Cyrillic, auto-detect, manual script choice,
Cyrillic↔Latin conversion without mutating the stored original, correct UTF-8/Windows-path handling for
č/ć/š/ž/đ (note dž vs đ, č vs ć), ekavica/ijekavica only as an optional tolerant matcher, don't let
normalization overwrite the original text.

## Phase 6 — Caption/word data model + editor

Word-level model: original text, normalized text, start, end, confidence, `source` (one of
`verified_lyrics|lrc|whisper|whisperx|fuzzy_aligned|interpolated|manual`), verification status.
Interpolation only across short gaps between confident anchors — never invent timing for a whole
unrecognized line; on alignment failure keep original text, flag the line, allow manual fix, don't mark
ready. Central caption editor: transcript/sentence/lyric/word granularity, split/merge/add/delete/
undo/redo/multi-select/time-nudge/snap/ripple-edit/find-replace/Latin+Cyrillic/uppercase/manual review.
Import/export SRT, VTT, ASS, TXT, JSON, LRC (ASS must escape `{`, `}`, `\`, newlines, Unicode
correctly).

## Phase 7 — Caption styling + video layout / OCR

Each of the 8 themes gets ≥3 caption presets: line-by-line, word-by-word, karaoke/active-word, with
pop/scale/slide/fade/bounce(limited)/glow/outline/shadow/blur-panel/gradient-panel, safe margins for
16:9/9:16/1:1/4:5, preview must approximate final render. `IVideoLayoutAnalysisService`: local ONNX OCR
(e.g. RapidOCR-compatible, license-checked) on representative frames → detect existing text/logo/
watermark/CTA/subscribe-button/face/person/central-object/free vs. occupied zones over time. Auto
caption placement priority: don't cover face > don't cover existing text > don't cover logo > don't
cover CTA > stay in safe zone > minimize unnecessary repositioning > stay readable > avoid platform UI
chrome. Offer Automatic/Top/Middle/Bottom/Manual position, keep-existing-text vs. cover vs.
remove-existing-text(advanced). Blur/gradient overlay must never be presented as real AI text removal.

## Phase 8 — Timeline + player

Real, non-destructive timeline model persisted inside `.npvsproject` (not just current UI-control
state): video/audio/caption/text/image-overlay tracks, playhead, zoom, horizontal scroll, snap, split,
trim-in/out, move, delete, duplicate, mute, volume, fade in/out, lock, hide, solo, undo/redo, keyboard
shortcuts. Player: play/pause/stop/seek/frame-step/volume/mute/fullscreen/current-time/total-time/
caption overlay/safe-zone overlay/OCR-occupancy overlay/preview-quality choice. Auto-proxy (720p or
configurable) for codecs that won't play smoothly, keep link to original, never render the proxy as
final.

## Phase 9 — Render pipeline

Default MP4/H.264(libx264)/CRF 18/preset medium/AAC 192kbps, keep original resolution/fps/rotation,
`+faststart`, keep original audio unless user changes it. Support libx264/h264_nvenc/h264_qsv/h264_amf
with automatic fallback, quality choice, size estimate, progress via `-progress pipe:1`, cancel that
actually kills the process tree, multiple queued export jobs, log the ffmpeg command (no secrets).
Never leave an incomplete MP4 looking like success — temp output + atomic rename. Default output name
`originalname_captioned.mp4`; never silently overwrite without confirmation.

## Phase 10 — Finish or remove planned-feature tiles

The 6 disabled start-screen tiles ("Kreiraj video iz šablona", "Brzi video od slike i pesme",
"Automatski video sa utisnutim titlovima (na slici)", "Upravljanje šablonima", "Upravljanje fontovima",
"Upravljanje efektima") must each be either fully implemented or removed from the active UI before
final release — they're correctly disabled today (not a bug), but "disabled forever" is not an
acceptable end state per the master prompt's completion criteria.

## Phase 11 — Final QA + distribution

Full test suite, real Windows smoke test, the user's own local Shorts regression clip (never committed
to git — goes in `test-data/local/`, gitignored; ~9:16, 1080x1920, ~24s, ~30fps, contains singing, may
already have on-screen text/logo/CTA), all 8 themes, installer, portable (fixed, no double-zip),
`THIRD_PARTY_NOTICES.md` + `Licenses/` (check FFmpeg build license, yt-dlp, Whisper.net, faster-whisper,
CTranslate2, WhisperX, Demucs, Chromaprint, OCR model, matcher — flag any AGPL component explicitly
before including it), release notes, refreshed `FUNCTION_MATRIX.md`/`function-contracts.json` with zero
`BROKEN`/`PLACEHOLDER` rows among active features.

## Fixed phase-completion report format (use every phase, ~20-30 lines total)

```
Faza: <name>
Urađeno: <short list>
Promenjeni fajlovi: <paths only>
Testovi: <command> — passed/failed/skipped (+ reason for any skip)
Funkcionalna matrica: <counts by status, from function-contracts.json>
Poznati problemi: <only real ones>
Sledeća faza: <one sentence>
PHASE_COMPLETE
```
Do not proceed to the next phase automatically after printing this — wait to be told to continue.
