"""
Turn a ChatGPT age-progression triptych (one man at 3 ages, left->right) into three
transparent, square roster portraits for the WPF FaceLibrary.

Usage:
    python tools/facegen.py "GPT IMGS/ChatGPT Image ....png" white_02
    python tools/facegen.py "<triptych>" white_02_206_x            # weight-locked (206lbs+)
    python tools/facegen.py "<triptych>" black_01_206_x u2net_human_seg   # better matting for bare torsos

Writes  Wpf/Assets/Faces/<name>_y.png  (young 18-26)
        Wpf/Assets/Faces/<name>_p.png  (prime 27-37)
        Wpf/Assets/Faces/<name>_v.png  (veteran 38-50)

<name> is {eth}_{id} for a general face, or {eth}_{id}_{min}_{max} to weight-lock it
(min/max in lbs, or 'x' for open-ended). Prefix with f_ for a female face (e.g. f_white_00);
no prefix = male. Optional 3rd arg picks the rembg model (default u2net; u2net_human_seg
segments shirtless bodies better).

Pipeline per panel: split into equal thirds -> rembg background cutout ->
crop head+shoulders -> pad sides transparent to a square -> resize 256x256 PNG.

Deps:  pip install pillow rembg onnxruntime
"""
import io
import os
import sys

from PIL import Image
from rembg import remove, new_session

STAGES = ["y", "p", "v"]
OUT_DIR = os.path.join("Wpf", "Assets", "Faces")
SIZE = 256


def process(src_path: str, name: str, model: str = "u2net") -> None:
    session = new_session(model)
    img = Image.open(src_path).convert("RGB")
    W, H = img.size
    tw = W // 3                      # panel width (3 equal panels)
    region_h = int(tw * 1.6)         # taller-than-wide crop keeps head + shoulders

    for i, stage in enumerate(STAGES):
        panel = img.crop((i * tw, 0, (i + 1) * tw, H))
        buf = io.BytesIO()
        panel.save(buf, "PNG")
        rgba = Image.open(io.BytesIO(remove(buf.getvalue(), session=session))).convert("RGBA")

        top = (rgba.split()[3].getbbox() or (0, 0, tw, H))[1]   # top of subject
        y0 = max(0, top - 35)
        if y0 + region_h > H:
            y0 = H - region_h
        strip = rgba.crop((0, y0, tw, y0 + region_h))

        square = Image.new("RGBA", (region_h, region_h), (0, 0, 0, 0))
        square.paste(strip, ((region_h - tw) // 2, 0))          # center horizontally
        out = os.path.join(OUT_DIR, f"{name}_{stage}.png")
        square.resize((SIZE, SIZE), Image.LANCZOS).save(out, "PNG")
        print(f"  {out}  ({os.path.getsize(out)} bytes)")


if __name__ == "__main__":
    if len(sys.argv) not in (3, 4):
        sys.exit('usage: python tools/facegen.py "<triptych.png>" <name e.g. white_02> [rembg_model]')
    src, name = sys.argv[1], sys.argv[2]
    model = sys.argv[3] if len(sys.argv) == 4 else "u2net"
    if not os.path.isfile(src):
        sys.exit(f"not found: {src}")
    print(f"{name} ({model}):")
    process(src, name, model)
    print("done")
