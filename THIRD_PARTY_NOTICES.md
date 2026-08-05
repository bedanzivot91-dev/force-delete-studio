# Third-Party Notices

NP Video Studio uses open-source components. This file lists every one actually used by this codebase
(cross-checked against every `PackageReference` in `src/*/*.csproj`, `Tools/ai-worker/requirements.txt`,
and every external command-line tool `FfmpegLocator`/`DependencyManagerService` invoke), split into two
groups with very different obligations:

- **Bundled** components ship inside the installer/portable build (their files are literally copied into
  `publish/win-x64/`) - their license terms apply to this distribution, and full license text is in
  `Licenses/`.
- **External** components are never bundled - the user installs them separately (via
  `scripts/check-dependencies.ps1`, Chocolatey, or `pip install -r requirements.txt`), and this app only
  invokes them as a subprocess or Python import at runtime. Listed here for transparency/attribution, not
  because distributing this app carries their redistribution terms.

**No AGPL component is used anywhere in this stack** (verified per-component below, not assumed).

## Bundled (license text in `Licenses/`)

| Component | License | Used for |
|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Diagnostics, Avalonia.Themes.Fluent | MIT | UI framework |
| CommunityToolkit.Mvvm | MIT | MVVM (`[ObservableProperty]`/`[RelayCommand]`) |
| Microsoft.Data.Sqlite | MIT | Project/song-library database access |
| SQLite (native library, bundled transitively by Microsoft.Data.Sqlite) | Public Domain | The actual database engine |
| Microsoft.Extensions.DependencyInjection | MIT | Composition root (`App.axaml.cs`) |
| Serilog, Serilog.Sinks.File | Apache License 2.0 | Application logging |
| Whisper.net, Whisper.net.Runtime | MIT | .NET bindings for local speech recognition |
| whisper.cpp (native library wrapped by Whisper.net.Runtime - `whisper.dll`, `ggml-*.dll`) | MIT | The actual Whisper inference engine |

Test-only packages (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`,
`Avalonia.Headless.XUnit`) are not listed above - they never ship in the built application, only in the
test project.

## External prerequisites (not bundled, not redistributed by this app)

| Tool/library | License | How it's used | Verified via |
|---|---|---|---|
| FFmpeg (gyan.dev "essentials" Windows build) | GPLv3 | Video/audio processing, subprocess (`ffmpeg.exe`/`ffprobe.exe`) | gyan.dev build page |
| yt-dlp | The Unlicense (public domain) | Downloading the user's own YouTube audio, subprocess | github.com/yt-dlp/yt-dlp/blob/master/LICENSE |
| Chromaprint (`fpcalc`) | Library: LGPL-2.1 / MIT; the `fpcalc` command-line binary: GPLv2+ | Audio fingerprinting for song recognition, subprocess | github.com/acoustid/chromaprint |
| Tesseract OCR + tessdata | Apache License 2.0 | On-screen text detection ("Analiza rasporeda videa"), subprocess | github.com/tesseract-ocr/tesseract, github.com/tesseract-ocr/tessdata |
| faster-whisper | MIT | Optional local AI worker engine (Balanced/MostAccurate profiles), Python import | github.com/SYSTRAN/faster-whisper |
| CTranslate2 (faster-whisper's inference backend) | MIT | Same as above, transitive dependency | github.com/OpenNMT/CTranslate2 |
| WhisperX | BSD 2-Clause | Optional local AI worker engine (word-level alignment) | github.com/m-bain/whisperX/blob/main/LICENSE |
| Demucs | MIT | Optional local AI worker engine (vocal/instrumental separation) | github.com/facebookresearch/demucs/blob/main/LICENSE |

None of the tools in this table are copied into `publish/win-x64/`, the installer, or the portable ZIP -
`Tools/ai-worker/requirements.txt` only lists package *names* for the user's own `pip install`, and
`FfmpegLocator`/`DependencyManagerService` only ever resolve a path to an executable the user already has
on their machine (see `CLAUDE.md`: "No bundled tools" is a real, current constraint, not an oversight).

## Notes on GPL/LGPL components above

FFmpeg (GPLv3) and `fpcalc` (GPLv2+) are the only copyleft-licensed tools this app depends on, and both
are external, user-installed prerequisites this app never bundles, links against, or redistributes - only
invoked as separate subprocesses via their own already-installed executable. This is the standard "mere
aggregation via subprocess" pattern that does not extend GPL's terms onto this application's own code or
create a distribution obligation on this project for those binaries.
