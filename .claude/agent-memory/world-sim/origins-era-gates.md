---
name: origins-era-gates
description: Origins phase 0 (v49, PR #50, branch world/origins-seams) - World.Mode/World.Era, Living.Era.Allows capability gate, start-relative WorldEvents.Epoch; where every gate sits and the RNG rule for adding more
metadata:
  type: project
---

Built 2026-11-15 (save v49; v48 reserved for contracts stage 9). Plan: e:\MMA Manager\output\review\10-origins-mode\plan.md
(owner approved: 1993 start, three tiny day-one promotions, shaped history).

- `Core.Model/Era.cs`: `WorldMode`, `CapabilityKind` (saved BY NAME - never rename), `Capability(Kind, Key?)`
  with `Id` = "Kind" or "Kind:key" (WeightClass:Lightweight, CountryScene:BRA), `EraState` (Chapters list,
  Unlocked SortedSet<string> for deterministic order).
- `Core.Sim/Living/Era.cs`: `Allows/AllowsMedia/AllowsDivision/AllowsScene/Unlock/Fire`. Modern short-circuits true.
- **Gate rule**: put the check BEFORE the system's first draw (so Origins draws nothing) and never between two
  draws (so Modern is untouched). Superfight/acquisition gates sit before their rng.Chance; WorldEvents gates
  after the pure schedule check.
- Media: `Rung.Qualifies` is now a method (era && Rule) - covers offers, renewals, ShopAround, seeding, LadderView.
- `PromotionSetup.CommissionTitle` returns `Title?` (null when era lacks class / women's).
- `WorldEvents.Epoch(world, d)` counts from StartDate; StartDate==default falls back to 2026-01 (LeagueBuilder and
  hand-built test worlds never set StartDate!).
- Defined but unread: Rounds/Judges/Gloves/CoPromotion/CountryScene (phases 1/3/4).
- Observer start (no player) scouted: sim + persistence already cope (longsim runs PlayerPromotion=default);
  Wpf has ~95 refs in 27 files; mid-game founding needs keyed stream + 0-0 pool (copy FoundingClass).

**Why:** later Origins phases hang off these seams. **How to apply:** new era-gated systems call `Era.Allows`
first; byte-identical Modern is checked with compare 40x5 + shoesweep. See [[deal-window-invariants]].
