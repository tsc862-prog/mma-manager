"""Flat bold WORDMARK generator for org logos (pure PIL, UFC/Bellator style).

The abbreviation IS the logo. Letters are drawn with real display fonts so they
are ALWAYS correct (no diffusion garbling), flat vector, no chrome, no mascots.
Four treatments (chosen by Tim): outlined, two_tone, big_italic, knockout.

Deterministic by (abbrev, region); a re-roll `salt` shifts style + palette so a
regenerated logo differs. Output: 512x512 transparent PNG. No GPU / no quota.
"""
import hashlib
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

F = r"C:\Windows\Fonts"
FONTS = {
    "impact":       F + r"\impact.ttf",
    "arial_blk":    F + r"\ariblk.ttf",
    "segoe_blk_it": F + r"\seguibli.ttf",
    "fg_heavy":     F + r"\FRAHV.TTF",
}
S = 512
STYLES = ["outlined", "two_tone", "big_italic", "knockout"]

# (primary, light/secondary, accent) — flat, high-contrast. Several per region
# so 331 orgs spread across many color identities.
PAL = {
    "NorthAmerica": [((198, 40, 40), (245, 245, 245), (17, 17, 17)),
                     ((13, 59, 102), (240, 240, 240), (224, 122, 30)),
                     ((17, 17, 17), (212, 175, 55), (245, 245, 245)),
                     ((120, 20, 30), (240, 240, 240), (30, 30, 30))],
    "SouthAmerica": [((0, 120, 80), (255, 214, 0), (20, 20, 20)),
                     ((0, 90, 130), (240, 90, 40), (255, 255, 255)),
                     ((20, 20, 20), (0, 155, 90), (255, 210, 0)),
                     ((0, 100, 70), (245, 245, 245), (255, 200, 0))],
    "Europe":       [((20, 24, 82), (245, 245, 245), (200, 16, 46)),
                     ((15, 15, 15), (200, 200, 208), (196, 30, 58)),
                     ((26, 71, 42), (240, 240, 240), (198, 160, 74)),
                     ((40, 40, 46), (235, 235, 240), (0, 130, 200))],
    "Asia":         [((176, 20, 28), (245, 224, 80), (20, 20, 20)),
                     ((17, 17, 20), (214, 40, 40), (240, 240, 240)),
                     ((30, 20, 60), (235, 90, 140), (245, 224, 80)),
                     ((150, 20, 24), (240, 240, 240), (245, 200, 60))],
    "Oceania":      [((0, 70, 120), (245, 245, 245), (224, 90, 40)),
                     ((20, 60, 60), (0, 168, 150), (245, 224, 80)),
                     ((30, 30, 34), (0, 130, 200), (240, 240, 240)),
                     ((0, 90, 110), (245, 245, 245), (0, 168, 150))],
    "Africa":       [((200, 90, 20), (20, 20, 20), (245, 210, 90)),
                     ((0, 110, 70), (245, 210, 90), (200, 40, 40)),
                     ((120, 40, 30), (240, 220, 180), (30, 30, 30)),
                     ((190, 70, 20), (245, 245, 245), (20, 20, 20))],
    "MiddleEast":   [((15, 60, 60), (212, 175, 55), (245, 245, 245)),
                     ((30, 40, 90), (240, 240, 240), (212, 175, 55)),
                     ((90, 20, 60), (245, 224, 120), (240, 240, 240)),
                     ((20, 55, 55), (245, 245, 245), (212, 175, 55))],
}


def _rng(seed):
    import random
    return random.Random(int(hashlib.sha1(seed.encode()).hexdigest()[:12], 16))


def _fit(font_path, text, maxw, maxh, hi=440):
    lo = 24
    while lo < hi:
        mid = (lo + hi + 1) // 2
        f = ImageFont.truetype(font_path, mid)
        b = f.getbbox(text)
        if b[2] - b[0] <= maxw and b[3] - b[1] <= maxh:
            lo = mid
        else:
            hi = mid - 1
    return ImageFont.truetype(font_path, lo)


def _text_img(text, font, fill, stroke_w=0, stroke_fill=None):
    b = font.getbbox(text, stroke_width=stroke_w)
    w, h = b[2] - b[0] + stroke_w * 2, b[3] - b[1] + stroke_w * 2
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(im).text((-b[0] + stroke_w, -b[1] + stroke_w), text, font=font,
                            fill=fill, stroke_width=stroke_w, stroke_fill=stroke_fill)
    return im


def _shear(im, k):
    """Italic shear (positive k = forward slant)."""
    w, h = im.size
    ext = int(abs(k) * h)
    canvas = Image.new("RGBA", (w + ext, h), (0, 0, 0, 0))
    canvas.alpha_composite(im, (0, 0))
    return canvas.transform((w + ext, h), Image.AFFINE, (1, -k, 0, 0, 1, 0),
                            resample=Image.BICUBIC)


def _place(canvas, im, cy=S // 2, maxw=int(S * 0.9), maxh=int(S * 0.62)):
    im = im.copy()
    if im.width > maxw or im.height > maxh:
        im.thumbnail((maxw, maxh))
    canvas.alpha_composite(im, ((S - im.width) // 2, cy - im.height // 2))


def _split_fill(text, font, ctop, cbot):
    base = _text_img(text, font, (255, 255, 255, 255))
    w, h = base.size
    grad = Image.new("RGBA", (w, h))
    gp = grad.load()
    for y in range(h):
        c = ctop if y < h * 0.52 else cbot
        for x in range(w):
            gp[x, y] = c + (255,)
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.paste(grad, (0, 0), base.split()[3])
    return out


def make(abbrev, region, path, salt=0):
    """Draw one wordmark logo. Returns the style name used."""
    r = _rng(f"{abbrev}|{region}|{salt}")
    pals = PAL.get(region, PAL["NorthAmerica"])
    c1, c2, acc = r.choice(pals)
    style = r.choice(STYLES)
    cv = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    if style == "outlined":
        f = _fit(FONTS["impact"], abbrev, int(S * 0.84), int(S * 0.52))
        t = _text_img(abbrev, f, c2 + (255,), 14, c1 + (255,))
        t = _shear(t, 0.18)
        _place(cv, t)

    elif style == "two_tone":
        f = _fit(FONTS["arial_blk"], abbrev, int(S * 0.84), int(S * 0.52))
        ol = _text_img(abbrev, f, (0, 0, 0, 0), 7, c2 + (255,))
        m = Image.new("RGBA", (S, S), (0, 0, 0, 0)); _place(m, ol); cv.alpha_composite(m)
        _place(cv, _split_fill(abbrev, f, c1, acc))

    elif style == "big_italic":
        f = _fit(FONTS["segoe_blk_it"], abbrev, int(S * 0.88), int(S * 0.6))
        t = _text_img(abbrev, f, c1 + (255,), 8, c2 + (255,))
        _place(cv, t)

    else:  # knockout — letters reversed out on an angled solid block
        f = _fit(FONTS["segoe_blk_it"], abbrev, int(S * 0.74), int(S * 0.42))
        t = _text_img(abbrev, f, c2 + (255,))
        bar_w, bar_h = int(t.width * 1.24), int(t.height * 1.55)
        bar = Image.new("RGBA", (bar_w, bar_h), (0, 0, 0, 0))
        ImageDraw.Draw(bar).polygon(
            [(int(bar_w * 0.05), 0), (bar_w, 0), (int(bar_w * 0.95), bar_h), (0, bar_h)],
            fill=c1 + (255,))
        bar.alpha_composite(t, ((bar_w - t.width) // 2, (bar_h - t.height) // 2))
        _place(cv, bar)

    cv.save(path)
    return style


if __name__ == "__main__":
    import json, sys
    HERE = Path(__file__).resolve().parent
    names = json.loads((HERE / "names.json").read_text(encoding="utf-8-sig"))
    OUT = HERE / "logos"; OUT.mkdir(exist_ok=True)
    n = 0
    for region, lst in names.items():
        for e in lst:
            make(e["abbrev"], region, OUT / f"{e['abbrev']}.png")
            n += 1
    print(f"generated {n} wordmark logos -> {OUT}")
