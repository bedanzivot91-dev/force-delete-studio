# Napomene uz izdanje — NP Video Studio v0.1.0

Ovo je sažetak šta je stvarno isporučeno kroz Faze 0–10, pisan za korisnika, ne za programera — za pun
tehnički detalj svake faze (šta je testirano, koji su stvarni poznati nedostaci, tačan commit) pogledajte
`docs/PHASE_STATUS.md`. Nijedna stavka ovde nije samo napisana i pretpostavljena da radi — sve je pokriveno
automatizovanim testovima koji stvarno pokreću ffmpeg/ffprobe/yt-dlp/Tesseract/Whisper, i sve je potvrđeno
na pravom Windows CI runner-u (`windows-latest`), ne samo u razvojnom Linux okruženju.

## CapCut raspored i kontrolisana ažuriranja AI alata

- Radna površina sada ima media panel levo, veliki plejer u sredini, inspektor desno i širok timeline
  dole; paneli imaju hvataljke za promenu veličine.
- Izabrani tekst/video se uređuje u desnom inspektoru, bez otvaranja velikih kartica u timeline-u.
- `lyric-align` je obavezan i posebno proveravan deo AI paketa, uz faster-whisper i Demucs.
- Dodati su ručno instaliranje/ažuriranje svih AI alata i režimi Samo obavesti, Automatski i Ručno sa
  podesivim intervalom. Automatski režim je dobrovoljan, nije podrazumevano uključen.

## Faza 0 — Revizija

Početna, poštena revizija postojećeg koda pre bilo kakve nove funkcionalnosti - utvrđeno tačno stanje
(70/70 testova, funkcionalna matrica), bez izmena ponašanja programa.

## Faza 1 — Build, zavisnosti, priprema za izdanje

Ispravljena neusklađenost verzije (sada `0.1.0` svuda), novi ekran „Alati i modeli“ (stvarna provera
FFmpeg/FFprobe/yt-dlp/Whisper), otklonjen ZIP-u-ZIP-u problem portable verzije, uklonjeni suvišni PDB i
strani (Linux/macOS) fajlovi iz izlaznog paketa.

## Faza 2 — Ojačavanje postojećih funkcija

Realni testovi protiv pravog mock yt-dlp procesa i drugih ojačanja postojećih ekrana, bez novih
korisnički vidljivih funkcija.

## Faza 3 — Pet novih tema

Obsidian Neon, Arctic Glass, Crimson Cyber, Midnight Pro, Ocean Glass - uz postojeće 3 (Dark Cinematic,
Minimal Light, Professional Studio), ukupno svih 8 planiranih tema, promena bez restarta programa.

## Faza 4 — Biblioteka pesama i prepoznavanje otiska

Novi ekran „Moje pesme“: lokalna biblioteka sa pravim Chromaprint/fpcalc otiskom numere, provera
duplikata pre dodavanja uz potvrdu korisnika.

## Faza 5 — AI pipeline (opcioni lokalni worker)

Python `ai_worker.py` sa iskrenom proverom mogućnosti (faster-whisper/WhisperX/Demucs) - „Fast“ profil
(Whisper.net) radi uvek bez ičega dodatnog; teži profili su opcioni i jasno prijavljuju kada nisu
instalirani, umesto lažnog rezultata.

## Faza 6 — Model reči/titlova + uređivač

Novi „Uređivač titlova“: uređivanje na nivou svake reči, undo/redo, uvoz/izvoz u SRT/VTT/ASS/TXT/JSON/LRC.

## Faza 7 — Stilizacija titlova + analiza rasporeda videa (OCR)

24 gotova stila titlova (3+ po temi), i novi ekran „Analiza rasporeda videa“ - pravi Tesseract OCR
pronalazi postojeći tekst/logo u kadru radi izbegavanja preklapanja sa titlom.

## Faza 8 — Timeline i plejer

Prava, nedestruktivna vremenska traka (video/audio/titl/tekst/slika-overlay trake) sa punim setom
operacija (seci/pomeri/dupliraj/utišaj/fade/zaključaj/sakrij/solo/undo/redo), i transportna traka
plejera (play/pause/stop/frame-step/seek/glasnoća) - bez stvarnog renderovanja kadra na ekranu (razvojni
sandbox nema ekran da se to proveri), sve ostalo je stvarno i testirano.

## Faza 9 — Render pipeline (izvoz videa)

Dugme „Izvezi video“: pravi ffmpeg izvoz projekta sa vremenske trake u gotov MP4 fajl - izbor kodeka
(H.264 softverski ili NVENC/QSV/AMF sa automatskim padom nazad na softverski), napredak uživo,
otkazivanje koje stvarno prekida proces, i red za više istovremenih izvoza. Verifikovano end-to-end
pravim ffmpeg-om i OCR-om (tačni tajminzi klipova, praznina i titlova u renderovanom fajlu).

## Faza 10 — Šabloni i brzi video, uklonjene lažne pločice

„Kreiraj video iz šablona“ (unapred dodate trake za uobičajene scenarije), „Brzi video od slike i
pesme“/„Automatski video sa utisnutim titlovima“ (jedna slika + jedna pesma = gotov MP4 za par minuta,
opciono sa automatski prepoznatim titlovima preko Whisper-a). Tri preostale planirane pločice
(Upravljanje šablonima/fontovima/efektima) su uklonjene iz aktivnog interfejsa umesto lažno prikazane
kao gotove, pošto nijedna nema stvarnu osnovu drugde u kodu.

## Poznata ograničenja koja ostaju (nisu skrivena)

- Nema stvarnog dekodiranja/renderovanja video kadra u plejeru unutar programa (samo transportna traka
  i stanje su stvarni) - razvojni sandbox nema ekran da se to proveri.
- Nema kompozicije više video traka istovremeno u izvozu (slika-overlay traka postoji u modelu, ali se
  još ne renderuje).
- Nema upravljanja fontovima ni video-efektima (videti Fazu 10 gore).
- Nema bundle-ovanog FFmpeg/yt-dlp/Chromaprint/Tesseract - korisnik ih instalira posebno (ili preko
  `scripts\check-dependencies.ps1`); videti `THIRD_PARTY_NOTICES.md` za razloge (GPL/LGPL licence tih
  alata bi inače prešle na sam instalater da se distribuiraju zajedno s njim).
- Ručna interaktivna provera instalacije/pokretanja/deinstalacije na pravom Windows računaru od strane
  krajnjeg korisnika, i regresivni test na korisnikovom sopstvenom Shorts snimku, još nisu urađeni u
  ovoj sesiji (videti `test-data/README.md`).

## Licence

Videti `THIRD_PARTY_NOTICES.md` i `Licenses/` za spisak svih korišćenih open-source komponenti sa
stvarno provere licencama. Nijedna AGPL komponenta nije korišćena.
