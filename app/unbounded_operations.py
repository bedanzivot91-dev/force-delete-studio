from __future__ import annotations

"""Unbounded user-controlled operations for large libraries/channels.

The application used to contain several unrelated hard ceilings (100 pages,
5,000 videos, 5,000/10,000 result rows).  Those values were implementation
shortcuts, not user choices.  This module removes those application-side caps
from the user's own-channel workflow while keeping two explicit semantics:

* a positive number means exactly the user-requested upper bound;
* 0 means "all available", with pagination continuing until YouTube says there
  is no next page (or the user pauses/stops the job).

This cannot remove YouTube's own API quota, authentication, network or account
limits.  It only guarantees that Suno Pesme Studio itself does not silently
truncate the operation at 5,000.
"""

from typing import Any, Iterator

import youtube_tools as yt


def _positive_or_unlimited(value: Any) -> int:
    try:
        number = int(value or 0)
    except (TypeError, ValueError):
        number = 0
    return max(0, number)


def _iter_channel_videos(
    core: Any,
    channel: dict[str, Any],
    *,
    api_key: str = "",
    access_token: str = "",
    max_videos: int = 0,
    known_ids: set[str] | None = None,
) -> Iterator[dict[str, Any]]:
    """Stream a channel page by page without an application page ceiling.

    For OAuth/API channels this follows nextPageToken until exhaustion.  RSS is
    inherently only a recent public feed; when no API/OAuth credential exists we
    preserve that upstream limitation instead of pretending it is a full scan.
    """
    max_videos = _positive_or_unlimited(max_videos)
    channel_id = str(channel.get("channel_id") or "")

    if api_key or access_token:
        uploads = str(channel.get("uploads_playlist") or "")
        if not uploads:
            resolved = yt.resolve_channel(channel_id, api_key, access_token=access_token)
            uploads = str(resolved.get("uploads_playlist") or "")
        if not uploads:
            raise core.YouTubeAPIError("Kanal nema pronađenu Uploads plejlistu.")

        token = ""
        yielded = 0
        seen_tokens: set[str] = set()
        while True:
            params: dict[str, Any] = {
                "part": "snippet,contentDetails,status",
                "playlistId": uploads,
                "maxResults": 50,
            }
            if token:
                params["pageToken"] = token
            data = yt._request_json(yt._api_url("playlistItems", api_key, **params), access_token=access_token)
            page_items = data.get("items") if isinstance(data.get("items"), list) else []
            page_ids: list[str] = []
            for entry in page_items:
                if not isinstance(entry, dict):
                    continue
                snippet = entry.get("snippet") if isinstance(entry.get("snippet"), dict) else {}
                content = entry.get("contentDetails") if isinstance(entry.get("contentDetails"), dict) else {}
                resource = snippet.get("resourceId") if isinstance(snippet.get("resourceId"), dict) else {}
                video_id = str(content.get("videoId") or resource.get("videoId") or "")
                if video_id:
                    page_ids.append(video_id)

            # Incremental scan: an all-known newest-first page means every later
            # page is older than data already stored, so stop before hydrating it.
            if known_ids is not None and page_ids and all(video_id in known_ids for video_id in page_ids):
                break

            hydrated = yt._hydrate_videos(api_key, page_ids, access_token=access_token)
            for video_id in page_ids:
                if known_ids is not None and video_id in known_ids:
                    continue
                video = hydrated.get(video_id)
                if not video:
                    continue
                yield video
                yielded += 1
                if max_videos and yielded >= max_videos:
                    return

            next_token = str(data.get("nextPageToken") or "")
            if not next_token or not page_items:
                return
            # Defensive loop guard for a malformed upstream response.  This is
            # not a quantity cap; it only prevents an identical token forever.
            if next_token in seen_tokens:
                raise core.YouTubeAPIError("YouTube je vratio isti pagination token više puta; skeniranje je zaustavljeno da ne uđe u beskonačnu petlju.")
            seen_tokens.add(next_token)
            token = next_token

    else:
        # YouTube RSS itself exposes only a small recent public feed.  Returning
        # those entries is more honest than labelling them "all videos".
        rows = yt.list_channel_videos(channel, api_key="", access_token="", max_pages=1)
        for index, video in enumerate(rows):
            if max_videos and index >= max_videos:
                break
            if known_ids is not None and str(video.get("video_id") or "") in known_ids:
                continue
            yield video


def _all_audio_analysis_state(db: Any) -> tuple[set[str], set[str]]:
    """Return analysed/uncertain video IDs without the old 5k/10k row cap."""
    analysed: set[str] = set()
    uncertain: set[str] = set()
    with db._connect() as conn:
        rows = conn.execute(
            """SELECT v.video_id,m.audio_checked_at,m.completeness_status,m.confidence
               FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id
               WHERE v.is_owned_channel=1"""
        ).fetchall()
        for row in rows:
            video_id = str(row["video_id"] or "")
            if not video_id:
                continue
            if str(row["audio_checked_at"] or ""):
                analysed.add(video_id)
            if str(row["completeness_status"] or "") in ("", "different_audio") or str(row["confidence"] or "") == "low":
                uncertain.add(video_id)
    return analysed, uncertain


def _all_found_song_ids(db: Any) -> set[str]:
    with db._connect() as conn:
        return {
            str(row[0])
            for row in conn.execute(
                """SELECT DISTINCT m.song_id
                   FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id
                   WHERE v.is_owned_channel=1
                     AND m.completeness_status IN ('complete','almost_complete','partial','short_clip')"""
            ).fetchall()
            if str(row[0] or "")
        }


def _estimate_video_total(channels: list[dict[str, Any]], max_videos: int) -> int:
    total = 0
    for channel in channels:
        declared = max(0, int(channel.get("video_count") or 0))
        if max_videos:
            total += min(declared, max_videos) if declared else max_videos
        else:
            total += declared
    return total


def _install_unbounded_owned_channel_scan(core: Any) -> dict[str, Any]:
    def scan_owned_youtube_channels(task: Any, options: dict[str, Any]) -> None:
        channels = [c for c in core.DB.list_youtube_channels() if int(c.get("is_owned") or 0) == 1]
        if not channels:
            raise RuntimeError("Dodaj najmanje jedan svoj YouTube kanal u YouTube centru.")
        include_private_unlisted = bool(options.get("include_private_unlisted", True))
        scan_mode = str(options.get("scan_mode") or "new").strip().lower()
        max_videos = _positive_or_unlimited(options.get("max_videos_per_channel"))
        songs = core.DB.export_rows()
        if not songs:
            raise RuntimeError("Suno biblioteka je prazna. Prvo uvezi pesme.")
        owned_ids = {str(c.get("channel_id") or "") for c in channels}
        run_id = core.DB.start_youtube_scan_run("owned_channels", len(channels), len(songs))
        task.total = _estimate_video_total(channels, max_videos) or len(channels)
        matched = 0
        errors = 0
        processed = 0
        found_collection = core.DB.get_collection_by_slug("youtube-pronađene-objave")
        review_collection = core.DB.get_collection_by_slug("youtube-audio-provera")
        try:
            for channel in channels:
                if task.cancel_event.is_set():
                    break
                label = str(channel.get("title") or channel.get("channel_id") or "YouTube kanal")
                profile_id = str(channel.get("oauth_profile_id") or "")
                if profile_id:
                    api_key, access_token = "", core.get_youtube_access_token(profile_id, required=True)
                else:
                    api_key, access_token = core.youtube_credentials()
                known_ids = core.DB.list_youtube_video_ids(str(channel.get("channel_id") or "")) if scan_mode == "new" else None
                latest = ""
                channel_count = 0
                channel_matches = 0
                task.log(
                    f"Čitam YouTube kanal: {label} · "
                    + (f"najviše {max_videos} videa po tvom izboru." if max_videos else "SVI dostupni videi, bez limita programa."),
                    "info",
                )
                try:
                    for video in _iter_channel_videos(
                        core, channel, api_key=api_key, access_token=access_token,
                        max_videos=max_videos, known_ids=known_ids,
                    ):
                        if task.cancel_event.is_set():
                            break
                        core.wait_if_paused(task)
                        if not include_private_unlisted and str(video.get("privacy_status") or "public") != "public":
                            continue
                        published_at = str(video.get("published_at") or "")
                        if published_at and (not latest or published_at > latest):
                            latest = published_at
                        core.DB.upsert_youtube_video(video, is_owned_channel=True)
                        song, match = core._best_song_match(video, songs, owned_ids)
                        if song and match and float(match.get("score") or 0) >= float(options.get("threshold") or 68):
                            core.DB.upsert_youtube_match(str(song["id"]), str(video["video_id"]), match)
                            channel_matches += 1
                            matched += 1
                            if review_collection:
                                core.DB.add_songs_to_collection(int(review_collection["id"]), [str(song["id"])])
                            if found_collection:
                                core.DB.add_songs_to_collection(int(found_collection["id"]), [str(song["id"])])
                        channel_count += 1
                        processed += 1
                        if processed > int(task.total or 0):
                            task.total = processed
                        task.set_progress(processed, max(processed, int(task.total or processed)), str(video.get("title") or video.get("video_id") or label))
                except Exception as exc:
                    errors += 1
                    task.log(f"Kanal {label} nije pročitan do kraja: {exc}", "error")
                core.DB.update_youtube_channel_scan(str(channel.get("channel_id") or ""), latest, channel_count)
                task.log(f"{label}: pročitano {channel_count} videa, kandidata za tvoje pesme {channel_matches}.", "success" if channel_count else "warning")

            summary = f"YouTube kanali provereni: {len(channels)}. Pročitano {processed} videa. Pronađene objave: {matched}. Greške: {errors}."
            core.DB.finish_youtube_scan_run(run_id, matched, errors, summary)
            if task.cancel_event.is_set():
                task.finish_partial(summary + " Provera je ručno zaustavljena.")
            elif errors and processed:
                task.finish_partial(summary)
            elif errors and not processed:
                task.fail(summary)
            else:
                task.finish(summary)
        except Exception:
            core.DB.finish_youtube_scan_run(run_id, matched, errors + 1, "Skeniranje je prekinuto greškom.")
            raise

    core.scan_owned_youtube_channels = scan_owned_youtube_channels
    return {"scan_owned_youtube_channels": scan_owned_youtube_channels}


def _install_unbounded_owned_audio_scan(core: Any) -> dict[str, Any]:
    def analyze_owned_youtube_audio(task: Any, options: dict[str, Any]) -> None:
        only_channel_id = str(options.get("channel_id") or "").strip()
        channels = [c for c in core.DB.list_youtube_channels() if int(c.get("is_owned") or 0) == 1]
        if only_channel_id:
            channels = [c for c in channels if str(c.get("channel_id") or "") == only_channel_id]
        if not channels:
            raise RuntimeError("Nije pronađen nijedan povezani moj YouTube kanal.")

        song_ids = [str(x) for x in (options.get("song_ids") or []) if str(x)]
        songs = core.DB.export_rows(song_ids or None)
        if not songs:
            raise RuntimeError("Suno biblioteka je prazna ili nema izabranih pesama.")

        core.ensure_ffmpeg(lambda m, p: task.log(m, "info") if p in (0, 100) else None)
        core.ensure_ytdlp(lambda m, p: task.log(m, "info") if p in (0, 100) else None)

        # The owned-channel matcher depends on the audio index, not titles.
        # Real Shorts commonly use quote titles that have no words in common
        # with the Suno song. Starting without the full available index made a
        # synthetic pre-indexed E2E pass while the user's real library found 0.
        index_before = core.song_finder_status()
        missing_before = int(index_before.get("songs_not_indexed") or 0)
        if missing_before:
            task.log(
                f"Pre YouTube provere pravim {missing_before} nedostajućih Suno audio-otisaka. "
                "Bez toga rezultat ne bi bio pouzdan.",
                "warning",
            )
            core.song_finder_index_task(task, {
                "force": False,
                "finish_task": False,
                "required_for_youtube": True,
            })
            if task.cancel_event.is_set():
                task.finish_partial("Zaustavljeno tokom pripreme Suno audio-indeksa; YouTube provera nije pokrenuta.")
                return
        index_after = core.song_finder_status()
        indexed = int(index_after.get("songs_indexed") or 0)
        available = int(index_after.get("songs_with_audio") or 0)
        still_missing = int(index_after.get("songs_not_indexed") or 0)
        if available and indexed == 0:
            raise RuntimeError(
                "Nijedna Suno pesma nema napravljen audio-otisak. Ponovo poveži Suno nalog i sinhronizuj biblioteku; "
                "YouTube provera nije pokrenuta da ne bi lažno prijavila 0 rezultata."
            )
        if still_missing:
            task.log(
                f"Upozorenje: {still_missing} Suno pesama nema dostupan audio-otisak i ne može biti pronađeno dok se "
                "ne osveži Suno veza. Nastavljam sa {indexed} pouzdano indeksiranih pesama.",
                "warning",
            )
        songs = core.DB.export_rows(song_ids or None)

        max_videos = _positive_or_unlimited(options.get("max_videos_per_channel"))
        scan_mode = str(options.get("scan_mode") or "new").strip().lower()
        analysed_ids, uncertain_ids = _all_audio_analysis_state(core.DB)
        owned_ids = {str(c.get("channel_id") or "") for c in channels}
        seen: set[str] = set()
        task.total = _estimate_video_total(channels, max_videos) or 1
        matched = 0
        errors = 0
        processed = 0
        skipped_existing = 0
        run_id = core.DB.start_youtube_scan_run("owned_audio_analysis", len(channels), len(songs))

        task.log(
            "YouTube audio provera: "
            + (f"najviše {max_videos} videa po kanalu (tvoj izbor)." if max_videos else "SVI dostupni videi po kanalu — aplikacija nema brojčani limit."),
            "info",
        )
        try:
            for channel in channels:
                if task.cancel_event.is_set():
                    break
                profile_id = str(channel.get("oauth_profile_id") or "")
                if profile_id:
                    api_key, access_token = "", core.get_youtube_access_token(profile_id, required=True)
                else:
                    api_key, access_token = core.youtube_credentials()
                label = str(channel.get("title") or channel.get("channel_id") or "YouTube kanal")
                channel_seen = 0
                latest = ""
                try:
                    for video in _iter_channel_videos(
                        core, channel, api_key=api_key, access_token=access_token,
                        max_videos=max_videos, known_ids=None,
                    ):
                        if task.cancel_event.is_set():
                            break
                        core.wait_if_paused(task)
                        video_id = str(video.get("video_id") or "")
                        if not video_id or video_id in seen:
                            continue
                        seen.add(video_id)
                        channel_seen += 1
                        published_at = str(video.get("published_at") or "")
                        if published_at and (not latest or published_at > latest):
                            latest = published_at

                        # 0 means metadata did not return duration.  Do not throw
                        # it away.  Known clips shorter than the real fingerprint
                        # minimum are the only ones that cannot be analysed.
                        duration = float(video.get("duration") or 0)
                        if 0 < duration < float(core.SHORT_CLIP_MIN_MATCH_SECONDS):
                            continue
                        if options.get("shorts_only") and duration > 70:
                            continue
                        if scan_mode == "new" and video_id in analysed_ids:
                            skipped_existing += 1
                            continue
                        if scan_mode == "uncertain" and video_id in analysed_ids and video_id not in uncertain_ids:
                            skipped_existing += 1
                            continue

                        core.DB.upsert_youtube_video(video, is_owned_channel=True)
                        processed += 1
                        if processed > int(task.total or 0):
                            task.total = processed
                        task.set_progress(processed - 1, max(processed, int(task.total or processed)), str(video.get("title") or video_id))
                        try:
                            result = core._analyse_video_against_songs(task, video, songs, owned_ids, options)
                            if result:
                                matched += 1
                        except (core.AudioMatchCancelled, core.AudioCancelled):
                            task.cancel_event.set()
                            break
                        except Exception as exc:
                            errors += 1
                            task.log(f"Audio analiza nije uspela za „{video.get('title') or video_id}“: {exc}", "error")
                        task.set_progress(processed, max(processed, int(task.total or processed)), str(video.get("title") or video_id))
                except Exception as exc:
                    errors += 1
                    task.log(f"Kanal „{label}“ nije pročitan do kraja: {exc}", "error")
                core.DB.update_youtube_channel_scan(str(channel.get("channel_id") or ""), latest, channel_seen)

            found_song_ids = _all_found_song_ids(core.DB)
            not_found_collection = core.DB.get_collection_by_slug("youtube-nisu-pronađene")
            if not_found_collection:
                collection_id = int(not_found_collection["id"])
                for song in songs:
                    song_id = str(song.get("id") or "")
                    if song_id in found_song_ids:
                        core.DB.remove_songs_from_collection(collection_id, [song_id])
                    else:
                        core.DB.add_songs_to_collection(collection_id, [song_id])

            cleanup = core.cleanup_youtube_audio_cache(
                max_age_days=max(1, int(options.get("cache_days") or 30)),
                max_size_gb=max(1.0, float(options.get("cache_gb") or 10)),
            )
            summary = (
                f"Audio analiza završena: analizirano {processed} YouTube videa, "
                f"{matched} povezano sa Suno originalima, {errors} grešaka"
                + (f", {skipped_existing} već proverenih preskočeno" if skipped_existing else "")
                + f". Očišćeno iz keša: {cleanup.get('removed', 0)} fajlova."
            )
            core.DB.finish_youtube_scan_run(run_id, matched, errors, summary)
            if task.cancel_event.is_set():
                task.finish_partial(summary + " Provera je ručno zaustavljena.")
            elif errors and processed:
                task.finish_partial(summary)
            elif errors and not processed:
                task.fail(summary)
            else:
                task.finish(summary)
        except Exception:
            core.DB.finish_youtube_scan_run(run_id, matched, errors + 1, "Audio skeniranje je prekinuto greškom.")
            raise

    core.analyze_owned_youtube_audio = analyze_owned_youtube_audio
    return {"analyze_owned_youtube_audio": analyze_owned_youtube_audio}


def _install_unbounded_result_queries(core: Any) -> dict[str, Any]:
    db_type = type(core.DB)

    def list_youtube_audio_analyses(self: Any, completeness_status: str = "", channel_id: str = "", limit: int = 0) -> list[dict[str, Any]]:
        where = ["v.is_owned_channel=1"]
        params: list[Any] = []
        if completeness_status:
            where.append("m.completeness_status=?")
            params.append(str(completeness_status))
        if channel_id:
            where.append("v.channel_id=?")
            params.append(str(channel_id))
        sql = f"""SELECT m.*,v.title video_title,v.channel_id,v.channel_title,v.published_at,v.url video_url,
                         v.thumbnail_url,v.duration video_duration,v.view_count,v.like_count,v.comment_count,v.privacy_status,
                         s.title song_title,s.duration song_duration,s.source_url,s.youtube_url original_url,
                         s.youtube_published_at original_published_at
                  FROM youtube_matches m JOIN youtube_videos v ON v.video_id=m.video_id JOIN songs s ON s.id=m.song_id
                  WHERE {' AND '.join(where)}
                  ORDER BY CASE m.completeness_status WHEN 'different_audio' THEN 0 WHEN '' THEN 1 WHEN 'short_clip' THEN 2 WHEN 'partial' THEN 3 WHEN 'almost_complete' THEN 4 WHEN 'complete' THEN 5 ELSE 6 END,
                           m.audio_checked_at DESC,v.published_at DESC"""
        requested = _positive_or_unlimited(limit)
        if requested:
            sql += " LIMIT ?"
            params.append(requested)
        with self._connect() as conn:
            return [dict(row) for row in conn.execute(sql, params).fetchall()]

    def publication_calendar(self: Any, limit: int = 0) -> list[dict[str, Any]]:
        sql = """SELECT s.id song_id,s.title song_title,v.video_id,v.title video_title,v.channel_id,v.channel_title,v.published_at,v.url video_url,v.view_count,m.score,m.status,
                        m.completeness_status,m.audio_score,m.coverage_percent,m.audio_checked_at
                 FROM youtube_matches m JOIN songs s ON s.id=m.song_id JOIN youtube_videos v ON v.video_id=m.video_id
                 WHERE m.match_type='owned_publication' ORDER BY v.published_at DESC"""
        params: list[Any] = []
        requested = _positive_or_unlimited(limit)
        if requested:
            sql += " LIMIT ?"
            params.append(requested)
        with self._connect() as conn:
            return [dict(row) for row in conn.execute(sql, params).fetchall()]

    db_type.list_youtube_audio_analyses = list_youtube_audio_analyses
    db_type.publication_calendar = publication_calendar
    return {
        "_iter_channel_videos_unbounded": _iter_channel_videos,
        "_all_audio_analysis_state_unbounded": _all_audio_analysis_state,
    }


def apply(core: Any) -> dict[str, Any]:
    if getattr(core, "_unbounded_operations_v1_installed", False):
        return {}
    exports: dict[str, Any] = {}
    exports.update(_install_unbounded_owned_channel_scan(core))
    exports.update(_install_unbounded_owned_audio_scan(core))
    exports.update(_install_unbounded_result_queries(core))
    core._unbounded_operations_v1_installed = True
    core.runtime_log(
        "Unbounded operations aktivni: 0=SVI YouTube videi, pozitivan broj=korisnikov limit; uklonjeni interni 5k/10k result capovi.",
        "info",
    )
    return exports
