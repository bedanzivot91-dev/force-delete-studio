/* Preserve the old appearance/export controls without letting them dominate the
 * 2026 Settings page. The exact existing DOM panel is moved into a collapsed
 * compatibility section, so all original listeners and functions remain alive.
 */
(() => {
  'use strict';

  const css = document.createElement('style');
  css.id = 'spsModern2026CompatStyle';
  css.textContent = `
    body.sps-modern-2026 .modern-legacy-themes{margin-top:14px}
    body.sps-modern-2026 .modern-legacy-themes>summary{cursor:pointer;padding:12px 14px;border:1px solid var(--m-line);border-radius:14px;background:rgba(255,255,255,.025);color:#8fa1b8;font-size:11px;font-weight:800;letter-spacing:.05em}
    body.sps-modern-2026 .modern-legacy-themes[open]>summary{margin-bottom:10px}
    body.sps-modern-2026 .modern-legacy-themes .legacy-theme-settings{display:block!important;margin:0}
  `;
  document.head.appendChild(css);

  function install() {
    const settings = document.getElementById('view-settings');
    const legacy = settings?.querySelector('.legacy-theme-settings');
    if (!settings || !legacy || document.getElementById('modernLegacyThemes')) return;

    const details = document.createElement('details');
    details.id = 'modernLegacyThemes';
    details.className = 'modern-legacy-themes';
    details.innerHTML = '<summary>STARE TEME I IZVOZ PODEŠAVANJA — KOMPATIBILNOST</summary>';
    legacy.parentNode.insertBefore(details, legacy);
    details.appendChild(legacy);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', install, {once:true});
  else install();
})();
