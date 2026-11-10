"""Generic fighter pipeline: ONE prime-age figure per fighter, wildcard style,
two weight categories that partition the game's real divisions by natural
walk-around weight (FighterGenerator.NaturalWeightBand):

  light  x_170   Strawweight..Welterweight  (natural weight <= 170 lbs)
  heavy  171_x   Middleweight..Heavyweight  (natural weight 171+ lbs)

Filenames: {sex}_{eth}_{id}_{band}_x_p_{face|body}.png — style token x
(wildcard) and stage p only. The game loader serves the prime image at every
age (nearest-stage fallback) and matches wildcard style at its any-style tier,
so generics backfill fighters no styled identity covers. Ids start at 500 so
generics are unmistakable from the styled 100-series.

Single-subject generation is the photo model's native mode: no triptych
layout fight, no split, no cross-panel gates -> ~1 gen + 1 cut per fighter.

Usage:  MMA_PACK_DIR=<pack dir, e.g. generated-pack-generic>  python zgeneric.py [count]
Resumable; also processes review declines/retries on each run (this pack has
no watcher — rerun the script after reviewing).
"""
import json
import os
import random
import shutil
import sys
import time
from pathlib import Path
from PIL import Image
from gradio_client import Client, handle_file
from batchgen import OUT, TOKEN, face_crop
from krea5 import retry, img_path
from calib3 import count_figures
from identityslots import roll_person, parse_slot
from speclog import record_spec, get_spec, log_decline


def find_spec(ident):
    """Spec lookup tolerant of both key eras: plain ident (manifest era) and
    legacy full slot base (ident_min_max_style)."""
    spec = get_spec(ident)
    if spec:
        return spec
    import speclog
    if speclog.SPECS.exists():
        all_specs = json.loads(speclog.SPECS.read_text(encoding="utf-8-sig"))
        for key, val in all_specs.items():
            if key.startswith(ident + "_"):
                return val
    return None
from ztrio import _REALISTIC as STYLE
import packmeta
import zclient

COUNT = int(sys.argv[1]) if len(sys.argv) > 1 else 10

# Weight bands, roll range each. Legacy two-bucket bands kept so existing
# fighters' roll_lbs still resolves; NEW slots roll from MEN_BANDS below.
BAND_ROLL = {
    "x_135": (106, 135),    # straw/fly/bantam — genuinely small
    "136_155": (136, 155),  # feather/light
    "156_185": (156, 185),  # welter/middle — deepest divisions
    "186_205": (186, 205),  # light-heavy
    "206_x": (206, 258),    # heavy / super-heavy
    "x_170": (115, 170), "171_x": (171, 260),   # legacy two-bucket
    "x_x": (115, 155),      # women wildcard (their range is narrow + light)
}
# (band, cumulative rng cutoff) — ONE rng.random() picks the band, so the
# seeded slot sequence is byte-identical to the old light/heavy split (which
# also consumed exactly one random()). Middle divisions weighted deepest.
MEN_BANDS = [("x_135", 0.14), ("136_155", 0.38), ("156_185", 0.66),
             ("186_205", 0.84), ("206_x", 1.01)]

# ~STYLE_PCT of generics get a SPECIFIC fighting style (the rest stay wildcard
# "x"). A styled fighter gets that style's visual features (cauliflower ears /
# boxer face / thai shorts, per identityslots.STYLE_TRAITS) AND matches that
# style preferentially in-game; wildcards just backfill. Decided by an ident
# HASH (not composer rng) so the slot sequence never reshuffles.
STYLE_PCT = 20
STYLE_CUM = [("wr", 0.20), ("st", 0.40), ("bjj", 0.56), ("kb", 0.70),
             ("gr", 0.82), ("br", 0.92), ("cl", 1.001)]


def _style_for(ident):
    import zlib
    if zlib.crc32(("STY" + ident).encode()) % 100 >= STYLE_PCT:
        return "x"                                  # wildcard (~80%)
    r = (zlib.crc32(("STC" + ident).encode()) % 1000) / 1000.0
    return next(s for s, cut in STYLE_CUM if r < cut)

def roll_persona(base, rng):
    """One marketable archetype per fighter at most (tagged for game hooks).
    Women: glam 30%, menace 6%, inked 5%, unathletic 4%. Men: adonis 12%,
    menace 8%, inked 7%, babyface 8%, unathletic 6%. Everyone else plain."""
    r = rng.random()
    if base.startswith("f_"):
        if r < 0.30: return "glam"
        if r < 0.36: return "menace"
        if r < 0.45: return "inked"        # 9% (was 5%) — more fully-tatted
        if r < 0.49: return "unathletic"
    else:
        if r < 0.12: return "adonis"
        if r < 0.20: return "menace"
        if r < 0.32: return "inked"        # 12% (was 7%) — more fully-tatted
        if r < 0.40: return "babyface"
        if r < 0.46: return "unathletic"
    return None



# The slot sequence is seeded-deterministic: idents derive from their index.
# The first LEGACY_SLOTS indices must keep the ORIGINAL 4-eth composition or
# every fighter already generated loses its place on resume. New-geography
# buckets (mena/poly) only enter the sequence beyond that watermark.
ETHS_LEGACY = ["white", "black", "asian", "latin"]
ETH_W_LEGACY = [0.30, 0.27, 0.22, 0.21]
ETHS = ["white", "black", "asian", "latin", "mena", "poly"]
ETH_W = [0.27, 0.24, 0.19, 0.17, 0.08, 0.05]
LEGACY_SLOTS = 1100          # pack reached 1000 fighters within these indices


def compose_slots(total=300):
    """Deterministic slot list (fixed seed): 2 women per 10; women are FULL
    weight wildcards (their visual weight spread is small — one pool matches
    everyone); men split 65/35 light/heavy (6 of the 9 real divisions are in
    the light bucket). NOTE: the female band token doesn't consume rng, so
    changing it never disturbs the seeded sequence."""
    rng = random.Random(50500)
    slots, counters = [], {}
    for i in range(total):
        female = (i % 10) >= 8
        sex = "f" if female else "m"
        pool, w = (ETHS_LEGACY, ETH_W_LEGACY) if i < LEGACY_SLOTS else (ETHS, ETH_W)
        eth = rng.choices(pool, w)[0]
        if female:
            band = "x_x"
        else:
            # ONE rng.random() -> a 5-band spread (was a 2-way light/heavy
            # split, also one random()); sequence stays byte-identical so no
            # existing ident is reshuffled — new slots just get finer bands.
            r = rng.random()
            band = next(b for b, cut in MEN_BANDS if r < cut)
        key = (sex, eth)
        counters[key] = counters.get(key, 499) + 1     # generic ids from 500
        ident = f"{sex}_{eth}_{counters[key]}"
        slots.append(f"{ident}_{band}_{_style_for(ident)}")
    return slots


def gen_prompt(base, traits, gear, glove, rng):
    # A/B-tested (glovetest.py): naming the object ("MMA/sparring gloves")
    # summons full boxing gloves; describing its anatomy wins — but even the
    # anatomical wording loses ~1 in 5 at scale. Hand gear is now a FRESH
    # RANDOM roll per generation (NOT keyed on base): the old crc32(base) hash
    # trapped every declined slot that hashed into "gloves" in an infinite
    # boxing-glove loop (same slot -> same hash -> gloves again). Bare hands
    # and wraps cannot misrender, so gloves get only ~20% exposure.
    r = rng.random()
    if r < 0.50:
        hands = "bare hands with relaxed open fingers, barefoot."
    elif r < 0.80:
        hands = (f"hands wrapped in plain {glove} cloth hand wraps around the "
                 f"knuckles and wrists, fingers bare, barefoot.")
    else:
        hands = (f"thin completely plain {glove} open-fingered grappling "
                 f"gloves - small padded strips across the knuckles only, "
                 f"bare fingers sticking out, like fingerless gym gloves, "
                 f"barefoot.")
    gear_direct = gear.replace("barefoot, bare hands.", hands)
    return (f"{STYLE} A single full-length studio portrait of exactly one "
            f"person. This person is {traits}. {gear_direct} Standing relaxed, "
            f"front view, arms at sides, looking straight into the camera with "
            f"both eyes open, full figure visible head to toe. "
            f"No floor shadow.")


def roll_lbs(base, rng):
    # wildcard MATCHING (x_x) still needs a concrete believable IMAGE weight
    band = "_".join(base.split("_")[3:5])
    lo, hi = BAND_ROLL.get(band, (150, 185))
    return rng.randint(lo, hi)


def finalize(cut_png, ident):
    """Write final images under PLAIN ident-based names (metadata lives in
    manifest.json, never in the filename) and attach them to the manifest."""
    fig = Image.open(cut_png).convert("RGBA")
    bbox = fig.getbbox()
    if bbox is None:
        raise ValueError("empty cutout")
    fig = fig.crop(bbox)
    body = OUT / f"{ident}_p_body.png"
    face = OUT / f"{ident}_p_face.png"
    fig.save(body)
    face_crop(body, face)
    packmeta.attach_image(ident, "p", "body", body.name, OUT)
    packmeta.attach_image(ident, "p", "face", face.name, OUT)


def process_reviews():
    """No watcher for this pack: apply decline/retry decisions inline so a
    rerun retires rejected fighters and frees their slots for new people.
    review.json is keyed by IDENT ({sex}_{eth}_{id})."""
    review_file = OUT / "review.json"
    if not review_file.exists():
        return {}
    review = json.loads(review_file.read_text(encoding="utf-8-sig"))
    retired_dir = OUT / "retired"
    notes = {}
    changed = False
    for ident, entry in list(review.items()):
        status = entry.get("status")
        if status not in ("declined", "retry"):
            continue
        note = entry.get("note", "").strip()
        log_decline(ident, note, kind=status)
        retired_dir.mkdir(exist_ok=True)
        for f in list(OUT.glob(f"{ident}_*")):
            if f.is_file():
                shutil.move(str(f), retired_dir / f"{int(time.time())}_{f.name}")
        for f in list((OUT / "raw").glob(f"{ident}_*")):
            f.unlink()
        if status == "retry" and note:
            notes[ident] = note           # same person: manifest entry survives
        else:
            packmeta.remove(ident, OUT)   # decline: new person takes the slot
        review.pop(ident)
        changed = True
        print(f"{status.upper()} {ident} ({note or 'no note'}) - slot freed", flush=True)
    if changed:
        review_file.write_text(json.dumps(review, indent=2, ensure_ascii=False),
                               encoding="utf-8")
    return notes


def main():
    # A fresh terminal without MMA_PACK_DIR once silently generated 29
    # fighters into the paused painted pack (batchgen's default) where no
    # review app looks. Generic generation must be explicitly aimed.
    if os.environ.get("MMA_PACK_DIR", "") == "":
        sys.exit("MMA_PACK_DIR is not set - refusing to generate into the "
                 "default pack.\nUse tools\\packgen\\run-generic.cmd or set "
                 "the env var first.")
    # single-writer lock: two concurrent runs on one pack race on manifest.json
    # (load-modify-save) and silently drop images (a fighter lost its face).
    lock = OUT / ".zgeneric.lock"
    if lock.exists():
        age = _mono_since(lock)
        if age is not None and age < 900:     # stale after 15 min of no touch
            sys.exit(f"another zgeneric is running on {OUT.name} "
                     f"(lock {int(age)}s old). Stop it or delete {lock}.")
    lock.write_text("running", encoding="utf-8")
    try:
        _run(zclient.make_client(),
             Client("not-lain/background-removal", token=TOKEN), lock)
    finally:
        lock.unlink(missing_ok=True)


def _mono_since(path):
    import time as _t
    try:
        return _t.time() - path.stat().st_mtime
    except OSError:
        return None


TRIPT_PCT = 12     # ~this % of new generic fighters get the 3-stage triptych
                   # treatment so they visibly age (spread across all weights)


def _is_triptych(ident):
    import zlib
    return zlib.crc32(("TRI" + ident).encode()) % 100 < TRIPT_PCT


def _gen_triptych(zgen, cut, base, ident, traits, gear, glove, skin, rng):
    """Generate a 3-stage aging sheet for a generic slot (same pipeline ztrio
    uses), splitting into y/p/v body+face. Returns True on success."""
    from ztrio import triptych_prompt, split_sheet
    for attempt in range(3):
        print(f"GEN  {base} (triptych, try {attempt + 1}) ...", flush=True)
        res = retry(lambda: zclient.generate(
            zgen, triptych_prompt(base, traits, gear, glove, skin),
            1536, 1280, steps=9))
        img = Image.open(img_path(res))
        if count_figures(img) == 3:
            raw = OUT / "raw" / f"{base}_sheet.png"
            img.save(raw)
            cp = retry(lambda: cut.predict(f=handle_file(str(raw)), api_name="/png"))
            cutp = OUT / "raw" / f"{base}_sheet_cut.png"
            Image.open(cp).save(cutp)
            try:
                split_sheet(cutp, base)
                return True
            except ValueError as ex:
                print(f"  sheet rejected ({ex}) - re-rolling", flush=True)
                raw.unlink(missing_ok=True)
        else:
            print(f"  {count_figures(img)} figures - re-rolling", flush=True)
    return False


def _run(zgen, cut, lock):
    (OUT / "raw").mkdir(parents=True, exist_ok=True)
    rng = random.Random()
    retry_notes = process_reviews()

    done = 0
    consecutive_failures = 0
    # slot list must outsize the target: declines retire slot ids for good,
    # and skipped slots burn entries too (+100 headroom, same seed = the
    # first N slots are identical however long the list is)
    for base in compose_slots(max(300, COUNT + 100)):
        lock.touch()                       # keep the single-writer lock fresh
        if done >= COUNT:
            break
        if consecutive_failures >= 3:
            print("CIRCUIT BREAKER: 3 consecutive slots failed all attempts - "
                  "ABORTING batch to protect credits.", flush=True)
            break
        ident = "_".join(base.split("_")[:3])
        if (list(OUT.glob(f"{ident}_*_p_body.png"))            # legacy names
                or (OUT / f"{ident}_p_body.png").exists()):    # manifest-era names
            done += 1
            continue

        # ~TRIPT_PCT of new slots become 3-stage triptychs (deterministic by
        # ident, so no composer rng is consumed and nothing reshuffles). Retry
        # slots stay single-image (the spec/note flow is prime-only).
        if _is_triptych(ident) and ident not in retry_notes:
            try:
                persona = roll_persona(base, rng)
                if persona == "inked":         # inked = tattoos, which don't
                    persona = None             # survive 3-stage continuity
                traits, gear, glove, _skin, extra_tags = roll_person(
                    base, rng, persona=persona, no_tattoo=True)
                record_spec(ident, traits, gear, glove, _skin)
                slot = parse_slot(base)
                packmeta.upsert(
                    ident, slot["sex"], slot["eth"],
                    None if slot["min"] == "x" else int(slot["min"]),
                    None if slot["max"] == "x" else int(slot["max"]),
                    None if slot["style"] in (None, "x") else slot["style"],
                    tags=([persona] if persona else []) + extra_tags,
                    cultures=packmeta.cultures_for(slot["eth"], _skin),
                    look=_skin, pack=OUT)
                if _gen_triptych(zgen, cut, base, ident, traits, gear, glove, _skin, rng):
                    done += 1
                    consecutive_failures = 0
                    print(f"DONE {base} (triptych)  ({done}/{COUNT})", flush=True)
                else:
                    packmeta.remove(ident, OUT)
                    consecutive_failures += 1
                    print(f"SKIP {base}: no 3-figure sheet in 3 tries", flush=True)
            except Exception as ex:
                print(f"FAIL {base}: {str(ex)[:200]}", flush=True)
                if "quota" in str(ex).lower():
                    print("QUOTA EXHAUSTED - rerun to resume.")
                    break
            continue

        raw = OUT / "raw" / f"{base}_fig.png"
        try:
            if not raw.exists():
                spec = find_spec(ident) if ident in retry_notes else None
                if spec:
                    traits, gear, glove = spec["traits"], spec["gear"], spec.get("glove", "black")
                    traits += (f". IMPORTANT reviewer feedback about the previous "
                               f"attempt that MUST be addressed: {retry_notes[ident]}")
                else:
                    persona = roll_persona(base, rng)
                    traits, gear, glove, _skin, extra_tags = roll_person(
                        base, rng, lbs_override=roll_lbs(base, rng), persona=persona)
                    record_spec(ident, traits, gear, glove, _skin)
                    slot = parse_slot(base)
                    packmeta.upsert(
                        ident, slot["sex"], slot["eth"],
                        None if slot["min"] == "x" else int(slot["min"]),
                        None if slot["max"] == "x" else int(slot["max"]),
                        None if slot["style"] in (None, "x") else slot["style"],
                        tags=([persona] if persona else []) + extra_tags,
                        cultures=packmeta.cultures_for(slot["eth"], _skin),
                        look=_skin, pack=OUT)
                for attempt in range(3):
                    print(f"GEN  {base} (try {attempt + 1}) ...", flush=True)
                    res = retry(lambda: zclient.generate(
                        zgen, gen_prompt(base, traits, gear, glove, rng),
                        640, 1280, steps=9))
                    img = Image.open(img_path(res))
                    n = count_figures(img, drop_edge_ranges=True)
                    if n == 1:
                        img.save(raw)
                        break
                    rej = OUT / "raw" / "rejects"
                    rej.mkdir(exist_ok=True)
                    img.save(rej / f"{base}_try{attempt + 1}_{n}f.png")
                    print(f"  {n} figures - re-rolling", flush=True)
                else:
                    print(f"SKIP {base}: no 1-figure image in 3 tries", flush=True)
                    consecutive_failures += 1
                    continue

            print(f"CUT  {base} ...", flush=True)
            cp = retry(lambda: cut.predict(f=handle_file(str(raw)), api_name="/png"))
            cut_png = OUT / "raw" / f"{base}_fig_cut.png"
            Image.open(cp).save(cut_png)
            finalize(cut_png, ident)
            done += 1
            consecutive_failures = 0
            print(f"DONE {base}  ({done}/{COUNT})", flush=True)
        except ValueError as ex:
            print(f"CUT-FAIL {base}: {ex} - re-rolling next run", flush=True)
            raw.unlink(missing_ok=True)
            consecutive_failures += 1
        except Exception as ex:
            print(f"FAIL {base}: {str(ex)[:200]}", flush=True)
            if "quota" in str(ex).lower():
                print("QUOTA EXHAUSTED - rerun to resume.")
                break
    print(f"ZGENERIC DONE: {done}/{COUNT}")


if __name__ == "__main__":
    main()
