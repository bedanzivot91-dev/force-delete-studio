import importlib.util
import io
import json
from contextlib import redirect_stdout
from pathlib import Path
import unittest


WORKER_PATH = Path(__file__).parents[2] / "ai-worker" / "ai_worker.py"
SPEC = importlib.util.spec_from_file_location("npvs_ai_worker_translation", WORKER_PATH)
WORKER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(WORKER)


class SubtitleTranslationTests(unittest.TestCase):
    def test_rejects_same_source_and_target_without_loading_engine(self):
        output = io.StringIO()
        with redirect_stdout(output):
            result = WORKER.run_subtitle_translation({
                "texts": ["Zdravo"], "sourceLanguage": "sr", "targetLanguage": "sr"
            })

        self.assertEqual(1, result)
        event = json.loads(output.getvalue())
        self.assertEqual("Error", event["type"])
        self.assertIn("jezici", event["message"])

    def test_tts_rejects_empty_text_before_loading_voice_engine(self):
        output = io.StringIO()
        with redirect_stdout(output):
            result = WORKER.run_text_to_speech({"text": "", "outputFilePath": "voice.wav"})

        self.assertEqual(1, result)
        event = json.loads(output.getvalue())
        self.assertEqual("Error", event["type"])
        self.assertIn("Tekst naracije", event["message"])


if __name__ == "__main__":
    unittest.main()
