'use strict';

const state = {
  songs: [], selected: new Set(), currentSong: null, status: null, collections: [],
  activeView: 'library', taskId: null, searchTimer: null, completedTasks: new Set(),
  libraryTotal: 0, searchUserEdited: false, searchGuardTimer: null,
  waveformPeaks: null, waveformSongId: null, previewEnd: null, autoCheckTimer: null,
  queue: [], queueIndex: -1, repeat: false, connectionPoll: null,
  youtubeChannels: [], youtubeMatches: [], youtubeCalendar: [], youtubeSummary: null,
  youtubeAudioResults: [], youtubeAudioSummary: null, youtubeMatrix: {channels:[],rows:[]}, youtubeCoverage: {rows:[],channels:[],summary:{}}, youtubeExtraReport: {duplicates:[],mixes:[],repeats:[],version_types:[]}, ytdlp: null, youtubeOAuth: null,
  page: 0, pageSize: 100, totalFiltered: 0, subtitleCues: [], advancedStatus: null, security: null,
  recognitionHistory: [], recognitionCurrent: null, watchedFolders: [],
  songFinderResults: [],
};

const $ = (id) => document.getElementById(id);
const qsa = (selector, root = document) => [...root.querySelectorAll(selector)];


const THEMES = [
  { id:'default', name:'Original Purple', colors:['#8b5cf6','#121620','#0b0d12'] },
  { id:'neon-purple', name:'Neon Purple', colors:['#c026ff','#25113d','#090611'] },
  { id:'cyber-blue', name:'Cyber Blue', colors:['#00b7ff','#102638','#061018'] },
  { id:'midnight-red', name:'Midnight Red', colors:['#ff334f','#2c1118','#0f0709'] },
  { id:'emerald-city', name:'Emerald City', colors:['#16e09a','#102821','#06110e'] },
  { id:'gold-noir', name:'Gold Noir', colors:['#eab84b','#282116','#0d0b08'] },
  { id:'graphite', name:'Urban Graphite', colors:['#aab4c3','#20242b','#0d0f12'] },
  { id:'ocean-glass', name:'Ocean Glass', colors:['#2dd4bf','#102833','#061217'] },
  { id:'magenta-pulse', name:'Magenta Pulse', colors:['#ff3dad','#321126','#11070d'] },
  { id:'tangerine-night', name:'Tangerine Night', colors:['#ff812d','#302015','#100a06'] },
  { id:'arctic', name:'Arctic City', colors:['#66d9ff','#dcecf3','#c6dce7'], light:true },
  { id:'concrete', name:'Concrete Metro', colors:['#7f8ea3','#252a31','#111419'] },
  { id:'violet-smoke', name:'Violet Smoke', colors:['#a78bfa','#262132','#0e0c13'] },
  { id:'electric-lime', name:'Electric Lime', colors:['#a3ff12','#24300f','#0b1005'] },
  { id:'rose-chrome', name:'Rose Chrome', colors:['#fb7185','#302027','#100b0d'] },
  { id:'sapphire', name:'Sapphire District', colors:['#4f7cff','#17213d','#080c18'] },
  { id:'crimson', name:'Crimson Underground', colors:['#dc2626','#2d1616','#100707'] },
  { id:'bronze', name:'Bronze Loft', colors:['#d28b47','#2b2119','#0e0b08'] },
  { id:'teal-underground', name:'Teal Underground', colors:['#13c8c0','#122b2b','#061010'] },
  { id:'metro-gray', name:'Metro Gray', colors:['#94a3b8','#1e2229','#0c0e12'] },
  { id:'nightclub', name:'Nightclub Neon', colors:['#7c3cff','#17102e','#06040c'] },
  // Full redesigns (own layout/shape/pattern language, not just a palette
  // swap of the shared component CSS above) -- see the
  // body[data-theme="..."] blocks near the end of style.css.
  { id:'neon-district', name:'Neon District', colors:['#00f0ff','#ff2bd6','#050914'], full:true },
  { id:'urban-concrete', name:'Urban Concrete', colors:['#c6ff3a','#2b2b28','#141412'], full:true },
  { id:'midnight-studio', name:'Midnight Studio', colors:['#6c8cff','#0e0e12','#050506'], full:true },
  { id:'aurora-glass', name:'Aurora Glass', colors:['#7ee8fa','#c084fc','#080a14'], full:true },
];

function escapeHtml(value) {
  return String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#039;');
}
function formatDuration(seconds) {
  const n = Math.max(0, Number(seconds || 0)); const h = Math.floor(n / 3600); const m = Math.floor((n % 3600) / 60); const s = Math.floor(n % 60);
  return h ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
}
function formatDate(value) {
  if (!value) return 'Bez datuma'; const d = new Date(value); if (Number.isNaN(d.getTime())) return String(value).slice(0, 10);
  return new Intl.DateTimeFormat('sr-RS', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(d);
}
function formatHours(seconds) { const value = Number(seconds || 0) / 3600; return value < 10 ? `${value.toFixed(1)} h` : `${Math.round(value)} h`; }
function formatBytes(bytes) {
  const n = Number(bytes || 0); if (!n) return '0 B'; const units = ['B', 'KB', 'MB', 'GB']; const i = Math.min(units.length - 1, Math.floor(Math.log(n) / Math.log(1024)));
  return `${(n / (1024 ** i)).toFixed(i ? 1 : 0)} ${units[i]}`;
}

async function api(path, options = {}) {
  const retries = Number(options.retries ?? (path === '/api/connect/start' || path === '/api/health' ? 2 : 0));
  const timeoutMs = Number(options.timeoutMs || 30000);
  let lastError = null;
  for (let attempt = 0; attempt <= retries; attempt += 1) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    const init = { ...options, signal: controller.signal, cache: 'no-store', headers: { ...(options.headers || {}) } };
    delete init.retries; delete init.timeoutMs;
    if (init.body && typeof init.body !== 'string') { init.headers['Content-Type'] = 'application/json'; init.body = JSON.stringify(init.body); }
    try {
      const response = await fetch(path, init);
      clearTimeout(timer);
      const type = response.headers.get('content-type') || '';
      const data = type.includes('application/json') ? await response.json() : await response.text();
      if (response.status === 423 || (data && typeof data === 'object' && data.locked)) { showSecurityLock(); const err=new Error(data?.message || 'Program je zaključan.'); err.locked=true; throw err; }
      if (!response.ok || (data && typeof data === 'object' && data.ok === false)) throw new Error(data?.message || data?.error || `Greška ${response.status}`);
      return data;
    } catch (error) {
      clearTimeout(timer); lastError = error;
      const networkError = error?.name === 'AbortError' || error instanceof TypeError || String(error?.message || '').includes('Failed to fetch');
      if (!networkError || attempt >= retries) break;
      await new Promise((resolve) => setTimeout(resolve, 500 * (attempt + 1)));
    }
  }
  const raw = String(lastError?.message || lastError || 'Nepoznata greška');
  if (lastError?.name === 'AbortError') throw new Error('Lokalni program nije odgovorio na vreme. Pokreni ponovo „Suno Pesme Studio.exe“.');
  if (lastError instanceof TypeError || raw.includes('Failed to fetch')) throw new Error('Lokalni server nije dostupan. Verzija v3.3.2 radi u pozadini bez crnog prozora — ponovo pokreni „Suno Pesme Studio.exe“. Tačna greška je u data/program.log.');
  throw lastError;
}
function toast(message, kind = 'info', timeout = 5000) {
  const box = document.createElement('div'); box.className = `toast ${kind}`; box.textContent = message; $('toastContainer').appendChild(box); setTimeout(() => box.remove(), timeout);
}
function downloadFromUrl(url) { const a = document.createElement('a'); a.href = url; a.download = ''; document.body.appendChild(a); a.click(); a.remove(); }

const viewText = {
  library: ['Moja Suno biblioteka', 'Sve pesme, tekstovi i fajlovi na jednom mestu.'],
  folders: ['Folderi i statusi', 'Organizuj duže verzije, objavljene pesme i svoje projekte.'],
  audio: ['Audio obrada', 'Skraćivanje, fade, normalizacija, konverzija i MP3 alati.'],
  download: ['Preuzimanje pesama', 'Izaberi folder, format i prateće fajlove.'],
  import: ['Povezivanje i uvoz', 'Uvezi ceo nalog, Workspaces, linkove ili lokalne fajlove.'],
  recognition: ['Pronalazač pesme', 'Pronađi naziv pesme iz Shortsa ili audio/video isečka i trajno sačuvaj istoriju.'],
  tools: ['Pametni alati', 'YouTube ↔ Suno audio prepoznavanje, kompletnost objave, kanali, datumi i zaštita pesama.'],
  production: ['Produkcija v3', 'Lyric video, Shorts, YouTube objava, integritet, zaštita i rollback.'],
  stats: ['Statistika biblioteke', 'Broj pesama, trajanje, modeli i preuzimanja.'],
  logs: ['Dnevnik rada', 'Uspešne radnje, upozorenja i greške.'],
  settings: ['Podešavanja i backup', 'Folderi, audio alati, izvoz i rezervna kopija.'],
};
function showView(name) {
  if (!viewText[name]) name = 'library'; state.activeView = name;
  qsa('.view').forEach((el) => el.classList.toggle('active', el.id === `view-${name}`));
  qsa('.nav-item').forEach((el) => el.classList.toggle('active', el.dataset.view === name));
  $('pageTitle').textContent = viewText[name][0]; $('pageSubtitle').textContent = viewText[name][1];
  if (name === 'stats') loadStats(); if (name === 'logs') loadLogs(); if (name === 'folders') renderFolders(); if (name === 'audio') loadAudioToolsStatus();
  if (name === 'download' || name === 'tools') updateSelectionUi();
  if (name === 'tools') { loadYoutubeCenter(); loadAdvancedStatus(); }
  if (name === 'recognition') { loadMusicRecognitionStatus(); loadMusicRecognitionHistory(); loadSongFinderStatus(); loadSongFinderResults(); }
  if (name === 'import') loadWatchedFolders();
  if (name === 'production') { loadV3Status(); loadSecurityStatus(); updateSelectionUi(); }
  if (name === 'settings') { checkLibraryHealth(false, true); renderThemes(); loadStorageSummary(); checkThemeContrast(); }
}

function selectedIds() { return [...state.selected]; }
function updateSelectionUi() {
  const count = state.selected.size;
  ['selectedCount', 'downloadSelectedCount', 'folderSelectedCount', 'toolsSelectedCount'].forEach((id) => { if ($(id)) $(id).textContent = count; });
  $('selectAll').checked = state.songs.length > 0 && state.songs.every((song) => state.selected.has(song.id));
  qsa('.song-select').forEach((box) => { box.checked = state.selected.has(box.dataset.id); });
}
function songHasLocalAudio(song) { return Boolean(song.local_audio || song.local_wav); }
function songPlayable(song) { return Boolean(song && (song.local_audio || song.local_wav || song.audio_url || (state.status?.connected && song.id && !String(song.id).startsWith('local-')))); }
function starText(rating) { const n = Math.max(0, Math.min(5, Number(rating || 0))); return '★'.repeat(n) + '☆'.repeat(5 - n); }

function collectionBadges(song) {
  return (song.collections || []).slice(0, 2).map((c) => `<span class="folder-badge" style="--folder-color:${escapeHtml(c.color || '#7c3aed')}">${escapeHtml(c.name)}</span>`).join('');
}
function renderSongs() {
  const grid = $('songsGrid'); grid.innerHTML = '';
  const isEmpty = state.songs.length === 0;
  $('emptyLibrary').classList.toggle('hidden', !isEmpty);
  if (isEmpty) {
    const hasLibrary = Number(state.libraryTotal || 0) > 0;
    const hasSearch = Boolean($('searchInput').value.trim());
    const hasFilters = $('filterSelect').value !== 'all' || $('collectionFilter').value !== '0' || Boolean($('sourceFilter')?.value) || Boolean($('dateFromFilter')?.value) || Boolean($('dateToFilter')?.value) || Number($('minDurationFilter')?.value || 0) > 0 || Number($('maxDurationFilter')?.value || 0) > 0;
    $('emptyLibraryTitle').textContent = hasLibrary ? 'Nema pesama za ovu pretragu' : 'Biblioteka je prazna';
    $('emptyLibraryText').textContent = hasLibrary
      ? `${state.libraryTotal} pesama je već u programu, ali ih aktivna pretraga ili filteri skrivaju.${hasSearch ? ` Pretraga: „${$('searchInput').value.trim()}”.` : ''}`
      : 'Poveži Suno nalog i pokreni sinhronizaciju.';
    $('emptyLibraryAction').textContent = hasLibrary ? 'Očisti pretragu i filtere' : 'Otvori uvoz';
    $('emptyLibraryAction').dataset.go = hasLibrary ? '' : 'import';
    $('emptyLibraryAction').dataset.clearLibrary = hasLibrary ? '1' : '';
  }
  const downloaded = state.songs.filter(songHasLocalAudio).length;
  const totalFiltered = Number(state.totalFiltered || state.songs.length);
  const totalPages = Math.max(1, Math.ceil(totalFiltered / Math.max(1, state.pageSize)));
  if ($('pageInfo')) $('pageInfo').textContent = `Strana ${Math.min(totalPages, state.page + 1)} od ${totalPages} · ${totalFiltered} rezultata`;
  if ($('prevPageBtn')) $('prevPageBtn').disabled = state.page <= 0;
  if ($('nextPageBtn')) $('nextPageBtn').disabled = (state.page + 1) * state.pageSize >= totalFiltered;
  $('librarySummary').textContent = `${state.songs.length} na ovoj strani · ${totalFiltered} odgovara filterima · ${state.libraryTotal || totalFiltered} ukupno · ${downloaded} lokalno na strani · ${state.selected.size} izabranih`;
  state.songs.forEach((song) => {
    const card = document.createElement('article'); card.className = `song-card ${state.selected.has(song.id) ? 'selected' : ''}`; card.dataset.id = song.id;
    const playable = songPlayable(song);
    const flags = [song.favorite ? '<span class="badge" title="Favorit">♥</span>' : '', song.is_liked ? '<span class="badge" title="Lajkovano">👍</span>' : '', songHasLocalAudio(song) ? '<span class="badge saved">✓ Lokalno</span>' : '<span class="badge stream">Suno</span>'].filter(Boolean).join('');
    card.innerHTML = `<div class="cover-wrap">${song.image_url ? `<img src="${escapeHtml(song.image_url)}" alt="Omot" loading="lazy">` : '<div class="cover-placeholder">♫</div>'}<input class="song-select" data-id="${escapeHtml(song.id)}" type="checkbox" ${state.selected.has(song.id) ? 'checked' : ''}><div class="song-badges">${flags}</div><button class="card-play" data-action="play" title="${playable ? 'Pusti pesmu' : 'Audio nije dostupan'}" ${playable ? '' : 'disabled'}>▶</button></div>
      <div class="song-body"><h3 class="song-title">${escapeHtml(song.title || 'Bez naslova')}</h3><div class="song-meta"><span>${escapeHtml(song.model_version || song.source_group || 'Suno')}</span><span>${formatDuration(song.duration)}</span></div><div class="song-tags">${escapeHtml(song.tags || song.custom_tags || (song.lyrics ? 'Pesma sa tekstom' : 'Bez teksta'))}</div><div class="folder-badges">${collectionBadges(song)}</div><div class="card-footer"><span class="rating">${starText(song.rating)}</span><span>${formatDate(song.created_at)}</span><button class="card-menu" data-action="details">⋯</button></div></div>`;
    card.addEventListener('click', (event) => {
      if (event.target.closest('.song-select')) return;
      const action = event.target.closest('[data-action]')?.dataset.action;
      if (action === 'play') { event.stopPropagation(); playSong(song); } else openSong(song.id);
    });
    card.querySelector('.song-select').addEventListener('change', (event) => { event.target.checked ? state.selected.add(song.id) : state.selected.delete(song.id); updateSelectionUi(); card.classList.toggle('selected', event.target.checked); });
    grid.appendChild(card);
  }); updateSelectionUi();
}
function currentSongFilterParams(extra = {}) {
  return new URLSearchParams({ search: $('searchInput').value.trim(), filter: $('filterSelect').value, sort: $('sortSelect').value, collection_id: $('collectionFilter').value || '0', source_group: $('sourceFilter')?.value || '', date_from: $('dateFromFilter')?.value || '', date_to: $('dateToFilter')?.value || '', min_duration: $('minDurationFilter')?.value || '0', max_duration: $('maxDurationFilter')?.value || '0', ...extra });
}

async function loadSongs(resetPage = false) {
  try {
    if (resetPage) state.page = 0;
    state.pageSize = Number($('pageSizeSelect')?.value || state.pageSize || 100);
    const params = currentSongFilterParams({ limit: String(state.pageSize), offset: String(state.page * state.pageSize) });
    const data = await api(`/api/songs?${params}`);
    state.songs = data.songs || [];
    state.libraryTotal = Number(data.total_library ?? state.libraryTotal ?? state.songs.length);
    state.totalFiltered = Number(data.total_filtered ?? state.songs.length);
    const maxPage = Math.max(0, Math.ceil(state.totalFiltered / Math.max(1, state.pageSize)) - 1);
    if (state.page > maxPage) { state.page = maxPage; return loadSongs(false); }
    renderSongs();
  } catch (error) { toast(error.message, 'error'); }
}
function reloadSongsFirstPage() { return loadSongs(true); }

async function loadCollections() {
  try {
    const data = await api('/api/collections'); state.collections = data.collections || [];
    const fill = (id, firstText) => {
      const el = $(id); if (!el) return; const previous = el.value;
      el.innerHTML = `<option value="">${escapeHtml(firstText)}</option>` + state.collections.map((c) => `<option value="${c.id}">${escapeHtml(c.name)} (${c.song_count || 0})</option>`).join('');
      if ([...el.options].some((o) => o.value === previous)) el.value = previous;
    };
    const collectionFilter = $('collectionFilter'); const previousFilter = collectionFilter.value;
    collectionFilter.innerHTML = '<option value="0">Svi folderi</option>' + state.collections.map((c) => `<option value="${c.id}">${escapeHtml(c.name)} (${c.song_count || 0})</option>`).join('');
    if ([...collectionFilter.options].some((o) => o.value === previousFilter)) collectionFilter.value = previousFilter;
    fill('bulkCollectionSelect', 'Dodaj u folder...'); fill('folderBulkTarget', 'Izaberi folder'); fill('downloadCollectionSubfolder', 'Bez posebnog podfoldera');
    renderFolders(); if (state.currentSong) renderModalCollections(state.currentSong);
  } catch (error) { toast(error.message, 'error'); }
}
function renderFolders() {
  if (!$('foldersGrid')) return;
  $('foldersGrid').innerHTML = state.collections.map((c) => `<article class="folder-card panel" style="--folder-color:${escapeHtml(c.color || '#7c3aed')}"><div class="folder-icon">▰</div><div><h3>${escapeHtml(c.name)}</h3><p>${c.song_count || 0} pesama</p><div class="button-row"><button class="btn primary small open-folder-collection" data-id="${c.id}">Otvori</button>${c.is_system ? '<span class="system-label">Ugrađeni</span>' : `<button class="btn danger small delete-folder" data-id="${c.id}">Obriši</button>`}</div></div></article>`).join('') || '<p class="muted">Nema foldera.</p>';
  qsa('.open-folder-collection', $('foldersGrid')).forEach((b) => b.addEventListener('click', () => { $('collectionFilter').value = b.dataset.id; showView('library'); loadSongs(); }));
  qsa('.delete-folder', $('foldersGrid')).forEach((b) => b.addEventListener('click', async () => { if (!confirm('Obrisati ovaj folder? Pesme neće biti obrisane.')) return; try { await api('/api/collection/delete', { method: 'POST', body: { collection_id: Number(b.dataset.id) } }); await loadCollections(); } catch (e) { toast(e.message, 'error'); } }));
}
async function createFolder() {
  const name = $('newFolderName').value.trim(); if (!name) return toast('Unesi naziv foldera.', 'error');
  try { await api('/api/collection/create', { method: 'POST', body: { name, color: $('newFolderColor').value } }); $('newFolderName').value = ''; await loadCollections(); toast('Folder je napravljen.', 'success'); } catch (e) { toast(e.message, 'error'); }
}
async function addSelectedToCollection(selectId) {
  const ids = selectedIds(); const collectionId = Number($(selectId).value || 0); if (!ids.length) return toast('Prvo izaberi pesme u Biblioteci.', 'error'); if (!collectionId) return toast('Izaberi folder.', 'error');
  try { const data = await api('/api/collection/add', { method: 'POST', body: { collection_id: collectionId, ids } }); toast(`Dodato u folder: ${data.count} pesama.`, 'success'); await Promise.all([loadCollections(), loadSongs()]); } catch (e) { toast(e.message, 'error'); }
}

async function loadStatus() {
  try {
    const data = await api('/api/status', { timeoutMs: 5000 }); state.status = data;
    const badge = $('connectionBadge'); badge.classList.toggle('offline', !data.connected); badge.classList.toggle('online', Boolean(data.connected)); badge.querySelector('span').textContent = data.connected ? 'Suno je povezan' : 'Nije povezano';
    $('connectBtn').textContent = data.connected ? 'Suno povezan' : 'Poveži Suno'; $('downloadFolderLabel').textContent = data.download_dir || 'Preuzete_pesme';
    if (!$('downloadTargetInput').value) $('downloadTargetInput').value = data.download_dir || ''; if (!$('downloadDirInput').value) $('downloadDirInput').value = data.download_dir || '';
    const count = Number(data.last_new_count || 0); $('newSongsBadge').textContent = count; $('newSongsBadge').classList.toggle('hidden', count <= 0);
    if ($('lastNewCheckLabel')) $('lastNewCheckLabel').textContent = data.last_new_check_at ? `Poslednja provera: ${formatDate(data.last_new_check_at)} · novih: ${count}` : 'Nove pesme još nisu proveravane.';
    if ($('autoCheckMinutes')) $('autoCheckMinutes').value = String(data.auto_check_minutes || 0);
    if ($('settingsAutoCheckMinutes')) $('settingsAutoCheckMinutes').value = String(data.auto_check_minutes || 0);
    if ($('autoBackupBeforeSync')) $('autoBackupBeforeSync').checked = Boolean(data.auto_backup_before_sync);
    if ($('settingsAutoBackup')) $('settingsAutoBackup').checked = Boolean(data.auto_backup_before_sync);
    if ($('sunoKeepaliveEnabled')) $('sunoKeepaliveEnabled').checked = data.suno_keepalive_enabled !== false;
    if ($('sunoKeepaliveMinutes')) $('sunoKeepaliveMinutes').value = String(data.suno_keepalive_minutes || 4);
    if ($('sunoAutoReopenBrowser')) $('sunoAutoReopenBrowser').checked = Boolean(data.suno_auto_reopen_browser);
    renderSunoSessionStatus(data.suno_session || {});
    const source = $('sourceFilter'); if (source) { const previous = source.value; source.innerHTML = '<option value="">Svi izvori</option>' + (data.sources || []).map((x) => `<option value="${escapeHtml(x)}">${escapeHtml(x)}</option>`).join(''); if ([...source.options].some((o) => o.value === previous)) source.value = previous; }
    if ($('copyrightOwnerNameInput') && !document.activeElement?.isSameNode($('copyrightOwnerNameInput'))) $('copyrightOwnerNameInput').value = data.copyright_owner_name || $('copyrightOwnerNameInput').value || '';
    if ($('youtubeCookiesBrowser')) $('youtubeCookiesBrowser').value = data.youtube_cookies_browser || 'none';
    if (data.theme && !localStorage.getItem('suno-theme')) applyTheme(data.theme, false);
    renderTask(data.task); scheduleAutoCheck(Number(data.auto_check_minutes || 0));
  } catch (error) {
    $('connectionBadge').querySelector('span').textContent = 'Lokalni server nije dostupan';
  }
}
function renderTask(task) {
  const panel = $('taskPanel'); if (!task) { panel.classList.add('hidden'); return; }
  const visible = task.status === 'running' || task.id === state.taskId || !state.completedTasks.has(task.id); panel.classList.toggle('hidden', !visible); if (!visible) return;
  $('taskTitle').textContent = task.title || 'Obrada'; $('taskMessage').textContent = task.message || ''; $('taskProgress').style.width = `${Number(task.percent || 0)}%`; $('taskCurrent').textContent = task.current || ''; $('taskCount').textContent = task.total ? `${task.done || 0} / ${task.total}` : ''; $('cancelTaskBtn').classList.toggle('hidden', task.status !== 'running');
  if ($('pauseTaskBtn')) { $('pauseTaskBtn').classList.toggle('hidden', task.status !== 'running'); $('pauseTaskBtn').textContent = task.paused ? 'Nastavi' : 'Pauziraj'; $('pauseTaskBtn').dataset.paused = task.paused ? '1' : ''; }
  $('taskLogs').innerHTML = (task.logs || []).slice(-120).map((r) => `<div class="${escapeHtml(r.level)}">[${escapeHtml(r.time)}] ${escapeHtml(r.message)}</div>`).join('');
  if (task.status !== 'running' && !state.completedTasks.has(task.id)) {
    state.completedTasks.add(task.id); if (state.taskId === task.id) state.taskId = null; const toastKind=task.status==='done'?'success':(task.status==='partial'||task.status==='cancelled'?'warning':'error'); toast(task.message || 'Završeno.', toastKind, 15000);
    Promise.all([loadSongs(), loadCollections(), loadStats(), loadLogs(), loadAudioToolsStatus()]).then(async () => { if (state.currentSong) await openSong(state.currentSong.id, false); if (task.type === 'sync' && task.status === 'done') showView('library'); if (['youtube_owned','youtube_global','youtube_audio_owned','youtube_audio_one'].includes(task.type)) await loadYoutubeCenter(); if(['import_folder','rescan_watched_folders'].includes(task.type)) await loadWatchedFolders(); if(task.type==='song_finder_index'){await loadSongFinderStatus();} });
  }
}
async function startBackground(path, body) { const data = await api(path, { method: 'POST', body }); state.taskId = data.task?.id || null; renderTask(data.task); return data; }

async function connectStart() {
  try {
    const data = await api('/api/connect/start', { method: 'POST', body: {}, retries: 2, timeoutMs: 12000 });
    $('connectMessage').textContent = data.message || 'Pokretanje Suno prozora je započeto.'; $('connectMessage').className = 'inline-message success'; toast(data.message || 'Pokrećem Suno.', 'success', 8000); showView('import');
    clearInterval(state.connectionPoll); let attempts = 0;
    state.connectionPoll = setInterval(async () => { attempts += 1; try { const diag = await api('/api/connect/diagnostics', { timeoutMs: 5000 }); renderConnectDiagnostics(diag); if (diag.launch?.status === 'error') { clearInterval(state.connectionPoll); return; } const ok = await connectCheck(false); if (ok || attempts >= 45) clearInterval(state.connectionPoll); } catch (_) { if (attempts >= 45) clearInterval(state.connectionPoll); } }, 2000);
  } catch (e) { $('connectMessage').textContent = e.message; $('connectMessage').className = 'inline-message error'; toast(e.message, 'error', 10000); }
}
async function connectCheck(showToast = true) {
  try { const data = await api('/api/connect/check', { method: 'POST', body: {}, timeoutMs: 45000 }); $('connectMessage').textContent = data.message || 'Veza je uspešna.'; $('connectMessage').className = 'inline-message success'; if (showToast) toast(data.message, 'success', 9000); await loadStatus(); return true; }
  catch (e) { $('connectMessage').textContent = e.message; $('connectMessage').className = 'inline-message error'; if (showToast) toast(e.message, 'error', 10000); return false; }
}
async function connectManualToken() { const token = $('manualSunoToken').value.trim(); if (!token) return toast('Nalepi Suno Bearer token.', 'error'); try { const data = await api('/api/connect/token', { method:'POST', body:{ token }, timeoutMs:45000 }); $('manualSunoToken').value=''; $('connectMessage').textContent=data.message; $('connectMessage').className='inline-message success'; toast(data.message,'success',9000); await loadStatus(); } catch(e){ toast(e.message,'error',10000); } }
async function testLocalServer() { try { const data=await api('/api/health',{retries:2,timeoutMs:5000}); toast(`Lokalni server radi · v${data.version} · PID ${data.pid}`,'success',7000); } catch(e){ toast(e.message,'error',10000); } }
function renderConnectDiagnostics(data) { const box=$('connectDiagnostics'); if(!box) return; box.classList.remove('hidden'); const launch=data.launch||{}, diag=data.connector||{}; box.innerHTML=`<strong>Pokretanje: ${escapeHtml(launch.status||'')}</strong><span>${escapeHtml(launch.message||'')}</span><span>Browser kartice: ${(diag.pages||[]).length} · Clerk: ${diag.clerk?'da':'ne'} · prijava: ${diag.signed_in?'da':'ne'}</span>`; }
async function checkNewSongs() { if (!state.status?.connected) { showView('import'); return toast('Prvo poveži Suno nalog.', 'error'); } try { await startBackground('/api/sync/check-new',{max_pages:10,include_main:true,include_workspaces:true,refresh_details:Boolean($('newCheckRefreshDetails')?.checked)}); $('taskPanel').classList.remove('hidden'); $('taskPanel').scrollIntoView({behavior:'smooth'}); } catch(e){ toast(e.message,'error',10000); } }
async function startSync() { try { await startBackground('/api/sync/start', { max_pages: Number($('syncPages').value || 100), liked: $('syncLikedOnly').checked, include_main: $('syncMainLibrary').checked, include_workspaces: $('syncWorkspaces').checked, refresh_details: $('syncRefreshDetails').checked, resume: $('syncResume')?.checked !== false }); $('taskPanel').classList.remove('hidden'); $('taskPanel').scrollIntoView({ behavior: 'smooth' }); } catch (e) { toast(e.message, 'error'); } }
async function importUrls() { const urls = $('urlImportText').value.trim(); if (!urls) return toast('Nalepi bar jedan Suno link.', 'error'); try { await startBackground('/api/import/urls', { urls }); } catch (e) { toast(e.message, 'error'); } }
async function importFolder() { const folder = $('localFolderInput').value.trim(); if (!folder) return toast('Izaberi lokalni folder.', 'error'); try { await startBackground('/api/import/folder', { folder }); } catch (e) { toast(e.message, 'error'); } }

function renderWatchedFolders(){
  const box=$('watchedFoldersList'); if(!box)return;
  if(!state.watchedFolders.length){box.innerHTML='<div class="empty compact"><p>Nema zapamćenih foldera. Uvezi folder iznad i program će ga trajno pamtiti.</p></div>';return;}
  box.innerHTML=state.watchedFolders.map((item)=>`<article class="youtube-match-card"><div class="youtube-match-main"><span class="match-score">${item.enabled?'AKTIVAN':'PAUZIRAN'}</span><h3>${escapeHtml(item.path)}</h3><p>Poslednja provera: ${escapeHtml(item.last_scan_at||'nije pokrenuta')} · fajlova: ${Number(item.last_file_count||0)} · novih: ${Number(item.last_added_count||0)}</p></div><div class="button-row"><button class="btn ghost small watched-toggle" data-path="${escapeHtml(item.path)}" data-enabled="${item.enabled?'0':'1'}">${item.enabled?'Pauziraj':'Aktiviraj'}</button><button class="btn danger small watched-remove" data-path="${escapeHtml(item.path)}">Ukloni iz praćenja</button></div></article>`).join('');
}
async function loadWatchedFolders(){try{const d=await api('/api/import/watched-folders',{timeoutMs:10000,retries:0});state.watchedFolders=d.folders||[];renderWatchedFolders();}catch(e){const box=$('watchedFoldersList');if(box)box.innerHTML=`<div class="inline-message error">${escapeHtml(e.message)}</div>`;}}
async function rescanWatchedFolders(){try{await startBackground('/api/import/rescan-watched',{});}catch(e){toast(e.message,'error',12000);}}
async function updateWatchedFolder(path,fields){try{const d=await api('/api/import/watched-folder/update',{method:'POST',body:{path,...fields}});state.watchedFolders=d.folders||[];renderWatchedFolders();toast('Lista zapamćenih foldera je osvežena.','success');}catch(e){toast(e.message,'error',10000);}}

function downloadOptions(forceMp3 = false) {
  return { format: forceMp3 ? 'mp3' : (document.querySelector('input[name="audioFormat"]:checked')?.value || 'mp3'), cover: $('optCover').checked, lyrics: $('optLyrics').checked, synced_lyrics: $('optSynced').checked, metadata: $('optMetadata').checked, embed_tags: $('optEmbed').checked, video: $('optVideo').checked, folders_by_month: $('optMonthFolders').checked, overwrite: $('optOverwrite').checked, target_dir: $('downloadTargetInput').value.trim() || state.status?.download_dir, collection_subfolder_id: Number($('downloadCollectionSubfolder').value || 0), parallel_downloads: Number($('parallelDownloads')?.value || 2), rate_limit_mbps: Number($('downloadRateLimit')?.value || 0) };
}
async function startDownload(forceMp3 = false) { const ids = selectedIds(); if (!ids.length) { showView('library'); return toast('Prvo izaberi pesme.', 'error'); } try { await startBackground('/api/download/start', { ids, options: downloadOptions(forceMp3) }); toast(`Preuzimanje ${ids.length} pesama je pokrenuto.`, 'success'); } catch (e) { toast(e.message, 'error'); } }
async function chooseFolder(inputId) {
  try { const input = $(inputId); const data = await api('/api/choose-folder', { method: 'POST', body: { initial: input.value || state.status?.download_dir || '' } }); if (data.path) input.value = data.path; return data.path || ''; } catch (e) { toast(e.message, 'error'); return ''; }
}
async function saveToFolder(ids) {
  if (!ids.length) return toast('Prvo izaberi pesmu.', 'error'); const target = await chooseFolder('downloadTargetInput'); if (!target) return;
  try { await startBackground('/api/save-to-folder', { ids, target_folder: target, options: { format: 'mp3', include_related: true } }); } catch (e) { toast(e.message, 'error'); }
}
async function exportData(format, ids = null) { try { const data = await api('/api/export', { method: 'POST', body: { format, ...(ids ? { ids } : {}) } }); downloadFromUrl(data.download_url); toast(`Izvoz ${format.toUpperCase()} je spreman.`, 'success'); } catch (e) { toast(e.message, 'error'); } }
async function backup() { try { const data = await api('/api/backup', { method: 'POST', body: {} }); downloadFromUrl(data.download_url); toast('Backup je napravljen.', 'success'); } catch (e) { toast(e.message, 'error'); } }

function renderLibraryHealth(health) {
  const missing = Number(health?.missing_count || 0); const repaired = Number(health?.repaired_count || 0); const status = $('libraryHealthStatus');
  status.textContent = missing ? `${missing} nevažećih lokalnih putanja${repaired ? ` · očišćeno ${repaired}` : ''}.` : 'Svi evidentirani lokalni fajlovi postoje.';
  status.className = `inline-message ${missing ? 'warning' : 'success'}`;
  const details = $('libraryHealthDetails'); const rows = health?.missing || [];
  details.classList.toggle('hidden', !rows.length); details.innerHTML = rows.slice(0, 200).map((row) => `<div class="warning">${escapeHtml(row.title || row.song_id)} · ${escapeHtml(row.field)} · ${escapeHtml(row.path)}</div>`).join('');
}
async function checkLibraryHealth(repair = false, silent = false) {
  try { const data = await api(repair ? '/api/library/repair' : '/api/library/health', repair ? { method: 'POST', body: {} } : {}); renderLibraryHealth(data.health || {}); if (!silent) toast(repair ? 'Provera i čišćenje putanja su završeni.' : 'Provera lokalnih fajlova je završena.', 'success'); if (repair) await loadSongs(); return data.health; } catch (e) { $('libraryHealthStatus').textContent = e.message; $('libraryHealthStatus').className = 'inline-message error'; if (!silent) toast(e.message, 'error'); }
}
async function chooseBackupFile() { try { const data = await api('/api/choose-file', { method: 'POST', body: { initial: $('restorePathInput').value || '' } }); if (data.path) $('restorePathInput').value = data.path; } catch (e) { toast(e.message, 'error'); } }
async function restoreBackup() {
  const path = $('restorePathInput').value.trim(); if (!path) return toast('Izaberi backup ZIP ili .db fajl.', 'error');
  if (!confirm('Vratiti ovu rezervnu kopiju? Trenutna baza će biti sačuvana kao pre-restore kopija.')) return;
  try {
    const data = await api('/api/restore', { method: 'POST', body: { path }, timeoutMs: 300000 });
    const restoredFiles = Number(data.restored_files || 0);
    const skippedFiles = Number(data.skipped_files || 0);
    const missing = Number(data.health?.missing_count || 0);
    const details = [`Učitano: ${data.songs || 0} pesama`, `vraćeno fajlova: ${restoredFiles}`];
    if (skippedFiles) details.push(`preskočeno: ${skippedFiles}`);
    if (missing) details.push(`još nedostaje: ${missing}`);
    if (data.restore_folder) details.push(`folder: ${data.restore_folder}`);
    $('restoreMessage').textContent = `${data.message} ${details.join(' · ')}.`;
    $('restoreMessage').className = `inline-message ${missing ? 'warning' : 'success'}`;
    state.selected.clear(); state.currentSong = null;
    await Promise.all([loadStatus(), loadCollections(), loadSongs(), loadStats()]);
    toast(missing ? 'Backup je vraćen, ali neke stare putanje i dalje nedostaju.' : 'Backup i lokalni fajlovi su uspešno vraćeni.', missing ? 'warning' : 'success', 12000);
    showView('library');
  } catch (e) { $('restoreMessage').textContent = e.message; $('restoreMessage').className = 'inline-message error'; toast(e.message, 'error', 9000); }
}
async function resetCurrentSongEdits() {
  if (!state.currentSong) return; if (!confirm('Vratiti naslov, autora i tekst sa Suno-a? Tvoje izmene tih polja biće zamenjene kada je Suno povezan ili pri sledećoj sinhronizaciji.')) return;
  try { const data = await api('/api/song/reset-user-edits', { method: 'POST', body: { id: state.currentSong.id, fields: ['title', 'display_name', 'lyrics'] } }); state.currentSong = data.song; await openSong(state.currentSong.id, false); await loadSongs(); toast('Zaštita korisničkih izmena je uklonjena i Suno podaci su osveženi kada su dostupni.', 'success', 9000); } catch (e) { toast(e.message, 'error', 9000); }
}
async function deleteCurrentSong() {
  if (!state.currentSong) return; const deleteFiles = $('deleteLocalFilesCheckbox').checked; const title = state.currentSong.title || 'ovu pesmu';
  const message = deleteFiles ? `Ukloniti „${title}” iz biblioteke I obrisati njene lokalne fajlove sa diska? Ova radnja se ne može poništiti.` : `Ukloniti „${title}” samo iz lokalne biblioteke programa? Pesma na Suno nalogu i lokalni fajlovi ostaju.`;
  if (!confirm(message)) return;
  try { const id = state.currentSong.id; await api('/api/song/delete', { method: 'POST', body: { id, delete_files: deleteFiles } }); state.selected.delete(id); state.currentSong = null; $('songModal').classList.add('hidden'); await Promise.all([loadSongs(), loadCollections(), loadStats()]); toast('Pesma je uklonjena iz lokalne biblioteke.', 'success'); } catch (e) { toast(e.message, 'error', 9000); }
}

async function loadStats() {
  try { const data = await api('/api/stats'); const s = data.stats || {}; state.libraryTotal = Number(s.total || state.libraryTotal || 0); const cards = [['Ukupno pesama', s.total || 0], ['Preuzeto', s.downloaded || 0], ['Sa tekstom', s.with_text || 0], ['Duže od 4 min', s.long_songs || 0], ['Suno lajkovi', s.liked || 0], ['Favoriti', s.favorites || 0], ['Ukupno trajanje', formatHours(s.seconds)]]; $('statCards').innerHTML = cards.map(([l, v]) => `<div class="stat-card panel"><span>${escapeHtml(l)}</span><strong>${escapeHtml(v)}</strong></div>`).join(''); renderBars('modelChart', s.models || []); renderBars('monthChart', [...(s.months || [])].reverse()); } catch (e) { toast(e.message, 'error'); }
}
function renderBars(id, rows) { const max = Math.max(1, ...rows.map((r) => Number(r.value || 0))); $(id).innerHTML = rows.length ? rows.map((r) => `<div class="bar-row"><span>${escapeHtml(r.name || 'Nepoznato')}</span><div class="bar-track"><div class="bar-fill" style="width:${Math.max(3, Number(r.value || 0) / max * 100)}%"></div></div><strong>${escapeHtml(r.value)}</strong></div>`).join('') : '<p class="muted">Nema podataka.</p>'; }
async function loadLogs() { try { const data = await api('/api/logs'); $('logsTable').innerHTML = (data.logs || []).map((r) => `<div class="log-row"><span>${escapeHtml(r.created_at)}</span><strong class="${escapeHtml(r.level)}">${escapeHtml(r.level)}</strong><span>${escapeHtml(r.message)}</span></div>`).join('') || '<p class="muted">Dnevnik je prazan.</p>'; } catch (e) { toast(e.message, 'error'); } }

async function openSong(id, showModal = true) {
  try {
    const data = await api(`/api/song?id=${encodeURIComponent(id)}`); const song = data.song; state.currentSong = song;
    $('modalCover').src = song.image_url || ''; $('modalCover').style.visibility = song.image_url ? 'visible' : 'hidden'; $('modalModel').textContent = song.model_version || song.source_group || 'Suno'; $('modalTitle').textContent = song.title || 'Bez naslova'; $('modalMeta').textContent = `${formatDate(song.created_at)} · ${formatDuration(song.duration)} · ${song.display_name || 'Bez autora'}`;
    const sunoUrl = song.source_url || (!String(song.id || '').startsWith('local-') ? `https://suno.com/song/${song.id}` : ''); $('modalSunoLink').href = sunoUrl || '#'; $('modalSunoLink').classList.toggle('hidden', !sunoUrl);
    $('modalLyrics').value = song.lyrics || ''; $('modalPrompt').value = song.prompt || ''; $('modalTags').value = song.tags || ''; $('modalCustomTags').value = song.custom_tags || ''; $('modalNotes').value = song.notes || ''; $('modalRating').value = String(song.rating || 0); $('modalFavorite').checked = Boolean(song.favorite);
    $('tagTitle').value = song.title || ''; $('tagArtist').value = song.display_name || ''; $('tagAlbum').value = song.album || 'Suno pesme'; $('tagGenre').value = song.genre || song.tags || ''; $('tagYear').value = song.year || String(song.created_at || '').slice(0, 4); $('tagTrack').value = song.track_number || ''; $('tagBpm').value = song.bpm || ''; $('tagKey').value = song.key_signature || ''; $('tagCopyright').value = song.copyright || ''; $('tagWebsite').value = song.website || song.youtube_url || '';
    $('youtubeUrl').value = song.youtube_url || ''; $('youtubeDate').value = String(song.youtube_published_at || '').slice(0, 10);
    $('trimStart').value = '0'; $('trimEnd').value = Number(song.duration || 0).toFixed(1); $('processedLabel').value = '';
    $('processingMode').value = 'auto'; $('processFormat').value = 'mp3'; $('processBitrate').value = '320k'; $('fadeIn').value = '0'; $('fadeOut').value = '0'; $('volumeDb').value = '0'; $('audioSpeed').value = '1'; $('sampleRate').value = '0'; $('audioChannels').value = '0'; $('normalizeAudio').checked = false; $('removeSilence').checked = false; $('replacePrimaryAudio').checked = false; $('markLongDone').checked = false;
    updateTextStats(); renderModalCollections(song); renderFiles(song); renderDerivedFiles(song); renderAudioInfoFallback(song);
    const playable = songPlayable(song); $('modalPlayBtn').disabled = !playable; $('modalDownloadBrowserBtn').disabled = !playable; $('modalSaveToFolderBtn').disabled = !playable; $('deleteLocalFilesCheckbox').checked = false; if (showModal) { switchModalTab('lyrics'); $('songModal').classList.remove('hidden'); }
  } catch (e) { toast(e.message, 'error'); }
}
function switchModalTab(name) { qsa('[data-modal-tab]').forEach((el) => el.classList.toggle('active', el.dataset.modalTab === name)); qsa('.modal-tab').forEach((el) => el.classList.toggle('active', el.id === `modalTab-${name}`)); if (name === 'audio' && state.currentSong) { loadWaveform(state.currentSong); loadAudioInfo(state.currentSong); } if (name === 'timing' && state.currentSong) loadSubtitleCues(); if (name === 'history' && state.currentSong) loadSongHistory(); }
function renderModalCollections(song) {
  const selected = new Set((song.collections || []).map((c) => Number(c.id))); $('modalCollections').innerHTML = state.collections.map((c) => `<label class="collection-check" style="--folder-color:${escapeHtml(c.color || '#7c3aed')}"><input type="checkbox" value="${c.id}" ${selected.has(Number(c.id)) ? 'checked' : ''}><span>▰ ${escapeHtml(c.name)}</span></label>`).join('');
}
function renderFiles(song) {
  const fields = [['MP3', song.local_audio], ['WAV', song.local_wav], ['MP4', song.local_video], ['Omot', song.local_cover], ['TXT', song.local_lyrics], ['LRC', song.local_lrc], ['SRT', song.local_srt]].filter(([, p]) => p);
  const localHtml = fields.map(([label, path]) => `<div class="file-item"><strong>${label}</strong><span title="${escapeHtml(path)}">${escapeHtml(path)}</span><a href="/media?path=${encodeURIComponent(path)}" target="_blank">Otvori</a></div>`).join('');
  $('modalFiles').innerHTML = `<div class="file-item"><strong>Original audio</strong><span>${songHasLocalAudio(song) ? 'Lokalni fajl' : 'Direktno sa Suno-a'}</span><a href="/api/song/audio?id=${encodeURIComponent(song.id)}&download=1">Preuzmi</a></div>${localHtml || '<p class="muted">Nema dodatnih lokalnih fajlova.</p>'}`;
}
function renderDerivedFiles(song) {
  const rows = song.derived_files || []; $('derivedFilesList').innerHTML = rows.length ? rows.map((f) => `<div class="file-item derived-item"><div><strong>${escapeHtml(f.label || 'Obrađena verzija')}</strong><span>${escapeHtml(String(f.format || '').toUpperCase())} · ${formatDuration(f.duration)} · ${escapeHtml(f.path)}</span></div><div class="derived-actions"><button class="link-button play-derived" data-id="${f.id}">Pusti</button><a href="/api/song/audio?file_id=${f.id}&download=1">Preuzmi</a><button class="link-button danger-text delete-derived" data-id="${f.id}">Obriši</button></div></div>`).join('') : '<p class="muted">Još nema obrađenih verzija.</p>';
  qsa('.play-derived', $('derivedFilesList')).forEach((b) => b.addEventListener('click', () => playSong(song, Number(b.dataset.id))));
  qsa('.delete-derived', $('derivedFilesList')).forEach((b) => b.addEventListener('click', async () => { if (!confirm('Obrisati ovu obrađenu verziju i njen fajl?')) return; try { await api('/api/derived/delete', { method: 'POST', body: { file_id: Number(b.dataset.id), delete_from_disk: true } }); await openSong(song.id, false); } catch (e) { toast(e.message, 'error'); } }));
}
async function updateCurrent(fields, message = 'Promene su sačuvane.') { if (!state.currentSong) return; try { const data = await api('/api/song/update', { method: 'POST', body: { id: state.currentSong.id, fields } }); state.currentSong = data.song; await loadSongs(); toast(message, 'success'); return data.song; } catch (e) { toast(e.message, 'error'); } }

async function playSong(song, fileId = 0, start = null, end = null) {
  if (!songPlayable(song) && !fileId) return toast('Audio nije dostupan. Preuzmi pesmu ili ponovo poveži Suno.', 'error');
  const player = $('audioPlayer'); state.previewEnd = end;
  $('playerCover').src = song.local_cover ? `/media?path=${encodeURIComponent(song.local_cover)}` : (song.image_url || ''); $('playerTitle').textContent = song.title || 'Bez naslova'; $('playerSubtitle').textContent = fileId ? 'Obrađena verzija' : (songHasLocalAudio(song) ? 'Lokalni audio' : 'Suno streaming'); $('playerBar').classList.add('visible');
  const src = fileId ? `/api/song/audio?file_id=${fileId}` : `/api/song/audio?id=${encodeURIComponent(song.id)}`;
  const seek = () => { if (start !== null && Number.isFinite(Number(start))) { try { player.currentTime = Math.max(0, Number(start)); } catch {} } };
  if (player.dataset.source !== src) { player.pause(); player.src = src; player.dataset.source = src; player.load(); player.addEventListener('loadedmetadata', seek, { once: true }); }
  else seek();
  // Poziv play() ide odmah iz korisničkog klika, da Chrome ne blokira plejer kao automatsko puštanje.
  player.play().catch((e) => toast(`Plejer nije pokrenut: ${e.message}`, 'error', 8000));
}

function updateTextStats() {
  const text = $('modalLyrics').value || ''; const lines = text ? text.replace(/\r/g, '').split('\n') : []; const nonEmpty = lines.map((x) => x.trim()).filter(Boolean); const counts = new Map(); nonEmpty.forEach((line) => counts.set(line.toLowerCase(), (counts.get(line.toLowerCase()) || 0) + 1)); const duplicates = [...counts.values()].filter((n) => n > 1).reduce((a, n) => a + n - 1, 0);
  $('lyricsChars').textContent = `${text.length} karaktera`; $('lyricsWords').textContent = `${(text.trim().match(/\S+/g) || []).length} reči`; $('lyricsLines').textContent = `${lines.length} redova`; $('lyricsDuplicates').textContent = `${duplicates} ponovljenih redova`;
}
function transformLyrics(action) {
  let text = $('modalLyrics').value.replace(/\r/g, '');
  if (action === 'clean') text = text.split('\n').map((line) => line.replace(/[ \t]+/g, ' ').trimEnd()).join('\n').replace(/\n{3,}/g, '\n\n').trim();
  if (action === 'blank') text = text.split('\n').filter((line) => line.trim()).join('\n');
  if (action === 'dedupe') { const out = []; text.split('\n').forEach((line) => { if (!out.length || out[out.length - 1].trim().toLowerCase() !== line.trim().toLowerCase()) out.push(line); }); text = out.join('\n'); }
  if (action === 'remove-sections') text = text.split('\n').filter((line) => !/^\s*\[(verse|chorus|pre-chorus|bridge|intro|outro|strofa|refren|predrefren|most|instrumental)[^\]]*\]\s*$/i.test(line)).join('\n');
  if (action === 'upper') text = text.toLocaleUpperCase('sr'); if (action === 'lower') text = text.toLocaleLowerCase('sr'); $('modalLyrics').value = text; updateTextStats();
}

async function loadAudioToolsStatus() {
  try { const data = await api('/api/audio-tools/status'); const t = data.tools || {}; const text = t.ready ? `Audio alati su spremni. ${t.version || ''}` : `Audio alati nisu preuzeti. Prvo korišćenje preuzima oko ${t.download_mb || 105} MB.`; ['audioToolsStatus', 'settingsAudioToolsStatus'].forEach((id) => { if ($(id)) { $(id).textContent = text; $(id).className = `inline-message ${t.ready ? 'success' : 'warning'}`; } }); return t; } catch (e) { toast(e.message, 'error'); }
}
async function prepareAudioTools() { try { await startBackground('/api/audio-tools/install', {}); } catch (e) { toast(e.message, 'error'); } }
function renderAudioInfoFallback(song) { $('audioInfoBox').innerHTML = `<span>Trajanje: <strong>${formatDuration(song.duration)}</strong></span><span>Izvor: <strong>${songHasLocalAudio(song) ? 'lokalni fajl' : 'Suno'}</strong></span>`; }
async function loadAudioInfo(song) {
  try { const data = await api(`/api/audio/info?id=${encodeURIComponent(song.id)}`); const i = data.info || {}; $('audioInfoBox').innerHTML = `<span>Trajanje <strong>${formatDuration(i.duration)}</strong></span><span>Format <strong>${escapeHtml(i.format || '')}</strong></span><span>Kodek <strong>${escapeHtml(i.codec || '')}</strong></span><span>Bitrate <strong>${i.bitrate_kbps || 0} kbps</strong></span><span>Sample rate <strong>${i.sample_rate || 0} Hz</strong></span><span>Kanali <strong>${i.channels || 0}</strong></span><span>Veličina <strong>${formatBytes(i.size)}</strong></span>`; } catch { renderAudioInfoFallback(song); }
}
async function loadWaveform(song) {
  if (state.waveformSongId === song.id && state.waveformPeaks) { drawWaveform(state.waveformPeaks); return; }
  $('waveformMessage').textContent = 'Pravim optimizovan talasni prikaz...'; const canvas = $('waveformCanvas'); const ctx = canvas.getContext('2d'); ctx.clearRect(0, 0, canvas.width, canvas.height);
  try {
    const data = await api(`/api/audio/waveform?id=${encodeURIComponent(song.id)}&width=${canvas.width}`);
    const waveform = data.waveform || {}; state.waveformPeaks = waveform.peaks || []; state.waveformSongId = song.id; drawWaveform(state.waveformPeaks);
    $('waveformMessage').textContent = `${waveform.cached ? 'Talasni prikaz učitan iz keša' : 'Talasni prikaz napravljen'} · izvor: ${waveform.source === 'local' ? 'lokalni audio' : 'Suno'} · klikni na talas da pomeriš poziciju.`;
  } catch (e) { $('waveformMessage').textContent = `Talasni oblik nije učitan: ${e.message}. Plejer i skraćivanje i dalje rade.`; drawWaveform(null); }
}
function drawWaveform(peaks) {
  const canvas = $('waveformCanvas'); const ctx = canvas.getContext('2d'); const w = canvas.width; const h = canvas.height; ctx.clearRect(0, 0, w, h); ctx.fillStyle = '#0b0f1a'; ctx.fillRect(0, 0, w, h); ctx.strokeStyle = '#8b5cf6'; ctx.lineWidth = 1;
  if (!peaks || !peaks.length) { ctx.strokeStyle = '#475569'; ctx.beginPath(); ctx.moveTo(0, h / 2); ctx.lineTo(w, h / 2); ctx.stroke(); return; }
  const amp = h / 2; ctx.beginPath();
  for (let x = 0; x < w; x++) { const pair = peaks[Math.min(peaks.length - 1, Math.floor(x / w * peaks.length))] || [0, 0]; const min = Number(pair[0] || 0); const max = Number(pair[1] || 0); ctx.moveTo(x, (1 + min) * amp); ctx.lineTo(x, (1 + max) * amp); } ctx.stroke(); drawTrimOverlay();
}
function drawTrimOverlay() {
  const canvas = $('waveformCanvas'); const ctx = canvas.getContext('2d'); const duration = Number(state.currentSong?.duration || 0); if (!duration) return; const start = Math.max(0, Number($('trimStart').value || 0)); const end = Math.min(duration, Number($('trimEnd').value || duration)); const x1 = start / duration * canvas.width; const x2 = end / duration * canvas.width; ctx.fillStyle = 'rgba(139,92,246,.18)'; ctx.fillRect(x1, 0, Math.max(1, x2 - x1), canvas.height); ctx.strokeStyle = '#f8fafc'; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(x1, 0); ctx.lineTo(x1, canvas.height); ctx.moveTo(x2, 0); ctx.lineTo(x2, canvas.height); ctx.stroke();
}
function audioProcessOptions(modeOverride = null) {
  const mode = modeOverride || $('processingMode').value || 'auto';
  const quick = mode === 'quick';
  return {
    start: Number($('trimStart').value || 0), end: Number($('trimEnd').value || state.currentSong?.duration || 0),
    format: $('processFormat').value, bitrate: $('processBitrate').value, processing_mode: mode,
    fade_in: quick ? 0 : Number($('fadeIn').value || 0), fade_out: quick ? 0 : Number($('fadeOut').value || 0),
    volume_db: quick ? 0 : Number($('volumeDb').value || 0), speed: quick ? 1 : Number($('audioSpeed').value || 1),
    sample_rate: quick ? 0 : Number($('sampleRate').value || 0), channels: quick ? 0 : Number($('audioChannels').value || 0),
    normalize: quick ? false : $('normalizeAudio').checked, remove_silence: quick ? false : $('removeSilence').checked,
    replace_primary: $('replacePrimaryAudio').checked, mark_long_done: $('markLongDone').checked,
    label: $('processedLabel').value.trim(), target_dir: $('processedTargetFolder').value.trim()
  };
}
async function processCurrentAudio(modeOverride = null) {
  if (!state.currentSong) return; const options = audioProcessOptions(modeOverride); if (options.end <= options.start) return toast('Kraj isečka mora biti posle početka.', 'error');
  if (options.processing_mode === 'quick' && options.format !== 'mp3') return toast('Za Suno pesmu najbrže isecanje koristi MP3 format. Za WAV/FLAC/M4A koristi preciznu obradu.', 'error', 9000);
  try { await startBackground('/api/audio/process', { id: state.currentSong.id, options }); $('songModal').classList.add('hidden'); toast(options.processing_mode === 'quick' ? 'Brzo isecanje je pokrenuto.' : 'Audio obrada je pokrenuta.', 'success'); } catch (e) { toast(e.message, 'error'); }
}
async function runShortsTeasers() {
  if (!state.currentSong) return;
  const status = $('shortsTeasersStatus'); if (status) { status.className = 'inline-message warning'; status.textContent = 'Analiziram pesmu i pravim 3 kratke najave (30-50 s)…'; }
  try { await startBackground('/api/v3/shorts/teasers', { id: state.currentSong.id, normalize: true }); toast('Pravljenje 3 kratke najave je pokrenuto.', 'success'); }
  catch (e) { if (status) { status.className = 'inline-message error'; status.textContent = e.message; } toast(e.message, 'error', 12000); }
}
async function cutShortsByText() {
  if (!state.currentSong) return;
  const text = $('shortsTextQuery')?.value.trim() || ''; if (!text) return toast('Upiši deo teksta pesme koji želiš da isečeš.', 'warning');
  const duration = Number($('shortsTextDuration')?.value || 30);
  const status = $('shortsTextClipStatus'); if (status) { status.className = 'inline-message warning'; status.textContent = 'Tražim taj deo teksta u pesmi…'; }
  try {
    const d = await api('/api/v3/shorts/text-clip', { method: 'POST', body: { id: state.currentSong.id, text, duration }, timeoutMs: 120000, retries: 0 });
    const r = d.result || {};
    if (status) { status.className = `inline-message ${r.approximate ? 'warning' : 'success'}`; status.textContent = r.approximate ? `Isečak je napravljen (${r.duration}s), ali tajming je procenjen jer pesma nema sačuvan LRC/SRT — proveri rezultat i po potrebi sačuvaj titlove.` : `Isečak je napravljen: ${r.duration}s od ${Math.round(r.start)}s (tekst pronađen na ${Math.round(r.matched_at)}s).`; }
    toast('Isečak po tekstu je sačuvan.', 'success');
    await openSong(state.currentSong.id, false);
  } catch (e) { if (status) { status.className = 'inline-message error'; status.textContent = e.message; } toast(e.message, 'error', 12000); }
}
async function processSelectedBatch() {
  const ids = selectedIds(); if (!ids.length) return toast('Prvo izaberi pesme u Biblioteci.', 'error');
  if (!state.currentSong) return toast('Otvori jednu pesmu i podesi početak, kraj i efekte.', 'error');
  const options = audioProcessOptions(); if (options.end <= options.start) return toast('Kraj isečka mora biti posle početka.', 'error');
  if (!confirm(`Primeniti ova audio-podešavanja na ${ids.length} izabranih pesama?`)) return;
  try { await startBackground('/api/audio/process-batch', { ids, options }); $('songModal').classList.add('hidden'); toast(`Masovna obrada ${ids.length} pesama je pokrenuta.`, 'success'); } catch (e) { toast(e.message, 'error'); }
}

async function saveTagsToLibrary() {
  await updateCurrent({ title: $('tagTitle').value, display_name: $('tagArtist').value, album: $('tagAlbum').value, genre: $('tagGenre').value, year: $('tagYear').value, track_number: $('tagTrack').value, bpm: $('tagBpm').value, key_signature: $('tagKey').value, copyright: $('tagCopyright').value, website: $('tagWebsite').value }, 'MP3 podaci su sačuvani u biblioteci.');
  if (state.currentSong) { $('modalTitle').textContent = state.currentSong.title; }
}
async function writeMp3Tags() { if (!state.currentSong) return; await saveTagsToLibrary(); try { const data = await api('/api/song/write-tags', { method: 'POST', body: { id: state.currentSong.id, backup: true } }); toast(`Podaci su upisani u MP3. Backup: ${data.backup || 'napravljen'}`, 'success', 9000); } catch (e) { toast(e.message, 'error', 9000); } }
async function renameSongFiles() { if (!state.currentSong) return; try { const data = await api('/api/song/rename-files', { method: 'POST', body: { id: state.currentSong.id } }); state.currentSong = data.song; renderFiles(data.song); toast('Lokalni fajlovi su preimenovani.', 'success'); } catch (e) { toast(e.message, 'error'); } }
async function saveFoldersAndPublication() {
  if (!state.currentSong) return;
  const ids = qsa('#modalCollections input:checked').map((x) => Number(x.value));
  const youtubeUrl = $('youtubeUrl').value.trim(); const youtubeDate = $('youtubeDate').value;
  const published = state.collections.find((c) => c.slug === 'objavljene-na-kanalu');
  if ((youtubeUrl || youtubeDate) && published && !ids.includes(Number(published.id))) ids.push(Number(published.id));
  try { await api('/api/song/collections', { method: 'POST', body: { id: state.currentSong.id, collection_ids: ids } }); await updateCurrent({ youtube_url: youtubeUrl, youtube_published_at: youtubeDate }, 'Folderi i podaci o objavi su sačuvani.'); await loadCollections(); await openSong(state.currentSong.id, false); } catch (e) { toast(e.message, 'error'); }
}

async function saveSettings() { const dir = $('downloadDirInput').value.trim(); if (!dir) return toast('Izaberi folder.', 'error'); try { const data = await api('/api/settings', { method: 'POST', body: { download_dir: dir } }); $('downloadFolderLabel').textContent = data.download_dir; $('downloadTargetInput').value = data.download_dir; toast('Podešavanja su sačuvana.', 'success'); } catch (e) { toast(e.message, 'error'); } }
async function openFolder() { try { await api('/api/open-folder', { method: 'POST', body: { path: $('downloadDirInput').value.trim() } }); } catch (e) { toast(e.message, 'error'); } }


function applyTheme(themeId, persist = true) {
  const valid = THEMES.some((t) => t.id === themeId) ? themeId : 'default'; document.body.dataset.theme = valid;
  localStorage.setItem('suno-theme', valid); if ($('themeSelect')) $('themeSelect').value = valid;
  qsa('.theme-card').forEach((x) => x.classList.toggle('active', x.dataset.theme === valid));
  if (persist) api('/api/settings', { method:'POST', body:{theme:valid} }).catch(()=>{});
}
function renderThemes() {
  if (!$('themeGrid')) return;
  $('themeSelect').innerHTML = THEMES.map((t)=>`<option value="${t.id}">${escapeHtml(t.name)}</option>`).join('');
  $('themeGrid').innerHTML = THEMES.slice(1).map((t)=>`<button class="theme-card" data-theme="${t.id}"><span class="theme-colors">${t.colors.map((c)=>`<i style="background:${c}"></i>`).join('')}</span><strong>${escapeHtml(t.name)}</strong></button>`).join('');
  applyTheme(localStorage.getItem('suno-theme') || state.status?.theme || 'default', false);
}

// --- Skaliranje i pristupačnost (section 5.6) ---
const A11Y_KEYS = { scale: 'suno-ui-scale', density: 'suno-ui-density', motion: 'suno-ui-motion', contrast: 'suno-ui-contrast', colorblind: 'suno-ui-colorblind' };
function loadA11ySettings() {
  return {
    scale: localStorage.getItem(A11Y_KEYS.scale) || '100',
    density: localStorage.getItem(A11Y_KEYS.density) || 'normal',
    motion: localStorage.getItem(A11Y_KEYS.motion) === '1',
    contrast: localStorage.getItem(A11Y_KEYS.contrast) === '1',
    colorblind: localStorage.getItem(A11Y_KEYS.colorblind) === '1',
  };
}
function applyA11ySettings(settings) {
  const shell = document.querySelector('.app-shell');
  const pct = Number(settings.scale) || 100;
  if (shell) { shell.dataset.uiScale = String(pct !== 100); shell.style.setProperty('--ui-scale', String(pct / 100)); }
  document.body.dataset.density = settings.density === 'normal' ? '' : settings.density;
  if (settings.motion) document.body.dataset.motion = 'reduced'; else delete document.body.dataset.motion;
  if (settings.contrast) document.body.dataset.contrast = 'high'; else delete document.body.dataset.contrast;
  if (settings.colorblind) document.body.dataset.colorblind = 'true'; else delete document.body.dataset.colorblind;
  if ($('textScaleSelect')) $('textScaleSelect').value = String(pct);
  if ($('densitySelect')) $('densitySelect').value = settings.density;
  if ($('reduceMotionToggle')) $('reduceMotionToggle').checked = settings.motion;
  if ($('highContrastToggle')) $('highContrastToggle').checked = settings.contrast;
  if ($('colorblindToggle')) $('colorblindToggle').checked = settings.colorblind;
}
function saveA11ySetting(key, value) {
  localStorage.setItem(A11Y_KEYS[key], String(value));
  applyA11ySettings(loadA11ySettings());
}
function resetThemeAndAppearance() {
  localStorage.removeItem('suno-theme');
  Object.values(A11Y_KEYS).forEach((k) => localStorage.removeItem(k));
  applyTheme('default');
  applyA11ySettings(loadA11ySettings());
  toast('Vraćeno na originalnu temu i podrazumevana podešavanja izgleda.', 'success');
}
function exportThemeSettings() {
  const payload = { theme: localStorage.getItem('suno-theme') || 'default', ...loadA11ySettings() };
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = 'suno-pesme-studio-izgled.json';
  a.click();
  URL.revokeObjectURL(a.href);
}
async function importThemeSettings(file) {
  try {
    const data = JSON.parse(await file.text());
    if (data.theme) applyTheme(data.theme);
    if (data.scale) saveA11ySetting('scale', data.scale);
    if (data.density) saveA11ySetting('density', data.density);
    saveA11ySetting('motion', Boolean(data.motion));
    saveA11ySetting('contrast', Boolean(data.contrast));
    saveA11ySetting('colorblind', Boolean(data.colorblind));
    toast('Podešavanja izgleda su uvezena.', 'success');
  } catch (e) { toast('Fajl nije validan JSON izvoz podešavanja.', 'error'); }
}
// Relative luminance + WCAG contrast ratio between the active theme's text
// and background colors -- a real check against the currently rendered
// CSS custom properties, not a canned number.
function relLuminance(hex) {
  const c = hex.replace('#', '');
  const full = c.length === 3 ? c.split('').map((x) => x + x).join('') : c;
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16) / 255)
    .map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
function checkThemeContrast() {
  const box = $('contrastCheckResult');
  if (!box) return;
  const styles = getComputedStyle(document.body);
  const textColor = styles.getPropertyValue('--text').trim();
  const bgColor = styles.getPropertyValue('--bg').trim();
  try {
    const L1 = relLuminance(textColor.startsWith('#') ? textColor : '#f5f7fb');
    const L2 = relLuminance(bgColor.startsWith('#') ? bgColor : '#0b0d12');
    const ratio = (Math.max(L1, L2) + 0.05) / (Math.min(L1, L2) + 0.05);
    const passesAA = ratio >= 4.5;
    box.classList.remove('hidden');
    box.innerHTML = `<span>Kontrast teksta na pozadini (tekuća tema)</span><strong>${ratio.toFixed(2)}:1 — ${passesAA ? 'zadovoljava WCAG AA (≥4.5:1)' : 'ISPOD WCAG AA (4.5:1)'}</strong>`;
  } catch (e) { box.classList.remove('hidden'); box.textContent = 'Provera kontrasta nije uspela za ovu temu.'; }
}
function scheduleAutoCheck(minutes) { clearInterval(state.autoCheckTimer); const n=Number(minutes||0); if(!n) return; state.autoCheckTimer=setInterval(()=>{ if(state.status?.connected && !state.taskId) checkNewSongs(); },n*60*1000); }
async function saveAutomationSettings() { const minutes=Number(($('settingsAutoCheckMinutes')||$('autoCheckMinutes')).value||0); const backup=Boolean($('settingsAutoBackup')?.checked || $('autoBackupBeforeSync')?.checked); try { const data=await api('/api/settings',{method:'POST',body:{auto_check_minutes:minutes,auto_backup_before_sync:backup}}); scheduleAutoCheck(data.auto_check_minutes); toast('Automatizacija je sačuvana.','success'); } catch(e){ toast(e.message,'error'); } }
function renderSunoSessionStatus(session){
  const box=$('sunoSessionStatus'); if(!box)return;
  const connected=Boolean(session?.connected), enabled=session?.keepalive_enabled!==false;
  const status=String(session?.status||'waiting'); const last=session?.last_success_at||session?.token_updated_at||'';
  const failures=Number(session?.consecutive_failures||0);
  let text=String(session?.message||'Čeka se Suno povezivanje.');
  if(connected&&enabled) text+=` Interval: ${Number(session?.keepalive_minutes||4)} min.`;
  if(last) text+=` Poslednje uspešno osvežavanje: ${formatDate(last)}.`;
  if(failures) text+=` Uzastopnih neuspeha: ${failures}.`;
  box.textContent=text;
  box.className=`inline-message ${!connected?'warning':status==='warning'?'error':enabled?'success':'warning'}`;
}
async function saveSunoSessionSettings(){
  try{
    const data=await api('/api/settings',{method:'POST',body:{suno_keepalive_enabled:$('sunoKeepaliveEnabled').checked,suno_keepalive_minutes:Number($('sunoKeepaliveMinutes').value||4),suno_auto_reopen_browser:$('sunoAutoReopenBrowser').checked}});
    renderSunoSessionStatus(data.suno_session||{}); toast('Podešavanja Suno sesije su sačuvana.','success');
  }catch(e){toast(e.message,'error',10000);}
}
async function refreshSunoSessionNow(){
  const button=$('refreshSunoSessionNowBtn'); button.disabled=true;
  try{const data=await api('/api/connect/keepalive',{method:'POST',body:{verify_api:true},timeoutMs:60000});renderSunoSessionStatus(data.session||{});toast(data.message||'Suno sesija je osvežena.','success',9000);await loadStatus();}
  catch(e){toast(e.message,'error',12000);await loadStatus();}
  finally{button.disabled=false;}
}
function notifyNewSongs(count) { if (!$('notifyNewSongs')?.checked || !count) return; if ('Notification' in window) { if(Notification.permission==='granted') new Notification('Suno Pesme Studio',{body:`Pronađeno je ${count} novih pesama.`}); else if(Notification.permission==='default') Notification.requestPermission().then((p)=>{ if(p==='granted') new Notification('Suno Pesme Studio',{body:`Pronađeno je ${count} novih pesama.`}); }); } }
function requireSelection() { const ids=selectedIds(); if(!ids.length){ toast('Prvo izaberi pesme u Biblioteci.','error'); showView('library'); return null; } return ids; }
async function bulkUpdate(fields,message) { const ids=requireSelection(); if(!ids)return; try{const data=await api('/api/bulk/update',{method:'POST',body:{ids,fields}}); toast(`${message} (${data.count})`,'success'); await loadSongs();}catch(e){toast(e.message,'error');} }
async function bulkTags(mode) { const ids=requireSelection(); if(!ids)return; const tags=$('bulkTagsInput').value.trim(); if(mode!=='clear'&&!tags)return toast('Upiši oznake.','error'); try{const data=await api('/api/bulk/tags',{method:'POST',body:{ids,tags,mode}}); toast(`Oznake su obrađene za ${data.count} pesama.`,'success'); await loadSongs();}catch(e){toast(e.message,'error');} }
async function bulkPublished(published) { const ids=requireSelection(); if(!ids)return; try{const data=await api('/api/bulk/published',{method:'POST',body:{ids,published}}); toast(`${published?'Označeno kao objavljeno':'Vraćeno na neobjavljeno'}: ${data.count}`,'success'); await Promise.all([loadSongs(),loadCollections()]);}catch(e){toast(e.message,'error');} }
async function bulkFolder(slug) { const ids=requireSelection(); if(!ids)return; try{const data=await api('/api/bulk/collection',{method:'POST',body:{ids,slug}}); toast(`U folder je dodato ${data.count} pesama.`,'success'); await loadCollections();}catch(e){toast(e.message,'error');} }
async function quickAudioPreset(preset) { const ids=requireSelection(); if(!ids)return; try{await startBackground('/api/audio/quick-preset',{ids,preset}); showView('audio'); toast('Masovna audio radnja je pokrenuta.','success');}catch(e){toast(e.message,'error');} }
async function exportSpecial(path) { const ids=state.selected.size?selectedIds():null; try{const data=await api(path,{method:'POST',body:{ids},timeoutMs:120000}); downloadFromUrl(data.download_url); toast(`Izvoz je napravljen: ${data.path}`,'success',9000);}catch(e){toast(e.message,'error',10000);} }
function toolOutput(html){$('toolsOutput').innerHTML=html;}
async function runReport(){const type=$('reportType').value; toolOutput('Pripremam izveštaj...'); try{const data=await api(`/api/reports?type=${encodeURIComponent(type)}`,{timeoutMs:type==='audio_quality'?120000:30000}); if(type==='duplicates'){const a=data.report?.same_title_duration||[],b=data.report?.same_audio||[];toolOutput(`<h3>Isti naslov i trajanje: ${a.length}</h3>${a.map((x)=>`<div class="report-row"><strong>${escapeHtml(x.duplicate_key)}</strong><span>${escapeHtml(x.songs)}</span></div>`).join('')||'<p>Nema duplikata.</p>'}<h3>Isti audio sadržaj: ${b.length}</h3>${b.map((x)=>`<div class="report-row"><strong>${escapeHtml(x.duplicate_key.slice(0,16))}</strong><span>${escapeHtml(x.songs)}</span></div>`).join('')||'<p>Nema duplikata.</p>'}`);}else{const rows=data.rows||[];toolOutput(`<h3>Rezultati: ${rows.length}</h3>${rows.map((x)=>`<div class="report-row"><strong>${escapeHtml(x.title||x.id)}</strong><span>${x.duration!==undefined?formatDuration(x.duration):''} ${escapeHtml(x.source_group||x.codec||x.error||'')}</span></div>`).join('')||'<p>Nema stavki za ovaj izveštaj.</p>'}`);}}catch(e){toolOutput(`<p class="danger-text">${escapeHtml(e.message)}</p>`);} }

function youtubeNumber(value){const n=Number(value||0);return new Intl.NumberFormat('sr-RS',{notation:n>=10000?'compact':'standard',maximumFractionDigits:1}).format(n);}
function youtubeStatusLabel(status){return ({new:'Novo',review:'Za proveru',contacted:'Kontaktiran',resolved:'Rešeno',authorized:'Dozvoljeno',rejected:'Nije moja pesma'})[status]||status||'Novo';}

async function loadYoutubeCenter(){
  if(!$('youtubeChannelsList'))return;
  try{
    const completeness=encodeURIComponent($('youtubeAudioCompletenessFilter')?.value||'');
    const [channelsData,calendarData,matchesData,audioData,matrixData,coverageData,extraData]=await Promise.all([
      api('/api/youtube/channels'),
      api('/api/youtube/calendar?limit=1000'),
      api(`/api/youtube/matches?type=external_candidate&status=${encodeURIComponent($('youtubeMatchStatusFilter')?.value||'')}&limit=1000`),
      api(`/api/youtube/audio-analysis?completeness=${completeness}&limit=5000`),
      api('/api/youtube/publication-matrix'),
      api('/api/youtube/coverage'),
      api('/api/youtube/duplicate-mix-report')
    ]);
    state.youtubeChannels=channelsData.channels||[];
    state.youtubeCalendar=calendarData.rows||[];
    state.youtubeMatches=matchesData.matches||[];
    state.youtubeSummary=channelsData.summary||matchesData.summary||{};
    state.youtubeAudioResults=audioData.rows||[];
    state.youtubeAudioSummary=audioData.summary||{};
    state.youtubeMatrix=matrixData.matrix||{channels:[],rows:[]};
    state.youtubeCoverage=coverageData.coverage||{rows:[],channels:[],summary:{}};
    state.youtubeExtraReport=extraData.report||{duplicates:[],mixes:[],repeats:[],version_types:[]};
    state.ytdlp=audioData.ytdlp||null;
    state.youtubeOAuth=channelsData.oauth||null;
    renderYoutubeSummary(channelsData.has_api_key);
    renderYoutubeOAuth();renderYoutubeChannels();renderYoutubeCalendar();renderYoutubeMatches();
    renderYoutubeAudioSummary();renderYtdlpStatus();renderYoutubeAudioResults();renderYoutubeMatrix();renderYoutubeCoverage();renderYoutubeExtraReport();
    if($('copyrightOwnerNameInput')&&!$('copyrightOwnerNameInput').value&&state.status?.copyright_owner_name)$('copyrightOwnerNameInput').value=state.status.copyright_owner_name;
  }catch(e){
    if($('youtubeChannelsList'))$('youtubeChannelsList').innerHTML=`<div class="inline-message error">${escapeHtml(e.message)}</div>`;
    if($('youtubeAudioResultsList'))$('youtubeAudioResultsList').innerHTML=`<div class="inline-message error">${escapeHtml(e.message)}</div>`;
  }
}
function renderYoutubeSummary(hasKey){
  const s=state.youtubeSummary||{},oauth=state.youtubeOAuth||{};
  if($('youtubeSummaryBadges'))$('youtubeSummaryBadges').innerHTML=`<span>${Number(s.owned_channels||0)} mojih kanala</span><span>${Number(s.owned_publications||0)} objava</span><span>${Number(s.suspicious||0)} kandidata</span>`;
  if($('youtubeKeyStatus')){$('youtubeKeyStatus').className=`inline-message ${hasKey?'success':'info'}`;$('youtubeKeyStatus').textContent=hasKey?'Rezervni YouTube API ključ je sačuvan.':'API ključ nije potreban kada je Google nalog povezan.';}
  if($('youtubeOAuthBadge')){$('youtubeOAuthBadge').className=`oauth-badge ${oauth.connected?'connected':''}`;$('youtubeOAuthBadge').textContent=oauth.connected?`${Number(oauth.profile_count||0)} Google nalog(a) · ${Number(oauth.channel_count||0)} kanal(a)`:'Nije povezano';}
}
function renderYoutubeOAuth(){
  const oauth=state.youtubeOAuth||{configured:false,connected:false,profiles:[]};
  const status=$('youtubeOAuthStatus'),config=$('youtubeOAuthConfigStatus'),profiles=$('youtubeOAuthProfiles');
  if(status){status.className=`inline-message ${oauth.connected?'success':oauth.configured?'warning':'info'}`;status.textContent=oauth.connected?`YouTube je povezan. Automatski je pronađeno ${Number(oauth.channel_count||0)} kanala.`:oauth.configured?'Google prijava je spremna. Klikni veliko zeleno dugme i izaberi mejl.':'Prvi put klikni veliko zeleno dugme; program će zatražiti Google OAuth JSON, a zatim otvoriti izbor mejla.';}
  if(config){config.className=`inline-message ${oauth.configured?'success':'warning'}`;config.textContent=oauth.configured?`OAuth klijent je spreman${oauth.project_id?` · projekat ${oauth.project_id}`:''}${oauth.imported_from?` · ${oauth.imported_from}`:''}.`:'OAuth klijent još nije izabran. Ovo se radi samo jednom.';}
  if($('youtubeOAuthFirstSetup'))$('youtubeOAuthFirstSetup').open=!oauth.configured;
  if(!profiles)return;
  const rows=oauth.profiles||[];
  profiles.innerHTML=rows.length?rows.map(p=>`<article class="youtube-oauth-profile" data-profile-id="${escapeHtml(p.profile_id)}"><div class="oauth-avatar">G</div><div><strong>${escapeHtml(p.email||'Google nalog')}</strong><span>${(p.channel_titles||[]).map(escapeHtml).join(' · ')||'Kanal će biti pronađen posle osvežavanja'}${p.needs_reauth?' · POTREBNO PONOVNO POVEZIVANJE':''}</span></div><button class="btn ghost small youtube-oauth-disconnect">Odvoji</button></article>`).join(''):'';
  qsa('.youtube-oauth-disconnect',profiles).forEach(b=>b.addEventListener('click',async()=>{const id=b.closest('[data-profile-id]').dataset.profileId;if(!confirm('Odvojiti ovaj Google/YouTube nalog od programa?'))return;try{const d=await api('/api/youtube/oauth/disconnect',{method:'POST',body:{profile_id:id}});toast(d.message,'success');await loadYoutubeCenter();}catch(e){toast(e.message,'error',10000);}}));
}
async function importYoutubeOAuthClient(){
  try{const d=await api('/api/youtube/oauth/import-client',{method:'POST',body:{force_update:true},timeoutMs:180000});state.youtubeOAuth=d.oauth||state.youtubeOAuth;renderYoutubeOAuth();toast(d.message,'success',9000);return true;}catch(e){toast(e.message,'error',12000);return false;}
}
async function connectYoutubeGoogle(){
  try{
    if(!state.youtubeOAuth?.configured){const ok=await importYoutubeOAuthClient();if(!ok)return;}
    const beforeCount=Number(state.youtubeOAuth?.profile_count||0);const beforeConnected=Math.max(0,...((state.youtubeOAuth?.profiles||[]).map(p=>Number(p.connected_at||0))));const d=await api('/api/youtube/oauth/start',{method:'POST',body:{email:''},timeoutMs:30000});
    if(!d.opened&&d.url)window.open(d.url,'_blank','noopener');toast(d.message,'success',10000);$('youtubeOAuthStatus').textContent='Čekam da izabereš Google mejl u otvorenom prozoru...';
    let tries=0;const timer=setInterval(async()=>{tries+=1;try{const r=await api('/api/youtube/oauth/status');state.youtubeOAuth=r.oauth||{};renderYoutubeOAuth();const newest=Math.max(0,...((state.youtubeOAuth.profiles||[]).map(p=>Number(p.connected_at||0))));if(Number(state.youtubeOAuth.profile_count||0)>beforeCount||newest>beforeConnected){clearInterval(timer);toast('YouTube je uspešno povezan preko Google naloga.','success',10000);await loadYoutubeCenter();}else if(tries>=60){clearInterval(timer);toast('Povezivanje još nije završeno. Klikni ponovo ako si zatvorio Google prozor.','warning',9000);}}catch(_){if(tries>=60)clearInterval(timer);}},2000);
  }catch(e){toast(e.message,'error',12000);}
}
async function refreshYoutubeGoogle(){try{const d=await api('/api/youtube/oauth/refresh-channels',{method:'POST',body:{},timeoutMs:90000});toast(d.message,'success',9000);await loadYoutubeCenter();}catch(e){toast(e.message,'error',12000);}}
async function removeYoutubeOAuthConfig(){if(!confirm('Ukloniti Google OAuth podešavanje i sve sačuvane Google prijave iz ovog programa?'))return;try{const d=await api('/api/youtube/oauth/remove-config',{method:'POST',body:{}});state.youtubeOAuth=d.oauth||{};toast(d.message,'success');await loadYoutubeCenter();}catch(e){toast(e.message,'error',10000);}}
function renderYoutubeChannels(){
  const box=$('youtubeChannelsList');if(!box)return;
  if(!state.youtubeChannels.length){box.innerHTML='<div class="empty-inline">Nijedan YouTube kanal još nije dodat.</div>';return;}
  box.innerHTML=state.youtubeChannels.map(c=>{const r=c.shorts_report||{};const shortsHint=r.shorts_total?`<i>${r.shorts_checked}/${r.shorts_total} Shorts audio-provereno</i>`:'';const avatar=c.thumbnail_url?`<img src="${escapeHtml(c.thumbnail_url)}" alt="">`:'▶';return `<article class="youtube-channel-card" data-channel-id="${escapeHtml(c.channel_id)}"><div class="youtube-channel-avatar">${avatar}</div><div class="youtube-channel-main"><strong>${escapeHtml(c.title||c.channel_id)}</strong><span>${escapeHtml(c.handle||c.channel_id)}</span><div class="youtube-channel-meta"><i>${c.is_owned?'Moj kanal':'Praćeni kanal'}</i><i>${escapeHtml(c.source_mode||'api').toUpperCase()}</i>${c.google_email?`<i>${escapeHtml(c.google_email)}</i>`:''}<i>ID: ${escapeHtml(c.channel_id)}</i><i>${youtubeNumber(c.video_count)} videa</i>${shortsHint}<i>Poslednja provera: ${formatDate(c.last_scan_at)}</i></div></div><div class="youtube-channel-actions">${c.is_owned&&r.shorts_not_checked?`<button class="btn secondary small youtube-scan-shorts">Proveri audio za Shorts (${r.shorts_not_checked})</button>`:''}<button class="icon-btn youtube-open-channel" title="Otvori kanal">↗</button><button class="icon-btn youtube-delete-channel" title="Ukloni iz programa">×</button></div></article>`;}).join('');
  qsa('.youtube-open-channel',box).forEach(b=>b.addEventListener('click',()=>{const id=b.closest('[data-channel-id]').dataset.channelId;openExternal(`https://www.youtube.com/channel/${encodeURIComponent(id)}`);}));
  qsa('.youtube-delete-channel',box).forEach(b=>b.addEventListener('click',async()=>{const card=b.closest('[data-channel-id]');if(!confirm('Ukloniti ovaj kanal iz programa? Video-snimci i pesme se ne brišu.'))return;try{await api('/api/youtube/channel/delete',{method:'POST',body:{channel_id:card.dataset.channelId}});toast('Kanal je uklonjen.','success');loadYoutubeCenter();}catch(e){toast(e.message,'error');}}));
  qsa('.youtube-scan-shorts',box).forEach(b=>b.addEventListener('click',async()=>{const id=b.closest('[data-channel-id]').dataset.channelId;try{await startBackground('/api/youtube/channel/scan-shorts',{channel_id:id,scan_mode:'new'});toast('Audio provera za Shorts je pokrenuta.','success');}catch(e){toast(e.message,'error',12000);}}));
}
function renderYoutubeCalendar(){
  const box=$('youtubePublicationCalendar');if(!box)return;
  if(!state.youtubeCalendar.length){box.innerHTML='<div class="empty-inline">Još nema automatski pronađenih datuma. Dodaj svoje kanale i klikni „Proveri sve moje kanale“.</div>';return;}
  box.innerHTML=state.youtubeCalendar.map(r=>`<div class="youtube-date-row"><span class="date">${formatDate(r.published_at)}</span><div><strong>${escapeHtml(r.song_title)}</strong><br><small>${escapeHtml(youtubeCompletenessLabel(r.completeness_status||''))}</small></div><div><strong>${escapeHtml(r.video_title)}</strong><br><small>${escapeHtml(r.channel_title)}</small></div><span>${r.audio_checked_at?`${Number(r.coverage_percent||0).toFixed(0)}% pesme`:`${youtubeNumber(r.view_count)} pregleda`}</span><button class="btn ghost small youtube-open-video" data-url="${escapeHtml(r.video_url)}">Otvori</button></div>`).join('');
  qsa('.youtube-open-video',box).forEach(b=>b.addEventListener('click',()=>openExternal(b.dataset.url)));
}
function renderYoutubeMatches(){
  const box=$('youtubeMatchesList');if(!box)return;
  if(!state.youtubeMatches.length){box.innerHTML='<div class="empty-inline">Nema kandidata u izabranom statusu. Pokreni globalnu YouTube pretragu.</div>';return;}
  box.innerHTML=state.youtubeMatches.map(m=>{const audioConfirmed=!!m.audio_checked_at;const audioBadge=audioConfirmed?`<span class="match-score">AUDIO POTVRĐENO — ${escapeHtml(youtubeCompletenessLabel(m.completeness_status||''))}</span>`:'<span class="match-score warning">SAMO NASLOV — audio nije proveren</span>';return `<article class="youtube-match-card ${m.status!=='new'?'reviewed':''}" data-match-id="${m.id}" data-song-id="${escapeHtml(m.song_id)}" data-video-url="${escapeHtml(m.video_url||'')}"><img class="youtube-match-thumb" src="${escapeHtml(m.thumbnail_url||'')}" alt=""><div class="youtube-match-main">${audioBadge} <span class="match-score">${Number(m.score||0).toFixed(0)}% naslov</span><h3>${escapeHtml(m.video_title)}</h3><p class="song-link">Moguća pesma: ${escapeHtml(m.song_title)}</p><p>Kanal: ${escapeHtml(m.channel_title)} · Objavljeno: ${formatDate(m.published_at)} · ${youtubeNumber(m.view_count)} pregleda</p><p>${escapeHtml(m.reason||'')}</p>${audioConfirmed?'':'<p class="muted">Naslov je samo kandidat — dok se audio ne proveri, ovo NIJE dokaz da je ovo tvoja pesma.</p>'}</div><div class="youtube-match-actions"><select class="youtube-match-status"><option value="new" ${m.status==='new'?'selected':''}>Novo</option><option value="review" ${m.status==='review'?'selected':''}>Za proveru</option><option value="contacted" ${m.status==='contacted'?'selected':''}>Kontaktiran</option><option value="resolved" ${m.status==='resolved'?'selected':''}>Rešeno</option><option value="authorized" ${m.status==='authorized'?'selected':''}>Dozvoljeno</option><option value="rejected" ${m.status==='rejected'?'selected':''}>Nije moja pesma</option></select><input class="youtube-contact-email" type="email" value="${escapeHtml(m.contact_email||'')}" placeholder="Email kanala — ručno"><button class="btn secondary small youtube-review-video">Otvori video</button>${audioConfirmed?'':'<button class="btn primary small youtube-confirm-audio">Potvrdi audio</button>'}<button class="btn primary small youtube-contact-channel">Pripremi kontakt</button><button class="btn secondary small youtube-evidence-package">Dokazni paket</button><button class="btn ghost small youtube-web-search">Web pretraga</button></div></article>`;}).join('');
  qsa('.youtube-match-status',box).forEach(sel=>sel.addEventListener('change',async()=>{const card=sel.closest('[data-match-id]');try{await api('/api/youtube/match/update',{method:'POST',body:{id:Number(card.dataset.matchId),fields:{status:sel.value}}});toast(`Status: ${youtubeStatusLabel(sel.value)}`,'success');}catch(e){toast(e.message,'error');}}));
  qsa('.youtube-contact-email',box).forEach(inp=>inp.addEventListener('change',async()=>{const card=inp.closest('[data-match-id]');try{await api('/api/youtube/match/update',{method:'POST',body:{id:Number(card.dataset.matchId),fields:{contact_email:inp.value.trim()}}});}catch(e){toast(e.message,'error');}}));
  qsa('.youtube-review-video',box).forEach(b=>b.addEventListener('click',()=>{const m=state.youtubeMatches.find(x=>String(x.id)===b.closest('[data-match-id]').dataset.matchId);if(m)openExternal(m.video_url);}));
  qsa('.youtube-confirm-audio',box).forEach(b=>b.addEventListener('click',async()=>{const card=b.closest('[data-match-id]');const m=state.youtubeMatches.find(x=>String(x.id)===card.dataset.matchId);if(!m?.video_url)return toast('Video link nije dostupan za ovaj kandidat.','error');try{await startBackground('/api/youtube/audio-analyze-url',{url:m.video_url,is_owned:m.match_type==='owned_publication',song_ids:m.song_id?[m.song_id]:[],force:false});toast('Audio potvrda je pokrenuta za ovaj video.','success');}catch(e){toast(e.message,'error',12000);}}));
  qsa('.youtube-contact-channel',box).forEach(b=>b.addEventListener('click',()=>prepareYoutubeContact(Number(b.closest('[data-match-id]').dataset.matchId))));
  qsa('.youtube-evidence-package',box).forEach(b=>b.addEventListener('click',()=>downloadYoutubeEvidence(Number(b.closest('[data-match-id]').dataset.matchId))));
  qsa('.youtube-web-search',box).forEach(b=>b.addEventListener('click',()=>openWebSearch(b.closest('[data-match-id]').dataset.songId)));
}

function youtubeCompletenessLabel(status){
  return ({complete:'Kompletna pesma',almost_complete:'Skoro kompletna',partial:'Delimično objavljena',short_clip:'Kratak isečak',different_audio:'Drugi / nepotvrđen audio','':'Nije audio-provereno'})[status]||status||'Nije audio-provereno';
}
function youtubeCompletenessClass(status){
  return ({complete:'complete',almost_complete:'almost',partial:'partial',short_clip:'clip',different_audio:'different','':'unchecked'})[status]||'unchecked';
}
function renderYoutubeAudioSummary(){
  const s=state.youtubeAudioSummary||{};
  const box=$('youtubeAudioSummaryBadges');if(!box)return;
  box.innerHTML=`<span class="summary-good">${Number(s.complete||0)} kompletnih</span><span>${Number(s.almost_complete||0)} skoro kompletnih</span><span class="summary-warn">${Number(s.partial||0)} delimičnih</span><span>${Number(s.short_clip||0)} isečaka</span><span>${Number(s.mix_videos||0)} mix videa</span><span>${Number(s.multi_segment_matches||0)} višedelnih poklapanja</span><span>${Number(s.fingerprints||0)} otisaka</span>`;
}
function renderYtdlpStatus(){
  const box=$('ytdlpStatus');if(!box)return;
  const y=state.ytdlp||{};
  box.className=`inline-message ${y.ready?'success':'warning'}`;
  box.textContent=y.ready?`yt-dlp je spreman${y.version?` · verzija ${y.version}`:''}.`:'yt-dlp nije pripremljen. Klikni dugme za instalaciju/proveru.';
}
function jsonList(value){if(Array.isArray(value))return value;if(!value)return[];try{const x=JSON.parse(value);return Array.isArray(x)?x:[];}catch{return[];}}
function youtubeMissingText(row){
  const start=Number(row.missing_start||0),end=Number(row.missing_end||0),internal=Number(row.internal_gap_seconds||0);
  const parts=[];
  if(start>1.5)parts.push(`nedostaje početak ${formatDuration(start)}`);
  if(end>1.5)parts.push(`nedostaje kraj ${formatDuration(end)}`);
  if(internal>1.5)parts.push(`unutrašnje praznine ${formatDuration(internal)}`);
  if(Number(row.has_intro||0))parts.push(`YouTube uvod ${formatDuration(Number(row.video_start||0))}`);
  if(Number(row.has_outro||0))parts.push('dodat završetak na videu');
  if(Number(row.occurrence_count||0)>1)parts.push(`pesma se ponavlja ${Number(row.occurrence_count)} puta`);
  if(Number(row.video_song_count||0)>1)parts.push(`video sadrži ${Number(row.video_song_count)} Suno pesme`);
  return parts.length?parts.join(' · '):'Nisu pronađeni značajni isečeni delovi.';
}
function audioSegmentMap(row){
  const duration=Math.max(1,Number(row.song_duration||0)),segments=jsonList(row.segments_json||row.segments),gaps=jsonList(row.missing_intervals_json||row.missing_intervals);
  if(!segments.length&&!gaps.length)return'';
  const bars=segments.map((x,i)=>{const left=Math.max(0,Math.min(100,Number(x.source_start||0)/duration*100)),width=Math.max(.7,Math.min(100-left,(Number(x.source_end||0)-Number(x.source_start||0))/duration*100));return `<i class="segment-hit" style="left:${left}%;width:${width}%" title="Segment ${i+1}: ${formatDuration(x.source_start)}–${formatDuration(x.source_end)}"></i>`;}).join('');
  return `<div class="audio-segment-map" title="Zelene zone su potvrđeni delovi Suno originala"><span></span>${bars}</div>`;
}

function renderYoutubeAudioResults(){
  const box=$('youtubeAudioResultsList');if(!box)return;
  const rows=state.youtubeAudioResults||[];
  if(!rows.length){box.innerHTML='<div class="empty-inline">Nema audio-proverenih YouTube objava u izabranom filteru. Pokreni „Analiziraj pesme sa mojih kanala“.</div>';return;}
  box.innerHTML=rows.map(r=>{
    const status=String(r.completeness_status||'');
    const audio=Math.max(0,Math.min(100,Number(r.audio_score||0)));
    const coverage=Math.max(0,Math.min(100,Number(r.coverage_percent||0)));
    return `<article class="youtube-audio-card ${youtubeCompletenessClass(status)}" data-match-id="${Number(r.id||0)}" data-song-id="${escapeHtml(r.song_id)}" data-video-id="${escapeHtml(r.video_id)}">
      <img class="youtube-audio-thumb" src="${escapeHtml(r.thumbnail_url||'')}" alt="">
      <div class="youtube-audio-card-main">
        <div class="youtube-audio-title-row"><span class="youtube-complete-badge ${youtubeCompletenessClass(status)}">${escapeHtml(youtubeCompletenessLabel(status))}</span><span class="confidence">Pouzdanost: ${escapeHtml(r.confidence||'nije određena')}</span></div>
        <h3>${escapeHtml(r.video_title||r.video_id)}</h3>
        <p class="audio-original">Suno original: <strong>${escapeHtml(r.song_title||r.song_id)}</strong></p>
        <p>${escapeHtml(r.channel_title||r.channel_id)} · ${formatDate(r.published_at)} · YouTube ${formatDuration(r.video_duration)} / Suno ${formatDuration(r.song_duration)}</p>
        <div class="audio-score-grid"><div><span>Audio podudaranje</span><strong>${audio.toFixed(1)}%</strong><i><b style="width:${audio}%"></b></i></div><div><span>Pokrivenost originala</span><strong>${coverage.toFixed(1)}%</strong><i><b style="width:${coverage}%"></b></i></div></div>
        <p class="missing-info">${escapeHtml(youtubeMissingText(r))}</p>
        <p class="matched-range">Pronađeno ${formatDuration(r.matched_seconds)} originala · Suno ${formatDuration(r.source_start)}–${formatDuration(r.source_end)} · video ${formatDuration(r.video_start)}–${formatDuration(r.video_end)}</p>
        ${audioSegmentMap(r)}
        <p class="audio-version-line"><strong>${escapeHtml(r.version_type||'nepoznata verzija')}</strong> · ${Number(r.segment_count||0)} segment(a)${Number(r.video_song_count||0)>1?` · ${Number(r.video_song_count)} pesme u videu`:''}</p>
        ${r.recommendation?`<p class="audio-recommendation">${escapeHtml(r.recommendation)}</p>`:''}
      </div>
      <div class="youtube-audio-actions"><button class="btn primary small youtube-audio-play-video">Otvori video</button><button class="btn secondary small youtube-audio-open-suno">Otvori Suno</button><button class="btn secondary small youtube-audio-recheck">Ponovo analiziraj</button><button class="btn ghost small youtube-audio-change-original">Promeni original</button></div>
    </article>`;
  }).join('');
  qsa('.youtube-audio-play-video',box).forEach(b=>b.addEventListener('click',()=>{const r=state.youtubeAudioResults.find(x=>String(x.id)===b.closest('[data-match-id]').dataset.matchId);if(r)openExternal(r.video_url);}));
  qsa('.youtube-audio-open-suno',box).forEach(b=>b.addEventListener('click',()=>{const card=b.closest('[data-song-id]');const song=state.songs.find(x=>String(x.id)===card.dataset.songId);openExternal(song?.source_url||`https://suno.com/song/${encodeURIComponent(card.dataset.songId)}`);}));
  qsa('.youtube-audio-recheck',box).forEach(b=>b.addEventListener('click',()=>analyzeYoutubeMatch(Number(b.closest('[data-match-id]').dataset.matchId))));
  qsa('.youtube-audio-change-original',box).forEach(b=>b.addEventListener('click',()=>manualLinkYoutubeMatch(b.closest('[data-video-id]').dataset.videoId)));
}
function renderYoutubeMatrix(){
  const box=$('youtubePublicationMatrix');if(!box)return;
  const matrix=state.youtubeMatrix||{channels:[],rows:[]},channels=matrix.channels||[],rows=matrix.rows||[];
  if(!channels.length){box.innerHTML='<div class="empty-inline">Dodaj najmanje jedan kanal označen kao „moj kanal“.</div>';return;}
  if(!rows.length){box.innerHTML='<div class="empty-inline">Suno biblioteka je prazna.</div>';return;}
  box.innerHTML=`<table class="youtube-matrix-table"><thead><tr><th>Suno pesma</th>${channels.map(c=>`<th>${escapeHtml(c.title||c.channel_id)}</th>`).join('')}</tr></thead><tbody>${rows.map(row=>{
    const song=row.song||{},cells=row.channels||{};
    return `<tr><th><strong>${escapeHtml(song.title||song.id)}</strong><small>${formatDuration(song.duration)}</small></th>${channels.map(c=>{const cell=cells[String(c.channel_id)];if(!cell)return '<td><span class="matrix-status missing">Nije pronađena</span></td>';const st=String(cell.completeness_status||''),combined=String(cell.combined_status||''),combinedCoverage=Number(cell.combined_coverage_percent||0),videoCount=Number(cell.combined_video_count||0);const label=combined==='complete'&&st!=='complete'?'Kompletna zbirno':youtubeCompletenessLabel(st||combined);const cls=youtubeCompletenessClass(st||combined);return `<td data-video-url="${escapeHtml(cell.video_url||'')}"><span class="matrix-status ${cls}">${escapeHtml(label)}</span><small>${Number(cell.audio_score||0).toFixed(0)}% audio · najbolji video ${Number(cell.coverage_percent||0).toFixed(0)}%</small><small>Zbirno ${combinedCoverage.toFixed(0)}% kroz ${videoCount} videa</small>${cell.video_url?'<button class="matrix-open-video">Otvori</button>':''}</td>`;}).join('')}</tr>`;
  }).join('')}</tbody></table>`;
  qsa('.matrix-open-video',box).forEach(b=>b.addEventListener('click',()=>openExternal(b.closest('[data-video-url]').dataset.videoUrl)));
}
function renderYoutubeCoverage(){
  const box=$('youtubeCoverageList'),summaryBox=$('youtubeCoverageSummary');if(!box)return;
  const data=state.youtubeCoverage||{rows:[],summary:{}},rows=data.rows||[],summary=data.summary||{};
  if(summaryBox)summaryBox.innerHTML=`<span class="summary-good">${Number(summary.complete||0)} kompletnih</span><span>${Number(summary.almost_complete||0)} skoro kompletnih</span><span class="summary-warn">${Number(summary.partial||0)} delimičnih</span><span>${Number(summary.short_clip||0)} samo isečak</span><span>${Number(summary.missing||0)} nisu pronađene</span>`;
  const useful=rows.filter(r=>r.status!=='missing'||Number(r.video_count||0)>0).slice(0,1000);
  if(!useful.length){box.innerHTML='<div class="empty-inline">Nema zbirnih rezultata. Pokreni audio analizu svojih kanala.</div>';return;}
  const labels={complete:'Kompletna',almost_complete:'Skoro kompletna',partial:'Delimična',short_clip:'Samo isečak',missing:'Nije pronađena'};
  box.innerHTML=useful.map(r=>{const song=r.song||{},channel=r.channel||{},coverage=Math.max(0,Math.min(100,Number(r.coverage_percent||0))),gaps=(r.missing_intervals||[]).map(x=>`${formatDuration(x[0])}–${formatDuration(x[1])}`).join(', ')||'nema';return `<article class="coverage-row ${escapeHtml(r.status||'missing')}"><div><strong>${escapeHtml(song.title||song.id)}</strong><span>${escapeHtml(channel.title||channel.channel_id)}</span></div><div class="coverage-meter"><i><b style="width:${coverage}%"></b></i><strong>${coverage.toFixed(1)}%</strong></div><div><span class="matrix-status ${youtubeCompletenessClass(r.status==='missing'?'':r.status)}">${escapeHtml(labels[r.status]||r.status)}</span><small>${Number(r.video_count||0)} videa · nedostaje: ${escapeHtml(gaps)}</small></div><p>${escapeHtml(r.recommendation||'')}</p></article>`;}).join('');
}
function renderYoutubeExtraReport(){
  const box=$('youtubeExtraReport');if(!box)return;const r=state.youtubeExtraReport||{},duplicates=r.duplicates||[],mixes=r.mixes||[],repeats=r.repeats||[],types=r.version_types||[];
  box.innerHTML=`<div class="extra-report-grid"><section><h3>Više objava iste pesme: ${duplicates.length}</h3>${duplicates.slice(0,30).map(x=>`<div class="report-row"><strong>${escapeHtml(x.song_title)}</strong><span>${escapeHtml(x.channel_title)} · ${Number(x.video_count)} videa</span></div>`).join('')||'<p>Nema potvrđenih duplih objava.</p>'}</section><section><h3>Mix / više pesama: ${mixes.length}</h3>${mixes.slice(0,30).map(x=>`<div class="report-row"><strong>${escapeHtml(x.video_title)}</strong><span>${Number(x.song_count)} pesme · ${escapeHtml(x.song_titles||'')}</span></div>`).join('')||'<p>Nema potvrđenih mix videa.</p>'}</section><section><h3>Ponovljena pesma u videu: ${repeats.length}</h3>${repeats.slice(0,30).map(x=>`<div class="report-row"><strong>${escapeHtml(x.song_title)}</strong><span>${Number(x.occurrence_count)} pojavljivanja · ${escapeHtml(x.video_title)}</span></div>`).join('')||'<p>Nema ponovljenih pojavljivanja.</p>'}</section><section><h3>Tipovi verzija</h3>${types.map(x=>`<div class="report-row"><strong>${escapeHtml(x.version_type)}</strong><span>${Number(x.count)}</span></div>`).join('')||'<p>Još nema klasifikovanih verzija.</p>'}</section></div>`;
}
async function buildSunoFingerprintIndex(){const ids=$('youtubeAudioSelectedOnly')?.checked?selectedIds():[];try{await startBackground('/api/youtube/fingerprint-index',{song_ids:ids,force:$('youtubeAudioForce')?.checked});$('taskPanel').classList.remove('hidden');toast('Pravljenje Suno audio indeksa je pokrenuto. Sledeće analize će biti brže.','success');}catch(e){toast(e.message,'error',12000);}}
async function analyzeYoutubeUrl(){const url=$('youtubeManualVideoUrl').value.trim();if(!url)return toast('Nalepi YouTube link videa.','error');const ids=$('youtubeAudioSelectedOnly')?.checked?selectedIds():[];try{await startBackground('/api/youtube/audio-analyze-url',{url,is_owned:$('youtubeManualOwned').checked,song_ids:ids,candidate_limit:Number($('youtubeAudioCandidateLimit').value||16),deep:$('youtubeAudioDeep').checked,reuse_cache:$('youtubeAudioReuseCache').checked,force:$('youtubeAudioForce').checked,detect_multiple:$('youtubeAudioDetectMultiple').checked,max_songs_per_video:Number($('youtubeAudioMaxSongsPerVideo').value||6)});$('taskPanel').classList.remove('hidden');toast('Analiza YouTube linka je pokrenuta.','success');}catch(e){toast(e.message,'error',12000);}}
async function exportYoutubeCoverageReport(){try{const d=await api('/api/youtube/coverage-report/export',{method:'POST',body:{},timeoutMs:120000});downloadFromUrl(d.download_url);toast(d.message,'success',9000);}catch(e){toast(e.message,'error',12000);}}

async function installYtdlp(){const b=$('installYtdlpBtn');b.disabled=true;try{const d=await api('/api/tools/ytdlp/install',{method:'POST',body:{force_update:true},timeoutMs:180000});state.ytdlp=d;renderYtdlpStatus();toast(d.message||'yt-dlp je spreman.','success',9000);}catch(e){toast(e.message,'error',12000);}finally{b.disabled=false;}}
async function analyzeOwnedYoutubeAudio(){
  const selectedOnly=$('youtubeAudioSelectedOnly').checked,ids=selectedOnly?selectedIds():[];
  if(selectedOnly&&!ids.length)return toast('Izaberi najmanje jednu Suno pesmu u Biblioteci ili isključi opciju „samo izabrane“.','error',9000);
  try{await startBackground('/api/youtube/audio-analyze-owned',{song_ids:ids,max_videos_per_channel:Number($('youtubeAudioMaxVideos').value||100),candidate_limit:Number($('youtubeAudioCandidateLimit').value||16),deep:$('youtubeAudioDeep').checked,reuse_cache:$('youtubeAudioReuseCache').checked,force:$('youtubeAudioForce').checked,max_pages:Number($('youtubeChannelScanPages').value||20),scan_mode:$('youtubeAudioScanMode').value||'new',detect_multiple:$('youtubeAudioDetectMultiple').checked,max_songs_per_video:Number($('youtubeAudioMaxSongsPerVideo').value||6),cache_days:Number($('youtubeAudioCacheDays').value||30),cache_gb:10});$('taskPanel').classList.remove('hidden');$('taskPanel').scrollIntoView({behavior:'smooth'});toast('YouTube ↔ Suno audio analiza je pokrenuta.','success');}catch(e){toast(e.message,'error',12000);}
}
async function analyzeYoutubeMatch(matchId){try{await startBackground('/api/youtube/audio-analyze-match',{id:matchId,force:true});$('taskPanel').classList.remove('hidden');toast('Ponovna audio analiza je pokrenuta.','success');}catch(e){toast(e.message,'error',12000);}}
async function manualLinkYoutubeMatch(videoId){
  if(!state.songs.length)await loadSongs();
  const search=prompt('Upiši deo naslova Suno originala koji želiš da povežeš sa ovim YouTube videom:','');if(search===null)return;
  const q=search.trim().toLowerCase();const found=state.songs.filter(s=>!q||String(s.title||'').toLowerCase().includes(q)).slice(0,20);
  if(!found.length)return toast('Nije pronađena Suno pesma sa tim naslovom.','error');
  const menu=found.map((s,i)=>`${i+1}. ${s.title} (${formatDuration(s.duration)})`).join('\n');const picked=prompt(`Izaberi broj Suno originala:\n\n${menu}`,'1');if(picked===null)return;const song=found[Number(picked)-1];if(!song)return toast('Izbor nije ispravan.','error');
  try{const d=await api('/api/youtube/manual-link',{method:'POST',body:{song_id:song.id,video_id:videoId}});toast(d.message,'success',9000);await analyzeYoutubeMatch(Number(d.match.id));}catch(e){toast(e.message,'error',12000);}
}
async function exportYoutubeAudioReport(){try{const d=await api('/api/youtube/audio-report/export',{method:'POST',body:{},timeoutMs:120000});downloadFromUrl(d.download_url);toast(d.message,'success',9000);}catch(e){toast(e.message,'error',12000);}}
async function cleanupYoutubeAudioCache(){if(!confirm('Obrisati stari lokalni YouTube audio-keš? Sačuvani rezultati analize ostaju u bazi.'))return;try{const d=await api('/api/youtube/audio-cache/cleanup',{method:'POST',body:{days:Number($('youtubeAudioCacheDays')?.value||30),gb:10},timeoutMs:120000});toast(d.message,'success');await loadYoutubeCenter();}catch(e){toast(e.message,'error',12000);}}

async function saveYoutubeSettings(clear=false){try{const body={copyright_owner_name:$('copyrightOwnerNameInput').value.trim(),cookies_browser:$('youtubeCookiesBrowser')?.value||'none'};if(clear)body.api_key='';else if($('youtubeApiKeyInput').value.trim())body.api_key=$('youtubeApiKeyInput').value.trim();const d=await api('/api/youtube/settings',{method:'POST',body});$('youtubeApiKeyInput').value='';toast(d.message,'success');await loadStatus();await loadYoutubeCenter();}catch(e){toast(e.message,'error',9000);}}
async function addYoutubeChannel(){const reference=$('youtubeChannelInput').value.trim();if(!reference)return toast('Upiši kanal, @handle ili channel ID.','error');try{const d=await api('/api/youtube/channel/add',{method:'POST',body:{reference,is_owned:$('youtubeChannelOwned').checked},timeoutMs:60000});$('youtubeChannelInput').value='';toast(d.message,'success');loadYoutubeCenter();}catch(e){toast(e.message,'error',12000);}}
async function scanOwnedYoutube(){try{await startBackground('/api/youtube/scan-owned',{max_pages:Number($('youtubeChannelScanPages').value||20),threshold:68,include_private_unlisted:$('youtubeIncludePrivate')?.checked!==false});$('taskPanel').classList.remove('hidden');$('taskPanel').scrollIntoView({behavior:'smooth'});}catch(e){toast(e.message,'error',12000);}}
async function scanGlobalYoutube(){const ids=$('youtubeSearchSelectedOnly').checked?selectedIds():[];if($('youtubeSearchSelectedOnly').checked&&!ids.length)toast('Nijedna pesma nije izabrana — program će koristiti prve pesme iz biblioteke.','info');try{await startBackground('/api/youtube/scan-global',{ids,max_songs:Number($('youtubeGlobalMaxSongs').value||50),threshold:Number($('youtubeMatchThreshold').value||62),results_per_song:Number($('youtubeResultsPerQuery')?.value||20),query_variants:Number($('youtubeQueryVariants')?.value||3),max_pages:Number($('youtubeSearchPages')?.value||1),include_owned_channels:Boolean($('youtubeIncludeOwnedGlobal')?.checked),api_call_budget:80});$('taskPanel').classList.remove('hidden');$('taskPanel').scrollIntoView({behavior:'smooth'});}catch(e){toast(e.message,'error',15000);}}
async function openExternal(url){try{await api('/api/youtube/open-url',{method:'POST',body:{url}});}catch(e){toast(e.message,'error');}}
async function openWebSearch(songId){try{const d=await api(`/api/youtube/search-links?song_id=${encodeURIComponent(songId)}`);toolOutput(`<h3>Ručna internet pretraga</h3><p class="muted">Otvori više izvora. Rezultati pretraživača su samo tragovi za ručnu proveru.</p><div class="button-row"><button id="openYoutubeSearchDynamic" class="btn primary">YouTube</button><button id="openGoogleSearchDynamic" class="btn secondary">Google</button><button id="openBingSearchDynamic" class="btn ghost">Bing</button></div>`);$('openYoutubeSearchDynamic').addEventListener('click',()=>openExternal(d.youtube));$('openGoogleSearchDynamic').addEventListener('click',()=>openExternal(d.google));$('openBingSearchDynamic').addEventListener('click',()=>openExternal(d.bing));$('toolsOutput').scrollIntoView({behavior:'smooth'});}catch(e){toast(e.message,'error');}}
async function downloadYoutubeEvidence(matchId){try{const d=await api('/api/youtube/evidence',{method:'POST',body:{id:matchId,owner_name:$('copyrightOwnerNameInput').value.trim()},timeoutMs:60000});downloadFromUrl(d.download_url);toast(d.message,'success',9000);}catch(e){toast(e.message,'error',12000);}}
async function exportYoutubeCalendar(){try{const d=await api('/api/youtube/calendar/export',{method:'POST',body:{},timeoutMs:60000});downloadFromUrl(d.download_url);toast(d.message,'success',9000);}catch(e){toast(e.message,'error',12000);}}
async function prepareYoutubeContact(matchId){
  try{
    const owner=$('copyrightOwnerNameInput').value.trim();const d=await api(`/api/youtube/contact-draft?id=${matchId}&owner_name=${encodeURIComponent(owner)}`);const email=state.youtubeMatches.find(x=>Number(x.id)===matchId)?.contact_email||'';
    toolOutput(`<div class="youtube-legal-notice"><strong>Važno:</strong> pregledaj video, proveri da li postoji dozvola/licenca i razmotri fair use pre slanja. Program ne donosi pravnu odluku.</div><h3>Poruka za kanal</h3><div id="youtubeContactDraftText" class="youtube-contact-draft">${escapeHtml(d.draft)}</div><div class="button-row"><button id="copyYoutubeDraftBtn" class="btn primary">Kopiraj poruku</button><button id="openChannelAboutBtn" class="btn secondary">Otvori kontakt kanala</button>${email?'<button id="openMailDraftBtn" class="btn success">Otvori email nacrt</button>':''}<button id="openCopyrightToolDraftBtn" class="btn ghost">YouTube Copyright alati</button></div>`);
    $('copyYoutubeDraftBtn').addEventListener('click',async()=>{await navigator.clipboard.writeText(d.draft);toast('Poruka je kopirana.','success');});
    $('openChannelAboutBtn').addEventListener('click',()=>openExternal(d.channel_about_url));
    if(email)$('openMailDraftBtn').addEventListener('click',async()=>{const subject=encodeURIComponent(`Molba za proveru korišćenja pesme: ${d.match.song_title}`);window.location.href=`mailto:${encodeURIComponent(email)}?subject=${subject}&body=${encodeURIComponent(d.draft)}`;await api('/api/youtube/match/update',{method:'POST',body:{id:matchId,fields:{contact_status:'drafted',contacted_at:new Date().toISOString(),status:'contacted'}}});toast('Otvoren je email nacrt. Program ne može da potvrdi da je poruka poslata.','info',9000);});
    $('openCopyrightToolDraftBtn').addEventListener('click',()=>openExternal(d.copyright_match_url));
    $('toolsOutput').scrollIntoView({behavior:'smooth'});
  }catch(e){toast(e.message,'error',12000);}
}


function statusPill(text, kind='info'){ return `<span class="status-pill ${kind}">${escapeHtml(text)}</span>`; }
async function loadAdvancedStatus(){
  if(!$('advancedStatusOutput')) return;
  try{
    const data=await api('/api/advanced/status'); state.advancedStatus=data;
    const cp=data.checkpoints||[], jobs=data.jobs||[], schedules=data.schedules||[], plugins=data.plugins||{};
    $('syncCheckpointStatus').textContent=cp.length?`Sačuvano ${cp.length} tačaka nastavka sinhronizacije.`:'Nema sačuvanog nastavka.';
    $('syncCheckpointStatus').className=`inline-message ${cp.length?'success':'info'}`;
    const stem=plugins.stems||{}, tr=plugins.transcription||{}, pan=plugins.panako||plugins.advanced_fingerprint||{}, chr=plugins.chromaprint||{};
    if($('pluginStatusBox')) $('pluginStatusBox').innerHTML=`<strong>Dodaci</strong><br>Stemovi: ${stem.installed?'spremni':'nisu instalirani'} · Transkripcija: ${tr.installed?'spremna':'nije instalirana'} · Panako: ${pan.installed?'spreman':'opciono'} · Chromaprint: ${chr.installed?'spreman':'opciono'}`;
    renderPersistentJobs(jobs); renderScheduleStatus(schedules);
    const backups=data.incremental_backups||[];
    $('advancedStatusOutput').innerHTML=`<h3>Stanje naprednih sistema</h3><div class="status-grid"><div>${statusPill(`${cp.length} checkpoint-a`,cp.length?'success':'info')}<p>Nastavak Suno sinhronizacije.</p></div><div>${statusPill(`${jobs.length} poslova`,jobs.some(j=>['failed','interrupted','paused'].includes(j.status))?'warning':'success')}<p>Red koji preživljava restart.</p></div><div>${statusPill(`${backups.length} backup-a`,'success')}<p>Inkrementalni snapshot-i.</p></div></div><h3>Inkrementalni backup-i</h3>${backups.map(b=>`<div class="job-row"><div><strong>${escapeHtml(b.backup_id)}</strong><span>${escapeHtml(b.created_at||'')} · ${Number(b.files||0)} fajlova</span></div><button class="btn ghost small restore-incremental" data-path="${escapeHtml(b.path)}">Vrati jednu pesmu</button></div>`).join('')||'<p class="muted">Još nema inkrementalnog backup-a.</p>'}`;
    qsa('.restore-incremental',$('advancedStatusOutput')).forEach(b=>b.addEventListener('click',()=>restoreIncrementalItem(b.dataset.path)));
    if($('youtubeScheduleMinutes')){const y=schedules.find(x=>x.task_type==='youtube_owned'); if(y)$('youtubeScheduleMinutes').value=String(y.enabled?y.interval_minutes:0);}
  }catch(e){$('advancedStatusOutput').innerHTML=`<p class="danger-text">${escapeHtml(e.message)}</p>`;}
}
function renderPersistentJobs(jobs){
  const box=$('persistentJobsList'); if(!box)return;
  box.innerHTML=(jobs||[]).map(j=>`<div class="job-row"><div><strong>${escapeHtml(j.title||j.task_type)}</strong><span>${escapeHtml(j.status)} · ${Number(j.progress||0).toFixed(0)}% · ${escapeHtml(j.updated_at||'')}</span></div><div class="button-row">${['failed','interrupted','paused','queued'].includes(j.status)?`<button class="btn primary small resume-job" data-id="${j.id}">Nastavi</button>`:''}<button class="btn ghost small delete-job" data-id="${j.id}">Ukloni</button></div></div>`).join('')||'<p class="muted">Nema sačuvanih poslova.</p>';
  qsa('.resume-job',box).forEach(b=>b.addEventListener('click',async()=>{try{const d=await api('/api/jobs/resume',{method:'POST',body:{id:Number(b.dataset.id)}});state.taskId=d.task?.id;renderTask(d.task);toast('Posao je nastavljen.','success');}catch(e){toast(e.message,'error');}}));
  qsa('.delete-job',box).forEach(b=>b.addEventListener('click',async()=>{try{await api('/api/jobs/delete',{method:'POST',body:{id:Number(b.dataset.id)}});loadAdvancedStatus();}catch(e){toast(e.message,'error');}}));
}
function renderScheduleStatus(schedules){const box=$('scheduleStatusBox');if(!box)return;const y=(schedules||[]).find(x=>x.task_type==='youtube_owned');box.textContent=y&&y.enabled?`Automatska YouTube provera: svakih ${y.interval_minutes} min · sledeće ${y.next_run_at||''}`:'Automatska YouTube provera je isključena.';box.className=`inline-message ${y&&y.enabled?'success':'info'}`;}
async function resetSyncCheckpoints(){if(!confirm('Resetovati sačuvani nastavak? Sledeća sinhronizacija kreće od početka.'))return;try{await api('/api/sync/checkpoints/clear',{method:'POST',body:{prefix:''}});toast('Sačuvani nastavak je obrisan.','success');loadAdvancedStatus();}catch(e){toast(e.message,'error');}}
async function restoreIncrementalItem(snapshot){
  const songId=prompt('Nalepi Suno ID pesme koju želiš da vratiš:',''); if(!songId)return;
  const field=prompt('Polje (ostavi prazno za sve): local_audio, local_wav, local_lyrics, local_cover, local_lrc, local_srt','')||'';
  try{const d=await api('/api/backup/incremental/restore-item',{method:'POST',body:{snapshot,song_id:songId.trim(),field:field.trim()},timeoutMs:180000});toast(`Vraćeno je ${d.count||0} fajlova.`,'success',10000);await Promise.all([loadSongs(false),loadAdvancedStatus()]);}catch(e){toast(e.message,'error',12000);}
}
async function runIncrementalBackup(){let folder=$('incrementalBackupDir').value.trim();if(!folder){folder=await chooseFolder('incrementalBackupDir');if(!folder)return;}try{const d=await startBackground('/api/backup/incremental',{folder,include_files:$('incrementalIncludeFiles').checked});toast('Inkrementalni backup je pokrenut.','success');return d;}catch(e){toast(e.message,'error',12000);}}
async function runRelocate(){let folder=$('relocateRootDir').value.trim();if(!folder){folder=await chooseFolder('relocateRootDir');if(!folder)return;}try{await startBackground('/api/files/relocate',{root:folder});toast('Traženje premeštenih fajlova je pokrenuto.','success');}catch(e){toast(e.message,'error',12000);}}
async function runQuality(){const ids=state.selected.size?selectedIds():[];try{await startBackground('/api/audio/quality',{ids,all:!ids.length});toast(ids.length?`Analiza kvaliteta ${ids.length} pesama je pokrenuta.`:'Analiza kvaliteta biblioteke je pokrenuta.','success');}catch(e){toast(e.message,'error',12000);}}
async function saveYoutubeSchedule(){const minutes=Number($('youtubeScheduleMinutes').value||0);try{const d=await api('/api/schedule/save',{method:'POST',body:{task_type:'youtube_owned',name:'Automatska YouTube provera',interval_minutes:Math.max(1,minutes||60),enabled:minutes>0,options:{max_pages:Number($('youtubeChannelScanPages')?.value||20),threshold:68,include_private_unlisted:$('youtubeIncludePrivate').checked}}});toast(minutes?'Raspored je sačuvan.':'Automatska provera je isključena.','success');renderScheduleStatus(d.schedules||[]);}catch(e){toast(e.message,'error');}}
async function chooseCloudRestore(){try{const d=await api('/api/choose-file',{method:'POST',body:{initial:$('cloudBackupDir').value||''}});if(d.path)$('cloudRestorePath').value=d.path;}catch(e){toast(e.message,'error');}}
async function restoreCloudBackup(){const path=$('cloudRestorePath').value.trim();if(!path)return toast('Izaberi cloud backup ZIP.','error');if(!confirm('Vraćanje će zameniti trenutnu bazu. Program automatski pravi sigurnosnu kopiju postojeće baze. Nastaviti?'))return;try{const d=await api('/api/cloud-backup/restore',{method:'POST',body:{path,restore_files:true},timeoutMs:600000});toast(`Cloud backup je vraćen. Fajlova: ${d.files_restored||0}. Lokacija: ${d.restore_root||'folder preuzimanja'}.`,'success',15000);await Promise.all([loadSongs(true),loadCollections(),loadStats(),loadAdvancedStatus()]);}catch(e){toast(e.message,'error',15000);}}
async function runCloudBackup(){let folder=$('cloudBackupDir').value.trim();if(!folder){folder=await chooseFolder('cloudBackupDir');if(!folder)return;}try{const d=await api('/api/cloud-backup',{method:'POST',body:{folder,include_files:$('cloudBackupFiles').checked},timeoutMs:300000});toast(`Cloud backup je napravljen: ${d.path||folder}`,'success',12000);}catch(e){toast(e.message,'error',15000);}}
async function runStems(){const ids=requireSelection();if(!ids)return;try{await startBackground('/api/plugins/stems/run',{ids,model:$('stemsModel')?.value||'htdemucs'});toast('Izdvajanje stemova je pokrenuto.','success');}catch(e){toast(e.message,'error',12000);}}
async function runTranscription(){const ids=requireSelection();if(!ids)return;try{await startBackground('/api/plugins/transcription/run',{ids,model:$('transcriptionModel')?.value||'small',language:$('transcriptionLanguage')?.value||'auto',align_original_lyrics:$('transcriptionAlignLyrics')?.checked!==false});toast('Lokalna transkripcija je pokrenuta.','success');}catch(e){toast(e.message,'error',12000);}}
async function installStemsPlugin(){try{await startBackground('/api/plugins/stems/install',{});toast('Instalacija dodatka za stemove je pokrenuta (potrebno je nekoliko minuta i internet veza).','success',10000);}catch(e){toast(e.message,'error',12000);}}
async function installTranscriptionPlugin(){try{await startBackground('/api/plugins/transcription/install',{});toast('Instalacija dodatka za transkripciju je pokrenuta (potrebno je nekoliko minuta i internet veza).','success',10000);}catch(e){toast(e.message,'error',12000);}}
async function saveUpdateUrl(){try{const d=await api('/api/update/settings',{method:'POST',body:{manifest_url:$('updateManifestUrl').value.trim()}});renderUpdateStatus(d.update);toast('Adresa za ažuriranje je sačuvana.','success');}catch(e){toast(e.message,'error');}}
function renderUpdateStatus(u){const box=$('updateStatusBox');if(!box)return;box.textContent=!u?.configured?'Manifest nije podešen.':u.available?`Dostupna verzija ${u.latest||u.version}.`:`Koristiš najnoviju verziju (${u.current||'3.0.0'}).`;box.className=`inline-message ${u?.available?'warning':'success'}`;}
async function checkProgramUpdate(){try{const d=await api('/api/update/status');renderUpdateStatus(d.update);}catch(e){toast(e.message,'error');}}
async function downloadProgramUpdate(){try{const d=await api('/api/update/download',{method:'POST',body:{},timeoutMs:300000});renderUpdateStatus(d.update||{available:true});toast(`Ažuriranje je preuzeto: ${d.path||''}. Zatvori program, raspakuj taj ZIP i pokreni INSTALIRAJ_PROGRAM.exe iz njega — automatski prepoznaje postojeću instalaciju i nadograđuje je.`,'success',15000);}catch(e){toast(e.message,'error',15000);}}
async function loadSubtitleCues(){if(!state.currentSong)return;try{const d=await api(`/api/subtitles?id=${encodeURIComponent(state.currentSong.id)}`);state.subtitleCues=d.cues||[];renderSubtitleCues();}catch(e){toast(e.message,'error');}}
function renderSubtitleCues(){const box=$('subtitleCueEditor');if(!box)return;box.innerHTML=state.subtitleCues.map((c,i)=>`<div class="subtitle-cue-row" data-index="${i}"><span>${i+1}</span><input class="cue-start" type="number" min="0" step="0.01" value="${Number(c.start_s??c.start??0).toFixed(2)}"><input class="cue-end" type="number" min="0" step="0.01" value="${Number(c.end_s??c.end??0).toFixed(2)}"><input class="cue-text" value="${escapeHtml(c.text||'')}"><button class="link-button danger-text cue-delete">×</button></div>`).join('')||'<p class="muted">Nema LRC/SRT redova. Klikni „Dodaj red”.</p>';}
function syncCuesFromEditor(){state.subtitleCues=qsa('.subtitle-cue-row',$('subtitleCueEditor')).map(r=>({start:Number(r.querySelector('.cue-start').value||0),end:Number(r.querySelector('.cue-end').value||0),text:r.querySelector('.cue-text').value})).filter(c=>c.text.trim());}
function shiftSubtitleCues(delta){syncCuesFromEditor();state.subtitleCues=state.subtitleCues.map(c=>({...c,start:Math.max(0,c.start+delta),end:Math.max(0,c.end+delta)}));renderSubtitleCues();}
async function saveSubtitleCues(){if(!state.currentSong)return;syncCuesFromEditor();try{const d=await api('/api/subtitles/save',{method:'POST',body:{id:state.currentSong.id,cues:state.subtitleCues},timeoutMs:120000});state.subtitleCues=d.cues||state.subtitleCues;renderSubtitleCues();toast('LRC i SRT su sačuvani.','success');await openSong(state.currentSong.id,false);}catch(e){toast(e.message,'error',12000);}}
async function compareYoutubeText(){if(!state.currentSong)return;const url=$('compareYoutubeUrl').value.trim()||state.currentSong.youtube_url||'';if(!url)return toast('Unesi YouTube adresu.','error');$('textCompareResult').textContent='Preuzimam titlove i poredim tekst...';try{const d=await api('/api/text/compare',{method:'POST',body:{song_id:state.currentSong.id,video_url:url},timeoutMs:180000});const r=d.result||{}, changed=r.changed_lines||[];$('textCompareResult').innerHTML=`<h3>Poklapanje teksta: ${Number(r.similarity||0).toFixed(1)}%</h3><p>Nedostajući stihovi: ${(r.missing_lines||[]).length} · promenjene grupe: ${changed.length}</p>${(r.missing_lines||[]).slice(0,30).map(x=>`<div class="warning">Nedostaje: ${escapeHtml(x)}</div>`).join('')}${changed.slice(0,20).map(x=>`<div class="info"><strong>Suno:</strong> ${escapeHtml((x.suno||[]).join(' / '))}<br><strong>YouTube:</strong> ${escapeHtml((x.youtube||[]).join(' / '))}</div>`).join('')}`;}catch(e){$('textCompareResult').innerHTML=`<p class="danger-text">${escapeHtml(e.message)}</p>`;}}
async function loadSongHistory(){if(!state.currentSong)return;try{const d=await api(`/api/song/history?id=${encodeURIComponent(state.currentSong.id)}`);renderSongHistory(d.history||[]);}catch(e){toast(e.message,'error');}}
function renderSongHistory(rows){const box=$('songHistoryList');if(!box)return;box.innerHTML=rows.map(h=>`<div class="file-item"><div><strong>${escapeHtml(h.action||'Izmena')}</strong><span>${escapeHtml(h.created_at||'')} · ${escapeHtml(Object.keys(h.fields||{}).join(', ')||'snimak')}</span></div><button class="btn ghost small undo-history" data-id="${h.id}">Vrati ovu verziju</button></div>`).join('')||'<p class="muted">Još nema sačuvane istorije.</p>';qsa('.undo-history',box).forEach(b=>b.addEventListener('click',async()=>{if(!confirm('Vratiti pesmu na ovu prethodnu verziju?'))return;try{const d=await api('/api/song/history/undo',{method:'POST',body:{id:state.currentSong.id,history_id:Number(b.dataset.id)}});state.currentSong=d.song;await openSong(state.currentSong.id,false);renderSongHistory(d.history||[]);toast('Prethodna verzija je vraćena.','success');}catch(e){toast(e.message,'error');}}));}

function selfTestStatusLabel(status) {
  if (status === 'pass') return 'ISPRAVNO';
  if (status === 'warn') return 'UPOZORENJE';
  return 'GREŠKA';
}
function renderSelfTest(report) {
  const summary = $('selfTestSummary');
  const results = $('selfTestResults');
  const failed = Number(report?.failed || 0);
  const warnings = Number(report?.warnings || 0);
  const passed = Number(report?.passed || 0);
  summary.textContent = `Završeno: ${passed} ispravno · ${warnings} upozorenja · ${failed} grešaka.`;
  summary.className = `inline-message ${failed ? 'error' : warnings ? 'warning' : 'success'}`;
  const rows = Array.isArray(report?.checks) ? report.checks : [];
  results.classList.toggle('hidden', rows.length === 0);
  results.innerHTML = rows.map((row) => {
    const status = String(row.status || 'fail');
    return `<div class="${status === 'pass' ? 'success' : status === 'warn' ? 'warning' : 'error'}"><strong>${escapeHtml(selfTestStatusLabel(status))} — ${escapeHtml(row.name || 'Provera')}</strong><br>${escapeHtml(row.detail || '')}</div>`;
  }).join('');
}
async function runProgramSelfTest() {
  const button = $('runSelfTestBtn');
  button.disabled = true;
  $('selfTestSummary').textContent = 'Provera je u toku: baza, folderi, fajlovi, FFmpeg i test isecanja MP3-a…';
  $('selfTestSummary').className = 'inline-message warning';
  try {
    const data = await api('/api/self-test', { method: 'POST', body: { include_audio: true }, timeoutMs: 180000 });
    renderSelfTest(data.report || {});
    toast(data.report?.failed ? 'Kompletna provera je pronašla greške.' : 'Kompletna provera je završena.', data.report?.failed ? 'error' : data.report?.warnings ? 'warning' : 'success', 10000);
    await loadStorageSummary();
  } catch (e) {
    $('selfTestSummary').textContent = e.message;
    $('selfTestSummary').className = 'inline-message error';
    toast(e.message, 'error', 12000);
  } finally { button.disabled = false; }
}
async function exportDiagnostics() {
  const button = $('exportDiagnosticsBtn');
  button.disabled = true;
  try {
    const data = await api('/api/diagnostics/export', { method: 'POST', body: { include_audio: true }, timeoutMs: 180000 });
    downloadFromUrl(data.download_url);
    toast(data.message || 'Dijagnostički paket je napravljen.', 'success', 10000);
  } catch (e) { toast(e.message, 'error', 12000); }
  finally { button.disabled = false; }
}
async function loadStorageSummary() {
  const box = $('storageSummary');
  if (!box) return;
  try {
    const data = await api('/api/storage');
    const storage = data.storage || {};
    box.textContent = `Preuzete pesme: ${formatBytes(storage.download_bytes)} u ${Number(storage.download_files || 0)} fajlova · Izvoz: ${formatBytes(storage.export_bytes)} u ${Number(storage.export_files || 0)} fajlova · Data: ${formatBytes(storage.data_bytes)} · Baza: ${formatBytes(storage.database_bytes)}.`;
    box.className = 'inline-message success';
  } catch (e) { box.textContent = e.message; box.className = 'inline-message error'; }
}

async function fullBackupAction(){try{const data=await api('/api/backup/full',{method:'POST',body:{},timeoutMs:300000});downloadFromUrl(data.download_url);toast('Kompletan backup je napravljen.','success',10000);}catch(e){toast(e.message,'error',10000);} }
async function clearLogsAction(){if(!confirm('Obrisati ceo Dnevnik rada?'))return;try{const data=await api('/api/logs/clear',{method:'POST',body:{}});toast(`Obrisano zapisa: ${data.count}`,'success');if(state.activeView==='logs')loadLogs();}catch(e){toast(e.message,'error');}}
async function openDataFolder(){try{await api('/api/open-folder',{method:'POST',body:{path:'data'}});}catch(e){toast(e.message,'error');}}
async function copySelected(kind){const ids=requireSelection();if(!ids)return;const chosen=state.songs.filter((x)=>ids.includes(x.id));const text=kind==='links'?chosen.map((x)=>x.source_url||`https://suno.com/song/${x.id}`).join('\n'):chosen.map((x)=>x.title||x.id).join('\n');await navigator.clipboard.writeText(text);toast(kind==='links'?'Suno linkovi su kopirani.':'Naslovi su kopirani.','success');}
async function shutdownProgram(){if(!confirm('Zatvoriti Suno Pesme Studio?'))return;try{await api('/api/shutdown',{method:'POST',body:{}});document.body.innerHTML='<main class="shutdown-screen"><h1>Program je zatvoren.</h1><p>Za ponovno pokretanje klikni prečicu „Suno Pesme Studio” na Desktopu ili u Start meniju.</p></main>';}catch(e){toast(e.message,'error');}}
function clearAdvancedFilters(){['dateFromFilter','dateToFilter','minDurationFilter','maxDurationFilter'].forEach((id)=>{$(id).value=id.includes('Duration')?'0':'';});$('sourceFilter').value='';loadSongs();}
function clearAllLibraryFilters(){
  const search=$('searchInput'); search.value=''; state.searchUserEdited=false;
  $('filterSelect').value='all'; $('sortSelect').value='newest'; $('collectionFilter').value='0';
  if($('sourceFilter'))$('sourceFilter').value='';
  ['dateFromFilter','dateToFilter'].forEach((id)=>{$(id).value='';});
  ['minDurationFilter','maxDurationFilter'].forEach((id)=>{$(id).value='0';});
  loadSongs();
}
function protectLibrarySearchFromAutofill(){
  const input=$('searchInput'); if(!input)return;
  const unlock=()=>input.removeAttribute('readonly');
  input.addEventListener('pointerdown',unlock,{once:true});
  input.addEventListener('focus',unlock,{once:true});
  input.addEventListener('keydown',()=>{state.searchUserEdited=true;unlock();},{capture:true});
  input.addEventListener('paste',()=>{state.searchUserEdited=true;unlock();},{capture:true});
  const clearInjectedValue=()=>{
    if(!state.searchUserEdited && document.activeElement!==input && input.value.trim()){
      input.value='';
      loadSongs();
    }
  };
  clearTimeout(state.searchGuardTimer);
  state.searchGuardTimer=setTimeout(clearInjectedValue,250);
  setTimeout(clearInjectedValue,900);
}
function playSelectedQueue(){const ids=requireSelection();if(!ids)return;state.queue=state.songs.filter((x)=>ids.includes(x.id)&&songPlayable(x));state.queueIndex=0;if(!state.queue.length)return toast('Izabrane pesme nemaju dostupan audio.','error');playSong(state.queue[0]);}
function playNextQueue(){if(state.repeat){$('audioPlayer').currentTime=0;$('audioPlayer').play().catch(()=>{});return;}if(state.queueIndex>=0&&state.queueIndex<state.queue.length-1){state.queueIndex+=1;playSong(state.queue[state.queueIndex]);}}

function v3SongId(){ return selectedIds()[0] || state.currentSong?.id || state.songs?.[0]?.id || ''; }
function v3Show(value, title='Rezultat'){
  const box=$('v3Output'); if(!box)return;
  const payload=typeof value==='string'?value:JSON.stringify(value,null,2);
  box.innerHTML=`<h3>${escapeHtml(title)}</h3><pre class="v3-pre">${escapeHtml(payload)}</pre>`;
  box.scrollIntoView({behavior:'smooth',block:'nearest'});
}
function v3UseTask(data){ state.taskId=data.task?.id; renderTask(data.task); return data; }
async function loadV3Status(){
  try{
    const d=await api('/api/v3/status',{timeoutMs:30000,retries:0});
    const r=d.preflight||{}; const tools=d.tools||{};
    // Each tool is shown exactly once here; d.panako duplicated this same
    // "panako" entry as a second line previously (identical information,
    // shown twice). Optional tools (e.g. panako) get a distinct label
    // instead of a plain SPREMAN/NIJE SPREMAN so a missing optional addon
    // doesn't read like a core-tool failure.
    const toolLine=Object.entries(tools).map(([k,v])=>{
      if(v?.ready)return `${k}: SPREMAN`;
      return v?.optional?`${k}: opcioni dodatak, nije instaliran`:`${k}: NIJE SPREMAN`;
    }).join(' · ');
    $('v3StatusBox').className=`panel inline-message ${r.readiness==='ready'?'success':r.readiness==='blocked'?'error':'warning'}`;
    $('v3StatusBox').innerHTML=`<strong>${r.readiness==='ready'?'Računar je spreman':r.readiness==='blocked'?'Program ima blokirajuće probleme':'Računar je spreman uz ograničenja'}</strong><br>${escapeHtml(toolLine)}<br>Slobodan prostor: ${Number(d.storage?.disk?.free_gb||0).toFixed(1)} GB · Watchdog: ${d.watchdog?'aktivan':'nije aktivan'}`;
  }catch(e){$('v3StatusBox').className='panel inline-message error';$('v3StatusBox').textContent=e.message;}
}
async function runV3Preflight(){try{const d=await api('/api/v3/preflight',{method:'POST',body:{},timeoutMs:60000,retries:0});v3Show(d.report,'Kompletna provera računara');toast(d.report?.readiness==='ready'?'Računar je spreman.':'Provera je završena uz upozorenja.','info',9000);loadV3Status();}catch(e){toast(e.message,'error',12000);}}
async function runV3Integrity(){try{v3UseTask(await api('/api/v3/integrity',{method:'POST',body:{repair_hashes:true,probe_audio:true}}));}catch(e){toast(e.message,'error',12000);}}
async function loadV3Duplicates(){try{const d=await api('/api/v3/duplicates',{timeoutMs:120000,retries:0});v3Show(d.report,'Duplikati i moguće alternativne verzije');}catch(e){toast(e.message,'error',12000);}}
async function chooseV3Folder(inputId){return chooseFolder(inputId);}
async function chooseV3File(inputId){try{const d=await api('/api/choose-file',{method:'POST',body:{initial:$(inputId).value||''}});if(d.path)$(inputId).value=d.path;return d.path||'';}catch(e){toast(e.message,'error');return '';}}
async function runV3Organize(){try{const ids=selectedIds();const body={ids,target:$('v3OrganizeTarget').value,template:$('v3OrganizeTemplate').value,mode:$('v3OrganizeMode').value,dry_run:$('v3OrganizeDryRun').checked};if(!body.target)throw new Error('Izaberi ciljni folder.');if(!body.dry_run&&!confirm('Program će stvarno '+(body.mode==='copy'?'kopirati':'premestiti')+' fajlove i promeniti putanje u biblioteci. Nastaviti?'))return;v3UseTask(await api('/api/v3/organize',{method:'POST',body}));}catch(e){toast(e.message,'error',12000);}}
async function loadV3ShortSuggestions(){const id=v3SongId();if(!id)return toast('Izaberi pesmu u Biblioteci.','warning');try{const d=await api(`/api/v3/shorts/suggest?id=${encodeURIComponent(id)}`,{timeoutMs:120000,retries:0});v3Show(d.result,'Predloženi Shorts delovi');}catch(e){toast(e.message,'error',12000);}}
async function runV3Shorts(){const id=v3SongId();if(!id)return toast('Izaberi pesmu u Biblioteci.','warning');try{v3UseTask(await api('/api/v3/shorts/render',{method:'POST',body:{id,durations:[15,30,45,60],normalize:true}}));}catch(e){toast(e.message,'error',12000);}}
async function runV3Lyric(){const id=v3SongId();if(!id)return toast('Izaberi pesmu u Biblioteci.','warning');try{v3UseTask(await api('/api/v3/lyric-video',{method:'POST',body:{id,aspect:$('v3LyricAspect').value,background:$('v3LyricBackground').value,font:$('v3LyricFont').value,font_size:Number($('v3LyricFontSize').value||54)}}));}catch(e){toast(e.message,'error',12000);}}
function v3YoutubePayload(){const local=$('v3YoutubePublishAt').value;let publishAt='';if(local){const d=new Date(local);if(!Number.isNaN(d.getTime()))publishAt=d.toISOString();}return {id:v3SongId(),video_path:$('v3YoutubeVideoPath').value,thumbnail_path:$('v3YoutubeThumbnailPath').value,playlist_id:$('v3YoutubePlaylistId').value.trim(),publish_at:publishAt,channel_profile:$('v3ChannelProfile').value,privacy_status:$('v3YoutubePrivacy').value};}
async function createV3YoutubePackage(){const body=v3YoutubePayload();if(!body.id)return toast('Izaberi pesmu u Biblioteci.','warning');try{const d=await api('/api/v3/youtube/package',{method:'POST',body});v3Show(d,'YouTube SEO paket');toast('YouTube paket je napravljen.','success');}catch(e){toast(e.message,'error',12000);}}
async function runV3YoutubeUpload(){const body=v3YoutubePayload();if(!body.id)return toast('Izaberi pesmu u Biblioteci.','warning');if(!body.video_path)return toast('Izaberi gotov MP4 video.','warning');const mode=body.publish_at?`zakazano za ${new Date(body.publish_at).toLocaleString()}`:`kao „${body.privacy_status}“`;const ok=confirm(`YouTube upload će stvarno poslati video ${mode}. Ovo nije simulacija. Nastaviti?`);if(!ok)return;body.confirm_upload=true;try{v3UseTask(await api('/api/v3/youtube/upload',{method:'POST',body}));}catch(e){toast(e.message,'error',12000);}}
async function createV3Proof(){const id=v3SongId();if(!id)return toast('Izaberi pesmu u Biblioteci.','warning');try{const d=await api('/api/v3/proof',{method:'POST',body:{id}});v3Show(d,'Dokazni paket');if(d.download_url)downloadFromUrl(d.download_url);}catch(e){toast(e.message,'error',12000);}}
async function runV3Panako(){const ids=selectedIds();if(!ids.length&&!v3SongId())return toast('Izaberi bar jednu pesmu.','warning');try{v3UseTask(await api('/api/v3/panako/index',{method:'POST',body:{ids:ids.length?ids:[v3SongId()]}}));}catch(e){toast(e.message,'error',12000);}}
async function runV3Snapshot(){try{v3UseTask(await api('/api/v3/snapshot',{method:'POST',body:{include_tools:false}}));}catch(e){toast(e.message,'error',12000);}}
async function saveV3Watch(){try{const d=await api('/api/v3/watch/save',{method:'POST',body:{interval_minutes:Number($('v3WatchInterval').value||360),enabled:$('v3WatchEnabled').checked,options:{include_owned_channels:true,max_songs:50,max_pages:3}}});v3Show(d.schedule,'Automatska YouTube provera');toast('Raspored je sačuvan.','success');}catch(e){toast(e.message,'error',12000);}}

function showSecurityLock(){ const modal=$('securityLockModal'); if(modal)modal.classList.remove('hidden'); setTimeout(()=>$('securityUnlockPin')?.focus(),80); }
function hideSecurityLock(){ $('securityLockModal')?.classList.add('hidden'); if($('securityUnlockPin'))$('securityUnlockPin').value=''; }
async function loadSecurityStatus(){
  try{const d=await api('/api/v3/security/status',{timeoutMs:10000,retries:0});state.security=d.security;const sec=d.security||{};if(sec.locked)showSecurityLock();else hideSecurityLock();if($('v3SecurityStatus')){$('v3SecurityStatus').className=`inline-message ${sec.enabled?'success':'warning'}`;$('v3SecurityStatus').textContent=sec.enabled?`PIN je uključen · automatsko zaključavanje: ${sec.timeout_minutes?sec.timeout_minutes+' min':'samo ručno'}`:'PIN zaštita nije uključena.';if($('v3SecurityTimeout'))$('v3SecurityTimeout').value=String(sec.timeout_minutes??30);}}catch(e){if(!e.locked&&$('v3SecurityStatus'))$('v3SecurityStatus').textContent=e.message;}
}
async function unlockSecurity(){const pin=$('securityUnlockPin').value;try{const d=await api('/api/v3/security/unlock',{method:'POST',body:{pin},timeoutMs:30000,retries:0});state.security=d.security;hideSecurityLock();$('securityUnlockMessage').textContent='';toast('Program je otključan.','success');await Promise.all([loadStatus(),loadSongs(true),loadSecurityStatus()]);}catch(e){$('securityUnlockMessage').className='inline-message error';$('securityUnlockMessage').textContent=e.message;}}
async function setSecurityPin(){const pin=$('v3SecurityNewPin').value;if(!pin)return toast('Unesi novi PIN.','warning');try{const d=await api('/api/v3/security/set-pin',{method:'POST',body:{new_pin:pin,timeout_minutes:Number($('v3SecurityTimeout').value||0)}});state.security=d.security;$('v3SecurityNewPin').value='';toast('PIN zaštita je sačuvana.','success');loadSecurityStatus();}catch(e){toast(e.message,'error',10000);}}
async function lockSecurity(){try{await api('/api/v3/security/lock',{method:'POST',body:{}});showSecurityLock();}catch(e){toast(e.message,'error');}}
async function disableSecurityPin(){const pin=prompt('Unesi trenutni PIN da isključiš zaštitu:');if(pin===null)return;try{await api('/api/v3/security/disable',{method:'POST',body:{pin}});toast('PIN zaštita je isključena.','success');loadSecurityStatus();}catch(e){toast(e.message,'error',10000);}}
async function openV3Maintenance(){try{await api('/api/v3/open-maintenance',{method:'POST',body:{}});}catch(e){toast(e.message,'error');}}
async function openV3Snapshots(){try{await api('/api/v3/open-snapshots',{method:'POST',body:{}});}catch(e){toast(e.message,'error');}}
async function cleanupV3Storage(){if(!confirm('Obrisati stari YouTube/waveform keš, stare .part fajlove i višak rollback kopija? Pesme i izvozi se ne brišu.'))return;try{const d=await api('/api/v3/storage/cleanup',{method:'POST',body:{confirm:true,cache_days:30,keep_rollbacks:5},timeoutMs:120000,retries:0});v3Show(d.report,'Bezbedno čišćenje prostora');toast(`Oslobođeno: ${Number(d.report?.freed_mb||0).toFixed(1)} MB`,'success');loadV3Status();}catch(e){toast(e.message,'error',12000);}}

function bindEvents() {
  $('securityUnlockBtn').addEventListener('click',unlockSecurity); $('securityUnlockPin').addEventListener('keydown',e=>{if(e.key==='Enter')unlockSecurity();}); $('v3SecuritySetBtn').addEventListener('click',setSecurityPin); $('v3SecurityLockBtn').addEventListener('click',lockSecurity); $('v3SecurityDisableBtn').addEventListener('click',disableSecurityPin); $('v3OpenMaintenanceBtn').addEventListener('click',openV3Maintenance); $('v3OpenSnapshotsBtn').addEventListener('click',openV3Snapshots); $('v3StorageCleanupBtn').addEventListener('click',cleanupV3Storage);
  $('v3RefreshStatusBtn').addEventListener('click',loadV3Status); $('v3PreflightBtn').addEventListener('click',runV3Preflight); $('v3IntegrityBtn').addEventListener('click',runV3Integrity); $('v3DuplicatesBtn').addEventListener('click',loadV3Duplicates); $('v3ChooseOrganizeTargetBtn').addEventListener('click',()=>chooseV3Folder('v3OrganizeTarget')); $('v3OrganizeBtn').addEventListener('click',runV3Organize); $('v3SuggestShortsBtn').addEventListener('click',loadV3ShortSuggestions); $('v3RenderShortsBtn').addEventListener('click',runV3Shorts); $('v3ChooseBackgroundBtn').addEventListener('click',()=>chooseV3File('v3LyricBackground')); $('v3LyricVideoBtn').addEventListener('click',runV3Lyric); $('v3ChooseYoutubeVideoBtn').addEventListener('click',()=>chooseV3File('v3YoutubeVideoPath')); $('v3ChooseYoutubeThumbnailBtn').addEventListener('click',()=>chooseV3File('v3YoutubeThumbnailPath')); $('v3YoutubePackageBtn').addEventListener('click',createV3YoutubePackage); $('v3YoutubeUploadBtn').addEventListener('click',runV3YoutubeUpload); $('v3ProofBtn').addEventListener('click',createV3Proof); $('v3PanakoIndexBtn').addEventListener('click',runV3Panako); $('v3SnapshotBtn').addEventListener('click',runV3Snapshot); $('v3SaveWatchBtn').addEventListener('click',saveV3Watch); $('v3ClearOutputBtn').addEventListener('click',()=>v3Show('Izaberi pesmu u Biblioteci, zatim pokreni željenu radnju.','Produkcija v3'));
  $('nav').addEventListener('click', (e) => { const item = e.target.closest('[data-view]'); if (item) showView(item.dataset.view); }); qsa('[data-go]').forEach((b) => b.addEventListener('click', () => showView(b.dataset.go)));
  $('connectBtn').addEventListener('click', () => state.status?.connected ? showView('import') : connectStart()); $('openSunoLoginBtn').addEventListener('click', connectStart); $('checkSunoBtn').addEventListener('click', () => connectCheck(true)); $('testServerBtn').addEventListener('click', testLocalServer); $('connectManualTokenBtn').addEventListener('click', connectManualToken); $('checkNewBtn').addEventListener('click', checkNewSongs); $('checkNewImportBtn').addEventListener('click', checkNewSongs); $('toolCheckNewBtn').addEventListener('click', checkNewSongs); $('syncBtn').addEventListener('click', startSync); $('startSyncBtn').addEventListener('click', startSync); $('importUrlsBtn').addEventListener('click', importUrls); $('importFolderBtn').addEventListener('click', importFolder); $('chooseLocalFolderBtn').addEventListener('click', () => chooseFolder('localFolderInput')); $('rescanWatchedFoldersBtn')?.addEventListener('click',rescanWatchedFolders); $('refreshWatchedFoldersBtn')?.addEventListener('click',loadWatchedFolders); $('watchedFoldersList')?.addEventListener('click',(e)=>{const t=e.target.closest('.watched-toggle');if(t)updateWatchedFolder(t.dataset.path,{enabled:t.dataset.enabled==='1'});const r=e.target.closest('.watched-remove');if(r&&confirm('Ukloniti folder iz praćenja? Pesme koje su već u Biblioteci ostaju sačuvane.'))updateWatchedFolder(r.dataset.path,{remove:true});});
  $('searchInput').addEventListener('input', () => { clearTimeout(state.searchTimer); state.searchTimer = setTimeout(reloadSongsFirstPage, 250); }); ['filterSelect', 'sortSelect', 'collectionFilter'].forEach((id) => $(id).addEventListener('change', reloadSongsFirstPage)); $('refreshSongsBtn').addEventListener('click', () => loadSongs(false)); $('applyAdvancedFiltersBtn').addEventListener('click', reloadSongsFirstPage); $('clearAdvancedFiltersBtn').addEventListener('click', clearAdvancedFilters); $('prevPageBtn').addEventListener('click',()=>{if(state.page>0){state.page-=1;loadSongs(false);}}); $('nextPageBtn').addEventListener('click',()=>{if((state.page+1)*state.pageSize<state.totalFiltered){state.page+=1;loadSongs(false);}}); $('pageSizeSelect').addEventListener('change',()=>{state.pageSize=Number($('pageSizeSelect').value||100);reloadSongsFirstPage();});
  $('emptyLibraryAction').addEventListener('click',(e)=>{if(e.currentTarget.dataset.clearLibrary==='1'){e.preventDefault();clearAllLibraryFilters();}});
  $('clearSearchBtn').addEventListener('click',()=>{ $('searchInput').value=''; state.searchUserEdited=false; loadSongs(); });
  $('selectAll').addEventListener('change', (e) => { state.songs.forEach((song) => e.target.checked ? state.selected.add(song.id) : state.selected.delete(song.id)); renderSongs(); }); $('clearSelectionBtn').addEventListener('click', () => { state.selected.clear(); renderSongs(); }); $('quickMp3Btn').addEventListener('click', () => startDownload(true)); $('exportSelectedBtn').addEventListener('click', () => state.selected.size ? exportData('zip', selectedIds()) : toast('Prvo izaberi pesme.', 'error')); $('bulkAddFolderBtn').addEventListener('click', () => addSelectedToCollection('bulkCollectionSelect')); $('saveSelectedToFolderBtn').addEventListener('click', () => saveToFolder(selectedIds()));
  $('createFolderBtn').addEventListener('click', createFolder); $('folderBulkAddBtn').addEventListener('click', () => addSelectedToCollection('folderBulkTarget'));
  $('goLibraryBtn').addEventListener('click', () => showView('library')); $('selectNotDownloadedBtn').addEventListener('click', async () => {
    $('filterSelect').value = 'not_downloaded'; $('collectionFilter').value = '0'; showView('library'); await loadSongs(true);
    try { const params=currentSongFilterParams({limit:'20000'}); const d=await api(`/api/song-ids?${params}`); state.selected=new Set(d.ids||[]); renderSongs(); toast(`Izabrano je ${state.selected.size} nepreuzetih pesama iz cele biblioteke.`, 'success', 9000); }
    catch(e){ toast(e.message,'error',10000); }
  }); $('startDownloadBtn').addEventListener('click', () => startDownload(false)); $('chooseDownloadTargetBtn').addEventListener('click', () => chooseFolder('downloadTargetInput'));
  $('openSelectedAudioBtn').addEventListener('click', () => { const id = selectedIds()[0]; if (!id) return toast('Izaberi jednu pesmu u Biblioteci.', 'error'); openSong(id).then(() => switchModalTab('audio')); }); $('prepareAudioToolsBtn').addEventListener('click', prepareAudioTools); $('installAudioToolsSettingsBtn').addEventListener('click', prepareAudioTools);
  $('cancelTaskBtn').addEventListener('click', async () => { try { await api('/api/task/cancel', { method: 'POST', body: {} }); } catch (e) { toast(e.message, 'error'); } }); $('pauseTaskBtn').addEventListener('click',async()=>{try{const paused=$('pauseTaskBtn').dataset.paused==='1';const d=await api(paused?'/api/task/resume':'/api/task/pause',{method:'POST',body:{}});renderTask(d.task);}catch(e){toast(e.message,'error');}}); $('resetSyncCheckpointBtn').addEventListener('click',resetSyncCheckpoints);
  qsa('[data-close-modal]').forEach((el) => el.addEventListener('click', () => $('songModal').classList.add('hidden'))); qsa('[data-modal-tab]').forEach((el) => el.addEventListener('click', () => switchModalTab(el.dataset.modalTab)));
  $('modalPlayBtn').addEventListener('click', () => state.currentSong && playSong(state.currentSong)); $('modalDownloadBrowserBtn').addEventListener('click', () => state.currentSong && downloadFromUrl(`/api/song/audio?id=${encodeURIComponent(state.currentSong.id)}&download=1`)); $('modalSaveToFolderBtn').addEventListener('click', () => state.currentSong && saveToFolder([state.currentSong.id]));
  $('modalLyrics').addEventListener('input', updateTextStats); qsa('[data-text-action]').forEach((b) => b.addEventListener('click', () => transformLyrics(b.dataset.textAction))); $('replaceLyricsBtn').addEventListener('click', () => { const find = $('lyricsFind').value; if (!find) return; $('modalLyrics').value = $('modalLyrics').value.split(find).join($('lyricsReplace').value); updateTextStats(); }); $('saveLyricsBtn').addEventListener('click', () => updateCurrent({ lyrics: $('modalLyrics').value })); $('copyLyricsBtn').addEventListener('click', async () => { await navigator.clipboard.writeText($('modalLyrics').value); toast('Tekst je kopiran.', 'success'); }); qsa('.lyric-download').forEach((b) => b.addEventListener('click', () => state.currentSong && downloadFromUrl(`/api/song/text?id=${encodeURIComponent(state.currentSong.id)}&format=${b.dataset.format}`)));
  $('saveNotesBtn').addEventListener('click', () => updateCurrent({ notes: $('modalNotes').value, custom_tags: $('modalCustomTags').value, rating: Number($('modalRating').value || 0), favorite: $('modalFavorite').checked ? 1 : 0 })); $('copyPromptBtn').addEventListener('click', async () => { await navigator.clipboard.writeText($('modalPrompt').value); toast('Prompt je kopiran.', 'success'); });
  $('saveTagFieldsBtn').addEventListener('click', saveTagsToLibrary); $('writeMp3TagsBtn').addEventListener('click', writeMp3Tags); $('renameSongFilesBtn').addEventListener('click', renameSongFiles); $('saveFoldersBtn').addEventListener('click', saveFoldersAndPublication);
  $('previewFullBtn').addEventListener('click', () => state.currentSong && playSong(state.currentSong)); $('previewClipBtn').addEventListener('click', () => state.currentSong && playSong(state.currentSong, 0, Number($('trimStart').value || 0), Number($('trimEnd').value || state.currentSong.duration || 0))); $('setStartCurrentBtn').addEventListener('click', () => { $('trimStart').value = $('audioPlayer').currentTime.toFixed(1); drawWaveform(state.waveformPeaks); }); $('setEndCurrentBtn').addEventListener('click', () => { $('trimEnd').value = $('audioPlayer').currentTime.toFixed(1); drawWaveform(state.waveformPeaks); }); ['trimStart', 'trimEnd'].forEach((id) => $(id).addEventListener('input', () => drawWaveform(state.waveformPeaks)));
  qsa('[data-quick-clip]').forEach((b) => b.addEventListener('click', () => { const length = Number(b.dataset.quickClip); const start = Number($('trimStart').value || 0); $('trimEnd').value = length ? Math.min(Number(state.currentSong?.duration || start + length), start + length).toFixed(1) : Number(state.currentSong?.duration || 0).toFixed(1); drawWaveform(state.waveformPeaks); })); $('chooseProcessedFolderBtn').addEventListener('click', () => chooseFolder('processedTargetFolder')); $('quickCutBtn').addEventListener('click', () => processCurrentAudio('quick')); $('processAudioBtn').addEventListener('click', () => processCurrentAudio(null)); $('processSelectedBatchBtn').addEventListener('click', processSelectedBatch); $('shortsTeasersBtn')?.addEventListener('click', runShortsTeasers); $('shortsTextClipBtn')?.addEventListener('click', cutShortsByText);
  $('waveformCanvas').addEventListener('click', (e) => { const duration = Number(state.currentSong?.duration || 0); if (!duration) return; const rect = e.currentTarget.getBoundingClientRect(); const seconds = Math.max(0, Math.min(duration, (e.clientX - rect.left) / rect.width * duration)); $('audioPlayer').currentTime = seconds; });
  $('audioPlayer').addEventListener('timeupdate', () => { if (state.previewEnd !== null && $('audioPlayer').currentTime >= Number(state.previewEnd)) { $('audioPlayer').pause(); state.previewEnd = null; } }); $('audioPlayer').addEventListener('error', () => toast('Pesma nije mogla da se pusti. Pogledaj Dnevnik ili ponovo sinhronizuj podatke.', 'error', 8000)); $('audioPlayer').addEventListener('ended', playNextQueue); $('playerSpeed').addEventListener('change',()=>{$('audioPlayer').playbackRate=Number($('playerSpeed').value||1);}); $('repeatPlayerBtn').addEventListener('click',()=>{state.repeat=!state.repeat;$('repeatPlayerBtn').classList.toggle('active',state.repeat);toast(state.repeat?'Ponavljanje uključeno.':'Ponavljanje isključeno.','info');}); $('playQueueBtn').addEventListener('click',playSelectedQueue); $('closePlayerBtn').addEventListener('click', () => { $('audioPlayer').pause(); $('playerBar').classList.remove('visible'); });
  $('refreshLogsBtn').addEventListener('click', loadLogs); $('chooseSettingsFolderBtn').addEventListener('click', () => chooseFolder('downloadDirInput')); $('saveSettingsBtn').addEventListener('click', saveSettings); $('openDownloadFolderBtn').addEventListener('click', openFolder); $('backupBtn').addEventListener('click', backup); $('chooseRestoreFileBtn').addEventListener('click', chooseBackupFile); $('restoreBackupBtn').addEventListener('click', restoreBackup); $('runSelfTestBtn').addEventListener('click', runProgramSelfTest); $('exportDiagnosticsBtn').addEventListener('click', exportDiagnostics); $('refreshStorageBtn').addEventListener('click', loadStorageSummary); $('checkLibraryBtn').addEventListener('click', () => checkLibraryHealth(false)); $('repairLibraryBtn').addEventListener('click', () => { if (confirm('Očistiti samo putanje ka fajlovima koji više ne postoje? Postojeći fajlovi se ne brišu.')) checkLibraryHealth(true); }); qsa('.export-all').forEach((b) => b.addEventListener('click', () => exportData(b.dataset.format))); $('disconnectBtn').addEventListener('click', async () => { try { const data = await api('/api/connect/disconnect', { method: 'POST', body: {} }); toast(data.message, 'success'); loadStatus(); } catch (e) { toast(e.message, 'error'); } });
  $('themeSelect').addEventListener('change',(e)=>applyTheme(e.target.value)); $('themeGrid').addEventListener('click',(e)=>{const card=e.target.closest('[data-theme]');if(card)applyTheme(card.dataset.theme);});
  $('resetThemeBtn').addEventListener('click', resetThemeAndAppearance);
  $('exportThemeSettingsBtn').addEventListener('click', exportThemeSettings);
  $('importThemeSettingsInput').addEventListener('change', (e) => { const f = e.target.files?.[0]; if (f) importThemeSettings(f); e.target.value = ''; });
  $('textScaleSelect').addEventListener('change', (e) => { saveA11ySetting('scale', e.target.value); checkThemeContrast(); });
  $('densitySelect').addEventListener('change', (e) => saveA11ySetting('density', e.target.value));
  $('reduceMotionToggle').addEventListener('change', (e) => saveA11ySetting('motion', e.target.checked));
  $('highContrastToggle').addEventListener('change', (e) => { saveA11ySetting('contrast', e.target.checked); checkThemeContrast(); });
  $('colorblindToggle').addEventListener('change', (e) => saveA11ySetting('colorblind', e.target.checked));
  $('autoCheckMinutes').addEventListener('change',async()=>{ $('settingsAutoCheckMinutes').value=$('autoCheckMinutes').value; await saveAutomationSettings(); }); $('saveAutomationSettingsBtn').addEventListener('click',saveAutomationSettings); $('autoBackupBeforeSync').addEventListener('change',()=>{$('settingsAutoBackup').checked=$('autoBackupBeforeSync').checked;saveAutomationSettings();}); $('saveSunoSessionSettingsBtn').addEventListener('click',saveSunoSessionSettings); $('refreshSunoSessionNowBtn').addEventListener('click',refreshSunoSessionNow);
  $('connectYoutubeGoogleBtn').addEventListener('click',connectYoutubeGoogle); $('refreshYoutubeGoogleBtn').addEventListener('click',refreshYoutubeGoogle); $('importYoutubeOAuthClientBtn').addEventListener('click',importYoutubeOAuthClient); $('removeYoutubeOAuthConfigBtn').addEventListener('click',removeYoutubeOAuthConfig); $('openGoogleCloudClientsBtn').addEventListener('click',()=>openExternal('https://console.cloud.google.com/auth/clients'));
  $('saveYoutubeSettingsBtn').addEventListener('click',()=>saveYoutubeSettings(false)); $('clearYoutubeApiKeyBtn').addEventListener('click',()=>saveYoutubeSettings(true)); $('addYoutubeChannelBtn').addEventListener('click',addYoutubeChannel); $('scanOwnedYoutubeBtn').addEventListener('click',scanOwnedYoutube); $('scanGlobalYoutubeBtn').addEventListener('click',scanGlobalYoutube); $('refreshYoutubeCalendarBtn').addEventListener('click',loadYoutubeCenter); $('exportYoutubeCalendarBtn').addEventListener('click',exportYoutubeCalendar); $('refreshYoutubeMatchesBtn').addEventListener('click',loadYoutubeCenter); $('youtubeMatchStatusFilter').addEventListener('change',loadYoutubeCenter); $('openCopyrightMatchBtn').addEventListener('click',()=>openExternal('https://studio.youtube.com/')); $('openCopyrightHelpBtn').addEventListener('click',()=>openExternal('https://support.google.com/youtube/answer/2807622'));
  $('installYtdlpBtn').addEventListener('click',installYtdlp); $('analyzeOwnedYoutubeAudioBtn').addEventListener('click',analyzeOwnedYoutubeAudio); $('buildSunoFingerprintIndexBtn').addEventListener('click',buildSunoFingerprintIndex); $('analyzeYoutubeUrlBtn').addEventListener('click',analyzeYoutubeUrl); $('exportYoutubeCoverageReportBtn').addEventListener('click',exportYoutubeCoverageReport); $('exportYoutubeCoverageReportBtn2').addEventListener('click',exportYoutubeCoverageReport); $('exportYoutubeAudioReportBtn').addEventListener('click',exportYoutubeAudioReport); $('cleanupYoutubeAudioCacheBtn').addEventListener('click',cleanupYoutubeAudioCache); $('refreshYoutubeAudioResultsBtn').addEventListener('click',loadYoutubeCenter); $('youtubeAudioCompletenessFilter').addEventListener('change',loadYoutubeCenter); $('refreshYoutubeMatrixBtn').addEventListener('click',loadYoutubeCenter); $('refreshYoutubeCoverageBtn').addEventListener('click',loadYoutubeCenter); $('refreshYoutubeExtraReportBtn').addEventListener('click',loadYoutubeCenter);
  $('bulkFavoriteBtn').addEventListener('click',()=>bulkUpdate({favorite:1},'Dodato u favorite')); $('bulkUnfavoriteBtn').addEventListener('click',()=>bulkUpdate({favorite:0},'Uklonjeno iz favorita')); $('bulkRatingBtn').addEventListener('click',()=>bulkUpdate({rating:Number($('bulkRatingSelect').value||0)},'Ocena je postavljena')); $('bulkAppendTagsBtn').addEventListener('click',()=>bulkTags('append')); $('bulkClearTagsBtn').addEventListener('click',()=>bulkTags('clear')); $('bulkPublishedBtn').addEventListener('click',()=>bulkPublished(true)); $('bulkUnpublishedBtn').addEventListener('click',()=>bulkPublished(false)); $('bulkArchiveBtn').addEventListener('click',()=>bulkUpdate({archived:1},'Pesme su arhivirane')); $('bulkUnarchiveBtn').addEventListener('click',()=>bulkUpdate({archived:0},'Pesme su vraćene iz arhive'));
  qsa('.tool-folder').forEach((b)=>b.addEventListener('click',()=>bulkFolder(b.dataset.slug))); qsa('.tool-audio-preset').forEach((b)=>b.addEventListener('click',()=>quickAudioPreset(b.dataset.preset))); $('openFoldersToolBtn').addEventListener('click',()=>showView('folders')); $('exportM3uBtn').addEventListener('click',()=>exportSpecial('/api/export/m3u')); $('exportLyricsBundleBtn').addEventListener('click',()=>exportSpecial('/api/export/lyrics')); $('copyTitlesBtn').addEventListener('click',()=>copySelected('titles')); $('copySunoLinksBtn').addEventListener('click',()=>copySelected('links')); $('runReportBtn').addEventListener('click',runReport); $('clearToolOutputBtn').addEventListener('click',()=>toolOutput('Ovde će se pojaviti rezultat izveštaja ili masovne radnje.')); $('fullBackupBtn').addEventListener('click',fullBackupAction); $('clearLogsToolBtn').addEventListener('click',clearLogsAction); $('openDataFolderBtn').addEventListener('click',openDataFolder); $('shutdownProgramBtn').addEventListener('click',shutdownProgram);
  $('resetSunoFieldsBtn').addEventListener('click', resetCurrentSongEdits); $('deleteLibrarySongBtn').addEventListener('click', deleteCurrentSong); $('refreshHistoryBtn').addEventListener('click',loadSongHistory); $('saveSubtitleCuesBtn').addEventListener('click',saveSubtitleCues); $('compareYoutubeTextBtn').addEventListener('click',compareYoutubeText); $('shiftCuesBackBtn').addEventListener('click',()=>shiftSubtitleCues(-0.1)); $('shiftCuesForwardBtn').addEventListener('click',()=>shiftSubtitleCues(0.1)); $('addCueBtn').addEventListener('click',()=>{syncCuesFromEditor();const start=state.subtitleCues.length?state.subtitleCues.at(-1).end:0;state.subtitleCues.push({start,end:start+3,text:''});renderSubtitleCues();}); $('subtitleCueEditor').addEventListener('click',e=>{const b=e.target.closest('.cue-delete');if(!b)return;const row=b.closest('.subtitle-cue-row');row.remove();syncCuesFromEditor();renderSubtitleCues();});
  $('refreshAdvancedStatusBtn').addEventListener('click',loadAdvancedStatus); $('chooseIncrementalBackupDirBtn').addEventListener('click',()=>chooseFolder('incrementalBackupDir')); $('runIncrementalBackupBtn').addEventListener('click',runIncrementalBackup); $('chooseRelocateRootBtn').addEventListener('click',()=>chooseFolder('relocateRootDir')); $('runRelocateBtn').addEventListener('click',runRelocate); $('runQualityAnalysisBtn').addEventListener('click',runQuality); $('saveYoutubeScheduleBtn').addEventListener('click',saveYoutubeSchedule); $('chooseCloudBackupDirBtn').addEventListener('click',()=>chooseFolder('cloudBackupDir')); $('runCloudBackupBtn').addEventListener('click',runCloudBackup); $('chooseCloudRestoreBtn').addEventListener('click',chooseCloudRestore); $('restoreCloudBackupBtn').addEventListener('click',restoreCloudBackup); $('runStemsBtn').addEventListener('click',runStems); $('runTranscriptionBtn').addEventListener('click',runTranscription); $('installStemsBtn').addEventListener('click',installStemsPlugin); $('installTranscriptionBtn').addEventListener('click',installTranscriptionPlugin); $('saveUpdateUrlBtn').addEventListener('click',saveUpdateUrl); $('checkUpdateBtn').addEventListener('click',checkProgramUpdate); $('downloadUpdateBtn').addEventListener('click',downloadProgramUpdate);
  $('saveAuddTokenBtn')?.addEventListener('click',saveAuddToken); $('recognizeMusicBtn')?.addEventListener('click',recognizeMusic); $('chooseMusicRecognitionFileBtn')?.addEventListener('click',chooseMusicRecognitionFile); $('refreshRecognitionHistoryBtn')?.addEventListener('click',loadMusicRecognitionHistory); $('openRecognitionFolderBtn')?.addEventListener('click',openRecognitionFolder); $('musicRecognitionHistory')?.addEventListener('click',(e)=>{const show=e.target.closest('.recognition-show');if(show){const r=state.recognitionHistory.find(x=>Number(x.id)===Number(show.dataset.id));if(r)showRecognitionResult({...r,...(r.result||{}),history_id:r.id});}const retry=e.target.closest('.recognition-retry');if(retry)retryRecognition(Number(retry.dataset.id));const add=e.target.closest('.recognition-add');if(add)addRecognitionToLibrary(Number(add.dataset.id));}); $('musicRecognitionResult')?.addEventListener('click',(e)=>{const retry=e.target.closest('.recognition-retry');if(retry)retryRecognition(Number(retry.dataset.id));const add=e.target.closest('.recognition-add');if(add)addRecognitionToLibrary(Number(add.dataset.id));});
  $('chooseSongFinderFileBtn')?.addEventListener('click',chooseSongFinderFile); $('analyzeSongFinderBtn')?.addEventListener('click',analyzeSongFinder); $('songFinderIndexAllBtn')?.addEventListener('click',()=>runSongFinderIndex(false)); $('songFinderIndexRefreshBtn')?.addEventListener('click',()=>runSongFinderIndex(false)); $('songFinderIndexRebuildBtn')?.addEventListener('click',rebuildSongFinderIndex); $('refreshSongFinderResultsBtn')?.addEventListener('click',loadSongFinderResults); $('songFinderResults')?.addEventListener('click',(e)=>{const retry=e.target.closest('.song-finder-retry');if(retry)retrySongFinder(Number(retry.dataset.id));}); $('songFinderResult')?.addEventListener('click',(e)=>{const retry=e.target.closest('.song-finder-retry');if(retry)retrySongFinder(Number(retry.dataset.id));});
  window.addEventListener('keydown', (e) => { if (e.key === 'Escape') $('songModal').classList.add('hidden'); if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='f'){e.preventDefault();showView('library');$('searchInput').focus();} if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='n'){e.preventDefault();checkNewSongs();} if(e.code==='Space'&&['INPUT','TEXTAREA','SELECT'].includes(document.activeElement?.tagName)===false){e.preventDefault();const a=$('audioPlayer');a.paused?a.play().catch(()=>{}):a.pause();} });
}

async function loadMusicRecognitionStatus(){
  try{const d=await api('/api/music-recognition/status',{timeoutMs:10000,retries:0});const box=$('musicRecognitionStatus');if(box){box.className=`inline-message ${d.configured?'success':'warning'}`;box.textContent=d.configured?`Pronalazač pesme je spreman. Sačuvano pokušaja: ${Number(d.history_count||0)}.`:'Unesi i sačuvaj AudD API token. Bez referentne baze nije moguće pouzdano prepoznati nepoznatu komercijalnu pesmu.';}if($('recognitionHistoryCount'))$('recognitionHistoryCount').textContent=String(Number(d.history_count||0));}catch(e){}
}
async function saveAuddToken(){
  const token=$('auddToken')?.value.trim()||'';if(!token)return toast('Unesi AudD API token.','warning');
  try{await api('/api/music-recognition/settings',{method:'POST',body:{audd_token:token},timeoutMs:30000,retries:0});$('auddToken').value='';toast('AudD token je sačuvan lokalno.','success');loadMusicRecognitionStatus();}catch(e){toast(e.message,'error',12000);}
}
function recognitionResultCard(r){
  if(!r?.found)return `<article class="youtube-match-card"><div class="youtube-match-main"><span class="match-score warning">NIJE PRONAĐENO</span><h3>${escapeHtml(r?.original_filename||'Audio/video isečak')}</h3><p>${escapeHtml(r?.message||r?.error_message||'Pesma nije prepoznata.')}</p></div></article>`;
  return `<article class="youtube-match-card"><div class="youtube-match-main"><span class="match-score">PRONAĐENO</span><h3>${escapeHtml(r.artist||'Nepoznat izvođač')} — ${escapeHtml(r.title||'Nepoznat naslov')}</h3><p>Album: ${escapeHtml(r.album||'Nije naveden')} · Datum: ${escapeHtml(r.release_date||'Nije naveden')} · Deo: ${escapeHtml(r.timecode||'nije naveden')}</p>${r.song_link?`<p><a href="${escapeHtml(r.song_link)}" target="_blank" rel="noopener">Otvori pesmu i servise</a></p>`:''}</div><div class="button-row">${r.history_id||r.id?`<button class="btn primary small recognition-add" data-id="${Number(r.history_id||r.id)}">Dodaj u Biblioteku</button><button class="btn ghost small recognition-retry" data-id="${Number(r.history_id||r.id)}">Ponovi</button>`:''}</div></article>`;
}
function showRecognitionResult(r){state.recognitionCurrent=r||null;const out=$('musicRecognitionResult');if(out)out.innerHTML=recognitionResultCard(r||{});}
function renderMusicRecognitionHistory(){
  const box=$('musicRecognitionHistory');if(!box)return;
  if($('recognitionHistoryCount'))$('recognitionHistoryCount').textContent=String(state.recognitionHistory.length);
  if(!state.recognitionHistory.length){box.innerHTML='<div class="empty compact"><p>Istorija je prazna. Izaberi isečak i pokreni Pronalazač pesme.</p></div>';return;}
  box.innerHTML=state.recognitionHistory.map((r)=>`<article class="youtube-match-card recognition-history-item" data-id="${Number(r.id)}"><div class="youtube-match-main"><span class="match-score ${r.found?'':'warning'}">${r.found?'PRONAĐENO':'NIJE PRONAĐENO'}</span><h3>${escapeHtml(r.found?`${r.artist||'Nepoznat izvođač'} — ${r.title||'Nepoznat naslov'}`:(r.original_filename||'Isečak'))}</h3><p>${escapeHtml(r.created_at||'')} ${r.library_song_id?'· dodato u Biblioteku':''}</p></div><div class="button-row"><button class="btn ghost small recognition-show" data-id="${Number(r.id)}">Prikaži</button><button class="btn ghost small recognition-retry" data-id="${Number(r.id)}">Ponovi</button>${r.found&&!r.library_song_id?`<button class="btn primary small recognition-add" data-id="${Number(r.id)}">Dodaj u Biblioteku</button>`:''}</div></article>`).join('');
}
async function loadMusicRecognitionHistory(){try{const d=await api('/api/music-recognition/history',{timeoutMs:15000,retries:0});state.recognitionHistory=d.items||[];renderMusicRecognitionHistory();}catch(e){const box=$('musicRecognitionHistory');if(box)box.innerHTML=`<div class="inline-message error">${escapeHtml(e.message)}</div>`;}}
async function chooseMusicRecognitionFile(){
  try{const d=await api('/api/choose-file',{method:'POST',body:{initial:$('musicRecognitionPath')?.value||'',filter:'Audio i video (*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.mp4;*.mov;*.mkv;*.webm)|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.mp4;*.mov;*.mkv;*.webm|Svi fajlovi (*.*)|*.*'}});if(d.path)$('musicRecognitionPath').value=d.path;return d.path||'';}catch(e){toast(e.message,'error',10000);return '';}
}
async function recognizeMusic(){
  let sourcePath=$('musicRecognitionPath')?.value.trim()||'';const file=$('musicRecognitionFile')?.files?.[0];
  if(!sourcePath&&!file)return toast('Izaberi audio ili video isečak.','warning');
  const status=$('musicRecognitionStatus'),out=$('musicRecognitionResult');status.className='inline-message warning';status.textContent='Analiziram sačuvani isečak i tražim pesmu…';out.innerHTML='';
  try{const body={start_seconds:Number($('musicRecognitionStart')?.value||0),duration_seconds:Number($('musicRecognitionDuration')?.value||25)};
    if(sourcePath)body.source_path=sourcePath;else{if(file.size>120*1024*1024)throw new Error('Fajl je prevelik za direktan uvoz. Koristi dugme „Izaberi fajl“ da program pročita lokalnu putanju.');const base64=await new Promise((resolve,reject)=>{const r=new FileReader();r.onload=()=>resolve(String(r.result).split(',',2)[1]||'');r.onerror=()=>reject(new Error('Ne mogu da pročitam fajl.'));r.readAsDataURL(file);});body.filename=file.name;body.audio_base64=base64;}
    const d=await api('/api/music-recognition/recognize',{method:'POST',body,timeoutMs:180000,retries:0});const r=d.result||{};showRecognitionResult(r);state.recognitionHistory=d.history||state.recognitionHistory;renderMusicRecognitionHistory();
    status.className=`inline-message ${r.found?'success':'warning'}`;status.textContent=r.found?`Pesma je pronađena preko ${r.provider||'AudD'}. Isečak i rezultat su trajno sačuvani.`:(r.message||'Pesma nije prepoznata. Isečak je sačuvan za ponovnu proveru.');
    loadMusicRecognitionStatus();
  }catch(e){status.className='inline-message error';status.textContent=e.message;toast(e.message,'error',15000);}
}
async function retryRecognition(id){try{const d=await api('/api/music-recognition/retry',{method:'POST',body:{id,start_seconds:Number($('musicRecognitionStart')?.value||0),duration_seconds:Number($('musicRecognitionDuration')?.value||25)},timeoutMs:180000,retries:0});showRecognitionResult(d.result||{});state.recognitionHistory=d.history||[];renderMusicRecognitionHistory();toast((d.result||{}).found?'Pesma je pronađena.':'Ponovna provera je završena.','success');loadMusicRecognitionStatus();}catch(e){toast(e.message,'error',15000);}}
async function addRecognitionToLibrary(id){try{const d=await api('/api/music-recognition/add-to-library',{method:'POST',body:{id},timeoutMs:60000,retries:0});state.recognitionHistory=d.history||[];renderMusicRecognitionHistory();await loadSongs(true);toast(d.message||'Pesma je dodata u Biblioteku.','success',10000);}catch(e){toast(e.message,'error',12000);}}
async function openRecognitionFolder(){try{const d=await api('/api/music-recognition/status');await api('/api/open-folder',{method:'POST',body:{path:d.folder}});}catch(e){toast(e.message,'error');}}

async function loadSongFinderStatus(){
  try{const d=await api('/api/song-finder/status',{timeoutMs:15000,retries:0});
    if($('songFinderIndexedCount'))$('songFinderIndexedCount').textContent=String(Number(d.songs_indexed||0));
    const box=$('songFinderIndexStatus');if(box){const ok=Number(d.songs_indexed||0)>0;box.className=`inline-message ${ok?'success':'warning'}`;box.textContent=`Indeksirano: ${Number(d.songs_indexed||0)} / ${Number(d.songs_with_audio||0)} pesama sa lokalnim audio fajlom (od ukupno ${Number(d.songs_total||0)} u Biblioteci).`+(d.last_indexed_at?` Poslednje indeksiranje: ${d.last_indexed_at}.`:' Indeksiranje još nije pokrenuto.');}
  }catch(e){const box=$('songFinderIndexStatus');if(box){box.className='inline-message error';box.textContent=e.message;}}
}
function songFinderBadge(status){
  if(status==='confirmed')return '<span class="match-score">POTVRĐENO</span>';
  if(status==='possible')return '<span class="match-score warning">MOGUĆE PODUDARANJE</span>';
  return '<span class="match-score warning">NIJE PRONAĐENA MOJA PESMA</span>';
}
function songFinderResultCard(r){
  const res=r?.result||r||{};
  const historyId=Number(r?.history_id||res.history_id||r?.id||0);
  const retryBtn=historyId?`<button class="btn ghost small song-finder-retry" data-id="${historyId}">Ponovi</button>`:'';
  if(!res.found){return `<article class="youtube-match-card"><div class="youtube-match-main">${songFinderBadge('not_found')}<h3>${escapeHtml(r?.original_filename||res.file||'Isečak')}</h3><p>Provereno pesama u indeksu: ${Number(res.songs_checked||0)} / ${Number(res.songs_indexed_total||0)}.</p></div></article>`;}
  if(res.multiple_songs_detected){
    const items=(res.distinct_matches||[]).map(m=>`<li>${songFinderBadge(m.status)} <strong>${escapeHtml(m.title||m.song_id)}</strong> — ${Number(m.confidence||0)}% · Isečak: ${Number(m.clip_start||0).toFixed(1)}s–${Number(m.clip_end||0).toFixed(1)}s (deo pesme: ${Number(m.song_start||0).toFixed(1)}s–${Number(m.song_end||0).toFixed(1)}s)</li>`).join('');
    return `<article class="youtube-match-card"><div class="youtube-match-main"><span class="match-score">PRONAĐENO VIŠE MOJIH PESAMA</span><h3>${escapeHtml(r?.original_filename||res.file||'Isečak')}</h3><p class="muted">Ovaj isečak sadrži više od jedne moje pesme, svaka u drugom delu isečka:</p><ul>${items}</ul><p class="muted">Motor: ${escapeHtml(res.engine||'chromaprint+spectral')} · Provereno pesama: ${Number(res.songs_checked||0)} / ${Number(res.songs_indexed_total||0)}</p></div><div class="button-row">${retryBtn}</div></article>`;
  }
  const p=res.primary||{};
  const timing=`Pesma: ${Number(p.song_start||0).toFixed(1)}s–${Number(p.song_end||0).toFixed(1)}s · Isečak: ${Number(p.clip_start||0).toFixed(1)}s–${Number(p.clip_end||0).toFixed(1)}s`;
  const alts=(res.alternatives||[]).map(a=>`<li>${escapeHtml(a.title||a.song_id)} — ${songFinderBadge(a.status)} (${Number(a.confidence||0)}%)</li>`).join('');
  return `<article class="youtube-match-card"><div class="youtube-match-main">${songFinderBadge(res.status)}<h3>${escapeHtml(p.title||res.title||'Nepoznata pesma iz Biblioteke')}</h3><p>Pouzdanost: ${Number(res.confidence||p.confidence||0)}% · ${escapeHtml(timing)}</p><p class="muted">Motor: ${escapeHtml(res.engine||'chromaprint+spectral')} · Provereno pesama: ${Number(res.songs_checked||0)} / ${Number(res.songs_indexed_total||0)}</p>${alts?`<p class="muted">Ostale mogućnosti:</p><ul>${alts}</ul>`:''}</div><div class="button-row">${retryBtn}</div></article>`;
}
function renderSongFinderResults(){
  const box=$('songFinderResults');if(!box)return;
  if(!state.songFinderResults.length){box.innerHTML='<div class="empty compact"><p>Nema sačuvanih rezultata. Ubaci Shorts, video ili audio i pokreni Pronalazač mojih pesama.</p></div>';return;}
  box.innerHTML=state.songFinderResults.map((r)=>`<article class="youtube-match-card" data-id="${Number(r.id)}"><div class="youtube-match-main">${songFinderBadge((r.result||{}).status||(r.found?'confirmed':'not_found'))}<h3>${escapeHtml(r.found?(r.title||'Prepoznata pesma'):(r.original_filename||'Isečak'))}</h3><p>${escapeHtml(r.created_at||'')}</p></div><div class="button-row"><button class="btn ghost small song-finder-retry" data-id="${Number(r.id)}">Ponovi</button></div></article>`).join('');
}
async function loadSongFinderResults(){try{const d=await api('/api/song-finder/results',{timeoutMs:15000,retries:0});state.songFinderResults=d.items||[];renderSongFinderResults();}catch(e){const box=$('songFinderResults');if(box)box.innerHTML=`<div class="inline-message error">${escapeHtml(e.message)}</div>`;}}
async function chooseSongFinderFile(){
  try{const d=await api('/api/choose-file',{method:'POST',body:{initial:$('songFinderPath')?.value||'',filter:'Audio i video (*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.mp4;*.mov;*.mkv;*.webm)|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.mp4;*.mov;*.mkv;*.webm|Svi fajlovi (*.*)|*.*'}});if(d.path)$('songFinderPath').value=d.path;return d.path||'';}catch(e){toast(e.message,'error',10000);return '';}
}
async function analyzeSongFinder(){
  const sourcePath=$('songFinderPath')?.value.trim()||'';
  if(!sourcePath)return toast('Izaberi Shorts, video ili audio fajl.','warning');
  const status=$('songFinderStatus'),out=$('songFinderResult');status.className='inline-message warning';status.textContent='Proveravam lokalno da li isečak sadrži neku od mojih pesama…';out.innerHTML='';
  try{const d=await api('/api/song-finder/analyze-file',{method:'POST',body:{source_path:sourcePath},timeoutMs:180000,retries:0});const r=d.result||{};out.innerHTML=songFinderResultCard(r);
    status.className=`inline-message ${r.found?'success':'warning'}`;status.textContent=r.found?`Pronađeno lokalno, bez tokena i interneta: ${r.status_label||''}.`:'Ova moja pesma nije pronađena u lokalnom indeksu.';
    loadSongFinderResults();
  }catch(e){status.className='inline-message error';status.textContent=e.message;toast(e.message,'error',15000);}
}
async function retrySongFinder(id){
  try{const d=await api('/api/song-finder/result/retry',{method:'POST',body:{id},timeoutMs:180000,retries:0});const out=$('songFinderResult');if(out)out.innerHTML=songFinderResultCard(d.result||{});toast((d.result||{}).found?'Pesma je pronađena.':'Ponovna provera je završena.','success');loadSongFinderResults();}catch(e){toast(e.message,'error',15000);}
}
async function runSongFinderIndex(force){
  try{await startBackground('/api/song-finder/index',{force:!!force});}catch(e){toast(e.message,'error',12000);}
}
function rebuildSongFinderIndex(){if(!confirm('Ponovo obraditi sve pesme u indeksu Pronalazača mojih pesama? Ovo može potrajati za veliku biblioteku.'))return;runSongFinderIndex(true);}

async function init() { applyTheme(localStorage.getItem('suno-theme') || 'default', false); applyA11ySettings(loadA11ySettings()); bindEvents(); protectLibrarySearchFromAutofill(); renderThemes(); await loadSecurityStatus(); if(state.security?.locked)return; await Promise.all([loadStatus(), loadCollections(), loadStats()]); await loadSongs(true); loadAudioToolsStatus(); loadAdvancedStatus(); loadMusicRecognitionStatus(); loadMusicRecognitionHistory(); loadSongFinderStatus(); loadWatchedFolders(); checkProgramUpdate(); setInterval(()=>{if(!state.security?.locked)loadStatus();},3000); }
document.addEventListener('DOMContentLoaded', init);
