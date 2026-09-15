from __future__ import annotations

import json
import re
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from difflib import SequenceMatcher
from typing import Any, Iterable


YOUTUBE_API = "https://www.googleapis.com/youtube/v3"
CHANNEL_ID_RE = re.compile(r"^UC[A-Za-z0-9_-]{20,30}$")
VIDEO_ID_RE = re.compile(r"^[A-Za-z0-9_-]{11}$")


class YouTubeAPIError(RuntimeError):
    pass


def _request_json(url: str, timeout: int = 30, access_token: str = "") -> dict[str, Any]:
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 SunoPesmeStudio/3.0",
        "Accept": "application/json",
    }
    if access_token:
        headers["Authorization"] = f"Bearer {access_token}"
    request = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            raw = response.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        message = f"YouTube API greška HTTP {exc.code}."
        try:
            payload = json.loads(body)
            message = payload.get("error", {}).get("message") or message
        except Exception:
            pass
        raise YouTubeAPIError(message) from exc
    except urllib.error.URLError as exc:
        raise YouTubeAPIError(f"Ne mogu da se povežem sa YouTube servisom: {exc.reason}") from exc
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError as exc:
        raise YouTubeAPIError("YouTube je vratio neispravan odgovor.") from exc
    if not isinstance(payload, dict):
        raise YouTubeAPIError("YouTube je vratio neočekivanu strukturu podataka.")
    return payload


def _api_url(path: str, api_key: str = "", **params: Any) -> str:
    clean = {key: value for key, value in params.items() if value not in (None, "", [])}
    if api_key:
        clean["key"] = api_key
    return f"{YOUTUBE_API}/{path}?{urllib.parse.urlencode(clean, doseq=True)}"


def extract_channel_reference(raw: str) -> tuple[str, str]:
    value = str(raw or "").strip()
    if not value:
        return "", ""
    if CHANNEL_ID_RE.match(value):
        return "id", value
    if value.startswith("@"):
        return "handle", value
    try:
        parsed = urllib.parse.urlparse(value if "://" in value else "https://youtube.com/" + value.lstrip("/"))
        path = parsed.path.strip("/")
        parts = [part for part in path.split("/") if part]
        if len(parts) >= 2 and parts[0] == "channel" and CHANNEL_ID_RE.match(parts[1]):
            return "id", parts[1]
        if parts and parts[0].startswith("@"):
            return "handle", parts[0]
        if len(parts) >= 2 and parts[0] in ("user", "c"):
            return "legacy", parts[1]
    except Exception:
        pass
    return "handle", value if value.startswith("@") else "@" + value


def _channel_from_api(api_key: str, kind: str, value: str, access_token: str = "") -> dict[str, Any] | None:
    params: dict[str, Any] = {"part": "snippet,contentDetails,statistics", "maxResults": 1}
    if kind == "id":
        params["id"] = value
    elif kind == "handle":
        params["forHandle"] = value
    else:
        params["forUsername"] = value
    data = _request_json(_api_url("channels", api_key, **params), access_token=access_token)
    items = data.get("items") if isinstance(data.get("items"), list) else []
    return items[0] if items and isinstance(items[0], dict) else None


def _channel_id_from_page(reference: str) -> str:
    kind, value = extract_channel_reference(reference)
    if kind == "id":
        return value
    if kind == "handle":
        url = "https://www.youtube.com/" + value
    elif kind == "legacy":
        url = "https://www.youtube.com/user/" + value
    else:
        url = reference
    request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0", "Accept-Language": "en-US,en;q=0.9"})
    try:
        with urllib.request.urlopen(request, timeout=25) as response:
            html = response.read().decode("utf-8", errors="replace")
    except Exception as exc:
        raise YouTubeAPIError("Kanal nije mogao da se razreši bez Google/YouTube veze. Poveži nalog ili unesi channel ID.") from exc
    patterns = (
        r'"channelId"\s*:\s*"(UC[A-Za-z0-9_-]{20,30})"',
        r'"externalId"\s*:\s*"(UC[A-Za-z0-9_-]{20,30})"',
        r'<meta itemprop="channelId" content="(UC[A-Za-z0-9_-]{20,30})"',
    )
    for pattern in patterns:
        match = re.search(pattern, html)
        if match:
            return match.group(1)
    raise YouTubeAPIError("Nisam pronašao YouTube channel ID na javnoj stranici.")


def resolve_channel(reference: str, api_key: str = "", access_token: str = "") -> dict[str, Any]:
    kind, value = extract_channel_reference(reference)
    if not value:
        raise YouTubeAPIError("Upiši YouTube kanal, @handle ili channel ID.")
    item: dict[str, Any] | None = None
    if api_key or access_token:
        item = _channel_from_api(api_key, kind, value, access_token=access_token)
    if item:
        snippet = item.get("snippet") if isinstance(item.get("snippet"), dict) else {}
        details = item.get("contentDetails") if isinstance(item.get("contentDetails"), dict) else {}
        related = details.get("relatedPlaylists") if isinstance(details.get("relatedPlaylists"), dict) else {}
        stats = item.get("statistics") if isinstance(item.get("statistics"), dict) else {}
        channel_id = str(item.get("id") or "")
        custom = str(snippet.get("customUrl") or "")
        thumbnails = snippet.get("thumbnails") if isinstance(snippet.get("thumbnails"), dict) else {}
        thumbnail_url = str(
            (thumbnails.get("high") or {}).get("url")
            or (thumbnails.get("medium") or {}).get("url")
            or (thumbnails.get("default") or {}).get("url")
            or ""
        )
        return {
            "channel_id": channel_id,
            "title": str(snippet.get("title") or channel_id),
            "handle": custom if custom.startswith("@") else ("@" + custom if custom else ""),
            "url": f"https://www.youtube.com/channel/{channel_id}",
            "uploads_playlist": str(related.get("uploads") or ""),
            "subscriber_count": int(stats.get("subscriberCount") or 0),
            "video_count": int(stats.get("videoCount") or 0),
            "view_count": int(stats.get("viewCount") or 0),
            "thumbnail_url": thumbnail_url,
            "source_mode": "oauth" if access_token else "api",
        }
    channel_id = _channel_id_from_page(reference)
    return {
        "channel_id": channel_id,
        "title": value,
        "handle": value if kind == "handle" else "",
        "url": f"https://www.youtube.com/channel/{channel_id}",
        "uploads_playlist": "UU" + channel_id[2:] if channel_id.startswith("UC") else "",
        "subscriber_count": 0,
        "video_count": 0,
        "view_count": 0,
        "source_mode": "rss",
    }


def parse_iso_duration(raw: str) -> float:
    value = str(raw or "").strip().upper()
    if not value.startswith("P"):
        return 0.0
    match = re.fullmatch(r"P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?)?", value)
    if not match:
        return 0.0
    days, hours, minutes, seconds = match.groups()
    return float(days or 0) * 86400 + float(hours or 0) * 3600 + float(minutes or 0) * 60 + float(seconds or 0)


def _hydrate_videos(api_key: str, video_ids: Iterable[str], access_token: str = "") -> dict[str, dict[str, Any]]:
    ids = [x for x in dict.fromkeys(str(v) for v in video_ids if VIDEO_ID_RE.match(str(v)))]
    result: dict[str, dict[str, Any]] = {}
    for start in range(0, len(ids), 50):
        batch = ids[start:start + 50]
        if not batch:
            continue
        data = _request_json(_api_url("videos", api_key, part="snippet,contentDetails,statistics,status", id=",".join(batch), maxResults=50), access_token=access_token)
        for item in data.get("items") or []:
            if not isinstance(item, dict):
                continue
            snippet = item.get("snippet") if isinstance(item.get("snippet"), dict) else {}
            content = item.get("contentDetails") if isinstance(item.get("contentDetails"), dict) else {}
            stats = item.get("statistics") if isinstance(item.get("statistics"), dict) else {}
            status = item.get("status") if isinstance(item.get("status"), dict) else {}
            vid = str(item.get("id") or "")
            result[vid] = {
                "video_id": vid,
                "channel_id": str(snippet.get("channelId") or ""),
                "channel_title": str(snippet.get("channelTitle") or ""),
                "title": str(snippet.get("title") or ""),
                "description": str(snippet.get("description") or ""),
                "published_at": str(snippet.get("publishedAt") or ""),
                "url": f"https://www.youtube.com/watch?v={vid}",
                "thumbnail_url": str((((snippet.get("thumbnails") or {}).get("high") or {}).get("url")) or (((snippet.get("thumbnails") or {}).get("medium") or {}).get("url")) or ""),
                "duration": parse_iso_duration(str(content.get("duration") or "")),
                "view_count": int(stats.get("viewCount") or 0),
                "like_count": int(stats.get("likeCount") or 0),
                "comment_count": int(stats.get("commentCount") or 0),
                "privacy_status": str(status.get("privacyStatus") or ""),
                "raw_json": item,
            }
    return result


def list_channel_videos(channel: dict[str, Any], api_key: str = "", max_pages: int = 20, access_token: str = "", known_ids: set[str] | None = None) -> list[dict[str, Any]]:
    """`known_ids`, when given, enables an incremental scan: the uploads
    playlist is newest-first, so once an entire page of results is already
    in `known_ids` (all previously seen), later pages can only be even
    older uploads we've already recorded, and pagination stops early
    instead of re-walking the whole channel history on every scan."""
    channel_id = str(channel.get("channel_id") or "")
    if not CHANNEL_ID_RE.match(channel_id):
        raise YouTubeAPIError("YouTube channel ID nije ispravan.")
    if api_key or access_token:
        uploads = str(channel.get("uploads_playlist") or "")
        if not uploads:
            resolved = resolve_channel(channel_id, api_key, access_token=access_token)
            uploads = str(resolved.get("uploads_playlist") or "")
        if not uploads:
            raise YouTubeAPIError("Kanal nema pronađenu Uploads plejlistu.")
        items: list[dict[str, Any]] = []
        token = ""
        ids: list[str] = []
        for _ in range(max(1, min(max_pages, 100))):
            data = _request_json(_api_url("playlistItems", api_key, part="snippet,contentDetails,status", playlistId=uploads, maxResults=50, pageToken=token), access_token=access_token)
            page_items = data.get("items") if isinstance(data.get("items"), list) else []
            page_ids: list[str] = []
            for entry in page_items:
                if not isinstance(entry, dict):
                    continue
                snippet = entry.get("snippet") if isinstance(entry.get("snippet"), dict) else {}
                content = entry.get("contentDetails") if isinstance(entry.get("contentDetails"), dict) else {}
                resource = snippet.get("resourceId") if isinstance(snippet.get("resourceId"), dict) else {}
                vid = str(content.get("videoId") or resource.get("videoId") or "")
                if vid:
                    page_ids.append(vid)
            ids.extend(page_ids)
            if known_ids is not None and page_ids and all(v in known_ids for v in page_ids):
                break
            token = str(data.get("nextPageToken") or "")
            if not token or not page_items:
                break
        hydrated = _hydrate_videos(api_key, ids, access_token=access_token)
        for vid in ids:
            if vid in hydrated:
                items.append(hydrated[vid])
        return items

    feed_url = f"https://www.youtube.com/feeds/videos.xml?channel_id={urllib.parse.quote(channel_id)}"
    request = urllib.request.Request(feed_url, headers={"User-Agent": "Mozilla/5.0 SunoPesmeStudio/3.0"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            xml_data = response.read()
    except Exception as exc:
        raise YouTubeAPIError(f"Ne mogu da pročitam RSS kanala: {exc}") from exc
    root = ET.fromstring(xml_data)
    ns = {"atom": "http://www.w3.org/2005/Atom", "yt": "http://www.youtube.com/xml/schemas/2015", "media": "http://search.yahoo.com/mrss/"}
    result: list[dict[str, Any]] = []
    for entry in root.findall("atom:entry", ns):
        vid = entry.findtext("yt:videoId", default="", namespaces=ns)
        title = entry.findtext("atom:title", default="", namespaces=ns)
        published = entry.findtext("atom:published", default="", namespaces=ns)
        author = entry.find("atom:author", ns)
        channel_title = author.findtext("atom:name", default="", namespaces=ns) if author is not None else ""
        media_group = entry.find("media:group", ns)
        description = media_group.findtext("media:description", default="", namespaces=ns) if media_group is not None else ""
        thumb = ""
        if media_group is not None:
            thumb_el = media_group.find("media:thumbnail", ns)
            thumb = thumb_el.attrib.get("url", "") if thumb_el is not None else ""
        if vid:
            result.append({
                "video_id": vid,
                "channel_id": channel_id,
                "channel_title": channel_title or str(channel.get("title") or ""),
                "title": title,
                "description": description,
                "published_at": published,
                "url": f"https://www.youtube.com/watch?v={vid}",
                "thumbnail_url": thumb,
                "duration": 0.0,
                "view_count": 0,
                "like_count": 0,
                "comment_count": 0,
                "privacy_status": "public",
                "raw_json": {},
            })
    return result


def search_videos(
    api_key: str,
    query: str,
    max_results: int = 25,
    published_after: str = "",
    access_token: str = "",
    max_pages: int = 1,
) -> list[dict[str, Any]]:
    if not api_key and not access_token:
        raise YouTubeAPIError("Za YouTube API pretragu poveži Google/YouTube nalog ili dodaj API ključ.")
    wanted = max(1, min(int(max_results or 25), 200))
    per_page = min(50, wanted)
    token = ""
    ids: list[str] = []
    for _ in range(max(1, min(int(max_pages or 1), 4))):
        params: dict[str, Any] = {
            "part": "snippet",
            "q": query,
            "type": "video",
            "maxResults": per_page,
            "order": "relevance",
            "safeSearch": "none",
            "pageToken": token,
        }
        if published_after:
            params["publishedAfter"] = published_after
        data = _request_json(_api_url("search", api_key, **params), access_token=access_token)
        page_items = data.get("items") if isinstance(data.get("items"), list) else []
        for item in page_items:
            if not isinstance(item, dict):
                continue
            ident = item.get("id") if isinstance(item.get("id"), dict) else {}
            vid = str(ident.get("videoId") or "")
            if vid and vid not in ids:
                ids.append(vid)
                if len(ids) >= wanted:
                    break
        if len(ids) >= wanted:
            break
        token = str(data.get("nextPageToken") or "")
        if not token or not page_items:
            break
    hydrated = _hydrate_videos(api_key, ids, access_token=access_token)
    return [hydrated[vid] for vid in ids if vid in hydrated]


def build_search_queries(song: dict[str, Any], max_queries: int = 3) -> list[str]:
    title = str(song.get("title") or "").strip()
    artist = str(song.get("display_name") or song.get("artist") or "").strip()
    lyrics = str(song.get("lyrics") or "").strip()
    candidates: list[str] = []
    if title:
        candidates.extend([f'"{title}"', title])
    if artist and title:
        candidates.append(f'{artist} "{title}"')
    normalized = normalize_title(title)
    if normalized and normalized.lower() != title.lower():
        candidates.append(normalized)
    title_key = normalize_title(title)
    first_line = next((line.strip() for line in lyrics.splitlines() if len(line.strip()) >= 12 and normalize_title(line) != title_key), "")
    if first_line:
        candidates.append(f'"{first_line[:90]}"')
    result: list[str] = []
    seen: set[str] = set()
    for value in candidates:
        key = normalize_title(value)
        if not key or key in seen:
            continue
        seen.add(key)
        result.append(value)
        if len(result) >= max(1, min(int(max_queries or 3), 5)):
            break
    return result


def normalize_title(value: str) -> str:
    raw = unicodedata.normalize("NFKD", str(value or ""))
    raw = "".join(ch for ch in raw if not unicodedata.combining(ch)).lower()
    raw = re.sub(r"\([^)]*(remaster|official|audio|video|lyrics|lyric)[^)]*\)", " ", raw)
    raw = re.sub(r"\[[^]]*(remaster|official|audio|video|lyrics|lyric)[^]]*\]", " ", raw)
    raw = re.sub(r"\b(official|audio|video|lyrics?|remastered?|pesma|song|music)\b", " ", raw)
    raw = re.sub(r"[^a-z0-9]+", " ", raw)
    return " ".join(raw.split())


def match_song_to_video(song: dict[str, Any], video: dict[str, Any], owned_channel_ids: set[str]) -> dict[str, Any]:
    song_title = normalize_title(str(song.get("title") or ""))
    video_title = normalize_title(str(video.get("title") or ""))
    if not song_title or not video_title:
        title_similarity = 0.0
    else:
        title_similarity = SequenceMatcher(None, song_title, video_title).ratio()
        if song_title in video_title or video_title in song_title:
            title_similarity = max(title_similarity, 0.94)
    song_tokens = {t for t in song_title.split() if len(t) >= 3}
    video_tokens = set(video_title.split())
    token_coverage = (len(song_tokens & video_tokens) / len(song_tokens)) if song_tokens else 0.0
    score = title_similarity * 50.0 + token_coverage * 15.0
    reasons: list[str] = [f"sličnost naslova {title_similarity * 100:.0f}%", f"poklapanje reči {token_coverage * 100:.0f}%"]

    song_duration = float(song.get("duration") or 0)
    video_duration = float(video.get("duration") or 0)
    duration_similarity = 0.0
    if song_duration > 1 and video_duration > 1:
        duration_similarity = max(0.0, 1.0 - abs(song_duration - video_duration) / max(song_duration, video_duration))
        score += duration_similarity * 20.0
        reasons.append(f"sličnost trajanja {duration_similarity * 100:.0f}%")

    haystack = normalize_title(f"{video.get('title','')} {video.get('description','')} {video.get('channel_title','')}")
    artist = normalize_title(str(song.get("display_name") or song.get("artist") or ""))
    if artist and artist in haystack:
        score += 10.0
        reasons.append("pronađen autor/brend")
    lyrics = str(song.get("lyrics") or "")
    first_line = next((normalize_title(line) for line in lyrics.splitlines() if len(normalize_title(line)) >= 12), "")
    if first_line and (first_line in haystack or SequenceMatcher(None, first_line, haystack[: max(len(first_line) * 3, 80)]).ratio() >= 0.72):
        score += 5.0
        reasons.append("pronađen prepoznatljiv stih")

    is_owned = str(video.get("channel_id") or "") in owned_channel_ids
    match_type = "owned_publication" if is_owned else "external_candidate"
    return {
        "score": round(min(100.0, score), 1),
        "title_similarity": round(title_similarity * 100.0, 1),
        "duration_similarity": round(duration_similarity * 100.0, 1),
        "match_type": match_type,
        "reason": ", ".join(reasons),
        "is_owned_channel": is_owned,
    }


def google_search_url(song_title: str) -> str:
    query = f'site:youtube.com/watch "{song_title}"'
    return "https://www.google.com/search?q=" + urllib.parse.quote_plus(query)


def bing_search_url(song_title: str) -> str:
    query = f'site:youtube.com/watch "{song_title}"'
    return "https://www.bing.com/search?q=" + urllib.parse.quote_plus(query)


def youtube_search_url(song_title: str) -> str:
    return "https://www.youtube.com/results?search_query=" + urllib.parse.quote_plus(f'"{song_title}"')


def contact_message(song: dict[str, Any], video: dict[str, Any], owner_name: str = "") -> str:
    title = str(song.get("title") or "ova pesma")
    original_url = str(song.get("youtube_url") or song.get("source_url") or "")
    suspect_url = str(video.get("url") or "")
    signature = owner_name.strip() or "Vlasnik autorskih prava"
    return (
        "Poštovani,\n\n"
        f"na vašem YouTube kanalu pronašao sam video koji sadrži moju pesmu „{title}“:\n{suspect_url}\n\n"
        + (f"Originalna objava / dokaz: {original_url}\n\n" if original_url else "")
        + "Molim vas da proverite da li imate moju izričitu dozvolu ili važeću licencu za korišćenje ove pesme. "
        "Ako je nemate, molim da video uklonite ili da mi odgovorite sa informacijom o osnovu korišćenja. "
        "Ova poruka nije automatska prijava niti pravna odluka; šaljem je radi mirnog rešavanja pre eventualnog obraćanja YouTube-u.\n\n"
        f"Srdačno,\n{signature}"
    )


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")
