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
    positive = {"lepo", "prelepo", "odlično", "bravo", "volim", "emocija", "pogodilo", "najbolje"}
    negative = {"loše", "dosadno", "glupo", "mrzim", "slabo", "predugo", "mutno", "tiho"}
    words = Counter()
    pos = neg = questions = 0
    for row in rows:
        text = str(row.get("text") or "").lower()
        tokens = re.findall(r"[a-zA-ZčćžšđČĆŽŠĐ]{4,}", text)
        words.update(tokens)
        pos += int(bool(set(tokens) & positive)); neg += int(bool(set(tokens) & negative)); questions += int("?" in text)
    top = sorted(rows, key=lambda x: (int(x.get("likes") or 0), int(x.get("replies") or 0)), reverse=True)[:10]
    return {"count": len(rows), "positive": pos, "negative": neg, "questions": questions, "top_words": words.most_common(20), "best_comments": top}


def visual_video_analysis(path: Path, ffmpeg: str, ffprobe: str) -> dict[str, Any]:
    probe = subprocess.run([ffprobe, "-v", "error", "-show_entries", "format=duration:stream=width,height,r_frame_rate", "-of", "json", str(path)], capture_output=True, text=True, timeout=60)
    info = json.loads(probe.stdout or "{}")
    stream = next((x for x in info.get("streams") or [] if x.get("width")), {})
    width, height = int(stream.get("width") or 0), int(stream.get("height") or 0)
    scan = subprocess.run([ffmpeg, "-hide_banner", "-i", str(path), "-vf", "blackdetect=d=0.5:pix_th=0.10,freezedetect=n=-50dB:d=2,blurdetect", "-an", "-f", "null", "-"], capture_output=True, text=True, timeout=900)
    log = (scan.stderr or "")[-200000:]
    black = [{"start": float(a), "end": float(b)} for a, b in re.findall(r"black_start:([\d.]+).*?black_end:([\d.]+)", log)]
    freezes = [{"start": float(x)} for x in re.findall(r"freeze_start:\s*([\d.]+)", log)]
    blur = [float(x) for x in re.findall(r"blur mean:\s*([\d.]+)", log)]
    issues = []
    if black: issues.append(f"Pronađeno crnih/praznih delova: {len(black)}")
    if freezes: issues.append(f"Pronađeno dugih nepomičnih delova: {len(freezes)}")
    ratio = width / max(height, 1) if width and height else 0
    if ratio and min(abs(ratio - 9/16), abs(ratio - 16/9), abs(ratio - 1)) > 0.03:
        issues.append("Format nije standardnih 9:16, 16:9 ili 1:1.")
    good = [x for x in ("Nema crnih delova" if not black else "", "Nema dugih zamrznutih scena" if not freezes else "") if x]
    return {"path": str(path), "duration": float((info.get("format") or {}).get("duration") or 0), "width": width, "height": height, "orientation": "vertical" if height > width else "horizontal", "black_segments": black, "freeze_segments": freezes, "blur_mean": sum(blur)/len(blur) if blur else None, "issues": issues, "good": good}


def suno_snapshot_diff(previous: list[dict[str, Any]], current: list[dict[str, Any]]) -> dict[str, Any]:
    old = {str(x.get("id")): x for x in previous}; new = {str(x.get("id")): x for x in current}
    fields = ("title", "lyrics", "prompt", "tags", "audio_url", "image_url", "model_version")
    changed = []
    for song_id in old.keys() & new.keys():
        differences = [f for f in fields if str(old[song_id].get(f) or "") != str(new[song_id].get(f) or "")]
        if differences: changed.append({"id": song_id, "title": new[song_id].get("title"), "fields": differences})
    return {"new": [new[x] for x in new.keys()-old.keys()], "missing": [old[x] for x in old.keys()-new.keys()], "changed": changed}


def quota_estimate(channels: int, pages: int, comment_pages: int = 0) -> dict[str, int]:
    return {"playlist_units": channels * pages, "video_detail_units": channels * pages, "comment_units": channels * comment_pages, "estimated_total": channels * (pages * 2 + comment_pages)}


def review_priority(result: dict[str, Any]) -> dict[str, Any]:
    confidence = float(result.get("confidence") or 0); seconds = float(result.get("matched_seconds") or result.get("covered_seconds") or 0)
    needs_review = confidence < 82 or seconds < 12
    return {"needs_review": needs_review, "priority": "high" if confidence < 65 else "normal", "reason": "Kratak ili nepouzdan audio pogodak" if needs_review else "Dovoljno jak automatski pogodak"}
