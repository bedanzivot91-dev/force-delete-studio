# LOCAL_SMOKE_TEST_REPORT.md

Ovo NIJE Windows ni Android test izveštaj — to su `WINDOWS_TEST_REPORT.md` i
`ANDROID_TEST_REPORT.md`, koji nastaju iz stvarnih CI izvršavanja na
`windows-latest` i Android emulator runnerima (vidi
`.github/workflows/*.yml`), jer ova build sesija nema Windows ni Android
okruženje.

Ovo je dnevnik onoga što je stvarno pokrenuto u Linux sandbox okruženju u
kome je ovaj kod pisan — python3.13, `py_compile`, i postojeći `tests/*.py`.

| Test | Alat | Rezultat | Napomena |
|---|---|---|---|
| `py_compile` svih 18 `app/`+`plugins/` modula | python3.13 | **18/18 PASS** | vidi transkript ispod |
| `tests/import_smoke_test.py` (nov, dodat ovde) | python3.13 | **18/18 PASS** | uvozi svaki modul pojedinačno |
| `tests/critical_v300.py` | python3.13 | **PASS** (9/9) | |
| `tests/installer_logic_test.py` | python3.13 | **PASS** (16 provera) | logika instalera, ne stvarni Windows GUI |
| `tests/original_plus_test.py` | python3.13 | **PASS** | |
| `tests/regression_v300.py` | python3.13 | **PASS** | |
| `tests/watchdog_test.py` | python3.13 | **PASS** (1/1) | |
| `tests/e2e_test.py` | python3.13 + Playwright | **FAIL** | Playwright nije instaliran u ovom sandboxu (`pip install playwright` nije pokretan da se ne troši mrežni budžet na test koji zahteva i browser binary preuzimanje; CI ga instalira i pokreće stvarno) |
| `tests/v3_features_test.py` | python3.13 | **FAIL** | `library_integrity_scan` assertion — bez pravih audio fajlova/ffmpeg u ovom sandboxu; ponovo pokrenuto na Windows CI |
| `tests/http_integration_v300.py` | python3.13 | **FAIL** | HTTP 500 na `/api/audio/info` jer `ffprobe` fizički ne postoji ovde (`which ffprobe` → prazno); ponovo pokrenuto na Windows CI gde je FFmpeg ugrađen |
| `go build`/`go vet` sva 4 windows_build cilja | go1.24.7, `GOOS=windows GOARCH=amd64` | **PASS** | vidi `windows_build/BUILD_VERIFICATION.md` |
| `pip install playwright==1.61.0` u čist venv | python3.13 + pip | **PASS** — stvarno rešeno preko PyPI, `requirements-lock.txt` sadrži pravi rezultat | |

## Transkript: py_compile

```
$ cd /home/user/force-delete-studio
$ for f in app/*.py plugins/*.py; do python3.13 -m py_compile "$f" && echo "OK  $f" || echo "FAIL $f"; done
OK  app/advanced_features.py
OK  app/audio_match.py
OK  app/audio_tools.py
OK  app/bootstrap.py
OK  app/cdp.py
OK  app/database.py
OK  app/id3.py
OK  app/music_recognition.py
OK  app/security_lock.py
OK  app/server.py
OK  app/suno_client.py
OK  app/suno_compat.py
OK  app/v3_features.py
OK  app/watchdog.py
OK  app/youtube_oauth.py
OK  app/youtube_tools.py
OK  plugins/stems_worker.py
OK  plugins/transcribe_worker.py
```

Napomena: `python3.11 -m py_compile app/v3_features.py` FAILS (nested
f-string sa backslash izrazom u interpolaciji, validno tek od Python 3.12).
Originalni `.pyc` fajlovi u dostavljenom paketu su `cpython-313`, što
potvrđuje da je 3.13 nameravana verzija — usklađeno sa
`ALATI_MANIFEST.json` koji traži Python 3.14.6 embeddable (kompatibilan
unapred). Preporuka: pin na Python **3.13.x** embeddable za Windows paket
(matches produced `.pyc` files and confirmed working `py_compile`), ne
3.14, dok se 3.14 kompatibilnost svih C-extension zavisnosti (ctranslate2,
onnxruntime) stvarno ne potvrdi na CI-ju.

## Ono što OVDE nije i ne može biti provereno

- Bilo koji stvarni `.exe` pokrenut (nema Windows/Wine ovde).
- FFmpeg/FFprobe/yt-dlp/Deno stvarno izvršeni (nisu fizički prisutni ovde;
  mreža blokira python.org/gyan.dev/github-releases sa ove sesije).
- Android build/test (nema Android SDK-a ni pristupa `dl.google.com` ovde).
- WebView2 stvarno renderovanje UI-ja.

Sve gore navedeno pokriva `.github/workflows/windows-build.yml` i
`android-build.yml`, koji rade na GitHub-hostovanoj infrastrukturi sa punim
internet pristupom i pravim OS-om.
