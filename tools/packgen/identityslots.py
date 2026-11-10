"""Slot-based identity randomizer for the fighter pack.

A pack filename base like  m_white_20_170_195_wr  defines a SLOT
(sex, ethnicity, weight band, fighting style). This module rolls a brand-new
random PERSON into a slot: fresh hair/beard/features/expression/gear each call.
Used when a reviewer declines a fighter (decline = new person, not a fix).
"""
import random
import re

SLOT_RE = re.compile(r"^(?P<sex>[mf])_(?P<eth>[a-z]+)_(?P<id>\d+)_(?P<min>\d+|x)_(?P<max>\d+|x)(?:_(?P<style>[a-z]+))?$")

# Specific sub-ethnicities per loader bucket (reviewer request): the filename
# token stays the coarse bucket (the game only knows Region), but prompts name
# real ethnic looks for authentic facial variety. Stored in specs.json via the
# traits string, ready if the game ever gains country-level nationality.
# Concrete, high-contrast tone anchors (reviewer: "everyone looks the same").
# Vague words like "tan" collapse to one generic mid-tone, especially in cel
# shading — named pigments render distinctly.
ETH_SKIN = {
    "white": ["Northern European features, cool pale ivory skin",
              "Scandinavian features, very fair porcelain skin",
              "Irish features, milky fair skin dense with freckles",
              "Eastern European Slavic features, fair neutral-beige skin",
              "Mediterranean features, warm golden olive skin",
              "Southern Italian features, sun-weathered bronze-olive skin",
              "ruddy British Isles features, pink-flushed pale skin",
              "Dagestani highlander features, fair weathered skin, a thick dark brow",
              "Georgian highlander features, olive-fair skin",
              "French features, light warm-fair skin",
              "Balkan features, olive-fair skin, dark hair",
              "Baltic features, very fair cool-toned skin",
              "Armenian features, olive skin, strong dark brows",
              "Greek features, warm olive skin, dark curly hair",
              "Portuguese features, warm light-olive skin",
              "Russian features, fair skin, broad cheekbones",
              "German features, fair ruddy skin, strong jaw",
              "English features, fair skin with a light tan",       # anglo
              "Romanian features, olive-fair skin, dark hair",      # was zero
              "Hungarian features, fair skin, broad cheekbones"],   # was zero
    "black": ["West African features, very deep ebony skin with cool undertones",
              "Nigerian features, blue-black darkest ebony skin",
              "East African features, dark espresso brown skin",
              "Senegalese features, deep matte onyx skin",
              "African-American features, warm medium chestnut skin",
              "Caribbean features, rich mahogany brown skin",
              "Ethiopian features, reddish copper-brown skin",
              "Congolese features, very dark cool ebony skin",
              "South African Zulu features, deep warm brown skin",
              "Cameroonian features, rich dark mahogany skin",
              "Somali features, slender features, deep coffee-brown skin",
              "Sudanese features, very dark blue-black skin",
              "Ghanaian features, warm dark umber skin",
              "Angolan features, deep bronze-brown skin"],
    "asian": ["Japanese features, cool light ivory skin",
              "Korean features, pale porcelain skin with neutral undertones",
              "Han Chinese features, warm light beige skin",
              "Thai features, warm honey-brown skin",
              "Filipino features, golden morena brown skin",
              "Vietnamese features, light golden-tan skin",
              "Mongolian features, broad face, weathered ochre-tan skin",
              "Kazakh steppe features, broad high cheekbones, warm tan skin",
              "Uzbek features, light golden-tan skin",
              "Indian South Asian features, deep warm sienna-brown skin",
              "Sri Lankan features, dark umber-brown skin",
              "Indonesian features, warm golden-brown skin",
              "Burmese features, golden tan skin",
              "Nepali features, warm wheat-tan skin"],
    "latin": ["Mexican mestizo features, warm caramel-tan skin",
              "Brazilian features, sun-kissed amber-brown skin",
              "Afro-Brazilian features, deep bronze-brown skin",
              "Argentinian features, lightly tanned warm-fair skin",
              "Colombian features, rich bronze skin",
              "Peruvian Andean features, earthy terracotta-brown skin",
              "Cuban features, warm tan skin",
              "Dominican features, caramel-brown skin"],
    # New geography (WorldAtlas): Middle East + North Africa, and the Pacific
    "mena": ["Gulf Arab features, warm olive-bronze skin",
             "Levantine features, warm light-olive skin",
             "Moroccan Berber features, tawny golden-brown skin",
             "Egyptian features, warm sand-brown skin",
             "Persian features, light warm-olive skin",
             "Turkish features, warm olive skin",
             "Kurdish features, olive-tan skin",
             "Israeli features, light olive skin",
             "Afghan features, weathered olive-tan skin"],
    "poly": ["Samoan features, a broad heavy build, warm golden-brown skin",
             "Maori features, bronze-brown skin",
             "Tongan features, deep golden-brown skin",
             "Fijian features, deep brown skin",
             "Papua New Guinean Melanesian features, deep dark-brown skin"],
}
DARK_HAIR_ETH = {"black", "asian", "latin", "mena", "poly"}
# Even dark-hair ethnicities aren't all identical black — a richer dark palette
# breaks the biggest "too similar" cluster (dark-haired men). No grey here
# (grey is age-gated elsewhere; base grey drew "too much grey" declines).
DARK_HAIR_COLORS = ["jet black", "black", "soft black"]
# ALL brown removed from the dark-ethnicity pool (reviewer: "brown to black
# hair" is the single most persistent decline across many sessions). Even
# "dark brown" youth drifted to black on the larger prime figure. Black-family
# only = nothing to drift FROM, and dark-ethnicity fighters read as black-haired
# realistically anyway. Browns/blonds/reds stay in HAIR_COLORS_ANY for light
# ethnicities where they're plausible and don't darken.
HAIR_COLORS_ANY = ["black", "dark brown", "brown", "light brown", "ash brown",
                   "sandy blond", "dirty blond", "ginger red", "auburn",
                   "strawberry blond"]

MALE_HAIR = ["shaved bald head", "short buzz cut", "short cropped {c} hair", "short faded {c} hair",
             "curly {c} crop", "{c} cornrows", "short {c} dreadlocks", "{c} hair in a small top knot",
             "slicked back {c} hair", "receding short {c} hair", "medium length {c} hair",
             "{c} hair in a man bun", "short {c} twists", "a {c} mohawk fade",
             "longer {c} hair tied back", "{c} curtain hair parted in the middle",
             "a bleached blond buzz cut", "wild untamed {c} hair",
             "a tall bold {c} mohawk with shaved sides", "a wild spiked {c} faux-hawk",
             # added for diversity (reviewer: "too similar")
             "a high and tight haircut", "a textured {c} crop with a skin fade",
             "a {c} comb-over fade", "long flowing {c} hair down to the shoulders",
             "a rounded {c} afro", "short {c} finger waves", "a neat {c} quiff",
             "a tidy {c} caesar cut with a straight fringe", "a {c} side part",
             "thick {c} hair swept back", "a {c} high-top fade",
             "{c} box braids pulled into a bun", "a bald head with a thick beard-line"]
FEMALE_HAIR = ["{c} hair in a tight high bun",                       # buns capped at 2 of 16
               "{c} cornrow braids into a low bun",
               "{c} hair in a tight ponytail", "a high {c} ponytail",
               "tight {c} braids pulled straight back", "twin {c} french braids",
               "a thick {c} braid over one shoulder", "a long {c} braided ponytail",
               "short {c} pixie cut", "an asymmetric short {c} cut",
               "a short practical {c} bob tucked behind the ears",
               "a chin-length loose {c} bob", "loose wavy shoulder-length {c} hair",
               "a short natural {c} curly crop", "short {c} dreadlocks",
               "slicked-back short {c} hair with an undercut",
               "a {c} mohawk with shaved sides",
               "an undercut with longer {c} hair swept hard to one side",
               # added for diversity
               "long straight {c} hair in a high ponytail", "{c} box braids",
               "a {c} afro puff", "{c} bantu knots", "a {c} half-up top knot",
               "shoulder-length {c} waves", "a sleek {c} low bun",
               "{c} micro braids gathered back", "a curly {c} shoulder-length shag"]
MALE_FACIAL = ["clean shaven", "light stubble", "heavy stubble", "a short beard", "a full beard",
               "a black goatee", "a thin moustache", "a chin strap beard",
               "a horseshoe moustache", "thick mutton chop sideburns", "a soul patch",
               "a neatly trimmed boxed beard", "a wild unkempt beard",
               # added for diversity
               "a van dyke beard", "an extended goatee", "a five o'clock shadow",
               "a circle beard", "a long full beard", "a pencil moustache",
               "a short ducktail beard", "a stubble beard with a faded neckline",
               "long bushy sideburns", "a thick moustache and soul patch"]
FACES = ["strong square jaw", "narrow angular face", "round heavy face", "sharp cheekbones",
         "heavy brow and deep-set eyes", "broad flat nose and wide jaw", "long face with a pointed chin",
         "high forehead and hollow cheeks", "wide-set eyes and a soft jawline",
         "prominent chin and thin lips", "small close-set eyes and a thick neck",
         "large expressive eyes and full lips", "asymmetric rugged face", "gaunt drawn face",
         # added for diversity
         "an oval face with balanced features", "a chiseled angular face",
         "a broad open friendly face", "a weathered deeply-lined face",
         "a diamond face with sharp cheekbones", "a heavy-jawed brawler's face",
         "a lean hollow-cheeked face", "a wide square face with a heavy brow",
         "a boyish rounded face", "a craggy uneven face with a strong nose"]
EXPRESSIONS = ["calm confident expression", "fierce focused expression", "relaxed neutral expression",
               "hard intense stare", "stoic unreadable expression", "a cocky half-smile",
               "an intensely focused gaze", "a gentle easygoing look", "a grim determined set to the mouth",
               "a wary watchful expression",
               # added for diversity
               "a slight knowing smirk", "a proud lifted chin", "a weary seasoned look",
               "a bright open smile", "a cold hard glare", "a quietly amused look"]
# "a battle-focused squint" removed: rendered as CLOSED eyes (reviewer x4);
# "both eyes open" in the prompt wasn't enough to override "squint"
# "heavy-lidded sleepy eyes" removed: photoreal renders it as closed eyes
# (three "closed eyes" declines, all had rolled it)
EYES = ["narrow hooded eyes", "sharp almond eyes", "deep-set dark eyes", "wide alert eyes",
        "calm steady heavy-browed eyes", "piercing light eyes",
        # added for diversity
        "warm brown eyes", "sharp hazel eyes", "round expressive eyes",
        "narrow foxlike eyes", "bright green eyes", "steely grey eyes"]

# The nose is the single biggest driver of facial identity and renders clearly
# at portrait scale, yet the face was built from shape+eyes only -> the model
# fell back to a per-ethnicity "average nose" and every fighter converged
# (reviewer: "too similar" x11). Name a concrete nose on EVERY face.
NOSES = ["a broad flat nose", "a wide fleshy nose", "a long straight nose",
         "a sharp aquiline hooked nose", "a small neat button nose",
         "a crooked nose broken more than once", "a bulbous rounded nose",
         "a strong Roman nose with a high bridge", "a short upturned nose",
         "a thin narrow nose", "a wide-bridged flattened boxer's nose",
         "a prominent beaked nose", "an average straight nose",
         "a squat pug nose"]
# Complexion / skin texture — macro details that survive the small render and
# individualize faces (freckles, ruddiness, scarring, weathering). Applied to
# MOST faces; some left plain. Positive imagery only (no negations).
COMPLEXIONS = ["skin dotted with freckles", "a ruddy weather-beaten complexion",
               "skin pitted with old acne scars", "a smooth clear even complexion",
               "leathery sun-weathered skin", "a pale washed-out complexion",
               "a scattering of dark moles across the cheek",
               "a blotchy uneven complexion", "deep nasolabial lines and a lived-in face",
               "puffy dark under-eye circles", "sallow tired-looking skin",
               "a flushed sweaty sheen", "coarse thick pored skin",
               "a gap between the front teeth"]

# Reviewer-tuned twice. Round 1: no hypertrophy adjectives, no dramatic scar
# wording, mature faces. Round 2 (A/B tested): negations DON'T beat the
# model's shredded-fighter prior — concrete body imagery does ("rugby prop"
# and "dad bod" tests held; vague "ordinary man" still grew abs). So styles
# contribute FEATURES only (ears/scars/neck), and the body comes from a
# physique TIER anchored to an unmistakable concrete image.
# No scar/wound wording anywhere — reviewer: models turn any damage mention
# into blood, scratches and head tattoos. Style reads come from bone
# structure, ears and build only.
STYLE_TRAITS = {
    "wr": "a thick neck and cauliflower ears",
    "gr": "thick forearms and cauliflower ears",
    "bjj": "a flexible frame and slight cauliflower ears",
    "st": "a sharp watchful boxer's face",
    "kb": "strong calves",
    "cl": "long arms and a strong neck",
    "br": "a slightly crooked flattened nose and a weathered face",
    "wl": "an unremarkable appearance",
    "x": "an unremarkable appearance",   # wildcard slot (generic fighters)
}
THAI_STYLES = {"kb", "cl"}
# NOTE: no anti-tattoo wording here — the tattoo roll appends either the
# fighter's tattoo or an untattooed clause (they used to conflict, which
# suppressed most rolled tattoos).
NATURAL = "mature adult face weathered by years of training, healthy skin"

# (tier text, weight) — concrete comparisons, sampled per fighter. Heavier
# divisions skew to the last tiers, lighter to the first (see roll_person).
# POSITIVE imagery only: the old soft-tier texts leaned on negations ("no
# six-pack", "undefined stomach") which photoreal reads as their opposites —
# reviewer: "all the guys are muscles/ripped". Name what IS there instead.
# Reviewer 2026-08-06: "everyone is muscle-builder looking - plenty of NORMAL
# looking mma fighters exist." So genuinely-ripped is now a rare low-weight
# minority and the ordinary/normal builds dominate the middle. All POSITIVE
# imagery (a "smooth flat stomach" reads plain; "no abs" grows abs).
PHYSIQUE_TIERS = [
    ("the slim wiry body of a distance runner - thin light arms, a narrow "
     "chest and a flat smooth stomach", 1.1),
    ("a lean lanky build with long thin limbs, a narrow frame and a smooth "
     "flat chest, naturally slight like a tall teenager", 1.1),
    ("the long smooth build of a casual swimmer - soft gentle muscle under a "
     "smooth surface and a smooth flat stomach", 1.3),
    ("the plain ordinary build of a regular working person - a smooth flat "
     "stomach, sloping relaxed shoulders and average everyday arms", 1.8),
    ("a compact everyday-fit build with light natural muscle tone, a smooth "
     "flat stomach and the practical shape of a recreational gym-goer", 1.0),
    ("a genuinely athletic fighter's build with clear muscle definition and "
     "visible abs", 0.45),
    ("a stocky thick-set strongman build - thick and powerful with a solid "
     "smooth heavy layer over the muscle", 1.1),
    ("built like a rugby prop forward - thick, heavy, sturdy and smooth all "
     "over", 1.0),
    ("a soft heavyset working-man's body carrying a round belly, round "
     "shoulders and thick soft arms", 1.0),
    ("the massive burly frame of a super heavyweight - a huge thick torso, "
     "a heavy solid gut, enormous rounded shoulders and tree-trunk legs, "
     "clearly well over 250 pounds", 1.0),
]

# Signature quirks (~22% of fighters, ONE each): memorable details that make
# an individual recognizable. MUST read at full-body scale — the face is only
# ~90px in the source render, so micro-details (eyebrow slits, nose studs,
# heterochromia) simply don't render (A/B tested and invisible). Body
# proportions and hair-level features survive; damage words summon blood.
QUIRKS = [
    ("an unusually tall long-limbed lanky frame", 1.2, None),
    ("a compact short stocky frame with a low center of gravity", 1.2, None),
    # dye quirks: pale-blond wording removed — "platinum"/"silver" kept
    # reading as GREY/aged (reviewer: "too much grey", "too many white/grey").
    # Only SATURATED colors survive; two-tone split-dye cut (reviewer: "two
    # tones of hair" declines).
    ("hair dyed a vivid electric blue", 0.45, None),
    ("hair dyed bright flamingo pink", 0.45, None),
    ("hair dyed a deep vivid crimson", 0.45, None),
    ("hair dyed a bright emerald green", 0.4, None),
    ("a dense band of freckles across the nose and cheeks", 1.0, None),
    ("a strong single unibrow", 0.7, "m"),
    ("noticeably protruding ears", 0.7, None),
    ("a sharp widow's peak hairline", 0.8, None),
    ("deep natural dimples", 0.6, None),
    # "bleached pale-blond eyebrows" read as GREY on dark hair (reviewer)
]

# Simple tattoos (reviewer request): mostly shoulder/chest, VERY rarely face.
# ~72% of fighters have none. Designs stay minimal so anime lineart keeps them clean.
# solid-fill designs ("plain solid armband", "black tribal band") rendered as
# featureless dark BLOBS (reviewer: "generic ink blob") — replaced with
# line-based designs that keep detail at portrait scale
TATTOO_DESIGNS = ["a minimalist geometric line pattern", "a small line-art cross",
                  "a thin linework rose", "a small compass rose",
                  "an anchor with rope detail", "a pair of clean line-art swallows",
                  "a small crescent moon and stars", "a simple mountain-range outline"]

# Themed flair (reviewer request): tattoos may represent FIGHTING or HOME
# COUNTRY. Imagery only — lettering/script renders as gibberish text.
# Reviewer: "same lion head keeps showing up" — variety comes from the
# PRODUCT motif x art style x explicit placement (~1300 combos), because the
# model renders a bare motif name nearly identically every time.
FIGHT_TATTOOS = ["a snarling wolf head", "a roaring lion head",
                 "two crossed boxing gloves", "a coiled striking cobra",
                 "a screaming eagle with talons out", "a rising phoenix",
                 "a fierce bulldog head", "a leaping tiger",
                 "a howling wolf silhouette", "a charging bull head",
                 "a crawling black panther", "a spartan helmet",
                 "a samurai helmet", "a coiled scorpion",
                 "a circling shark", "a crowned king cobra hood",
                 "a griffin with spread wings", "a snarling bear head"]

TATTOO_ARTSTYLES = ["bold heavy blackwork", "delicate fine-line",
                    "classic americana traditional, thick outlines",
                    "soft gray shaded realism", "sharp geometric linework",
                    "stippled dotwork"]

# rolled sub-ethnicity prefix -> heritage motif (matched like CULTURE_HINTS)
HERITAGE_TATTOOS = {
    "Samoan": "traditional Samoan tribal bands of dense geometric patterns",
    "Maori": "Maori-style koru spiral patterns",
    "Tongan": "traditional Polynesian tribal geometric bands",
    "Fijian": "traditional Polynesian tribal geometric bands",
    "Papua New Guinean": "traditional Melanesian geometric patterns",
    "Japanese": "a koi fish swimming through stylized waves",
    "Han Chinese": "a long coiling dragon",
    "Thai": "a traditional sak yant geometric yantra design",
    "Burmese": "a traditional sak yant geometric yantra design",
    "Irish": "an intricate Celtic knotwork band",
    "ruddy British Isles": "an intricate Celtic knotwork band",
    "Scandinavian": "Norse runic bands around a longship silhouette",
    "Northern European": "Norse runic band patterns",
    "Mexican mestizo": "an Aztec eagle-warrior motif",
    "Brazilian": "a fierce jaguar head",
    "Afro-Brazilian": "a fierce jaguar head",
    "Cuban": "a crowing rooster in bold linework",
    "Eastern European Slavic": "a roaring bear head",
    "Dagestani": "a mountain eagle with spread wings",
    "Georgian highlander": "a mountain range silhouette under a sun",
    "Armenian": "a mountain range silhouette under a sun",
    "Mongolian": "a stylized steppe eagle",
    "Kazakh": "a stylized steppe eagle",
    "West African": "bold Adinkra symbol patterns",
    "Nigerian": "bold Adinkra symbol patterns",
    "Senegalese": "bold Adinkra symbol patterns",
    "Cameroonian": "bold Adinkra symbol patterns",
    "Indian South Asian": "an ornate mandala",
    "Sri Lankan": "an ornate mandala",
    "Gulf Arab": "flowing geometric arabesque patterns",
    "Levantine": "flowing geometric arabesque patterns",
    "Moroccan Berber": "bold Berber geometric symbols",
    "Egyptian": "a stylized eye of Horus",
    "Persian": "an intricate Persian medallion pattern",
    "Turkish": "a crescent and star motif",
}


def heritage_motif(skin_text):
    for prefix, motif in HERITAGE_TATTOOS.items():
        if skin_text.startswith(prefix):
            return motif
    return None
# explicit left/right ({s}) — "one shoulder" let the model default to the
# SAME side every time (reviewer: tattoos all in the same spots)
TATTOO_SPOTS = [("across the {s} shoulder", 0.26), ("on the {s} side of the chest", 0.20),
                ("wrapped around the {s} upper arm", 0.18), ("on the {s} forearm", 0.13),
                ("on the {s} side of the ribcage", 0.10), ("on the {s} calf", 0.06),
                # neck ink implies existing coverage (reviewer): casual roll
                # gets shoulder+arm ink alongside it
                ("climbing the {s} side of the neck, with matching ink across "
                 "the {s} shoulder and down the {s} upper arm", 0.04),
                ("small, high on one cheekbone", 0.03)]

TRUNKS = [("navy", "gold"), ("black", "red"), ("white", "black"), ("maroon", "white"),
          ("green", "gold"), ("royal blue", "white"), ("charcoal", "orange"), ("purple", "gold"),
          ("red", "black"), ("grey", "blue")]
THAI_SHORTS = [("gold and black", ""), ("royal blue", "white"), ("red and gold", ""), ("emerald green", "gold")]
GLOVES = ["black", "red", "royal blue", "dark red"]


def parse_slot(base):
    m = SLOT_RE.match(base)
    if not m:
        return None
    d = m.groupdict()
    d["style"] = d["style"] or "wl"
    return d


def _pounds(slot):
    lo = None if slot["min"] == "x" else int(slot["min"])
    hi = None if slot["max"] == "x" else int(slot["max"])
    if lo and hi:
        return (lo + hi) // 2
    if lo:
        return lo + 20
    if hi:
        return hi - 10
    return 140          # both bounds open (x_x wildcard, e.g. women) — was a crash


def roll_person(base, rng=None, avoid_note="", lbs_override=None, persona=None,
                skin_override=None, no_tattoo=False):
    """Roll a brand-new person for a slot ->
    (traits, gear, glove, skin, extra_tags).
    extra_tags: quirk-derived tags (e.g. "wildhair" for crazy dye/mohawks)
    that STACK with the persona tag — a fighter can be menace + wildhair.
    lbs_override: concrete walk-around weight for wide/open bands (generic
    two-category slots would otherwise all anchor to the same midpoint).
    persona: optional marketable-archetype variant, tagged in the manifest for
    game hooks (popularity etc.) —
      glam (f)     magazine-cover star, younger, signature/showcase kit
      adonis (m)   chiseled cover-athlete looks
      menace       cold intimidating heel presence
      inked        heavy showman tattoos (full sleeve + chest)
      babyface (m) fresh-faced clean-cut prospect, younger
      unathletic   regional-circuit tough guy: soft untrained build"""
    glam = persona == "glam"
    rng = rng or random.Random()
    slot = parse_slot(base)
    if slot is None:
        raise ValueError(f"{base} is not a slot-formatted name")

    female = slot["sex"] == "f"
    # skin_override forces a specific sub-ethnicity (targeted gap-fill); else roll
    skin = skin_override or rng.choice(ETH_SKIN.get(slot["eth"], ETH_SKIN["white"]))
    color = rng.choice(DARK_HAIR_COLORS if slot["eth"] in DARK_HAIR_ETH else HAIR_COLORS_ANY)
    hair = rng.choice(FEMALE_HAIR if female else MALE_HAIR).format(c=color)
    # Reviewer strikes: the model kept rendering TWO-TONE / streaked hair and
    # shifting hair color. Pin a single uniform natural color on EVERY prompt
    # (this trait feeds both the single-figure and triptych pipelines).
    hair += (f", hair a single uniform solid {color} color all over, "
             f"NOT two-tone, no highlights, no dyed streaks, no color patches")
    # mohawks/faux-hawks should be OCCASIONAL statements, not 10% of the
    # roster: rejection-sample most of them back into the normal pool
    if ("mohawk" in hair or "faux-hawk" in hair) and rng.random() < 0.75:
        hair = rng.choice(FEMALE_HAIR if female else MALE_HAIR).format(c=color)
    facial = "" if female else ", " + rng.choice(MALE_FACIAL)
    # face = shape + nose + eyes (+ a complexion on most). Nose+complexion are
    # the individuating axes that break the model's "average face" convergence.
    face = rng.choice(FACES) + ", " + rng.choice(NOSES) + ", " + rng.choice(EYES)
    if rng.random() < 0.7:
        face += ", " + rng.choice(COMPLEXIONS)
    expr = rng.choice(EXPRESSIONS)
    # Women skew older + explicitly hardened: young female subjects are the
    # most anime-drift-prone combination (two reviewer strikes on one slot).
    # (Anime-era rule; photoreal doesn't drift, so glam rolls may go younger.)
    # Wide age band — a narrow 26-33 band made fighters read as clones
    # (reviewer: "too similar" x8).
    age = rng.randint(23, 39) if female else rng.randint(22, 41)
    if female and glam:
        age = rng.randint(24, 30)
        expr += (", a strikingly beautiful photogenic face with striking "
                 "well-proportioned features, glamorous styled hair and subtle "
                 "natural makeup, the polished confident presence of a "
                 "magazine-cover athlete, still clearly a real professional fighter")
    elif female:
        expr += ", the hardened look of a seasoned professional athlete"
        # Counter the model's beauty prior: without an anchor EVERY woman
        # renders as a magazine face, which makes the glam tag meaningless.
        # Concrete beats abstract (same lesson as bodies).
        look = rng.random()
        if look < 0.55:
            expr += (", a plain ordinary everyday face with average "
                     "unremarkable features, makeup-free, the practical "
                     "appearance of a regular neighborhood gym coach")
        elif look < 0.75:
            expr += (", a rough sturdy weathered face with heavy plain "
                     "features, hardened by years of sparring")
    elif not female and persona not in ("adonis", "babyface", "menace", "unathletic"):
        # Men had NO beauty-counter (only women did), so every ordinary man
        # rendered as the model's default handsome fighter and they converged
        # ("too similar" across ethnicities). Give men the same anchor.
        look = rng.random()
        if look < 0.45:
            expr += (", a plain ordinary forgettable face with average "
                     "unremarkable features and a wholly unglamorous everyday look")
        elif look < 0.72:
            expr += (", a rough rugged weather-beaten face with heavy plain "
                     "features, hardened by years of fighting")
        # else (~28%): left as rolled - some men are just normal-looking
    if persona == "adonis" and not female:
        age = rng.randint(24, 31)
        expr += (", a strikingly handsome chiseled photogenic face with a "
                 "sculpted jawline, neatly styled hair, the polished confident "
                 "presence of a magazine-cover athlete, still clearly a real "
                 "professional fighter")
    elif persona == "babyface" and not female:
        age = rng.randint(21, 26)
        facial = ", clean shaven"
        expr += (", a boyish fresh clean-cut face with youthful features and "
                 "a friendly approachable look")
    elif persona == "menace":
        # look only — any damage/scar wording summons blood (handbook rule)
        expr += (", a cold unblinking intimidating stare under a heavy "
                 "scowling brow, a grim imposing menacing presence")
    lbs = lbs_override if lbs_override else _pounds(slot)
    style_txt = STYLE_TRAITS[slot["style"]]

    # physique tier: weight-scaled center with a HARD ±1-tier window. The old
    # soft skew let a 179 lb "heavy" roll the slim-runner tier (generic-pack
    # heavies photographed as flyweights). Scale spans the actual roll range
    # (115-260); the massive super-heavy tier engages from ~220 lbs up.
    n = len(PHYSIQUE_TIERS)
    center = min(n - 1, max(0, (lbs - 115) / (265 - 115) * (n - 1)))
    weights = [w / (1 + abs(i - center)) if abs(i - center) <= 1.5 else 0.0
               for i, (_, w) in enumerate(PHYSIQUE_TIERS)]
    physique = rng.choices([t for t, _ in PHYSIQUE_TIERS], weights)[0]
    if persona == "unathletic":
        # the regional-circuit tough guy who looks like a bar patron, not an
        # athlete (tagged for game hooks: conditioning/underdog narratives)
        physique = ("the soft untrained build of an ordinary bar regular - a "
                    "plump round belly, soft heavy arms and a soft chest")
        expr += ", a soft fleshy face with the beginnings of a double chin"
    if persona == "unathletic":
        # the regional-circuit tough guy: positive imagery only (dad-bod rule)
        physique = ("the soft untrained build of an ordinary bar regular - a "
                    "plump round belly, soft heavy arms and a soft rounded chest")
        expr += ", a soft fleshy face with the beginnings of a double chin"

    sexword = "woman" if female else "man"
    # tattoo roll: most fighters have none; face ink is deliberately very rare.
    # no_tattoo suppresses ALL ink (triptychs: a tattoo that appears in one
    # aging stage and vanishes in another — reviewer "lost tattoos" — is worse
    # than none; continuity across 3 stages is unreliable).
    if no_tattoo:
        tattoo = ", with completely clean untattooed skin"
    elif persona == "inked":
        side = rng.choice(["left", "right"])
        motif = heritage_motif(skin)
        if motif and rng.random() < 0.40:
            # heritage flair: the showman's sleeve tells you where he's from
            tattoo = (f", with a full sleeve of {motif} covering the entire "
                      f"{side} arm from shoulder to wrist")
        else:
            # varied layouts (reviewer: everything rendered as one right
            # sleeve); collarbone wording keeps chest ink visible above a
            # sports bra
            layouts = [
                (f"a full sleeve of dense intricate black tattoos covering the "
                 f"entire {side} arm from shoulder to wrist", 3),
                ("full sleeves of dense intricate black tattoos covering both "
                 "arms from shoulder to wrist", 2),
                (f"a full {side}-arm sleeve of intricate black tattoos plus a "
                 f"large ornate piece spread across the upper chest and "
                 f"collarbones", 2),
                # neck ink never comes alone — it caps an already-covered body
                (f"a full sleeve of dense intricate black tattoos covering the "
                 f"{side} arm, a large ornate piece across the chest, and a bold "
                 f"design climbing the {side} side of the neck", 1.5),
                (f"a large ornate black tattoo covering the {side} thigh from the "
                 f"hem of the shorts down to the knee", 1.5),
                (f"an ornate black tattoo across the ribs and {side} side of the "
                 f"waist", 1.5),
                # the fully-committed showman (reviewer wants MORE of these):
                # heavily weighted so ~1 in 3 inked fighters is fully covered
                ("a heavily tattooed upper body - full sleeves of dense "
                 "intricate black tattoos covering both arms from shoulder to "
                 "wrist, a large ornate piece spread across the entire chest, "
                 "and bold designs climbing up both sides of the neck", 6),
            ]
            texts, weights = zip(*layouts)
            tattoo = ", with " + rng.choices(texts, weights)[0]
    elif rng.random() < (0.18 if female else 0.30):
        spots, weights = zip(*TATTOO_SPOTS)
        spot = rng.choices(spots, weights)[0].format(s=rng.choice(["left", "right"]))
        # flavor: 45% simple, 30% fight-themed, 25% heritage (when the rolled
        # nationality has a motif; otherwise fight-themed). Themed pieces get
        # an art style so the same motif never renders the same way twice.
        flavor = rng.random()
        motif = heritage_motif(skin)
        # ALL branches get an art style now — the bare-name simple branch
        # rendered as featureless "ink blobs" (reviewer)
        art = rng.choice(TATTOO_ARTSTYLES)
        if flavor < 0.45:
            design = rng.choice(TATTOO_DESIGNS)
        elif flavor < 0.75 or motif is None:
            design = rng.choice(FIGHT_TATTOOS)
        else:
            design = motif
        tattoo = f", with a {art} tattoo of {design} {spot}"
    else:
        tattoo = ", with completely clean untattooed skin"

    # signature quirk: one distinguishing detail. Raised 22% -> 33% to fight
    # "too similar" declines — dark-hair ethnicities are locked to black/brown
    # hair, so their faces converge without a quirk to break them apart.
    quirk = ""
    if rng.random() < 0.33:
        pool = [(t, w) for t, w, sex_only in QUIRKS
                if sex_only is None or sex_only == slot["sex"]]
        texts, weights = zip(*pool)
        quirk = ", " + rng.choices(texts, weights)[0]

    # "professional fighter" is kept AWAY from the body sentence — it drags the
    # body toward cover-model shredded (A/B tested); gear carries the context.
    traits = (f"a fictional {age} year old {sexword} of about {lbs} pounds: {skin}, "
              f"{hair}{facial}, {face}{quirk}, {expr}, {NATURAL}, with {physique}, and {style_txt}{tattoo}")

    # GLOVES ARE NEVER GENERATED — "MMA gloves" is the single biggest
    # bodybuilder trigger (proven by gloveless A/B). The finalize edit adds
    # them; the color rides along in the spec.
    glove = rng.choice(GLOVES)
    if slot["style"] in THAI_STYLES:
        c, trim = rng.choice(THAI_SHORTS)
        shorts = (f"completely plain {c} satin shorts with a wide waistband"
                  + (f", {trim} trim" if trim else ""))
    else:
        c, trim = rng.choice(TRUNKS)
        # "compression-fit trunks" removed: photoreal renders that cut as
        # underwear (three "not wearing shorts" declines, all compression rolls)
        cut = rng.choice(["athletic shorts", "long board-short style fight trunks",
                          "short retro-cut trunks", "loose knee-length training shorts"])
        pattern = rng.choice([f"with a {trim} waistband",
                              f"with a single {trim} stripe down each side",
                              f"with {trim} side panels",
                              f"with a thin {trim} hem"])
        shorts = f"plain {c} {cut} {pattern}"
    # Women's kits are a MATCHING SET most of the time (reviewer): the bra
    # takes the shorts' main color ~75%; occasional deliberate contrast.
    if female and glam:
        # Glam kits: premium ring wear, test-rendered (scratchpad glamtest).
        # Two looks — "signature" satin set, and the more revealing
        # "showcase" vale-tudo cut (short high-cut trunks, bare midriff).
        # Wording stays athletic-anchored: swim/underwear terms re-trigger
        # the photoreal "not wearing shorts" failure.
        gc, trim = rng.choice([("black", "gold"), ("deep crimson", "gold"),
                               ("royal purple", "silver"), ("emerald green", "gold"),
                               ("white", "rose gold"), ("midnight blue", "silver")])
        if rng.random() < 0.5:
            top = (f"A fitted {gc} athletic crop top with elegant thin crossed "
                   f"shoulder straps and {trim} trim, ")
            shorts = (f"matching high-shine {gc} satin fight shorts with {trim} "
                      f"trim, a flattering tailored athletic cut")
        else:
            top = (f"A fitted {gc} halter-style athletic sport top with {trim} "
                   f"trim and a bare midriff, ")
            shorts = (f"matching {gc} low-rise vale tudo style fight trunks - "
                      f"short fitted high-cut athletic trunks with {trim} piping")
    elif female:
        # varied top cuts (reviewer: identical sports bras made the women look
        # like clones). Bare garment names collapse to the same bra (A/B
        # tested) — describe the SILHOUETTE: coverage length, neckline,
        # straps, front details. Front-visible features only (portraits are
        # front view; racerback details are wasted words).
        tcut = rng.choice([
            "scoop-neck sports bra",
            "high-neck sports bra with a neckline up at the collarbones",
            "longline sports bra extending well down past the lowest ribs",
            "sleeveless athletic top that covers the whole torso down to the "
            "waistband of the shorts",
            "zip-front sports bra with a short zipper at the front",
            "sports bra with thin double spaghetti straps",
            "fitted athletic rash guard with snug elbow-length sleeves",
            "fitted long-sleeved compression rash guard covering the arms "
            "down to the wrists",
            "one-shoulder athletic top with a single wide strap"])
        if rng.random() < 0.75:
            top = f"A matching {c} {tcut} in the same color as the shorts, "
        else:
            bra_c = rng.choice(["black", "dark navy", "charcoal grey",
                                "white", "burgundy"])
            top = f"Plain {bra_c} {tcut}, "
        # bottoms variety too: compression shorts and full-length leggings
        # alongside the shared shorts roll
        broll = rng.random()
        if broll < 0.22:
            shorts = (f"matching high-waisted {c} compression shorts "
                      f"reaching to mid-thigh")
        elif broll < 0.36:
            shorts = f"matching full-length fitted {c} athletic leggings"
    else:
        top = "Shirtless, "
    wraps = ", black ankle wraps" if rng.random() < 0.2 else ""
    gear = f"{top}{shorts}{wraps}, barefoot, bare hands."

    if avoid_note:
        traits += f". IMPORTANT reviewer guidance about the previous rejected attempt: avoid this problem: {avoid_note}"

    # quirk-derived tags stack with the persona tag (multi-tag fighters).
    # "wildhair" = genuinely wild statements (mohawks, vivid dye) — subtle
    # fashion (bleached tips) stays untagged.
    extra_tags = []
    if "mohawk" in hair or "faux-hawk" in hair or "dyed" in quirk:
        extra_tags.append("wildhair")
    return traits, gear, glove, skin, extra_tags
