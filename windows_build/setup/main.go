//go:build windows

package main

import (
	"archive/zip"
	"bufio"
	"crypto/sha256"
	_ "embed"
	"encoding/hex"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

//go:embed LICENSE.txt
var licenseText string

const (
	appName = "Suno Pesme Studio"
	version = "3.3.2"

	mbOK              = 0x00000000
	mbYesNo           = 0x00000004
	mbIconError       = 0x00000010
	mbIconInformation = 0x00000040
	idYes             = 6

	coinitApartmentThreaded = 0x2
	clsctxInprocServer      = 0x1
	stgmRead                = 0x00000000
	swShowNormal            = 1

	keyWrite             = 0x20006
	regOptionNonVolatile = 0
	regSz                = 1
	regDword             = 4
	errorSuccess         = 0

	createNoWindow = 0x08000000

	wmClose                        = 0x0010
	th32csSnapProcess              = 0x00000002
	processTerminate               = 0x0001
	processQueryLimitedInformation = 0x1000
	synchronize                    = 0x00100000
	waitObject0                    = 0x00000000
	waitTimeout                    = 0x00000102
)

var (
	user32      = syscall.NewLazyDLL("user32.dll")
	messageBoxW = user32.NewProc("MessageBoxW")

	shell32              = syscall.NewLazyDLL("shell32.dll")
	shBrowseForFolderW   = shell32.NewProc("SHBrowseForFolderW")
	shGetPathFromIDListW = shell32.NewProc("SHGetPathFromIDListW")

	ole32            = syscall.NewLazyDLL("ole32.dll")
	coInitializeEx   = ole32.NewProc("CoInitializeEx")
	coUninitialize   = ole32.NewProc("CoUninitialize")
	coCreateInstance = ole32.NewProc("CoCreateInstance")
	coTaskMemFree    = ole32.NewProc("CoTaskMemFree")

	advapi32        = syscall.NewLazyDLL("advapi32.dll")
	regCreateKeyExW = advapi32.NewProc("RegCreateKeyExW")
	regSetValueExW  = advapi32.NewProc("RegSetValueExW")
	regCloseKey     = advapi32.NewProc("RegCloseKey")

	kernel32                   = syscall.NewLazyDLL("kernel32.dll")
	createToolhelp32Snapshot   = kernel32.NewProc("CreateToolhelp32Snapshot")
	process32FirstW            = kernel32.NewProc("Process32FirstW")
	process32NextW             = kernel32.NewProc("Process32NextW")
	openProcess                = kernel32.NewProc("OpenProcess")
	queryFullProcessImageNameW = kernel32.NewProc("QueryFullProcessImageNameW")
	terminateProcess           = kernel32.NewProc("TerminateProcess")
	waitForSingleObject        = kernel32.NewProc("WaitForSingleObject")
	closeHandle                = kernel32.NewProc("CloseHandle")
	getCurrentProcessID        = kernel32.NewProc("GetCurrentProcessId")

	findWindowW  = user32.NewProc("FindWindowW")
	postMessageW = user32.NewProc("PostMessageW")
)

type browseInfoW struct {
	HwndOwner      uintptr
	PidlRoot       uintptr
	PszDisplayName *uint16
	LpszTitle      *uint16
	UlFlags        uint32
	Lpfn           uintptr
	LParam         uintptr
	IImage         int32
}

const (
	bifReturnOnlyFSDirs = 0x0001
	bifNewDialogStyle   = 0x0040
	bifEditBox          = 0x0010
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

type runningProcess struct {
	PID  uint32
	Path string
}

type guid struct {
	Data1 uint32
	Data2 uint16
	Data3 uint16
	Data4 [8]byte
}

var (
	clsidShellLink  = guid{0x00021401, 0x0000, 0x0000, [8]byte{0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}}
	iidIShellLinkW  = guid{0x000214F9, 0x0000, 0x0000, [8]byte{0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}}
	iidIPersistFile = guid{0x0000010B, 0x0000, 0x0000, [8]byte{0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}}
)

type iUnknownVtbl struct {
	QueryInterface uintptr
	AddRef         uintptr
	Release        uintptr
}

type iShellLinkWVtbl struct {
	QueryInterface      uintptr
	AddRef              uintptr
	Release             uintptr
	GetPath             uintptr
	GetIDList           uintptr
	SetIDList           uintptr
	GetDescription      uintptr
	SetDescription      uintptr
	GetWorkingDirectory uintptr
	SetWorkingDirectory uintptr
	GetArguments        uintptr
	SetArguments        uintptr
	GetHotkey           uintptr
	SetHotkey           uintptr
	GetShowCmd          uintptr
	SetShowCmd          uintptr
	GetIconLocation     uintptr
	SetIconLocation     uintptr
	SetRelativePath     uintptr
	Resolve             uintptr
	SetPath             uintptr
}

type iShellLinkW struct{ lpVtbl *iShellLinkWVtbl }

type iPersistFileVtbl struct {
	QueryInterface uintptr
	AddRef         uintptr
	Release        uintptr
	GetClassID     uintptr
	IsDirty        uintptr
	Load           uintptr
	Save           uintptr
	SaveCompleted  uintptr
	GetCurFile     uintptr
}

type iPersistFile struct{ lpVtbl *iPersistFileVtbl }

func u16(s string) *uint16 { p, _ := syscall.UTF16PtrFromString(s); return p }

func messageBox(title, text string, flags uint32) int {
	r, _, _ := messageBoxW.Call(0, uintptr(unsafe.Pointer(u16(text))), uintptr(unsafe.Pointer(u16(title))), uintptr(flags))
	return int(r)
}

func hresultFailed(hr uintptr) bool { return int32(hr) < 0 }

// stageComponentsCLI runs the exact same component fetch/verify/extract
// logic the GUI installer uses (prepareComponents), headless, against a
// target directory. This exists so CI (which has real internet access,
// unlike the sandbox this project was developed in) can assemble a
// genuinely offline-capable release package without a second,
// out-of-sync copy of the download URLs/hashes living in a build script.
// Not reachable from the shipped GUI installer's normal flow.
func stageComponentsCLI(target string) int {
	if err := os.MkdirAll(target, 0755); err != nil {
		fmt.Fprintln(os.Stderr, "mkdir:", err)
		return 1
	}
	log := func(s string) { fmt.Println(s) }
	// prepareComponents both downloads/verifies-by-SHA-256 and runs each
	// tool with --version at the end (see the `checks` block at its tail)
	// before returning, so a nil error here already means every component
	// was physically staged and actually executed successfully.
	if err := prepareComponents(target, nil, log); err != nil {
		fmt.Fprintln(os.Stderr, "prepareComponents:", err)
		return 1
	}
	fmt.Println("Komponente su preuzete, proverene i uspešno pokrenute u:", target)
	return 0
}

// acceptLicense writes the embedded LICENSE.txt to a temp file, opens it
// in Notepad so the user actually reads the full text (not just a
// MessageBox summary, which can't scroll long text), then requires an
// explicit Yes/No to continue. Returns false if declined or if the
// license can't even be shown (fail closed, not open).
func acceptLicense() bool {
	tmp := filepath.Join(os.TempDir(), "SunoPesmeStudio-LICENSE.txt")
	if err := os.WriteFile(tmp, []byte(licenseText), 0644); err != nil {
		messageBox(appName+" — greška", "Licenca nije mogla da se prikaže: "+err.Error(), mbOK|mbIconError)
		return false
	}
	cmd := exec.Command("notepad.exe", tmp)
	_ = cmd.Start()
	result := messageBox(appName+" — licenca",
		"Licenca je otvorena u Notepad-u ("+tmp+").\r\n\r\nPročitaj je, zatim se vrati ovde i klikni Da ako prihvataš uslove, ili Ne da otkažeš instalaciju.",
		mbYesNo|mbIconInformation)
	return result == idYes
}

func main() {
	if len(os.Args) > 2 && os.Args[1] == "--stage-components" {
		os.Exit(stageComponentsCLI(os.Args[2]))
	}
	exe, err := os.Executable()
	if err != nil {
		fail(err)
	}
	root := filepath.Dir(exe)
	programSrc := filepath.Join(root, "Program")
	manifest := filepath.Join(root, "MANIFEST_SHA256.txt")
	if err = verifyPackage(root, programSrc, manifest); err != nil {
		fail(err)
	}

	local := strings.TrimSpace(os.Getenv("LOCALAPPDATA"))
	if local == "" {
		fail(fmt.Errorf("Windows LOCALAPPDATA nije dostupan"))
	}

	// Dobrodošli (section 22, step 1)
	messageBox(appName+" — dobrodošli", "Dobrodošao/la u instalaciju "+appName+" "+version+".\r\n\r\nOva instalacija je grafička, potpuno offline i ne koristi CMD, BAT ni PowerShell.\r\n\r\nSledeći korak je licenca.", mbOK|mbIconInformation)

	// Licenca (section 22, step 2) -- shows the real license text via
	// Notepad (reliable, already-proven Win32 surface: ShellExecute/exec,
	// not a hand-rolled multiline edit control) and requires explicit
	// acceptance before continuing.
	if !acceptLicense() {
		return
	}

	messageBox(appName+" — instalacija", "Sada biraš gde će program biti instaliran. Posle klika na OK izaberi željeni disk i folder. Instalacija ne koristi CMD.", mbOK|mbIconInformation)
	installRoot := ""
	for {
		selectedBase, pickErr := browseInstallFolder("Izaberi folder za instalaciju Suno Pesme Studio")
		if pickErr != nil {
			fail(pickErr)
		}
		if strings.TrimSpace(selectedBase) == "" {
			return
		}
		installRoot = selectedBase
		if !strings.EqualFold(filepath.Base(filepath.Clean(installRoot)), appName) {
			installRoot = filepath.Join(installRoot, appName)
		}
		if err := checkInstallLocationWritable(installRoot); err == nil {
			break
		} else {
			messageBox(appName+" — lokacija nije upisiva", "Windows ne dozvoljava upis u izabrani folder bez dodatnih ovlašćenja.\r\n\r\n"+installRoot+"\r\n\r\nIzaberi drugi folder na kome imaš dozvolu za pisanje.\r\n\r\nDetalj: "+err.Error(), mbOK|mbIconError)
		}
	}
	versionsRoot := filepath.Join(installRoot, "Versions")
	dataRoot := filepath.Join(local, appName)
	logPath := filepath.Join(dataRoot, "logs", "instalacija.log")
	_ = os.MkdirAll(filepath.Dir(logPath), 0755)
	logf, _ := os.OpenFile(logPath, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0600)
	if logf != nil {
		defer logf.Close()
	}
	progressStatus := ""
	log := func(s string) {
		if logf != nil {
			fmt.Fprintf(logf, "%s %s\r\n", time.Now().Format("2006-01-02 15:04:05"), s)
			_ = logf.Sync()
		}
		if progressStatus != "" {
			_ = os.WriteFile(progressStatus, []byte(s+"\r\n"), 0600)
		}
	}

	prompt := "Instalirati Suno Pesme Studio 3.3.2?\r\n\r\n" +
		"Izabrana lokacija:\r\n" + installRoot + "\r\n\r\n" +
		"Ovo je grafička Windows instalacija bez CMD-a.\r\n" +
		"Originalna tamna tema, sve stare funkcije i nove mogućnosti biće instalirani lokalno na PC.\r\n\r\n" +
		"Biblioteka, istorija Pronalazača pesme i objavljene pesme ostaju sačuvane."
	if messageBox(appName+" — grafička instalacija", prompt, mbYesNo|mbIconInformation) != idYes {
		return
	}

	progressStatus = filepath.Join(os.TempDir(), fmt.Sprintf("SunoPesmeStudio-install-%d.status", os.Getpid()))
	_ = os.WriteFile(progressStatus, []byte("Pokretanje instalacije...\r\n"), 0600)
	progressExe := filepath.Join(root, "INSTALACIJA_NAPREDAK.exe")
	if fileReady(progressExe, 10000) {
		progressCmd := exec.Command(progressExe, progressStatus, fmt.Sprintf("%d", os.Getpid()))
		progressCmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
		_ = progressCmd.Start()
	}
	log("Početak GUI instalacije " + version)
	if err = os.MkdirAll(versionsRoot, 0755); err != nil {
		failLog(log, err)
	}
	if err = os.MkdirAll(dataRoot, 0755); err != nil {
		failLog(log, err)
	}
	legacyRoots := []string{installRoot, filepath.Join(local, "Programs", appName)}
	seenLegacy := map[string]bool{}
	for _, legacyRoot := range legacyRoots {
		legacyRoot = filepath.Clean(legacyRoot)
		if seenLegacy[strings.ToLower(legacyRoot)] {
			continue
		}
		seenLegacy[strings.ToLower(legacyRoot)] = true
		_ = migrateLegacyData(legacyRoot, dataRoot, log)
	}

	// Zaustavi stari lokalni server/watchdog pre zamene verzije.
	requestLocalShutdown(dataRoot, log)
	// Prvo tražimo od stare verzije da se normalno zatvori. Ako ostane aktivna,
	// prekidamo samo procese čiji je naziv tačno Suno Pesme Studio.exe.
	remaining, closeErr := closePreviousInstances(log)
	if closeErr != nil {
		log("Upozorenje pri zatvaranju stare verzije: " + closeErr.Error())
	}
	if len(remaining) > 0 {
		log("Neki stari procesi nisu zatvoreni, ali side-by-side instalacija se nastavlja: " + strings.Join(remaining, "; "))
	}

	installID := version + "-" + time.Now().Format("20060102-150405")
	stage := filepath.Join(os.TempDir(), "SunoPesmeStudio-stage-"+installID)
	versionDir := filepath.Join(versionsRoot, installID)
	_ = os.RemoveAll(stage)
	if err = copyTree(programSrc, stage); err != nil {
		failLog(log, fmt.Errorf("kopiranje kompletnog Program foldera: %w", err))
	}
	if err = verifyProgramManifest(stage, manifest); err != nil {
		_ = os.RemoveAll(stage)
		failLog(log, fmt.Errorf("provera kopiranog Program foldera nije prošla: %w", err))
	}
	log("Programski folder je pripremljen. Proveravam postojeće komponente i preuzimam samo ono što nedostaje.")
	if err = prepareComponents(stage, []string{programSrc, installRoot, filepath.Join(local, "Programs", appName)}, log); err != nil {
		_ = os.RemoveAll(stage)
		failLog(log, fmt.Errorf("priprema ugrađenih komponenti nije uspela: %w", err))
	}
	if err = verifyProgramManifest(stage, manifest); err != nil {
		_ = os.RemoveAll(stage)
		failLog(log, fmt.Errorf("provera Program foldera posle pripreme komponenti nije prošla: %w", err))
	}

	stagedExe := filepath.Join(stage, appName+".exe")
	cmd := exec.Command(stagedExe, "--self-test")
	cmd.Env = os.Environ()
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
	if err = cmd.Run(); err != nil {
		_ = os.RemoveAll(stage)
		failLog(log, fmt.Errorf("samoprovera nove verzije nije prošla; postojeća instalacija nije promenjena: %w", err))
	}
	if err = verifyProgramManifest(stage, manifest); err != nil {
		_ = os.RemoveAll(stage)
		failLog(log, fmt.Errorf("provera Program foldera posle samoprovere nije prošla: %w", err))
	}

	// Tek nakon što su sve komponente preuzete i proverene kopiramo kompletan
	// program na izabrani disk. Tako korisnik nikada ne dobija polovičan .nova folder.
	versionStage := filepath.Join(versionsRoot, ".nova-"+installID)
	_ = os.RemoveAll(versionStage)
	if err = copyTree(stage, versionStage); err != nil {
		_ = os.RemoveAll(stage)
		_ = os.RemoveAll(versionStage)
		failLog(log, fmt.Errorf("kopiranje proverene verzije na izabrani disk: %w", err))
	}
	if err = verifyProgramManifest(versionStage, manifest); err != nil {
		_ = os.RemoveAll(stage)
		_ = os.RemoveAll(versionStage)
		failLog(log, fmt.Errorf("provera kopirane verzije na izabranom disku nije prošla: %w", err))
	}
	_ = os.RemoveAll(stage)
	finalTest := exec.Command(filepath.Join(versionStage, appName+".exe"), "--self-test")
	finalTest.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
	if err = finalTest.Run(); err != nil {
		_ = os.RemoveAll(versionStage)
		failLog(log, fmt.Errorf("završna samoprovera na izabranom disku nije prošla: %w", err))
	}
	if err = verifyProgramManifest(versionStage, manifest); err != nil {
		_ = os.RemoveAll(versionStage)
		failLog(log, fmt.Errorf("završna provera fajlova na izabranom disku nije prošla: %w", err))
	}
	if err = os.Rename(versionStage, versionDir); err != nil {
		_ = os.RemoveAll(versionStage)
		failLog(log, fmt.Errorf("ne mogu da završim novi verzioni folder: %w", err))
	}

	appExe := filepath.Join(versionDir, appName+".exe")
	uninstallExe := filepath.Join(versionDir, "Deinstaliraj "+appName+".exe")
	if err = createAllShortcuts(appExe, uninstallExe, dataRoot); err != nil {
		log("Upozorenje, prečice: " + err.Error())
	}
	if err = addUninstallRegistry(uninstallExe, installRoot); err != nil {
		log("Upozorenje, registry: " + err.Error())
	}
	_ = os.WriteFile(filepath.Join(installRoot, "AKTIVNA_VERZIJA.txt"), []byte(versionDir+"\r\n"), 0600)

	cleanupLegacyInstallFiles(installRoot, versionsRoot, versionDir, log)
	cleanupOldVersions(versionsRoot, versionDir, log)

	log("GUI instalacija i samoprovera uspešne: " + versionDir)
	messageBox(appName+" — instalacija završena",
		"Program je uspešno instaliran u folder koji si izabrao, bez CMD-a.\r\n\r\nNova verzija:\r\n"+versionDir+"\r\n\r\nTrajni podaci:\r\n"+dataRoot,
		mbOK|mbIconInformation)

	// Još jednom pokušavamo da zatvorimo eventualno zaostalu staru instancu,
	// pa pokrećemo novu verziju.
	_, _ = closePreviousInstances(log)
	launch := exec.Command(appExe)
	launch.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	if err = launch.Start(); err != nil {
		log("Program je instaliran, ali automatsko pokretanje nije uspelo: " + err.Error())
		messageBox(appName+" — instalacija završena", "Instalacija je završena, ali program nije automatski pokrenut. Pokreni ga preko nove Desktop prečice.\r\n\r\n"+err.Error(), mbOK|mbIconInformation)
	}
}

// requestGracefulShutdown asks a running instance to shut itself down (and
// its Python backend) cleanly via the same local /api/shutdown endpoint
// launcher/main.go's own window-close handler uses. The current launcher
// hosts its UI in a WebView2 window (github.com/jchv/go-webview2), which
// doesn't register under the legacy "SunoPesmeStudioDesktopV*" window
// classes closePreviousInstances below still tries first for genuinely old
// versions -- without this, an in-place upgrade would fall straight
// through to force-killing just the launcher.exe by process name, which
// can orphan the Python watchdog/server subprocess tree it spawned.
func requestGracefulShutdown() {
	client := &http.Client{Timeout: 2 * time.Second}
	req, err := http.NewRequest(http.MethodPost, "http://127.0.0.1:8765/api/shutdown", nil)
	if err != nil {
		return
	}
	resp, err := client.Do(req)
	if err == nil {
		resp.Body.Close()
	}
}

func closePreviousInstances(log func(string)) ([]string, error) {
	requestGracefulShutdown()
	// Native Win32 prozor aplikacije (starije verzije). Ovo je normalno,
	// nenasilno zatvaranje za instance koje i dalje registruju stare klase.
	for _, className := range []string{"SunoPesmeStudioDesktopV6", "SunoPesmeStudioDesktopV5", "SunoPesmeStudioDesktopV4"} {
		hwnd, _, _ := findWindowW.Call(uintptr(unsafe.Pointer(u16(className))), 0)
		if hwnd != 0 {
			log("Šaljem zahtev za zatvaranje stare verzije: " + className)
			postMessageW.Call(hwnd, wmClose, 0, 0)
		}
	}

	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		list, err := findSunoProcesses()
		if err == nil && len(list) == 0 {
			return nil, nil
		}
		time.Sleep(250 * time.Millisecond)
	}

	list, err := findSunoProcesses()
	if err != nil {
		return nil, err
	}
	currentPID, _, _ := getCurrentProcessID.Call()
	remaining := make([]string, 0)
	for _, proc := range list {
		if uintptr(proc.PID) == currentPID {
			continue
		}
		h, _, openErr := openProcess.Call(processTerminate|processQueryLimitedInformation|synchronize, 0, uintptr(proc.PID))
		if h == 0 {
			remaining = append(remaining, fmt.Sprintf("PID %d (%s): %v", proc.PID, proc.Path, openErr))
			continue
		}
		log(fmt.Sprintf("Zaustavljam zaostali proces PID %d: %s", proc.PID, proc.Path))
		r, _, termErr := terminateProcess.Call(h, 0)
		if r == 0 {
			remaining = append(remaining, fmt.Sprintf("PID %d (%s): %v", proc.PID, proc.Path, termErr))
			closeHandle.Call(h)
			continue
		}
		waitForSingleObject.Call(h, 5000)
		closeHandle.Call(h)
	}
	return remaining, nil
}

func findSunoProcesses() ([]runningProcess, error) {
	snap, _, err := createToolhelp32Snapshot.Call(th32csSnapProcess, 0)
	if snap == ^uintptr(0) || snap == 0 {
		return nil, fmt.Errorf("CreateToolhelp32Snapshot: %v", err)
	}
	defer closeHandle.Call(snap)

	var pe processEntry32W
	pe.Size = uint32(unsafe.Sizeof(pe))
	ok, _, firstErr := process32FirstW.Call(snap, uintptr(unsafe.Pointer(&pe)))
	if ok == 0 {
		return nil, fmt.Errorf("Process32FirstW: %v", firstErr)
	}
	currentPID, _, _ := getCurrentProcessID.Call()
	out := make([]runningProcess, 0)
	for {
		if uintptr(pe.ProcessID) != currentPID {
			exeName := strings.ToLower(syscall.UTF16ToString(pe.ExeFile[:]))
			if exeName == strings.ToLower(appName+".exe") {
				path := queryProcessPath(pe.ProcessID)
				out = append(out, runningProcess{PID: pe.ProcessID, Path: path})
			}
		}
		next, _, _ := process32NextW.Call(snap, uintptr(unsafe.Pointer(&pe)))
		if next == 0 {
			break
		}
	}
	return out, nil
}

func queryProcessPath(pid uint32) string {
	h, _, _ := openProcess.Call(processQueryLimitedInformation, 0, uintptr(pid))
	if h == 0 {
		return appName + ".exe"
	}
	defer closeHandle.Call(h)
	buf := make([]uint16, 32768)
	size := uint32(len(buf))
	r, _, _ := queryFullProcessImageNameW.Call(h, 0, uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&size)))
	if r == 0 || size == 0 {
		return appName + ".exe"
	}
	return syscall.UTF16ToString(buf[:size])
}

func cleanupLegacyInstallFiles(installRoot, versionsRoot, current string, log func(string)) {
	entries, err := os.ReadDir(installRoot)
	if err != nil {
		return
	}
	for _, entry := range entries {
		full := filepath.Join(installRoot, entry.Name())
		if samePath(full, versionsRoot) || samePath(full, current) || strings.EqualFold(entry.Name(), "AKTIVNA_VERZIJA.txt") {
			continue
		}
		// Stari v5.1 je instalirao program direktno u koren. Sada se uklanja samo programski sadržaj;
		// trajni podaci su već migrirani u LOCALAPPDATA\\Suno Pesme Studio.
		if strings.EqualFold(entry.Name(), appName+".exe") ||
			strings.EqualFold(entry.Name(), "Deinstaliraj "+appName+".exe") ||
			strings.EqualFold(entry.Name(), "Dokumentacija") ||
			strings.EqualFold(entry.Name(), "Komponente") ||
			strings.HasPrefix(strings.ToLower(entry.Name()), ".nova-") ||
			strings.HasPrefix(strings.ToLower(entry.Name()), ".stara-") {
			if err := os.RemoveAll(full); err != nil {
				log("Stari fajl ostavljen do sledećeg čišćenja: " + full + " — " + err.Error())
			}
		}
	}
}

func cleanupOldVersions(versionsRoot, current string, log func(string)) {
	entries, err := os.ReadDir(versionsRoot)
	if err != nil {
		return
	}
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		full := filepath.Join(versionsRoot, entry.Name())
		if samePath(full, current) || strings.HasPrefix(entry.Name(), ".nova-") {
			continue
		}
		if err := os.RemoveAll(full); err != nil {
			log("Stara verzija ostavljena jer je još zaključana: " + full + " — " + err.Error())
		}
	}
}

func samePath(a, b string) bool {
	aa, _ := filepath.Abs(a)
	bb, _ := filepath.Abs(b)
	return strings.EqualFold(filepath.Clean(aa), filepath.Clean(bb))
}

func fail(err error) {
	messageBox(appName+" — greška instalacije", err.Error(), mbOK|mbIconError)
	os.Exit(1)
}
func failLog(log func(string), err error) { log("GREŠKA: " + err.Error()); fail(err) }

func fileHash(path string) (string, error) {
	f, err := os.Open(path)
	if err != nil {
		return "", err
	}
	defer f.Close()
	h := sha256.New()
	if _, err = io.Copy(h, f); err != nil {
		return "", err
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}

func verifyProgramManifest(program, manifest string) error {
	info, err := os.Stat(program)
	if err != nil || !info.IsDir() {
		return fmt.Errorf("nedostaje kompletan Program folder")
	}
	f, err := os.Open(manifest)
	if err != nil {
		return fmt.Errorf("nedostaje MANIFEST_SHA256.txt: %w", err)
	}
	defer f.Close()

	count := 0
	sc := bufio.NewScanner(f)
	for sc.Scan() {
		line := strings.TrimSpace(sc.Text())
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		parts := strings.SplitN(line, "  ", 2)
		if len(parts) != 2 {
			return fmt.Errorf("neispravan manifest: %s", line)
		}
		expected := strings.ToLower(strings.TrimSpace(parts[0]))
		displayRel := strings.TrimSpace(parts[1])
		relSlash := strings.ReplaceAll(displayRel, "\\", "/")
		relSlash = strings.TrimPrefix(relSlash, "./")
		if strings.HasPrefix(strings.ToLower(relSlash), "program/") {
			relSlash = relSlash[len("Program/"):]
		}
		rel := filepath.Clean(filepath.FromSlash(relSlash))
		if rel == "." || rel == ".." || filepath.IsAbs(rel) ||
			strings.HasPrefix(rel, ".."+string(os.PathSeparator)) {
			return fmt.Errorf("nebezbedna putanja u manifestu: %s", displayRel)
		}
		got, e := fileHash(filepath.Join(program, rel))
		if e != nil {
			return fmt.Errorf("nedostaje ili je oštećen fajl %s: %w", displayRel, e)
		}
		if !strings.EqualFold(got, expected) {
			return fmt.Errorf("kontrolni zbir nije ispravan za %s (očekivano %s, dobijeno %s)", displayRel, expected, got)
		}
		count++
	}
	if err = sc.Err(); err != nil {
		return err
	}
	if count < 3 {
		return fmt.Errorf("manifest je nepotpun")
	}
	return nil
}

func verifyPackage(root, program, manifest string) error {
	_ = root
	if err := verifyProgramManifest(program, manifest); err != nil {
		return err
	}
	if _, err := os.Stat(filepath.Join(program, appName+".exe")); err != nil {
		return fmt.Errorf("nedostaje glavni Windows program: %w", err)
	}
	return nil
}

func copyFile(src, dst string) error {
	srcInfo, err := os.Stat(src)
	if err != nil {
		return err
	}
	if srcInfo.IsDir() {
		return fmt.Errorf("izvor nije fajl: %s", src)
	}
	if err := os.MkdirAll(filepath.Dir(dst), 0755); err != nil {
		return err
	}
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()
	out, err := os.Create(dst)
	if err != nil {
		return err
	}
	n, e1 := io.Copy(out, in)
	e2 := out.Sync()
	e3 := out.Close()
	if e1 != nil {
		return e1
	}
	if n != srcInfo.Size() {
		return fmt.Errorf("nepotpuno kopiranje %s: kopirano %d od %d bajtova", src, n, srcInfo.Size())
	}
	if e2 != nil {
		return e2
	}
	if e3 != nil {
		return e3
	}
	dstInfo, err := os.Stat(dst)
	if err != nil {
		return err
	}
	if dstInfo.Size() != srcInfo.Size() {
		return fmt.Errorf("odredišni fajl je nepotpun %s: ima %d umesto %d bajtova", dst, dstInfo.Size(), srcInfo.Size())
	}
	return nil
}

func copyTree(src, dst string) error {
	return filepath.WalkDir(src, func(path string, d os.DirEntry, err error) error {
		if err != nil {
			return err
		}
		rel, err := filepath.Rel(src, path)
		if err != nil {
			return err
		}
		target := filepath.Join(dst, rel)
		if d.IsDir() {
			return os.MkdirAll(target, 0755)
		}
		return copyFile(path, target)
	})
}

func mergeTree(src, dst string) error {
	if _, err := os.Stat(src); err != nil {
		return nil
	}
	return filepath.WalkDir(src, func(path string, d os.DirEntry, err error) error {
		if err != nil {
			return nil
		}
		rel, _ := filepath.Rel(src, path)
		target := filepath.Join(dst, rel)
		if d.IsDir() {
			return os.MkdirAll(target, 0755)
		}
		if _, e := os.Stat(target); e == nil {
			return nil
		}
		return copyFile(path, target)
	})
}

func migrateLegacyData(oldInstall, dataRoot string, log func(string)) error {
	mappings := []struct{ source, target string }{
		{"Biblioteka_pesama", "Biblioteka_pesama"},
		{"Objavljene_pesme", "Objavljene_pesme"},
		{"Preuzete_pesme", "Preuzete_pesme"},
		{"Pronalazac_pesme", "Pronalazac_pesme"},
		{"Shazam_prepoznavanje", "Pronalazac_pesme"},
		{"Izvoz", "Izvoz"}, {"Backup", "Backup"}, {"data", "data"}, {"logs", "logs"},
	}
	for _, item := range mappings {
		src := filepath.Join(oldInstall, item.source)
		if _, err := os.Stat(src); err == nil {
			log("Migriram stare podatke bez brisanja: " + src)
			_ = mergeTree(src, filepath.Join(dataRoot, item.target))
		}
	}
	return nil
}

func createAllShortcuts(appExe, uninstallExe, dataRoot string) error {
	desktop := filepath.Join(os.Getenv("USERPROFILE"), "Desktop", appName+".lnk")
	startDir := filepath.Join(os.Getenv("APPDATA"), "Microsoft", "Windows", "Start Menu", "Programs", appName)
	if err := os.MkdirAll(startDir, 0755); err != nil {
		return err
	}

	entries := []struct{ link, target, args, work, desc string }{
		{desktop, appExe, "", filepath.Dir(appExe), appName},
		{filepath.Join(startDir, appName+".lnk"), appExe, "", filepath.Dir(appExe), appName},
		{filepath.Join(startDir, "Otvori trajne podatke.lnk"), appExe, "--otvori-podatke", filepath.Dir(appExe), "Otvori biblioteku i trajne podatke"},
		{filepath.Join(startDir, "Deinstaliraj.lnk"), uninstallExe, "", filepath.Dir(uninstallExe), "Deinstaliraj " + appName},
	}
	for _, e := range entries {
		if err := createShortcut(e.link, e.target, e.args, e.work, e.desc); err != nil {
			return err
		}
	}
	_ = dataRoot
	return nil
}

func createShortcut(linkPath, targetPath, arguments, workingDir, description string) error {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()
	hr, _, _ := coInitializeEx.Call(0, coinitApartmentThreaded)
	if hresultFailed(hr) && uint32(hr) != 0x80010106 {
		return fmt.Errorf("CoInitializeEx: 0x%08X", uint32(hr))
	}
	if !hresultFailed(hr) {
		defer coUninitialize.Call()
	}

	var shellLink *iShellLinkW
	hr, _, _ = coCreateInstance.Call(
		uintptr(unsafe.Pointer(&clsidShellLink)), 0, clsctxInprocServer,
		uintptr(unsafe.Pointer(&iidIShellLinkW)), uintptr(unsafe.Pointer(&shellLink)),
	)
	if hresultFailed(hr) || shellLink == nil {
		return fmt.Errorf("CoCreateInstance ShellLink: 0x%08X", uint32(hr))
	}
	defer syscall.SyscallN(shellLink.lpVtbl.Release, uintptr(unsafe.Pointer(shellLink)))

	if r, _, _ := syscall.SyscallN(shellLink.lpVtbl.SetPath, uintptr(unsafe.Pointer(shellLink)), uintptr(unsafe.Pointer(u16(targetPath)))); hresultFailed(r) {
		return fmt.Errorf("IShellLink.SetPath: 0x%08X", uint32(r))
	}
	if workingDir != "" {
		if r, _, _ := syscall.SyscallN(shellLink.lpVtbl.SetWorkingDirectory, uintptr(unsafe.Pointer(shellLink)), uintptr(unsafe.Pointer(u16(workingDir)))); hresultFailed(r) {
			return fmt.Errorf("IShellLink.SetWorkingDirectory: 0x%08X", uint32(r))
		}
	}
	if arguments != "" {
		if r, _, _ := syscall.SyscallN(shellLink.lpVtbl.SetArguments, uintptr(unsafe.Pointer(shellLink)), uintptr(unsafe.Pointer(u16(arguments)))); hresultFailed(r) {
			return fmt.Errorf("IShellLink.SetArguments: 0x%08X", uint32(r))
		}
	}
	if description != "" {
		_, _, _ = syscall.SyscallN(shellLink.lpVtbl.SetDescription, uintptr(unsafe.Pointer(shellLink)), uintptr(unsafe.Pointer(u16(description))))
	}
	_, _, _ = syscall.SyscallN(shellLink.lpVtbl.SetShowCmd, uintptr(unsafe.Pointer(shellLink)), swShowNormal)
	_, _, _ = syscall.SyscallN(shellLink.lpVtbl.SetIconLocation, uintptr(unsafe.Pointer(shellLink)), uintptr(unsafe.Pointer(u16(targetPath))), 0)

	var persist *iPersistFile
	r, _, _ := syscall.SyscallN(shellLink.lpVtbl.QueryInterface, uintptr(unsafe.Pointer(shellLink)), uintptr(unsafe.Pointer(&iidIPersistFile)), uintptr(unsafe.Pointer(&persist)))
	if hresultFailed(r) || persist == nil {
		return fmt.Errorf("QueryInterface IPersistFile: 0x%08X", uint32(r))
	}
	defer syscall.SyscallN(persist.lpVtbl.Release, uintptr(unsafe.Pointer(persist)))

	if err := os.MkdirAll(filepath.Dir(linkPath), 0755); err != nil {
		return err
	}
	r, _, _ = syscall.SyscallN(persist.lpVtbl.Save, uintptr(unsafe.Pointer(persist)), uintptr(unsafe.Pointer(u16(linkPath))), 1)
	if hresultFailed(r) {
		return fmt.Errorf("IPersistFile.Save: 0x%08X", uint32(r))
	}
	return nil
}

func addUninstallRegistry(uninstallExe, target string) error {
	const hkeyCurrentUser = uintptr(0x80000001)
	subkey := `Software\Microsoft\Windows\CurrentVersion\Uninstall\SunoPesmeStudio`
	var key uintptr
	var disposition uint32
	r, _, _ := regCreateKeyExW.Call(
		hkeyCurrentUser,
		uintptr(unsafe.Pointer(u16(subkey))),
		0, 0, regOptionNonVolatile, keyWrite, 0,
		uintptr(unsafe.Pointer(&key)), uintptr(unsafe.Pointer(&disposition)),
	)
	if r != errorSuccess {
		return fmt.Errorf("RegCreateKeyExW: %d", r)
	}
	defer regCloseKey.Call(key)

	values := map[string]string{
		"DisplayName":     appName,
		"DisplayVersion":  version,
		"Publisher":       "Suno Pesme Studio",
		"InstallLocation": target,
		"UninstallString": `"` + uninstallExe + `"`,
		"DisplayIcon":     filepath.Join(target, appName+".exe"),
	}
	for name, value := range values {
		if err := regSetString(key, name, value); err != nil {
			return err
		}
	}
	if err := regSetDword(key, "NoModify", 1); err != nil {
		return err
	}
	if err := regSetDword(key, "NoRepair", 1); err != nil {
		return err
	}
	return nil
}

func regSetString(key uintptr, name, value string) error {
	data, _ := syscall.UTF16FromString(value)
	r, _, _ := regSetValueExW.Call(
		key, uintptr(unsafe.Pointer(u16(name))), 0, regSz,
		uintptr(unsafe.Pointer(&data[0])), uintptr(len(data)*2),
	)
	if r != errorSuccess {
		return fmt.Errorf("RegSetValueExW %s: %d", name, r)
	}
	return nil
}

func regSetDword(key uintptr, name string, value uint32) error {
	r, _, _ := regSetValueExW.Call(
		key, uintptr(unsafe.Pointer(u16(name))), 0, regDword,
		uintptr(unsafe.Pointer(&value)), 4,
	)
	if r != errorSuccess {
		return fmt.Errorf("RegSetValueExW %s: %d", name, r)
	}
	return nil
}

func requestLocalShutdown(dataRoot string, log func(string)) {
	_ = os.MkdirAll(filepath.Join(dataRoot, "data"), 0755)
	_ = os.WriteFile(filepath.Join(dataRoot, "data", "watchdog.stop"), []byte("stop\r\n"), 0600)
	client := &http.Client{Timeout: 2 * time.Second}
	req, _ := http.NewRequest(http.MethodPost, "http://127.0.0.1:8765/api/shutdown", strings.NewReader("{}"))
	req.Header.Set("Content-Type", "application/json")
	if resp, err := client.Do(req); err == nil {
		_ = resp.Body.Close()
		log("Poslat zahtev za gašenje stare lokalne instance.")
	}
	time.Sleep(1200 * time.Millisecond)
}

func downloadFileOnce(url, dst string, log func(string)) error {
	if err := os.MkdirAll(filepath.Dir(dst), 0755); err != nil {
		return err
	}
	tmp := dst + ".part"
	_ = os.Remove(tmp)
	client := &http.Client{Timeout: 35 * time.Minute}
	req, err := http.NewRequest(http.MethodGet, url, nil)
	if err != nil {
		return err
	}
	req.Header.Set("User-Agent", "Suno-Pesme-Studio-Installer/3.3.2")
	resp, err := client.Do(req)
	if err != nil {
		return fmt.Errorf("%s: %w", url, err)
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("%s: HTTP %s", url, resp.Status)
	}
	out, err := os.Create(tmp)
	if err != nil {
		return err
	}
	_, copyErr := io.Copy(out, resp.Body)
	closeErr := out.Close()
	if copyErr != nil {
		_ = os.Remove(tmp)
		return copyErr
	}
	if closeErr != nil {
		_ = os.Remove(tmp)
		return closeErr
	}
	st, err := os.Stat(tmp)
	if err != nil || st.Size() == 0 {
		_ = os.Remove(tmp)
		return fmt.Errorf("preuzet je prazan fajl")
	}
	_ = os.Remove(dst)
	if err = os.Rename(tmp, dst); err != nil {
		_ = os.Remove(tmp)
		return err
	}
	log(fmt.Sprintf("Preuzeto: %s (%d bajtova)", url, st.Size()))
	return nil
}

func downloadFile(url, dst string, log func(string)) error {
	var last error
	for attempt := 1; attempt <= 3; attempt++ {
		if err := downloadFileOnce(url, dst, log); err == nil {
			return nil
		} else {
			last = err
			log(fmt.Sprintf("Preuzimanje nije uspelo (%d/3): %v", attempt, err))
			_ = os.Remove(dst)
			_ = os.Remove(dst + ".part")
			time.Sleep(time.Duration(attempt) * 2 * time.Second)
		}
	}
	return last
}

func downloadAny(urls []string, dst string, log func(string)) error {
	var errors []string
	for _, url := range urls {
		if err := downloadFile(url, dst, log); err == nil {
			return nil
		} else {
			errors = append(errors, err.Error())
		}
	}
	return fmt.Errorf("nijedan zvanični izvor nije uspeo:\r\n%s", strings.Join(errors, "\r\n"))
}

func verifyFileSHA(path, expected string) error {
	got, err := fileHash(path)
	if err != nil {
		return err
	}
	expected = strings.ToLower(strings.TrimSpace(expected))
	if expected == "" || !strings.EqualFold(got, expected) {
		return fmt.Errorf("SHA-256 nije ispravan za %s; očekivano %s, dobijeno %s", filepath.Base(path), expected, got)
	}
	return nil
}

func checksumFromFile(path, wanted string) (string, error) {
	b, err := os.ReadFile(path)
	if err != nil {
		return "", err
	}
	for _, line := range strings.Split(string(b), "\n") {
		fields := strings.Fields(strings.TrimSpace(line))
		if len(fields) == 0 {
			continue
		}
		h := strings.TrimSpace(fields[0])
		if len(h) != 64 {
			continue
		}
		if wanted == "" || strings.Contains(strings.ToLower(line), strings.ToLower(wanted)) {
			return h, nil
		}
	}
	return "", fmt.Errorf("kontrolni zbir za %s nije pronađen", wanted)
}

func extractZipAll(zipPath, dst string) error {
	r, err := zip.OpenReader(zipPath)
	if err != nil {
		return err
	}
	defer r.Close()
	base := filepath.Clean(dst) + string(os.PathSeparator)
	for _, f := range r.File {
		name := filepath.Clean(filepath.Join(dst, filepath.FromSlash(f.Name)))
		if !strings.HasPrefix(strings.ToLower(name), strings.ToLower(base)) {
			return fmt.Errorf("nebezbedna ZIP putanja: %s", f.Name)
		}
		if f.FileInfo().IsDir() {
			if err := os.MkdirAll(name, 0755); err != nil {
				return err
			}
			continue
		}
		if err := os.MkdirAll(filepath.Dir(name), 0755); err != nil {
			return err
		}
		in, err := f.Open()
		if err != nil {
			return err
		}
		out, err := os.Create(name)
		if err != nil {
			in.Close()
			return err
		}
		_, e1 := io.Copy(out, in)
		e2 := out.Close()
		e3 := in.Close()
		if e1 != nil {
			return e1
		}
		if e2 != nil {
			return e2
		}
		if e3 != nil {
			return e3
		}
	}
	return nil
}

func extractNamedFromZip(zipPath, dstDir string, wanted map[string]string) error {
	r, err := zip.OpenReader(zipPath)
	if err != nil {
		return err
	}
	defer r.Close()
	found := map[string]bool{}
	for _, f := range r.File {
		base := strings.ToLower(filepath.Base(filepath.FromSlash(f.Name)))
		targetName, ok := wanted[base]
		if !ok || f.FileInfo().IsDir() {
			continue
		}
		if err := os.MkdirAll(dstDir, 0755); err != nil {
			return err
		}
		in, err := f.Open()
		if err != nil {
			return err
		}
		outPath := filepath.Join(dstDir, targetName)
		out, err := os.Create(outPath)
		if err != nil {
			in.Close()
			return err
		}
		_, e1 := io.Copy(out, in)
		e2 := out.Close()
		e3 := in.Close()
		if e1 != nil {
			return e1
		}
		if e2 != nil {
			return e2
		}
		if e3 != nil {
			return e3
		}
		found[base] = true
	}
	for name := range wanted {
		if !found[name] {
			return fmt.Errorf("%s nije pronađen u ZIP paketu", name)
		}
	}
	return nil
}

func fileReady(path string, minSize int64) bool {
	st, err := os.Stat(path)
	return err == nil && !st.IsDir() && st.Size() >= minSize
}

func verifyPE(path string) error {
	f, err := os.Open(path)
	if err != nil {
		return err
	}
	defer f.Close()
	var mz [2]byte
	if _, err = io.ReadFull(f, mz[:]); err != nil {
		return err
	}
	if mz[0] != 'M' || mz[1] != 'Z' {
		return fmt.Errorf("%s nije Windows EXE", filepath.Base(path))
	}
	return nil
}

func runTool(path string, args ...string) error {
	if err := verifyPE(path); err != nil {
		return err
	}
	cmd := exec.Command(path, args...)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
	if out, err := cmd.CombinedOutput(); err != nil {
		text := strings.TrimSpace(string(out))
		if len(text) > 500 {
			text = text[:500]
		}
		return fmt.Errorf("%s provera nije prošla: %v %s", filepath.Base(path), err, text)
	}
	return nil
}

func findReusableFile(bases []string, rel string) string {
	relClean := strings.ToLower(filepath.Clean(rel))
	for _, base := range bases {
		if strings.TrimSpace(base) == "" {
			continue
		}
		direct := filepath.Join(base, rel)
		if fileReady(direct, 2) {
			return direct
		}
		base = filepath.Clean(base)
		_ = filepath.WalkDir(base, func(path string, d os.DirEntry, err error) error {
			if err != nil || d == nil {
				return nil
			}
			if d.IsDir() {
				r, _ := filepath.Rel(base, path)
				if len(strings.Split(filepath.Clean(r), string(os.PathSeparator))) > 6 {
					return filepath.SkipDir
				}
				return nil
			}
			lower := strings.ToLower(filepath.Clean(path))
			if strings.HasSuffix(lower, relClean) && fileReady(path, 2) {
				direct = path
				return fmt.Errorf("found")
			}
			return nil
		})
		if fileReady(direct, 2) {
			return direct
		}
	}
	return ""
}

func reuseComponents(stage string, bases []string, log func(string)) {
	rels := []string{
		filepath.Join("python", "python.exe"), filepath.Join("python", "pythonw.exe"),
		filepath.Join("tools", "ffmpeg", "bin", "ffmpeg.exe"), filepath.Join("tools", "ffmpeg", "bin", "ffprobe.exe"),
		filepath.Join("tools", "yt-dlp", "yt-dlp.exe"), filepath.Join("tools", "deno", "deno.exe"),
	}
	for _, rel := range rels {
		target := filepath.Join(stage, rel)
		if fileReady(target, 2) {
			continue
		}
		if src := findReusableFile(bases, rel); src != "" {
			if err := copyFile(src, target); err == nil {
				log("Ponovo korišćena postojeća komponenta: " + src)
			}
		}
	}
	// Python mora da se kopira kao ceo embeddable folder, ne samo dva EXE fajla.
	if srcExe := findReusableFile(bases, filepath.Join("python", "python.exe")); srcExe != "" {
		srcDir := filepath.Dir(srcExe)
		if fileReady(filepath.Join(srcDir, "pythonw.exe"), 2) {
			_ = os.RemoveAll(filepath.Join(stage, "python"))
			if err := copyTree(srcDir, filepath.Join(stage, "python")); err == nil {
				log("Ponovo korišćen kompletan postojeći Python folder: " + srcDir)
			}
		}
	}
}

func downloadVerified(urls []string, hashes []string, dst string, log func(string)) error {
	var errs []string
	for i, url := range urls {
		if err := downloadFile(url, dst, log); err != nil {
			errs = append(errs, err.Error())
			continue
		}
		if i < len(hashes) && strings.TrimSpace(hashes[i]) != "" {
			if err := verifyFileSHA(dst, hashes[i]); err != nil {
				errs = append(errs, err.Error())
				_ = os.Remove(dst)
				continue
			}
		}
		return nil
	}
	return fmt.Errorf("preuzimanje ili provera nije uspela:\r\n%s", strings.Join(errs, "\r\n"))
}

func prepareComponents(stage string, reuseBases []string, log func(string)) error {
	cache := filepath.Join(os.TempDir(), "SunoPesmeStudio-component-cache")
	if err := os.MkdirAll(cache, 0755); err != nil {
		return err
	}

	reuseComponents(stage, reuseBases, log)

	pythonDir := filepath.Join(stage, "python")
	if !fileReady(filepath.Join(pythonDir, "python.exe"), 100000) || !fileReady(filepath.Join(pythonDir, "pythonw.exe"), 100000) {
		log("Python nije pronađen u paketu/prethodnoj instalaciji; preuzimam zvanični embeddable paket")
		// Use the same single pinned runtime as the current component stage.
		// Keeping obsolete fallback versions here made a clean build capable
		// of silently installing a different Python than the one CI tested.
		pythonZip := filepath.Join(cache, "python.zip")
		urls := []string{currentPythonURL}
		hashes := []string{currentPythonSHA256}
		if err := downloadVerified(urls, hashes, pythonZip, log); err != nil {
			return fmt.Errorf("Python: %w", err)
		}
		_ = os.RemoveAll(pythonDir)
		if err := extractZipAll(pythonZip, pythonDir); err != nil {
			return fmt.Errorf("Python raspakivanje: %w", err)
		}
	}

	ffDir := filepath.Join(stage, "tools", "ffmpeg", "bin")
	ffmpegExe := filepath.Join(ffDir, "ffmpeg.exe")
	ffprobeExe := filepath.Join(ffDir, "ffprobe.exe")
	if !fileReady(ffmpegExe, 1000000) || !fileReady(ffprobeExe, 1000000) {
		log("FFmpeg nije pronađen; preuzimam provereni BtbN full GPL paket sa Chromaprintom")
		if err := stageChromaprintFFmpeg(stage, log); err != nil { return fmt.Errorf("FFmpeg: %w", err) }
	}

	ytdlp := filepath.Join(stage, "tools", "yt-dlp", "yt-dlp.exe")
	if !fileReady(ytdlp, 1000000) {
		log("yt-dlp nije pronađen; preuzimam zvanični Windows EXE")
		if err := downloadVerified([]string{
			"https://github.com/yt-dlp/yt-dlp/releases/download/2026.07.04/yt-dlp.exe",
			"https://github.com/yt-dlp/yt-dlp/releases/download/2026.06.09/yt-dlp.exe",
		}, []string{
			"52fe3c26dcf71fbdc85b528589020bb0b8e383155cfa81b64dd447bbe35e24b8",
			"3a48cb955d55c8821b60ccbdbbc6f61bc958f2f3d3b7ad5eaf3d83a543293a27",
		}, ytdlp, log); err != nil {
			log("Pinovana yt-dlp izdanja nisu uspela; pokušavam latest: " + err.Error())
			if e2 := downloadFile("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", ytdlp, log); e2 != nil {
				return fmt.Errorf("yt-dlp: %v; latest: %v", err, e2)
			}
		}
	}

	denoDir := filepath.Join(stage, "tools", "deno")
	denoExe := filepath.Join(denoDir, "deno.exe")
	if !fileReady(denoExe, 1000000) {
		log("Deno nije pronađen; preuzimam aktuelni paket sa zvaničnom SHA-256 proverom")
		if err := stageCurrentDeno(stage, log); err != nil { return fmt.Errorf("Deno: %w", err) }
	}

	checks := []struct {
		path string
		args []string
	}{
		{filepath.Join(pythonDir, "python.exe"), []string{"--version"}},
		{filepath.Join(pythonDir, "pythonw.exe"), []string{"-c", "import sys; sys.exit(0)"}},
		{ffmpegExe, []string{"-version"}},
		{ffprobeExe, []string{"-version"}},
		{ytdlp, []string{"--version"}},
		{denoExe, []string{"--version"}},
	}
	for _, check := range checks {
		if !fileReady(check.path, 100000) {
			return fmt.Errorf("komponenta nedostaje ili je premala: %s", check.path)
		}
		if err := runTool(check.path, check.args...); err != nil {
			return err
		}
		log("Provera komponente uspešna: " + check.path)
	}
	log("Sve osnovne komponente su fizički prisutne i uspešno pokrenute pre aktiviranja verzije.")
	return nil
}

var _ = stgmRead

func checkInstallLocationWritable(installRoot string) error {
	parent := filepath.Dir(filepath.Clean(installRoot))
	if err := os.MkdirAll(parent, 0755); err != nil {
		return err
	}
	probeDir, err := os.MkdirTemp(parent, ".suno-studio-provera-")
	if err != nil {
		return err
	}
	defer os.RemoveAll(probeDir)
	probe := filepath.Join(probeDir, "provera.tmp")
	if err := os.WriteFile(probe, []byte("ok"), 0600); err != nil {
		return err
	}
	return nil
}

func browseInstallFolder(title string) (string, error) {
	display := make([]uint16, 32768)
	bi := browseInfoW{PszDisplayName: &display[0], LpszTitle: u16(title), UlFlags: bifReturnOnlyFSDirs | bifNewDialogStyle | bifEditBox}
	pidl, _, callErr := shBrowseForFolderW.Call(uintptr(unsafe.Pointer(&bi)))
	if pidl == 0 {
		if callErr != syscall.Errno(0) {
			return "", callErr
		}
		return "", nil
	}
	defer coTaskMemFree.Call(pidl)
	buf := make([]uint16, 32768)
	r, _, e := shGetPathFromIDListW.Call(pidl, uintptr(unsafe.Pointer(&buf[0])))
	if r == 0 {
		return "", e
	}
	return syscall.UTF16ToString(buf), nil
}
