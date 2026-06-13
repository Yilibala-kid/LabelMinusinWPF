"""Persistent PaddleOCR pipeline process.

stdin:  {"image": "path", "detect_only": false}
stdout: {"regions": [{"text": "...", "score": 0.9, "box": [x, y, w, h]}]}
"""
import json
import os
import sys
import inspect
import threading

import numpy as np
from PIL import Image

try:
    import cv2
except Exception:
    cv2 = None

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

os.environ.setdefault("FLAGS_allocator_strategy", "auto_growth")
os.environ.setdefault("FLAGS_use_mkldnn", "0")
os.environ.setdefault("FLAGS_enable_mkldnn", "0")
os.environ.setdefault("FLAGS_enable_pir_api", "0")
os.environ.setdefault("PADDLEOCR_HOME", os.path.join(os.path.dirname(__file__), "..", "v6"))
os.environ.setdefault("PADDLE_PDX_CACHE_HOME", os.path.join(os.path.dirname(__file__), "..", "v6"))

from paddleocr import TextDetection, TextRecognition


def _require_model_dir(name):
    model_root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "v6"))
    candidates = [
        os.path.join(model_root, "official_models", name),
        os.path.join(model_root, name),
        os.path.join(os.path.expanduser("~"), ".paddlex", "official_models", name),
    ]
    for path in candidates:
        if os.path.isdir(path):
            return path
    raise RuntimeError(
        "Missing PP-OCRv6 model directory. Checked: " + "; ".join(candidates)
    )


def _plain(value):
    if hasattr(value, "tolist"):
        return value.tolist()
    if isinstance(value, dict):
        return {key: _plain(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [_plain(item) for item in value]
    return value


def _result_dict(result):
    data = getattr(result, "json", None)
    if callable(data):
        data = data()
    if data is None and hasattr(result, "to_dict"):
        data = result.to_dict()
    if data is None and isinstance(result, dict):
        data = result

    data = _plain(data or {})
    if isinstance(data, dict) and "res" in data:
        return data["res"]
    return data


def _rect_from_box(box):
    box = _plain(box)
    if not box:
        return [0, 0, 0, 0]

    if len(box) == 4 and all(isinstance(item, (int, float)) for item in box):
        x1, y1, x2, y2 = box
        return [float(x1), float(y1), max(1.0, float(x2) - float(x1)), max(1.0, float(y2) - float(y1))]

    points = []
    for point in box:
        if isinstance(point, (list, tuple)) and len(point) >= 2:
            points.append((float(point[0]), float(point[1])))

    if not points:
        return [0, 0, 0, 0]

    xs = [point[0] for point in points]
    ys = [point[1] for point in points]
    left, top, right, bottom = min(xs), min(ys), max(xs), max(ys)
    return [left, top, max(1.0, right - left), max(1.0, bottom - top)]


def _poly_from_box(box):
    box = _plain(box)
    if not box:
        return None

    points = []
    for point in box:
        if isinstance(point, (list, tuple)) and len(point) >= 2:
            points.append([float(point[0]), float(point[1])])

    return points[:4] if len(points) >= 4 else None


def _extract_detection_regions(results):
    regions = []

    for result in results:
        data = _result_dict(result)
        scores = data.get("dt_scores") or data.get("det_scores") or data.get("scores") or []
        boxes = data.get("dt_polys") or data.get("dt_boxes") or data.get("boxes") or []

        for index, box in enumerate(boxes):
            score = scores[index] if index < len(scores) else 1.0
            regions.append(
                {
                    "text": "",
                    "score": float(score),
                    "box": _rect_from_box(box),
                    "poly": _poly_from_box(box),
                }
            )

    return regions


def _crop_rect(box, width, height, padding=2):
    x, y, w, h = box
    left = max(0, int(x) - padding)
    top = max(0, int(y) - padding)
    right = min(width, int(x + w) + padding)
    bottom = min(height, int(y + h) + padding)
    if right <= left or bottom <= top:
        return None
    return left, top, right, bottom


def _crop_poly(full_image, poly):
    if cv2 is None or not poly or len(poly) < 4:
        return None

    points = np.array(poly[:4], dtype=np.float32)
    crop_width = int(
        max(
            np.linalg.norm(points[0] - points[1]),
            np.linalg.norm(points[2] - points[3]),
        )
    )
    crop_height = int(
        max(
            np.linalg.norm(points[0] - points[3]),
            np.linalg.norm(points[1] - points[2]),
        )
    )
    if crop_width <= 0 or crop_height <= 0:
        return None

    target = np.array(
        [
            [0, 0],
            [crop_width, 0],
            [crop_width, crop_height],
            [0, crop_height],
        ],
        dtype=np.float32,
    )
    matrix = cv2.getPerspectiveTransform(points, target)
    crop = cv2.warpPerspective(
        np.array(full_image),
        matrix,
        (crop_width, crop_height),
        flags=cv2.INTER_CUBIC,
        borderMode=cv2.BORDER_REPLICATE,
    )
    if crop_height / max(1, crop_width) >= 1.5:
        crop = np.rot90(crop)
    return np.ascontiguousarray(crop)


def _recognition_text(result):
    data = _result_dict(result)
    text = data.get("rec_text") or data.get("text") or ""
    score = data.get("rec_score") or data.get("score") or 0.0
    return str(text or ""), float(score or 0.0)


def _predict_text(recognizer, image):
    rec_results = recognizer.predict(image if isinstance(image, np.ndarray) else np.array(image))
    if not rec_results:
        return "", 0.0
    return _recognition_text(rec_results[0])


def _recognize_regions(image_path, regions, recognizer):
    recognized = []

    with Image.open(image_path) as image:
        full = image.convert("RGB")
        width, height = full.size

        for region in regions:
            crop = _crop_poly(full, region.get("poly"))
            if crop is None:
                crop_rect = _crop_rect(region["box"], width, height)
                if crop_rect is None:
                    continue
                crop = full.crop(crop_rect)
            text, rec_score = _predict_text(recognizer, crop)

            if not text.strip():
                continue

            recognized.append(
                {
                    "text": text,
                    "score": min(float(region["score"]), rec_score),
                    "box": region["box"],
                }
            )

    return recognized


def _create_detector():
    kwargs = {
        "model_name": "PP-OCRv6_medium_det",
        "model_dir": _require_model_dir("PP-OCRv6_medium_det"),
        "device": "cpu",
        "enable_mkldnn": False,
    }

    signature = inspect.signature(TextDetection)
    if not any(param.kind == inspect.Parameter.VAR_KEYWORD for param in signature.parameters.values()):
        kwargs = {key: value for key, value in kwargs.items() if key in signature.parameters}

    return TextDetection(**kwargs)


def _create_recognizer():
    kwargs = {
        "model_name": "PP-OCRv6_medium_rec",
        "model_dir": _require_model_dir("PP-OCRv6_medium_rec"),
        "device": "cpu",
        "enable_mkldnn": False,
    }

    signature = inspect.signature(TextRecognition)
    if not any(param.kind == inspect.Parameter.VAR_KEYWORD for param in signature.parameters.values()):
        kwargs = {key: value for key, value in kwargs.items() if key in signature.parameters}

    return TextRecognition(**kwargs)


sys.stderr.write("paddle-ocr loading\n")
sys.stderr.flush()
detector = _create_detector()
recognizer = None
recognizer_error = None
recognizer_ready = threading.Event()


def _warmup_recognizer():
    global recognizer, recognizer_error
    try:
        recognizer = _create_recognizer()
    except Exception as exc:
        recognizer_error = exc
    finally:
        recognizer_ready.set()


threading.Thread(target=_warmup_recognizer, daemon=True).start()
sys.stderr.write("paddle-ocr ready\n")
sys.stderr.flush()

for line in sys.stdin:
    line = line.strip()
    if not line:
        continue

    try:
        req = json.loads(line)
        image = req["image"]
        detect_only = bool(req.get("detect_only", False))
        results = detector.predict(image)
        regions = _extract_detection_regions(results)
        if detect_only:
            output_regions = regions
        else:
            recognizer_ready.wait()
            if recognizer_error is not None:
                raise recognizer_error
            output_regions = _recognize_regions(image, regions, recognizer)
        sys.stdout.write(json.dumps({"regions": output_regions, "error": None}, ensure_ascii=True) + "\n")
        sys.stdout.flush()
    except Exception as exc:
        sys.stdout.write(
            json.dumps({"regions": [], "error": str(exc)}, ensure_ascii=True)
            + "\n"
        )
        sys.stdout.flush()
