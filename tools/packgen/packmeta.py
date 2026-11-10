"""Pack manifest: ALL fighter metadata lives in <pack>/manifest.json, not in
filenames. Image filenames are opaque pointers (legacy files keep their old
token-encoded names; new files use plain {ident}_{stage}_{kind}.png).

Schema (version 1):
  { "version": 1,
    "fighters": {
      "m_white_100": {                    # ident = {sex}_{eth}_{id}, stable
        "sex": "m", "eth": "white",
        "weight": {"min": 170, "max": 190},   # null bound = open
        "style": "gr",                        # null = wildcard/any style
        "images": {"p": {"face": "f.png", "body": "b.png"}, ...},
        "tags": ["menace"],                   # game hooks (popularity etc.)
        "cultures": ["russian"]               # WorldAtlas culture keys
      }, ... } }

migrate_pack() builds the manifest for an existing pack from its filename
tokens + tags.json, without renaming any file.
"""
import json
import os
import re
import tempfile
import time as _time
from pathlib import Path

PACK = Path(os.environ.get("MMA_PACK_DIR", r"e:\MMA Manager\generated-pack"))
MANIFEST = PACK / "manifest.json"


class _ManifestLock:
    """Cross-process mutex for manifest read-modify-write. The generator and the
    review app both mutate manifest.json; without this they raced and produced
    torn writes ('Extra data' JSON corruption) and lost updates."""
    def __init__(self, pack):
        self.path = (Path(pack) if pack else PACK) / ".manifest.lock"

    def __enter__(self):
        for _ in range(600):                       # ~30s max
            try:
                fd = os.open(str(self.path), os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                os.close(fd)
                return self
            except FileExistsError:
                try:                               # steal a stale (>15s) lock
                    if _time.time() - self.path.stat().st_mtime > 15:
                        self.path.unlink(); continue
                except OSError:
                    pass
                _time.sleep(0.05)
        return self                                # give up waiting; proceed

    def __exit__(self, *a):
        try:
            self.path.unlink()
        except OSError:
            pass


def locked_update(pack, mutate):
    """Serialize a load -> mutate(manifest) -> atomic save against all other
    writers. Returns whatever mutate returns."""
    with _ManifestLock(pack):
        m = load(pack)
        result = mutate(m)
        save(m, pack)
        return result

FILE_RE = re.compile(
    r"^(?P<sex>[mf])_(?P<eth>[a-z]+)_(?P<id>\d+)_(?P<min>\d+|x)_(?P<max>\d+|x)"
    r"(?:_(?P<style>[a-z]+))?_(?P<stage>[ypv])_(?P<kind>face|body)$")

# rolled skin text -> WorldAtlas culture keys (prefix match, first hit wins);
# falls back to ETH_CULTURES for the bucket.
CULTURE_HINTS = {
    "Northern European": ["german", "dutch", "scandinavian"],
    "Scandinavian": ["scandinavian"],
    "Irish": ["celtic", "anglo"],
    "Eastern European Slavic": ["polish", "czech", "russian", "balkan"],
    "Mediterranean": ["italian", "greek", "iberian"],
    "Southern Italian": ["italian"],
    "ruddy British Isles": ["anglo", "celtic"],
    "Dagestani": ["russian"],
    "Georgian highlander": ["georgian", "armenian"],
    "West African": ["westafrican"],
    "Nigerian": ["westafrican"],
    "East African": ["eastafrican"],
    "Senegalese": ["westafrican"],
    "African-American": ["anglo"],
    "Caribbean": ["anglo", "latino"],
    "Ethiopian": ["eastafrican"],
    "Japanese": ["japanese"],
    "Korean": ["korean"],
    "Han Chinese": ["chinese"],
    "Thai": ["thai"],
    "Filipino": ["filipino"],
    "Vietnamese": ["vietnamese"],
    "Mongolian": ["mongolian", "centralasian"],
    "Kazakh": ["centralasian"],
    "Uzbek": ["centralasian"],
    "Indian South Asian": ["indian"],
    "Sri Lankan": ["indian"],
    "Mexican mestizo": ["latino"],
    "Brazilian": ["brazilian"],
    "Afro-Brazilian": ["brazilian"],
    "Argentinian": ["latino"],
    "Colombian": ["latino"],
    "Peruvian Andean": ["latino"],
    "Gulf Arab": ["arabic"],
    "Levantine": ["arabic", "israeli"],
    "Moroccan Berber": ["arabic"],
    "Egyptian": ["arabic"],
    "Persian": ["persian"],
    "Turkish": ["turkic"],
    "Kurdish": ["turkic", "persian", "arabic"],
    "Samoan": ["polynesian"],
    "Maori": ["polynesian"],
    "Tongan": ["polynesian"],
    "Fijian": ["polynesian"],
    "Papua New Guinean": ["polynesian"],
    "French": ["french"],
    "Balkan": ["balkan"],
    "Baltic": ["baltic"],
    "Armenian": ["armenian"],
    "Congolese": ["centralafrican"],
    "South African Zulu": ["southernafrican"],
    "Cameroonian": ["westafrican"],
    "Somali": ["eastafrican"],
    "Sudanese": ["eastafrican"],
    "Ghanaian": ["westafrican"],
    "Angolan": ["southernafrican"],
    "Greek": ["greek"],
    "Portuguese": ["iberian"],
    "Russian": ["russian"],
    "German": ["german"],
    "English": ["anglo"],
    "Romanian": ["romanian"],
    "Hungarian": ["hungarian"],
    "Indonesian": ["indonesian"],
    "Burmese": ["thai"],
    "Nepali": ["indian"],
    "Cuban": ["latino"],
    "Dominican": ["latino"],
    "Israeli": ["israeli"],
    "Afghan": ["persian"],
}

# bucket-level culture defaults (used when no skin-text hint matches)
ETH_CULTURES = {
    "white": ["anglo", "celtic", "french", "german", "dutch", "iberian",
              "italian", "scandinavian", "polish", "czech", "hungarian",
              "romanian", "balkan", "greek", "russian", "baltic", "georgian",
              "armenian"],
    "black": ["westafrican", "eastafrican", "centralafrican", "southernafrican"],
    "asian": ["japanese", "korean", "chinese", "thai", "vietnamese", "filipino",
              "indonesian", "indian", "mongolian", "centralasian"],
    "latin": ["latino", "brazilian"],
    "mena": ["arabic", "persian", "turkic", "israeli"],
    "poly": ["polynesian"],
}


def cultures_for(eth, skin_text=""):
    for prefix, cultures in CULTURE_HINTS.items():
        if skin_text.startswith(prefix):
            return cultures
    return ETH_CULTURES.get(eth, [])


def load(pack=None):
    p = (Path(pack) if pack else PACK) / "manifest.json"
    if not p.exists():
        return {"version": 1, "fighters": {}}
    raw = p.read_text(encoding="utf-8-sig")
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        # torn read from a concurrent write: recover the valid JSON prefix
        try:
            obj, _ = json.JSONDecoder().raw_decode(raw)
            return obj
        except Exception:
            _time.sleep(0.1)
            return json.loads(p.read_text(encoding="utf-8-sig"))


def save(manifest, pack=None):
    """Atomic write (temp + os.replace) so a concurrent reader never sees a
    half-written file."""
    p = (Path(pack) if pack else PACK) / "manifest.json"
    fd, tmp = tempfile.mkstemp(dir=str(p.parent), suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            f.write(json.dumps(manifest, indent=2, ensure_ascii=False))
        os.replace(tmp, p)
    except BaseException:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise


def _bound(s):
    return None if s == "x" else int(s)


def upsert(ident, sex, eth, wmin, wmax, style, tags=None, cultures=None,
           look=None, pack=None):
    """Create/replace a fighter's metadata (images are attached separately).
    look = the rolled sub-ethnicity + skin-tone descriptor, e.g.
    "Afro-Brazilian features, deep bronze-brown skin"."""
    def mut(m):
        m["fighters"][ident] = {
            "sex": sex, "eth": eth,
            "weight": {"min": wmin, "max": wmax},
            "style": style,                      # None = wildcard
            "look": look or "",
            "images": m["fighters"].get(ident, {}).get("images", {}),
            "tags": tags or [],
            "cultures": cultures or ETH_CULTURES.get(eth, []),
        }
        return m["fighters"][ident]
    return locked_update(pack, mut)


def attach_image(ident, stage, kind, filename, pack=None):
    def mut(m):
        f = m["fighters"].get(ident)
        if f is None:
            raise KeyError(f"{ident} not in manifest — upsert() it before attaching images")
        f["images"].setdefault(stage, {})[kind] = filename
    locked_update(pack, mut)


IDENT_RE = re.compile(r"^[mf]_[a-z]+_\d+$")


def slot_base(ident, pack=None):
    """Rebuild the slot string ({ident}_{min}_{max}_{style}) the prompt roller
    consumes, from the manifest. Falls back to open band / wildcard style for
    fighters with no entry."""
    e = load(pack)["fighters"].get(ident)
    if not e:
        return f"{ident}_x_x_x"
    w = e.get("weight") or {}
    lo = "x" if w.get("min") is None else str(w["min"])
    hi = "x" if w.get("max") is None else str(w["max"])
    return f"{ident}_{lo}_{hi}_{e.get('style') or 'x'}"


def remove(ident, pack=None):
    locked_update(pack, lambda m: m["fighters"].pop(ident, None))


def migrate_pack(pack=None):
    """Build/refresh the manifest from legacy token-encoded filenames.
    Idempotent; keeps files where they are. Pulls tags from tags.json and
    cultures from the specs.json skin text where available."""
    pack = Path(pack) if pack else PACK
    tags_file = pack / "tags.json"
    tags = (json.loads(tags_file.read_text(encoding="utf-8-sig"))
            if tags_file.exists() else {})
    specs_file = pack / "specs.json"
    specs = (json.loads(specs_file.read_text(encoding="utf-8-sig"))
             if specs_file.exists() else {})
    m = load(pack)
    seen = 0
    for png in sorted(pack.glob("*.png")):
        t = FILE_RE.match(png.stem)
        if not t:
            continue
        d = t.groupdict()
        ident = f"{d['sex']}_{d['eth']}_{d['id']}"
        base = png.stem.rsplit("_", 2)[0]          # full legacy slot base
        skin = ((specs.get(base) or specs.get(ident) or {}).get("skin", ""))
        if ident not in m["fighters"]:
            style = d["style"] if d["style"] and d["style"] != "x" else None
            m["fighters"][ident] = {
                "sex": d["sex"], "eth": d["eth"],
                "weight": {"min": _bound(d["min"]), "max": _bound(d["max"])},
                "style": style,
                "look": skin,
                "images": {},
                "tags": list(tags.get(base, [])),
                "cultures": cultures_for(d["eth"], skin),
            }
            seen += 1
        elif skin and not m["fighters"][ident].get("look"):
            m["fighters"][ident]["look"] = skin      # backfill on re-migrate
        m["fighters"][ident]["images"].setdefault(d["stage"], {})[d["kind"]] = png.name
    # backfill entries the filename scan can't visit (manifest-era plain names)
    for ident, e in m["fighters"].items():
        if not e.get("look"):
            spec = specs.get(ident) or next(
                (v for k, v in specs.items() if k.startswith(ident + "_")), None)
            if spec and spec.get("skin"):
                e["look"] = spec["skin"]
    save(m, pack)
    return seen, len(m["fighters"])


if __name__ == "__main__":
    new, total = migrate_pack()
    print(f"{PACK.name}: migrated {new} new fighters, manifest now {total}")
