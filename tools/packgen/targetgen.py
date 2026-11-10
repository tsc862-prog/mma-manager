"""Targeted demographic gap-fill for the generic pack.

Unlike zgeneric (random weighted distribution), this generates an EXPLICIT plan
of (sub-ethnicity, sex, weight band, prime-or-triptych) to fill known holes:
MENA, Polynesian, true heavyweights, under-supplied cultures (incl. Romanian /
Hungarian which had zero identities), and light-end triptychs so light-division
stars can visibly age.

Forces the sub-ethnicity (roll_person skin_override) and the weight band, so the
manifest weight/culture are exactly on target. IDs use a dedicated 4000+ range
per (sex,eth) so they never collide with the 500-series random generics.
Writes into $MMA_PACK_DIR (the generic pack). Resumable (skips existing),
stops cleanly on quota.

Usage:  MMA_PACK_DIR=<generic pack dir>  python targetgen.py [max_new]
"""
import os
import sys
import time
from pathlib import Path
from PIL import Image
from gradio_client import Client, handle_file
from batchgen import OUT, TOKEN
from krea5 import retry, img_path
from calib3 import count_figures
from identityslots import roll_person, ETH_SKIN
import zgeneric
import ztrio
import packmeta
import zclient
import random

# skin substring -> ethnicity bucket (reverse of ETH_SKIN)
SKIN_BUCKET = {s: eth for eth, skins in ETH_SKIN.items() for s in skins}


def find_skin(key):
    for s in SKIN_BUCKET:
        if s.startswith(key):
            return s
    raise ValueError(f"no ETH_SKIN entry starts with {key!r}")


def band_bounds(band):
    lo, hi = band.split("_")
    return (None if lo == "x" else int(lo), None if hi == "x" else int(hi))


def roll_lbs(mn, mx, rng):
    if mn and mx:
        return rng.randint(mn, mx)
    if mx:                                   # open-low (light): spread below cap
        return rng.randint(max(106, mx - 45), mx)
    if mn:                                   # open-high (heavy): cap at 258
        return rng.randint(mn, min(mn + 55, 258))
    return rng.randint(115, 145)             # x_x wildcard = women (light)


# ---- THE PLAN -------------------------------------------------------------
# (skin_key, sex, band, count, n_triptych). n_triptych of the count get the
# 3-stage treatment (so they age); the rest are prime-only.
PLAN = [
    # === MENA men (~120, biggest hole) =====================================
    ("Gulf Arab", "m", "x_170", 16, 3), ("Gulf Arab", "m", "171_x", 8, 1),
    ("Levantine", "m", "x_170", 12, 2), ("Moroccan Berber", "m", "x_170", 12, 1),
    ("Egyptian", "m", "x_170", 10, 1),  ("Persian", "m", "x_170", 14, 3),
    ("Persian", "m", "171_x", 4, 0),    ("Afghan", "m", "171_x", 6, 0),
    ("Turkish", "m", "x_170", 12, 2),   ("Turkish", "m", "171_x", 6, 1),
    ("Kurdish", "m", "x_170", 8, 1),    ("Israeli", "m", "x_170", 10, 2),
    # === MENA women (~25) ==================================================
    ("Gulf Arab", "f", "x_x", 6, 2), ("Levantine", "f", "x_x", 5, 1),
    ("Persian", "f", "x_x", 5, 2),   ("Turkish", "f", "x_x", 5, 1),
    ("Egyptian", "f", "x_x", 4, 0),
    # === Polynesian men (~35, bigger builds) ===============================
    ("Samoan", "m", "206_265", 8, 1), ("Samoan", "m", "171_x", 6, 1),
    ("Maori", "m", "171_x", 7, 1),    ("Tongan", "m", "206_265", 5, 0),
    ("Fijian", "m", "171_x", 5, 1),   ("Papua New Guinean", "m", "171_x", 4, 0),
    # === Polynesian women (~10) ============================================
    ("Samoan", "f", "x_x", 4, 1), ("Maori", "f", "x_x", 3, 1), ("Fijian", "f", "x_x", 3, 0),
    # === Dedicated true heavyweights (206-265) =============================
    ("English", "m", "206_265", 6, 1), ("Russian", "m", "206_265", 6, 1),
    ("Brazilian", "m", "206_265", 5, 0), ("South African Zulu", "m", "206_265", 5, 1),
    ("Angolan", "m", "206_265", 3, 0),
    # === Under-supplied cultures (mostly light generic) ====================
    ("English", "m", "x_170", 12, 3), ("ruddy British Isles", "m", "x_170", 8, 2),
    ("English", "m", "171_x", 5, 0),
    ("Russian", "m", "x_170", 12, 3), ("Russian", "m", "171_x", 6, 1),
    ("Russian", "f", "x_x", 5, 2),
    ("Brazilian", "m", "x_170", 10, 2), ("Afro-Brazilian", "m", "x_170", 4, 1),
    ("Brazilian", "f", "x_x", 5, 2),
    ("South African Zulu", "m", "x_170", 8, 1), ("Angolan", "m", "x_170", 4, 0),
    ("Japanese", "m", "x_170", 16, 5), ("Japanese", "f", "x_x", 5, 2),
    ("Han Chinese", "m", "x_170", 10, 3), ("Han Chinese", "f", "x_x", 4, 1),
    ("French", "m", "x_170", 8, 2), ("French", "f", "x_x", 3, 1),
    ("Romanian", "m", "x_170", 9, 2), ("Romanian", "f", "x_x", 3, 0),
    ("Hungarian", "m", "x_170", 6, 1), ("Hungarian", "f", "x_x", 2, 0),
]

ID_BASE = 4000     # targeted ids: 4000+ per (sex,eth), never collide w/ 500s


def _vary_light(ident, band):
    """Spread the generic-light 'x_170' entries across real light bands so
    reviewers see weight variety, not a wall of 170. Specific bands (171_x,
    206_265, x_x) pass through untouched. Deterministic by ident."""
    if band != "x_170":
        return band
    import zlib
    r = zlib.crc32(("LB" + ident).encode()) % 100
    return "x_135" if r < 30 else "136_155" if r < 65 else "156_170"


def expand_plan():
    """Deterministic ident assignment: walk the plan, number per (sex,eth) from
    ID_BASE. Yields (ident, sex, eth, skin, band, is_triptych)."""
    counters = {}
    for key, sex, band, count, ntri in PLAN:
        skin = find_skin(key)
        eth = SKIN_BUCKET[skin]
        for i in range(count):
            counters[(sex, eth)] = counters.get((sex, eth), ID_BASE - 1) + 1
            ident = f"{sex}_{eth}_{counters[(sex, eth)]}"
            yield ident, sex, eth, skin, _vary_light(ident, band), (i < ntri)


def _roll_traits(base, rng, skin, lbs=None, no_tattoo=False):
    """Roll a targeted person WITH a persona + quirk tags (was missing — gap-fill
    fighters showed no glam/menace/wildhair chips). no_tattoo (triptychs) drops
    ink and the inked persona, which don't survive 3-stage continuity."""
    persona = zgeneric.roll_persona(base, rng)
    if no_tattoo and persona == "inked":
        persona = None
    traits, gear, glove, _s, extra = roll_person(
        base, rng, lbs_override=lbs, persona=persona, skin_override=skin,
        no_tattoo=no_tattoo)
    tags = ([persona] if persona else []) + extra
    return traits, gear, glove, tags


def _clear_review(ident):
    """Drop any stale review.json entry for an ident before regenerating it,
    so the new person starts pending (not the old person's approve/decline)."""
    import json
    rv = OUT / "review.json"
    if not rv.exists():
        return
    data = json.loads(rv.read_text(encoding="utf-8-sig"))
    if data.pop(ident, None) is not None:
        rv.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


def _upsert(ident, sex, eth, band, skin, tags, style):
    mn, mx = band_bounds(band)
    st = None if style in (None, "x") else style     # None = wildcard
    packmeta.upsert(ident, sex, eth, mn, mx, st,
                    tags=tags, cultures=packmeta.cultures_for(eth, skin),
                    look=skin, pack=OUT)


def gen_prime(zgen, cut, ident, sex, eth, skin, band, rng):
    style = zgeneric._style_for(ident)            # ~20% get a specific style
    base = f"{ident}_{band}_{style}"
    lbs = roll_lbs(*band_bounds(band), rng)
    traits, gear, glove, tags = _roll_traits(base, rng, skin, lbs)
    _upsert(ident, sex, eth, band, skin, tags, style)
    for attempt in range(3):
        res = retry(lambda: zclient.generate(
            zgen, zgeneric.gen_prompt(base, traits, gear, glove, rng), 640, 1280, steps=9))
        img = Image.open(img_path(res))
        if count_figures(img, drop_edge_ranges=True) == 1:
            (OUT / "raw").mkdir(exist_ok=True)
            raw = OUT / "raw" / f"{ident}_fig.png"
            img.save(raw)
            cp = retry(lambda: cut.predict(f=handle_file(str(raw)), api_name="/png"))
            cutp = OUT / "raw" / f"{ident}_cut.png"
            Image.open(cp).save(cutp)
            zgeneric.finalize(cutp, ident)
            return True
    return False


def gen_triptych(zgen, cut, ident, sex, eth, skin, band, rng):
    style = zgeneric._style_for(ident)
    base = f"{ident}_{band}_{style}"
    lbs = roll_lbs(*band_bounds(band), rng)
    traits, gear, glove, tags = _roll_traits(base, rng, skin, lbs, no_tattoo=True)
    _upsert(ident, sex, eth, band, skin, tags, style)
    for attempt in range(3):
        res = retry(lambda: zclient.generate(
            zgen, ztrio.triptych_prompt(base, traits, gear, glove, skin),
            1536, 1280, steps=9))
        img = Image.open(img_path(res))
        if count_figures(img) == 3:
            (OUT / "raw").mkdir(exist_ok=True)
            raw = OUT / "raw" / f"{ident}_sheet.png"
            img.save(raw)
            cp = retry(lambda: cut.predict(f=handle_file(str(raw)), api_name="/png"))
            cutp = OUT / "raw" / f"{ident}_sheet_cut.png"
            Image.open(cp).save(cutp)
            try:
                ztrio.split_sheet(cutp, base)
                return True
            except ValueError as ex:
                print(f"  sheet rejected ({ex})", flush=True)
                raw.unlink(missing_ok=True)
    return False


def main():
    if os.environ.get("MMA_PACK_DIR", "") == "":
        sys.exit("MMA_PACK_DIR not set — aim this at the generic pack.")
    max_new = int(sys.argv[1]) if len(sys.argv) > 1 else 9999
    lock = OUT / ".zgeneric.lock"          # share the generic-pack writer lock
    if lock.exists() and time.time() - lock.stat().st_mtime < 900:
        sys.exit("another generator holds the generic-pack lock.")
    lock.write_text("targetgen", encoding="utf-8")
    zgen = zclient.make_client()
    cut = Client("not-lain/background-removal", token=TOKEN)
    rng = random.Random()
    done = fail = 0
    try:
        for ident, sex, eth, skin, band, tri in expand_plan():
            if done >= max_new:
                break
            lock.touch()
            if (OUT / f"{ident}_p_body.png").exists():
                continue
            _clear_review(ident)      # a regenerated ident must not inherit a
                                      # stale approve/decline from a prior person
            kind = "TRIPTYCH" if tri else "prime"
            print(f"GEN  {ident}  {skin.split(' features')[0]}  {band}  {kind}", flush=True)
            try:
                ok = (gen_triptych if tri else gen_prime)(zgen, cut, ident, sex, eth, skin, band, rng)
                if ok:
                    done += 1
                    print(f"DONE {ident}  ({done})", flush=True)
                else:
                    packmeta.remove(ident, OUT)
                    fail += 1
                    print(f"SKIP {ident}: no clean sheet in 3 tries", flush=True)
            except Exception as ex:
                m = str(ex)
                print(f"FAIL {ident}: {m[:140]}", flush=True)
                if "quota" in m.lower():
                    print("QUOTA EXHAUSTED - rerun to resume.")
                    break
    finally:
        lock.unlink(missing_ok=True)
    print(f"TARGETGEN DONE: {done} new ({fail} skipped)")


if __name__ == "__main__":
    main()
