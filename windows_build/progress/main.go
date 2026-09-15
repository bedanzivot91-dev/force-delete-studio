//go:build windows

package main

import (
	"os"
	"strconv"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

const (
	wsOverlappedWindow  = 0x00CF0000
	wsVisible           = 0x10000000
	wsChild             = 0x40000000
	ssLeft              = 0x00000000
	wmDestroy           = 0x0002
	wmClose             = 0x0010
	swShow              = 5
	processSynchronize  = 0x00100000
	waitTimeoutProgress = 0x00000102
)

type wndClassEx struct {
	Size       uint32
	Style      uint32
	WndProc    uintptr
	ClsExtra   int32
	WndExtra   int32
	Instance   uintptr
	Icon       uintptr
	Cursor     uintptr
	Background uintptr
	MenuName   *uint16
	ClassName  *uint16
	IconSm     uintptr
}

type msg struct {
	Hwnd           uintptr
	Message        uint32
	WParam, LParam uintptr
	Time           uint32
	PtX, PtY       int32
	Private        uint32
}

var (
	u32                         = syscall.NewLazyDLL("user32.dll")
	k32                         = syscall.NewLazyDLL("kernel32.dll")
	registerClassEx             = u32.NewProc("RegisterClassExW")
	createWindowEx              = u32.NewProc("CreateWindowExW")
	defWindowProc               = u32.NewProc("DefWindowProcW")
	showWindow                  = u32.NewProc("ShowWindow")
	updateWindow                = u32.NewProc("UpdateWindow")
	getMessage                  = u32.NewProc("GetMessageW")
	translateMessage            = u32.NewProc("TranslateMessage")
	dispatchMessage             = u32.NewProc("DispatchMessageW")
	postQuitMessage             = u32.NewProc("PostQuitMessage")
	setWindowText               = u32.NewProc("SetWindowTextW")
	destroyWindow               = u32.NewProc("DestroyWindow")
	getModuleHandle             = k32.NewProc("GetModuleHandleW")
	openProcessProgress         = k32.NewProc("OpenProcess")
	waitForSingleObjectProgress = k32.NewProc("WaitForSingleObject")
	closeHandleProgress         = k32.NewProc("CloseHandle")
)

func p16(s string) *uint16 { p, _ := syscall.UTF16PtrFromString(s); return p }

var labelHwnd uintptr

func wndProc(hwnd uintptr, message uint32, wparam, lparam uintptr) uintptr {
	switch message {
	case wmClose:
		// Prozor se može zatvoriti, ali instalacija nastavlja bez prekida.
		destroyWindow.Call(hwnd)
		return 0
	case wmDestroy:
		postQuitMessage.Call(0)
		return 0
	}
	r, _, _ := defWindowProc.Call(hwnd, uintptr(message), wparam, lparam)
	return r
}

func main() {
	if len(os.Args) < 3 {
		return
	}
	statusPath := os.Args[1]
	pid64, _ := strconv.ParseUint(os.Args[2], 10, 32)
	hproc, _, _ := openProcessProgress.Call(processSynchronize, 0, uintptr(pid64))
	if hproc != 0 {
		defer closeHandleProgress.Call(hproc)
	}

	inst, _, _ := getModuleHandle.Call(0)
	cls := "SunoPesmeStudioInstallerProgress"
	wc := wndClassEx{Size: uint32(unsafe.Sizeof(wndClassEx{})), WndProc: syscall.NewCallback(wndProc), Instance: inst, Background: 6, ClassName: p16(cls)}
	registerClassEx.Call(uintptr(unsafe.Pointer(&wc)))
	hwnd, _, _ := createWindowEx.Call(0, uintptr(unsafe.Pointer(p16(cls))), uintptr(unsafe.Pointer(p16("Suno Pesme Studio — instalacija u toku"))), wsOverlappedWindow|wsVisible, 380, 220, 720, 220, 0, 0, inst, 0)
	if hwnd == 0 {
		return
	}
	labelHwnd, _, _ = createWindowEx.Call(0, uintptr(unsafe.Pointer(p16("STATIC"))), uintptr(unsafe.Pointer(p16("Priprema instalacije..."))), wsChild|wsVisible|ssLeft, 24, 35, 650, 115, hwnd, 0, inst, 0)
	showWindow.Call(hwnd, swShow)
	updateWindow.Call(hwnd)

	go func() {
		last := ""
		for {
			if hproc != 0 {
				r, _, _ := waitForSingleObjectProgress.Call(hproc, 0)
				if r != waitTimeoutProgress {
					destroyWindow.Call(hwnd)
					return
				}
			}
			if b, err := os.ReadFile(statusPath); err == nil {
				text := strings.TrimSpace(string(b))
				if text != "" && text != last {
					setWindowText.Call(labelHwnd, uintptr(unsafe.Pointer(p16(text))))
					last = text
				}
			}
			time.Sleep(300 * time.Millisecond)
		}
	}()

	var m msg
	for {
		r, _, _ := getMessage.Call(uintptr(unsafe.Pointer(&m)), 0, 0, 0)
		if int32(r) <= 0 {
			break
		}
		translateMessage.Call(uintptr(unsafe.Pointer(&m)))
		dispatchMessage.Call(uintptr(unsafe.Pointer(&m)))
	}
}
