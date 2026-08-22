//go:build windows

package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"

	webview2 "github.com/jchv/go-webview2"
)

const (
	productName = "NP Suno Unified Studio"
	windowTitle = "NP Suno Unified Studio 2026"
)

type moduleInfo struct {
	Key         string `json:"key"`
	Name        string `json:"name"`
	Description string `json:"description"`
	Ready       bool   `json:"ready"`
	Path        string `json:"path"`
}

func rootDir() (string, error) {
	exe, err := os.Executable()
	if err != nil {
		return "", err
	}
	return filepath.Dir(exe), nil
}

func moduleExecutable(root, key string) (string, error) {
	switch strings.ToLower(strings.TrimSpace(key)) {
	case "suno":
		return filepath.Join(root, "Modules", "Suno", "Suno Pesme Studio.exe"), nil
	case "video":
		return filepath.Join(root, "Modules", "Video", "NPVideoStudio.exe"), nil
	default:
		return "", fmt.Errorf("nepoznat modul: %s", key)
	}
}

func fileReady(path string) bool {
	st, err := os.Stat(path)
	return err == nil && !st.IsDir() && st.Size() > 0
}

func modules(root string) []moduleInfo {
	suno, _ := moduleExecutable(root, "suno")
	video, _ := moduleExecutable(root, "video")
	return []moduleInfo{
		{
			Key:         "suno",
			Name:        "Suno / Biblioteka / YouTube",
			Description: "Pesme, biblioteka, Shorts prepoznavanje, YouTube analiza i Suno alati.",
			Ready:       fileReady(suno),
			Path:        suno,
		},
		{
			Key:         "video",
			Name:        "NP Video Studio",
			Description: "Kompletan video editor: timeline, tekst, titlovi, efekti, render i AI alati.",
			Ready:       fileReady(video),
			Path:        video,
		},
	}
}

func selfTest(root string) error {
	missing := []string{}
	for _, m := range modules(root) {
		if !m.Ready {
			missing = append(missing, m.Path)
		}
	}

	// A launcher-only existence check is not enough. Verify several payload files
	// whose absence would make the child apps look installed but fail immediately.
	required := []string{
		filepath.Join(root, "Modules", "Suno", "app", "server.py"),
		filepath.Join(root, "Modules", "Suno", "app", "youtube_match_recovery.py"),
		filepath.Join(root, "Modules", "Suno", "app", "web", "modern_2026_shell_extension.js"),
		filepath.Join(root, "Modules", "Suno", "tools", "ffmpeg", "bin", "ffmpeg.exe"),
		filepath.Join(root, "Modules", "Video", "Tools", "ffmpeg", "ffmpeg.exe"),
		filepath.Join(root, "Modules", "Video", "Tools", "yt-dlp", "yt-dlp.exe"),
		filepath.Join(root, "Modules", "Video", "Tools", "fpcalc", "fpcalc.exe"),
		filepath.Join(root, "Modules", "Video", "Tools", "tesseract", "tesseract.exe"),
	}
	for _, path := range required {
		if !fileReady(path) {
			missing = append(missing, path)
		}
	}
	if len(missing) > 0 {
		return fmt.Errorf("unified paket nije kompletan; nedostaje %d fajl(ova):\n%s", len(missing), strings.Join(missing, "\n"))
	}
	return nil
}

func launchModule(root, key string) error {
	exe, err := moduleExecutable(root, key)
	if err != nil {
		return err
	}
	if !fileReady(exe) {
		return fmt.Errorf("modul nije instaliran ili je oštećen: %s", exe)
	}
	cmd := exec.Command(exe)
	cmd.Dir = filepath.Dir(exe)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: false}
	if err := cmd.Start(); err != nil {
		return fmt.Errorf("modul nije mogao da se pokrene: %w", err)
	}
	return nil
}

const shellHTML = `<!doctype html>
<html lang="sr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>NP Suno Unified Studio</title>
<style>
:root{color-scheme:dark;font-family:"Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;background:#090b12;color:#f5f7fb}
*{box-sizing:border-box} body{margin:0;min-height:100vh;background:radial-gradient(circle at 15% 0%,#23315a55,transparent 35%),radial-gradient(circle at 100% 15%,#5b2b6a44,transparent 32%),#090b12}
.wrap{max-width:1180px;margin:0 auto;padding:52px 42px 40px}.eyebrow{font-size:13px;letter-spacing:.18em;text-transform:uppercase;color:#9aa8c5;font-weight:700}.hero{margin-top:14px;display:flex;align-items:flex-end;justify-content:space-between;gap:28px}.hero h1{font-size:44px;line-height:1.04;margin:0;font-weight:760;letter-spacing:-.035em}.hero p{max-width:560px;color:#b9c2d4;font-size:16px;line-height:1.6;margin:0}
.badge{display:inline-flex;align-items:center;gap:8px;padding:8px 12px;border:1px solid #ffffff18;border-radius:999px;background:#ffffff0c;color:#c7d1e8;font-size:13px}.dot{width:8px;height:8px;border-radius:50%;background:#77e69d;box-shadow:0 0 16px #77e69d77}
.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px;margin-top:36px}.card{position:relative;min-height:300px;border:1px solid #ffffff18;border-radius:24px;background:linear-gradient(145deg,#171b28e8,#10131de8);padding:28px;overflow:hidden;box-shadow:0 20px 60px #00000038}.card:before{content:"";position:absolute;inset:-80px -120px auto auto;width:260px;height:260px;border-radius:50%;background:#647bff22;filter:blur(8px)}.card.video:before{background:#da63ff22}.icon{display:grid;place-items:center;width:52px;height:52px;border-radius:16px;background:#ffffff0e;border:1px solid #ffffff1b;font-size:24px}.card h2{font-size:25px;margin:22px 0 9px;letter-spacing:-.02em}.card p{margin:0;color:#aeb8cb;line-height:1.6;font-size:15px;max-width:460px}.meta{margin-top:22px;color:#7f8ba5;font-size:13px}.open{position:absolute;left:28px;bottom:28px;right:28px;height:48px;border:0;border-radius:14px;background:#f3f5fa;color:#111520;font-weight:750;font-size:15px;cursor:pointer;transition:.18s}.video .open{background:#d9d2ff}.open:hover{transform:translateY(-1px);filter:brightness(1.05)}.open:disabled{cursor:not-allowed;opacity:.42;transform:none}.foot{display:flex;justify-content:space-between;gap:16px;margin-top:22px;color:#75809a;font-size:12.5px}.error{margin-top:18px;padding:12px 14px;border-radius:12px;background:#ff5f6d18;border:1px solid #ff778522;color:#ffb5bd;display:none}
@media(max-width:820px){.wrap{padding:32px 22px}.hero{align-items:flex-start;flex-direction:column}.hero h1{font-size:34px}.grid{grid-template-columns:1fr}.card{min-height:285px}}
</style>
</head>
<body>
<div class="wrap">
  <div class="eyebrow">UNIFIED WORKSPACE · 2026</div>
  <div class="hero"><div><h1>NP Suno<br>Unified Studio</h1></div><p>Jedan instalirani paket za muziku, biblioteku, YouTube/Shorts prepoznavanje i kompletan video editor. Moduli dele isti suite i pokreću se iz jednog glavnog ulaza.</p></div>
  <div style="margin-top:22px"><span class="badge"><span class="dot"></span><span id="suiteState">Proveravam instalaciju…</span></span></div>
  <div class="grid">
    <section class="card"><div class="icon">♫</div><h2>Suno / Biblioteka / YouTube</h2><p>Upravljanje pesmama, biblioteka, fingerprint prepoznavanje, Shorts i YouTube analiza, Suno alati i audio workflow.</p><div class="meta" id="sunoMeta">Provera modula…</div><button class="open" id="sunoBtn" onclick="openModule('suno')">Otvori muzički studio</button></section>
    <section class="card video"><div class="icon">▶</div><h2>NP Video Studio</h2><p>Timeline editor, tekst i titlovi, sistemski fontovi, efekti, keyframes, audio/video alati, render, OCR i AI workflow.</p><div class="meta" id="videoMeta">Provera modula…</div><button class="open" id="videoBtn" onclick="openModule('video')">Otvori video studio</button></section>
  </div>
  <div class="error" id="errorBox"></div>
  <div class="foot"><span>NP Suno Unified Studio</span><span>Jedan suite · dva potpuno ugrađena radna modula</span></div>
</div>
<script>
const byId=id=>document.getElementById(id);
async function refresh(){
  try{
    const mods=await unifiedStatus();
    let all=true;
    for(const m of mods){
      const ready=!!m.ready; all=all&&ready;
      byId(m.key+'Btn').disabled=!ready;
      byId(m.key+'Meta').textContent=ready?'Modul je instaliran i spreman':'Modul nije kompletno instaliran';
    }
    byId('suiteState').textContent=all?'Kompletan suite je spreman':'Instalacija nije kompletna';
  }catch(e){showError(String(e));}
}
async function openModule(key){
  byId('errorBox').style.display='none';
  try{await launchUnifiedModule(key);}catch(e){showError(String(e));}
}
function showError(text){const b=byId('errorBox');b.textContent=text;b.style.display='block';}
window.addEventListener('load',refresh);
</script>
</body>
</html>`

func runUI(root string) error {
	w := webview2.NewWithOptions(webview2.WebViewOptions{
		Debug:     false,
		AutoFocus: true,
		WindowOptions: webview2.WindowOptions{
			Title:  windowTitle,
			Width:  1180,
			Height: 760,
			Center: true,
		},
	})
	if w == nil {
		return fmt.Errorf("WebView2 runtime nije dostupan")
	}
	defer w.Destroy()

	if err := w.Bind("unifiedStatus", func() []moduleInfo { return modules(root) }); err != nil {
		return err
	}
	if err := w.Bind("launchUnifiedModule", func(key string) error { return launchModule(root, key) }); err != nil {
		return err
	}
	w.SetHtml(shellHTML)
	w.Run()
	return nil
}

func main() {
	root, err := rootDir()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}

	if len(os.Args) > 1 {
		switch os.Args[1] {
		case "--self-test":
			if err := selfTest(root); err != nil {
				fmt.Fprintln(os.Stderr, err)
				os.Exit(2)
			}
			return
		case "--launch-suno":
			if err := launchModule(root, "suno"); err != nil {
				fmt.Fprintln(os.Stderr, err)
				os.Exit(3)
			}
			return
		case "--launch-video":
			if err := launchModule(root, "video"); err != nil {
				fmt.Fprintln(os.Stderr, err)
				os.Exit(4)
			}
			return
		}
	}

	if err := selfTest(root); err != nil {
		fmt.Fprintln(os.Stderr, err)
		// Still open the shell: it will clearly show which module is missing
		// instead of silently doing nothing.
	}
	if err := runUI(root); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(5)
	}
}
