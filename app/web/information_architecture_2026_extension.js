/* Suno Pesme Studio — information architecture cleanup (2026).
 *
 * The historical YouTube Center accumulated general Suno/library/audio/system
 * tools.  This extension MOVES the existing DOM cards to the page where the
 * action logically belongs.  Nodes are never cloned, so original IDs and event
 * listeners stay intact.
 */
(() => {
  'use strict';
  if (document.getElementById('spsIa2026Marker')) return;
  const marker = document.createElement('span');
  marker.id = 'spsIa2026Marker';
  marker.hidden = true;
  document.body.appendChild(marker);

  const byHeading = (root, selector, title) => [...(root?.querySelectorAll(selector) || [])]
    .find(node => (node.querySelector('h2,h3')?.textContent || '').trim() === title) || null;

  function makeSection(id, title, description, open = false) {
    const details = document.createElement('details');
    details.id = id;
    details.className = 'ia2026-section panel';
    details.open = open;
    details.innerHTML = `<summary><span>${title}</span><small>${description}</small></summary><div class="ia2026-grid"></div>`;
    return details;
  }

  const css = document.createElement('style');
  css.id = 'spsIa2026Style';
  css.textContent = `
    .ia2026-section{margin:16px 0;overflow:hidden}
    .ia2026-section>summary{list-style:none;cursor:pointer;display:flex;align-items:center;justify-content:space-between;gap:18px;padding:16px 18px;border-bottom:1px solid transparent}
    .ia2026-section>summary::-webkit-details-marker{display:none}
    .ia2026-section>summary span{font-weight:800;letter-spacing:-.015em}
    .ia2026-section>summary small{color:var(--m-muted,#9aa5b5);font-size:13px;font-weight:500;text-align:right}
    .ia2026-section[open]>summary{border-bottom-color:var(--m-line,rgba(148,163,184,.16))}
    .ia2026-grid{display:grid;grid-template-columns:repeat(2,minmax(300px,1fr));gap:12px;padding:14px}
    .ia2026-grid>.tool-card,.ia2026-grid>.panel{margin:0!important;height:100%}
    .ia2026-empty-source{display:none!important}
    .ia2026-history-count{display:inline-flex;align-items:center;justify-content:center;min-width:34px;min-height:28px;padding:4px 9px;border:1px solid var(--m-line,rgba(148,163,184,.16));border-radius:999px;background:var(--m-accent-soft,rgba(124,92,255,.16));color:var(--m-text,#f6f9ff);font-weight:800}
    @media(max-width:980px){.ia2026-grid{grid-template-columns:1fr}.ia2026-section>summary{align-items:flex-start;flex-direction:column}.ia2026-section>summary small{text-align:left}}
  `;
  document.head.appendChild(css);

  const tools = document.getElementById('view-tools');
  const library = document.getElementById('view-library');
  const audio = document.getElementById('view-audio');
  const importView = document.getElementById('view-import');
  const settings = document.getElementById('view-settings');
  const recognition = document.getElementById('view-recognition');

  // app.js has always updated recognitionHistoryCount when status/history loads,
  // but the historical HTML never created the element. Complete that control
  // instead of keeping a silent optional reference that can never be visible.
  if (recognition && !document.getElementById('recognitionHistoryCount')) {
    const historyHeading = [...recognition.querySelectorAll('.section-title')].find(section =>
      (section.querySelector('h3')?.textContent || '').trim() === 'Istorija AudD pokušaja'
    );
    if (historyHeading) {
      const count = document.createElement('span');
      count.id = 'recognitionHistoryCount';
      count.className = 'ia2026-history-count';
      count.textContent = String(Array.isArray(state?.recognitionHistory) ? state.recognitionHistory.length : 0);
      count.title = 'Broj sačuvanih AudD pokušaja';
      historyHeading.appendChild(count);
    }
  }

  if (!tools || !library || !audio || !importView || !settings) return;

  const toolGrid = tools.querySelector('.tool-grid');
  if (!toolGrid) return;

  // SUNO: account/sync monitoring belongs with connection and import.
  const sunoSection = makeSection(
    'ia2026SunoOperations',
    'Suno nalog i automatska provera',
    'Nove pesme i periodična provera naloga.',
    false,
  );
  const sunoCard = byHeading(toolGrid, '.tool-card', 'Nove Suno pesme');
  if (sunoCard) sunoSection.querySelector('.ia2026-grid').appendChild(sunoCard);
  if (sunoSection.querySelector('.tool-card')) importView.appendChild(sunoSection);

  // AUDIO: batch audio presets belong on the Audio page.
  const audioSection = makeSection(
    'ia2026AudioBatchOperations',
    'Masovna audio obrada',
    'Klipovi, normalizacija i fade za izabrane pesme.',
    false,
  );
  const audioCard = byHeading(toolGrid, '.tool-card', 'Brza audio obrada');
  if (audioCard) audioSection.querySelector('.ia2026-grid').appendChild(audioCard);
  if (audioSection.querySelector('.tool-card')) audio.appendChild(audioSection);

  // LIBRARY: metadata, collections, export and reports are library operations.
  const librarySection = makeSection(
    'ia2026LibraryOperations',
    'Biblioteka — masovne akcije i izveštaji',
    'Ocene, oznake, statusi, kolekcije, izvoz i provera biblioteke.',
    false,
  );
  const libraryGrid = librarySection.querySelector('.ia2026-grid');
  ['Favoriti i ocene', 'Oznake i status', 'Brze kolekcije', 'Izvoz', 'Izveštaji'].forEach(title => {
    const card = byHeading(toolGrid, '.tool-card', title);
    if (card) libraryGrid.appendChild(card);
  });
  if (libraryGrid.children.length) library.appendChild(librarySection);

  // SETTINGS: backup/data-folder/log cleanup are system maintenance, not YouTube.
  const systemSection = makeSection(
    'ia2026SystemOperations',
    'Backup i održavanje podataka',
    'Rezervne kopije, data folder i čišćenje dnevnika.',
    false,
  );
  const maintenanceCard = byHeading(toolGrid, '.tool-card', 'Backup i održavanje');
  if (maintenanceCard) systemSection.querySelector('.ia2026-grid').appendChild(maintenanceCard);
  if (systemSection.querySelector('.tool-card')) settings.appendChild(systemSection);

  // If no general-purpose cards remain, the old grid has no job in YouTube.
  if (!toolGrid.querySelector('.tool-card')) toolGrid.classList.add('ia2026-empty-source');

  // Hide tab choices whose content has been moved away. YouTube keeps only
  // channel/results/coverage/intelligence concepts on its own page.
  tools.querySelectorAll('.tools-tab').forEach(tab => {
    if (['library-tools', 'system'].includes(String(tab.dataset.toolsTab || ''))) tab.classList.add('hidden');
  });

  // Titles are explicit, so there is no ambiguity about where a function lives.
  const titleMap = {
    library: ['Biblioteka', 'Pesme, izbor, kolekcije, oznake, izvoz i izveštaji.'],
    import: ['Suno', 'Povezivanje naloga, sinhronizacija i provera novih Suno pesama.'],
    audio: ['Audio', 'Isecanje, obrada i masovne audio radnje.'],
    production: ['Video Studio', 'Jedna radna površina za timeline, titlove, preview i render.'],
    tools: ['YouTube', 'Moji kanali, audio prepoznavanje, rezultati i YouTube analitika.'],
    settings: ['Podešavanja', 'Program, backup, održavanje, teme, pristupačnost i napredni moduli.'],
  };
  Object.entries(titleMap).forEach(([key, value]) => { if (typeof viewText === 'object') viewText[key] = value; });
})();
