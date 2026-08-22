# Baseline Audit — Phase 0

Date: 2026-08-02. Commit at audit time: `dbd18de` (branch `claude/np-video-studio-desktop-po139q`).
Working tree was clean (`git status --short` empty) before this audit; no behavior was changed while
producing this document, only new `docs/*` and `CLAUDE.md` files were added.

## 0. Source availability

Verified present: `NPVideoStudio.sln`, 8 `.csproj` files, real `.cs` sources under `src/` and `tests/`,
Avalonia `.axaml` views under `src/NPVideoStudio.App/Views` and `Themes`. This is a source checkout,
not a compiled-only distribution — proceeding with audit as instructed.

## 1. Build result (real, just run)

```
dotnet build NPVideoStudio.sln -c Release
```
Result: **Build succeeded. 0 Warning(s). 0 Error(s).** All 8 projects compiled (Domain, Core, Media,
Infrastructure, Diagnostics, AI, App, UnitTests).

## 2. Test result (real, just run)

```
dotnet test tests/NPVideoStudio.UnitTests/NPVideoStudio.UnitTests.csproj -c Release --filter "FullyQualifiedName!~IntegrationTests"
```
Result: **Passed! Failed: 0, Passed: 66, Skipped: 0, Total: 66.**

4 additional tests (`LyricSearchServiceIntegrationTests` × 3, `SubtitleGeneratorServiceIntegrationTests`
× 1) require downloading the real Whisper model from `huggingface.co`, which this sandbox's outbound
proxy blocks. They are excluded above and were last verified passing for real on GitHub Actions CI
(Windows runner, commit `dbd18de`, run `30748256836`): `Passed! - Failed: 0, Passed: 70, Skipped: 0,
Total: 70`. So the true total is **70/70 passing**, split 66 local + 4 CI-only-verified.

## 3. Projects (8)

`NPVideoStudio.Domain`, `NPVideoStudio.Core`, `NPVideoStudio.Media`, `NPVideoStudio.Infrastructure`,
`NPVideoStudio.AI`, `NPVideoStudio.Diagnostics`, `NPVideoStudio.App`, `NPVideoStudio.UnitTests`.
No project has an explicit `<Version>` set (see finding 3 below).

## 4. View / ViewModel inventory

10 `.axaml` views, 16 ViewModel `.cs` files. Every `Button`/`ToggleButton` with a `Command=` binding in
every `.axaml` file under `src/NPVideoStudio.App/Views` was cross-checked against a matching
`[RelayCommand]`-generated command in its `x:DataType` ViewModel — **no orphaned bindings found** (no
button bound to a command that doesn't exist, and grep found no `Button` without either a `Command=` or
`IsEnabled="False"` + explicit "planned feature" styling). Full list in `FUNCTION_MATRIX.md`.

## 5. Placeholder / dead-code scan (real grep results, not assumed)

- `NotImplementedException`: **0 matches** in `src/`.
- Empty `catch (...) { }` blocks: **0 matches** in `src/`.
- `async void` outside a UI event handler: **0 matches** — the only `async void` in the codebase is
  `WorkspaceView.axaml.cs: OnDrop(...)`, a legitimate drag-and-drop event handler.
- `TODO`/`FIXME`: **0 matches** in `src/`.
- Text "Uskoro" / "u razvoju" appears in exactly two places, both honest disclosures, not fake active
  buttons:
  1. `WorkspaceView.axaml` — a static text label stating the timeline is in development (media
     library/import above it are fully functional and correctly not covered by this label).
  2. `StartScreenViewModel.PlannedFeatures` — 6 tiles rendered with `IsEnabled="False"` and a visible
     "Uskoro — u razvoju" subtitle: *Kreiraj video iz šablona*, *Brzi video od slike i pesme*,
     *Automatski video sa utisnutim titlovima (na slici)*, *Upravljanje šablonima*, *Upravljanje
     fontovima*, *Upravljanje efektima*. These satisfy the "must not look active" rule already (real
     `IsEnabled="False"`, not just visual styling).

**Conclusion: no `BROKEN` or fake-`PLACEHOLDER` UI found in the current build.** The gaps are entirely
*absent* features (timeline, render, song fingerprinting, OCR, 5 extra themes, dependency-manager
screen, song library) rather than broken or deceptive existing ones.

## 6. Master-prompt "known problems" — verified one by one

| # | Claim | Verified? | Evidence |
|---|---|---|---|
| 1 | Portable lacks `Tools/ffmpeg.exe`, `Tools/ffprobe.exe`, `Tools/yt-dlp.exe` | **True** | No `Tools/` directory anywhere in the repo; `FfmpegLocator` falls back to PATH. `scripts/check-dependencies.ps1` installs via winget instead. |
| 2 | No pre-fetched Whisper model | **True, by design** | `WhisperTranscriber.DownloadModelAsync` only runs after an explicit UI button click (consent-gated); never bundled. |
| 3 | No faster-whisper/WhisperX/Demucs/Chromaprint/OCR/caption pipeline | **True** | None of these are referenced anywhere in `src/`. |
| 4 | Whisper tiny model only | **True** | `WhisperTranscriber` hardcodes `GgmlType.Tiny`. |
| 5 | Highlight tool is loudness-only, not real chorus detection | **True, and disclosed** | `SongHighlightService` uses only ffmpeg `astats` RMS loudness; the UI/README already call it a heuristic, not a chorus detector. |
| 6 | Timeline is a placeholder | **True, and disclosed** | `WorkspaceView` has no track/clip editing; the in-development label is present and accurate. |
| 7 | Only 3 themes exist | **True** | `Themes/DarkCinematic.axaml`, `MinimalLight.axaml`, `ProfessionalStudio.axaml` — exactly 3, no more. |
| 8 | Portable is "ZIP inside ZIP" | **True, root cause identified** | `build-release.ps1` makes one flat `dist/NPVideoStudio-Portable.zip`. The CI workflow uploads that single file via `actions/upload-artifact`, which itself always zips artifact contents for download — so the artifact a user downloads from the Actions UI is a zip containing the already-zipped portable build. The build script itself is not at fault; the workflow's artifact upload step is. |
| 9 | Release ships PDBs and non-Windows Whisper native libs | **True** | Confirmed directly in CI compression logs (run `30743419577`): `NPVideoStudio.*.pdb` files and `runtimes/linux-arm`, `linux-arm64`, `linux-x64`, `macos-arm64`, `macos-x64`, `win-arm64`, `win-x86` all present in the win-x64 publish output alongside the win-x64 native libs actually needed. |
| 10 | Version mismatch 0.1.0 vs 1.0.0 | **True** | `installer/NPVideoStudio.iss` hardcodes `MyAppVersion "0.1.0"`. No `.csproj` sets `<Version>` or `<AssemblyVersion>`, so the compiled `NPVideoStudio.App` assembly gets the .NET SDK's implicit default, `1.0.0.0`. `App.axaml.cs: ThisAssemblyVersion()` reads that assembly version at runtime (falling back to the string literal `"0.1.0"` only if the reflection call returns null, which it won't) — so the running app actually reports `1.0.0.0` in its startup log, not `0.1.0`. |

All 10 claims in the master prompt's "known problems" section were verified true against the actual
source and CI output — none were rejected, but none were taken on faith either.

## 7. Packaging config as it exists today

- `scripts/build-release.ps1`: self-contained `dotnet publish -r win-x64`, then `Compress-Archive` into
  one flat zip, then optional Inno Setup compile if `ISCC.exe` is on PATH.
- `installer/NPVideoStudio.iss`: copies `..\publish\win-x64\*` recursively, English-only wizard chrome
  (Serbian `.isl` isn't in the Chocolatey Inno Setup package — documented in-file), optional
  desktop-icon and `.npvsproject` file-association tasks, no admin required (`PrivilegesRequired=lowest`).
- `.github/workflows/windows-build.yml`: installs FFmpeg + yt-dlp via Chocolatey, restores, builds,
  tests, installs Inno Setup, runs `build-release.ps1`, uploads both the installer exe and the portable
  zip as separate `upload-artifact` artifacts (see finding 8 above for the nesting this causes on the
  portable one).

## 8. Settings UI vs. `AppSettings` model

`AppSettings` already has `FfmpegPath`, `FfprobePath`, `YtDlpPath` fields, but `SettingsView.axaml` /
`SettingsViewModel` do not expose any of them — they're only settable by hand-editing
`settings.json`. Confirmed by reading both files; this matches the master prompt's note that these
fields may exist in the model without UI wiring.

## 9. What was deliberately NOT done this phase

Per the master prompt's Phase 0 scope: no new features, no theme additions, no AI model installs, no
architecture changes. This document and its companions (`FUNCTION_MATRIX.md`,
`function-contracts.json`, `ARCHITECTURE.md`, `MASTER_SPEC.md`, `PHASE_STATUS.md`, `CLAUDE.md`) are the
only changes made in this phase.
