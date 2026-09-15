/* Arbitrary-count selection: 1, 10, 15, 100, 5000 or 0=all.
 * Runs in the shared app.js lexical scope.
 */
(() => {
  'use strict';
  if ($('exactDownloadSelection')) return;

  const css = document.createElement('style');
  css.textContent = `
    .exact-selection{margin-top:12px;padding:12px;border:1px solid #334155;background:#0c131c;border-radius:8px}
    .exact-selection-grid{display:grid;grid-template-columns:minmax(150px,220px) 1fr;gap:10px;align-items:end}
    .exact-selection label{display:flex;flex-direction:column;gap:5px}.exact-selection .button-row{margin:0}
    .exact-selection-note{margin:8px 0 0;color:#91a3b8;font-size:12px}
    @media(max-width:760px){.exact-selection-grid{grid-template-columns:1fr}}
  `;
  document.head.appendChild(css);

  function cleanCount(value) {
    const raw = String(value ?? '').trim();
    if (!raw) return 0;
    const number = Number(raw);
    if (!Number.isFinite(number) || number < 0 || !Number.isInteger(number)) {
      throw new Error('Broj pesama mora biti ceo broj 0 ili veći. 0 znači sve.');
    }
    return number;
  }

  function rememberCount(value) {
    try { localStorage.setItem('sps-exact-song-count', String(value)); } catch (_) {}
    qsa('[data-exact-count]').forEach(input => { if (input.value !== String(value)) input.value = String(value); });
  }

  async function selectExact(scope, count) {
    count = cleanCount(count);
    rememberCount(count);
    let params;
    if (scope === 'filtered') {
      params = currentSongFilterParams({limit:String(count)});
    } else {
      params = new URLSearchParams({
        search:'', filter:'all', collection_id:'0', source_group:'',
        date_from:'', date_to:'', min_duration:'0', max_duration:'0',
        // For Suno-only we filter local-* IDs client-side. Fetch all first so
        // selecting e.g. 100 means 100 Suno records, not 100 mixed records.
        limit:'0',
      });
    }
    const data = await api(`/api/song-ids?${params.toString()}`, {timeoutMs:120000,retries:0});
    let ids = Array.isArray(data.ids) ? data.ids.map(String) : [];
    if (scope === 'suno') ids = ids.filter(id => !id.startsWith('local-'));
    if (count > 0) ids = ids.slice(0, count);
    state.selected = new Set(ids);
    renderSongs();
    updateSelectionUi();
    const scopeText = scope === 'suno' ? 'sa Suno naloga' : 'iz trenutnog filtera';
    toast(`Izabrano ${ids.length} pesama ${scopeText}. ${count === 0 ? '0 = sve, bez limita programa.' : `Traženo: ${count}.`}`, 'success', 9000);
    return ids;
  }

  const saved = (() => { try { return localStorage.getItem('sps-exact-song-count') || '10'; } catch (_) { return '10'; } })();

  const downloadCount = $('downloadSelectedCount')?.closest('.big-number');
  if (downloadCount) {
    const box = document.createElement('div');
    box.id = 'exactDownloadSelection';
    box.className = 'exact-selection';
    box.innerHTML = `
      <div class="exact-selection-grid">
        <label><strong>Koliko pesama želiš?</strong><input data-exact-count id="exactDownloadCount" type="number" min="0" step="1" value="${escapeHtml(saved)}" placeholder="npr. 10, 15, 100, 5000 ili 0"></label>
        <div class="button-row">
          <button id="exactFilteredSelectBtn" type="button" class="btn primary">IZABERI TAJ BROJ IZ FILTERA</button>
          <button id="exactSunoSelectBtn" type="button" class="btn secondary">IZABERI TAJ BROJ SA SUNO</button>
        </div>
      </div>
      <p class="exact-selection-note"><strong>0 = SVE.</strong> Nema maksimuma 200, 5000 ili 20000. Možeš i ručno čekirati bilo koju kombinaciju pesama u Biblioteci.</p>`;
    downloadCount.insertAdjacentElement('afterend', box);
    $('exactFilteredSelectBtn').addEventListener('click', () => selectExact('filtered', $('exactDownloadCount').value).catch(e => toast(e.message,'error',10000)));
    $('exactSunoSelectBtn').addEventListener('click', () => selectExact('suno', $('exactDownloadCount').value).catch(e => toast(e.message,'error',10000)));
    $('exactDownloadCount').addEventListener('change', e => { try { rememberCount(cleanCount(e.target.value)); } catch (err) { toast(err.message,'error'); } });
  }

  const selectionActions = document.querySelector('#view-library .selection-actions');
  if (selectionActions && !$('libraryExactCount')) {
    const wrap = document.createElement('span');
    wrap.style.display = 'inline-flex';
    wrap.style.gap = '6px';
    wrap.style.alignItems = 'center';
    wrap.innerHTML = `<input data-exact-count id="libraryExactCount" type="number" min="0" step="1" value="${escapeHtml(saved)}" title="0 = sve" style="width:92px"><button id="libraryExactSelectBtn" type="button" class="btn small primary">IZABERI BROJ</button>`;
    selectionActions.prepend(wrap);
    $('libraryExactSelectBtn').addEventListener('click', () => selectExact('filtered', $('libraryExactCount').value).catch(e => toast(e.message,'error',10000)));
    $('libraryExactCount').addEventListener('change', e => { try { rememberCount(cleanCount(e.target.value)); } catch (err) { toast(err.message,'error'); } });
  }

  // Expose only a tiny test/debug surface; normal UI uses the buttons above.
  window.SPSExactSelection = {selectExact, cleanCount};
})();
