/* Suno Pesme Studio — one clear workspace, one navigation owner.
 * This intentionally runs last and replaces every historical menu grouping.
 * Existing views and controls are moved, never cloned, so their real listeners remain intact.
 */
(() => {
  'use strict';
  if (document.getElementById('sunoWorkspaceRedesignMarker')) return;

  const marker = document.createElement('meta');
  marker.id = 'sunoWorkspaceRedesignMarker';
  marker.name = 'suno-workspace';
  marker.content = 'single-owner-v1';
  document.head.appendChild(marker);

  const style = document.createElement('style');
  style.id = 'sunoWorkspaceRedesignStyle';
  style.textContent = `
    body.suno-workspace-redesign .sidebar nav{display:flex!important;flex-direction:column!important;gap:7px!important;overflow-y:auto!important}
    body.suno-workspace-redesign .sidebar nav>.nav-item{display:flex!important;width:100%!important;min-height:48px!important;font-size:14px!important}
    body.suno-workspace-redesign .sidebar nav>.nav-item span{font-size:18px!important}
    body.suno-workspace-redesign .sps-nav-section,body.suno-workspace-redesign .nav-group-title,body.suno-workspace-redesign .nav-more{display:none!important}
    body.suno-workspace-redesign .suno-tools-drawer{margin-top:10px;border-top:1px solid var(--m-line,var(--line));padding-top:10px}
    body.suno-workspace-redesign .suno-tools-drawer summary{cursor:pointer;list-style:none;color:var(--m-muted,var(--muted));font-size:12px;font-weight:800;letter-spacing:.08em;padding:10px 12px;border-radius:10px}
    body.suno-workspace-redesign .suno-tools-drawer summary:hover{background:var(--m-panel-2,#151a23);color:var(--m-text,#fff)}
    body.suno-workspace-redesign .suno-tools-grid{display:grid;gap:6px;padding-top:6px}
    body.suno-workspace-redesign .suno-tool-link{border:0;background:transparent;color:var(--m-muted,var(--muted));text-align:left;padding:9px 12px;border-radius:9px;cursor:pointer;font-weight:650}
    body.suno-workspace-redesign .suno-tool-link:hover{background:var(--m-panel-2,#151a23);color:var(--m-text,#fff)}
    body.suno-workspace-redesign .suno-home-hero{padding:28px;display:grid;grid-template-columns:minmax(0,1.4fr) minmax(260px,.6fr);gap:22px;align-items:center;overflow:hidden;position:relative}
    body.suno-workspace-redesign .suno-home-hero:after{content:'♫';position:absolute;right:28px;top:-34px;font-size:180px;opacity:.045;pointer-events:none}
    body.suno-workspace-redesign .suno-home-hero h2{font-size:clamp(26px,3vw,42px);margin:0 0 9px;letter-spacing:-.035em}
    body.suno-workspace-redesign .suno-home-actions{display:flex;flex-wrap:wrap;gap:10px;margin-top:20px}
    body.suno-workspace-redesign .suno-flow{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px;margin:14px 0}
    body.suno-workspace-redesign .suno-flow-card{min-height:126px;padding:18px;text-align:left;cursor:pointer;color:var(--m-text,var(--text));background:var(--m-panel,var(--card));border:1px solid var(--m-line,var(--line));border-radius:var(--m-radius-sm,14px);transition:.18s ease}
    body.suno-workspace-redesign .suno-flow-card:hover{transform:translateY(-2px);border-color:var(--m-accent,var(--accent))}
    body.suno-workspace-redesign .suno-flow-card b{display:grid;place-items:center;width:30px;height:30px;border-radius:9px;background:var(--m-accent-soft,rgba(59,130,246,.15));color:var(--m-accent,#38bdf8);margin-bottom:14px}
    body.suno-workspace-redesign .suno-flow-card strong,body.suno-workspace-redesign .suno-flow-card span{display:block}
    body.suno-workspace-redesign .suno-flow-card span{font-size:12px;color:var(--m-muted,var(--muted));margin-top:5px;line-height:1.45}
    body.suno-workspace-redesign .suno-home-secondary{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}
    body.suno-workspace-redesign .suno-home-secondary .panel{margin:0!important;min-height:112px;cursor:pointer}
    body.suno-workspace-redesign .suno-context-bar{display:flex;gap:8px;overflow-x:auto;margin:0 0 14px;padding:8px;position:sticky;top:104px;z-index:12}
    body.suno-workspace-redesign .suno-context-bar button{white-space:nowrap}
    body.suno-workspace-redesign .modern-top-badge{display:none!important}
    /* Signal Grid is the only horizontal workspace.  The redesign runs after
       the theme layouts, so it must explicitly preserve the horizontal rail;
       otherwise the generic column rule turns the rail into a huge overlay. */
    body.suno-workspace-redesign[data-sps-skin="signal-grid"] .sidebar{height:62px!important;min-height:62px!important;overflow:visible!important}
    body.suno-workspace-redesign[data-sps-skin="signal-grid"] .sidebar nav{display:flex!important;flex:1 1 auto!important;flex-direction:row!important;align-items:center!important;gap:4px!important;overflow-x:auto!important;overflow-y:hidden!important;min-width:0!important}
    body.suno-workspace-redesign[data-sps-skin="signal-grid"] .sidebar nav>.nav-item{display:inline-flex!important;width:auto!important;min-width:max-content!important;min-height:36px!important;padding:7px 10px!important;font-size:10px!important}
    body.suno-workspace-redesign[data-sps-skin="signal-grid"] .suno-tools-drawer{position:relative!important;flex:0 0 auto!important;margin:0!important;padding:0!important;border:0!important}
    body.suno-workspace-redesign[data-sps-skin="signal-grid"] .suno-tools-drawer summary{min-height:36px!important;padding:10px!important;border:1px solid var(--m-line)!important;border-radius:3px!important;font-size:10px!important;white-space:nowrap!important}
    body.suno-workspace-redesign[data-sps-skin="signal-grid"] .suno-tools-drawer[open] .suno-tools-grid{position:absolute!important;right:0!important;top:44px!important;z-index:100!important;width:230px!important;padding:8px!important;border:1px solid var(--m-line)!important;background:var(--m-panel-solid)!important;box-shadow:0 18px 48px rgba(0,0,0,.45)!important}
    body.suno-workspace-redesign[data-sps-skin="signal-grid"] .main{padding-top:14px!important}
    body.suno-workspace-redesign[data-sps-skin="signal-grid"] .topbar{top:8px!important}
    @media(max-width:1050px){body.suno-workspace-redesign .suno-flow{grid-template-columns:repeat(2,1fr)}body.suno-workspace-redesign .suno-home-hero{grid-template-columns:1fr}}
    @media(max-width:760px){body.suno-workspace-redesign .sidebar nav{flex-direction:row!important}body.suno-workspace-redesign .sidebar nav>.nav-item{min-width:58px!important;width:auto!important;font-size:0!important;justify-content:center!important}body.suno-workspace-redesign .suno-tools-drawer{display:none}body.suno-workspace-redesign .suno-flow,body.suno-workspace-redesign .suno-home-secondary{grid-template-columns:1fr}body.suno-workspace-redesign .suno-context-bar{top:8px}}
  `;
  document.head.appendChild(style);
  document.body.classList.add('suno-workspace-redesign');

  const nav = document.querySelector('.sidebar nav');
  const allButtons = [...document.querySelectorAll('.sidebar .nav-item[data-view]')];
  const byView = new Map(allButtons.map(button => [button.dataset.view, button]));
  allButtons.forEach(button => button.remove());
  nav?.replaceChildren();

  const mainItems = [
    ['home', '⌂', 'Početna'],
    ['library', '♫', 'Moje Suno pesme'],
    ['import', '↻', 'Poveži i sinhronizuj'],
    ['recognition', '⌕', 'Proveri klip'],
    ['tools', '▶', 'Moji YouTube kanali'],
    ['settings', '⚙', 'Podešavanja'],
  ];
  for (const [view, icon, label] of mainItems) {
    let button = byView.get(view);
    if (!button) {
      button = document.createElement('button');
      button.className = 'nav-item';
      button.dataset.view = view;
    }
    button.innerHTML = `<span>${icon}</span>${label}`;
    nav?.appendChild(button);
  }

  const utilityViews = [
    ['download','Preuzimanje pesama'],['folders','Kolekcije'],['audio','Audio obrada'],
    ['smart','Pametni filteri'],['versions','Verzije pesama'],['release','Priprema objave'],
    ['stats','Statistika biblioteke'],['logs','Dnevnik rada']
  ];
  const drawer = document.createElement('details');
  drawer.className = 'suno-tools-drawer';
  drawer.innerHTML = '<summary>DODATNI ALATI</summary><div class="suno-tools-grid"></div>';
  const grid = drawer.querySelector('.suno-tools-grid');
  utilityViews.forEach(([view,label]) => {
    const button = document.createElement('button');
    button.className = 'suno-tool-link';
    button.dataset.openView = view;
    button.textContent = label;
    grid.appendChild(button);
  });
  nav?.appendChild(drawer);

  const main = document.querySelector('main.main');
  const firstView = main?.querySelector('.view');
  const home = document.createElement('section');
  home.id = 'view-home';
  home.className = 'view';
  home.innerHTML = `
    <div class="panel suno-home-hero">
      <div><span class="eyebrow">SUNO PESME STUDIO</span><h2>Sve vaše Suno pesme i YouTube provere na jednom mestu</h2><p class="muted">Jednostavan tok rada bez Video Studija, timeline-a i NP funkcija.</p><div class="suno-home-actions"><button class="btn primary" data-open-view="import">Poveži Suno</button><button class="btn success" data-open-view="tools">Proveri moje kanale</button></div></div>
      <div class="inline-message" id="sunoHomeState"><strong>Program je spreman.</strong><br>Prvo povežite Suno nalog ili otvorite postojeću biblioteku.</div>
    </div>
    <div class="suno-flow" aria-label="Glavni tok rada">
      <button class="suno-flow-card" data-open-view="import"><b>1</b><strong>Povežite Suno</strong><span>Bezbedna prijava i sinhronizacija naloga.</span></button>
      <button class="suno-flow-card" data-open-view="library"><b>2</b><strong>Pregledajte pesme</strong><span>Pretraga, reprodukcija, tekstovi i fajlovi.</span></button>
      <button class="suno-flow-card" data-open-view="tools"><b>3</b><strong>Proverite kanale</strong><span>Program traži vaše pesme u vašim video-klipovima.</span></button>
      <button class="suno-flow-card" data-open-view="recognition"><b>4</b><strong>Proverite jedan klip</strong><span>Ubacite MP4 ili audio i odmah pronađite pesmu.</span></button>
    </div>
    <div class="suno-home-secondary">
      <button class="panel suno-flow-card" data-open-view="download"><strong>Preuzimanje</strong><span>Sačuvajte MP3, omot i tekst.</span></button>
      <button class="panel suno-flow-card" data-open-view="folders"><strong>Kolekcije</strong><span>Organizujte pesme bez dupliranja.</span></button>
      <button class="panel suno-flow-card" data-open-view="stats"><strong>Statistika</strong><span>Pregled sadržaja Suno biblioteke.</span></button>
    </div>`;
  // Routed views live inside a nested content host in the production HTML.
  // insertBefore() requires the reference node to be a DIRECT child; using
  // main here raised NotFoundError and stopped the whole workspace halfway.
  const viewHost = firstView?.parentElement || main;
  viewHost?.insertBefore(home, firstView || null);

  const library = document.getElementById('view-library');
  if (library) {
    const context = document.createElement('div');
    context.className = 'panel suno-context-bar';
    context.innerHTML = '<button class="btn ghost small" data-open-view="download">Preuzmi pesme</button><button class="btn ghost small" data-open-view="folders">Kolekcije</button><button class="btn ghost small" data-open-view="smart">Pametni filteri</button><button class="btn ghost small" data-open-view="versions">Verzije</button><button class="btn ghost small" data-open-view="audio">Audio obrada</button>';
    library.prepend(context);
  }

  const pageCopy = {
    home:['Početna','Jasan tok rada: Suno → pesme → YouTube provera.'],
    library:['Moje Suno pesme','Pretraga, reprodukcija, tekstovi i fajlovi.'],
    import:['Poveži i sinhronizuj','Povezivanje Suno naloga i uvoz pesama.'],
    recognition:['Provera jednog klipa','Pronađite svoju Suno pesmu u MP4 ili audio-fajlu.'],
    tools:['Moji YouTube kanali','Pronađite svoje Suno pesme u video-klipovima na kanalima.'],
    settings:['Podešavanja','Izgled, ažuriranja i tehničke opcije programa.']
  };
  const openView = view => {
    if (view === 'home') {
      document.querySelectorAll('.view').forEach(section => section.classList.toggle('active', section.id === 'view-home'));
      if (typeof state === 'object') state.activeView = 'home';
    } else if (typeof showView === 'function') showView(view);
    const copy = pageCopy[view];
    if (copy) {
      const title = document.getElementById('pageTitle');
      const subtitle = document.getElementById('pageSubtitle');
      if (title) title.textContent = copy[0];
      if (subtitle) subtitle.textContent = copy[1];
    }
    document.querySelectorAll('.sidebar nav>.nav-item').forEach(button => button.classList.toggle('active', button.dataset.view === view));
    window.scrollTo({top:0,behavior:'smooth'});
  };
  document.addEventListener('click', event => {
    const trigger = event.target.closest('[data-open-view]');
    if (trigger) { event.preventDefault(); openView(trigger.dataset.openView); }
    const navButton = event.target.closest('.sidebar nav>.nav-item[data-view]');
    if (navButton) setTimeout(() => openView(navButton.dataset.view), 0);
  });

  const legacyVideoWords = /video studio|timeline|renderuj video|np studio/i;
  document.querySelectorAll('button,h1,h2,h3,p,span,strong').forEach(node => {
    if (legacyVideoWords.test(node.textContent || '') && !node.closest('[id^="modal"]')) node.hidden = true;
  });
  openView('home');
})();
