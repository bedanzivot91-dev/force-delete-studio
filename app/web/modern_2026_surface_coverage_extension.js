/* Explicit page-by-page 2026 surface coverage.
 * Shared .panel/.btn/input rules already modernize the whole app; this layer
 * covers page-specific structures so no historical screen keeps a flat/dated
 * visual island inside the new shell.
 */
(() => {
  'use strict';
  if (document.getElementById('spsModern2026SurfaceCoverage')) return;
  const css = document.createElement('style');
  css.id = 'spsModern2026SurfaceCoverage';
  css.textContent = `
    /* Every routed page gets the same spacing rhythm and visual entry surface. */
    body.sps-modern-2026 .view.modern-view-surface{min-width:0}
    body.sps-modern-2026 .view.modern-view-surface>.panel,
    body.sps-modern-2026 .view.modern-view-surface>.two-col,
    body.sps-modern-2026 .view.modern-view-surface>.settings-grid,
    body.sps-modern-2026 .view.modern-view-surface>.stat-cards{margin-bottom:16px}

    /* BIBLIOTEKA */
    body.sps-modern-2026 #view-library .library-toolbar,
    body.sps-modern-2026 #view-library .selection-bar{border:1px solid var(--m-line);border-radius:16px;background:rgba(7,12,20,.56);box-shadow:var(--m-shadow-soft)}
    body.sps-modern-2026 #view-library .pagination-bar{border:1px solid var(--m-line);border-radius:15px;background:rgba(255,255,255,.022)}

    /* KOLEKCIJE */
    body.sps-modern-2026 #view-folders .folder-create{border:1px solid var(--m-line);border-radius:19px;background:linear-gradient(125deg,var(--m-accent-soft),rgba(12,19,31,.65));box-shadow:var(--m-shadow-soft)}
    body.sps-modern-2026 #view-folders .folder-card{border-radius:18px!important;box-shadow:0 12px 34px rgba(0,0,0,.16)}
    body.sps-modern-2026 #view-folders .folder-icon{border-radius:14px}

    /* AUDIO */
    body.sps-modern-2026 #view-audio .audio-landing{border:1px solid var(--m-line);border-radius:20px;background:linear-gradient(125deg,var(--m-accent-soft),rgba(12,19,31,.67));box-shadow:var(--m-shadow-soft)}
    body.sps-modern-2026 #view-audio .big-tool-icon{border-radius:18px;background:linear-gradient(145deg,var(--m-accent),var(--m-accent-2));box-shadow:0 16px 40px color-mix(in srgb,var(--m-accent) 22%,transparent)}
    body.sps-modern-2026 #view-audio .mini-feature{border-radius:17px!important}

    /* PREUZIMANJE */
    body.sps-modern-2026 #view-download .format-card,
    body.sps-modern-2026 #view-download .toggle-row,
    body.sps-modern-2026 #view-download .download-performance{border-radius:16px!important}
    body.sps-modern-2026 #view-download .download-footer{border-top:1px solid var(--m-line);padding:17px 2px 2px}
    body.sps-modern-2026 #view-download .big-number strong{background:linear-gradient(135deg,var(--m-accent),var(--m-accent-2));-webkit-background-clip:text;background-clip:text;color:transparent}

    /* SUNO / UVOZ */
    body.sps-modern-2026 #view-import .connect-hero{border-radius:22px;box-shadow:var(--m-shadow-soft)}
    body.sps-modern-2026 #view-import .connect-art{border-radius:22px;background:linear-gradient(145deg,var(--m-accent),var(--m-accent-2));box-shadow:0 18px 50px color-mix(in srgb,var(--m-accent) 25%,transparent)}
    body.sps-modern-2026 #view-import .manual-token-box,
    body.sps-modern-2026 #view-import .organized-extra-imports{border-radius:15px}

    /* PRONALAZAČ */
    body.sps-modern-2026 #view-recognition .tools-hero{border-radius:20px;box-shadow:var(--m-shadow-soft)}
    body.sps-modern-2026 #view-recognition .youtube-action-card{padding:17px}
    body.sps-modern-2026 #view-recognition details.panel>summary{cursor:pointer;padding:4px 0}
    body.sps-modern-2026 #view-recognition .youtube-matches-list>*{border-color:var(--m-line)!important;border-radius:15px!important;background:rgba(255,255,255,.025)!important}

    /* PAMETNA BIBLIOTEKA */
    body.sps-modern-2026 #view-smart .smart-rule-row{padding:10px;border:1px solid var(--m-line);border-radius:14px;background:rgba(255,255,255,.023)}
    body.sps-modern-2026 #view-smart .smart-rules-list{gap:10px}
    body.sps-modern-2026 #view-smart .file-item{border-color:var(--m-line);border-radius:14px;background:rgba(255,255,255,.023)}

    /* VERZIJE PESME */
    body.sps-modern-2026 #view-versions .version-member{padding:13px 14px;border:1px solid var(--m-line);border-radius:15px;background:rgba(255,255,255,.025);box-shadow:inset 0 1px 0 rgba(255,255,255,.02)}
    body.sps-modern-2026 #view-versions .version-member.is-master{border-color:color-mix(in srgb,var(--m-accent) 60%,var(--m-line));box-shadow:0 0 0 2px var(--m-accent-soft)}

    /* PRIPREMA ZA OBJAVU */
    body.sps-modern-2026 #view-release .release-readiness-table{gap:7px}
    body.sps-modern-2026 #view-release .release-row{padding:11px 12px;border-radius:13px}
    body.sps-modern-2026 #view-release .release-row:not(.release-row-head){border:1px solid var(--m-line);background:rgba(255,255,255,.025);box-shadow:inset 0 1px 0 rgba(255,255,255,.018)}
    body.sps-modern-2026 #view-release .release-row-head{color:#93a4b9}

    /* YOUTUBE */
    body.sps-modern-2026 #view-tools .youtube-command-center,
    body.sps-modern-2026 #view-tools .youtube-audio-lab{border-radius:20px}
    body.sps-modern-2026 #view-tools .youtube-date-row,
    body.sps-modern-2026 #view-tools .youtube-audio-card{border-color:var(--m-line)!important;border-radius:16px!important;background:rgba(255,255,255,.024)!important}
    body.sps-modern-2026 #view-tools .youtube-matrix-wrap{border-color:var(--m-line);border-radius:16px;background:rgba(3,8,15,.45)}

    /* VIDEO STUDIO */

    /* STATISTIKA */
    body.sps-modern-2026 #view-stats .chart-panel{border-radius:19px}
    body.sps-modern-2026 #view-stats .bar-track{height:9px;border:1px solid var(--m-line)}

    /* DNEVNIK */
    body.sps-modern-2026 #view-logs .logs-table{border:1px solid var(--m-line);border-radius:15px;overflow:auto;background:rgba(3,8,15,.42)}
    body.sps-modern-2026 #view-logs .log-row{padding:11px 12px}

    /* PODEŠAVANJA */
    body.sps-modern-2026 #view-settings .settings-grid{align-items:start}
    body.sps-modern-2026 #view-settings #accessibilityPanel{border-color:color-mix(in srgb,var(--m-accent-2) 20%,var(--m-line))}
    body.sps-modern-2026 #view-settings details>summary{transition:background .16s,border-color .16s}
    body.sps-modern-2026 #view-settings details>summary:hover{background:rgba(255,255,255,.045)!important}

    /* Shared results/tables that appear on several pages. */
    body.sps-modern-2026 .file-item,
    body.sps-modern-2026 .report-row,
    body.sps-modern-2026 .diagnostics-row{border-color:var(--m-line)!important;border-radius:13px!important;background:rgba(255,255,255,.022)!important}

    @media(max-width:900px){
      body.sps-modern-2026 #view-library .library-toolbar{border-radius:14px}
      body.sps-modern-2026 #view-versions .version-member{grid-template-columns:1fr}
      body.sps-modern-2026 #view-release .release-row{grid-template-columns:1fr}
    }
  `;
  document.head.appendChild(css);
})();
