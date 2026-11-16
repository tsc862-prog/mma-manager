---
name: promises-invariants
description: Contracts stage 9 (v48) promises with teeth + matching rights — rules as built, the "right never dearer" min(), voice rule, traps
metadata:
  type: project
---

Built 2026-11-13 on branch contracts/promises (PR #48, draft, save v48).

Rules as built:
- Promises (activity 2-4 fights / home show = his home COUNTRY / title shot) live in `World.Promises`, belong to
  fighter+promotion (not a contract). Value enters `Negotiator.Appeal` via `Promises.AppealOf` (only when the slip
  HasPromises, so AI offers are byte-identical). Monthly test runs just before Lifers.RunMonthly.
- Broken = -15 loyalty, Lifers `GrievanceKind.BrokenPromise` for 365d (cleared by a later kept promise), and
  `Promises.Distrust` (+10%/break, max 2, 730d) read by BOTH NegotiationSession and AskingQuote (asks must agree).
- Leaving by any door unkept = Broken (LeavePromotion hook); retire/fold/sale = Void. Deal running out does NOT break;
  the window-close leave does.
- Matching right: MatchTerms = min(score-beating match, max(rival purse, live floor)). The min matters — at a
  non-exclusive house the score match can be BELOW the rival's dollars (Score divides non-exclusive money by 0.8),
  so "exact match" alone would make the right a cost. It only bites at an exclusive house vs a bigger stage.
- Table lines are the FIGHTER in first person (owner: talks are promoter and fighter only; no agent/"we"/"his
  people"). `AgentStance` -> `Style` (BargainingStyle) + `Stance`; obsolete alias kept for the UI branch.

Review fixes (lead, 2026-11-15): leash premiums must also land ON floors (live purse, rival match) and
rival acceptance uses FinalScore — otherwise a matching right/clause costs $0 whenever a floor binds.
Deadlines capped at deal end (min(365, fights*120)); broken promises are public news + Wariness (+3%/break, max 3,
365d; promise appeal halved per break). Separate Warned (deadline, reset when it moves) vs LastFightWarnedOn (per
deal). Promise ids from World.NewPromiseId (saved in IdCounters). Mail.Post drops an unread letter with an
identical subject — give distinct warnings distinct subjects. Lead runs balance; don't start runs.

Follow-up (PR #54, 2026-11-15): public Wariness and personal Distrust are TWO reasons, never one
(Promises.TrustAt/TrustReasons; NegotiationView.PublicBreaks/WarinessPercent/BrokenToHim/DistrustPercent/TrustNote).
His break quote = Promises.BrokenLine (variant by promise-id hash; "left" vs deadline told apart by
Note == DeadlinePassedNote). Promise options are judged on the SLIP's Fights (View(bool,bool) passes DesiredFights).
PutInWriting keeps OffTerms but sets LastRefusal. PurseOff was always exact; a UI mismatch there is a stale view.
Country text mid-sentence goes through Geography.CountryNames.InSentence.

Traps: tests hiring a fighter at his ask then pricing promises see no change — LivePurseFloor floors the number;
price on a free agent. The Bash heredoc chokes on long python edit scripts: Write a .py to scratchpad and run it.

**Why:** owner answers 2026-11-11 ([[contracts-staged-plan]]); builds on [[rival-offers-invariants]] and
[[free-agent-offers-invariants]]. **How to apply:** stage 10 (AI expiries through OfferMarket) must keep promises
and rights player-only; any new table text stays first person.
