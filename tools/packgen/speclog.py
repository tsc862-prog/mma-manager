"""Spec + decline bookkeeping for the fighter pack.

specs.json      base -> the traits/gear prompt fragments that produced the
                CURRENT image in that slot (overwritten on each regen).
decline-log.jsonl  append-only history: one line per decline, carrying the
                reviewer's note AND the spec that produced the rejected image —
                the raw material for prompt-improvement analysis.
"""
import json
import os
from pathlib import Path

PACK = Path(os.environ.get("MMA_PACK_DIR", r"e:\MMA Manager\generated-pack"))
SPECS = PACK / "specs.json"
DECLINES = PACK / "decline-log.jsonl"


def record_spec(base, traits, gear, glove=None, skin=None):
    # utf-8-sig: tolerate a BOM (PowerShell 5.1's `-Encoding utf8` writes one)
    specs = json.loads(SPECS.read_text(encoding="utf-8-sig")) if SPECS.exists() else {}
    specs[base] = {"traits": traits, "gear": gear}
    if glove:
        specs[base]["glove"] = glove     # added by the finalize edit, not generated
    if skin:
        specs[base]["skin"] = skin       # re-stated per panel in triptych prompts
    SPECS.write_text(json.dumps(specs, indent=2, ensure_ascii=False), encoding="utf-8")


def get_spec(base):
    if SPECS.exists():
        return json.loads(SPECS.read_text(encoding="utf-8-sig")).get(base)
    return None


def log_decline(base, note, kind="decline"):
    entry = {"base": base, "kind": kind, "note": note, "spec": get_spec(base)}
    with DECLINES.open("a", encoding="utf-8") as f:
        f.write(json.dumps(entry, ensure_ascii=False) + "\n")


def decline_count():
    if not DECLINES.exists():
        return 0
    return sum(1 for line in DECLINES.read_text(encoding="utf-8").splitlines() if line.strip())
