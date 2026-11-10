"""Calibrate triptych reliability: canvas sizes x seeds, counting figures
locally on the raw sheet (columns deviating from the flat gray background)."""
from PIL import Image
from gradio_client import Client
from batchgen import OUT, TOKEN
from krea5 import STYLE, retry, img_path

BODY = ("This person is a fictional 30 year old man of about 180 pounds: medium "
        "brown skin, short black hair, short beard, calm expression, with the "
        "body of an amateur boxer - fit but ordinary, soft edges, no six-pack. "
        "He wears plain maroon fight trunks, completely plain blue fingerless "
        "MMA gloves with no markings, barefoot.")

def prompt():
    return ("A triptych: exactly three figures and no more, side by side with "
            "clear gaps between them, on a plain solid uniform light gray "
            "background. The SAME fictional man at three ages - all three have "
            "the same face, features and skin tone, clearly the identical "
            f"person. {BODY} LEFT figure: age 21, boyish smooth face, light "
            "stubble, fuller hair, slightly slimmer. CENTER figure: age 30, his "
            "prime. RIGHT figure: age 40 veteran, hair and beard heavily "
            "streaked with grey, visible wrinkles, weathered skin, same skin "
            "color. Each standing relaxed, front view, arms at sides, full "
            f"figure head to toe. {STYLE} No floor shadow.")

# NOTE: a "collage detector" via background-shade sampling was tried and
# removed — measured luminance deltas between seamed collages and true
# triptychs are within noise. Collages that pass the figure count get killed
# by the skin-tone gate after bg removal instead (costs one cut call).
def count_figures(img, drop_edge_ranges=False):
    im = img.convert("RGB")
    w, h = im.size
    px = im.load()
    # background reference: median of the four corners
    corners = [px[2, 2], px[w - 3, 2], px[2, h - 3], px[w - 3, h - 3]]
    bg = tuple(sorted(c[i] for c in corners)[1] for i in range(3))
    occupied = []
    # Sample only the upper 85%: photoreal renders soft floor shadows that
    # run continuously under all three figures — full-height sampling reads
    # that band as "occupied" in every column and merges the sheet into one
    # blob (good triptychs got rejected as "1 figure", 6 gens per slot).
    for x in range(0, w, 2):
        hits = 0
        for y in range(0, int(h * 0.85), 6):
            r, g, b = px[x, y]
            if abs(r - bg[0]) + abs(g - bg[1]) + abs(b - bg[2]) > 60:
                hits += 1
            if hits > 3:
                break
        occupied.append(hits > 3)
    ranges, start = [], None
    for i, occ in enumerate(occupied + [False]):
        if occ and start is None:
            start = i
        elif not occ and start is not None:
            if (i - start) * 2 > w * 0.05:
                ranges.append((start * 2, i * 2))
            start = None
    if drop_edge_ranges:
        # Single-figure mode: backdrop vignette (radial studio shading) can
        # read as a phantom figure hugging a side edge. The real subject is
        # centered — keep any range spanning the centerline (even if vignette
        # merged it into an edge), drop off-center ranges that touch an edge.
        ranges = [r for r in ranges
                  if r[0] <= w // 2 <= r[1] or (r[0] > 0 and r[1] < w - 4)]
    return len(ranges)

if __name__ == "__main__":     # guard: count_figures is imported by the pipeline
    import zclient
    z = zclient.make_client()
    for label, (W, H) in [("1536x1024", (1536, 1024)), ("1536x1280", (1536, 1280))]:
        for seed in (101, 202, 303):
            res = retry(lambda: zclient.generate(z, prompt(), W, H, steps=9))
            img = Image.open(img_path(res))
            n = count_figures(img)
            img.save(OUT.parent / f"calib-{label}-{seed}.png")
            print(f"{label} seed {seed}: {n} figures", flush=True)
    print("CALIB DONE")
