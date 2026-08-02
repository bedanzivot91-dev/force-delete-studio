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
- **No bundled tools**: `ffmpeg`, `ffprobe`, `yt-dlp` are resolved via `FfmpegLocator` (override path →
  `Tools/<name>/` next to the exe → PATH). None are actually bundled in `Tools/` today — Portable users
  need them on PATH or via `scripts/check-dependencies.ps1`. This is a real gap, not yet fixed.
- **Whisper model** is downloaded on demand (~75 MB tiny model) after an explicit button click — never
  pre-bundled, by design (consent-gated download).
- **Version mismatch is real**: `installer/NPVideoStudio.iss` says `0.1.0`, but no `.csproj` sets
  `<Version>`/`<AssemblyVersion>`, so the compiled assembly reports the SDK default `1.0.0.0`
  (`App.axaml.cs: ThisAssemblyVersion()`). Not fixed yet — tracked in BASELINE_AUDIT.
- **Portable ZIP-in-ZIP is real but the cause is GitHub Actions, not the build script**:
  `build-release.ps1` produces one flat `dist/NPVideoStudio-Portable.zip`. The workflow then uploads
  that single file as an `upload-artifact` artifact, and GitHub Actions always wraps artifact contents
  in its own zip for download — so a user downloading from the Actions UI gets a zip containing another
  zip. Fix (not yet done): upload the extracted publish folder as the artifact instead of the pre-zipped
  file, or accept the nesting and document it.
- **Release includes PDBs and non-Windows Whisper native libs**: confirmed in CI compression logs —
  `linux-*`, `macos-*`, `win-arm64`, `win-x86` runtime folders and `.pdb` files all ship in the win-x64
  publish output. Not yet trimmed.
- **Timeline/render/caption-burn-in/song-fingerprinting/OCR do not exist yet.** Only 3 themes exist
  (Dark Cinematic, Minimal Light, Professional Studio), not 8. These are the biggest remaining gaps —
  see MASTER_SPEC phases 3–9.

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
