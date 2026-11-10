"""Anime-style test batch: 5 fighters through the standard triptych pipeline
with an anime style block (matched to the qwen-f_asian sample the reviewer
liked). Distinct id range (200+) so they stand out in the review app."""
import random
from PIL import Image
from gradio_client import Client, handle_file
from batchgen import OUT, TOKEN
from krea5 import retry, img_path
import ztrio
from calib3 import count_figures
from identityslots import roll_person
from speclog import record_spec

ANIME_STYLE = ("Modern anime illustration for a fighting game character sheet: "
               "clean confident dark lineart, cel shading with flat two-tone "
               "shadows, semi-realistic adult anime proportions with mature "
               "faces and realistic athletic bodies, muted rich colors, the "
               "style of a seinen martial arts anime. Flawless clean healthy "
               "bare skin. Plain solid uniform light gray background. All "
               "clothing is plain solid-color fabric.")
ztrio.STYLE = ANIME_STYLE          # triptych_prompt reads the module global

SLOTS = [
    # wave 1
    "m_white_200_170_190_st",
    "m_black_200_200_230_wr",
    "f_asian_200_x_115_bjj",
    "m_latin_200_145_160_kb",
    "m_asian_200_185_205_br",
    # wave 2
    "m_white_201_200_230_br",
    "m_black_201_155_175_st",
    "m_asian_201_x_125_kb",
    "m_latin_201_170_190_wr",
    "f_white_200_115_130_st",
    "f_black_200_125_140_gr",
    "m_white_202_145_160_wl",
    "m_black_202_185_205_bjj",
    "m_asian_202_230_x_wr",
    "m_latin_202_135_150_cl",
]

def main():
    zgen = Client("mrfakename/Z-Image-Turbo", token=TOKEN)
    cut = Client("not-lain/background-removal", token=TOKEN)
    (OUT / "raw").mkdir(exist_ok=True)
    rng = random.Random()
    done = 0
    for base in SLOTS:
        # fighter number = slot identity (see ztrio): don't resurrect renamed slots
        ident = "_".join(base.split("_")[:3])
        if list(OUT.glob(f"{ident}_*_p_body.png")):
            done += 1
            continue
        try:
            traits, gear, glove, skin, _tags = roll_person(base, rng)
            record_spec(base, traits, gear, glove, skin)
            sheet_raw = OUT / "raw" / f"{base}_sheet.png"
            for attempt in range(3):
                print(f"GEN  {base} (anime, try {attempt + 1}) ...", flush=True)
                res = retry(lambda: zgen.predict(
                    prompt=ztrio.triptych_prompt(base, traits, gear, glove, skin),
                    height=1024, width=1536, num_inference_steps=9,
                    seed=0, randomize_seed=True, api_name="/generate_image"))
                img = Image.open(img_path(res))
                if count_figures(img) != 3:
                    print("  wrong figure count - re-rolling", flush=True)
                    continue
                img.save(sheet_raw)
                cp = retry(lambda: cut.predict(f=handle_file(str(sheet_raw)), api_name="/png"))
                cut_png = OUT / "raw" / f"{base}_sheet_cut.png"
                Image.open(cp).save(cut_png)
                try:
                    ztrio.split_sheet(cut_png, base)
                    done += 1
                    print(f"DONE {base}  ({done}/{len(SLOTS)})", flush=True)
                    break
                except ValueError as ex:
                    print(f"  sheet rejected ({ex}) - re-rolling", flush=True)
                    sheet_raw.unlink(missing_ok=True)
        except Exception as ex:
            print(f"FAIL {base}: {str(ex)[:200]}", flush=True)
            if "quota" in str(ex).lower():
                print("QUOTA EXHAUSTED - rerun to resume.")
                break
    print(f"ZANIME DONE: {done}/{len(SLOTS)}")

if __name__ == "__main__":
    main()
