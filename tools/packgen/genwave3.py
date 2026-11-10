"""Generation 3 — clean restart on the gloveless recipe. Slot ids start at
100 so this generation is unmistakable from archived v1/v2 work.

Raw primes only (review-first flow). Usage: genwave3.py [count]  (default 10)
Resumable: reruns skip finished raws and continue the composed sequence.
"""
import random
import sys
from pathlib import Path
from PIL import Image
from gradio_client import Client
from batchgen import OUT, TOKEN
from krea5 import STYLE, retry, img_path
from identityslots import roll_person
from speclog import record_spec

COUNT = int(sys.argv[1]) if len(sys.argv) > 1 else 10
rng = random.Random(30100)          # fixed seed -> stable slot composition
roll_rng = random.Random()

MALE_BANDS = [("x", "125"), ("125", "140"), ("135", "150"), ("145", "160"),
              ("155", "175"), ("170", "190"), ("185", "205"), ("200", "230"), ("230", "x")]
MALE_BAND_W = [0.05, 0.07, 0.09, 0.11, 0.16, 0.16, 0.14, 0.12, 0.10]
FEMALE_BANDS = [("x", "115"), ("115", "130"), ("125", "140"), ("135", "155")]
# Seeded-deterministic sequence: first LEGACY_SLOTS keep the original 4-eth
# composition (existing styled fighters keep their idents on resume); the new
# geography buckets (mena/poly) enter beyond that watermark.
ETHS_LEGACY = ["white", "black", "asian", "latin"]
ETH_W_LEGACY = [0.30, 0.27, 0.22, 0.21]
ETHS = ["white", "black", "asian", "latin", "mena", "poly"]
ETH_W = [0.27, 0.24, 0.19, 0.17, 0.08, 0.05]
LEGACY_SLOTS = 200
STYLES = ["wr", "st", "kb", "bjj", "gr", "br", "cl", "wl"]
STYLE_W = [0.17, 0.17, 0.13, 0.12, 0.11, 0.10, 0.08, 0.12]

def compose_slots(total=300, females_per_10=2):
    slots, counters = [], {}
    for i in range(total):
        female = (i % 10) >= 10 - females_per_10
        sex = "f" if female else "m"
        pool, w = (ETHS_LEGACY, ETH_W_LEGACY) if i < LEGACY_SLOTS else (ETHS, ETH_W)
        eth = rng.choices(pool, w)[0]
        lo, hi = rng.choice(FEMALE_BANDS) if female else rng.choices(MALE_BANDS, MALE_BAND_W)[0]
        style = rng.choices(STYLES, STYLE_W)[0]
        key = (sex, eth)
        counters[key] = counters.get(key, 99) + 1      # ids from 100
        slots.append(f"{sex}_{eth}_{counters[key]}_{lo}_{hi}_{style}")
    return slots

def main():
    gen = Client("black-forest-labs/FLUX.1-Krea-dev", token=TOKEN)
    (OUT / "raw").mkdir(exist_ok=True)
    generated = 0
    for base in compose_slots():
        if generated >= COUNT:
            break
        prime_raw = OUT / "raw" / f"{base}_p.png"
        if prime_raw.exists() or (OUT / f"{base}_p_body.png").exists():
            continue
        try:
            traits, gear, glove, _skin, _tags = roll_person(base, roll_rng)
            record_spec(base, traits, gear, glove)
            print(f"GEN  {base} ...", flush=True)
            res = retry(lambda: gen.predict(
                prompt=(f"Full body of {traits} standing in a relaxed stance, "
                        f"front view, arms at the sides. Correct adult human "
                        f"anatomy, figure about 7 heads tall, long legs. {gear} "
                        f"{STYLE} Full figure visible head to toe, no floor "
                        f"shadow."),
                seed=0, randomize_seed=True, width=640, height=1280,
                guidance_scale=4.5, num_inference_steps=28, api_name="/infer"))
            Image.open(img_path(res)).save(prime_raw)
            generated += 1
            print(f"OK   {base}  ({generated}/{COUNT})", flush=True)
        except Exception as ex:
            print(f"FAIL {base}: {str(ex)[:200]}", flush=True)
            if "quota" in str(ex).lower():
                print("QUOTA EXHAUSTED - rerun to resume.")
                break
    print(f"WAVE3 DONE: {generated}/{COUNT}")

if __name__ == "__main__":
    main()
