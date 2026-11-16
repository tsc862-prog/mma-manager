---
name: origins-followups
description: Origins follow-ups PR #60 (2026-11-16, no save break) - band/open-aware Desk replacements, bracket->card rounds rule, derived split text, Era.ClassFought, OriginsStory.PlayerNotes
metadata:
  type: project
---

PR #60 (branch world/origins-followups, draft). Builds on [[origins-phase2]].

- Desk class checks: the Origins-safe pattern is "same class OR open bout/house OR the band division (Lightest != null,
  filed under bout.WeightClass, Matches(stay)) Matches(incoming)". Gate the extra branch on world.Mode == Origins so
  today's error text and order stay identical. Any `new Fight {...}` copy of a booked bout must carry OpenWeight
  (ReplaceFighter used to drop it). ReplacementPool/Suggest/AvailableRoster/EligibleOpponents were already band-aware.
- Rule decided: a bracket house that splits becomes a card house AT THE CARD STANDARD (OriginsReforms.CardRounds=3 for
  Fight.Rounds/Standard/Title); BracketRounds=2 only while it runs brackets.
- Split wording derives from OriginsReforms.BandsAfter (LimitLbs of each line): first split = "over 185 lb" (heavy band
  is LHW..HW), not the lore's "about two hundred pounds" (that chapter text is database's, left alone).
- Era.ClassFought = AllowsDivision OR a live house has a non-open division filed under the class (band counts only for
  its top class). AllowsDivision still waits for the first champion.
- OriginsStory.Gaps stays the dev note; PlayerNotes/PlayerNote(key) is the player wording (test bans "phase"/"build").
- longsim origins 5 12 stayed HEALTH OK after the rounds rule. CI `origins 10 12`: main fails 1 seed, branch 2, all
  the "roster N < floor" check (DLB 7<8, SFA 9<10) - that check is noisy at one-man margins on main too.
- ROOT CAUSE of "X roster N < MinRoster" (traced 2026-11-16, 40 seeds): every AI case holds exactly its signing
  target ON THE BOOKS (FreeAgencyService counts Status != Retired, i.e. injured men count); the check counts only
  Status == Active and samples the final tick, 2005-01-07, which is DLB's 8-week night day in many seeds (0 days
  after the night, 5-6 men hurt) or a week after SFA's. Not a sim bug. The scripted player was a real cause (it
  counted Active only and held a head count, so a split SFA sat at 12 across 7 bands): fixed in the script.
  APPLIED (lead agreed, c616965): fail on "on the books >= MinRoster"; fit-today is a note with days since the
  last night. origins 40 12 on the branch: OK (main, old check: 5 seeds fail).
- balance.yml: when longsim exits non-zero (origins HEALTH FAIL), the "Artifact name" step is skipped and upload
  fails with "artifact name input ... empty"; read the job log, the outputs are still printed there.
- Modern identity proof that worked: diff the per-side log text (compare 40x5 + shoestring 7/13/42 + shoesweep 1-20).

**Why:** UI review found these; the lead wants Modern byte-identical. **How to apply:** reuse FitsBout-style gating for any
new player-side weight check; give the UI ClassFought/PlayerNote instead of letting it cache.
