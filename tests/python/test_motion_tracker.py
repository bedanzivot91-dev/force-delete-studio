import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import cv2
import numpy as np


class MotionTrackerRegressionTests(unittest.TestCase):
    def test_csrt_tracks_real_moving_target_across_synthetic_video(self):
        repo = Path(__file__).resolve().parents[2]
        worker = repo / "ai-worker" / "motion_tracker.py"
        self.assertTrue(worker.is_file(), worker)

        with tempfile.TemporaryDirectory(prefix="npvs_tracking_test_") as temp_dir:
            temp = Path(temp_dir)
            video = temp / "moving-target.avi"
            fps = 20.0
            width, height = 160, 120
            writer = cv2.VideoWriter(
                str(video), cv2.VideoWriter_fourcc(*"MJPG"), fps, (width, height)
            )
            self.assertTrue(writer.isOpened(), "OpenCV nije mogao da napravi sintetički test video.")

            for frame_index in range(50):
                frame = np.zeros((height, width, 3), dtype=np.uint8)
                x = 18 + frame_index
                y = 45
                # Textured 30x30 target: the internal black/white pattern gives CSRT real features,
                # instead of relying on a featureless solid rectangle that can be tracker-dependent.
                cv2.rectangle(frame, (x, y), (x + 30, y + 30), (240, 240, 240), -1)
                cv2.rectangle(frame, (x + 4, y + 4), (x + 13, y + 13), (0, 0, 0), -1)
                cv2.rectangle(frame, (x + 17, y + 17), (x + 26, y + 26), (0, 0, 0), -1)
                cv2.line(frame, (x + 2, y + 28), (x + 28, y + 2), (80, 80, 80), 2)
                writer.write(frame)
            writer.release()
            self.assertTrue(video.is_file() and video.stat().st_size > 10_000)

            request = {
                "mediaFilePath": str(video),
                "sourceStartSeconds": 0.0,
                "sourceEndSeconds": 2.0,
                "sampleIntervalSeconds": 0.10,
                "initialRegion": {
                    "centerX": (18 + 15) / width,
                    "centerY": (45 + 15) / height,
                    "width": 30 / width,
                    "height": 30 / height,
                },
            }
            request_path = temp / "request.json"
            request_path.write_text(json.dumps(request), encoding="utf-8")

            completed = subprocess.run(
                [sys.executable, str(worker), "--request", str(request_path)],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                encoding="utf-8",
                errors="replace",
                check=False,
            )
            self.assertEqual(0, completed.returncode, completed.stdout + "\n" + completed.stderr)
            payload = json.loads(completed.stdout)
            self.assertEqual("CSRT", payload.get("tracker"))
            points = payload.get("trackingPoints") or []
            self.assertGreaterEqual(len(points), 10)
            self.assertLess(points[0]["centerX"], points[-1]["centerX"] - 0.15)
            self.assertGreaterEqual(points[-1]["sourceTimeSeconds"], 1.95)
            for point in points:
                for key in ("centerX", "centerY", "width", "height", "confidence"):
                    self.assertGreaterEqual(point[key], 0.0, (key, point))
                    self.assertLessEqual(point[key], 1.0, (key, point))


if __name__ == "__main__":
    unittest.main()
