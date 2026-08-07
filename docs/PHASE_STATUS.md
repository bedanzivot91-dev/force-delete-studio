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

## Next action

Phase 11 needs the two items above from the user (a real Windows machine, and their own regression clip)
before it can be called fully complete - everything else in Phase 11's spec is done. This is also the
last planned phase (`docs/MASTER_SPEC.md` only defines Phases 0-11).

Both post-Phase-11 follow-ups above still need the user to `git pull` + re-run `scripts\build-release.ps1`
and confirm on their real machine that: (a) a picture now actually appears in the player while scrubbing/
stepping/playing, and (b) "Izvezi video" now produces a real playable file that reflects what's actually
on the timeline (the stale-timeline export bug above is now fixed in code and covered by a regression
test, but not yet confirmed against the user's own real project/clip on real Windows).
