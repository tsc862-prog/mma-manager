---
name: rival-offers-invariants
description: Contracts stage 6 (v46) rival offers — OfferMarket rules, rate tuning numbers, the window interplay, and traps found building it
metadata:
  type: project
---

Built 2026-11-12 on branch contracts/rival-offers (save v46): `Core.Sim/Contracts/OfferMarket.cs`,
`Core.Model/RivalOffer.cs`, `World.RivalOffers`, `DecisionKind.RivalOffer`.

Rules as built:
- Lodged only on the LAST fight (FightsRemaining == 1) of a player-rules deal (DealWindow.PlayerRules),
  once per deal (reuses `Contract.PoachWarned` as the flag). Never inside the exclusive window.
- Weekly hash roll `Mix(seed, fid*0x9E3779B9 ^ day ^ salt)`, chance 0.03 + 0.002*pop (+0.10 champion),
  cap 35%; hard cap 5 per holder per trailing 365 days. Tick: `OfferMarket.RunWeekly` runs after
  RunDueEvents and BEFORE `DealWindow.RunWeekly` (so a closing window with an offer goes to the rival).
- Let go (or silence past 21-day deadline): window still the player's; offer deadline extends to
  window end; at close he signs the lodged terms if rival solvent + HasRoom + can pay signing bonus,
  else Withdrawn and DealWindow closes him to FA.
- Match = rival's structure, purse so OfferMarket.Score(ours) >= Score(theirs), floored at live purse
  and the table floor. The table (NegotiationSession.Resume only) uses the match purse as a floor.
- Desk calls of kind RivalOffer are excluded from Decisions.RunMonthly's 2-call cap and expiry loop
  (otherwise the cap would skip `rng.Chance(0.35)` and shift the weekly stream in player worlds).

Rate measurements (test harness, founded org script-only ~20 roster): ~13 eligible deals/yr, each
~7 weeks on the last fight with a suitor. Flat 25%/wk saturated the cap (5/yr every seed); flat 8%
gave 3-5/yr. A small org's decent fighters almost always have a bigger suitor.

Review fixes (2026-11-12, commit 3150c85): Desk card rebuilt from live MatchTerms each tick end and
after signings/departures (OfferMarket.RefreshDesk) — a frozen price lied once his last fight was
fought; the table scores the WHOLE deal against the rival (a purse floor alone let thin bonuses
undercut); settle on LeavePromotion/Retire; no LetGo timeout (benching dodge); PoachWarned cleared
when the rival falls away or a clause tops the deal up; Score's personal discount only at his own
promotion.

Traps: offer ids are Max+1 and get reused after old offers are pruned (365 days after resolution) —
key durable counting on (Fighter, Made), not Id. `FreeAgencyService.HarnessHandsOff` is
[ThreadStatic] (safe in parallel tests). The test-file python edits: use io.open(newline='') to keep CRLF.

**Why:** owner's 2026-11-11 answers ([[contracts-staged-plan]]). **How to apply:** stage 7 (free-agent
offers, table reads the offer) and stage 10 (AI expiries through OfferMarket) build on this; see
[[deal-window-invariants]].
