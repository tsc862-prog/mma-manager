---
name: origins-phase1
description: Origins phase 1 (save v52, PR #55, branch world/origins-phase1) - the 1993 world build path, open division, OriginsShows calendar, 1993 money levers and the targets they were tuned to, scene gate, review lessons
metadata:
  type: project
---

Built 2026-11-15/16 (draft PR #55, reviewed, fixes in 26df632). Builds on [[origins-era-gates]].

- `Mode == Origins` dispatches `WorldBuilder.Build` to `Booking/OriginsWorldBuilder.cs` (own stream). Phase-0 gate
  tests keep today's world shape via internal `Options.GatedModernShape`.
- Open division = `Division.Open` flag (Matches = sex only), nominal class `Division.OpenClass`. Never add a
  WeightClass member for it. Open belts are NEVER handed down (VacateBelt/departure -> vacant, `TitleReign.Vacated`).
- `ForwardCalendar.RunWeekly` hands Origins worlds to `Living/OriginsShows` after `Audit`. Tournament nights are
  `Event.Format = Tournament`, empty until `WorldTick.RunEventCore` runs them. Every bracket win = accolade
  "Won {night}" (durable; news caps at 1000). One bookability rule: `OriginsShows.CanBook` (bracket field too).
  PlayerBooking mutators refuse tournament nights. Title-shot promise at a bracket house = place in the bracket.
- Money tuning target (lead, 2026-11-15): over 5 AI years each house ends ~0.8-2x its bank, lean years possible,
  no folds. Origins levers: bracket FightNight tier + 1.35 ticket premium (SuperCard tier tripled CRU's bank),
  crowd 0.5x, ticket 20+0.4/pt, production 4,000+9/head, purse cap from own room /14 units floor 500, asks =
  standing share of the market ceiling (min 0.15), sponsors 0.2x, merch 150/pt + $1/head, night bonus 50/pt,
  overhead 1,500+300/pt, prestige climb 0.5x, SFA 10-week / DLB 8-week cadence. Result seeds 1-5: CRU 0.8-1.5x,
  SFA ~1.6x, DLB 1.6-2.0x. The big early leaks were side income (merch, sponsors), not gates.
- Modern identity: compare 40x5 + shoesweep + shoestrings IDENTICAL to main at 4a7900c.
- longsim `origins N Y`: digest determinism for AI and scripted runs, cadence floor, active band 80-160,
  bank band 0.4-2.5x. Bash heredocs with `'''` python sometimes fail in this shell: write scripts with Write.

**Why:** later phases build on these seams. **How to apply:** Origins-only branches test `world.Mode` (or Origins-only
data) before any draw; measure with `longsim origins N Y` (seconds per seed).
