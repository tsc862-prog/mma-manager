---
name: engine-baseline-numbers
description: FightLab and in-world bulk numbers, golden hashes and tuned constants for the engine after step 3 (stoppages and scoring, 2026-11-11), with the pre-step-3 baseline; compare future changes against these
metadata:
  type: project
---

## After step 3 (branch engine/stoppages-scoring, PR #37, code commit 107ba21)
Golden master: results e6c94a818fcb451f, scorecards 462d2ea204d03f97.
FightLab scale 2 (seed 2026): 3 rds finish 36.2 (KO 10.3 TKO 11.6 SUB 14.3; UD 50.3 SD 10.0 MD 2.2 Draw 1.4), 5 rds 55.4;
men 37.8 / women 27.0; M HW 44.3 vs M Straw 29.3; KD 0.50/fight; 10-8 3.8%; MatchRating mean 45.7; upset curve unchanged (13-17 85.3, 18-22 91.0).
Style spread grew 15.7 -> 19.7 pp (Wrestler +4, Kickboxer -4, BJJ -2): decisions without knockdown 10-8s favour control.
In-world (longsim compare 40x5, new reporting block): finish 45.4 (main 45.3) - KO 13.9 TKO 15.1 SUB 16.4, SD 7.3 MD 1.7 Draw 1.1; KD 0.57/bout; 10-8 4.7%.
World drift vs like-for-like base: active 2428 -> 2405 (-1%, likely KO/TKO losses feeding InjuryModel), pop 36.1 -> 36.0, retirements 341.7 -> 344.8/yr, careers 17.4 -> 17.3, bankruptcies 13 -> 13.

Key constants (Bout.cs): KdOffset 71, KdHealth 0 (knockdowns no longer tied to accumulated damage - front-loads finishes),
OutOffset 47, FlashBase 0.0045, StopOffset 22 / StopHealth 50, PounceStopOffset 45, SubOffset 58, SubBottomPenalty 16 with SubFlex 0.25
(keeps Flexibility above the relevance floor), CutOpen 0.04 / CutWorsen 0.5, division power 0.86 (straw) .. 1.16 (HW), women x0.88.
MatchRating knockdown weight 12 -> 30, finish bonus 20 -> 22 (holds the star distribution).

Dead ends: lab matched-pair finish rate UNDER-predicts the world (world has mismatches and locals, which stoppages hit harder):
lab proxy 38.4 vs base 38.0 still came out +2.2 pp in-world. Always calibrate with a CI compare run, not the lab.

## Pre-step-3 baseline (main 7ce2628)
Golden ce24ec976588fde0 / 937bdf982dea33b4. Lab 3 rds finish 38.1 (KO 13.8 TKO 0.5 SUB 23.9), 5 rds 59.7; KD 1.50; 10-8 33%.
In-world 40x5: finish 45.3 (KO 17.2 TKO 0.8 SUB 27.3), KD 1.66/bout, 10-8 38.7%.

**Why:** later steps (fatigue and priced attributes, step 4) must report before/after against these.
**How to apply:** `tools/longsim fightlab quick 2` for tuning; `gh workflow run balance.yml -f head=<branch> -f base= -f shoestring=` for the in-world block (needs the longsim reporting from PR #37). See [[stored-card-reshuffles-world]].
