/* Suno Pesme Studio — 2026 workspace shell.
 *
 * This file is loaded LAST.  Earlier extensions add functionality; this layer
 * changes the actual visual hierarchy so the product no longer looks like the
 * original legacy desktop layout with a coat of paint.  Existing DOM nodes,
 * IDs and listeners are moved, never cloned/replaced, so functionality stays
 * attached to the same controls.
 */
(() => {
  'use strict';
  if (document.getElementById('spsWorkspaceShell2026Style')) return;

  const $id = id => document.getElementById(id);
  const body = document.body;
  if (!body) return;

  body.classList.add('sps-shell-2026', 'sps-modern-2026');
  body.dataset.uiGeneration = '2026.2';

  const css = document.createElement('style');
  css.id = 'spsWorkspaceShell2026Style';
  css.textContent = `
    /* ---------- BASE TYPOGRAPHY / RHYTHM ---------- */
    body.sps-shell-2026{
      --shell-gap:16px;
      --shell-sidebar:292px;
      --shell-radius-xl:24px;
      --shell-radius-lg:18px;
      --shell-radius-md:14px;
      --shell-text:#f5f8ff;
      --shell-muted:#9aa9bc;
      --shell-surface:rgba(13,19,31,.86);
      --shell-surface-2:rgba(18,26,41,.88);
      --shell-line:rgba(148,163,184,.17);
      font-family:"Segoe UI Variable Text","Segoe UI Variable Display","Segoe UI",Inter,system-ui,-apple-system,sans-serif !important;
      font-size:15px !important;
      line-height:1.45;
      letter-spacing:-.006em;
      color:var(--m-text,var(--shell-text));
    }
    body.sps-shell-2026 :where(button,input,select,textarea,label,table,th,td,p,li,summary){font-family:inherit}
    body.sps-shell-2026 :where(p,li,td,label,input,select,textarea,button){font-size:14px}
    body.sps-shell-2026 :where(.muted,.fine-print,.help,.hint,.note,.status-text){font-size:13.5px !important;line-height:1.5}
    body.sps-shell-2026 :where(code,pre,.mini-log,.log-output,.mono,.pws-time){font-family:"Cascadia Mono","Segoe UI Mono",Consolas,monospace}

    /* ---------- TRUE APP SHELL ---------- */
    body.sps-shell-2026 .app-shell{
      display:grid !important;
      grid-template-columns:var(--shell-sidebar) minmax(0,1fr) !important;
      align-items:start;
      gap:var(--shell-gap);
      min-height:100vh;
      padding:14px !important;
      box-sizing:border-box;
    }
    body.sps-shell-2026 .sidebar{
      position:sticky !important;
      inset:auto !important;
      top:14px !important;
      grid-column:1;
      width:auto !important;
      height:calc(100vh - 28px) !important;
      max-height:calc(100vh - 28px) !important;
      padding:14px !important;
      box-sizing:border-box;
      border:1px solid var(--m-line,var(--shell-line)) !important;
      border-radius:var(--shell-radius-xl) !important;
      background:
        radial-gradient(520px 240px at 0 0,color-mix(in srgb,var(--m-accent,#7c5cff) 13%,transparent),transparent 70%),
        linear-gradient(180deg,rgba(14,20,33,.96),rgba(8,13,22,.94)) !important;
      backdrop-filter:blur(26px) saturate(135%);
      box-shadow:0 28px 80px rgba(0,0,0,.32) !important;
      overflow:auto !important;
    }
    body.sps-shell-2026 .main{
      grid-column:2 !important;
      min-width:0;
      width:100%;
      max-width:none !important;
      margin:0 !important;
      padding:0 0 118px !important;
    }

    /* ---------- BRAND / NAVIGATION ---------- */
    body.sps-shell-2026 .brand{
      display:grid !important;
      grid-template-columns:48px minmax(0,1fr);
      align-items:center;
      gap:12px !important;
      margin:0 0 14px !important;
      padding:5px 5px 16px !important;
      border-bottom:1px solid var(--m-line,var(--shell-line));
    }
    body.sps-shell-2026 .brand-mark{width:48px !important;height:48px !important;border-radius:16px !important;font-size:22px !important}
    body.sps-shell-2026 .brand strong{display:block;font-size:18px !important;line-height:1.15;font-weight:780;letter-spacing:-.035em}
    body.sps-shell-2026 .brand span{display:block;margin-top:4px !important;font-size:12.5px !important;letter-spacing:.08em !important;color:var(--m-accent-2,#59d7ff) !important;font-weight:750}
    body.sps-shell-2026 .brand .sps-generation-tag{
      grid-column:1/-1;display:flex;align-items:center;justify-content:space-between;gap:8px;
      margin-top:2px;padding:8px 10px;border:1px solid var(--m-line,var(--shell-line));border-radius:12px;
      background:rgba(255,255,255,.035);color:#bdc9d9;font-size:12.5px;font-weight:700
    }
    body.sps-shell-2026 .sps-generation-tag b{color:var(--m-accent-2,#59d7ff);font-size:12.5px;letter-spacing:.08em}
    body.sps-shell-2026 .sidebar nav{display:flex !important;flex-direction:column;gap:10px !important}
    body.sps-shell-2026 .sps-nav-section{
      display:flex;flex-direction:column;gap:4px;padding:6px;border:1px solid transparent;border-radius:16px
    }
    body.sps-shell-2026 .sps-nav-section:hover{border-color:rgba(148,163,184,.08);background:rgba(255,255,255,.018)}
    body.sps-shell-2026 .sps-nav-section-title{
      display:flex;align-items:center;justify-content:space-between;padding:3px 7px 6px;
      color:#78889e;font-size:12.5px !important;font-weight:800;letter-spacing:.08em;text-transform:uppercase
    }
    body.sps-shell-2026 .sps-nav-section-title::after{content:"";width:28px;height:1px;background:var(--m-line,var(--shell-line))}
    body.sps-shell-2026 .nav-item{
      min-height:46px !important;padding:10px 12px !important;border-radius:13px !important;
      font-size:14px !important;font-weight:680 !important;line-height:1.2 !important;letter-spacing:0 !important;
      color:#aebacf !important;border:1px solid transparent !important;background:transparent !important
    }
    body.sps-shell-2026 .nav-item span{font-size:18px !important;line-height:1}
    body.sps-shell-2026 .nav-item:hover{color:#fff !important;background:rgba(148,163,184,.075) !important;border-color:rgba(148,163,184,.10) !important;transform:translateX(2px)}
    body.sps-shell-2026 .nav-item.active{
      color:#fff !important;
      border-color:color-mix(in srgb,var(--m-accent,#7c5cff) 42%,transparent) !important;
      background:linear-gradient(100deg,color-mix(in srgb,var(--m-accent,#7c5cff) 24%,transparent),color-mix(in srgb,var(--m-accent-2,#39d4ff) 8%,transparent)) !important;
      box-shadow:inset 3px 0 0 var(--m-accent,#7c5cff),0 12px 30px rgba(0,0,0,.15) !important
    }
    body.sps-shell-2026 .nav-group-title,body.sps-shell-2026 .nav-more>summary{font-size:12.5px !important}
    body.sps-shell-2026 .sidebar-bottom{margin-top:12px;padding-top:12px !important;border-top:1px solid var(--m-line,var(--shell-line))}
    body.sps-shell-2026 .connection,body.sps-shell-2026 .version{font-size:12.5px !important;line-height:1.45}

    /* ---------- WORKSPACE CHROME ---------- */
    body.sps-shell-2026 .topbar{
      position:sticky !important;top:14px !important;z-index:40;
      min-height:86px !important;margin:0 0 16px !important;padding:14px 18px !important;
      border:1px solid var(--m-line,var(--shell-line)) !important;border-radius:var(--shell-radius-xl) !important;
      background:
        linear-gradient(100deg,color-mix(in srgb,var(--m-accent,#7c5cff) 8%,rgba(13,19,31,.94)),rgba(13,19,31,.91) 45%,color-mix(in srgb,var(--m-accent-2,#39d4ff) 5%,rgba(13,19,31,.92))) !important;
      backdrop-filter:blur(28px) saturate(145%);box-shadow:0 20px 64px rgba(0,0,0,.27) !important
    }
    body.sps-shell-2026 .topbar::before{
      content:"";position:absolute;left:18px;right:18px;bottom:-1px;height:1px;
      background:linear-gradient(90deg,var(--m-accent,#7c5cff),var(--m-accent-2,#39d4ff),transparent 75%);opacity:.7
    }
    body.sps-shell-2026 .topbar h1{font-size:28px !important;line-height:1.08 !important;font-weight:780 !important;letter-spacing:-.045em !important;margin:0 0 5px !important}
    body.sps-shell-2026 .topbar p{font-size:14px !important;line-height:1.35;color:var(--m-muted,var(--shell-muted)) !important;margin:0}
    body.sps-shell-2026 .top-actions{display:flex;flex-wrap:wrap;align-items:center;justify-content:flex-end;gap:8px !important}
    body.sps-shell-2026 .sps-workspace-badge{
      display:inline-flex;align-items:center;gap:8px;min-height:36px;padding:0 12px;
      border:1px solid color-mix(in srgb,var(--m-accent-2,#39d4ff) 30%,var(--m-line,var(--shell-line)));
      border-radius:999px;background:rgba(255,255,255,.035);color:#dbe6f5;font-size:12.5px !important;font-weight:800;letter-spacing:.055em
    }
    body.sps-shell-2026 .sps-workspace-badge i{width:8px;height:8px;border-radius:50%;background:#40dda6;box-shadow:0 0 16px rgba(64,221,166,.65)}
    body.sps-shell-2026 .modern-top-badge,body.sps-shell-2026 .modern-quick-skin{font-size:12.5px !important}
    body.sps-shell-2026 .modern-quick-skin span,body.sps-shell-2026 .modern-quick-skin select{font-size:12.5px !important}

    /* ---------- CONTENT CANVAS ---------- */
    body.sps-shell-2026 .sps-workspace-stage{display:block;min-width:0}
    body.sps-shell-2026 .view{position:relative;min-width:0}
    body.sps-shell-2026 .view.active{display:block}
    body.sps-shell-2026 .sps-view-kicker{
      display:flex;align-items:center;gap:9px;margin:0 2px 12px;color:#8797ac;font-size:12.5px;font-weight:800;letter-spacing:.09em;text-transform:uppercase
    }
    body.sps-shell-2026 .sps-view-kicker::before{content:"";width:24px;height:3px;border-radius:99px;background:linear-gradient(90deg,var(--m-accent,#7c5cff),var(--m-accent-2,#39d4ff))}
    body.sps-shell-2026 .panel{
      border:1px solid var(--m-line,var(--shell-line)) !important;border-radius:var(--shell-radius-lg) !important;
      background:linear-gradient(180deg,rgba(18,26,41,.88),rgba(11,17,29,.84)) !important;
      box-shadow:0 14px 42px rgba(0,0,0,.18) !important;overflow:clip
    }
    body.sps-shell-2026 .view.active>.panel+ .panel,body.sps-shell-2026 .view.active>section+section{margin-top:16px}
    body.sps-shell-2026 .form-panel{padding:20px !important}
    body.sps-shell-2026 .panel :where(h2,h3){letter-spacing:-.025em}
    body.sps-shell-2026 .panel h2{font-size:21px !important;line-height:1.2}
    body.sps-shell-2026 .panel h3{font-size:17px !important;line-height:1.25}
    body.sps-shell-2026 .section-title{align-items:flex-start;gap:12px}
    body.sps-shell-2026 hr{border:0;border-top:1px solid var(--m-line,var(--shell-line))}

    /* ---------- CONTROLS ---------- */
    body.sps-shell-2026 .btn{
      min-height:42px !important;padding:9px 14px !important;border-radius:12px !important;
      font-size:13.5px !important;font-weight:760 !important;line-height:1.15 !important;letter-spacing:.005em !important
    }
    body.sps-shell-2026 .btn.small{min-height:36px !important;padding:7px 11px !important;font-size:13px !important}
    body.sps-shell-2026 .btn.large{min-height:48px !important;font-size:14px !important}
    body.sps-shell-2026 :where(input:not([type=checkbox]):not([type=radio]):not([type=range]),select,textarea){
      min-height:42px !important;padding:9px 11px !important;border-radius:11px !important;
      border:1px solid var(--m-line,var(--shell-line)) !important;background:rgba(4,9,16,.56) !important;
      color:var(--m-text,var(--shell-text)) !important;font-size:14px !important;box-sizing:border-box
    }
    body.sps-shell-2026 textarea{min-height:94px !important;line-height:1.5}
    body.sps-shell-2026 :where(input,select,textarea):focus{outline:2px solid color-mix(in srgb,var(--m-accent,#7c5cff) 38%,transparent) !important;outline-offset:1px;border-color:color-mix(in srgb,var(--m-accent,#7c5cff) 55%,transparent) !important}
    body.sps-shell-2026 .button-row{gap:8px !important;align-items:center;flex-wrap:wrap}
    body.sps-shell-2026 label{color:#b9c5d5;font-size:13.5px !important;font-weight:650}

    /* ---------- LIBRARY / TABLES ---------- */
    body.sps-shell-2026 :where(.song-card,.library-card,.stat-card,.youtube-card,.release-card,.tool-card){border-radius:15px !important;border-color:var(--m-line,var(--shell-line)) !important}
    body.sps-shell-2026 table{font-size:14px !important;border-collapse:separate;border-spacing:0}
    body.sps-shell-2026 th{font-size:12.5px !important;text-transform:uppercase;letter-spacing:.055em;color:#8fa0b7}
    body.sps-shell-2026 td{font-size:14px !important;line-height:1.4}
    body.sps-shell-2026 :where(th,td){padding:11px 12px !important;border-bottom-color:var(--m-line,var(--shell-line)) !important}

    /* ---------- YOUTUBE / MATCHING ---------- */
    body.sps-shell-2026 #view-tools .panel,body.sps-shell-2026 #view-recognition .panel{border-radius:20px !important}
    body.sps-shell-2026 #view-tools [id*="youtube" i],body.sps-shell-2026 #view-recognition [id*="recognition" i]{scroll-margin-top:118px}
    body.sps-shell-2026 .youtube-audio-center,body.sps-shell-2026 #youtubeAudioBridgeCard{
      border-color:color-mix(in srgb,var(--m-accent-2,#39d4ff) 25%,var(--m-line,var(--shell-line))) !important;
      background:linear-gradient(145deg,color-mix(in srgb,var(--m-accent-2,#39d4ff) 7%,rgba(18,26,41,.92)),rgba(10,17,29,.9)) !important
    }

    /* ---------- VIDEO STUDIO: OVERRIDE LEGACY MICRO UI ---------- */

    /* ---------- TASK / PLAYER / MODALS ---------- */
    body.sps-shell-2026 #taskPanel,body.sps-shell-2026 .task-panel{border-radius:16px !important;border-color:var(--m-line,var(--shell-line)) !important}
    body.sps-shell-2026 .player,body.sps-shell-2026 .audio-player{border-radius:20px 20px 0 0 !important;backdrop-filter:blur(28px)}
    body.sps-shell-2026 .modal-content,body.sps-shell-2026 .dialog,body.sps-shell-2026 .modal-card{border-radius:22px !important;border-color:var(--m-line,var(--shell-line)) !important}

    /* ---------- RESPONSIVE ---------- */
    @media(max-width:1180px){
      body.sps-shell-2026{--shell-sidebar:250px}
      body.sps-shell-2026 .app-shell{gap:12px;padding:10px !important}
      body.sps-shell-2026 .sidebar{top:10px !important;height:calc(100vh - 20px) !important}
      body.sps-shell-2026 .topbar{top:10px !important}
      body.sps-shell-2026 .topbar h1{font-size:25px !important}
    }
    @media(max-width:860px){
      body.sps-shell-2026 .app-shell{display:block !important;padding:8px !important}
      body.sps-shell-2026 .sidebar{position:relative !important;top:auto !important;height:auto !important;max-height:none !important;margin-bottom:10px}
      body.sps-shell-2026 .sidebar nav{display:grid !important;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px !important}
      body.sps-shell-2026 .sps-nav-section{min-width:0}
      body.sps-shell-2026 .main{padding-bottom:110px !important}
      body.sps-shell-2026 .topbar{position:relative !important;top:auto !important}
    }
    @media(max-width:600px){
      body.sps-shell-2026 .sidebar nav{grid-template-columns:1fr}
      body.sps-shell-2026 .topbar{align-items:flex-start;flex-direction:column}
      body.sps-shell-2026 .top-actions{justify-content:flex-start}
    }
  `;
  document.head.appendChild(css);

  const sidebar = document.querySelector('.sidebar');
  const nav = sidebar?.querySelector('nav');
  const brand = sidebar?.querySelector('.brand');
  const main = document.querySelector('.main');
  const topbar = main?.querySelector('.topbar');

  if (brand && !brand.querySelector('.sps-generation-tag')) {
    const tag = document.createElement('div');
    tag.className = 'sps-generation-tag';
    tag.innerHTML = '<span>Nova radna površina</span><b>2026.2</b>';
    brand.appendChild(tag);
  }

  // Navigation has one owner: modern_2026_navigation_final.js, loaded after
  // this shell. An older duplicate here used a different assignment for
  // Recognition, Release and Statistics, so the sidebar moved twice during
  // startup and briefly showed the wrong information architecture. The shell
  // now supplies only layout/styling; the final navigation module performs
  // one deterministic move of the original buttons and their listeners.

  if (topbar) {
    const actions = topbar.querySelector('.top-actions') || topbar;
    if (!$id('spsWorkspaceGenerationBadge')) {
      const badge = document.createElement('span');
      badge.id = 'spsWorkspaceGenerationBadge';
      badge.className = 'sps-workspace-badge';
      badge.innerHTML = '<i></i><span>2026 WORKSPACE · MATCH RECOVERY</span>';
      actions.prepend(badge);
    }
  }

  if (main && !$id('spsWorkspaceStage')) {
    const stage = document.createElement('div');
    stage.id = 'spsWorkspaceStage';
    stage.className = 'sps-workspace-stage';
    const views = [...main.querySelectorAll(':scope > .view')];
    if (views.length) {
      const first = views[0];
      first.parentNode.insertBefore(stage, first);
      views.forEach(view => stage.appendChild(view)); // MOVE, do not clone.
    }
  }

  const viewLabels = {
    library:'Biblioteka · pesme i status',
    import:'Suno · povezivanje i sinhronizacija',
    recognition:'Prepoznavanje · moje pesme',
    tools:'YouTube · kanali i audio provera',
    settings:'Sistem · podešavanja i održavanje',
    folders:'Biblioteka · kolekcije',
    download:'Biblioteka · preuzimanje',
    audio:'Audio Studio · obrada',
    smart:'Biblioteka · pametni filteri',
    versions:'Biblioteka · verzije pesme',
    release:'Objava · priprema paketa',
    stats:'Biblioteka · statistika',
    logs:'Sistem · dnevnik rada',
  };
  document.querySelectorAll('.view[id^="view-"]').forEach(view => {
    const key = view.id.slice(5);
    view.dataset.studioModule = key;
    if (!view.querySelector(':scope > .sps-view-kicker')) {
      const kicker = document.createElement('div');
      kicker.className = 'sps-view-kicker';
      kicker.textContent = viewLabels[key] || `Studio · ${key}`;
      view.prepend(kicker);
    }
  });

  // Make the new generation unmistakable even when a legacy skin setting was
  // saved months ago. The compatibility setting remains stored, but it cannot
  // remove this shell or shrink its typography.
  try { localStorage.setItem('sps-ui-generation', '2026.2'); } catch (_) {}
})();
