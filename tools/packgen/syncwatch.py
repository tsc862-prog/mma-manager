"""Auto-sync daemon for fighter portraits.

Watches the packs' APPROVED fighters and mirrors them into the game assets the
moment approvals change — the sync counterpart to watcher.py (which auto-
processes declines). Uses the incremental syncassets.sync, so each pass only
copies what changed and prunes what's no longer approved; it also mirrors into
the build output so a game RELAUNCH shows new fighters without a rebuild.

Run:  python tools/packgen/syncwatch.py
      (defaults to both fighter packs; override with MMA_PACK_DIR="a;b")
"""
import json
import os
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import syncassets
import packmeta

INTERVAL = 60      # seconds between checks


def default_packs():
    root = Path(__file__).resolve().parent.parent.parent
    return [root / "generated-pack-realistic", root / "generated-pack-generic"]


def signature(packs):
    """Approved idents + their prime-body mtime, so BOTH new approvals and
    re-rolls of an approved fighter trigger a re-sync."""
    sig = []
    for pack in packs:
        rf = pack / "review.json"
        if not rf.exists():
            continue
        review = json.loads(rf.read_text(encoding="utf-8-sig"))
        fighters = packmeta.load(pack)["fighters"]
        for ident, e in review.items():
            if e.get("status") != "approved":
                continue
            body = fighters.get(ident, {}).get("images", {}).get("p", {}).get("body")
            mt = 0
            if body and (pack / body).exists():
                mt = int((pack / body).stat().st_mtime)
            sig.append((pack.name, ident, mt))
    return tuple(sorted(sig))


def main():
    env = os.environ.get("MMA_PACK_DIR", "")
    packs = [Path(p) for p in env.split(";") if p.strip()] or default_packs()
    print(f"SYNCWATCH up - watching {len(packs)} packs every {INTERVAL}s", flush=True)
    last = None
    while True:
        try:
            sig = signature(packs)
            if sig != last:
                stats = syncassets.sync(packs, mirror_output=True, verbose=False)
                print(f"[{time.strftime('%H:%M:%S')}] synced {stats['fighters']} approved "
                      f"(+{stats['copied']} new, -{stats['pruned']} pruned, "
                      f"{stats['output_copied']} to build output)", flush=True)
                last = sig
        except Exception as ex:
            print(f"SYNCWATCH error: {str(ex)[:200]}", flush=True)
        time.sleep(INTERVAL)


if __name__ == "__main__":
    main()
