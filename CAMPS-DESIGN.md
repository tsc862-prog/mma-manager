# Camps & Teams — Design

*Drafted 2026-07-07. Status: designed, not built. Builds on the RelationshipGraph
(save v26): `RelationshipKind.Mentor` is reserved for this feature, and camp
membership becomes the main engine for Friend bonds.*

The fantasy: fighters don't develop in a vacuum — they belong to **teams** (the
American Top Team / Gracie Barra / Tiger Muay Thai of this world). Where a fighter
trains shapes how they grow, who their people are, and what you (and your rivals)
can see through the fog. Retired greats stay in the world as **coaches**, and the
best of them build dynasties.

---

## 1. Entities

```
Camp
  CampId, Name              "Apex Combat Club", "Bandeira Jiu-Jitsu", "Emerald Tiger"
  City                      camps live in real map cities (geography tie-in)
  Specialty                 PrimaryStyle (+ secondary): what it teaches best
  Reputation 0–100          the camp's name in the sport; grows with members' wins/titles
  HeadCoach                 FighterId? of a retired great (null = journeyman coaching staff)
  CoachQuality 0–100        derived: head coach's peak ability + accolades + years coaching
  Capacity                  elite camps are selective (~12–20 trainees); local gyms are open
  MonthlyFee                what a fighter (or their org) pays to train there

Fighter.Camp : CampId?      null = trains solo / unaffiliated (a real disadvantage)
```

Persistence: save **v27** — `camps` table + `camp_id` column on fighters.
`RelationshipKind.Mentor` (already in the model since v26) finally gets populated.

## 2. World build — designed camps, like the designed roster

Seed **~16 designed camps** matching regional fight culture, same philosophy as
`DesignedRoster` (authored day-one world, procedural growth after):

- USA: a Vegas super-camp (all-round, elite), a Midwest wrestling factory, a
  Miami striking gym
- Brazil: two BJJ dynasties (Rio vs São Paulo — instant turf war with the CK/RCS
  storyline), a Chute Boxe-style aggression camp
- Thailand: a Muay Thai temple camp; Japan: a legacy dojo; Russia/Dagestan: a
  sambo/wrestling mountain camp; Europe: Amsterdam kickboxing, London MMA;
  Africa/Oceania: one rising camp each (homegrown-hero synergy)

At build, ~70% of fighters get assigned by **home city proximity + style match +
tier** (stars land at elite camps — which makes those camps scouting honeypots).
The pecking order is deliberately NOISY: gyms take people, not rankings, so
lower names share the mats at big camps alongside the stars (user call,
2026-07-07 — "the biggest earners will likely get the spots, but it's not
uncommon for lower names to be part of big camps").
Designed champions get hand-picked camps in `DesignedRoster` (a `Camp:` field),
so storylines start alive: the ICF lifer trains at the legacy dojo, etc.

## 3. Membership dynamics (monthly, in the tick)

- **Newgens** join a camp near their hometown (or start unaffiliated if none fits).
  Dynasty newgens join their famous relative's camp — the family pipeline.
- **Switching is rare and story-driven**: a 3-loss skid ("looking for new coaching"),
  a mentor retiring, or moving up in the world (title contender → elite camp
  invitation). Small monthly chance, gated by capacity + fee; news for names.
- **Camp mates become friends**: `Bonds.RunMonthly` gains a camp channel — same-camp
  pairs form Friend bonds at a much higher rate than mere rostermates, and those
  bonds deepen faster (cap 95). This supersedes the current roster-only formation
  as the primary friendship engine.
- **Teammates refuse to fight** — a hard matchmaker rule (stronger than friend
  avoidance): only a title collision books campmates, and it's a mega-storyline
  ("teammates collide") with the bond damage the Bonds system already models.

## 4. Development — the core payoff

`FighterDevelopment.Advance` already takes a growth multiplier (the player org's
coach staff uses it). Camps feed the same lever, per fighter:

```
campMult = 0.85 (unaffiliated)
         | 0.95 + CoachQuality/100 × 0.25         (0.95–1.20)
         × (specialty matches fighter style ? 1.05 : 1.0)
```

- **Channel shaping** (ties into the fog-of-war `StatChannel`s): a BJJ camp grows
  ground stats fastest; a striking camp, standup. Cross-training arc: a one-note
  fighter at a well-rounded camp targets their weakest channels — journeymen can
  reinvent themselves.
- **Aging**: elite camps (CoachQuality ≥ 75) soften the decline curve slightly —
  the "old lion at a great camp" archetype.
- **Stacking guard**: camp multiplier × player-org coach multiplier is capped
  (~1.35 total) so the two systems don't compound into ability inflation. Elite
  camp capacity is the population-level guard — only so many fighters get the
  best rooms.

## 5. Coaches — retirement is a beginning

On retirement (`Lifecycle`), notable fighters roll a next chapter:

- **Head coach of a new camp** (pop ≥ 60 or titles held, in a city without an
  elite camp): "X opens his own gym" — procedural camp founding, the camp layer's
  equivalent of `WorldEvents.GenerateStartup`. Reputation seeds from their fame.
- **Joins an existing camp** as a coach → bumps its CoachQuality; a legend
  arriving is news.
- **Mentor bonds**: a head coach takes the camp's highest-potential prospect as
  protégé (`RelationshipKind.Mentor`, A = mentor): extra dev multiplier (+0.08),
  a slice of inherited persona/fanbase, and a built-in narrative ("Sato's last
  student"). When the protégé wins gold, the mentor's legacy beat fires.

## 6. Fog of war tie-in

- **Own-roster reveal**: your fighters' campmates are seen daily by people you
  talk to — `ScoutingService` reveal bonus for anyone sharing a camp with your
  roster. Signing from "friendly" camps is safer.
- **AI fog**: `PerceivedAbility` error shrinks for fighters at high-reputation
  camps (they're on everyone's tape) — big camps get their members noticed,
  which cuts both ways: your hidden gem at an elite camp won't stay hidden.
- **Camp report** (money sink): pay to scout a camp — every member revealed one
  band for a season. The scouting-staff role gets a real target.

## 7. Player interaction (decisions + money sinks)

You run a promotion, not a gym — you influence camps through money and mail:

- **Stipends**: pay a fighter's elite-camp fee (~$3–10k/mo) — better development
  and a loyalty bump; stop paying and both fade. Line item: `FinanceCategory.CampFees`.
- **Team deals**: sponsor a camp (org-level monthly) — discount stipends for all
  your fighters there, camp-report access, and its prospects prefer signing with
  you (Negotiator appeal — the bond pull, org-level).
- **Decision mail** (FM staple): "Your champ wants to move to the Vegas
  super-camp: $8k/mo and better coaching, but he leaves his mentor and his two
  best friends." Approve → dev up, bonds strained. Refuse → loyalty hit.
- **Endgame**: bankroll a retiring loyal champion's new camp in your home city —
  a pipeline that feeds you prospects (the zero-to-hero dynasty closer).

## 8. AI behavior

Orgs don't manage camps; **fighters** choose them (dev- and story-driven), and
AI orgs exploit the visibility (poach targets at famous camps are correctly
priced). Elite camps naturally fill with big-org stars — geography of talent
emerges: Vegas/Rio/Phuket become scouting hubs, which the map screen can show.

## 9. Build plan (phases, each shippable + testable)

1. **Core** — Camp entity, designed seed camps, build-time assignment, dev
   multiplier, save v27, camp shown on fighter profile.
   *Tests: dev delta measurable over 24 months; assignment coherent (style/city);
   round-trip.*
2. **People** — retirement→coach pipeline, procedural camp founding, mentor
   bonds, camp-channel friendships, teammate refusal in the matchmaker.
   *Tests: legend retires → camp founded/quality bump; mentor boost; campmates
   never booked on undercards.*
3. **Player** — stipends, team deals, camp reports, decision mail, fog bonuses.
   *Tests: fee billing, reveal bonus, appeal pull.*
4. **Color** — camp rivalries (two camps whose fighters keep colliding), camp
   pages in the UI, "teammates collide" mega-beat, map-screen camp markers.

## 10. Risks / tuning watchlist

- **Ability inflation** — the multiplier cap + camp capacity are the guards;
  validate with a 30-year probe (population mean ability should stay flat).
- **Friendship graph size** — camp-channel formation needs the per-fighter cap
  (≤3 friends) or matchmaking starves at small orgs whose roster all train together.
- **Churn noise** — camp switching must stay rare (< ~2% of fighters/month) or
  the news feed drowns.
- **Determinism** — camp choice rolls come off the tick's seeded RNG like
  everything else; no wall-clock anywhere.
