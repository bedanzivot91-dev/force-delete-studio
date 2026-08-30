//go:build windows

package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"

	"golang.org/x/sys/windows/registry"
)

const webView2ClientGUID = `{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`
const webView2StandaloneName = "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"

func webView2RuntimeInstalled() bool {
	checks := []struct {
		root registry.Key
		path string
	}{
		{registry.LOCAL_MACHINE, `SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\` + webView2ClientGUID},
		{registry.CURRENT_USER, `Software\Microsoft\EdgeUpdate\Clients\` + webView2ClientGUID},
	}
	for _, check := range checks {
		key, err := registry.OpenKey(check.root, check.path, registry.QUERY_VALUE)
		if err != nil {
			continue
		}
		version, _, err := key.GetStringValue("pv")
		key.Close()
		version = strings.TrimSpace(version)
		if err == nil && version != "" && version != "0.0.0.0" {
			return true
		}
	}
	return false
}

func ensureOfflineWebView2(root string) error {
	if webView2RuntimeInstalled() {
		return nil
	}
	installer := filepath.Join(root, "tools", "webview2", webView2StandaloneName)
	st, err := os.Stat(installer)
	if err != nil || st.IsDir() || st.Size() < 50*1024*1024 {
		return fmt.Errorf("WebView2 Runtime nije instaliran, a offline installer nedostaje ili je nepotpun: %s", installer)
	}
	cmd := exec.Command(installer, "/silent", "/install")
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
	if out, err := cmd.CombinedOutput(); err != nil {
		text := strings.TrimSpace(string(out))
		if len(text) > 700 {
			text = text[len(text)-700:]
		}
		return fmt.Errorf("WebView2 Runtime offline instalacija nije uspela: %w %s", err, text)
	}
	if !webView2RuntimeInstalled() {
		return fmt.Errorf("WebView2 installer je završen, ali Runtime nije potvrđen u Windows registru")
	}
	return nil
}

// Normalno pokretanje programa mora biti potpuno offline. --self-test ne sme
// menjati Windows, a --otvori-podatke ne koristi WebView2, pa te režime preskačemo.
func init() {
	if len(os.Args) > 1 && (os.Args[1] == "--self-test" || os.Args[1] == "--otvori-podatke") {
		return
	}
	root, err := rootDir()
	if err == nil {
		err = ensureOfflineWebView2(root)
	}
	if err != nil {
		message(appName+" — WebView2 Runtime", err.Error()+"\r\n\r\nProgram nije pokrenut da ne bi radio u nepotpunom stanju.", mbOK|mbIconError)
		os.Exit(3)
	}
}
