# Phase Status

Read this before starting any work. Update the row when a phase finishes. Details for each phase are
in `docs/MASTER_SPEC.md` — read only that phase's section, not the whole file, and never the original
giant prompt again.

| Phase | Name | Status | Finished commit |
|---|---|---|---|
| 0 | Audit | DONE | f850ace |
| 1 | Build, dependencies, release foundation | DONE (partial - see below) | 1193f77 |
| 2 | Existing-feature hardening | DONE (partial - see below) | cf63149 |
| 3 | Five new themes | DONE (partial - see below) | 5114fbb |
| 4 | Song library + fingerprinting | DONE (partial - see below) | 7c5d6e2 |
| 5 | AI pipeline (worker, faster-whisper, Demucs, WhisperX) | DONE (partial - see below) | 1354fb4 |
| 6 | Caption/word data model + editor | DONE (partial - see below) | 6ab46b0 |
| 7 | Caption styling + video layout/OCR | DONE (partial - see below) | 802004d |
| 8 | Timeline + player | DONE (partial - see below) | bb13397 |
| 9 | Render pipeline | DONE (partial - see below) | 6a4bba5 |
| 10 | Finish or remove planned-feature tiles | DONE (partial - see below) | 764d223 |
| 11 | Final QA + distribution | DONE (partial - see below) | 4876d95 |

## Baseline at end of Phase 0

- Build: clean, 0 warnings/errors (8/8 projects).
- Tests: 70/70 passing total — 66 run locally in the dev sandbox, 4 Whisper-model-download integration
  tests verified separately on Windows CI (sandbox proxy blocks huggingface.co).
- Function matrix: 19 `WORKING_VERIFIED`, 33 `IMPLEMENTED_NOT_RUNTIME_VERIFIED`, 0 `BROKEN`,
  0 `PLACEHOLDER`, 9 `NOT_PRESENT`, 0 `BLOCKED_BY_DEPENDENCY` (61 rows total).
- 10/10 of the master prompt's "known problems" claims verified true against real source/CI evidence
  (see `BASELINE_AUDIT.md` §6) — none rejected, none assumed.
- No behavior changed this phase; only `CLAUDE.md` and `docs/*` were added.

## What Phase 1 actually delivered (and what it deliberately didn't)

Delivered:
- Version mismatch fixed for real: `Directory.Build.props` sets `<Version>0.1.0</Version>`; verified
  the compiled assembly now reports `0.1.0.0` (was `1.0.0.0`), and `App.axaml.cs: ThisAssemblyVersion()`
  formats it to exactly `0.1.0`, matching the installer.
- New `IDependencyManagerService`/`DependencyManagerService` (Diagnostics project) + "Alati i modeli"
  screen (`DependencyManagerViewModel`/`View`) showing real Installed/Not-installed status (real
  version-command exit codes, not file-existence guesses) for FFmpeg, FFprobe, yt-dlp, and the Whisper
  model, with a cancellable model download — the app's first Cancel button anywhere.
- `AppSettings.FfmpegPath/FfprobePath/YtDlpPath` wired into the Settings screen (was model-only before,
  BASELINE_AUDIT §8) — with an explicit note that changes need a restart to take effect.
- Portable "ZIP inside ZIP" root cause fixed: CI now uploads the extracted `NPVideoStudio-Portable-x64`
  folder as the workflow artifact instead of an already-zipped file; `build-release.ps1` also produces a
  real `NPVideoStudio-Portable-x64-<version>.zip` (renamed from the old unversioned
  `NPVideoStudio-Portable.zip`) with `VERSION.txt`/`README-FIRST.txt` inside, matching the master
  prompt's requested structure.
- Release cleanup: `build-release.ps1` now strips `.pdb` files and every non-`win-x64` `runtimes/`
  folder (Linux/macOS/win-arm64/win-x86 Whisper native libs) from the publish output before both the
  portable zip and the installer are built from it.
- New tests: `DependencyManagerServiceTests.cs` (6, real ffmpeg/ffprobe + genuinely-absent yt-dlp) and
  one new `[AvaloniaFact]` in `AppSmokeTests.cs` that exercises the real navigation-to-Whisper-status
  chain end to end. Total local (non-integration) tests: 73/73 passing.
- Function matrix updated: 22 `WORKING_VERIFIED`, 38 `IMPLEMENTED_NOT_RUNTIME_VERIFIED`, 0 `BROKEN`,
  0 `PLACEHOLDER`, 7 `NOT_PRESENT`, 0 `BLOCKED_BY_DEPENDENCY` (67 rows).

Deliberately not done (out of scope for what's realistically achievable/verifiable from a Linux sandbox
without downloading large binaries, per the token-saving and dependency-vetting rules):
- No actual `fpcalc`/Chromaprint/Demucs/OCR/AI-worker dependency tracking — none of those tools are
  used by any feature yet (Phases 4/5/7), so `DependencyManagerService` only tracks what's real today.
- No bundled `Tools/ffmpeg/`, `Tools/yt-dlp/` binaries in the repo/portable package — bundling real
  Windows binaries can't be built or verified from this Linux sandbox, and downloading ~100MB+ of
  platform-specific binaries wasn't attempted; the app still resolves these via PATH or a user-set path,
  same as before. This remains a real, open gap.
- Richer dependency states (Ažuriranje dostupno/Oštećeno/Nekompatibilno) — no checksum or
  expected-version pinning system exists to honestly back those, so they weren't faked.
Phase 1 was confirmed on real Windows CI at commit `1193f77` (after fixing 2 test failures the first
Phase 1 CI run caught - see below): `Verzija: 0.1.0` printed by the script, PDB/non-win-x64 runtime
cleanup confirmed by inspecting the actual Inno Setup compression log (only `runtimes\win-x64\*.dll`
present, no `.pdb` files), and the portable artifact upload confirmed to send 238 raw files (not a
pre-made zip), fixing the double-zip for real.

**Real CI-only failures caught and fixed during Phase 1** (both were test-design bugs exposed by real
Windows CI, not app bugs): `DependencyManagerServiceTests`'s "yt-dlp not installed" test assumed a
nonexistent override path meant "not installed", but `FfmpegLocator` falls back to bare `"yt-dlp"` on
PATH when the override file doesn't exist - and the Windows runner genuinely has yt-dlp on PATH (via
choco), so the fallback silently found the real tool. Fixed by pointing the override at a real
non-executable file, forcing that exact (broken) path to be used regardless of environment. Separately,
the new `AppSmokeTests` dependency-manager test budgeted only ~1s for 3 real sequential process
launches - too tight under real CI load; bumped to 15s.

## What Phase 2 actually delivered

Delivered (targeted, not exhaustive - see "not done" below):
- `tests/FakeYtDlp`: a real, cross-platform mock `yt-dlp` CLI (built like any other project, its native
  apphost lands next to the test binary via a normal ProjectReference) that understands exactly the
  argument shapes `YouTubeDownloadService` sends. This is the master spec's explicitly-named "yt-dlp
  servis sa mock procesom" test. `YouTubeDownloadServiceTests.cs` (6 tests) now covers real process
  launch, JSON parsing, non-YouTube-URL rejection, non-zero-exit-code handling, the ownership-
  confirmation guard, and output-file renaming - closing the single gap the master spec called out by
  name for this phase.
- `SongHighlightsViewModelTests.cs` (3 tests): drives `PickSongCommand`/`AnalyzeCommand`/
  `ExportAllCommand` end to end through real ffmpeg against the real `lyric_test_song.mp3` fixture, with
  a `FakeStorageService` standing in for file/folder pickers. Found and fixed a real test-authoring bug
  along the way (used a 2s highlight window; the ViewModel itself enforces a 10s floor and was silently
  rejecting the request) - not an app bug, but proof the test was actually exercising the real
  validation path.
- `SettingsViewModelTests.cs` (3 tests): drives `SaveCommand`/`ResetToDefaultsCommand` against a real,
  isolated `SettingsService`, reloading from disk with a fresh instance to prove persistence rather than
  just checking the in-memory object.
- These upgrades are all ViewModel-level tests constructed directly (no `Application`/`Window`), a
  deliberately different pattern from the `[AvaloniaFact]` tests added in Phase 1 - ViewModels are plain
  MVVM objects, and constructing them directly avoids both the Avalonia-dispatcher-pumping complexity
  hit in Phase 1 and any risk of mutating the shared `App.Services` singleton state that other
  `[AvaloniaFact]` tests in the same run also depend on.
- Local (non-integration) test count: 73 → 85, all passing. Function matrix: 22 → 28 `WORKING_VERIFIED`,
  38 → 32 `IMPLEMENTED_NOT_RUNTIME_VERIFIED` (67 rows total, 0 `BROKEN`/`PLACEHOLDER` throughout).

Deliberately not done this phase (Phase 2's full scope per MASTER_SPEC lists ~15 areas; converting every
remaining `IMPLEMENTED_NOT_RUNTIME_VERIFIED` row in one pass would far exceed a reasonable phase size):
- Project lifecycle (`newproject.create`, `workspace.save-project`, `start.open-project`,
  `start.recent.open`) was considered but deliberately skipped this phase: testing it through the real
  `App.Services` DI container (the only place these ViewModels' full dependency graph is wired) would
  mean writing real files into this sandbox's real Documents folder and a real shared SQLite db that
  other `[AvaloniaFact]` tests in the same test run also touch, with no isolation mechanism in place yet
  (the app's composition root doesn't support an injectable settings path). Doing this safely needs
  either a settings-path override hook in `App.axaml.cs` or a way to isolate the DI container per test -
  neither exists yet, so it's flagged for a future phase rather than risking test pollution.
- Media import (drag-and-drop + file picker), Diagnostics run-checks/auto-fix/support-package via the
  ViewModel (only ever tested at the service level), and the per-item `OpenExportedCommand`/
  `OpenFolderCommand`/`OpenGeneratedSrtCommand` "open in OS file browser" commands across every tool
  screen remain `IMPLEMENTED_NOT_RUNTIME_VERIFIED` - none of these surfaced a real bug when reasoned
  through, and file-browser-opening commands are inherently hard to assert against in a headless test.

## What Phase 3 actually delivered

Delivered:
- Five new theme resource dictionaries under `src/NPVideoStudio.App/Themes/`, matching the master
  prompt's palettes and the existing themes' exact structural template (Color keys → SolidColorBrush
  keys → `ThemeCornerRadius`/`ThemeCardCornerRadius`/`ThemeBorderThickness`): `ObsidianNeon` (dark,
  violet accent), `ArcticGlass` (light, semi-transparent glass panel, blue accent), `CrimsonCyber`
  (dark, sharp/geometric corners, cyan accent), `MidnightPro` (dark, matches DarkCinematic's corner
  radii, blue accent), `OceanGlass` (light glass, teal accent). Total: 3 → 8 themes, closing the
  5-extra-themes gap from BASELINE_AUDIT §6.
- `AppTheme` enum (`Domain/Enums.cs`) extended with the 5 new values; `EnumLabelConverter` gives each a
  Serbian display label; `App.axaml.cs: ApplyTheme` extended to map all 8 to their files and to treat
  `ArcticGlass`/`OceanGlass` as Light-variant themes (alongside the existing `MinimalLight`) so
  Avalonia's built-in Light/Dark control styling matches each theme's actual background lightness.
  Runtime theme switching (already supported, no restart needed) covers all 8 without changes to that
  mechanism.
- Two new/extended test layers: `ThemeResourceCompletenessTests.cs` (9 tests, plain XML parsing, no
  Avalonia runtime) asserts exactly 8 theme files exist and each defines all 15 required semantic
  resource keys — catches a missing key that would otherwise silently render nothing at runtime.
  `AppSmokeTests.cs: AllEightThemes_LoadAsRealAvaloniaResourceDictionaries` goes further and loads each
  theme through Avalonia's real `ResourceInclude`/`avares://` parser (the same class `ApplyTheme` uses)
  and resolves `ThemeAccentBrush` from it, so a malformed color value or a typo'd file name throws here
  instead of only failing at manual runtime. Local (non-integration) test count: 85 → 95, all passing.
- Function matrix: no per-control row changes (theme selection is a plain `SelectedItem` binding, not a
  `Command`), but the 5-extra-themes gap noted in the `NOT_PRESENT` summary is now marked closed.

Deliberately not done this phase, per the explicit token-conscious instruction to keep Phase 3 lean:
- No dedicated theme-gallery screen with live preview cards (MASTER_SPEC §Phase 3 mentions this) — the
  existing Settings ComboBox already lets users pick and immediately see any of the 8 themes applied
  app-wide with no restart, which covers the actual user need; a separate gallery screen is a bigger,
  lower-value UI addition with no new functional coverage, deferred.
- No per-theme caption presets (MASTER_SPEC ties these to captions, "≥3 presets per theme") — there is
  no caption editor yet (that's Phase 6), so there is nothing for a caption preset to style; building
  presets now would mean designing against a UI that doesn't exist. Deferred to whichever phase adds
  the caption editor.
- No full CI round-trip verification cycle for this phase — this is a pure Avalonia XAML-resource
  change with no OS-specific process behavior (no ffmpeg/yt-dlp/Whisper involved), already exercised by
  Avalonia's real headless renderer locally; a full Windows CI run adds no additional confidence here
  proportional to its cost, so local verification (build + full non-integration test pass, both green)
  was treated as sufficient before pushing.

## What Phase 4 actually delivered

Delivered:
- New "Moje pesme" library screen (`MySongsView`/`MySongsViewModel`), reachable from the start screen:
  import audio, see it analyzed, decide whether it's a duplicate or a new song, delete a record with or
  without its audio file, re-analyze an existing entry.
- Real Chromaprint/fpcalc fingerprinting (`SongRecognitionService`, `NPVideoStudio.Media`): five windows
  per track (start/quarter/mid/three-quarter/end, 5-15s configurable) - `ffmpeg` extracts each window
  (fpcalc itself can only read from the start of a file), `fpcalc -raw` fingerprints it. Matching
  (`FingerprintMatcher`, pure/testable) uses best-alignment average Hamming distance across a bounded
  offset search - the same bit-population-count technique real Chromaprint-based matchers use.
  Auto-accept requires ≥2 agreeing windows, confidence ≥0.80, and a sane duration ratio - never guesses
  off a single window, and the UI never auto-adds a match on its own initiative regardless of
  confidence; the user always sees the top 3 candidates (or none) and decides.
- New `SongLibrary` SQLite table (`AppDatabase` schema v2) with a real file backup taken before the
  v1→v2 migration actually runs (spec: "migration + backup-before-migration") - verified by a test that
  builds a genuine v1-only database file, runs the real migration, and asserts both the backup file and
  the new table exist afterward.
- `fpcalc` (Chromaprint) added to "Alati i modeli" as a 5th tracked dependency, same optional treatment
  as yt-dlp (rest of the app works without it); CI now installs it via `choco install chromaprint` with
  `continue-on-error: true` so a missing/renamed choco package can never fail the whole Windows build.
- New tests: `FingerprintMatcherTests.cs` (7, pure Hamming-distance logic, no process),
  `SongRecognitionServiceTests.cs` (5, real ffmpeg + a new `tests/FakeFpcalc` mock process - same "mock
  process, not a fake in test code alone" pattern as `tests/FakeYtDlp`), `SongLibraryRepositoryTests.cs`
  (7, real SQLite CRUD + the migration/backup path), `MySongsViewModelTests.cs` (6, ViewModel-level
  decision flow against fakes), plus one new `AppSmokeTests.cs` navigation test. Local (non-integration)
  test count: 95 → 121, all passing.
- Function matrix: 28 → 33 `WORKING_VERIFIED`, 32 → 34 `IMPLEMENTED_NOT_RUNTIME_VERIFIED` (74 rows
  total, 0 `BROKEN`/`PLACEHOLDER` throughout).

Deliberately simplified vs. the master prompt's full Phase 4 wording (see FUNCTION_MATRIX.md's "Moje
pesme" section for the fuller reasoning):
- `SongLibraryEntry` doesn't store sample-rate/channel-count columns - this codebase has no ffprobe
  stream-level parsing to source them from honestly (`MediaAsset` only exposes `Duration`), and faking
  those values would violate the "never guess" rule more than simply omitting them.
- No "linked Shorts projects" / "find projects using this song" - the `Project` domain model has no
  concept of song usage yet; that's naturally Phase 8 (timeline) territory, not Phase 4's.
- Tempo-ratio/pitch-shift warnings are approximated by a duration-ratio check only. Real tempo/pitch
  detection is a DSP feature well beyond fingerprint comparison and was not implemented or faked.
- No AcoustID web-service lookup exists or was considered - Phase 4's fingerprinting is purely local
  (match against your own library), so the "check AGPL licensing" spec instruction is satisfied by not
  having an AcoustID client at all; only the LGPL-2.1 `fpcalc` *tool* is shelled out to, never linked.

## What Phase 5 actually delivered

Delivered:
- `IAiWorkerClient`/`AiWorkerClient` (`NPVideoStudio.AI`): the local AI worker orchestration layer the
  spec asks for - versioned (protocol v1) JSON-request-file-in / JSONL-events-out subprocess protocol,
  no HTTP, no audio bytes through JSON. A real, committed Python worker (`ai-worker/ai_worker.py`,
  `ai-worker/requirements.txt`) is bundled into the publish output at `Tools/ai-worker/` (wired via
  `NPVideoStudio.App.csproj`, same "real file, not a fabricated binary" bar as Phase 1's dependency
  work). Manually verified end-to-end in this sandbox (real `python3` + real script, no fakes): capability
  check honestly reports `faster-whisper`/`WhisperX`/`Demucs` all absent (this sandbox has no PyPI
  access to install them, and none of this was faked), and a transcription job request returns a clear
  `Error` event pointing at the missing dependency instead of a fabricated transcript.
- New 6th "Alati i modeli" dependency row ("AI radnik") via `DependencyManagerService`, following the
  exact same honesty rule as every other row here: `Installed` only when the worker is actually reachable
  **and** at least one heavier engine (faster-whisper/WhisperX/Demucs) is actually importable - not just
  because the subprocess launched without throwing.
- `KnownSongLyricLocator` (`NPVideoStudio.AI`, pure/testable): the "known song → verified lyrics, ASR
  only helps timing" half of the spec. Fuzzy-match + DP (Needleman-Wunsch-style) sequence alignment
  between a song's verified lyrics and whatever words the worker actually heard, so exported captions
  carry the correct (verified) text at the time it's actually sung. Verified lyrics text is never
  replaced by an ASR guess; interpolation is limited to short internal gaps between two confident
  anchors, and an unanchored run on either end is left unresolved rather than guessed at.
- `SerbianScriptConverter` (`NPVideoStudio.AI`, pure/testable): lossless Cyrillic↔Latin transliteration
  covering the spec's explicitly-named edge cases - đ (single letter) vs dž (digraph), č vs ć, and
  digraph casing (a single uppercase Cyrillic Љ/Њ/Џ correctly expands to title-case "Lj"/"Nj"/"Dž" or
  all-caps "LJ"/"NJ"/"DŽ" depending on the following letter's case, since Cyrillic has no separate form
  for the two). Never mutates a stored original - produces a converted copy only.
- New tests: `AiWorkerClientTests.cs` (5, real subprocess against a new `tests/FakeAiWorker` mock
  process - same "mock process" pattern as `tests/FakeYtDlp`/`tests/FakeFpcalc` - covering launch, JSONL
  parsing, non-zero-exit handling without double-reporting an error the worker already explained, and
  cancellation actually killing the process), `KnownSongLyricLocatorTests.cs` (6, pure DP-alignment
  logic incl. a mid-transcript missed word and completely unrelated ASR text), `SerbianScriptConverterTests.cs`
  (13, pure transliteration incl. round-trips). Local (non-integration) test count: 121 → 148, all
  passing. Function matrix: no new `function-contracts.json` rows (that file is UI-control-scoped, and
  this phase intentionally added no new screen - see FUNCTION_MATRIX.md's Phase 5 section for why).

Deliberately not done this phase (the real, not-yet-closed gap, same honesty bar as every prior phase):
- No actual faster-whisper/Demucs/WhisperX orchestration exists inside `ai_worker.py` - only capability
  detection and an honest "not installed" error for the two real job kinds. This sandbox has no network
  access to `pip install` any of the three, so there is no way to build and verify real transcription
  logic against them here; faking a transcript would violate this codebase's "never guess" rule far more
  than leaving the gap explicit. This is real, necessary follow-up work, not scope creep to defer -
  whoever picks this up next needs either network access to install these packages or a Windows CI job
  extended to install and exercise them.
- No UI wiring for `KnownSongLyricLocator`/`SerbianScriptConverter` - nothing in the app calls either
  outside their own tests yet. There is no caption screen to show the result to (that's Phase 6), and
  wiring a script-toggle into an existing screen without a caption editor to toggle it in would be a UI
  change with no real use, so it was deferred rather than added prematurely.
- Balanced/Most-accurate profile selection has no UI control anywhere yet (spec's three profiles are
  modeled as the `AiProcessingProfile` enum, used by `AiWorkerRequest`, but nothing in the UI lets a user
  pick one) - there's no screen that would meaningfully offer this choice until Phase 6/7 exist.

## What Phase 6 actually delivered

Delivered:
- `CaptionWord` (Domain): the word-level model the spec asks for - original text, normalized text,
  start, end, confidence, `Source` (`VerifiedLyrics|Lrc|Whisper|WhisperX|FuzzyAligned|Interpolated|Manual`,
  matching Phase 5's `AiWorkerWord`/`KnownSongLyricLocator` output), `VerificationStatus`. One deliberate,
  documented addition beyond the spec's exact field list: `LineBreakAfter` (bool) - the only way to
  express "sentence/line granularity" from a word-level model without a second parallel data structure
  that could drift out of sync with the words it groups; a caption line is just a run of words ending at
  the first one with this flag set.
- `CaptionEditSession` (`NPVideoStudio.AI`, pure/testable): split/merge/add/delete/undo/redo/time-nudge
  (with optional ripple - shifts every following word to preserve relative gaps)/find-replace, plus
  `ConvertScript` (Latin↔Cyrillic via Phase 5's `SerbianScriptConverter`, applied as an explicit, undoable
  editing action - never a silent background normalization). Undo/redo is whole-list snapshotting, not a
  command-pattern with a hand-written inverse per operation - simpler to get right, and every mutating
  method is proven correct against it in tests.
- `CaptionFormatConverter` (`NPVideoStudio.AI`, pure/testable): import + export for SRT/VTT/JSON/LRC
  (round-trip tested both ways), export-only for ASS (correct minimal `[Script Info]`/`[V4+ Styles]`/
  `[Events]` structure, proper `{`/`}`/`\`/newline escaping) and TXT.
- New "Uređivač titlova" screen (`CaptionEditorView`/`CaptionEditorViewModel`/`CaptionWordItemViewModel`),
  reachable from the start screen: new/open/save-as (format picker), per-word split/merge/delete/nudge/
  line-break-toggle, multi-select delete, find & replace, Latin↔Cyrillic conversion, undo/redo. Also
  reachable from "Generiši titlove (SRT)" via a new "Otvori u uređivaču titlova" button - the first real
  hand-off from Phase 5/1-era tooling into this phase's editor.
- New tests: `CaptionEditSessionTests.cs` (11), `CaptionFormatConverterTests.cs` (10, incl. real SRT/VTT/
  LRC/JSON round-trips and ASS escaping), plus one new `AppSmokeTests.cs` navigation test that drives a
  real new-document → add-word → undo-available round trip through the actual DI-wired ViewModel. Local
  (non-integration) test count: 148 → 170, all passing.
- Function matrix: no new `function-contracts.json` rows yet for the new screen's individual controls -
  see FUNCTION_MATRIX.md's Phase 6 section; the screen itself will get proper per-control rows next time
  FUNCTION_MATRIX.md is refreshed in depth, consistent with how Phase 4/5 handled this.

Deliberately not done this phase (real, explicitly-flagged gaps, not fabricated completeness):
- No ASS *import* - parsing arbitrary override tags/karaoke timing correctly is a much bigger job than
  writing a minimal-but-correct exporter, and a fragile parser would silently mis-import real files,
  which is worse than not supporting it yet. No TXT import either - plain text carries no timing at all,
  and inventing timestamps for it would violate this codebase's "never guess" rule more directly than any
  other gap here.
- No multi-select drag/shift-click, no keyboard shortcuts for nudge/split/merge, no per-word split-ratio
  picker (split always divides at the midpoint) - the underlying `CaptionEditSession` methods support
  arbitrary ratios and take an `IEnumerable<Guid>` for bulk operations, but the UI only exposes the common
  case (checkbox multi-select + always-50/50 split) this pass. A future phase can add richer UI over the
  same already-tested session methods without touching the editing logic itself.
- Direct in-place text edits (typing in a word's TextBox) do not go through the undo/redo stack - only
  the explicit structural operations (split/merge/delete/nudge/find-replace/insert/script-convert) do.
  Snapshotting on every keystroke would make undo nearly useless (one step per character); a real
  keystroke-coalescing undo scheme is future work, not faked here.
- `CaptionWord` is not yet wired into `.npvsproject`/the timeline - there is no timeline yet (Phase 8).
  This phase's editor works standalone against imported/exported files, exactly as scoped.

## What Phase 7 actually delivered

Delivered:
- `CaptionStylePreset`/`CaptionSafeMargins` (Domain) + `CaptionStylePresetCatalog` (App): 24 real presets
  (3 per theme x 8 themes, spec minimum), covering all three granularities (line/word/karaoke) and all
  10 named animation kinds at least once, with colors taken directly from each theme's own XAML resource
  dictionary (accent/text/accent-subtle) - never invented independently of the theme they belong to.
  Documented safe-margin estimates per aspect ratio (16:9/9:16/1:1/4:5/21:9), based on published platform
  safe-area guidance, not measured from a specific app build.
- New "Stilovi titlova" screen: browse the catalog filtered by theme, with a static color-swatch preview
  per card (real Avalonia brushes parsed from each preset's hex colors, not placeholder colors).
  Deliberately NOT a live animation preview - see "not done" below.
- `IVideoLayoutAnalysisService`/`TesseractOcrService` (Media): real local OCR-based video layout analysis
  via the Tesseract CLI (Apache 2.0), installed and run end-to-end in this sandbox while building this
  service (confirmed real bounding boxes + confidence on a generated test image before writing any
  production code). This substitutes for the spec's named ONNX/RapidOCR path - RapidOCR needs a model
  file this sandbox has no verified way to download/license-check, whereas Tesseract is a real,
  immediately-installable system package, same "shell out to an external tool" pattern as ffmpeg/yt-dlp/
  fpcalc/the Phase 5 AI worker. Samples N evenly-spaced frames via ffmpeg, runs `tesseract ... tsv` on
  each, normalizes bounding boxes to 0..1.
- `VideoLayoutAggregator` (Media, pure/testable): turns per-frame OCR regions into "how often is each
  3x3 grid zone occupied by existing text" - the honest, currently-real half of the spec's occupancy-
  over-time concept.
- `CaptionPlacementAdvisor` (AI, pure/testable): implements the *full* spec priority chain in code (face
  > text > logo > CTA > safe zone > minimize repositioning > readable > platform chrome) for "Automatic"
  mode, Manual/Top/Middle/Bottom passing straight through unchanged. Only the "existing text" signal is
  real today - see "not done" below for why face/logo/CTA aren't populated, and how the algorithm is
  already written to accept them once they exist.
- New "Analiza rasporeda videa" screen: pick a video, run the real Tesseract-based analysis, see detected
  text regions per sampled frame, per-zone occupancy percentages, and the recommended caption position
  (with a placement-mode picker and any overlap warning).
- Tesseract OCR added as a 7th tracked, optional dependency in "Alati i modeli" (same honesty rule as
  every other row: real version-command exit code, not file existence) and to Windows CI (`choco install
  tesseract`, `continue-on-error: true`, same treatment as Chromaprint - no test asserts it's present,
  since unlike ffmpeg/ffprobe it isn't guaranteed pre-installed everywhere).
- New tests: `VideoLayoutAggregatorTests.cs` (5), `CaptionPlacementAdvisorTests.cs` (8),
  `TesseractOcrServiceTests.cs` (4, parses a TSV sample captured from an actual `tesseract 5.3.4` run -
  not an invented shape), `CaptionStylePresetCatalogTests.cs` (20, catches a missing theme/typo before it
  silently renders nothing), plus 2 new `AppSmokeTests.cs` navigation tests. Local (non-integration) test
  count: 187 → 209, all passing.
- Function matrix: no new `function-contracts.json` rows yet for these two new screens' individual
  controls - same deferred-to-next-refresh treatment as Phase 6's caption editor.

Deliberately not done this phase (real, explicitly-flagged gaps):
- No live animated preview for the 24 style presets - only a static color-swatch preview. Building 10
  distinct, correct Avalonia animations (Pop/Scale/Slide/Fade/Bounce/Glow/Outline/Shadow/BlurPanel/
  GradientPanel) is a substantial separate piece of real UI-animation work; a rushed/approximate
  implementation would violate "preview must approximate final render" more than a plainly-labeled static
  preview does. Real animation playback naturally belongs with Phase 8/9 once there's an actual
  timeline/player to preview against.
- No face/person/logo/CTA/subscribe-button/central-object detection - this sandbox has no verified,
  license-clear path to a real model for any of these (unlike OCR, where Tesseract was genuinely
  available and verified). `CaptionPlacementAdvisor`'s priority algorithm already implements the full
  chain in code so a future phase can plug real detectors in without touching the algorithm - only
  `VideoLayoutAnalysisResult` needs new fields.
- No "remove existing text" (advanced) option - spec explicitly separates this from keep/cover, and it
  needs real inpainting, which needs a model this phase doesn't have either. Not started, not faked.
- The two new screens aren't wired to each other or to the caption editor yet (e.g. no "apply this
  preset to my captions" button) - there's no timeline/render pipeline yet (Phases 8-9) for a style
  preset or placement recommendation to actually apply to, so wiring them together now would be UI with
  no real effect behind it.

## What Phase 8 actually delivered

Delivered:
- `Timeline`/`TimelineTrack`/`TimelineClip` (Domain), added to `Project` and persisted inside
  `.npvsproject` (spec: "not just current UI-control state"). Word-level-model-style design: a track is
  just a list of clips, `TimelineClip.TimelineEndSeconds` is computed from trim in/out, and non-
  destructive editing never touches the underlying `MediaAsset` file - only `SourceTrimInSeconds`/
  `SourceTrimOutSeconds` change which slice plays. `Project.ProjectFormatVersion` bumped to 2;
  verified (not assumed) that a real pre-Phase-8 project file with no `timeline` property at all still
  loads correctly with an empty `Timeline`, via a test that hand-builds that exact old JSON shape rather
  than trusting System.Text.Json's default behavior blindly.
- `TimelineEditSession` (`NPVideoStudio.AI`, pure/testable): the full spec verb list - add/remove track,
  split/trim-in/trim-out/move (incl. cross-track)/delete/duplicate/mute/volume/fade-in-out/lock/hide/
  solo/undo/redo, plus `SnapToNearest` (spec's "snap"). Same whole-state-snapshot undo/redo approach as
  Phase 6's `CaptionEditSession`, for the same reason. Deliberately does not enforce "clips on a track
  never overlap" - real editors commonly allow overlapping layers (esp. caption/text/image-overlay
  tracks), and the spec never actually states that constraint.
- `PlayerStateMachine` (`NPVideoStudio.AI`, pure/testable): play/pause/stop/seek/frame-step/volume/mute/
  current-time/total-time state and arithmetic (spec's player half) - restart-from-zero-after-end,
  clamped seeking, frame-accurate stepping via a real frame-rate parameter.
- `IProxyGeneratorService`/`ProxyGeneratorService` (Media): real ffmpeg-based auto-proxy generation
  (spec: "720p or configurable ... keep link to original, never render the proxy as final") - temp file +
  atomic rename, same "never leave a half-written file looking valid" rule as every other real
  download/generation path in this codebase. Verified end-to-end in this sandbox: generated a real
  640x480 synthetic test video and confirmed the actual proxy output measures 320x240 via ffprobe - found
  and fixed a real bug along the way (the original temp-file naming used a `.part` suffix that broke
  ffmpeg's output-format auto-detection; fixed by keeping the real extension at the end of the temp name).
  Registered in DI for future use; no UI trigger button yet (see "not done").
- Real UI integration into the existing "Radni prostor" (workspace) screen, replacing its old "Timeline
  je u razvoju" placeholder: a player transport bar (play/pause/stop/step/seek slider/volume, clearly
  labeled that real video decode/render isn't wired yet) and a timeline section (add/remove tracks of all
  5 kinds, per-track lock/hide/mute/solo toggles, per-clip split-at-playhead/nudge/duplicate/delete/mute/
  fade toggles, undo/redo). Button-based operations rather than drag-to-resize/move or a pixel-accurate
  zoomed canvas - same reasoning as Phase 6's caption editor: real and fully testable without a display,
  versus an unverifiable drag/canvas-rendering interaction. `WorkspaceViewModel.SaveProjectAsync`/
  auto-save-on-import now write the timeline back into the project before saving.
- New tests: `TimelineEditSessionTests.cs` (14), `PlayerStateMachineTests.cs` (14),
  `ProxyGeneratorServiceTests.cs` (2, real ffmpeg - generates its own tiny synthetic source video via
  ffmpeg's lavfi test source rather than a committed binary fixture), `WorkspaceViewModelTests.cs` (6,
  drives the real DI-constructible `WorkspaceViewModel`/`TimelineViewModel`/`PlayerViewModel` through
  add-track/add-clip/split/undo/play-pause-stop, `[AvaloniaFact]`-based since `PlayerViewModel` needs a
  real Avalonia dispatcher for its `DispatcherTimer`), plus 2 new `ProjectRepositoryTests.cs` cases
  (timeline round-trip, pre-Phase-8 file compatibility). Local (non-integration) test count: 209 → 246,
  all passing.
- Function matrix: no new `function-contracts.json` rows yet for the new player/timeline controls - same
  deferred-to-next-refresh treatment as Phases 6/7.

Deliberately not done this phase (real, explicitly-flagged gaps):
- No real video decode/render - `PlayerStateMachine`/`PlayerViewModel` are a fully real, tested state
  machine and transport UI, but nothing actually decodes or paints a video frame yet. This sandbox has no
  display (confirmed since Phase 0: `XOpenDisplay failed`), so there is no way to verify a real decoder
  integration (e.g. LibVLCSharp) actually renders correctly here - wiring one in without any way to check
  it works would violate this codebase's "never claim what wasn't verified" rule more than leaving the
  gap explicit. A future phase with access to a real display (or at minimum a way to capture/verify
  rendered frames) should pick this up.
- No pixel-accurate zoomed timeline canvas, no mouse drag-to-move/resize/scrub, no keyboard shortcuts -
  the UI exposes the same operations via buttons instead (see above). `TimelineEditSession.SnapToNearest`
  exists and is tested but isn't wired to any UI gesture yet (there's no drag to snap during).
  `Timeline.ZoomPixelsPerSecond` exists in the persisted model but isn't read by the UI yet.
- No caption-overlay/safe-zone-overlay/OCR-occupancy-overlay/preview-quality-choice on the player (spec
  lists these) - these are meaningful once there's a real rendered frame to overlay onto; wiring overlay
  toggles onto a placeholder preview would be UI with no real effect behind it, same reasoning as Phase
  7's screens not being cross-wired yet.
- No UI trigger for `ProxyGeneratorService` (built, tested, DI-registered) - deferred purely for time;
  the real gap is genuinely just "no button calls it yet", not missing functionality underneath.
- `WorkspaceViewModel`'s `PlayerViewModel` (and its `DispatcherTimer`) isn't disposed when navigating away
  from the workspace - `MainWindowViewModel` doesn't dispose any ViewModel on navigation today (not a
  Phase-8-specific gap, but this phase's is the first ViewModel where it could actually leak a running
  timer). Left as-is rather than inventing new navigation-lifecycle infrastructure nothing else uses yet.

## What Phase 9 actually delivered

Delivered:
- `RenderJob`/`RenderSettings`/`VideoCodec`/`RenderJobStatus` (Domain, spec defaults: MP4/H.264 libx264/
  CRF 18/preset medium/AAC 192kbps/`+faststart`).
- `FfmpegFilterGraphBuilder` (`NPVideoStudio.Media`, pure/testable - no process execution, so every edge
  case is unit-testable without ffmpeg installed): builds the real `-filter_complex` graph for a project's
  timeline - trim/scale-and-pad-to-project-format/fade per clip, black+silent gap-filling between clips
  with a timing gap, `concat` across all segments, then chained `drawtext` for every Caption/Text-track
  clip at its exact timeline window. Every clip (and gap filler) is normalized to the project's own export
  resolution (`Project.Format.Width/Height`) before concat - found and fixed a real bug while building
  this: ffmpeg's `concat` filter rejects mismatched input resolutions outright, which a hardcoded
  1920x1080 gap-filler size hit immediately against 320x240 test clips. Also empirically verified (not
  assumed) `drawtext` escaping against a real ffmpeg 6.1.1 run: colon must be escaped even inside quotes
  (an unescaped colon silently truncates everything before it) and comma requires the value to be quoted
  at all (unquoted, it breaks the whole filtergraph).
- `IRenderService`/`RenderService` (`NPVideoStudio.Media`): runs the real ffmpeg render - automatic
  fallback to libx264 if a requested hardware encoder (nvenc/qsv/amf) fails, live progress via
  `-progress pipe:1` (parses `out_time_ms`, which is actually microseconds despite the name), cancellation
  that kills the real process tree, temp-file-then-atomic-rename output (never leaves a half-written file
  looking valid), overwrite-without-confirmation guard, logs the exact ffmpeg command onto the job (no
  secrets - every argument is a local path or a plain encoding setting).
- `RenderQueueViewModel`/`RenderQueueView` + `RenderJobItemViewModel`: real export screen reachable from
  the workspace ("Izvezi video" button) - codec/preset/CRF/audio-bitrate pickers, output file picker
  (defaults to `{ProjectName}_captioned.mp4` next to the project file, per spec), and a queue that runs
  any number of jobs concurrently, each with its own live progress bar and cancel button (spec: "multiple
  queued export jobs"). Progress/status is polled from the plain `RenderJob` object on a real
  `DispatcherTimer` tick, the same pattern `PlayerViewModel` already uses, rather than inventing a second
  progress-reporting channel. The logged ffmpeg command is written to the real Serilog log on completion,
  failure, or cancellation (`RenderService`/`Media` itself never takes a logger - consistent with every
  other Media-layer service in this codebase; the ViewModel layer does the logging, as usual).
- Manually verified end-to-end against real ffmpeg + real Tesseract OCR before writing any automated
  test: a 2-clip timeline with a 1s gap and two caption windows rendered to exactly 6.0s at the project's
  1920x1080 format (despite 320x240 source clips), with "ZDRAVO"/"SVET" burned-in text appearing via OCR
  at precisely their specified windows and nowhere else.
- New tests: `FfmpegFilterGraphBuilderTests.cs` (15, pure logic - no gaps, no missing track, gap-filler,
  fade, mute, drawtext timing/escaping, only-first-non-empty-video-track), `RenderServiceTests.cs` (4,
  real ffmpeg+Tesseract - the full 2-clip/gap/caption scenario above, overwrite-without-confirmation
  guard, real hardware-codec-unavailable fallback to libx264 since this sandbox has no GPU, and real
  mid-render cancellation with process-tree kill verified via no leftover temp file). Found and fixed a
  real bug via this last test: `RenderService.RenderAsync` only set `job.Status = Cancelled` in the
  post-await "did cancellation happen" check, but a cancelled `-progress pipe:1` read throws
  `OperationCanceledException` straight out of the awaited call instead of returning normally, skipping
  that check entirely and leaving `job.Status` stuck on `Running` forever - fixed by catching the
  cancellation around the run call and setting `Cancelled` there. `RenderQueueViewModelTests.cs` (10,
  `[AvaloniaFact]` - real `DispatcherTimer` construction - against a fake `IRenderService` covering
  success/failure/cancel/multiple-concurrent-jobs/output-path-validation/overwrite-guard/picker-confirms-
  overwrite/back-navigation). Local (non-integration) test count: 246 → 275, all passing.
- Function matrix: no new `function-contracts.json` rows yet for the render/export screen - same
  deferred-to-next-refresh treatment as Phases 6/7/8.

Deliberately not done this phase (real, explicitly-flagged gaps):
- No file-size estimate before rendering (spec lists this alongside quality choice) - a genuinely accurate
  estimate needs either a real two-pass/probe measurement or a bitrate-based calculation that would be
  misleading for CRF-mode encoding (CRF targets quality, not a fixed bitrate, so output size isn't
  knowable in advance without actually encoding). Left out rather than showing a number that would
  regularly be wrong.
- No rendering of more than the first non-empty Video track (no multi-video-track layering/compositing
  yet), no mixing in of separate standalone Audio-kind tracks (only the rendered video track's own audio
  is kept), no ImageOverlay track compositing, no per-clip caption position/style (burned in at a single
  fixed position/style for every caption, regardless of `CaptionStyleGalleryViewModel` choices) - these
  all need real multi-layer video compositing in the filter graph, which is its own substantial piece of
  work; today's `FfmpegFilterGraphBuilder` handles exactly what it documents and nothing silently wrong.
- `RenderService`'s automatic hardware-encoder fallback re-runs the entire render from scratch with
  libx264 rather than resuming - acceptable for now (matches spec's "automatic fallback" wording, which
  doesn't require resuming), but means a fallback roughly doubles wall-clock time on a failing hardware
  path.
- No UI display of the logged ffmpeg command itself (it's written to the real Serilog log file, per spec,
  but there's no "show command" button/tooltip on a queue row) - deferred purely for time.
- `RenderQueueViewModel`'s `DispatcherTimer` isn't disposed when navigating away from the render screen -
  same known gap as `WorkspaceViewModel`'s `PlayerViewModel` timer since Phase 8 (`MainWindowViewModel`
  doesn't dispose any ViewModel on navigation today); not a new problem introduced by this phase.

## What Phase 10 actually delivered

The spec required each of the 6 disabled planned-feature tiles to be either fully implemented or removed
- never left "disabled forever". Split 3/3 based on what actually had real groundwork elsewhere in the
app versus what would mean inventing a whole new subsystem from a bare tile label with no other spec
support:

Implemented for real:
- **"Kreiraj video iz šablona"** - `ProjectTemplate` (Domain): a fixed, honest-scope list of 4 starter
  templates (Prazan projekat/Govor sa titlovima/Muzički spot/Slike i tekst), each just a named set of
  starter `TimelineTrackKind`s. New `TemplateGalleryViewModel`/`View` picker screen forwards straight into
  the existing, already-tested `NewProjectViewModel` flow (extended with an optional `template` param that
  adds those starter tracks to the new project's timeline before saving) - reuses all of that screen's
  existing format-picker/save/recent-projects logic rather than duplicating it.
- **"Brzi video od slike i pesme"** / **"Automatski video sa utisnutim titlovima (na slici)"** - both now
  open the same real `QuickVideoViewModel`/`View` screen (auto-captions toggled on by default from the
  second tile, off from the first, either way user-toggleable - the two planned tiles were the same
  underlying wizard at two capability levels, not two different features). New `IQuickVideoService`/
  `QuickVideoService` (`NPVideoStudio.Media`): real ffmpeg `-loop 1` still-image-as-video + `-shortest`
  (caps output to the song's length) - verified empirically before writing any code: a 5s synthetic song +
  a still image produced exactly a 5.0s output. Deliberately a separate, simpler pipeline from
  `IRenderService`/`FfmpegFilterGraphBuilder` rather than teaching the general timeline pipeline to treat
  images as looped video clips (a much bigger change for a one-off wizard). Caption burn-in reuses the
  existing `ISubtitleGeneratorService`/Whisper pipeline to generate a real .srt, then burns it in with
  ffmpeg's own `subtitles` filter (not per-word `drawtext` like the timeline renderer) - verified via real
  OCR: burned-in text appeared at exactly its .srt time window and nowhere else. Proactively escapes
  Windows drive-letter colons in the subtitles filter's path argument (documented ffmpeg-wiki behavior,
  not reproducible in this Linux sandbox - same "verify what CI can, document the rest" treatment as
  yt-dlp/Whisper-model paths) - a direct lesson from Phase 9's Windows-only cancellation race, so this
  pass's cancellation handling (`Kill()` + wait for real exit before cleanup, best-effort delete that never
  masks the real exception) was written defensively from the start instead of discovered via a second CI
  failure.
- New tests: `QuickVideoServiceTests.cs` (6, real ffmpeg + Tesseract OCR - duration/resolution match,
  caption burn-in window verified by OCR, overwrite guard, real mid-encode cancellation using a 4K/10s
  fixture for a real cancellation window, escaping unit tests), `QuickVideoViewModelTests.cs` (12, fakes -
  auto-captions gating, SRT-then-render sequencing, failure paths, overwrite guard),
  `TemplateGalleryViewModelTests.cs` (2), `NewProjectViewModelTests.cs` (5, template starter-track
  addition - the plain no-template flow is unchanged/still produces an empty timeline), plus 2 new
  `AppSmokeTests.cs` cases driving the real XAML for both new screens end to end (catches the kind of
  runtime-only binding failure a `dotnet build` can't see - relevant here since `TemplateGalleryView.axaml`
  uses a compiled binding path to reach the parent `ItemsControl`'s DataContext for its per-item command).
  Local (non-integration) test count: 275 → 302, all passing.

Removed (real, explicit reasoning, not silently dropped):
- **"Upravljanje šablonima"** - with templates as a fixed built-in list (no user-authored/CRUD-able
  content), there is nothing to actually "manage" beyond what the template gallery already shows when
  creating a project; a separate "manage" screen would just be an empty-feeling read-only duplicate.
- **"Upravljanje fontovima"** - no font system exists anywhere in this app (caption/timeline burn-in uses
  ffmpeg's default `drawtext` font unconditionally, `CaptionStylePreset` has no font field) and nothing
  else in MASTER_SPEC motivates one. Building real font selection + real `fontfile` wiring + OCR-verified
  proof that a chosen font actually changes the render is a legitimate separate feature, not something to
  bolt on as a rushed one-off.
- **"Upravljanje efektima"** - no effects concept exists beyond the fade in/out that already ships under
  its own real name; there is no spec definition anywhere of which specific effects would need to exist,
  so building "effects management" now would mean inventing scope rather than implementing something
  already designed.

Deliberately not done this phase (real, explicitly-flagged gaps):
- Quick Video's still-image support is intentionally NOT plumbed into the general
  `FfmpegFilterGraphBuilder`/timeline pipeline - a project's Video track still cannot contain a still image
  as a clip. This is a real, separate future enhancement (Phase 9's "not yet mixed in" standalone-Audio-
  track gap is the natural companion piece to that, if ever picked up).
- No custom/user-created templates - `ProjectTemplate.BuiltIn` is a fixed in-code list, not persisted or
  editable from the UI.
- `QuickVideoViewModel` has no queue (unlike `RenderQueueViewModel`) - it runs exactly one job at a time,
  matching its "quick one-off wizard" scope rather than the render pipeline's "multiple queued export
  jobs" spec requirement (which is Phase 9's, not Phase 10's).
- Function matrix: no new `function-contracts.json` rows yet for the template gallery/quick video screens
  - same deferred-to-next-refresh treatment as Phases 6-9.

## What Phase 11 actually delivered

Delivered:
- **Full test suite**: 306/306 passing (302 run locally + 4 real Whisper-model-download integration
  tests verified on Windows CI, per the established sandbox-network-limitation pattern). No regressions
  from any Phase 10 work.
- **All 8 themes**: already covered by `AppSmokeTests.cs: AllEightThemes_LoadAsRealAvaloniaResourceDictionaries`
  (real Avalonia XAML parsing of every theme file) - re-verified passing, not newly built this phase.
- **Installer + portable ZIP (fixed, no double-zip)**: verified by reading `build-release.ps1` and
  `.github/workflows/windows-build.yml` directly rather than assuming - both the version-mismatch fix
  and the ZIP-in-ZIP fix documented as "not yet fixed" in `CLAUDE.md` had actually already been fixed in
  Phase 1, and PDB/non-win-x64-runtime trimming had already been fixed in Phase 1 too. `CLAUDE.md`'s
  "Real, verified constraints" section was stale on all three points - corrected this phase so future
  sessions don't re-flag already-solved problems.
- **`THIRD_PARTY_NOTICES.md` + `Licenses/`**: every actual dependency (all `PackageReference`s across
  every `.csproj`, `Tools/ai-worker/requirements.txt`, every external tool `FfmpegLocator`/
  `DependencyManagerService` invoke) individually researched via real web search, not assumed from
  memory - split into **bundled** (ships in `publish/win-x64/`: Avalonia, CommunityToolkit.Mvvm,
  Microsoft.Data.Sqlite, SQLite itself, Microsoft.Extensions.DependencyInjection, Serilog, Whisper.net +
  whisper.cpp - all MIT/Apache-2.0/Public-Domain, full verified license text in `Licenses/`) and
  **external, not bundled** (FFmpeg GPLv3, yt-dlp Unlicense, Chromaprint/fpcalc LGPL-2.1/GPLv2+,
  Tesseract+tessdata Apache-2.0, faster-whisper/CTranslate2 MIT, WhisperX BSD-2-Clause, Demucs MIT - user-
  installed prerequisites this app only invokes as a subprocess/Python import, never redistributes).
  **Zero AGPL components found anywhere in this stack** - checked explicitly per the spec's requirement,
  not just assumed clean.
- **`RELEASE_NOTES.md`**: new phase-by-phase user-facing changelog for v0.1.0 (Phases 0-10), plus a
  "known limitations" section that doesn't hide anything already documented in `PHASE_STATUS.md`.
- **`README.md` refreshed**: was still describing the app as "Phase 1 of 10" and claiming timeline/
  render/OCR/song-fingerprinting "don't exist yet" - all built since. Feature list, architecture diagram,
  and test count (121 → 306) brought in line with actual current state.
- **Zero `BROKEN`/`PLACEHOLDER` confirmed, not assumed**: real grep audit across `src/` for
  `TODO`/`FIXME`/`HACK`/`NotImplementedException`/"not implemented"/"placeholder"/"coming soon" - every
  hit was either a legitimate Avalonia `PlaceholderText` UI property or an honest doc-comment describing
  an already-tracked, already-disclosed gap (never a live-looking dead button or a silently-wrong
  result). `function-contracts.json`'s existing `BROKEN: 0`/`PLACEHOLDER: 0` counts stand confirmed.

Deliberately not done this phase (real, explicitly-flagged gaps - genuinely require the user, not
something that can be faked or skipped past):
- **No real interactive Windows smoke test** - the automated CI build+test on `windows-latest` is real
  and thorough, but nobody has actually clicked through install → launch → open a project → import media
  → uninstall on a real Windows machine as a human. This sandbox has no way to do that itself.
- **No regression pass against the user's own Shorts clip** (spec: ~9:16, 1080x1920, ~24s, singing,
  possibly pre-existing on-screen text/logo/CTA) - `test-data/local/` + `test-data/README.md` are set up
  and gitignored, ready for it, but no such file exists in this session. Fabricating a substitute clip
  would defeat the entire point of this check (catching real-world edge cases synthetic lavfi test clips
  can't).

## Post-Phase-11 follow-up: real player preview frame

Not part of `docs/MASTER_SPEC.md`'s 11 phases - added after the user actually ran a local build (per the
Phase 11 "next action" below) and found the in-workspace player showed no picture at all. That gap had
been disclosed in the UI's own text since Phase 8, but real usage showed it as a blocking problem for
actually editing (no way to see where a text overlay lands, whether a clip's trim point is right, etc.),
so it was fixed directly rather than just re-explained.

Delivered:
- **`IFramePreviewService` / `FramePreviewService`** (`NPVideoStudio.Media`): real ffmpeg single-frame
  extraction (`-ss <t> -i <file> -frames:v 1 -f image2pipe -vcodec png -`, read straight off stdout, no
  temp file) - verified empirically before writing any code that piping a PNG through ffmpeg's stdout
  this way produces valid, correctly-decodable bytes every time.
- **`TimelinePreviewResolver`** (`NPVideoStudio.AI`, pure logic, fully unit tested): maps the current
  timeline tracks + media library + playhead position to a real source file path and in-source timestamp
  (first non-hidden Video track, clip under the playhead, `SourceTrimIn + offsetIntoClip`).
- **Wired into `WorkspaceViewModel`**: refreshes `PlayerViewModel.CurrentFrameBitmap` on every seek, frame
  step, play tick, and timeline edit, with cancellation-token-swap handling so fast scrubbing doesn't pile
  up overlapping ffmpeg calls. `WorkspaceView.axaml`'s player panel now has a real `<Image>` bound to it.
- **A real, previously-latent bug found and fixed along the way**: the seek `Slider` was two-way-bound
  directly to `PlayerViewModel.CurrentTimeSeconds`, completely bypassing `PlayerStateMachine.Seek()` -
  dragging it never actually updated the player's internal position. No test exercised a real slider drag
  before now, so this had been silently broken since Phase 8.
- **Tests**: `TimelinePreviewResolverTests.cs` (9 pure-logic cases), `FramePreviewServiceTests.cs` (4 real
  ffmpeg-integration cases - valid PNG at the right size via a manual IHDR-chunk read, since Avalonia's
  headless test platform stubs out real bitmap decoding and reports a fake 1x1 size for any image, so
  `Bitmap` can't be used to verify decodability in this test environment). Full non-integration suite
  (315 tests) passing with no regressions.

Deliberately not done (real, explicit scope limit, not an oversight): this is **not** continuous video
playback. Each position change fetches one still frame: correct for scrubbing/stepping/checking a
position, but pressing play does not show a smoothly moving picture at 30fps, only the player's transport
state (time/progress) advancing while frames refresh a few times a second at most. A true streaming
decoder (e.g. LibVLCSharp) is a much larger scope with its own bundling/licensing questions and no way to
verify smooth playback in this Linux sandbox (no real display) - left as a possible future improvement,
not started here.

## Second post-Phase-11 follow-up: export/timeline sync bug + navigation resource leaks

Found by directly re-inspecting the workspace/export/navigation code end to end (not from a specific new
user report) while looking into whether "Izvezi video" (part of the still-open item below) is actually
reliable. Three real, confirmed (not hypothetical) bugs, all fixed:

- **Export could silently render a stale timeline.** `WorkspaceViewModel.ExportVideo()` fired
  `ExportRequested` (which opens the render queue against the live `Project` object) without ever calling
  `Timeline.SaveToProject()` first. `TimelineEditSession` (the thing the UI actually edits) only writes
  its tracks back onto `Project.Timeline.Tracks` when that's explicitly called - it wasn't guaranteed to
  have happened recently before pressing Export (only on manual "Sačuvaj projekat" or the next media
  import). Confirmed via a new regression test
  (`WorkspaceViewModelTests.ExportVideo_UnsavedTimelineEdit_SyncsToProjectBeforeRaisingExportRequested`)
  that a clip added to the timeline was invisible to the render queue until this fix. Fixed by having
  `ExportVideo()` call `Timeline.SaveToProject()` before raising the event.
- **`TotalTimeLabel`/`CurrentTimeLabel` didn't reliably update.** Both are plain computed properties, not
  `[ObservableProperty]` fields themselves - `PlayerViewModel.Retarget()` (called whenever the timeline's
  total duration changes, e.g. after adding a clip) updated `TotalDurationSeconds` but nothing told the UI
  the *label* text depending on it had also changed, so the displayed total time could visibly lag behind
  the real duration. Fixed with `[NotifyPropertyChangedFor]` on both underlying seconds properties, with
  two new regression tests in `PlayerViewModelTests.cs` that assert on the actual label text (not just the
  raw numeric property, which is why this had gone uncaught).
- **Navigating away from the workspace or render queue never called `Dispose()`.** Both are `IDisposable`
  (a `DispatcherTimer` and, for the workspace, also a frame-preview `CancellationTokenSource`), but
  `MainWindowViewModel` never disposed the outgoing page on any navigation - reachable via the always-
  visible top navbar ("Početni ekran"/"Podešavanja"/"Dijagnostika"), not just each page's own in-context
  back button. Fixed with a single `OnCurrentPageChanging` hook that disposes the outgoing page, with one
  deliberate exception: Workspace -> RenderQueue is not an abandonment (RenderQueue's own "Nazad" hands
  the exact same live workspace instance back), so that specific transition is excluded.

Also fixed in the same pass: the portable ZIP's `README-FIRST.txt` told the user to run
`scripts\check-dependencies.ps1`, but that script was never actually copied into the portable folder
(only ever existed in a full source checkout) - `build-release.ps1` now bundles it into
`dist\NPVideoStudio-Portable-x64\scripts\`.

Full non-integration suite: 318 tests passing (315 + 3 new regression tests), no regressions.

## Third post-Phase-11 follow-up: EasyOCR fallback for stylized on-screen text

The user reported "prepoznavanje teksta iz videa" not working and supplied a real video to prove it -
`TesseractOcrService` genuinely returned garbage on it. Root-caused before writing any code: the video's
on-screen caption ("NEDOSTAJEŠ PUNOO") uses a colored, outlined "bubble letter" font typical of short-form
video templates - Tesseract is built for plain printed/document text and is known to struggle badly with
this style. Confirmed directly (not assumed): ran real `tesseract` against the actual extracted frame
(including a cropped, 3x-upscaled close-up of just the text) and got nonsense output every time.

Before proposing a fix, verified a real alternative actually solves it: installed EasyOCR (a deep-learning
OCR library, Apache 2.0) and ran it against the exact same frame. With its dedicated `rs_latin` language
model: `'NEDOSTAJEŠ' 0.81`, `'PUNOO' 0.95` - both words read correctly, diacritics included, vs.
Tesseract's complete failure on the same input.

Delivered:
- **`easyocr-helper/ocr_frame.py`**: small bundled Python script, one image path in, one JSON array of
  `{text, confidence, x, y, width, height}` out (coordinates already normalized 0..1, same shape
  `TesseractOcrService.ParseTsv` produces) - the plain request/response subprocess pattern already used
  for ffmpeg/yt-dlp/Tesseract/fpcalc, not the JSONL-event `ai_worker.py` protocol (that one's shaped for
  long-running audio jobs with progress events; a single-frame OCR call doesn't need it).
- **`EasyOcrVideoLayoutAnalysisService`** (`NPVideoStudio.Media`): real `IVideoLayoutAnalysisService`
  implementation shelling out to that script, same frame-sampling logic as `TesseractOcrService`.
- **`CompositeVideoLayoutAnalysisService`**: prefers EasyOCR when actually installed (checked via a cheap
  `python -c "import easyocr, PIL"` probe, cached), falls back to Tesseract otherwise or if a specific run
  throws - so a machine without the optional Python/EasyOCR setup keeps working exactly as before, with
  no UI or ViewModel changes needed. Wired in as the sole `IVideoLayoutAnalysisService` registration in
  `App.axaml.cs`.
- **Tests**: pure `ParseRegions` tests against the real captured JSON shape from the run above, plus a
  real end-to-end integration test (`EasyOcrVideoLayoutAnalysisServiceTests.AnalyzeAsync_RealFrameWithPlainText_...`)
  that generates a real ffmpeg clip with drawtext, runs the actual bundled script against it, and asserts
  real detected text - confirmed passing here (14s real run, not skipped). Self-skips (returns without
  asserting) when Python/EasyOCR isn't installed, since this is an optional dependency **not** installed
  on this project's Windows CI (PyTorch is a heavy, slow addition for what's a fallback-only feature) -
  deliberately not wired into CI, the inverse of the Whisper-model tests (those need CI's real internet
  and fail here; this one needs a local install this sandbox happens to have and CI doesn't).
- **`THIRD_PARTY_NOTICES.md`**: EasyOCR (Apache 2.0) + PyTorch (BSD-3-Clause) added as an external,
  optional prerequisite.

Deliberately not done: no Settings/UI toggle to force one engine or the other, no UI indicator of which
engine actually ran for a given analysis - the composite fallback is intentionally invisible/automatic to
keep this change's blast radius small (zero touched ViewModel/View code). A future pass could surface
which engine produced a given result if that turns out to matter in practice.

Full non-integration suite: 326 tests passing (319 + 7 new), no regressions.

## Fourth post-Phase-11 follow-up: custom self-contained installer (Inno Setup download is blocked here)

The user demanded a real "double-click and install" experience, not just the portable ZIP - a genuine
installer that adds a Start Menu shortcut and shows up in Windows' Add/Remove Programs.

`installer/NPVideoStudio.iss` (Inno Setup) already existed and is still the primary path, but building it
requires `ISCC.exe`, which requires downloading and installing Inno Setup from `jrsoftware.org` first -
confirmed via `curl -sI` that this sandbox's network policy blocks `jrsoftware.org` with the same generic
403 "policy denial" already seen for `ffmpeg.org`/`gyan.dev`. `build-release.ps1`'s step 7/7 already
handles this gracefully (skips with a warning if `ISCC.exe` isn't on PATH), so a machine that *can* reach
jrsoftware.org and has Inno Setup installed still gets the real Inno Setup installer - nothing about that
path changed. But it means this sandbox can never verify or produce that installer itself, and there's no
guarantee the user's own machine has Inno Setup installed either.

Rather than keep asking the user to go install a third-party tool just to get an installer, built a second,
independent installer with zero external dependencies: **`src/NPVideoStudio.Installer`**
(`NPVideoStudioSetup.exe`), a small self-contained .NET console/WinExe app that:
- Copies everything next to it (the whole portable payload) into `%LocalAppData%\Programs\NP Video Studio`
  (no admin rights needed - same per-user convention VS Code uses).
- Creates a real Start Menu `.lnk` shortcut by shelling out to `powershell.exe`'s `WScript.Shell` COM
  object (the standard way to write a `.lnk` file; hand-writing the binary MS-SHLLINK format or using COM
  interop directly isn't reliable when cross-compiling from this Linux sandbox).
- Registers a proper `HKEY_CURRENT_USER\...\Uninstall\NPVideoStudio` entry so the app shows up in Windows
  Settings > Apps, with an `UninstallString` pointing back at itself with `--uninstall`.
- `--uninstall` mode removes the shortcut and registry entry, then hands off to a short-lived `cmd.exe`
  (`timeout /t 2 & rmdir /s /q ...`) to delete its own install folder after the process exits, since a
  running exe can't delete its own containing directory synchronously.

Published as a self-contained, single-file, fully trimmed (`PublishTrimmed=true -p:TrimMode=full`) win-x64
binary - trims cleanly to ~12MB with **zero trim-analyzer warnings** (it only touches trim-safe BCL APIs:
Registry, Process, File I/O, one P/Invoke for a message box), confirmed via a real cross-compiled publish
in this sandbox (`file` confirms a genuine `PE32+ executable (GUI) x86-64, for MS Windows`). `dotnet build`
of the full solution (all 8 projects including this new one) succeeds with 0 warnings, 0 errors.

Wired into `build-release.ps1` as a new step 5/7 (steps renumbered `/6` to `/7`): publishes
`NPVideoStudioSetup.exe` straight into the same folder as the main app, so it's included both in the
portable ZIP and inside the Inno Setup payload if that step also runs. `README-FIRST.txt`'s generated text
now leads with "run NPVideoStudioSetup.exe to install" instead of only describing the portable/extract
option.

Deliberately not a general-purpose installer framework: one product, one fixed install location, no
install-path picker, no component selection, no upgrade/repair handling. Inno Setup remains the "real"
installer for anyone who has or can get it; this is the honest, dependency-free fallback for anyone who
can't - explicitly documented as such in the class-level doc comment.

**Not verified end-to-end**: this sandbox has no Windows environment, so only compilation/publishing was
confirmed here. The actual install/shortcut/registry/uninstall behavior needs to be confirmed on a real
Windows machine.

Full non-integration suite: still 326 tests passing, no regressions (the installer project has no unit
tests of its own - it's a thin, mostly I/O/OS-API shell with no pure logic to extract, unlike the
ffmpeg/yt-dlp helpers).

## Fifth post-Phase-11 follow-up: importing a video didn't show it in the player

Real user report, with screenshots: after using "Dodaj medije" to import a video, the player still said
"Nema kadra za prikaz". Not a bug in frame extraction (that was already fixed in the first follow-up above)
- the actual cause was a workflow gap: importing only ever added the file to the media library. Showing
anything in the player additionally required the user to know to click "+ Video traka", then select the
asset in a dropdown and click "+ Klip", *then* have the playhead land on that clip - four extra, undiscoverable
steps with no prompt telling them to do so. A first-time, non-technical user has no reason to expect
"import" and "place on a video track so it previews" are two separate actions.

Fix: `TimelineViewModel.AutoPlaceFirstImportOnEmptyTimeline(MediaAsset)` - when a freshly imported asset
has a video stream and the timeline doesn't have a single clip on it yet (a brand new project), it
auto-creates a video track and places the clip at time 0, exactly as if the user had clicked "+ Video
traka" → selected it → "+ Klip" themselves. `WorkspaceViewModel.ImportFilesAsync` calls it right after each
import. Deliberately scoped to *only* the very first import on a genuinely empty timeline - once any clip
already exists, later imports go back to landing in the library only, so an import mid-edit never
rearranges work already in progress. Audio-only imports are left alone (nothing to preview, and the render
pipeline only reads a project's Video-track clips - confirmed by reading `FfmpegFilterGraphBuilder.Build`,
which pulls both `:v` and `:a` streams for export from the Video track's clips only, so placing a
video+audio asset there is also correct for a later Export, not just for the player).

Verified two ways: two new `WorkspaceViewModelTests` (first video import on an empty timeline lands on a
new video track at 0s and the player has a frame; a second import after a clip already exists does not
re-arrange the timeline), and a real, live run - built the app for Linux, launched it under Xvfb, and
drove it with `xdotool` through the actual reported flow (new YouTube-format project → Dodaj medije →
picked the user's own previously-uploaded `user_video.mp4` from the file picker) and captured a real
screenshot: the player shows the actual decoded frame from that file immediately after import, with the
video track and its clip already present on the timeline, no further clicks needed.

Full non-integration suite: 328 tests passing (326 + 2 new), no regressions.

## Sixth post-Phase-11 follow-up: real auto-captions on the timeline + working per-clip text styling

User asked for a real feature set, not a bug fix: automatically put speech-to-text captions onto the
video, and be able to actually change the caption's font/size/color/position/placement. Investigated
before writing code and found the honest gap: "Generiši titlove (SRT)" already really transcribes speech
locally (Whisper.net), but only ever wrote a standalone `.srt` file - nothing connected that output to the
timeline. Separately, "Stilovi titlova" (24 presets) turned out to be a **preview-only color swatch**:
`FfmpegFilterGraphBuilder`'s caption/text burn-in used one hardcoded `drawtext` (fontsize=36, white,
fixed position) for every clip regardless of which "style" was picked - changing a style in that gallery
never touched the exported video. Karaoke word-by-word highlighting was confirmed to not exist at all (the
Python word-timestamp path is an explicit `"...još nije implementirana"` stub) - out of scope for this
pass, called out honestly to the user as separate, larger follow-up work.

Delivered, real and working:
- **`TimelineClip` gained real per-clip style fields**: `FontChoice` (`CaptionFontChoice`: Default/Arial/
  ArialBold/Impact/ComicSansBold/Georgia), `FontSizePx`, `TextColor` (hex), `TextPosition`
  (Top/Middle/Bottom) - `FfmpegFilterGraphBuilder` now builds each caption/text clip's `drawtext` from
  these instead of one hardcoded style, confirmed with a real `ffmpeg` render + extracted frame (custom
  70px green top-positioned text actually appeared in the output, not just the filter string).
  `CaptionFontResolver` maps a font choice to a real Windows system font file path (`C:\Windows\Fonts\
  *.ttf` - this app is Windows-only) for `drawtext`'s `fontfile=`, returning null (no fontfile arg, same
  as before) for `Default` or on any machine where that file doesn't exist, so a missing font degrades
  gracefully instead of breaking export.
- **`TimelineEditSession.SetTextStyle`**: the fifth mutator alongside SetFade/SetClipMute/etc, with a real
  undo/redo test (style changes correctly revert on Undo).
- **Real, per-clip UI controls** on every Caption/Text timeline clip (font/size/color/position), wired via
  `TimelineClipItemViewModel` → `TimelineViewModel.CreateClipItem`'s new `onTextStyleChanged` callback -
  deliberately does NOT mutate the live session clip directly before calling into the session (would have
  made the session's own undo snapshot capture the *new* value as if it were "before", silently breaking
  undo - caught and fixed before shipping, covered by the SetTextStyle undo test above).
- **"Automatski dodaj titlove iz videa"** button in the workspace: runs the same local Whisper
  transcription as the SRT tool but calls the new `ISubtitleGeneratorService.TranscribeAsync` (returns
  timed segments instead of writing a file) and `TimelineViewModel.AddGeneratedCaptions` places each
  segment as a real clip on a new Caption track - a genuine transcribe → clips-on-timeline → burned-into-
  export pipeline where none existed before. Deliberately does not auto-download the Whisper model
  (consent-gated download stays a one-time explicit click in the SRT tool); tells the user to do that
  first if it's not ready yet.
- **`CaptionStyleGalleryView`'s disclosure text corrected** - it previously implied styles "apply... on the
  exported video (Faza 8/9)", which was never true; now honestly says the gallery is inspiration only and
  points at the real, working per-clip controls in the timeline.

Verified three ways: 16 new unit tests (`SetTextStyle` incl. undo/clamping, `FfmpegFilterGraphBuilder`
position-enum mapping + default-matches-old-look, `CaptionFontResolver` on/off Windows, three
`GenerateCaptionsForVideoAsync` scenarios - video present, no video yet, model not downloaded); a live run
of the actual compiled app under Xvfb confirming the new button/message render and read correctly; and,
since repeated synthetic xdotool clicks inside the Timeline's `ItemsControl` proved flaky in this specific
headless sandbox (first click in that region reliably worked, later ones intermittently didn't - isolated
to be an Xvfb/xdotool synthetic-input quirk, not a code regression, since automated tests exercise the
exact same commands directly and always pass), the export path itself was verified by driving the real
`FfmpegFilterGraphBuilder` + a real `ffmpeg` process end-to-end and inspecting the actual rendered frame -
which showed the exact custom font size/color/position requested, burned into a real playable MP4.

Full non-integration suite: 344 tests passing (328 + 16 new), no regressions.

Deliberately not done in this pass: karaoke word-by-word highlighting (needs real word-level ASR
timestamps, which don't exist anywhere working in this codebase yet - a genuinely separate, larger piece
of work), and a font *name* picker beyond the fixed six safe system fonts (arbitrary font names need
fontconfig, not guaranteed present in the bundled ffmpeg build).

## Seventh post-Phase-11 follow-up: real karaoke word-by-word captions

The previous follow-up above said karaoke needed "real word-level ASR timestamps, which don't exist
anywhere working in this codebase yet." Re-investigated on a direct user demand to build it anyway, and
found that claim was too pessimistic: it's true for the Python `ai-worker.py` path (still an unimplemented
stub), but Whisper.net - the C# library already used for every other transcription in this app - has its
own real word-timing support that was never being used: `WithTokenTimestamps()` + `SplitOnWord()` +
`WithMaxSegmentLength(1)` on its processor builder, confirmed by pulling Whisper.net's own source
(`WhisperProcessor.cs`/`WhisperProcessorBuilder.cs` from its public repo) - this is the exact technique
whisper.cpp's own CLI uses for its `--max-len 1 --split-on-word` word-level SRT export, so it's a real,
documented, native capability, not a workaround.

Delivered:
- **`WhisperTranscriber.TranscribeWordsAsync`**: same transcription pipeline as the existing line-level
  `TranscribeAsync`, but built with those three options so whisper.cpp itself emits one *segment* per
  word (with real per-word timing) instead of hand-parsing the noisier raw per-token array.
  `ISubtitleGeneratorService`/`SubtitleGeneratorService` got a matching `TranscribeWordsAsync`.
- **"Karaoke titlovi (reč po reč)"** button next to "Automatski dodaj titlove iz videa" in the workspace -
  reuses the exact same `TimelineViewModel.AddGeneratedCaptions` placement path (already generic over any
  list of timed segments), so each transcribed word becomes its own short-lived clip on a new caption
  track. On playback/export this makes each word appear on screen individually, exactly when it's spoken -
  the word-by-word "karaoke" style short-form editors (CapCut etc.) use. Deliberately not attempted:
  highlighting one word *inside* an otherwise-static full sentence, which would need per-character glyph-
  width measurement that ffmpeg's `drawtext` doesn't expose - word-by-word popup is the real, robust
  interpretation actually built here, not a stand-in oversold as the other one.

Verified: a new `GenerateKaraokeCaptionsForVideoAsync` unit test (three fake word segments → three correctly
timed/ordered clips on a caption track); the new button renders correctly in a real run of the compiled app
under Xvfb; and, since this sandbox's network policy blocks `huggingface.co` (so the real Whisper model
genuinely cannot be downloaded here - confirmed again via `curl`, matching the standing, documented
constraint), the actual per-word ASR quality can only be verified on CI or the user's own machine, same as
this project's other Whisper-integration tests. What *was* verified directly here is the render path that
doesn't depend on real speech: drove `FfmpegFilterGraphBuilder` + a real `ffmpeg` process with three
synthetic word-clips at 0-1s/1-2s/2-3s and extracted real frames at t=0.5s and t=1.5s - each frame shows
exactly one word ("ZDRAVO" then "SVIMA", the other absent), confirming the chained per-clip `drawtext`
filters correctly show-and-hide each word in its own time window.

Full non-integration suite: 345 tests passing (344 + 1 new), no regressions.

## Eighth post-Phase-11 follow-up: real clip-to-clip transitions (xfade/acrossfade)

User asked for feature parity with CapCut/VN/DaVinci-style editors, specifically "efekti i tranzicije
između klipova" - transitions between clips did not exist at all: adjacent Video-track clips only ever
hard-cut, and the existing `FadeInSeconds`/`FadeOutSeconds` fields fade a single clip to/from black, not
into the *next* clip.

Delivered:
- **`ClipTransitionType`** (Domain): None/Fade/WipeLeft/WipeRight/SlideLeft/SlideRight/Dissolve/ZoomIn -
  each name matches an ffmpeg `xfade` transition name exactly (lowercased), plus `TransitionInSeconds`/
  `TransitionInDurationSeconds` on `TimelineClip` (the transition FROM the previous Video-track clip INTO
  this one).
- **`FfmpegFilterGraphBuilder` rewritten** from one flat `concat` over every segment to a left-to-right
  join chain, so each adjacent pair can independently be either a plain hard-cut `concat` (unchanged
  default behavior, verified byte-identical via existing regression tests) or a real `xfade`/`acrossfade`
  pair when a transition is set - with the correct `offset` (running duration so far minus the transition
  length) and clamped duration (never longer than either neighboring clip, so a 3s transition requested on
  two 1s clips doesn't crash ffmpeg). A transition is skipped (falls back to hard cut) when there's a real
  gap before the clip - nothing to blend into a black filler.
  - **Real correctness catch, fixed before shipping**: a transition overlap shortens the rendered video
    relative to the authored timeline, so any caption/text clip timed after a transition would show up
    late (or past the end) unless shifted. Added a `MapToRenderedTime` pass that shifts every caption's
    burned-in timestamp earlier by the cumulative transition overlap before it - covered by a dedicated
    regression test (`Build_CaptionAfterATransition_TimestampIsShiftedEarlierByTheOverlapAmount`).
- **`TimelineEditSession.SetTransition`** (undo-safe, same pattern as `SetTextStyle`) + real, working
  ComboBox (transition type) + NumericUpDown (duration) on every Video-track clip in the workspace UI.

Verified three ways: 5 new `FfmpegFilterGraphBuilderTests` (offset/duration math, `None` still hard-cuts,
a real gap disables the transition instead of crashing, the caption time-shift) + 1 new
`TimelineEditSessionTests` (undo), all 350 tests passing, no regressions (the no-transition path was
re-verified identical, including against `RenderServiceTests.cs`'s real-ffmpeg-executing tests); the new
UI controls confirmed rendering live in the compiled app under Xvfb, including opening the transition
ComboBox and seeing all 8 real options; and - the one that actually proves the pixels are right - a real
`ffmpeg` render of two solid-color clips (red, blue) with a 1s `fade` transition between them, with frames
extracted before/during/after: red at t=1.5s, a genuine **red-blue blend (purple)** at t=2.5s (mid-
transition), blue at t=4.5s - the crossfade is a real per-pixel blend, not a label that does nothing.

Full non-integration suite: 350 tests passing (345 + 5 new), no regressions.

Deliberately not done in this pass (explicitly told to the user, not silently dropped): audio-specific
tooling (music library, auto-ducking, noise removal) and further caption animation styles beyond what
already shipped (per-clip font/size/color/position, karaoke word-by-word) - both real, separately-scoped
follow-ups for a future pass, not abandoned.

## Ninth post-Phase-11 follow-up: mismatched project/video orientation auto-fixed on first import

Real user report with screenshots: a portrait (1080x1920) video imported into a project created with the
default horizontal (1920x1080) canvas showed up tiny, pillarboxed with black bars either side. Correct
given the mismatch, but a non-technical user has no reason to expect "create a new project" and "the
orientation of the video I'm about to import" are two choices that both have to agree - especially since
the start screen's platform tiles (YouTube Shorts, TikTok, etc.) already default to the right orientation,
so this only bites people who click the generic "Novi projekat" tile first.

Fix: `WorkspaceViewModel.TryAdjustProjectFormatToMatch`, called right after
`TimelineViewModel.AutoPlaceFirstImportOnEmptyTimeline` succeeds (i.e. only on a genuinely fresh project
with nothing on the timeline yet - never reflows an edit in progress). Detects a real orientation mismatch
(portrait vs. landscape; either side being exactly square is left alone rather than guessed at) and resizes
`Project.Format` to the video's own real resolution, marking `AspectRatio = Custom`. `StatusMessage`
reports the change plainly instead of silently resizing the canvas underneath the user.

**Real bug caught and fixed before shipping**: the header's format summary (`"1920×1080 · 30 fps ·
Horizontalni"`) is bound through `Project.Format.Width` etc., but `Project`/`ProjectFormat` are plain,
non-observable domain classes - mutating them in place doesn't notify the UI, so the header kept showing
the stale format after the fix ran (confirmed with a live Xvfb run, not assumed). Fixed by adding a real
`WorkspaceViewModel.FormatSummaryLabel` bindable property, explicitly refreshed wherever `Format` changes,
and pointing the header's XAML at it instead of the dead nested-path binding.

Verified: 2 new `WorkspaceViewModelTests` (orientation mismatch adjusts Width/Height and updates the label;
orientation-already-matching import leaves the format untouched) plus a real live run under Xvfb importing
the user's own real portrait video into a horizontal project - header correctly updated to
`"1080×1920 · 30 fps · Vertikalni"` after import, confirmed only after a clean rebuild (the first live
check used a stale incremental Avalonia XAML build and still showed the old header - caught, diagnosed as
a build-cache issue rather than a real code bug, and re-verified clean before considering this done).

Full non-integration suite: 352 tests passing (350 + 2 new), no regressions.

## Tenth post-Phase-11 follow-up: bundled Whisper model + editable caption text

Real user report: "why does Whisper Tiny still have to be downloaded separately - put it in the program
itself" and "why can't I check/correct what was recognized as text." Two separate, concrete fixes:

**Bundled Whisper model.** `WhisperModelLocator.ResolveModelPath(overridePath, defaultAppDataPath)`
(new, in `NPVideoStudio.Media`) mirrors the existing `FfmpegLocator` resolution order: an explicit
override path if it exists, then `Tools/whisper-models/ggml-tiny.bin` next to the exe, then the old
`%LocalAppData%` download-on-demand default. `WhisperTranscriber`'s constructor now routes through it
instead of going straight to the AppData path. `scripts/build-release.ps1` downloads the real model
(`https://huggingface.co/sandrohanea/whisper.net/resolve/v4/classic/ggml-tiny.bin`, same URL
`WhisperGgmlDownloader` itself uses - confirmed by reading Whisper.net's own source, not guessed) into
`Tools/whisper-models/ggml-tiny.bin` at build time, same best-effort try/catch pattern as the yt-dlp/ffmpeg
downloads (a failed download during build is a warning, not a fatal error - falls back to the in-app
download button). `scripts/check-dependencies.ps1` gained a matching check-and-offer-to-download step.
**Real, disclosed constraint**: `huggingface.co` is blocked in this sandbox (confirmed again this session),
so the model is bundled only when `build-release.ps1`/`check-dependencies.ps1` run on the user's own
Windows machine with real internet - never in a chat-delivered sandbox package.

**Editable caption text.** Before this, an auto-generated (or karaoke) caption clip's text could only be
deleted and retyped from scratch as a brand-new Text-track clip - there was no way to fix a single
misheard word in place. Added `TimelineEditSession.SetTextContent(clipId, newText)` (no-ops on a clip
whose `TextContent` is null, i.e. a real media clip - can't accidentally turn a video clip into a text
clip), wired through `TimelineClipItemViewModel.TextContent` (get/set, same undo-safe callback pattern as
the existing font/size/color/position controls) and a new "Tekst:" `TextBox` in `WorkspaceView.axaml`,
visible whenever `IsTextClip` is true (i.e. for every Caption/Text-track clip, whether auto-generated,
karaoke, or hand-typed).

Verified: `TimelineEditSessionTests` (`SetTextContent_UpdatesTextAndSupportsUndo`,
`SetTextContent_OnNonTextClip_DoesNothing`), `WhisperModelLocatorTests` (override/bundled/default
fallback), and a new `TimelineViewModelTests` exercising the real UI-facing chain end to end - adding a
Text track via `AddTextTrackCommand`, adding a clip via the track's `AddClipAtPlayheadCommand`, editing
`TextContent` through the exact property `WorkspaceView.axaml`'s `TextBox` binds to, and confirming the
edit survives `SaveToProject()`. That last test caught a real gap in the *test*, not the product: because
`AddClipAtPlayheadCommand` triggers `RefreshFromSession()` (which rebuilds every track/clip VM instance,
not just the one edited), the pre-call `TimelineTrackItemViewModel` reference goes stale the instant its
own command fires - confirmed harmless (a standalone console harness instantiating the same
`TimelineViewModel` directly showed the clip really was added, just not visible on the stale VM
instance) and not something a real bound `ObservableCollection` in the UI would ever observe, since
XAML always re-reads the live collection. Live Xvfb click-through of the "+ Tekst traka" button itself
hit the same synthetic-input flakiness already diagnosed earlier in this document (first click in a
region not reliably registering under this specific Xvfb/xdotool combination) - worked around the same
way as before, by proving correctness directly against the real ViewModel objects instead of fighting
unreliable synthetic clicks.

Full non-integration suite: 359 tests passing (352 + 7 new: 2 SetTextContent, 3 WhisperModelLocator, 2
TimelineViewModel), no regressions.

**Not yet addressed** (raised by the user in the same message, no code changes yet): a more discoverable
home-screen-level entry point for "add text to video" (the functionality exists once inside a project -
"Automatski dodaj titlove iz videa" / "Karaoke titlovi" buttons plus the per-clip style/text controls -
but nothing on the start screen points there); confirming in writing that there is no video-length cap
anywhere in the transcription/caption pipeline (grepped - there isn't one; only "Isečci iz pesme" is
Shorts-specific by design); and honestly scoping the "more functional player" request (real continuous
audio+video playback vs. the current real-but-frame-snapshot-only preview) as a distinct, materially
larger piece of future work rather than a small follow-up.

## Eleventh post-Phase-11 follow-up: home-screen "add text" shortcut + real audio/video player

Real, repeated user demand after the tenth follow-up: "why isn't there a home-screen button for adding
text" and "why is the player still just silent snapshots - build a real one, don't just explain why it's
hard." Two concrete, real (not partial) fixes:

**Home-screen "Dodaj tekst u video" shortcut.** New tile, first in the "Alati" section (most direct
answer to "why isn't this on the home screen"): `StartScreenViewModel.AddTextToVideoCommand` ->
`MainWindowViewModel.OpenWorkspaceForAddingText()` opens a fresh project's workspace and immediately
calls new `WorkspaceViewModel.StartAddTextToVideoFlowAsync()`, which prompts for a video file, imports
and auto-places it (reusing the exact same tested `ImportFilesAsync` path - including the orientation
auto-fix from the ninth follow-up), then adds a Text track with one starter clip so the "Tekst:"/font/
size/color/position controls are visible and ready with zero further navigation. A no-op if the file
picker is cancelled. The underlying text-adding functionality already existed (sixth follow-up) - this
follow-up is purely about discoverability, which was the actual, real gap reported.

**Real, continuous audio+video player ("Pravi plejer sa zvukom").** The existing `Player` (frame-by-frame
ffmpeg snapshot preview, Phase 11) stays as-is - cheap, always available, no native dependency - but a
second, real player was added alongside it via LibVLC (`LibVLCSharp`, `LibVLCSharp.Avalonia`,
`VideoLAN.LibVLC.Windows` - all LGPL-2.1-or-later, added to `THIRD_PARTY_NOTICES.md`/`Licenses/`).
New `RealPreviewViewModel` wraps a real `LibVLC`/`MediaPlayer`, constructed defensively: `IsAvailable`
is false (with a real message in `UnavailableReason`, no crash) whenever libvlc's native library can't be
loaded - the honest, expected outcome on this project's own Linux dev sandbox (no `libvlc.so` here),
verified true on every test run in this repo, not assumed. `WorkspaceViewModel.RenderRealPreviewCommand`
("Renderuj i pusti sa zvukom") deliberately reuses the *exact same* `IRenderService`/
`FfmpegFilterGraphBuilder` pipeline "Izvezi video" already uses - not a separate, simplified preview path
that could drift from what actually exports - just with a fast/low-quality preset (`ultrafast`, CRF 28)
so the wait before playback starts is reasonable instead of a full-quality export's minutes. Real,
disclosed trade-offs of this approach, stated plainly rather than left implicit:
- A render has to finish before anything plays - unlike scrubbing the snapshot preview, which is instant.
  This is the actual cost of "real audio+video" versus "an accurate single frame."
- `VideoLAN.LibVLC.Windows` bundles ~100MB of native `libvlc`/`libvlccore` + the full codec/demux/mux
  plugin set for win-x64 only (`VlcWindowsX86Enabled=false` set in `NPVideoStudio.App.csproj` to avoid
  bundling an unused win-x86 copy too, which would have doubled it again) - confirmed via a real win-x64
  publish in this sandbox (`libvlc/win-x64/libvlc.dll` etc. present, ~102MB). This roughly doubles
  installed/portable-ZIP size versus the ninth follow-up's package. A real, disclosed cost of building
  this feature for real instead of faking it.

Verified: dedicated `RealPreviewViewModelTests` confirming `RealPreviewViewModel.IsAvailable` is false
with a real `UnavailableReason` on this sandbox (not throwing) and that `LoadAndPlay` no-ops rather than
crashing when unavailable; `WorkspaceViewModelTests`
(`RenderRealPreviewAsync_NoClipsOnTimeline_DoesNotCallRenderService`,
`RenderRealPreviewAsync_ClipExistsButRealPlayerUnavailableOnThisMachine_DoesNotCallRenderService`,
`StartAddTextToVideoFlowAsync_VideoPicked_ImportsItAndAddsTextTrackWithStarterClip`,
`StartAddTextToVideoFlowAsync_PickerCancelled_AddsNoTracks`); a real win-x64 cross-compiled publish
confirming `libvlc.dll`/`libvlccore.dll`/the plugins tree are genuinely bundled; and Avalonia's compiled
bindings (`AvaloniaUseCompiledBindingsByDefault=true`) validating every new `RealPreview.*`/
`RenderRealPreviewCommand` binding in `WorkspaceView.axaml` against real property/command types at
build time - a build-time guarantee stronger than a screenshot for binding correctness, though a live
Xvfb click-through of the new tile/buttons themselves hit the same synthetic-input flakiness already
documented earlier in this file (first click in a region not reliably registering under this specific
Xvfb/xdotool combination across multiple app restarts) and could not be independently confirmed visually
this session - the app launched clean with no exceptions logged (including LibVLC's own native-library
lookup), which is the strongest evidence available in this environment that construction doesn't crash.

**Not yet independently confirmed real playback quality/latency on a real Windows machine** - this
sandbox cannot produce audio or a native libvlc.so to test against, so "does it actually play smoothly
with correct audio sync" needs the user's own machine and their own regression clip, same category of
gap as the still-open Phase 11 checklist below.

Full non-integration suite: 365 tests passing (359 + 6 new), no regressions.

## Twelfth post-Phase-11 follow-up: 11 real text-on-video features, researched against real GitHub prior art

Real user demand: "find similar programs on GitHub, focus on text-in-video, add at least 10 more real
functions." Research: searched for open-source caption/text-overlay tooling and looked closely at
OpenReel Video (github.com/Augani/openreel-video, an open-source CapCut-style editor) - confirmed its
real feature vocabulary is outline/shadow/background/alignment/case-transform/karaoke-highlight, which is
exactly the category of controls FFmpeg's own `drawtext` filter documents natively (confirmed by reading
FFmpeg's real filter source docs, `doc/filters.texi`, not guessed) - so this pass grounds every new control
in both real prior art and real, existing FFmpeg capability, not invented syntax.

**11 real, working per-clip text features added** (all in `TimelineClip`/`TextAdvancedStyle`, wired through
`TimelineEditSession.SetTextAdvancedStyle`/`ApplyTextStyleToAllClipsOnTrack`, `TimelineClipItemViewModel`,
and burned into the actual export via `FfmpegFilterGraphBuilder` - not preview-only):

1. **Outline/kontura** - `TextOutlineColor`/`TextOutlineWidthPx` → drawtext `borderw`/`bordercolor`.
2. **Shadow/senka** - `TextShadowColor`/`TextShadowOffsetPx` → drawtext `shadowcolor`/`shadowx`/`shadowy`.
3. **Background box on/off** - `HasTextBackground` (default true, preserves the old always-on look) +
   `TextBackgroundColor`/`TextBackgroundOpacity` → drawtext `box`/`boxcolor`/`boxborderw`. Before this, the
   box was permanently hardcoded on with no way to turn it off - a real, previously-missing control.
4. **Horizontal alignment** - new `TextHorizontalAlign` enum (Left/Center/Right), independent of the
   existing vertical `CaptionTextPosition` (Top/Middle/Bottom) - combined, a real 9-zone position grid
   instead of always horizontally centered.
5. **Bold toggle** - `IsTextBold`, independent of the `ArialBold`/`ComicSansBold` font-choice presets (an
   already-bold preset stays bold with the toggle off; the toggle can also bold a plain Arial/Georgia
   clip). `CaptionFontResolver.ResolveFontFilePath` now resolves the real bold/italic font file variant
   per family (arialbd.ttf, georgiab.ttf, comicbd.ttf, etc.) instead of only supporting pre-baked presets.
6. **Italic toggle** - `IsTextItalic`, same mechanism (ariali.ttf/georgiai.ttf/comici.ttf, or the combined
   bold-italic variant when both toggles are on).
7. **Case transform** - `TextCase` (Normal/UPPERCASE/lowercase/Title Case), applied to the burned-in text
   only - never mutates the actual `TextContent`/transcription behind it.
8. **Line spacing** - `LineSpacingPx` → drawtext `line_spacing` (matters for multi-line captions).
9. **Text fade-in/fade-out** - reuses the existing `FadeInSeconds`/`FadeOutSeconds` fields (already present
   on every clip type, but never previously applied to Caption/Text clips at all) via drawtext's `alpha`
   option, confirmed from FFmpeg's own docs to accept a per-frame expression (not just a fixed value) -
   ramps 0→1 over the fade-in window and 1→0 over the fade-out window.
10. **9-zone position** - the real combination of #4 (horizontal) and the pre-existing vertical position.
11. **"Primeni na sve titlove na traci" (apply style to all clips on this track)** - a real bulk-editing
    command (`TimelineEditSession.ApplyTextStyleToAllClipsOnTrack`), so styling a batch of auto-generated
    captions doesn't mean re-clicking the same font/outline/shadow/etc. on every single clip by hand. Never
    touches `TextContent` on the target clips.

**Real bug found and fixed while writing this pass's tests** (not hypothetical - caught by a real failing
test, not code review): `TimelineEditSession`'s internal `Clone(TimelineClip)` - used by every single undo
snapshot - had an explicit field list that had silently fallen behind `TimelineClip` itself over several
past sessions. `FontChoice`, `FontSizePx`, `TextColor`, `TextPosition`, `TransitionInType`,
`TransitionInDurationSeconds` were all missing from it, meaning **every edit's undo snapshot silently
discarded a clip's text style and transition settings**, resetting them to hardcoded type defaults on
undo instead of the actual previous value. A style-A → style-B → Undo sequence landed on defaults, not
style A. Existing tests never caught this because they always undid from an already-default starting
state, where the bug is invisible. Fixed by listing every `TimelineClip` field explicitly (deliberately no
reflection/serialization shortcut, so a future field addition is a visible one-line diff, not another
silent gap) and covered with a new regression test
(`SetTextStyle_TwoSuccessiveEdits_UndoRevertsToFirstEditNotHardcodedDefaults`) that fails without the fix
and passes with it.

**Player**: no further player work this pass - the eleventh follow-up already added a real LibVLC-based
continuous audio+video player, using the same GitHub-sourced approach (LibVLCSharp/libvlc) many real
open-source video editors use for embedded playback; this user message explicitly redirected focus to
text-in-video instead.

Verified: 25 new/updated tests (`FfmpegFilterGraphBuilderTests` - outline, no-outline, shadow, background
off, custom background color/opacity, all 3 horizontal alignments, all 4 case transforms, line spacing,
no-alpha-without-fade, alpha-with-fade-in-and-out; `TimelineEditSessionTests` - SetTextAdvancedStyle
+ undo, the two-edits-then-undo regression test, ApplyTextStyleToAllClipsOnTrack correctness +
track-scoping + undo; `CaptionFontResolverTests` - bold/italic variant resolution, already-bold-preset
stays bold without the toggle) plus a real, hands-on ffmpeg render (not just assertions): a 6-second test
video with 5 back-to-back caption clips (default/outline/shadow/left-align-uppercase/right-align),
rendered through the real `FfmpegFilterGraphBuilder` output with real ffmpeg, frames extracted at each
clip's midpoint and visually inspected - confirmed a red outline, a blue drop shadow, no background box
where disabled, "LEVO" (uppercase, left-pinned) and "desno" (right-pinned) exactly where expected.

Full non-integration suite: 390 tests passing (365 + 25 new), no regressions.

## Thirteenth post-Phase-11 follow-up: researched player improvement - bounded-range preview rendering

Direct follow-up to a real, repeated user demand: "search GitHub for a player solution, build the most
functional player." Searched specifically for comparable open-source projects on the same tech stack as
this app (C#/Avalonia) rather than general video-player advice, to find something concretely applicable:

**Real research finding**: FramePFX (github.com/AngryCarrot789/FramePFX) is a real, open-source non-linear
video editor written in C# using Avalonia - the closest possible comparison to this project's own
architecture. Its own documentation describes live, full-timeline multi-clip compositing (decoding and
compositing every clip's frames on every playback tick, without pre-rendering) as a genuinely hard,
still-unsolved performance problem even for a project built specifically for that: "AVMediaVideoClip is
extremely slow for large resolution videos (4K takes around 40ms to decode and render onscreen)," and the
project is undergoing a full architectural rewrite partly because of it. This is real, useful evidence:
the "render then play" approach this app's real LibVLC player (eleventh follow-up) already uses is the
same pragmatic tradeoff a dedicated comparable project also lands on, not a shortcut - but it also pointed
directly at the concrete, achievable improvement actually worth building: make the render step itself fast
enough to not matter, instead of chasing true live compositing.

**Built**: `FfmpegFilterGraphBuilder.ExtractRangeTimeline(timeline, rangeStart, rangeEnd)` cuts a new,
standalone `Timeline` containing only the clips overlapping a requested time window, each re-timed
relative to the window's own start (trims split correctly at the boundary; a clip whose start gets cut
off has its `TransitionInType` reset to `None`, since there's nothing left in the reduced timeline for it
to transition from). `WorkspaceViewModel.RenderRealPreviewAroundPlayheadCommand` ("Renderuj deo oko
plejhed-a") uses it to render only a ~15-second window around the current playhead instead of the whole
project, wrapped in a temporary in-memory `Project` so the exact same `IRenderService`/real-ffmpeg
pipeline the full-render command already uses runs unchanged - previewing a change deep into a 30-minute
project now takes seconds instead of however long the whole project takes to encode. The original
full-timeline "Renderuj celu traku i pusti sa zvukom" command stays available alongside it.

Verified: 6 new `FfmpegFilterGraphBuilderTests` (clip fully inside range, clip entirely outside → track
dropped, clip straddling the range start → trimmed + transition cleared, clip straddling the range end →
trimmed, text style fields preserved through the cut, and a full `Build()` on a range-extracted timeline
producing the correct shorter duration and `trim=` arguments); 2 new `WorkspaceViewModelTests` (no-clips
guard, graceful-degrade-when-player-unavailable, mirroring the full-render command's existing tests); and
a real, hands-on ffmpeg render (not just assertions) - a real 20-second test video with a caption
authored at the original timeline's 11-13s, range-extracted to [10,15], rendered through real ffmpeg:
confirmed via `ffprobe` the real output is exactly 5.000000 seconds (not 20), and a frame extracted at the
output's 1.5s mark (corresponding to the original timeline's 11.5s, mid-caption) correctly shows the
caption text - proving the whole cut-trim-retime-render chain is correct end to end, not just that the
individual pieces look right in isolation.

Full non-integration suite: 398 tests passing (390 + 8 new), no regressions.

## Fourteenth post-Phase-11 follow-up: real, install-breaking bug in the custom installer

Real user report, with screenshots: after a genuinely successful "Instalacija je uspešno završena" message,
the installer immediately failed to auto-launch the app with "The system cannot find the file specified,"
and the install folder contained mangled sibling folders like "NP Video Studiolibvlc" and
"NP Video Studioruntimes" next to the real "NP Video Studio" folder, instead of `libvlc`/`runtimes` living
inside it.

**Root cause, found by reading the code, not guessed**: `NPVideoStudio.Installer/Program.cs`'s
`CopyDirectory` joined paths with `dirPath.Replace(sourceDir, targetDir, StringComparison.Ordinal)` - a
naive string swap. `AppContext.BaseDirectory` (the installer's real `sourceDir`, wherever
`NPVideoStudioSetup.exe` itself is running from) is documented .NET behavior to always end with a trailing
directory separator; `InstallDir` (`Path.Combine(LocalAppData, "Programs", "NP Video Studio")`) never has
one. Replacing a prefix that includes a trailing separator with one that doesn't silently eats the
separator between the install folder and every single copied item's name - not just nested subfolders
(`libvlc` → `NP Video Studiolibvlc`) but every top-level file too, including the app's own exe
(`NPVideoStudio.exe` → `...NP Video StudioNPVideoStudio.exe`, a path nothing else in the installer ever
constructs or looks for). This is why the install itself reported success (every file really did get
copied - just to the wrong, mangled path) while the immediate post-install launch failed outright.

**Why this was never caught before now**: this Linux dev sandbox cannot execute a Windows PE binary, so
`NPVideoStudioSetup.exe`'s actual copy logic could never run end-to-end here - exactly the standing,
disclosed constraint in this file's own header ("packaged for Windows via GitHub Actions CI... because
the installer and native runtime behavior can't be verified on Linux"). No automated test covered this
method either (it was a private method in a console app's `Program.cs`, nothing referenced it). This is
the first time that specific gap produced a real, concrete, install-breaking failure a real user actually
hit - not a hypothetical risk.

**Fixed** with `Path.GetRelativePath(sourceDir, path)` + `Path.Combine(targetDir, relative)`, which are
correct regardless of either side's trailing separator - `CopyDirectory` made `public` (was `private`) and
covered by a new, real regression test (`InstallerCopyDirectoryTests.cs`, in a new
`NPVideoStudio.UnitTests` → `NPVideoStudio.Installer` project reference) using real temporary directories
on disk, deliberately reproducing the exact trailing-separator mismatch rather than asserting against the
old buggy string output. **Verified the test actually catches the bug** (not just theoretically) by
temporarily reverting the fix, confirming the test fails with the exact same symptom the user hit
("Expected .../NP Video Studio/NPVideoStudio.exe to exist"), then restoring the fix and confirming it
passes - the same before/after methodology used elsewhere in this file for other real bug fixes.

Also checked the alternate Inno Setup installer (`installer/NPVideoStudio.iss`) for the same class of bug:
clean - its `[Files]` section uses Inno Setup's own native `Source`/`DestDir` copy mechanism with
`recursesubdirs createallsubdirs`, never custom string path-joining, so it was never affected.

Full non-integration suite: 399 tests passing (398 + 1 new), no regressions.

## Fifteenth post-Phase-11 follow-up: whole-program audit + the real root cause of "doesn't recognize song lyrics"

Real, repeated user demand: a full function-by-function audit of the entire program, with an exact report
of what was checked, how, and what percentage. Delegated a focused investigation (not guessed) into every
code path behind the specific reported symptom - "ne prepoznaje tekst pesme" / "ne ubacuje tekst iz pesme"
(doesn't recognize/insert song lyrics) - across `WhisperTranscriber`, `LyricMatcher`, `LyricSearchService`,
`KnownSongLyricLocator`, `WhisperModelLocator`, `DependencyManagerService`, and all three real consumers of
Whisper transcription (Pronađi tekst u pesmi, Generiši titlove SRT, workspace auto-captions). Found and
fixed five real bugs, not just re-confirmed the already-disclosed "model not downloaded" gap:

**The root cause, confirmed real (not the disclosed model-download requirement)**:
`LyricMatcher.Normalize` only lowercased and stripped punctuation - it never transliterated script.
Whisper transcribes Serbian speech/singing in **Cyrillic** ("волим те"), but the search UI's own
watermark invites **Latin** input ("npr. volim te draga moja") and `AppSettings.Language` defaults to
"sr-Latn". A Latin-typed phrase shared zero normalized tokens with a Cyrillic transcript, so **every
single search silently returned "not found"** - which read to the user exactly as "the program doesn't
recognize the song's lyrics at all," while technically reporting a normal, honest "not found" message.
`SerbianScriptConverter` (real, tested, lossless Cyrillic↔Latin transliteration) already existed in the
same project for exactly this purpose but was never called from the matching path. Fixed: `Normalize` now
transliterates Cyrillic to Latin when detected, and additionally folds the five Serbian Latin diacritics
(š/đ/č/ć/ž) so a phrase typed without them (common on keyboards that don't have them) still matches a
transcript that has them, in both directions. This same fix transitively fixes `KnownSongLyricLocator`
(used to place known/verified stored lyrics onto the timeline), which builds on the same `Normalize`.

**Four more real bugs found and fixed in the same pass**:
- **Wrong language hint**: `WhisperFactory` was built with `WithLanguage("auto")` - documented
  whisper.cpp behavior is that auto-detection frequently misdetects the wrong language on singing/music
  specifically (as opposed to plain speech). Every string and every piece of content this app targets is
  Serbian (see CLAUDE.md); hardcoded to `"sr"` instead, removing an avoidable source of garbled/wrong-
  language transcription.
- **Misleading model-not-ready message**: pointed to "Podešavanja → AI modeli" - a screen that does not
  exist anywhere in this app (confirmed by grep). Fixed to name the real, working place (the "Preuzmi
  model" button inside "Generiši titlove (SRT)"/"Pronađi tekst u pesmi").
- **Wrong path in the Alati i modeli/Dijagnostika screen**: `DependencyManagerService` always reconstructed
  the AppData default model path, even when the model was actually resolved from the bundled
  `Tools/whisper-models/ggml-tiny.bin` next to the exe - "Otvori folder" could open a path with nothing in
  it. Fixed by exposing the service's own real, resolved `ModelPath` (new `ILyricSearchService.ModelPath`)
  instead of reconstructing a guess.
- **Padding/duration clamp bug**: for a lyric match near the very start of a track, `Duration` always
  added the padding twice regardless of whether `Start` actually got clamped to zero - producing an
  exported clip running measurably past the phrase's real end. Fixed to compute `Duration` from the real
  (possibly-clamped) start to `end + padding`, correct in both cases.

**Full-program audit scope and methodology, quantified honestly**: 408 automated tests passing (405 + 3
new headless-navigation smoke tests), across 54 test files. `AppSmokeTests.cs` boots the *real* Avalonia
application composition root headlessly (no mocked navigation) and now covers real, exception-free
navigation into 15 of the app's screens - including three added this pass (Pronađi tekst u pesmi, Generiši
titlove SRT, Preuzmi sa YouTube-a) that were previously untested at the navigation level, closing a real
gap directly in the area under complaint. This headless approach was used instead of a live Xvfb
click-through because synthetic mouse clicks in this specific sandbox proved unreliable again this session
(confirmed by testing: even the always-first "Novi projekat" tile didn't register a click after an app
restart) - a real, previously-documented environment quirk, not a code regression; a headless Avalonia
navigation test is strictly stronger evidence for "does this screen open and initialize without throwing"
than a screenshot anyway, since it fails loudly on any unhandled exception instead of just looking wrong.

**Honestly out of scope for this sandbox, disclosed rather than silently skipped**: whether real singing
transcription quality is actually *good* (only checkable with a real downloaded model + real audio, both
blocked/unavailable here); the real Windows install-and-click experience beyond what headless Avalonia
tests can prove; two lower-priority findings from the same investigation left unfixed this pass - a missing
ffmpeg surfaces as a raw English OS error message instead of a translated one, and changing FFmpeg path in
Settings needs an app restart to take effect (both real, both minor, both logged here for a future pass
rather than silently dropped).

Full non-integration suite: 408 tests passing (405 + 3 new), no regressions.

## Sixteenth post-Phase-11 follow-up: installer robustness against a silent (no dialog, no error) failure

Real user report: after the fourteenth follow-up's path-joining fix, `NPVideoStudioSetup.exe` now does
**nothing at all** on double-click - no error dialog, no window, no visible sign of running. This is a
different failure mode than the fourteenth follow-up's bug (which at least reported "success" then failed
to launch) - genuine silence means either something is failing before `Install()`'s own try/catch can run,
or the OS/AV layer is intervening before the process's own code gets a chance to show anything, neither of
which this Linux sandbox can reproduce or directly observe (cannot execute a Windows PE binary at all).
Made the installer maximally robust against every plausible cause this session's own changes could have
introduced, rather than guessing at one and hoping:

- **Long-path awareness**: this session's eleventh follow-up bundled a deep libvlc plugin tree
  (`libvlc/win-x64/plugins/<category>/<name>_plugin.dll`) that `CopyDirectory` now walks recursively - a
  real install path can plausibly approach or exceed the classic 260-character Windows `MAX_PATH` limit
  depending on the user's own folder/username depth, which throws `PathTooLongException` mid-copy on an
  otherwise completely normal machine. Added `<longPathAware>true</longPathAware>` to a new
  `NPVideoStudio.Installer/app.manifest` (the installer previously had no manifest at all) and to the main
  app's existing manifest (it loads libvlc plugins from the same deep tree at runtime).
- **Removed a real silent-failure gap**: `Main()` previously only wrapped `Install()`'s own body in a
  try/catch - any exception thrown before that point (or one `Install()` itself didn't anticipate) would
  propagate out of a console-less `WinExe` with zero visible sign to the user, indistinguishable from
  "double-clicking did nothing." Wrapped the entirety of `Main()` in a top-level try/catch that both shows
  a message box and writes a real log file (`NPVideoStudioSetup-greska.log`) next to the exe - the same
  folder the user already has open - so any future failure leaves real, findable evidence instead of
  silence, whether or not the current fix turns out to be the actual cause.

**Also addressed directly**: the user asked why the delivered folder/zip is still named with "Portable" in
it, reading that as a sign something didn't really install. That naming is unrelated to any code path -
it's purely this chat-delivered package's own folder name, distinct from the actual installed app name
("NP Video Studio" everywhere it matters: Start Menu, Add/Remove Programs, window title) - renamed the
sandbox-delivered package folder to remove the ambiguity going forward. `scripts/build-release.ps1`'s own
official CI portable-ZIP artifact name is untouched (a real, intentionally-named separate distribution
format for the GitHub Actions release pipeline, not the same thing as this chat-delivered install package).

Verified: full non-integration suite still green (408/408, no regressions - these are infrastructure/
manifest changes with no testable business logic of their own). **Cannot verify** whether either specific
fix (long-path limit, or a trim/single-file/AV-related failure the crash log will now surface) is the
actual root cause of the reported silent failure - this Linux sandbox cannot execute the resulting
`NPVideoStudioSetup.exe` at all, so this is deliberately a defense-in-depth pass (fix every plausible real
cause this session's own changes could have introduced) rather than a single confirmed fix, and honestly
disclosed as such. If the next report is still silence, the crash log (or its absence) is the next real
diagnostic signal to look at, not a guess.

**Package actually delivered to the user for this follow-up** (built and sent in-session, not just
committed): renamed to `NPVideoStudio-Instalacija-x64` as promised above. New finding while rebuilding:
`gyan.dev` (the FFmpeg "essentials" build source `scripts/build-release.ps1` uses) is **not reachable from
this sandbox's network policy** - a real, previously-undocumented gap (CLAUDE.md's network section only
listed `huggingface.co`/`youtube.com` as known-blocked). Confirmed `github.com` release-asset download URLs
*do* work here even though the `github.com` HTML page itself doesn't (403), so a GitHub-hosted FFmpeg
mirror (`BtbN/FFmpeg-Builds`) downloads fine - but at ~278 MB for ffmpeg.exe+ffprobe.exe combined (a full
GPL/master build with far more codecs than gyan's "essentials" subset), bundling it would have nearly
tripled this chat-delivered package's size and part count (4 parts -> ~17), for a user who has already
struggled with manual multi-part reassembly. Judgment call: left FFmpeg out of this specific hand-delivered
package (as it always has been - this was never actually bundled in a chat-delivered package this session,
only intended for the real `windows-latest` CI build, which *can* reach gyan.dev), bundled `yt-dlp.exe`
(small, 18 MB, downloads fine from `github.com`), and disclosed the gap plainly in the package's own
`PROCITAJ_PRVO.txt` rather than silently shipping an incomplete package with no explanation. Final delivered
zip: 86 MB, split into 4 parts (was 7-8 in earlier rounds) via the same `cmd /c copy /b` reassembly
instructions, MD5-verified byte-identical after reassembly before sending.

## Seventeenth post-Phase-11 follow-up: real destination/desktop-shortcut choice in the fallback installer

Real user report, and a legitimate one: the fallback `NPVideoStudioSetup.exe` never asked where to
install, never offered a desktop shortcut, and never asked whether to launch the app afterward - it
silently always installed to the same fixed `%LocalAppData%\Programs\NP Video Studio` location. This was
a deliberate original design choice (documented in the old class-level doc comment: "one product, one
install location, no custom install path picker"), but never actually communicated to the user as a
tradeoff - to them it just looked broken/opaque, and asking for a destination picker + desktop icon
option is completely standard, reasonable installer behavior.

Two more capable alternatives were considered and ruled out before landing on the actual fix:
- **A real WinForms dialog directly in this project** (`InstallOptionsForm` with a text box, "Browse..."
  button, checkboxes) - built once, then discovered this dev sandbox's .NET SDK has no WindowsDesktop
  workload installed at all (`dotnet workload install windowsdesktop` reports "not recognized" - it isn't
  distributable for a Linux SDK host in the first place, since it contains real Windows-only build tooling,
  not just reference assemblies). `<EnableWindowsTargeting>true</EnableWindowsTargeting>` doesn't help
  either - it only affects framework-reference resolution, not the `Microsoft.NET.Sdk.WindowsDesktop`
  targets import itself, which is a hardcoded relative path in the local SDK installation, not something
  NuGet can supply. Reverted this approach entirely (deleted the file, TFM back to plain `net8.0`).
- **Compiling the existing, already-correct `installer/NPVideoStudio.iss` Inno Setup script** - it already
  has a real destination-picker wizard page (no `DisableDirPage`) and a desktop-icon task checkbox, and was
  confirmed clean of the CopyDirectory bug class in an earlier pass. Blocked here too: `jrsoftware.org` (the
  only real source of the Inno Setup compiler) returns a 403 through this sandbox's network policy - the
  exact same reachability gap `NPVideoStudio.Installer`'s own doc comment cites as the reason the custom
  installer exists in the first place, now confirmed to also apply to this dev sandbox, not just
  hypothetical end-user machines.

**What actually shipped**: `Install()` now shells out to `powershell.exe` running
`System.Windows.Forms.FolderBrowserDialog` (`Add-Type -AssemblyName System.Windows.Forms`) for a real
folder-browse dialog, then two `MessageBoxW`-based yes/no prompts for "add a desktop shortcut?" and "launch
after install?" - the same shell-out-to-powershell.exe pattern this file already used and trusted for
`CreateShortcut`'s WScript.Shell COM call, so no new UI framework dependency was added to this project at
all, and it still builds as plain `net8.0` (verified: `dotnet build` succeeds, 0 warnings/errors). Every
real Windows machine has `powershell.exe` with `System.Windows.Forms` available even though this Linux
sandbox can't compile a project that references it directly - this sidesteps the SDK gap rather than
working around it partially. `CreateShortcut` was generalized to take an explicit shortcut path (Start Menu
vs. Desktop); `Uninstall()` now reads the real install location back from the registry (`RegisterUninstallEntry`
already wrote it there) instead of assuming the old fixed default, since the location is no longer fixed.
A real typo caught before commit: a stray Cyrillic "а" (U+0430) had been typed into an otherwise-Latin
Serbian string during a fast edit - caught by grepping the file's own bytes with `cat -A`, ironic given this
exact class of Cyrillic/Latin mixing was this session's root-cause bug for a completely different feature
(lyric search) - fixed before it could ship as a second instance of the same defect class.

Verified: full non-integration suite still green (408/408, no regressions - `CopyDirectory`'s public surface
and behavior are untouched, only `Install`/`Uninstall`'s surrounding orchestration changed, and those aren't
unit-testable without a real Windows session to run `powershell.exe`/`MessageBoxW`/shell COM against, which
this sandbox cannot do - honestly out of scope here, same standing constraint as the rest of this
installer). New package rebuilt and delivered to the user with this fix; `PROCITAJ_PRVO.txt` in the
delivered package also now includes real cleanup instructions for the mangled `%LocalAppData%\Programs\NP
Video Studio*` leftovers a prior buggy install run may have left behind, since deleting those wasn't
something code could safely automate from here.

## Next action

Phase 11 needs the two items above from the user (a real Windows machine, and their own regression clip)
before it can be called fully complete - everything else in Phase 11's spec is done. This is also the
last planned phase (`docs/MASTER_SPEC.md` only defines Phases 0-11).

Post-Phase-11 follow-ups above still need the user to `git pull` + re-run `scripts\build-release.ps1` and
confirm on their real machine that: (a) a picture now actually appears in the player while scrubbing/
stepping/playing, (b) "Izvezi video" now produces a real playable file that reflects what's actually on
the timeline, and (c) OCR on their real video reads the stylized caption correctly once they've installed
the optional `easyocr-helper/requirements.txt` dependency (not bundled - Python/PyTorch is too heavy to
auto-download the way FFmpeg/yt-dlp now are).
