# FUNCTION_PARITY_REPORT.md

Poređenje `ORIGINAL_*` (snimljeno odmah posle uvoza 3.3.2 baseline-a, pre
ijedne izmene) i `FINAL_*` (regenerisano u ovoj sesiji, posle svih izmena)
inventar dokumenata u `docs/`. Svi `FINAL_*` fajlovi su regenerisani istim
metodom kao `ORIGINAL_*` (AST parsiranje, regex nad string literalima,
stvarna instancijacija `LibraryDB` za šemu) — nisu ručno prepisani.

## Python backend (`app/`, `plugins/`)

| Provera | Rezultat |
|---|---|
| `docs/ORIGINAL_FUNCTION_INVENTORY.json` vs `docs/FINAL_FUNCTION_INVENTORY.json` | **Identični** (`diff` bez razlike) — nijedna Python funkcija/klasa/metoda nije uklonjena, izmenjena niti dodata ovaj put |
| `/api/*` rute (`ORIGINAL_API_INVENTORY.json` vs `FINAL_API_INVENTORY.json`) | **159 = 159**, ista lista |
| SQLite šema (`ORIGINAL_DATABASE_SCHEMA.sql` vs `FINAL_DATABASE_SCHEMA.sql`, dobijena stvarnim instanciranjem `LibraryDB()`) | **38 = 38** tabela/indeksa, identična imena i kolone |

Zaključak: **nijedna postojeća backend funkcija nije obrisana ni oštećena**
u ovoj sesiji. Sve izmene ovog prolaza su bile van `app/`/`plugins/`:
frontend (teme, pristupačnost), Windows Go shell, Android aplikacija, CI,
dokumentacija.

## Frontend (`app/web/`)

Nije generisan automatski inventar (zahtevao bi HTML/JS parser koji ovaj
projekat nema), ali promene su poznate direktno iz git istorije ove sesije:

- **Dodato**: `body[data-theme="urban-concrete"|"midnight-studio"|"aurora-glass"]`
  CSS blokovi (3 nove kompletne teme), pristupačnost/skaliranje sekcija
  (`#accessibilityPanel`), `THEMES` niz proširen sa 3 nova unosa u `app.js`.
- **Nije obrisano ništa** — svaka postojeća CSS klasa, ID i JS funkcija iz
  originalnog `style.css`/`app.js`/`index.html` ostaje netaknuta (potvrđeno
  `git diff` pregledom pri svakoj izmeni ove sesije — izmene su isključivo
  dodavanja/append, nijedan `git diff` u ovoj sesiji nije uklonio postojeći
  blok iz ova tri fajla).

## Windows shell (`windows_build/`)

- **Popravljeno, ne obrisano**: `windows_build` prethodno nije imao
  `go.mod` (nije se ni kompajlirao), i `progress.go`/`uninstaller.go` su
  imali sukobljene `func main()` u istom paketu (takođe se nije
  kompajlirao). Sve postojeće funkcije iz tih fajlova su zadržane, samo
  premeštene u sopstvene pakete (`progress/`, `uninstaller/`).
- **Dodato**: WebView2 native prozor (zamenjuje browser-tab pristup),
  single-instance zaštita, `--stage-components` CLI mod, Dobrodošli/Licenca
  koraci, `/api/shutdown` graceful-shutdown poziv pre nadogradnje.
- Realna provera: sve 4 `.exe` mete se i dalje kompajliraju čisto
  (`go vet` bez upozorenja) posle svake izmene ove sesije.

## Android (`android/`)

Nova aplikacija ovog prolaza — nema "original" verziju za poređenje.
Trenutni obim: 5 tema, biblioteka (Room), lokalni Pronalazač pesme,
praćenje foldera + WorkManager rescan, podešavanja. Detaljna
funkcija-po-funkcija matrica u `docs/ANDROID_FEATURE_PARITY_MATRIX.md`.

## Šta ovaj izveštaj NE tvrdi

Ovo je poređenje na nivou **postojanja** funkcija/ruta/tabela (da li je
nešto obrisano), ne dokaz da svaka funkcija **radi ispravno** u svakom
scenariju — to pokrivaju `LOCAL_SMOKE_TEST_REPORT.md` (šta je stvarno
pokrenuto ovde) i stvarni CI run-ovi na `windows-latest`/`ubuntu-latest`
(šta je stvarno pokrenuto tamo, sa linkovima u finalnom izveštaju
korisniku).
