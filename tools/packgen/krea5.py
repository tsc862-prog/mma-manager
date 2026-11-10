"""Pilot: 5 fighters on the Krea recipe, each as youth/prime/veteran with
weight bands in prompt + filename ({sex}_{eth}_{id}_{min}_{max}_{stage}).

Per fighter: Krea prime gen -> Kontext de-brand -> Kontext age edits (y/v)
-> bg removal + face crop for all three stages. Resumable per output file.
"""
import time
from pathlib import Path
from PIL import Image
from gradio_client import Client, handle_file
from batchgen import OUT, TOKEN, face_crop

# Positive-first (negation lists backfire — Qwen painted the blood we told it
# not to). One deliberate exception: "not a photograph" keeps Krea painterly.
STYLE = ("Stylized digital painting for a premium sports management game: "
         "visible painterly matte brushwork, muted rich colors, warm studio "
         "light, mature realistic adult facial proportions, serious grounded "
         "expression, clearly a hand-painted illustration with soft brush "
         "strokes, not a photograph. Flawless smooth clean healthy bare skin. "
         "Plain solid uniform light warm gray background. All clothing is "
         "plain solid-color fabric.")

# base_name (with weight band), seed, prime traits, gear, walkaround lbs
FIGHTERS = [
    ("m_white_10_240_x", 5001,
     "a fictional 30 year old male MMA heavyweight: ruddy pale skin, short dark blond hair, short beard, huge powerful build, walks around at about 260 pounds with heavy shoulders and thick torso",
     "Shirtless, plain navy fight trunks with gold waistband, black MMA gloves."),
    ("m_black_10_200_230", 5002,
     "a fictional 27 year old male MMA light heavyweight: deep brown skin, short faded haircut, clean shaven, broad muscular build, walks around at about 215 pounds",
     "Shirtless, plain white fight trunks with red waistband, red MMA gloves."),
    ("m_asian_10_x_150", 5003,
     "a fictional 26 year old male MMA flyweight: East Asian features, light tan skin, short black hair, clean shaven, small wiry build, walks around at about 140 pounds",
     "Shirtless, plain black fight trunks with gold waistband, blue MMA gloves."),
    ("m_latin_10_165_190", 5004,
     "a fictional 29 year old male MMA welterweight: tan olive skin, short dark curly hair, light stubble, lean muscular build, walks around at about 175 pounds",
     "Shirtless, plain green fight trunks with white waistband, black MMA gloves."),
    ("f_white_10_x_125", 5005,
     "a fictional 27 year old female MMA strawweight: pale lightly freckled skin, auburn hair in a tight ponytail, lean compact athletic build, walks around at about 120 pounds",
     "Plain black sports bra, plain dark purple fight shorts, black MMA gloves."),
]

# Aging must be VISIBLE but bounded: youth clearly boyish, veteran a worn
# late-career fighter, never a retiree. Sex-aware, with identity guards
# (reviewer-caught failures: skin tone lightened, a woman grew a beard, one
# head grew). Veteran markers VARY per fighter — reviewer feedback: aging
# shouldn't always just mean grey hair. Marker mix is seeded by the slot name
# so the same fighter always ages the same way.
import random as _random
import zlib as _zlib

KEEP = ("Keep the exact same person with exactly the same skin tone, the same "
        "head size and body proportions, the same pose, clothing, colors, "
        "lighting and background.")
KEEP_HAIR = "Keep the same hairstyle. "

VET_HAIR = [  # (marker text, changes-hairstyle?)
    ("clearly grey at the temples", False),
    ("salt-and-pepper hair throughout", False),
    ("a receding hairline with visibly thinning hair", True),
    ("hair kept the same color but dull and thinner", False),
]
VET_FACE = [
    "deeper lines on the forehead and crow's feet around the eyes",
    "hollowed weathered cheeks",
    "a heavier brow with old scar tissue around the eyebrows",
    "a slightly puffier face and a nose flattened by years of fights",
]
VET_BODY = [
    "a slightly thicker midsection",
    "a slightly leaner, more sinewy frame",
    "more pronounced battered ears",
    "drier, more weathered skin",
]

def age_edit(stage, female, base=""):
    rng = _random.Random(_zlib.crc32(base.encode()) if base else 39)
    person = "woman" if female else "man"
    nofacial = " Absolutely no facial hair — she is a woman." if female else ""
    if stage == "y":
        return (f"Make this fighter look 21 years old: noticeably {'girlish' if female else 'boyish'} "
                f"fresher face, smooth unlined skin, softer jawline, brighter eyes, "
                f"fuller thicker hair with no grey, slightly slimmer less developed "
                f"build.{nofacial} {KEEP_HAIR}{KEEP}")
    # Veteran: identity FIRST and loudest — reviewer: 'veteran heads look way
    # too different'. Only two aging changes, everything else pinned.
    hair, hair_changes = rng.choice(VET_HAIR)
    second = rng.choice(VET_FACE if rng.random() < 0.7 else VET_BODY)
    return (f"Age this exact {person} to 39 years old. The result MUST be "
            f"instantly recognizable as the very same {person}: identical face "
            f"shape, identical features, identical skin tone, identical head "
            f"size. Apply ONLY these two aging changes and nothing else: "
            f"{hair}, and {second}. Still athletic and in fighting shape, not "
            f"elderly.{nofacial} {'' if hair_changes else KEEP_HAIR}{KEEP}")

# Back-compat for older scripts (male wording, generic seed).
AGE_EDITS = {"y": age_edit("y", False), "v": age_edit("v", False)}
AGE_GUIDANCE = 3.0            # youth edits: commit firmly
VET_GUIDANCE = 2.5            # veteran edits: gentler = identity survives

def retry(fn, tries=3, wait=8):
    for i in range(tries):
        try:
            return fn()
        except Exception as ex:
            if "quota" in str(ex).lower() or i == tries - 1:
                raise
            print(f"  retry {i + 1} after: {str(ex)[:120]}", flush=True)
            time.sleep(wait)

def img_path(res):
    p = res[0] if isinstance(res, (list, tuple)) else res
    return (p.get("path") or p.get("url")) if isinstance(p, dict) else p

def finish_stage(cut, raw_file, base, stage):
    body = OUT / f"{base}_{stage}_body.png"
    face = OUT / f"{base}_{stage}_face.png"
    if body.exists() and face.exists():
        return
    cp = retry(lambda: cut.predict(f=handle_file(str(raw_file)), api_name="/png"))
    Image.open(cp).save(body)
    face_crop(body, face)
    print(f"OK   {base}_{stage}", flush=True)

def main():
    gen = Client("black-forest-labs/FLUX.1-Krea-dev", token=TOKEN)
    edit = Client("mcp-tools/FLUX.1-Kontext-Dev", token=TOKEN)
    cut = Client("not-lain/background-removal", token=TOKEN)
    (OUT / "raw").mkdir(exist_ok=True)

    for base, seed, traits, gear in FIGHTERS:
        try:
            prime_raw = OUT / "raw" / f"{base}_p.png"
            if not prime_raw.exists():
                print(f"GEN  {base} (prime, krea) ...", flush=True)
                res = retry(lambda: gen.predict(
                    prompt=(f"Full body of {traits} standing in a relaxed athletic "
                            f"stance, front view, arms at the sides. Correct adult "
                            f"human anatomy, figure about 7 heads tall, long legs, "
                            f"NOT chibi. {gear} Barefoot. {STYLE} Full figure "
                            f"visible head to toe, no floor shadow."),
                    seed=seed, randomize_seed=False, width=640, height=1280,
                    guidance_scale=4.5, num_inference_steps=28, api_name="/infer"))
                Image.open(img_path(res)).save(prime_raw)

                print(f"DEBRAND {base} ...", flush=True)
                res = retry(lambda: edit.predict(
                    input_image=handle_file(str(prime_raw)),
                    prompt=("Remove every logo, brand mark, symbol and piece of "
                            "text from all clothing and gloves, leaving the fabric "
                            "plain solid color. Keep the person, pose, colors, "
                            "lighting and background exactly the same."),
                    seed=7, randomize_seed=False, guidance_scale=2.5, steps=20,
                    api_name="/infer"))
                Image.open(img_path(res)).save(prime_raw)

            finish_stage(cut, prime_raw, base, "p")

            for stage, prompt in AGE_EDITS.items():
                stage_raw = OUT / "raw" / f"{base}_{stage}.png"
                if not stage_raw.exists():
                    print(f"AGE  {base} -> {stage} ...", flush=True)
                    res = retry(lambda: edit.predict(
                        input_image=handle_file(str(prime_raw)), prompt=prompt,
                        seed=11, randomize_seed=False, guidance_scale=AGE_GUIDANCE,
                        steps=20, api_name="/infer"))
                    Image.open(img_path(res)).save(stage_raw)
                finish_stage(cut, stage_raw, base, stage)
        except Exception as ex:
            print(f"FAIL {base}: {str(ex)[:200]}", flush=True)
            if "quota" in str(ex).lower():
                print("QUOTA EXHAUSTED - rerun to resume.")
                return
    print("KREA5 DONE")

if __name__ == "__main__":
    main()
