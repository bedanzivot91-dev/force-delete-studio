# NP Video Studio

Desktop program za montažu, uređivanje, titlovanje, obradu i izvoz video-snimaka za YouTube, YouTube
Shorts, TikTok, Instagram i Facebook. Radi lokalno na Windows računaru, bez interneta za osnovne
funkcije.

## VAŽNO — trenutno stanje programa

Kroz Fazu 10 (od planiranih 11) NP Video Studio je zamišljen kao program uporediv po obimu sa
CapCut/DaVinci Resolve, a takav program se realno gradi u mnogo faza tokom dužeg perioda, ne za jednu
sesiju rada. Da ne bismo tvrdili nešto što ne radi, ovde je tačan spisak šta je **stvarno završeno i
testirano**, a šta je **planirano za kasnije**.

### Šta radi u ovoj verziji

- Početni ekran: Novi projekat, Otvori projekat, Nedavni projekti, brze prečice za YouTube / YouTube
  Shorts / TikTok / Instagram Reel / Facebook Reel format, Podešavanja, Dijagnostika sistema.
- Formati projekta: 16:9, 9:16, 1:1, 4:5, 21:9 i ručni unos; rezolucije 720p/1080p/1440p/4K/ručno;
  frame rate 23.976–60 fps ili ručno. Program jasno pokazuje da li je projekat horizontalan,
  vertikalan ili kvadratan.
- Uvoz medija (video, audio, slike) preko dijaloga ili prevlačenjem (drag&drop) u program, sa
  stvarnom analizom fajla preko FFprobe (trajanje, rezolucija, fps, kodek, veličina).
- Media biblioteka sa pregledom uvezenih fajlova, označavanjem omiljenih i uklanjanjem iz projekta
  (bez brisanja originalnog fajla sa diska).
- Čuvanje projekta u `.npvsproject` formatu, sa atomskim upisom (projekat se ne može oštetiti ako
  program padne usred čuvanja).
- Automatsko čuvanje (auto-save) na podesivom intervalu i **oporavak posle pada programa** — ako se
  program neočekivano zatvori, pri sledećem pokretanju program to prepoznaje i nudi nastavak od
  poslednje automatski sačuvane verzije.
- Lista nedavnih projekata u lokalnoj SQLite bazi.
- Podešavanja: tema, folder za projekte, cache folder, auto-save, čuvanje logova, putanje za
  FFmpeg/FFprobe/yt-dlp (opciono — prazno polje znači „nađi sam").
- **Alati i modeli** (ekran, dostupan sa početnog ekrana): stvarno stanje FFmpeg-a, FFprobe-a, yt-dlp-a,
  fpcalc-a (Chromaprint, za prepoznavanje pesama) i lokalnog Whisper modela — svaki alat se stvarno
  pokreće (verzija + izlazni kod), ne samo proverava da li fajl postoji. Whisper model može da se
  preuzme (i otkaže tokom preuzimanja) direktno sa ovog ekrana.
- Svih 8 planiranih tema: Dark Cinematic, Minimal Light, Professional Studio, Obsidian Neon, Arctic
  Glass, Crimson Cyber, Midnight Pro, Ocean Glass. Menjaju se bez restarta programa.
- Dijagnostički ekran koji stvarno proverava .NET okruženje, FFmpeg/FFprobe, foldere, slobodan
  prostor na disku i lokalnu bazu — sa objašnjenjem problema, razlogom i predlogom rešenja, i dugmetom
  za automatsku popravku gde je to moguće. Uključuje i pravljenje ZIP paketa za podršku (logovi +
  informacije o sistemu, bez ličnih video/audio/projektnih fajlova).
- Strukturirano logovanje u fajlove (app i errors logovi, sa rotacijom po danu i podesivim periodom
  čuvanja).
- **Isečci iz pesme** (Alati → Isečci iz pesme): učitate audio fajl, program analizira glasnoću kroz
  celu numeru preko FFmpeg-a i predloži 3 (podesivo) najglasnija, međusobno nepreklapajuća dela u
  trajanju od 30 do 50 sekundi (podesivo), koje možete izvesti kao zasebne audio fajlove — spremne kao
  sirovi materijal za najavu pesme na YouTube Shorts/TikTok/Reels. Ovo je heuristika po glasnoći, ne
  prepoznavanje refrena, i program to jasno kaže u samom interfejsu.
- **Pronađi tekst u pesmi** (Alati → Pronađi tekst u pesmi): ukucate stih, program lokalno transkribuje
  pevanje preko Whisper-a (model se preuzima samo uz vaš izričit klik na dugme, ~75 MB, jednom) i
  prikaže gde misli da se taj tekst peva, sa procenom tačnosti za svako mesto (prepoznavanje pevanja je
  manje pouzdano od govora, program to ne krije). Svako pronađeno mesto može da se izveze kao poseban
  audio isečak. Radi na sopstvenim pesmama - npr. numere sa Suno profila ili sopstvenih YouTube kanala.
- **Preuzmi sa YouTube-a** (Alati → Preuzmi sa YouTube-a): nalepite link ka SVOM YouTube videu (npr.
  pesma napravljena u Suno-u i postavljena na sopstveni kanal), program preko yt-dlp učita naslov,
  kanal i trajanje, tražite da potvrdite da je sadržaj vaš, pa preuzima kompletan audio kao MP3. Radi
  isključivo sa YouTube linkovima (youtube.com/youtu.be) - nije opšti downloader za tuđe video. Preuzeti
  fajl se jednim klikom otvara direktno u alatima „Isečci iz pesme", „Pronađi tekst u pesmi" ili
  „Generiši titlove".
- **Generiši titlove (SRT)** (Alati → Generiši titlove): učitate audio ili video fajl, program ga
  lokalno transkribuje preko Whisper-a (isti model kao za pretragu teksta) i sačuva standardni `.srt`
  fajl sa tačnim vremenima svake linije - spreman za otpremanje na YouTube/TikTok/Reels ili uvoz u bilo
  koji drugi editor. Ovo je samostalan `.srt` fajl, ne titlovi urezani u sliku na NP Video Studio
  timeline-u (to je i dalje planirano za kasniju fazu, videti ispod).
- **Moje pesme** (Alati → Moje pesme): lokalna biblioteka vaših pesama. Uvezete audio fajl, program
  izračuna otisak pesme (Chromaprint/fpcalc, 5 delova numere) i proveri da li je pesma već u biblioteci
  pre nego što je doda - nikad automatski, uvek vam pokaže moguća poklapanja i traži potvrdu. Obrisati
  zapis iz biblioteke ne briše i sam audio fajl, osim ako to izričito ne zatražite.
- **Uređivač titlova** (Alati → Uređivač titlova): uređivanje titlova na nivou svake reči (tajming,
  tekst, undo/redo), uvoz/izvoz u SRT/VTT/ASS/TXT/JSON/LRC.
- **Stilovi titlova** (Alati → Stilovi titlova): 24 gotova stila (3+ po svakoj od 8 tema), red-po-red/
  reč-po-reč/karaoke prikaz.
- **Analiza rasporeda videa** (Alati → Analiza rasporeda videa): OCR (Tesseract) pronalazi postojeći
  tekst/logo u kadru, da titl ne bi preklopio nešto što je već na slici.
- **Radni prostor - Timeline i plejer**: prave, testirane trake (video/audio/titl/tekst/slika-overlay)
  sa split/trim/move/duplicate/mute/volume/fade/lock/hide/solo/undo/redo po klipu, i transportna traka
  plejera (play/pause/stop/frame-step/seek/volume) - bez stvarnog dekodiranja/renderovanja kadra na
  ekranu (sandbox u kom je program razvijan nema ekran da se to proveri), ali svaka druga operacija je
  stvarna i testirana.
- **Izvoz videa** (dugme „Izvezi video“ u radnom prostoru): pravi ffmpeg render projekta u MP4/H.264 (ili
  NVENC/QSV/AMF sa automatskim padom nazad na H.264 ako hardverski enkoder ne uspe), sa pravim napretkom
  uživo, otkazivanjem koje stvarno prekida proces, i redom za više istovremenih izvoza.
- **Kreiraj video iz šablona** (početni ekran): novi projekat sa unapred dodatim trakama za uobičajene
  scenarije (govor sa titlovima, muzički spot, slike i tekst).
- **Brzi video od slike i pesme** / **Automatski video sa utisnutim titlovima** (početni ekran): od jedne
  slike i jedne pesme napravi gotov MP4 za nekoliko minuta, opciono sa automatski prepoznatim i
  utisnutim titlovima (lokalni Whisper).

Sve navedeno je pokriveno sa 306 automatizovanih testova (`dotnet test`) koji stvarno pokreću FFprobe/
FFmpeg/Tesseract, stvarno čuvaju i učitavaju projekte (uključujući srpsku latinicu, ćirilicu i putanje sa
razmacima), i pokreću headless UI testove koji podižu celu aplikaciju i proveravaju svaki ekran bez
grešaka. Četiri od njih preuzimaju i pokreću pravi Whisper model i rade samo tamo gde ima interneta do
huggingface.co (radi na CI-ju i na krajnjem korisničkom računaru; ne radi u ograničenom razvojnom
sandboxu bez tog pristupa - videćete jasno zbog čega u samom testu).

### Šta JOŠ NIJE implementirano (planirano za naredne faze)

Ovo je **jasno označeno u samom programu** (dugmad su vidljivo onemogućena, sa natpisom „Uskoro — u
razvoju"), ne prikazuje se kao gotovo:

- Upravljanje šablonima/fontovima/efektima — namerno uklonjeno umesto lažno prikazano kao gotovo (nema
  stvarne osnove za njih u kodu danas; videti `docs/PHASE_STATUS.md` Faza 10 za obrazloženje).
- Stvarno dekodiranje/renderovanje video kadra u plejeru (samo transportna traka i stanje su stvarni).
- Kompozicija više video traka istovremeno (slika-overlay traka postoji u modelu ali se još ne renderuje
  u izvozu), profili kanala, plugin sistem, thumbnail editor, muzički vizualizator.

Ako imate konkretnu funkciju koja vam odmah treba, javite je — lakše je ubaciti jednu jasno definisanu
funkciju u sledeću iteraciju nego čekati da čitav NLE bude gotov.

Ovaj README će se ažurirati posle svake faze da tačno odražava stvarno stanje programa. Za detaljan
istorijat po fazama pogledajte `RELEASE_NOTES.md` i `docs/PHASE_STATUS.md`.

## Sistemski zahtevi

- Windows 10 (verzija 1809 ili novija) ili Windows 11, 64-bit.
- .NET 8 Desktop Runtime (instalira se automatski uz program, ili preko `scripts\check-dependencies.ps1`).
- FFmpeg i FFprobe (isto - automatski, ili ručno ako korisnik ima svoje kopije).
- yt-dlp (opciono, samo za alat „Preuzmi sa YouTube-a" - automatski preko `check-dependencies.ps1`;
  ostatak programa radi normalno i bez njega).
- Minimum 4 GB RAM (preporučeno 8+ GB), minimum 2 GB slobodnog prostora na disku za sam program (video
  projekti zahtevaju dodatni prostor prema veličini snimaka).

## Instalacija

Installer se automatski pravi i testira na pravom Windows serveru pri svakoj izmeni koda (GitHub
Actions, `windows-latest`) — nije samo napisan i pretpostavljen da radi. Preuzmite ga sa stranice
Actions build-a (Artifacts → `NPVideoStudio-Setup`) ili ga napravite sami preko
`scripts\build-release.ps1` (vidi "Pravljenje instalacije" ispod).

1. Pokrenite `NPVideoStudio-Setup-X.X.X.exe`.
2. Pratite čarobnjak za instalaciju (može bez administratorskih prava — instalira se za trenutnog
   korisnika).
3. Izaberite da li želite prečicu na Radnoj površini i da li želite da se `.npvsproject` fajlovi
   povežu sa programom.
4. Po završetku, program se pokreće preko Start Menu prečice ili prečice na Desktopu.

### Deinstalacija

Windows → Podešavanja → Aplikacije → NP Video Studio → Deinstaliraj (ili preko Start Menu prečice
„Deinstaliraj NP Video Studio"). Deinstalacija **ne briše vaše projekte** (oni ostaju gde god ste ih
sačuvali) niti podešavanja/logove, osim ako to izričito potvrdite kada vas program pita.

### Portable verzija

`scripts\build-release.ps1` takođe pravi `NPVideoStudio-Portable-x64-<verzija>.zip` — raspakujte ga
bilo gde i pokrenite `NPVideoStudio.exe` direktno, bez instalacije. Unutra su i `VERSION.txt` i
`README-FIRST.txt`.

## Pokretanje iz izvornog koda (za razvoj / testiranje pre nego što installer bude gotov)

Potreban je [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) i FFmpeg/FFprobe na PATH-u.

```
git clone <repozitorijum>
cd <folder-repozitorijuma>
dotnet build
dotnet run --project src/NPVideoStudio.App
```

## Kako se koristi (trenutna funkcionalnost)

### Novi projekat

Na početnom ekranu kliknite „Novi projekat" (ručan izbor formata) ili neku od prečica („Kreiraj
YouTube video", „Kreiraj TikTok video" itd. — automatski popune preporučeni format). Unesite naziv,
potvrdite format/rezoluciju/frame rate i kliknite „Napravi projekat".

### Dodavanje videa, muzike i slika

U otvorenom projektu kliknite „Dodaj medije" i izaberite fajlove, ili ih jednostavno prevucite mišem u
prozor programa. Podržani su MP4, MOV, MKV, AVI, WEBM, M4V, MPEG (video); MP3, WAV, AAC, M4A, FLAC,
OGG, WMA (audio); JPG, PNG, WEBP, BMP, GIF, TIFF (slike). Svaki fajl se odmah analizira i prikazuje
trajanje, rezoluciju, fps i veličinu.

### Isečci iz pesme za Shorts najavu

Na početnom ekranu, sekcija „Alati" → „Isečci iz pesme". Izaberite audio fajl, po želji podesite
min/maks trajanje isečka (podrazumevano 30-50 sek) i broj isečaka (podrazumevano 3), kliknite
„Analiziraj pesmu". Program prikazuje predložene isečke sa vremenskim opsegom i prosečnom glasnoćom;
dugme „Izvezi sve" snima svaki kao poseban MP3 fajl u folder koji izaberete, a „Otvori" pokreće
izvezeni isečak u podrazumevanom plejeru da ga preslušate.

### Preuzimanje pesme sa YouTube-a

Na početnom ekranu, sekcija „Alati" → „Preuzmi sa YouTube-a". Nalepite link ka svom videu, kliknite
„Učitaj podatke" da vidite naslov/kanal/trajanje, potvrdite kvačicom da je sadržaj vaš, pa kliknite
„Preuzmi pesmu". Nakon preuzimanja, dugmad „Otvori u Isečci iz pesme" / „Otvori u Pronađi tekst u
pesmi" vode direktno u odgovarajući alat sa već učitanim fajlom.

### Generisanje titlova (SRT)

Na početnom ekranu, sekcija „Alati" → „Generiši titlove (SRT)". Izaberite audio ili video fajl,
kliknite „Generiši titlove", izaberite gde da se sačuva `.srt` fajl. Program ga lokalno transkribuje i
sačuva sa tačnim vremenima - spreman za otpremanje na YouTube/TikTok/Reels ili uvoz u drugi editor.

### Timeline, titlovi na slici, izvoz videa

U otvorenom projektu, sekcija „Timeline“ dodaje trake i klipove; „Uređivač titlova“ i „Stilovi titlova“
uređuju tekst/izgled titlova; dugme „Izvezi video“ pravi gotov MP4 fajl preko pravog ffmpeg render
pipeline-a. Video-efekti, maske, prelazi i profili kanala su i dalje planirani za naredne faze —
videćete ih jasno označene kao „u razvoju" na početnom ekranu, umesto da se lažno prikazuju kao gotove.

## Gde se šta nalazi

- **Projekti:** podrazumevano `Dokumenti\NP Video Studio\Projects` (može se promeniti u Podešavanjima).
- **Backup fajlovi projekta:** u podfolderu `Backups` pored samog `.npvsproject` fajla.
- **Auto-save (oporavak posle pada):** `%LocalAppData%\NP Video Studio\AutoSave`.
- **Logovi:** `%LocalAppData%\NP Video Studio\Logs`.
- **Podešavanja i lokalna baza:** `%LocalAppData%\NP Video Studio\settings.json` i
  `%LocalAppData%\NP Video Studio\npvideostudio.db`.

## Rešavanje čestih problema

Prvi korak je uvek **Dijagnostika sistema** u programu — proverava .NET, FFmpeg/FFprobe, foldere,
prostor na disku i bazu, i za većinu problema nudi dugme „Pokušaj automatsku popravku". Ako to ne
pomogne, kliknite „Napravi paket za podršku" i priložite dobijeni ZIP fajl kada tražite pomoć — sadrži
samo logove i informacije o sistemu, nikad vaše video/audio/projektne fajlove.

- **Program se ne pokreće:** pokrenite `scripts\check-dependencies.ps1` da proverite i instalirate
  .NET 8 Desktop Runtime i FFmpeg.
- **Fajl se ne uvozi / nema podataka o trajanju ili rezoluciji:** proverite da li je FFprobe
  instaliran (Dijagnostika sistema); fajl možda koristi kodek koji FFprobe na vašem sistemu ne
  prepoznaje.
- **Projekat se ne otvara:** proverite da fajl `.npvsproject` nije premešten/obrisan; ako je oštećen,
  potražite automatsku rezervnu kopiju u `Backups` folderu pored projekta.
- **Program se neočekivano zatvorio:** pri sledećem pokretanju program to prepoznaje i nudi nastavak
  od poslednje automatski sačuvane verzije.
- **„Preuzmi sa YouTube-a" javlja da yt-dlp nije pronađen:** pokrenite `scripts\check-dependencies.ps1`
  da ga instalira; ostatak programa radi normalno i bez ovog alata.

## Ažuriranje

Sistem ažuriranja (provera nove verzije, preuzimanje, migracija baze) je planiran za kasniju fazu.
Trenutno se nova verzija instalira ručno preko novog installer-a (koji čuva vaše projekte,
podešavanja i bazu — instalacija samo zamenjuje fajlove same aplikacije).

## Pravljenje instalacije (za programere)

Na Windows računaru sa .NET 8 SDK i (opciono) [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

Ovo pravi:
1. Self-contained publish u `publish\win-x64`, očišćen od PDB fajlova i native biblioteka za platforme
   koje Windows x64 verzija ne koristi (Linux/macOS/win-arm64/win-x86 Whisper runtime-ovi).
2. Portable folder `dist\NPVideoStudio-Portable-x64` i ZIP `dist\NPVideoStudio-Portable-x64-X.X.X.zip`.
3. Windows installer u `dist\NPVideoStudio-Setup-X.X.X.exe` (ako je Inno Setup instaliran).

Verzija (`X.X.X`) se čita iz `Directory.Build.props` u korenu repozitorijuma — to je jedino mesto gde
se menja pri podizanju verzije (installer-ov `MyAppVersion` u `installer\NPVideoStudio.iss` treba ručno
održavati usklađenim s njim).

Ovaj skript se stvarno pokreće na pravom Windows runner-u (`windows-latest`) u GitHub Actions pri svakoj
izmeni koda - build, ceo test paket i pravljenje instalatera/portable verzije su realno provereni, ne
samo napisani. Ostaje: ručna interaktivna provera instalacije/pokretanja/deinstalacije od strane pravog
korisnika na sopstvenom računaru (nešto što automatizovani CI ne pokriva) i regresivni test na
korisnikovom sopstvenom Shorts snimku (videti `test-data/README.md`).

## Arhitektura (za programere)

```
src/
  NPVideoStudio.App/             Avalonia MVVM desktop aplikacija (UI, DI kompozicija)
  NPVideoStudio.Domain/          Modeli: Project, Timeline, MediaAsset, ProjectFormat, RenderJob, AppSettings...
  NPVideoStudio.Core/            Interfejsi servisa (repository, settings, diagnostics, render...)
  NPVideoStudio.Infrastructure/  SQLite, JSON perzistencija, auto-save, logovanje (Serilog)
  NPVideoStudio.Media/           FFprobe/FFmpeg render pipeline, isečci iz pesme, YouTube preuzimanje (yt-dlp), OCR (Tesseract)
  NPVideoStudio.AI/              Lokalna Whisper transkripcija, timeline/caption edit sesije, player state machine
  NPVideoStudio.Diagnostics/     Sistemska dijagnostika i paket za podršku
tests/
  NPVideoStudio.UnitTests/       xUnit + Avalonia.Headless testovi (306)
docs/                            README i dokumentacija (MASTER_SPEC, PHASE_STATUS, FUNCTION_MATRIX...)
scripts/                         check-dependencies.ps1, build-release.ps1
installer/                       Inno Setup skripta (NPVideoStudio.iss)
THIRD_PARTY_NOTICES.md, Licenses/  Licence svih korišćenih open-source komponenti
```

**Zašto Avalonia umesto WPF:** specifikacija dozvoljava oba (WPF ili Avalonia). Avalonia je izabrana
jer se, za razliku od WPF-a, može build-ovati i testirati na Linux razvojnom okruženju koje je
korišćeno za ovu fazu — što znači da je svaki build i test u ovom repozitorijumu stvarno pokrenut i
proveren, a ne samo napisan. Krajnji rezultat je i dalje prava Windows desktop aplikacija.

Detaljan plan svih faza nalazi se u `docs/MASTER_SPEC.md`, a tačno stanje svake završene faze u
`docs/PHASE_STATUS.md`.
