/* UI organization layer.
 *
 * The original application accumulated useful functions in several historical
 * sections.  This extension does not duplicate those functions: it MOVES the
 * existing DOM panels (and therefore keeps their existing event handlers) into
 * a clear workflow: Suno -> Library/Audio -> Video Studio -> YouTube.  System,
 * backup and maintenance controls are moved out of Video Studio into Settings.
 */
(() => {
  'use strict';

  const production = $('view-production');
  const settings = $('view-settings');
  const tools = $('view-tools');
  const importView = $('view-import');
  if (!production || !settings || !tools || $('organizedWorkflowRibbon')) return;

  const css = document.createElement('style');
  css.textContent = `
    .organized-workflow{display:grid;grid-template-columns:repeat(5,minmax(130px,1fr));gap:8px;margin:0 0 14px;padding:10px;border:1px solid #293548;background:#0e141c;border-radius:8px}
    .organized-step{display:flex;align-items:center;gap:9px;padding:10px 12px;border:1px solid #2a3646;background:#111923;border-radius:7px;cursor:pointer;text-align:left;color:#dce7f7}
    .organized-step:hover{border-color:#3b82f6;background:#122034}.organized-step b{display:grid;place-items:center;width:25px;height:25px;border-radius:50%;background:#2563eb;color:white;flex:0 0 auto}.organized-step span{font-size:12px;line-height:1.25}
    .organized-section{margin:12px 0;padding:14px;border:1px solid #2a3646;background:#10161e;border-radius:8px}.organized-section>.section-title{margin-bottom:10px}
    .organized-grid{display:grid;grid-template-columns:repeat(2,minmax(280px,1fr));gap:12px}.organized-grid>.panel{margin:0!important}
    .organized-maintenance{margin-top:18px}.organized-maintenance summary{cursor:pointer;font-weight:700;padding:14px;border:1px solid #334155;background:#111923;border-radius:8px}.organized-maintenance[open] summary{border-radius:8px 8px 0 0}.organized-maintenance-body{border:1px solid #334155;border-top:0;padding:12px;background:#0d131a;border-radius:0 0 8px 8px}
    .yt-reconcile{margin:0 0 14px;border:1px solid #1d4ed8;background:linear-gradient(135deg,rgba(37,99,235,.13),rgba(14,165,233,.06));padding:14px;border-radius:8px}.yt-reconcile-head{display:flex;gap:12px;align-items:flex-start;justify-content:space-between}.yt-reconcile h2{margin:0 0 5px;font-size:18px}.yt-reconcile p{margin:0}.yt-reconcile-stats{display:grid;grid-template-columns:repeat(4,minmax(120px,1fr));gap:8px;margin-top:12px}.yt-reconcile-stat{padding:9px 10px;border:1px solid #2b3d55;background:#0c131c;border-radius:6px}.yt-reconcile-stat span{display:block;font-size:10px;color:#8fa3bd;text-transform:uppercase}.yt-reconcile-stat strong{display:block;margin-top:3px;font-size:18px}.yt-reconcile-actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:12px}
    .organized-extra-imports{margin-top:12px}.organized-extra-imports>summary{cursor:pointer;font-weight:700;padding:10px 12px;border:1px solid #334155;background:#111923;border-radius:7px}.organized-extra-imports[open]>summary{border-radius:7px 7px 0 0}.organized-extra-imports-body{padding:12px;border:1px solid #334155;border-top:0;border-radius:0 0 7px 7px;background:#0e141c}
    @media(max-width:1100px){.organized-workflow{grid-template-columns:1fr 1fr}.organized-grid,.yt-reconcile-stats{grid-template-columns:1fr 1fr}}
    @media(max-width:720px){.organized-workflow,.organized-grid,.yt-reconcile-stats{grid-template-columns:1fr}.yt-reconcile-head{display:block}}
  `;
  document.head.appendChild(css);

  // Keep the main navigation short and literal.  Video Studio is an editor;
  // maintenance is no longer presented as if it belonged to video editing.
  const productionNav = document.querySelector('.nav-item[data-view="production"]');
  if (productionNav) productionNav.innerHTML = '<span>⚡</span> VIDEO STUDIO';

  const ribbon = document.createElement('div');
  ribbon.id = 'organizedWorkflowRibbon';
  ribbon.className = 'organized-workflow';
  ribbon.innerHTML = `
    <button class="organized-step" data-organized-view="import"><b>1</b><span><strong>SUNO</strong><br>Poveži i sinhronizuj</span></button>
    <button class="organized-step" data-organized-view="library"><b>2</b><span><strong>BIBLIOTEKA</strong><br>Izaberi pesmu</span></button>
    <button class="organized-step" data-organized-view="audio"><b>3</b><span><strong>AUDIO</strong><br>Iseci i obradi</span></button>
    <button class="organized-step" data-organized-view="production"><b>4</b><span><strong>VIDEO STUDIO</strong><br>Timeline, titlovi, render</span></button>
    <button class="organized-step" data-organized-view="tools"><b>5</b><span><strong>YOUTUBE</strong><br>Provera i objava</span></button>`;
  production.prepend(ribbon);
  ribbon.addEventListener('click', e => {
    const button = e.target.closest('[data-organized-view]');
    if (button) showView(button.dataset.organizedView);
  });

  function panelByHeading(root, heading) {
    return qsa('.form-panel', root).find(panel => (panel.querySelector('h2')?.textContent || '').trim() === heading) || null;
  }

  // ---- Video Studio: only editing/creation/publication actions stay here. ----
  const workspace = $('productionWorkspace');
  const quick = document.createElement('section');
  quick.id = 'organizedStudioActions';
  quick.className = 'organized-section';
  quick.innerHTML = '<div class="section-title"><div><h2>Studio alati uz timeline</h2><p class="muted">Video funkcije su na jednom mestu. Održavanje programa je premešteno u Podešavanja.</p></div></div><div class="organized-grid" id="organizedStudioGrid"></div>';
  if (workspace) workspace.insertAdjacentElement('afterend', quick); else production.appendChild(quick);
  const studioGrid = $('organizedStudioGrid');

  const shortsPanel = panelByHeading(production, 'Pametni Shorts isečci');
  const publishPanel = panelByHeading(production, 'YouTube paket i direktan upload');
  if (shortsPanel) studioGrid.appendChild(shortsPanel);
  if (publishPanel) studioGrid.appendChild(publishPanel);

  // The old single-button lyric card is replaced by the real timeline editor;
  // keeping a second renderer form visible would again scatter the same job in
  // two places.  The backend remains intact for compatibility/tests.
  const oldLyric = panelByHeading(production, 'Automatski lyric video');
  if (oldLyric) oldLyric.classList.add('hidden');

  // ---- Maintenance belongs in Settings, never in the Video Studio. ----
  const maintenance = document.createElement('details');
  maintenance.className = 'organized-maintenance';
  maintenance.innerHTML = '<summary>NAPREDNO ODRŽAVANJE, ZAŠTITA I ROLLBACK</summary><div class="organized-maintenance-body"><p class="muted">Ovde su sistemske funkcije koje ne pripadaju obradi videa.</p><div class="organized-grid" id="organizedMaintenanceGrid"></div></div>';
  settings.appendChild(maintenance);
  const maintenanceGrid = $('organizedMaintenanceGrid');
  [
    'Zaključavanje programa',
    'Instalacija, rollback i potpis',
    'Integritet i duplikati',
    'Automatska organizacija foldera',
    'Zaštita pesama i rollback',
    'Panako (opcioni dodatni fingerprint motor)',
  ].forEach(name => {
    const panel = panelByHeading(production, name);
    if (panel) maintenanceGrid.appendChild(panel);
  });

  // Remove now-empty historical two-column wrappers from Video Studio.
  qsa('.two-col', production).forEach(row => {
    if (!row.querySelector('.form-panel')) row.remove();
  });

  // Keep the v3 output next to the Studio tools, not below maintenance cards.
  const v3Output = $('v3Output')?.closest('.panel');
  if (v3Output && v3Output.parentNode === production) quick.insertAdjacentElement('afterend', v3Output);

  // ---- YouTube Center: one visible reconciliation/status card. ----
  const reconcile = document.createElement('section');
  reconcile.id = 'youtubeReconcileCard';
  reconcile.className = 'yt-reconcile';
  reconcile.innerHTML = `
    <div class="yt-reconcile-head"><div><h2>Suno ↔ YouTube — stvarna audio veza</h2><p class="muted">Indeksirana Suno pesma mora ostati prepoznatljiva i kada joj stari CDN link više ne radi. Privatni video automatski pokušava prijavljene browsere.</p></div><button id="ytReconRefresh" class="btn secondary">OSVEŽI STANJE</button></div>
    <div class="yt-reconcile-stats">
      <div class="yt-reconcile-stat"><span>Suno biblioteka</span><strong id="ytReconTotal">—</strong></div>
      <div class="yt-reconcile-stat"><span>Audio indeksirano</span><strong id="ytReconIndexed">—</strong></div>
      <div class="yt-reconcile-stat"><span>Nedostaje indeks</span><strong id="ytReconMissing">—</strong></div>
      <div class="yt-reconcile-stat"><span>Moji YouTube kanali</span><strong id="ytReconChannels">—</strong></div>
    </div>
    <div id="ytReconMessage" class="inline-message" style="margin-top:10px">Učitavam stanje...</div>
    <div class="yt-reconcile-actions">
      <button id="ytReconAllVideos" class="btn success">PONOVO PROVERI SVE MOJE VIDEOE</button>
      <button id="ytReconIndex" class="btn secondary">DOPUNI SUNO AUDIO INDEKS</button>
      <button id="ytReconResults" class="btn primary">OTVORI REZULTATE</button>
    </div>`;
  const toolsTabs = tools.querySelector('.tools-tabs');
  if (toolsTabs) toolsTabs.insertAdjacentElement('afterend', reconcile); else tools.prepend(reconcile);

  async function refreshReconcile() {
    try {
      const [status, channels] = await Promise.all([
        api('/api/song-finder/status', {timeoutMs:15000,retries:0}),
        api('/api/youtube/channels', {timeoutMs:15000,retries:0}),
      ]);
      const total = Number(status.songs_total || 0);
      const indexed = Number(status.songs_indexed || 0);
      const missing = Number(status.songs_not_indexed || Math.max(0,total-indexed));
      const owned = (channels.channels || []).filter(c => Number(c.is_owned || 0) === 1).length;
      $('ytReconTotal').textContent = String(total);
      $('ytReconIndexed').textContent = String(indexed);
      $('ytReconMissing').textContent = String(missing);
      $('ytReconChannels').textContent = String(owned);
      const chroma = status.chromaprint !== false;
      $('ytReconMessage').className = `inline-message ${chroma ? (missing ? 'warning' : 'success') : 'error'}`;
      $('ytReconMessage').textContent = !chroma
        ? 'Chromaprint nije dostupan — audio prepoznavanje nije pouzdano. Pokreni „Testiraj moj računar“.'
        : missing
          ? `${indexed}/${total} Suno pesama ima audio otisak. Možeš dopuniti samo ono što nedostaje; postojeći otisci se ne računaju ponovo.`
          : `Svih ${indexed} dostupnih Suno pesama je indeksirano. YouTube analiza sada koristi i otisak čiji stari Suno audio link više nije dostupan.`;
    } catch (error) {
      $('ytReconMessage').className = 'inline-message error';
      $('ytReconMessage').textContent = `Ne mogu da pročitam stanje: ${error.message}`;
    }
  }

  $('ytReconRefresh').addEventListener('click', refreshReconcile);
  $('ytReconIndex').addEventListener('click', () => buildSunoFingerprintIndex());
  $('ytReconResults').addEventListener('click', () => setToolsTab('results'));
  $('ytReconAllVideos').addEventListener('click', async () => {
    try {
      const body = {
        max_pages:100,
        max_videos_per_channel:5000,
        candidate_limit:20,
        deep:false,
        reuse_cache:true,
        force:false,
        scan_mode:'all',
        detect_multiple:true,
        max_songs_per_video:6,
        cache_days:30,
        cache_gb:10,
      };
      await startBackground('/api/youtube/audio-analyze-owned', body);
      $('taskPanel').classList.remove('hidden');
      $('taskPanel').scrollIntoView({behavior:'smooth'});
      toast('Pokrenuta je ponovna audio provera svih sačuvanih videa sa tvojih kanala.', 'success', 10000);
    } catch (error) {
      toast(error.message, 'error', 15000);
    }
  });

  // Refresh whenever the user enters YouTube Center, without starting work.
  $('nav')?.addEventListener('click', e => {
    if (e.target.closest('[data-view="tools"]')) setTimeout(refreshReconcile, 0);
  });
  refreshReconcile();

  // ---- Suno import: primary actions first; alternative import paths collapsed. ----
  if (importView && !$('organizedExtraImports')) {
    const linkBox = qsa('.panel', importView).find(panel => (panel.querySelector('h2')?.textContent || '').includes('Uvezi Suno linkove'));
    if (linkBox) {
      const details = document.createElement('details');
      details.id = 'organizedExtraImports';
      details.className = 'organized-extra-imports';
      details.innerHTML = '<summary>DODATNI NAČINI UVOZA — LINKOVI, FOLDERI I LOKALNI FAJLOVI</summary><div class="organized-extra-imports-body"></div>';
      linkBox.parentNode.insertBefore(details, linkBox);
      details.querySelector('.organized-extra-imports-body').appendChild(linkBox);
    }
  }
})();
