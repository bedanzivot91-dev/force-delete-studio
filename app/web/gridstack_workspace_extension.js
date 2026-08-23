/* Functional Suno workspace powered by the locally bundled GridStack 13.2.0. */
(() => {
  'use strict';
  const home = document.getElementById('view-home');
  if (!home || home.dataset.gridWorkspaceReady === '1' || typeof GridStack === 'undefined') return;
  home.dataset.gridWorkspaceReady = '1';

  const css = document.createElement('link');
  css.rel = 'stylesheet';
  css.href = '/vendor/gridstack/gridstack.min.css';
  css.dataset.sunoGridstack = '1';
  document.head.appendChild(css);
  const style = document.createElement('style');
  style.id = 'sunoGridWorkspaceStyle';
  style.textContent = `
    .suno-workspace-toolbar{display:flex;align-items:center;gap:9px;flex-wrap:wrap;margin:14px 0 8px;padding:10px 12px;border:1px solid var(--m-line,var(--line));border-radius:var(--m-radius-sm,12px);background:var(--m-panel,var(--card))}
    .suno-workspace-toolbar strong{margin-right:auto}.suno-workspace-toolbar .workspace-state{font-size:12px;color:var(--m-muted,var(--muted))}
    .suno-functional-grid{min-height:280px}.suno-functional-grid>.grid-stack-item>.grid-stack-item-content{inset:6px!important;overflow:visible}
    .suno-functional-grid .suno-flow-card{width:100%!important;height:100%!important;margin:0!important;display:flex!important;flex-direction:column!important;justify-content:center!important;overflow:hidden}
    .suno-functional-grid.workspace-editing>.grid-stack-item>.grid-stack-item-content{outline:2px dashed var(--m-accent,var(--accent));outline-offset:-3px;cursor:move}
    .suno-functional-grid.workspace-editing .suno-flow-card::after{content:'PREVUCI / PROMENI VELIČINU';font-size:9px;letter-spacing:.08em;color:var(--m-accent,var(--accent));margin-top:auto;padding-top:8px}
    body[data-sps-skin="graphite-console"] .suno-functional-grid{background-image:linear-gradient(rgba(255,255,255,.025) 1px,transparent 1px),linear-gradient(90deg,rgba(255,255,255,.025) 1px,transparent 1px);background-size:24px 24px}
    body[data-sps-skin="vinyl-loft"] .suno-functional-grid>.grid-stack-item>.grid-stack-item-content{inset:10px!important;transform:rotate(-.35deg)}
    body[data-sps-skin="signal-grid"] .suno-functional-grid>.grid-stack-item>.grid-stack-item-content{inset:3px!important}
    body[data-sps-skin="paper-studio"] .suno-functional-grid>.grid-stack-item>.grid-stack-item-content{inset:8px!important}
    body[data-sps-skin="neon-stage"] .suno-functional-grid>.grid-stack-item:nth-child(even)>.grid-stack-item-content{transform:translateY(8px)}
    body[data-sps-skin="album-wall"] .suno-functional-grid>.grid-stack-item>.grid-stack-item-content{inset:12px!important}
    body[data-sps-skin="mixer-desk"] .suno-functional-grid>.grid-stack-item>.grid-stack-item-content{inset:4px!important}
    @media(max-width:760px){.suno-functional-grid>.grid-stack-item>.grid-stack-item-content{inset:5px!important}.suno-workspace-toolbar strong{width:100%}}
  `;
  document.head.appendChild(style);

  const primary = home.querySelector('.suno-flow');
  const secondary = home.querySelector('.suno-home-secondary');
  if (!primary || !secondary) return;
  const cards = [...primary.children, ...secondary.children];
  const gridElement = document.createElement('div');
  gridElement.className = 'grid-stack suno-functional-grid';
  cards.forEach((card, index) => {
    const item = document.createElement('div');
    item.className = 'grid-stack-item';
    item.dataset.cardIndex = String(index);
    const content = document.createElement('div');
    content.className = 'grid-stack-item-content';
    content.appendChild(card);
    item.appendChild(content);
    gridElement.appendChild(item);
  });
  const toolbar = document.createElement('div');
  toolbar.className = 'suno-workspace-toolbar';
  toolbar.innerHTML = '<strong>Moja radna površina</strong><span class="workspace-state">Raspored je zaključan — sve funkcije rade jednim klikom.</span><button class="btn ghost small" type="button" data-workspace-edit>Uredi raspored</button><button class="btn ghost small" type="button" data-workspace-reset>Vrati raspored teme</button>';
  primary.replaceWith(toolbar, gridElement);
  secondary.remove();

  const layouts = {
    'aurora-flow': [[0,0,3,2],[3,0,3,2],[6,0,3,2],[9,0,3,2],[0,2,4,1],[4,2,4,1],[8,2,4,1]],
    'graphite-console': [[0,0,6,1],[6,0,6,1],[0,1,6,1],[6,1,6,1],[0,2,4,1],[4,2,4,1],[8,2,4,1]],
    'vinyl-loft': [[0,0,5,3],[5,0,7,2],[5,2,7,2],[0,3,5,2],[0,5,4,1],[4,5,4,1],[8,5,4,1]],
    'signal-grid': [[0,0,3,1],[3,0,3,1],[6,0,3,1],[9,0,3,1],[0,1,4,1],[4,1,4,1],[8,1,4,1]],
    'paper-studio': [[0,0,6,2],[6,0,6,2],[0,2,4,2],[4,2,4,2],[8,2,4,2],[0,4,6,1],[6,4,6,1]],
    'neon-stage': [[0,0,8,2],[8,0,4,2],[0,2,4,2],[4,2,8,2],[0,4,4,1],[4,4,4,1],[8,4,4,1]],
    'album-wall': [[0,0,4,3],[4,0,4,3],[8,0,4,3],[0,3,6,2],[6,3,6,2],[0,5,6,1],[6,5,6,1]],
    'mixer-desk': [[0,0,2,3],[2,0,2,3],[4,0,2,3],[6,0,2,3],[8,0,4,1],[8,1,4,1],[8,2,4,1]]
  };
  const theme = () => document.body.dataset.spsSkin || 'aurora-flow';
  const storageKey = () => `suno-workspace-grid-v1:${theme()}`;
  const grid = GridStack.init({column:12,cellHeight:72,margin:0,float:true,disableDrag:true,disableResize:true,resizable:{handles:'all'}}, gridElement);
  let editing = false;
  const applyLayout = (saved = true) => {
    let positions = layouts[theme()] || layouts['aurora-flow'];
    if (saved) {
      try { const value = JSON.parse(localStorage.getItem(storageKey()) || 'null'); if (Array.isArray(value) && value.length === cards.length) positions = value; } catch (_) {}
    }
    grid.batchUpdate();
    [...gridElement.children].forEach((item, index) => { const p = positions[index]; grid.update(item,{x:p[0],y:p[1],w:p[2],h:p[3]}); });
    grid.batchUpdate(false);
  };
  const setEditing = value => {
    editing = value; grid.enableMove(value); grid.enableResize(value);
    gridElement.classList.toggle('workspace-editing', value);
    toolbar.querySelector('[data-workspace-edit]').textContent = value ? 'Zaključaj raspored' : 'Uredi raspored';
    toolbar.querySelector('.workspace-state').textContent = value ? 'Prevucite panele ili promenite njihovu veličinu.' : 'Raspored je zaključan — sve funkcije rade jednim klikom.';
  };
  grid.on('change', (_event, items) => {
    if (!editing || !items) return;
    const saved = [...gridElement.children].map(item => { const n=item.gridstackNode; return [n.x,n.y,n.w,n.h]; });
    localStorage.setItem(storageKey(), JSON.stringify(saved));
  });
  toolbar.querySelector('[data-workspace-edit]').addEventListener('click', event => { event.stopPropagation(); setEditing(!editing); });
  toolbar.querySelector('[data-workspace-reset]').addEventListener('click', event => { event.stopPropagation(); localStorage.removeItem(storageKey()); applyLayout(false); });
  document.getElementById('modernSkinQuickSelect')?.addEventListener('change', () => { setEditing(false); requestAnimationFrame(() => applyLayout(true)); });
  applyLayout(true);
})();
