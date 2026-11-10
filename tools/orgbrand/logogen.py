"""Diffusion org-logo pipeline.

Generates a flat-vector emblem logo per promotion via Z-Image (the bake-off
proved it produces clean sports-logo emblems AND legible short acronyms), then
keys the white studio background to transparent so the logo floats on any card.

Each logo = regional imagery motif + regional color palette + emblem shape,
with the abbreviation baked in for short ones (<=4 chars, which render clean)
and left text-free for longer ones (the game overlays the real name anyway).
Deterministic by org name -> a promotion always gets the same logo.

Metadata -> tools/orgbrand/logos/manifest.json  (abbrev -> {name,region,motif,file})
Images   -> tools/orgbrand/logos/<abbrev>.png    (512x512, transparent)

Usage:
  python logogen.py --sample N     # N logos spread across all regions (review)
  python logogen.py                # every org in names.json (resumable)
"""
import json
import sys
from collections import deque
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "packgen"))
from PIL import Image
from gradio_client import Client
from batchgen import TOKEN
from krea5 import retry, img_path
import wordmarks   # org logos are flat PIL wordmarks (no diffusion); team = FLUX/Z-Image

HERE = Path(__file__).resolve().parent
OUT = HERE / "logos"
MANIFEST = OUT / "manifest.json"

# Combat / championship iconography — real fight promotions are typographic
# with minimal hard iconography (an octagon, a fist, angular marks), NOT animal
# mascots (reviewer: "animals read like a school logo"). Shared across regions;
# fight branding isn't regional (regional flavor lives in the color palette).
# Dropped the ambiguous ones (reviewer): "championship belt" -> read as a UFO;
# "octagon cage silhouette" crosshatch -> messy netting; "slash marks" ->
# "weird formatting". Kept clean, legible combat icons.
ORG_ICONS = [
    "a clenched raised fist", "two crossed fists", "crossed MMA gloves",
    "a bold sharp lightning bolt", "a stylized flame", "a bold five-point star",
    "bold angular wings", "a sharp geometric arrowhead", "a fist inside an octagon",
    "a laurel wreath around a star", "a bold letter monogram",
]

# Fight TEAM / gym / camp iconography — a different visual language from
# promotions: martial-arts imagery and fierce MASCOT animals ARE right here
# (Team Alpha Male's gorilla, Tiger Muay Thai), which would read "school" on a
# promotion but reads "gym" on a team.
TEAM_ICONS = [
    "a snarling tiger head", "a fierce gorilla head", "a snarling wolf head",
    "a cobra ready to strike", "a roaring lion head", "a snarling pitbull head",
    "a clenched fist", "two crossed fists", "a muay thai fighter silhouette",
    "a brazilian jiu-jitsu belt knot", "crossed muay thai shin guards",
    "a spartan warrior helmet", "a scorpion", "a fierce ram head",
    "a bold letter monogram",
]
ICONS = ORG_ICONS      # active set — swapped to TEAM_ICONS in --team mode

# Regional palettes (primary, secondary) — named for the prompt.
PALETTES = {
    # Signature tint per region, but each region ALSO draws from the shared
    # diverse pool below — so no region is monochrome. (fill, accent): fill is
    # always saturated/dark so the white-bg key never eats it; accent may be
    # light. Reviewer: "too much of the same color".
    "NorthAmerica": [("deep red", "white"), ("navy blue", "gold"), ("black", "silver"),
                     ("steel blue", "orange")],
    "SouthAmerica": [("emerald green", "yellow"), ("royal blue", "orange"),
                     ("black", "lime green"), ("crimson", "white")],
    "Europe": [("navy blue", "silver"), ("burgundy", "cream"), ("royal purple", "gold"),
               ("forest green", "white")],
    "Asia": [("crimson red", "gold"), ("black", "red"), ("deep purple", "gold"),
             ("jade green", "cream")],
    "Oceania": [("ocean blue", "white"), ("teal", "orange"), ("navy", "sky blue"),
                ("charcoal", "aqua")],
    "Africa": [("burnt orange", "black"), ("deep green", "gold"),
               ("terracotta red", "cream"), ("indigo", "amber")],
    "MiddleEast": [("dark teal", "gold"), ("maroon", "gold"),
                   ("sand gold", "black"), ("emerald", "cream")],
}

# Shared diverse palettes every region can roll (breaks the "dark + gold" rut).
SHARED_PALETTES = [
    ("deep red", "white"), ("royal blue", "white"), ("black", "white"),
    ("forest green", "white"), ("black", "red"), ("orange", "navy"),
    ("crimson", "black"), ("purple", "white"), ("teal", "orange"),
    ("navy", "red"), ("charcoal", "yellow"), ("maroon", "cream"),
    ("cobalt blue", "orange"), ("wine red", "gold"), ("slate gray", "red"),
    ("olive green", "cream"), ("burnt orange", "white"), ("indigo", "cyan"),
]

# Clean solid container shapes (no crosshatch cages, no ribbons). The octagon
# reads as MMA when it's a clean solid shape, not a netted cage.
SHAPES = ["a rounded rectangle badge", "a shield", "a circle", "a hexagon",
          "an octagon", "a rounded square badge"]

ORG_STYLES = ["modern minimalist tech branding", "sleek contemporary sports brand",
              "bold flat geometric design", "clean modern fight-league branding",
              "sharp minimal monogram style", "premium modern esports branding"]
TEAM_STYLES = ["rugged fight-gym crest style", "bold martial-arts team badge style",
               "aggressive combat-team mascot style", "vintage boxing-gym style",
               "hard-nosed training-camp style", "bold athletic team style"]
STYLES = ORG_STYLES    # active set — swapped to TEAM_STYLES in --team mode

# Per-kind config: output folder, names file, prompt noun, aesthetic, type.
KINDS = {
    # ORG uses the FLUX logo model (distinctive designed emblems/wordmarks —
    # reviewer approved). TEAM keeps Z-Image gym badges.
    "org":  dict(subdir="logos", names="names.json", type="org",
                 model="fantaxy/ofai-flx-logo", endpoint="/infer",
                 size=1024, steps=8),
    "team": dict(subdir="team-logos", names="team-names.json", type="team",
                 model="mrfakename/Z-Image-Turbo", endpoint="/generate_image",
                 size=768, steps=9,
                 noun="a fight team / MMA gym / martial-arts academy team crest, "
                      "a training camp logo",
                 aesthetic="bold rugged athletic gym-team style"),
}
KIND = "org"           # set from argv in main()

# EVERY logo is a SOLID CONTAINED badge (reviewer: bare wordmarks/marks floating
# on transparency read as "bad transparency"). Org leans clean monogram/letters
# (modern); team keeps balanced icon+letters.
ORG_LAYOUTS = [("lettermark", 55), ("icon", 30), ("emblem", 15)]
TEAM_LAYOUTS = [("lettermark", 42), ("emblem", 34), ("icon", 24)]
LAYOUTS = ORG_LAYOUTS    # swapped to TEAM_LAYOUTS in --team mode


def _rng(seed_str):
    import hashlib, random
    return random.Random(int(hashlib.sha1(seed_str.encode()).hexdigest()[:12], 16))


def _text_clause(abbrev, where="beneath the emblem"):
    if len(abbrev) <= 5:
        return f'the bold large letters "{abbrev}" {where}'
    return "no text and no letters"


# ORG logos via the FLUX logo model. Distinctive emblems (reviewer loved the
# eagle/lion/metallic-shield) + bold wordmarks. Animals ARE good here (they
# read esports-fierce on FLUX, not collegiate like the old Z-Image badges).
ORG_EMBLEMS = [
    "a fierce detailed eagle head", "a bold roaring lion head",
    "a snarling wolf head", "a fierce tiger head", "a powerful bull head",
    "a striking cobra", "a fierce bear head", "a bold spartan warrior helmet",
    "a fierce falcon head", "a bold flame", "sharp aggressive spread wings",
    "crossed swords", "a fierce panther head", "a bold ram head",
]
ORG_WORDMARKS = [
    "a bold aggressive sharp italic wordmark",
    "a bold heavy blocky upright wordmark",
    "a bold outlined collegiate-style wordmark",
    "the bold letters with a flame rising behind them",
    "the bold letters with sharp aggressive wings behind them",
]


def _org_flux(r, abbrev, name, region, c1, c2):
    """FLUX-logo org prompt: distinctive emblem+letters, bold wordmark, or a
    sleek metallic 3D emblem — with ALL extra text/taglines suppressed."""
    clean = (f"the ONLY text is the letters '{abbrev}', absolutely NO tagline, "
             f"NO subtitle, NO slogan, NO small text, NO extra words, sharp "
             f"clean flat vector design, solid pure white background, centered, "
             f"professional MMA fight promotion logo")
    roll = r.random()
    if roll < 0.45:                                   # emblem above letters
        emblem = r.choice(ORG_EMBLEMS)
        layout = "emblem"
        body = (f"{emblem} emblem above the bold letters '{abbrev}', {c1} and {c2}")
    elif roll < 0.75:                                 # bold wordmark
        wm = r.choice(ORG_WORDMARKS)
        layout = "wordmark"
        body = (f"{wm} of the letters '{abbrev}', {c1} and {c2}")
    else:                                             # metallic 3D emblem
        layout = "metallic"
        shape = r.choice(["angular shield", "hexagon crest", "circular medallion",
                          "sharp diamond"])
        body = (f"a sleek metallic 3D {shape} emblem with the letters "
                f"'{abbrev}', {c1} and metallic silver")
    return (f"professional modern MMA fight promotion logo, {body}. {clean}"), layout, layout


def build_prompt(name, abbrev, region, salt=0):
    # salt (re-roll count) shifts layout/color/icon so a re-roll differs
    r = _rng(f"{name}#{salt}" if salt else name)
    if KIND == "org":
        c1, c2 = r.choice(PALETTES[region]) if r.random() < 0.5 else r.choice(SHARED_PALETTES)
        return _org_flux(r, abbrev, name, region, c1, c2)
    icon = r.choice(ICONS)
    # 50% signature-region palette, 50% shared diverse pool
    c1, c2 = r.choice(PALETTES[region]) if r.random() < 0.50 else r.choice(SHARED_PALETTES)
    style = r.choice(STYLES)
    shape = r.choice(SHAPES)
    layout = r.choices([l for l, _ in LAYOUTS], [w for _, w in LAYOUTS])[0]
    letters = f'the bold heavy letters "{abbrev}"' if len(abbrev) <= 5 else "no lettering"
    # every logo is a SOLID FILLED contained badge — never floating elements.
    # "no drop shadow / flat" stops diffusion adding gray shadows that survive
    # the white-background key as gray halos.
    badge = (f"a SOLID FILLED {c1} {shape} (a solid filled shape, NOT an "
             f"outline)")
    tail = ("bold clean flat vector, sharp crisp edges, flat design with no "
            "drop shadow and no gradient, solid pure white background around "
            f"the badge, centered, {KINDS[KIND]['aesthetic']}, "
            f"{KINDS[KIND]['noun']}, no photograph")

    if layout == "lettermark":
        # letters ARE the logo — a clean bold modern monogram filling the badge
        return (f"a {style} fight-promotion logo: {letters} as a clean bold "
                f"modern monogram in {c2}, filling {badge}, minimal with a "
                f"small simple {icon} accent, {tail}"), icon, layout
    if layout == "icon":
        # one simple bold mark, letters small — modern and uncluttered
        return (f"a {style} fight-promotion logo: one large simple bold {icon} "
                f"in {c2} centered on {badge}, {letters} small in {c2} below, "
                f"clean and uncluttered, {tail}"), icon, layout
    # emblem — icon above letters, both inside the solid badge
    return (f"a {style} fight-promotion logo emblem: {icon} above {letters}, "
            f"both in {c2}, on {badge}, {tail}"), icon, layout


def key_white_to_transparent(im, tol=32):
    """Flood-fill from the four corners, turning contiguous near-white pixels
    transparent. Corner-flood (not global) so white INSIDE the emblem stays."""
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()
    seen = bytearray(w * h)
    dq = deque()
    for cx, cy in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        dq.append((cx, cy))
    while dq:
        x, y = dq.popleft()
        if x < 0 or y < 0 or x >= w or y >= h:
            continue
        idx = y * w + x
        if seen[idx]:
            continue
        r, g, b, a = px[x, y]
        if r >= 255 - tol and g >= 255 - tol and b >= 255 - tol:
            seen[idx] = 1
            px[x, y] = (r, g, b, 0)
            dq.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    return im


def finalize(raw_img, path):
    im = key_white_to_transparent(raw_img)
    bbox = im.getbbox()
    if bbox:
        im = im.crop(bbox)
    # pad to square, then to 512
    w, h = im.size
    s = max(w, h)
    canvas = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    canvas.alpha_composite(im, ((s - w) // 2, (s - h) // 2))
    canvas.resize((512, 512)).save(path)


def load_manifest():
    return json.loads(MANIFEST.read_text(encoding="utf-8-sig")) if MANIFEST.exists() else {}


def save_manifest(m):
    MANIFEST.write_text(json.dumps(m, indent=2, ensure_ascii=False), encoding="utf-8")


def select(names, sample):
    """Round-robin across regions so a starter sample spans all seven."""
    if not sample:
        return [(r, e) for r, lst in names.items() for e in lst]
    picks, i = [], 0
    regions = list(names)
    while len(picks) < sample:
        r = regions[i % len(regions)]
        idx = i // len(regions)
        if idx < len(names[r]):
            picks.append((r, names[r][idx]))
        elif all(i // len(regions) >= len(names[x]) for x in regions):
            break
        i += 1
    return picks


def process_reviews():
    """Declined / re-roll logos (from logoreview.py) are deleted so this run
    regenerates them; a re-roll counter salts the seed so they come out
    different. Approved logos are left untouched. Returns {abbrev: salt}."""
    rv = OUT / "review.json"
    if not rv.exists():
        return {}
    review = json.loads(rv.read_text(encoding="utf-8-sig"))
    manifest = load_manifest()
    salts = {}
    changed = False
    for ab, e in list(review.items()):
        if e.get("status") in ("declined", "reroll"):
            (OUT / f"{ab}.png").unlink(missing_ok=True)
            (OUT / "raw" / f"{ab}.png").unlink(missing_ok=True)
            manifest.pop(ab, None)
            e["rerolls"] = e.get("rerolls", 0) + 1
            salts[ab] = e["rerolls"]
            e["status"] = "pending"          # clear the decision; it re-enters review
            e["note"] = ""
            changed = True
            print(f"RE-ROLL {ab} (attempt {salts[ab] + 1})", flush=True)
    if changed:
        rv.write_text(json.dumps(review, indent=2, ensure_ascii=False), encoding="utf-8")
        save_manifest(manifest)
    return salts


def main():
    global KIND, OUT, MANIFEST, ICONS, STYLES, LAYOUTS
    if "--team" in sys.argv:
        KIND = "team"
        ICONS, STYLES, LAYOUTS = TEAM_ICONS, TEAM_STYLES, TEAM_LAYOUTS
    cfg = KINDS[KIND]
    OUT = HERE / cfg["subdir"]
    MANIFEST = OUT / "manifest.json"
    OUT.mkdir(exist_ok=True)
    names_path = HERE / cfg["names"]
    if not names_path.exists():
        sys.exit(f"{cfg['names']} not found — generate {KIND} names first.")
    names = json.loads(names_path.read_text(encoding="utf-8-sig"))
    salts = process_reviews()
    sample = 0
    if "--sample" in sys.argv:
        sample = int(sys.argv[sys.argv.index("--sample") + 1])

    is_org = (KIND == "org")           # org = flat PIL wordmark; team = FLUX/Z-Image
    z = sz = steps = endpoint = None
    if not is_org:
        z = Client(cfg["model"], token=TOKEN)
        sz, steps, endpoint = cfg["size"], cfg["steps"], cfg["endpoint"]

    def _gen(prompt):
        if endpoint == "/infer":            # FLUX logo space: (image, seed)
            res = z.predict(prompt=prompt, seed=0, randomize_seed=True,
                            width=sz, height=sz, num_inference_steps=steps,
                            api_name="/infer")
            return res[0] if isinstance(res, (list, tuple)) else res
        return z.predict(prompt=prompt, height=sz, width=sz,   # Z-Image
                         num_inference_steps=steps, seed=0, randomize_seed=True,
                         api_name="/generate_image")

    manifest = load_manifest()
    done = 0
    for region, e in select(names, sample):
        ab = e["abbrev"]
        path = OUT / f"{ab}.png"
        if path.exists():
            continue
        try:
            if is_org:
                style = wordmarks.make(ab, region, path, salt=salts.get(ab, 0))
                motif = layout = style
                print(f"GEN  {ab}  {e['name']} ({region}: {style})", flush=True)
            else:
                prompt, motif, layout = build_prompt(e["name"], ab, region, salts.get(ab, 0))
                print(f"GEN  {ab}  {e['name']} ({region}: {layout}/{motif})", flush=True)
                raw = Image.open(img_path(retry(lambda: _gen(prompt))))
                (OUT / "raw").mkdir(exist_ok=True)
                raw.save(OUT / "raw" / f"{ab}.png")     # keep for re-keying w/o regen
                finalize(raw, path)
            manifest[ab] = {"name": e["name"], "region": region, "type": KINDS[KIND]["type"],
                            "motif": motif, "layout": layout, "file": path.name}
            save_manifest(manifest)
            done += 1
            print(f"DONE {ab}  ({done})", flush=True)
        except Exception as ex:
            m = str(ex)
            print(f"FAIL {ab}: {m[:140]}", flush=True)
            if "quota" in m.lower():
                print("QUOTA EXHAUSTED - rerun to resume.")
                break
    print(f"LOGOGEN DONE: {done} new logos")


if __name__ == "__main__":
    main()
