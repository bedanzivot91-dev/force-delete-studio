/* Five real application layouts. This runs last so themes alter structure, not just colour. */
(() => {
  'use strict';
  if (document.getElementById('spsRealThemeLayouts')) return;
  const style = document.createElement('style');
  style.id = 'spsRealThemeLayouts';
  style.textContent = `
    body[data-sps-skin="aurora-flow"]{--m-radius:22px;--m-radius-sm:14px}

    body[data-sps-skin="graphite-console"]{--m-radius:8px;--m-radius-sm:5px;font-family:"IBM Plex Sans","Segoe UI",sans-serif;letter-spacing:0}
    body[data-sps-skin="graphite-console"] .app-shell{grid-template-columns:218px minmax(0,1fr)!important}
    body[data-sps-skin="graphite-console"] .sidebar{width:198px!important;inset:8px auto 8px 8px!important;border-radius:8px!important;padding:10px 8px!important}
    body[data-sps-skin="graphite-console"] .main{grid-column:2!important;padding:10px 18px 105px 8px!important}
    body[data-sps-skin="graphite-console"] .topbar{top:8px!important;min-height:60px!important;border-radius:7px!important;padding:9px 12px!important}
    body[data-sps-skin="graphite-console"] .nav-item{min-height:34px!important;padding:7px 9px!important;border-radius:4px!important;font-size:11px!important}
    body[data-sps-skin="graphite-console"] .panel,body[data-sps-skin="graphite-console"] .song-card,body[data-sps-skin="graphite-console"] .btn{border-radius:6px!important;box-shadow:none!important}
    body[data-sps-skin="graphite-console"] .songs-grid{grid-template-columns:repeat(auto-fill,minmax(205px,1fr))!important;gap:8px!important}

    body[data-sps-skin="vinyl-loft"]{--m-bg:#160f0c;--m-bg-2:#241914;--m-panel:rgba(54,38,29,.91);--m-panel-solid:#30221b;--m-panel-2:#493329;--m-line:rgba(244,217,177,.18);--m-line-strong:rgba(244,217,177,.34);--m-text:#fff4df;--m-muted:#c8ad8f;--m-accent:#e89345;--m-accent-2:#f5cf78;--m-accent-soft:rgba(232,147,69,.16);--m-radius:28px}
    body[data-sps-skin="vinyl-loft"] .app-shell{grid-template-columns:330px minmax(0,1fr)!important}
    body[data-sps-skin="vinyl-loft"] .sidebar{width:300px!important;inset:18px auto 18px 18px!important;border-radius:34px!important;background:linear-gradient(165deg,#3a281f,#1b1411)!important}
    body[data-sps-skin="vinyl-loft"] .main{grid-column:2!important;padding:24px 34px 130px 18px!important}
    body[data-sps-skin="vinyl-loft"] .topbar h1,body[data-sps-skin="vinyl-loft"] .panel h2{font-family:Georgia,"Times New Roman",serif!important;font-weight:600!important}
    body[data-sps-skin="vinyl-loft"] .songs-grid{grid-template-columns:repeat(auto-fill,minmax(310px,1fr))!important;gap:22px!important}
    body[data-sps-skin="vinyl-loft"] .song-card{border-radius:26px!important;box-shadow:0 20px 60px rgba(0,0,0,.3)!important}
    body[data-sps-skin="vinyl-loft"] .cover-wrap{aspect-ratio:1/1!important}

    body[data-sps-skin="signal-grid"]{--m-bg:#02090d;--m-bg-2:#06141a;--m-panel:rgba(4,24,31,.91);--m-panel-solid:#071b23;--m-panel-2:#0b2731;--m-accent:#17dfc0;--m-accent-2:#32a8ff;--m-radius:4px;font-family:"Cascadia Code","Segoe UI Mono",monospace}
    body[data-sps-skin="signal-grid"] .app-shell{display:block!important;min-height:100vh!important}
    body[data-sps-skin="signal-grid"] .sidebar{position:sticky!important;inset:0!important;top:0!important;width:auto!important;height:auto!important;max-height:none!important;border-radius:0!important;padding:7px 16px!important;display:flex!important;align-items:center!important;gap:16px!important;z-index:40!important}
    body[data-sps-skin="signal-grid"] .brand{min-width:185px!important;margin:0!important;padding:0 14px 0 0!important;border:0!important;border-right:1px solid var(--m-line)!important}
    body[data-sps-skin="signal-grid"] .sidebar nav{display:flex!important;overflow-x:auto!important;gap:5px!important;align-items:center!important}
    body[data-sps-skin="signal-grid"] .sps-nav-section{display:flex!important;gap:4px!important;align-items:center!important;border:0!important;padding:0!important}
    body[data-sps-skin="signal-grid"] .sps-nav-section-title,body[data-sps-skin="signal-grid"] .sidebar-bottom{display:none!important}
    body[data-sps-skin="signal-grid"] .nav-item{min-height:36px!important;width:auto!important;white-space:nowrap!important;border-radius:3px!important;padding:7px 10px!important;font-size:10px!important}
    body[data-sps-skin="signal-grid"] .main{grid-column:auto!important;padding:14px 22px 105px!important}
    body[data-sps-skin="signal-grid"] .topbar{top:62px!important;border-radius:3px!important}
    body[data-sps-skin="signal-grid"] .panel,body[data-sps-skin="signal-grid"] .song-card,body[data-sps-skin="signal-grid"] .btn{border-radius:3px!important}

    body[data-sps-skin="paper-studio"]{--m-bg:#eef1f5;--m-bg-2:#f8fafc;--m-panel:rgba(255,255,255,.96);--m-panel-solid:#fff;--m-panel-2:#f5f7fa;--m-line:rgba(35,47,62,.13);--m-line-strong:rgba(35,47,62,.24);--m-text:#17202b;--m-muted:#637083;--m-accent:#3f63d8;--m-accent-2:#0e9e8f;--m-accent-soft:rgba(63,99,216,.1);--m-radius:16px;background:#eef1f5!important;color:var(--m-text)!important}
    body[data-sps-skin="paper-studio"] .app-shell{grid-template-columns:250px minmax(0,1fr)!important}
    body[data-sps-skin="paper-studio"] .sidebar{width:222px!important;background:#fff!important;box-shadow:0 8px 30px rgba(30,45,65,.09)!important}
    body[data-sps-skin="paper-studio"] .main{grid-column:2!important;max-width:1480px!important;margin:0 auto!important;padding:18px 28px 110px 8px!important}
    body[data-sps-skin="paper-studio"] .topbar,body[data-sps-skin="paper-studio"] .panel,body[data-sps-skin="paper-studio"] .song-card{background:#fff!important;color:#17202b!important;box-shadow:0 8px 28px rgba(30,45,65,.08)!important}
    body[data-sps-skin="paper-studio"] .nav-item{color:#536174!important}
    body[data-sps-skin="paper-studio"] .nav-item.active{color:#173373!important;background:#e9efff!important}
    body[data-sps-skin="paper-studio"] input:not([type="checkbox"]):not([type="radio"]),body[data-sps-skin="paper-studio"] select,body[data-sps-skin="paper-studio"] textarea{background:#f7f9fc!important;color:#17202b!important}
    body[data-sps-skin="paper-studio"] .songs-grid{grid-template-columns:repeat(auto-fill,minmax(280px,1fr))!important}

    body[data-sps-skin="neon-stage"]{--m-bg:#090414;--m-bg-2:#160625;--m-panel:rgba(27,10,45,.9);--m-panel-solid:#1d0b30;--m-panel-2:#28103d;--m-line:rgba(255,84,214,.22);--m-line-strong:rgba(67,225,255,.42);--m-text:#fff7ff;--m-muted:#c6a9d4;--m-accent:#ff43c6;--m-accent-2:#43e1ff;--m-accent-soft:rgba(255,67,198,.16);--m-radius:12px}
    body[data-sps-skin="neon-stage"] .app-shell{grid-template-columns:236px minmax(0,1fr)!important}
    body[data-sps-skin="neon-stage"] .sidebar{width:208px!important;inset:14px auto 14px 14px!important;border-radius:12px!important;background:linear-gradient(180deg,#200b35,#0b0713)!important;box-shadow:0 0 35px rgba(255,67,198,.16)!important}
    body[data-sps-skin="neon-stage"] .main{grid-column:2!important;padding:16px 24px 118px 8px!important}
    body[data-sps-skin="neon-stage"] .topbar{border-radius:12px!important;border-top:2px solid var(--m-accent)!important}
    body[data-sps-skin="neon-stage"] .nav-item{border-radius:5px!important;border-left:2px solid transparent!important}
    body[data-sps-skin="neon-stage"] .nav-item.active{border-left-color:var(--m-accent-2)!important;box-shadow:0 0 18px rgba(67,225,255,.14)!important}
    body[data-sps-skin="neon-stage"] .songs-grid{grid-template-columns:repeat(auto-fill,minmax(240px,1fr))!important;gap:14px!important}
    body[data-sps-skin="neon-stage"] .song-card{clip-path:polygon(0 0,calc(100% - 13px) 0,100% 13px,100% 100%,0 100%)!important;border-radius:0!important}

    body[data-sps-skin="album-wall"]{--m-bg:#11100f;--m-bg-2:#1b1815;--m-panel:rgba(38,33,28,.92);--m-panel-solid:#2a241f;--m-panel-2:#352d26;--m-line:rgba(241,220,190,.18);--m-line-strong:rgba(241,220,190,.34);--m-text:#fff7eb;--m-muted:#bda990;--m-accent:#f0bb62;--m-accent-2:#d96f45;--m-accent-soft:rgba(240,187,98,.15);--m-radius:20px}
    body[data-sps-skin="album-wall"] .app-shell{grid-template-columns:370px minmax(0,1fr)!important}
    body[data-sps-skin="album-wall"] .sidebar{width:338px!important;inset:16px auto 16px 16px!important;border-radius:20px!important;background:linear-gradient(145deg,#332a22,#161311)!important}
    body[data-sps-skin="album-wall"] .main{grid-column:2!important;padding:20px 28px 126px 12px!important}
    body[data-sps-skin="album-wall"] .songs-grid{grid-template-columns:repeat(auto-fill,minmax(360px,1fr))!important;gap:26px!important}
    body[data-sps-skin="album-wall"] .song-card{border-radius:8px!important;padding:10px!important;background:#2a241f!important;box-shadow:10px 14px 0 #0c0b0a!important}
    body[data-sps-skin="album-wall"] .cover-wrap{aspect-ratio:1/1!important;border-radius:4px!important}

    body[data-sps-skin="mixer-desk"]{--m-bg:#080b0d;--m-bg-2:#10161a;--m-panel:rgba(18,25,29,.94);--m-panel-solid:#151d21;--m-panel-2:#1d282d;--m-line:rgba(132,207,185,.17);--m-line-strong:rgba(132,207,185,.34);--m-text:#edf9f5;--m-muted:#8ea9a1;--m-accent:#73d8b8;--m-accent-2:#f0a45d;--m-accent-soft:rgba(115,216,184,.14);--m-radius:7px}
    body[data-sps-skin="mixer-desk"] .app-shell{grid-template-columns:minmax(0,1fr) 286px!important}
    body[data-sps-skin="mixer-desk"] .sidebar{left:auto!important;right:12px!important;width:258px!important;inset:12px 12px 12px auto!important;border-radius:7px!important;background:linear-gradient(180deg,#1a2429,#0c1114)!important}
    body[data-sps-skin="mixer-desk"] .main{grid-column:1!important;padding:14px 8px 118px 22px!important}
    body[data-sps-skin="mixer-desk"] .topbar{border-radius:7px!important;border-bottom:3px solid var(--m-accent)!important}
    body[data-sps-skin="mixer-desk"] .nav-item,body[data-sps-skin="mixer-desk"] .btn,body[data-sps-skin="mixer-desk"] .panel{border-radius:5px!important;box-shadow:none!important}
    body[data-sps-skin="mixer-desk"] .songs-grid{grid-template-columns:repeat(auto-fill,minmax(220px,1fr))!important;gap:8px!important}

    @media(max-width:900px){body[data-sps-skin] .app-shell{display:block!important}body[data-sps-skin] .sidebar{position:relative!important;inset:auto!important;left:auto!important;right:auto!important;width:auto!important;height:auto!important;margin:8px!important}body[data-sps-skin] .main{grid-column:auto!important;padding:8px 12px 100px!important}body[data-sps-skin="signal-grid"] .topbar{top:8px!important}}
  `;
  document.head.appendChild(style);
})();
