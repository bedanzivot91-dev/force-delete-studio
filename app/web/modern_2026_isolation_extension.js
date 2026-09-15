/* Keep the 2026 skin visually deterministic even when an old theme was saved.
 * Legacy theme choice/export data is preserved, but historical full-theme CSS
 * must not leak structural rules (round stats, numbered nav, ON AIR pseudo
 * labels, tiny type, etc.) into the new 2026 interface.
 */
(() => {
  'use strict';
  if (window.SPSModern2026Isolation) return;

  const rememberLegacy = value => {
    const theme = String(value || 'default');
    document.body.dataset.spsLegacyTheme = theme;
    try { localStorage.setItem('sps-legacy-theme-selection', theme); } catch (_) {}
    return theme;
  };

  const forceModernBase = () => {
    const current = String(document.body.dataset.theme || 'default');
    if (current !== 'default') rememberLegacy(current);
    document.body.dataset.theme = 'default';
  };

  // app.js may have already restored an old saved theme before extensions load.
  forceModernBase();

  if (typeof applyTheme === 'function' && !applyTheme.__sps2026Wrapped) {
    const legacyApplyTheme = applyTheme;
    applyTheme = function applyLegacyThemeWithoutVisualLeak(themeId, persist = true) {
      legacyApplyTheme(themeId, persist);
      rememberLegacy(document.body.dataset.theme || themeId || 'default');
      document.body.dataset.theme = 'default';
    };
    applyTheme.__sps2026Wrapped = true;
  }

  const observer = new MutationObserver(mutations => {
    if (!mutations.some(item => item.attributeName === 'data-theme')) return;
    const current = String(document.body.dataset.theme || 'default');
    if (current !== 'default') {
      rememberLegacy(current);
      document.body.dataset.theme = 'default';
    }
  });
  observer.observe(document.body, {attributes:true, attributeFilter:['data-theme']});

  const css = document.createElement('style');
  css.id = 'spsModern2026IsolationStyle';
  css.textContent = `
    body.sps-modern-2026 .brand::after{content:none!important}
    body.sps-modern-2026 .nav-item::before{content:none!important}
    body.sps-modern-2026 .stat-card{aspect-ratio:auto!important;display:block!important;text-align:left!important;border-width:1px!important}
    body.sps-modern-2026 .stat-card span,body.sps-modern-2026 .stat-card strong{font-family:inherit!important}
  `;
  document.head.appendChild(css);

  window.SPSModern2026Isolation = {forceModernBase, rememberLegacy};
})();
