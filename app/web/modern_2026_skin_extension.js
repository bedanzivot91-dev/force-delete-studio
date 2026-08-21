/* Suno Pesme Studio — modern 2026 application skin.
 *
 * This is deliberately a presentation layer only: it does not replace IDs,
 * buttons, forms, listeners or backend calls. Every existing control remains
 * the same DOM node; the extension gives the whole application one consistent
 * contemporary desktop-studio visual system.
 */
(() => {
  'use strict';

  if (document.getElementById('spsModern2026Style')) return;

  const SKINS = new Set(['aurora', 'graphite', 'midnight']);

  function readSkin() {
    try {
      const saved = localStorage.getItem('sps-modern-2026-skin') || 'aurora';
      return SKINS.has(saved) ? saved : 'aurora';
    } catch (_) {
      return 'aurora';
    }
  }

  function applySkin(name) {
    const skin = SKINS.has(String(name)) ? String(name) : 'aurora';
    document.body.classList.add('sps-modern-2026');
    document.body.dataset.spsSkin = skin;
    try { localStorage.setItem('sps-modern-2026-skin', skin); } catch (_) {}
    document.querySelectorAll('[data-modern-skin-choice]').forEach(button => {
      const active = button.dataset.modernSkinChoice === skin;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', active ? 'true' : 'false');
    });
    const select = document.getElementById('modernSkinQuickSelect');
    if (select && select.value !== skin) select.value = skin;
  }

  const css = document.createElement('style');
  css.id = 'spsModern2026Style';
  css.textContent = `
    body.sps-modern-2026{
      --m-bg:#060912;
      --m-bg-2:#0a0f1b;
      --m-panel:rgba(15,22,36,.78);
      --m-panel-solid:#101827;
      --m-panel-2:rgba(20,29,46,.78);
      --m-line:rgba(148,163,184,.16);
      --m-line-strong:rgba(148,163,184,.28);
      --m-text:#f6f9ff;
      --m-muted:#8fa1b8;
      --m-accent:#7c5cff;
      --m-accent-2:#00c2ff;
      --m-accent-soft:rgba(124,92,255,.16);
      --m-good:#35d39a;
      --m-warn:#f7b84b;
      --m-bad:#ff667d;
      --m-shadow:0 24px 80px rgba(0,0,0,.38);
      --m-shadow-soft:0 12px 42px rgba(0,0,0,.22);
      --m-radius:20px;
      --m-radius-sm:14px;
      --m-blur:22px;
      margin:0;
      color:var(--m-text);
      background:
        radial-gradient(900px 520px at 18% -8%,rgba(124,92,255,.18),transparent 62%),
        radial-gradient(760px 520px at 92% 7%,rgba(0,194,255,.11),transparent 62%),
        linear-gradient(145deg,var(--m-bg),var(--m-bg-2) 52%,#080d17);
      background-attachment:fixed;
      font-family:"Segoe UI Variable Text","Segoe UI Variable","Segoe UI",Inter,Arial,sans-serif;
      font-size:14px;
      letter-spacing:-.006em;
      min-height:100vh;
      overflow-x:hidden;
    }
    body.sps-modern-2026[data-sps-skin="graphite"]{
      --m-bg:#080a0d;--m-bg-2:#101318;--m-panel:rgba(20,23,29,.82);--m-panel-solid:#15191f;--m-panel-2:rgba(29,33,41,.82);
      --m-line:rgba(203,213,225,.13);--m-line-strong:rgba(203,213,225,.24);--m-accent:#d7dde8;--m-accent-2:#7f91aa;--m-accent-soft:rgba(215,221,232,.10);
    }
    body.sps-modern-2026[data-sps-skin="midnight"]{
      --m-bg:#02070b;--m-bg-2:#06121a;--m-panel:rgba(6,20,29,.82);--m-panel-solid:#081824;--m-panel-2:rgba(9,28,40,.82);
      --m-line:rgba(103,232,249,.14);--m-line-strong:rgba(103,232,249,.25);--m-accent:#29d3ff;--m-accent-2:#2f7dff;--m-accent-soft:rgba(41,211,255,.12);
    }
    body.sps-modern-2026::before{
      content:"";position:fixed;inset:0;pointer-events:none;z-index:-1;opacity:.35;
      background-image:linear-gradient(rgba(255,255,255,.018) 1px,transparent 1px),linear-gradient(90deg,rgba(255,255,255,.014) 1px,transparent 1px);
      background-size:48px 48px;mask-image:linear-gradient(to bottom,rgba(0,0,0,.7),transparent 72%);
    }
    body.sps-modern-2026 *{scrollbar-width:thin;scrollbar-color:rgba(148,163,184,.30) transparent}
    body.sps-modern-2026 *::-webkit-scrollbar{width:9px;height:9px}
    body.sps-modern-2026 *::-webkit-scrollbar-thumb{background:rgba(148,163,184,.25);border:2px solid transparent;background-clip:padding-box;border-radius:999px}
    body.sps-modern-2026 *::-webkit-scrollbar-track{background:transparent}

    body.sps-modern-2026 .app-shell{grid-template-columns:282px minmax(0,1fr);min-height:100vh}
    body.sps-modern-2026 .sidebar{
      inset:12px auto 12px 12px;width:258px;padding:17px 12px 14px;border:1px solid var(--m-line);border-radius:24px;
      background:linear-gradient(180deg,rgba(13,19,31,.90),rgba(8,13,22,.88));
      box-shadow:0 26px 70px rgba(0,0,0,.34);backdrop-filter:blur(28px) saturate(135%);overflow:auto;
    }
    body.sps-modern-2026[data-sps-skin] .sidebar{background:linear-gradient(180deg,color-mix(in srgb,var(--m-panel-solid) 88%,transparent),rgba(7,11,18,.88))}
    body.sps-modern-2026 .brand{padding:4px 8px 18px;margin-bottom:13px;border-bottom:1px solid var(--m-line);gap:11px}
    body.sps-modern-2026 .brand-mark{
      width:43px;height:43px;border-radius:15px;background:linear-gradient(145deg,var(--m-accent),var(--m-accent-2));
      box-shadow:0 11px 35px color-mix(in srgb,var(--m-accent) 30%,transparent);font-size:21px
    }
    body.sps-modern-2026 .brand strong{font-size:16px;letter-spacing:-.02em}
    body.sps-modern-2026 .brand span{margin-top:3px;color:var(--m-accent-2);font-size:9px;letter-spacing:.19em;font-weight:800}
    body.sps-modern-2026 .nav-group-title{padding:6px 12px 5px;color:#64748b;font-size:9px;font-weight:850;letter-spacing:.16em}
    body.sps-modern-2026 nav{gap:4px}
    body.sps-modern-2026 .nav-item{
      min-height:43px;padding:10px 12px;border:1px solid transparent;border-radius:13px;color:#9eacc0;font-size:12px;font-weight:720;letter-spacing:.01em;
      transition:background .18s ease,border-color .18s ease,color .18s ease,transform .18s ease;
    }
    body.sps-modern-2026 .nav-item:hover{background:rgba(148,163,184,.075);border-color:rgba(148,163,184,.08);color:#eef5ff;transform:translateX(2px)}
    body.sps-modern-2026 .nav-item.active{
      color:#fff;border-color:color-mix(in srgb,var(--m-accent) 36%,transparent);
      background:linear-gradient(100deg,color-mix(in srgb,var(--m-accent) 21%,transparent),color-mix(in srgb,var(--m-accent-2) 8%,transparent));
      box-shadow:inset 3px 0 0 var(--m-accent),0 10px 30px rgba(0,0,0,.13)
    }
    body.sps-modern-2026 .nav-item span{font-size:16px;opacity:.95}
    body.sps-modern-2026 .nav-more{margin-top:8px;padding-top:8px;border-top:1px solid var(--m-line)}
    body.sps-modern-2026 .nav-more>summary{list-style:none;cursor:pointer;padding:9px 12px;color:#718198;font-size:9px;font-weight:850;letter-spacing:.14em;border-radius:11px}
    body.sps-modern-2026 .nav-more>summary:hover{background:rgba(148,163,184,.06);color:#aebbd0}
    body.sps-modern-2026 .sidebar-bottom{padding-top:12px}
    body.sps-modern-2026 .connection{padding:10px 11px;border:1px solid var(--m-line);border-radius:13px;background:rgba(255,255,255,.025);font-size:11px}
    body.sps-modern-2026 .version{padding:10px 8px 0;color:#536176;font-size:10px;line-height:1.35}

    body.sps-modern-2026 .main{grid-column:2;max-width:none;padding:18px 30px 126px 18px}
    body.sps-modern-2026 .topbar{
      position:sticky;top:12px;z-index:18;min-height:74px;margin:0 0 18px;padding:13px 16px 13px 19px;border:1px solid var(--m-line);border-radius:20px;
      background:linear-gradient(180deg,rgba(15,22,36,.86),rgba(10,16,27,.78));box-shadow:0 16px 50px rgba(0,0,0,.24);backdrop-filter:blur(28px) saturate(140%);
    }
    body.sps-modern-2026 .topbar h1{margin:0 0 3px;font-size:24px;line-height:1.12;letter-spacing:-.035em;font-weight:760}
    body.sps-modern-2026 .topbar p{font-size:12px;color:var(--m-muted)}
    body.sps-modern-2026 .top-actions{gap:8px}
    body.sps-modern-2026 .modern-top-badge{display:inline-flex;align-items:center;gap:7px;padding:7px 10px;border:1px solid var(--m-line);border-radius:999px;color:#a9b7ca;background:rgba(255,255,255,.025);font-size:10px;font-weight:800;letter-spacing:.08em}
    body.sps-modern-2026 .modern-top-badge i{width:7px;height:7px;border-radius:50%;background:var(--m-good);box-shadow:0 0 14px color-mix(in srgb,var(--m-good) 70%,transparent)}
    body.sps-modern-2026 .modern-quick-skin{display:flex;align-items:center;gap:7px;padding:4px 6px 4px 9px;border:1px solid var(--m-line);border-radius:12px;background:rgba(255,255,255,.025)}
    body.sps-modern-2026 .modern-quick-skin span{font-size:9px;color:#75869d;font-weight:850;letter-spacing:.12em}
    body.sps-modern-2026 .modern-quick-skin select{min-width:110px;min-height:32px;padding:5px 28px 5px 9px;border:0;background:rgba(255,255,255,.04);font-size:11px}

    body.sps-modern-2026 .view{animation:modernViewIn .22s ease both}
    body.sps-modern-2026 .view.active{display:block}
    @keyframes modernViewIn{from{opacity:.55;transform:translateY(5px)}to{opacity:1;transform:none}}
    body.sps-modern-2026 .panel{
      border:1px solid var(--m-line);border-radius:var(--m-radius);background:linear-gradient(180deg,var(--m-panel),rgba(10,16,27,.72));
      box-shadow:var(--m-shadow-soft);backdrop-filter:blur(var(--m-blur)) saturate(125%);
    }
    body.sps-modern-2026[data-sps-skin] .panel{background:linear-gradient(180deg,var(--m-panel),color-mix(in srgb,var(--m-panel-solid) 76%,transparent))}
    body.sps-modern-2026 .form-panel{padding:20px}
    body.sps-modern-2026 .form-panel h2,body.sps-modern-2026 .panel h2{letter-spacing:-.025em}
    body.sps-modern-2026 .form-panel h3,body.sps-modern-2026 .panel h3{letter-spacing:-.018em}
    body.sps-modern-2026 .muted,body.sps-modern-2026 .fine-print{color:var(--m-muted)}
    body.sps-modern-2026 .section-title{gap:14px}
    body.sps-modern-2026 .section-title h2,body.sps-modern-2026 .section-title h3{margin-top:0;margin-bottom:3px}

    body.sps-modern-2026 button,body.sps-modern-2026 input,body.sps-modern-2026 select,body.sps-modern-2026 textarea{font-family:inherit}
    body.sps-modern-2026 .btn{
      min-height:40px;padding:9px 14px;border-radius:12px;border:1px solid var(--m-line-strong);font-weight:760;letter-spacing:.006em;
      box-shadow:none;transition:transform .16s ease,border-color .16s ease,background .16s ease,box-shadow .16s ease;
    }
    body.sps-modern-2026 .btn:hover{transform:translateY(-1px);box-shadow:0 10px 30px rgba(0,0,0,.18)}
    body.sps-modern-2026 .btn:active{transform:translateY(0) scale(.985)}
    body.sps-modern-2026 .btn.primary{border-color:color-mix(in srgb,var(--m-accent) 65%,white 4%);background:linear-gradient(135deg,var(--m-accent),color-mix(in srgb,var(--m-accent-2) 58%,var(--m-accent)));box-shadow:0 9px 28px color-mix(in srgb,var(--m-accent) 20%,transparent)}
    body.sps-modern-2026 .btn.success{border-color:rgba(53,211,154,.38);background:linear-gradient(135deg,rgba(29,143,105,.95),rgba(35,191,137,.88));color:#ecfff8}
    body.sps-modern-2026 .btn.secondary{background:rgba(148,163,184,.075);border-color:var(--m-line);color:#e7eef8}
    body.sps-modern-2026 .btn.ghost{background:rgba(255,255,255,.018);border-color:var(--m-line);color:#b9c5d5}
    body.sps-modern-2026 .btn.danger{background:rgba(255,102,125,.10);border-color:rgba(255,102,125,.34);color:#ffb1bd}
    body.sps-modern-2026 .btn.small{min-height:34px;padding:7px 11px;border-radius:10px;font-size:11px}
    body.sps-modern-2026 .btn.large{min-height:46px;border-radius:14px}
    body.sps-modern-2026 .icon-btn{width:40px;height:40px;border:1px solid var(--m-line);border-radius:12px;background:rgba(148,163,184,.07);color:#eaf1fa}
    body.sps-modern-2026 button:focus-visible,body.sps-modern-2026 input:focus-visible,body.sps-modern-2026 select:focus-visible,body.sps-modern-2026 textarea:focus-visible,body.sps-modern-2026 summary:focus-visible{outline:2px solid var(--m-accent-2);outline-offset:2px}

    body.sps-modern-2026 input:not([type="checkbox"]):not([type="radio"]):not([type="range"]),body.sps-modern-2026 select,body.sps-modern-2026 textarea{
      min-height:42px;padding:9px 11px;border:1px solid var(--m-line);border-radius:12px;background:rgba(4,9,17,.62);color:var(--m-text);box-shadow:inset 0 1px 0 rgba(255,255,255,.018);transition:border-color .16s,box-shadow .16s,background .16s;
    }
    body.sps-modern-2026 textarea{min-height:88px;line-height:1.48}
    body.sps-modern-2026 input::placeholder,body.sps-modern-2026 textarea::placeholder{color:#596a80}
    body.sps-modern-2026 input:not([type="checkbox"]):not([type="radio"]):not([type="range"]):focus,body.sps-modern-2026 select:focus,body.sps-modern-2026 textarea:focus{
      border-color:color-mix(in srgb,var(--m-accent) 65%,white 5%);background:rgba(5,10,19,.82);box-shadow:0 0 0 4px color-mix(in srgb,var(--m-accent) 13%,transparent);outline:0;
    }
    body.sps-modern-2026 input[type="checkbox"],body.sps-modern-2026 input[type="radio"]{accent-color:var(--m-accent)}
    body.sps-modern-2026 input[type="range"]{accent-color:var(--m-accent)}
    body.sps-modern-2026 .search-wrap{min-height:46px;padding:0 12px;border:1px solid var(--m-line);border-radius:14px;background:rgba(4,9,17,.64);box-shadow:inset 0 1px 0 rgba(255,255,255,.02)}
    body.sps-modern-2026 .search-wrap:focus-within{border-color:color-mix(in srgb,var(--m-accent) 58%,transparent);box-shadow:0 0 0 4px var(--m-accent-soft)}
    body.sps-modern-2026 .search-wrap input{min-height:42px!important;border:0!important;background:transparent!important;box-shadow:none!important}
    body.sps-modern-2026 .toolbar{padding:11px;gap:9px}
    body.sps-modern-2026 .selection-bar{padding:11px 13px;border-radius:16px;gap:13px}
    body.sps-modern-2026 .selection-actions{gap:7px;flex-wrap:wrap}
    body.sps-modern-2026 .summary-line{padding:12px 4px 9px;color:#7f90a7;font-size:11px}

    body.sps-modern-2026 .songs-grid{gap:14px;grid-template-columns:repeat(auto-fill,minmax(250px,1fr))}
    body.sps-modern-2026 .song-card{
      border:1px solid var(--m-line);border-radius:18px;background:linear-gradient(180deg,rgba(18,27,43,.92),rgba(11,17,28,.92));box-shadow:0 11px 34px rgba(0,0,0,.17);overflow:hidden;
      transition:transform .18s ease,border-color .18s ease,box-shadow .18s ease;
    }
    body.sps-modern-2026 .song-card:hover{transform:translateY(-4px);border-color:color-mix(in srgb,var(--m-accent) 40%,var(--m-line));box-shadow:0 22px 60px rgba(0,0,0,.32)}
    body.sps-modern-2026 .song-card.selected{border-color:var(--m-accent);box-shadow:0 0 0 2px var(--m-accent-soft),0 20px 52px rgba(0,0,0,.28)}
    body.sps-modern-2026 .cover-wrap{background:#0c1421}
    body.sps-modern-2026 .cover-wrap:after{background:linear-gradient(transparent 48%,rgba(4,8,14,.80))}
    body.sps-modern-2026 .card-play{width:42px;height:42px;background:rgba(248,251,255,.95);box-shadow:0 12px 30px rgba(0,0,0,.28)}
    body.sps-modern-2026 .song-body{padding:14px}
    body.sps-modern-2026 .song-title{font-size:14px;font-weight:760}
    body.sps-modern-2026 .song-meta{color:#74869d}
    body.sps-modern-2026 .card-footer{border-color:var(--m-line)}
    body.sps-modern-2026 .badge,body.sps-modern-2026 .folder-badge,body.sps-modern-2026 .pill{border:1px solid rgba(255,255,255,.11);background:rgba(7,12,20,.72);backdrop-filter:blur(12px);border-radius:999px}

    body.sps-modern-2026 .task-panel{margin-bottom:15px;padding:14px 16px;border:1px solid color-mix(in srgb,var(--m-accent) 30%,var(--m-line));border-radius:17px;background:linear-gradient(120deg,var(--m-accent-soft),rgba(12,19,31,.86));box-shadow:var(--m-shadow-soft)}
    body.sps-modern-2026 .progress{height:7px;background:rgba(148,163,184,.12)}
    body.sps-modern-2026 .progress div{background:linear-gradient(90deg,var(--m-accent),var(--m-accent-2));box-shadow:0 0 20px color-mix(in srgb,var(--m-accent) 35%,transparent)}
    body.sps-modern-2026 .mini-log,body.sps-modern-2026 .tools-output,body.sps-modern-2026 .diagnostics-box{border:1px solid var(--m-line);border-radius:13px;background:rgba(2,7,13,.65);color:#aebbd0}

    body.sps-modern-2026 .two-col{gap:14px;margin-bottom:14px}
    body.sps-modern-2026 .settings-grid{gap:14px}
    body.sps-modern-2026 .stat-cards{gap:11px}
    body.sps-modern-2026 .stat-card{position:relative;overflow:hidden;padding:18px;border-radius:18px}
    body.sps-modern-2026 .stat-card:after{content:"";position:absolute;width:100px;height:100px;right:-45px;top:-45px;border-radius:50%;background:var(--m-accent-soft);filter:blur(4px)}
    body.sps-modern-2026 .stat-card strong{font-size:27px;letter-spacing:-.04em}
    body.sps-modern-2026 .bar-track{background:rgba(148,163,184,.10)}
    body.sps-modern-2026 .bar-fill{background:linear-gradient(90deg,var(--m-accent),var(--m-accent-2))}
    body.sps-modern-2026 .log-row{border-color:var(--m-line);font-family:"Cascadia Code","Segoe UI Mono",Consolas,monospace}

    body.sps-modern-2026 .format-card,body.sps-modern-2026 .toggle-row,body.sps-modern-2026 .mini-feature,
    body.sps-modern-2026 .tool-card,body.sps-modern-2026 .advanced-card,body.sps-modern-2026 .youtube-action-card,
    body.sps-modern-2026 .youtube-settings-box,body.sps-modern-2026 .version-group-card,body.sps-modern-2026 .folder-card{
      border:1px solid var(--m-line)!important;border-radius:16px!important;background:linear-gradient(180deg,rgba(255,255,255,.035),rgba(255,255,255,.018))!important;
      box-shadow:inset 0 1px 0 rgba(255,255,255,.025);transition:border-color .17s ease,background .17s ease,transform .17s ease;
    }
    body.sps-modern-2026 .tool-card:hover,body.sps-modern-2026 .advanced-card:hover,body.sps-modern-2026 .youtube-action-card:hover,body.sps-modern-2026 .mini-feature:hover{border-color:var(--m-line-strong)!important;background:rgba(255,255,255,.045)!important}
    body.sps-modern-2026 .format-card:has(input:checked){border-color:color-mix(in srgb,var(--m-accent) 58%,transparent)!important;background:var(--m-accent-soft)!important}
    body.sps-modern-2026 .toggle-row{padding:12px}
    body.sps-modern-2026 .big-number strong{font-size:42px;letter-spacing:-.05em}
    body.sps-modern-2026 .step{border-radius:10px;background:var(--m-accent-soft);color:color-mix(in srgb,var(--m-accent) 40%,white)}

    body.sps-modern-2026 .tools-hero,body.sps-modern-2026 .production-hero,body.sps-modern-2026 .audio-landing,body.sps-modern-2026 .connect-hero{
      border-color:color-mix(in srgb,var(--m-accent) 22%,var(--m-line));background:linear-gradient(125deg,var(--m-accent-soft),rgba(16,24,39,.58));overflow:hidden;position:relative;
    }
    body.sps-modern-2026 .tools-hero:after,body.sps-modern-2026 .production-hero:after,body.sps-modern-2026 .audio-landing:after,body.sps-modern-2026 .connect-hero:after{
      content:"";position:absolute;width:260px;height:260px;border-radius:50%;right:-120px;top:-150px;background:color-mix(in srgb,var(--m-accent-2) 12%,transparent);filter:blur(3px);pointer-events:none;
    }
    body.sps-modern-2026 .tools-tabs{display:flex;gap:6px;padding:7px;overflow:auto;background:rgba(7,12,20,.62);border-radius:16px}
    body.sps-modern-2026 .tools-tab{white-space:nowrap;min-height:38px;padding:8px 12px;border:1px solid transparent;border-radius:11px;background:transparent;color:#8192aa;font-weight:760;font-size:11px}
    body.sps-modern-2026 .tools-tab:hover{background:rgba(148,163,184,.07);color:#dce6f3}
    body.sps-modern-2026 .tools-tab.active{color:#fff;border-color:color-mix(in srgb,var(--m-accent) 38%,transparent);background:linear-gradient(135deg,var(--m-accent-soft),color-mix(in srgb,var(--m-accent-2) 8%,transparent));box-shadow:inset 0 0 0 1px rgba(255,255,255,.025)}
    body.sps-modern-2026 .youtube-oauth-connect{border:1px solid var(--m-line);border-radius:18px;background:rgba(255,255,255,.022);overflow:hidden}
    body.sps-modern-2026 .youtube-google-icon{border-radius:15px;box-shadow:0 12px 30px rgba(0,0,0,.22)}
    body.sps-modern-2026 .youtube-first-setup,body.sps-modern-2026 .youtube-advanced-settings,body.sps-modern-2026 .organized-maintenance,body.sps-modern-2026 .organized-extra-imports{
      border-radius:15px;overflow:hidden
    }
    body.sps-modern-2026 .youtube-first-setup>summary,body.sps-modern-2026 .youtube-advanced-settings>summary,body.sps-modern-2026 .organized-maintenance>summary,body.sps-modern-2026 .organized-extra-imports>summary{
      border:1px solid var(--m-line)!important;border-radius:14px!important;background:rgba(255,255,255,.028)!important;color:#c8d3e1!important;padding:12px 14px!important
    }
    body.sps-modern-2026 details[open]>summary{border-bottom-left-radius:8px!important;border-bottom-right-radius:8px!important}
    body.sps-modern-2026 .model-bar{border:1px solid var(--m-line);border-radius:15px;background:rgba(3,8,15,.42);padding:8px}
    body.sps-modern-2026 .model-chip{border:1px solid var(--m-line);border-radius:11px;background:rgba(255,255,255,.025)}
    body.sps-modern-2026 .youtube-summary-badges span{border:1px solid var(--m-line);border-radius:999px;background:rgba(255,255,255,.035);padding:6px 9px}
    body.sps-modern-2026 .yt-reconcile{border:1px solid color-mix(in srgb,var(--m-accent) 34%,var(--m-line))!important;border-radius:18px!important;background:linear-gradient(135deg,var(--m-accent-soft),rgba(7,16,28,.68))!important;box-shadow:var(--m-shadow-soft)}
    body.sps-modern-2026 .yt-reconcile-stat{border:1px solid var(--m-line)!important;border-radius:13px!important;background:rgba(3,9,17,.48)!important}

    body.sps-modern-2026 .organized-workflow{gap:7px;padding:7px;border:1px solid var(--m-line)!important;border-radius:16px!important;background:rgba(5,10,18,.54)!important}
    body.sps-modern-2026 .organized-step{min-height:58px;border:1px solid var(--m-line)!important;border-radius:13px!important;background:rgba(255,255,255,.025)!important;color:#dbe5f3!important}
    body.sps-modern-2026 .organized-step:hover{border-color:color-mix(in srgb,var(--m-accent) 35%,var(--m-line))!important;background:var(--m-accent-soft)!important}
    body.sps-modern-2026 .organized-step b{background:linear-gradient(135deg,var(--m-accent),var(--m-accent-2))!important;box-shadow:0 8px 20px var(--m-accent-soft)}
    body.sps-modern-2026 .organized-section{border:1px solid var(--m-line)!important;border-radius:18px!important;background:rgba(12,19,31,.62)!important}

    body.sps-modern-2026 #productionWorkspace{border:1px solid var(--m-line)!important;border-radius:20px!important;background:rgba(10,16,27,.72)!important;box-shadow:var(--m-shadow)}
    body.sps-modern-2026 #productionWorkspace .pws-head{padding:15px 16px;border-bottom:1px solid var(--m-line)!important;background:linear-gradient(125deg,var(--m-accent-soft),color-mix(in srgb,var(--m-accent-2) 7%,transparent))!important}
    body.sps-modern-2026 #productionWorkspace .pws-body{gap:12px;padding:12px;background:rgba(3,8,15,.38)!important}
    body.sps-modern-2026 #productionWorkspace .pws-panel,body.sps-modern-2026 #productionWorkspace .pws-timeline-panel{border:1px solid var(--m-line)!important;border-radius:16px!important;background:rgba(13,21,34,.74)!important;overflow:hidden}
    body.sps-modern-2026 #productionWorkspace .pws-preview-shell{border:1px solid var(--m-line);border-radius:14px!important;background:linear-gradient(145deg,#03070d,#0a1420)!important}
    body.sps-modern-2026 #productionWorkspace .pws-preview{border:1px solid rgba(255,255,255,.09);border-radius:12px;box-shadow:0 20px 60px rgba(0,0,0,.42)!important}
    body.sps-modern-2026 #productionWorkspace .pws-status{border:1px solid var(--m-line)!important;border-radius:11px!important;background:rgba(4,9,17,.5)}
    body.sps-modern-2026 #productionWorkspace .pws-timeline-toolbar{border-bottom:1px solid var(--m-line)!important;background:rgba(255,255,255,.025)}
    body.sps-modern-2026 #productionWorkspace .pws-scroll{background:#050a11!important}
    body.sps-modern-2026 #productionWorkspace .pws-ruler{background:#0b121d!important;border-color:var(--m-line)!important}
    body.sps-modern-2026 #productionWorkspace .pws-track{border-color:var(--m-line)!important}
    body.sps-modern-2026 #productionWorkspace .pws-cue{border-color:var(--m-accent)!important;background:color-mix(in srgb,var(--m-accent) 28%,transparent)!important;border-radius:7px!important;box-shadow:0 6px 18px rgba(0,0,0,.18)}
    body.sps-modern-2026 #productionWorkspace .pws-cue.active{border-color:var(--m-warn)!important;background:rgba(247,184,75,.24)!important}
    body.sps-modern-2026 #productionWorkspace .pws-playhead{background:#ff4d6d!important;box-shadow:0 0 12px rgba(255,77,109,.45)}

    body.sps-modern-2026 .modal-backdrop{background:rgba(1,4,9,.72);backdrop-filter:blur(14px)}
    body.sps-modern-2026 .modal-card{border:1px solid var(--m-line-strong);border-radius:22px;background:linear-gradient(180deg,rgba(17,25,40,.97),rgba(8,13,22,.97));box-shadow:0 40px 120px rgba(0,0,0,.60)}
    body.sps-modern-2026 .modal-close{border:1px solid var(--m-line);border-radius:11px;background:rgba(255,255,255,.055)}
    body.sps-modern-2026 .modal-tabs{gap:5px;padding:5px;border:1px solid var(--m-line);border-radius:13px;background:rgba(3,8,15,.45)}
    body.sps-modern-2026 .modal-tabs button{border-radius:9px;color:#8798ae}
    body.sps-modern-2026 .modal-tabs button.active{background:var(--m-accent-soft);color:#fff}

    body.sps-modern-2026 .player{left:294px;right:20px;bottom:16px;border:1px solid var(--m-line-strong);border-radius:18px;background:linear-gradient(180deg,rgba(14,21,34,.92),rgba(7,12,20,.92));box-shadow:0 22px 70px rgba(0,0,0,.46);backdrop-filter:blur(26px) saturate(135%)}
    body.sps-modern-2026 .player img{border-radius:12px}
    body.sps-modern-2026 .toast-container{top:96px;right:24px}
    body.sps-modern-2026 .toast{border:1px solid var(--m-line-strong);border-radius:14px;background:rgba(16,24,39,.94);backdrop-filter:blur(20px);box-shadow:0 18px 48px rgba(0,0,0,.34)}

    body.sps-modern-2026 .modern-skin-panel{margin-bottom:14px;padding:18px;overflow:hidden;position:relative}
    body.sps-modern-2026 .modern-skin-panel:before{content:"";position:absolute;width:320px;height:180px;right:-130px;top:-100px;border-radius:50%;background:var(--m-accent-soft);filter:blur(10px);pointer-events:none}
    body.sps-modern-2026 .modern-skin-title{display:flex;justify-content:space-between;gap:16px;align-items:flex-start;margin-bottom:13px}
    body.sps-modern-2026 .modern-skin-title h2{margin:0 0 4px;font-size:19px}
    body.sps-modern-2026 .modern-skin-title p{margin:0;color:var(--m-muted);font-size:12px}
    body.sps-modern-2026 .modern-skin-grid{display:grid;grid-template-columns:repeat(3,minmax(150px,1fr));gap:10px}
    body.sps-modern-2026 .modern-skin-choice{position:relative;display:grid;grid-template-columns:46px 1fr;gap:11px;align-items:center;min-height:72px;padding:11px;border:1px solid var(--m-line);border-radius:15px;background:rgba(255,255,255,.025);color:#dce7f5;text-align:left}
    body.sps-modern-2026 .modern-skin-choice:hover{border-color:var(--m-line-strong);background:rgba(255,255,255,.045)}
    body.sps-modern-2026 .modern-skin-choice.active{border-color:var(--m-accent);box-shadow:0 0 0 2px var(--m-accent-soft);background:var(--m-accent-soft)}
    body.sps-modern-2026 .modern-skin-preview{width:46px;height:46px;border-radius:13px;border:1px solid rgba(255,255,255,.13);box-shadow:inset 0 1px 0 rgba(255,255,255,.11)}
    body.sps-modern-2026 .modern-skin-preview.aurora{background:radial-gradient(circle at 25% 20%,#8b5cf6,transparent 42%),linear-gradient(145deg,#0b1020,#06131c)}
    body.sps-modern-2026 .modern-skin-preview.graphite{background:radial-gradient(circle at 28% 22%,#d7dde8,transparent 32%),linear-gradient(145deg,#171a20,#07090c)}
    body.sps-modern-2026 .modern-skin-preview.midnight{background:radial-gradient(circle at 25% 20%,#29d3ff,transparent 38%),linear-gradient(145deg,#041823,#020609)}
    body.sps-modern-2026 .modern-skin-choice strong{display:block;font-size:12px}.modern-skin-choice small{display:block;margin-top:3px;color:#7f90a7;font-size:10px}
    body.sps-modern-2026 .legacy-theme-settings{display:none!important}

    body.sps-modern-2026 .pagination-bar{border-radius:15px;padding:9px 11px}
    body.sps-modern-2026 .empty{padding:64px 20px}
    body.sps-modern-2026 .empty-icon{border:1px solid var(--m-line);background:var(--m-accent-soft);color:var(--m-accent);box-shadow:0 14px 45px rgba(0,0,0,.22)}
    body.sps-modern-2026 hr{border:0;border-top:1px solid var(--m-line)}

    @media(max-width:1200px){
      body.sps-modern-2026 .app-shell{grid-template-columns:238px minmax(0,1fr)}
      body.sps-modern-2026 .sidebar{width:220px}
      body.sps-modern-2026 .main{padding-right:18px}
      body.sps-modern-2026 .player{left:250px}
      body.sps-modern-2026 .modern-quick-skin{display:none}
    }
    @media(max-width:900px){
      body.sps-modern-2026 .app-shell{display:block}
      body.sps-modern-2026 .sidebar{position:relative;inset:auto;width:auto;margin:10px;border-radius:20px;max-height:none}
      body.sps-modern-2026 nav{grid-template-columns:repeat(2,minmax(0,1fr))}
      body.sps-modern-2026 .nav-more{grid-column:1/-1}
      body.sps-modern-2026 .main{padding:6px 10px 120px}
      body.sps-modern-2026 .topbar{top:8px;border-radius:17px;align-items:flex-start}
      body.sps-modern-2026 .top-actions{justify-content:flex-end}
      body.sps-modern-2026 .player{left:10px;right:10px;bottom:10px}
      body.sps-modern-2026 .modern-skin-grid{grid-template-columns:1fr}
      body.sps-modern-2026 .two-col,body.sps-modern-2026 .settings-grid{grid-template-columns:1fr}
    }
    @media(max-width:640px){
      body.sps-modern-2026 nav{grid-template-columns:1fr}
      body.sps-modern-2026 .topbar{display:block}
      body.sps-modern-2026 .top-actions{margin-top:10px;justify-content:flex-start}
      body.sps-modern-2026 .toolbar,body.sps-modern-2026 .selection-bar{align-items:stretch;flex-direction:column}
      body.sps-modern-2026 .selection-actions{margin-left:0}
      body.sps-modern-2026 .songs-grid{grid-template-columns:1fr}
    }
  `;
  document.head.appendChild(css);

  function installTopChrome() {
    const topActions = document.querySelector('.top-actions');
    if (!topActions) return;

    if (!document.getElementById('modernTopBadge')) {
      const badge = document.createElement('span');
      badge.id = 'modernTopBadge';
      badge.className = 'modern-top-badge';
      badge.innerHTML = '<i></i> STUDIO 2026';
      topActions.prepend(badge);
    }

    if (!document.getElementById('modernSkinQuickSelect')) {
      const quick = document.createElement('label');
      quick.className = 'modern-quick-skin';
      quick.innerHTML = '<span>SKIN</span><select id="modernSkinQuickSelect" aria-label="Moderni skin"><option value="aurora">Aurora</option><option value="graphite">Graphite</option><option value="midnight">Midnight</option></select>';
      topActions.prepend(quick);
      quick.querySelector('select').addEventListener('change', e => applySkin(e.target.value));
    }
  }

  function installSettingsSkinPanel() {
    const settings = document.getElementById('view-settings');
    if (!settings) return;

    const legacy = settings.querySelector('.theme-settings');
    if (legacy) legacy.classList.add('legacy-theme-settings');
    if (document.getElementById('modernSkinPanel')) return;

    const panel = document.createElement('section');
    panel.id = 'modernSkinPanel';
    panel.className = 'panel modern-skin-panel';
    panel.innerHTML = `
      <div class="modern-skin-title">
        <div><h2>Izgled programa — 2026 UI</h2><p>Tri moderna skina koriste isti čist raspored. Menja se samo vizuelni karakter, ne funkcije programa.</p></div>
        <span class="modern-top-badge"><i></i> AKTIVAN NOVI UI</span>
      </div>
      <div class="modern-skin-grid">
        <button type="button" class="modern-skin-choice" data-modern-skin-choice="aurora"><span class="modern-skin-preview aurora"></span><span><strong>Aurora Studio</strong><small>Ljubičasti + cyan akcenti</small></span></button>
        <button type="button" class="modern-skin-choice" data-modern-skin-choice="graphite"><span class="modern-skin-preview graphite"></span><span><strong>Graphite Pro</strong><small>Neutralan profesionalni izgled</small></span></button>
        <button type="button" class="modern-skin-choice" data-modern-skin-choice="midnight"><span class="modern-skin-preview midnight"></span><span><strong>Midnight Signal</strong><small>Tamna audio/video konzola</small></span></button>
      </div>`;

    if (legacy) legacy.insertAdjacentElement('beforebegin', panel);
    else settings.prepend(panel);

    panel.querySelectorAll('[data-modern-skin-choice]').forEach(button => {
      button.addEventListener('click', () => applySkin(button.dataset.modernSkinChoice));
    });
  }

  function modernizeStructure() {
    document.querySelectorAll('.view').forEach(view => view.classList.add('modern-view-surface'));
    document.querySelectorAll('.panel').forEach(panel => panel.classList.add('modern-card-surface'));

    const brand = document.querySelector('.brand');
    if (brand && !brand.querySelector('.modern-brand-year')) {
      const year = document.createElement('span');
      year.className = 'modern-brand-year';
      year.textContent = '2026';
      year.style.cssText = 'margin-left:auto;padding:4px 7px;border-radius:999px;border:1px solid var(--m-line);color:#75869d;font-size:9px;font-weight:850;letter-spacing:.08em';
      brand.appendChild(year);
    }
  }

  function install() {
    applySkin(readSkin());
    installTopChrome();
    installSettingsSkinPanel();
    modernizeStructure();
    applySkin(readSkin());
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', install, {once:true});
  else install();

  window.SPSModern2026 = {applySkin, readSkin};
})();
