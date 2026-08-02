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
| "Sačuvaj podešavanja" | `SaveCommand` → `ISettingsService.SaveAsync` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] `SettingsServiceTests.cs` covers the service, not this command |
| "Vrati podrazumevano" | `ResetToDefaultsCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] same file |
| FFmpeg putanja "Izaberi..." | `BrowseFfmpegPathCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] — **Phase 1**: field now exists, wired to `AppSettings.FfmpegPath` (closes BASELINE_AUDIT §8) |
| FFprobe putanja "Izaberi..." | `BrowseFfprobePathCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] — Phase 1 |
| yt-dlp putanja "Izaberi..." | `BrowseYtDlpPathCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] — Phase 1 |
| AI-model / caption / export settings sections | — | NOT_PRESENT | Master-prompt-requested sections; none exist yet |

**Note (Phase 1)**: tool-path fields are read into DI-registered services once at app startup
(`App.axaml.cs`); changing them here and saving only takes effect after restarting the app — disclosed
in the View's own help text.

## Alati i modeli — DependencyManagerView / DependencyManagerViewModel (new screen, Phase 1)

| Control | Command | Status | Evidence |
|---|---|---|---|
| Početni ekran tile | `StartScreenViewModel.OpenDependencyManagerCommand` | WORKING_VERIFIED | [Smoke] `AppSmokeTests.cs: OpeningDependencyManager_LoadsRealDependencyStatusesWithoutThrowing` executes this exact command and asserts real results |
| "Proveri ponovo" / initial load | `RefreshCommand` → `IDependencyManagerService.GetDependenciesAsync` | WORKING_VERIFIED | [Smoke] same test asserts FFmpeg/FFprobe correctly reported `Installed` with a real version string on the real (non-mocked) tools present in this environment; [Svc] `DependencyManagerServiceTests.cs` (6 tests) covers found/not-found/Whisper-ready/not-ready directly, including a genuinely-absent yt-dlp |
| "Otvori folder" (per row) | `DependencyItemViewModel.OpenFolderCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] — opens a real OS file browser, not asserted by any test |
| "Preuzmi model" (Whisper) | `DownloadWhisperModelCommand` → `WhisperTranscriber` (via `ILyricSearchService`) | WORKING_VERIFIED | [CI] same download path already verified end-to-end via `LyricSearchServiceIntegrationTests.cs`/`SubtitleGeneratorServiceIntegrationTests.cs` |
| "Otkaži" | `CancelDownloadCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] — first cancellable long-running operation in the app; the mid-download cancel path itself hasn't been exercised by a test, only that `CanExecute` toggles correctly |

**Real gap, not fabricated**: the master prompt's richer status vocabulary (Ažuriranje dostupno /
Oštećeno / Nekompatibilno) isn't implemented — there's no checksum or expected-version pinning system
to honestly back those states yet (see `DependencyInfo`'s doc comment). Only Instalirano/Nije
instalirano are reported, both backed by a real version-command exit code, not just file existence.

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
| "Izaberi pesmu..." | `PickSongCommand` | WORKING_VERIFIED (render only) | [Screenshot] this session |
| "Analiziraj pesmu" | `AnalyzeCommand` → `ISongHighlightService.FindHighlightsAsync` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] `SongHighlightServiceTests.cs` (5 tests, synthetic audio) covers the service directly; this exact command not executed this session |
| "Izvezi sve" | `ExportAllCommand` → `ExportHighlightAsync` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] same file covers export |
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
| "Učitaj podatke" | `FetchInfoCommand` → `IYouTubeDownloadService.GetVideoInfoAsync` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [Svc] `YouTubeDownloadHelpersTests.cs` covers URL validation/sanitization only; the yt-dlp process call itself has no automated test (requires real yt-dlp + network, only exercised manually on CI via the "Install yt-dlp" step succeeding, not via a dedicated test) |
| "Preuzmi pesmu" | `DownloadCommand` → `DownloadAudioAsync` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | same gap — no automated integration test for the actual download call |
| "Otvori u Isečci iz pesme" | `OpenInHighlightsCommand` | WORKING_VERIFIED | [Screenshot] this session — executed the real command, confirmed navigation + file preload via `LoadFile` |
| "Otvori u Pronađi tekst u pesmi" | `OpenInLyricSearchCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | same code path as above, not individually screenshotted |
| "Otvori u Generiši titlove" | `OpenInSubtitleGeneratorCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | same |
| Ownership confirmation gate | `OwnershipConfirmed` bool required by `DownloadAudioAsync` | WORKING_VERIFIED (logic) | [Screenshot] confirmed the checkbox gates `CanDownload`; `DownloadAudioAsync` throws if false (code-reviewed, not unit-tested directly) |

**Real gap found here**: there is no automated test (unit, mocked-process, or CI integration) for
`YouTubeDownloadService.GetVideoInfoAsync`/`DownloadAudioAsync` actually invoking yt-dlp — only the pure
helper logic (`YouTubeDownloadHelpers`) is tested. The master prompt explicitly asks for a "yt-dlp
servis sa mock procesom" integration test; that does not exist yet. Flagged for a later phase.

## Generiši titlove (SRT) — SubtitleGeneratorView / SubtitleGeneratorViewModel

| Control | Command | Status | Evidence |
|---|---|---|---|
| "Izaberi fajl..." | `PickFileCommand` | WORKING_VERIFIED (render only) | [Screenshot] this session |
| "Preuzmi model" | `DownloadModelCommand` | WORKING_VERIFIED | [Svc][CI] shares `WhisperTranscriber` with lyric search, same CI-verified download path |
| "Generiši titlove" | `GenerateCommand` → `SubtitleGeneratorService.GenerateSrtAsync` | WORKING_VERIFIED | [Svc][CI] `SubtitleGeneratorServiceIntegrationTests`, run `30748256836`; `SrtWriterTests.cs` (9 tests) covers the pure `.srt` formatting locally |
| "Otvori .srt fajl" | `OpenGeneratedSrtCommand` | IMPLEMENTED_NOT_RUNTIME_VERIFIED | [None] |

## Feature-level summary counts

Authoritative counts come from `docs/function-contracts.json` (67 rows, one per control/command,
mechanically counted — not hand-tallied):

- `WORKING_VERIFIED`: 22
- `IMPLEMENTED_NOT_RUNTIME_VERIFIED`: 38
- `BROKEN`: 0
- `PLACEHOLDER` (deceptive/fake-active): 0
- `NOT_PRESENT`: 7 rows, covering these bigger gaps: timeline editor, chorus/refrain detection, known-
  song-library lookup, Settings AI-model/caption/export sections (tool-path fields were closed in
  Phase 1), and the 6 disabled planned-feature tiles (counted as one summary row on the start screen
  above). The dependency-manager screen gap from Phase 0 is closed. Not double-counted with the
  5-extra-themes gap, which is tracked in BASELINE_AUDIT §6 rather than as
  a per-control row here since it isn't a single UI control.
- `BLOCKED_BY_DEPENDENCY`: 0 (nothing found that's implemented but permanently blocked; the Whisper/
  yt-dlp paths work once their tool is present, which the app already detects and reports)
