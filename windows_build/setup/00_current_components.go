//go:build windows

package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
)

const (
	currentPythonVersion = "3.13.15"
	currentPythonURL     = "https://www.python.org/ftp/python/3.13.15/python-3.13.15-embed-amd64.zip"
	currentPythonSHA256  = "d1f04d990aee1253d8569e8e5104e30fa9f5fa830899f14843448872d936a2cf"

	currentDenoVersion = "2.9.5"
	currentDenoURL     = "https://github.com/denoland/deno/releases/download/v2.9.5/deno-x86_64-pc-windows-msvc.zip"
	currentDenoSHAURL  = currentDenoURL + ".sha256sum"
)

// A release build starts with an empty Program/ staging directory. Put the
// current runtime versions there first, before the older compatibility code in
// prepareComponents() runs. The final offline package therefore contains one
// Python runtime and one Deno binary, never parallel old/new copies.
func init() {
	if len(os.Args) <= 2 || os.Args[1] != "--stage-components" {
		return
	}
	log := func(s string) { fmt.Println(s) }
	if err := stageCurrentPython(os.Args[2], log); err != nil {
		fmt.Fprintln(os.Stderr, "Current Python staging:", err)
		os.Exit(1)
	}
	if err := stageCurrentDeno(os.Args[2], log); err != nil {
		fmt.Fprintln(os.Stderr, "Current Deno staging:", err)
		os.Exit(1)
	}
}

func commandOutput(path string, args ...string) string {
	cmd := exec.Command(path, args...)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
	out, err := cmd.CombinedOutput()
	if err != nil {
		return ""
	}
	return strings.TrimSpace(string(out))
}

func stageCurrentPython(stage string, log func(string)) error {
	pythonDir := filepath.Join(stage, "python")
	pythonExe := filepath.Join(pythonDir, "python.exe")
	pythonwExe := filepath.Join(pythonDir, "pythonw.exe")
	if fileReady(pythonExe, 100000) && fileReady(pythonwExe, 100000) &&
		strings.Contains(commandOutput(pythonExe, "--version"), "Python "+currentPythonVersion) {
		log("Aktuelni Python " + currentPythonVersion + " je već prisutan.")
		return nil
	}

	cache := filepath.Join(os.TempDir(), "SunoPesmeStudio-component-cache")
	if err := os.MkdirAll(cache, 0755); err != nil {
		return err
	}
	zipPath := filepath.Join(cache, "python-"+currentPythonVersion+"-embed-amd64.zip")
	log("Preuzimam aktuelni Python " + currentPythonVersion + " embeddable x64...")
	if err := downloadVerified([]string{currentPythonURL}, []string{currentPythonSHA256}, zipPath, log); err != nil {
		return fmt.Errorf("Python %s: %w", currentPythonVersion, err)
	}

	// Python embeddable runtime is a single versioned component. Replace an old
	// runtime atomically in staging rather than retaining multiple versions.
	fresh := pythonDir + ".current"
	_ = os.RemoveAll(fresh)
	if err := extractZipAll(zipPath, fresh); err != nil {
		_ = os.RemoveAll(fresh)
		return fmt.Errorf("Python %s raspakivanje: %w", currentPythonVersion, err)
	}
	if !fileReady(filepath.Join(fresh, "python.exe"), 100000) ||
		!strings.Contains(commandOutput(filepath.Join(fresh, "python.exe"), "--version"), "Python "+currentPythonVersion) {
		_ = os.RemoveAll(fresh)
		return fmt.Errorf("raspakovani Python nije potvrđen kao %s", currentPythonVersion)
	}
	_ = os.RemoveAll(pythonDir)
	if err := os.Rename(fresh, pythonDir); err != nil {
		return err
	}
	log("Python " + currentPythonVersion + " je SHA-256 proveren i postavljen kao jedina ugrađena Python verzija.")
	return nil
}

func stageCurrentDeno(stage string, log func(string)) error {
	denoDir := filepath.Join(stage, "tools", "deno")
	denoExe := filepath.Join(denoDir, "deno.exe")
	if fileReady(denoExe, 1000000) && strings.HasPrefix(commandOutput(denoExe, "--version"), "deno "+currentDenoVersion) {
		log("Aktuelni Deno " + currentDenoVersion + " je već prisutan.")
		return nil
	}

	cache := filepath.Join(os.TempDir(), "SunoPesmeStudio-component-cache")
	if err := os.MkdirAll(cache, 0755); err != nil {
		return err
	}
	zipPath := filepath.Join(cache, "deno-"+currentDenoVersion+"-x86_64-windows.zip")
	shaPath := zipPath + ".sha256sum"
	log("Preuzimam aktuelni Deno " + currentDenoVersion + " x86_64 Windows...")
	if err := downloadFile(currentDenoSHAURL, shaPath, log); err != nil {
		return fmt.Errorf("Deno %s checksum: %w", currentDenoVersion, err)
	}
	// This URL is the official checksum sidecar for this exact asset, so the
	// first valid SHA-256 in it belongs to currentDenoURL even when GitHub's
	// sidecar format omits the filename.
	expected, err := checksumFromFile(shaPath, "")
	if err != nil {
		return fmt.Errorf("Deno %s checksum parsing: %w", currentDenoVersion, err)
	}
	if err := downloadFile(currentDenoURL, zipPath, log); err != nil {
		return fmt.Errorf("Deno %s: %w", currentDenoVersion, err)
	}
	if err := verifyFileSHA(zipPath, expected); err != nil {
		return fmt.Errorf("Deno %s integritet: %w", currentDenoVersion, err)
	}
	if err := os.MkdirAll(denoDir, 0755); err != nil {
		return err
	}
	if err := extractNamedFromZip(zipPath, denoDir, map[string]string{"deno.exe": "deno.exe"}); err != nil {
		return fmt.Errorf("Deno %s raspakivanje: %w", currentDenoVersion, err)
	}
	if got := commandOutput(denoExe, "--version"); !strings.HasPrefix(got, "deno "+currentDenoVersion) {
		return fmt.Errorf("Deno verzija nije %s: %s", currentDenoVersion, got)
	}
	log("Deno " + currentDenoVersion + " je proveren zvaničnim per-asset SHA-256 checksumom i postavljen kao jedina Deno verzija.")
	return nil
}
