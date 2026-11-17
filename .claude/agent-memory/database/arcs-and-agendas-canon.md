---
name: arcs-and-agendas-canon
description: 2026-11 arcs + owner agendas pass: where they live, how beats are detected and recorded, the text rules, the 31 arcs and 25 owner agendas, what was left for a save break or for world-sim
metadata:
  type: project
---

Branch database/arcs-and-agendas (draft PR). No model field, no save break, no RNG draw. `Flavor/Arcs.cs` (engine), `ArcsModern.cs` (24 arcs), `ArcsOrigins.cs` (7), `OwnerAgendas.cs` (25 owners), `OwnerVoices.cs`. One hook: `WorldTick.AdvanceWeek` calls `Flavor.Arcs.RunWeekly` + `Flavor.OwnerVoices.RunWeekly` just before `Digest.Weekly` (text only: News + one accolade).

Arcs: cast by "First Last" (NOT FullName: it carries the nickname). Beats share a Slot = alternatives (the belt changed hands, or it did not); a slot needs the one before it told. Conditions are observable (Booked, Won, Rematch, TitleWonFrom, Defended, Defences, WonStreak, LeftOrg, SignedWith, Retired...); bout/title conditions only count the last 14 days AND a date after the previous beat (so a beat is always about a NEW event). Max 3 beats a week. Day-one calendar already books some pairs, so those slot-1 beats fire in week one.
Fired record = accolade on the Lead, title "Saga: {arc title}: {beat tag}" (saved with the fighter, never evicted; news caps at 1000 so the feed item is NOT a safe record). Valuation reads only "gold/silver/bronze"/"Discovered"; the accolade is inert. Arc ids, titles and beat tags are the saved record: never rename.
Text rules (tests enforce): no digits, no hidden-ability words, no first/only/never/every/nobody/ever, tokens only {AOrg}{BOrg}{ARec}{BRec}{Belt}; Origins arcs no ranking/stream/belt/title/glove.
Owners: stateless. OwnerVoices reads the week's news headlines (" signs with ", " away from ", " dethrones ", " claims the vacant ", " vacates the ", "announces retirement") and posts a General item "{Owner} on {Last}" by a hash roll (Mix(seed, fnv(headline)^date^promo^moment^salt); share 35 signs / 70 others). If the headline wording in Contracts/News changes, update OwnerVoices.Moments. Page line is appended in Biographer.Promotion after the owner note. Origins owners: Pastor, Hiyama, Sampaio + arrival founders Fukuhara, Kessler, Beaulieu (Raczkowski is a CAMP coach, no agenda).
Left out (needs save break): former teammates (camp history), arcs keyed to a stored "feud reason" or per-arc progress counters. Wire-worthy agendas for world-sim: BuryRival (Ilic vs Reuss, Bittencourt vs RCS), Sell (Feldstein), Hometown (Nwosu won't export cheaply, Zulu/Chambi/Wattana), Spectacle (Faisal, Ravelo), Money (Hennessy, Cartwright).
