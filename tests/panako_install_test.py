from __future__ import annotations
import json, os, shutil, subprocess, sys, tempfile, zipfile
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'app'))
import v3_features


def _build_test_jar(build_dir: Path, name: str, with_main_class: bool) -> Path:
    """A real, runnable (or deliberately not-runnable) .jar built with the
    real javac/jar tools -- not a fake byte string -- so install_panako_jar()
    is exercised against genuine JVM behavior (including the exact "no main
    manifest attribute" error a real broken jar produces)."""
    source = build_dir / "Main.java"
    source.write_text(
        "public class Main { public static void main(String[] args) { "
        "System.out.println(\"panako-test-ok\"); } }",
        encoding="utf-8",
    )
    subprocess.run(["javac", str(source)], cwd=build_dir, check=True, capture_output=True)
    # Random padding: a real Panako jar has real weight (deps, class files).
    # install_panako_jar() rejects anything under 200KB as "too small to be
    # real" -- all-zero padding compresses away to nothing under jar's
    # DEFLATE, so this has to be incompressible random data to be a
    # meaningful test of that size floor.
    (build_dir / "pad.bin").write_bytes(os.urandom(260_000))
    jar_path = build_dir / name
    if with_main_class:
        manifest = build_dir / "manifest.txt"
        manifest.write_text("Main-Class: Main\n", encoding="utf-8")
        subprocess.run(["jar", "cfm", str(jar_path), str(manifest), "Main.class", "pad.bin"], cwd=build_dir, check=True, capture_output=True)
    else:
        subprocess.run(["jar", "cf", str(jar_path), "Main.class", "pad.bin"], cwd=build_dir, check=True, capture_output=True)
    return jar_path


def main():
    checks = []
    have_java_tools = bool(shutil.which("javac") and shutil.which("jar") and shutil.which("java"))

    with tempfile.TemporaryDirectory(prefix="sps-panako-") as raw:
        root = Path(raw) / "program_root"
        root.mkdir()

        # -- status before anything is installed: optional, not blocking --
        status = v3_features.panako_status(root)
        assert not status["ready"] and not status["verified"]
        assert "opcioni" in status["message"].lower() or "nije instaliran" in status["message"].lower()
        assert "GPL" in status["license"]
        checks.append('panako_status: not installed -> not ready, license notice present, never a hard error')

        # -- rejects a non-jar file outright --
        not_a_jar = Path(raw) / "not_a_jar.jar"
        not_a_jar.write_text("this is just text, not a real jar", encoding="utf-8")
        try:
            v3_features.install_panako_jar(root, not_a_jar)
            raise AssertionError("expected install_panako_jar to reject a non-ZIP file")
        except RuntimeError as exc:
            assert "nije ispravan" in str(exc).lower() or "zip" in str(exc).lower()
        checks.append('install_panako_jar: rejects a file that is not a real ZIP/.jar')

        # -- rejects a too-small file even if it happens to be a valid empty zip --
        tiny_zip = Path(raw) / "tiny.jar"
        with zipfile.ZipFile(tiny_zip, "w") as zf:
            zf.writestr("hello.txt", "x")
        try:
            v3_features.install_panako_jar(root, tiny_zip)
            raise AssertionError("expected install_panako_jar to reject a too-small jar")
        except RuntimeError as exc:
            assert "premali" in str(exc).lower()
        checks.append('install_panako_jar: rejects a real but implausibly small jar (200KB floor)')

        # -- rejects when no Java runtime is found, before touching the jar --
        big_enough_zip = Path(raw) / "big_enough.jar"
        with zipfile.ZipFile(big_enough_zip, "w", zipfile.ZIP_STORED) as zf:
            zf.writestr("pad.bin", os.urandom(260_000))
        with mock.patch.object(v3_features, "_panako_java", return_value=None):
            try:
                v3_features.install_panako_jar(root, big_enough_zip)
                raise AssertionError("expected install_panako_jar to require Java")
            except RuntimeError as exc:
                assert "java" in str(exc).lower()
        checks.append('install_panako_jar: refuses to proceed without a real Java runtime')

        if not have_java_tools:
            checks.append('SKIPPED (no javac/jar/java on this machine): real-jar install/verify/tamper-detection cases')
        else:
            with tempfile.TemporaryDirectory(prefix="sps-panako-build-") as build_raw:
                build_dir = Path(build_raw)
                good_jar = _build_test_jar(build_dir, "good.jar", with_main_class=True)
                broken_jar = _build_test_jar(build_dir, "broken.jar", with_main_class=False)

                # -- a real runnable jar installs and verifies for real --
                result = v3_features.install_panako_jar(root, good_jar)
                assert result["installed"] and result["exit_code"] == 0
                assert "panako-test-ok" in result["output"]
                checks.append('install_panako_jar: a real runnable jar installs, runs under java -jar, and captures its real output')

                status_after = v3_features.panako_status(root)
                assert status_after["ready"] and status_after["verified"]
                checks.append('panako_status: ready=True only AFTER a real java -jar run actually succeeded, not just because the file exists')

                # -- a jar without a Main-Class must be rejected with the real JVM error, not silently accepted --
                try:
                    v3_features.install_panako_jar(root, broken_jar)
                    raise AssertionError("expected install_panako_jar to reject a jar with no Main-Class")
                except RuntimeError as exc:
                    assert "no main manifest attribute" in str(exc).lower()
                checks.append('install_panako_jar: a jar with no Main-Class fails with the real "no main manifest attribute" JVM error, not a fabricated one')

                # -- a failed re-install attempt must NOT clobber the previously working install --
                # install_panako_jar() validates the candidate at its own source path before ever
                # touching the installed panako.jar, specifically so a bad re-install attempt can't
                # overwrite (and thereby break) an already-working one.
                still_good_status = v3_features.panako_status(root)
                assert still_good_status["ready"] and still_good_status["verified"]
                checks.append('panako_status: a failed re-install attempt (bad candidate jar) leaves the previously working install untouched and still ready')
                jar_path = root / "tools" / "panako" / "panako.jar"

                # -- tampering with the installed jar after the fact must flip verified back to False --
                with jar_path.open("ab") as fh:
                    fh.write(b"corrupted-extra-bytes")
                tampered_status = v3_features.panako_status(root)
                assert not tampered_status["verified"] and not tampered_status["ready"]
                checks.append('panako_status: a jar that changed on disk after install (corrupted/swapped) is no longer trusted as verified')

    print(json.dumps({'ok': True, 'passed': len(checks), 'checks': checks}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
