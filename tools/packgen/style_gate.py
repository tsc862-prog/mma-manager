"""Cel-shading flatness score: anime renders concentrate pixels into few flat
colors; painterly/photoreal renders spread across gradients. Score = fraction
of opaque pixels covered by the 48 most common quantized colors.

Usage: style_gate.py report   -> score every fighter in both packs (calibration)
"""
import sys
from collections import Counter
from pathlib import Path
from PIL import Image

def flatness(png):
    im = Image.open(png).convert("RGBA")
    im.thumbnail((160, 160))
    px = list(im.getdata())
    opaque = [(r // 12, g // 12, b // 12) for r, g, b, a in px if a > 200]
    if len(opaque) < 500:
        return None
    counts = Counter(opaque)
    top = sum(n for _, n in counts.most_common(48))
    return top / len(opaque)

def pack_scores(pack):
    out = {}
    for p in sorted(Path(pack).glob("*_p_body.png")):
        s = flatness(p)
        if s is not None:
            out[p.stem[:-7]] = s
    return out

if __name__ == "__main__":
    anime = pack_scores(r"e:\MMA Manager\generated-pack-anime")
    painted = pack_scores(r"e:\MMA Manager\generated-pack")
    print("=== painted pack (reference for NON-anime look) ===")
    ps = sorted(painted.values())
    print(f"n={len(ps)} min={ps[0]:.3f} median={ps[len(ps)//2]:.3f} max={ps[-1]:.3f}")
    print("=== anime pack, sorted (lowest = least anime) ===")
    for base, s in sorted(anime.items(), key=lambda kv: kv[1]):
        print(f"{s:.3f}  {base}")
