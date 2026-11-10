# Fighter Portrait Packs — Pipeline Handbook

Generated fighter art for the game: per fighter, a transparent full-body shot and
face crop at three life stages (youth / prime / veteran), produced by AI image
generation with automated quality gates and a human review loop. This file is the
master reference for pausing, resuming, or switching art styles.

**Status (2026-07-03 late evening):** painted PAUSED · anime PAUSED ·
**realistic ACTIVE — 69 fighters on disk, user reviewing, watcher live.**

## Current state (2026-07-03 late session)

1. **Quota is back and the per-style prompt ordering fix WORKS** — the last
   batch leg ran 20 fighters with ZERO re-rolls (100% first-try sheets).
   Pack grew 8 → 69 fighters in one evening (~$3-4 of GPU).
2. **Realistic-era fixes landed this session (all verified live):**
   - **Veteran men aging scaled back** (reviewer: "veteran looks to old").
     The structural aging list exists for cel shading; photoreal renders it
     literally (38 photographs as ~50). Realistic men now get light markers
     + v_age capped 39; women keep the strong wording (their photo aging
     reads subtle already). `ztrio.triptych_prompt`.
   - **Photo-collage mode pinned away**: wide heavyweight bodies made the
     model render three SEPARATE seamed studio panels — each often a
     different person. Layout clause now demands "ONE single continuous
     scene ... one seamless background spanning the entire image" (positive
     phrasing; naming panels would summon them).
   - **Figure counter ignores the bottom 15%** (`calib3.count_figures`):
     photoreal floor shadows run continuously under all three figures and
     merged good triptychs into "1 figure" — slots burned 6 gens producing
     GOOD sheets that were auto-discarded.
   - **Rejected sheets are saved** to `raw/rejects/{base}_try{n}_{count}f.png`
     — post-mortems no longer require digging in gradio's temp cache. Two
     good rejects were rescued directly into the pack (zero-cost fighters).
   - A background-uniformity collage gate was tried and REMOVED: collage
     bg shades measure within noise of good sheets. Collages passing the
     count die at the skin-tone gate one cut-call later; acceptable.
   - **Review app shows live queue state**: counts in tab labels, retry
     count, 15s polling (header-only — never re-renders cards mid-note),
     "N NEW on disk — hit Refresh" nudge.
3. **Process hygiene on Windows:** stopping a background PowerShell task does
   NOT kill its python child, and two pythons can bind the same port
   (SO_REUSEADDR) with the OLD one winning — after editing pipeline code,
   kill leftover pythons by command line
   (`Get-CimInstance Win32_Process | ? CommandLine -match 'watcher|reviewapp'`)
   before relaunching, and restart the watcher after ANY ztrio/calib3 edit
   (it imports them at startup).
4. Earlier identity tweaks (still current, `tools/packgen/identityslots.py`):
   women's bra matches shorts color ~75%; tattoo clause only applies to
   fighters who rolled none.

---

## The three packs

| Pack | Folder | Style | Review app | State |
|---|---|---|---|---|
| Painted  | `generated-pack`           | painterly semi-real (Krea-era look) | :8765 | paused (~25 approved) |
| Anime    | `generated-pack-anime`     | semi-realistic seinen anime         | :8766 | paused (~60 fighters, many approved) |
| Realistic| `generated-pack-realistic` | studio-photo photoreal              | :8767 | active (~96, styled triptychs) |
| Generic  | `generated-pack-generic`   | studio-photo photoreal              | :8768 | 1000 fighters on disk (2026-07-04), review ongoing |

**Generic pack** (`tools/packgen/zgeneric.py [count]`, ids 500+): PRIME age only
(loader's nearest-stage fallback serves it at every age), wildcard style `x`
(matches at the loader's any-style tier — generics backfill fighters no styled
identity covers), and TWO weight categories partitioning the real divisions by
natural weight: light `x_170` (Straw..Welter) / heavy `171_x` (Middle..Heavy).
Single-figure generation (no triptych), ~1 gen + 1 cut per fighter. No watcher:
rerunning zgeneric processes declines/retries first, then generates. Its
figure pre-check uses `count_figures(img, drop_edge_ranges=True)` — backdrop
vignette reads as a phantom edge figure on single-subject shots (the centered
subject's range is always kept). Physique tiers are now HARD-windowed to the
rolled weight (a 179 lb "heavy" once rolled the slim-runner tier).

**Auto-sync daemon** (`tools/packgen/syncwatch.py`): the sync counterpart to
watcher.py — polls the packs' APPROVED sets every 60s and, on any change,
mirrors approved fighters into `Wpf/Assets/Painted` via the incremental
`syncassets.sync()` (only changed files copied, un-approved pruned) AND into
the build output dir, so a game RELAUNCH shows new approvals with NO rebuild.
Run it alongside review; approve fighters and they ship themselves. `syncassets.py`
is now importable (`from syncassets import sync`) and does incremental+output
mirroring; the manual `python syncassets.py` still works for a one-shot sync.

**Daily-credit top-up mode**: `zgeneric.py N` is fully resumable and stops
itself on quota ("QUOTA EXHAUSTED - rerun to resume"). To grow the pack on
daily PRO quota alone, just rerun with the standing target (e.g. 1000) once a
day — it skips everything that exists, processes review declines first, and
generates until the day's quota dies. No supervisor needed for that mode; the
single pass exiting on quota IS the stop.

**Personas/tags** (generic pack): each fighter may roll ONE marketable
archetype, recorded in `<pack>/tags.json` (base -> ["tag"]), shown as a gold
chip in the review app, shipped by syncassets as `Assets/Painted/tags.json`
(csproj-bundled) for game hooks (popularity boosts etc.). Rates in
`zgeneric.roll_persona`; prompt effects in `identityslots.roll_person`:
- `glam` (f, 30%) — cover-star face/styling; kit rolls signature satin set or
  showcase vale-tudo cut (both test-rendered; athletic wording ONLY — swim/
  underwear terms re-trigger the "not wearing shorts" failure)
- `adonis` (m, 12%) — chiseled cover-athlete looks, younger roll
- `menace` (8%/6%) — cold intimidating heel stare (look only, NO damage words)
- `inked` (7%/5%) — showman tattoos, 6 weighted layouts (single/double sleeve,
  sleeve+collarbone chest piece, neck+collarbone, thigh, ribs/waist — neck and
  thigh test-rendered clean, no face drift)
- `babyface` (m, 8%) — clean-shaven boyish prospect, age 21-26
- `unathletic` (m 6% / f 4%) — regional-circuit tough guy: soft untrained
  bar-regular build, plump belly, fleshy face (game hooks: conditioning
  penalties / everyman-underdog narratives)
- `wildhair` (~7%, QUIRK-derived, STACKS with any persona) — mohawks/faux-hawks
  (rejection-sampled to ~3%) or vivid dye quirks (blue/pink/crimson/two-tone/
  platinum). Tags are a LIST: quirk tags stack on persona tags (menace +
  wildhair = pink-mohawk heel). Review app chips have an ✕ to strip a tag
  the render doesn't earn while still approving the image (POST /untag,
  manifest edit).
Declining a tagged fighter removes the tag with them; slot renames migrate
tags (reviewapp.rename_slot).

### Tag semantics (WIRED into the game, 2026-07-03)

Runtime data: `Assets/Painted/tags.json` in the build output — a flat map of
identity key -> list of tags, e.g. `"f_white_507_x_170_x": ["glam"]`. The key
is the same `{sex}_{eth}_{id}_{min}_{max}_{style}` base that `PaintedLibrary`
computes for portrait matching, so the natural wiring point is: when a fighter
is assigned a painted identity, read that identity's tags and apply modifiers.

Intended effects (design intent per reviewer, tune freely):
| tag | intended game effect |
|---|---|
| glam | popularity/fan-draw boost (marketable star) — the original request |
| adonis | male equivalent of glam: popularity/fan-draw boost |
| menace | "heel heat": draw boost esp. for grudge/title fights, maybe minus charisma |
| inked | recognizability/merch appeal: smaller steady popularity bonus |
| babyface | prospect hype: news-feed narratives, faster popularity growth when winning |

A fighter has at most ONE tag. Tags describe the LOOK baked into the portrait,
so effects should stay flavor/popularity-side; competitive stats come from the
sim as usual.

Packs are fully independent (own review.json, specs.json, decline-log). The game
loader (`Wpf/Art/PaintedLibrary` reading `Wpf/Assets/Painted`) is style-agnostic —
shipping a pack = `MMA_PACK_DIR=<pack> python tools/packgen/syncassets.py`, which
mirrors all APPROVED fighters into `Wpf/Assets/Painted` (full replace — slot
names collide across SAME-ID-RANGE packs — old bundle archived to `_archive/`).
Multiple packs ship together via `MMA_PACK_DIR="packA;packB"` (realistic 100s +
generic 500s don't collide); single-stage packs ship 2 files per fighter.
**The REALISTIC pack has been the shipped bundle since 2026-07-03 (70 fighters).**
After syncing, also purge `Wpf/bin/.../Assets/Painted` and rebuild — incremental
builds overwrite same-named files but never REMOVE stale ones (mixed-pack bin).

## Asset metadata — MANIFEST ERA (2026-07-04)

**All fighter metadata lives in `<pack>/manifest.json`** (see
`tools/packgen/packmeta.py` for schema + helpers). Filenames are opaque
pointers; new files are written as `{ident}_{stage}_{face|body}.png` where
ident = `{sex}_{eth}_{id}`. Legacy token-encoded filenames
(`{sex}_{eth}_{id}_{min}_{max}_{style}_{stage}_{kind}.png`) still work — the
migration pointed manifests at them in place, and both the pipeline and the
game keep a filename-parsing fallback for manifest-less packs.

Manifest fields per fighter: sex `m|f` · eth
`white|black|asian|latin|mena|poly` · weight `{min,max}` in lbs (null = open)
· style `wr gr bjj st kb cl br wl` (null = wildcard) · images per stage
(`y|p|v` — youth ≤25, prime 26–35, veteran 36+) · tags (personas) · cultures
(WorldAtlas culture keys, derived from the rolled sub-ethnicity).

- Review app reads the manifest; weight/style overrides are manifest edits
  (files are NEVER renamed; the ident never changes). review.json is keyed by
  ident.
- syncassets ships images + a merged bundle `manifest.json` +
  a derived `tags.json` (compat for the game's existing tag hooks).
- `PaintedLibrary` loads the bundle manifest; `Portraits` attaches the World
  so the fighter's COUNTRY culture drives bucket choice (a Samoan draws poly,
  an Iranian mena, culture-exact identities preferred), falling back to the
  continental region when unwired.
- **Geography buckets `mena`/`poly` enter the slot sequence beyond a legacy
  watermark** (zgeneric index 1100, genwave3 index 200): the composers are
  seeded-deterministic, and changing the eth list below the watermark would
  reshuffle every existing fighter's ident. Never lower the watermarks.

## Z-Image space (2026-07-10): switched fork -> OFFICIAL via adapter

The fork `mrfakename/Z-Image-Turbo` (api `/generate_image`, free width/height,
single-image result) went into permanent RUNTIME_ERROR. All generators now go
through **`tools/packgen/zclient.py`**, which targets the OFFICIAL
`Tongyi-MAI/Z-Image-Turbo`. Its api differs: endpoint `/generate`, resolution
is a PRESET dropdown string (not free w/h), result is a GALLERY list. The
adapter maps our tuned sizes to presets (prime 640x1280 -> `720x1280`,
triptych 1536x1280 -> `1728x1344`) and unwraps the gallery to a local path, so
callers still pass width/height/steps unchanged. If the space moves again, fix
`zclient.SPACE` + `_RES_MAP` in one place. bg-removal (`not-lain/background-removal`)
is unchanged.

## Pipeline (per fighter, ~2 GPU calls ≈ $0.06)

1. **Triptych generation** — ONE Z-Image-Turbo image (1536×1024) containing the
   same person at three ages side by side. Identity is intrinsic because all three
   come from a single generation. (Space: `mrfakename/Z-Image-Turbo`, `/generate_image`.)
2. **Figure-count pre-check** (local, free) — exactly 3 figures or re-roll.
3. **Background removal** — `not-lain/background-removal`, `/png` endpoint.
4. **Split & gates** (local, free): split at transparent gaps; skin tone measured
   per panel from the FACE region and normalized (re-roll if mismatch > 26);
   youth↔prime HAIR color measured and corrected (re-roll if > 55).
5. **Face crops** — head+shoulders, 512×512.

## Running it (scripts in `tools/packgen/`, review app in `tools/reviewapp.py`)

Everything is switched by environment variables:

```powershell
$env:MMA_PACK_DIR  = "e:\MMA Manager\generated-pack-realistic"
$env:MMA_STYLE     = "realistic"    # anime | painted | realistic
$env:MMA_PACK_PORT = "8767"

python tools\reviewapp.py           # review UI on the port above
python tools\packgen\watcher.py     # auto-processes review decisions every 30s
python tools\packgen\ztrio.py 150   # generate until N fighters exist (resumable)
```

HF token: `e:\MMA Manager\hf-token.txt` (HF PRO account + prepaid credits).

## Review loop semantics

- **Approve** — final for that fighter. Weight/style dropdowns override the slot
  (files are renamed; wildcard = matches anyone).
- **Try Again / Redo** (note required) — SAME person regenerated, note enforced.
- **Decline** — fighter discarded, brand-new person rolled into the slot (note is
  logged for pattern analysis only). Special: a decline whose note contains
  "realistic"/"photo" SAVES the fighter to `generated-pack-realistic` first.
- The watcher does all processing automatically; approvals need nothing.

## Hard-won model lessons (do not relearn these)

- **Never list what you don't want** — "no blood" makes Qwen paint blood; negation
  summons content. Positive-only phrasing.
- **Concrete beats abstract** — "rugby prop forward" fixed bodies after every
  "NOT a bodybuilder" failed; named pigments ("espresso brown") fixed skin
  diversity; naming the rolled hair color beat "same hair color".
- **"MMA gloves" in a body description triggers bodybuilder physiques** on FLUX
  models (Z-Image is immune — gloves are safe in its prompts).
- **Naming glove types summons BOXING gloves** on photoreal Z-Image ("fingerless
  MMA gloves", "4-ounce MMA competition gloves", "sparring gloves" all failed
  A/B). Describe the anatomy instead: "thin open-fingered grappling gloves -
  small padded strips across the knuckles only, bare fingers sticking out, like
  fingerless gym gloves". Hand wraps ("cloth hand wraps around knuckles and
  wrists, fingers bare") also render perfectly (~30% of generics use them).
- Photoreal reads gear/trait words LITERALLY that cel styles stylized:
  "compression-fit trunks" -> underwear; "heavy-lidded sleepy eyes" -> closed
  eyes; the anime structural-aging list -> a 50-year-old. Audit trait pools
  when switching styles.
- **The model's beauty prior makes EVERY woman a magazine face** unless
  anchored: non-glam women roll ordinary-looks anchors (55% "plain everyday
  face ... regular neighborhood gym coach", 20% "rough sturdy weathered face",
  25% unanchored) so glam-tagged stars actually stand out. Test-verified
  (scratchpad plaintest).
- **The same applies to male bodies**: the soft physique tiers originally used
  negations ("no six-pack", "undefined stomach") which photoreal renders as
  their opposites — every man came out ripped. Tier texts are positive-only
  now ("a smooth soft stomach and a soft rounded chest"), test-verified.
- **Garment names collapse to the model's default garment** (women's "cropped
  tank top" and "longline bra" rendered identically). Describe the SILHOUETTE:
  coverage length, neckline height, straps, front details. Same principle as
  gloves. Front-view portraits: back details (racerback) are wasted words.
- **Prompt position matters** — style block leads the prompt; at the end of a
  250-word prompt it loses to the model's photoreal default.
- **Don't rewrite the style block to fix defects** — any wording change shifts the
  whole aesthetic (the "cel-shaded" tag incident). Fix defects with measurement
  gates and pixel post-processing instead.
- **1536×1024 = 3 figures reliably; 1920+ invites a 4th.**
- Ideogram-4 is DQ'd (safety filter blocks shirtless athletes); Qwen-Image DQ'd
  (endemic blood on fight subjects); FLUX family DQ'd for bodies (six-pack prior).

**Uniqueness layers** (2026-07-05): ~22% roll ONE signature quirk — but only
features that survive full-body scale (the face is ~90px in the source: nose
studs / eyebrow slits / heterochromia were A/B-tested INVISIBLE). Working
pool: stature (tall-lanky / short-stocky), fashion hair (platinum, bleached
tips), freckles, unibrow, ears, widow's peak, dimples, bleached brows.
Tattoos have flavor: 45% simple / 30% FIGHT motifs (wolf, lion, crossed
gloves...) / 25% HERITAGE motifs keyed to the rolled nationality
(identityslots.HERITAGE_TATTOOS: sak yant for Thai, Samoan bands, Aztec
eagle-warrior, steppe eagle...). Inked-persona fighters get a themed heritage
sleeve 40% of the time. Imagery only — lettering renders as gibberish.

## Identity generation

`identityslots.py` rolls each fighter from a slot name: sub-ethnicity + named skin
pigment (29), hairstyles (18 M / 16 F, buns capped), facial hair (13), face shapes
(14) + eyes (6) + expressions (10), weight-skewed physique tiers (concrete imagery),
gear cuts/patterns/colors, simple tattoos (~28%; shoulder/chest/arm, face 3%; same
tattoo pinned across all panels), 20% ankle wraps. Ages rolled per fighter within
the game bands; veteran hair ages realistically (40% no grey; redheads fade, never
grey) with structural face aging (hollowed cheeks, folds — reads on dark skin).

## Resuming a paused style

```powershell
$env:MMA_PACK_DIR = "e:\MMA Manager\generated-pack-anime"; $env:MMA_STYLE = "anime"; $env:MMA_PACK_PORT = "8766"
python tools\reviewapp.py       # + watcher.py + ztrio.py N   (same trio of processes)
```

Archives of earlier experiments: `_archive\generated-pack-v1|v2` (Krea/photo eras),
`ai-style-tests\`, comparison sheets (`model-sweep.png`, `comic-bakeoff.png`, etc.)
in the project root.
