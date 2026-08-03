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

## Next action

Start Phase 7 (caption styling + video layout/OCR) only when told to proceed.
