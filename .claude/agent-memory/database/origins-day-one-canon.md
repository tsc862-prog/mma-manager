---
name: origins-day-one-canon
description: Origins mode (1993) authored canon: 3 founding promotions, 43 pioneers, voices; where it lives and the rules it follows
metadata:
  type: project
---

Origins day-one content lives in `Core.Sim/Generation/Origins/` (PR #49, draft, branch db/origins-pioneers). Not wired into any builder.

- Promotions: CRU Crucible Challenge (Los Angeles, Len Pastor; 8-man one-night PPV, 20-min single round, draw at limit), SFA Shinken Fighting Association (Tokyo, Kosaku Hiyama; 3x5 rounds, judges, 3 rope escapes), DLB Duelo Livre (Rio, Anibal Sampaio; garage gym-duel, 30-min single round, bare hands).
- 43 men: CRU 10, SFA 10, DLB 11, free agents 12. Greats (70+): Zandvliet (NED kickboxer), Neves (BJJ), Orekhov (sambo), Hagiwara (shoot-style pro wrestler), Aguiar (luta livre).
- Feuds: Quintela|Paranhos, Hagiwara|Ishikari, Zandvliet|Delacroix, Mgeladze|Orekhov. Only ONE row of each pair sets RivalOf (the builder would double-add otherwise).
- Surnames must not collide with today's designed roster (side tables key on last name); Vilela is the one shared surname, keyed by "First Last".
- Skills are arithmetic (overall + discipline offset, -24 for untaught technical skills); no RNG.
- Showcase generation: run the test with ORIGINS_SHOWCASE_PATH set to a file path.
