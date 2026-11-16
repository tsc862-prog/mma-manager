---
name: free-agent-offers-invariants
description: Contracts stage 7 (v47) free-agent rival offers — Holder = courting promotion, how the FA pass defers, final offer + one improvement, harness courting, traps
metadata:
  type: project
---

Built 2026-11-12 on branch contracts/free-agent-offers (save v47).

Rules as built:
- `RivalOffer.Holder` for kind FreeAgent = the COURTING player-rules promotion (talks memory owner),
  so `NegotiationSession.Resume`'s `rival.Holder == promo.Id` check reads both kinds unchanged.
- Hook in `FreeAgencyService.RunMonthly`, at the exact point it would `Sign`: `OfferMarket.TryLodgeFreeAgent`
  lodges the same `AiOpeningOffer` terms (deadline 10-14 days, keyed hash) if `CourtedBy` (World.Talks,
  not soured, <=60 days, PlayerRules) and the shared 5/yr cap has room. `Have()` adds `SeatsHeld` (the
  rival holds his seat); FAs with a live offer are skipped by the quiet market. FreeAgency draws no RNG,
  and every added read is a no-op when RivalOffers/Talks are empty -> all-AI worlds untouched.
- No Desk card for FA offers; the table is the channel. Clock stops in the final week, and on the
  rival's improvement (deadline pushed to >= date+7).
- Final offer (`PutInWriting`; `LodgeFinal` is internal, stored on `RivalOffer.Final`): lodged only if
  FinalScore (Score / ChampionsClausePremium when the clause is in) >= rival's Score. Next tick: rival comes
  back ONCE only if its keyed 50% roll says so (`WouldComeBack`) and it is solvent + HasRoom; it raises purse
  to CeilingPurse = min(PurseCap, ask) (never past its cap), then a signing bonus up to half that purse.
  Its first offer usually already sits at that purse, so the come-back is in practice a signing bonus.
  A walk-out cancels a lodged final.
- Caps (review, lead's ruling 2026-11-13): FA offers <= 2/yr per courting org AND count toward the 5 total;
  <= 1 FA offer per courting org per monthly pass. Lodge gate == resolve gate (distress, HasRoom, bonus cash).
- Score applies TableDiscount for the courting side of a live FA offer (else BeatRival erased a
  discoverer's gratitude).
- Rival signs at deadline / on LetGo / if he walked out of the owner's talks (soured memory). Talks
  merely going cold do NOT end it early (would break the deadline the owner was told).
- Retire of a free agent -> `OfferMarket.OnRetired` (Lifecycle.Retire else-branch).
- Patience +1 on a fresh sit-down with a live offer; resumed talks get +1 only if offer Made > LastTalked
  (strict, so reopening same day can't farm patience).

Harness: shoesweep courts by default (`CourtFreeAgents` monthly, `AnswerFreeAgentOffers` after every
week since deadlines < 4 weeks); `--no-court` = old script. So shoesweep numbers moved partly from the
script change, not just the feature.

Traps: persisted ContractOffer.Round is not saved (NotSaved entry in PersistenceGraph); the Bash tool
chokes on long quoted heredocs with C# interpolation — write spec files with Write and apply with a
python replacer instead.

**Why:** owner's 2026-11-11 answers ([[contracts-staged-plan]]); builds on [[rival-offers-invariants]].
**How to apply:** stage 8 (UI) reads `NegotiationSession.View` only; MainViewModel's stop text still says
"Match it on the Desk" for FA offers — stage 8 must fix it. Stage 10 moves AI expiries onto OfferMarket.

Merged as PR #46 (c7d4ab4) 2026-11-13. Review-fix balance: compare + shoestrings identical; courting
sweep mean 34.2 / National 16/20; --no-court sweep identical to main. Script lodged only 3 finals (51 found
none inside 1.3x stretch) — the come-back is untested at scale; an unpushed harness fix widened the final
budget to 1.25x PurseCap and added a real signing bonus.
First balance (run 43343647311, 2026-11-13): compare 40x5 byte-identical to main; shoesweep 1-20x12 script-only:
mean 33.8 (main 33.3), National 14/20 (main 16/20); offers/seed-year EOD 3.17 + FA 0.82 = 3.99. Script lodged
0 finals (MatchTerms purse always past its 1.3x stretch) — improvement path untested by the harness.
