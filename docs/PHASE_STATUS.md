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
| 4 | Song library + fingerprinting | DONE (partial - see below) | (pending push) |
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

## Next action

Start Phase 5 (AI pipeline: known song → verified lyrics; unknown song → ASR) only when told to proceed.
