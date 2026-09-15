//go:build windows

package main

import (
	"fmt"
	"os"
	"path/filepath"
)

const webView2StandaloneX64URL = "https://go.microsoft.com/fwlink/?linkid=2124701"
const webView2StandaloneName = "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"

// The release is advertised as fully offline, so staging must include the
// Microsoft Evergreen Standalone Installer. This is installation media, not a
// second WebView2 runtime tree: Windows keeps one Evergreen Runtime and updates
// it normally after installation.
func init() {
	if len(os.Args) <= 2 || os.Args[1] != "--stage-components" {
		return
	}
	log := func(s string) { fmt.Println(s) }
	if err := stageWebView2OfflineInstaller(os.Args[2], log); err != nil {
		fmt.Fprintln(os.Stderr, "WebView2 offline staging:", err)
		os.Exit(1)
	}
}

func stageWebView2OfflineInstaller(stage string, log func(string)) error {
	target := filepath.Join(stage, "tools", "webview2", webView2StandaloneName)
	if fileReady(target, 50*1024*1024) {
		if err := verifyPE(target); err == nil {
			log("WebView2 Evergreen Standalone x64 installer je već prisutan u staged paketu.")
			return nil
		}
	}

	if err := os.MkdirAll(filepath.Dir(target), 0755); err != nil {
		return err
	}
	log("Preuzimam aktuelni Microsoft WebView2 Evergreen Standalone x64 installer za potpuno offline instalaciju...")
	if err := downloadFile(webView2StandaloneX64URL, target, log); err != nil {
		return fmt.Errorf("WebView2 Standalone: %w", err)
	}
	if err := verifyPE(target); err != nil {
		_ = os.Remove(target)
		return fmt.Errorf("WebView2 Standalone nije validan Windows EXE: %w", err)
	}
	log("WebView2 Evergreen Standalone x64 installer je staged kao jedina offline WebView2 instalaciona komponenta.")
	return nil
}
