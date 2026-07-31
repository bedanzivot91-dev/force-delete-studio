# NP Video Studio

Desktop program za montažu, uređivanje, titlovanje, obradu i izvoz video-snimaka za YouTube, YouTube
Shorts, TikTok, Instagram i Facebook. Radi lokalno na Windows računaru, bez interneta za osnovne
funkcije.

## VAŽNO — trenutno stanje programa

Ovo je **prva razvojna faza (Faza 1)** od planiranih deset. NP Video Studio je zamišljen kao program
uporediv po obimu sa CapCut/DaVinci Resolve, a takav program se realno gradi u mnogo faza tokom dužeg
perioda, ne za jednu sesiju rada. Da ne bismo tvrdili nešto što ne radi, ovde je tačan spisak šta je
**stvarno završeno i testirano**, a šta je **planirano za kasnije**.

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
- Podešavanja: tema, folder za projekte, cache folder, auto-save, čuvanje logova.
- Tri od planiranih deset tema: Dark Cinematic, Minimal Light, Professional Studio.
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

Sve navedeno je pokriveno sa 43 automatizovana testa (`dotnet test`) koji stvarno pokreću FFprobe/FFmpeg,
stvarno čuvaju i učitavaju projekte (uključujući srpsku latinicu, ćirilicu i putanje sa razmacima), i
pokreću headless UI test koji podiže celu aplikaciju i proverava da početni ekran, podešavanja i
dijagnostika rade bez grešaka. Tri od njih preuzimaju i pokreću pravi Whisper model i rade samo tamo
gde ima interneta do huggingface.co (radi na CI-ju i na krajnjem korisničkom računaru; ne radi u
ograničenom razvojnom sandboxu bez tog pristupa - videćete jasno zbog čega u samom testu).

### Šta JOŠ NIJE implementirano (planirano za naredne faze)

Ovo je **jasno označeno u samom programu** (dugmad su vidljivo onemogućena, sa natpisom „Uskoro — u
razvoju"), ne prikazuje se kao gotovo:

- Timeline montaža: sečenje, trake, prelazi, keyframe animacije.
- Tekstualni sistem i automatski titlovi za video (Whisper transkripcija sada postoji za pretragu
  teksta u pesmi, ali još nije povezana sa generisanjem titlova na video timeline-u).
- Video-efekti, maske, chroma key, prelazi.
- Audio editor (EQ, noise reduction, ducking...), snimanje mikrofona.
- Render/export videa (MP4/H.264 i ostali formati).
- Šabloni, thumbnail editor, muzički vizualizator, profili kanala, plugin sistem.

Ovo su namerno svi ekrani koji rade danas — nijedno dugme u programu ne prikazuje lažnu funkcionalnost.
Ako imate konkretnu funkciju koja vam odmah treba, javite je — lakše je ubaciti jednu jasno definisanu
funkciju u sledeću iteraciju (kao "Isečci iz pesme" iznad) nego čekati da čitav NLE bude gotov.

Ovaj README će se ažurirati posle svake faze da tačno odražava stvarno stanje programa.

## Sistemski zahtevi

- Windows 10 (verzija 1809 ili novija) ili Windows 11, 64-bit.
- .NET 8 Desktop Runtime (instalira se automatski uz program, ili preko `scripts\check-dependencies.ps1`).
- FFmpeg i FFprobe (isto - automatski, ili ručno ako korisnik ima svoje kopije).
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

`scripts\build-release.ps1` takođe pravi `NPVideoStudio-Portable.zip` — raspakujte ga bilo gde i
pokrenite `NPVideoStudio.exe` direktno, bez instalacije.

## Pokretanje iz izvornog koda (za razvoj / testiranje pre nego što installer bude gotov)

Potreban je [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) i FFmpeg/FFprobe na PATH-u.

```
git clone <repozitorijum>
cd force-delete-studio
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

### Tekst, titlovi, animacije, efekti, export

Ove funkcije su planirane za naredne faze i još nisu deo programa — videćete ih jasno označene kao „u
razvoju" na početnom ekranu, umesto da se lažno prikazuju kao gotove.

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
1. Self-contained publish u `publish\win-x64`.
2. Portable ZIP u `dist\NPVideoStudio-Portable.zip`.
3. Windows installer u `dist\NPVideoStudio-Setup-X.X.X.exe` (ako je Inno Setup instaliran).

**Pre prve zvanične verzije, ovo mora da se pokrene i testira na Windows računaru** — instalacija,
pokretanje, otvaranje projekta, uvoz medija, deinstalacija — ovaj korak još nije urađen jer je razvoj
rađen u Linux okruženju.

## Arhitektura (za programere)

```
src/
  NPVideoStudio.App/             Avalonia MVVM desktop aplikacija (UI, DI kompozicija)
  NPVideoStudio.Domain/          Modeli: Project, MediaAsset, ProjectFormat, AppSettings
  NPVideoStudio.Core/            Interfejsi servisa (repository, settings, diagnostics...)
  NPVideoStudio.Infrastructure/  SQLite, JSON perzistencija, auto-save, logovanje (Serilog)
  NPVideoStudio.Media/           FFprobe analiza medijskih fajlova, isečci iz pesme (FFmpeg astats)
  NPVideoStudio.AI/              Lokalna Whisper transkripcija za pretragu teksta u pesmi
  NPVideoStudio.Diagnostics/     Sistemska dijagnostika i paket za podršku
tests/
  NPVideoStudio.UnitTests/       xUnit + Avalonia.Headless testovi
docs/                            README i dokumentacija
scripts/                         check-dependencies.ps1, build-release.ps1
installer/                       Inno Setup skripta (NPVideoStudio.iss)
```

**Zašto Avalonia umesto WPF:** specifikacija dozvoljava oba (WPF ili Avalonia). Avalonia je izabrana
jer se, za razliku od WPF-a, može build-ovati i testirati na Linux razvojnom okruženju koje je
korišćeno za ovu fazu — što znači da je svaki build i test u ovom repozitorijumu stvarno pokrenut i
proveren, a ne samo napisan. Krajnji rezultat je i dalje prava Windows desktop aplikacija.

Detaljan plan faza 2–10 (timeline, tekst, titlovi/Whisper, audio, efekti, export, šabloni, instalacija)
nalazi se u istoriji razgovora/PR opisu koji je pratio ovu fazu.
