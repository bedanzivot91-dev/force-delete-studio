from __future__ import annotations

import base64
import ctypes
import hashlib
import json
import os
import secrets
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from ctypes import wintypes
from pathlib import Path
from typing import Any


AUTH_SCOPE = "openid email https://www.googleapis.com/auth/youtube.readonly https://www.googleapis.com/auth/youtube.force-ssl"
REQUIRED_YOUTUBE_SCOPES = {"https://www.googleapis.com/auth/youtube.readonly", "https://www.googleapis.com/auth/youtube.force-ssl"}
YOUTUBE_API = "https://www.googleapis.com/youtube/v3"
USERINFO_URL = "https://openidconnect.googleapis.com/v1/userinfo"
DEFAULT_AUTH_URI = "https://accounts.google.com/o/oauth2/v2/auth"
DEFAULT_TOKEN_URI = "https://oauth2.googleapis.com/token"


class YouTubeOAuthError(RuntimeError):
    pass


class _DATA_BLOB(ctypes.Structure):
    _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_char))]


def _dpapi_protect(raw: bytes) -> bytes:
    if os.name != "nt":
        return raw
    crypt32 = ctypes.windll.crypt32
    kernel32 = ctypes.windll.kernel32
    crypt32.CryptProtectData.argtypes = [ctypes.POINTER(_DATA_BLOB), wintypes.LPCWSTR, ctypes.POINTER(_DATA_BLOB), wintypes.LPVOID, wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(_DATA_BLOB)]
    crypt32.CryptProtectData.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [wintypes.HLOCAL]
    kernel32.LocalFree.restype = wintypes.HLOCAL
    in_buffer = ctypes.create_string_buffer(raw)
    in_blob = _DATA_BLOB(len(raw), ctypes.cast(in_buffer, ctypes.POINTER(ctypes.c_char)))
    out_blob = _DATA_BLOB()
    if not crypt32.CryptProtectData(ctypes.byref(in_blob), "Suno Pesme Studio YouTube OAuth", None, None, None, 0, ctypes.byref(out_blob)):
        raise ctypes.WinError()
    try:
        return ctypes.string_at(out_blob.pbData, out_blob.cbData)
    finally:
        kernel32.LocalFree(out_blob.pbData)


def _dpapi_unprotect(raw: bytes) -> bytes:
    if os.name != "nt":
        return raw
    crypt32 = ctypes.windll.crypt32
    kernel32 = ctypes.windll.kernel32
    crypt32.CryptUnprotectData.argtypes = [ctypes.POINTER(_DATA_BLOB), ctypes.POINTER(wintypes.LPWSTR), ctypes.POINTER(_DATA_BLOB), wintypes.LPVOID, wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(_DATA_BLOB)]
    crypt32.CryptUnprotectData.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [wintypes.HLOCAL]
    kernel32.LocalFree.restype = wintypes.HLOCAL
    in_buffer = ctypes.create_string_buffer(raw)
    in_blob = _DATA_BLOB(len(raw), ctypes.cast(in_buffer, ctypes.POINTER(ctypes.c_char)))
    out_blob = _DATA_BLOB()
    if not crypt32.CryptUnprotectData(ctypes.byref(in_blob), None, None, None, None, 0, ctypes.byref(out_blob)):
        raise ctypes.WinError()
    try:
        return ctypes.string_at(out_blob.pbData, out_blob.cbData)
    finally:
        kernel32.LocalFree(out_blob.pbData)


def _read_json_response(request: urllib.request.Request, timeout: int = 30) -> dict[str, Any]:
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            raw = response.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        message = f"Google OAuth greška HTTP {exc.code}."
        try:
            payload = json.loads(body)
            message = str(payload.get("error_description") or payload.get("error", {}).get("message") or payload.get("error") or message)
        except Exception:
            pass
        raise YouTubeOAuthError(message) from exc
    except urllib.error.URLError as exc:
        raise YouTubeOAuthError(f"Ne mogu da se povežem sa Google servisom: {exc.reason}") from exc
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError as exc:
        raise YouTubeOAuthError("Google je vratio neispravan odgovor.") from exc
    if not isinstance(payload, dict):
        raise YouTubeOAuthError("Google je vratio neočekivanu strukturu podataka.")
    return payload


def _post_form(url: str, values: dict[str, Any], timeout: int = 30) -> dict[str, Any]:
    encoded = urllib.parse.urlencode({k: v for k, v in values.items() if v not in (None, "")}).encode("utf-8")
    request = urllib.request.Request(
        url,
        data=encoded,
        method="POST",
        headers={"Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json", "User-Agent": "SunoPesmeStudio/3.0"},
    )
    return _read_json_response(request, timeout=timeout)


def _get_json(url: str, access_token: str, timeout: int = 30) -> dict[str, Any]:
    request = urllib.request.Request(
        url,
        headers={"Authorization": f"Bearer {access_token}", "Accept": "application/json", "User-Agent": "SunoPesmeStudio/3.0"},
    )
    return _read_json_response(request, timeout=timeout)


def _channel_from_item(item: dict[str, Any], profile_id: str, email: str) -> dict[str, Any]:
    snippet = item.get("snippet") if isinstance(item.get("snippet"), dict) else {}
    details = item.get("contentDetails") if isinstance(item.get("contentDetails"), dict) else {}
    related = details.get("relatedPlaylists") if isinstance(details.get("relatedPlaylists"), dict) else {}
    stats = item.get("statistics") if isinstance(item.get("statistics"), dict) else {}
    channel_id = str(item.get("id") or "")
    custom = str(snippet.get("customUrl") or "")
    return {
        "channel_id": channel_id,
        "title": str(snippet.get("title") or channel_id),
        "handle": custom if custom.startswith("@") else ("@" + custom if custom else ""),
        "url": f"https://www.youtube.com/channel/{channel_id}",
        "uploads_playlist": str(related.get("uploads") or ""),
        "subscriber_count": int(stats.get("subscriberCount") or 0),
        "video_count": int(stats.get("videoCount") or 0),
        "view_count": int(stats.get("viewCount") or 0),
        "source_mode": "oauth",
        "oauth_profile_id": profile_id,
        "google_email": email,
    }


class YouTubeOAuthManager:
    """Lokalni Google OAuth 2.0/PKCE menadžer bez dodatnih Python paketa."""

    def __init__(self, data_dir: str | Path, port: int = 8765):
        self.data_dir = Path(data_dir)
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.config_path = self.data_dir / "youtube_oauth_client.json"
        self.tokens_path = self.data_dir / "youtube_oauth_tokens.bin"
        self.port = int(port)
        self._lock = threading.RLock()
        self._pending: dict[str, dict[str, Any]] = {}

    @property
    def redirect_uri(self) -> str:
        return f"http://127.0.0.1:{self.port}/"

    def _load_config(self) -> dict[str, Any]:
        if not self.config_path.exists():
            return {}
        try:
            data = json.loads(self.config_path.read_text(encoding="utf-8"))
        except Exception as exc:
            raise YouTubeOAuthError(f"Google OAuth podešavanje nije čitljivo: {exc}") from exc
        return data if isinstance(data, dict) else {}

    def has_client_config(self) -> bool:
        try:
            return bool(self._load_config().get("client_id"))
        except Exception:
            return False

    def import_client_config(self, path: str | Path) -> dict[str, Any]:
        source = Path(path)
        if not source.exists() or not source.is_file():
            raise YouTubeOAuthError("Izabrani Google OAuth JSON fajl ne postoji.")
        try:
            raw = json.loads(source.read_text(encoding="utf-8-sig"))
        except Exception as exc:
            raise YouTubeOAuthError(f"Google OAuth JSON nije ispravan: {exc}") from exc
        section = raw.get("installed") if isinstance(raw, dict) else None
        client_type = "installed"
        if not isinstance(section, dict):
            if isinstance(raw, dict) and isinstance(raw.get("web"), dict):
                raise YouTubeOAuthError("Izabran je Web application JSON. Napravi Google OAuth klijent tipa Desktop app i preuzmi njegov JSON.")
            raise YouTubeOAuthError("JSON mora da sadrži Google OAuth klijent tipa Desktop app.")
        client_id = str(section.get("client_id") or "").strip()
        if not client_id.endswith(".apps.googleusercontent.com"):
            raise YouTubeOAuthError("Google OAuth client_id nije prepoznat.")
        config = {
            "client_type": client_type,
            "client_id": client_id,
            "client_secret": str(section.get("client_secret") or "").strip(),
            "auth_uri": str(section.get("auth_uri") or DEFAULT_AUTH_URI),
            "token_uri": str(section.get("token_uri") or DEFAULT_TOKEN_URI),
            "project_id": str(section.get("project_id") or raw.get("project_id") or ""),
            "imported_from": source.name,
            "imported_at": int(time.time()),
        }
        self.config_path.write_text(json.dumps(config, ensure_ascii=False, indent=2), encoding="utf-8")
        try:
            os.chmod(self.config_path, 0o600)
        except Exception:
            pass
        return self.config_summary()

    def remove_client_config(self) -> None:
        for path in (self.config_path, self.tokens_path):
            try:
                path.unlink(missing_ok=True)
            except Exception:
                pass
        with self._lock:
            self._pending.clear()

    def config_summary(self) -> dict[str, Any]:
        try:
            config = self._load_config()
            error = ""
        except Exception as exc:
            config = {}
            error = str(exc)
        client_id = str(config.get("client_id") or "")
        return {
            "configured": bool(client_id),
            "client_type": str(config.get("client_type") or ""),
            "project_id": str(config.get("project_id") or ""),
            "client_hint": (client_id[:12] + "…" + client_id[-10:]) if len(client_id) > 25 else client_id,
            "imported_from": str(config.get("imported_from") or ""),
            "config_error": error,
        }

    def _load_store(self) -> dict[str, Any]:
        if not self.tokens_path.exists():
            return {"profiles": {}}
        try:
            encoded = self.tokens_path.read_bytes()
            protected = base64.b64decode(encoded)
            raw = _dpapi_unprotect(protected)
            payload = json.loads(raw.decode("utf-8"))
            return payload if isinstance(payload, dict) else {"profiles": {}}
        except Exception as exc:
            raise YouTubeOAuthError(f"Sačuvana Google prijava nije mogla da se pročita: {exc}") from exc

    def _save_store(self, store: dict[str, Any]) -> None:
        raw = json.dumps(store, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        protected = _dpapi_protect(raw)
        temp = self.tokens_path.with_suffix(".tmp")
        temp.write_bytes(base64.b64encode(protected))
        temp.replace(self.tokens_path)
        try:
            os.chmod(self.tokens_path, 0o600)
        except Exception:
            pass

    def start_authorization(self, login_hint: str = "") -> dict[str, Any]:
        config = self._load_config()
        client_id = str(config.get("client_id") or "")
        if not client_id:
            raise YouTubeOAuthError("Prvo jednom izaberi Google OAuth JSON fajl. Posle toga se YouTube povezuje samo izborom mejla.")
        state = secrets.token_urlsafe(32)
        verifier = secrets.token_urlsafe(64)[:96]
        challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode("ascii")).digest()).decode("ascii").rstrip("=")
        with self._lock:
            self._pending[state] = {"verifier": verifier, "created_at": time.time(), "login_hint": str(login_hint or "").strip()}
            # Ukloni stare nedovršene prijave.
            self._pending = {k: v for k, v in self._pending.items() if time.time() - float(v.get("created_at") or 0) < 900}
        params = {
            "client_id": client_id,
            "redirect_uri": self.redirect_uri,
            "response_type": "code",
            "scope": AUTH_SCOPE,
            "access_type": "offline",
            "prompt": "consent select_account",
            "include_granted_scopes": "true",
            "state": state,
            "code_challenge": challenge,
            "code_challenge_method": "S256",
        }
        if login_hint:
            params["login_hint"] = str(login_hint).strip()
        auth_uri = str(config.get("auth_uri") or DEFAULT_AUTH_URI)
        return {"url": auth_uri + "?" + urllib.parse.urlencode(params), "state": state, "redirect_uri": self.redirect_uri}

    def _exchange_code(self, code: str, verifier: str) -> dict[str, Any]:
        config = self._load_config()
        return _post_form(str(config.get("token_uri") or DEFAULT_TOKEN_URI), {
            "client_id": config.get("client_id"),
            "client_secret": config.get("client_secret"),
            "code": code,
            "code_verifier": verifier,
            "grant_type": "authorization_code",
            "redirect_uri": self.redirect_uri,
        })

    def _refresh_profile(self, profile_id: str, profile: dict[str, Any]) -> dict[str, Any]:
        refresh_token = str(profile.get("refresh_token") or "")
        if not refresh_token:
            raise YouTubeOAuthError("Google prijava nema refresh token. Poveži nalog ponovo.")
        config = self._load_config()
        token_data = _post_form(str(config.get("token_uri") or DEFAULT_TOKEN_URI), {
            "client_id": config.get("client_id"),
            "client_secret": config.get("client_secret"),
            "refresh_token": refresh_token,
            "grant_type": "refresh_token",
        })
        access_token = str(token_data.get("access_token") or "")
        if not access_token:
            raise YouTubeOAuthError("Google nije vratio novi pristupni token.")
        profile["access_token"] = access_token
        profile["expires_at"] = int(time.time()) + int(token_data.get("expires_in") or 3600) - 60
        if token_data.get("scope"):
            profile["scope"] = str(token_data.get("scope"))
        store = self._load_store()
        profiles = store.setdefault("profiles", {})
        profiles[profile_id] = profile
        self._save_store(store)
        return profile

    def get_access_token(self, profile_id: str = "") -> str:
        store = self._load_store()
        profiles = store.get("profiles") if isinstance(store.get("profiles"), dict) else {}
        if not profiles:
            raise YouTubeOAuthError("YouTube nije povezan preko Google naloga.")
        chosen_id = str(profile_id or "")
        if chosen_id and chosen_id not in profiles:
            raise YouTubeOAuthError("Google prijava za ovaj kanal više ne postoji. Poveži nalog ponovo.")
        if not chosen_id:
            chosen_id = sorted(profiles, key=lambda key: str(profiles[key].get("connected_at") or ""), reverse=True)[0]
        profile = dict(profiles[chosen_id])
        granted = set(str(profile.get("scope") or "").split())
        missing = REQUIRED_YOUTUBE_SCOPES - granted
        if missing:
            raise YouTubeOAuthError("Stara Google prijava nema dozvolu za privatne/unlisted videe. Odvoji nalog i poveži ga ponovo izborom mejla.")
        if not profile.get("access_token") or int(profile.get("expires_at") or 0) <= int(time.time()) + 30:
            profile = self._refresh_profile(chosen_id, profile)
        return str(profile.get("access_token") or "")

    def refresh_access_token(self, profile_id: str = "") -> str:
        """Prinudno osveži token kada Google vrati 401 tokom dužeg uploada."""
        store = self._load_store()
        profiles = store.get("profiles") if isinstance(store.get("profiles"), dict) else {}
        if not profiles:
            raise YouTubeOAuthError("YouTube nije povezan preko Google naloga.")
        chosen_id = str(profile_id or "")
        if chosen_id and chosen_id not in profiles:
            raise YouTubeOAuthError("Google prijava za ovaj kanal više ne postoji.")
        if not chosen_id:
            chosen_id = sorted(profiles, key=lambda key: str(profiles[key].get("connected_at") or ""), reverse=True)[0]
        profile = self._refresh_profile(chosen_id, dict(profiles[chosen_id]))
        return str(profile.get("access_token") or "")

    def _list_channels(self, access_token: str, profile_id: str, email: str) -> list[dict[str, Any]]:
        params = urllib.parse.urlencode({"part": "snippet,contentDetails,statistics", "mine": "true", "maxResults": 50})
        data = _get_json(f"{YOUTUBE_API}/channels?{params}", access_token)
        channels: list[dict[str, Any]] = []
        for item in data.get("items") or []:
            if isinstance(item, dict) and item.get("id"):
                channels.append(_channel_from_item(item, profile_id, email))
        return channels

    def handle_callback(self, query: dict[str, list[str]]) -> dict[str, Any]:
        error = str((query.get("error") or [""])[0])
        if error:
            description = str((query.get("error_description") or [error])[0])
            raise YouTubeOAuthError(f"Google prijava nije završena: {description}")
        state = str((query.get("state") or [""])[0])
        code = str((query.get("code") or [""])[0])
        if not state or not code:
            raise YouTubeOAuthError("Google nije vratio kod za povezivanje.")
        with self._lock:
            pending = self._pending.pop(state, None)
        if not pending:
            raise YouTubeOAuthError("Google prijava je istekla ili ne pripada ovom programu. Pokreni povezivanje ponovo.")
        token_data = self._exchange_code(code, str(pending.get("verifier") or ""))
        access_token = str(token_data.get("access_token") or "")
        if not access_token:
            raise YouTubeOAuthError("Google nije vratio pristupni token.")
        userinfo = _get_json(USERINFO_URL, access_token)
        email = str(userinfo.get("email") or pending.get("login_hint") or "Google nalog")
        subject = str(userinfo.get("sub") or email)
        temporary_profile = hashlib.sha256((subject + access_token[:24]).encode("utf-8")).hexdigest()[:20]
        channels = self._list_channels(access_token, temporary_profile, email)
        channel_key = ",".join(sorted(str(c.get("channel_id") or "") for c in channels)) or temporary_profile
        profile_id = hashlib.sha256((subject + "|" + channel_key).encode("utf-8")).hexdigest()[:20]
        for channel in channels:
            channel["oauth_profile_id"] = profile_id
        store = self._load_store()
        profiles = store.setdefault("profiles", {})
        old = profiles.get(profile_id) if isinstance(profiles.get(profile_id), dict) else {}
        refresh_token = str(token_data.get("refresh_token") or old.get("refresh_token") or "")
        profile = {
            "profile_id": profile_id,
            "email": email,
            "subject": subject,
            "access_token": access_token,
            "refresh_token": refresh_token,
            "expires_at": int(time.time()) + int(token_data.get("expires_in") or 3600) - 60,
            "scope": str(token_data.get("scope") or AUTH_SCOPE),
            "channel_ids": [str(c.get("channel_id") or "") for c in channels],
            "channel_titles": [str(c.get("title") or "") for c in channels],
            "connected_at": int(time.time()),
        }
        profiles[profile_id] = profile
        self._save_store(store)
        return {"profile": self.public_profile(profile_id, profile), "channels": channels}

    def public_profile(self, profile_id: str, profile: dict[str, Any]) -> dict[str, Any]:
        return {
            "profile_id": profile_id,
            "email": str(profile.get("email") or "Google nalog"),
            "channel_ids": list(profile.get("channel_ids") or []),
            "channel_titles": list(profile.get("channel_titles") or []),
            "connected_at": int(profile.get("connected_at") or 0),
            "has_refresh_token": bool(profile.get("refresh_token")),
            "needs_reauth": bool(REQUIRED_YOUTUBE_SCOPES - set(str(profile.get("scope") or "").split())),
        }

    def profiles(self) -> list[dict[str, Any]]:
        try:
            store = self._load_store()
        except Exception:
            return []
        profiles = store.get("profiles") if isinstance(store.get("profiles"), dict) else {}
        return [self.public_profile(pid, dict(profile)) for pid, profile in profiles.items() if isinstance(profile, dict)]

    def status(self) -> dict[str, Any]:
        profiles = self.profiles()
        return {
            **self.config_summary(),
            "connected": bool(profiles),
            "profiles": profiles,
            "profile_count": len(profiles),
            "channel_count": sum(len(p.get("channel_ids") or []) for p in profiles),
            "redirect_uri": self.redirect_uri,
        }

    def revoke_and_disconnect_profile(self, profile_id: str) -> None:
        """Pokušaj Google opoziv tokena, zatim uvek ukloni lokalnu prijavu."""
        store = self._load_store()
        profiles = store.get("profiles") if isinstance(store.get("profiles"), dict) else {}
        profile = profiles.get(str(profile_id))
        if not isinstance(profile, dict):
            raise YouTubeOAuthError("Google nalog nije pronađen.")
        token = str(profile.get("refresh_token") or profile.get("access_token") or "")
        if token:
            try:
                request = urllib.request.Request(
                    "https://oauth2.googleapis.com/revoke",
                    data=urllib.parse.urlencode({"token": token}).encode("utf-8"),
                    method="POST",
                    headers={"Content-Type": "application/x-www-form-urlencoded", "User-Agent": "SunoPesmeStudio/3.0"},
                )
                with urllib.request.urlopen(request, timeout=20) as response:
                    response.read()
            except Exception:
                # Lokalno odvajanje mora da uspe i kada internet/revocation servis nije dostupan.
                pass
        profiles.pop(str(profile_id), None)
        store["profiles"] = profiles
        self._save_store(store)

    def disconnect_profile(self, profile_id: str) -> None:
        store = self._load_store()
        profiles = store.get("profiles") if isinstance(store.get("profiles"), dict) else {}
        profile = profiles.pop(str(profile_id), None)
        if not profile:
            raise YouTubeOAuthError("Google nalog nije pronađen.")
        self._save_store(store)

    def refresh_channels(self, profile_id: str = "") -> list[dict[str, Any]]:
        store = self._load_store()
        profiles = store.get("profiles") if isinstance(store.get("profiles"), dict) else {}
        ids = [profile_id] if profile_id else list(profiles.keys())
        result: list[dict[str, Any]] = []
        changed = False
        for pid in ids:
            if pid not in profiles:
                continue
            profile = dict(profiles[pid])
            access_token = self.get_access_token(pid)
            channels = self._list_channels(access_token, pid, str(profile.get("email") or "Google nalog"))
            profile["channel_ids"] = [str(c.get("channel_id") or "") for c in channels]
            profile["channel_titles"] = [str(c.get("title") or "") for c in channels]
            profiles[pid] = profile
            result.extend(channels)
            changed = True
        if changed:
            store["profiles"] = profiles
            self._save_store(store)
        return result
