"""Procedural org-logo generator (vector-style, PIL).

Logos are DRAWN, not diffusion-generated: real fonts make the initials correct,
flat geometry gives a true logo look, it's deterministic and free (no GPU), and
it matches the game's existing flat-vector art (CharacterFactory/FlatArt).

Each logo = regional color palette + emblem device (shield/circle/etc.) +
a simple central motif + the org's abbreviation in a bold display face.
Deterministic by org name, so a given promotion always gets the same logo.

Usage:  python tools/orgbrand/orglogos.py [--sample]   (sample = a few demos)
Output: tools/orgbrand/logos/<abbrev>.png  (512x512, transparent)
"""
import hashlib
import json
import math
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
OUT = HERE / "logos"
FONT_BOLD = r"C:\Windows\Fonts\ariblk.ttf"      # Arial Black — clean, heavy
FONT_IMPACT = r"C:\Windows\Fonts\impact.ttf"

# Regional palettes: (primary, secondary, accent). Flat, high-contrast.
PALETTES = {
    "NorthAmerica": [((22, 35, 74), (198, 40, 40), (245, 245, 245)),
                     ((17, 17, 17), (212, 175, 55), (245, 245, 245)),
                     ((13, 59, 102), (240, 240, 240), (224, 122, 30))],
    "SouthAmerica": [((0, 102, 68), (255, 214, 0), (255, 255, 255)),
                     ((0, 90, 130), (240, 90, 40), (255, 255, 255)),
                     ((20, 20, 20), (0, 155, 90), (255, 210, 0))],
    "Europe": [((20, 24, 82), (200, 16, 46), (255, 255, 255)),
               ((15, 15, 15), (176, 176, 184), (196, 30, 58)),
               ((26, 71, 42), (240, 240, 240), (198, 160, 74))],
    "Asia": [((150, 20, 28), (245, 224, 80), (20, 20, 20)),
             ((17, 17, 20), (214, 40, 40), (240, 240, 240)),
             ((40, 20, 70), (235, 90, 140), (245, 224, 80))],
    "Oceania": [((0, 70, 120), (255, 255, 255), (224, 90, 40)),
                ((20, 60, 60), (0, 168, 150), (245, 224, 80)),
                ((30, 30, 34), (0, 130, 200), (240, 240, 240))],
    "Africa": [((200, 90, 20), (20, 20, 20), (245, 210, 90)),
               ((0, 110, 70), (245, 210, 90), (200, 40, 40)),
               ((120, 40, 30), (240, 220, 180), (30, 30, 30))],
    "MiddleEast": [((15, 60, 60), (212, 175, 55), (245, 245, 245)),
                   ((30, 40, 90), (240, 240, 240), (212, 175, 55)),
                   ((90, 20, 60), (245, 224, 120), (240, 240, 240))],
}

# Emblem outer shapes.
SHAPES = ["shield", "circle", "hexagon", "diamond", "rounded"]
# Central motifs (simple, symbolic — drawn as geometry, not fetched art).
MOTIFS = ["fist", "star", "chevrons", "wings", "cross-bars", "flame", "mountain"]


def _rng(seed_str):
    import random
    return random.Random(int(hashlib.sha1(seed_str.encode()).hexdigest()[:12], 16))


def _outer(draw, shape, box, fill, outline, w):
    x0, y0, x1, y1 = box
    if shape == "circle":
        draw.ellipse(box, fill=fill, outline=outline, width=w)
    elif shape == "rounded":
        draw.rounded_rectangle(box, radius=(x1 - x0) // 5, fill=fill, outline=outline, width=w)
    elif shape == "hexagon":
        cx, cy, r = (x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2
        pts = [(cx + r * math.cos(math.radians(a - 90)), cy + r * math.sin(math.radians(a - 90)))
               for a in range(0, 360, 60)]
        draw.polygon(pts, fill=fill, outline=outline, width=w)
    elif shape == "diamond":
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        draw.polygon([(cx, y0), (x1, cy), (cx, y1), (x0, cy)], fill=fill, outline=outline, width=w)
    else:  # shield
        cx = (x0 + x1) / 2
        pts = [(x0, y0), (x1, y0), (x1, y0 + (y1 - y0) * 0.55),
               (cx, y1), (x0, y0 + (y1 - y0) * 0.55)]
        draw.polygon(pts, fill=fill, outline=outline, width=w)


def _motif(draw, motif, cx, cy, r, color):
    if motif == "star":
        pts = []
        for i in range(10):
            rr = r if i % 2 == 0 else r * 0.45
            a = math.radians(i * 36 - 90)
            pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
        draw.polygon(pts, fill=color)
    elif motif == "chevrons":
        for k in range(3):
            off = k * r * 0.42
            draw.line([(cx - r, cy + off + r * 0.3), (cx, cy + off - r * 0.2),
                       (cx + r, cy + off + r * 0.3)], fill=color, width=max(6, int(r * 0.16)), joint="curve")
    elif motif == "cross-bars":
        draw.rectangle([cx - r, cy - r * 0.22, cx + r, cy + r * 0.22], fill=color)
        draw.rectangle([cx - r * 0.22, cy - r, cx + r * 0.22, cy + r], fill=color)
    elif motif == "mountain":
        draw.polygon([(cx - r, cy + r * 0.7), (cx - r * 0.3, cy - r * 0.5),
                      (cx + r * 0.1, cy + r * 0.1), (cx + r * 0.5, cy - r * 0.7),
                      (cx + r, cy + r * 0.7)], fill=color)
    elif motif == "flame":
        draw.polygon([(cx, cy - r), (cx + r * 0.6, cy + r * 0.2), (cx + r * 0.3, cy + r * 0.8),
                      (cx - r * 0.3, cy + r * 0.8), (cx - r * 0.6, cy + r * 0.2)], fill=color)
    elif motif == "wings":
        for s in (-1, 1):
            draw.polygon([(cx, cy - r * 0.3), (cx + s * r, cy - r * 0.6),
                          (cx + s * r * 0.9, cy), (cx + s * r * 0.4, cy + r * 0.1)], fill=color)
    elif motif == "fist":
        draw.rounded_rectangle([cx - r * 0.6, cy - r * 0.5, cx + r * 0.6, cy + r * 0.6],
                               radius=int(r * 0.25), fill=color)
        for k in range(4):
            fx = cx - r * 0.6 + k * r * 0.4
            draw.rounded_rectangle([fx, cy - r * 0.75, fx + r * 0.3, cy - r * 0.35],
                                   radius=int(r * 0.12), fill=color)
    else:  # generic bars
        draw.rectangle([cx - r, cy - r * 0.3, cx + r, cy + r * 0.3], fill=color)


def make_logo(name, abbrev, region, path):
    rng = _rng(name)
    pal = rng.choice(PALETTES[region])
    primary, secondary, accent = pal
    shape = rng.choice(SHAPES)
    motif = rng.choice(MOTIFS)

    S = 512
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = 46
    _outer(d, shape, (pad, pad, S - pad, S - pad), primary, accent, 10)

    # motif in the upper-center
    _motif(d, motif, S / 2, S * 0.42, S * 0.19, secondary)

    # abbreviation, bold, lower band
    try:
        font = ImageFont.truetype(FONT_IMPACT, 118 if len(abbrev) <= 3 else 92)
    except OSError:
        font = ImageFont.truetype(FONT_BOLD, 96)
    tb = d.textbbox((0, 0), abbrev, font=font)
    tw, th = tb[2] - tb[0], tb[3] - tb[1]
    d.text(((S - tw) / 2 - tb[0], S * 0.66 - tb[1]), abbrev, font=font, fill=accent,
           stroke_width=4, stroke_fill=secondary)
    img.save(path)


def main():
    OUT.mkdir(exist_ok=True)
    names = json.loads((HERE / "names.json").read_text(encoding="utf-8-sig"))
    if "--sample" in sys.argv:
        picks = [("NorthAmerica", 0), ("SouthAmerica", 1), ("Asia", 2),
                 ("Africa", 4), ("MiddleEast", 0), ("Europe", 3)]
        for region, i in picks:
            e = names[region][i]
            make_logo(e["name"], e["abbrev"], region, OUT / f"sample_{e['abbrev']}.png")
            print(f"sample: {e['name']} [{e['abbrev']}] ({region})")
        return
    n = 0
    for region, lst in names.items():
        for e in lst:
            make_logo(e["name"], e["abbrev"], region, OUT / f"{e['abbrev']}.png")
            n += 1
    print(f"generated {n} logos -> {OUT}")


if __name__ == "__main__":
    main()
