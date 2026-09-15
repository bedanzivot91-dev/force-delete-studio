from __future__ import annotations

import json
import re
import subprocess
import urllib.parse
import urllib.request
import urllib.error
from collections import Counter
from datetime import date, timedelta
from pathlib import Path
from typing import Any


ANALYTICS_API = "https://youtubeanalytics.googleapis.com/v2/reports"


def _analytics_report(token: str, start: str, end: str, metrics: str, dimensions: str = "", filters: str = "", sort: str = "") -> dict[str, Any]:
    params: dict[str, Any] = {"ids": "channel==MINE", "startDate": start, "endDate": end, "metrics": metrics}
    if dimensions: params["dimensions"] = dimensions
    if filters: params["filters"] = filters
    if sort: params["sort"] = sort
    raw = _google_json(f"{ANALYTICS_API}?{urllib.parse.urlencode(params)}", token)
    headers = [str(x.get("name") or "") for x in raw.get("columnHeaders") or []]
    return {"available": True, "columns": headers, "rows": [dict(zip(headers, row)) for row in raw.get("rows") or []], "source": "YouTube Analytics API"}


def youtube_analytics_suite(token: str, start_date: str = "", end_date: str = "", video_id: str = "") -> dict[str, Any]:
    """Return only API-backed reports. Unsupported/forbidden reports are explicit errors, never invented zeroes."""
    end = end_date or date.today().isoformat()
    start = start_date or (date.today() - timedelta(days=28)).isoformat()
    video_filter = f"video=={video_id}" if video_id else ""
    search_filter = ";".join(x for x in (video_filter, "insightTrafficSourceType==YT_SEARCH") if x)
    specs = {
        "daily": ("views,estimatedMinutesWatched,averageViewDuration,averageViewPercentage,subscribersGained,subscribersLost,likes,comments", "day", video_filter, "day"),
        "traffic_sources": ("views,estimatedMinutesWatched", "insightTrafficSourceType", video_filter, "-views"),
        "youtube_search_terms": ("views,estimatedMinutesWatched", "insightTrafficSourceDetail", search_filter, "-views"),
        "devices": ("views,estimatedMinutesWatched", "deviceType", video_filter, "-views"),
        "countries": ("views,estimatedMinutesWatched", "country", video_filter, "-views"),
        "playback_locations": ("views,estimatedMinutesWatched", "insightPlaybackLocationType", video_filter, "-views"),
        "subscribed_status": ("views,estimatedMinutesWatched", "subscribedStatus", video_filter, "-views"),
        "content_types": ("views,estimatedMinutesWatched", "creatorContentType", video_filter, "-views"),
        "top_videos": ("views,estimatedMinutesWatched,averageViewPercentage,likes,comments", "video", video_filter, "-views"),
    }
    reports: dict[str, Any] = {}
    for name, (metrics, dimensions, filters, sort) in specs.items():
        try:
            reports[name] = _analytics_report(token, start, end, metrics, dimensions, filters, sort)
        except Exception as exc:
            reports[name] = {"available": False, "rows": [], "error": str(exc), "source": "YouTube Analytics API"}
    days = max(1, (date.fromisoformat(end) - date.fromisoformat(start)).days + 1)
    previous_end = date.fromisoformat(start) - timedelta(days=1)
    previous_start = previous_end - timedelta(days=days - 1)
    try:
        reports["previous_period"] = _analytics_report(token, previous_start.isoformat(), previous_end.isoformat(), "views,estimatedMinutesWatched,subscribersGained,likes,comments", "", video_filter, "")
    except Exception as exc:
        reports["previous_period"] = {"available": False, "rows": [], "error": str(exc), "source": "YouTube Analytics API"}
    return {"start_date": start, "end_date": end, "previous_start": previous_start.isoformat(), "previous_end": previous_end.isoformat(), "video_id": video_id, "reports": reports, "source": "YouTube Analytics API", "estimated": False}


def _google_json(url: str, token: str, timeout: int = 45) -> dict[str, Any]:
    request = urllib.request.Request(url, headers={"Authorization": f"Bearer {token}", "Accept": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            payload = json.loads(response.read().decode("utf-8", errors="replace"))
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")[:1000]
        if exc.code in (401, 403):
            raise RuntimeError("YouTube pristup je istekao ili nema potrebnu dozvolu. Ponovo poveži kanal (OAuth).") from exc
        raise RuntimeError(f"YouTube API greška {exc.code}: {detail}") from exc
    if not isinstance(payload, dict):
        raise RuntimeError("Google je vratio neočekivane podatke.")
    return payload


def youtube_analytics(token: str, start_date: str = "", end_date: str = "", video_id: str = "") -> dict[str, Any]:
    end = end_date or date.today().isoformat()
    start = start_date or (date.today() - timedelta(days=28)).isoformat()
    metrics = "views,estimatedMinutesWatched,averageViewDuration,averageViewPercentage,subscribersGained,subscribersLost,likes,comments"
    params: dict[str, Any] = {"ids": "channel==MINE", "startDate": start, "endDate": end, "metrics": metrics, "dimensions": "day", "sort": "day"}
    if video_id:
        params["filters"] = f"video=={video_id}"
    raw = _google_json(f"{ANALYTICS_API}?{urllib.parse.urlencode(params)}", token)
    headers = [str(x.get("name") or "") for x in raw.get("columnHeaders") or []]
    rows = [dict(zip(headers, row)) for row in raw.get("rows") or []]
    totals: dict[str, float] = {}
    for name in headers:
        if name == "day":
            continue
        values = [float(row.get(name) or 0) for row in rows]
        totals[name] = sum(values) / len(values) if name in {"averageViewDuration", "averageViewPercentage"} and values else sum(values)
    return {"start_date": start, "end_date": end, "video_id": video_id, "rows": rows, "totals": totals, "source": "YouTube Analytics API"}


def youtube_retention(token: str, video_id: str) -> dict[str, Any]:
    params = {"ids": "channel==MINE", "startDate": "2000-01-01", "endDate": date.today().isoformat(), "metrics": "audienceWatchRatio,relativeRetentionPerformance", "dimensions": "elapsedVideoTimeRatio", "filters": f"video=={video_id}", "sort": "elapsedVideoTimeRatio"}
    raw = _google_json(f"{ANALYTICS_API}?{urllib.parse.urlencode(params)}", token)
    headers = [str(x.get("name") or "") for x in raw.get("columnHeaders") or []]
    points = [dict(zip(headers, row)) for row in raw.get("rows") or []]
    drops = sorted(points, key=lambda x: float(x.get("audienceWatchRatio") or 0))[:5]
    return {"video_id": video_id, "points": points, "weakest_points": drops}


def fetch_comments(token: str, video_id: str, max_pages: int = 3) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    page = ""
    for _ in range(max(1, min(max_pages, 20))):
        params = {"part": "snippet,replies", "videoId": video_id, "maxResults": 100, "textFormat": "plainText", "order": "relevance", "pageToken": page}
        raw = _google_json("https://www.googleapis.com/youtube/v3/commentThreads?" + urllib.parse.urlencode(params), token)
        for item in raw.get("items") or []:
            top = (((item.get("snippet") or {}).get("topLevelComment") or {}).get("snippet") or {})
            rows.append({"id": item.get("id"), "text": top.get("textOriginal") or top.get("textDisplay") or "", "author": top.get("authorDisplayName") or "", "likes": int(top.get("likeCount") or 0), "published_at": top.get("publishedAt") or "", "replies": int((item.get("snippet") or {}).get("totalReplyCount") or 0)})
        page = str(raw.get("nextPageToken") or "")
        if not page:
            break
    return rows


def analyze_comments(rows: list[dict[str, Any]]) -> dict[str, Any]:
    positive = {"lepo", "prelepo", "odlično", "bravo", "volim", "emocija", "pogodilo", "najbolje", "savršeno", "divno", "jako", "istina"}
    negative = {"loše", "dosadno", "glupo", "mrzim", "slabo", "predugo", "mutno", "tiho", "užasno", "bezveze"}
    emotions = {"tuga": {"tužno", "tuga", "boli", "plačem", "nedostaje"}, "nostalgija": {"sećanje", "nekada", "vrati", "pamtim"}, "ljutnja": {"besan", "ljut", "mrzim", "prevara"}, "podrška": {"bravo", "podrška", "nastavi", "najbolje"}}
    stop = {"kako", "kada", "koji", "koja", "ovo", "onaj", "samo", "tako", "zato", "moja", "tvoja", "jeste", "biti"}
    words = Counter()
    pos = neg = neutral = questions = 0; emotion_counts = Counter(); classified = []
    for row in rows:
        text = str(row.get("text") or "").lower()
        tokens = re.findall(r"[a-zA-ZčćžšđČĆŽŠĐ]{4,}", text)
        token_set = set(tokens); words.update(x for x in tokens if x not in stop)
        p, n = len(token_set & positive), len(token_set & negative)
        label = "pozitivan" if p > n else ("negativan" if n > p else "neodređen")
        pos += int(label == "pozitivan"); neg += int(label == "negativan"); neutral += int(label == "neodređen"); questions += int("?" in text)
        found_emotions = [name for name, lexicon in emotions.items() if token_set & lexicon]
        emotion_counts.update(found_emotions)
        classified.append({**row, "heuristic_label": label, "matched_emotions": found_emotions})
    top = sorted(rows, key=lambda x: (int(x.get("likes") or 0), int(x.get("replies") or 0)), reverse=True)[:10]
    return {"count": len(rows), "positive": pos, "negative": neg, "unclassified": neutral, "questions": questions, "emotions": dict(emotion_counts), "top_words": words.most_common(20), "best_comments": top, "classified_comments": classified, "method": "transparent Serbian keyword heuristic", "estimated": True, "warning": "Sentiment je heuristička procena, nije podatak koji vraća YouTube."}


def visual_video_analysis(path: Path, ffmpeg: str, ffprobe: str) -> dict[str, Any]:
    probe = subprocess.run([ffprobe, "-v", "error", "-show_entries", "format=duration:stream=width,height,r_frame_rate", "-of", "json", str(path)], capture_output=True, text=True, timeout=60)
    info = json.loads(probe.stdout or "{}")
    stream = next((x for x in info.get("streams") or [] if x.get("width")), {})
    width, height = int(stream.get("width") or 0), int(stream.get("height") or 0)
    scan = subprocess.run([ffmpeg, "-hide_banner", "-i", str(path), "-vf", "blackdetect=d=0.5:pix_th=0.10,freezedetect=n=-50dB:d=2,blurdetect,scdet=t=10,signalstats,metadata=print", "-an", "-f", "null", "-"], capture_output=True, text=True, timeout=900)
    log = (scan.stderr or "")[-200000:]
    black = [{"start": float(a), "end": float(b)} for a, b in re.findall(r"black_start:([\d.]+).*?black_end:([\d.]+)", log)]
    freezes = [{"start": float(x)} for x in re.findall(r"freeze_start:\s*([\d.]+)", log)]
    blur = [float(x) for x in re.findall(r"blur mean:\s*([\d.]+)", log)]
    scene_times = [float(x) for x in re.findall(r"lavfi\.scd\.time=([\d.]+)", log)]
    yavg = [float(x) for x in re.findall(r"lavfi\.signalstats\.YAVG=([\d.]+)", log)]
    issues = []
    if black: issues.append(f"Pronađeno crnih/praznih delova: {len(black)}")
    if freezes: issues.append(f"Pronađeno dugih nepomičnih delova: {len(freezes)}")
    ratio = width / max(height, 1) if width and height else 0
    if ratio and min(abs(ratio - 9/16), abs(ratio - 16/9), abs(ratio - 1)) > 0.03:
        issues.append("Format nije standardnih 9:16, 16:9 ili 1:1.")
    duration = float((info.get("format") or {}).get("duration") or 0)
    avg_scene = duration / max(1, len(scene_times) + 1) if duration else None
    if avg_scene and avg_scene > 12: issues.append(f"Prosečna scena je duga {avg_scene:.1f} s; proveri da li je spot vizuelno prespor.")
    if avg_scene and avg_scene < 0.7: issues.append(f"Prosečna scena traje {avg_scene:.1f} s; rezovi mogu biti prebrzi.")
    if yavg and sum(yavg)/len(yavg) < 32: issues.append("Video je tehnički veoma taman prema prosečnoj luminansi.")
    if yavg and sum(yavg)/len(yavg) > 220: issues.append("Video je tehnički veoma svetao prema prosečnoj luminansi.")
    good = [x for x in ("Nema crnih delova" if not black else "", "Nema dugih zamrznutih scena" if not freezes else "") if x]
    return {"path": str(path), "duration": duration, "width": width, "height": height, "orientation": "vertical" if height > width else "horizontal", "black_segments": black, "freeze_segments": freezes, "blur_mean": sum(blur)/len(blur) if blur else None, "scene_changes": scene_times, "average_scene_seconds": avg_scene, "average_luma": sum(yavg)/len(yavg) if yavg else None, "issues": issues, "good": good, "source": "local FFmpeg objective analysis", "estimated": False, "scope_warning": "Ovo je tehnička analiza; ne izmišlja umetnički sud o priči ili emociji spota."}


def suno_snapshot_diff(previous: list[dict[str, Any]], current: list[dict[str, Any]]) -> dict[str, Any]:
    old = {str(x.get("id")): x for x in previous}; new = {str(x.get("id")): x for x in current}
    fields = ("title", "lyrics", "prompt", "tags", "audio_url", "image_url", "model_version")
    changed = []
    for song_id in old.keys() & new.keys():
        differences = [f for f in fields if str(old[song_id].get(f) or "") != str(new[song_id].get(f) or "")]
        if differences: changed.append({"id": song_id, "title": new[song_id].get("title"), "fields": differences, "before": {f: old[song_id].get(f) for f in differences}, "after": {f: new[song_id].get(f) for f in differences}})
    return {"new": [new[x] for x in new.keys()-old.keys()], "missing": [old[x] for x in old.keys()-new.keys()], "changed": changed}


def quota_estimate(channels: int, pages: int, comment_pages: int = 0) -> dict[str, int]:
    return {"playlist_units": channels * pages, "video_detail_units": channels * pages, "comment_units": channels * comment_pages, "estimated_total": channels * (pages * 2 + comment_pages)}


def review_priority(result: dict[str, Any]) -> dict[str, Any]:
    confidence = float(result.get("confidence") or 0); seconds = float(result.get("matched_seconds") or result.get("covered_seconds") or 0)
    needs_review = confidence < 82 or seconds < 12
    return {"needs_review": needs_review, "priority": "high" if confidence < 65 else "normal", "reason": "Kratak ili nepouzdan audio pogodak" if needs_review else "Dovoljno jak automatski pogodak"}


def build_review_queue(recognitions: list[dict[str, Any]], audio_rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    queue = []
    for row in recognitions:
        result = row.get("result") if isinstance(row.get("result"), dict) else {}
        review = result.get("review") if isinstance(result.get("review"), dict) else review_priority(result.get("primary") or result)
        if not row.get("found") or review.get("needs_review"):
            queue.append({"kind": "local_clip", "id": row.get("id"), "title": row.get("title") or row.get("original_filename"), "priority": review.get("priority", "high"), "reason": review.get("reason") or "Pesma nije potvrđena", "source": "local audio fingerprint", "raw": row})
    for row in audio_rows:
        status = str(row.get("completeness_status") or "")
        score = float(row.get("audio_score") or 0)
        if status in {"different_audio", "short_clip", "partial", ""} or score < 82:
            queue.append({"kind": "youtube_match", "id": row.get("id"), "title": row.get("song_title"), "video_title": row.get("video_title"), "priority": "high" if score < 65 else "normal", "reason": f"Status {status or 'nije određen'}, audio {score:.1f}%", "source": "YouTube audio fingerprint", "raw": row})
    return sorted(queue, key=lambda x: (x.get("priority") != "high", str(x.get("title") or "").casefold()))


def build_song_report(song: dict[str, Any], match: dict[str, Any] | None = None, analytics: dict[str, Any] | None = None, comments: dict[str, Any] | None = None, visual: dict[str, Any] | None = None) -> dict[str, Any]:
    match = match or {}
    return {"song": {"id": song.get("id"), "title": song.get("title"), "suno_url": song.get("source_url"), "duration": song.get("duration")}, "youtube": {"video_id": match.get("video_id"), "url": match.get("video_url"), "channel": match.get("channel_title"), "published_at": match.get("published_at"), "views": match.get("view_count"), "likes": match.get("like_count"), "comments": match.get("comment_count"), "audio_score": match.get("audio_score"), "coverage_percent": match.get("coverage_percent"), "completeness_status": match.get("completeness_status"), "source": "YouTube Data API + local audio fingerprint" if match else "not available"}, "analytics": analytics or {"available": False, "reason": "YouTube Analytics nije učitan za ovaj video."}, "comment_analysis": comments or {"available": False, "reason": "Komentari nisu analizirani."}, "visual_analysis": visual or {"available": False, "reason": "Video fajl nije tehnički analiziran."}, "truth_policy": "Nedostajući podaci ostaju nedostupni; program ih ne zamenjuje nulom ili procenom."}
