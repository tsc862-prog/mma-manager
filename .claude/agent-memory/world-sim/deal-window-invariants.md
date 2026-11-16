---
name: deal-window-invariants
description: How the player's deals end (DealWindow, v45) and the traps found building it — Settle runs before the card is marked complete, single-booking rule, harness org runs as AI
metadata:
  type: project
---

Built 2026-11-12 on branch contracts/deals-end (PR #43, save v45): `Core.Sim/Contracts/DealWindow.cs`.
Player's deal at 0 fights -> 30-day exclusive window (`Contract.WindowEndsOn`), then free agent.
Everything gated on `DealWindow.PlayerRules(world, promo)` so all-AI `compare` is untouched.

Traps worth remembering:
- `EventService.Settle` runs while the card is still `Scheduled`: anything that pulls bouts from
  "scheduled player cards" during settlement will rip the bout off the card being settled
  (Collection-modified crash). Pass the card's id as an exception.
- A fighter can be on at most ONE scheduled card (ForwardCalendar.Audit first-claim + PlayerBooking
  BookedElsewhere), so "his last fight is booked and he is on another card" cannot arise naturally.
- **tools/longsim shoestring/shoesweep set `PlayerPromotion = default`**: the founded org is an AI
  org, so any player-only rule never reaches it unless the harness opts in. DealWindow uses a
  per-world `ConditionalWeakTable` opt-in (`HarnessPlayerRules`); the older static
  `FreeAgencyService.HarnessHandsOff` is reset by each shoesweep run's `finally` while parallel
  runs continue (a small race, not fixed).
- LocalCircuit's monthly taker loop draws one `rng.Chance` per eligible fighter: exclusions must
  come AFTER the roll or the shared stream shifts.
- Before v45 the player's run-out deals never ended (TryPoachFromPlayer monthly forever); the
  tick no longer calls it. Tests still call it directly.

**Why:** owner answers 2026-11-11 (contracts review Q1). **How to apply:** later stages (rival
offers = stage 6) hang off `DealWindow`/`World.Talks`; see [[contracts-staged-plan]].
