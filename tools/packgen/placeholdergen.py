"""Grey placeholder portraits for anonymous LOCAL fighters (Fighter.LocalTalent).

One FACE bust (512x512, grey, MMA rashguard) + one full BODY figure (tall
transparent cutout, fight shorts/gloves) per weight class x sex. Generated via
HF Z-Image, then FORCED TO GRAYSCALE (kills the peachy/yellow skin the model
sometimes adds) and keyed for the body cutout. Retries the space's stochastic
'maybe not safe' refusal.

Output -> Wpf/Assets/Placeholders/{sex}_{class}_{face|body}.png
Consumed by Wpf/Art/PlaceholderLibrary.cs (keyed by WeightClass+Sex).
Style/decisions: see memory [[fighter-portraits]] / the grey-avatar reference.

Usage:  python tools/packgen/placeholdergen.py [--face-only|--body-only]
"""
import sys
from collections import deque
from pathlib import Path
import zclient
from krea5 import retry
from PIL import Image, ImageStat, ImageOps

OUT = Path(__file__).resolve().parent.parent.parent / "Wpf" / "Assets" / "Placeholders"
OUT.mkdir(parents=True, exist_ok=True)

CLASSES = [
    ("strawweight",     "a slim slight, narrow shoulders and a lean face"),
    ("flyweight",       "a slim, narrow shoulders and a lean face"),
    ("bantamweight",    "a lean, slim shoulders"),
    ("featherweight",   "a lean athletic build"),
    ("lightweight",     "an athletic build"),
    ("welterweight",    "a solid athletic build, broad shoulders"),
    ("middleweight",    "a muscular build, broad shoulders and a strong neck"),
    ("lightheavyweight","a big broad-shouldered muscular build, thick neck"),
    ("heavyweight",     "a large heavy build, very broad shoulders, thick neck and a full round face"),
]

FACE_P = ("a plain flat default profile placeholder avatar icon, front view head "
          "and shoulders bust of {sex} generic anonymous MMA fighter with {build}, "
          "a smooth FEATURELESS blank face with no eyes no nose no mouth, simple "
          "short neat hair, wearing a fitted athletic MMA rashguard fight top, "
          "flat solid grey tones with soft smooth shading, clean modern "
          "minimalist vector illustration, plain pure white background, centered, "
          "no text")

BODY_P = ("a plain flat minimalist vector illustration of {sex} generic anonymous "
          "MMA fighter with {build}, FULL BODY from head to feet standing straight "
          "facing forward with arms relaxed at the sides, a smooth FEATURELESS "
          "blank face with no features, simple short hair, wearing a fitted MMA "
          "rashguard top, MMA fight shorts and MMA gloves, flat solid light grey "
          "and medium grey tones with soft smooth shading, plain pure white "
          "background, centered, whole figure visible, no text")


def to_grey_rgb(im):
    g = ImageOps.grayscale(im.convert("RGB"))
    return Image.merge("RGB", (g, g, g))


def to_grey_rgba(rgba):
    a = rgba.split()[3]
    out = to_grey_rgb(rgba.convert("RGB")).convert("RGBA")
    out.putalpha(a)
    return out


def key_white(im, tol=32):
    im = im.convert("RGBA"); w, h = im.size; px = im.load()
    seen = bytearray(w * h); dq = deque([(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)])
    while dq:
        x, y = dq.popleft()
        if x < 0 or y < 0 or x >= w or y >= h:
            continue
        i = y * w + x
        if seen[i]:
            continue
        r, g, b, a = px[x, y]
        if r >= 255 - tol and g >= 255 - tol and b >= 255 - tol:
            seen[i] = 1; px[x, y] = (r, g, b, 0)
            dq.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    return im


def not_refused(im):
    return ImageStat.Stat(im.convert("L")).mean[0] > 150   # grey ~200; chalkboard ~60


def opaque_frac(rgba):
    h = rgba.split()[3].histogram()
    return sum(h[16:]) / float(rgba.width * rgba.height)


def gen_face(z, sk, sex_word, cname, build):
    p = FACE_P.format(sex=sex_word, build=build)
    for a in range(6):
        im = Image.open(retry(lambda: zclient.generate(z, p, 512, 512, steps=9))).convert("RGB")
        if not_refused(im):
            to_grey_rgb(im).resize((512, 512), Image.LANCZOS).save(OUT / f"{sk}_{cname}_face.png")
            print(f"OK face {sk}_{cname} (try {a + 1})", flush=True); return True
        print(f".. face {sk}_{cname} refused (try {a + 1})", flush=True)
    print(f"FAIL face {sk}_{cname}", flush=True); return False


def gen_body(z, sk, sex_word, cname, build):
    p = BODY_P.format(sex=sex_word, build=build)
    for a in range(8):
        raw = Image.open(retry(lambda: zclient.generate(z, p, 640, 1280, steps=9))).convert("RGB")
        if not not_refused(raw):
            print(f".. body {sk}_{cname} refused (try {a + 1})", flush=True); continue
        keyed = key_white(raw)
        if 0.06 <= opaque_frac(keyed) <= 0.5:
            grey = to_grey_rgba(keyed)
            bb = grey.getbbox(); fig = grey.crop(bb) if bb else grey
            W, H = 328, 920; fig.thumbnail((W - 12, H - 12))
            cv = Image.new("RGBA", (W, H), (0, 0, 0, 0))
            cv.alpha_composite(fig, ((W - fig.width) // 2, (H - fig.height) // 2))
            cv.save(OUT / f"{sk}_{cname}_body.png")
            print(f"OK body {sk}_{cname} (try {a + 1})", flush=True); return True
        print(f".. body {sk}_{cname} odd-crop (try {a + 1})", flush=True)
    print(f"FAIL body {sk}_{cname}", flush=True); return False


def main():
    face_only = "--face-only" in sys.argv
    body_only = "--body-only" in sys.argv
    z = zclient.make_client()
    n = 0
    for sk, sw in (("m", "a male"), ("f", "a female")):
        for cname, build in CLASSES:
            if not body_only and gen_face(z, sk, sw, cname, build):
                n += 1
            if not face_only and gen_body(z, sk, sw, cname, build):
                n += 1
    print(f"PLACEHOLDERS DONE: {n}", flush=True)


if __name__ == "__main__":
    main()
