from __future__ import annotations

import hashlib
import hmac
import os
import threading
import time
from typing import Any


class AppSecurity:
    """Opcioni lokalni PIN. Hash se čuva u bazi; PIN nikada nije sačuvan."""

    def __init__(self, db: Any):
        self.db = db
        self._lock = threading.RLock()
        self._unlocked = not self.enabled()
        self._last_activity = time.monotonic()
        self._failed_attempts = 0
        self._blocked_until = 0.0

    def enabled(self) -> bool:
        return bool(self.db.get_setting("security_pin_hash", "") and self.db.get_setting("security_pin_salt", ""))

    def timeout_minutes(self) -> int:
        try:
            return max(0, min(1440, int(self.db.get_setting("security_timeout_minutes", "30") or 30)))
        except Exception:
            return 30

    def _derive(self, pin: str, salt: bytes) -> str:
        return hashlib.pbkdf2_hmac("sha256", pin.encode("utf-8"), salt, 310_000).hex()

    def is_locked(self) -> bool:
        with self._lock:
            if not self.enabled():
                self._unlocked = True
                return False
            timeout = self.timeout_minutes()
            if timeout and self._unlocked and time.monotonic() - self._last_activity > timeout * 60:
                self._unlocked = False
            return not self._unlocked

    def touch(self) -> None:
        with self._lock:
            if self._unlocked:
                self._last_activity = time.monotonic()

    def status(self) -> dict[str, Any]:
        with self._lock:
            wait = max(0, int(self._blocked_until - time.monotonic()))
            return {
                "enabled": self.enabled(),
                "locked": self.is_locked(),
                "timeout_minutes": self.timeout_minutes(),
                "failed_attempts": self._failed_attempts,
                "retry_after_seconds": wait,
            }

    def set_pin(self, new_pin: str, *, timeout_minutes: int = 30) -> dict[str, Any]:
        value = str(new_pin or "")
        if len(value) < 4 or len(value) > 64:
            raise ValueError("PIN mora imati od 4 do 64 znaka.")
        if value.isdigit() and len(set(value)) == 1:
            raise ValueError("PIN ne sme biti sastavljen od jednog ponovljenog broja.")
        salt = os.urandom(16)
        self.db.set_setting("security_pin_salt", salt.hex())
        self.db.set_setting("security_pin_hash", self._derive(value, salt))
        self.db.set_setting("security_timeout_minutes", str(max(0, min(1440, int(timeout_minutes)))))
        with self._lock:
            self._unlocked = True
            self._last_activity = time.monotonic()
            self._failed_attempts = 0
            self._blocked_until = 0.0
        return self.status()

    def unlock(self, pin: str) -> dict[str, Any]:
        with self._lock:
            now = time.monotonic()
            if now < self._blocked_until:
                raise PermissionError(f"Previše pokušaja. Pokušaj ponovo za {int(self._blocked_until-now)+1} sekundi.")
            if not self.enabled():
                self._unlocked = True
                return self.status()
            try:
                salt = bytes.fromhex(self.db.get_setting("security_pin_salt", ""))
            except Exception as exc:
                raise RuntimeError("Podaci za zaključavanje su oštećeni.") from exc
            expected = self.db.get_setting("security_pin_hash", "")
            actual = self._derive(str(pin or ""), salt)
            if not hmac.compare_digest(expected, actual):
                self._failed_attempts += 1
                if self._failed_attempts >= 5:
                    delay = min(300, 15 * (2 ** min(4, self._failed_attempts - 5)))
                    self._blocked_until = now + delay
                raise PermissionError("PIN nije ispravan.")
            self._unlocked = True
            self._last_activity = now
            self._failed_attempts = 0
            self._blocked_until = 0.0
            return self.status()

    def lock_now(self) -> dict[str, Any]:
        with self._lock:
            if self.enabled():
                self._unlocked = False
            return self.status()

    def disable(self, pin: str) -> dict[str, Any]:
        self.unlock(pin)
        self.db.set_setting("security_pin_salt", "")
        self.db.set_setting("security_pin_hash", "")
        self.db.set_setting("security_timeout_minutes", "30")
        with self._lock:
            self._unlocked = True
        return self.status()
