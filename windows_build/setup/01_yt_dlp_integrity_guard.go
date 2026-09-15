//go:build windows

package main

import (
	"fmt"
	"net/http"
	"strings"
)

// ytDlpIntegrityGuard prevents the legacy emergency "latest" download in
// setup/main.go from ever becoming an unverified executable. The normal pinned
// yt-dlp releases are SHA-256 checked by downloadVerified and pass through.
//
// Runtime self-update in the Python application has its own official
// SHA2-256SUMS verification; this guard is specifically for the native Windows
// installer/build path. If both pinned releases are unavailable, installation
// now fails closed instead of executing bytes whose digest was never checked.
type ytDlpIntegrityGuard struct {
	base http.RoundTripper
}

func (g *ytDlpIntegrityGuard) RoundTrip(req *http.Request) (*http.Response, error) {
	if req != nil && req.URL != nil &&
		strings.EqualFold(req.URL.Hostname(), "github.com") &&
		strings.EqualFold(req.URL.Path, "/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe") {
		return nil, fmt.Errorf("unverified yt-dlp latest fallback is blocked; a SHA-256-pinned release is required")
	}
	return g.base.RoundTrip(req)
}

func init() {
	base := http.DefaultTransport
	if base == nil {
		base = http.NewFileTransport(http.Dir("."))
	}
	http.DefaultTransport = &ytDlpIntegrityGuard{base: base}
}
