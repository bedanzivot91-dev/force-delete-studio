from __future__ import annotations

import base64
import hashlib
import json
import os
import secrets
import socket
import struct
import subprocess
import time
import urllib.request
from pathlib import Path
from typing import Any
from urllib.parse import urlparse


class CDPError(RuntimeError):
    pass


class SimpleWebSocket:
    """Minimal RFC6455 client, sufficient for local Chrome DevTools Protocol."""

    def __init__(self, url: str, timeout: float = 10.0):
        self.url = url
        self.timeout = timeout
        self.sock: socket.socket | None = None
        self._buffer = b""
        self._connect()

    def _connect(self) -> None:
        parsed = urlparse(self.url)
        if parsed.scheme != "ws":
            raise CDPError("Podržan je samo lokalni ws:// Chrome DevTools priključak.")
        host = parsed.hostname or "127.0.0.1"
        port = parsed.port or 80
        path = parsed.path or "/"
        if parsed.query:
            path += "?" + parsed.query

        sock = socket.create_connection((host, port), timeout=self.timeout)
        sock.settimeout(self.timeout)
        key = base64.b64encode(secrets.token_bytes(16)).decode("ascii")
        request = (
            f"GET {path} HTTP/1.1\r\n"
            f"Host: {host}:{port}\r\n"
            "Upgrade: websocket\r\n"
            "Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            "Sec-WebSocket-Version: 13\r\n\r\n"
        )
        sock.sendall(request.encode("ascii"))
        response = b""
        while b"\r\n\r\n" not in response:
            chunk = sock.recv(4096)
            if not chunk:
                break
            response += chunk
            if len(response) > 65536:
                break
        header, _, rest = response.partition(b"\r\n\r\n")
        first = header.split(b"\r\n", 1)[0]
        if b" 101 " not in first:
            sock.close()
            raise CDPError(f"Chrome DevTools WebSocket nije prihvatio vezu: {first.decode(errors='replace')}")
        accept_expected = base64.b64encode(
            hashlib.sha1((key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").encode("ascii")).digest()
        )
        headers = {}
        for line in header.split(b"\r\n")[1:]:
            if b":" in line:
                k, v = line.split(b":", 1)
                headers[k.strip().lower()] = v.strip()
        if headers.get(b"sec-websocket-accept") != accept_expected:
            sock.close()
            raise CDPError("Neispravan odgovor Chrome DevTools WebSocket veze.")
        self.sock = sock
        self._buffer = rest

    def close(self) -> None:
        if self.sock:
            try:
                self._send_frame(b"", opcode=0x8)
            except Exception:
                pass
            try:
                self.sock.close()
            except Exception:
                pass
            self.sock = None

    def _send_frame(self, payload: bytes, opcode: int = 0x1) -> None:
        if not self.sock:
            raise CDPError("WebSocket veza nije otvorena.")
        first = 0x80 | (opcode & 0x0F)
        length = len(payload)
        mask = secrets.token_bytes(4)
        if length < 126:
            header = bytes([first, 0x80 | length])
        elif length < (1 << 16):
            header = bytes([first, 0x80 | 126]) + struct.pack("!H", length)
        else:
            header = bytes([first, 0x80 | 127]) + struct.pack("!Q", length)
        masked = bytes(b ^ mask[i % 4] for i, b in enumerate(payload))
        self.sock.sendall(header + mask + masked)

    def send_text(self, text: str) -> None:
        self._send_frame(text.encode("utf-8"), opcode=0x1)

    def _recv_exact(self, size: int) -> bytes:
        while len(self._buffer) < size:
            if not self.sock:
                raise CDPError("WebSocket veza je zatvorena.")
            chunk = self.sock.recv(max(4096, size - len(self._buffer)))
            if not chunk:
                raise CDPError("Chrome je zatvorio DevTools vezu.")
            self._buffer += chunk
        data, self._buffer = self._buffer[:size], self._buffer[size:]
        return data

    def recv_text(self) -> str:
        fragments: list[bytes] = []
        current_opcode = None
        while True:
            first2 = self._recv_exact(2)
            b1, b2 = first2
            fin = bool(b1 & 0x80)
            opcode = b1 & 0x0F
            masked = bool(b2 & 0x80)
            length = b2 & 0x7F
            if length == 126:
                length = struct.unpack("!H", self._recv_exact(2))[0]
            elif length == 127:
                length = struct.unpack("!Q", self._recv_exact(8))[0]
            mask_key = self._recv_exact(4) if masked else b""
            payload = self._recv_exact(length) if length else b""
            if masked:
                payload = bytes(b ^ mask_key[i % 4] for i, b in enumerate(payload))

            if opcode == 0x8:
                raise CDPError("Chrome je zatvorio DevTools vezu.")
            if opcode == 0x9:
                self._send_frame(payload, opcode=0xA)
                continue
            if opcode == 0xA:
                continue
            if opcode in (0x1, 0x2):
                current_opcode = opcode
                fragments = [payload]
            elif opcode == 0x0 and current_opcode is not None:
                fragments.append(payload)
            else:
                continue
            if fin:
                data = b"".join(fragments)
                return data.decode("utf-8", errors="replace")


class ChromeConnector:
    def __init__(self, data_dir: Path, port: int = 9333):
        self.data_dir = data_dir
        self.port = port
        self.profile_dir = data_dir / "chrome_suno_profil"
        self.process: subprocess.Popen[Any] | None = None
        self._counter = 0

    @staticmethod
    def find_browser() -> str | None:
        candidates: list[str] = []
        env = os.environ
        for key in ("PROGRAMFILES", "PROGRAMFILES(X86)", "LOCALAPPDATA"):
            base = env.get(key)
            if base:
                candidates.extend([
                    os.path.join(base, "Google", "Chrome", "Application", "chrome.exe"),
                    os.path.join(base, "Microsoft", "Edge", "Application", "msedge.exe"),
                    os.path.join(base, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                ])
        candidates.extend([
            r"C:\Program Files\Google\Chrome\Application\chrome.exe",
            r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        ])
        for path in candidates:
            if path and os.path.isfile(path):
                return path
        return None

    def _connector_alive(self) -> bool:
        try:
            return bool(self._json_get("/json/list", timeout=1.2))
        except Exception:
            return False

    def launch(self) -> dict[str, Any]:
        # Repeated clicks used to open three or more Suno tabs. Reuse the same
        # dedicated browser process instead.
        if self._connector_alive():
            try:
                pages = self.pages()
                suno_pages = [p for p in pages if p.get("type") == "page" and "suno.com" in str(p.get("url", ""))]
                if suno_pages:
                    self.command_on_page(suno_pages[0], "Page.bringToFront", timeout=4.0)
                    return {
                        "ok": True,
                        "message": "Suno prozor je već otvoren. Prijavi se ako je potrebno, pa klikni „Proveri vezu“.",
                        "reused": True,
                    }
            except Exception:
                pass

        browser = self.find_browser()
        if not browser:
            return {"ok": False, "message": "Nisam pronašao Chrome, Edge ili Brave na računaru."}
        self.profile_dir.mkdir(parents=True, exist_ok=True)
        args = [
            browser,
            f"--remote-debugging-port={self.port}",
            "--remote-allow-origins=*",
            f"--user-data-dir={self.profile_dir}",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-mode",
            "--disable-features=TranslateUI",
            "https://suno.com/me",
        ]
        try:
            flags = 0
            if os.name == "nt":
                flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
            self.process = subprocess.Popen(args, creationflags=flags)
            # Give DevTools a short moment to start so the next click does not
            # accidentally create a duplicate window.
            deadline = time.monotonic() + 8
            while time.monotonic() < deadline:
                if self._connector_alive():
                    break
                time.sleep(0.25)
            return {
                "ok": True,
                "message": "Otvoren je poseban Suno prozor. Prijava je već vidljiva ako je nalog zapamćen. Ne moraš da čekaš da se spisak pesama učita — vrati se u program i klikni „Proveri vezu“.",
                "browser": browser,
            }
        except Exception as exc:
            return {"ok": False, "message": f"Ne mogu da pokrenem browser: {exc}"}

    def _json_get(self, path: str, timeout: float = 3.0) -> Any:
        url = f"http://127.0.0.1:{self.port}{path}"
        req = urllib.request.Request(url, headers={"User-Agent": "Suno-Pesme-Studio/1.0.2"})
        with urllib.request.urlopen(req, timeout=timeout) as response:
            return json.loads(response.read().decode("utf-8"))

    def pages(self) -> list[dict[str, Any]]:
        try:
            data = self._json_get("/json/list")
            return data if isinstance(data, list) else []
        except Exception as exc:
            raise CDPError(f"Chrome konektor nije dostupan: {exc}") from exc

    def _suno_pages(self) -> list[dict[str, Any]]:
        pages = self.pages()
        result = [p for p in pages if p.get("type") == "page" and "suno.com" in str(p.get("url", ""))]
        if not result:
            raise CDPError("U posebnom browser prozoru nije otvoren Suno.com.")
        # Prefer the /me library tab, then any current Suno tab.
        result.sort(key=lambda p: ("/me" not in str(p.get("url", "")), str(p.get("url", ""))))
        return result

    def _suno_page(self) -> dict[str, Any]:
        return self._suno_pages()[0]

    def command_on_page(
        self,
        page: dict[str, Any],
        method: str,
        params: dict[str, Any] | None = None,
        timeout: float = 12.0,
    ) -> dict[str, Any]:
        ws_url = page.get("webSocketDebuggerUrl")
        if not ws_url:
            raise CDPError("Chrome nije dao DevTools adresu za Suno stranicu.")
        ws = SimpleWebSocket(str(ws_url), timeout=timeout)
        try:
            self._counter += 1
            request_id = self._counter
            ws.send_text(json.dumps({"id": request_id, "method": method, "params": params or {}}))
            deadline = time.monotonic() + timeout
            while time.monotonic() < deadline:
                message = json.loads(ws.recv_text())
                if message.get("id") != request_id:
                    continue
                if "error" in message:
                    raise CDPError(str(message["error"]))
                result = message.get("result")
                return result if isinstance(result, dict) else {}
            raise CDPError("Isteklo je vreme za odgovor Suno stranice.")
        finally:
            ws.close()

    def command(self, method: str, params: dict[str, Any] | None = None, timeout: float = 12.0) -> dict[str, Any]:
        return self.command_on_page(self._suno_page(), method, params=params, timeout=timeout)

    def evaluate_on_page(self, page: dict[str, Any], expression: str, timeout: float = 12.0) -> Any:
        response = self.command_on_page(
            page,
            "Runtime.evaluate",
            {
                "expression": expression,
                "awaitPromise": True,
                "returnByValue": True,
                "userGesture": True,
            },
            timeout=timeout,
        )
        result = response.get("result", {})
        if result.get("subtype") == "error":
            raise CDPError(str(result.get("description") or result))
        if response.get("exceptionDetails"):
            raise CDPError(str(response["exceptionDetails"]))
        return result.get("value")

    def evaluate(self, expression: str, timeout: float = 12.0) -> Any:
        return self.evaluate_on_page(self._suno_page(), expression, timeout=timeout)

    def get_user_agent(self) -> str | None:
        try:
            value = self.evaluate("navigator.userAgent", timeout=5.0)
            return str(value) if value else None
        except Exception:
            return None

    def _cookie_session_token(self) -> str | None:
        """Best-effort fallback for builds where Clerk is not on window."""
        for page in self._suno_pages():
            for method in ("Network.getAllCookies", "Storage.getCookies"):
                try:
                    result = self.command_on_page(page, method, timeout=8.0)
                    candidates: list[str] = []
                    for cookie in result.get("cookies", []):
                        if not isinstance(cookie, dict):
                            continue
                        domain = str(cookie.get("domain") or "")
                        if cookie.get("name") == "__session" and "suno.com" in domain:
                            value = cookie.get("value")
                            if value:
                                candidates.append(str(value))
                    if candidates:
                        return max(candidates, key=len)
                except Exception:
                    pass
        return None

    def refresh_session_activity(self, *, touch: bool = True) -> dict[str, Any]:
        """Touch the live Suno/Clerk session and mint a fresh bearer token.

        A Clerk bearer JWT is short-lived even though the browser login session is
        still valid. Calling ``session.touch()`` records activity and
        ``getToken({skipCache:true})`` mints a new JWT. This keeps the dedicated
        Suno browser session active while the local program is running, without
        storing a password or bypassing Suno's server-side maximum session life.
        """
        touch_js = "true" if touch else "false"
        expression = rf"""
        (async () => {{
          const sleep = (ms) => new Promise(resolve => setTimeout(resolve, ms));
          for (let i = 0; i < 30; i++) {{
            try {{
              const clerk = window.Clerk;
              const session = clerk && clerk.session;
              if (session) {{
                let touched = false;
                let touchError = "";
                if ({touch_js} && typeof session.touch === "function") {{
                  try {{ await session.touch({{ intent: "focus" }}); touched = true; }}
                  catch (error) {{ touchError = String(error && (error.message || error) || "touch failed"); }}
                }}
                let token = null;
                try {{ token = await session.getToken({{ skipCache: true }}); }} catch (_) {{}}
                if (!token) {{ try {{ token = await session.getToken(); }} catch (_) {{}} }}
                return {{
                  ok: Boolean(token), token: token || null, touched,
                  touchError, status: String(session.status || ""),
                  id: String(session.id || ""),
                  expireAt: session.expireAt ? String(session.expireAt) : "",
                  abandonAt: session.abandonAt ? String(session.abandonAt) : ""
                }};
              }}
            }} catch (_) {{}}
            await sleep(250);
          }}
          return {{ ok: false, token: null, touched: false, reason: "Clerk session is not available" }};
        }})()
        """
        last_error = ""
        for page in self._suno_pages():
            try:
                value = self.evaluate_on_page(page, expression, timeout=15.0)
                if isinstance(value, dict) and value.get("token"):
                    value["page_url"] = str(page.get("url") or "")
                    return value
                if isinstance(value, dict):
                    last_error = str(value.get("reason") or value.get("touchError") or last_error)
            except Exception as exc:
                last_error = str(exc)

        cookie_token = self._cookie_session_token()
        if cookie_token:
            return {
                "ok": True,
                "token": cookie_token,
                "touched": False,
                "fallback": "cookie",
                "warning": "Clerk nije bio dostupan; upotrebljen je __session kolačić.",
            }
        return {"ok": False, "token": None, "touched": False, "reason": last_error or "Suno session is not available"}

    def get_session_token(self) -> str | None:
        result = self.refresh_session_activity(touch=False)
        token = result.get("token") if isinstance(result, dict) else None
        return str(token) if token else None

    def diagnostics(self) -> dict[str, Any]:
        info: dict[str, Any] = {
            "port": self.port,
            "profile": str(self.profile_dir),
            "pages": [],
            "clerk": False,
            "signed_in": False,
            "user_agent": self.get_user_agent() or "",
        }
        try:
            for page in self._suno_pages():
                info["pages"].append({"url": page.get("url", ""), "title": page.get("title", "")})
                try:
                    state = self.evaluate_on_page(
                        page,
                        "({clerk: Boolean(window.Clerk), signedIn: Boolean(window.Clerk && window.Clerk.session), ready: document.readyState})",
                        timeout=5.0,
                    )
                    if isinstance(state, dict):
                        info["clerk"] = bool(info["clerk"] or state.get("clerk"))
                        info["signed_in"] = bool(info["signed_in"] or state.get("signedIn"))
                        info["ready_state"] = state.get("ready")
                except Exception:
                    pass
        except Exception as exc:
            info["error"] = str(exc)
        return info

    def connection_status(self) -> dict[str, Any]:
        try:
            session_refresh = self.refresh_session_activity(touch=True)
            token = str(session_refresh.get("token") or "")
            diag = self.diagnostics()
            diag["session_refresh"] = {key: value for key, value in session_refresh.items() if key != "token"}
            if not token:
                return {
                    "ok": False,
                    "connected": False,
                    "message": (
                        "Suno nalog je otvoren, ali program nije dobio svež prijavni token. "
                        "Zatvori duple Suno kartice, osveži jednu karticu sa Ctrl+R i ponovo klikni „Proveri vezu“."
                    ),
                    "diagnostics": diag,
                }
            return {
                "ok": True,
                "connected": True,
                "message": "Svež Suno token je pronađen. Proveravam novu Suno biblioteku v3...",
                "token": token,
                "user_agent": diag.get("user_agent") or "",
                "diagnostics": diag,
            }
        except Exception as exc:
            return {"ok": False, "connected": False, "message": str(exc), "diagnostics": self.diagnostics()}
