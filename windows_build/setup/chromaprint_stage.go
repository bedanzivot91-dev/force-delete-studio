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
	btbnFFmpegURL      = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
	btbnChecksumsURL   = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/checksums.sha256"
	btbnFFmpegFilename = "ffmpeg-master-latest-win64-gpl.zip"
)

// The mature installer still has an old Gyan "essentials" fallback inside
// prepareComponents(). That build deliberately omits libchromaprint, while the
// song finder needs FFmpeg's chromaprint muxer to survive real Shorts
// re-encoding. The release package is assembled through --stage-components,
// so seed that staging directory with a verified full GPL build first. The
// existing prepareComponents() then sees working ffmpeg/ffprobe binaries and
// leaves them in place. The normal GUI installer copies this already-complete
// Program/ folder from the offline package, so end users receive exactly the
// binaries verified here.
func init() {
	if len(os.Args) <= 2 || os.Args[1] != "--stage-components" {
		return
	}
	log := func(s string) { fmt.Println(s) }
	if err := stageChromaprintFFmpeg(os.Args[2], log); err != nil {
		fmt.Fprintln(os.Stderr, "Chromaprint FFmpeg staging:", err)
		os.Exit(1)
	}
}

func ffmpegHasChromaprintBinary(path string) error {
	if !fileReady(path, 1000000) {
		return fmt.Errorf("ffmpeg.exe nedostaje ili je premali: %s", path)
	}
	cmd := exec.Command(path, "-hide_banner", "-muxers")
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
	out, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("ffmpeg -muxers nije uspeo: %w", err)
	}
	if !strings.Contains(strings.ToLower(string(out)), "chromaprint") {
		return fmt.Errorf("FFmpeg radi, ali nema Chromaprint muxer; takav paket nije dozvoljen za Pronalazač mojih pesama")
	}
	return nil
}

func stageChromaprintFFmpeg(stage string, log func(string)) error {
	ffDir := filepath.Join(stage, "tools", "ffmpeg", "bin")
	ffmpegExe := filepath.Join(ffDir, "ffmpeg.exe")
	ffprobeExe := filepath.Join(ffDir, "ffprobe.exe")

	// Reuse only an already-good package. Merely having an executable is not
	// enough: the old essentials build launches normally but lacks Chromaprint.
	if fileReady(ffprobeExe, 1000000) {
		if err := ffmpegHasChromaprintBinary(ffmpegExe); err == nil {
			log("FFmpeg sa Chromaprintom je već prisutan u staged paketu.")
			return nil
		}
	}

	cache := filepath.Join(os.TempDir(), "SunoPesmeStudio-component-cache")
	if err := os.MkdirAll(cache, 0755); err != nil {
		return err
	}
	zipPath := filepath.Join(cache, "ffmpeg-chromaprint-full.zip")
	checksumsPath := filepath.Join(cache, "btbn-checksums.sha256")

	log("Preuzimam BtbN full GPL FFmpeg sa Chromaprintom...")
	if err := downloadFile(btbnChecksumsURL, checksumsPath, log); err != nil {
		return fmt.Errorf("BtbN checksums: %w", err)
	}
	expected, err := checksumFromFile(checksumsPath, btbnFFmpegFilename)
	if err != nil {
		return fmt.Errorf("BtbN checksum za %s: %w", btbnFFmpegFilename, err)
	}
	if err := downloadFile(btbnFFmpegURL, zipPath, log); err != nil {
		return fmt.Errorf("BtbN full FFmpeg: %w", err)
	}
	if err := verifyFileSHA(zipPath, expected); err != nil {
		return fmt.Errorf("BtbN full FFmpeg integritet: %w", err)
	}

	// Non-destructive upgrade: preserve the existing FFmpeg folder and every
	// auxiliary file in it. Only ffmpeg.exe and ffprobe.exe are replaced by the
	// verified Chromaprint-capable binaries from the full build.
	if err := os.MkdirAll(ffDir, 0755); err != nil {
		return err
	}
	if err := extractNamedFromZip(zipPath, ffDir, map[string]string{
		"ffmpeg.exe":  "ffmpeg.exe",
		"ffprobe.exe": "ffprobe.exe",
	}); err != nil {
		return fmt.Errorf("BtbN full FFmpeg raspakivanje: %w", err)
	}
	if err := runTool(ffprobeExe, "-version"); err != nil {
		return err
	}
	if err := ffmpegHasChromaprintBinary(ffmpegExe); err != nil {
		return err
	}
	log("BtbN full FFmpeg je SHA-256 proveren i Chromaprint muxer je potvrđen; postojeći FFmpeg folder nije obrisan.")
	return nil
}
