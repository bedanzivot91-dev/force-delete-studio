/* Suno Pesme Studio — whole-library download controls.
   This file is concatenated to app.js by server.py, so it intentionally shares
   the existing state/api/helper lexical scope instead of duplicating app logic. */

const _bulkOriginalDownloadOptions = downloadOptions;
const _bulkOriginalStartDownload = startDownload;
let _bulkDownloadPreset = '';

function _bulkSetAudioFormat(format) {
  qsa('input[name="audioFormat"]').forEach((radio) => {
    radio.checked = radio.value === format;
    const card = radio.closest('.format-card');
    if (card) card.classList.toggle('active', radio.checked);
  });
}

function _bulkGetAudioFormat() {
  return document.querySelector('input[name="audioFormat"]:checked')?.value || 'mp3';
}

downloadOptions = function downloadOptionsWithSongFolders(forceMp3 = false) {
  const options = _bulkOriginalDownloadOptions(forceMp3);
  options.folder_per_song = Boolean($('optSongFolders')?.checked);
  if (forceMp3 || _bulkDownloadPreset === 'audio' || _bulkDownloadPreset === 'audio_text') {
    options.format = 'mp3';
    options.audio_format = 'mp3';
  } else {
    const format = _bulkGetAudioFormat();
    options.audio_format = format;
    options.format = format;
  }
  if (_bulkDownloadPreset === 'audio') {
    options.cover = false;
    options.lyrics = false;
    options.synced_lyrics = false;
    options.metadata = false;
    options.embed_tags = false;
    options.video = false;
  } else if (_bulkDownloadPreset === 'audio_text') {
    options.cover = false;
    options.lyrics = true;
    options.synced_lyrics = false;
    options.metadata = false;
    options.embed_tags = false;
    options.video = false;
  }
  return options;
};

startDownload = async function bulkStartDownload(forceMp3 = false) {
  const ids = selectedIds();
  if (!ids.length) {
    showView('library');
    return toast('Prvo izaberi pesme.', 'error');
  }
  try {
    const options = downloadOptions(forceMp3);
    await startBackground('/api/download/start', { ids, options });
    const label = _bulkDownloadPreset === 'audio_text'
      ? 'MP3 + tekst'
      : _bulkDownloadPreset === 'audio'
        ? 'samo MP3'
        : 'izabrani sadržaj';
    toast(`Preuzimanje ${ids.length} pesama (${label}) je pokrenuto.`, 'success', 9000);
  } catch (e) {
    toast(e.message, 'error', 12000);
  }
};

saveToFolder = async function configurableSaveToFolder(ids) {
  if (!ids.length) return toast('Prvo izaberi pesmu ili više pesama.', 'error');
  const target = await chooseFolder('downloadTargetInput');
  if (!target) return;
  $('downloadTargetInput').value = target;
  showView('download');
  toast(`Izabrano je ${ids.length} pesama. Sada izaberi MP3/WAV i prateće fajlove, pa pokreni preuzimanje.`, 'success', 12000);
};

function _bulkAllFilterParams(filterName = 'all') {
  return new URLSearchParams({
    search: '',
    filter: filterName,
    collection_id: '0',
    source_group: '',
    date_from: '',
    date_to: '',
    min_duration: '0',
    max_duration: '0',
    limit: '0',
  });
}

async function selectWholeLibraryForBulk(scope = 'filtered') {
  try {
    let params;
    if (scope === 'filtered') {
      params = currentSongFilterParams({ limit: '0' });
    } else if (scope === 'not_downloaded') {
      params = _bulkAllFilterParams('not_downloaded');
    } else {
      params = _bulkAllFilterParams('all');
    }

    const data = await api(`/api/song-ids?${params.toString()}`, { timeoutMs: 120000 });
    let ids = Array.isArray(data.ids) ? data.ids.map(String) : [];

    if (scope === 'suno') {
      ids = ids.filter((id) => !id.startsWith('local-'));
    }

    state.selected = new Set(ids);
    renderSongs();
    updateSelectionUi();

    const label = scope === 'suno'
      ? 'sa Suno naloga'
      : scope === 'not_downloaded'
        ? 'nepreuzetih iz cele biblioteke'
        : 'iz trenutnog filtera, kroz sve stranice';
    toast(`Izabrano je ${state.selected.size} pesama ${label}. Broj 50/100/200 važi samo za prikaz jedne stranice.`, 'success', 12000);
    return ids;
  } catch (error) {
    toast(error.message, 'error', 12000);
    return [];
  }
}

function _bulkButton(id, text, className, handler) {
  if ($(id)) return $(id);
  const button = document.createElement('button');
  button.id = id;
  button.type = 'button';
  button.className = className;
  button.textContent = text;
  button.addEventListener('click', handler);
  return button;
}

function _setDownloadExtras(preset) {
  _bulkDownloadPreset = preset;
  _bulkSetAudioFormat('mp3');

  const set = (id, checked) => { if ($(id)) $(id).checked = checked; };
  if (preset === 'audio') {
    set('optCover', false);
    set('optLyrics', false);
    set('optSynced', false);
    set('optMetadata', false);
    set('optEmbed', false);
    set('optVideo', false);
    toast('PODEŠENO: SAMO MP3.', 'info', 7000);
  } else if (preset === 'audio_text') {
    set('optCover', false);
    set('optLyrics', true);
    set('optSynced', false);
    set('optMetadata', false);
    set('optEmbed', false);
    set('optVideo', false);
    toast('PODEŠENO: MP3 + TEKST PESME.', 'success', 7000);
  } else {
    set('optCover', true);
    set('optLyrics', true);
    set('optSynced', true);
    set('optMetadata', true);
    set('optEmbed', true);
    set('optVideo', true);
    toast('PODEŠENO: SVE DOSTUPNO.', 'info', 9000);
  }
}

function installWholeLibraryDownloadUi() {
  const selectionActions = document.querySelector('#view-library .selection-actions');
  if (selectionActions && !$('selectAllFilteredBtn')) {
    const filtered = _bulkButton(
      'selectAllFilteredBtn',
      'IZABERI SVE REZULTATE',
      'btn small success',
      () => selectWholeLibraryForBulk('filtered'),
    );
    const suno = _bulkButton(
      'selectAllSunoBtn',
      'SVE SA SUNO NALOGA',
      'btn small primary',
      () => selectWholeLibraryForBulk('suno'),
    );
    selectionActions.prepend(suno);
    selectionActions.prepend(filtered);
  }

  if ($('saveSelectedToFolderBtn')) {
    $('saveSelectedToFolderBtn').textContent = 'SAČUVAJ / IZABERI SADRŽAJ';
    $('saveSelectedToFolderBtn').title = 'Izaberi folder, format i koje prateće fajlove želiš da sačuvaš.';
  }

  const downloadStepOneRow = $('goLibraryBtn')?.parentElement;
  if (downloadStepOneRow && !$('downloadSelectAllSunoBtn')) {
    downloadStepOneRow.appendChild(_bulkButton(
      'downloadSelectAllFilteredBtn',
      'SVE IZ TRENUTNOG FILTERA',
      'btn secondary',
      () => selectWholeLibraryForBulk('filtered'),
    ));
    downloadStepOneRow.appendChild(_bulkButton(
      'downloadSelectAllSunoBtn',
      'SVE SA SUNO NALOGA',
      'btn primary',
      () => selectWholeLibraryForBulk('suno'),
    ));
  }

  if ($('selectNotDownloadedBtn')) {
    $('selectNotDownloadedBtn').textContent = 'SVE NEPREUZETE — CELA BIBLIOTEKA';
    $('selectNotDownloadedBtn').onclick = () => selectWholeLibraryForBulk('not_downloaded');
  }

  const bigNumber = $('downloadSelectedCount')?.closest('.big-number');
  if (bigNumber && !$('bulkSelectionHint')) {
    const hint = document.createElement('div');
    hint.id = 'bulkSelectionHint';
    hint.className = 'inline-message success';
    hint.innerHTML = '<strong>NEMA OGRANIČENJA NA 200 ZA PREUZIMANJE.</strong> 50/100/200 je samo broj pesama prikazanih na jednoj strani. Dugmad iznad mogu da izaberu sve sinhronizovane pesme odjednom.';
    bigNumber.insertAdjacentElement('afterend', hint);
  }

  const extrasGrid = $('optCover')?.closest('.option-grid');
  if (extrasGrid && !$('optSongFolders')) {
    const folderLabel = document.createElement('label');
    folderLabel.className = 'toggle-row';
    folderLabel.innerHTML = '<input id="optSongFolders" type="checkbox"><span><strong>Poseban folder za svaku pesmu</strong><small>Naziv pesme [kratki Suno ID] — sprečava mešanje fajlova istog naslova</small></span>';
    const monthLabel = $('optMonthFolders')?.closest('.toggle-row');
    if (monthLabel) extrasGrid.insertBefore(folderLabel, monthLabel);
    else extrasGrid.appendChild(folderLabel);

    const saved = localStorage.getItem('suno-folder-per-song');
    $('optSongFolders').checked = saved === null ? true : saved === '1';
    $('optSongFolders').addEventListener('change', () => {
      localStorage.setItem('suno-folder-per-song', $('optSongFolders').checked ? '1' : '0');
    });
  }

  if (extrasGrid && !$('bulkDownloadPresets')) {
    const presets = document.createElement('div');
    presets.id = 'bulkDownloadPresets';
    presets.className = 'button-row';
    presets.innerHTML = '<button id="bulkPresetAudioOnly" type="button" class="btn ghost small">SAMO MP3</button><button id="bulkPresetAudioText" type="button" class="btn secondary small">MP3 + TEKST</button><button id="bulkPresetAll" type="button" class="btn success small">SVE DOSTUPNO</button>';
    extrasGrid.insertAdjacentElement('beforebegin', presets);
    $('bulkPresetAudioOnly').addEventListener('click', () => _setDownloadExtras('audio'));
    $('bulkPresetAudioText').addEventListener('click', () => _setDownloadExtras('audio_text'));
    $('bulkPresetAll').addEventListener('click', () => _setDownloadExtras('all'));
  }

  const downloadHeading = extrasGrid?.closest('.form-panel')?.querySelector('.section-title');
  if (downloadHeading && !$('downloadAvailableFilesInfo')) {
    const info = document.createElement('p');
    info.id = 'downloadAvailableFilesInfo';
    info.className = 'muted';
    info.textContent = 'Brzi izbor: SAMO MP3 ili MP3 + TEKST. Ručno možeš uključiti i omot, LRC/SRT, JSON, ID3 i Suno MP4 kada postoje.';
    downloadHeading.insertAdjacentElement('afterend', info);
  }
}

document.addEventListener('DOMContentLoaded', installWholeLibraryDownloadUi);
