/* Standalone Suno-only guard. Video/NP production UI must never enter the app. */
(() => {
  'use strict';
  if (document.getElementById('sunoOnlyRuntimeMarker')) return;
  const marker = document.createElement('meta');
  marker.id = 'sunoOnlyRuntimeMarker';
  marker.name = 'application-scope';
  marker.content = 'suno-only';
  document.head.appendChild(marker);

  const productionButton = document.querySelector('.nav-item[data-view="production"]');
  const productionView = document.getElementById('view-production');
  if (productionButton?.classList.contains('active')) {
    document.querySelector('.nav-item[data-view="library"]')?.click();
  }
  productionButton?.remove();
  productionView?.remove();

  document.querySelectorAll('.sps-nav-section').forEach(section => {
    if (!section.querySelector('.nav-item[data-view]')) section.remove();
  });
  const badge = document.getElementById('modernTopBadge');
  if (badge) badge.innerHTML = '<i></i> SUNO DESKTOP';
  document.body.dataset.applicationScope = 'suno-only';
})();
