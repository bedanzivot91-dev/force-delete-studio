//go:build windows

package main

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

// pathInside reports whether child is child-or-descendant of parent without
// relying on a string prefix (C:\App2 must not count as inside C:\App).
func pathInside(parent, child string) bool {
	p, err := filepath.Abs(filepath.Clean(parent))
	if err != nil {
		return false
	}
	c, err := filepath.Abs(filepath.Clean(child))
	if err != nil {
		return false
	}
	rel, err := filepath.Rel(p, c)
	if err != nil {
		return false
	}
	return rel == "." || (rel != ".." && !strings.HasPrefix(rel, ".."+string(os.PathSeparator)))
}

func validateNativeRemoveTarget(target string) error {
	clean, err := filepath.Abs(filepath.Clean(strings.TrimSpace(target)))
	if err != nil || strings.TrimSpace(target) == "" {
		return fmt.Errorf("neispravna putanja instalacije")
	}
	if strings.EqualFold(filepath.Dir(clean), clean) {
		return fmt.Errorf("odbijeno brisanje korena diska: %s", clean)
	}
	if !strings.EqualFold(filepath.Base(clean), appName) {
		return fmt.Errorf("ciljni folder nije %s: %s", appName, clean)
	}

	activeMarker := filepath.Join(clean, "AKTIVNA_VERZIJA.txt")
	b, err := os.ReadFile(activeMarker)
	if err != nil {
		return fmt.Errorf("nedostaje validan instalacioni marker %s: %w", activeMarker, err)
	}
	active := filepath.Clean(strings.TrimSpace(string(b)))
	if active == "" {
		return fmt.Errorf("instalacioni marker je prazan")
	}
	versionsRoot := filepath.Join(clean, "Versions")
	if !pathInside(versionsRoot, active) || strings.EqualFold(filepath.Clean(active), filepath.Clean(versionsRoot)) {
		return fmt.Errorf("aktivna verzija nije unutar Versions foldera: %s", active)
	}
	for _, required := range []string{
		filepath.Join(active, appName+".exe"),
		filepath.Join(active, "Deinstaliraj "+appName+".exe"),
	} {
		st, statErr := os.Stat(required)
		if statErr != nil || st.IsDir() || st.Size() == 0 {
			return fmt.Errorf("cilj nije kompletna Suno instalacija; nedostaje %s", required)
		}
	}
	return nil
}

func init() {
	args := os.Args[1:]
	if len(args) >= 4 && args[0] == "--native-remove" {
		if err := validateNativeRemoveTarget(args[1]); err != nil {
			fmt.Fprintln(os.Stderr, "native-remove refused:", err)
			os.Exit(2)
		}
	}
}
