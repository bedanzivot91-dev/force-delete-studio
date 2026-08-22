# Architecture — as it actually exists (Phase 0)

## Projects and their real responsibilities

```
NPVideoStudio.Domain          Plain data models, no logic beyond simple defaults/computed properties.
                               Project, MediaAsset, ProjectFormat, AppSettings, LyricMatch,
                               SongHighlight, YouTubeVideoInfo, RecentProjectEntry, Enums (TargetPlatform,
                               AppTheme, DiagnosticStatus lives in Core instead - see below).

NPVideoStudio.Core             Service *interfaces* only, plus DiagnosticCheckResult/DiagnosticStatus.
                               No implementations live here - this project has zero external
                               dependencies beyond Domain, by design, so anything can reference it.

NPVideoStudio.Infrastructure   Real implementations of persistence-and-lifecycle interfaces:
                               ProjectRepository, RecentProjectsService, AutoSaveService,
                               SettingsService (all backed by AppDatabase / Microsoft.Data.Sqlite,
                               Pooling=false to avoid a real Windows file-lock bug that was hit and
                               fixed), plus AppLogging (Serilog setup).

NPVideoStudio.Media            Process-wrapping services that shell out to ffmpeg/ffprobe/yt-dlp:
                               FfprobeService (IMediaProbeService), FfmpegLocator (shared tool
                               resolution: override path -> Tools/<name>/ -> PATH),
                               SongHighlightService, YouTubeDownloadService +
                               YouTubeDownloadHelpers (pure URL-validation/filename logic, split out
                               specifically so it's unit-testable without a real process).

NPVideoStudio.AI               Whisper.net-based local speech recognition:
                               WhisperTranscriber (shared model download + WAV conversion +
                               transcription - both LyricSearchService and SubtitleGeneratorService
                               delegate to this instead of duplicating it), LyricMatcher (pure
                               phrase-matching logic, no Whisper dependency, unit-testable),
                               SrtWriter (pure .srt formatting), LyricSearchService,
                               SubtitleGeneratorService.

NPVideoStudio.Diagnostics      DiagnosticsService: real runtime checks (.NET version, FFmpeg/FFprobe/
                               yt-dlp presence+version, folder write access, disk space, SQLite
                               integrity, settings.json validity) with auto-fix for the safe subset
                               (create missing folders, reset corrupt settings file).

NPVideoStudio.App              Avalonia UI: Views (.axaml) + ViewModels (MVVM,
                               CommunityToolkit.Mvvm [ObservableProperty]/[RelayCommand]) + the DI
                               composition root (App.axaml.cs) + Themes (3 ResourceDictionary files) +
                               Services/StorageService (Avalonia file/folder picker wrapper).

NPVideoStudio.UnitTests        xUnit + Avalonia.Headless.XUnit. References every other project.
```

No `NPVideoStudio.Rendering`, `NPVideoStudio.Captions`, `NPVideoStudio.SongRecognition`, or
`NPVideoStudio.AI.Worker` project exists. The master-prompt's proposed module layout for those phases
has not been created yet — that's Phase 5+ work, not Phase 0.

## Dependency direction (verified from actual `.csproj` ProjectReferences, not assumed)

```
Domain  <-  Core  <-  Infrastructure
                   <-  Media
                   <-  AI (also -> Domain, Media for FfmpegLocator/WhisperTranscriber ffmpeg conversion)
                   <-  Diagnostics (also -> Infrastructure for AppDatabase, Media for FfmpegLocator)
                   <-  App (also -> Infrastructure, Media, AI, Diagnostics directly)
```
Core has no implementation dependencies — every concrete service lives in Infrastructure, Media, AI, or
App, and is wired together only in `App.axaml.cs`'s composition root. This means the interfaces in Core
could be mocked/faked freely for testing without pulling in ffmpeg or Whisper, though the current test
suite mostly exercises real implementations rather than mocks (per the master prompt's own preference
for real fixtures over mocks for pipeline-critical code).

## Composition root (`App.axaml.cs: OnFrameworkInitializationCompleted`)

Registration order, singleton unless noted:
1. `SettingsService` (loaded synchronously via `Task.Run(...).GetAwaiter().GetResult()` before anything
   else, because later registrations read `settingsService.Current.*` paths eagerly).
2. Logger (Serilog), wired to `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`.
3. `AppDatabase`, `IProjectRepository`, `IRecentProjectsService`, `IAutoSaveService`,
   `IMediaProbeService` (constructed with `settingsService.Current.FfprobePath`),
   `IDiagnosticsService`, `IStorageService` (closure captures the main window lazily via
   `(Current as App)?.MainWindowRef` so it doesn't need the window to exist yet at registration time).
4. `ISongHighlightService`, `ILyricSearchService`, `IYouTubeDownloadService`, `ISubtitleGeneratorService`
   — all singletons, all constructed with explicit `FfmpegPath`/`YtDlpPath` overrides from settings
   rather than relying on ambient PATH lookup inside the DI factory.
5. ViewModels: `StartScreenViewModel`, `SettingsViewModel`, `DiagnosticsViewModel`,
   `SongHighlightsViewModel`, `LyricSearchViewModel`, `YouTubeDownloadViewModel`,
   `SubtitleGeneratorViewModel` are transient (a fresh instance per navigation); `MainWindowViewModel`
   is the single singleton root.

## Navigation model

There is no router/frame stack — `MainWindowViewModel.CurrentPage` (a `ViewModelBase?`) is swapped
directly, and `MainWindow.axaml`'s `DataTemplate`s pick the matching View by ViewModel type. Cross-tool
hand-off (e.g. YouTube download -> highlight cutter with the downloaded file preloaded) is done via
C# events on the source ViewModel (`OpenInHighlightsRequested`, etc.) that `MainWindowViewModel`
subscribes to when it constructs that page, then calls a public `LoadFile(path)` method on the
destination ViewModel before swapping `CurrentPage`. This is a real, working pattern already used by
3 tool screens - see `MainWindowViewModel.CreateYouTubeDownloadPage()`.

## Persistence

- Projects: JSON-serialized `.npvsproject` files via `ProjectRepository`, atomic write (temp file +
  rename), with a `Backups` subfolder next to the project file.
- App state: single SQLite file at `%LocalAppData%\NP Video Studio\npvideostudio.db` (recent projects
  list). `Pooling=false` in the connection string - a real fix for a Windows-only file-lock bug found
  via CI, not a defensive default.
- Settings: `%LocalAppData%\NP Video Studio\settings.json`, plain JSON, no migration system yet (the
  master prompt's Phase-4 "SQLite migrations" requirement doesn't apply to the current schema-less
  settings file, but will matter once "Moje pesme" is added as a real table).
- Auto-save: separate folder, polled by `AutoSaveService.Start(() => CurrentProject)`.

## Theming

3 `ResourceDictionary` files under `Themes/`, loaded by `App.axaml.cs: ApplyTheme(AppTheme)` via
`avares://NPVideoStudio/Themes/{name}.axaml` (note: the URI authority is the *assembly name*
`NPVideoStudio`, not the project folder name `NPVideoStudio.App` - this was a real bug, already found
and fixed). Views reference theme colors exclusively through `DynamicResource` semantic keys
(`ThemeAccentBrush`, `ThemeSurfaceBrush`, etc.), not hardcoded colors - consistent with what the master
prompt asks for the 5 new themes, so extending this list should be mechanical rather than requiring a
View rewrite.

## Background work / threading

Every service method that shells out to ffmpeg/ffprobe/yt-dlp or calls Whisper.net is `async Task`,
using `Process.WaitForExitAsync` and `CancellationToken` parameters throughout (though most ViewModel
call sites do not yet wire a cancel button to that token - there is no "Cancel" UI control anywhere in
the current screens, which is a real gap relative to the master prompt's stability requirements).
`IProgress<string>` is used for coarse-grained status text (e.g. "Preuzimanje modela...") rather than
numeric percentage progress - there is no percentage progress bar anywhere yet.
