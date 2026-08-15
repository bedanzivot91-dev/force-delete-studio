#!/usr/bin/env python3
"""Single-image OCR helper for NP Video Studio's EasyOcrVideoLayoutAnalysisService.

Reads one image path from argv, runs EasyOCR with the Serbian Latin language model (this app is
Serbian-Latin-only, see CLAUDE.md), and prints one JSON array to stdout: [{text, confidence, x, y,
width, height}, ...] with x/y/width/height already normalized to 0..1 fractions of the image size -
the same normalized-region shape TesseractOcrService.ParseTsv produces, so the C# side parses both
identically.

Deliberately NOT the ai_worker.py JSONL-event protocol (Phase 5): that protocol is shaped for
long-running audio transcription jobs with progress events. A single-frame OCR call is a plain
request/response - one argument in, one JSON blob out - so it reuses the simpler subprocess pattern
already used for Tesseract/ffmpeg/yt-dlp/fpcalc instead of adding one more protocol.

Why EasyOCR alongside Tesseract rather than replacing it: verified directly against a real user
video's on-screen decorative caption text ("NEDOSTAJEŠ PUNOO" in a colored, outlined comic-style
font) - Tesseract returned garbage, EasyOCR with the rs_latin model read both words correctly
(81% / 95% confidence). Tesseract stays the default (lighter weight, no extra Python ML
dependencies); EasyOcrVideoLayoutAnalysisService is the opt-in fallback for exactly this failure
mode. Install with: pip install -r easyocr-helper/requirements.txt
"""
import json
import sys


def main() -> int:
    if len(sys.argv) != 2:
        print(json.dumps({"error": "Upotreba: ocr_frame.py <putanja-do-slike>"}), file=sys.stderr)
        return 1

    image_path = sys.argv[1]

    try:
        import easyocr
        from PIL import Image
    except ImportError as ex:
        print(json.dumps({"error": f"EasyOCR nije instaliran: {ex}"}), file=sys.stderr)
        return 1

    try:
        with Image.open(image_path) as img:
            width, height = img.size
    except OSError as ex:
        print(json.dumps({"error": f"Ne mogu da otvorim sliku: {ex}"}), file=sys.stderr)
        return 1

    if width <= 0 or height <= 0:
        print(json.dumps({"error": "Slika ima nevažeću veličinu."}), file=sys.stderr)
        return 1

    reader = easyocr.Reader(["rs_latin"], gpu=False, verbose=False)
    results = reader.readtext(image_path)

    regions = []
    for bbox, text, confidence in results:
        text = text.strip()
        if not text:
            continue
        xs = [point[0] for point in bbox]
        ys = [point[1] for point in bbox]
        left, right = min(xs), max(xs)
        top, bottom = min(ys), max(ys)
        regions.append({
            "text": text,
            "confidence": float(confidence),
            "x": left / width,
            "y": top / height,
            "width": (right - left) / width,
            "height": (bottom - top) / height,
        })

    print(json.dumps(regions, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
