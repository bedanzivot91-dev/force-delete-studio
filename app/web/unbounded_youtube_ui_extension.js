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

  function installControl() {
    const card = $('youtubeReconcileCard');
    const oldButton = $('ytReconAllVideos');
    if (!card || !oldButton || $('ytReconVideoLimit')) return;

    const actions = oldButton.parentElement;
    const field = document.createElement('label');
    field.style.display = 'inline-flex';
    field.style.flexDirection = 'column';
    field.style.gap = '4px';
    field.style.minWidth = '215px';
    field.innerHTML = '<span class="muted" style="font-size:11px">VIDEA PO KANALU — 0 = SVI</span><input id="ytReconVideoLimit" type="number" min="0" step="1" value="0" placeholder="0 = svi, ili 10 / 100 / 5000">';
    actions.insertBefore(field, oldButton);

    // organized_ui_extension attached a hard-coded 5000 listener to the old
    // node. Replacing the node removes that listener instead of stacking two
    // scans on one click.
    const button = oldButton.cloneNode(true);
    button.textContent = 'PROVERI MOJE VIDEOE';
    button.title = '0 proverava sve dostupne videe; pozitivan broj je tvoj limit po kanalu.';
    oldButton.replaceWith(button);

    button.addEventListener('click', async () => {
      try {
        const limit = parseVideoLimit($('ytReconVideoLimit').value);
        try { localStorage.setItem('sps-youtube-video-limit', String(limit)); } catch (_) {}
        const body = {
          max_pages: 0,
          max_videos_per_channel: limit,
          candidate_limit: 20,
          deep: false,
          reuse_cache: true,
          force: false,
          scan_mode: 'all',
          detect_multiple: true,
          max_songs_per_video: 6,
          cache_days: 30,
          cache_gb: 10,
        };
        await startBackground('/api/youtube/audio-analyze-owned', body);
        $('taskPanel').classList.remove('hidden');
        $('taskPanel').scrollIntoView({behavior:'smooth'});
        toast(limit === 0
          ? 'Pokrenuta je provera SVIH dostupnih videa. Program nema brojčani limit; YouTube kvota/mreža i dalje važe.'
          : `Pokrenuta je provera do ${limit} videa po kanalu — tačno po tvom izboru.`, 'success', 12000);
      } catch (error) {
        toast(error.message, 'error', 15000);
      }
    });

    try {
      const saved = localStorage.getItem('sps-youtube-video-limit');
      if (saved !== null) $('ytReconVideoLimit').value = String(parseVideoLimit(saved));
    } catch (_) {}

    const note = document.createElement('p');
    note.className = 'fine-print';
    note.style.flexBasis = '100%';
    note.innerHTML = '<strong>NEMA LIMITA OD 5.000.</strong> Unesi 10, 15, 100, 5000 ili bilo koji drugi ceo broj. <strong>0 = svi video-snimci koje YouTube nalog može da izlista.</strong> Ako YouTube iscrpi API kvotu ili traži ponovnu prijavu, posao prijavljuje tu stvarnu spoljašnju grešku umesto da glumi da je kanal završen.';
    actions.appendChild(note);
  }

  // The old center loaded only 5,000 audio rows and 1,000 calendar rows even
  // after a larger scan. Keep its existing rendering, then replace those two
  // state slices with complete result sets and rerender only the affected UI.
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
      // The main center remains usable even if a huge result refresh fails.
      if ($('youtubeAudioResultsList')) {
        const warning = document.createElement('div');
        warning.className = 'inline-message warning';
        warning.textContent = `Kompletan prikaz rezultata nije učitan: ${error.message}`;
        $('youtubeAudioResultsList').prepend(warning);
      }
    }
    installControl();
  };

  installControl();
  window.SPSYoutubeVideoLimit = {parseVideoLimit};
})();
