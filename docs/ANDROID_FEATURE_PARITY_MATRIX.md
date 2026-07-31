# ANDROID_FEATURE_PARITY_MATRIX.md

Stanje posle ovog prolaza (tanka vertikalna isečka, dogovoreno sa
korisnikom pre početka rada). `android/` je pravi Gradle/Kotlin/Compose
projekat (ne desktop WebView utisnut u telefon) sa sopstvenim navigacionim,
dodirnim i Material3-zasnovanim, ali prilagođenim dizajnom.

| Windows funkcija | Android status | Napomena |
|---|---|---|
| Biblioteka (pregled pesama) | **Delimično podržano** | `LibraryScreen` + Room `SongEntity`/`SongDao` postoje i renderuju/pretražuju stvarne redove; uvoz fajlova (SAF), foldere, tagove, kolekcije nije urađeno u ovom prolazu |
| Petica tema | **Delimično podržano (2/5)** | `AppTheme.ORIGINAL` i `AppTheme.NEON_DISTRICT` implementirane kao zaseban Compose `ColorScheme`+`Shapes` (uglovi/oblici se strukturno razlikuju, ne samo boje); ostale 3 nisu portovane |
| Odvojen izbor teme Windows/Android | **Podržano** | `ThemePreference` (DataStore) čuva izbor lokalno po uređaju, nezavisno od desktop podešavanja |
| Pronalazač pesme | **Nije urađeno u ovom prolazu** | Zahteva mikrofon snimanje + audio fingerprint poređenje; RECORD_AUDIO dozvola je već deklarisana u manifestu za sledeću fazu |
| YouTube pregled/kanali | **Nije urađeno u ovom prolazu** | |
| Windows↔Android sinhronizacija | **Nije urađeno u ovom prolazu** | |
| WorkManager pozadinski poslovi | **Nije urađeno u ovom prolazu** | Zavisnost je dodata u `build.gradle.kts` (`androidx.work:work-runtime-ktx`), nijedan `Worker` još nije napisan |
| Backup/restore | **Nije urađeno u ovom prolazu** | |
| Offline rad za lokalne funkcije | **Podržano za biblioteku** | Room baza je lokalna po dizajnu; nema mrežnog poziva na putanji za pregled/pretragu/omiljene |
| Bezbedno čuvanje tokena (Android Keystore) | **Infrastruktura dodata, nije povezana na ekran** | `androidx.security:security-crypto` je zavisnost; nijedan ekran još ne čuva token (nema još YouTube OAuth/Pronalazač ekrana koji bi ga koristili) |
| Media3 plejer | **Zavisnost dodata, nije povezana na UI** | `androidx.media3` u `build.gradle.kts`; nijedan `ExoPlayer` još nije instanciran |

## Zašto nije više urađeno u ovom prolazu

Korisnik je eksplicitno izabrao "tanka vertikalna isečka kroz sve odjednom"
kao pristup fazama pre početka rada — cilj ovog prolaza je dokazati da
Android deo postoji kao pravi, samostalan Kotlin/Compose projekat sa
stvarnom lokalnom bibliotekom i sopstvenim dizajnom, ne katalogizovati
svaku preostalu funkciju kao "urađeno".

## Šta NIJE i ne može biti provereno u ovoj build sesiji

- Da li se Gradle projekat stvarno sinhronizuje i gradi: `google()`
  Maven repozitorijum (jedini izvor za `com.android.application` plugin i
  gotovo sve `androidx.*`/Compose zavisnosti) vraća `403` sa mrežne
  politike ove sandbox sesije — potvrđeno direktno (`gradle wrapper` je
  pokušao i eksplicitno prijavio "could not resolve plugin artifact
  com.android.application... Searched in: Google, MavenRepo, Gradle
  Central Plugin Repository").
- Da li se APK/AAB stvarno instalira i radi na uređaju/emulatoru — nema
  Android SDK-a ni emulatora u ovoj sesiji.
- `gradle-wrapper.jar` i `gradlew`/`gradlew.bat` JESU stvarno generisani u
  ovoj sesiji (`gradle wrapper --gradle-version 8.14.3`, BUILD SUCCESSFUL) —
  to je proverljivo, sam Gradle build projekta nije.

`.github/workflows/android-build.yml` radi na GitHub-hostovanom Ubuntu
runneru sa punim pristupom `dl.google.com`/Play Maven repozitorijumu i
stvarnim Android SDK-om (`android-actions/setup-android`) — tamo se prvi
put stvarno vidi da li se projekat gradi. Rezultat tog CI run-a je pravi
`ANDROID_TEST_REPORT.md` dokaz, ne ovaj dokument.
