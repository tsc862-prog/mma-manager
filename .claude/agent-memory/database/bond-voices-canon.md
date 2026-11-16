---
name: bond-voices-canon
description: 2026-11 bond voices: tie-aware quotes (Quotes.Say with World) and Biographer.People; how ties are read, shares, era rules, what is not modelled
metadata:
  type: project
---

Branch db/bond-voices. Text only; no model change, no RNG. `Ties.Strongest(world, me, other, on)` reads bonds, Rivalry, real fight history (events dated at least 7 days before `on`, so the bout being quoted is not "history"), camp, country. Precedence: Family, Mentor/Student, Rival, Rematch (series lean Leads/Trails/Level), Friend, Teammate, Countryman.
`Quotes.Say/Attributed(world, f, moment, on, other)` takes a tie line by hash roll (salt 0xB0D5A11, share 75/75/75/70/55/60/40/30); the world-less overload is untouched and pinned by hash in BondVoicesTests. Only result moments (WinFinish/WinDecision/TitleWin = won; Loss/TitleLoss = lost) have tie lines; `other` is always the OPPONENT of the moment. Lines live in `Flavor/TieLines.cs` (hot register = Loudmouth/Hothead/Showman, else cool); append only; no digits/counts/"first/only"; era-safe for 1993 (no ranking/weight/glove/ppv/stream).
`Biographer.People(world, f)` returns PeopleLine(Kind, Other, Text); `Biographer.Fighter` appends it after the career line. Feud reason shown only when both are Designed (DesignedRoster.FeudReason, or OriginsPioneers.FeudReason in Origins mode).
Not modelled (proposal): former teammates (no camp history on Fighter); a feud reason is not stored on Rivalry.
Call sites switched to the world overload: Living/News.TitleChanged, Storylines callout. WPF Moments.cs / BriefingViewModel still call the world-less one (UI area).
