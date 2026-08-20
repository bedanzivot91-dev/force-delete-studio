/* Practical Video Studio actions missing from the first timeline pass.
 * Adds Suno timing import, LRC/SRT import/export and one-click transcription
 * without creating a second editor or duplicating the timeline.
 */
(() => {
  'use strict';
  const workspace = $('productionWorkspace');
  if (!workspace || $('studioSubtitleWorkflow')) return;

  const css = document.createElement('style');
  css.textContent = `
    .studio-subtitle-flow{margin:0 12px 12px;padding:12px;border:1px solid #31507a;background:linear-gradient(135deg,rgba(37,99,235,.10),rgba(14,165,233,.05));border-radius:8px}
    .studio-subtitle-flow h3{margin:0 0 4px}.studio-subtitle-flow p{margin:0 0 10px}.studio-subtitle-flow .button-row{gap:7px;flex-wrap:wrap}
    .studio-subtitle-flow input[type=file]{display:none}
  `;
  document.head.appendChild(css);

  const flow = document.createElement('section');
  flow.id = 'studioSubtitleWorkflow';
  flow.className = 'studio-subtitle-flow';
  flow.innerHTML = `
    <h3>Tekst i automatski titlovi</h3>
    <p class="muted">Sve ide u ISTI timeline iznad. Nema drugog editora: uvezi stvarni Suno tajming, svoj LRC/SRT ili napravi početni raspored iz teksta pa ga ručno dotegni.</p>
    <div class="button-row">
      <button id="studioPullSunoTiming" class="btn success">POVUCI SUNO TAJMING</button>
      <button id="studioEstimateFromLyrics" class="btn secondary">NAPRAVI TITLOVE IZ TEKSTA</button>
      <button id="studioImportSubtitles" class="btn secondary">UVEZI LRC / SRT</button>
      <button id="studioTranscribe" class="btn secondary">TRANSKRIBUJ AUDIO</button>
      <button id="studioExportLrc" class="btn ghost">PREUZMI LRC</button>
      <button id="studioExportSrt" class="btn ghost">PREUZMI SRT</button>
      <input id="studioSubtitleFile" type="file" accept=".lrc,.srt,text/plain">
    </div>`;
  const timeline = workspace.querySelector('.pws-timeline-panel');
  if (timeline) timeline.insertAdjacentElement('beforebegin', flow);
  else workspace.appendChild(flow);

  function currentSongId() {
    const audio = $('pwsAudio');
    if (audio?.src) {
      try {
        const url = new URL(audio.src, location.href);
        const id = url.searchParams.get('id');
        if (id) return id;
      } catch (_) {}
    }
    return selectedIds()[0] || state.currentSong?.id || '';
  }

  async function confirmReplace(songId) {
    const existing = await api(`/api/subtitles?id=${encodeURIComponent(songId)}`, {timeoutMs:30000,retries:0});
    const count = Array.isArray(existing.cues) ? existing.cues.length : 0;
    return !count || confirm(`Ova pesma već ima ${count} sačuvanih titlova. Zameniti ih novim tajmingom?`);
  }

  function parseLrc(text, duration) {
    const rows = [];
    const rx = /\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]\s*(.*)$/;
    for (const raw of String(text || '').replace(/\r/g,'').split('\n')) {
      const match = raw.match(rx);
      if (!match || !match[4]?.trim()) continue;
      const fractionRaw = match[3] || '0';
      const fraction = Number(`0.${fractionRaw.padEnd(3,'0').slice(0,3)}`);
      rows.push({start_s:Number(match[1])*60+Number(match[2])+fraction,text:match[4].trim()});
    }
    rows.sort((a,b)=>a.start_s-b.start_s);
    return rows.map((row,index)=>({
      start_s:row.start_s,
      end_s:Math.max(row.start_s+.05, Math.min(Number(duration||row.start_s+3), index+1<rows.length ? rows[index+1].start_s-.03 : Number(duration||row.start_s+3))),
      text:row.text,
    })).filter(c=>c.end_s>c.start_s);
  }

  function srtSeconds(raw) {
    const match=String(raw||'').trim().match(/(\d+):(\d{2}):(\d{2})[,.](\d{1,3})/);
    if(!match)return NaN;
    return Number(match[1])*3600+Number(match[2])*60+Number(match[3])+Number(`0.${match[4].padEnd(3,'0').slice(0,3)}`);
  }

  function parseSrt(text) {
    const normalized=String(text||'').replace(/\r/g,'').trim();
    if(!normalized)return [];
    const cues=[];
    for(const block of normalized.split(/\n\s*\n/)){
      const lines=block.split('\n');
      const timeIndex=lines.findIndex(line=>line.includes('-->'));
      if(timeIndex<0)continue;
      const pair=lines[timeIndex].split('-->');
      const start=srtSeconds(pair[0]),end=srtSeconds(pair[1]);
      const body=lines.slice(timeIndex+1).join('\n').replace(/<[^>]+>/g,'').trim();
      if(Number.isFinite(start)&&Number.isFinite(end)&&end>start&&body)cues.push({start_s:start,end_s:end,text:body});
    }
    return cues;
  }

  async function saveAndReload(songId, cues, message) {
    if (!cues.length) throw new Error('Nisam pronašao nijedan ispravan titl/tajming.');
    await api('/api/subtitles/save',{method:'POST',body:{id:songId,cues},timeoutMs:120000,retries:0});
    toast(message, 'success', 10000);
    // The real timeline owns its internal state. Reload through its normal
    // button so preview, waveform, undo stack and renderer all see one source.
    $('pwsLoadSong')?.click();
  }

  $('studioPullSunoTiming').addEventListener('click', async()=>{
    const id=currentSongId();
    if(!id)return toast('Prvo izaberi i učitaj pesmu.','warning');
    try{
      if(!(await confirmReplace(id)))return;
      const [lrc,songData]=await Promise.all([
        api(`/api/song/text?id=${encodeURIComponent(id)}&format=lrc`,{timeoutMs:60000,retries:0}),
        api(`/api/song?id=${encodeURIComponent(id)}`,{timeoutMs:30000,retries:0}),
      ]);
      const cues=parseLrc(String(lrc||''),Number(songData.song?.duration||0));
      await saveAndReload(id,cues,`Učitano ${cues.length} Suno vremenski poravnatih redova u isti timeline.`);
    }catch(error){toast(`Suno tajming nije učitan: ${error.message}`,'error',15000);}
  });

  $('studioEstimateFromLyrics').addEventListener('click', async()=>{
    const id=currentSongId();
    if(!id)return toast('Prvo izaberi i učitaj pesmu.','warning');
    try{
      if(!(await confirmReplace(id)))return;
      const data=await api(`/api/song?id=${encodeURIComponent(id)}`,{timeoutMs:30000,retries:0});
      const song=data.song||{};
      const lines=String(song.lyrics||'').replace(/\r/g,'').split('\n').map(x=>x.trim()).filter(x=>x&&!/^\[[^\]]+\]$/.test(x));
      if(!lines.length)throw new Error('Pesma nema tekst iz kojeg mogu da napravim početni raspored.');
      const duration=Math.max(1,Number(song.duration||0));
      const weights=lines.map(line=>Math.max(1,line.replace(/\s+/g,' ').length));
      const total=weights.reduce((a,b)=>a+b,0);
      let cursor=0;
      const cues=lines.map((line,index)=>{
        const slice=duration*weights[index]/total;
        const start=cursor;cursor=Math.min(duration,cursor+slice);
        return {start_s:start,end_s:Math.max(start+.05,cursor),text:line};
      });
      await saveAndReload(id,cues,`Napravljen je početni raspored za ${cues.length} redova. Ovo je PROCENA iz teksta — pomeri blokove na timeline-u gde treba.`);
    }catch(error){toast(error.message,'error',15000);}
  });

  $('studioImportSubtitles').addEventListener('click',()=>$('studioSubtitleFile').click());
  $('studioSubtitleFile').addEventListener('change',async e=>{
    const file=e.target.files?.[0];e.target.value='';
    const id=currentSongId();
    if(!file||!id)return;
    try{
      if(!(await confirmReplace(id)))return;
      const text=await file.text();
      const songData=await api(`/api/song?id=${encodeURIComponent(id)}`);
      const cues=file.name.toLowerCase().endsWith('.srt')?parseSrt(text):parseLrc(text,Number(songData.song?.duration||0));
      await saveAndReload(id,cues,`Uvezeno ${cues.length} titlova iz ${file.name}.`);
    }catch(error){toast(`Uvoz titlova nije uspeo: ${error.message}`,'error',15000);}
  });

  $('studioTranscribe').addEventListener('click',async()=>{
    const id=currentSongId();
    if(!id)return toast('Prvo izaberi pesmu.','warning');
    const oldSelection=new Set(state.selected);
    try{
      state.selected=new Set([id]);updateSelectionUi();
      await runTranscription();
      toast('Transkripcija je pokrenuta. Kada završi, klikni „Učitaj izabranu pesmu“ da vidiš nove titlove.','success',10000);
    }finally{
      state.selected=oldSelection;updateSelectionUi();
    }
  });

  $('studioExportLrc').addEventListener('click',()=>{const id=currentSongId();if(id)downloadFromUrl(`/api/song/text?id=${encodeURIComponent(id)}&format=lrc`);});
  $('studioExportSrt').addEventListener('click',()=>{const id=currentSongId();if(id)downloadFromUrl(`/api/song/text?id=${encodeURIComponent(id)}&format=srt`);});
})();
