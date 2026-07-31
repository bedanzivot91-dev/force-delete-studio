# windows_build — real cross-compile verification (this session)

## Defects found and fixed

1. **No `go.mod` existed.** `go build ./...` failed with "cannot find main
   module". The previous package's `IZVEŠTAJ-STVARNE-PROVERE.txt` claimed
   "Go cross-compile Windows x64 GUI installera/launchera/deinstalera/
   prozora napretka" had been verified — without a module file, that command
   could not have succeeded as described. Fixed: `go mod init
   sunopesmestudio/windows_build`.
2. **`progress.go` and `uninstaller.go` both declared `package main` with
   their own `func main()` in the same directory (`windows_build/`).** Two
   `main()` functions in one package do not compile — confirmed by running
   `go build ./...` before the fix, which failed. These are genuinely two
   different target binaries (`INSTALACIJA_NAPREDAK.exe` invoked by
   `setup/main.go` as a subprocess, and `Deinstaliraj Suno Pesme Studio.exe`
   the standalone uninstaller). Fixed: moved each into its own package
   directory, `windows_build/progress/main.go` and
   `windows_build/uninstaller/main.go`.
3. **`launcher/main.go`'s `openAppWindow()` shelled out to
   Chrome/Edge/Brave with `--app=http://127.0.0.1:8765/`, falling back to
   the OS default browser via `rundll32 url.dll,FileProtocolHandler`.**
   This is the browser-tab / visible-localhost pattern the product spec
   explicitly forbids for the desktop build (section 6). Fixed: the UI is
   now hosted in a native window via `github.com/jchv/go-webview2` (pure
   Go, CGO-free WebView2 COM binding, MIT-licensed), no address bar, no
   dependency on a specific browser being installed, own window/taskbar
   identity, single-instance mutex + focus-existing-window behavior added.

## Commands actually run in this session, with real results

```
$ cd windows_build && go mod init sunopesmestudio/windows_build
go: creating new go.mod: module sunopesmestudio/windows_build

$ GOOS=windows GOARCH=amd64 go get github.com/jchv/go-webview2@latest
go: added github.com/jchv/go-webview2 v0.0.0-20260205173254-56598839c808
go: added github.com/jchv/go-winloader v0.0.0-20250406163304-c1995be93bd1
go: added golang.org/x/sys v0.0.0-20210218145245-beda7e5e158e

$ GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go vet ./...
(no output — clean)

$ GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -ldflags="-H windowsgui" -o launcher.exe ./launcher
$ GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -ldflags="-H windowsgui" -o setup.exe ./setup
$ GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -ldflags="-H windowsgui" -o progress.exe ./progress
$ GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -ldflags="-H windowsgui" -o uninstaller.exe ./uninstaller
(all exit 0)

$ file *.exe
launcher.exe:    PE32+ executable (GUI) x86-64, for MS Windows, 15 sections
setup.exe:       PE32+ executable (GUI) x86-64, for MS Windows, 15 sections
progress.exe:    PE32+ executable (GUI) x86-64, for MS Windows, 15 sections
uninstaller.exe: PE32+ executable (GUI) x86-64, for MS Windows, 15 sections
```

SHA-256 of this session's build (regenerated on every CI run, will differ
run to run — recorded here only as proof the above commands were actually
executed, not as a pinned release hash):

```
4e1fd70dd92cf95e892fcfe31f9b831381722b9dfa8d71ad6d84626f0e9f787a  launcher.exe
b9702adebf0171da1596c03a72931b8822cda3981b0dbfb9c23408b6fbd46359  setup.exe
cc871ccc0e97eedeaaab4af636d73d56f3627fdac319f99fe5be1154dfeea430  progress.exe
889a673cc1bde721f143bd63a9319abb237bd0a11259ac0b3bc314c70aea30c3  uninstaller.exe
```

## What is verified vs. not

- **Verified in this session:** all four binaries compile cleanly for
  `windows/amd64`, are genuine PE32+ GUI-subsystem executables (checked with
  `file`), `go vet` is clean.
- **NOT verified in this session (no Windows OS available here):**
  launching any of these `.exe` files, WebView2 actually rendering the UI,
  the single-instance mutex/focus behavior, the installer's GUI wizard
  flow, or the uninstaller's registry/file cleanup. These run for real on
  `windows-latest` in `.github/workflows/windows-build.yml`; see that
  workflow's run log for actual execution results, not just compilation.
