---
name: origins-phase3
description: Origins phase 3 (PR #62, branch world/origins-phase3, no save break) - scenes waking/talent weights, second centre, one-art development + cross-training rooms, all-rounder test, intake shift, size table, unified code, rankings gate; numbers across seeds
metadata:
  type: project
---

Built 2026-11-16 (draft PR #62). Builds on [[origins-phase2]] and [[origins-followups]]. No model shape change
(Camp.Specialty init -> set), save version stays 53.

- Scenes (`Living/OriginsScenes`): intake picks scene by sqrt(atlas Weight) x ramp (0.25 -> 1.0 over 5 years since
  WokeOn = the waking chapter's FiredOn; day-one scenes = StartDate). A woken scene with no gym opens one the next
  month; more when men > 15 x gyms (15%/mo keyed, max 8). Gyms teach the country's art (OriginsTalent.ArtOf) until
  the first camp chapter fires, then 50% (80% after the fall) open whole-game.
- Second centre = the 15k night's city if outside North America, else JPN (derived from the chapter record's City;
  no Note field written). Effect: interest +15 base and current (cap 85), ring, exempt from the code, AI never adopts it.
  15k crowds don't happen at 0.5x crowds: it fires at the deadline (2003-01) in every seed seen.
- One-art development: FighterDevelopment.Advance(oneArt) filters the skill list to the stronger half + body/head;
  same draw COUNT per bump, so Modern (oneArt=false always) is identical. TrainsOneArt = Origins && (no room ||
  room doesn't cross-train). CrossTrains = Origins: Specialty WellRounded && first-camp chapter OPEN (live or fired).
- IsAllRounder = both halves >= BaseLevel (mean of 6 physical + 4 mental) - 16. A gap test failed: brawlers (bad
  at everything) read as all-rounders. Margin 16 (not 12) so authored Declan Voss counts. Cross-trained brawler -> boxer.
- Intake share: 0 before first camp; 10% +7/yr to 45%; after the fall 50% +7/yr to 85% (hash of id). Second art
  from the other half; skills = max(art1, art2 - 6) - 3 on technical skills (CA within ~+2..+8 of a specialist).
- All-rounders won only ~50% vs specialists (green newgens vs established men): specialists-fall fires at deadline.
- Room conversions after the camp chapter: 1% + rep/3000 a month, +4% after the fall; reputation drifts UP only to
  15 + 0.6 x top-3 fame + 8/belt + 10 if whole-game. By ~2006 nearly every gym teaches the whole game.
- Unified code = FightRules.Modern keeping OpenWeight/Enclosure; Fight.Rounds null so SuggestCard takes TitleRounds
  (5) for title bouts (one-line OriginsShows edit). Bound = licensed home region (Sanction country+area) and not
  second centre; enforced at the chapter and every month after. FoundedUnder applies it after EraRules' draws.
- Rankings: RankingService.RecomputeGlobal clears GlobalRankings/P4P in Origins until unified-rules fires (no FOTY before).

- CI origins 40x14 (run 43900495106): 0 violations; 1 seed fails on the known DLB roster 7<8 (main fails 1 too).
  Medians: scenes 5 (1996) -> 15 (1999) -> 19 (2001) -> 23 (2006); gyms 11 -> 20 -> 27 -> 36, whole-game 0 -> 5 -> 14
  -> 34; all-rounders of active 1% -> 2% -> 10% -> 22% (2003) -> 42% (2006), of new men 0 -> 8 -> 26 -> 51 -> 74%.
  first camp 1999.3, specialists-fall 2003.2 (range 2000.8-2004.0: sometimes earned), second centre and unified
  always at deadline (2003.0, 2006.0). Houses outside USA/JPN/BRA: median 1 by 2001 (founding unchanged).

**Why:** phases 4-6 build on these seams; the UI reads OriginsScenes.Scenes / OriginsCamps.Options / IsAllRounder.
**How to apply:** new Origins growth effects go through TrainsOneArt/CrossTrains, never a new draw on the weekly
stream; measure with `longsim origins N 14` (SPORT line) - Era 3 is done by ~2006.
