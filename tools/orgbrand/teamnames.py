"""Fight-team / camp name bucket generator.

Produces fictional MMA gym / training-camp names per Region, so the sim can
give a fighter a regionally-plausible home team. Distinct from promotion names
(orgnames.py): camps read like gyms ("Apex Top Team", "Tiger Muay Thai",
"Team Ironside"), not leagues.

Output: tools/orgbrand/team-names.json  ->  { "<Region>": [{"name","abbrev"}] }
Usage:  python tools/orgbrand/teamnames.py [per_region]   (default 55)
Merges on rerun (keeps existing, appends new uniques).
"""
import json
import random
import sys
from pathlib import Path

OUT = Path(__file__).resolve().parent / "team-names.json"

# The "camp" half of the name.
SUFFIXES = [
    ("Top Team", 5), ("MMA", 5), ("Fight Team", 4), ("Academy", 4),
    ("Combat Club", 3), ("Martial Arts", 3), ("Fight Lab", 2), ("BJJ", 2),
    ("Fight Club", 2), ("Training Center", 2), ("Gym", 2), ("Muay Thai", 2),
]
# "Team {Mod}" prefix pattern fires sometimes instead of a suffix.
TEAM_PREFIX_RATE = 0.18

MODIFIERS = {
    "NorthAmerica": ["Apex", "Alpha", "Elite", "Extreme", "Kings", "Syndicate",
        "Renegade", "Warrior", "Iron", "Savage", "Frontline", "Steel City",
        "Blackbelt", "Grit", "Maverick", "Ridgeline", "Anvil", "Redline"],
    "SouthAmerica": ["Nova", "Fenix", "Pitbull", "Tropa", "Favela", "Jungle",
        "Capital", "Guerreiro", "Muralha", "Vale", "Fera", "Raiz", "Selva", "Bravo"],
    "Europe": ["Sparta", "Titan", "Gladiator", "Viking", "Legion", "Ironside",
        "Vanguard", "Sabre", "Crown", "Bulldog", "Valhalla", "Bastion",
        "Sovereign", "Granite", "Wolfpack"],
    "Asia": ["Tiger", "Dragon", "Bushido", "Sakura", "Jade", "Onyx", "Kensei",
        "Typhoon", "Lotus", "Ronin", "Shogun", "Monsoon", "Iron Fist"],
    "Oceania": ["City", "Outback", "Southern", "Reef", "Boomerang", "Coastal",
        "Kanga", "Redback", "Tasman", "Southside", "Barrier"],
    "Africa": ["Lion", "Sahara", "Zulu", "Serengeti", "Golden", "Kilimanjaro",
        "Simba", "Ivory", "Rift", "Ubuntu", "Ashanti", "Baobab"],
    "MiddleEast": ["Desert", "Falcon", "Nomad", "Oasis", "Dune", "Scimitar",
        "Sultan", "Cedar", "Sabre", "Mirage", "Peninsula", "Sandstorm"],
}

STOPWORDS = {"of", "the", "and"}
# Real camps/teams and promotion abbreviations to never mint.
REAL_ABBREVS = {"ATT", "AKA", "TAM", "GB", "UFC", "PFL", "ONE", "AMC", "AOKA"}


def _pick(pool, rng):
    items, weights = zip(*pool)
    return rng.choices(items, weights)[0]


def abbrev(name):
    words = [w for w in name.replace("-", " ").split() if w.lower() not in STOPWORDS]
    return "".join(w[0].upper() for w in words if w[:1].isalpha())[:5]


def gen_region(region, count, rng, taken_names, taken_ab):
    mods = MODIFIERS[region]
    out, attempts = [], 0
    while len(out) < count and attempts < count * 40:
        attempts += 1
        mod = rng.choice(mods)
        if rng.random() < TEAM_PREFIX_RATE:
            name = f"Team {mod}"
        else:
            name = f"{mod} {_pick(SUFFIXES, rng)}"
        if name in taken_names:
            continue
        ab = abbrev(name)
        if len(ab) < 2 or ab in taken_ab or ab in REAL_ABBREVS:
            continue
        taken_names.add(name); taken_ab.add(ab)
        out.append({"name": name, "abbrev": ab})
    return out


def main():
    per = int(sys.argv[1]) if len(sys.argv) > 1 else 55
    rng = random.Random(880)
    existing = json.loads(OUT.read_text(encoding="utf-8-sig")) if OUT.exists() else {}
    taken_names, taken_ab = set(REAL_ABBREVS), set(REAL_ABBREVS)
    for lst in existing.values():
        for e in lst:
            taken_names.add(e["name"]); taken_ab.add(e["abbrev"])
    result = {r: list(existing.get(r, [])) for r in MODIFIERS}
    for region in MODIFIERS:
        need = per - len(result[region])
        if need > 0:
            result[region] += gen_region(region, need, rng, taken_names, taken_ab)
    OUT.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
    total = sum(len(v) for v in result.values())
    print(f"team-names.json: {total} team names across {len(result)} regions")
    for r, lst in result.items():
        print(f"  {r}: {len(lst)}  e.g. " + ", ".join(f"{e['name']} ({e['abbrev']})" for e in lst[:3]))


if __name__ == "__main__":
    main()
