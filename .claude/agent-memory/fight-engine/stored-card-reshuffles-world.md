---
name: stored-card-reshuffles-world
description: Changing FightOutcome.RoundScores (even with identical results) reshuffles the whole world via Storylines.Controversies and exposed a world-sim dictionary-order bug in a persistence test
metadata:
  type: project
---

`Storylines.Controversies` (world-sim) rolls 12% "ROBBERY?" on the weekly world stream only for close decisions, and a UD's closeness is read off `RoundScores`. So any change to the stored card, even with byte-identical bout results, changes the draw count and reshuffles every later world roll (longsim compare not byte-identical; a control build with the old card WAS byte-identical).

That reshuffle made `PersistenceTests.PlayerPaths_CancelledEventsAndReleasedLocals_DoNotChangeWhatTheLoadedWorldDoes(holeInTheMiddle: True)` fail (2026-11-10, PR2 engine/correctness-copy): first divergence week 7, new fighters 6190-6195 get different attribute rolls. Root cause is world-sim iterating `World.Fighters.Values` in enumeration order where it feeds a roll (the skipped strict test documents the order differs from id 6188 after a cancelled event); seed 21 used to hide it by luck.

**Why:** "results unchanged" is not "world unchanged" — the engine's text-free outputs (cards) feed world rolls.
**How to apply:** before claiming an engine change is world-neutral, run longsim compare against base; if a persistence hole test breaks, it is likely this order bug, which world-sim owns, not the engine.
