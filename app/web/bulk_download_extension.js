/* Suno Pesme Studio — whole-library download controls.
   This file is concatenated to app.js by server.py, so it intentionally shares
   the existing state/api/helper lexical scope instead of duplicating app logic. */

const _bulkOriginalDownloadOptions = downloadOptions;
downloadOptions = function downloadOptionsWithSongFolders(forceMp3 = false) {
  const options = _bulkOriginalDownloadOptions(forceMp3);
  options.folder_per_song = Boolean($('optSongFolders')?.checked);
  return options;
};

saveToFolder = async function configurableSaveToFolder(ids) {
  if (!ids.length) return toast('Prvo izaberi pesmu ili više pesama.', 'error');
  const target = await chooseFolder('downloadTargetInput');
  if (!target) return;
  $('downloadTargetInput').value = target;
  showView('download');
  toast(`Izabrano je ${ids.length} pesama. Sada izaberi MP3/WAV, tekst, omot i ostale fajlove pa klikni „Pokreni preuzimanje“.`, 'success', 12000);
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
      // Locally imported files use the local-* id namespace.  The remaining
      // synced records are the user's Suno-library records.
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
  const set = (id, checked) => { if ($(id)) $(id).checked = checked; };
  if (preset === 'audio') {
    set('optCover', false);
    set('optLyrics', false);
    set('optSynced', false);
    set('optMetadata', false);
    set('optEmbed', true);
    set('optVideo', false);
    toast('Podešeno: samo izabrani audio format, bez dodatnih fajlova.', 'info');
  } else if (preset === 'audio_text') {
    set('optCover', false);
    set('optLyrics', true);
    set('optSynced', true);
    set('optMetadata', false);
    set('optEmbed', true);
    set('optVideo', false);
    toast('Podešeno: audio + TXT tekst + LRC/SRT kada postoje.', 'info');
  } else {
    set('optCover', true);
    set('optLyrics', true);
    set('optSynced', true);
    set('optMetadata', true);
    set('optEmbed', true);
    set('optVideo', true);
    toast('Podešeno: sve dostupno — audio plus omot, tekst, LRC/SRT, JSON i Suno MP4 kada postoji.', 'info', 9000);
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
  }

  const bigNumber = $('downloadSelectedCount')?.closest('.big-number');
  if (bigNumber && !$('bulkSelectionHint')) {
    const hint = document.createElement('div');
    hint.id = 'bulkSelectionHint';
    hint.className = 'inline-message success';
    hint.innerHTML = '<strong>NEMA OGRANIČENJA NA 200 ZA PREUZIMANJE.</strong> 50/100/200 je samo broj pesama prikazanih na jednoj stranici. Dugmad iznad mogu da izaberu sve sinhronizovane pesme odjednom. Za preuzimanje nije potreban fingerprint indeks — dovoljno je da je pesma u tvojoj Suno biblioteci u programu.';
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
    presets.innerHTML = '<button id="bulkPresetAudioOnly" type="button" class="btn ghost small">SAMO AUDIO</button><button id="bulkPresetAudioText" type="button" class="btn secondary small">AUDIO + TEKST</button><button id="bulkPresetAll" type="button" class="btn success small">SVE DOSTUPNO</button>';
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
    info.textContent = 'Možeš posebno da biraš: MP3/WAV, JPG omot, TXT tekst, LRC/SRT, JSON Suno podatke, ID3 podatke u MP3-u i Suno MP4 kada postoji.';
    downloadHeading.insertAdjacentElement('afterend', info);
  }
}

document.addEventListener('DOMContentLoaded', installWholeLibraryDownloadUi);
