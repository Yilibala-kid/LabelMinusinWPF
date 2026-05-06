"""manga-ocr 持久推理进程 — stdin JSON 请求，stdout JSON 响应"""
import sys, json, os

model_dir = os.path.join(os.path.dirname(__file__), "model")

from PIL import Image
from manga_ocr import MangaOcr

sys.stderr.write("manga-ocr 加载中...\n")
sys.stderr.flush()
mocr = MangaOcr(pretrained_model_name_or_path=model_dir)
sys.stderr.write("manga-ocr 就绪\n")
sys.stderr.flush()

for line in sys.stdin:
    line = line.strip()
    if not line:
        continue
    try:
        req = json.loads(line)
        full = Image.open(req["image"]).convert("RGB")
        texts = []
        for x, y, w, h in req["boxes"]:
            if w <= 0 or h <= 0:
                texts.append("")
                continue
            try:
                texts.append(mocr(full.crop((x, y, x + w, y + h))))
            except Exception:
                texts.append("")

        sys.stdout.write(json.dumps({"texts": texts}, ensure_ascii=False) + "\n")
        sys.stdout.flush()
    except Exception as e:
        sys.stdout.write(json.dumps({"texts": [], "error": str(e)}, ensure_ascii=False) + "\n")
        sys.stdout.flush()
