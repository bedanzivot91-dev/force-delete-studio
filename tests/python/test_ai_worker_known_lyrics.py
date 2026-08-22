import importlib.util
import pathlib
import unittest
from types import SimpleNamespace


REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
WORKER_PATH = REPO_ROOT / "ai-worker" / "ai_worker.py"
SPEC = importlib.util.spec_from_file_location("npvs_ai_worker", WORKER_PATH)
worker = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(worker)


class KnownLyricsLosslessTests(unittest.TestCase):
    def test_all_caps_first_lyric_is_never_guessed_to_be_title(self):
        text = "JOŠ TE VOLIM\n[Verse 2]\nNedostaješ mi svake noći\n[Refren]\nVRATI MI SE"
        self.assertEqual(
            ["JOŠ TE VOLIM", "Nedostaješ mi svake noći", "VRATI MI SE"],
            worker._verified_lyric_lines(text),
        )

    def test_unknown_bracketed_text_is_preserved_instead_of_silently_dropped(self):
        self.assertEqual(
            ["[ovo je stvarni stih]", "Drugi stih"],
            worker._verified_lyric_lines("[ovo je stvarni stih]\nDrugi stih"),
        )

    def test_complete_alignment_preserves_exact_user_text(self):
        lyrics = ["Čuvam tvoje ime", "JOŠ TE VOLIM"]
        aligned = [
            SimpleNamespace(start=1.0, end=2.0, score=0.91, matched=True, line="normalized one"),
            SimpleNamespace(start=2.1, end=3.0, score=0.72, matched=True, line="normalized two"),
        ]
        words = worker._lossless_verified_words(aligned, lyrics)
        self.assertEqual(lyrics, [item["text"] for item in words])
        self.assertEqual(1.0, words[0]["start"])
        self.assertEqual(3.0, words[1]["end"])

    def test_partial_alignment_fails_instead_of_returning_sixty_percent(self):
        lyrics = ["Prvi stih", "Drugi stih", "Treći stih", "Četvrti stih", "Peti stih"]
        aligned = [
            SimpleNamespace(start=0.0, end=1.0, score=0.9, matched=True),
            SimpleNamespace(start=1.0, end=2.0, score=0.9, matched=True),
            SimpleNamespace(start=2.0, end=3.0, score=0.9, matched=True),
            SimpleNamespace(start=None, end=None, score=0.0, matched=False),
            SimpleNamespace(start=None, end=None, score=0.0, matched=False),
        ]
        with self.assertRaisesRegex(RuntimeError, r"3/5.*4: Četvrti stih.*5: Peti stih"):
            worker._lossless_verified_words(aligned, lyrics)

    def test_missing_tail_line_fails_clearly(self):
        lyrics = ["Jedan", "Dva", "Tri"]
        aligned = [
            SimpleNamespace(start=0.0, end=1.0, score=0.9, matched=True),
            SimpleNamespace(start=1.0, end=2.0, score=0.9, matched=True),
        ]
        with self.assertRaisesRegex(RuntimeError, r"2/3.*3: Tri"):
            worker._lossless_verified_words(aligned, lyrics)


if __name__ == "__main__":
    unittest.main()
