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
| FFmpeg, FFprobe (gyan.dev "essentials" Windows build, `ffmpeg.exe`/`ffprobe.exe`) | GPLv3 | Video/audio processing - downloaded and copied into `Tools/ffmpeg/` by `scripts/build-release.ps1` at build time (not committed to this repo), so both the portable ZIP and the installer work without a separate install |
| yt-dlp (`yt-dlp.exe`) | The Unlicense (public domain) | Downloading the user's own YouTube audio - downloaded and copied into `Tools/yt-dlp/` by `scripts/build-release.ps1` the same way |
| LibVLCSharp, LibVLCSharp.Avalonia | LGPL-2.1-or-later | .NET/Avalonia bindings for real, continuous audio+video playback ("Pravi plejer sa zvukom" in the workspace) |
| VideoLAN.LibVLC.Windows (native `libvlc.dll`/`libvlccore.dll` + codec/demux plugins, win-x64 only - see `VlcWindowsX86Enabled=false` in `NPVideoStudio.App.csproj`) | LGPL-2.1-or-later | The actual VLC playback engine LibVLCSharp calls into - dynamically loaded at runtime (LGPL's linking requirement is satisfied: this app calls it through LibVLCSharp's P/Invoke layer, never statically links or modifies it), ~100MB bundled since it ships its own decoder/demuxer plugin set |

Test-only packages (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`,
`Avalonia.Headless.XUnit`) are not listed above - they never ship in the built application, only in the
test project.

**Corresponding source for the bundled GPLv3 FFmpeg/FFprobe binaries**: the exact source used for
gyan.dev's builds is the official FFmpeg git repository, https://github.com/FFmpeg/FFmpeg - the full
GPLv3 text is in `Licenses/GPLv3-FFmpeg.txt` (fetched from that same repository). `scripts/build-release.ps1`
always pulls the current `ffmpeg-release-essentials.zip` from https://www.gyan.dev/ffmpeg/builds/, which
is itself the build gyan.dev publishes as corresponding to a specific tagged FFmpeg source release -
see that page for the exact version/commit pairing at build time.

## External prerequisites (not bundled, not redistributed by this app)

| Tool/library | License | How it's used | Verified via |
|---|---|---|---|
| Chromaprint (`fpcalc`) | Library: LGPL-2.1 / MIT; the `fpcalc` command-line binary: GPLv2+ | Audio fingerprinting for song recognition, subprocess | github.com/acoustid/chromaprint |
| Tesseract OCR + tessdata | Apache License 2.0 | On-screen text detection ("Analiza rasporeda videa"), subprocess | github.com/tesseract-ocr/tesseract, github.com/tesseract-ocr/tessdata |
| EasyOCR (+ PyTorch, its inference backend) | Apache License 2.0 (EasyOCR); PyTorch: BSD-3-Clause | Optional, better-on-stylized-text alternative for the same on-screen text detection, via `easyocr-helper/ocr_frame.py`, Python import | `pip show easyocr` license field; verified directly - reads decorative/colored caption text real Tesseract cannot |
| faster-whisper | MIT | Optional local AI worker engine (Balanced/MostAccurate profiles), Python import | github.com/SYSTRAN/faster-whisper |
| CTranslate2 (faster-whisper's inference backend) | MIT | Same as above, transitive dependency | github.com/OpenNMT/CTranslate2 |
| WhisperX | BSD 2-Clause | Optional local AI worker engine (word-level alignment) | github.com/m-bain/whisperX/blob/main/LICENSE |
| Demucs | MIT | Optional local AI worker engine (vocal/instrumental separation) | github.com/facebookresearch/demucs/blob/main/LICENSE |

FFmpeg and yt-dlp moved from this table to "Bundled" above - `fpcalc` and Tesseract remain external,
user-installed prerequisites for now (see `CLAUDE.md` and `docs/PHASE_STATUS.md` for why - bundling them
is a real, disclosed gap, not an oversight: Tesseract in particular ships as a Windows installer rather
than a simple portable ZIP, which `scripts/build-release.ps1` doesn't yet automate extracting from).
None of the tools in this table are copied into `publish/win-x64/`, the installer, or the portable ZIP -
`Tools/ai-worker/requirements.txt` only lists package *names* for the user's own `pip install`, and
`FfmpegLocator`/`DependencyManagerService` only ever resolve a path to an executable the user already has
on their machine.

## Notes on GPL/LGPL components above

FFmpeg (GPLv3) is now bundled (see above) - shipped as unmodified upstream binaries with the complete
GPLv3 license text (`Licenses/GPLv3-FFmpeg.txt`) and a pointer to the exact corresponding source, which
satisfies GPLv3's source-availability requirement for binary redistribution. `fpcalc` (GPLv2+) remains an
external, user-installed prerequisite this app never bundles, links against, or redistributes - only
invoked as a separate subprocess via its own already-installed executable, the standard "mere aggregation
via subprocess" pattern that doesn't extend GPL's terms onto this application's own code.
