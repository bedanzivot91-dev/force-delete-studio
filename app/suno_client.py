from __future__ import annotations

import base64
import json
import os
import re
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path
from typing import Any, Callable

from suno_compat import normalize_page


API_BASE = "https://studio-api.prod.suno.com"
DEFAULT_USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36"
)


class SunoAPIError(RuntimeError):
    def __init__(self, message: str, status_code: int | None = None):
        super().__init__(message)
        self.status_code = status_code


def sanitize_filename(name: str, max_len: int = 130) -> str:
    name = re.sub(r'[<>:"/\\|?*\x00-\x1F]', "_", name or "Bez naslova")
    name = re.sub(r"\s+", " ", name).strip(" .")
    if not name:
        name = "Bez naslova"
    reserved = {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(1, 10)), *(f"LPT{i}" for i in range(1, 10))}
    if name.upper() in reserved:
        name = "_" + name
    return name[:max_len].rstrip(" .")


def _unwrap_clip_payload(payload: Any, song_id: str = "") -> dict[str, Any] | None:
    """Pronađi stvarni clip objekat u promenljivim Suno odgovorima."""
    wanted = str(song_id or "").strip()
    seen: set[int] = set()

    def walk(node: Any, depth: int = 0) -> dict[str, Any] | None:
        if depth > 8 or not isinstance(node, (dict, list)):
            return None
        marker = id(node)
        if marker in seen:
            return None
        seen.add(marker)
        if isinstance(node, list):
            for item in node:
                found = walk(item, depth + 1)
                if found:
                    return found
            return None
        ident = str(node.get("id") or node.get("clip_id") or node.get("song_id") or "").strip()
        looks = bool(ident and any(k in node for k in ("title", "audio_url", "video_url", "image_url", "metadata", "created_at")))
        if looks and (not wanted or ident == wanted):
            result = dict(node)
            result.setdefault("id", ident)
            return result
        for key in ("clip", "song", "track", "content", "data", "result", "payload", "item"):
            if key in node:
                found = walk(node.get(key), depth + 1)
                if found:
                    return found
        for value in node.values():
            if isinstance(value, (dict, list)):
                found = walk(value, depth + 1)
                if found:
                    return found
        return None

    return walk(payload)


def _should_send_suno_auth(url: str) -> bool:
    host = (urllib.parse.urlparse(str(url or "")).hostname or "").lower()
    return host == "studio-api.prod.suno.com" or host.endswith(".studio-api.prod.suno.com")


def _validate_downloaded_file(path: Path, content_type: str = "") -> None:
    content_type = str(content_type or "").split(";", 1)[0].strip().lower()
    with path.open("rb") as handle:
        head = handle.read(240)
    if content_type.startswith("text/") or content_type in {"application/json", "application/xml", "text/html"}:
        sample = head.decode("utf-8", errors="replace").replace("\r", " ").replace("\n", " ")
        raise SunoAPIError(f"Server nije vratio medijski fajl već {content_type or 'tekst'}: {sample[:160]}")
    head = head[:32]
    suffix = path.suffix.lower().removesuffix(".part")
    # Kod privremenog fajla je nastavak npr. .part, pa uzmi prethodni nastavak.
    if path.name.lower().endswith(".mp3.part"):
        suffix = ".mp3"
    elif path.name.lower().endswith(".wav.part"):
        suffix = ".wav"
    elif path.name.lower().endswith(".mp4.part"):
        suffix = ".mp4"
    if suffix == ".mp3":
        is_mp3 = head.startswith(b"ID3") or (len(head) >= 2 and head[0] == 0xFF and (head[1] & 0xE0) == 0xE0)
        if not is_mp3:
            raise SunoAPIError("Preuzeti fajl nije prepoznat kao ispravan MP3.")
    elif suffix == ".wav" and not (head.startswith(b"RIFF") and head[8:12] == b"WAVE"):
        raise SunoAPIError("Preuzeti fajl nije prepoznat kao ispravan WAV.")
    elif suffix == ".mp4" and not (len(head) >= 12 and head[4:8] == b"ftyp"):
        raise SunoAPIError("Preuzeti fajl nije prepoznat kao ispravan MP4.")


def extract_items(payload: Any) -> list[dict[str, Any]]:
    """Normalizuj različite Suno odgovore i pronađi stvarne clip objekte."""

    def looks_like_song(value: Any) -> bool:
        if not isinstance(value, dict):
            return False
        if value.get("id") and any(
            key in value for key in ("title", "audio_url", "image_url", "metadata", "created_at", "major_model_version")
        ):
            return True
        for key in ("clip", "song", "track"):
            if isinstance(value.get(key), dict) and value[key].get("id"):
                return True
        content = value.get("content")
        if isinstance(content, dict):
            return looks_like_song(content)
        return False

    def find_array(node: Any, depth: int = 0, seen: set[int] | None = None) -> list[Any]:
        if depth > 6:
            return []
        if seen is None:
            seen = set()
        if isinstance(node, (dict, list)):
            marker = id(node)
            if marker in seen:
                return []
            seen.add(marker)
        if isinstance(node, list):
            if node and any(looks_like_song(item) for item in node):
                return node
            for item in node:
                found = find_array(item, depth + 1, seen)
                if found:
                    return found
            return []
        if not isinstance(node, dict):
            return []

        preferred = (
            "project_clips", "playlist_clips", "playlist_songs", "clips", "items", "songs",
            "tracks", "entries", "results", "generations",
        )
        for key in preferred:
            value = node.get(key)
            if isinstance(value, list) and (not value or any(looks_like_song(item) for item in value)):
                return value
        for key in ("data", "playlist", "feed", "library", "result", "payload"):
            value = node.get(key)
            if isinstance(value, (dict, list)):
                found = find_array(value, depth + 1, seen)
                if found:
                    return found
        for value in node.values():
            if isinstance(value, (dict, list)):
                found = find_array(value, depth + 1, seen)
                if found:
                    return found
        return []

    items = payload if isinstance(payload, list) else find_array(payload)
    result: list[dict[str, Any]] = []
    seen_ids: set[str] = set()
    for raw in items:
        item = raw
        for _ in range(4):
            if not isinstance(item, dict):
                break
            nested = None
            for key in ("clip", "song", "track", "content"):
                if isinstance(item.get(key), dict):
                    nested = item[key]
                    break
            if nested is None:
                break
            item = nested
        if not isinstance(item, dict):
            continue
        song_id = str(item.get("id") or item.get("clip_id") or item.get("song_id") or "").strip()
        if not song_id:
            continue
        if not item.get("id"):
            item = dict(item)
            item["id"] = song_id
        if song_id in seen_ids:
            continue
        seen_ids.add(song_id)
        result.append(item)
    return result


class SunoClient:
    """Client for Suno's current web API with automatic v3/v2/legacy fallback."""

    def __init__(
        self,
        token: str,
        user_agent: str = DEFAULT_USER_AGENT,
        device_id: str | None = None,
        token_provider: Callable[[bool], str | None] | None = None,
    ):
        self.token = token.strip()
        self.user_agent = user_agent or DEFAULT_USER_AGENT
        self.device_id = device_id or str(uuid.uuid4())
        self.token_provider = token_provider
        self.last_feed_mode = ""

    def _current_token(self, force_refresh: bool = False) -> str:
        """Return a fresh browser token when a provider is available.

        Clerk session JWTs are intentionally short-lived. Long sync/download jobs
        must not keep using the one token captured when the user clicked Connect.
        The provider is responsible for caching and for talking to the dedicated
        Suno browser only when refresh is actually needed.
        """
        if self.token_provider is not None:
            try:
                fresh = self.token_provider(bool(force_refresh))
                if fresh:
                    self.token = str(fresh).strip()
                elif force_refresh:
                    raise SunoAPIError("Automatsko osvežavanje Suno tokena nije uspelo.", 401)
            except SunoAPIError:
                raise
            except Exception as exc:
                if force_refresh:
                    raise SunoAPIError(f"Automatsko osvežavanje Suno tokena nije uspelo: {exc}", 401) from exc
        if not self.token:
            raise SunoAPIError("Suno nalog nije povezan ili je sesija istekla.", 401)
        return self.token

    @staticmethod
    def _browser_token() -> str:
        # Current Suno feed/v3 requests include a small timestamp token.
        compact = json.dumps({"timestamp": int(time.time() * 1000)}, separators=(",", ":"))
        encoded = base64.b64encode(compact.encode("utf-8")).decode("ascii").rstrip("=")
        return json.dumps({"token": encoded}, separators=(",", ":"))

    def _request(
        self,
        url: str,
        *,
        method: str = "GET",
        data: bytes | None = None,
        timeout: float = 30,
        headers: dict[str, str] | None = None,
        retries: int = 3,
    ) -> urllib.response.addinfourl:
        last_exc: Exception | None = None
        auth_retry_used = False
        attempt = 0
        max_attempts = max(1, int(retries))

        while attempt < max_attempts:
            token = self._current_token(force_refresh=auth_retry_used)
            merged = {
                "User-Agent": self.user_agent,
                "Accept": "application/json, text/plain, */*",
                "Accept-Language": "sr-RS,sr;q=0.9,en-US;q=0.8,en;q=0.7",
                "Authorization": f"Bearer {token}",
                "Origin": "https://suno.com",
                "Referer": "https://suno.com/",
                "device-id": self.device_id,
            }
            if data is not None:
                merged["Content-Type"] = "application/json"
            if headers:
                merged.update(headers)

            try:
                req = urllib.request.Request(url, data=data, headers=merged, method=method)
                return urllib.request.urlopen(req, timeout=timeout)
            except urllib.error.HTTPError as exc:
                body = ""
                try:
                    body = exc.read(800).decode("utf-8", errors="replace")
                except Exception:
                    pass
                if exc.code in (401, 403) and self.token_provider is not None and not auth_retry_used:
                    # Mint a new Clerk JWT and repeat the same request once. This
                    # is essential for jobs that run longer than the token TTL.
                    auth_retry_used = True
                    continue
                if exc.code == 401:
                    raise SunoAPIError(
                        "Suno sesija više nije važeća. Program je pokušao automatsko osvežavanje, "
                        "ali Suno traži novu prijavu. Otvori Suno povezivanje i prijavi se ponovo.",
                        401,
                    ) from exc
                if exc.code == 403:
                    raise SunoAPIError(
                        f"Suno je odbio zahtev (403): {body or exc.reason}. Osveži Suno vezu i pokušaj ponovo.",
                        403,
                    ) from exc
                if exc.code in (429, 500, 502, 503, 504) and attempt < max_attempts - 1:
                    time.sleep(1.5 * (2**attempt))
                    last_exc = exc
                    attempt += 1
                    continue
                raise SunoAPIError(f"Suno API greška {exc.code}: {body or exc.reason}", exc.code) from exc
            except (urllib.error.URLError, TimeoutError, OSError) as exc:
                last_exc = exc
                if attempt < max_attempts - 1:
                    time.sleep(1.5 * (2**attempt))
                    attempt += 1
                    continue
                break
            attempt += 1
        raise SunoAPIError(f"Mrežna greška: {last_exc}")

    def json_request(
        self,
        path_or_url: str,
        *,
        method: str = "GET",
        timeout: float = 30,
        payload: dict[str, Any] | None = None,
        headers: dict[str, str] | None = None,
        retries: int = 3,
    ) -> Any:
        url = path_or_url if path_or_url.startswith("http") else API_BASE + path_or_url
        data = None if payload is None else json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        with self._request(url, method=method, data=data, timeout=timeout, headers=headers, retries=retries) as response:
            raw = response.read()
            if not raw:
                return {}
            try:
                return json.loads(raw.decode("utf-8"))
            except Exception as exc:
                raise SunoAPIError("Suno je vratio odgovor koji nije JSON.") from exc

    def _feed_v3(
        self,
        *,
        cursor: str | None = None,
        filters: dict[str, Any] | None = None,
        limit: int = 50,
    ) -> tuple[list[dict[str, Any]], str | None, bool, Any]:
        body: dict[str, Any] = {
            "limit": max(1, min(int(limit), 100)),
            "filters": filters or {},
        }
        if cursor:
            body["cursor"] = cursor
        result = self.json_request(
            "/api/feed/v3",
            method="POST",
            payload=body,
            headers={"browser-token": self._browser_token()},
            timeout=40,
            retries=3,
        )
        page = normalize_page(result, extract_items, mode="v3")
        return page.items, page.next_cursor, page.has_more, result

    def list_library_cursor(
        self,
        cursor: str | None = None,
        *,
        liked: bool = False,
        trashed: bool = False,
    ) -> tuple[list[dict[str, Any]], str | None, bool, str]:
        """Čitaj glavni deo biblioteke. Workspaces se čitaju posebno."""
        filters: dict[str, Any] = {
            "disliked": "False",
            "trashed": "True" if trashed else "False",
            "fromStudioProject": {"presence": "False"},
        }
        try:
            items, next_cursor, has_more, _ = self._feed_v3(cursor=cursor, filters=filters)
            items = self._local_filter(items, liked=liked, trashed=trashed)
            self.last_feed_mode = "v3"
            return items, next_cursor, has_more, "v3"
        except SunoAPIError as exc:
            if exc.status_code == 401:
                raise
            v3_error = str(exc)

        params: dict[str, str] = {"page": "1"}
        if cursor and cursor.isdigit():
            params["page"] = cursor
        if liked:
            params["liked"] = "true"
        if trashed:
            params["trashed"] = "true"
        try:
            result = self.json_request("/api/feed/v2?" + urllib.parse.urlencode(params), retries=2)
            items = self._local_filter(extract_items(result), liked=liked, trashed=trashed)
            page = int(params["page"])
            next_cursor = str(page + 1) if items else None
            self.last_feed_mode = "v2"
            return items, next_cursor, bool(items), "v2"
        except SunoAPIError as exc:
            if exc.status_code == 401:
                raise
            v2_error = str(exc)

        try:
            result = self.json_request("/api/feed/?" + urllib.parse.urlencode(params), retries=2)
            items = self._local_filter(extract_items(result), liked=liked, trashed=trashed)
            page = int(params["page"])
            next_cursor = str(page + 1) if items else None
            self.last_feed_mode = "legacy"
            return items, next_cursor, bool(items), "legacy"
        except SunoAPIError as exc:
            raise SunoAPIError(
                "Suno nije prihvatio nijedan način čitanja glavne biblioteke. "
                f"v3: {v3_error}; v2: {v2_error}; stari način: {exc}"
            ) from exc

    def list_projects_page(self, page: int = 1) -> tuple[list[dict[str, Any]], bool]:
        params = urllib.parse.urlencode({
            "page": max(1, int(page)),
            "sort": "max_created_at_last_updated_clip",
            "show_trashed": "false",
            "exclude_shared": "false",
        })
        endpoints = (
            f"/api/project/me?{params}",
            f"/api/workspace/me?{params}",
            f"/api/workspaces/me?{params}",
        )
        errors: list[str] = []
        for endpoint in endpoints:
            try:
                payload = self.json_request(endpoint, timeout=35, retries=2)
            except SunoAPIError as exc:
                if exc.status_code == 401:
                    raise
                errors.append(f"{endpoint}: {exc}")
                continue
            projects: list[dict[str, Any]] = []
            containers: list[dict[str, Any]] = []
            if isinstance(payload, dict):
                containers.append(payload)
                if isinstance(payload.get("data"), dict):
                    containers.append(payload["data"])
            for container in containers:
                for key in ("projects", "workspaces", "items", "results"):
                    value = container.get(key)
                    if isinstance(value, list):
                        projects = [
                            x for x in value
                            if isinstance(x, dict) and (x.get("id") or x.get("project_id") or x.get("workspace_id"))
                        ]
                        if projects:
                            break
                if projects:
                    break
            has_more = False
            for container in containers:
                has_more = bool(container.get("has_more", has_more))
            if projects:
                return projects, has_more
        if errors:
            raise SunoAPIError("Nije moguće pročitati spisak Workspaces/Projects. " + " | ".join(errors))
        return [], False

    def list_all_projects(self, max_pages: int = 100) -> list[dict[str, Any]]:
        all_projects: list[dict[str, Any]] = []
        seen: set[str] = set()
        for page in range(1, max(1, min(max_pages, 1000)) + 1):
            projects, has_more = self.list_projects_page(page)
            for project in projects:
                project_id = str(project.get("id") or project.get("project_id") or project.get("workspace_id") or "").strip()
                if project_id and project_id not in seen:
                    seen.add(project_id)
                    all_projects.append(project)
            if not has_more or not projects:
                break
            time.sleep(0.2)
        return all_projects

    def list_workspace_cursor(
        self,
        workspace_id: str,
        cursor: str | None = None,
        *,
        liked: bool = False,
        trashed: bool = False,
    ) -> tuple[list[dict[str, Any]], str | None, bool, str]:
        workspace_id = str(workspace_id or "").strip()
        if not workspace_id:
            return [], None, False, "workspace-v3"
        filters: dict[str, Any] = {
            "disliked": "False",
            "trashed": "True" if trashed else "False",
            "workspace": {"presence": "True", "workspaceId": workspace_id},
        }
        try:
            items, next_cursor, has_more, _ = self._feed_v3(cursor=cursor, filters=filters)
            items = self._local_filter(items, liked=liked, trashed=trashed)
            return items, next_cursor, has_more, "workspace-v3"
        except SunoAPIError as v3_exc:
            if v3_exc.status_code == 401:
                raise
            # Fallback for older accounts/builds where project detail still exposes clips.
            page = int(cursor) if cursor and str(cursor).isdigit() else 1
            try:
                payload = self.json_request(f"/api/project/{urllib.parse.quote(workspace_id)}?page={page}", timeout=35, retries=2)
                items = self._local_filter(extract_items(payload), liked=liked, trashed=trashed)
                return items, str(page + 1) if items else None, bool(items), "workspace-project"
            except SunoAPIError as fallback_exc:
                raise SunoAPIError(
                    f"Workspace {workspace_id} nije mogao da se pročita. v3: {v3_exc}; project fallback: {fallback_exc}"
                ) from fallback_exc

    @staticmethod
    def _local_filter(items: list[dict[str, Any]], *, liked: bool, trashed: bool) -> list[dict[str, Any]]:
        result: list[dict[str, Any]] = []
        for item in items:
            reaction = item.get("reaction") if isinstance(item.get("reaction"), dict) else {}
            metadata = item.get("metadata") if isinstance(item.get("metadata"), dict) else {}
            vote = item.get("vote") or metadata.get("vote")
            is_liked = bool(item.get("is_liked")) or reaction.get("reaction_type") in ("L", "LIKE") or vote == "up"
            is_trashed = bool(item.get("is_trashed"))
            if liked and not is_liked:
                continue
            if trashed and not is_trashed:
                continue
            if not trashed and is_trashed:
                continue
            result.append(item)
        return result

    def list_library_page(self, page: int = 1, liked: bool = False, trashed: bool = False) -> list[dict[str, Any]]:
        """Compatibility helper. Page 1 uses v3; later pages use v2/legacy."""
        if page <= 1:
            items, _, _, _ = self.list_library_cursor(None, liked=liked, trashed=trashed)
            return items
        params = {"page": str(page)}
        if liked:
            params["liked"] = "true"
        if trashed:
            params["trashed"] = "true"
        try:
            payload = self.json_request("/api/feed/v2?" + urllib.parse.urlencode(params), retries=2)
        except SunoAPIError:
            payload = self.json_request("/api/feed/?" + urllib.parse.urlencode(params), retries=2)
        return self._local_filter(extract_items(payload), liked=liked, trashed=trashed)

    def test_connection(self) -> dict[str, Any]:
        items, next_cursor, has_more, mode = self.list_library_cursor(None)
        projects_count = 0
        projects_error = ""
        try:
            projects, _ = self.list_projects_page(1)
            projects_count = len(projects)
        except Exception as exc:
            projects_error = str(exc)
        return {
            "ok": True,
            "mode": mode,
            "items": len(items),
            "next_cursor": bool(next_cursor),
            "has_more": bool(has_more),
            "projects": projects_count,
            "projects_error": projects_error,
        }

    def list_playlist(self, playlist_id: str, page: int = 1) -> tuple[dict[str, Any], list[dict[str, Any]]]:
        playlist_id = str(playlist_id or "").strip()
        if not playlist_id:
            return {}, []
        encoded = urllib.parse.quote(playlist_id)
        errors: list[str] = []
        endpoints = (
            f"/api/playlist/v2/{encoded}?page={page}&page_size=50",
            f"/api/playlist/{encoded}?page={page}&page_size=50",
            f"/api/playlist/{encoded}/clips?page={page}&page_size=50",
        )
        last_info: dict[str, Any] = {}
        for endpoint in endpoints:
            try:
                payload = self.json_request(endpoint, timeout=35, retries=2)
            except SunoAPIError as exc:
                if exc.status_code == 401:
                    raise
                errors.append(f"{endpoint}: {exc}")
                continue
            info = payload if isinstance(payload, dict) else {}
            items = extract_items(payload)
            if info:
                last_info = info
            if items or page > 1:
                return info, items
        if page == 1:
            try:
                items, _, _, payload = self._feed_v3(
                    cursor=None,
                    filters={
                        "disliked": "False",
                        "trashed": "False",
                        "fromStudioProject": {"presence": "False"},
                        "playlist": {"presence": "True", "playlistId": playlist_id},
                    },
                    limit=50,
                )
                if items:
                    return payload if isinstance(payload, dict) else last_info, items
            except SunoAPIError as exc:
                errors.append(f"feed/v3: {exc}")
        if errors and not last_info:
            raise SunoAPIError("Plejlista nije mogla da se pročita. " + " | ".join(errors[:4]))
        return last_info, []

    def get_clip(self, song_id: str) -> dict[str, Any]:
        payload = self.json_request(f"/api/clip/{song_id}")
        clip = _unwrap_clip_payload(payload, song_id)
        if not clip:
            items = extract_items(payload)
            clip = next((item for item in items if str(item.get("id") or "") == str(song_id)), items[0] if items else None)
        if not isinstance(clip, dict):
            raise SunoAPIError("Detalji pesme nisu dostupni ili Suno koristi novu strukturu odgovora.")
        clip.setdefault("id", str(song_id))
        return clip

    def get_aligned_lyrics(self, song_id: str) -> list[dict[str, Any]]:
        try:
            payload = self.json_request(f"/api/gen/{song_id}/aligned_lyrics/v2/", timeout=25)
        except SunoAPIError:
            return []
        if isinstance(payload, dict) and isinstance(payload.get("data"), dict):
            payload = payload["data"]
        if not isinstance(payload, dict):
            return []
        lines = payload.get("aligned_lyrics")
        if not isinstance(lines, list):
            return []
        result = []
        for line in lines:
            if not isinstance(line, dict):
                continue
            text = line.get("text") or line.get("word") or ""
            words = line.get("words") if isinstance(line.get("words"), list) else []
            starts = [w.get("start_s") for w in words if isinstance(w, dict) and isinstance(w.get("start_s"), (int, float))]
            ends = [w.get("end_s") for w in words if isinstance(w, dict) and isinstance(w.get("end_s"), (int, float))]
            start = line.get("start_s")
            end = line.get("end_s")
            if not isinstance(start, (int, float)) and starts:
                start = min(starts)
            if not isinstance(end, (int, float)) and ends:
                end = max(ends)
            if text and isinstance(start, (int, float)) and isinstance(end, (int, float)):
                result.append({"text": str(text).strip(), "start_s": float(start), "end_s": float(end)})
        result.sort(key=lambda x: x["start_s"])
        return result

    def request_wav(self, song_id: str, timeout: float = 120) -> str | None:
        try:
            with self._request(API_BASE + f"/api/gen/{song_id}/convert_wav/", method="POST", data=b"{}", timeout=20) as response:
                response.read()
        except SunoAPIError:
            return None
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                payload = self.json_request(f"/api/gen/{song_id}/wav_file/", timeout=20)
                found = self._find_wav_url(payload)
                if found:
                    return found
            except SunoAPIError:
                pass
            time.sleep(2)
        return None

    @classmethod
    def _find_wav_url(cls, data: Any) -> str | None:
        if isinstance(data, str):
            return data if data.startswith("http") and ".wav" in data.lower() else None
        if isinstance(data, dict):
            for key in ("audio_url_wav", "wav_url", "wav_audio_url", "master_wav_url", "preview_wav_url"):
                value = data.get(key)
                if isinstance(value, str) and value.startswith("http") and ".wav" in value.lower():
                    return value
            for value in data.values():
                found = cls._find_wav_url(value)
                if found:
                    return found
        if isinstance(data, list):
            for value in data:
                found = cls._find_wav_url(value)
                if found:
                    return found
        return None

    def download_file(
        self,
        url: str,
        target: Path,
        *,
        progress: Callable[[int, int], None] | None = None,
        cancel: Callable[[], bool] | None = None,
        auth: bool | None = None,
        max_bytes_per_second: int = 0,
        refresh_url: Callable[[], str | None] | None = None,
        validate_media: bool = True,
        resume: bool = True,
    ) -> Path:
        """Preuzmi fajl atomski, uz nastavak `.part` fajla kada server podržava HTTP Range."""
        target.parent.mkdir(parents=True, exist_ok=True)
        temp = target.with_suffix(target.suffix + ".part")
        last_error: Exception | None = None
        current_url = str(url or "").strip()
        if not current_url.startswith(("http://", "https://")):
            raise SunoAPIError("Adresa za preuzimanje nije ispravna.")

        for attempt in range(5):
            existing_bytes = temp.stat().st_size if resume and temp.exists() else 0
            send_auth = _should_send_suno_auth(current_url) if auth is None else bool(auth)
            headers = {
                "User-Agent": self.user_agent,
                "Accept": "audio/*,video/*,image/*,application/octet-stream;q=0.9,*/*;q=0.5",
                "Referer": "https://suno.com/",
                "device-id": self.device_id,
            }
            if existing_bytes:
                headers["Range"] = f"bytes={existing_bytes}-"
            if send_auth:
                headers["Authorization"] = f"Bearer {self._current_token(force_refresh=attempt > 0)}"
            req = urllib.request.Request(current_url, headers=headers)
            try:
                with urllib.request.urlopen(req, timeout=120) as response:
                    status = int(getattr(response, "status", 200) or 200)
                    content_type = str(response.headers.get("Content-Type") or "")
                    remaining = int(response.headers.get("Content-Length") or 0)
                    accepted_resume = bool(existing_bytes and status == 206)
                    if accepted_resume:
                        mode = "ab"
                        done = existing_bytes
                        total = existing_bytes + remaining if remaining else 0
                        content_range = str(response.headers.get("Content-Range") or "")
                        match = re.search(r"/(\d+)$", content_range)
                        if match:
                            total = int(match.group(1))
                    else:
                        # Server nije prihvatio Range; bezbedno krećemo ispočetka.
                        mode = "wb"
                        done = 0
                        total = remaining
                    started = time.monotonic()
                    with temp.open(mode) as out:
                        while True:
                            if cancel and cancel():
                                raise SunoAPIError("Preuzimanje je zaustavljeno. Delimični fajl je sačuvan za nastavak.")
                            chunk = response.read(1024 * 256)
                            if not chunk:
                                break
                            out.write(chunk)
                            done += len(chunk)
                            if max_bytes_per_second > 0:
                                transferred = done - (existing_bytes if accepted_resume else 0)
                                expected = transferred / float(max_bytes_per_second)
                                elapsed = time.monotonic() - started
                                if expected > elapsed:
                                    time.sleep(min(expected - elapsed, 1.0))
                            if progress:
                                progress(done, total)
                        out.flush()
                        os.fsync(out.fileno())

                actual_size = temp.stat().st_size
                if actual_size < 1024:
                    temp.unlink(missing_ok=True)
                    raise SunoAPIError("Preuzeti fajl je premali i verovatno nije ispravan.")
                if total and actual_size != total:
                    last_error = SunoAPIError(f"Preuzimanje nije kompletno ({actual_size}/{total} bajtova). Nastaviće se automatski.")
                    if attempt < 4:
                        time.sleep(2 * (attempt + 1))
                        continue
                    raise last_error
                if validate_media:
                    try:
                        _validate_downloaded_file(temp, content_type)
                    except Exception:
                        temp.unlink(missing_ok=True)
                        raise
                os.replace(temp, target)
                return target
            except urllib.error.HTTPError as exc:
                last_error = exc
                if exc.code == 416 and temp.exists():
                    # Neki CDN vraća 416 kada je delimični fajl zapravo već kompletan.
                    try:
                        if temp.stat().st_size >= 1024:
                            if validate_media:
                                _validate_downloaded_file(temp, str(exc.headers.get("Content-Type") or ""))
                            os.replace(temp, target)
                            return target
                    except Exception:
                        temp.unlink(missing_ok=True)
                if exc.code in (401, 403) and attempt < 4:
                    self._current_token(force_refresh=True)
                    if refresh_url is not None:
                        try:
                            refreshed = str(refresh_url() or "").strip()
                            if refreshed:
                                current_url = refreshed
                        except Exception as refresh_exc:
                            last_error = refresh_exc
                    time.sleep(1.5 * (attempt + 1))
                    continue
                if exc.code == 429 and attempt < 4:
                    try:
                        wait = max(3, min(120, int(exc.headers.get("Retry-After") or (8 * (attempt + 1)))))
                    except Exception:
                        wait = 8 * (attempt + 1)
                    time.sleep(wait)
                    if refresh_url is not None:
                        try:
                            refreshed = str(refresh_url() or "").strip()
                            if refreshed:
                                current_url = refreshed
                        except Exception:
                            pass
                    continue
                if exc.code not in (408, 500, 502, 503, 504):
                    temp.unlink(missing_ok=True)
                raise SunoAPIError(f"Preuzimanje nije uspelo (HTTP {exc.code}).", status_code=int(exc.code)) from exc
            except SunoAPIError:
                raise
            except Exception as exc:
                last_error = exc
                if attempt < 4 and isinstance(exc, (TimeoutError, urllib.error.URLError, ConnectionError, OSError)):
                    time.sleep(2 * (attempt + 1))
                    continue
                raise
        raise SunoAPIError(f"Preuzimanje nije uspelo: {last_error}")



def format_lrc(lines: list[dict[str, Any]]) -> str:
    out = []
    for line in lines:
        seconds = max(0.0, float(line["start_s"]))
        minutes = int(seconds // 60)
        secs = int(seconds % 60)
        hundredths = int((seconds - int(seconds)) * 100)
        out.append(f"[{minutes:02d}:{secs:02d}.{hundredths:02d}]{line['text']}")
    return "\n".join(out)


def format_srt(lines: list[dict[str, Any]]) -> str:
    def stamp(value: float) -> str:
        value = max(0.0, value)
        ms = int(round(value * 1000))
        h, rem = divmod(ms, 3_600_000)
        m, rem = divmod(rem, 60_000)
        s, milli = divmod(rem, 1000)
        return f"{h:02d}:{m:02d}:{s:02d},{milli:03d}"

    blocks = []
    for idx, line in enumerate(lines, 1):
        blocks.append(f"{idx}\n{stamp(float(line['start_s']))} --> {stamp(float(line['end_s']))}\n{line['text']}")
    return "\n\n".join(blocks)
