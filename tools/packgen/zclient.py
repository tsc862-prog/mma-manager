"""Z-Image client adapter.

The pipeline was built against the fork `mrfakename/Z-Image-Turbo` (api
`/generate_image`, free width/height, single-image result). That space went
into RUNTIME_ERROR (owner's problem). This adapter targets the OFFICIAL
`Tongyi-MAI/Z-Image-Turbo` instead, whose api differs:
  - endpoint `/generate`
  - resolution is a PRESET dropdown string, not free width/height
  - result is a GALLERY (list), not a single path

So callers keep passing width/height/steps and this maps to the nearest preset
and unwraps the gallery, returning a local image path (compatible with
krea5.img_path / Image.open). One place to change if the space ever moves again.
"""
from gradio_client import Client
from batchgen import TOKEN

SPACE = "Tongyi-MAI/Z-Image-Turbo"

# Our tuned resolutions -> the official space's nearest preset.
_RES_MAP = {
    (640, 1280): "720x1280 ( 9:16 )",     # prime single figure (tall)
    (720, 1280): "720x1280 ( 9:16 )",
    # triptych: 1728-wide (9:7) made the CENTER figure render at a different
    # scale/identity (reviewer: "prime zooms in oddly / looks different"). Back
    # to a width near the proven 1536, but taller than 1024 so faces/eyes stay
    # bigger than the original deformed-eye size.
    (1536, 1280): "1472x1104 ( 4:3 )",
    (1536, 1024): "1536x1024 ( 3:2 )",    # legacy triptych / calibration
    (512, 512): "1024x1024 ( 1:1 )",      # probe
}


def make_client():
    return Client(SPACE, token=TOKEN)


def _preset(width, height):
    if (width, height) in _RES_MAP:
        return _RES_MAP[(width, height)]
    # fallback: pick a portrait/landscape 1:1-ish default
    return "1024x1024 ( 1:1 )"


def _gallery_path(result):
    """Unwrap the /generate result -> a local image path."""
    gallery = result[0] if isinstance(result, (list, tuple)) else result
    item = gallery[0] if isinstance(gallery, (list, tuple)) else gallery
    if isinstance(item, dict):
        img = item.get("image", item)
        if isinstance(img, dict):
            return img.get("path") or img.get("url")
        return img
    if isinstance(item, (list, tuple)):
        return item[0]
    return item


def generate(client, prompt, width, height, steps=9, seed=0, randomize_seed=True):
    """Generate one image; returns a local path (like the old /generate_image)."""
    result = client.predict(
        prompt=prompt,
        resolution=_preset(width, height),
        seed=seed,
        steps=steps,
        shift=3.0,
        random_seed=randomize_seed,
        gallery_images=[],
        api_name="/generate",
    )
    return _gallery_path(result)
