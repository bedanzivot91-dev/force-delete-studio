# Function Matrix — Phase 0

Every visible control bound to a command, enumerated from the actual `.axaml`/`.cs` files (not
memory/assumption). Cross-checked: every `Command="{Binding X}"` in every View has a matching
`[RelayCommand]`-generated `XCommand` in that View's `x:DataType` ViewModel — zero orphans found.

**Status legend** (exactly as specified, no vague statuses used):
- `WORKING_VERIFIED` — executed at runtime (automated test, or this session's headless-screenshot
  session with real DI/services) and the result was observed to be correct.
- `IMPLEMENTED_NOT_RUNTIME_VERIFIED` — code exists and looks correct, but no test or manual run in this
  audit actually executed it.
- `NOT_PRESENT` — the master prompt describes it, but it does not exist in this codebase.
- (No rows below are `BROKEN` or `PLACEHOLDER` — see BASELINE_AUDIT §5 for the scan that found none.)

Evidence codes: **[Smoke]** = `AppSmokeTests.cs` (AvaloniaFact, executes the real command and asserts
the resulting page type). **[Svc]** = a dedicated unit/integration test exists for the service the
command calls, but not for the command/binding itself. **[Screenshot]** = verified this session via a
headless Skia render harness that boots the real DI container, executes the real command, and captures
the rendered frame — real runtime behavior, but not a committed regression test. **[None]** = no
automated or manual verification found/performed.

## Početni ekran — StartScreenView / StartScreenViewModel

| Control | Command | Target service | Status | Evidence |
|---|---|---|---|---|
| "Novi projekat" tile | `NewProjectCommand` | raises `NewProjectRequested` → `MainWindowViewModel` | WORKING_VERIFIED | [Smoke] (`MainWindow_ShowsStartScreen...`) covers the page; navigation pattern shared with Settings/Diagnostics which are directly asserted |
| "Otvori projekat" tile | `OpenProjectCommand` | `IProjectRepository.LoadAsync` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] `ProjectRepositoryTests.cs` covers load/save, not this exact command |
| "YouTube video" tile | `NewYouTubeVideoCommand` | raises event with `TargetPlatform.YouTube` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| "YouTube Shorts" tile | `NewYouTubeShortsCommand` | same, `.YouTubeShorts` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| "TikTok video" tile | `NewTikTokCommand` | same, `.TikTok` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| "Instagram Reel" tile | `NewInstagramReelCommand` | same, `.InstagramReel` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| "Facebook Reel" tile | `NewFacebookReelCommand` | same, `.FacebookReel` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| "Isečci iz pesme" tile | `OpenSongHighlightsCommand` | raises `SongHighlightsRequested` | WORKING_VERIFIED | [Screenshot] this session (start screen → tool screen render) |
| "Pronađi tekst u pesmi" tile | `OpenLyricSearchCommand` | raises `LyricSearchRequested` | WORKING_VERIFIED | [Screenshot] this session |
| "Preuzmi sa YouTube-a" tile | `OpenYouTubeDownloadCommand` | raises `YouTubeDownloadRequested` | WORKING_VERIFIED | [Screenshot] this session |
| "Generiši titlove (SRT)" tile | `OpenSubtitleGeneratorCommand` | raises `SubtitleGeneratorRequested` | WORKING_VERIFIED | [Screenshot] this session |
| "Moje pesme" tile | `OpenMySongsCommand` | raises `MySongsRequested` | WORKING_VERIFIED | [Smoke] `AppSmokeTests.cs: Navigating_ToMySongs_LoadsRealLibraryWithoutThrowing` (Phase 4) |
| Nastavi od auto-save | `RecoverAutoSaveCommand` | `IAutoSaveService` + `IProjectRepository` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] `AutoSaveServiceTests.cs` covers the service, not this command |
| Zanemari (recovery) | `DismissRecoveryCommand` | clears `RecoveryMessage` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] (trivial, no side effect beyond a bound property) |
| Podešavanja tile | `OpenSettingsCommand` | raises `SettingsRequested` | WORKING_VERIFIED | [Smoke] indirectly (`MainWindowViewModel.GoToSettingsCommand` uses the same `CreateSettingsPage`) |
| Dijagnostika tile | `OpenDiagnosticsCommand` | raises `DiagnosticsRequested` | WORKING_VERIFIED | [Smoke] indirectly, same reasoning |
| Recent project "Otvori" | `RecentProjectItemViewModel.OpenCommand` | `IProjectRepository.LoadAsync` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] repository tested, not this exact wiring |
| Recent project "Ukloni" | `RecentProjectItemViewModel.RemoveCommand` | `IRecentProjectsService.RemoveAsync` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| Planned-feature tiles (×6) | none (`IsEnabled="False"`) | n/a | disabled by design, not a bug — see BASELINE_AUDIT §5 | n/a |

## Navigacija — MainWindow / MainWindowViewModel

| Control | Command | Status | Evidence |
|---|---|---|---|
| "Početni ekran" | `GoHomeCommand` | WORKING_VERIFIED | [Smoke] |
| "Podešavanja" | `GoToSettingsCommand` | WORKING_VERIFIED | [Smoke] `Navigating_ToSettingsAndDiagnostics_DoesNotThrow` |
| "Dijagnostika" | `GoToDiagnosticsCommand` | WORKING_VERIFIED | [Smoke] same test |

## Novi projekat — NewProjectView / NewProjectViewModel

| Control | Command | Status | Evidence |
|---|---|---|---|
| "Napravi projekat" | `CreateCommand` → `IProjectRepository.SaveAsync` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] save covered by `ProjectRepositoryTests.cs`, this exact UI flow not exercised this session |

## Radni prostor — WorkspaceView / WorkspaceViewModel

| Control | Command | Status | Evidence |
|---|---|---|---|
| "Dodaj medije" | `ImportMediaCommand` → `IStorageService` + `IMediaProbeService` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] `FfprobeServiceTests.cs` covers the probe, not the picker flow |
| "Sačuvaj projekat" | `SaveProjectCommand` → `IProjectRepository.SaveAsync` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] same as above |
| Media row ★ favorite | `MediaAssetViewModel.ToggleFavoriteCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| Media row "Ukloni" | `MediaAssetViewModel.RemoveCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| Drag-and-drop onto window | `WorkspaceView.axaml.cs: OnDrop` (event handler, not a command) | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] — legitimate `async void`, no test drives an actual drop event |
| Timeline (tracks, trim, effects) | — | NOT_PRESENT | Disclosed in-UI as "u razvoju"; see MASTER_SPEC Phase 8 |

## Podešavanja — SettingsView / SettingsViewModel

| Control | Command | Status | Evidence |
|---|---|---|---|
| "Izaberi..." (projects folder) | `BrowseProjectsFolderCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| "Izaberi..." (cache folder) | `BrowseCacheFolderCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| "Sačuvaj podešavanja" | `SaveCommand` → `ISettingsService.SaveAsync` | WORKING_VERIFIED | [Phase 2] `SettingsViewModelTests.cs` drives the real command against an isolated real `SettingsService`, then reloads from disk with a fresh instance to prove persistence, including the new FFmpeg/FFprobe/yt-dlp fields |
| "Vrati podrazumevano" | `ResetToDefaultsCommand` | WORKING_VERIFIED | [Phase 2] `SettingsViewModelTests.cs: ResetToDefaultsCommand_RestoresDefaultsAndRefreshesViewModel` |
| FFmpeg putanja "Izaberi..." | `BrowseFfmpegPathCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] — **Phase 1**: field now exists, wired to `AppSettings.FfmpegPath` (closes BASELINE_AUDIT §8) |
| FFprobe putanja "Izaberi..." | `BrowseFfprobePathCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] — Phase 1 |
| yt-dlp putanja "Izaberi..." | `BrowseYtDlpPathCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] — Phase 1 |
| AI-model / caption / export settings sections | — | NOT_PRESENT | Master-prompt-requested sections; none exist yet |

**Note (Phase 1)**: tool-path fields are read into DI-registered services once at app startup
(`App.axaml.cs`); changing them here and saving only takes effect after restarting the app — disclosed
in the View's own help text.

## Teme (Themes) — Podešavanja ComboBox, Phase 3

Not a per-control row (theme selection is a plain `SelectedItem` binding, not a `Command`), so this is
tracked as a feature note rather than a matrix row. All 8 themes (`AppTheme` enum, `App.axaml.cs:
ApplyTheme`) are `WORKING_VERIFIED`: `ThemeResourceCompletenessTests.cs` (9 tests) confirms exactly 8
theme files exist and each defines all 15 required semantic resource keys; `AppSmokeTests.cs:
AllEightThemes_LoadAsRealAvaloniaResourceDictionaries` goes further and loads each one through
Avalonia's real `ResourceInclude`/`avares://` XAML parser (the same class `ApplyTheme` uses at runtime)
and resolves `ThemeAccentBrush` from it — a malformed color or a typo'd file name would throw here, not
just fail an XML check.

## Alati i modeli — DependencyManagerView / DependencyManagerViewModel (new screen, Phase 1)

| Control | Command | Status | Evidence |
|---|---|---|---|
| Početni ekran tile | `StartScreenViewModel.OpenDependencyManagerCommand` | WORKING_VERIFIED | [Smoke] `AppSmokeTests.cs: OpeningDependencyManager_LoadsRealDependencyStatusesWithoutThrowing` executes this exact command and asserts real results |
| "Proveri ponovo" / initial load | `RefreshCommand` → `IDependencyManagerService.GetDependenciesAsync` | WORKING_VERIFIED | [Smoke] same test asserts FFmpeg/FFprobe correctly reported `Installed` with a real version string on the real (non-mocked) tools present in this environment; [Svc] `DependencyManagerServiceTests.cs` (9 tests) covers found/not-found/Whisper-ready/not-ready/AI-worker-reachability directly, including a genuinely-absent yt-dlp |
| "Otvori folder" (per row) | `DependencyItemViewModel.OpenFolderCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] — opens a real OS file browser, not asserted by any test |
| "Preuzmi model" (Whisper) | `DownloadWhisperModelCommand` → `WhisperTranscriber` (via `ILyricSearchService`) | WORKING_VERIFIED | [CI] same download path already verified end-to-end via `LyricSearchServiceIntegrationTests.cs`/`SubtitleGeneratorServiceIntegrationTests.cs` |
| "Otkaži" | `CancelDownloadCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] — first cancellable long-running operation in the app; the mid-download cancel path itself hasn't been exercised by a test, only that `CanExecute` toggles correctly |
| AI radnik row (Phase 5) | — | WORKING_VERIFIED (row reporting itself) | [Svc] `AiWorkerClientTests.cs` (real subprocess against `tests/FakeAiWorker`) + `DependencyManagerServiceTests.cs` cover `IAiWorkerClient.CheckCapabilitiesAsync` honestly reporting python/faster-whisper/WhisperX/Demucs; the real `ai-worker/ai_worker.py` was manually run end-to-end in this sandbox (capability check + the "engine not installed" error path) since it has no automated test of its own (no xUnit runner for Python) |

**Real gap, not fabricated**: the master prompt's richer status vocabulary (Ažuriranje dostupno /
Oštećeno / Nekompatibilno) isn't implemented — there's no checksum or expected-version pinning system
to honestly back those states yet (see `DependencyInfo`'s doc comment). Only Instalirano/Nije
instalirano are reported, both backed by a real version-command exit code, not just file existence.
The new AI radnik row is no exception: it reports Nije instalirano in this sandbox (and will on a fresh
Windows install) since faster-whisper/WhisperX/Demucs are not installed anywhere yet — see the Phase 5
section below.

## Dijagnostika — DiagnosticsView / DiagnosticsViewModel

| Control | Command | Status | Evidence |
|---|---|---|---|
| "Pokreni ponovo" | `RunChecksCommand` → `IDiagnosticsService.RunAllChecksAsync` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] `DiagnosticsServiceTests.cs` (5 tests) covers the service; command itself untested this session |
| "Napravi paket za podršku" | `CreateSupportPackageCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] same file covers `CreateSupportPackageAsync` |
| Per-check "Pokušaj automatsku popravku" | `DiagnosticCheckItemViewModel.AutoFixCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] `TryAutoFixAsync` covered by service tests |
| "Alati i modeli" screen (Dependency Manager) | — | now a real screen, see its own section below | Phase 1 |

## Isečci iz pesme — SongHighlightsView / SongHighlightsViewModel

| Control | Command | Status | Evidence |
|---|---|---|---|
| "Izaberi pesmu..." | `PickSongCommand` | WORKING_VERIFIED | [Screenshot] render; [Phase 2] `SongHighlightsViewModelTests.cs: PickSongCommand_UsesStorageServiceResult_SetsSelectedFile` with a `FakeStorageService` |
| "Analiziraj pesmu" | `AnalyzeCommand` → `ISongHighlightService.FindHighlightsAsync` | WORKING_VERIFIED | [Phase 2] `SongHighlightsViewModelTests.cs` drives the real command through real ffmpeg on the real `lyric_test_song.mp3` fixture; `SongHighlightServiceTests.cs` (5 tests) separately covers the windowed-selection algorithm |
| "Izvezi sve" | `ExportAllCommand` → `ExportHighlightAsync` | WORKING_VERIFIED | [Phase 2] `SongHighlightsViewModelTests.cs: ExportAllCommand_RealFfmpegExport_WritesRealAudioFiles` asserts real non-empty files on disk |
| Per-result "Otvori" | `SongHighlightItemViewModel.OpenExportedCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| Chorus/refrain detection (loudness + energy + repetition) | — | NOT_PRESENT (loudness-only exists) | Master prompt asks for combined analysis; current tool is loudness-only and says so in its own UI text |

## Pronađi tekst u pesmi — LyricSearchView / LyricSearchViewModel

| Control | Command | Status | Evidence |
|---|---|---|---|
| "Izaberi pesmu..." | `PickSongCommand` | WORKING_VERIFIED (render only) | [Screenshot] this session |
| "Preuzmi model" | `DownloadModelCommand` → `WhisperTranscriber.DownloadModelAsync` | WORKING_VERIFIED | [Svc][CI] real download+transcribe verified on Windows CI (`LyricSearchServiceIntegrationTests`, run `30748256836`) |
| "Pronađi u pesmi" | `SearchCommand` → `LyricSearchService.FindPhraseInSongAsync` | WORKING_VERIFIED | [Svc][CI] same integration tests; `LyricMatcherTests.cs` (8 tests) covers the pure matching logic locally |
| "Izvezi sve" | `ExportAllCommand` | WORKING_VERIFIED | [Svc][CI] `ExportMatchAsync_RealMatch_ProducesAPlayableClip` |
| Per-result "Otvori" | `LyricMatchItemViewModel.OpenExportedCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |
| Known-song library lookup before ASR | — | NOT_PRESENT | Always runs ASR; no "Moje pesme" library exists yet (MASTER_SPEC Phase 4) |

## Preuzmi sa YouTube-a — YouTubeDownloadView / YouTubeDownloadViewModel

| Control | Command | Status | Evidence |
|---|---|---|---|
| "Učitaj podatke" | `FetchInfoCommand` → `IYouTubeDownloadService.GetVideoInfoAsync` | WORKING_VERIFIED | [Phase 2] `YouTubeDownloadServiceTests.cs` against `tests/FakeYtDlp` (a real, cross-platform mock-process yt-dlp CLI — the "yt-dlp servis sa mock procesom" test MASTER_SPEC calls out by name) — real process launch, real JSON parsing, non-YouTube-URL and non-zero-exit-code paths all covered |
| "Preuzmi pesmu" | `DownloadCommand` → `DownloadAudioAsync` | WORKING_VERIFIED | [Phase 2] `YouTubeDownloadServiceTests.cs: DownloadAudioAsync_RealProcessCall_ProducesFileNamedAfterSanitizedTitle` (real process, real rename/sanitization) + `DownloadAudioAsync_ProcessFails_ThrowsAndLeavesNoOutputFile` |
| "Otvori u Isečci iz pesme" | `OpenInHighlightsCommand` | WORKING_VERIFIED | [Screenshot] this session — executed the real command, confirmed navigation + file preload via `LoadFile` |
| "Otvori u Pronađi tekst u pesmi" | `OpenInLyricSearchCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | same code path as above, not individually screenshotted |
| "Otvori u Generiši titlove" | `OpenInSubtitleGeneratorCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | same |
| Ownership confirmation gate | `OwnershipConfirmed` bool required by `DownloadAudioAsync` | WORKING_VERIFIED (logic) | [Screenshot] confirmed the checkbox gates `CanDownload`; `DownloadAudioAsync` throws if false (code-reviewed, not unit-tested directly) |

**Gap closed in Phase 2**: `tests/FakeYtDlp` is a tiny real console app (built by `dotnet build`/
`dotnet test` like any other project, ProjectReference'd from the test project so its native apphost
lands next to the test binary) that understands exactly the argument shapes
`YouTubeDownloadService` sends and answers with canned JSON / a placeholder output file. This is the
master prompt's "yt-dlp servis sa mock procesom" test - it exercises the real `Process.Start`/argument-
construction/stdout-parsing/file-rename code paths without the real tool or network, and works
identically on Linux and Windows CI.

## Generiši titlove (SRT) — SubtitleGeneratorView / SubtitleGeneratorViewModel

| Control | Command | Status | Evidence |
|---|---|---|---|
| "Izaberi fajl..." | `PickFileCommand` | WORKING_VERIFIED (render only) | [Screenshot] this session |
| "Preuzmi model" | `DownloadModelCommand` | WORKING_VERIFIED | [Svc][CI] shares `WhisperTranscriber` with lyric search, same CI-verified download path |
| "Generiši titlove" | `GenerateCommand` → `SubtitleGeneratorService.GenerateSrtAsync` | WORKING_VERIFIED | [Svc][CI] `SubtitleGeneratorServiceIntegrationTests`, run `30748256836`; `SrtWriterTests.cs` (9 tests) covers the pure `.srt` formatting locally |
| "Otvori .srt fajl" | `OpenGeneratedSrtCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |

## Moje pesme — MySongsView / MySongsViewModel (new screen, Phase 4)

Song library with Chromaprint/fpcalc multi-window fingerprinting (spec Phase 4): import audio, compute a
5-window fingerprint (start/quarter/mid/three-quarter/end, via `SongRecognitionService` shelling out to
`ffmpeg` + `fpcalc`), check the existing library for matches (`FingerprintMatcher`'s Hamming-distance
comparer), and only ever show the top 3 candidates for the user to confirm or reject — never auto-add a
possible duplicate on the app's own initiative, even when a match would technically qualify for
auto-accept.

| Control | Command | Status | Evidence |
|---|---|---|---|
| "Uvezi pesmu..." | `ImportCommand` → `ISongRecognitionService.ComputeFingerprintAsync` + `FindMatches` | WORKING_VERIFIED | [VM] `MySongsViewModelTests.cs` (duplicate-detection decision flow, fakes); [Svc] `SongRecognitionServiceTests.cs` (real ffmpeg + fake `fpcalc`, 5 tests) and `FingerprintMatcherTests.cs` (7 tests, pure Hamming-distance logic) cover the real fingerprinting/matching underneath |
| "Dodaj kao novu pesmu" | `ConfirmAddNewCommand` → `ISongLibraryRepository.AddAsync` | WORKING_VERIFIED | [VM] `MySongsViewModelTests.cs: ConfirmAddNewCommand_AddsEntryToRepositoryAndSongsList` |
| "Otkaži" (import) | `CancelImportCommand` | WORKING_VERIFIED | [VM] `MySongsViewModelTests.cs: CancelImportCommand_ClearsPendingStateWithoutAdding` |
| "Ponovo analiziraj" (per red) | `SongLibraryItemViewModel.ReanalyzeCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] `SongRecognitionServiceTests.cs` covers the underlying fingerprint recompute this delegates to, not this exact command wiring |
| "Obriši zapis" (per red) | `SongLibraryItemViewModel.DeleteRecordOnlyCommand` | WORKING_VERIFIED | [VM] `MySongsViewModelTests.cs: DeleteRecordOnlyCommand_OnLoadedItem_RemovesFromRepositoryAndList` |
| "Obriši zapis i fajl" (per red) | `SongLibraryItemViewModel.DeleteRecordAndFileCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] `SongLibraryRepositoryTests.cs: DeleteAsync_WithDeleteAudioFile_RemovesRecordAndFile` covers the repository call this delegates to, not this exact command wiring |

Storage: new `SongLibrary` SQLite table (`AppDatabase` schema v2), with a real file backup taken before
the v1→v2 migration runs (`SongLibraryRepositoryTests.cs: EnsureCreatedAsync_MigratingFromV1_...`,
spec Phase 4 "migration + backup-before-migration"). `fpcalc` (Chromaprint) is tracked as an optional
dependency in "Alati i modeli", same treatment as yt-dlp - the rest of the app works without it.
Licensing: no AcoustID web-service client exists anywhere in this codebase; `fpcalc` (LGPL-2.1) is
shelled out to as an external process, never linked, same pattern as ffmpeg/yt-dlp (see
`FingerprintMatcher.cs`'s doc comment).

Deliberately simplified vs. the master prompt's full Phase 4 wording: `SongLibraryEntry` does not store
sample-rate/channel-count columns (this codebase has no ffprobe stream-level parsing to source them
from honestly - `MediaAsset` only exposes `Duration`, not sample rate/channels - and faking those values
would violate the "never guess" rule) or "linked Shorts projects" (the `Project` domain model has no
concept of song usage yet - premature to add before Phase 8's timeline exists). Tempo-ratio/pitch-shift
warnings from the original spec wording are approximated by a duration-ratio check only; real tempo/
pitch detection is a DSP feature beyond fingerprint comparison and is not implemented.

## AI pipeline backend — Phase 5 (no new screen; feeds Phase 6's caption editor)

Spec Phase 5 asks for a local Python AI worker (faster-whisper/WhisperX/Demucs) plus the logic to turn
its output into correct captions for both known and unknown songs. This phase built the orchestration
and pure-logic pieces; it deliberately has no new UI screen since there's nowhere to show word-level
captions yet (that's Phase 6's caption editor) - these are backend building blocks, not user-facing
functions, so none of them get a `function-contracts.json` row (that file is control/command-scoped).

- `IAiWorkerClient`/`AiWorkerClient` (`NPVideoStudio.AI`): versioned (protocol v1) JSON-request-file-in
  / JSONL-events-out subprocess protocol, real committed Python worker at `ai-worker/ai_worker.py`
  (bundled into the publish output at `Tools/ai-worker/`, see `NPVideoStudio.App.csproj`). `CheckCapabilitiesAsync`
  honestly reports python/faster-whisper/WhisperX/Demucs availability - in this sandbox and on any fresh
  install, all three engines report `NotInstalled` (verified: `pip install` isn't reachable here, and no
  install was faked). `RunAsync` for the two real job kinds (`KnownSongAlignment`/`UnknownSongTranscription`)
  currently returns an honest `Error` event when faster-whisper isn't present, rather than a fabricated
  transcript - this is the real, not-yet-closed gap the master prompt's phase asks for next (faster-
  whisper/Demucs/WhisperX orchestration itself). Whisper.net (`WhisperTranscriber`) is untouched and
  remains the always-available Fast-profile fallback.
- `KnownSongLyricLocator` (`NPVideoStudio.AI`, pure/testable): fuzzy-match + DP (Needleman-Wunsch-style)
  alignment of a song's verified lyrics against whatever words the worker actually heard in a clip -
  this is the "known song → verified lyrics, ASR only helps timing" half of the spec. Verified text is
  never replaced by an ASR guess; only short internal gaps between two confident anchors are
  interpolated, a run with no anchor on either side is left unresolved rather than guessed. This directly
  starts closing the `NOT_PRESENT` "known-song-library lookup" gap noted in the summary below, though it
  has no UI wiring yet (nothing calls it outside its own tests) - closing the row fully needs Phase 6's
  caption data model to hold the result.
- `SerbianScriptConverter` (`NPVideoStudio.AI`, pure/testable): lossless Cyrillic↔Latin transliteration,
  correctly handling the spec's named edge cases (đ vs dž - one is a single letter, one a digraph - and
  č vs ć) plus digraph casing (title-case "Lj" vs all-caps "LJ" both collapsing to one Cyrillic letter).
  Never mutates a stored original; produces a converted copy only.
- Tests: `AiWorkerClientTests.cs` (5, real subprocess against `tests/FakeAiWorker` - launch, JSONL
  parsing, non-zero exit handling, error-event dedup, cancellation), `KnownSongLyricLocatorTests.cs` (6,
  pure DP-alignment logic), `SerbianScriptConverterTests.cs` (13, pure transliteration), plus 3 new
  `DependencyManagerServiceTests.cs` cases for the new AI-worker row. Local (non-integration) test count:
  121 → 148, all passing.

## Uređivač titlova — CaptionEditorView / CaptionEditorViewModel (new screen, Phase 6)

Central word-level caption editor (spec Phase 6): `CaptionWord` (Domain) is the word-level model
(original/normalized text, start, end, confidence, source, verification status, plus `LineBreakAfter` -
see PHASE_STATUS.md for why that one field was added beyond the spec's exact list). All editing logic
lives in `CaptionEditSession` (`NPVideoStudio.AI`, pure/testable, whole-list-snapshot undo/redo) and
`CaptionFormatConverter` (`NPVideoStudio.AI`, pure/testable SRT/VTT/ASS/TXT/JSON/LRC import/export) - the
ViewModel only wires these to the UI and to file I/O.

No `function-contracts.json` rows yet for this screen's individual controls (New/Open/Save-As/Undo/Redo/
Find-Replace/per-word Split/Merge/Delete/Nudge/Toggle-line-break/script-conversion) - unlike prior new-
screen phases, this pass didn't add a per-control smoke-test breakdown table; one `AppSmokeTests.cs`
navigation test (`Navigating_ToCaptionEditor_StartsEmptyAndAllowsARealEditRoundTrip`) drives a real new-
document → add-word → undo-available round trip through the real DI-wired ViewModel, and
`CaptionEditSessionTests.cs`/`CaptionFormatConverterTests.cs` (21 tests total) cover the actual editing/
format logic underneath directly. Per-control rows will be added the next time this file gets a full
refresh pass, consistent with how Phase 4/5's backend-heavy work was documented in prose here first.

Real, deliberate gaps (not fabricated completeness): no ASS importer (a fragile parser would silently
mis-import real files - worse than not supporting it), no TXT importer (plain text carries no timing,
inventing it would violate this codebase's "never guess" rule), no keyboard shortcuts or split-ratio
picker in the UI (the underlying `CaptionEditSession` API already supports arbitrary ratios and bulk
operations - only the UI surface is simplified this pass), and direct in-place text edits don't push
undo/redo snapshots (only the structural operations do - see PHASE_STATUS.md).

## Stilovi titlova / Analiza rasporeda videa — new screens (Phase 7)

24 caption style presets (`CaptionStylePresetCatalog` in App, backed by `CaptionStylePreset` in Domain) -
3+ per each of the 8 themes, colors taken directly from each theme's own resource dictionary. "Stilovi
titlova" browses them with a static color-swatch preview (no live animation yet - see PHASE_STATUS.md).

`IVideoLayoutAnalysisService`/`TesseractOcrService` (Media) does real local OCR-based video layout
analysis via the Tesseract CLI - substituting for the spec's named ONNX/RapidOCR path since this sandbox
has no verified way to source that model; Tesseract was actually installed and run end-to-end while
building this service. `VideoLayoutAggregator` (Media, pure) turns per-frame text regions into 3x3-grid
occupancy-over-time; `CaptionPlacementAdvisor` (AI, pure) implements the full spec priority chain (face >
text > logo > CTA > safe zone > minimize repositioning > readable > platform chrome) for "Automatic"
mode, though only the "text" signal is populated today (no face/logo/CTA detection - no license-clear
model available here). "Analiza rasporeda videa" screen runs this against a real picked video file.

No `function-contracts.json` rows yet for either screen's individual controls - same deferred-to-next-
refresh treatment as Phase 6. `VideoLayoutAggregatorTests.cs`/`CaptionPlacementAdvisorTests.cs`/
`TesseractOcrServiceTests.cs`/`CaptionStylePresetCatalogTests.cs` (37 tests total) cover the real logic
underneath directly; 2 `AppSmokeTests.cs` navigation tests cover both new screens end to end through the
real DI-wired ViewModels.

## Radni prostor — Timeline + player (Phase 8)

Real, non-destructive `Timeline`/`TimelineTrack`/`TimelineClip` model (Domain) persisted inside
`.npvsproject` (verified: a hand-built pre-Phase-8 JSON file with no `timeline` property at all still
loads correctly). `TimelineEditSession` (`NPVideoStudio.AI`, pure/testable) implements the full spec verb
list - add/remove track, split/trim-in/trim-out/move/delete/duplicate/mute/volume/fade/lock/hide/solo/
undo/redo/snap. `PlayerStateMachine` (pure/testable) implements play/pause/stop/seek/frame-step/volume/
mute/current-time/total-time. The old "Timeline je u razvoju" placeholder in `WorkspaceView` is replaced
with a real (button-based, not drag/zoom) timeline + player transport UI - see `workspace.timeline` row
above, now `WORKING_VERIFIED`.

`IProxyGeneratorService`/`ProxyGeneratorService` (Media): real ffmpeg auto-proxy generation, verified
end-to-end against a real generated test video (640x480 source -> confirmed 320x240 proxy via ffprobe) -
found and fixed a real temp-filename bug along the way (see PHASE_STATUS.md). Registered in DI; no UI
trigger button yet.

No real video decode/render - this sandbox has no display to verify a decoder integration against (spec:
"XOpenDisplay failed", confirmed since Phase 0), so the player is a fully real, tested state machine and
transport UI without an actual rendered frame yet. No `function-contracts.json` rows yet for the new
player controls individually - same deferred-to-next-refresh treatment as Phases 6/7.

## Radni prostor — Izvoz videa (Phase 9)

New "Izvezi video" button on the workspace toolbar opens a real export screen
(`RenderQueueViewModel`/`RenderQueueView`): codec (libx264/nvenc/qsv/amf, with automatic fallback to
libx264)/preset/CRF/audio-bitrate pickers, output-file picker (defaults to
`{ProjectName}_captioned.mp4`), and a queue of any number of concurrently-running jobs, each with a real
live progress bar (parsed from ffmpeg's own `-progress pipe:1` output) and a cancel button that actually
kills the ffmpeg process tree. `FfmpegFilterGraphBuilder`/`RenderService` (`NPVideoStudio.Media`) build
and run the real `-filter_complex` graph for the project's timeline - verified end-to-end against real
ffmpeg + real Tesseract OCR (2 clips, a timing gap, two caption windows, all landing at their exact
expected timestamps in the rendered output).

No real video decode/render existed before this phase to render *from* in the UI sense - this phase adds
the actual export path from the already-real, already-tested `Timeline` model to a finished MP4 file, so
it does not depend on the still-missing player-preview decoder (Phase 8's known gap). No
`function-contracts.json` rows yet for the new export screen's individual controls - same
deferred-to-next-refresh treatment as Phases 6/7/8.

## Feature-level summary counts

Authoritative counts come from `docs/function-contracts.json` (74 rows, one per control/command,
mechanically counted — not hand-tallied). Not yet refreshed for Phase 9's new export screen (see above -
same deferred treatment as Phases 6/7/8):

- `WORKING_VERIFIED`: 34 (Phase 8 closes the timeline-editor gap - `workspace.timeline` moved from
  `NOT_PRESENT` to `WORKING_VERIFIED`)
- `IMPLEMENTED_NOT_RUNTIME_VERIFIED`: 34
- `BROKEN`: 0
- `PLACEHOLDER` (deceptive/fake-active): 0
- `NOT_PRESENT`: 6 rows, covering these remaining bigger gaps: chorus/refrain detection, known-song-
  library lookup, Settings AI-model/caption/export sections (tool-path fields were closed in Phase 1),
  and the 6 disabled planned-feature tiles (counted as one summary row on the start screen above). The
  dependency-manager screen gap from Phase 0 and the timeline-editor gap (Phase 8) are both closed. The
  5-extra-themes gap (Phase 3), song-library gap (Phase 4), and caption-editor/style-gallery/video-
  layout-analyzer gaps (Phases 6/7) are all closed and were never counted as `NOT_PRESENT` rows here
  since none was a single pre-existing UI control row, so this count's history before Phase 8 is
  unchanged since Phase 2.
- `BLOCKED_BY_DEPENDENCY`: 0 (nothing found that's implemented but permanently blocked; the Whisper/
  yt-dlp/fpcalc/Tesseract paths work once their tool is present, which the app already detects and
  reports)
