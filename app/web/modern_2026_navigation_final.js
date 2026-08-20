/* Final 2026 navigation grouping.
 * Runs after the workspace shell and only MOVES existing navigation buttons.
 * No button is cloned or replaced, so all original listeners remain attached.
 */
(() => {
  'use strict';
  if (document.getElementById('spsFinalNav2026Marker')) return;
  const nav = document.querySelector('.sidebar nav');
  if (!nav) return;

  const marker = document.createElement('span');
  marker.id = 'spsFinalNav2026Marker';
  marker.hidden = true;
  document.body.appendChild(marker);

  const buttons = [...nav.querySelectorAll('.nav-item[data-view]')];
  const byView = new Map(buttons.map(button => [String(button.dataset.view || ''), button]));
  const extras = [...nav.children].filter(node =>
    !node.classList?.contains('sps-nav-section') &&
    !node.classList?.contains('nav-item')
  );

  // Detach original buttons before removing the old grouping wrappers.
  buttons.forEach(button => button.remove());
  nav.querySelectorAll(':scope > .sps-nav-section').forEach(section => section.remove());

  const groups = [
    ['SUNO I BIBLIOTEKA', ['import','library','download','folders','smart','versions','stats']],
    ['AUDIO I VIDEO', ['recognition','audio','production']],
    ['YOUTUBE I OBJAVA', ['release','tools']],
    ['SISTEM', ['logs','settings']],
  ];
  const used = new Set();
  const fragment = document.createDocumentFragment();

  for (const [title, views] of groups) {
    const section = document.createElement('div');
    section.className = 'sps-nav-section';
    const heading = document.createElement('div');
    heading.className = 'sps-nav-section-title';
    heading.textContent = title;
    section.appendChild(heading);
    for (const view of views) {
      const button = byView.get(view);
      if (!button) continue;
      section.appendChild(button);
      used.add(button);
    }
    if (section.querySelector('.nav-item')) fragment.appendChild(section);
  }

  const leftovers = buttons.filter(button => !used.has(button));
  if (leftovers.length) {
    const section = document.createElement('div');
    section.className = 'sps-nav-section';
    const heading = document.createElement('div');
    heading.className = 'sps-nav-section-title';
    heading.textContent = 'OSTALO';
    section.appendChild(heading);
    leftovers.forEach(button => section.appendChild(button));
    fragment.appendChild(section);
  }

  nav.prepend(fragment);
  extras.forEach(node => nav.appendChild(node));
})();
