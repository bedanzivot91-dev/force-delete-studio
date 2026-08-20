/* Final layout cleanup: keep the primary workflow literal and move system
 * controls out of creative surfaces. Existing DOM nodes are moved, not copied,
 * so their original event listeners and IDs remain intact.
 */
(() => {
  'use strict';
  if ($('workflowCleanupMarker')) return;
  const marker=document.createElement('span');marker.id='workflowCleanupMarker';marker.hidden=true;document.body.appendChild(marker);

  const nav=$('nav');
  if(nav){
    const more=nav.querySelector('.nav-more');
    const title=nav.querySelector('.nav-group-title');
    const button=name=>nav.querySelector(`.nav-item[data-view="${name}"]`);
    const order=['import','library','audio','production','tools','settings'];
    const labels={
      import:'＋ SUNO — POVEŽI I SINHRONIZUJ',
      library:'▦ BIBLIOTEKA',
      audio:'✂ AUDIO',
      production:'⚡ VIDEO STUDIO',
      tools:'▶ YOUTUBE',
      settings:'⚙ PODEŠAVANJA',
    };
    if(title)title.textContent='GLAVNI TOK';
    for(const name of order){
      const node=button(name);if(!node)continue;
      node.textContent=labels[name];
      if(more)nav.insertBefore(node,more);else nav.appendChild(node);
    }
    if(more){
      more.querySelector('summary').textContent='DODATNI ALATI I IZVEŠTAJI';
      ['download','recognition','folders','smart','versions','release','stats','logs'].forEach(name=>{
        const node=button(name);if(node)more.appendChild(node);
      });
    }
  }

  // Keep page naming in sync with the new navigation.
  viewText.import=['Suno — poveži i sinhronizuj','Glavni ulaz za nalog i nove pesme. Dodatni načini uvoza su sklopljeni ispod.'];
  viewText.production=['Video Studio','Jedna radna površina: audio, timeline, titlovi, pregled i render.'];
  viewText.tools=['YouTube','Kanali, Suno ↔ YouTube audio provera, rezultati i YouTube analitika.'];

  const production=$('view-production');
  const settings=$('view-settings');
  if(production&&settings){
    let systemDetails=$('cleanSystemSettings');
    if(!systemDetails){
      systemDetails=document.createElement('details');
      systemDetails.id='cleanSystemSettings';
      systemDetails.className='organized-maintenance';
      systemDetails.innerHTML='<summary>SISTEM, DIJAGNOSTIKA I NAPREDNI MODULI</summary><div class="organized-maintenance-body"><p class="muted">Ovo su podešavanja programa. Namerno nisu u Video Studiju.</p><div id="cleanSystemSettingsBody"></div></div>';
      settings.appendChild(systemDetails);
    }
    const body=$('cleanSystemSettingsBody');
    const hero=production.querySelector('.production-hero');
    const status=$('v3StatusBox');
    if(hero){
      const h=hero.querySelector('h2');if(h)h.textContent='Provera računara i produkcionih komponenti';
      const p=hero.querySelector('p');if(p)p.textContent='FFmpeg, zaštita, integritet i komponente programa. Ovaj panel je premešten u Podešavanja.';
      body.appendChild(hero);
    }
    if(status)body.appendChild(status);

    // The old YouTube page had a generic "Napredni sistemi" block containing
    // backups, AI plugins, relocation, update settings and job queue. Those are
    // application settings, not YouTube results.
    const advanced=document.querySelector('#view-tools .advanced-systems-panel');
    if(advanced)body.appendChild(advanced);
  }

  // Video Studio should open on the editor, not on legacy maintenance output.
  if(production){
    const workspace=$('productionWorkspace');
    const ribbon=$('organizedWorkflowRibbon');
    if(workspace&&ribbon&&workspace.previousElementSibling!==ribbon){
      ribbon.insertAdjacentElement('afterend',workspace);
    }
    const output=$('v3Output')?.closest('.panel');
    if(output){
      const heading=output.querySelector('h2');if(heading)heading.textContent='Studio rezultat i napravljeni fajlovi';
    }
  }
})();
