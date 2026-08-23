//go:build windows

package main

// CI-only headless entry point for the real uninstaller logic. The normal
// shipped flow remains graphical. This uses the same shutdown/process cleanup,
// shortcut cleanup, registry cleanup and directory removal helpers as the GUI
// uninstaller, but omits the final MessageBox so a hosted Windows runner can
// verify complete removal without synthetic clicks.

import (
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

func init() {
	args := os.Args[1:]
	if len(args) >= 3 && args[0] == "--ci-remove" {
		// This switch recursively deletes a caller-provided install directory.
		// Keep it usable by the real GitHub Actions install/uninstall regression,
		// but make it unreachable as a hidden destructive mode on end-user PCs.
		if !strings.EqualFold(strings.TrimSpace(os.Getenv("GITHUB_ACTIONS")), "true") {
			fmt.Fprintln(os.Stderr, "--ci-remove is available only inside GitHub Actions")
			os.Exit(2)
		}
		deleteData, _ := strconv.ParseBool(args[2])
		os.Exit(ciRemove(filepath.Clean(args[1]), deleteData))
	}
}

func ciRemove(installDir string, deleteData bool) int {
	if strings.TrimSpace(installDir) == "" || installDir == "." {
		fmt.Fprintln(os.Stderr, "ci-remove install directory is empty")
		return 1
	}

	requestLocalShutdown()
	closeRunningApplication()
	removeShortcuts()
	deleteUninstallRegistry()

	local := strings.TrimSpace(os.Getenv("LOCALAPPDATA"))
	dataRoot := filepath.Join(local, appName)
	if deleteData && local != "" {
		_ = os.RemoveAll(dataRoot)
	}

	var lastErr error
	for i := 0; i < 12; i++ {
		if err := os.RemoveAll(installDir); err == nil {
			if _, statErr := os.Stat(installDir); os.IsNotExist(statErr) {
				lastErr = nil
				break
			} else {
				lastErr = statErr
			}
		} else {
			lastErr = err
		}
		time.Sleep(500 * time.Millisecond)
	}
	if lastErr != nil {
		fmt.Fprintln(os.Stderr, "ci-remove failed:", lastErr)
		return 1
	}
	if deleteData && local != "" {
		if _, err := os.Stat(dataRoot); !os.IsNotExist(err) {
			fmt.Fprintln(os.Stderr, "ci-remove persistent data still exists:", dataRoot)
			return 1
		}
	}
	fmt.Println("CI_UNINSTALL_REMOVED=" + installDir)
	return 0
}
