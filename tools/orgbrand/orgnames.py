"""Org-name bucket generator.

Produces a reviewable pool of fictional MMA promotion names per Region, so the
sim can draw a regionally-flavoured name when it spawns a new AI promotion.
Parallel to the fighter-portrait buckets: generate -> (human review/edit the
JSON) -> the game consumes approved entries, removing each as it's used.

Names follow the designed-roster house style ({Modifier} {Fight-word}, e.g.
"Apex Fighting Championship") and abbreviations are the word initials. Output:
  tools/orgbrand/names.json  ->  { "<Region>": [ {"name","abbrev"}... ] }

Usage:  python tools/orgbrand/orgnames.py [per_region]   (default 60)
Deterministic (fixed seed) so re-runs are stable; edits to names.json survive
because the writer MERGES (keeps existing entries, only appends new uniques).
"""
import json
import random
import sys
from pathlib import Path

OUT = Path(__file__).resolve().parent / "names.json"

# Fight-words: the generic half of a promotion name. "full" pair with a single
# modifier ("Apex Fighting Championship"); "short" read well after a place
# ("Rio Combat"). Weighted toward the sober end — the designed roster is.
FIGHTWORDS_FULL = [
    ("Fighting Championship", 5), ("Fighting Alliance", 3), ("Fight League", 4),
    ("Fighting Federation", 3), ("Combat League", 3), ("Fighting Network", 2),
    ("Combat Sport", 2), ("Mixed Martial Arts", 2), ("Cage Championship", 2),
    ("Fighting Series", 2), ("Combat Alliance", 2), ("Fight Series", 2),
]
FIGHTWORDS_SHORT = [
    ("Combat", 4), ("MMA", 5), ("Fights", 3), ("Cage", 3), ("FC", 4),
    ("Fight Club", 2), ("Combatives", 1), ("Fight Nights", 2), ("Arena", 2),
]

# Region modifier pools — invented flavour words, no real promotions.
MODIFIERS = {
    "NorthAmerica": ["Apex", "Vanguard", "Frontier", "Liberty", "Coastal",
        "Summit", "Empire", "Ironclad", "Maverick", "Rampart", "Blackstone",
        "Coliseum", "Meridian", "Sentinel", "Patriot", "Redline", "Bedrock",
        "Highrise", "Crossfire", "Keystone", "Titan", "Anvil"],
    "SouthAmerica": ["Rio", "Amazonia", "Copa", "Furia", "Guerreiro", "Selva",
        "Andes", "Pampas", "Tropa", "Cordillera", "Capoeira", "Tijuca",
        "Vale Tudo", "Bravo", "Sol", "Toro", "Fenix", "Jaguar", "Chaco"],
    "Europe": ["Vanguard", "Ironhold", "Sovereign", "Continental", "Crown",
        "Legion", "Bastion", "Meridian", "Valor", "Sterling", "Regent",
        "Albion", "Nordkraft", "Vortex", "Citadel", "Hanseatic", "Carpath",
        "Zenith", "Gladius", "Baltic", "Alpine"],
    "Asia": ["Iron Circle", "Dragon", "Rising Sun", "Tiger", "Jade", "Bushido",
        "Sakura", "Onyx", "Dynasty", "Kensei", "Shogun", "Lotus", "Typhoon",
        "Vermilion", "Silk Road", "Monsoon", "Ronin", "Celestial", "Garuda"],
    "Oceania": ["Southern Cross", "Outback", "Coral", "Reef", "Antipodean",
        "Tasman", "Boomerang", "Sunburnt", "Kanga", "Redback", "Coral Sea",
        "Barrier", "Southlands", "Kiwi", "Corroboree", "Longboard"],
    "Africa": ["Sahara", "Serengeti", "Savanna", "Kilimanjaro", "Nile",
        "Cradle", "Ubuntu", "Baobab", "Sahel", "Kalahari", "Ivory", "Zambezi",
        "Ashanti", "Atlas", "Rift Valley", "Simba", "Golden Lion", "Congo"],
    "MiddleEast": ["Gulf", "Desert", "Falcon", "Oasis", "Sultan", "Dune",
        "Scimitar", "Mirage", "Caliph", "Cedar", "Zenith", "Levant", "Sandstorm",
        "Crescent", "Peninsula", "Saffron", "Dinar", "Sabre", "Persia"],
}

STOPWORDS = {"of", "the", "and", "de", "da"}

# Real, notable MMA promotion abbreviations — never mint a fictional org that
# collides. (AFC is the game's own designed flagship; not a real-world brand.)
REAL_ABBREVS = {"UFC", "PFL", "ONE", "WEC", "KSW", "RIZIN"}


def _pick(pool, rng):
    items, weights = zip(*pool)
    return rng.choices(items, weights)[0]


def abbrev(name):
    words = [w for w in name.replace("-", " ").split() if w.lower() not in STOPWORDS]
    letters = [w[0].upper() for w in words if w[:1].isalpha()]
    # MMA/FC etc. are already acronyms — keep them whole if they're the tail
    ac = "".join(letters)
    return ac[:5]


def gen_region(region, count, rng, taken_names, taken_abbrevs):
    mods = MODIFIERS[region]
    out = []
    attempts = 0
    while len(out) < count and attempts < count * 40:
        attempts += 1
        mod = rng.choice(mods)
        if rng.random() < 0.65:
            word = _pick(FIGHTWORDS_FULL, rng)
        else:
            word = _pick(FIGHTWORDS_SHORT, rng)
        name = f"{mod} {word}"
        if name in taken_names:
            continue
        ab = abbrev(name)
        if len(ab) < 2 or ab in taken_abbrevs or ab in REAL_ABBREVS:
            continue
        taken_names.add(name)
        taken_abbrevs.add(ab)
        out.append({"name": name, "abbrev": ab})
    return out


# Designed-roster names/abbrevs to avoid colliding with (WorldBuilder).
DESIGNED = [
    ("Apex Fighting Championship", "AFC"), ("Vanguard MMA", "VMMA"),
    ("Ironhold Fighting Alliance", "IFA"), ("Iron Circle FC", "ICF"),
    ("Gulf Fighting Championship", "GFC"), ("United Fight League", "UFL"),
    ("Rio Combat Sport", "RCS"), ("Southern Cross MMA", "SCM"),
    ("Cage Kings", "CK"), ("Coastal Combat", "CC"), ("Sahara Combat League", "SCL"),
]


def main():
    per = int(sys.argv[1]) if len(sys.argv) > 1 else 60
    rng = random.Random(770)
    existing = json.loads(OUT.read_text(encoding="utf-8-sig")) if OUT.exists() else {}

    taken_names = {n for n, _ in DESIGNED}
    taken_abbrevs = {a for _, a in DESIGNED}
    for lst in existing.values():
        for e in lst:
            taken_names.add(e["name"]); taken_abbrevs.add(e["abbrev"])

    result = {r: list(existing.get(r, [])) for r in MODIFIERS}
    for region in MODIFIERS:
        need = per - len(result[region])
        if need > 0:
            result[region] += gen_region(region, need, rng, taken_names, taken_abbrevs)

    OUT.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
    total = sum(len(v) for v in result.values())
    print(f"orgbrand/names.json: {total} names across {len(result)} regions")
    for r, lst in result.items():
        print(f"  {r}: {len(lst)}  e.g. " + ", ".join(f"{e['name']} ({e['abbrev']})" for e in lst[:3]))


if __name__ == "__main__":
    main()
