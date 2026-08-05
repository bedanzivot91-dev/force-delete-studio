# NP Video Studio — orientation for future work sessions

Read this file first. Read `docs/MASTER_SPEC.md` for the full multi-phase plan and
`docs/PHASE_STATUS.md` for what phase is next. Do not re-audit the whole repo each session —
`docs/BASELINE_AUDIT.md` and `docs/FUNCTION_MATRIX.md` already capture the Phase 0 findings.

## What this is

Windows desktop video-editing app for YouTube/Shorts/TikTok/Instagram/Facebook content, built in
phases (see MASTER_SPEC). .NET 8 + Avalonia 11 + MVVM (CommunityToolkit.Mvvm), built and tested in a
Linux dev sandbox, packaged for Windows via GitHub Actions CI (`windows-latest`) because the installer
and native runtime behavior can't be verified on Linux.

## Solution layout

```
src/NPVideoStudio.App/            Avalonia UI, MVVM, DI composition root (App.axaml.cs)
src/NPVideoStudio.Domain/         Plain models: Project, MediaAsset, AppSettings, LyricMatch, etc.
src/NPVideoStudio.Core/           Service interfaces only (NPVideoStudio.Core/Services/*)
src/NPVideoStudio.Infrastructure/ SQLite persistence, auto-save, logging (Serilog)
src/NPVideoStudio.Media/          FFprobe/FFmpeg/yt-dlp process wrappers
src/NPVideoStudio.AI/             Whisper.net transcription, lyric matching, SRT writing
src/NPVideoStudio.Diagnostics/    System checks screen + support ZIP
tests/NPVideoStudio.UnitTests/    xUnit + Avalonia.Headless.XUnit
installer/NPVideoStudio.iss       Inno Setup script
scripts/                          check-dependencies.ps1, build-release.ps1
.github/workflows/windows-build.yml   CI: build, test, publish, installer, portable ZIP
THIRD_PARTY_NOTICES.md, Licenses/     Every OSS dependency actually used, real verified licenses (Phase 11)
test-data/local/                  Gitignored - the user's own regression clip goes here, never committed
```

## Commands

```
dotnet build NPVideoStudio.sln -c Release
dotnet test tests/NPVideoStudio.UnitTests/NPVideoStudio.UnitTests.csproj -c Release --filter "FullyQualifiedName!~IntegrationTests"
```
Run the filtered command during normal dev — the sandbox's proxy blocks `huggingface.co`, so the 4
Whisper-model integration tests (real download + transcription) only pass on GitHub Actions CI, which
has full internet. Don't "fix" them locally; verify via CI job logs instead.

## Real, verified constraints (don't re-derive these)

- **Sandbox network**: `github.com`/`api.nuget.org` reachable; `huggingface.co` and (presumably)
  `youtube.com` blocked by the proxy. yt-dlp/Whisper-model-download code paths are verified on CI only.
- **No bundled tools, still true**: `ffmpeg`, `ffprobe`, `yt-dlp`, `fpcalc` (Chromaprint), `tesseract` are
  all resolved via `FfmpegLocator`/`DependencyManagerService` (override path → `Tools/<name>/` next to the
  exe → PATH). None are bundled in `Tools/` — users need them on PATH or via
  `scripts/check-dependencies.ps1`. This is a real, deliberate gap (see `THIRD_PARTY_NOTICES.md` for why
  it also keeps the licensing story simple — none of these GPL/LGPL-licensed tools are redistributed by
  this app). The only things actually bundled in the publish output are Whisper.net + its native
  whisper.cpp binaries (`whisper.dll`, `ggml-*.dll`) — both MIT.
- **Whisper model** is downloaded on demand (~75 MB tiny model) after an explicit button click — never
  pre-bundled, by design (consent-gated download).
- **Version is fixed via `Directory.Build.props`** (`<Version>0.1.0</Version>`, single source of truth for
  every project) and matches `installer/NPVideoStudio.iss`'s `MyAppVersion` — bump both by hand together.
  The old "compiled assembly reports SDK default 1.0.0.0" bug from Phase 0 is resolved.
- **Portable ZIP-in-ZIP is fixed**: `scripts/build-release.ps1` still produces the real
  `dist/NPVideoStudio-Portable-x64-<version>.zip` for manual releases, but the CI workflow's "Upload
  portable build" step uploads the *extracted* `dist/NPVideoStudio-Portable-x64/` folder instead of that
  zip — GitHub Actions' own artifact-zipping no longer wraps an already-zipped file.
- **PDB/non-Windows-runtime trimming is fixed**: `build-release.ps1` step "3/5" deletes all `*.pdb` files
  and every `runtimes/<rid>` folder except `win-x64` before packaging — confirmed empty of `.pdb`/non-
  win-x64 entries in real CI compression logs.
- **Timeline (Phase 8), render pipeline (Phase 9), quick-video/templates (Phase 10), song-fingerprinting
  (Phase 4), and OCR (Phase 7) all exist and are real, tested features now** — this file used to say they
  didn't; that was true only through Phase 7. All 8 themes exist (Dark Cinematic, Minimal Light,
  Professional Studio, Obsidian Neon, Arctic Glass, Crimson Cyber, Midnight Pro, Ocean Glass), verified by
  `AppSmokeTests.cs: AllEightThemes_LoadAsRealAvaloniaResourceDictionaries`. Real remaining gaps: no
  multi-video-track compositing/ImageOverlay rendering, no font/effects management (see
  `docs/PHASE_STATUS.md`'s Phase 9/10 sections for the exact, current list — read those instead of
  assuming anything here is still missing).

## Conventions already established in this codebase

- Every user-facing string is Serbian (Latin script). Never introduce English UI text.
- Services take an optional override path in their constructor and resolve via `FfmpegLocator`/
  settings, never a hardcoded path (spec-style rule already enforced).
- New Whisper-dependent services should go through `WhisperTranscriber` (shared model download +
  transcription), not duplicate that logic — see `LyricSearchService` and `SubtitleGeneratorService`
  for the pattern.
- Pure/testable logic is extracted out of services that need real processes or network (e.g.
  `LyricMatcher`, `SrtWriter`, `YouTubeDownloadHelpers`) specifically so it can be unit tested without
  ffmpeg/yt-dlp/Whisper actually running.
- Two xUnit test classes that both download the shared Whisper model must share the
  `[Collection("Whisper model tests")]` attribute (see `WhisperModelCollection.cs`) — otherwise xUnit
  runs them in parallel and they race on the same model file (real CI failure, already hit once).
- Disabled/planned features are shown as disabled tiles with "Uskoro — u razvoju", never as a live-
  looking button with no effect (`StartScreenViewModel.PlannedFeatures`).
