//go:build windows

package main

import (
	"fmt"
	"os"
	"syscall"
	"time"
	"unsafe"
)

const (
	fallbackClassName = "NPSunoUnifiedFallbackWindow"
	fallbackSunoID    = 1001
	fallbackVideoID   = 1002

	wmCommand = 0x0111
	wmDestroy = 0x0002
	wmSetFont = 0x0030

	wsOverlappedWindow = 0x00CF0000
	wsChild            = 0x40000000
	wsVisible          = 0x10000000
	wsTabStop          = 0x00010000

	swShow         = 5
	defaultGUIFont = 17
	colorWindow    = 5
)

var (
	fallbackUser32 = syscall.NewLazyDLL("user32.dll")
	fallbackKernel = syscall.NewLazyDLL("kernel32.dll")
	fallbackGDI32  = syscall.NewLazyDLL("gdi32.dll")

	fallbackRegisterClassEx = fallbackUser32.NewProc("RegisterClassExW")
	fallbackCreateWindowEx  = fallbackUser32.NewProc("CreateWindowExW")
	fallbackDefWindowProc   = fallbackUser32.NewProc("DefWindowProcW")
	fallbackShowWindow      = fallbackUser32.NewProc("ShowWindow")
	fallbackUpdateWindow    = fallbackUser32.NewProc("UpdateWindow")
	fallbackGetMessage      = fallbackUser32.NewProc("GetMessageW")
	fallbackTranslate       = fallbackUser32.NewProc("TranslateMessage")
	fallbackDispatch        = fallbackUser32.NewProc("DispatchMessageW")
	fallbackPostQuit        = fallbackUser32.NewProc("PostQuitMessage")
	fallbackSendMessage     = fallbackUser32.NewProc("SendMessageW")
	fallbackGetSystemMetric = fallbackUser32.NewProc("GetSystemMetrics")
	fallbackMessageBox      = fallbackUser32.NewProc("MessageBoxW")
	fallbackGetModuleHandle = fallbackKernel.NewProc("GetModuleHandleW")
	fallbackGetStockObject  = fallbackGDI32.NewProc("GetStockObject")

	fallbackRoot string
)

type fallbackPoint struct {
	X int32
	Y int32
}

type fallbackMsg struct {
	Hwnd     syscall.Handle
	Message  uint32
	WParam   uintptr
	LParam   uintptr
	Time     uint32
	Pt       fallbackPoint
	LPrivate uint32
}

type fallbackWndClassEx struct {
	CbSize        uint32
	Style         uint32
	LpfnWndProc   uintptr
	CbClsExtra    int32
	CbWndExtra    int32
	HInstance     syscall.Handle
	HIcon         syscall.Handle
	HCursor       syscall.Handle
	HbrBackground syscall.Handle
	LpszMenuName  *uint16
	LpszClassName *uint16
	HIconSm       syscall.Handle
}

func fallbackUTF16(s string) *uint16 {
	p, _ := syscall.UTF16PtrFromString(s)
	return p
}

func fallbackShowError(text string) {
	fallbackMessageBox.Call(
		0,
		uintptr(unsafe.Pointer(fallbackUTF16(text))),
		uintptr(unsafe.Pointer(fallbackUTF16(productName))),
		0x10,
	)
}

func fallbackWndProc(hwnd uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	switch msg {
	case wmCommand:
		switch int(wParam & 0xffff) {
		case fallbackSunoID:
			if err := launchModule(fallbackRoot, "suno"); err != nil {
				fallbackShowError(err.Error())
			}
			return 0
		case fallbackVideoID:
			if err := launchModule(fallbackRoot, "video"); err != nil {
				fallbackShowError(err.Error())
			}
			return 0
		}
	case wmDestroy:
		fallbackPostQuit.Call(0)
		return 0
	}
	ret, _, _ := fallbackDefWindowProc.Call(hwnd, uintptr(msg), wParam, lParam)
	return ret
}

func fallbackControl(parent uintptr, className, text string, id, x, y, width, height int) uintptr {
	style := uintptr(wsChild | wsVisible)
	if className == "BUTTON" {
		style |= wsTabStop
	}
	hwnd, _, _ := fallbackCreateWindowEx.Call(
		0,
		uintptr(unsafe.Pointer(fallbackUTF16(className))),
		uintptr(unsafe.Pointer(fallbackUTF16(text))),
		style,
		uintptr(x), uintptr(y), uintptr(width), uintptr(height),
		parent,
		uintptr(id),
		0,
		0,
	)
	if hwnd != 0 {
		font, _, _ := fallbackGetStockObject.Call(defaultGUIFont)
		fallbackSendMessage.Call(hwnd, wmSetFont, font, 1)
	}
	return hwnd
}

func runNativeFallback(root string, webviewErr error) error {
	fallbackRoot = root

	hInstance, _, _ := fallbackGetModuleHandle.Call(0)
	className := fallbackUTF16(fallbackClassName)
	wc := fallbackWndClassEx{
		CbSize:        uint32(unsafe.Sizeof(fallbackWndClassEx{})),
		LpfnWndProc:   syscall.NewCallback(fallbackWndProc),
		HInstance:     syscall.Handle(hInstance),
		HbrBackground: syscall.Handle(colorWindow + 1),
		LpszClassName: className,
	}
	atom, _, registerErr := fallbackRegisterClassEx.Call(uintptr(unsafe.Pointer(&wc)))
	if atom == 0 && registerErr != syscall.Errno(1410) { // ERROR_CLASS_ALREADY_EXISTS
		return fmt.Errorf("native launcher klasa nije mogla da se registruje: %v", registerErr)
	}

	const width, height = 780, 430
	screenW, _, _ := fallbackGetSystemMetric.Call(0)
	screenH, _, _ := fallbackGetSystemMetric.Call(1)
	x := (int(screenW) - width) / 2
	y := (int(screenH) - height) / 2
	if x < 0 {
		x = 0
	}
	if y < 0 {
		y = 0
	}

	hwnd, _, createErr := fallbackCreateWindowEx.Call(
		0,
		uintptr(unsafe.Pointer(className)),
		uintptr(unsafe.Pointer(fallbackUTF16(windowTitle))),
		wsOverlappedWindow,
		uintptr(x), uintptr(y), width, height,
		0, 0, hInstance, 0,
	)
	if hwnd == 0 {
		return fmt.Errorf("native launcher prozor nije mogao da se napravi: %v", createErr)
	}

	fallbackControl(hwnd, "STATIC", "NP Suno Unified Studio", 0, 34, 32, 700, 34)
	fallbackControl(hwnd, "STATIC", "Bezbedni Windows launcher je aktivan. Oba ugrađena modula ostaju dostupna iz istog programa.", 0, 34, 78, 700, 28)

	status := modules(root)
	statusText := "Status modula:"
	for _, module := range status {
		state := "nije spreman"
		if module.Ready {
			state = "spreman"
		}
		statusText += "  " + module.Name + " — " + state + "."
	}
	fallbackControl(hwnd, "STATIC", statusText, 0, 34, 116, 700, 42)

	fallbackControl(hwnd, "BUTTON", "Otvori Suno / Biblioteka / YouTube", fallbackSunoID, 34, 194, 330, 58)
	fallbackControl(hwnd, "BUTTON", "Otvori NP Video Studio", fallbackVideoID, 386, 194, 330, 58)
	fallbackControl(hwnd, "STATIC", "Ako je WebView2 prikaz privremeno nedostupan, ovaj launcher sprečava tiho gašenje programa i pokreće iste instalirane module.", 0, 34, 286, 682, 44)

	if webviewErr != nil {
		fallbackControl(hwnd, "STATIC", "WebView2 detalj: "+webviewErr.Error(), 0, 34, 342, 682, 26)
	}

	fallbackShowWindow.Call(hwnd, swShow)
	fallbackUpdateWindow.Call(hwnd)

	var msg fallbackMsg
	for {
		ret, _, err := fallbackGetMessage.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
		if int32(ret) == -1 {
			return fmt.Errorf("native launcher message loop greška: %v", err)
		}
		if ret == 0 {
			return nil
		}
		fallbackTranslate.Call(uintptr(unsafe.Pointer(&msg)))
		fallbackDispatch.Call(uintptr(unsafe.Pointer(&msg)))
	}
}

// The pinned go-webview2 implementation can occasionally return from Run()
// immediately after a fresh WebView2 runtime install, before the shell becomes
// usable. The normal WebView2 shell remains the primary UI. If it dies during
// that startup window, keep the SAME process alive with a functional native
// launcher instead of silently exiting with code 0.
func init() {
	if len(os.Args) != 1 {
		return
	}

	root, err := rootDir()
	if err != nil {
		fallbackShowError(err.Error())
		os.Exit(2)
	}
	if err := selfTest(root); err != nil {
		fmt.Fprintln(os.Stderr, err)
	}

	started := time.Now()
	err = runUI(root)
	if err == nil && time.Since(started) >= 3*time.Second {
		os.Exit(0)
	}
	if err == nil {
		err = fmt.Errorf("WebView2 shell se zatvorio tokom početnog pokretanja")
	}
	if fallbackErr := runNativeFallback(root, err); fallbackErr != nil {
		fallbackShowError(fallbackErr.Error())
		os.Exit(5)
	}
	os.Exit(0)
}
