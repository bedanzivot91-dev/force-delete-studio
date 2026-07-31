//go:build windows

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
)

const (
	appName           = "Suno Pesme Studio"
	version           = "3.3.2"
	healthURL         = "http://127.0.0.1:8765/api/health"
	appURL            = "http://127.0.0.1:8765/"
	createNoWindow    = 0x08000000
	mbOK              = 0x00000000
	mbIconError       = 0x00000010
	mbIconInformation = 0x00000040
)

var (
	user32      = syscall.NewLazyDLL("user32.dll")
	messageBoxW = user32.NewProc("MessageBoxW")
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

func browserCandidates() []string {
	var paths []string
	for _, base := range []string{os.Getenv("PROGRAMFILES(X86)"), os.Getenv("PROGRAMFILES"), os.Getenv("LOCALAPPDATA")} {
		if strings.TrimSpace(base) == "" {
			continue
		}
		paths = append(paths,
			filepath.Join(base, "Microsoft", "Edge", "Application", "msedge.exe"),
			filepath.Join(base, "Google", "Chrome", "Application", "chrome.exe"),
			filepath.Join(base, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
		)
	}
	return paths
}

func openAppWindow() error {
	for _, p := range browserCandidates() {
		if st, err := os.Stat(p); err == nil && !st.IsDir() {
			cmd := exec.Command(p, "--app="+appURL, "--new-window")
			cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
			if err := cmd.Start(); err == nil {
				return nil
			}
		}
	}
	cmd := exec.Command("rundll32.exe", "url.dll,FileProtocolHandler", appURL)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	return cmd.Start()
}

func selfTest(root string) error {
	if missing := missingFiles(root); len(missing) > 0 {
		return fmt.Errorf("nedostaju fajlovi:\n• %s", strings.Join(missing, "\n• "))
	}
	return nil
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
		deadline := time.Now().Add(60 * time.Second)
		for time.Now().Before(deadline) {
			if healthReady() {
				break
			}
			time.Sleep(300 * time.Millisecond)
		}
	}
	if !healthReady() {
		logPath := filepath.Join(userDataDir(), "data", "server-konzola.log")
		message(appName+" — server se nije pokrenuo",
			"Lokalni deo programa se nije pokrenuo za 60 sekundi.\r\n\r\nDijagnostika:\r\n"+logPath,
			mbOK|mbIconError)
		return
	}
	if err := openAppWindow(); err != nil {
		message(appName, "Program radi lokalno, ali prozor nije mogao da se otvori:\r\n"+err.Error(), mbOK|mbIconInformation)
	}
}
