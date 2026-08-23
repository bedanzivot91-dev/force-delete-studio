/* User-controlled YouTube scan size.
 * 0 = every video available through the connected YouTube account.
 * A positive integer = that many videos per channel. No app-side maximum.
 */
(() => {
  'use strict';

  function parseVideoLimit(value) {
    const raw = String(value ?? '').trim();
    if (!raw) return 0;
    const number = Number(raw);
    if (!Number.isSafeInteger(number) || number < 0) {
      throw new Error('Broj videa mora biti ceo broj 0 ili veći. 0 znači SVI.');
    }
    return number;
  }

  function savedLimit() {
    try { return parseVideoLimit(localStorage.getItem('sps-youtube-video-limit') ?? '0'); } catch (_) { return 0; }
  }

  function rememberLimit(value) {
    const limit = parseVideoLimit(value);
    try { localStorage.setItem('sps-youtube-video-limit', String(limit)); } catch (_) {}
    if ($('youtubeAudioMaxVideos')) $('youtubeAudioMaxVideos').value = String(limit);
    return limit;
  }

  function normalizeLegacyControls() {
    const maxVideos = $('youtubeAudioMaxVideos');
    if (maxVideos) {
      maxVideos.removeAttribute('max');
      maxVideos.min = '0';
      maxVideos.step = '1';
      maxVideos.value = String(savedLimit());
      const label = maxVideos.closest('label');
      if (label) {
        for (const node of [...label.childNodes]) {
          if (node.nodeType === Node.TEXT_NODE && node.textContent.trim()) node.textContent = 'Videa po kanalu — 0 = SVI ';
        }
      }
      maxVideos.addEventListener('change', () => {
        try { rememberLimit(maxVideos.value); } catch (error) { toast(error.message,'error'); }
      });
    }

    const pageInput = $('youtubeChannelScanPages');
    const pageLabel = pageInput?.closest('label');
    if (pageLabel) pageLabel.classList.add('hidden');
  }

  scanOwnedYoutube = async function scanOwnedYoutubeNoAppLimit() {
    const limit = rememberLimit($('youtubeAudioMaxVideos')?.value ?? savedLimit());
    const scanMode = $('youtubeChannelScanMode')?.value || 'new';
    try {
      await startBackground('/api/youtube/audio-analyze-owned', {
        max_pages:0,
        max_videos_per_channel:limit,
        candidate_limit:20,
        deep:false,
        reuse_cache:true,
        force:false,
        scan_mode:scanMode === 'full' ? 'all' : scanMode,
        detect_multiple:true,
        max_songs_per_video:6,
        cache_days:30,
        cache_gb:10,
      });
      $('taskPanel').classList.remove('hidden');
      $('taskPanel').scrollIntoView({behavior:'smooth'});
      toast(limit===0
        ? 'Provera kanala je pokrenuta bez brojčanog limita programa — čita do kraja YouTube paginacije.'
        : `Provera kanala je pokrenuta do ${limit} videa po kanalu, po tvom izboru.`, 'success', 10000);
    } catch (error) { toast(error.message,'error',15000); }
  };

  analyzeOwnedYoutubeAudio = async function analyzeOwnedYoutubeAudioNoAppLimit() {
    const selectedOnly=$('youtubeAudioSelectedOnly')?.checked===true;
    const ids=selectedOnly?selectedIds():[];
    if(selectedOnly&&!ids.length)return toast('Izaberi najmanje jednu Suno pesmu u Biblioteci ili isključi opciju „samo izabrane“.','error',9000);
    let limit;
    try { limit=rememberLimit($('youtubeAudioMaxVideos')?.value ?? savedLimit()); }
    catch(error){ return toast(error.message,'error',9000); }
    try{
      await startBackground('/api/youtube/audio-analyze-owned',{
        song_ids:ids,
        max_videos_per_channel:limit,
        candidate_limit:Number($('youtubeAudioCandidateLimit')?.value||16),
        deep:$('youtubeAudioDeep')?.checked===true,
        reuse_cache:$('youtubeAudioReuseCache')?.checked!==false,
        force:$('youtubeAudioForce')?.checked===true,
        max_pages:0,
        scan_mode:$('youtubeAudioScanMode')?.value||'new',
        detect_multiple:$('youtubeAudioDetectMultiple')?.checked!==false,
        max_songs_per_video:Number($('youtubeAudioMaxSongsPerVideo')?.value||6),
        cache_days:Number($('youtubeAudioCacheDays')?.value||30),
        cache_gb:10,
      });
      $('taskPanel').classList.remove('hidden');$('taskPanel').scrollIntoView({behavior:'smooth'});
      toast(limit===0?'YouTube ↔ Suno analiza SVIH dostupnih videa je pokrenuta.':`YouTube ↔ Suno analiza do ${limit} videa po kanalu je pokrenuta.`,'success',10000);
    }catch(error){toast(error.message,'error',15000);}
  };

  const originalLoadYoutubeCenter = loadYoutubeCenter;
  loadYoutubeCenter = async function loadYoutubeCenterUnbounded() {
    await originalLoadYoutubeCenter();
    try {
      const completeness = encodeURIComponent($('youtubeAudioCompletenessFilter')?.value || '');
      const [audioData, calendarData] = await Promise.all([
        api(`/api/youtube/audio-analysis?completeness=${completeness}&limit=0`, {timeoutMs:120000,retries:0}),
        api('/api/youtube/calendar?limit=0', {timeoutMs:120000,retries:0}),
      ]);
      state.youtubeAudioResults = audioData.rows || [];
      state.youtubeAudioSummary = audioData.summary || state.youtubeAudioSummary || {};
      state.youtubeCalendar = calendarData.rows || [];
      state.ytdlp = audioData.ytdlp || state.ytdlp;
      renderYoutubeAudioSummary();
      renderYtdlpStatus();
      renderYoutubeAudioResults();
      renderYoutubeCalendar();
    } catch (error) {
      if ($('youtubeAudioResultsList')) {
        const warning = document.createElement('div');
        warning.className = 'inline-message warning';
        warning.textContent = `Kompletan prikaz rezultata nije učitan: ${error.message}`;
        $('youtubeAudioResultsList').prepend(warning);
      }
    }
  };

  normalizeLegacyControls();
  window.SPSYoutubeVideoLimit = {parseVideoLimit,rememberLimit};
})();
