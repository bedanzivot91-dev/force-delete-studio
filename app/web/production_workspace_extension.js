/* Real production workspace for Video i objava.
 *
 * This file is appended to app.js by server.py, therefore it intentionally
 * shares app.js' lexical scope (state/api/$/toast/escapeHtml/etc.).  It does
 * not replace the existing v3 tools; it adds the missing editor surface above
 * them and uses the same saved subtitle/render APIs already used elsewhere.
 */
(() => {
  'use strict';

  const host = $('view-production');
  if (!host || $('productionWorkspace')) return;

  const ws = {
    song: null,
    cues: [],
    duration: 0,
    peaks: [],
    pixelsPerSecond: 12,
    activeCue: -1,
    drag: null,
    history: [],
    future: [],
    dirty: false,
    background: '',
  };

  const css = document.createElement('style');
  css.textContent = `
    #productionWorkspace{margin:0 0 18px;border:1px solid #334155;overflow:hidden}
    .pws-head{display:flex;gap:14px;align-items:center;justify-content:space-between;padding:14px 16px;border-bottom:1px solid #263244;background:linear-gradient(135deg,rgba(59,130,246,.12),rgba(139,92,246,.10))}
    .pws-head h2{margin:0 0 4px;font-size:19px}.pws-head p{margin:0}
    .pws-song{display:flex;align-items:center;gap:10px;min-width:0}.pws-song-cover{width:48px;height:48px;border-radius:8px;object-fit:cover;background:#0b0f14;border:1px solid #334155}.pws-song-meta{min-width:0}.pws-song-title{display:block;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:470px}.pws-song-sub{display:block;color:#93a4bd;font-size:12px;margin-top:3px}
    .pws-body{display:grid;grid-template-columns:minmax(360px,1.05fr) minmax(300px,.95fr);gap:12px;padding:12px;background:#0b0f14}
    .pws-panel{border:1px solid #263244;background:#10161e;border-radius:8px;padding:12px;min-width:0}
    .pws-preview-shell{display:flex;justify-content:center;align-items:center;min-height:420px;background:#070a0f;border-radius:7px;overflow:hidden;position:relative}
    .pws-preview{position:relative;width:min(100%,680px);aspect-ratio:16/9;background:radial-gradient(circle at 50% 35%,#1f2937,#070a0f);overflow:hidden;box-shadow:0 12px 40px rgba(0,0,0,.35)}
    .pws-preview.vertical{width:min(56%,350px);aspect-ratio:9/16}
    .pws-preview-bg{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;opacity:.72}.pws-preview-shade{position:absolute;inset:0;background:linear-gradient(180deg,rgba(0,0,0,.12),rgba(0,0,0,.28) 55%,rgba(0,0,0,.55))}
    .pws-preview-text{position:absolute;left:7%;right:7%;bottom:10%;text-align:center;font-weight:700;line-height:1.18;white-space:pre-wrap;overflow-wrap:anywhere;text-shadow:0 2px 5px #000,0 0 2px #000;transition:opacity .08s}
    .pws-preview-wave{position:absolute;left:8%;right:8%;bottom:4%;height:24px;display:flex;align-items:center;gap:2px;opacity:.7}.pws-preview-wave i{display:block;flex:1;min-width:1px;background:currentColor;border-radius:2px}
    .pws-transport{display:grid;grid-template-columns:auto auto auto 1fr auto;gap:8px;align-items:center;margin-top:10px}.pws-transport input[type=range]{width:100%}.pws-time{font-variant-numeric:tabular-nums;color:#cbd5e1;min-width:110px;text-align:right}
    .pws-controls{display:grid;grid-template-columns:1fr 1fr;gap:10px}.pws-controls label{display:flex;flex-direction:column;gap:5px;color:#aebed2;font-size:12px}.pws-controls input,.pws-controls select{width:100%}.pws-span2{grid-column:1/-1}.pws-color-row{display:grid;grid-template-columns:1fr 1fr;gap:10px}.pws-color-row input[type=color]{height:38px;padding:3px}
    .pws-status{margin-top:10px;padding:8px 10px;border:1px solid #263244;border-radius:6px;color:#9fb0c7;font-size:12px}.pws-status.dirty{border-color:#9a6a12;color:#fbbf24}.pws-status.saved{border-color:#147d57;color:#6ee7b7}
    .pws-timeline-panel{margin:0 12px 12px;border:1px solid #263244;background:#0d131a;border-radius:8px;overflow:hidden}
    .pws-timeline-toolbar{display:flex;flex-wrap:wrap;gap:8px;align-items:center;padding:10px 12px;border-bottom:1px solid #263244}.pws-timeline-toolbar label{display:flex;gap:6px;align-items:center;font-size:12px;color:#aebed2}.pws-timeline-toolbar input[type=range]{width:130px}
    .pws-scroll{overflow:auto;max-height:360px;background:#080c11}.pws-timeline{position:relative;min-height:230px;user-select:none}
    .pws-ruler{position:relative;height:32px;border-bottom:1px solid #334155;background:#0d131a}.pws-tick{position:absolute;top:0;height:100%;border-left:1px solid #273444;font-size:10px;color:#8292a8;padding-left:3px}.pws-tick.major{border-left-color:#526274;color:#cbd5e1}
    .pws-track{position:relative;height:72px;border-bottom:1px solid #202b38}.pws-track-label{position:sticky;left:0;z-index:6;display:inline-flex;align-items:center;height:22px;padding:0 7px;background:#111923;border-right:1px solid #334155;color:#9fb0c7;font-size:10px;text-transform:uppercase;letter-spacing:.06em}
    .pws-wave-canvas{position:absolute;left:0;top:23px;height:46px;width:100%;opacity:.9}
    .pws-cue{position:absolute;top:29px;height:34px;min-width:8px;border:1px solid #3b82f6;background:rgba(37,99,235,.28);border-radius:5px;overflow:visible;cursor:grab;color:#e6f0ff;font-size:11px;line-height:32px;padding:0 8px;white-space:nowrap;text-overflow:ellipsis;z-index:3}.pws-cue.active{border-color:#fbbf24;background:rgba(217,119,6,.34);z-index:4}.pws-cue.dragging{cursor:grabbing;box-shadow:0 0 0 2px rgba(96,165,250,.25)}
    .pws-handle{position:absolute;top:-1px;width:7px;height:34px;background:rgba(255,255,255,.15);cursor:ew-resize}.pws-handle.left{left:-1px;border-radius:5px 0 0 5px}.pws-handle.right{right:-1px;border-radius:0 5px 5px 0}
    .pws-playhead{position:absolute;top:0;bottom:0;width:2px;background:#ef4444;z-index:8;pointer-events:none}.pws-playhead:before{content:'';position:absolute;top:0;left:-4px;border-left:5px solid transparent;border-right:5px solid transparent;border-top:7px solid #ef4444}
    .pws-editor{display:grid;grid-template-columns:70px 70px minmax(180px,1fr) 34px;gap:6px;align-items:center;padding:6px 8px;border-bottom:1px solid #202b38}.pws-editor.active{background:rgba(245,158,11,.08)}.pws-editor input{min-width:0}.pws-editor button{height:32px}.pws-editor-head{position:sticky;top:0;z-index:5;background:#111923;color:#91a3bb;font-size:10px;text-transform:uppercase}.pws-empty{padding:24px;text-align:center;color:#8495aa}
    @media (max-width:1050px){.pws-body{grid-template-columns:1fr}.pws-preview-shell{min-height:330px}.pws-song-title{max-width:280px}}
  `;
  document.head.appendChild(css);

  const panel = document.createElement('section');
  panel.id = 'productionWorkspace';
  panel.className = 'panel';
  panel.innerHTML = `
    <div class="pws-head">
      <div>
        <h2>Video radna površina — timeline i titlovi</h2>
        <p class="muted">Pravi pregled, pomeraj i razvlači titlove na vremenskoj liniji, pregledaj kadar uz audio i renderuj isti projekat.</p>
      </div>
      <div class="button-row">
        <button id="pwsLoadSong" class="btn primary">UČITAJ IZABRANU PESMU</button>
        <button id="pwsSave" class="btn success">SAČUVAJ TITLOVE</button>
        <button id="pwsRender" class="btn danger">RENDERUJ VIDEO</button>
      </div>
    </div>
    <div class="pws-body">
      <div class="pws-panel">
        <div class="pws-song">
          <img id="pwsSongCover" class="pws-song-cover" alt="">
          <div class="pws-song-meta"><strong id="pwsSongTitle" class="pws-song-title">Nijedna pesma nije učitana</strong><span id="pwsSongSub" class="pws-song-sub">Izaberi pesmu u Biblioteci, pa klikni „Učitaj izabranu pesmu“.</span></div>
        </div>
        <div class="pws-preview-shell" style="margin-top:10px">
          <div id="pwsPreview" class="pws-preview">
            <img id="pwsPreviewBg" class="pws-preview-bg" alt="">
            <div class="pws-preview-shade"></div>
            <div id="pwsPreviewText" class="pws-preview-text">Pregled titla</div>
            <div id="pwsPreviewWave" class="pws-preview-wave hidden"></div>
          </div>
        </div>
        <div class="pws-transport">
          <button id="pwsPlay" class="btn primary small">▶</button>
          <button id="pwsStop" class="btn secondary small">■</button>
          <button id="pwsPrevCue" class="btn ghost small">← TITL</button>
          <input id="pwsSeek" type="range" min="0" max="1000" value="0" step="1">
          <span id="pwsTime" class="pws-time">0:00 / 0:00</span>
        </div>
        <audio id="pwsAudio" preload="metadata"></audio>
      </div>
      <div class="pws-panel">
        <h3 style="margin-top:0">Izgled videa</h3>
        <div class="pws-controls">
          <label>Format<select id="pwsAspect"><option value="16:9">YouTube 16:9</option><option value="9:16">Shorts / TikTok / Reels 9:16</option></select></label>
          <label>Font<input id="pwsFont" value="Arial"></label>
          <label>Veličina teksta<input id="pwsFontSize" type="number" min="24" max="140" value="54"></label>
          <label>Prikaz talasa u renderu<select id="pwsRenderWave"><option value="0">Ne</option><option value="1">Da</option></select></label>
          <div class="pws-color-row pws-span2">
            <label>Boja teksta<input id="pwsTextColor" type="color" value="#ffffff"></label>
            <label>Boja ivice<input id="pwsOutlineColor" type="color" value="#000000"></label>
          </div>
          <label class="pws-span2">Pozadina — slika ili video<div class="folder-path-row"><input id="pwsBackground" placeholder="Opciono — koristi omot ako je prazno"><button id="pwsChooseBackground" class="btn ghost small">Izaberi</button></div></label>
          <label>Zoom timeline<input id="pwsZoom" type="range" min="5" max="32" value="12" step="1"></label>
          <label>Snap<select id="pwsSnap"><option value="0.01">0.01 s</option><option value="0.05">0.05 s</option><option value="0.1" selected>0.10 s</option><option value="0.5">0.50 s</option></select></label>
        </div>
        <div class="button-row" style="margin-top:12px">
          <button id="pwsUndo" class="btn ghost small">↶ UNDO</button><button id="pwsRedo" class="btn ghost small">↷ REDO</button>
          <button id="pwsAddCue" class="btn secondary small">+ DODAJ TITL</button>
          <button id="pwsStartHere" class="btn ghost small">START = PLEJHED</button>
          <button id="pwsEndHere" class="btn ghost small">KRAJ = PLEJHED</button>
        </div>
        <div id="pwsStatus" class="pws-status">Radna površina je spremna. Učitaj pesmu.</div>
        <p class="fine-print" style="margin-bottom:0">Promene na timeline-u nisu lažni preview: dugme „Sačuvaj titlove“ upisuje iste LRC/SRT cue podatke koje renderer koristi. Render koristi format, font, veličinu, boje, pozadinu i opcioni waveform iz ove radne površine.</p>
      </div>
    </div>
    <div class="pws-timeline-panel">
      <div class="pws-timeline-toolbar">
        <strong>Timeline</strong><span id="pwsCueCount" class="muted">0 titlova</span><span class="muted">Prevuci blok = pomeri · ručice levo/desno = promeni trajanje · klik = seek</span>
      </div>
      <div id="pwsScroll" class="pws-scroll"><div id="pwsTimeline" class="pws-timeline"></div></div>
      <div id="pwsCueEditors"></div>
    </div>
  `;

  const statusBox = $('v3StatusBox');
  if (statusBox && statusBox.parentNode === host) statusBox.insertAdjacentElement('afterend', panel);
  else host.prepend(panel);

  function cloneCues(cues = ws.cues) {
    return cues.map(c => ({ start: Number(c.start ?? c.start_s ?? 0), end: Number(c.end ?? c.end_s ?? 0), text: String(c.text || '') }));
  }

  function setStatus(text, kind = '') {
    const box = $('pwsStatus');
    box.textContent = text;
    box.className = `pws-status ${kind}`.trim();
  }

  function markDirty(message = 'Postoje nesačuvane promene na titlovima.') {
    ws.dirty = true;
    setStatus(message, 'dirty');
  }

  function snapshot() {
    ws.history.push(cloneCues());
    if (ws.history.length > 60) ws.history.shift();
    ws.future = [];
  }

  function restore(cues, message) {
    ws.cues = cloneCues(cues);
    ws.activeCue = Math.min(ws.activeCue, ws.cues.length - 1);
    renderTimeline();
    renderCueEditors();
    markDirty(message);
  }

  function snapTime(value) {
    const snap = Math.max(.01, Number($('pwsSnap')?.value || .1));
    return Math.max(0, Math.round(Number(value || 0) / snap) * snap);
  }

  function assColor(hex, fallback) {
    const raw = String(hex || fallback || '').trim();
    if (/^&H[0-9a-f]{8}$/i.test(raw)) return raw.toUpperCase();
    const match = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(raw);
    if (!match) return fallback;
    return `&H00${match[3]}${match[2]}${match[1]}`.toUpperCase();
  }

  function saveProjectSettings() {
    if (!ws.song) return;
    const payload = {
      aspect: $('pwsAspect').value,
      font: $('pwsFont').value,
      fontSize: Number($('pwsFontSize').value || 54),
      textColor: $('pwsTextColor').value,
      outlineColor: $('pwsOutlineColor').value,
      waveform: $('pwsRenderWave').value,
      background: $('pwsBackground').value,
      zoom: Number($('pwsZoom').value || 12),
      snap: $('pwsSnap').value,
    };
    try { localStorage.setItem(`sps-pws:${ws.song.id}`, JSON.stringify(payload)); } catch (_) {}
  }

  function loadProjectSettings() {
    if (!ws.song) return;
    let saved = null;
    try { saved = JSON.parse(localStorage.getItem(`sps-pws:${ws.song.id}`) || 'null'); } catch (_) {}
    if (!saved) return;
    if (saved.aspect) $('pwsAspect').value = saved.aspect;
    if (saved.font) $('pwsFont').value = saved.font;
    if (saved.fontSize) $('pwsFontSize').value = String(saved.fontSize);
    if (saved.textColor) $('pwsTextColor').value = saved.textColor;
    if (saved.outlineColor) $('pwsOutlineColor').value = saved.outlineColor;
    if (saved.waveform !== undefined) $('pwsRenderWave').value = String(saved.waveform);
    if (saved.background) $('pwsBackground').value = saved.background;
    if (saved.zoom) $('pwsZoom').value = String(saved.zoom);
    if (saved.snap) $('pwsSnap').value = String(saved.snap);
  }

  function updatePreviewStyle() {
    const preview = $('pwsPreview');
    const vertical = $('pwsAspect').value === '9:16';
    preview.classList.toggle('vertical', vertical);
    const text = $('pwsPreviewText');
    text.style.fontFamily = $('pwsFont').value || 'Arial';
    const requested = Number($('pwsFontSize').value || (vertical ? 64 : 54));
    text.style.fontSize = `${Math.max(18, requested * (vertical ? .55 : .48))}px`;
    text.style.color = $('pwsTextColor').value || '#fff';
    const outline = $('pwsOutlineColor').value || '#000';
    text.style.webkitTextStroke = `${Math.max(1, requested / 28)}px ${outline}`;
    $('pwsPreviewWave').classList.toggle('hidden', $('pwsRenderWave').value !== '1');
    saveProjectSettings();
  }

  function setPreviewBackground() {
    if (!ws.song) return;
    const img = $('pwsPreviewBg');
    const raw = $('pwsBackground').value.trim();
    ws.background = raw;
    const fallback = ws.song.image_url || (ws.song.local_cover ? `/media?path=${encodeURIComponent(ws.song.local_cover)}` : '');
    if (!raw) {
      img.src = fallback;
      return;
    }
    const lower = raw.toLowerCase();
    if (/\.(png|jpe?g|webp|bmp)$/i.test(lower)) {
      img.src = `/media?path=${encodeURIComponent(raw)}`;
      img.onerror = () => { img.onerror = null; img.src = fallback; setStatus('Pozadina je izabrana za render, ali browser preview ne može da pročita tu putanju. Render je i dalje može koristiti.', 'dirty'); };
    } else {
      img.src = fallback;
      setStatus('Video pozadina je izabrana. U preview-u se prikazuje omot; pravi video fajl će biti korišćen pri renderu.', ws.dirty ? 'dirty' : '');
    }
    saveProjectSettings();
  }

  function waveformBars() {
    const wrap = $('pwsPreviewWave');
    const source = ws.peaks.length ? ws.peaks : Array.from({ length: 38 }, (_, i) => [-.15 - (i % 5) * .05, .15 + (i % 7) * .04]);
    const count = 42;
    wrap.innerHTML = '';
    for (let i = 0; i < count; i += 1) {
      const pair = source[Math.floor(i / count * source.length)] || [0, 0];
      const h = Math.max(2, Math.min(22, Math.abs(Number(pair[1] || 0) - Number(pair[0] || 0)) * 24));
      const bar = document.createElement('i');
      bar.style.height = `${h}px`;
      wrap.appendChild(bar);
    }
  }

  async function loadWorkspaceSong() {
    const id = selectedIds()[0] || state.currentSong?.id || state.songs?.[0]?.id || '';
    if (!id) return toast('Izaberi pesmu u Biblioteci pre otvaranja radne površine.', 'warning', 9000);
    try {
      setStatus('Učitavam pesmu, titlove i waveform...');
      const [songData, subtitleData] = await Promise.all([
        api(`/api/song?id=${encodeURIComponent(id)}`, { timeoutMs: 30000 }),
        api(`/api/subtitles?id=${encodeURIComponent(id)}`, { timeoutMs: 30000 }),
      ]);
      ws.song = songData.song;
      ws.duration = Math.max(.01, Number(ws.song.duration || 0));
      ws.cues = cloneCues(subtitleData.cues || []);
      ws.history = [];
      ws.future = [];
      ws.activeCue = ws.cues.length ? 0 : -1;
      ws.dirty = false;
      loadProjectSettings();
      ws.pixelsPerSecond = Number($('pwsZoom').value || 12);
      $('pwsSongTitle').textContent = ws.song.title || 'Bez naslova';
      $('pwsSongSub').textContent = `${formatDuration(ws.duration)} · ${ws.cues.length} titlova · ${songHasLocalAudio(ws.song) ? 'lokalni audio' : 'Suno streaming'}`;
      $('pwsSongCover').src = ws.song.local_cover ? `/media?path=${encodeURIComponent(ws.song.local_cover)}` : (ws.song.image_url || '');
      $('pwsAudio').src = `/api/song/audio?id=${encodeURIComponent(ws.song.id)}`;
      $('pwsAudio').load();
      setPreviewBackground();
      updatePreviewStyle();
      renderTimeline();
      renderCueEditors();
      updatePreviewAt(0);
      setStatus(`Učitano: ${ws.song.title || ws.song.id}. Timeline je spreman.`, 'saved');
      try {
        const width = Math.max(800, Math.min(2600, Math.round(ws.duration * Math.min(ws.pixelsPerSecond, 14))));
        const wf = await api(`/api/audio/waveform?id=${encodeURIComponent(ws.song.id)}&width=${width}`, { timeoutMs: 120000 });
        ws.peaks = wf.waveform?.peaks || [];
        renderTimeline();
        waveformBars();
      } catch (waveError) {
        ws.peaks = [];
        waveformBars();
        setStatus(`Pesma i titlovi su učitani. Waveform nije napravljen: ${waveError.message}`, 'dirty');
      }
    } catch (error) {
      setStatus(`Radna površina nije učitana: ${error.message}`);
      toast(error.message, 'error', 12000);
    }
  }

  function cueAt(time) {
    const t = Number(time || 0);
    let best = -1;
    for (let i = 0; i < ws.cues.length; i += 1) {
      const cue = ws.cues[i];
      if (t >= Number(cue.start) && t <= Number(cue.end)) { best = i; break; }
    }
    return best;
  }

  function updatePreviewAt(time) {
    const duration = Math.max(.01, ws.duration);
    const t = Math.max(0, Math.min(duration, Number(time || 0)));
    const index = cueAt(t);
    ws.activeCue = index;
    $('pwsPreviewText').textContent = index >= 0 ? ws.cues[index].text : '';
    $('pwsTime').textContent = `${formatDuration(t)} / ${formatDuration(duration)}`;
    $('pwsSeek').value = String(Math.round(t / duration * 1000));
    const head = $('pwsPlayhead');
    if (head) head.style.left = `${t * ws.pixelsPerSecond}px`;
    qsa('.pws-cue', $('pwsTimeline')).forEach((el, i) => el.classList.toggle('active', i === index));
    qsa('.pws-editor', $('pwsCueEditors')).forEach((el, i) => el.classList.toggle('active', i === index));
  }

  function drawWaveCanvas(canvas) {
    const ctx = canvas.getContext('2d');
    const rectW = Math.max(1, Math.round(ws.duration * ws.pixelsPerSecond));
    const w = Math.max(1, Math.min(4096, rectW));
    canvas.width = w;
    canvas.height = 46;
    ctx.clearRect(0, 0, w, 46);
    ctx.fillStyle = '#0a1119';
    ctx.fillRect(0, 0, w, 46);
    if (!ws.peaks.length) {
      ctx.strokeStyle = '#334155'; ctx.beginPath(); ctx.moveTo(0,23); ctx.lineTo(w,23); ctx.stroke(); return;
    }
    ctx.strokeStyle = '#60a5fa'; ctx.lineWidth = 1; ctx.beginPath();
    for (let x = 0; x < w; x += 1) {
      const pair = ws.peaks[Math.min(ws.peaks.length - 1, Math.floor(x / w * ws.peaks.length))] || [0,0];
      const min = Number(pair[0] || 0), max = Number(pair[1] || 0);
      ctx.moveTo(x, 23 + min * 21); ctx.lineTo(x, 23 + max * 21);
    }
    ctx.stroke();
  }

  function renderTimeline() {
    const timeline = $('pwsTimeline');
    if (!timeline) return;
    ws.pixelsPerSecond = Number($('pwsZoom')?.value || ws.pixelsPerSecond || 12);
    const duration = Math.max(1, ws.duration || 1);
    const width = Math.max(900, Math.ceil(duration * ws.pixelsPerSecond));
    timeline.style.width = `${width}px`;
    const every = duration > 900 ? 30 : duration > 360 ? 10 : 5;
    const major = every * 2;
    let ticks = '';
    for (let t = 0; t <= duration; t += every) {
      ticks += `<span class="pws-tick ${t % major === 0 ? 'major' : ''}" style="left:${t * ws.pixelsPerSecond}px">${t % major === 0 ? escapeHtml(formatDuration(t)) : ''}</span>`;
    }
    const cueBlocks = ws.cues.map((cue, index) => {
      const left = Math.max(0, Number(cue.start) * ws.pixelsPerSecond);
      const widthPx = Math.max(8, (Math.max(Number(cue.start) + .05, Number(cue.end)) - Number(cue.start)) * ws.pixelsPerSecond);
      return `<div class="pws-cue ${index === ws.activeCue ? 'active' : ''}" data-index="${index}" style="left:${left}px;width:${widthPx}px" title="${escapeHtml(cue.text)}"><span class="pws-handle left" data-handle="start"></span>${escapeHtml(cue.text)}<span class="pws-handle right" data-handle="end"></span></div>`;
    }).join('');
    timeline.innerHTML = `<div class="pws-ruler">${ticks}</div><div class="pws-track"><span class="pws-track-label">AUDIO</span><canvas id="pwsWaveCanvas" class="pws-wave-canvas"></canvas></div><div class="pws-track" id="pwsSubtitleTrack"><span class="pws-track-label">TITLOVI</span>${cueBlocks}</div><div id="pwsPlayhead" class="pws-playhead"></div>`;
    drawWaveCanvas($('pwsWaveCanvas'));
    $('pwsCueCount').textContent = `${ws.cues.length} titlova`;
    updatePreviewAt($('pwsAudio').currentTime || 0);
  }

  function renderCueEditors() {
    const box = $('pwsCueEditors');
    if (!ws.cues.length) {
      box.innerHTML = '<div class="pws-empty">Nema sačuvanih titlova. Klikni „+ Dodaj titl“ ili prvo napravi/uvezi LRC/SRT.</div>';
      return;
    }
    box.innerHTML = `<div class="pws-editor pws-editor-head"><span>Početak</span><span>Kraj</span><span>Tekst</span><span></span></div>` + ws.cues.map((cue, index) => `
      <div class="pws-editor ${index === ws.activeCue ? 'active' : ''}" data-index="${index}">
        <input class="pws-start" type="number" min="0" step="0.01" value="${Number(cue.start).toFixed(2)}">
        <input class="pws-end" type="number" min="0" step="0.01" value="${Number(cue.end).toFixed(2)}">
        <input class="pws-text" value="${escapeHtml(cue.text)}">
        <button class="btn danger small pws-delete" title="Obriši titl">×</button>
      </div>`).join('');
  }

  function setCue(index, patch, { history = true, rerender = true } = {}) {
    if (index < 0 || index >= ws.cues.length) return;
    if (history) snapshot();
    const old = ws.cues[index];
    let start = patch.start === undefined ? Number(old.start) : snapTime(patch.start);
    let end = patch.end === undefined ? Number(old.end) : snapTime(patch.end);
    start = Math.max(0, Math.min(start, Math.max(0, ws.duration - .05)));
    end = Math.max(start + .05, Math.min(end, Math.max(start + .05, ws.duration || end)));
    ws.cues[index] = { start, end, text: patch.text === undefined ? old.text : String(patch.text) };
    ws.activeCue = index;
    markDirty();
    if (rerender) { renderTimeline(); renderCueEditors(); }
  }

  async function saveCues() {
    if (!ws.song) return toast('Prvo učitaj pesmu.', 'warning');
    try {
      const clean = cloneCues().filter(c => c.text.trim() && c.end > c.start).sort((a,b) => a.start - b.start);
      const result = await api('/api/subtitles/save', { method:'POST', body:{ id:ws.song.id, cues:clean }, timeoutMs:120000 });
      ws.cues = cloneCues(result.cues || clean);
      ws.dirty = false;
      ws.history = [];
      ws.future = [];
      renderTimeline(); renderCueEditors();
      setStatus(`Sačuvano ${ws.cues.length} titlova. Renderer sada koristi ove tajminge.`, 'saved');
      toast('Titlovi i tajming su sačuvani.', 'success');
    } catch (error) {
      setStatus(`Čuvanje nije uspelo: ${error.message}`, 'dirty');
      toast(error.message, 'error', 12000);
    }
  }

  async function renderVideo() {
    if (!ws.song) return toast('Prvo učitaj pesmu.', 'warning');
    if (ws.dirty) {
      const ok = confirm('Imaš nesačuvane promene titlova. Sačuvati ih pre rendera?');
      if (!ok) return;
      await saveCues();
      if (ws.dirty) return;
    }
    const body = {
      id: ws.song.id,
      aspect: $('pwsAspect').value,
      background: $('pwsBackground').value.trim(),
      font: $('pwsFont').value.trim() || 'Arial',
      font_size: Number($('pwsFontSize').value || 54),
      text_color: assColor($('pwsTextColor').value, '&H00FFFFFF'),
      outline_color: assColor($('pwsOutlineColor').value, '&H00000000'),
      waveform: $('pwsRenderWave').value === '1',
    };
    saveProjectSettings();
    try {
      const d = await api('/api/v3/lyric-video', { method:'POST', body });
      if (d.task?.id) { state.taskId = d.task.id; $('taskPanel').classList.remove('hidden'); updateTask(d.task); pollTask(); }
      setStatus('Render je pokrenut. Prati stvarni napredak iznad radne površine.', 'saved');
      toast('Render lyric videa je pokrenut.', 'success');
    } catch (error) {
      setStatus(`Render nije pokrenut: ${error.message}`, 'dirty');
      toast(error.message, 'error', 12000);
    }
  }

  function seekTo(seconds, play = false) {
    if (!ws.song) return;
    const audio = $('pwsAudio');
    audio.currentTime = Math.max(0, Math.min(ws.duration, Number(seconds || 0)));
    updatePreviewAt(audio.currentTime);
    if (play) audio.play().catch(() => {});
  }

  $('pwsLoadSong').addEventListener('click', loadWorkspaceSong);
  $('pwsSave').addEventListener('click', saveCues);
  $('pwsRender').addEventListener('click', renderVideo);
  $('pwsPlay').addEventListener('click', () => {
    if (!ws.song) return toast('Prvo učitaj pesmu.', 'warning');
    const audio = $('pwsAudio');
    if (audio.paused) audio.play().catch(e => toast(`Audio nije pokrenut: ${e.message}`, 'error'));
    else audio.pause();
  });
  $('pwsStop').addEventListener('click', () => { const a=$('pwsAudio'); a.pause(); seekTo(0); });
  $('pwsPrevCue').addEventListener('click', () => {
    if (!ws.cues.length) return;
    const t = $('pwsAudio').currentTime;
    let target = ws.cues[0];
    for (const cue of ws.cues) { if (cue.start < t - .05) target = cue; else break; }
    seekTo(target.start, true);
  });
  $('pwsSeek').addEventListener('input', e => seekTo(Number(e.target.value || 0) / 1000 * ws.duration));
  $('pwsAudio').addEventListener('timeupdate', e => updatePreviewAt(e.currentTarget.currentTime));
  $('pwsAudio').addEventListener('play', () => { $('pwsPlay').textContent = '❚❚'; });
  $('pwsAudio').addEventListener('pause', () => { $('pwsPlay').textContent = '▶'; });
  $('pwsAudio').addEventListener('loadedmetadata', e => {
    if ((!ws.duration || ws.duration <= .01) && Number.isFinite(e.currentTarget.duration)) {
      ws.duration = e.currentTarget.duration; renderTimeline();
    }
  });

  ['pwsAspect','pwsFont','pwsFontSize','pwsTextColor','pwsOutlineColor','pwsRenderWave'].forEach(id => $(id).addEventListener('input', updatePreviewStyle));
  $('pwsBackground').addEventListener('change', setPreviewBackground);
  $('pwsChooseBackground').addEventListener('click', async () => {
    const path = await chooseV3File('pwsBackground');
    if (path) { setPreviewBackground(); saveProjectSettings(); }
  });
  $('pwsZoom').addEventListener('input', () => { renderTimeline(); saveProjectSettings(); });
  $('pwsSnap').addEventListener('change', saveProjectSettings);

  $('pwsAddCue').addEventListener('click', () => {
    if (!ws.song) return toast('Prvo učitaj pesmu.', 'warning');
    snapshot();
    const start = snapTime($('pwsAudio').currentTime || 0);
    ws.cues.push({ start, end: Math.min(ws.duration, start + 3), text: 'Novi titl' });
    ws.cues.sort((a,b) => a.start - b.start);
    ws.activeCue = ws.cues.findIndex(c => c.start === start && c.text === 'Novi titl');
    markDirty('Dodat je novi titl.'); renderTimeline(); renderCueEditors();
  });
  $('pwsStartHere').addEventListener('click', () => { if (ws.activeCue >= 0) setCue(ws.activeCue, { start:$('pwsAudio').currentTime }); });
  $('pwsEndHere').addEventListener('click', () => { if (ws.activeCue >= 0) setCue(ws.activeCue, { end:$('pwsAudio').currentTime }); });
  $('pwsUndo').addEventListener('click', () => {
    if (!ws.history.length) return;
    ws.future.push(cloneCues());
    const previous = ws.history.pop();
    restore(previous, 'Undo: vraćena je prethodna izmena.');
  });
  $('pwsRedo').addEventListener('click', () => {
    if (!ws.future.length) return;
    ws.history.push(cloneCues());
    const next = ws.future.pop();
    restore(next, 'Redo: izmena je ponovo primenjena.');
  });

  $('pwsCueEditors').addEventListener('focusin', e => {
    const row = e.target.closest('.pws-editor[data-index]');
    if (!row) return;
    ws.activeCue = Number(row.dataset.index);
    const cue = ws.cues[ws.activeCue];
    if (cue) seekTo(cue.start);
  });
  $('pwsCueEditors').addEventListener('change', e => {
    const row = e.target.closest('.pws-editor[data-index]');
    if (!row) return;
    const index = Number(row.dataset.index);
    if (e.target.classList.contains('pws-start')) setCue(index, { start:Number(e.target.value) });
    else if (e.target.classList.contains('pws-end')) setCue(index, { end:Number(e.target.value) });
    else if (e.target.classList.contains('pws-text')) setCue(index, { text:e.target.value });
  });
  $('pwsCueEditors').addEventListener('click', e => {
    const button = e.target.closest('.pws-delete');
    if (!button) return;
    const row = button.closest('.pws-editor[data-index]');
    if (!row) return;
    snapshot();
    ws.cues.splice(Number(row.dataset.index), 1);
    ws.activeCue = Math.min(ws.activeCue, ws.cues.length - 1);
    markDirty('Titl je obrisan.'); renderTimeline(); renderCueEditors();
  });

  $('pwsTimeline').addEventListener('click', e => {
    if (ws.drag) return;
    const cueEl = e.target.closest('.pws-cue');
    if (cueEl) {
      const index = Number(cueEl.dataset.index);
      ws.activeCue = index;
      seekTo(ws.cues[index]?.start || 0);
      renderCueEditors();
      updatePreviewAt($('pwsAudio').currentTime);
      return;
    }
    const rect = $('pwsTimeline').getBoundingClientRect();
    seekTo((e.clientX - rect.left) / ws.pixelsPerSecond);
  });

  $('pwsTimeline').addEventListener('pointerdown', e => {
    const cueEl = e.target.closest('.pws-cue');
    if (!cueEl || !ws.song) return;
    e.preventDefault();
    const index = Number(cueEl.dataset.index);
    const cue = ws.cues[index];
    if (!cue) return;
    snapshot();
    ws.activeCue = index;
    const handle = e.target.closest('[data-handle]')?.dataset.handle || 'move';
    ws.drag = { pointerId:e.pointerId, index, mode:handle, x:e.clientX, start:Number(cue.start), end:Number(cue.end), el:cueEl };
    cueEl.classList.add('dragging');
    cueEl.setPointerCapture?.(e.pointerId);
  });

  $('pwsTimeline').addEventListener('pointermove', e => {
    const d = ws.drag;
    if (!d || d.pointerId !== e.pointerId) return;
    const delta = (e.clientX - d.x) / ws.pixelsPerSecond;
    let start = d.start, end = d.end;
    if (d.mode === 'start') start = Math.min(d.end - .05, snapTime(d.start + delta));
    else if (d.mode === 'end') end = Math.max(d.start + .05, snapTime(d.end + delta));
    else {
      const length = d.end - d.start;
      start = snapTime(d.start + delta);
      start = Math.max(0, Math.min(start, Math.max(0, ws.duration - length)));
      end = start + length;
    }
    start = Math.max(0, Math.min(start, Math.max(0, ws.duration - .05)));
    end = Math.max(start + .05, Math.min(end, ws.duration));
    ws.cues[d.index] = { ...ws.cues[d.index], start, end };
    d.el.style.left = `${start * ws.pixelsPerSecond}px`;
    d.el.style.width = `${Math.max(8,(end-start)*ws.pixelsPerSecond)}px`;
    markDirty();
  });

  function finishDrag(e) {
    const d = ws.drag;
    if (!d || (e && d.pointerId !== e.pointerId)) return;
    d.el?.classList.remove('dragging');
    ws.drag = null;
    renderTimeline(); renderCueEditors();
  }
  $('pwsTimeline').addEventListener('pointerup', finishDrag);
  $('pwsTimeline').addEventListener('pointercancel', finishDrag);

  document.addEventListener('keydown', e => {
    if (state.activeView !== 'production' || !ws.song) return;
    const tag = String(e.target?.tagName || '').toLowerCase();
    const typing = tag === 'input' || tag === 'textarea' || tag === 'select';
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); saveCues(); return; }
    if (!typing && e.code === 'Space') { e.preventDefault(); $('pwsPlay').click(); }
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') { e.preventDefault(); (e.shiftKey ? $('pwsRedo') : $('pwsUndo')).click(); }
  });

  // Load automatically when the user opens Video i objava and there is an
  // explicit library selection.  Never trigger network work merely at app
  // startup; this keeps the editor lightweight.
  $('nav')?.addEventListener('click', e => {
    const target = e.target.closest('[data-view="production"]');
    if (target && selectedIds().length && (!ws.song || ws.song.id !== selectedIds()[0])) {
      setTimeout(loadWorkspaceSong, 0);
    }
  });

  waveformBars();
})();
