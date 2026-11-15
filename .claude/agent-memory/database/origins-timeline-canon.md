---
name: origins-timeline-canon
description: Origins timeline authored content (54 chapters, 16 media partners, 13 sponsors, 10 arrivals): where it lives, the rules, canon facts to keep consistent
metadata:
  type: project
---

Lives in `Core.Sim/Generation/Origins/` (branch db/origins-chapters, draft PR; not wired). Chapters split by era in `OriginsChapters.Era1..6.cs`; vocabulary in `OriginsCapabilities`; trigger condition keys in `OriginsChapters.KnownConditions`.

- Chapter keys are saved: never rename. Windows are months from Jan 1993; each chapter's earliest >= its requirements' earliest AND latest >= their latest (tested). Closing chapter `the-sport-you-built` requires every leaf so nothing is orphaned.
- Each capability (6 media, 7 sponsor types, 9 classes, 9 plain) is unlocked EXACTLY once. Weight order: Heavy+Middle (1999ish), LightHeavy+Welter, Light+Feather, Bantam, Fly (late), Straw with the women's division.
- Country scenes only use atlas countries that have a city (Germany, Spain, Italy etc. have none): 30 wake in 8 chapters, NED/CAN first, GHA/MAR/SEN/PER last.
- Canon introduced: Keystone Event Network (PPV that drops the sport), Nocturne Cable (lean-years lifeline), Hokuto Television (Japan weekly), Frontline Sports Network, Harbor Broadcasting Company (prime time), Cascade Live (in-house stream tech), Lumen Play (major streamer). Arrivals: Seiryu Grand Combat (Tokyo, Ryoji Fukuhara), Bellwether Fighting Championship (Las Vegas, Dale Kessler; the Boom giant), Bellona Fight League (Toronto, Marguerite Beaulieu; all-women), Fenwick Street Combat Club (Chicago, Dennis Raczkowski), fighters Voss, Moreira (DLB), Slobodin, Aubuchon (first women's star, BLF), Nakagawa, Asante.
- Surname "Marchand" collides with today's roster (side tables key on surname); renamed to Aubuchon. Always test new surnames against DesignedRoster.
- Narration rules: no real people/brands/events; 60-120 word bodies; slots {Org} {Founder} {City} {Country} {Fighter} {Year} {Partner}; no "stream" before 2008, no "app/social media" before 2005, no internet words before 1995; ByPlayer/ByRival come as a pair, null needs a Note.
- Showcase: run `OriginsChapterTests.Showcase_RendersTheWholeTimeline` with ORIGINS_TIMELINE_PATH set.
