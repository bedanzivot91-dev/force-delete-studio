# GAP_ANALYSIS.md — Suno Pesme Studio 3.3.2

Zasnovano na stvarnoj analizi dostavljenog paketa
`SunoPesmeStudiov3.3.2ORIGINALPOPRAVLJENINSTALLER.zip`, izvršenoj u ovoj
sesiji (python3.13, `py_compile`, AST parsiranje, pokretanje `tests/*.py`,
provera mrežnog pristupa i alata dostupnih u ovom build okruženju).

**Obim ovog prolaza je namerno ograničen na "tanku vertikalnu isečku"** — to
je odluka koju je korisnik doneo eksplicitno (opcija "Tanka vertikalna
isečka kroz sve odjednom" u pojašnjavajućem pitanju pre početka rada), zbog
toga što je puna specifikacija realno više nedelja profesionalnog rada.
Ovaj dokument zato ne tvrdi da je urađen kompletan katalog svakog dugmeta i
dijaloga (to bi zahtevalo ručni UI obilazak koji ova sesija — bez
Windows/Android uređaja — ne može stvarno da izvrši); umesto toga navodi
stvarno proverene činjenice i jasno obeležava šta ostaje.

## 1. Šta je stvarno zatečeno u paketu

- `app/*.py` (16 modula, ukupno ~13.000 linija) i `plugins/*.py` — realan,
  netrivijalan Python backend sa sirovim `http.server` (nema Flask/FastAPI).
  159 `/api/*` ruta pronađeno regex-om nad `server.py` (vidi
  `docs/ORIGINAL_API_INVENTORY.json`).
- `app/web/{index.html,app.js,style.css}` — originalni frontend, 402+1022+170
  linija.
- SQLite šema sa 21 tabelom (`docs/ORIGINAL_DATABASE_SCHEMA.sql`), uključujući
  `songs`, `youtube_channels/videos/matches`, `recognized_tracks`,
  `audio_fingerprints`, `job_queue`, `song_history`, `backup_items`,
  `subtitle_cues`, `text_comparisons` — dakle biblioteka, YouTube praćenje,
  Pronalazač pesme i audio fingerprint sistem već imaju realnu šemu, ne samo
  UI mockup.
- `windows_build/{launcher,setup}` — Go izvor koji se STVARNO kompajlira u
  PE32+ Windows x64 GUI izvršne fajlove (prethodni tim je to uradio; ja sam
  ponovo proverio cross-compile u ovoj sesiji, vidi
  `windows_build/BUILD_VERIFICATION.md`).
- `tests/*.py` — 8 test fajlova. Pokrenuto stvarno u ovoj sesiji sa
  python3.13:
  - `critical_v300.py` — PASS (9/9)
  - `installer_logic_test.py` — PASS (16 provera)
  - `original_plus_test.py` — PASS
  - `regression_v300.py` — PASS
  - `watchdog_test.py` — PASS (1/1)
  - `e2e_test.py` — FAIL: Playwright nije instaliran u ovom okruženju
    (očekivano — opcioni E2E test, ne blokira jezgro)
  - `v3_features_test.py` — FAIL: `library_integrity_scan` assertion
    (`checked_files>=1 and not missing`) — u ovom Linux sandboxu nema
    stvarnih audio fajlova/ffmpeg da se test-biblioteka pravilno napuni;
    **nije potvrđeno da li je ovo pravi bug ili samo posledica sandboxa —
    zahteva ponovno pokretanje na Windows CI runneru sa ffmpeg-om.**
  - `http_integration_v300.py` — FAIL: `HTTP 500` na `/api/audio/info`
    jer `ffprobe` fizički ne postoji u ovom Linux okruženju (potvrđeno:
    `which ffmpeg ffprobe yt-dlp deno` ne vraća ništa). Očekivano da prođe
    na Windows CI runneru gde `.github/workflows/windows-build.yml` postavlja
    stvarni FFmpeg.
  - Stvaran log svake komande: `docs/LOCAL_SMOKE_TEST_REPORT.md`.
- Nov `tests/import_smoke_test.py` (dodat u ovoj sesiji) — uvozi svih 18
  modula pojedinačno pod python3.13: **18/18 OK**
  (`docs/PYTHON_IMPORT_REPORT.json`).

## 2. Šta NIJE bilo ispravno u prethodnom paketu (potvrđeno)

- `Program/python/`, `Program/tools/ffmpeg|yt-dlp|deno/` sadrže samo
  `README.txt` fajlove — nema fizičkih binarnih fajlova. Sopstveni
  `VAZNO-PROČITAJ.txt` prethodnog paketa to i priznaje: "Ovaj ZIP NE SADRŽI
  fizički Python, FFmpeg, yt-dlp i Deno... Zato ovo nije potpuno offline
  paket." Ovo je tačno ono što korisnička specifikacija zabranjuje (tačka 7).
- `ALATI_MANIFEST.json` navodi `"program_version": "3.3.1"` iako je paket
  imenovan 3.3.2 — verzija u manifestu nije bila usklađena.
- Testovi rade samo `python compileall` + Go cross-compile proveru; nijedan
  test u prethodnom `IZVEŠTAJ-STVARNE-PROVERE.txt` nije stvarno pokrenuo
  GUI installer na pravom Windows računaru — dokument to i sam priznaje.

## 3. Zašto potpuno fizičko offline pakovanje NIJE moguće iz OVE build sesije

Provereno direktno u ovom kontejneru:
- Izlazni mrežni pristup je ograničen na dozvoljenu listu domena
  (pypi.org, registry.npmjs.org, git, anthropic.com, golang proxy). Zahtevi
  ka `python.org`, `github.com` (release download), `www.gyan.dev` (FFmpeg),
  `dl.google.com` (Android/Gradle Maven) vraćaju `403`/`000`.
- Nema Windows OS-a, nema Android SDK-a, nema `dotnet` SDK-a, nema Wine u
  ovom okruženju.
- Zbog toga sesija ne može fizički da preuzme i ugradi Python
  embeddable/FFmpeg/yt-dlp/Deno/WebView2 Fixed Runtime/AI modele u ZIP koji
  bi tvrdio da je "potpuno offline" — to bi bilo upravo lažno predstavljanje
  koje korisnik izričito zabranjuje.
- **Rešenje sprovedeno u ovoj sesiji:** `.github/workflows/windows-build.yml`
  radi na GitHub-hostovanom `windows-latest` runneru koji IMA pun internet
  pristup i pravi Windows OS. Taj CI posao stvarno preuzima komponente,
  proverava SHA-256, pokreće `--version` self-test i pravi finalni ZIP —
  rezultat je pravi, ne simuliran. Link na CI run se dostavlja korisniku po
  završetku.

## 4. Šta ostaje za kompletnu usklađenost sa specifikacijom (nedovršeno)

Jasno i bez uvijanja — ovo NIJE urađeno u ovom prolazu:

1. **4 od 5 tema** — u ovom prolazu je urađena samo Originalna tema
   (nepromenjena) + Neon District (potpuno nova, kompletna). Urban Concrete,
   Midnight Studio i Aurora Glass nisu implementirane.
2. **Android aplikacija** — urađen je samo tanak Kotlin/Compose skelet
   (Početna + Biblioteka ekran, Room baza, 2 teme). Pronalazač pesme,
   YouTube pregled, sinhronizacija sa Windows aplikacijom, WorkManager
   pozadinski poslovi, SAF uvoz fajlova — nisu implementirani.
3. **AI runtime (transkripcija, stem separation)** — `plugins/*_worker.py`
   postoje i uvoze se čisto, ali stvarni ONNX/ctranslate2 modeli NISU
   preuzeti niti ugrađeni (mreža ovde to ne dozvoljava); ostaju kao
   `pip install`-abilni opcioni moduli dok se ne pokrenu na CI/Windows sa
   punim pristupom.
4. **YouTube OAuth i Suno nalog** — kod postoji i uvozi se čisto, ali
   funkcionalno JE NEOPHODAN korisnički OAuth client JSON / Suno nalog da bi
   se stvarno testiralo krajnje-do-kraja (po dogovoru sa korisnikom, ovo
   ostaje "donesi svoje").
5. **Windows GUI installer koraci (Dobrodošli/Licenca/...)** — Go izvor u
   `windows_build/setup/main.go` postoji od pre i cross-compajlira se
   ispravno, ali GUI koraci nisu ponovo dizajnirani niti ručno klikani na
   pravom Windows-u u ovoj sesiji — to se dešava na Windows CI runneru.
6. **Code signing, WebView2 Fixed Version Runtime fizičko pakovanje, AI
   model manifest sa rollback-om, LAN/QR sinhronizacija Windows↔Android,
   Google Play AAB submission** — nisu urađeni u ovom prolazu.
7. **Potpuna funkcija-po-funkcija tabela (svako dugme/dijalog)** —
   `docs/ORIGINAL_FUNCTION_INVENTORY.json` je generisan AST parserom (stvarne
   klase/funkcije po modulu), ali ručni UI-katalog svakog dugmeta u
   `app/web/index.html` nije urađen red-po-red u ovom prolazu.

Ovo su tačke za sledeću fazu, ne tvrdnje da su "skoro gotove".
