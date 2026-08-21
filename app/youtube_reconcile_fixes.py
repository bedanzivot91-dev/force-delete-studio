from __future__ import annotations

"""Targeted YouTube <-> Suno reconciliation fixes.

These patches address two real failure modes that are different from the
large-library performance work in runtime_fixes.py:

1. A Suno song can be fully fingerprinted/indexed while its temporary CDN URL
   has expired or disappeared.  The old YouTube analyser still demanded a
   CURRENT audio source before it would compare that already-cached fingerprint,
   so an indexed song could be skipped with "bez audio izvora" and never match.

2. OAuth can list private/unlisted uploads, but yt-dlp does not consume the
   YouTube Data API OAuth token for media playback.  When such a video is owned
   by the user, yt-dlp needs browser cookies.  We now automatically retry the
   browsers that actually exist on the Windows machine and remember the first
   one that works, instead of failing every private video with "Sign in".

No cookie values are read or stored by this module.  yt-dlp performs its normal
local --cookies-from-browser integration and only the browser NAME is persisted.
"""

import os
import threading
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any


@dataclass(frozen=True)
class _CachedFingerprintSource:
    song_id: str


_AUTH_LOCK = threading.RLock()
_AUTH_WORKING_BROWSER = ""
_AUTH_FAILED_UNTIL: dict[str, float] = {}


def _auth_error(exc: BaseException) -> bool:
    text = str(exc or "").casefold()
    needles = (
        "private video",
        "sign in",
        "cookies-from-browser",
        "cookies for the authentication",
        "authentication",
        "login required",
        "confirm you're not a bot",
        "confirm you’re not a bot",
        "age-restricted",
    )
    return any(token in text for token in needles)


def _browser_profile_exists(browser: str) -> bool:
    local = Path(os.environ.get("LOCALAPPDATA") or "")
    roaming = Path(os.environ.get("APPDATA") or "")
    candidates = {
        "edge": [local / "Microsoft" / "Edge" / "User Data"],
        "chrome": [local / "Google" / "Chrome" / "User Data"],
        "brave": [local / "BraveSoftware" / "Brave-Browser" / "User Data"],
        "vivaldi": [local / "Vivaldi" / "User Data"],
        "opera": [roaming / "Opera Software" / "Opera Stable"],
        "firefox": [roaming / "Mozilla" / "Firefox" / "Profiles"],
    }
    return any(path.is_dir() for path in candidates.get(browser, []))


def _browser_candidates(configured: str = "") -> list[str]:
    configured = str(configured or "").strip().lower()
    ordered: list[str] = []
    with _AUTH_LOCK:
        if _AUTH_WORKING_BROWSER:
            ordered.append(_AUTH_WORKING_BROWSER)
    if configured:
        ordered.append(configured)
    # Edge first on Windows because it is always present on normal Windows 10/11
    # installations; then the browsers the application already supports.
    ordered.extend(["edge", "chrome", "brave", "firefox", "vivaldi", "opera"])
    now = time.monotonic()
    result: list[str] = []
    for browser in ordered:
        if browser in result or not _browser_profile_exists(browser):
            continue
        with _AUTH_LOCK:
            failed_until = float(_AUTH_FAILED_UNTIL.get(browser, 0.0) or 0.0)
        if failed_until > now and browser != configured:
            continue
        result.append(browser)
    return result


def _remember_browser(core: Any, browser: str) -> None:
    global _AUTH_WORKING_BROWSER
    browser = str(browser or "").strip().lower()
    if not browser:
        return
    with _AUTH_LOCK:
        _AUTH_WORKING_BROWSER = browser
        _AUTH_FAILED_UNTIL.pop(browser, None)
    try:
        core.DB.set_setting("youtube_cookies_browser", browser)
    except Exception:
        pass
    try:
        core.runtime_log(f"YouTube privatni video: automatska prijava radi preko browsera {browser}.", "info")
    except Exception:
        pass


def _mark_browser_failed(browser: str) -> None:
    with _AUTH_LOCK:
        _AUTH_FAILED_UNTIL[browser] = time.monotonic() + 15 * 60


def _install_cached_fingerprint_sources(core: Any) -> dict[str, Any]:
    original_source = core._song_audio_source_for_match
    original_signature = core._signature_for_source

    def source_for_match(song: dict[str, Any]) -> Any:
        # IMPORTANT: do not call original_source() first. The mature resolver
        # performs get_clip() when both local_audio and audio_url are missing.
        # That would make a 3000-song already-indexed library hit Suno again and
        # defeats the entire point of a persistent fingerprint cache.
        local = core._existing_audio_path(song)
        if local is not None:
            return local
        remote = str(song.get("audio_url") or "").strip()
        if remote:
            return remote

        song_id = str(song.get("id") or "")
        if not song_id:
            return None
        cached = core.DB.get_audio_fingerprint("suno", song_id, core.AUDIO_MATCH_VERSION)
        if cached and cached.get("payload"):
            # This is the key correctness fix: "indexed" now really means the
            # YouTube matcher can use the song even when its old Suno CDN URL
            # is no longer available. No Suno network request happens here.
            return _CachedFingerprintSource(song_id)

        # Only a song that has neither a live source NOR a cached fingerprint
        # is allowed to fall through to the old get_clip() refresh behavior.
        return original_source(song)

    def signature_for_source(
        source_type: str,
        source_id: str,
        source: Any,
        task: Any = None,
        label: str = "",
        force: bool = False,
    ) -> dict[str, Any]:
        if not isinstance(source, _CachedFingerprintSource):
            return original_signature(source_type, source_id, source, task, label, force)

        song_id = str(source.song_id or source_id or "")
        cached = core.DB.get_audio_fingerprint("suno", song_id, core.AUDIO_MATCH_VERSION)
        if cached and cached.get("payload"):
            # A true force rebuild should use a live source when one can be
            # refreshed. If there is no live source, the existing fingerprint
            # is still much better than falsely declaring an indexed song
            # unmatchable.
            if force:
                try:
                    song = core.DB.get_song(song_id) or {"id": song_id}
                    live = original_source(song)
                    if live is not None:
                        return original_signature(source_type, song_id, live, task, label, True)
                except Exception:
                    pass
            try:
                signature = core.unpack_signature(cached.get("payload") or b"")
                if signature:
                    return signature
            except Exception:
                pass

        # Cached row is corrupt/stale. Give the mature source resolver one final
        # chance to refresh the Suno URL rather than silently returning a false
        # "not found".
        song = core.DB.get_song(song_id) or {"id": song_id}
        live = original_source(song)
        if live is None:
            raise core.AudioMatchError(
                f"Suno pesma {song_id} ima zapis u indeksu, ali je otisak nečitljiv i audio izvor trenutno nije dostupan."
            )
        return original_signature(source_type, song_id, live, task, label, force)

    core._song_audio_source_for_match = source_for_match
    core._signature_for_source = signature_for_source
    return {
        "_song_audio_source_for_match": source_for_match,
        "_signature_for_source": signature_for_source,
    }


def _install_ytdlp_auth_fallback(core: Any) -> dict[str, Any]:
    original_download = core.download_youtube_audio
    original_inspect = core.inspect_youtube_video

    def download_youtube_audio(
        video_url: str,
        progress: Any = None,
        cancel_check: Any = None,
        reuse_cache: bool = True,
        cookie_browser: str = "",
    ) -> Path:
        configured = str(cookie_browser or "").strip().lower()
        first_error: BaseException | None = None
        try:
            return original_download(
                video_url,
                progress,
                cancel_check,
                reuse_cache=reuse_cache,
                cookie_browser=configured,
            )
        except BaseException as exc:
            if not _auth_error(exc):
                raise
            first_error = exc

        tried: list[str] = [configured] if configured else []
        for browser in _browser_candidates(configured):
            if browser == configured:
                continue
            if cancel_check and cancel_check():
                raise core.AudioMatchCancelled("YouTube audio analiza je zaustavljena.")
            tried.append(browser)
            try:
                if progress:
                    progress(f"Privatni YouTube video: pokušavam prijavu preko {browser} browsera...", 1)
                result = original_download(
                    video_url,
                    progress,
                    cancel_check,
                    reuse_cache=reuse_cache,
                    cookie_browser=browser,
                )
                _remember_browser(core, browser)
                return result
            except BaseException as exc:
                if not _auth_error(exc):
                    raise
                _mark_browser_failed(browser)

        attempted = ", ".join(x for x in tried if x) or "nijedan prijavljeni browser nije pronađen"
        raise core.AudioMatchError(
            "YouTube video zahteva prijavu. Program je automatski pokušao browser cookies "
            f"({attempted}), ali nije dobio pristup. Otvori YouTube u Edge/Chrome/Brave/Firefox browseru, "
            "prijavi isti nalog koji poseduje kanal i ponovi proveru. Originalna greška: "
            + str(first_error or "authentication required")
        )

    def inspect_youtube_video(
        video_url: str,
        cancel_check: Any = None,
        cookie_browser: str = "",
    ) -> dict[str, Any]:
        configured = str(cookie_browser or "").strip().lower()
        first_error: BaseException | None = None
        try:
            return original_inspect(video_url, cancel_check, configured)
        except BaseException as exc:
            if not _auth_error(exc):
                raise
            first_error = exc
        for browser in _browser_candidates(configured):
            if browser == configured:
                continue
            try:
                result = original_inspect(video_url, cancel_check, browser)
                _remember_browser(core, browser)
                return result
            except BaseException as exc:
                if not _auth_error(exc):
                    raise
                _mark_browser_failed(browser)
        raise core.AudioMatchError(str(first_error or "YouTube video zahteva prijavu."))

    core.download_youtube_audio = download_youtube_audio
    core.inspect_youtube_video = inspect_youtube_video
    return {
        "download_youtube_audio": download_youtube_audio,
        "inspect_youtube_video": inspect_youtube_video,
    }


def apply(core: Any) -> dict[str, Any]:
    if getattr(core, "_youtube_reconcile_fixes_v1_installed", False):
        return {}
    exports: dict[str, Any] = {}
    exports.update(_install_cached_fingerprint_sources(core))
    exports.update(_install_ytdlp_auth_fallback(core))
    core._youtube_reconcile_fixes_v1_installed = True
    core.runtime_log(
        "YouTube/Suno reconciliation fixes aktivni: indeksirani otisci rade bez starog Suno URL-a; privatni YouTube video dobija browser-auth fallback.",
        "info",
    )
    return exports
