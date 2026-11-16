---
name: contracts-staged-plan
description: Owner's 2026-11-11 answers on the contracts plan (output/review/contracts-review.md) and which stages are built
metadata:
  type: project
---

Plan: `e:\MMA Manager\output\review\contracts-review.md` sections (4) stages, (5) owner questions.
Owner answers 2026-11-11: deals END at 0 with a 30-day exclusive window; rival offers 2-5/yr, clock
stops only for top-billed/champion/final week; no bluffs v1; matching a rival is PLAYER-ONLY first;
matching rights as a contract term now, release clauses later; bidding-war fighter chooses by his
wants (home, title path, friends) not richest stage; promises with teeth = activity, home show,
title shot; asks from public facts done (#41).

Built: stage 5 (deals end + TalksMemory) on PR #43 (2026-11-12); stage 6 rival offers
(RivalOffer + OfferMarket, WarnExpiring removed) on PR #45 (2026-11-12, v46), see
[[rival-offers-invariants]]. Stage 7 free-agent offers on PR #46 (v47, 2026-11-13), see [[free-agent-offers-invariants]]. Stage 8 UI (PR #47). Stage 9 promises + matching rights on PR #48 (v48, 2026-11-13), see [[promises-invariants]]. Next: stage 10.

**Why:** the lead hands these stages out one PR each. **How to apply:** check which stage is next
before designing; see [[deal-window-invariants]].
