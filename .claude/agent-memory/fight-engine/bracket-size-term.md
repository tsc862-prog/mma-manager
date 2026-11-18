---
name: bracket-size-term
description: PR #86 (2026-11-17) - bracket bouts ask EventService.SizeTermFor; how often split-house brackets occur; custom+modern-rules size term is relative only
metadata:
  type: project
---

PR #86 (branch engine/bracket-size-term): `TournamentService.Run` sets `BandedSizeTerm = EventService.SizeTermFor(...)` on every bracket bout.

- **How rare the case is:** a probe over 3 Origins observer worlds x 20 years found 77 bracket nights. 76 were at open-weight houses, where the rules already turn the size term on. Only 1 was at a split house: CRU's last bracket, booked before its split, on 1999-06-04 in observer-7, under custom rules.
  - A bracket house becomes a card house when it splits or takes the code, so only "the bracket already on the books" hits this case.
  - Class brackets (`wc` not null) never run in the game.
- **No TickIdentity pin moved**, Modern or Origins.
- **Engine change:** `ApplyRules` with `_size && _rules.IsModern` (custom path only through carried condition) now runs `ApplyRelativeSize` instead of the full OpenPower/HeavyGas term.
- **Text-only quirk:** `AfterExchangeRules` says "All that size is a heavy load" for a 110 kg+ man when `_size` is set. On the custom path this makes text differ between same-size heavy men, but results do not.
- **Test trick:** to get "flag off" at world level, set `w.Mode = WorldMode.Modern` before `RunOneNight`. `SizeTermFor` is gated on the mode, so this turns the flag off while seeds stay the same (seeds come from the fight id).
- **Lab numbers:**
  - Two-man bracket at the split CRU, 300 nights: the big man wins 70.3% with the flag on and 42.0% with it off.
  - Engine, carried path on today's rules, 113 v 77 kg: the big man wins 81.2%.

**Why:** the reviewer of #83 found that brackets bypassed the only setter. See [[unified-size-term]].
**How to apply:** any new path that builds its own FightParams for a booked bout must call `SizeTermFor`.
