from __future__ import annotations

import ctypes
import os
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
USER_DATA_ROOT = Path(os.environ.get("SUNO_STUDIO_USER_DIR") or ROOT).expanduser().resolve()
DATA_DIR = Path(os.environ.get("SUNO_STUDIO_DATA_DIR") or (USER_DATA_ROOT / "data")).expanduser().resolve()
STOP_FILE = DATA_DIR / "watchdog.stop"
LOG_FILE = DATA_DIR / "watchdog.log"
SERVER = ROOT / "app" / "server.py"
SERVER_CONSOLE_LOG = DATA_DIR / "server-konzola.log"


def log(message: str) -> None:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).isoformat(timespec="seconds")
    try:
        with LOG_FILE.open("a", encoding="utf-8") as fh:
            fh.write(f"[{stamp}] {message}\n")
    except Exception:
        pass


def acquire_single_instance() -> object | None:
    if os.name == "nt":
        kernel32 = ctypes.windll.kernel32
        handle = kernel32.CreateMutexW(None, False, "Local\\SunoPesmeStudioV3Watchdog")
        if not handle:
            return None
        if kernel32.GetLastError() == 183:  # ERROR_ALREADY_EXISTS
            kernel32.CloseHandle(handle)
            return None
        return handle
    try:
        import fcntl
        lock_path = DATA_DIR / "watchdog.lock"
        lock_path.parent.mkdir(parents=True, exist_ok=True)
        fh = lock_path.open("a+")
        fcntl.flock(fh.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        return fh
    except Exception:
        return None


def main() -> int:
    lock = acquire_single_instance()
    if lock is None:
        return 0
    STOP_FILE.unlink(missing_ok=True)
    env = os.environ.copy()
    env["SUNO_WATCHDOG"] = "1"
    env.setdefault("SUNO_AUTO_OPEN", "0")
    crash_times: list[float] = []
    backoff = 1
    log(f"Watchdog je pokrenut. Python: {sys.executable}")
    while True:
        if STOP_FILE.exists():
            STOP_FILE.unlink(missing_ok=True)
            log("Primljen je zahtev za normalno gašenje.")
            return 0
        started = time.monotonic()
        try:
            creationflags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
            DATA_DIR.mkdir(parents=True, exist_ok=True)
            with SERVER_CONSOLE_LOG.open("a", encoding="utf-8", errors="replace") as console:
                console.write(f"\n[{datetime.now(timezone.utc).isoformat(timespec='seconds')}] Pokretanje servera\n")
                console.flush()
                process = subprocess.Popen(
                    [sys.executable, str(SERVER)],
                    cwd=str(ROOT),
                    env=env,
                    stdin=subprocess.DEVNULL,
                    stdout=console,
                    stderr=subprocess.STDOUT,
                    creationflags=creationflags,
                )
                log(f"Server je pokrenut, PID {process.pid}.")
                code = process.wait()
        except Exception as exc:
            code = -1
            log(f"Watchdog nije uspeo da pokrene server: {exc}")
        runtime = time.monotonic() - started
        if STOP_FILE.exists():
            STOP_FILE.unlink(missing_ok=True)
            log(f"Server je normalno ugašen, izlazni kod {code}.")
            return 0
        now = time.monotonic()
        crash_times = [value for value in crash_times if now - value < 600]
        crash_times.append(now)
        if runtime >= 120:
            crash_times.clear()
            backoff = 1
        if len(crash_times) >= 6:
            log("Server je pao 6 puta za manje od 10 minuta. Automatsko ponovno pokretanje je zaustavljeno.")
            return 2
        log(f"Server je neočekivano stao (kod {code}, radio {runtime:.1f}s). Ponovni pokušaj za {backoff}s.")
        time.sleep(backoff)
        backoff = min(30, backoff * 2)


if __name__ == "__main__":
    raise SystemExit(main())
