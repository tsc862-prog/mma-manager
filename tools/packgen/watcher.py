"""Review watcher daemon (triptych era).

Polls review.json every 30s and processes decisions automatically:
- declined  -> retire files, roll a BRAND-NEW person into the slot (note used
               as avoid-guidance), regenerate triptych -> back to pending.
- retry     -> SAME person spec regenerated with the note as mandatory
               feedback (any per-stage flags mean the whole triptych re-rolls,
               since all ages come from one generation) -> back to pending.
- approved  -> left alone (triptych fighters arrive finished; approval final).

Quota errors back off 30 minutes. Run alongside the review app.
"""
import json
import random
import shutil
import time
import zlib
from pathlib import Path
from PIL import Image
from gradio_client import Client, handle_file
from batchgen import OUT, TOKEN
from krea5 import retry, img_path
from ztrio import triptych_prompt, split_sheet
from calib3 import count_figures
from identityslots import roll_person, parse_slot
from speclog import record_spec, get_spec, log_decline
import packmeta
import zclient

REVIEW = OUT / "review.json"
RETIRED = OUT / "retired"
RETIRED.mkdir(exist_ok=True)
POLL = 30

def load():
    return json.loads(REVIEW.read_text(encoding="utf-8")) if REVIEW.exists() else {}

def clear(base):
    r = load()
    r.pop(base, None)
    REVIEW.write_text(json.dumps(r, indent=2, ensure_ascii=False), encoding="utf-8")

REALISTIC = OUT.parent / "generated-pack-realistic"

def retire(ident, keep_realistic=False):
    """Declined fighters normally retire; a decline noted 'realistic' means
    the reviewer wants the (off-style but good) fighter SAVED to the
    realistic bucket instead. Globbing by ident catches both legacy
    token-encoded names and manifest-era plain names."""
    if keep_realistic:
        REALISTIC.mkdir(exist_ok=True)
        entry = packmeta.load(OUT)["fighters"].get(ident)
        for f in list(OUT.glob(f"{ident}_*")):
            if f.is_file():
                shutil.move(str(f), REALISTIC / f.name)
        if entry:       # carry the metadata into the destination pack
            dest = packmeta.load(REALISTIC)
            dest["fighters"][ident] = entry
            packmeta.save(dest, REALISTIC)
    else:
        for f in list(OUT.glob(f"{ident}_*")):
            if f.is_file():
                shutil.move(str(f), RETIRED / f"{int(time.time())}_{f.name}")
    for f in list((OUT / "raw").glob(f"{ident}_*")):
        f.unlink()

def regen(zgen, cut, base, traits, gear, glove, skin="", note_suffix="", age_salt=0):
    if note_suffix:
        traits += note_suffix
    for attempt in range(3):
        res = retry(lambda: zclient.generate(
            zgen, triptych_prompt(base, traits, gear, glove, skin, age_salt),
            1536, 1280, steps=9))
        img = Image.open(img_path(res))
        if count_figures(img) == 3:
            sheet_raw = OUT / "raw" / f"{base}_sheet.png"
            img.save(sheet_raw)
            cp = retry(lambda: cut.predict(f=handle_file(str(sheet_raw)), api_name="/png"))
            cut_png = OUT / "raw" / f"{base}_sheet_cut.png"
            Image.open(cp).save(cut_png)
            try:
                split_sheet(cut_png, base)
                return True
            except ValueError as ex:      # figure count / skin mismatch -> re-roll
                print(f"  sheet rejected ({ex}) - re-rolling", flush=True)
                sheet_raw.unlink(missing_ok=True)
    return False

def main():
    zgen = zclient.make_client()
    cut = Client("not-lain/background-removal", token=TOKEN)
    rng = random.Random()
    print(f"WATCHER up - polling every {POLL}s", flush=True)
    while True:
        try:
            review = load()
            actionable = [(k, e) for k, e in review.items()
                          if e.get("status") in ("declined", "retry")]
            for key, entry in actionable:
                # keys may be manifest-era idents or legacy full slot bases
                if packmeta.IDENT_RE.match(key):
                    ident, base = key, packmeta.slot_base(key, OUT)
                elif parse_slot(key) is not None:
                    ident, base = "_".join(key.split("_")[:3]), key
                else:
                    continue
                note = entry.get("note", "").strip()
                status = entry["status"]
                log_decline(ident, note, kind=status)
                # rescue-to-realistic only applies when working a DIFFERENT
                # pack; inside the realistic pack a decline is just a decline
                keep = (status == "declined" and REALISTIC != OUT
                        and ("realist" in note.lower() or "photo" in note.lower()))
                print(f"{status.upper()} {ident} ({note or 'no note'})"
                      + (" -> saved to realistic bucket" if keep else ""), flush=True)
                slot = parse_slot(base)
                retire(ident, keep_realistic=keep)
                if status == "declined":
                    # total amnesia: no avoid-note in the prompt, fresh ages —
                    # the note is only kept in the analysis log
                    packmeta.remove(ident, OUT)
                    traits, gear, glove, skin, extra_tags = roll_person(base, rng)
                    record_spec(ident, traits, gear, glove, skin)
                    packmeta.upsert(
                        ident, slot["sex"], slot["eth"],
                        None if slot["min"] == "x" else int(slot["min"]),
                        None if slot["max"] == "x" else int(slot["max"]),
                        None if slot["style"] in (None, "x") else slot["style"],
                        tags=extra_tags,
                        cultures=packmeta.cultures_for(slot["eth"], skin),
                        look=skin, pack=OUT)
                    ok = regen(zgen, cut, base, traits, gear, glove, skin,
                               age_salt=rng.randint(1, 999_999))
                else:
                    spec = get_spec(ident) or get_spec(base)
                    if not spec:
                        print(f"  no spec for {ident}, skipping", flush=True)
                        clear(key)
                        continue
                    suffix = (f". IMPORTANT reviewer feedback about the previous "
                              f"attempt that MUST be addressed: {note}") if note else ""
                    ok = regen(zgen, cut, base, spec["traits"], spec["gear"],
                               spec.get("glove", "black"), spec.get("skin", ""),
                               note_suffix=suffix)
                clear(key)
                print(f"  -> {'regenerated, pending review' if ok else 'FAILED 3 sheets, slot left empty'}", flush=True)
        except Exception as ex:
            msg = str(ex)
            print(f"WATCHER error: {msg[:200]}", flush=True)
            if "quota" in msg.lower():
                print("quota exhausted - sleeping 30 min", flush=True)
                time.sleep(1800)
        time.sleep(POLL)

if __name__ == "__main__":
    main()
