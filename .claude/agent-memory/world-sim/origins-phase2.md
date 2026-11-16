---
name: origins-phase2
description: Origins phase 2 (save v53, PR #59, branch world/origins-phase2) - timeline engine rules, contested capabilities, Backlash/bans/licences, weight bands, founding, the tuning that kept CRU alive, longsim origins health line
metadata:
  type: project
---

Built 2026-11-16 (draft PR #59). Builds on [[origins-phase1]] and [[origins-era-gates]].

- Engine: `Living/OriginsTimeline.RunWeekly` (WorldTick, Origins only, after the month block). A chapter is LIVE when
  month >= Earliest and every Require fired on an EARLIER day (same-day chaining caused order violations via the
  scripted player). Dated chapters: keyed 2-12 months after their last requirement. Deadline = LatestMonth.
- Contested caps: Media/Sponsor of a live "SignMedia/SignSponsor" chapter and scenes of a live HoldEventInNewCountry
  chapter are open to whoever is first (Era.Allows/AllowsMedia/AllowsSceneIn). Era.Scenes (talent) stays unlocked-only.
- Bans start the DAY AFTER the chapter (a card fought that tick was otherwise "in a banned region").
- Crown detection must accept ANY class the chapter draws (FW crowned before LW broke the unlock invariant).
- HoldEventAbove = attendance, not capacity (Origins crowds are 0.5x: it fires at the deadline; phase 3).
- Weight lines: `Division.Lightest` bands; DrawLines must strip booked bouts that cross the new lines (incl. title
  bouts not inside the title's division) or champions end up outside their band; ResultsProcessor's champ-vs-champ
  move-up is skipped for banded divisions.
- Tuning that made 10/10 worlds healthy over 12 years (CRU used to fold in ~half): bracket house becomes a card
  house when it splits (kills the 120k/yr prize), licence 10%/mo for a rules house banned at home, +10% reform
  pressure when banned, Origins debt ceiling 0.25x, rooms sized to crowd (RoomIn: 600+100*prestige, step up when
  sold out), talent target grows with houses' roster demand, split houses share depth per division.
- Generic founded houses still mostly fold within 2-6 years (authored SGC/BFC thrive): reported, not fixed.
- longsim `origins N Y`: health = sport alive, chapter order/window, nothing used before unlock (a week's grace),
  no night in a banned region, cadence for solvent houses alive all year; money only notes now.

- Review lessons (2026-11-16): a licence must beat any ban (Era.Banned checks sanctions first); a split must clear
  Rules.Fight.OpenWeight (size inside bands goes through FightParams.BandedSizeTerm, the one Engine switch); Origins
  deals/offers must take OriginsBroadcasters names via keyed picks (Modern keeps its rng draws); scale Distress with
  the Origins ceiling; monthly "once" announcements use the window (PreviousMonthlyTick, this tick]; any threshold
  chapter must sit under the founding ceiling BEFORE it fires; never create a venue-less card (TryCreateEvent).
- Founded-house folds (cause, untuned): they sell out ~1,000-seat halls (98% full) where production + rent eat the
  whole gate (22k vs 23k a night); purses (7k/night) and overhead (40k/yr) are the loss, ~-40k/yr. Not the Backlash.

**Why:** phases 3-6 build on these seams. **How to apply:** new Origins effects hang off ApplyEffects/detectors;
keep keyed rolls; check `longsim origins 5 12` (~20 s) before CI.
