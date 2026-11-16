---
name: origins-money
description: Origins money tuning (PR #64, 2026-11-16, no save break) - why CRU declined and founded houses folded, the 1993 rent/room/bracket/prize/bank levers, 40-seed before/after, the RoomIn perf trap, the ORIGINS MONEY report
metadata:
  type: project
---

PR #64 (branch world/origins-tuning, draft). Builds on [[origins-phase2]] and [[origins-followups]].

Causes found (longsim `origins 40 12`, ORIGINS MONEY block, branch world/origins-tuning-base = main + report):
- Founded houses: the atlas rooms rent at today's 7-11 a seat (WorldMap.AddVenue) against a 23-dollar
  Origins ticket; founders' authored halls are 4 a seat. Rent was 9.6k of a 23-24k gate a night; net
  -47k per house-year. And RoomIn's "sold out -> 2x crowd, closest room" could never step from a ~1,100
  hall (500+8m) to a ~3,600 forum (1500+30m): demand 1,800-1,900 sat in 1,050 seats for years.
- CRU: the 30k cheque was ~120k/yr (37% of gate) and a bracket paid finalists 3 purses + 3 win bonuses;
  then the Backlash exile years finish it (8/40 folded on main by year 12).
- Scripted player at CRU: the Origins script never re-signed men in the player's deal window, so its
  stars walked and its crowds were a third lower than the AI's CRU. A harness bug, fixed in the script.

Levers (all Origins-gated): Financials.RentOf (min(rent, 4/seat)), also in Negotiator.PurseCap's Origins
branch; RoomIn picks max (crowd*seat - OriginsProduction - rent) for the expected crowd (past same-city
uncapped nights = their crowd, else Financials.DemandAt without the clash scan); bracket payroll = each
man's purse once, no win bonuses; CRU prize 20k; generic founding bank 50k + Range(0,6)*25k (same draw).
Results 40x12 on phase-3 main (29e733a): CRU folds 10 -> 0, median end 332k -> 752k; founded 6y survival
47/84 -> 74/84, 9y 7/46 -> 37/46, net per house-year -48k -> -8k. Lowering the bank barely moved folds:
survival is set by the yearly drift; the bank only sets when the unlucky go. PurseCap must budget rent for
the seats it expects (SeatsRent): a house in a forum otherwise priced its men to the 500 floor and starved.
Player founding (PR #65) shapes the bank as NewcomerBank(steps); keep base + steps.

Traps: PurseCap calls HomeVenue -> RoomIn ~1M times a run; anything in RoomIn that scans world.Events
(Compute's IsClash) multiplies runtime (20-year CI run went 16 -> 56 min). No caches (save/load parity).
20-year origins runs fail on main too (18/40 on 29e733a; branch 29/40): Boom-era houses with 6-8 divisions
(target ~48 men) whose purse cap sits at the 500 floor under the 750 ask floor sign nobody and run short in
2010-12. More limping houses survive on the branch, so more of them are counted. Unfixed (contracts floor).

**Why:** the owner wants founders solvent on shows and new houses a real part of the sport.
**How to apply:** read ORIGINS MONEY before touching Origins prices; ORIGINS_NIGHTS=<abbr> traces a house.
