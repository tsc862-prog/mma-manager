"""Triptych pipeline: ONE Z-Image generation per fighter produces all three
ages side by side (intrinsic identity), then the sheet is background-cut and
split at transparent column gaps into y/p/v body assets + face crops.

Usage: ztrio.py [count]   (default 5). Resumable per fighter.
"""
import random
import sys
from pathlib import Path
from PIL import Image
from gradio_client import Client, handle_file
from batchgen import OUT, TOKEN, face_crop
from krea5 import retry, img_path
from genwave3 import compose_slots
from identityslots import roll_person, parse_slot
from speclog import record_spec

# Style is env-switchable alongside the pack dir (MMA_STYLE=anime|painted) so
# the two packs can evolve independently while the user decides.
_ANIME = ("Modern anime illustration for a fighting game character sheet: "
          "clean confident dark lineart, cel shading with flat two-tone "
          "shadows, semi-realistic adult anime proportions with mature faces "
          "and realistic athletic bodies, muted rich colors, the style of a "
          "seinen martial arts anime. Clean healthy skin with no cuts or "
          "bruises. Plain solid uniform light gray background. All clothing "
          "is plain solid-color fabric.")
_PAINTED = ("Stylized digital painting for a premium sports management game: "
            "visible painterly matte brushwork, muted rich colors, warm studio "
            "light, mature realistic adult facial proportions, serious grounded "
            "expression, clearly a hand-painted illustration with soft brush "
            "strokes, not a photograph. Flawless smooth clean healthy bare "
            "skin. Plain solid uniform light gray background. All clothing is "
            "plain solid-color fabric.")
_REALISTIC = ("Professional full-body studio photograph, photorealistic, shot "
              "on a neutral seamless light gray studio backdrop with soft even "
              "lighting, natural realistic skin texture, sharp focus. Clean "
              "healthy skin with no cuts or bruises. All clothing is plain "
              "solid-color fabric.")
import os as _os
STYLE = {"painted": _PAINTED, "realistic": _REALISTIC}.get(
    _os.environ.get("MMA_STYLE", "anime"), _ANIME)

COUNT = int(sys.argv[1]) if len(sys.argv) > 1 else 5
roll_rng = random.Random()

def triptych_prompt(base, traits, gear, glove, skin="", age_salt=0):
    # Game age bands (reviewer-set): youth 18-25, prime 26-36, veteran 36-45.
    # Ages are seeded by slot so RETRIES keep the same ages; DECLINES pass a
    # random age_salt so the replacement forgets everything about the original.
    import zlib
    arng = random.Random(zlib.crc32(base.encode()) + age_salt)
    y_age = arng.randint(19, 25)
    p_age = arng.randint(27, 35)
    v_age = arng.randint(37, 42)   # reviewer: veterans rendered "a bit TOO old"

    female = base.startswith("f_")
    person = "woman" if female else "man"
    # Gear may differ between the age panels (reviewer: same outfit isn't
    # required, but a change must not be forced either) — the prime wears the
    # rolled gear, the others wear any plain unmarked fight gear.
    # A/B-tested wording (packgen glovetest): describe the glove's anatomy,
    # never name "MMA/sparring gloves" — the object name summons boxing gloves
    gear_direct = gear.replace(
        "barefoot, bare hands.",
        f"thin completely plain {glove} open-fingered grappling gloves - small "
        f"padded strips across the knuckles only, bare fingers sticking out, "
        f"like fingerless gym gloves, barefoot. The other two figures wear "
        f"similar plain solid-color fight gear with the same thin open-fingered "
        f"grappling gloves, bare fingers visible — the colors may be the same "
        f"or different between the figures.")
    # Reviewer-driven: dark-skinned fighters kept getting LIGHTER side panels
    # ("youth and veteran look white") — so the exact skin tone is restated
    # inside EVERY panel clause, not just once globally.
    skin_pin = f", with exactly the same {skin}" if skin else ", with exactly the same skin color as the center figure"

    # Veteran hair: weighted roll — grey was far too common, and gingers were
    # turning grey (red hair fades, it doesn't grey). Most 38-42 athletes have
    # little or no grey; aging shows in the face instead.
    redhead = any(wd in traits for wd in ("ginger", "red hair", "auburn"))
    if redhead:
        vet_hair = ("hair still clearly red, only slightly faded toward copper "
                    "with age - red hair does not turn grey")
    else:
        roll = arng.random()
        if roll < 0.40:
            vet_hair = "hair still fully its natural color with no grey"
        elif roll < 0.75:
            vet_hair = "a subtle hint of grey at the temples only, hair otherwise its natural color"
        elif roll < 0.90:
            vet_hair = "a few scattered silver strands, hair otherwise unchanged"
        else:
            vet_hair = "noticeably greying at the temples"

    # Veteran aging must be STRUCTURAL (bone/face shape), not just skin lines —
    # fine wrinkles vanish on dark skin in cel shading (reviewer: black veteran
    # women show no aging). Youth gets extra softness for contrast.
    vet_face = ("a visibly aged facial structure: slightly hollowed cheeks with "
                "harder cheekbone definition, deeper folds from the nose to the "
                "corners of the mouth, a leaner more angular face, heavier "
                "tired eyelids, and faint creases across the forehead")
    if "photorealistic" in STYLE and not female:
        # The structural list above exists because cel shading swallows fine
        # wrinkles — photoreal does NOT, it renders every marker literally and
        # a 38-year-old photographs as ~50 (reviewer declines on MEN: "veteran
        # looks to old"; women's realistic aging reads subtle already, leave it).
        vet_face = ("light natural aging for an athlete in his late thirties: "
                    "faint crow's feet, slightly deeper lines around the mouth, "
                    "a slightly leaner face, clearly still young-looking and fit")
        v_age = min(v_age, 39)
    if female:
        young = (f"LEFT figure: age {y_age}, girlish fresher face with soft "
                 f"rounded cheeks, smooth full features, bright clear eyes, "
                 f"fuller thicker hair in exactly the same color as the center "
                 f"figure, slightly slimmer build{skin_pin}.")
        vet = (f"RIGHT figure: age {v_age}, a late-career athlete - {vet_hair}, "
               f"{vet_face}, but still clearly a fit professional in fighting "
               f"shape, NOT elderly, absolutely no facial hair{skin_pin}.")
    else:
        young = (f"LEFT figure: age {y_age}, boyish fresher face with soft "
                 f"rounded cheeks, smooth full features, light stubble only, "
                 f"fuller thicker hair in exactly the same color as the center "
                 f"figure, slightly slimmer build{skin_pin}.")
        vet = (f"RIGHT figure: age {v_age}, a late-career athlete - {vet_hair}, "
               f"{vet_face}, but still clearly a fit professional in fighting "
               f"shape, NOT elderly{skin_pin}.")
    # "A triptych: exactly three figures and no more" + 1536-wide canvas =
    # 3/3 in calibration; 1920 wide produced 4-5 figures every time.
    # Tattoos are part of identity: if the person has one, it must appear
    # identically in every age panel.
    tattoo_pin = (" The exact same tattoo appears identically on all three "
                  "figures." if "tattoo" in traits else "")
    # Hair color must not drift youth->prime (reviewer: "brown youth -> black
    # prime"). Name the EXACT rolled color (longest-match first, so "deep
    # espresso brown" beats "brown" and "jet black" beats "black"), and
    # explicitly forbid darkening toward black between the two.
    from identityslots import DARK_HAIR_COLORS, HAIR_COLORS_ANY
    _all_hair = sorted(set(DARK_HAIR_COLORS + HAIR_COLORS_ANY), key=len, reverse=True)
    hc = next((c for c in _all_hair if c in traits), None) or "the identical"
    hair_pin = (f" The LEFT (youth) and CENTER (prime) figures have EXACTLY the "
                f"same {hc} hair — identical {hc} hair color on both, with NO "
                f"darkening and NO shift toward black between youth and prime.")

    # STYLE placement is per-style. Anime/painted: style LEADS (at the tail it
    # was diluted and photoreal default broke through). REALISTIC: layout
    # leads, style trails — "studio photograph" up front implies ONE subject
    # and fought "exactly three figures" (~8% sheet success = $7 of churn);
    # drift toward photoreal is harmless when photoreal IS the target.
    lead = "" if "photorealistic" in STYLE else STYLE + " "
    # "ONE single continuous scene" pins against photo-collage mode: on wide
    # heavyweight bodies the model renders three SEPARATE studio panels with
    # seams — and each panel invents a different person (positive phrasing;
    # "no panel borders" would summon panels).
    return (f"{lead}A triptych: exactly three figures and no more, standing side by "
            f"side with clear gaps between them, all together in ONE single "
            f"continuous scene, on one seamless plain solid uniform light "
            f"gray background that spans the entire image. The SAME fictional {person} at three ages - all "
            f"three figures have the same face shape, the same features and "
            f"exactly the same skin tone, clearly the identical person. All "
            f"three figures are lit identically with flat even lighting; the "
            f"center figure has exactly the same complexion and skin color as "
            f"the left and right figures, no tan, no darker lighting on the "
            f"center figure. This person is {traits}. {gear_direct} {young} "
            f"CENTER figure: age {p_age}, in their prime. {vet}{tattoo_pin}"
            f"{hair_pin} Each standing relaxed, front view, arms at sides, "
            f"full figure head to toe, all three in the same consistent "
            f"rendering style. Every face has clear, natural, well-formed "
            f"symmetrical eyes, both eyes open and looking straight ahead, no "
            f"distorted or malformed eyes. "
            + (STYLE + " " if "photorealistic" in STYLE else "")
            + "No floor shadow.")

def _skin_stats(fig):
    """Mean RGB of skin pixels in the FACE region. The torso band failed for
    women (it sampled the sports bra, saw 'matching' panels, and skipped the
    correction — reviewer: 'the skin tone just gets darker'). The r>g>b skin
    heuristic also rejects grey hair and most gear."""
    w, h = fig.size
    box = fig.crop((int(w * 0.20), 0, int(w * 0.80), int(h * 0.20)))
    px = box.load()
    n, s = 0, [0, 0, 0]
    for y in range(0, box.height, 2):
        for x in range(0, box.width, 2):
            r, g, b, a = px[x, y]
            if a > 200 and r > g > b and 15 <= r - b <= 130:
                lum = (r + g + b) / 3
                if 45 < lum < 235:
                    s[0] += r; s[1] += g; s[2] += b; n += 1
    if n < 40:
        return None
    return [c / n for c in s]


def _match_tone(fig, cur, tgt):
    """Multiplicative per-channel transfer toward the target skin tone."""
    scale = [max(0.55, min(1.6, t / c if c > 10 else 1.0)) for t, c in zip(tgt, cur)]
    r, g, b, a = fig.split()
    r = r.point(lambda v, s=scale[0]: min(255, int(v * s)))
    g = g.point(lambda v, s=scale[1]: min(255, int(v * s)))
    b = b.point(lambda v, s=scale[2]: min(255, int(v * s)))
    return Image.merge("RGBA", (r, g, b, a))


def _is_skin(r, g, b):
    return r > g > b and 15 <= r - b <= 130


def _hair_region(fig):
    """Top-of-head band: top 14% of the figure, middle 60% of its width."""
    w, h = fig.size
    return (int(w * 0.20), 0, int(w * 0.80), int(h * 0.14))


def _hair_stats(fig):
    """Mean RGB of hair pixels (opaque, non-skin, not near-white) in the head band."""
    box = fig.crop(_hair_region(fig))
    px = box.load()
    n, s = 0, [0, 0, 0]
    for y in range(0, box.height, 2):
        for x in range(0, box.width, 2):
            r, g, b, a = px[x, y]
            if a > 200 and not _is_skin(r, g, b) and (r + g + b) / 3 < 235:
                s[0] += r; s[1] += g; s[2] += b; n += 1
    if n < 40:
        return None
    return [c / n for c in s]


def _match_hair(fig, cur, tgt):
    """Shift ONLY the hair pixels of the head band toward the target color.
    Clamp floor 0.35 (was 0.5) so a brown->black correction can actually reach
    near-black instead of stalling at medium brown."""
    scale = [max(0.35, min(1.9, t / c if c > 8 else 1.0)) for t, c in zip(tgt, cur)]
    x0, y0, x1, y1 = _hair_region(fig)
    px = fig.load()
    for y in range(y0, y1):
        for x in range(x0, x1):
            r, g, b, a = px[x, y]
            if a > 200 and not _is_skin(r, g, b) and (r + g + b) / 3 < 235:
                px[x, y] = (min(255, int(r * scale[0])), min(255, int(g * scale[1])),
                            min(255, int(b * scale[2])), a)
    return fig


def split_sheet(cut_png, base):
    """Split a transparent triptych at fully-transparent column gaps, then
    normalize skin tone across the three panels (the model consistently gives
    the center figure a darker complexion — reviewer's top decline reason).
    Raises ValueError to trigger a free re-roll when panels are hopelessly
    mismatched or the figure count is wrong."""
    im = Image.open(cut_png).convert("RGBA")
    w, h = im.size
    alpha = im.getchannel("A")
    cols_occupied = []
    px = alpha.load()
    for x in range(w):
        occ = any(px[x, y] > 8 for y in range(0, h, 4))
        cols_occupied.append(occ)
    ranges, start = [], None
    for x, occ in enumerate(cols_occupied + [False]):
        if occ and start is None:
            start = x
        elif not occ and start is not None:
            if x - start > w * 0.05:
                ranges.append((start, x))
            start = None
    if len(ranges) != 3:
        raise ValueError(f"expected 3 figures, found {len(ranges)}")

    figs = []
    for (x0, x1) in ranges:
        fig = im.crop((max(0, x0 - 6), 0, min(w, x1 + 6), h))
        figs.append(fig.crop(fig.getbbox()))

    stats = [_skin_stats(f) for f in figs]
    if all(stats):
        # target = component-wise median (robust to one outlier panel).
        # Tight settings: flat/cel styles make even small deltas glaring
        # (reviewer: anime "skin tones way off by era").
        tgt = [sorted(s[i] for s in stats)[1] for i in range(3)]
        worst = max(sum(abs(s[i] - tgt[i]) for i in range(3)) / 3 for s in stats)
        if worst > 26:
            raise ValueError(f"skin tone mismatch across panels ({worst:.0f})")
        figs = [_match_tone(f, s, tgt) if sum(abs(s[i] - tgt[i]) for i in range(3)) / 3 > 2 else f
                for f, s in zip(figs, stats)]

    # Hair color continuity youth<->prime, enforced numerically (prompt pins
    # failed three reviews in a row). Veteran is left alone — grey is allowed.
    hy, hp = _hair_stats(figs[0]), _hair_stats(figs[1])
    if hy and hp:
        delta = sum(abs(a - b) for a, b in zip(hy, hp)) / 3
        if delta > 30:      # tightened 40->30: brown<->black drift (~43) and even
            raise ValueError(f"hair color mismatch youth vs prime ({delta:.0f})")  # partial drift re-rolls, not weak-corrects
        if delta > 12:
            figs[0] = _match_hair(figs[0], hy, hp)

    import packmeta
    ident = "_".join(base.split("_")[:3])
    for fig, stage in zip(figs, ("y", "p", "v")):
        body = OUT / f"{ident}_{stage}_body.png"
        face = OUT / f"{ident}_{stage}_face.png"
        fig.save(body)
        face_crop(body, face)
        packmeta.attach_image(ident, stage, "body", body.name, OUT)
        packmeta.attach_image(ident, stage, "face", face.name, OUT)

def main():
    import zclient
    zgen = zclient.make_client()
    cut = Client("not-lain/background-removal", token=TOKEN)
    (OUT / "raw").mkdir(exist_ok=True)

    done = 0
    consecutive_failures = 0   # COST CIRCUIT-BREAKER (a layout/prompt bug once
                               # churned ~$7 at an 8% success rate before anyone
                               # noticed — never let a systemic failure run)
    for base in compose_slots():
        if done >= COUNT:
            break
        if consecutive_failures >= 3:
            print("CIRCUIT BREAKER: 3 consecutive slots failed all attempts - "
                  "something systemic is wrong (prompt/layout/model). ABORTING "
                  "batch to protect credits. Fix the cause before rerunning.")
            break
        # The fighter NUMBER is the slot identity: reviewer weight/style
        # overrides rename the token part, and the renamed slot must not be
        # resurrected as a duplicate person.
        ident = "_".join(base.split("_")[:3])
        if (list(OUT.glob(f"{ident}_*_p_body.png"))
                or (OUT / f"{ident}_p_body.png").exists()):
            done += 1
            continue
        sheet_raw = OUT / "raw" / f"{base}_sheet.png"
        try:
            if not sheet_raw.exists():
                from calib3 import count_figures
                import packmeta
                from identityslots import parse_slot
                traits, gear, glove, skin, extra_tags = roll_person(base, roll_rng)
                record_spec(ident, traits, gear, glove, skin)
                slot = parse_slot(base)
                packmeta.upsert(
                    ident, slot["sex"], slot["eth"],
                    None if slot["min"] == "x" else int(slot["min"]),
                    None if slot["max"] == "x" else int(slot["max"]),
                    None if slot["style"] in (None, "x") else slot["style"],
                    tags=extra_tags,
                    cultures=packmeta.cultures_for(slot["eth"], skin),
                    look=skin, pack=OUT)
                for attempt in range(3):     # local pre-check: free re-rolls
                    print(f"GEN  {base} (triptych, try {attempt + 1}) ...", flush=True)
                    res = retry(lambda: zclient.generate(
                        zgen, triptych_prompt(base, traits, gear, glove, skin),
                        1536, 1280, steps=9))
                    img = Image.open(img_path(res))
                    n = count_figures(img)
                    if n == 3:
                        img.save(sheet_raw)
                        break
                    # keep rejects on disk — post-mortems on figure-count
                    # failures otherwise mean spelunking gradio's temp cache
                    rej = OUT / "raw" / "rejects"
                    rej.mkdir(exist_ok=True)
                    img.save(rej / f"{base}_try{attempt + 1}_{n}f.png")
                    print(f"  {n} figures - re-rolling", flush=True)
                else:
                    print(f"SKIP {base}: no 3-figure sheet in 3 tries", flush=True)
                    consecutive_failures += 1
                    continue

            print(f"CUT  {base} ...", flush=True)
            cp = retry(lambda: cut.predict(f=handle_file(str(sheet_raw)), api_name="/png"))
            cut_png = OUT / "raw" / f"{base}_sheet_cut.png"
            Image.open(cp).save(cut_png)
            split_sheet(cut_png, base)
            done += 1
            consecutive_failures = 0
            print(f"DONE {base}  ({done}/{COUNT})", flush=True)
        except ValueError as ex:
            print(f"SPLIT-FAIL {base}: {ex} - re-rolling sheet next run", flush=True)
            sheet_raw.unlink(missing_ok=True)
            consecutive_failures += 1
        except Exception as ex:
            print(f"FAIL {base}: {str(ex)[:200]}", flush=True)
            if "quota" in str(ex).lower():
                print("QUOTA EXHAUSTED - rerun to resume.")
                break
    print(f"ZTRIO DONE: {done}/{COUNT}")

if __name__ == "__main__":
    main()
