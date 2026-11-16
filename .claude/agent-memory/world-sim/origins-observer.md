---
name: origins-observer
description: Origins observer start PR A (2026-11-16, no save break, v53) - World.HasPlayer, Options.OriginsObserver, ScoutingService.PublicRead, Mail no-op, id-0 booking guards, what was audited
metadata:
  type: project
---

Branch world/observer-world. Builds on [[origins-era-gates]] (observer notes there) and [[origins-phase2]].

- "No player" = PlayerPromotion == default (id 0; ids start at 1). `World.HasPlayer` is computed, unsaved. The save already
  carried a NULL player column, so no shape change.
- `Options.OriginsObserver(seed)` = `Origins(seed) with { Observer = true }`; Origins builder flags nobody. It builds
  EXACTLY what `Origins(seed, playerIndex: -1)` built (test asserts graph-equal). Modern build throws on Observer.
  NB longsim `origins` AI runs build with CRU as player then clear it: CRU gets no front office / Objectives there,
  so that harness world != an observer world (not changed; it is balance-tester's file).
- `PublicRead(world, f)`: PerceivedAbility's model with familiarity = record (+ camp exposure), no own floor, competence 0,
  hash (evaluator 0, salt 0x0B5E). Least informed by construction. Pure, no stream.
- `Mail.Post` returns when !HasPlayer. Audit: the only sim reader of world.Mail besides Post is Lifers.RecentlyMailed,
  behind `mine` (player-only). longsim reads Mail only in Check/WorldChecker (ref check) and PlayerScript (player runs).
  Post draws nothing and its arguments are evaluated before the guard, so no stream moves.
- Booking: `PlayerBooking.NoHouse/NoHouseText` guard TryCreateEvent/CreateEvent, AddBout, AddLocalBout, Editable (Move,
  SetMainCardSize, Replace, SetVenue), RemoveBout(world), CancelEvent; IsMine/UpcomingEvents/CardsBooking need HasPlayer.
  OriginsShows.ScheduleTournament/ScheduleCard refuse only when called FOR the player (promoId == PlayerPromotion):
  OriginsReviewTests use ScheduleTournament as a helper on AI houses, a blanket !HasPlayer guard broke two tests.
- The only sim site that creates something owned by PlayerPromotion is PlayerBooking.TryCreateEvent; every
  `== world.PlayerPromotion` test elsewhere matches nothing once no id-0 event/contract/title exists.
- AdvanceWeeksStoppingForPlayer with no player == AdvanceWeeks == AdvanceWeek(runPlayerEvents:false): OfferMarket
  returns null without player rules, RunDueEvents' filter only excludes id-0 events. Test runs 5 years week-for-week.

**Why:** founding mid-game (PR B) builds on this. **How to apply:** new player-facing code tests HasPlayer; the UI's
observer read is PublicRead, never true ability.
