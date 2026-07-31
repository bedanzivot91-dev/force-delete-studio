//go:build windows

package main

import (
	"fmt"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

const (
	appName           = "Suno Pesme Studio"
	mbOK              = 0
	mbYesNo           = 4
	mbIconInformation = 0x40
	mbIconWarning     = 0x30
	mbIconError       = 0x10
	idYes             = 6

	createNoWindow           = 0x08000000
	movefileDelayUntilReboot = 0x00000004
	keyWrite                 = 0x20006
	errorSuccess             = 0
	wmClose                  = 0x0010
	th32csSnapProcess        = 0x00000002
	processTerminate         = 0x0001
	synchronize              = 0x00100000
)

var (
	user32          = syscall.NewLazyDLL("user32.dll")
	procMessageBoxW = user32.NewProc("MessageBoxW")
	findWindowW     = user32.NewProc("FindWindowW")
	postMessageW    = user32.NewProc("PostMessageW")

	advapi32       = syscall.NewLazyDLL("advapi32.dll")
	regOpenKeyExW  = advapi32.NewProc("RegOpenKeyExW")
	regDeleteTreeW = advapi32.NewProc("RegDeleteTreeW")
	regCloseKey    = advapi32.NewProc("RegCloseKey")

	kernel32                 = syscall.NewLazyDLL("kernel32.dll")
	moveFileExW              = kernel32.NewProc("MoveFileExW")
	createToolhelp32Snapshot = kernel32.NewProc("CreateToolhelp32Snapshot")
	process32FirstW          = kernel32.NewProc("Process32FirstW")
	process32NextW           = kernel32.NewProc("Process32NextW")
	openProcess              = kernel32.NewProc("OpenProcess")
	terminateProcess         = kernel32.NewProc("TerminateProcess")
	waitForSingleObject      = kernel32.NewProc("WaitForSingleObject")
	closeHandle              = kernel32.NewProc("CloseHandle")
	getCurrentProcessID      = kernel32.NewProc("GetCurrentProcessId")
)

type processEntry32W struct {
	Size            uint32
	CntUsage        uint32
	ProcessID       uint32
	DefaultHeapID   uintptr
	ModuleID        uint32
	CntThreads      uint32
	ParentProcessID uint32
	PriClassBase    int32
	Flags           uint32
	ExeFile         [260]uint16
}

func u16(s string) *uint16 { p, _ := syscall.UTF16PtrFromString(s); return p }
func msg(title, text string, flags uint32) int {
	r, _, _ := procMessageBoxW.Call(0, uintptr(unsafe.Pointer(u16(text))), uintptr(unsafe.Pointer(u16(title))), uintptr(flags))
	return int(r)
}

func main() {
	args := os.Args[1:]
	if len(args) >= 4 && args[0] == "--native-remove" {
		installDir := args[1]
		deleteData, _ := strconv.ParseBool(args[2])
		parentPID, _ := strconv.Atoi(args[3])
		helperRemove(installDir, deleteData, parentPID)
		return
	}

	if msg(appName+" — deinstalacija", "Deinstalirati program?\r\n\r\nBiblioteka pesama, istorija Pronalazača pesme i Objavljene pesme neće biti obrisane osim ako to posebno potvrdiš.", mbYesNo|mbIconWarning) != idYes {
		return
	}
	exe, err := os.Executable()
	if err != nil {
		msg(appName, err.Error(), mbOK|mbIconError)
		return
	}
	installDir := filepath.Dir(exe)
	installRoot := installDir
	if strings.EqualFold(filepath.Base(filepath.Dir(installDir)), "Versions") {
		installRoot = filepath.Dir(filepath.Dir(installDir))
	}
	deleteData := msg(appName+" — trajni podaci", "Obrisati i SVE trajne podatke?\r\n\r\nDA briše Biblioteku, istoriju Pronalazača pesme, Objavljene pesme i podešavanja.\r\nNE uklanja samo program i čuva tvoje podatke.", mbYesNo|mbIconWarning) == idYes

	tempCopy := filepath.Join(os.TempDir(), fmt.Sprintf("Suno-Pesme-Studio-Deinstalacija-%d.exe", time.Now().UnixNano()))
	if err := copyFile(exe, tempCopy); err != nil {
		msg(appName+" — greška", "Ne mogu da pripremim grafičku deinstalaciju: "+err.Error(), mbOK|mbIconError)
		return
	}
	cmd := exec.Command(tempCopy, "--native-remove", installRoot, strconv.FormatBool(deleteData), strconv.Itoa(os.Getpid()))
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
	if err := cmd.Start(); err != nil {
		_ = os.Remove(tempCopy)
		msg(appName+" — greška", "Ne mogu da pokrenem grafičku deinstalaciju: "+err.Error(), mbOK|mbIconError)
		return
	}
}

func helperRemove(installDir string, deleteData bool, parentPID int) {
	// Parent GUI exits immediately after launching this helper. A short wait avoids locked-file races.
	_ = parentPID
	time.Sleep(1200 * time.Millisecond)
	requestLocalShutdown()
	closeRunningApplication()

	removeShortcuts()
	deleteUninstallRegistry()

	local := strings.TrimSpace(os.Getenv("LOCALAPPDATA"))
	dataRoot := filepath.Join(local, appName)
	if deleteData {
		_ = os.RemoveAll(dataRoot)
	}

	var lastErr error
	for i := 0; i < 8; i++ {
		if err := os.RemoveAll(installDir); err == nil {
			_, statErr := os.Stat(installDir)
			if os.IsNotExist(statErr) {
				lastErr = nil
				break
			}
			lastErr = statErr
		} else {
			lastErr = err
		}
		time.Sleep(500 * time.Millisecond)
	}

	if lastErr != nil {
		msg(appName+" — deinstalacija", "Program nije mogao potpuno da se ukloni. Zatvori sve prozore programa i pokušaj ponovo.\r\n\r\n"+lastErr.Error(), mbOK|mbIconError)
	} else if deleteData {
		msg(appName+" — deinstalacija", "Program i svi trajni podaci su uklonjeni.", mbOK|mbIconInformation)
	} else {
		msg(appName+" — deinstalacija", "Program je uklonjen. Biblioteka, istorija Pronalazača pesme i ostali trajni podaci su sačuvani.", mbOK|mbIconInformation)
	}

	self, _ := os.Executable()
	if self != "" {
		// Windows deletes this temporary GUI helper on the next restart; no CMD is used.
		moveFileExW.Call(uintptr(unsafe.Pointer(u16(self))), 0, movefileDelayUntilReboot)
	}
}

func requestLocalShutdown() {
	local := strings.TrimSpace(os.Getenv("LOCALAPPDATA"))
	if local != "" {
		dataDir := filepath.Join(local, appName, "data")
		_ = os.MkdirAll(dataDir, 0755)
		_ = os.WriteFile(filepath.Join(dataDir, "watchdog.stop"), []byte("stop\r\n"), 0600)
	}
	client := &http.Client{Timeout: 2 * time.Second}
	req, _ := http.NewRequest(http.MethodPost, "http://127.0.0.1:8765/api/shutdown", strings.NewReader("{}"))
	req.Header.Set("Content-Type", "application/json")
	if resp, err := client.Do(req); err == nil {
		_ = resp.Body.Close()
	}
	time.Sleep(1200 * time.Millisecond)
}

func closeRunningApplication() {
	for _, className := range []string{"SunoPesmeStudioDesktopV6", "SunoPesmeStudioDesktopV5", "SunoPesmeStudioDesktopV4"} {
		hwnd, _, _ := findWindowW.Call(uintptr(unsafe.Pointer(u16(className))), 0)
		if hwnd != 0 {
			postMessageW.Call(hwnd, wmClose, 0, 0)
		}
	}
	time.Sleep(1500 * time.Millisecond)

	snap, _, _ := createToolhelp32Snapshot.Call(th32csSnapProcess, 0)
	if snap == 0 || snap == ^uintptr(0) {
		return
	}
	defer closeHandle.Call(snap)
	currentPID, _, _ := getCurrentProcessID.Call()
	var pe processEntry32W
	pe.Size = uint32(unsafe.Sizeof(pe))
	ok, _, _ := process32FirstW.Call(snap, uintptr(unsafe.Pointer(&pe)))
	if ok == 0 {
		return
	}
	for {
		name := strings.ToLower(syscall.UTF16ToString(pe.ExeFile[:]))
		if uintptr(pe.ProcessID) != currentPID && name == strings.ToLower(appName+".exe") {
			h, _, _ := openProcess.Call(processTerminate|synchronize, 0, uintptr(pe.ProcessID))
			if h != 0 {
				terminateProcess.Call(h, 0)
				waitForSingleObject.Call(h, 5000)
				closeHandle.Call(h)
			}
		}
		next, _, _ := process32NextW.Call(snap, uintptr(unsafe.Pointer(&pe)))
		if next == 0 {
			break
		}
	}
}

func removeShortcuts() {
	paths := []string{
		filepath.Join(os.Getenv("USERPROFILE"), "Desktop", appName+".lnk"),
		filepath.Join(os.Getenv("APPDATA"), "Microsoft", "Windows", "Start Menu", "Programs", appName),
	}
	for _, p := range paths {
		_ = os.RemoveAll(p)
	}
}

func deleteUninstallRegistry() {
	const hkeyCurrentUser = uintptr(0x80000001)
	const sub = `Software\Microsoft\Windows\CurrentVersion\Uninstall`
	var key uintptr
	r, _, _ := regOpenKeyExW.Call(hkeyCurrentUser, uintptr(unsafe.Pointer(u16(sub))), 0, keyWrite, uintptr(unsafe.Pointer(&key)))
	if r != errorSuccess {
		return
	}
	defer regCloseKey.Call(key)
	regDeleteTreeW.Call(key, uintptr(unsafe.Pointer(u16("SunoPesmeStudio"))))
}

func copyFile(src, dst string) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()
	out, err := os.Create(dst)
	if err != nil {
		return err
	}
	defer out.Close()
	_, err = out.ReadFrom(in)
	if err != nil {
		return err
	}
	return out.Sync()
}
