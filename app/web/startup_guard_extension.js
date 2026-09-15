/* Startup task guard for large Suno libraries.
 *
 * app.js historically ran runSongFinderIndex(false) automatically whenever
 * even one song was missing a fingerprint.  On a 3k-song first run that means
 * the application starts a huge background job before the user asks for it,
 * occupies the single ACTIVE_TASK slot and makes unrelated buttons answer
 * "Drugi posao je već u toku".  Status checking is cheap; full fingerprinting
 * is now explicit from Pronalazač or the Suno↔YouTube status card.
 */
(() => {
  'use strict';

  autoRefreshSongFinderIndex = async function autoRefreshSongFinderIndexSafe() {
    try {
      const status = await api('/api/song-finder/status', {timeoutMs:15000,retries:0});
      const missing = Number(status.songs_not_indexed || 0);
      if (missing > 0) {
        const box = $('songFinderIndexStatus');
        if (box) {
          box.className = 'inline-message warning';
          box.textContent = `${missing} Suno pesama još nema audio otisak. Indeksiranje se više NE pokreće samo pri startu programa — pokreni ga kada ti odgovara.`;
        }
      }
    } catch (_) {
      // Startup must never become blocked by an optional index-status check.
    }
  };
})();
