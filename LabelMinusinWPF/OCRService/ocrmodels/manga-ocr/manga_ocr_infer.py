"""manga-ocr 持久推理进程 — stdin JSON 请求，stdout JSON 响应"""
import sys, json, os

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

model_dir = os.path.join(os.path.dirname(__file__), "model")

from PIL import Image
from manga_ocr import MangaOcr


def _crop_box(box, width, height):
    x, y, w, h = box
    left = max(0, int(x))
    top = max(0, int(y))
    right = min(width, int(x + w))
    bottom = min(height, int(y + h))
    if right <= left or bottom <= top:
        return None
    return left, top, right, bottom

sys.stderr.write("manga-ocr loading\n")
sys.stderr.flush()
mocr = MangaOcr(pretrained_model_name_or_path=model_dir)
sys.stderr.write("manga-ocr ready\n")
sys.stderr.flush()

for line in sys.stdin:
    line = line.strip()
    if not line:
        continue
    try:
        req = json.loads(line)
        texts = []
        with Image.open(req["image"]) as image:
            full = image.convert("RGB")
            width, height = full.size

            for box in req["boxes"]:
                crop_box = _crop_box(box, width, height)
                if crop_box is None:
                    texts.append("")
                    continue
                try:
                    texts.append(mocr(full.crop(crop_box)))
                except Exception:
                    texts.append("")

        sys.stdout.write(json.dumps({"texts": texts}, ensure_ascii=True) + "\n")
        sys.stdout.flush()
    except Exception as e:
        sys.stdout.write(json.dumps({"texts": [], "error": str(e)}, ensure_ascii=True) + "\n")
        sys.stdout.flush()
