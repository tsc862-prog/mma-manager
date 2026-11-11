---
name: engine-baseline-numbers
description: FightLab bulk numbers and golden-master hashes for the engine as of 2026-11-10 (PR #14 benchmark, PR2 correctness/copy); compare future changes against these
metadata:
  type: project
---

Baseline engine (main fbcedf3 + PR #14 benchmark), FightLab scale 10:
- 3 rds: finish 38.4% (KO 13.7, TKO 0.5, SUB 24.2), UD 51.9, SD 9.7, MD 0, Draw 0; avg 12.3 min
- 5 rds: finish 59.2% (KO 23.9, TKO 0.7, SUB 34.6); avg 17.4 min
- 10-8 share of rounds ~33%; knockdowns 1.52/fight, 9.3% end it
- Scale 1: style spread Brawler vs Well-rounded 14.2 pp (step-4 target 6 pp)
- Never read in the cage: Strength, Composure, Heart, Discipline, height, reach, stance.
  Negligible (<1.15 pp/CA pt at 30->90 swing): Cardio, Recovery, ClinchControl, PunchPower, KickPower (only max of the two is read).

Golden master (4000 bouts): results ce24ec976588fde0 (unchanged by PR2); scorecards 44461ed3557b9b37 -> 937bdf982dea33b4 after PR2 (stores a deciding judge's card).
PR2 (engine/correctness-copy): card favours loser 4.0% -> 0%; level stored cards 7.1% (bug 2 tie-break, step 3); pbp repetition 34.9% -> 13.7%; text-off alloc 13.8 KB -> 1.2 KB/bout.

**Why:** later steps (stoppages/scoring, fatigue, priced attributes) must report before/after against these.
**How to apply:** rerun `tools/longsim fightlab 10` and compare; any outcome change updates both golden constants in FightBenchmarkTests. See [[stored-card-reshuffles-world]].
