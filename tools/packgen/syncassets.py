"""Sync APPROVED pack fighters into the game's bundled portrait assets.

Manifest era: each pack's manifest.json is the source of truth. This merges
every approved fighter from every listed pack into Wpf/Assets/Painted (the
folder PaintedLibrary loads): copies their images, writes a merged bundle
manifest.json, and a derived tags.json (compat for the game's tag hooks).

INCREMENTAL: only files that changed are copied and un-approved fighters'
files are pruned, so it's cheap to run continuously (see syncwatch.py). It
also MIRRORS into the build output dir when present, so a game relaunch picks
up new fighters WITHOUT a rebuild.

Usage:  MMA_PACK_DIR=<pack dir, or several ";"-separated>  python syncassets.py
Callable:  from syncassets import sync; sync([Path(...), ...])
"""
import json
import os
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import packmeta

ROOT = Path(__file__).resolve().parent.parent.parent
ASSETS = ROOT / "Wpf" / "Assets" / "Painted"
OUTPUT_ASSETS = ROOT / "Wpf" / "bin" / "Debug" / "net8.0-windows" / "Assets" / "Painted"


def _build_bundle(pack_dirs):
    bundle = {"version": 1, "fighters": {}}
    src_files = {}          # filename -> source path
    skipped = []
    for pack in pack_dirs:
        review_file = pack / "review.json"
        review = (json.loads(review_file.read_text(encoding="utf-8-sig"))
                  if review_file.exists() else {})
        fighters = packmeta.load(pack)["fighters"]
        approved = sorted(i for i, e in review.items() if e.get("status") == "approved")
        for ident in approved:
            entry = fighters.get(ident)
            if entry is None:
                skipped.append((pack.name, ident, "no manifest entry")); continue
            imgs = entry.get("images", {})
            wanted = [(fn, pack / fn) for stage in imgs.values() for fn in stage.values()]
            if not ("p" in imgs
                    and all(set(v) == {"face", "body"} for v in imgs.values())
                    and all(p.exists() for _, p in wanted)):
                skipped.append((pack.name, ident, "incomplete images")); continue
            if ident in bundle["fighters"]:
                skipped.append((pack.name, ident, "ident collision")); continue
            bundle["fighters"][ident] = entry
            for fn, p in wanted:
                src_files[fn] = p
    return bundle, src_files, skipped


def _mirror(dest, src_files, bundle, tags, best_effort=False):
    """Make `dest` exactly match src_files + manifest + tags, incrementally."""
    dest.mkdir(parents=True, exist_ok=True)
    copied = pruned = 0
    for fn, src in src_files.items():
        d = dest / fn
        try:
            if not d.exists() or d.stat().st_mtime < src.stat().st_mtime:
                shutil.copy2(src, d); copied += 1
        except OSError:
            if not best_effort:
                raise
    keep = set(src_files)
    for png in dest.glob("*.png"):
        if png.name not in keep:
            try:
                png.unlink(); pruned += 1
            except OSError:
                if not best_effort:
                    raise
    try:
        (dest / "manifest.json").write_text(
            json.dumps(bundle, indent=2, ensure_ascii=False), encoding="utf-8")
        (dest / "tags.json").write_text(
            json.dumps(tags, indent=2, ensure_ascii=False), encoding="utf-8")
    except OSError:
        if not best_effort:
            raise
    return copied, pruned


def sync(pack_dirs, mirror_output=True, verbose=True):
    """Mirror approved fighters into Wpf/Assets/Painted (+ build output).
    Returns a stats dict. Incremental — safe to call frequently."""
    bundle, src_files, skipped = _build_bundle(pack_dirs)
    tags = {i: e["tags"] for i, e in bundle["fighters"].items() if e.get("tags")}
    copied, pruned = _mirror(ASSETS, src_files, bundle, tags)
    out_copied = 0
    if mirror_output and OUTPUT_ASSETS.parent.parent.exists():   # game has been built
        out_copied, _ = _mirror(OUTPUT_ASSETS, src_files, bundle, tags, best_effort=True)
    stats = {"fighters": len(bundle["fighters"]), "tagged": len(tags),
             "copied": copied, "pruned": pruned, "output_copied": out_copied,
             "skipped": len(skipped)}
    if verbose:
        print(f"shipped {stats['fighters']} fighters ({stats['tagged']} tagged) -> {ASSETS}")
        print(f"  copied {copied}, pruned {pruned}"
              + (f", mirrored {out_copied} to build output" if mirror_output else ""))
        for pk, ident, why in skipped[:10]:
            print(f"  SKIP {pk}/{ident}: {why}")
    return stats


if __name__ == "__main__":
    _default = ROOT / "generated-pack"
    packs = [Path(p) for p in os.environ.get("MMA_PACK_DIR", str(_default)).split(";") if p.strip()]
    sync(packs)
