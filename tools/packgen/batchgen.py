"""Resumable batch generator for the painted fighter pack.

Per identity: FLUX body render -> HF background removal -> derived face crop.
Outputs to e:/MMA Manager/generated-pack/{name}_body.png + {name}_face.png.
Safe to re-run: finished identities are skipped, so a quota cutoff just pauses
the batch until the next run.
"""
import os, sys, time
from pathlib import Path
from PIL import Image
from gradio_client import Client, handle_file

# Pack directory is env-switchable so painted and anime packs stay fully
# separated (user wants to be able to change styles without mixing assets).
OUT = Path(os.environ.get("MMA_PACK_DIR", r"e:\MMA Manager\generated-pack"))
OUT.mkdir(exist_ok=True)
TOKEN = Path(r"e:\MMA Manager\hf-token.txt").read_text().strip()

STYLE = ("Stylized digital painting illustration, painterly semi-realistic style "
         "like a premium sports management game: visible soft brush strokes, matte "
         "lighting, warm studio light, muted rich colors, slightly stylized features, "
         "clearly an illustration and not a photograph. Plain solid uniform light "
         "warm gray background, no gradient, no background shadows. No text, no "
         "logos, no lettering anywhere, plain solid-color gear.")

def body_prompt(traits, gear):
    return (f"Full body of {traits} standing in a relaxed athletic stance, front "
            f"view, arms at the sides. Correct adult human anatomy, figure about 7 "
            f"heads tall, long legs, NOT chibi, NOT caricature. {gear} Barefoot. "
            f"{STYLE} Full figure visible head to toe, no floor shadow.")

# name, seed, traits, gear  (8 male + 2 female across eth/age/build)
IDENTITIES = [
    ("m_white_00_y", 2001,
     "a fictional 22 year old male MMA fighter: pale skin, short brown crew cut hair, clean shaven, lean lightweight build",
     "Shirtless, black fight trunks with white trim, red MMA gloves."),
    ("m_white_01_p", 2002,
     "a fictional 33 year old male MMA heavyweight: ruddy pale skin, shaved bald head, full brown beard, huge powerful build with a slight belly",
     "Shirtless, navy fight trunks with gold trim, black MMA gloves."),
    ("m_black_00_p", 2003,
     "a fictional 29 year old male MMA middleweight: dark brown skin, short buzzed black hair, black goatee, muscular athletic build",
     "Shirtless, white fight trunks with black trim, red MMA gloves."),
    ("m_black_01_v", 2004,
     "a fictional 40 year old male MMA light-heavyweight veteran: deep brown skin, grey-flecked short hair, grey stubble, cauliflower ears, weathered face, powerful build",
     "Shirtless, maroon fight trunks, black MMA gloves."),
    ("m_asian_00_y", 2005,
     "a fictional 24 year old male MMA flyweight: East Asian features, light tan skin, short spiky black hair, clean shaven, small lean wiry build",
     "Shirtless, royal blue fight trunks with white trim, blue MMA gloves."),
    ("m_asian_01_p", 2006,
     "a fictional 31 year old male MMA welterweight: East Asian features, black hair in a small top knot, thin black goatee, athletic muscular build",
     "Shirtless, black fight trunks with red trim, black MMA gloves."),
    ("m_latin_00_p", 2007,
     "a fictional 28 year old male MMA lightweight: tan olive skin, dark slicked-back hair, short stubble, lean muscular build",
     "Shirtless, green fight trunks with gold trim, red MMA gloves."),
    ("m_white_02_v", 2008,
     "a fictional 44 year old male MMA middleweight veteran: weathered pale skin, grey receding short hair, grey goatee, crooked flattened nose, sturdy build",
     "Shirtless, charcoal fight trunks, red MMA gloves."),
    ("f_asian_00_y", 2009,
     "a fictional 25 year old female MMA flyweight: East Asian features, black hair in a tight high bun, fierce focused expression, lean athletic build",
     "Black sports bra, teal fight shorts with black waistband, black MMA gloves."),
    ("f_black_00_p", 2010,
     "a fictional 30 year old female MMA strawweight: dark brown skin, cornrow braids tied into a low bun, athletic build",
     "Black sports bra, red fight shorts, red MMA gloves."),
]

def face_crop(body_png, face_png):
    im = Image.open(body_png).convert("RGBA")
    bx = im.getbbox()
    fh = bx[3] - bx[1]
    band = im.crop((bx[0], bx[1], bx[2], bx[1] + int(fh * 0.10)))
    hb = band.getbbox()
    cx = bx[0] + (hb[0] + hb[2]) // 2
    # Head + shoulders. Buns/top-knots add figure height above the face, so a
    # taller crop keeps the chin+shoulders in frame for every hairstyle.
    side = int(fh * 0.30)
    top = bx[1] - int(side * 0.04)
    crop = im.crop((cx - side // 2, top, cx + side // 2, top + side))
    crop.resize((512, 512), Image.LANCZOS).save(face_png)

def main():
    gen = Client("black-forest-labs/FLUX.1-schnell", token=TOKEN)
    cut = Client("not-lain/background-removal", token=TOKEN)
    done = failed = 0
    for name, seed, traits, gear in IDENTITIES:
        body_out = OUT / f"{name}_body.png"
        face_out = OUT / f"{name}_face.png"
        if body_out.exists() and face_out.exists():
            print(f"SKIP {name} (done)")
            done += 1
            continue
        try:
            print(f"GEN  {name} ...", flush=True)
            raw = gen.predict(prompt=body_prompt(traits, gear), seed=seed,
                              randomize_seed=False, width=640, height=1280,
                              num_inference_steps=4, api_name="/infer")
            raw_path = raw[0] if isinstance(raw, (list, tuple)) else raw
            (OUT / "raw").mkdir(exist_ok=True)
            Image.open(raw_path).save(OUT / "raw" / f"{name}.png")
            print(f"CUT  {name} ...", flush=True)
            cut_path = cut.predict(f=handle_file(str(OUT / "raw" / f"{name}.png")), api_name="/png")
            Image.open(cut_path).save(body_out)
            face_crop(body_out, face_out)
            done += 1
            print(f"OK   {name}", flush=True)
            time.sleep(2)
        except Exception as ex:
            failed += 1
            msg = str(ex)
            print(f"FAIL {name}: {msg[:200]}", flush=True)
            if "quota" in msg.lower():
                print("QUOTA EXHAUSTED - rerun this script later to resume.")
                break
    print(f"BATCH RESULT: {done} done, {failed} failed, {len(IDENTITIES)} total")

if __name__ == "__main__":
    main()
