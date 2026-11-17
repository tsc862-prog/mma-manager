---
name: unique-names-canon
description: Full-name uniqueness rules (2026-11-16, PR #70): scope, resolution ladder, what yields to what, residual gaps
metadata:
  type: project
---

One full name per real fighter ever (active+retired), plus named staff/owners and authored people not yet in the world (`Generation/NameRegistry.cs`, built fresh per call, never saved). "Local X" club filler is exempt.

- Old ladder (16 tries, key salt 0x4E11_D3D0) is kept draw for draw; deeper resolution (`ResolveDeep`, salt 0x7A3E_C0DE) is a SEPARATE keyed stream so the nickname re-roll after it sees the same state. Order: fresh pair from pool, double-barrelled surname ("Kuznetsov-Gadzhiev"), generation suffix ("Marsh II").
- The RESERVE (`Generation/NameReserve.cs`, 32 cultures, ~990 names): extra first names/surnames the normal roll never draws; the deep step tries base+reserve (40 hash-picked tries) BEFORE hyphen/suffix. Base pools alone left ~1,000 hyphenates per world (anglo 730 men for 288 pairs). Sized so pairs >= 2x projected YEAR-25 population per culture (table in the PR; test `EveryCulturesPoolPlusReserve_HoldsTwiceItsYear25Population`). Women gain only the surnames (reserve firsts are men's). Hygiene test: no repeats, none in base pool, no authored surname (roster/legends/pioneers/staff/owners/crossovers), no real-star surnames. NEW authored surnames must not reuse reserve surnames (the test fails).
- Pioneer names are reserved in Modern mode too (PaintedLibrary.PioneerOf binds by exact name in every mode).
- Famous-name juniors (`keepSurname`): surname is exact; first name walks `NameData.AllFirstNames(sex)`. Tests (BondTests) assert elder.LastName == kid.LastName.
- Root causes found: small pools (10-14 firsts) saturate with 6,000+ fighters so the ladder ran dry (~1,000 day-one repeats); `NewgenService` never called EnsureUniqueName.
- Authored wins: reserved sets are mode-specific (Modern: roster, legends, designed staff/owners, crossovers via `WorldEvents.CrossoverNames`; Origins: pioneers, arrivals + leads, promotion owners). New authored people in those tables are reserved automatically; any NEW authored table needs adding to NameRegistry.
- Residual: generated staff (StaffService 16x16 pool, world-sim's, rolled after fighters) can still share a name with a fighter (~1 per world).
- When adding authored names, a generated fighter will be renamed away from them, never the reverse.
- RealNames (`Generation/RealNames.cs`): deny-list of real notable full names, treated as taken in BOTH modes (best effort, append-only). New pool/reserve entries: check pairs by eye; add any real person you recognise.
- Staff: `StaffNaming.Resolve` (called after SeedFrontOffices in both builders) renames GENERATED staff whose name is taken (fighters, authored, real, earlier staff), keyed hash off (promo id or market, role, list index), staff reserve 33x35 in StaffNaming. Staff have NO id; staff are only generated at world build. Authored staff keep names.
- Reserve content rules learned: no given names as surnames (Thai surnames are long Sanskritic forms), no women's first names as surnames, Brazilian/Latino surnames collide with authored roster often (Souza, Carvalho, Vilela...): the hygiene test catches them.
