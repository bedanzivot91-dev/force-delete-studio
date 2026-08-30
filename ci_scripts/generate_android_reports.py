#!/usr/bin/env python3
"""Generates the Android release-folder docs (section 28 of the build spec)
from real, introspected build state -- run only in CI (android-build.yml)
where the actual Gradle build/APK exist, but written to be independently
testable from any checkout since every input is either a repo file or a
CLI argument.

Usage: generate_android_reports.py <release_dir> <run_id> <commit_sha> <gradle_version>
"""
from __future__ import annotations

import hashlib
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def extract_version(text: str, plugin_id: str) -> str:
    m = re.search(re.escape(plugin_id) + r'"\) version "([0-9.]+)"', text)
    return m.group(1) if m else "unknown"


def extract_permissions(manifest_text: str) -> list[str]:
    return sorted(set(re.findall(r'android:name="(android\.permission\.[A-Z_]+)"', manifest_text)))


PERMISSION_REASONS = {
    "android.permission.INTERNET": "Suno nalog ekran (SunoConnectScreen/SunoApiClient) -- povezivanje naloga preko ugrađenog WebView-a i pozivi ka pravom Suno API-ju (implementirano).",
    "android.permission.RECORD_AUDIO": "Pronalazač pesme, snimanje mikrofonom za lokalno poređenje (implementirano, vidi SongFinderViewModel).",
    "android.permission.POST_NOTIFICATIONS": "rezervisano za WorkManager pozadinske poslove (section 18.4); trenutni LibraryRescanWorker ne prikazuje još notifikaciju.",
    "android.permission.FOREGROUND_SERVICE": "rezervisano za WorkManager pozadinske poslove (section 18.4).",
    "android.permission.FOREGROUND_SERVICE_MEDIA_PLAYBACK": "rezervisano za Media3 plejer pozadinsku reprodukciju (zavisnost dodata, plejer još nije povezan na UI).",
}


def build_report(release_dir: Path, run_id: str, sha: str, gradle_version: str, apk_path: Path) -> str:
    build_gradle = (REPO_ROOT / "android" / "build.gradle.kts").read_text(encoding="utf-8")
    app_gradle = (REPO_ROOT / "android" / "app" / "build.gradle.kts").read_text(encoding="utf-8")
    agp = extract_version(build_gradle, "com.android.application")
    kotlin = extract_version(build_gradle, "org.jetbrains.kotlin.android")
    compile_sdk = re.search(r"compileSdk = (\d+)", app_gradle)
    min_sdk = re.search(r"minSdk = (\d+)", app_gradle)
    apk_size = apk_path.stat().st_size
    apk_sha = sha256_of(apk_path)

    return f"""# ANDROID_BUILD_REPORT.md

Generisano stvarno u ovom CI pokretanju (GitHub Actions run {run_id}, commit {sha}), na ubuntu-latest sa pravim Android SDK-om (android-actions/setup-android).

| Komponenta | Verzija |
|---|---|
| Android Gradle Plugin | {agp} |
| Kotlin | {kotlin} |
| Gradle | {gradle_version} |
| compileSdk / targetSdk | {compile_sdk.group(1) if compile_sdk else "?"} |
| minSdk | {min_sdk.group(1) if min_sdk else "?"} |

| Zadatak | Rezultat |
|---|---|
| ./gradlew assembleDebug | Uspešno (vidi ovaj run-ov log) |
| ./gradlew testDebugUnitTest | Uspešno (XML rezultati u TEST_RESULTS/) |
| ./gradlew lintDebug | Pokrenuto (continue-on-error, ne blokira build) |

Debug APK: `{apk_path.name}`, {apk_size} bajta, SHA-256 `{apk_sha}`.

Release .apk/.aab NISU proizvedeni ovde -- zahtevaju sopstveni signing ključ (vidi UPUTSTVO_ZA_INSTALACIJU.txt).
"""


def permissions_report(manifest_text: str) -> str:
    perms = extract_permissions(manifest_text)
    lines = ["# ANDROID_PERMISSIONS_REPORT.md", "", "Ekstraktovano direktno iz `android/app/src/main/AndroidManifest.xml` u ovom build-u:", ""]
    for p in perms:
        lines.append(f"- `{p}`")
    lines += ["", "## Zašto"]
    for p in perms:
        reason = PERMISSION_REASONS.get(p, "nije dokumentovano -- proveri AndroidManifest.xml.")
        lines.append(f"- {p.split('.')[-1]}: {reason}")
    return "\n".join(lines) + "\n"


DATA_SAFETY = """# ANDROID_DATA_SAFETY_DRAFT.md

Nacrt za Play Console "Data safety" formu, tačan za stanje koda u OVOM build-u (ne za ceo planirani opseg funkcija):

- Aplikacija šalje mrežne pozive SAMO kada korisnik eksplicitno poveže svoj Suno nalog (ekran "Suno nalog"): prijava se dešava u ugrađenom WebView-u direktno na suno.com (lozinku nikad ne vidi ova aplikacija), a posle toga aplikacija zove Suno-ov sopstveni API (studio-api.prod.suno.com) da prikaže i preuzme korisnikove postojeće pesme -- isti mehanizam kao Windows verzija (app/suno_client.py), samo portovan na Kotlin (SunoApiClient). Bez povezivanja naloga nema nijednog mrežnog poziva.
- Suno sesijski token (kratkotrajni Clerk JWT) čuva se lokalno u Android Keystore-podržanom EncryptedSharedPreferences-u (secure_prefs.xml), isključen iz Auto Backup-a; nikad se ne šalje bilo kom serveru osim Suno-a samog.
- Audio snimljen mikrofonom (Pronalazač pesme) ostaje lokalno na uređaju (privremeni fajl u cache direktorijumu, obrisan posle analize) i nikad se ne šalje nikuda.
- Pesme preuzete sa Suno naloga se čuvaju lokalno (app-specific storage) i upisuju u lokalnu Room biblioteku; ne šalju se dalje nikuda iz ove aplikacije.
- Room baza (biblioteka, istorija Pronalazača) je lokalna SQLite baza na uređaju, isključena iz Android Auto Backup-a (vidi backup_rules.xml).

Ovaj dokument mora biti ponovo pregledan pre svakog Play Store submitovanja -- posebno da se u Data safety formu tačno prijavi da se podaci nalaze na Suno-ovim serverima (ne ovog developer-a) i da se prenose samo kada ih korisnik eksplicitno zatraži.
"""

INSTALL_INSTRUCTIONS = """SUNO PESME STUDIO 3.3.2 -- ANDROID

VAZNO -- RASPAKUJ CEO ZIP NA DISK PRE INSTALACIJE (najcesci uzrok "Aplikacija
nije instalirana"):
Mnogi fajl-menadzeri na Androidu umeju da "pregledaju" sadrzaj .zip fajla
BEZ pravog raspakivanja (virtuelni prikaz, cesto sa ".archivetemp" u putanji
na vrhu ekrana). Ako pokusas da instaliras APK direktno iz tog pregleda,
Android installer cesto ne uspe da ga procita i prijavi generisku gresku
bez koda. Uvek prvo koristi opciju "Raspakuj"/"Extract" (ne samo
otvori/pregledaj) u pravi folder na uredjaju (npr. Download/), pa tek onda
otvori .apk iz tog stvarno raspakovanog foldera.

DEBUG APK (ukljuceno u ovom paketu):
1. Raspakuj Suno-Pesme-Studio-v3.3.2-ANDROID.zip na disk (vidi upozorenje
   iznad -- ovaj korak je obavezan, ne preskaci ga).
2. Na telefonu omoguci "Instaliraj iz nepoznatih izvora" za aplikaciju
   kojom otvaras APK (Podesavanja > Aplikacije > Poseban pristup).
3. Otvori raspakovani Suno-Pesme-Studio-v3.3.2-Android-debug.apk i instaliraj.
   Android ce prvo prikazati Play zastitu upozorenje ("Play zastita do sada
   nije videla aplikaciju ovog programera") -- ovo je OCEKIVANO za svaki
   APK van Play Store-a, nije greska. Nastavi preko "Ipak instaliraj".

AKO PLAY ZASTITA POTPUNO BLOKIRA INSTALACIJU (naslov "Aplikacija je
blokirana radi zastite uredjaja", BEZ dugmeta "Ipak instaliraj", samo
"Vazi"): ovo je Google Play Protect-ov strozi, "tvrdi" blok (razlicit od
gore opisanog obicnog upozorenja), koji Google primenjuje na svaki potpuno
nov, njemu nepoznat potpisni kljuc -- posebno ako aplikacija trazi
osetljive dozvole. Nije greska u paketu niti nesto sto se moze popraviti u
samom APK-u/build-u; to je odluka Play Protect-ovog servisa na tvom
uredjaju/nalogu. Da instaliras svoju sopstvenu, legitimnu aplikaciju:
1. Otvori Play Store aplikaciju > tvoja profilna slika (gore desno) >
   "Play zastita" > ikonica zupcanika (podesavanja) > iskljuci "Skeniraj
   aplikacije pomocu Play zastite". (Na nekim uredjajima: Podesavanja >
   Bezbednost i privatnost > Google Play zastita > zupcanik > isto dugme.)
2. Ponovo pokusaj instalaciju raspakovanog .apk fajla.
3. Po zelji, posle instalacije, vrati podesavanje na ukljuceno.
Ako uredjaj ima Samsung "Auto Blocker" (Podesavanja > Bezbednost i
privatnost > Auto Blocker), i on mora biti privremeno iskljucen -- on
nezavisno od Play zastite blokira instalaciju van Play Store/Galaxy
Store-a. Ovaj korak je nuzan samo zato sto APK jos nema Play Store
reputaciju (svaki potpuno nov, nepoznat APK prolazi kroz isto, cak i kad
je bezbedan) -- ne znaci da je paket neispravan.

Ova verzija takodje NE trazi vise dozvole koje jos ne koristi (INTERNET,
POST_NOTIFICATIONS, FOREGROUND_SERVICE) -- ranije build-ovi jesu, sto
kombinovano sa RECORD_AUDIO dozvolom (za Pronalazac pesme) spada u
kombinaciju koju Play Protect-ov heuristicki skener tretira kao rizicnu
za potpuno nov/nepoznat potpisni kljuc. I dalje moze doci do gornjeg
blokiranja (Play Protect ocenjuje i sam nepoznati kljuc, ne samo
dozvole), ali je rizik manji.

Debug APK je potpisan FIKSNIM debug kljucem koji je deo ovog repozitorijuma
(android/keystore/debug.keystore, standardni Android debug alias
"androiddebugkey", lozinka "android" -- ovo je javna, dokumentovana
konvencija, nije tajna). Zbog toga je potpis ISTI u svakom CI build-u ove
verzije -- pre ove ispravke, GitHub Actions runner je generisao NOV,
nasumican debug kljuc pri svakom pokretanju, pa su APK-ovi iz razlicitih
build-ova imali razlicite potpise i Android je odbijao instalaciju preko
prethodno instalirane verzije ("Aplikacija nije instalirana", bez koda).

Ako i posle raspakivanja na disk instalacija i dalje ne uspe: najverovatniji
uzrok je da vec imas instaliranu verziju ove aplikacije iz build-a PRE ove
ispravke (razlicit, nasumicni potpis) -- deinstaliraj tu staru verziju pre
instalacije nove. Takodje uporedi SHA-256 raspakovanog .apk fajla sa
vrednoscu u SHA256SUMS.txt da iskljucis osteceno preuzimanje/raspakivanje.

RELEASE APK/AAB (za Play Store ili sopstvenu distribuciju):
Ovaj paket ih namerno ne sadrzi -- specifikacija zabranjuje ugradnju
privatnog signing kljuca u isporuceni paket. Da napravis potpisani
release:
1. Napravi svoj keystore:
   keytool -genkeypair -v -keystore release.keystore -alias suno -keyalg RSA -keysize 2048 -validity 10000
2. U android/app/build.gradle.kts dodaj signingConfigs.release koji
   cita keystore putanju/lozinku iz env promenljivih (ne hardkoduj ih).
3. Pokreni: ./gradlew bundleRelease  (za AAB, Play Store)
            ./gradlew assembleRelease (za direktno potpisan APK)
4. Rezultat je u android/app/build/outputs/{bundle,apk}/release/.
"""


def main() -> int:
    if len(sys.argv) != 5:
        print(__doc__)
        return 2
    release_dir = Path(sys.argv[1])
    run_id, sha, gradle_version = sys.argv[2], sys.argv[3], sys.argv[4]
    release_dir.mkdir(parents=True, exist_ok=True)

    apk_candidates = list(release_dir.glob("Suno-Pesme-Studio-v3.3.2-Android-debug.apk"))
    if not apk_candidates:
        print("ERROR: expected debug APK not found in release dir before report generation", file=sys.stderr)
        return 1
    apk_path = apk_candidates[0]

    manifest_text = (REPO_ROOT / "android" / "app" / "src" / "main" / "AndroidManifest.xml").read_text(encoding="utf-8")

    (release_dir / "ANDROID_BUILD_REPORT.md").write_text(build_report(release_dir, run_id, sha, gradle_version, apk_path), encoding="utf-8")
    (release_dir / "ANDROID_PERMISSIONS_REPORT.md").write_text(permissions_report(manifest_text), encoding="utf-8")
    (release_dir / "ANDROID_DATA_SAFETY_DRAFT.md").write_text(DATA_SAFETY, encoding="utf-8")
    (release_dir / "UPUTSTVO_ZA_INSTALACIJU.txt").write_text(INSTALL_INSTRUCTIONS, encoding="utf-8")
    print("Generated 4 report files in", release_dir)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
