//go:build windows

package main

import (
	"fmt"
	"os"
	"strings"
)

// --stage-components is a build/CI maintenance entry point compiled into the
// same native setup binary as the end-user GUI. It downloads native tools into
// a caller-provided target and executes them for validation, so it must not be
// an undocumented general-purpose mode on a user's machine.
func init() {
	if len(os.Args) > 2 && os.Args[1] == "--stage-components" {
		if !strings.EqualFold(strings.TrimSpace(os.Getenv("GITHUB_ACTIONS")), "true") {
			fmt.Fprintln(os.Stderr, "--stage-components is available only inside GitHub Actions")
			os.Exit(2)
		}
	}
}
