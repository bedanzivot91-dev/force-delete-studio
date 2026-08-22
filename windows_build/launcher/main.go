//go:build windows

// Suno Pesme Studio launcher.
//
// This replaces the previous implementation, which opened the app UI by
// shelling out to Chrome/Edge/Brave with --app=http://127.0.0.1:8765/ (or,
// failing that, the OS default browser via rundll32 url.dll). That is
// exactly the "browser tab" / "visible localhost" pattern the product spec
// forbids for the desktop build: no address bar control, no taskbar/window
// identity of our own, and a hard dependency on the user having one of
// those three browsers installed.
//
// The UI is now hosted directly inside a native WebView2 window owned by
// this process (github.com/jchv/go-webview2 -- a pure-Go, CGO-free binding
// over the WebView2 COM API, cross-compiles cleanly from a non-Windows
// host). The Python backend is still a hidden loopback-only HTTP server
// (app/server.py binds 127.0.0.1 only and validates Origin); the WebView2
// window simply navigates to it internally, with no address bar or browser
// chrome for the user to see or tamper with.
package main

import (
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"
	"unsafe"

	webview2 "github.com/jchv/go-webview2"
)

const (
	appName           = "Suno Pesme Studio"
	windowTitle       = "Suno Pesme Studio 3.3.2"
	version           = "3.3.2"
	appPort           = "8765"
	healthURL         = "http://127.0.0.1:" + appPort + "/api/health"
	appURL            = "http://127.0.0.1:" + appPort + "/"
	createNoWindow    = 0x08000000
	mbOK              = 0x00000000
	mbIconError       = 0x00000010
	mbIconInformation = 0x00000040
	singleInstanceMtx = "Local\\SunoPesmeStudio_3_3_2_SingleInstance"
	mainWindowClass   = "SunoPesmeStudioMainWindow"
	errorAlreadyExist = 183
)

var (
	user32         = syscall.NewLazyDLL("user32.dll")
	kernel32       = syscall.NewLazyDLL("kernel32.dll")
	messageBoxW    = user32.NewProc("MessageBoxW")
	findWindowW    = user32.NewProc("FindWindowW")
	setForegroundW = user32.NewProc("SetForegroundWindow")
	showWindowProc = user32.NewProc("ShowWindow")
	createMutexW   = kernel32.NewProc("CreateMutexW")
	getLastError   = kernel32.NewProc("GetLastError")
)

func u16(s string) *uint16 { p, _ := syscall.UTF16PtrFromString(s); return p }

func message(title, text string, flags uint32) {
	messageBoxW.Call(0, uintptr(unsafe.Pointer(u16(text))), uintptr(unsafe.Pointer(u16(title))), uintptr(flags))
}

func rootDir() (string, error) {
	exe, err := os.Executable()
	if err != nil {
		return "", err
	}
	return filepath.Dir(exe), nil
}

func userDataDir() string {
	base := strings.TrimSpace(os.Getenv("LOCALAPPDATA"))
	if base == "" {
		base = os.TempDir()
	}
	return filepath.Join(base, appName)
}

func requiredFiles(root string) []string {
	return []string{
		filepath.Join(root, "python", "python.exe"),
		filepath.Join(root, "python", "pythonw.exe"),
		filepath.Join(root, "app", "watchdog.py"),
		filepath.Join(root, "app", "server.py"),
		filepath.Join(root, "app", "web", "index.html"),
		filepath.Join(root, "app", "web", "style.css"),
		filepath.Join(root, "app", "web", "app.js"),
		filepath.Join(root, "tools", "ffmpeg", "bin", "ffmpeg.exe"),
		filepath.Join(root, "tools", "ffmpeg", "bin", "ffprobe.exe"),
		filepath.Join(root, "tools", "yt-dlp", "yt-dlp.exe"),
		filepath.Join(root, "tools", "deno", "deno.exe"),
	}
}

func missingFiles(root string) []string {
	var out []string
	for _, p := range requiredFiles(root) {
		st, err := os.Stat(p)
		if err != nil || st.IsDir() || st.Size() == 0 {
			rel, _ := filepath.Rel(root, p)
			out = append(out, rel)
		}
	}
	return out
}

func healthReady() bool {
	c := &http.Client{Timeout: 800 * time.Millisecond}
	r, err := c.Get(healthURL)
	if err != nil {
		return false
	}
	defer r.Body.Close()
	if r.StatusCode != http.StatusOK {
		return false
	}
	var payload map[string]any
	if json.NewDecoder(r.Body).Decode(&payload) != nil {
		return false
	}
	return payload["ok"] == true
}

func startWatchdog(root string) error {
	py := filepath.Join(root, "python", "pythonw.exe")
	watchdog := filepath.Join(root, "app", "watchdog.py")
	data := userDataDir()
	if err := os.MkdirAll(filepath.Join(data, "data"), 0755); err != nil {
		return err
	}
	env := append(os.Environ(),
		"SUNO_STUDIO_USER_DIR="+data,
		"SUNO_STUDIO_DATA_DIR="+filepath.Join(data, "data"),
		"SUNO_STUDIO_DOWNLOAD_DIR="+filepath.Join(data, "Preuzete_pesme"),
		"SUNO_STUDIO_EXPORT_DIR="+filepath.Join(data, "Izvoz"),
		"SUNO_STUDIO_PUBLISHED_DIR="+filepath.Join(data, "Objavljene_pesme"),
		"SUNO_STUDIO_LIBRARY_DIR="+filepath.Join(data, "Biblioteka_pesama"),
		"SUNO_STUDIO_RECOGNITION_DIR="+filepath.Join(data, "Pronalazac_pesme"),
		"SUNO_STUDIO_PORT="+appPort,
		"SUNO_AUTO_OPEN=0",
		"PYTHONUTF8=1",
		"PYTHONIOENCODING=utf-8",
	)
	cmd := exec.Command(py, watchdog)
	cmd.Dir = root
	cmd.Env = env
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
	return cmd.Start()
}

func shutdownBackend() {
	c := &http.Client{Timeout: 2 * time.Second}
	req, err := http.NewRequest(http.MethodPost, "http://127.0.0.1:"+appPort+"/api/shutdown", nil)
	if err != nil {
		return
	}
	resp, err := c.Do(req)
	if err == nil {
		resp.Body.Close()
	}
}

// tail returns roughly the last maxBytes of a text file, or "" if it
// doesn't exist / can't be read. Used to put the ACTUAL cause of a
// startup failure directly in front of the user instead of just a path
// they'd have to go dig up in Notepad themselves.
func tail(path string, maxBytes int64) string {
	f, err := os.Open(path)
	if err != nil {
		return ""
	}
	defer f.Close()
	st, err := f.Stat()
	if err != nil {
		return ""
	}
	size := st.Size()
	start := int64(0)
	if size > maxBytes {
		start = size - maxBytes
	}
	if _, err := f.Seek(start, 0); err != nil {
		return ""
	}
	buf := make([]byte, size-start)
	n, _ := f.Read(buf)
	text := strings.TrimSpace(string(buf[:n]))
	if start > 0 {
		if idx := strings.IndexByte(text, '\n'); idx >= 0 {
			text = text[idx+1:]
		}
		text = "…\r\n" + text
	}
	return strings.ReplaceAll(text, "\n", "\r\n")
}

func startupDiagnostics() string {
	data := userDataDir()
	var parts []string
	if wd := tail(filepath.Join(data, "data", "watchdog.log"), 2000); wd != "" {
		parts = append(parts, "watchdog.log (poslednji deo):\r\n"+wd)
	}
	if sv := tail(filepath.Join(data, "data", "server-konzola.log"), 2000); sv != "" {
		parts = append(parts, "server-konzola.log (poslednji deo):\r\n"+sv)
	}
	if len(parts) == 0 {
		return "Ni watchdog.log ni server-konzola.log još ne postoje ili su prazni — server verovatno nije uspeo da se ni pokrene."
	}
	return strings.Join(parts, "\r\n\r\n")
}

func selfTest(root string) error {
	if missing := missingFiles(root); len(missing) > 0 {
		return fmt.Errorf("nedostaju fajlovi:\n• %s", strings.Join(missing, "\n• "))
	}
	return nil
}

// acquireSingleInstance returns true if this process is the first instance.
// If another instance already owns the mutex, it tries to bring that
// instance's window to the foreground and the caller should exit.
func acquireSingleInstance() bool {
	h, _, _ := createMutexW.Call(0, 0, uintptr(unsafe.Pointer(u16(singleInstanceMtx))))
	if h == 0 {
		return true // couldn't create mutex, don't block startup over it
	}
	errCode, _, _ := getLastError.Call()
	if errCode == errorAlreadyExist {
		hwnd, _, _ := findWindowW.Call(0, uintptr(unsafe.Pointer(u16(windowTitle))))
		if hwnd != 0 {
			const swRestore = 9
			showWindowProc.Call(hwnd, swRestore)
			setForegroundW.Call(hwnd)
		}
		return false
	}
	return true
}

func showNativeWindow() {
	w := webview2.NewWithOptions(webview2.WebViewOptions{
		Debug:     false,
		AutoFocus: true,
		WindowOptions: webview2.WindowOptions{
			Title:  windowTitle,
			Width:  1360,
			Height: 860,
			Center: true,
		},
	})
	if w == nil {
		message(appName, "WebView2 runtime nije dostupan na ovom sistemu.\r\n\r\nInstaliraj ponovo program (INSTALIRAJ_PROGRAM.exe), koji ugrađuje potreban WebView2 Runtime.", mbOK|mbIconError)
		shutdownBackend()
		return
	}
	defer w.Destroy()
	w.Navigate(appURL)
	w.Run()
	shutdownBackend()
}

func main() {
	root, err := rootDir()
	if err != nil {
		message(appName, err.Error(), mbOK|mbIconError)
		return
	}
	if len(os.Args) > 1 && os.Args[1] == "--self-test" {
		if err := selfTest(root); err != nil {
			os.Exit(2)
		}
		os.Exit(0)
	}
	if len(os.Args) > 1 && os.Args[1] == "--otvori-podatke" {
		_ = os.MkdirAll(userDataDir(), 0755)
		cmd := exec.Command("explorer.exe", userDataDir())
		cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
		_ = cmd.Start()
		return
	}

	if !acquireSingleInstance() {
		// Another instance is already running and was just focused.
		return
	}

	if err := selfTest(root); err != nil {
		message(appName+" — nepotpuna instalacija",
			"Program nije kompletno instaliran.\r\n\r\n"+err.Error()+
				"\r\n\r\nPonovo pokreni grafički installer iz kompletnog paketa. Alati se pripremaju samo tokom instalacije, ne pri svakom pokretanju.",
			mbOK|mbIconError)
		return
	}
	if !healthReady() {
		if err := startWatchdog(root); err != nil {
			message(appName+" — greška pokretanja", err.Error(), mbOK|mbIconError)
			return
		}
		// 90s, not 60s: real end-user machines (unlike the CI runner this
		// was built and self-tested on) commonly run antivirus real-time
		// scanning that can add real delay the first time this many fresh
		// binaries/DLLs execute after install/upgrade. This only helps a
		// genuinely-slow-but-working startup; a hard failure (e.g. a port
		// already held by a previous instance) still surfaces the same
		// diagnostics below, just after a slightly longer wait.
		deadline := time.Now().Add(90 * time.Second)
		for time.Now().Before(deadline) {
			if healthReady() {
				break
			}
			time.Sleep(300 * time.Millisecond)
		}
	}
	if !healthReady() {
		logPath := filepath.Join(userDataDir(), "data")
		message(appName+" — server se nije pokrenuo",
			"Lokalni deo programa se nije pokrenuo na vreme.\r\n\r\nFajlovi za dijagnostiku: "+logPath+
				"\r\n\r\n"+startupDiagnostics(),
			mbOK|mbIconError)
		return
	}
	showNativeWindow()
}
