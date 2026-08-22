//go:build windows

package main

// CI-only real installation entry point.
//
// The normal end-user installer remains fully graphical.  This file exists so
// the Windows workflow can exercise the SAME package verification, component
// preparation, Program staging, launcher self-test, Versions layout, shortcut
// registration and uninstall registration that the GUI path uses, without
// requiring synthetic mouse/keyboard clicks on hosted runners.

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"
)

func init() {
	if len(os.Args) >= 3 && os.Args[1] == "--ci-install" {
		os.Exit(ciInstall(os.Args[2]))
	}
}

func ciInstall(installRoot string) int {
	exe, err := os.Executable()
	if err != nil {
		fmt.Fprintln(os.Stderr, "ci-install executable:", err)
		return 1
	}
	root := filepath.Dir(exe)
	programSrc := filepath.Join(root, "Program")
	manifest := filepath.Join(root, "MANIFEST_SHA256.txt")
	if err := verifyPackage(root, programSrc, manifest); err != nil {
		fmt.Fprintln(os.Stderr, "ci-install package verification:", err)
		return 1
	}

	installRoot = filepath.Clean(strings.TrimSpace(installRoot))
	if installRoot == "" || installRoot == "." {
		fmt.Fprintln(os.Stderr, "ci-install target is empty")
		return 1
	}
	if err := os.RemoveAll(installRoot); err != nil {
		fmt.Fprintln(os.Stderr, "ci-install clean target:", err)
		return 1
	}
	if err := os.MkdirAll(installRoot, 0755); err != nil {
		fmt.Fprintln(os.Stderr, "ci-install mkdir target:", err)
		return 1
	}

	local := strings.TrimSpace(os.Getenv("LOCALAPPDATA"))
	if local == "" {
		fmt.Fprintln(os.Stderr, "ci-install LOCALAPPDATA is unavailable")
		return 1
	}
	dataRoot := filepath.Join(local, appName)
	versionsRoot := filepath.Join(installRoot, "Versions")
	if err := os.MkdirAll(versionsRoot, 0755); err != nil {
		fmt.Fprintln(os.Stderr, "ci-install versions mkdir:", err)
		return 1
	}
	if err := os.MkdirAll(dataRoot, 0755); err != nil {
		fmt.Fprintln(os.Stderr, "ci-install data mkdir:", err)
		return 1
	}

	log := func(s string) { fmt.Println("[ci-install] " + s) }
	installID := version + "-ci-" + time.Now().Format("20060102-150405.000000000")
	stage := filepath.Join(os.TempDir(), "SunoPesmeStudio-ci-stage-"+installID)
	versionStage := filepath.Join(versionsRoot, ".nova-"+installID)
	versionDir := filepath.Join(versionsRoot, installID)
	_ = os.RemoveAll(stage)
	_ = os.RemoveAll(versionStage)

	fail := func(label string, cause error) int {
		_ = os.RemoveAll(stage)
		_ = os.RemoveAll(versionStage)
		fmt.Fprintln(os.Stderr, "ci-install "+label+":", cause)
		return 1
	}

	// Same package -> stage path used by the GUI installer.
	if err := copyTree(programSrc, stage); err != nil {
		return fail("copy Program to stage", err)
	}
	if err := verifyProgramManifest(stage, manifest); err != nil {
		return fail("verify staged Program", err)
	}
	if err := prepareComponents(stage, []string{
		programSrc,
		installRoot,
		filepath.Join(local, "Programs", appName),
	}, log); err != nil {
		return fail("prepare components", err)
	}
	if err := verifyProgramManifest(stage, manifest); err != nil {
		return fail("verify Program after components", err)
	}

	stagedExe := filepath.Join(stage, appName+".exe")
	stagedTest := exec.Command(stagedExe, "--self-test")
	stagedTest.Env = os.Environ()
	stagedTest.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
	if err := stagedTest.Run(); err != nil {
		return fail("staged launcher self-test", err)
	}

	// Same .nova -> final Versions promotion used by the GUI installer.
	if err := copyTree(stage, versionStage); err != nil {
		return fail("copy stage to .nova version", err)
	}
	if err := verifyProgramManifest(versionStage, manifest); err != nil {
		return fail("verify .nova version", err)
	}
	_ = os.RemoveAll(stage)

	finalTest := exec.Command(filepath.Join(versionStage, appName+".exe"), "--self-test")
	finalTest.Env = os.Environ()
	finalTest.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
	if err := finalTest.Run(); err != nil {
		return fail("final launcher self-test", err)
	}
	if err := verifyProgramManifest(versionStage, manifest); err != nil {
		return fail("final manifest verification", err)
	}
	if err := os.Rename(versionStage, versionDir); err != nil {
		return fail("promote .nova version", err)
	}

	appExe := filepath.Join(versionDir, appName+".exe")
	uninstallExe := filepath.Join(versionDir, "Deinstaliraj "+appName+".exe")
	if err := createAllShortcuts(appExe, uninstallExe, dataRoot); err != nil {
		return fail("create shortcuts", err)
	}
	if err := addUninstallRegistry(uninstallExe, installRoot); err != nil {
		return fail("register uninstaller", err)
	}
	activeFile := filepath.Join(installRoot, "AKTIVNA_VERZIJA.txt")
	if err := os.WriteFile(activeFile, []byte(versionDir+"\r\n"), 0600); err != nil {
		return fail("write active version", err)
	}

	cleanupLegacyInstallFiles(installRoot, versionsRoot, versionDir, log)
	cleanupOldVersions(versionsRoot, versionDir, log)

	fmt.Println("CI_INSTALL_ROOT=" + installRoot)
	fmt.Println("CI_INSTALL_VERSION_DIR=" + versionDir)
	fmt.Println("CI_INSTALL_APP=" + appExe)
	fmt.Println("CI_INSTALL_UNINSTALLER=" + uninstallExe)
	return 0
}
