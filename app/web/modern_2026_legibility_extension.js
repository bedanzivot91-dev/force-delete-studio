/* Final typography/legibility pass for the 2026 UI.
 * Loaded after every other visual layer so old 9/10/11px historical rules can
 * no longer make the current interface look tiny or dated.
 */
(() => {
  'use strict';
  if (document.getElementById('spsModern2026Legibility')) return;
  const css = document.createElement('style');
  css.id = 'spsModern2026Legibility';
  css.textContent = `
    body.sps-modern-2026{font-size:15px;line-height:1.55;text-rendering:optimizeLegibility;-webkit-font-smoothing:antialiased}
    body.sps-modern-2026 p,body.sps-modern-2026 li,body.sps-modern-2026 label,body.sps-modern-2026 .inline-message,body.sps-modern-2026 .summary-line{font-size:14px;line-height:1.55}
    body.sps-modern-2026 .muted{font-size:13.5px;line-height:1.5;color:#9eacc0}
    body.sps-modern-2026 .fine-print,body.sps-modern-2026 small{font-size:12.5px;line-height:1.5}

    body.sps-modern-2026 .brand strong{font-size:17px}
    body.sps-modern-2026 .brand span{font-size:12.5px;letter-spacing:.12em}
    body.sps-modern-2026 .modern-brand-year{font-size:12.5px!important}
    body.sps-modern-2026 .nav-group-title{font-size:12.5px;letter-spacing:.10em}
    body.sps-modern-2026 .nav-item{font-size:14px;min-height:46px}
    body.sps-modern-2026 .nav-more>summary{font-size:12.5px;letter-spacing:.08em}
    body.sps-modern-2026 .connection{font-size:12.5px}
    body.sps-modern-2026 .version{font-size:12.5px;line-height:1.45}

    body.sps-modern-2026 .topbar h1{font-size:27px}
    body.sps-modern-2026 .topbar p{font-size:14px}
    body.sps-modern-2026 .modern-top-badge{font-size:12.5px}
    body.sps-modern-2026 .modern-quick-skin span{font-size:12.5px}
    body.sps-modern-2026 .modern-quick-skin select{font-size:13px;min-height:36px}

    body.sps-modern-2026 .panel h2,body.sps-modern-2026 .form-panel h2{font-size:20px;line-height:1.25}
    body.sps-modern-2026 .panel h3,body.sps-modern-2026 .form-panel h3{font-size:16px;line-height:1.3}
    body.sps-modern-2026 .btn{font-size:14px;line-height:1.25;min-height:42px}
    body.sps-modern-2026 .btn.small{font-size:13px;min-height:36px}
    body.sps-modern-2026 .btn.large{font-size:15px;min-height:48px}
    body.sps-modern-2026 input,body.sps-modern-2026 select,body.sps-modern-2026 textarea{font-size:14px;line-height:1.4;min-height:42px}
    body.sps-modern-2026 textarea{line-height:1.55}
    body.sps-modern-2026 input[type=checkbox],body.sps-modern-2026 input[type=radio],body.sps-modern-2026 input[type=color],body.sps-modern-2026 input[type=range]{min-height:0}

    body.sps-modern-2026 .badge,body.sps-modern-2026 .folder-badge,body.sps-modern-2026 .pill,body.sps-modern-2026 .oauth-badge,body.sps-modern-2026 .count-badge,body.sps-modern-2026 .status-pill{font-size:12.5px!important}
    body.sps-modern-2026 .system-label,body.sps-modern-2026 .youtube-channel-meta i{font-size:12.5px!important}
    body.sps-modern-2026 .song-title{font-size:16px}
    body.sps-modern-2026 .song-meta,body.sps-modern-2026 .song-tags{font-size:12.5px;line-height:1.45}
    body.sps-modern-2026 .format-card span,body.sps-modern-2026 .toggle-row small,body.sps-modern-2026 .download-footer span,body.sps-modern-2026 .mini-feature span{font-size:12.5px}
    body.sps-modern-2026 .stat-card span,body.sps-modern-2026 .bar-row{font-size:13px!important}

    body.sps-modern-2026 .task-head span,body.sps-modern-2026 .task-meta{font-size:12.5px}
    body.sps-modern-2026 .mini-log,body.sps-modern-2026 .logs-table,body.sps-modern-2026 .log-row,body.sps-modern-2026 .tools-output,body.sps-modern-2026 pre,body.sps-modern-2026 code{font-size:12.5px;line-height:1.55}
    body.sps-modern-2026 .toast{font-size:13.5px;line-height:1.45}

    body.sps-modern-2026 .modal-tabs button,body.sps-modern-2026 .text-tools button,body.sps-modern-2026 .quick-clips button{font-size:13px}
    body.sps-modern-2026 .text-stats,body.sps-modern-2026 .text-stats span,body.sps-modern-2026 .audio-info-box,body.sps-modern-2026 .file-list{font-size:13px!important}
    body.sps-modern-2026 .derived-item span,body.sps-modern-2026 .audio-source-note{font-size:12.5px!important}

    body.sps-modern-2026 .tools-tab,body.sps-modern-2026 .youtube-action-card,body.sps-modern-2026 .youtube-settings-box,body.sps-modern-2026 .advanced-card{font-size:14px}
    body.sps-modern-2026 .youtube-action-card p,body.sps-modern-2026 .youtube-settings-box p,body.sps-modern-2026 .advanced-card p{font-size:13.5px}
    body.sps-modern-2026 .youtube-summary-badges,body.sps-modern-2026 .youtube-calendar,body.sps-modern-2026 .youtube-matrix-wrap,body.sps-modern-2026 .youtube-coverage-list,body.sps-modern-2026 .youtube-audio-results-list,body.sps-modern-2026 .youtube-matches-list{font-size:13px}
    body.sps-modern-2026 .youtube-channel-main span,body.sps-modern-2026 .youtube-audio-card-main p{font-size:13px!important}
    body.sps-modern-2026 .youtube-complete-badge,body.sps-modern-2026 .confidence,body.sps-modern-2026 .audio-score-grid span,body.sps-modern-2026 .audio-score-grid strong,body.sps-modern-2026 .matched-range{font-size:12.5px!important}
    body.sps-modern-2026 .matrix-status,body.sps-modern-2026 .matrix-open-video,body.sps-modern-2026 .matrix-open-suno,body.sps-modern-2026 .matrix-search-video{font-size:12.5px!important}
    body.sps-modern-2026 .youtube-matrix-table thead th{font-size:13px!important}

    body.sps-modern-2026 #productionWorkspace{font-size:14px}
    body.sps-modern-2026 .pws-head h2{font-size:21px}
    body.sps-modern-2026 .pws-song-sub,body.sps-modern-2026 .pws-controls label,body.sps-modern-2026 .pws-status,body.sps-modern-2026 .pws-timeline-toolbar{font-size:13px}
    body.sps-modern-2026 .pws-track-label,body.sps-modern-2026 .pws-editor-head,body.sps-modern-2026 .pws-tick{font-size:12.5px}
    body.sps-modern-2026 .pws-cue{font-size:12.5px}
    body.sps-modern-2026 .pws-editor input{font-size:13px;min-height:36px}

    body.sps-modern-2026 .ia2026-section>summary span{font-size:15px}
    body.sps-modern-2026 .ia2026-section>summary small{font-size:13px}

    /* Never let historical compact styling shrink text; compact changes space only. */
    body.sps-modern-2026[data-density="compact"]{font-size:15px}
    body.sps-modern-2026[data-density="comfortable"]{font-size:15.5px}

    @media(max-width:900px){
      body.sps-modern-2026{font-size:15px}
      body.sps-modern-2026 .topbar h1{font-size:24px}
      body.sps-modern-2026 .nav-item{font-size:14px}
      body.sps-modern-2026 input,body.sps-modern-2026 select,body.sps-modern-2026 textarea{font-size:16px}
    }
  `;
  document.head.appendChild(css);
})();