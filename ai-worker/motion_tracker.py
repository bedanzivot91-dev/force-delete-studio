#!/usr/bin/env python3
"""Offline object tracking for NP Video Studio.

Input: --request <JSON file>. Output: one JSON object on stdout.
Uses OpenCV contrib's CSRT tracker. It never extrapolates after a tracking failure: if CSRT loses the
object before the requested source range ends the command exits non-zero with a clear Serbian message.
"""
import argparse
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")


def clamp(value: float, low: float, high: float) -> float:
    return max(low, min(high, value))


def create_csrt(cv2):
    creator = getattr(cv2, "TrackerCSRT_create", None)
    if creator is not None:
        return creator()
    legacy = getattr(cv2, "legacy", None)
    creator = getattr(legacy, "TrackerCSRT_create", None) if legacy is not None else None
    if creator is not None:
        return creator()
    raise RuntimeError("OpenCV je instaliran bez CSRT trackera; potreban je opencv-contrib-python-headless.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--request", required=True)
    args = parser.parse_args()

    try:
        with open(args.request, "r", encoding="utf-8") as handle:
            request = json.load(handle)
    except (OSError, json.JSONDecodeError) as ex:
        print(json.dumps({"error": f"Ne mogu da pročitam tracking zahtev: {ex}"}, ensure_ascii=False))
        return 2

    source = request.get("mediaFilePath")
    if not source or not os.path.isfile(source):
        print(json.dumps({"error": "Video fajl za Motion Tracking ne postoji."}, ensure_ascii=False))
        return 2

    try:
        import cv2
    except ImportError:
        print(json.dumps({
            "error": "OpenCV tracking nije instaliran. Otvorite Alati i modeli i instalirajte/ažurirajte AI alate."
        }, ensure_ascii=False))
        return 3

    start = max(0.0, float(request.get("sourceStartSeconds", 0.0)))
    end = float(request.get("sourceEndSeconds", 0.0))
    if end <= start + 0.05:
        print(json.dumps({"error": "Motion Tracking opseg je prekratak."}, ensure_ascii=False))
        return 2

    interval = clamp(float(request.get("sampleIntervalSeconds", 0.1)), 0.04, 1.0)
    region = request.get("initialRegion") or {}
    cx = clamp(float(region.get("centerX", 0.5)), 0.0, 1.0)
    cy = clamp(float(region.get("centerY", 0.5)), 0.0, 1.0)
    rw = clamp(float(region.get("width", 0.25)), 0.02, 1.0)
    rh = clamp(float(region.get("height", 0.25)), 0.02, 1.0)

    capture = cv2.VideoCapture(source)
    if not capture.isOpened():
        print(json.dumps({"error": "OpenCV ne može da otvori video za Motion Tracking."}, ensure_ascii=False))
        return 4

    try:
        fps = float(capture.get(cv2.CAP_PROP_FPS) or 0.0)
        if fps <= 0.001:
            raise RuntimeError("Video nema validan FPS za Motion Tracking.")

        capture.set(cv2.CAP_PROP_POS_MSEC, start * 1000.0)
        ok, frame = capture.read()
        if not ok or frame is None:
            raise RuntimeError("Ne mogu da pročitam početni kadar za Motion Tracking.")

        height, width = frame.shape[:2]
        # OpenCV 5's Python CSRT binding requires an integer Rect bounding box. Older OpenCV builds
        # accepted floats here, so normalise once to a strict pixel rectangle that works on both APIs.
        box_w = min(width, max(2, int(round(rw * width))))
        box_h = min(height, max(2, int(round(rh * height))))
        box_x = int(round(clamp(cx * width - box_w / 2.0, 0.0, max(0.0, width - box_w))))
        box_y = int(round(clamp(cy * height - box_h / 2.0, 0.0, max(0.0, height - box_h))))
        initial_box = (box_x, box_y, box_w, box_h)

        tracker = create_csrt(cv2)
        init_result = tracker.init(frame, initial_box)
        if init_result is False:
            raise RuntimeError("CSRT nije prihvatio početni region objekta.")

        def point(source_time: float, box) -> dict:
            x, y, w, h = [float(v) for v in box]
            return {
                "sourceTimeSeconds": source_time,
                "centerX": clamp((x + w / 2.0) / width, 0.0, 1.0),
                "centerY": clamp((y + h / 2.0) / height, 0.0, 1.0),
                "width": clamp(w / width, 0.0, 1.0),
                "height": clamp(h / height, 0.0, 1.0),
                # CSRT's Python API exposes success/failure, not a calibrated probability.
                "confidence": 1.0,
            }

        current_box = initial_box
        points = [point(start, current_box)]
        next_sample = start + interval
        last_frame_time = start

        while True:
            ok, frame = capture.read()
            if not ok or frame is None:
                break

            frame_index = max(0.0, float(capture.get(cv2.CAP_PROP_POS_FRAMES)) - 1.0)
            source_time = frame_index / fps
            if source_time <= start:
                source_time = start + (1.0 / fps)
            if source_time > end + (0.5 / fps):
                break

            tracked, current_box = tracker.update(frame)
            if not tracked:
                raise RuntimeError(
                    f"CSRT je izgubio objekat na {source_time:.2f}s. Tracking je prekinut; ostatak putanje nije izmišljen."
                )

            last_frame_time = source_time
            if source_time + 1e-6 >= next_sample or source_time + (0.5 / fps) >= end:
                points.append(point(min(source_time, end), current_box))
                while next_sample <= source_time + 1e-6:
                    next_sample += interval

            if source_time + (0.5 / fps) >= end:
                break

        # A source range can end between frames, but ending materially early means decoding/tracking did not
        # cover what the caller asked for and must not be reported as a complete path.
        allowed_shortfall = max(0.15, 2.5 / fps)
        if last_frame_time < end - allowed_shortfall:
            raise RuntimeError(
                f"Motion Tracking nije pokrio ceo opseg: stigao je do {last_frame_time:.2f}s od traženih {end:.2f}s."
            )

        if points[-1]["sourceTimeSeconds"] < end - 1e-6:
            final_point = point(end, current_box)
            points.append(final_point)

        print(json.dumps({"trackingPoints": points, "tracker": "CSRT"}, ensure_ascii=False))
        return 0
    except Exception as ex:
        print(json.dumps({"error": str(ex)}, ensure_ascii=False))
        return 5
    finally:
        capture.release()


if __name__ == "__main__":
    sys.exit(main())
