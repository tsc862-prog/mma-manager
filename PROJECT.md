# MMA Manager — Project Brief

A single-player MMA **management sim** in the vein of Football Manager, OOTP,
TEW, and WMMA. You play as the **owner/booker** of a fight promotion in an
all-fictional world: sign fighters, book cards, make matchups, run the
business, and watch a text-based fight engine simulate the bouts. No real
people or companies (geography IS real: real countries, cities and regions, like FM/OOTP).
Minimal graphics — this is a data-and-systems game.

This document is the master context for the project. Read it first; it captures
every decision made during planning so work can continue without re-deriving
anything.

---

## Current status — pick up here (updated 2026-07-05)

The core sim (model → fight engine → booking → living world → persistence) is
**done and stable**, and there's a working **WPF app** in a dark "fight-night"
theme on top of it. `dotnet test` is at **231 passing**. Recent sessions have
been polish + player-facing surfacing, not core sim.

**Core fantasy (decided 2026-07-03): Zero to Hero.** The main way to play is to
found a brand-new promotion (or take over a small one) and build it to the
international stage. The established AI orgs are the ladder; the talent
treadmill (develop cheap prospects, big orgs poach them) is the core tension.

**Done recently (this + last few sessions):**
- **Temperament, voice, and people behind the desks (save v42, 2026-08-15).
  SAVE-BREAKING** (fighters gained `disposition` + four temperament columns;
  promotions gained `owner_name`/`owner_note`/`founded_year`; staff gained
  `background`). A second aliveness pass, this one on the LIVE world:
  (1) **Fighters have a character, and it isn't their portrait.** The sim used
  to read temperament off `Fighter.PersonaTag` — a tag the WPF layer stamps
  from the ART PACK at load — so a fighter's willingness to start a beef was
  decided by which picture they were handed, and **headless worlds had no
  personalities at all** (`Storylines.Spice` returned 0 for everyone). Now
  `Disposition` (8 archetypes: quiet professional, loudmouth, showman,
  hothead, mercenary, company man, warhorse, student) + a `Temperament` of
  Ego/Professionalism/Temper/Ambition. It feeds beef-starting (`Spice`),
  asking price and agent toughness (`FighterValuation`), what a fighter WANTS
  from a deal (`OfferPriorities`), development rate, how hard being benched
  bites, fame growth from a win (near neutral in aggregate — it redistributes
  rather than inflates), and WHO misses weight (professionalism inverted,
  replacing a coin toss). **51 designed fighters are authored** — the bios
  always said this out loud and the sim finally hears it: O'Rourke "predicts
  rounds, calls shots" is a Loudmouth, Volkov's "no entrance music, no
  interest in leaving" is a Company Man, Al-Marri "reads every offer sheet
  that lands" is a Mercenary. All HASH-DERIVED from the fighter id (the
  `StarRoll` trick), so it costs the shared generation stream nothing.
  (2) **The sport has a voice.** All ~50 mail messages and the entire news
  feed were third-person narration — nobody in the world ever said anything.
  `Flavor.Quotes` gives 8 dispositions × 11 moments (win/loss/belt/cut/
  passed-over/lowball/signed/retiring/missed-weight) picked deterministically
  off fighter id + date, so a save reloads word for word. Wired into title
  changes, callouts, signings, retirements and scale drama.
  (3) **Front offices and owners.** Staff were four anonymous candidates with
  names pasted from two word lists, and **no promotion employed a single
  one** — so hiring was picking a number and rivals had nothing to poach.
  Now 18 authored professionals with histories, every AI house staffed to its
  prestige, generated candidates given a background line, and 19 authored
  owners (name + what they're like to work for + founding year) who **sign
  their own board notes** — `Mail.Owner(world)` replaced the literal "The
  Owner"/"The Board" at 8 sites. Two guards learned the hard way: staff
  effects stay `IsPlayerControlled`-only (handing twenty orgs a marketing
  department lifted every gate in the sim and broke the persona/hometown
  draw tests), and the player starts with EMPTY desks (a founded shoestring
  inheriting a ~35k/mo salary went broke by show four).
  (4) **Buildings with names.** Every city on earth got the identical three
  rooms — "{City} Fight Hall / The {City} Forum / {City} Arena" — so a poster
  from Osaka and one from Lagos named the same venue. `Geography.Venues` names
  the three rooms in all 49 atlas cities (Korakuen Basement → Ryōgoku Hall →
  The Sunrise Dome), and a signature annual show now goes HOME and into the
  BIGGEST room: ICF's Emperor's New Year finally runs in the dome its lore
  always claimed.
  (5) **Contracts have a life stage.** Every fighter in the world opened on
  3-6 fights at the identical formula price. Now ~12% are on their final
  fight, a slice are locked up 6-8, ~18% are bargains the org is enjoying and
  ~12% albatrosses signed on last year's form. Fixed a real flattening bug on
  the way: `PurseCap` applied LAST meant **the AFC's entire roster — champion
  and prelim alike — was paid exactly $65,500**; capping first and spreading
  around it (plus an ability tilt) gives a big org's books a shape
  ($48.5k–$105k, Mercer top). And `DesignedRoster.PurseFactors` puts the lore
  in the payroll: Okafor "fights for peanuts by choice" while the Gulf pays
  its hired guns 2.2×.
  374 tests green (+12 `TemperamentTests`). NOTE: contract and nickname rolls
  both derive everything from a SINGLE `NextULong`, deliberately — spending
  extra draws in `Generate`/`SignContract` reshuffles every tuned seed in the
  project, which is what broke four unrelated tests mid-session.
- **The world has a PAST, a warm half, and a front page (2026-08-15,
  no save-schema change — everything lands in already-persisted tables:
  fighters, camps, title lineages, relationships, news).** A pass on the
  initial configuration, aimed at how the first hour reads:
  (1) **THE ERA BEFORE** — `DesignedLegends` (23 authored retired greats) +
  `LegendBuilder`. Their reigns are prepended to the belts they held, so a
  title is a line of succession instead of one man's souvenir (the AFC
  lightweight belt now reads *Vasquez 2010–2015, 6 def → Mercer 2024–now*);
  they run the famous rooms as `HeadCoach` (every designed camp now has a
  name on the door); they carry `Accolades` and authored `Bio`s; and today's
  designed fighters are written as their heirs. Runs AFTER `HistoryBuilder`
  on purpose — that back-dates careers into the RECENT window, which is
  exactly wrong for a man who last fought fifteen years ago, so legends carry
  a plain tally with no staged bouts (same compromise the minted opposition
  already makes). Only the established houses get one: GFC/RCS/SCL are new
  by design and their belts are supposed to feel young.
  (2) **The warm half of the relationship graph** — it used to start
  completely EMPTY (zero friends, family or mentors) even though the bios
  already asserted a dozen bonds. `DesignedRoster.Bonds` (16) seeds them:
  Volkov really did drill Baranov, Matsuda handed Sato the ICF lightweight
  lineage, Okafor and Mensah headline side by side, Rattanak and Chaiya are
  cousins. Mentors can be LEGENDS, which is how a master ends up in a
  prospect's corner on day one (worth `Camps.MentorBonus` development).
  (3) **Fighters train where their story says** — `DesignedRoster.CampHomes`
  (30 entries), claimed BEFORE the general camp sort (`Camps.Build` split
  into `Open` + `Assign`), so a Bangkok muay thai lifer is at Emerald Tiger
  instead of wherever the ability queue landed him.
  (4) **DAY-ONE NEWS** — `Living.Prologue` writes ~30 back-dated headlines
  DERIVED from the built world (real reigns off the lineages, real streaks
  off the records, the seeded feuds, the genuinely unsigned, the hall of fame
  class, the same-region turf wars). The feed used to open BLANK on a world
  with decades of history in it. No invented facts; `PrologueTests` asserts
  every title-change headline matches a real lineage entry.
  Three defects fixed along the way, plus one found by the new feed:
  (a) `NewGameWindow`'s scale presets hardcoded `TalentPool` 14/20/32,
  silently undoing the 18→40→60→**90** tuning — a startup was picking from a
  nearly empty shelf. Now 60/90/140.
  (b) Designed camps anchored to their country's biggest-market city, so all
  three US rooms opened in Las Vegas (including the Miami one) — `CampSpec`
  gained `CityName`; the Miami room became the LA one to match the atlas.
  (c) **Nicknames**: 30 region-neutral names over ~2,500 nicknamed fighters
  meant ~46 men called "The Hammer", designed champions among them. Now
  culture-keyed (13 flavour pools: `Pitbull`/`O Predador` for Brazilians,
  `Medved`/`Grom` for Russians, `Naga`/`The Left Shin` for Thais) over a
  ~6,000-deep combinatorial tail, with world-wide uniqueness enforced in
  `EnsureUniqueName` — which is also what RESERVES the authored ones, since
  the designed roster enters the world first. **2,512 nicknames, 2,512
  distinct.** `RollNickname` derives pool AND index from a SINGLE `NextULong`,
  so the generator's RNG stream is byte-identical to before and every tuned
  seed keeps its shape. It also surfaced a genuine content collision: two
  authored champions were both "El Toro" (Ferreyra → "El Sombrero").
  (d) **A belt is no longer won on debut.** An unbeaten champion's "current
  run" is their whole career, so `PinChampions` handed them the title in
  their first professional fight — the AFC featherweight champ opened the
  game as a 26-year-old with an *eight-year, thirteen-defense* reign and no
  non-title bouts. `MinBoutsBeforeABelt` (3) + `MaxReignBouts` (7) cap it; he
  now reads 4 years, 6 defenses. 362 tests green (+12 `PrologueTests`).
- **Choose your home town when founding (2026-08-09, no save change).**
  `FoundingSpec.HomeCityName` + a Home town combo in the New Game dialog,
  filtered to the chosen region and re-filled when the region changes; the
  region's fight capital leads the list (tagged "· fight capital") and is
  the default, so the old behaviour is what you get by not touching it.
  `WorldAtlas.CitiesIn(region)` reads the static atlas — the dialog runs
  BEFORE the world's geography exists, so it can't ask the World. HQ drives
  where the brand is strongest (`SeedCityPopularity`), the default venue, and
  the hometown-hero draw, so a smaller town is a genuinely quieter start.
  350 tests green.
- **Fog-of-war pricing & display (save v41, 2026-08-09). SAVE-BREAKING**
  (fighters gained `discovered_by`). Tim's play feedback, all pricing-fog:
  (1) **The market pays for what it has SEEN** — `FighterValuation.ResumeFactor`
  floor 0.30→0.15, fully proven at 12 decisive bouts (was 10): an unproven
  fighter is exposure-money cheap no matter how good the hidden tape is or
  how much they grew in the gym, and an 0-1 never out-asks an 0-0 (the one
  thing the market saw was a loss). (2) **Gratitude has an address**
  (`Fighter.DiscoveredBy`, set at LocalScene.Discover): a discovered local
  deals at ~55% with the org that gave them the shot while TotalBouts ≤ 5
  (`Negotiator.GratitudeFactor`, composed into Evaluate/AiOpeningOffer/
  EffectiveAskingPurse — which now takes the fighter); the open market still
  pays the hometown-hype floor. (3) **Public proof prices** — an Olympic/
  world-championship podium adds a medal premium to the ask (gold $12k /
  silver $7k / bronze $4k, biggest only), matching the SpecialCircuit
  accolade text ("… — gold"); crossover fame already prices via pop money.
  (4) **True OVERALL is never shown** — every screen now displays
  `FighterRow.Scouted` = `ScoutingService.PerceivedAbility(world, playerOrg,
  f)`: your scouts' deterministic read (own roster near-exact, unproven
  outsiders off by up to ~10, consistently — the same fog the AI lives
  with). Columns/labels renamed OVR→Scout/Sct across Roster, Fighters,
  Rankings, World Rankings, Contracts, booking picks/tape, negotiation,
  notes, orgs, profile ("SCOUT", tooltip says the truth may differ).
  (5) **The flagship is never quietly for sale** (`Acquisitions.IsForSale`):
  the sweep caught AFC being BOUGHT by IFA at Y4 on seed 8 — prestige 75,
  299 fighters, killed by a six-month buyout wave that dipped it $14M into
  the red. Folding needs a −$8M debt-ceiling breach; being bought only
  needed a negative balance, a far twitchier line for a giant with lumpy
  eight-figure cash flow. Now the sport's #1 brand by prestige is excluded
  outright (it can still FOLD on the ceiling like anyone else), and BOTH
  for-sale paths require the trailing-120-day ledger to be bleeding, so a
  thin bank that's climbing is never a sale. AFC back to #1 in 10/10.
  (6) **longsim compare now splits BANKRUPTCIES from ACQUISITIONS** — a
  defunct org is tagged "(bought)" from the buyer's ledger, and the
  aggregate prints the true bankruptcy count. Every "fold" in the v41 sweep
  turned out to be a buyout (AFC bought VMMA + IFA on seed 5; VMMA bought
  IFA + UFL on seed 9); reading consolidation as insolvency sends you
  tuning the wrong system. 349 tests green.
- **The big-money era (save v40, 2026-08-08). SAVE-BREAKING** (events gained
  `hype_bonus`, fights `overturned`, decisions `promotion_id`+`price`, meta
  `last_acquisition`, new `bidding_wars` table). Six features, Tim's picks
  1-6 from the money/drama pitch:
  (1) **Org acquisitions** (`Living.Acquisitions`): the market consolidates —
  a National+ org that IsForSale (in the red, or thin-banked AND bleeding hard
  over the trailing 120-day LEDGER window; thresholds set at "dying" — the
  first cut sold IFA in 10/10 sweep seeds) gets bought by any National+ org
  with 1.25× the asking price banked (MONEY BUYS UP THE LADDER — ICF absorbing
  a fallen VMMA is the story, not a violation). Price = prestige² brand +
  contract book + cash, fire-sale discounted by distress. Champions always
  transfer, belts retire with the banner, the rest hit free agency; the freed
  seat erases one recent-arrivals gate stamp (`PromotionLadder.OnSeatFreed` —
  consolidation is WHY the door reopens). The PLAYER gets first refusal via
  THE DESK (`AcquisitionOffer`) when National+ with ~the cash. Rare and
  seismic: ~3 deals/15yr (12-month cooldown). RATE-MEASUREMENT RULE learned
  the hard way: news caps at 1,000 items — count beats from DURABLE signals
  (ledger `FinanceCategory.Acquisition`, `Event.HypeBonus`, org fates), never
  the news feed.
  (2) **Market-share dilution** (`Org.MarketShare`): operating bands over
  comfort (Int 3 · Cont 12 · Nat 14 · Reg 12, absolute) shave gates and
  rights fees 3%/excess org to a 20% floor — the ECONOMY shapes the pyramid
  now (the v39 gate only stops floods), and the weakest of a fat band bleed
  into acquisition targets.
  (3) **Bidding wars** (`Contracts.BiddingWars`, `World.BiddingWars`
  persisted): pop-62+ free agents with 2+ rooms that CanCarry the number
  (2.5× purse cap or a deep bank) escalate publicly +20% a fortnight, up to
  3 rounds; the winner signs at war prices plus a bid-sized signing bonus
  (the money sink). War fighters are off the quiet FA board; ANY signature
  ends the war — the player swoops via normal negotiation, and the agent
  mails the number to beat.
  (4) **Scandals**: failed tests (0.4%/corner — the old 1.5% quietly made
  hundreds of suspensions a year; visible once overturns started stamping
  records) fine 30% of the purse, and a winning cheat's result is OVERTURNED
  to NC for both (`Fight.OverturnedNc`; records+streaks adjusted, TotalBouts
  invariant holds, profile shows "— overturned"). ROBBERY calls on close
  decisions (12%) pump rivalry heat, sympathy pop, and the rematch machinery.
  (5) **Camp withdrawals & short-notice saves**
  (`Storylines.RollCampWithdrawals`, weekly, cards 1-5 weeks out): AI cards
  get saved by roster bodies or free agents on 1.25× one-fight deals
  (champion out = belt off the card; unranked sub = title off the line); the
  player gets the mail plus a shortlist of callable names. Only
  advance-booked cards live in this window (AI events book-and-run same-day),
  so this is player + superfight texture by design.
  (6) **Press tours** (`Living.PressTour`; the [14,21)-day window is exactly
  one weekly tick wide, so each card tours once): persona-driven pressers set
  `Event.HypeBonus` (electric +8-15%, brawl +12-20% with a commission fine
  and rep hit, sparks silent), spent by Financials on attendance and PPV.
  Player-booked cards tour routinely; 9/10 staged cards hyped in-tick.
  PLAY-FEEDBACK fixes same session: booking screen opens fighter profiles
  (double-click corners on the card sheet or matchup panels); NEW BOUTS ADD
  TO THE BOTTOM of the bill (`Card.Insert(0)` — the main event keeps its
  slot; promote deliberately); DISCOVERED locals price their hype (ask floor
  $5k + $150/ceiling point over 55 while ≤4 bouts) and their record is JUST
  the discovery fight (1-0 by the night's method — the club-tally
  back-dating machinery, RealizeClubRecord, is deleted); COMMISSION TITLES
  from the Org screen (`PromotionSetup.CommissionTitle`, $15k, creates the
  division too if the class was staffed after founding; vacant belt, first
  title fight crowns); "CONTRACT TALKS" button on the fighter profile
  (`ContractsViewModel.OpenTalksWith` — free agents and own roster, lands on
  the Contracts screen with them across the table); HEAD-TO-HEAD line in the
  booking appraisal ("They've fought 2 times (Silva 2–0) — last: Silva by
  KO, Mar 2029") so a rematch is never an accident.
  Verified: 344 tests; sweep on-baseline (AFC #1 10/10, IFA alive 10/10,
  CK/RCS 0/10, zero zombies); 15-yr durable-signal probe: 3 acquisitions
  (ICF bought VMMA — the fragile giant absorbed instead of folding), wars
  steady, superfight pressers firing.
- **A signable day-one free-agent market (2026-08-08).** The founding start's
  FA market was all washed veterans: ~62% losing records (impossible actives
  like 14-35 — careers the game's own washout filter would never allow),
  only ~18% with <3 fights, avg age 28 / ~15 bouts. Three generation fixes,
  no model changes (saves unaffected; same seed now builds a different
  world): (1) **Survivorship in `ApplyRecord`** — win rate is matched-
  opposition (`0.5+(overall-48)*0.004`, floor .42 — a regional fighter
  fights regional peers, ability tilts the rate rather than setting it vs
  the world average), and after any loss that drops a 3+ bout record below
  the washout line (.45, mirroring `Lifecycle.RollWashouts`) the backstory
  can END there — active fighters are washout SURVIVORS, so losing records
  stay short (3-5, not 5-15). Plus `GenOptions.MaxBouts` cap for
  deliberately green talent. (2) **Bimodal FA pool** — 40% of
  `FreeAgentPool` is now `SeedGreenIntake` (age 18-23, ≤3 bouts, real PA
  headroom, ¼ women), kept OUT of the tier sort: unsigned because unproven,
  not because they're the ability sort's dregs. (3) **Founding pool greens
  are green on paper** — the 70% prospect share gets `MaxBouts` 0-4. Result
  (3-seed audit): <3 fights 16-21%→~41%, losing records 60-65%→~40% (and
  mild), avg bouts 15→7. 338 tests green.
- **Rate-based promotion gate (save v39, 2026-08-06). SAVE-BREAKING**
  (promotions gained `last_cleared_tier`/`last_cleared_on`). Design directive:
  the ladder gate is a FLOOD BACKSTOP, not the pacing — an org growing at a
  natural rate should never hit it. The old crowd-priced door counted the
  STANDING population against a day-one-shaped baseline, and since the living
  world naturally drifts bigger (deep rosters + media money genuinely carry
  more big orgs each decade), every stock baseline eventually walled natural
  climbers for 5-17 years (the 30-yr shoestring spent most of its life
  "queued"). Now `PromotionLadder` prices the RATE of arrivals: each step-up
  stamps the org (`LastClearedTier/On`), the market absorbs 2 entrants per
  tier per 36 months free, and overflow pays 0.2×n² of the base cushion
  (+20% busy-market premium, +80%, +180% — a land-rush stops itself; arrivals
  age out and the door reopens on its own, no seniority decay needed). The
  INTERNATIONAL door is the one deliberate exception: a standing wall —
  1.3×(residents beyond ONE) on operators (SizeOf), no decay, no world-growth
  scaling. Paired with a slower in-tier climb (`Standing.DeltaStepSlope`
  0.62→1.05 — climb speed only, equilibria unchanged) so arrivals stagger.
  Reference 30-yr shoestring (seed 42): tier crossings Y7/Y11/Y22, ends
  prestige 67 Continental #18 — vs the old run's Y8/Y17 and 13 straight years
  dead-queued at National. Sweep on-baseline (AFC #1 10/10, ~1 Int @Y5, zero
  zombies). Known trade-off, documented as taste: with the gate quiet, the
  world's midsection fattens over decades (Continental band ~13-18 by Y30,
  summit ~5) — the "sport globalizes" reading; if a stricter pyramid is ever
  wanted, the shape-holder belongs in the ECONOMY (market-share dilution per
  tier), not the gate. The player remains never-priced (eligibility opens
  their door; the board just reads the room).
- **The alive-world batch (save v38, 2026-08-06). SAVE-BREAKING** (fights
  gained `weight_miss`; new `decisions` table). Six features: (1) **Deeper
  callouts** — winners call out the champ, the last man to beat them
  ("run it back"), or the rung above; each seeds/stokes a Rivalry the
  matchmaker and box office already sell. (2) **Champ-vs-champ superfights**
  (`Storylines.MaybeSuperfight`, ~1/yr world-wide): a dominant champion
  (2+ defenses, 60+ pop) moves up; AI orgs stage a dedicated super-card a
  month out; WIN and they conquer the division for good — weight class
  changes, the old belt passes to its top contender (`VacateTitlesExcept`),
  TWO-DIVISION CHAMPION news + accolade (ResultsProcessor now resolves the
  division by the TITLE, not the winner's weight — superfight corners
  differ). Player org never auto-booked. (3) **Weight misses**
  (`RollWeightMisses`, 4%/bout at weigh-ins): catchweight, fifth-of-purse
  fine to the house, morale/pop dent — and a belt never survives the scale
  (challenger → title off the line; CHAMPION → stripped, vacant). (4) **MMA
  Weekly** (`Digest`): one weekly mail — top stories, the 14-day calendar,
  a rotating ONE TO WATCH prospect. (5) **Signature annual events**
  (`SignatureEvents`): 9 designed orgs each stage a named flagship in their
  signature month at max tier (ICF Emperor's New Year, CK Reinado, VMMA
  Crown of Europe…) — 90/90 staged over a 10-yr probe. (6) **THE DESK**
  (`Living.Decisions` + `World.Decisions`, Home-screen panel with Do
  it/Pass): ≤1 new call/month, 2 max pending, deadlines count as a quiet no
  — TitleShotRequest (yes: jump to #1 + loyalty; no/ignore: loyalty burn),
  SponsorShowcase (accepted promise judged at next event settle: deliver a
  home-region main-carder for a full bonus fee, fail for a strike),
  VeteranCut (board asks; cutting costs reputation). 329 tests
  (`AliveWorldTests`); sweep on-baseline (2 Int + AFC #1 in 10/10).
- **DOUBLE depth — the big-league era (2026-08-05).** Tim: "double those depth
  targets." `RosterTargetPerDivision` 24/20/18/14/12; designed rosters ×2
  (AFC 230, VMMA 190, ICF 180, CK 145, mids ~85-95, City 48-60); women
  22/class (floor 8); FA bench 140; founding pool 90; startups sign 12/class.
  **Activity scaled with it**: Continental+ orgs run BIWEEKLY 13-bout cards
  (Nationals and below stay monthly/10) so a 24-deep division still gets
  everyone ~2 fights/yr. Day one: AFC 301 across 12 divisions (18-40 each),
  world ~2,440 actives; Y30 ~2,490 actives, 659 events/yr. **Re-tuning the
  doubling forced** (each verified by sweep): retainer 500→200/spot (the old
  "pittance" folded RCS/UFL year-one at double rosters); designed treasuries
  ×2 (payroll footprint doubled — runway must too; startup founding cash
  90k→200k); per-show standing gains NORMALIZED past ~16 shows/yr (a
  biweekly giant's card is half the statement — else the climb rate doubled
  and the summit flooded); Int cushion 35M→60M + Int crowd markup 0.9→1.3
  (biweekly banking); BarSlope 0.45→0.50 for deep-era show quality but
  TopTierCurve KEPT at 0.03 (0.045 pushed AFC's own equilibrium onto the
  International cliff and the flagship died on some seeds). Final state: AFC
  #1 in 10/10 (roster ~290, never folds), mids never fold, crown scarce at
  Y5 (1.6 Int avg). KNOWN KNOBS: Y30 summit density is ~8 of 34 (the
  $60-90M war chests queued at 69 do eventually drain through — reads as
  "the sport globalized", tighten via Int perExcess/base if unwanted);
  VMMA mortality 3/10 (design says high-wire, was 1-2/10). 324 tests.
- **A properly populated world (2026-08-05).** Rosters were thin everywhere
  (10-15 fighters at small orgs, ~8/division at the giants). Scaled ~1.5x
  across the board: `RosterTargetPerDivision` 12/10/9/7/6 (Intl→City),
  designed `RosterSize`s up (AFC 76→115, VMMA 63→95, ICF 60→90, CK 48→72,
  mids ~48, City 24-30), women 8→11 per class, `FreeAgentPool` 55→90,
  startups sign 8/class, AI cards 10 bouts (prelims soak the depth — the
  media broadcast windows already split them; fighters still get ~1.7-2
  fights/yr). Day one: AFC fields 149 across 12 divisions (12-18 each),
  giants 120-150, City halls 18-31; ~1265 actives. **Intake now follows
  OPPORTUNITY**: when >¼ of the sport is unsigned, the newgen growth baseline
  drops 4→1 (the fixed baseline grew the FA pool without bound — 146→541 by
  Y5). Verified: 324 tests (global-rankings top-3 → top-5: a class carries
  several orgs' champions plus the women's champs now); sweep healthy (AFC
  ~153 roster, 2 Int + #1 in 10/10, ~1366 actives Y5); 30-yr world 1887
  actives, summit holds (3 Int, AFC 85 contested, $41-43M challengers
  queued).
- **The Organizations app — a full page for every promotion (2026-08-05).** New
  world-side desktop app (🏢, violet accent, `Tab.Orgs = 18`): the master list
  ranks all promotions (logo, LEVEL, prestige, player in gold); the ORG PAGE
  (`OrgsViewModel` + `Views/OrgsView`) shows the masthead (logo, "Continental
  promotion · HQ Las Vegas · #1 of 19 in the world"), prestige/reputation bars,
  the **LEVEL line with climb progress** ("Continental · 80% of the way to
  International"), the Biographer story, **MONEY & MEDIA** (balance, roster
  line, TV/PPV rights deals with fees + months left, sponsorships, the in-house
  streaming platform with subscriber count, house rules), **THE BELTS** (every
  division's champion, reign + belt prestige, reusing `BeltRow`), **THE
  MARQUEE** (top six names, champions crowned, click → profile) and **RECENT
  SHOWS** (click → event page). `OpenOrgCommand` now lands here (fighter
  profiles' company links, etc.) instead of the Rankings tab. Note: `Tab.Media
  = 17` was taken by the (separately built) Media desk app — Orgs is 18.
  UI-only, no save impact.
- **Fighter profile: org history + event click-throughs (2026-08-04).** The
  profile now answers "who have they fought for, and where": a **career-stints
  line** ("Fought under:  SCL '19-'22  →  CK '22-'24  →  AFC '24-") derived
  from the taped record (`FighterRow.CareerOrgs`, hidden for one-org lifers
  still signed there); the **company line is a click-through** to that org's
  Rankings & Champions page (new `MainViewModel.OpenOrgCommand`); the Fight
  History grid gained an **Org column** (who staged each bout) and its
  **Opponent and Event cells are links** — opponent opens their profile, event
  opens the full event page (`FightHistoryRow.EventId/OpponentId/Org`).
  UI-only, no save impact.
- **Churn is CONTRACT-shaped (2026-08-04, same session).** A fighter with
  fights left on a deal rides it out for the guaranteed money — age-retirement
  ×0.35 and washout ×0.15 while signed (winless floors excepted) — while an
  UNSIGNED fighter is far quicker to go: age rates ×2, plus a hang-up term for
  the vet nobody calls (+4%/mo, +9%/mo when the RECORD is why). Careers now
  cluster their endings where deals END: a "sunset roll" in
  `ContractLifecycle.ProcessExpiries` (hash-based, no RNG stream) retires an
  aging fighter at expiry (18%/35+, 35%/37+, 60%/40+, ×1.5 cold momentum)
  before renewal/poaching/market. Yearly honours now exclude the club circuit
  (a promotion-less Local Fight Night can't be Event of the Year — club
  slugfests rate "exciting" monthly). 324 tests; sweep on-baseline (AFC ~104
  roster, 2 Int + #1 in 10/10).
- **The career pyramid: debut 0-0 → grind → climb or quit (2026-08-04).**
  Tim's target world: fighters debut 0-0 at the local level, work up on
  results, rare ones become stars — and failure QUITS (no 0-10 records).
  Built as a churn loop: (1) **washouts** (`Lifecycle.RollWashouts`, monthly):
  a losing record past 3 bouts risks walking away — sharply likelier early in
  a career, on a skid, and unsigned (winless past the fair start: 60%/mo
  unsigned, 30%/mo signed — deliberately harsh; softer odds leaked 0-6
  survivors); quiet by design (News.Retired already gates on fame). (2)
  **Faster age retirement** (~2x rates, hard stop 42) + a "phone stopped
  ringing" term (unsigned, 30+, 10+ bouts: +4%/mo). (3) **Wider green
  intake**: NewgenBaseline 2→4 and intake = retired + washouts + baseline, so
  the base stays wide while the filter prunes it; founding TalentPool 40→60,
  70% prospects aged 19-24. (4) **Market dynamics**: a skidding FREE AGENT
  prices to move (ask ×0.7 at -2 streak, ×0.5 at -4 — how a faded name steps
  DOWN a tier instead of rotting unsigned); National+ orgs don't sign <4-bout
  kids as division filler (debuts belong on the small halls; `RosterPolicy.
  Wants` still lets a genuinely elite/famous prospect through); POACHING now
  requires division ROOM (target+2, draws exempt) — without it the giants
  hoarded the deepening talent base and ate the retainer (AFC ran to ~190 on
  a ~110 target; back to ~103 with the gate). Verified: 324 tests incl.
  `WashoutTests` (no winless 6+; hopeless ≤2%; wide green base); 4-yr probe:
  864 debuts → 163 quit, 440 signed and climbing (top debut kid 7-0, pop 94,
  at a Continental); sweep on-baseline (2 Int + AFC #1 in 10/10, ~1190
  actives).
- **The founding hiring market got real (2026-08-04).** A fresh shoestring's
  free-agent screen felt empty because `FoundingSpec.TalentPool` (18) was
  dumped into the GENERIC world pool — random weight classes, so most landed
  in divisions the org didn't run. Now `SeedFoundingTalent` generates the
  pool (default 40) INTO the founding org's actual divisions — a quarter into
  the women's divisions when they're on — as ~60% raw prospects (age 20-25,
  Overall 34-50) and ~40% affordable journeymen (26-34, Overall 42-58), 60%
  from the home region (local-draw bonuses on early cards). Generated before
  HistoryBuilder, so every rolled record is a real career. Day-one probe
  (seed 42 defaults): 16-21 FAs per founding division, 2-6 at ≤$4.5k asks,
  153 unsigned world-wide. Fixed a fog-of-war-sensitive poach test along the
  way (the new pool's picks can sit on losing skids — `WouldPursue` refuses
  streak ≤ -3 regardless of loyalty). 323 tests.
- **The local circuit became real events (2026-08-04).** Tim asked for every
  fight on a record to be representable in the log — so `LocalCircuit` no
  longer rolls invisible results. Each month, idle fighters (free agents +
  non-exclusive, 180+ days unbooked, 30%/mo) get BOOKED onto real "Local
  Fight Night — {city}" cards per region (`Event.Promotion = default` — a
  promotion-less event that resolves to null org everywhere; smallest hall in
  the region's anchor city; ≤6 bouts). Mostly pro-vs-local (minted club
  opponents, women supported via `CreateOpponent` sex param), occasionally
  (25%) two idle pros who share a division fight EACH OTHER. Bouts run
  through the real FightEngine + ResultsProcessor, so records, streaks,
  fame (capped at the City ceiling for nobody's-show cards), morale, city
  rep, and even local-scene DISCOVERIES all flow; a discovered local's
  pre-discovery club tally is now back-dated into real dated "Club show"
  events at discovery (`RealizeClubRecord` — synthesized outcomes in the
  HistoryBuilder style, BoutHistory front-inserted chronologically). Events
  screen shows these as org "LOCAL". The circuit self-paces (a local card
  counts as activity for the 180-day idle clock → ~2 club fights/yr per idle
  fighter). The profile's "…plus W-L club-circuit bouts" footer stays as a
  legacy-save safety net but should read empty in new worlds. 323 tests;
  `LocalCircuitTests` now asserts the invariant: every pool fighter's
  Record.TotalBouts == BoutHistory.Count.
- **Network personalities + honest records (2026-08-03).** (1) **Every media
  rung now offers real CHOICES** (`PartnerProfile` in MediaService — derived
  from the partner NAME, no schema change): flagship (fee ×1.2-1.25, reach
  ×1.1, stricter clauses — quality/star floors +5, cadence ×0.75), standard,
  niche (×0.8-0.85 fee/reach, no quality clause, patient cadence), budget
  (×0.65-0.75). PPV carriers differ by the promotion's CUT (PrimeView 60% /
  MainCard 55% / BoxSeat 50% — `MediaService.PpvShare` feeds Financials). Up
  to TWO offers per rung can sit on the table at once, so the strict-flagship
  vs easy-budget call is a real decision; tags show on offers/deals/mail and
  the ladder lists each rung's houses. Exposure/fame/brand carry scale with
  the network's reach (a niche channel's prime hour isn't the flagship's).
  (2) **Records with no history, fixed at the source**: mid-sim NEWGENS were
  spawning with the generator's rolled backstory record (up to ~8 phantom
  bouts at age 22) — `GenOptions.WithRecord=false` makes every debutant turn
  pro 0-0 (crossovers already arrived 0-0; the initial build still rolls
  records because HistoryBuilder makes them real). The two INTENDED off-book
  sources — a discovered local's club record and free agents moonlighting on
  the local circuit (`LocalCircuit`) — now reconcile on the profile: a footer
  under Fight History reads "…plus W-L across N club-circuit bouts no
  commission taped" whenever the tally exceeds the listed fights, so no
  number on a profile is ever a mystery. 323 tests; sweep re-baselined
  healthy (1.9 Int avg, AFC #1 10/10).
- **Ladder + media-market fixes from the 30-yr shoestring review (2026-08-03).**
  The 30-yr AI-run shoestring exposed two flaws. (1) **Freed channels went to
  the same orgs forever**: the monthly offer lottery iterated promotions in
  creation order, so a freed partner was always snapped up by early-created
  orgs — NWF spent 15 years as a National org with no TV. Now a released
  partner SHOPS AROUND: it immediately pitches the best qualified laneless org
  (merit order), and never re-pitches the org it just left (their CourtedBy
  entry stands until the partner signs elsewhere — eventual forgiveness, not
  instant amnesia; this also stops a struck-out AI org from re-signing its own
  dropped partner in the same tick). (2) **The cushion floors deadlocked the
  capped middle**: a tier-capped org's economy EQUILIBRATES (bank plateaus
  ~$13.5-14M at the National cap), so any fixed markup floor above that was a
  forever-wall; but zero floor flooded the tier (22 Continentals, empty
  Regional). Landed on: middle-tier markup decays to a **5% residual**
  (senior queuers with plateau banks clear a moderately crowded door; a
  saturating tier still self-seals), the summit keeps 40%; queue seniority now
  **survives a ≤2-point prestige wobble** (a one-month dip used to torch years
  of waiting). Continental base occupancy 4→5. Verified (321 tests, longsim):
  sweep on-baseline (2 Int in 10/10, AFC #1 10/10, no folds); 30-yr world
  3 Internationals w/ contested crown + $41-45M queued challengers; seed-42
  shoestring reaches the Continental door by Y30 (bank $13.98M vs door
  $14.1M, TV landed Y28) — and in looser configs crossed it (Continental Y21),
  so the marginal org's fate is now market state, not frozen math. Remaining
  taste knobs: upper-middle density at Y30 (16 of 34 ≥Continental — trade via
  the 5% residual) and merit-ordered TV means a mid-pack National waits long
  for a channel.
- **The Media desk + a finite media market (save v37, 2026-08-03). SAVE-BREAKING.**
  (1) **Media moved to its own app** (📡 "Media", `Tab.Media`/`MediaView`/
  `MediaViewModel`): current deals per lane, the in-house platform's books +
  launch button, media offers (sign/pass), and THE LADDER — every screen in
  the sport with qualification gates, fees and terms (ON AIR rungs in gold).
  Finance keeps sponsors only. (2) **Networks pitch ONCE** (`Promotion.CourtedBy`,
  persisted): pass on an offer (or let it lapse) and that partner moves on —
  they only return when they re-enter the market. (3) **Partners are exclusive**
  (PPV carrier excepted — it's a pipe): a channel carries ONE promotion, so the
  dial is finite (4 names per linear/stream rung); seeding assigns biggest-first
  down the dial, signing withdraws that partner's pitches everywhere, and a
  dropped/expired deal releases them world-wide (clearing CourtedBy so they can
  court afresh). (4) **Market churn**: at expiry a clean partner re-signs ~75%
  of the time — otherwise they "go another direction", freeing the channel;
  without this the day-one assignments locked the dial forever and no riser
  could ever get on TV. Verified: 320 tests; sweep/shoestring/30-yr
  re-baselined (unchanged health; NWF now holds specialty cable at National
  until a cable slot frees — scarcity as texture).
- **The commercial rework: per-event sponsors + the media ladder (save v36,
  2026-08-03). SAVE-BREAKING.** Killed the idle-income annuity (a do-nothing
  shoestring used to bank ~$10k/mo forever off `20k + prestige×3k` monthly
  sponsorship): ALL commercial income is now earned on fight night.
  (1) **Sponsorships** (`Finance/Sponsorships.cs`): brands pay **per event**
  against contract terms (`DealRequirement` — local fighters, card quality,
  orthodox rules, star power, attendance, a title fight), by archetype (local
  business → supplement/equipment/energy/brewery → national → global) gated by
  reach tier, filling 2–6 slots. Miss the terms → half pay + a strike; two
  strikes → they walk. Terms run in EVENTS (an idle org's deal never pays and
  never expires). Offers arrive by mail, signed on the Finance page; AI signs
  its own. (2) **The media ladder** (`Finance/MediaService.cs`): three LANES —
  linear TV (specialty cable → cable sports → prime broadcast), streaming
  (in-house platform OR a major-streamer license), and the PPV carrier
  (replaces the old `CanSellPpv` prestige gate — no carrier, no buys). Deals
  carry a **broadcast window** (main-card min/max), quality/star floors and a
  **content cadence**; misses strike (3 → dropped mid-term), reach loss kills
  renewal. Rights fees are per event aired and beat the gate at the top
  (prime ~$500k+, major stream ~$900k+/event) while City/Regional stay
  gate-first. (3) **In-house streaming** from day one: `LaunchStream` ($50k +
  monthly ops), subscribers chase `prestige² + star²` scaled by ACTIVITY —
  they grow with every card and melt when dark. (4) **Main card / prelims**:
  `Event.MainCardFights` (last N of the running order; 0 = auto-fit to the
  deal window at run time). Prelim wins earn half pop capped a tier lower;
  main-card wins scale by the platform's exposure (up to 1.5× prime) with a
  fame-ceiling bump — and `Standing` positive swings scale by `BrandFactor`
  (0.75 with no screen at all → 1.25 prime), so media is optional money but
  mandatory oxygen for the climb. (5) **Merch** is a per-event table
  (attendance-driven) + a mail-order trickle scaled by events in the last 120
  days (zero when dark). (6) **Overhead base now scales with tier** (8k City →
  45k International) so the $250k founding runway is a real ~yearlong runway.
  Booking UI gained the main-card selector + live contract-clause warnings;
  Finance UI gained Sponsors/Media/Offers (sign/pass) + the launch-stream
  button. `AShoestring_SurvivesItsFirstYear` is no longer vacuous — an idle org
  now bleeds. **Ladder re-priced for media-era income** (the per-event rights
  money ~10×'d a pinned org's savings rate and mushroomed the summit to 8
  Internationals by Y25): Continental/International cushions $6M/$12M →
  $12M/$35M, International crowd markup 0.35 → 0.9 per excess, the
  media BrandFactor on standing TAPERS out by prestige 60 (a screen gets a
  SMALL show seen; at the top the quality bar rules), seniority floor 40% →
  25% and National base occupancy 9 → 10 (the sticky summit stacked the middle
  and deadlocked a capped Regional org ~$150k under its National door — probe
  line `ladder queued for…` now prints door-vs-bank in longsim shoestring).
  Verified: 318 tests; longsim re-baselined (`*_final.log`): 30-yr pyramid
  1→2→4 Internationals w/ contested crown + $45M challengers queued at the
  Continental ceiling; shoestring arc Regional Y5 → National ~Y13 → Y20
  prestige 44/$15M/#12 queued for Continental; 10-seed sweep 2 Internationals
  in 10/10, AFC #1 in 10/10, zero zombies.
- **Every fighter-bearing message now links (2026-08-02).** Sweep of the
  remaining `Mail.Post`/`News.Post` senders that had a fighter in hand but
  weren't stamping the deep-link id: crossover arrivals (the "whole world is
  talking about X" mail now opens X's profile), local-scene discoveries (news +
  the wants-to-sign mail), camp items (retired star opens a gym, coach joins,
  hopeful wants a bigger camp, teammates collide, the torch passes), bonds
  (family fights, broken friendships), famous-name newgen debuts, grappling-meet
  gold, local-circuit streaks, and Grand Prix winners. With the earlier pass
  (titles, signings, poaches, awards, alerts, faces), effectively every message
  ABOUT a fighter now carries the "👤 Open …" action / FightWire profile link
  + ☆ Shortlist. Sim-side only — ids ride the existing v35 schema.
- **Fighter profile: the career block + the trophy case (2026-08-02).** The
  profile header now states WHO HOLDS THEIR PAPER — `FighterRow.CompanyLine`
  ("Apex Fighting Championship (AFC) · International promotion" / "Unsigned —
  on the open market" / "Retired from competition") over the purse line and a
  new `ContractDetail` terms line (signed date · exclusive/non-exclusive ·
  champion's clause · guaranteed money · signing/performance bonuses · PPV
  points). Above Fight History sits **🏆 ACHIEVEMENTS**
  (`FighterRow.CareerAchievements`, cached): every title reign pulled from the
  BELTS' lineages — 👑 current reigns first in gold ("reigning champion since
  Aug 2017 · 13 defenses · AFC", real thanks to v33 back-dated history), 🏆
  past reigns with their span — then the honours (🏅 Fighter of the Year, 💥
  Fight/KO/Sub of the Year, 🌟 Rookie, 🥇🥈🥉 special-event medals) newest
  first. The old flat gold accolades text in the header was retired (the badges
  emoji row + tooltip stays). UI-only — no save impact.
- **The Notes app + a world you can grab (save v35, 2026-08-02). SAVE-BREAKING.**
  (1) **Notes** (📝, org-side app, notes-yellow accent): a legal-pad **notepad**
  ("the plan", Segoe Print on paper) whose text lives on `World.OwnerNotes`, and
  **THE SHORTLIST** — fighters pinned from anywhere, shown with a live line
  (division/age/record/OVR + "FREE AGENT" in green / "signed to X" / "on OUR
  roster"), ✕ to unpin, double-click for the profile. Both persist with the
  career (`World.OwnerNotes` + `World.Shortlist`; meta `owner_notes` column +
  `shortlist` table; **save v35, older saves refused**; locked into the
  round-trip test). (2) **☆ Shortlist everywhere**:
  `MainViewModel.ToggleShortlistCommand` + labelled toggle on the fighter
  profile, buttons on the Fighters database and the free-agent market, a
  ☆ button on fighter-linked MAIL, and a ☆ link on fighter-linked FightWire
  stories. (3) **More interactible mail/events/news**: event-page bout rows'
  fighter names are clickable (open the profile — `EventBoutRow.RedId/BlueId`),
  on top of the existing mail action buttons and FightWire read-more links.
  308 tests green.
- **The immersion pass — the OS becomes a place (2026-08-02).** The fiction now has
  a name: **PromoterOS** (window title, start-screen footer). Starting or loading a
  career plays a ~2s **sign-in interstitial** (org logo, "Signing in — the desk of
  {org}", org-accent progress bar; skipped under `HeadlessAutoBoot`), then the
  desktop opens on the Dashboard and a **morning briefing toast** greets you
  ("Good morning, boss — N unread. Next card: X, 12 days out"; click → inbox or
  Book Event). **Per-app accent colours** (`AppSpec.Accent`): every window carries
  its app's colour on the title-bar identity stripe, the active border, and the
  taskbar underline — Finance is ledger green, Book Event crimson, News press
  blue, etc. **Toasts chime** (SystemSounds.Asterisk, never in harness runs) and
  slide in; **windows fade/scale in** on open. A **sticky note on the desk**
  (Segoe Print on a rotated yellow note, `MainViewModel.DeskNotes`) lists what
  actually needs doing — next card countdown / book a card, deals running out,
  unread mail, books in the red — and peeks out from behind maximized windows.
  **FightWire went light-paper**: the site inside the dark browser chrome is now
  cream paper with ink type and press-blue links — the one light window in the
  dark OS, deliberately. (Gotcha again: the app-level implicit TextBlock style
  bleeds light text onto paper — give light-themed controls explicit TextBlock
  content.) All renders + selftest green.
- **Deep links + the last two app identities (save v34, 2026-08-02). SAVE-BREAKING.**
  (1) **Mail and news know who they're about**: `MailMessage`/`NewsItem` gained
  optional `AboutFighter`/`AboutEvent` ids (persisted — schema columns
  `about_fighter`/`about_event`; **save v34, older saves refused**).
  `Mail.Post`/`News.Post` take optional id params (all 60+ call sites compile
  unchanged); ids are stamped at the high-value senders: contract/poach alerts,
  show reports + FOTN (event id), title changes/claims/defenses, signings,
  retirements, poaches, callouts, year-end awards, face-of-company stories.
  (2) **The UI acts on them**: the mail reading pane grows action buttons
  ("👤 Open {name}", "🎫 Open the event page") and FightWire stories carry
  "Fighter profile →" / "Event coverage →" links — both routed through
  `OpenFighterCommand` / the new `MainViewModel.OpenEventCommand`
  (`EventsPage.Select` + Events app). (3) **Contracts is a signed document**:
  the offer form is headed "FIGHTER SERVICES AGREEMENT — by and between {org}
  and {fighter}" (Georgia), with a ruled authorized-signature line and a
  "✍ Sign & send offer" button. (4) **Book Event marquee**: the main event's
  row in the card grid carries a warm gold-tinted background. 308 tests green.
- **Desktop stage 3 — floating by default + two more app skins (2026-08-02).**
  (1) App windows now open RESTORED (floating, cascading from the top-left) per
  the user's call — maximize is a double-click away; harness renders still open
  maximized (`OpenWindow.IsMaximized = HeadlessAutoBoot`) so screenshots stay
  full-bleed; layout memory still wins when a window has one. (2) **News is
  FIGHTWIRE, in a browser**: chrome bar with a live address line
  (`https://fightwire.net/latest`, changes with the filter — `NewsViewModel.Address`),
  serif Georgia masthead ("THE FIGHT GAME, COVERED — SINCE 1998"), the category
  filter worn as the site's SECTION NAV (blue-underlined links), a ★ MY PROMOTION
  toggle, and editorial story cards (kicker + date, serif headline — gold when
  it's about you). (3) **Finance is THE BOOKS, a ledger app**: account header
  (monospace gold balance + income/expense/net chips), P&L sheets and the
  12-month grid in right-aligned Consolas figures — green income, red
  parenthesized outflows — plus "THE JOURNAL" transaction sheet. Pure XAML skins;
  view models untouched except `NewsViewModel.Address`.
- **Desktop shell stage 2 — the OS is the game now (2026-08-02).** The sidebar
  shell is DELETED; the pseudo-OS desktop from stage 1 is the one and only shell
  (no more `--desktop` flag). `MainViewModel.NavigateTo(tab)` now always opens
  the matching app window; `SelectedTabIndex` is gone; the `Tab` constants remain
  as the internal navigation ids (and the `--rendertab N` argument, which now
  renders the desktop with app N maximized — all render hooks reworked). A career
  opens on the Dashboard app. New in stage 2: **window-layout memory** — each
  app reopens at its last position/size/maximized state, persisted to
  `%LOCALAPPDATA%\MmaManager\desktop-layout.json` (saved on window close + app
  exit; never applied in harness renders, so screenshots stay deterministic);
  **toast notifications** — a notification corner above the taskbar (max 3,
  auto-expire ~7s, click-through) with new-mail toasts after every advance
  (`Desktop.Notify`), plus a **tray unread-mail badge** (✉ N → opens Inbox);
  and the first **per-app skin**: the Inbox is now a mail client (message list
  with unread dots/accent pill/sender/category/date, Mark-all-read, and a
  letter-style reading pane with an empty state). `MailViewModel` gained
  `MarkAllReadCommand`/`HasSelection`. Verified: all `--rendertab` indexes,
  `--renderdesktop` (now also shows a demo toast), `--renderstart`, `--selftest`.
- **Pseudo-OS desktop shell, stage 1 (2026-08-01) — behind a toggle.** The game can
  now present as the owner's computer: launch with **`--desktop`** and instead of
  the sidebar you get a desktop (org-watermark wallpaper), YOUR PROMOTION / WORLD
  **icon groups**, a **taskbar** (org-logo start button with a two-column start
  menu — apps + New/Save/Load/Quit; running-app buttons with an org-accent active
  bar; the system tray holds the status line, Advance buttons, the GAME date and
  balance where an OS clock would sit), and real **app windows** hosting the same
  `Views/` UserControls the tabs do — drag by title bar, resize grip,
  minimize/maximize/close, double-click-title to maximize, click-to-front
  z-ordering. Apps open MAXIMIZED by default (taskbar switching ≈ tabs) and float
  when restored; windows are singletons per app. Plumbing: `Shell/DesktopViewModel`
  (AppSpec registry, `OpenWindow` state, `OpenForTab` mapping) + `Shell/AppWindow`
  chrome control; `MainViewModel.NavigateTo(tab)` is the ONE navigation seam both
  shells share (all internal jumps rewired through it); `MainWindow` builds both
  shells and prunes the unused one in its ctor (`MainViewModel.UseDesktopShell`,
  set from the CLI flag). Key gotcha solved: hosted views inherit the WINDOW state
  as DataContext inside the canvas, so `DesktopViewModel.ScreenContext` stamps the
  shell MainViewModel onto every launched screen. The classic sidebar remains the
  default and is untouched. New hook: `--renderdesktop <path>` (maximized Dashboard
  + floated Events). Next stages: make desktop the default once it feels right,
  then per-app skinning (mail client, browser-style news, spreadsheet finance).
- **Real fight records — the world has a PAST (2026-08-01, save v33).**
  `Core.Sim/Generation/HistoryBuilder` (runs in `WorldBuilder.Build`, off switch
  `Options.GenerateHistory`) turns every fighter's generated/authored tally into
  a real career of **back-dated completed events** in `world.Events` (~3.4k
  events / ~14k fights, ~380ms, ~3MB save): dated bouts with opponents, methods,
  rounds and synthetic statlines (star ratings read them like live bouts).
  Opponents: same-division roster peers (mutually consistent — the loss shows on
  the other career too; designed rivalry pairs are EXCLUDED, their history is
  authored), minted retired journeymen with real names, and "Local X" filler for
  lesser fighters' early bouts. `FightRecord` + `CurrentStreak` are now DERIVED
  from the bouts (designed W/L totals and authored streaks preserved — locked by
  test). Champions' reigns are real title fights: lineage `WonOn` predates the
  save, defenses counted; authored lore bouts pinned by hand (Mercer's razor-thin
  title decision over Seo, the 1-1 Ozols–Urbonas wars w/ the belt changing hands,
  Hale dethroning Tanner — Tanner's prior reign in the lineage, Rivas' vacated CK
  reign, Okonjo/Batyr title defenses vs Corrigan/Hattori, Farouk's GFC loss).
  Downstream, all for free: profile Fight History full on day one, résumé
  rankings + P4P judge the pre-history (RecomputeAll runs at build), rematch/
  trilogy hype can fire, event numbering continues each org's actual past (AFC's
  next card is ~#152), Rookie of the Year only sees genuine debuts, historical
  venues respect the org reach ladder. New `World.StartDate` (persisted, v33):
  scouting counts pre-save bouts as RECORD, not tape — the fog holds on day one
  (`Rows.BuildScoutReport` filters on it). `HistoryBuilderTests` (12 tests, lore
  locked across 3 seeds); suite at 308.
- **UI overhaul phase 5 — event pages + the Org HQ (2026-08-01).** (1) The Events
  tab became a master-detail **event page** (`EventsViewModel` + rebuilt
  `Views/EventsView`): the world's cards on the left (org logo, stars, gold rows
  for YOUR shows, "My events only" filter), and on the right the full page — the
  poster, the masthead (date · venue, city · tier · promotion), the **box office
  line** (tickets/gate/PPV/production/payroll/net, red when in the red), and the
  card in billing order with portraits, per-bout results ("X wins by KO — R1
  2:33"), star ratings, loser-dimming, ★ TITLE tags and a gold **🏆 NEW CHAMPION**
  tag when a belt changed hands that night (checked against the title's lineage).
  `EventsPage.Select(id)` is the external jump-in (fight-night sim lands there).
  The old `EventRow`/`Events`/`EventResults` plumbing was deleted from
  MainViewModel/Rows (`--selftest` counts `EventsPage.Events`). (2) The Org tab
  became the **HQ** (three columns): THE HOUSE (the Biographer story — authored
  lore leads — plus a history line: events run since when, biggest house,
  milestones) with a **POSTER WALL** of the last six completed shows; THE BELTS
  (every strap with its prestige number, 👑 champion portrait/record, reign line,
  and a "Previously:" lineage of past reigns); and OPERATIONS (the existing house
  rules / marketing / staff / objectives / hiring market, now in one scrolling
  column). Verified via `--rendertab 12` (events) and `5` (org) + `--selftest`.
- **Watch tab retired + rebillable cards (2026-08-01).** The broadcast is no longer
  a sidebar destination (it was empty/stale except on fight night): watching a card
  now opens the modal **`WatchWindow`** ("Leave the arena" to close; closing pauses
  the broadcast clock via `WatchViewModel.Pause`). `MainViewModel.RunLive` shows it
  (skipped under `HeadlessAutoBoot` — `--renderwatch` renders the window's content
  directly). TAB INDEXES SHIFTED AGAIN (Watch removed): world tabs are 8-15.
  Book Event gained **rebilling**: "▲ Up the bill / ▼ Down the bill" move the
  selected fight through the running order (`BookingViewModel.MoveBout` swaps
  within `Event.Card`; main event = last on the sim list; slots, projection and
  the live poster all recompute, selection survives).
- **UI overhaul phase 4 — the front-office screens cleaned up (2026-08-01).**
  Contracts: the free-agent market gained a **name search + division filter + market
  count** (`ContractsViewModel.FaSearch`/`FaDivision`/`MarketLine`; filtering happens
  in `RefreshFreeAgents`) and an **Age** column; the roster panel became **YOUR
  DEALS** with a **Terms** column (exclusive/clause) and expiring deals in gold. The
  negotiation panel is now **THE NEGOTIATING TABLE**: portrait+identity header,
  "WHAT THEY WANT" priorities, thousands-formatted money inputs (StringFormat=N0), a
  live **deal-total line** ("Commits $475,000 guaranteed over 4 fights · up to
  $316,000 more in bonuses" — `OfferTotalLine`), a prominent gold Make Offer, and
  the room's answer colored by outcome (`ResponseTone`: signed green / counter gold /
  rejected red); Release is hidden unless the target is your own fighter. Booking:
  the left column became **PLAN A NEW CARD** (card chrome, gold Create Event) +
  **YOUR UPCOMING EVENTS**; the add-bout panel is **BUILD A MATCHUP** with a gold
  Add Bout; status feedback finally distinguishes success from refusal
  (`BookingViewModel.StatusIsError` via `Ok`/`Err` helpers — "Bout added." is green
  now, not red). Two shared styles landed in App.xaml for all future screens:
  `SectionLabel` (small-caps header) and `Card` (panel chrome). Verified via
  `--rendercontracts`, `--renderbooking`, `--selftest`.
- **UI overhaul phase 3 — fight night is an occasion + saves finished (2026-08-01).**
  (1) **The broadcast got its production values.** The engine now exports the
  canonical judges' card — `FightOutcome.RoundScores` (`RoundScore(Red, Blue)` per
  COMPLETED round, 10-8 on a knockdown/rout, no per-judge noise; TRANSIENT like
  PlayByPlay — never persisted, so no save impact). The Watch screen uses it for a
  **live judges' card** that fills in as each round ends (a quick finish shows only
  the rounds that happened). Around it: a **cold open** (the event poster cage-side
  + a welcome/tease script beat with the main-event names corner-tinted, before the
  first walkouts), a **pre-fight tale of the tape** (age/height/reach/style/form,
  edges bolded) that swaps to the post-fight stat compare, and **winner emphasis**
  (loser's walkout shot fades, 🏆 WINNER tag). Result wording fixed ("X wins by",
  was "X def. by"). `--renderwatch` now lands the still on a decision bout
  (`WatchViewModel.ShowBout`/`BoutWithScorecard`) and renders at 1500px.
  (2) **Saves are a system now**: Load opens an in-app **slot picker**
  (`LoadGameWindow` — every save in the default dir newest-first with age/size, plus
  Browse… for stray files), and an **autosave** (`autosave.mmasave`) is written
  silently after every advance and every settled fight night — never on bankruptcy
  (CONTINUE keeps the last living world), and never from headless harness runs
  (gated on `HeadlessAutoBoot`). CONTINUE's label refreshes after every save.
  Verified: 296 tests green, `--renderwatch`/`--renderstart`/`--rendertab`/`--selftest`
  all pass. No save-format change.
- **UI overhaul phase 2 — views, roster, saves (2026-08-01, same session as phase 1).**
  Four structural steps. (1) **MainWindow.xaml decomposed**: every screen extracted
  into its own `Views/{Name}View.xaml` UserControl (15 views, script-extracted;
  MainWindow is now ~250 lines of shell + start screen). The double-click-to-profile
  code-behind became the `Mvvm/FighterLink.OpenOnDoubleClick` attached behavior
  (handles every fighter-bearing row type; MainWindow.xaml.cs is just the ctor).
  (2) **Theme literals centralized**: 170 hardcoded hex colors replaced with
  StaticResource references; three new semantic brushes (`SurfaceSunken` #181C22,
  `Track` #232A33, `TextFaint` #8A929B); the ~45 remaining literals are genuine
  one-off tints (fight-night banner, watch result colors, map heat). (3) **Roster
  screen** (`MyRosterViewModel` + `Views/RosterView`, tab index 1): your fighters
  with the FRONT-OFFICE columns the world Fighters database doesn't owe you —
  contract (bouts left · purse, gold when expiring), loyalty, morale, camp, and
  availability (ready / booked-on-card / recovering / injured, precomputed from
  scheduled events) — division-grouped, empty-gym prompt for a fresh founding.
  ALL TAB INDEXES SHIFTED (+1 after Home; world tabs now 9-16) — `ViewModels.Tab`
  is still the single source of truth and all navigation goes through it.
  (4) **Save home + CONTINUE**: saves default to `Documents\MMA Manager\Saves`
  (`Wpf/SaveLocator`; dialogs open there, filename defaults to `{abbrev}-career`),
  and the start screen gains a gold CONTINUE button that resumes the newest save
  in that directory (visible only when one exists; load errors surface on the
  menu). Verified: all 16 tabs render via `--rendertab`, `--renderstart` with and
  without a save present, `--selftest` green. No sim/save-format impact.
- **UI overhaul phase 1 — the front door + a shell that wears your org (2026-08-01).**
  Three linked changes. (1) A **start screen**: the app no longer boots straight into a
  hardcoded world — an interactive launch shows a title menu (NEW CAREER → the setup
  dialog, LOAD CAREER, QUICK START → a random-seed AFC world, EXIT) over a vector
  octagon backdrop; `MainViewModel.IsGameStarted` toggles it, and
  `MainViewModel.HeadlessAutoBoot` (set by `App.OnStartup` whenever ANY CLI arg is
  present) preserves the old boot-into-seed-2026 behavior for every render/selftest
  hook. New hook: `--renderstart <path>` screenshots the menu. (2) **Sidebar
  navigation**: the 16 top tabs became a left nav rail (`SidebarTabs` TabControl style,
  App.xaml) with an org-identity block on top (logo, name, tier + climb bar, balance)
  and two labeled sections — YOUR PROMOTION (7 org screens) then WORLD (8 research
  screens, blue) — so what's yours and what's global read apart at a glance. Tab
  indices are unchanged (`Tab` constants; the old divider TabItem is now the WORLD
  section header; `Tab.Camps = 15` added). (3) **Org-accent theming**: the shell wears
  the player org's poster colour — `PosterFactory.AccentFor` (public) feeds
  `OrgAccent`/`OrgAccentDim` dynamic resources on every refresh, colouring the toolbar
  edge, org name, section label, climb bar and selected-tab accent (AFC = gold, CK =
  green, a founded org = its stock palette). Gotcha fixed en route: the app-wide
  implicit TextBlock style was overriding selected-tab foregrounds, so the nav
  TabItem templates render headers via a `TextBlock` with `TemplateBinding
  Foreground`, not a bare ContentPresenter. No sim/save impact; verified via
  `--renderstart`, `--rendertab 0/1/13`, `--renderfounded 0`, `--selftest`.
- **Promotion queue seniority — the pressure valve on crowd-priced climbing (save v32,
  2026-07-10).** A 30-yr shoestring sim exposed the failure mode of crowd-priced
  cushions: NWF sat ELIGIBLE at the Regional ceiling (rep 100, ~$3.4M banked) for 23
  straight years because no National incumbent ever vacated a slot — the queue had no
  drain. Fix: `Promotion.EligibleSince` (persisted) stamps when an org first maxes its
  tier; in `PromotionLadder.PromotionCushion(world,…)` the CROWD MARKUP (never the base
  survival cushion) now decays 15%/year queued, floored at 40% of the original markup —
  so the longest-waiting climber's price drops first, the queue drains in seniority
  order at a bounded pace, and a saturated tier still always costs extra. The clock
  resets on promotion or on slipping below the ceiling. Verified: the same 30-yr
  shoestring now leaps Regional Y5 → National Y15 and ends prestige 44 / $6.5M / #10
  (was stuck Regional #18); 10-seed × 10-yr sweep stays healthy (1-2 Internationals by
  Y10, pyramid holds, 2 designed folds total — no return of top-heavy drift). Locked by
  2 new `PromotionLadderTests`. SAVE v32 — old saves refused.
- **Belt prestige — every title carries its own name (save v31, 2026-07-09).**
  `Title.Prestige` (0-100, persisted): seeded from the org's prestige when the belt is
  created, then pulled toward the CREDIBILITY of the fighters who contest it — an EMA
  (15%/title fight) over `TitleCredibility.Contest`. Credibility uses PUBLIC evidence
  only (record, win streak, ranking within the company, fame — never true ability, so
  it's Fog-of-War consistent), judged against what a belt at that stage demands
  (proven ≈ 5 bouts at City … 18 at International). User's canonical example holds: a
  3-0 prospect in a title fight LIFTS a city belt (cred ~57) but TANKS a world title
  (cred ~32) — unless they're the rare CROSSOVER, where fame is its own credential
  (`max(cred, pop×0.9)`). Effects: a belt on the line adds DRAW in proportion to its
  name (`drawPower += belt×0.3` in Financials — feeds attendance AND PPV buys), title
  wins mint FAME in proportion to the belt (`0.5 + belt/30` extra pop-gain units,
  replacing the flat +1), and the Standing quality bonus for a title fight scales with
  the belt (`maxBelt/14`, was flat +5). Emergent and verified (6-yr sim): AFC belts sit
  74-80, GFC's middleweight strap hit 77 while the org sat at 44 (elite hired guns
  outshining their house), startup trinkets drift to 6-32. Belt prestige shows on the
  Rankings champion card. Sweep unchanged-healthy (2 folds/10 seeds; avg pop +1 from
  richer title fame — watch on long runs). Locked by `TitlePrestigeTests` (4 tests);
  `ActivityAwareBooking` idle bound moved off the knife-edge (⅝, guards the greedy-booker
  regression). SAVE v31 — old saves refused.
- **Résumé-priced contracts — rookies and journeymen are genuinely cheap (2026-07-09).**
  Design rule (user): a fighter's asking price does NOT depend on which org is across
  the table — instead the MARKET contains genuinely cheap fighters: the unproven and
  the losing. `FighterValuation` scales the ability money by a résumé factor —
  `(0.30 + 0.70·experience)` (fully proven by ~10 decisive bouts) `× form` (losing
  records slide toward 0.35) — with a $1,500 floor; popularity money is NOT discounted
  (fame is its own proof — a 0-0 celebrity crossover still charges for the name).
  Prices recover on their own as records build, since deals re-sign every 3-6 fights —
  so prospects are cheap, contenders are not, and "develop cheap talent" is finally
  priced like the core fantasy says. Two knock-on fixes: POACHING gained the solvency
  gate (`BestSuitor` skips distressed orgs — a poach pays the loyalty premium, the most
  expensive signing in the sport; without this, cheap asks made poaches land so easily
  that a distressed VMMA star-shopped itself to death, 9/10 folds), and the distressed
  `BargainAppeal` penalizes proven losers (rebuild with prospects, not deadweight).
  10-seed × 5-yr sweep after: ONE designed fold total (VMMA 1/10, all others 0/10 —
  best world health yet); actives settle ~905 (journeymen displaced by cheap prospects
  retire sooner: the down-and-out treadmill). The shoestring playthrough transformed:
  roster 16 and 13 events in YEAR ONE (was 2-3 fighters farming sponsorship), a true
  invest-at-a-loss first year, Regional by Y5, National mid-teens, same Y20 endpoint
  (prestige 44 · $6.5M · #9). Locked by `AskingPrices_FollowTheResume_NotJustTheTalent`;
  `AiRosterTests` recalibrated (idle bound = majority-busy; the deadweight bar applies
  to orgs at ≥¾ tier roster target — a convalescing org legitimately carries
  developmental depth while it recovers).
- **Venue picking uses the real box office + a simulated 20-yr shoestring playthrough
  (2026-07-08).** `WorldMap.PickVenueIn` was still estimating the draw with the
  pre-rebalance attendance formula (base 2,500 seats), so a prestige-5 startup booked
  ~3,000-seat arenas — and paid arena rent — for a ~400-head crowd. It now mirrors
  `Financials.Compute` (190 + prestige·38 + cityPop·78 + regionPop·14, +900 for a
  typical main event), so shoestrings book the cheap fight halls that exist for them.
  Verified with a simulated zero-to-hero playthrough (`tools/longsim` shoestring mode,
  seed 42): found at prestige 5/$250k, bargain-hire agent (see tools/longsim/README.md),
  City → Regional Y8 → National Y17 (first TV deal) → Y20 prestige 44, $6.4M, #9 of 34.
  Findings recorded in the README: an AI-run founded org can never sign anyone
  (PurseCap ≈ 0 → every negotiation rejected → zombie org; this also makes
  `AShoestring_SurvivesItsFirstYear` vacuous — it survives by never operating), and
  advisor-priced hiring ($8–10k asks) folds a shoestring in year one — bargain
  signings (≤ ~$4.5k) are the viable opening. One seed-fragile test hardened
  (`ActivityAwareBooking` now picks the deepest AI-run org, not VMMA-or-bust).
- **Designed-world sizing pass — no org opens with outrageous books (2026-07-08).**
  Audit found every National+ designed org except ICF/CK opened OVER its tier roster
  target (AFC +24, UFL +20, RCS +18, IFA/GFC +14, VMMA +11, SCM +10) — paying full
  exclusive contracts for seats its own tier economics can't support — and CK (the
  Brazilian establishment) started with a smaller bank ($500k) than the upstart (RCS
  $650k) trying to kill it. Fixed in `DefaultPromotions`: National+ roster sizes now sit
  at their tier target (AFC 96→76, VMMA 74→63, IFA 56→44, GFC 44→32, UFL 50→32,
  RCS 48→30, SCM 40→32), CK's bank 0.5M→1.3M, VMMA 2.5M→3.0M, ICF 1.5M→2.0M
  (its committed book was 1.9× its bank). Regional/City feeders untouched — they sign
  non-exclusive (no retainer) and their looser sizes are free texture. Verified 10 seeds ×
  10 YEARS: CK folds 4/10→1/10, UFL/ICF/IFA/SCM/GFC/RCS 0/10 over a decade, IFA ends
  every seed in the black (was −$3M in 10/10), 2 Internationals by Y10 in 10/10 (summit
  contested), pyramid healthy. VMMA still dies @Y2 in ~4/10 — accepted: it runs the
  widest product below the summit (12 divisions at Continental gate) and when it dies,
  IFA inherits Europe and gets rich — a real emergent story, not broken books. Three
  seed-fragile tests hardened (venue-capacity ties, tournament pool depth).
- **Roster retainer restructured — per-spot cost is anti-hoarding only (2026-07-08).**
  Design rule (user): fighters earn their real money PER FIGHT (purses at settle — already
  true); the only job of a per-roster-spot cost is to stop hoarding, never to bankrupt.
  `Ledger.ChargeMonthlyOverhead` replaced the flat $1k/exclusive/month retainer with a
  banded one: `RetainerPerSpot` $500/mo for spots inside the org's tier roster target
  (`RosterTargetPerDivision × divisions`) and `HoardingRetainerPerSpot` $3k/mo for every
  spot beyond it. Paired behavior: `ContractLifecycle.ProcessExpiries` now lets a wanted
  non-champion WALK when their division is already over target (`DivisionOverTarget`), so
  AI rosters converge to target instead of paying the tax forever. Non-exclusive deals
  still cost nothing monthly (the small-hall lever). Verified on the 10-seed × 5-yr sweep:
  AFC roster 123.6 → 104.5 avg (was 163 in the 30-yr run), sane-sized orgs pay HALF the
  old retainer (IFA in the red 3/10 → 2/10, both trivial), folds improved again (UFL 3→1,
  VMMA 4→3, ICF/IFA/SCM still 0/10; 3 seeds run 5 years with zero designed folds).
  Locked by 2 more `SolvencyGateTests` (6 total).
- **Solvency-gated roster growth — the AI stops signing into the grave (2026-07-08).**
  A 10-seed × 5-yr sweep exposed the designed mid-tier collapsing on most fresh seeds
  (ICF folded 9/10, IFA 8/10, UFL 7/10, VMMA 5/10): contract prices scale with prestige
  but the Continental gate can't cover them, and free agency kept filling divisions to
  tier target while $2M+ in the red. Fix: `Solvency.Distress` (0 healthy · 1 in the red ·
  2 past half the debt ceiling) now gates the AI everywhere rosters grow —
  `FreeAgencyService` drops a distressed org's fill target to the card minimum (4),
  re-ranks the market by BARGAIN appeal (perceived ability − pop·0.5 + youth: cheap
  developable prospects, not names) and refuses anyone asking over
  `PurseCap × DistressedPurseFactor` (35% / 15%); `ContractLifecycle.ProcessExpiries`
  lets expensive non-champions walk at expiry under the same budget (champions exempt —
  the belt is the gate income). Re-measured: ICF/IFA/SCM/GFC folds 0/10 (IFA's books
  went from −$3M in 10/10 seeds to positive in 7/10), UFL 7→3, CK 6→4, VMMA 5→4; three
  seeds now run 5 years with zero folds, avg orgs alive 19.0→21.8, world size/popularity
  unchanged, AFC #1 in 10/10. Remaining known weakness: VMMA/CK/UFL still fold Y1-2 in
  ~3-4/10 seeds — a DAY-ONE book problem (VMMA opens with a $4.3M committed purse book
  on a $2.5M bank; CK opens the RCS turf war undercapitalized), i.e. designed-world
  sizing, not AI behavior. Locked by `SolvencyGateTests` (4 tests); diagnosis + sweep
  harness in the session scratchpad (`longsim`).
- **Authored lore — a curated day-one world (save v30, 2026-07-08).** Every designed
  entity now carries hand-written prose, persisted and shown in the UI: all **121
  designed fighters** (was 105) have a unique `Bio` (backstory the `Biographer` leads
  with before appending the LIVE career/form line, so authored text never goes stale),
  all **19 designed orgs** have `Lore` (house identity — founding story, grudges — shown
  on the Org page ahead of the derived facts), and the **16 designed camps** have `Lore`
  (shown on the Camps tab under the camp name). Procedural entities stay prose-less and
  keep the derived Biographer text. The 16 new designed fighters fill structural gaps:
  missing designed belts (GFC MW hired gun Dudaev + UFL LHW Slater + RCS LHW Siqueira +
  SCM MW Rennie; women: VMMA BW Novakova, ICF FLW Fujimoto, CK BW Mendes), new day-one
  feuds (Marchand–Weiss at VMMA, Kang–Sato at ICF, Tuilagi–Rahiri at SCM, Reyes–Zhang in
  the AFC women's strawweight), a loyal homegrown GFC prospect (Al-Suwaidi, the counter-
  story to the hired guns), SCL's second believer (Mensah), and two FA hooks (Gadzhiev
  the unsigned Caucasus phenom, Corrigan the comeback dad). Also fixed along the way:
  designed fighters only restrict to triptych portrait identities when one of the right
  ETHNICITY + WEIGHT exists (an Irish champion no longer ages in a Thai body). Locked by
  `DesignedWorldTests.AuthoredLore_LandsOnFightersOrgsAndCamps` + updated Biographer
  tests; verified via `--rendertab` (profile 9, camps 15).
- **A living pyramid — designed start, then endless churn (2026-07-06).** The world now
  keeps the shape of a real sport: many tiny promotions at the bottom, a few giants at
  the top. The hand-designed 11 orgs still create the lively opening (loyal lifers,
  homegrown hero, Europe/Brazil turf wars — see [[designed-initial-db]]), each cleared to
  its designed tier; from there they can fail and **new startups generate continuously**.
  The world now STARTS bigger and pyramid-shaped — **19 designed promotions** across all
  tiers (City 5 · Regional 5 · National 5 · Continental 3 · International 1, ~1,050
  fighters) instead of 11 top-heavy ones. Fixed the old top-heaviness at the source: the
  new-org pool used to be 11 fixed names each usable ONCE (the base couldn't be
  replenished). Now `WorldEvents` mints **unlimited procedural startups** (name parts + a
  unique 3-letter code) into interest-weighted regions — but only **gradually** (~once a
  year, cap 34), since the world already starts full, so it evolves at a pace a player can
  follow rather than churning chaotically. The steeply-tiered promotion cushion (Reg $1.4M
  → Nat $2.8M → Cont $6M → **Intl $12M**) keeps the summit exclusive. Also added a cheap
  **"Fight Hall"** venue tier (capacity ~500-900, low rent) so tiny promotions aren't
  buried by arena overhead — the small hall is what keeps a shoestring's card affordable.
  (Note: a from-scratch FOUNDED org that's AI-run can starve for talent in this deeper,
  more competitive market — a sim artifact; a real player hires manually. And the steady-
  state middle tiers are dynamic — orgs rise and fall through Regional/National — so the
  distribution shifts over a long run rather than holding a fixed shape.)
- **Opt-in promotion — growth is a choice, not a shove (save v25, 2026-07-06).**
  A promotion no longer gets *forced* up a tier the moment its prestige qualifies (the
  trap that folded over-eager climbers). `Promotion.TierClearance` is the tier it's
  chosen to operate at; prestige is CAPPED at that tier's ceiling (`OrgSizing.PrestigeCeiling`)
  so its whole economy — gate, purses, overhead — stays cheap while it builds a roster,
  stars and a war chest. Once it maxes a tier it's ELIGIBLE (`PromotionLadder`): the
  player gets a board mail and an "Eligible to promote / Take the leap" banner on the Org
  screen; the AI steps up only when its bank can carry the bigger stage (a cushion scaled
  to the target), else it holds and keeps building. Fixed the root cause of the fold
  trap along the way: the free-agency division target is now **tier-scaled**
  (`OrgSizing.RosterTargetPerDivision`, 4→8 by tier) so a small org runs a lean, affordable
  roster instead of over-hiring for a stage it can't fund. Verified: a seed that used to
  fold at National (over-promoted, roster ballooned to 40, bled out) now either holds at a
  sustainable Regional or grinds all the way to International tier by tier as it banks each
  cushion. Locked by `PromotionLadderTests`.
- **New money sinks — things to spend on (save v23, 2026-07-06).** The player was
  piling up idle cash, so three investment levers were added. (1) **Scouting** — a new
  `StaffRole` that slots into the existing hire/fire staff UI; `StaffService.ScoutingBonus`
  multiplies the Fog-of-War reveal (`ScoutingService.Assess` gained a `scoutingBonus`
  param) so a well-scouted org sees exact stats on prospects/rivals far sooner. (2)
  **Retention bonuses** (`RetentionService`) — a profile button that pays a lump sum to
  buy a fighter's LOYALTY (diminishing returns near the cap), the direct counter to the
  giants poaching your homegrown stars. (3) **Fighter marketing** (`MarketingService` +
  `Fighter.MarketingPush`, persisted) — a profile toggle that runs a paid promo campaign,
  growing a chosen fighter's Popularity month over month (amplified by the Marketing dept)
  and billed monthly; auto-cancels if unaffordable. New P&L categories `RetentionBonus` +
  `FighterMarketing`. Locked by `SpendingTests` + a `ScoutingTests` case; WPF verified.
- **Org marketing investment + the AI uses every sink (save v24, 2026-07-06).** A
  scalable place to sink real money: `Promotion.MarketingBudget` (`OrgMarketing`) — a
  monthly brand campaign, set on the Org screen, that lifts attendance (fuller house →
  higher show quality → faster climb) and adds a standing bonus to the climb engine.
  Deliberately SATURATING (money moves the needle less the bigger you are) and bounded
  (the house is only a fraction of show quality, so you can't market a bad card to the
  top) — but enough to genuinely push to the next tier. **Recalibrated** so the impact
  is real at achievable spend: saturation lowered (prestige²×550 → ×220), effect
  ceilings raised (attendance +55%→+75%, standing +12→+16), and the AI pours HARD when
  within 8 prestige of the next tier (0.45 of surplus vs 0.22 baseline). Crucially,
  `AiFrontOffice.RunMonthly` now has **rival orgs spend the sinks too**: they pour SURPLUS
  cash (above a survival buffer) into brand marketing, pay retention bonuses to hold
  flight-risk stars, and bankroll promo pushes on prospects — all gated so nobody spends
  into the red. Net over a 30-yr sim: the AI's idle-cash mountain is gone (an org that
  hoarded $122M now runs at ~$7M, spending the rest), and marketing is a real lever — NWF,
  which never once cracked International in the no-spending baseline, broke through in Yr
  25 on ~$850k/mo, then slid back when it stopped paying (marketing accelerates a climb,
  it doesn't permanently buy a tier). To keep the SUMMIT exclusive, the standing bonus
  was trimmed (+16 → +11, so money can't buy a whole tier by itself) and the climb bar
  now bends UP convexly above prestige 60 (`Standing.TopTierCurve`) — reaching/holding a
  global (International) standing demands genuinely elite product, so marketing can carry
  you to the top of Continental but not park you at the very top of the sport. Verified:
  the designed AFC holds International across seeds, most mature worlds settle at 1-3
  International orgs (down from 4+ with a hollow middle), and a dedicated founded org can
  still grind its way to the top. Also **fame is now capped by the stage** (`OrgSizing.FameCeiling`):
  winning/marketing only builds popularity up to the promotion's reach (City ~42 …
  Continental ~88 … only International mints a 100-pop global star) — never stripping fame
  earned at a bigger org. Locked by `SpendingTests`.
- **Climb re-paced into a career-long grind (2026-07-06).** The zero-to-hero climb
  was too fast (a founded org hit National in ~3 years). Three changes in `Standing`
  + `WorldBuilder`: (1) a founded promotion now starts at **prestige 5** — a rock-bottom
  City-tier nobody (below ~5 the projected gate can't cover pro purses, so the org
  can't field a roster and dies; 5 is the viable floor); (2) a **young-org ramp** — a
  brand-new promotion earns only ~28% of a show's prestige value at launch, rising to
  full credit over its first ~4-5 years, so building a name is a slog; (3) a
  **prestige-scaled `DeltaStep`** (`15 + 0.62×prestige`) so each show moves prestige
  less the higher you are — making each tier take longer than the last WITHOUT shifting
  where established orgs settle (equilibrium is where quality = bar, independent of the
  step). Net pace, verified over 12-yr AI-run sims: Regional ~Y3-5, National ~Y6-7,
  Continental ~Y10-12, International rarely reached in a decade. Prestige still accrues
  in hundredths (fractional) under a whole-number `Prestige.Value`. Locked by
  `ClimbEngineTests.AShoestringPromotion_TakesYearsToClimb_NotMonths`.
- **Startups now invest at a loss to build a name (2026-07-06).** A brand-new promotion
  used to be profitable almost immediately. The box-office house floor (`Financials`)
  was cut (`400 + prestige×60` → `150 + prestige×34`) so attendance is genuinely
  FANBASE-driven: a promotion with no `cityPop`/`regionPop` following draws a thin house
  its early shows can't cover, so it bleeds cash until its reputation builds and the
  gate grows. Established orgs draw off their big fanbase term, so they're barely
  affected. Verified: a founded org typically loses money its first 1-2 years (one seed
  dips from $250k to $30k before turning the corner), then grows — nobody folds on the
  $250k runway. Locked by `ClimbEngineTests.AShoestring_InvestsAtALoss_WhileItBuildsAFollowing`.
- **Fog of War for fighter stats (2026-07-06).** A player-facing knowledge layer —
  the true ratings never change and the sim always uses them; this just gates how
  much of each stat the player has *earned* the right to see. Per individual stat
  (`Core.Sim/Scouting/ScoutingService.cs`, no saved state — computed on the fly),
  a stat shows as **Exact** (the number), **Approximate** (a band word — Weak /
  Average / Strong / Elite, drawn amber), or **Hidden** (a dim "?"). Each stat is
  fed by one **evidence channel**, and channels fill from three sources: (a) a
  fighter's **pro-record length** is public, giving general familiarity — but capped
  so record alone tops out at strong *bands*, never exact; (b) the **tape you've
  actually watched**, where HOW they fought reveals the matching stats (a KO shows
  power, a sub shows grappling, going the distance shows the gas tank, getting
  finished exposes the chin); and (c) their **publicly-known style** (a known wrestler
  isn't a mystery on the mat). Your **own roster clears ~1.7× faster** (you don't
  train with them — they're at camps — but you know your guys). Net effect, verified
  over a 2-yr sample: raw newgens are near-total mysteries, rival veterans you've
  never studied read as all-bands (scout them for exact numbers), fighters you watch
  or own go exact. Surfaced on the fighter profile with a "🔍 scouted %" header;
  intangibles/tendencies stay public. Locked by `ScoutingTests`.
- **AI Fog of War — rivals misjudge talent too (2026-07-06).** The AI no longer sees
  through the fog either: `ScoutingService.PerceivedAbility(org, fighter)` returns the
  org's *read* of a fighter — the true ability nudged by an estimation error that
  shrinks as the fighter builds a record (public), is on the org's own books, and as
  the org is bigger (prestige = a real scouting department; the regional scenes guess).
  The error is a deterministic hash of (org, fighter) — stable, reproducible, no save
  state. The AI's *acquisition* decisions now run on perceived ability
  (`PoachingService.WouldPursue`, `FreeAgencyService.SigningAppeal`), so orgs disagree
  on the same prospect and can chase a bust or miss a gem — a market a sharp player can
  exploit. *Retention* stays on the public record (a proven loser is cut regardless of
  fog). Verified across a live world: big orgs misjudge prospects ~2× less than small
  ones, veterans are read exactly; the talent tiers still form cleanly (`AiRosterTests`
  green). Locked by 4 more `ScoutingTests` (13 total).
- **World Map — drill-down region/country/city overview (2026-07-05, reworked 2026-07-06).**
  The "World Map" tab (`WorldMapViewModel`) is a **hierarchical heat-card navigator**:
  it opens on the seven continental regions (colour-keyed by MMA interest), you click a
  region to see its **countries**, and a country to see its individual **fight cities**
  — a breadcrumb (World › Region › Country) walks back up. Each card lists the promotions
  based there and colour-codes the local interest; region/country cards show a "hottest
  cities" line and a "›" drill chevron, city cards show market + local promotions + how
  many fighters call it home. The player's region/HQ city is gold-highlighted. (An
  earlier build tried a literal equirectangular globe with real Natural Earth coastlines
  and plotted cities, but it wasn't as useful as the grouped drill-down for reading where
  the sport is hot and who's based where, so it was reverted.) Verified via `--rendertab 14`.
- **Initial-DB rebuild — designed storylines that exploit the new systems (2026-07-05).**
  The hand-designed roster (`DesignedRoster`) was rebuilt to seed narratives the
  loyalty / MMA-interest / head-to-head / win-streak systems can play out.
  `DesignedFighter` gained `Loyalty` and `Streak`; `CreateDesigned` wires them
  (loyalty defaults age-scaled, streak from undefeated/champion form). Signature
  arcs: **career lifers** who anchor a mid org and won't be pried loose (Daichi
  Sato ICF loy 88, Goran Vukovic VMMA loy 85); a **homegrown hero** — Chidi Okafor,
  a Nigerian champ at city-level SCL — whose stardom lifts his home region's MMA
  interest (the Pacquiao effect); **mercenaries** who churn (GFC's belt-holders at
  loy 24-30, Tyshawn Cole loy 16). New designed orgs turn the map into **turf wars**:
  IFA raised to a Continental European rival of VMMA, plus national challengers UFL
  (USA) and RCS (Brazil, vs CK), each with a designed champion roster; `MaxActiveOrgs`
  16→ raised to make room. Verified over a 3-yr diagnostic on seed 9: all three lifers
  stayed through their prime while becoming top draws, mercenaries churned (Cole left
  AFC, Al-Marri's loyalty collapsed 28→3), Africa interest climbed 46→62 off Chidi,
  and the Europe war resolved (IFA folded, VMMA survived). **Fixed a latent bug the
  rebuild surfaced:** neglect-decay measured idleness from a fighter's *last bout*, so
  an aging star in age-scaled post-fight recovery bled loyalty as if benched. Now it
  measures from when they were last *available and unused* (`ReadyToFightOn`), sparing
  the recovering while still souring the genuinely shelved. Locked by
  `ProgressionTests.NeglectDecay_SoursTheBenched_ButSparesTheRecovering`.
- **Fighter loyalty (save v21, 2026-07-05).** A per-fighter `Fighter.Loyalty`
  (0-100) toward their CURRENT org — the price a rival pays to pry them loose.
  **Starts ~20** on a fresh signing (resets on any move; +5 on re-upping in place),
  **grows slowly** (+1 per fight booked, +2 more for a title shot) so real
  attachment is the reward of YEARS of service, and **decays** (-2/month) once a
  fighter is genuinely benched (available but unused for 120 days — recovery/injury
  layoffs don't count, see the DB-rebuild note above). In poaching, loyalty is a
  price-to-leave multiplier: `Negotiator.PoachOffer` has the rival OVERPAY up to
  its gate, but a fighter's asking price scales from ~1× (fresh) up to **5-10× at
  max loyalty** (`LoyaltyLeavePrice`, quadratic in loyalty so it bites late) — and
  a bigger STEP UP eases the ceiling (10× for a one-tier move, ~5× for a giant
  multi-tier leap that sells itself). So a career lifer basically won't jump for
  money — only a much bigger org with the budget to massively overpay can tempt
  them (never impossible, just very expensive), while fresh signings and neglected
  fighters move freely. Verified over 8 years: avg loyalty stays modest, but the
  most-featured fighters climb toward the 90s (a decade to max) and AI-to-AI
  poaching keeps flowing (~22 moves/2yr). Persisted; locked by `ProgressionTests`
  (a lifer resists a poach a mercenary takes) and the round-trip test. (UI: the
  loyalty stat isn't surfaced on the profile yet — a WPF follow-up.)
- **AI-to-AI poaching — the treadmill runs everywhere (2026-07-05).** Poaching was
  player-only; now `PoachingService.TryPoach` is generalized to any org, and
  `ContractLifecycle.ProcessExpiries` runs it for AI fighters too: when a deal
  expires, a promotion on a genuinely bigger stage that would roster the fighter
  (fit or draw) signs them away before the current org can re-up — even a champion,
  who vacates the belt on the way up. The player is excluded as an auto-suitor (you
  sign your own). Verified over 2 years: ~28 AI-to-AI moves, and the tiers separate
  cleanly — Continental+ orgs carry 4-13% losing-record deadweight while the
  national feeder tier runs 42-58% (winners tier UP, journeymen slide DOWN). Note a
  real constraint surfaced: at world *build* the big orgs have no fanbase yet so
  their `PurseCap` is low and they can't afford a max-priced star — poaching of the
  most expensive names only kicks in once orgs build a gate. Locked by
  `ProgressionTests` (AI-to-AI poaches occur over time) and the retuned
  `AiRosterTests` (top orgs stay ≤⅓ deadweight; draws/champions don't count).
- **Relative brand growth, idle decay & draw-hungry market (2026-07-05).** Org
  popularity used to grow a flat +1-4 per show regardless of standing. Now
  `WorldMap.GrowCityPopularity` grows the brand RELATIVE to where it sits: the gain
  is driven by the card's **star power** (avg popularity of its top-three names)
  minus the org's current standing in that city — so a small brand climbs fast off
  almost any card, but an established one only grows by featuring fighters *more
  popular than the ones that got it there* (a card beneath its own draw wins no new
  fans), scaled by how full the house is. Paired with two market changes so the
  whole system self-brakes: (a) an **idle promotion fades** — `WorldTick` decays an
  org's city fame by 1/month once it's gone `IdleReputationDays` (50) without a card
  (`Reputation.DecayOrg`); (b) **the market chases draws** — `FreeAgencyService`
  now bids on a *signing appeal* (ability + ½·popularity), not raw ability, and
  poaching/`RosterPolicy` already weight popularity (`OrgSizing.DrawFloor`), so the
  prestigious orgs (bidding first) hoover up the popular names. Net effect, verified
  over a 10-yr run: the biggest draws tier UP into the top promotions, and growth
  gets genuinely harder — to keep climbing you need ever-more-popular fighters, but
  those are expensive and get signed away above you. Locked by `CityTests`
  (relative growth flattens / a card beneath standing adds nothing; an idle org
  fades). NOTE: AI-to-AI poaching of *contracted* stars still doesn't exist (only
  the player is poached); AI stars move up only when a deal expires and they reject
  a small org's capped offer — a candidate follow-up for even more churn.
- **Fit-based poaching + non-exclusive contracts (2026-07-05).** Three linked
  contract changes. (1) **Poaching goes after fit — ability OR draw, tier-scaled.**
  The old `Attractive` gate (`ability ≥ 60 OR popularity ≥ 50`) let a giant sign
  any popular-but-outmatched small-org champ. Replaced with
  `PoachingService.WouldPursue`, the same call an org makes off the free-agent
  market: a bigger stage chases a fighter who's either **competitive at their
  level** (`OrgSizing.TalentFloor`: Intl 68 · Cont 60 · Nat 52 · Reg 44, with a
  small draw allowance) **OR a big enough draw to sell tickets on their name**
  (`OrgSizing.DrawFloor`: Intl 75 · Cont 64 · Nat 54 · Reg 44) — never a fighter in
  free-fall. Both bars scale with the stage, so a 50-pop name is a regional draw
  but a national nobody. `RosterPolicy.Wants` gained the same draw clause, so
  free-agency signing and re-signing value names too. This is the treadmill's real
  tension: build a fighter into a star and the giants come for their *name* — the
  owner chooses to pay up or let them walk. Verified over a 5-yr run: a homegrown
  fighter (ability 42) built up to pop 76 gets chased by the AFC purely as a draw,
  while a modest 50-pop local name stays. (2) **Non-exclusive
  contracts are cheaper.** `Negotiator` discounts a non-exclusive offer's required
  money by 20% (`NonExclusiveMoneyFactor`) — the fighter keeps outside earnings
  (they moonlight on the local circuit), so they take less from this promotion.
  (3) **Small halls sign non-exclusive.** City/Regional orgs now offer
  non-exclusive deals (in both `AiOpeningOffer` and initial-roster
  `PromotionSetup.SignContract`); National+ demand exclusivity — so the cheaper
  non-exclusive deal is the small-org lever for affording talent, and those
  fighters keep their careers alive moonlighting between shows. Locked by
  `ProgressionTests` (fame-alone isn't poached; genuine stars still are),
  `NegotiationTests` (non-exclusive comes cheaper) and the updated `LocalCircuit`
  test (non-exclusive contracted fighters moonlight too).
- **Post-fight recovery, age & Recovery-stat driven (save v20, 2026-07-05).**
  Fighters used to be re-bookable the instant a fight ended (only injuries shelved
  anyone). Now **every** bout schedules a recovery window on `Fighter.ReadyToFightOn`
  (persisted, v20): a routine camp-and-recover turnaround (~3 base weeks, +1 for a
  long fight, +1 for taking the loss, +2 for a knockout) for the healthy, or the
  injury's return date for the hurt. Both layoffs now scale by **age** (~+5%/yr
  past 30) and the **Recovery stat** (0.7× elite engine … 1.3× poor) via a shared
  `InjuryModel.RecoveryScale` — so a young, durable fighter turns around in ~3-4
  weeks while an old, gas-less one who got knocked out sits for a couple of months
  (and injuries stretch proportionally). Matchmaking honours the clock:
  `Fighter.IsMatchReady(date)` gates the AI `Matchmaker`, `PlayerBooking`,
  `CardFiller`, `TournamentService` and the off-screen `LocalCircuit` — judged
  against the **event date** (so the player can book a fighter who'll be recovered
  by fight night). Locked by `RecoveryTests` (recovery scheduled + not bookable
  same day; veteran/poor-recovery rests longer; matchmaker skips the recovering)
  and the persistence round-trip.
- **World Map screen (2026-07-05).** A new "World Map" tab (`WorldMapViewModel` +
  `MainWindow.xaml`, tab index 14) — a visual read on the global scene. The seven
  continental regions render as heat-mapped cards in a four-wide grid, colour-keyed
  by MMA interest (cold blue → fight-mad red), each showing the promotions based
  there (player's org in gold, its region gold-bordered), the region's interest
  number, and its hottest fight cities. Bottom "hottest cities" rows align across
  cards. Verified via the offscreen `--rendertab 14` renderer.
- **MMA Interest per city / country / region (save v22, 2026-07-05).** Every
  `City` and `Country` now carries an `MmaInterest` (0-100) — how fight-mad the
  place is, distinct from raw market SIZE (a small city can be a hotbed, a big one
  indifferent). Seeded at world creation from the nation's talent `Weight` plus a
  stable string-hash jitter (no RNG-stream disruption, so existing seeds are
  unchanged), and mostly static — a celebrity crossover nudges its region up
  (`MmaInterest.BumpRegion`). Region interest is DERIVED (`MmaInterest.InRegion`,
  market-weighted average of its cities) so the three levels never drift.
  **Effects** run through `MmaInterest.Factor`, a deliberately BONUS-only multiplier
  (≥ 1.0, pivot 45): a hot market fills more seats (attendance) and grows a
  promotion's following faster (`GrowCityPopularity`), while an ordinary market just
  performs at the tuned baseline — never penalised below it, so the finely-balanced
  economy holds (verified: no new spurious folds). The upshot is exactly the goal —
  **high-interest regions throw off more gate, so they support more promotions
  head-to-head** (e.g. FFC now survives in fight-mad North America, where it used to
  fold every seed), while cooler markets still cull the weaker of two rivals.
  Persisted (v22); locked by `CityTests` (seeded spread, a hot market out-draws a
  cold one, a crossover lifts regional interest) and the persistence round-trip.
  **Interest is now DYNAMIC** (four movers, all decaying back to a stored baseline
  so the map never drifts to all-max): (1) new promotions **found where interest is
  high** — `MaybeNewPromotion` picks its region weighted by `(interest-25)²`, so
  fight-mad markets attract startups (verified 10-yr: NA/SA get ~10/9 new orgs, the
  Middle East ~1); the org pool was deepened so hot regions can take more than one.
  (2) A **national hero** (a popular fighter on a national+ stage) props up interest
  in their HOME region toward a capped ceiling above its baseline — a cold market
  rides high while it has a homegrown star (Pacquiao/Ngannou) and slides back once
  they fade. (3) A **big promotion's show in a cool market** grows the sport there
  (bigger stage = bigger lift), but only below a warm-market threshold so hot cities
  aren't inflated. (4) A **crossover** lifts its whole region. `MmaInterest.Tick`
  (monthly) applies the decay + hero support; the rest fire on their events. Verified
  10-yr: interest settles into a stable spread (~56 cool → ~74 hot) rather than
  climbing to max. Locked by `CityTests` (decay-to-baseline, a hero lifts their home
  region, a big show warms a market).
- **Head-to-head regional competition (2026-07-05).** Multiple promotions in the
  same region now split the market: `Financials.RegionalCompetition` shrinks a
  card's house based on same-or-bigger-tier rivals in the org's `BaseRegion`. It's
  RELATIVE-STRENGTH-based — pressure from a rival scales with `rival.prestige /
  my.prestige` (a stronger neighbour carves off more of the gate, an equal splits
  it, a weaker one barely dents you) and falls off with tier distance (a same-tier
  peer competes fully; a promotion far above you plays to a different crowd). So the
  bigger brand wins a head-to-head and the weaker one bleeds out — verified over
  8 seeds/10yr: ~30 orgs born, ~8 fold, failures cluster in contested regions
  (emergent orgs that invade an occupied market usually die; two same-tier orgs in
  one region → one folds, even a top org). Solo-region orgs are untouched. Locked
  by `CityTests` (a contested market draws a smaller house).
- **Popularity rebalanced — win streaks build stars, symmetric to losing skids
  (2026-07-05).** The loss-decay added earlier had NO winning counterpart, so world
  popularity deflated ~30% over a decade (losers lost more than winners gained) —
  which quietly starved box offices and folded established orgs (ICF re-broke). Fixed
  by giving wins a matching STREAK boost (`ResultsProcessor`: a fighter on a tear
  gains popularity faster, `×(1 + 0.4·min(streak-1,6))`), mirroring the losing-skid
  slide, and softening the single-loss penalty so one loss ≈ one win. Result: world
  popularity holds steady (~36 avg over a decade), hot fighters become genuine draws,
  faders slide off — and no more spurious big-org bankruptcies (0 across 8 seeds/10yr
  before competition). Also: `GrowCityPopularity` now floors an org's fanbase target
  at its PRESTIGE (brand pull), with fighter draw as upside, so an established org's
  gate can't be starved below its stature.
- **Popularity now bleeds on losses (2026-07-05).** Popularity used to be a
  ratchet — it only ever went up, so a former star coasted on old fame forever
  (a 11-19 fighter still headlining). Now a defeat costs a fighter some shine in
  `ResultsProcessor`: a single loss barely dents a rising name, but it COMPOUNDS
  on a skid (streak multiplier, capped) and cuts deeper the higher they stood
  (fame-scaled), floored at 3. A once-popular fighter who loses for years slides
  off the marquee — e.g. a 78-pop draw drops below ~40 over a ~dozen straight
  losses, while one-off losses stay negligible. Wins still build names unchanged.
  The **inked** persona (which had made a fighter's popularity loss-proof) was
  removed at the same time as unbalanced. Locked by `PersonaTests`
  (`ALongLosingSkid_ErodesAStar_OffTheMarquee`).
- **Crossovers get a boost, not max popularity (2026-07-05).** Celebrity
  crossovers were arriving at 81-96 popularity — instantly above every homegrown
  champion (the sport's biggest designed stars top out ~79-85). Recalibrated to a
  believable *boost* band (~50-70, preserving each celebrity's relative fame — the
  influencer draws highest, niche Olympians least) with a small seeded ±5 jitter
  at debut so it's not a fixed number. They're an instant attraction but have to
  win to climb past the established elite. `WorldEventsTests` now locks the boost
  band (`InRange 45..78`) instead of the old `>= 80`.
- **Smarter AI matchmaker — real title pictures (2026-07-05).** `Matchmaker`
  went from "book the #1 ranked contender, rotate belts by event count" to booking
  a division like a real matchmaker: (1) **vacant belts are contested** — a belt
  with no champion but two available contenders is crowned in the cage (top two
  fight for it) instead of sitting idle or being handed over on paper; (2) the
  challenger is a **worthy, non-rematch #1 contender** — `PickChallenger` skips
  whoever the champ just beat (no instant rematch) and prefers a contender in form
  (winning/even record or a live streak), falling back to the top contender only
  to avoid freezing a belt; (3) **the undercard builds the division** — pairing
  two ranked (top-6) contenders is tiered as an *eliminator* and floated to the
  front of the undercard so a clear next challenger emerges, while idle rank-and-
  file still get booked to stay active; (4) rotation is now driven by **how long
  each belt has gone undefended** (longest wait headlines, vacant belts jump the
  queue) rather than a modular event counter. Fully deterministic. Verified over
  3 AI years: across four AI orgs' 48 belts, 56/60 champions carry winning records
  and belts accumulate real reigns (95 title defenses, many champions on 3-7 fight
  streaks) — versus the paper, losing-record champions the old path produced.
  Locked by `BookingTests` (vacant belt contested as main event, immediate rematch
  avoided, losing #1 contender passed over for a deserving one).
- **Unique fighter names (2026-07-05).** Two independently generated newgens
  could collide on the exact same first+last (seen live: VMMA's "Sloane Nash"
  dethroning "Sloane 'Smooth' Nash"). `FighterGenerator.EnsureUniqueName` now
  re-rolls a colliding name from the fighter's own culture pool (last name first,
  then the first name on a deep clash) — deterministic off world seed + fighter
  id, so a seed still yields a stable world. Called at every newgen insertion
  point (`Populate`, `PopulateWomen`) and on local-circuit discovery
  (`LocalScene.Discover`); it deliberately skips the hand-authored designed /
  crossover names (never renames those) and the intentional "Local Striker"-style
  filler (which is meant to repeat). Locked by `GeneratorTests` (deep 400-fighter
  world has zero duplicate names; a forced collider is renamed while the original
  is left untouched).
- **Realistic card minimums + local-vs-local filler (2026-07-05).** No more
  unrealistic two-fight shows: every card is padded to a minimum length
  (`CardFiller.MinCardBouts = 6`) at run time in `EventService.RunEvent`, so both
  AI and player cards run full. A promotion first fleshes out the undercard from
  its own available (unbooked, uninjured, active) roster — best same-division
  pairing per division; a small org (City/Regional) that's run out of contracted
  talent papers the rest with **local-vs-local** filler (`LocalScene.CreateLocalPair`
  → two unnamed amateur-tier locals). Deep orgs (National+) never touch locals —
  they fill from their own bench, and a card already at/over the minimum is left
  untouched. Filler slots in as **prelims** (`ev.Card.Insert(0, …)`), so the
  booked main event always stays last (`ev.Card[^1]`). Pure filler has no roster
  impact: a local-vs-local bout **discovers no prospect** (only a local UPSETTING
  a real pro is discovered) and both locals fade back to the scene — including on
  a draw (`ResultsProcessor` now retires locals in the no-winner branch too, so
  none linger as undead free agents). Locked by `CardFillerTests` (4 tests: city
  filler, no false discovery, deep-org roster-only, full-card left alone).
- **Solvency rebalance — big orgs rarely fail (2026-07-05).** Bankruptcy used
  to hit one org deterministically every seed (ICF always folded) because a
  flat −$300k debt ceiling killed any org whose prestige equilibrium sat near a
  tier boundary and lost its revenue tier. Three fixes: (1) `Solvency.DebtCeilingFor`
  now SCALES with reach tier — City −$300k, Regional −$400k, National −$1M,
  Continental −$3M, International −$8M — so a giant weathers millions in the red
  and only the small/newly-founded realistically fold (the player's own zero-to-
  hero promotion is exactly where the bankruptcy stakes live). (2) `Standing`
  gained **institutional inertia** — negative prestige deltas are dampened,
  smoothly scaled by prestige (a giant barely budges on a rough patch, a small
  outfit still declines nearly full-rate), so established brands don't freefall
  out of their tier. (3) ICF's designed champions were bumped (overall 70-73→75-78,
  pop +10) and its start raised to prestige 49/$1.5M so its equilibrium sits
  comfortably ABOVE the Continental floor instead of oscillating on it. Result:
  across 8 seeds × 10 years, zero big-org failures and ICF never folds (was
  every seed). Emergent org founding is now capped at 12 active orgs so the
  world doesn't balloon.
- **A living, deeper world: 9 orgs + emergent world events.** The initial DB
  went from 4 promotions to **9** (`WorldBuilder.DefaultPromotions`): AFC (Intl),
  VMMA/ICF (Cont), GFC (Gulf oil-money, MiddleEast), IFA (Eastern Europe),
  SCM (Oceania/ANZ), CK (South America), CC (US regional feeder), SCL (emerging
  Africa) — spread across 7 regions, descending prestige so talent tiers into
  the top. New orgs got **designed marquee champions + hooks**
  (`DesignedRoster`: Rashid "The Sandstorm" Al-Marri, Dmitri "The Hammer" Volkov,
  Tyshawn "Young Money" Cole the poachable phenom, Chidi "Lion of Lagos" Okafor…
  ~16 new profiles). Small feeders (<32 prestige) don't run women's divisions.
  *Emergent world events* (`Living/WorldEvents`, monthly in the tick,
  date-driven + idempotent so NO new persistence): **celebrity crossovers** —
  every 18 months from mid-2026 a famous outsider (boxing champ, Olympian,
  influencer, ex-footballer — an 8-deep authored pool, the first is the early
  "thing to follow") arrives at ~90 popularity but 0-0 and unproven, signs with
  the biggest AI org (never the player), gets a bio accolade, and makes world
  news + player mail. **New promotions** — every 30 months a fresh org launches
  (6-deep pool), stocked from the best free agents region-first, with belts; if
  it's the first in its region it's flagged "MMA arrives in {region}"
  (regional expansion) with a board letter. Verified: over 5 years the world
  grows to 11+ orgs (a founded org can climb 22→53 prestige into Continental)
  with several crossovers, and men/women still never cross. Locked by
  `WorldEventsTests` (5 tests). Design decisions (2026-07-05): deep world
  (+5 orgs), scripted-first-then-emergent beats, recurring emergent crossovers.
- **Women's divisions + portrait-first generation (save v19).** Two interlocked
  features. *Portrait-first generation* (`Generation/IdentityCatalog`): instead
  of minting a fighter and hoping the art layer can match them, the generator
  draws a real identity from the art pack (~1,470 identities distilled to
  `IdentitySpec{Sex, Culture, WeightBand, Style, Triptych}`) that fits the
  fighter's sex + weight, and builds them to it — culture drives name AND
  nationality, style comes from the identity, stars (overall ≥70) prefer
  three-stage identities so they visibly age. So every generated fighter has an
  exact portrait, and names are now genuinely diverse (Zuberi Njoroge, Szymon
  Wisniewski…). The catalog is opaque on `World.ArtCatalog` (Core.Model stays
  free of Core.Sim); the WPF layer builds it from the manifest
  (`PaintedLibrary.BuildCatalog`) and attaches it on new-game/load; headless
  runs stay procedural. *Women's divisions*: `Division` gained a `Sex`;
  `Division.Matches(f)` (weight AND sex) now gates matchmaking, rankings,
  booking, free agency, poaching, and cuts — men and women never share a cage
  (`PlayerBooking.AddBout` rejects it, `Matchmaker` pools per-division). The
  world runs Women's Strawweight/Flyweight/Bantamweight on every established
  org via a dedicated female-talent pass (`WorldBuilder.AddWomensDivisions` —
  bolted on so the men's world is untouched); founded promotions get Women's
  Straw+Fly too. 126 women, 12 female champions with culture-correct names
  (Enkhjin Tumur/Mongolia). The 292 female pack identities finally get used.
  Persistence v19 (division +sex, division_rankings +sex; v&lt;19 = Male).
  UI: `FighterRow.Division`/`DivisionPick` show "Women's X"; Book Event and
  Rankings division pickers are sex-aware. Locked by `WomensDivisionTests`
  (5 tests). Fighter sex was already persisted, so no fighter-schema change.
- **Smarter AI rosters: activity-aware matchmaking + a roster policy.**
  *Activity-aware booking* (`Matchmaker`): the undercard used to greedily take
  the highest-ability pairings, so a deep roster's top few headlined every
  month and everyone below sat idle. Now each division's candidate pairs are
  scored by how long BOTH fighters have been idle (`LastFightDates` from
  completed cards), most-idle first, ability as the tiebreaker — so the whole
  roster rotates through (measured: ~90% of a 65-man roster fought within any
  6-month window, vs a handful before). Pairings stay competitive — each
  fighter is only matched against a near neighbour in the ability order.
  *Roster policy* (`Org/RosterPolicy.Wants`): the big stages don't keep
  journeymen with more losses than wins. One policy — champions, top-3
  contenders, and young high-ceiling prospects are always kept; below that,
  Continental/International cut on a losing record OR a −3 skid, National cut
  on a losing record, City/Regional keep almost everyone. It drives three
  things: `ProcessExpiries` won't RE-SIGN an unwanted fighter (they walk to
  FA), `FreeAgencyService` won't SIGN one (unless a division is below 4 and
  needs bodies), and `Contracts/RosterManagement.RunMonthly` CUTS them —
  bounded at 3/org/month, never below a 4-man division, no reputation dent
  (routine business, via `Release(dentReputation:false)`). Net effect: losing
  fighters slide DOWN the ladder to promotions that'll still book them
  (CK's sub-.500 count dropped ~28→9 over two years; the giants stay
  winner-heavy). Cuts surface in the advance summary ("9 cut"). Player rosters
  are never touched. Locked by `AiRosterTests` (6 tests).
- **Progression trio: tier meter · milestones · talent treadmill.**
  *(1) Per-tier progress meter* — `OrgSizing.Progress` reads the FRACTIONAL
  prestige into a within-tier 0–100 climb (`TierStanding{Tier, Percent, Next}`),
  shown as a filling blue bar + "National · 46% to Continental" on the Home
  card and Org header. Makes the smooth climb legible.
  *(2) Milestones* (`Org/Milestones`, save v18) — one-time player "firsts":
  first show, first sellout, 10k house, first champion, first title defense,
  first TV deal, first PPV, five-star fight, first show abroad. Each fires ONCE
  (recorded on `Promotion.Milestones`, a persisted set) as celebratory news +
  a board letter; checked in `EventService.Settle` (+ the TV one in
  `RevenueService`). Player-only. Home shows an achievements strip (earned gold
  🏆, remaining greyed ○, "N of 9"). *(3) The talent treadmill*
  (`Contracts/PoachingService`) — the giants come for the player's homegrown
  stars at free agency. A fighter worth having (ability ≥60 or pop ≥50) on an
  expiring deal gets courted by any promotion on a bigger reach tier: the owner
  gets a **warning mail the fight before free agency** (`WarnExpiring`, once
  per contract via a transient `Contract.PoachWarned`), and if they don't
  re-sign in time the star **walks** (`TryPoachFromPlayer` at
  `ProcessExpiries`, which previously skipped player fighters entirely) —
  vacating any belt on the way out, with news + a board letter. Champion's
  clause is still the defense (auto-extends, so they never hit expiry). The
  suitor pays its capped best; the star takes a slight discount for the bigger
  stage (accept-or-counter), only staying if even the giant's max is
  insulting. The top org can't be out-tiered, so its stars are safe. Locked by
  `ProgressionTests` (9 tests).
- **Dynamic prestige — the climb engine (`Org/Standing`).** Prestige is now
  EARNED show by show instead of creeping +1 for a sold-out title card. Every
  settled card gets a **quality score** (0.55×avg fight rating + 0.25×house
  [fill + raw attendance up to 20k] + 0.20×main-event star power, plus small
  title/PPV bonuses) measured against a **bar that rises with current
  prestige** (`35 + prestige×0.45`); margin over/under the bar converts to
  **±0..2 prestige per show** (`(quality−bar)/18`, clamped). Result: a scrappy
  City org climbs ~1/show on good small cards and reaches the next tier in a
  season, then STALLS at each level until its product genuinely improves —
  bigger houses, bigger stars, better fights. Coasting slips you back (a big
  org running mediocre cards erodes toward equilibrium, never runs away to
  100). `Standing.Apply` is the single prestige gate: it clamps 1–100 and, on
  a **reach-tier crossing**, posts Milestone news for any org + a Board letter
  to the player ("We're a National promotion now… the networks will come
  calling"). The player gets a **show-report mail** after every card (avg
  stars, house, quality vs the bar at their level, the ±delta and why).
  **Inactivity decay**: a brand dark >120 days loses 1/mo until it books again
  (`MonthlyDrift`, wired into the monthly tick after Solvency). **Collapsed
  cards** (everyone pulls out) cost prestige + reputation (`OnCancelled`).
  Year-end Event of the Year now routes its bump through `Apply` too, so it can
  announce a tier jump. **Prestige accrues in HUNDREDTHS** (save v17):
  `Promotion.PrestigeHundredths` (0–10000) is the fine-grained accumulator the
  climb moves in fractional points; `Prestige` (0–100) is its floored view that
  every system reads, so a show a hair over the bar still nudges up instead of
  rounding to zero. `Standing.Apply` takes a `double` delta; setting `Prestige`
  directly snaps the accumulator (×100). v&lt;17 saves migrate (hundredths =
  prestige×100). Locked by `ClimbEngineTests` (7 tests).
- **Org rulesets — house rules (save v16).** Every promotion carries an
  `OrgRules` (`Promotion.Rules`): **round length** (3/4/5 min — flows into
  the engine via `FightParams.RoundSeconds`; `Bout.RoundSeconds` is no longer
  a const, so short-round orgs genuinely fight shorter rounds), **standard
  bout rounds** (2/3/5), **title fight rounds** (3/5), and a **Fight of the
  Night bonus** ($, paid at settle to BOTH fighters of each card's best-rated
  fight — real ledger money, +6 morale, +1 pop each, +1 org reputation, mail
  to the player; duds under 32 rating get nothing). `EventService.RunEvent`
  sims under the host promotion's rules; the AI `Matchmaker` books its cards
  at its own org's formats; the player's Book Event rounds combo defaults to
  the house standard and snaps to title length when the title box is ticked.
  The Org tab has an editable **House rules** card (player org only).
  Persistence v16 (promotions +round_minutes/standard_rounds/title_rounds/
  fotn_bonus; v<16 loads defaults). Locked by `OrgRulesTests` (4 tests).
- **Fight night flow + combined matchup builder.** Booked cards can no longer
  be viewed/run ahead of time: "Run & Watch" became **Watch Tonight** and is
  disabled until the event's date (the render harness's `Execute` bypass keeps
  headless renders working). Advancing **force-pauses on fight night**: the
  tick stops at a due player card, a crimson banner appears under the toolbar
  ("🔔 FIGHT NIGHT — {name}") with **Watch Live** / **Sim Result** buttons,
  and further advances are refused until one is chosen
  (`MainViewModel.DuePlayerEvent`; Sim lands on the Events tab with the box
  office in the status line). On Book Event the old separate Add-bout form and
  Tale of the Tape card are ONE panel: each corner's portrait (red/blue
  framed) sits above its picker and appears the moment that corner is chosen
  (`UpdateAppraisal` sets portraits per-corner), with rounds/title/Add Bout,
  the appraisal bars, and the stat tape below; "Suggest an opponent" is a
  full-width button under the blue combo. **Full-body shots** now carry the
  big moments (`Portraits.Body`, flat-vector fallback for pack gaps): the
  fighter profile — a near life-size TRANSPARENT cut-out (235×~650) standing
  down the whole left side of the page (the pack bodies are RGBA cut-outs;
  no frame, no background) — the Book Event corner face-off (112×150
  top-cropped walkouts), and the Watch-tab intros (270px). The Contracts
  negotiation panel uses a 110px FACE shot (a body there read wrong). Grids
  and small chips stay on face thumbnails.
- **Watch tab: broadcast layout + results sheet + readable stars.** The tab
  was Left-aligned with a star column, which collapsed the play-by-play to a
  sliver on wide monitors — now it stretches (capped 1780) with a 620px right
  rail: the cage panel (bigger walkouts) plus **THE CARD**, a running-order
  sheet (`WatchViewModel.CardRows`) where each bout's result + stars fill in
  (green) the moment the broadcast reaches them — upcoming bouts stay
  unspoiled ("· upcoming"), and when the broadcast ends a **FINAL** tag turns
  it into the full card-results screen (Skip reveals everything).
  `MatchRating.Stars` now shows EARNED STARS ONLY ("★★★") — the old ☆-padded
  five-glyph line made every fight look five-star at a glance.
- **Billing order + corner colors.** The sim still runs `Event.Card` front to
  back (`Card[^1]` = main event, played last) but every player-facing list now
  reads TOP-BILLED via `CardBilling.Slot/Position` (Rows.cs): the booking grid
  has a gold "Main event" / "Co-main" / "Fight N" Slot column with the main
  event on top, the Watch running order is billing-ordered (#1 = main event),
  Events-tab result lines are slot-prefixed and reversed, and commentary
  headers announce "MAIN EVENT — A vs B" instead of "Bout 1". Fighter names
  render in CORNER COLORS (light red #E88A80 / light blue #82AEE8) in the
  play-by-play (`Mvvm/CornerText`: lines are segmented around each name and
  rendered as tinted inline runs via an attached property), in the
  "Now in the Cage" panel, and on the Book Event corner cards.
- **Bookable local talent (save v15).** The small-org special: a City/Regional
  promotion can book a roster fighter against an unnamed hometown opponent —
  "Local Striker", "Local Wrestler" (5 archetypes, `Living/LocalScene`) — via
  the Local fight panel on Book Event (visible only at those tiers; sim-side
  `PlayerBooking.AddLocalBout` enforces it). Locals are minted from the HOST
  city's club scene (real hometown/country → the right name culture and
  portrait pool), amateur-tier (~24 overall) and hobbled where club fighters
  really are — no gas tank (cardio/recovery 8–22), no championship pace — so
  a real pro wins the vast majority in the actual fight engine (a weak pro can
  still get caught). Flat $1,000 appearance fee (no contract), half popularity
  credit for squashing one, muted city-rep echo. After the bout the local
  RETIRES back into the scene — unless they pull the upset, which triggers the
  **discovery storyline** (`LocalScene.Discover`): renamed with a real name
  from their own country, a raw prospect ceiling (58–76 PA), a permanent
  profile accolade ("Discovered on the local circuit — upset X at Y"), a news
  story, and a mail from Matchmaking that they want to sign with the org that
  gave them the shot (they're in the FA pool at a modest ask). Unfought locals
  evaporate if the bout/card is scrapped; they're excluded from free-agency,
  the local circuit, and FA screens (`World.FreeAgents`). `Fighter.LocalTalent`
  persists (save v15, `is_local`). Locked by `LocalFightTests` (4 tests).
- **The reach ladder: tier-gated booking + business unlocks.** A promotion's
  prestige tier (`Org/OrgSizing`: City <20 · Regional 20 · National 32 ·
  Continental 45 · International 70) is now a rung of the real geography and
  gates everything. *Where you can book* (`Org/OrgReach.CanBookIn/Bookable`):
  City = the HQ metro only; Regional = the HQ's in-country area (the West,
  Southeast Brazil); National = anywhere in the HQ country; Continental = the
  home continent; International = the world. The player's host-city picker
  only lists bookable cities (with a reach line: "Reach: London · England
  (GBR) at 20 prestige · PPV at 45"); out-of-reach bookings clamp to the HQ;
  AI orgs tour only where their reach allows (`PickTourStop`/`PickVenue`
  filter through it — CK stays in Brazil, VMMA tours Europe). *What you can
  sell* (`Org/OrgPerks`): **TV at National** — the network deal signs itself
  on the monthly tick when the tier is reached (news + player mail) and
  lapses at expiry if reach shrank; value = 60k + (prestige−45)×6k/month with
  a carriage multiplier (Continental ×1.25, International ×1.6). **PPV at
  Continental** — `Financials` only sells PPV for Continental+ brands (buys =
  (prestige−45)×2k + draw×3k); the Ppv/SuperCard card tiers are hidden in the
  booking combo and clamped in `PlayerBooking` below their tier (super-cards
  are International-only). Thresholds are tuned so the AI orgs sit on their
  designed identities: AFC 74 International, VMMA 58 / ICF 45 Continental,
  CK 33 National (and CK gets TV now). The Home tier line shows the reach
  geographically ("▲ Reach: United States · North America at 45 prestige —
  N to go"). Locked by `ReachTests` (7 tests). Also fixed: `manifest.json`
  (the pack metadata syncassets.py now writes) wasn't copied to the build
  output, silently dropping persona tags and per-identity cultures — the
  csproj now ships it.
- **Country layer (save v14).** Under the cities sits a full real-world
  **atlas** (`Geography/WorldAtlas`): **115+ countries**, each with a continent,
  a talent **Weight** (how much fight talent it produces; most countries have
  no fight scene yet — adding one later = adding one `CitySpec`), and a name
  **Culture**. The **Big Five** fight nations are subdivided into in-country
  areas (USA West/Midwest/Northeast/South; Brazil, Russia, UK, Japan likewise).
  Every fighter now has a REAL nationality (`Fighter.Country`; `Nationality`
  stays as the continental rollup): the generator rolls the country off a
  dedicated per-fighter stream (atlas-weighted within the continent), draws the
  name from that country's **culture-keyed pool** (~38 pools in `NameData` —
  a Georgian reads Georgian, a Thai reads Thai), and rolls the hometown
  **in-country when it has a fight scene**, else anywhere on the continent (the
  Irish contender who came up in London — profiles show "London · Ireland").
  All **51 designed fighters** re-authored with real countries (Tsereteli GEO,
  Batyr KAZ, Okonjo NGA, O'Rourke IRL, Fetu SAM…). `Reputation` gained
  `FighterInCountry`/`OrgInCountry` (market-weighted, derived like regions);
  the Negotiator's home appeal is now same-country 1.0 / same-continent 0.5.
  City picker + profiles + bios all show "City, Country". Persistence v14
  (countries table; cities carry country/area; fighters carry country_id);
  v<14 saves migrate on load against the atlas (cities matched by real name,
  fighter countries from hometown else the continent's flagship); the writer
  now always stamps `World.LatestSaveVersion` so migrated saves re-save
  correctly. Locked by `CountryTests` (6 tests).
- **City-level geography (save v13).** The world grew to **49 REAL cities**
  (7 per region; first = anchor metro, each region's real fight capital — Las
  Vegas, Sao Paulo, London, Tokyo, Sydney, Johannesburg, Abu Dhabi; venue names
  stay fictional). Reputation now lives at the CITY level —
  `Fighter.CityReputation` / `Promotion.PopularityByCity` are the stored truth;
  region numbers are always **derived** (market-weighted average) via
  `Geography/Reputation` (the only read/write path; results echo faintly around
  the host region, big-brand shows echo harder). Fame lands where you fight:
  event results, the local circuit (home city), special events (home region).
  Attendance reads host-CITY popularity first (×30) + region rollup (×10), so
  touring a new city is an investment. **Hometowns**: every fighter rolls a
  `HomeCity` (market-weighted, seeded local fame); a hometown headliner adds
  +12 draw and host-city fame adds draw/4. **Booking is a geography decision**:
  Book Event has a Host City picker (HQ-first list with your pop per city), the
  projection strip reprices per city, and the event title shows "venue, city";
  AI orgs tour their region (~every other show away from HQ) via
  `WorldMap.PickTourStop` + `PickVenueIn`. Promotions have an HQ (`HomeCity`,
  anchor metro; shown in the Home tagline); fighter profiles show the hometown.
  v12 saves migrate on load (region values painted across cities, hometowns
  defaulted to anchors). Locked by `CityTests` (7 tests).
- **UI polish pass (2026-07-04).** *Watch tab overhaul*: a cage-side panel
  tracks the broadcast — full-body walkout shots (first use of `Portraits.Body`
  in a screen), records/billing, then on the result reveal a winner line, the
  ★ rating, and a corner-vs-corner statline (strikes/KDs/TDs/subs/control,
  better number in green); broadcast speed selector (Slow/Normal/Fast).
  *Book Event*: a projected box-office strip (`Financials.Estimate` — payroll
  at purses + half win bonuses) shows tickets/gate/production/payroll → net in
  green/red BEFORE you commit to a card. *Home*: "▲ National reach at 46
  prestige — 12 to go" tier-progress line. *Rankings*: champion showcase card
  (portrait, reign length + defenses, double-click to profile; vacant belts
  invite a first champion). *News*: category filter + "my promotion only"
  toggle, player stories in gold. *Finance*: monthly P&L grid grew in/out bar
  chart + green/red net. *Fighters grid*: "Form" column (▲/▼ momentum,
  colored). *Events grid*: card-average ★ rating column. *Contracts*: committed-
  purses payroll line. *Inbox tab header* shows the unread count. *Org*:
  prestige/reputation bars. Global **dark scrollbars** (thin, rounded).
- **World-color batch: local circuit · closures/bankruptcy · match ratings ·
  yearly awards · special events · honor badges (save v12).**
  *Match ratings* (`Flavor/MatchRating`): every fight scores 0–100 from its
  recorded action, shown as ★☆ lines on Events results + fight history; drives
  Fight of the Night. *Yearly awards* (`Storylines.YearEndAwards`): Fight/KO/
  Submission/Event/Fighter/Rookie of the Year — news + permanent
  `Fighter.Accolades` (persisted) + pop boosts; Event of the Year lifts host
  prestige. *Local circuit* (`Living/LocalCircuit`): free agents and idle
  non-exclusive fighters take monthly fights vs unnamed local talent (real
  record, momentum, home rep; capable pros ~95% win rate, count in the advance
  report). *Special events* (`Living/SpecialCircuit`): Olympics every 4 yrs +
  annual World Wrestling/Jiu-Jitsu championships — style-matched pros medal by
  ability → news + profile accolade + pop/rep boost. *Closures*
  (`Org/Solvency`): any org past −$300k folds (roster to market, belts vacated,
  cards cancelled, `Promotion.Defunct`, news/mail) — **including the player**
  (warnings on the way down, then game over). *Honor badges*: 👑🏅💥🌟🥇 on the
  Fighters grid + profile (accolade tooltip). **Economy rebalance this forced**:
  `Negotiator.PurseCap(world,promo)` derives the max purse an org offers from
  its expected box office (initial contracts + AI offers both capped — fighters
  who outgrow the budget walk upward: the talent ladder); event production now
  scales with the actual house (10k + $12×attendance + rent); fight-night
  bonuses scale with prestige; AI booking cadence fixed at ~monthly per org.
  Validated: all four AI orgs solvent over a 2-year headless run (they
  previously all went bankrupt once closures existed to notice).
- **Generic portrait pack shipped + persona tags wired into the game.** The
  bundle is now realistic (108 triptych identities) **plus generic** (281
  single-stage fillers) with `Assets/Painted/tags.json`. `Fighter.PersonaTag`
  (persisted, save v11 — v10 saves still load) is stamped by
  `PaintedLibrary.PersonaFor` in `MainViewModel.RefreshAll` from the marketable
  look baked into the assigned portrait: **glam/adonis** +10 main-event draw,
  **menace** +10 on title/grudge headliners (+4 otherwise), **babyface** 2×
  popularity growth on wins. Profile shows a ★ persona chip. (The **inked**
  persona's effects — draw bonus + loss-proof popularity — were removed 2026-07-05
  as unbalanced; the portrait tag stays as pure cosmetic flavor, easy to re-enable.) **Designed headliners take triptych
  identities** (all 3 life stages, `Fighter.Designed` flag) so stars visibly
  age — 51/51 covered per `--selftest`. Locked by `PersonaTests` (4 tests).
- **Designed initial database; newgens stay procedural.** The world's headliners
  are now hand-authored (`Core.Sim/Generation/DesignedRoster`, ~50 fighters):
  every champion in all four orgs, the AFC's nine #1 contenders, and six
  free-agent story hooks (the hyped prodigy, the fallen ex-champ, the gatekeeper…).
  `FighterGenerator.CreateDesigned` materializes a design (identity, archetype,
  level, record) and procedurally fills everything beneath; `WorldBuilder` seats
  org-tagged designs on their roster (counting against quotas), crowns designed
  champions via `ConfigureDivisions`' new champions param, and the generator
  fills all depth. Newgens/`NewgenService` untouched. Off switch:
  `Options.UseDesignedRoster = false`. Locked by `DesignedRosterTests` (5 tests).
  Note: designed *records* were originally backstory-only; since v33 the
  `HistoryBuilder` turns every record into real back-dated events, so the résumé
  rankings (including P4P) judge the pre-history from day one.
- **Small-org economy fixed + hardest-difficulty season locked by test.**
  `SeasonSimTests` plays a full Shoestring year headlessly (hire via the scout,
  3-bout card every 8 weeks, restock as deals expire) and asserts 6 shows run
  with the balance green throughout — it exposed that the old economy bankrupted
  a $100k org by show 2 (and had AI Cage Kings −$1.1M/yr). Two tuning changes:
  event **production now scales with prestige** (`Financials`: 15k + 1.8k×prestige
  base, 2k + 140×prestige per fight — AFC-scale unchanged, small-hall shows cost
  small-hall money) and **fighter purses rescaled** (`FighterValuation`: floor
  $2.5k with steep growth above ability 50; popularity only pays above 30 —
  scrubs fight for a few grand, stars price small orgs out). Result: Shoestring
  year one ends ~$157k from $100k with smart play; all AI orgs solvent.
- **Founded promotions start empty — you hire everyone.** Founding no longer
  auto-signs a starter roster: the gym starts empty, every belt starts **vacant**
  (`PromotionSetup.ConfigureVacantDivisions`; the first title fight crowns a
  champion — `ResultsProcessor` posts "claims the vacant …" news + owner mail),
  and `FoundingSpec.TalentPool` generates extra unsigned fighters to hire from.
  New Game jumps a founded save straight to Contracts. **Scout's suggested
  hire**: `SigningAdvisor.Suggest` (Core.Sim/Contracts) weighs thin divisions,
  bankroll affordability (purse ceiling ≈ balance/12), ability + youth upside;
  the Contracts tab's "Suggest a signing" button selects the pick and explains
  why. Book Event's division dropdown now lists **only weight classes you have
  fighters in** (`BookingViewModel.RefreshDivisions`).
- **Home page.** The Dashboard tab is now a card-based home screen (`DashboardViewModel`
  + the "Home" tab in `MainWindow.xaml`): your org at a glance (logo, size tier,
  world rank, prestige/reputation bars, balance, roster), next event (with a
  "Book Event ▸" jump), owner objectives, inbox preview, latest headlines
  (player items in gold), the promotion ladder (player row highlighted), your
  champions, and deals about to run out ("Contracts ▸" jump). Card shortcuts
  navigate via `Dashboard.Navigate`.
- **Book Event layout fix.** The fight-card grid squished at real window sizes
  (Auto Matchup column + empty star filler = stale width negotiation). Matchup
  is now the star column (min 320) with fixed Division/Rounds/Stakes, and Add
  bout + Tale of the Tape share one fixed right rail, so the lineup gets every
  spare pixel at any width. `--renderbooking [width height] <path>` now takes an
  optional size for checking layouts.
- **New Game setup + founding flow.** New Game now opens a setup dialog
  (`Wpf/NewGameWindow`): *found a new promotion* (name/abbreviation/home region,
  three starting scales — Shoestring $100k/2 divisions, Regional startup
  $250k/3, Funded challenger $1M/5) or *take over* any of the four established
  orgs; world seed is choosable. Sim side: `WorldBuilder.Options.Founding`
  (`FoundingSpec`) stands the player org up at the bottom of the ladder with an
  empty roster and vacant belts (see the hire-everyone bullet above).
  App boots into a quick-start AFC world; headless hooks
  `--rendernewgame <path>` and `--renderfounded <tabIndex> <path>` render the
  dialog and a founded world. Locked by `FoundingTests` (6 tests).
- **Flat-illustration character generator** (`Wpf/Art/CharacterFactory`): procedural
  face shots (200×200) and full-body shots (200×300) in a flat avatar-pack style —
  `CharacterDna` rolls skin/hair/features/gear deterministically per fighter id
  (nationality → skin & style pools, sex-aware, age → grey/balding/wrinkles, weight
  class → face width & build, grappler → cauliflower ears). Shared `FlatHead`
  renderer keeps face/body consistent; palettes + helpers in `FlatArt`. **Now the
  app-wide portrait default** via `Portraits.For` (photo pool `FaceLibrary` and soft
  vector `PortraitFactory` kept as one-line alternatives). Preview: `--renderchars
  <path>` contact sheet (see `flat-characters-preview.png`). Body shots aren't used
  by any screen yet — candidate for the Watch tab / fight intros.
- **Dark fight-night theme** (gold + crimson) applied globally via `Wpf/App.xaml`
  implicit styles + brush resources; all inline colors swept; DatePicker/ComboBox
  themed; render harness backgrounds darkened.
- **Watch the fights.** `Advance Week`/`Advance Month` now **pause** when the
  player's booked card comes due (instead of silently auto-running it): the app
  jumps to Book Event, prompts "▶ … is tonight — Run & Watch", and the **Watch**
  tab reveals the play-by-play line by line. (`WorldTick.AdvanceWeeksStoppingForPlayer`
  + `PendingPlayerEvent`; `MainViewModel.RunLive`; `WatchViewModel`.)
- **Résumé-based cross-org / P4P rankings** (`RankingService.RecomputeGlobal`):
  fixed the bug where winless / losing-record high-OVR fighters ranked over
  proven winners. Global rankings now use the same quality-of-wins résumé score
  as per-promotion rankings + a champion bonus, and exclude winless non-champions.
  Locked by `GlobalRankings_NeverRankAWinlessNonChampion`.
- **Regional reputation/popularity, surfaced.** Per-region fame bars for fighters
  (Fighter tab → "Regional Fame", from `Fighter.RegionalReputation`) and orgs
  (Rankings tab → "Popularity by region", from `Promotion.PopularityByRegion`,
  seeded at world build: strong at home, weaker abroad by prestige).
- **Prose bios for every fighter and org** — deterministic, generated from actual
  attributes/state (`Core.Sim/Flavor/Biographer`). Fighter bio on the Fighter tab
  (`SelectedFighter.Story`), org bio on the Rankings tab (`SelectedOrgBio`).
- **Natural (walk-around) weight** per fighter (`Fighter.NaturalWeightLbs`, rolled
  from a realistic per-division band, persisted). Portraits now **weight-lock**:
  `FaceLibrary` filename ranges (`{eth}_{id}_{min}_{max}_{stage}`) match a face to a
  fighter's build, so a heavyweight-built face only goes to heavyweight-built fighters.

**UI screens (a left sidebar nav; indexes in `ViewModels.Tab` — the single source
of truth; each screen is a UserControl in `Wpf/Views/`):**
*Your promotion (org-accent):* Home 0, Roster 1, Book Event 2, Contracts 3,
Finance 4, Org 5, Inbox 6 · *WORLD section header 7* · *World research
(blue accent, `WorldTab` style):* Fighters 8, Fighter 9, Rankings & Champions 10,
World Rankings 11, Events 12, News 13, World Map 14, Camps 15. The broadcast is
NOT a tab: watching a card opens the modal fight-night `WatchWindow` (hosting
`Views/WatchView`) over the shell. Double-clicking a fighter row on any grid (the
`mvvm:FighterLink.OpenOnDoubleClick` attached behavior) — or a champion/expiring
card on Home — jumps to their profile (`MainViewModel.OpenFighter`).
Save version is **19** (`World.LatestSaveVersion`; the writer always stamps it).

**What we want to do next (zero-to-hero build order):**
1. **Milestones & goals** — surface the climb: first sellout, first TV deal, #1
   in home region, first international card, poaching from a bigger org. Detect
   in `EventService.Settle`/`Standing`, deliver through news + inbox. `Org.Objectives`
   (2 hardcoded yearly goals) is the seed of this. (The prestige climb it
   surfaces now exists — see the climb engine below.)
3. **The talent treadmill** — AI orgs should court the player's stars when deals
   run out (today `FreeAgencyService` only signs unattached FAs). Losing your
   champion to the AFC should hurt and be fightable (exclusivity, champion's
   clause, matching rights).
4. **Smarter AI booking** — activity-aware matchmaking (low-ranked fighters
   currently fight rarely) + ability-based signing/cutting. Long-standing
   refinement noted in Roadmap step 5.
5. **Watch-screen polish** — fighter portraits + a live scorecard during play-by-play.
6. **Smaller polish** — seed starting fighter fame from popularity/nationality;
   dark scrollbars; event-detail drill-down, filtering/sorting, charts.

(Player control in the UI — booking, signing/cutting/negotiating, staff — is
**done**: Book Event and Contracts are player-driven end-to-end.)

**How to build/test/render (Windows, per-user dotnet):**
```
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
# kill any stray WPF process holding DLL locks first:
Get-Process -Name MmaManager.Wpf -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build "e:\MMA Manager\MmaManager.sln" -c Debug
dotnet test  "e:\MMA Manager\MmaManager.sln" -c Debug        # 119 tests, keep green
# render a tab offscreen to a PNG (index from the tab list above):
dotnet run --project "e:\MMA Manager\Wpf" -c Debug -- --rendertab 2 out.png

# headless long-horizon sims & balance sweeps (see tools/longsim/README.md):
dotnet run --project "e:\MMA Manager\tools\longsim" -- 2026 30          # 30-yr world, yearly breakdowns
dotnet run --project "e:\MMA Manager\tools\longsim" -- compare 10 5     # 10-seed balance sweep (the regression catcher)
dotnet run --project "e:\MMA Manager\tools\longsim" -- diagnose 7 3     # quarterly P&L of the designed orgs
dotnet run --project "e:\MMA Manager\tools\longsim" -- shoestring 42 20 # simulated zero-to-hero playthrough
```

---

## Art pipeline (separate project)

All AI portrait / logo / placeholder generation and the approve-decline review
loop live in their own project: **E:/FighterArt** (own git repo, README = the
old FIGHTER-PACKS.md handbook). The game never reads it. The contract is what
FighterArt's `sync/` writes into `Wpf/Assets/Painted` (PNGs + manifest.json v1 +
tags.json), `Wpf/Assets/Placeholders`, and the build-output mirror. Game paths
are set in `E:/FighterArt/config.toml`. Moved 2026-09-12.

## Stack

| Concern | Choice | Why |
|--------|--------|-----|
| Language / runtime | **C# on .NET 8** | Strong typing for a large domain model; near-C++ perf for bulk world simulation without the C++ pain; LINQ suits roster/ranking queries; it's what the commercial sims in this genre use. |
| UI | **Avalonia** (cross-platform) or **WPF** (Windows-only) | Data-binding-first frameworks; the UI is just screens bound to the model. *Not finalised — see Open Decisions.* |
| Persistence | In-memory world **serialised to disk** to start; SQLite in reserve | Whole world lives in memory for speed; save = serialise the graph. |
| Dependencies | Keep the sim core BCL-only | The core must stay trivially testable and serialisable. |

---

## Architecture

### The four layers

The game is four stacked layers; each depends on the one below, which is also
the build order.

1. **The world (data model)** — entities and their relationships. *Done: see
   `Core.Model`.*
2. **The fight engine + world tick (the sim)** — two fighters in, play-by-play
   and a result out; plus the weekly world advance (AI booking, aging,
   injuries, etc.). The hard, high-value part.
3. **The booking loop (the game)** — sign, book cards, make matchups, sim
   events, update records/rankings, manage the budget.
4. **The living world** — AI promotions, time progression, aging/growth/decline,
   retirements, and newgen (fictional prospect) generation.

### The one rule that matters most

The **entire simulation is a headless, UI-independent library.** No UI
references anywhere in `Core.Sim` or `Core.Model`. The whole world is a plain
in-memory object graph; the sim mutates it; the UI is only a viewer bound on
top. This is non-negotiable because:

- **Balancing needs mass simulation** — "sim these two fighters 10,000 times,
  report the win split"; "advance 20 years, check champions/retirements/newgen
  quality look sane." Only possible if the core runs headless in tests/CLI.
- **Determinism via a seeded RNG** — a single seeded random source threads
  through the whole sim, so a save + seed reproduces any fight exactly. Makes
  bugs reproducible and testing possible. The master seed lives on `World`.

### Save compatibility: none, on purpose (dev rule, 2026-07-08)

**Never write code that accommodates old save data.** The game is early in
development and no real careers exist yet; save files are disposable. Concretely:

- When the model changes shape, bump `World.LatestSaveVersion`, change the
  schema/reader/writer to the new shape ONLY, and **delete** any code whose only
  purpose is reading an older shape. No `if (SaveVersion >= N)` gates, no
  migration passes, no legacy fallbacks in `Persistence`.
- The reader refuses any save whose version doesn't match exactly
  (`WorldReader.ReadMeta` throws with a clear message; the app shows
  "Load failed: …" and the player starts a new world).
- When a change invalidates existing saves, **call it out explicitly in the
  session summary** ("this breaks existing saves") so it's a known event, not a
  surprise — that alert is the only save-compat obligation.

(All accumulated migration shims — v11 through v28 gates, the pre-city/
pre-country rebuild paths — were deleted on 2026-07-08 under this rule.)

### Project layout

```
MmaManager.sln
├── Core.Model      (done)  domain entities — no dependencies
├── Core.Sim        (done)  fight engine, generator, booking, living world — refs Model, headless
├── Persistence     (done)  SQLite save/load of the World graph — refs Model
├── UI              (done)  console sandbox / smoke-test harness — refs everything
├── Wpf             (done)  MmaManager.Wpf — the WPF app (observe & advance) — refs everything
└── Tests           (done)  xUnit suite (119 tests) — the regression gate
```

### Model conventions (already in force in `Core.Model`)

- **Ratings are 0–100.** The `Rating` struct clamps on construction, so an
  out-of-range value can't exist. Implicit int conversions keep math clean.
- **Strongly-typed ids** (`FighterId`, `PromotionId`, …). `Value == 0` = unset.
- **Relationships are by id, never object reference.** `World` is the single
  resolver (`world.Get(id)`, `world.RosterOf(id)`, `world.FreeAgents()`). Keeps
  the graph acyclic and saves trivial.
- **No duplicated derived data.** Roster derives from contracts; the belt-holder
  lives only on `Title`. The one deliberate cache is `FightRecord`, updated by
  the sim when a bout completes.
- **Mutable class vs value type:** things the sim changes over time are
  `sealed class`; pure values (`Rating`, `Money`, ids, records) are immutable
  structs/records.
- **Save handling:** call `World.SyncIdCounters()` right after loading (id
  counters are private and don't persist); `FightOutcome.PlayByPlay` may be
  dropped on save and regenerated from the seed. No migrations, ever — see the
  save-compatibility rule above.

---

## Current state — `Core.Model` (complete)

Dependency-free .NET 8 class library. Contents:

- **Fighter** with ~35 skills across five groups + five behavioural tendency
  sliders:
  - Striking (7): PunchPower, PunchTechnique, KickPower, KickTechnique,
    ClinchStriking, StrikingDefense, Footwork
  - Grappling (8): TakedownOffense, TakedownDefense, TopControl, BottomGame,
    SubmissionOffense, SubmissionDefense, Scrambling, ClinchControl
  - Physical (6): Strength, Cardio, Speed, Chin, Recovery, Flexibility
  - Mental (4): FightIQ, Composure, Heart, Discipline
  - Intangibles (2): Popularity, Charisma
  - Tendencies (5): StrikingPreference, Aggression, Pace, Counterfighting,
    FinishInstinct
  - Plus: physical measurables (height/reach/DOB/stance), `PrimaryStyle`,
    hidden `PotentialAbility` cap, dynamic state (Status, Morale, Momentum,
    CurrentInjury), Contract, FightRecord, BoutHistory, `AgeOn(date)`.
- **Promotion** (+ Division, Title, TitleReign) — finances, prestige, regional
  popularity, the divisions it runs, its belts.
- **Event / Fight / FightOutcome / FightStatline / EventFinancials** — a card,
  its bouts, and post-sim results and money.
- **World** — aggregate root and save root: dictionaries of all entities, the
  current date, master RNG seed, player's promotion, id generation, and lookup
  helpers.

The `Core.Model/README.md` documents these conventions in-repo.

---

## Roadmap

1. **Data model + fighter generator** — *done.* The model is complete and the
   generator (`Core.Sim/Generation`) fills a `World` with coherent fictional
   fighters (a wrestler *looks* like a wrestler; ratings correlate; ages/records/
   potential distribute realistically), reproducibly seeded from `World.RngSeed`.
2. **Fight engine MVP** — *done (first tuning pass).* Tick/exchange-based state
   machine in `Core.Sim/Engine` (`FightEngine` → per-bout `Bout`): tracks
   position (standing / clinch / ground sub-positions), picks actions weighted
   by ratings + tendencies, resolves via a logistic comparison + seeded RNG,
   applies damage / stamina drain / position change / scoring, emits play-by-play,
   and finishes by KO/TKO/submission or 3-judge decision. Stamina scales
   effectiveness so cardio decides late rounds. `FightLab` mass-sims matchups for
   balancing; current tuning gives ~85% for a +20 overall favourite, symmetric
   corners, and realistic finish rates. Known lever to revisit: pure wrestlers
   beat equal-overall strikers ~68% (arguably realistic; tunable via scoring/getups).
   Tuning dials live as constants at the top of `Bout.cs`.
3. ~~**Booking loop**~~ — *done.* Create events, matchmaking, sim a full card,
   update records/rankings/titles/finances. See `Core.Sim/Booking`.
4. **Management layer** — contracts, finances, roster limits, prestige.
5. ~~**Living world**~~ — *done (MVP).* `Core.Sim/Living`: `WorldTick` advances
   time in weekly steps with staggered per-promotion events; `FighterDevelopment`
   grows the young toward `PotentialAbility` and declines the old (athletic tools
   first); `InjuryModel` shelves the hurt; `Lifecycle` heals, retires, and hands
   off vacated belts; `NewgenService` feeds in young prospects; the tick restocks
   thin divisions from free agents. `WorldBuilder` seeds a multi-promotion world
   (talent tiered by prestige). Validated over 3 years: stable population, prospects
   climb ~+7–11 overall, belts change hands, ~40% finishes.
   *Refinements to revisit (all "smarter AI booking"):* activity-aware matchmaking
   (low-ranked fighters fight rarely today) and ability-based signing/cutting (a
   strong free agent can sit unsigned). ~~Records-aware rankings~~ *done* —
   `RankingService.RecomputePromotion` now ranks contenders by a résumé score built
   mostly from fight results weighted by opponent quality (beating a champion / a
   top-ranked, high-overall fighter counts most), recency, and finishes; ability is
   only a small baseline. Recomputed after each card.
6. **Depth & polish** — titles, rivalries/storylines, morale, media, hype/buyrates.

Playable and fun by the end of step 3 — that's the first milestone to aim for.

---

## Immediate next tasks

1. ~~**Scaffold the solution**~~ — *done.* `MmaManager.sln` builds green with
   `Core.Model`, `Core.Sim`, `Persistence`, `UI` in the dependency directions
   above. (`UI` is a placeholder console host until the framework is chosen.)
2. ~~**Fighter generator** in `Core.Sim`~~ — *done.* See `Core.Sim/Generation`
   (`FighterGenerator`, `StyleTemplates`, `NameData`) and the deterministic
   `SimRandom`. `FighterAbility` (in `Core.Sim/Ability`) is the first-pass CA
   weighting the growth model will build on. The `UI` console host prints a
   scouting sheet as a smoke test.
3. ~~**Fight engine MVP**~~ — *done (first tuning pass).* See `Core.Sim/Engine`
   (`FightEngine`, `Bout`, `FightLab`) and Roadmap step 2 for status/known levers.
4. ~~**Booking loop**~~ — *done.* See `Core.Sim/Booking`: `LeagueBuilder` (new-game
   world: promotion, signed roster, divisions, belts, seeded rankings),
   `Matchmaker` (competitive pairings + rotating title headliner), `EventService`
   (book + sim a card), `ResultsProcessor` (records/momentum/titles),
   `RankingService` (contender movement), `Financials` (gate/PPV/payroll).
   Validated over a 14-event season: ~50% finishes, belts change hands, budget
   trends up. The `UI` host now runs a full card and shows the fallout.
5. ~~**Living world**~~ — *done (MVP).* See `Core.Sim/Living` (`WorldTick`,
   `FighterDevelopment`, `InjuryModel`, `Lifecycle`, `NewgenService`) and
   `WorldBuilder` for the multi-promotion world. The `UI` host now advances the
   world three years and shows the fallout (champions, risers, pound-for-pound).
6. ~~**Persistence (SQLite)**~~ — *done.* `Persistence/`: `SaveGame.Save/Load`
   round-trips the whole `World` graph to a SQLite file (`Microsoft.Data.Sqlite`,
   `Pooling=false`). Schema in `SaveSchema`; the 32 rating columns are generated
   from `FighterColumns` so writer/reader can't drift. Play-by-play is dropped
   (regenerable from seed). Round-trip verified by test on a 30-week-advanced world.
7. ~~**WPF UI (observe & advance)**~~ — *done (first cut + profiles + art).* `Wpf/`
   (`MmaManager.Wpf`), MVVM: New Game / Save / Load / Advance Week+Month, and five
   screens — Dashboard, Roster (browsable list), a dedicated **Fighter** profile page
   (large portrait, full vitals, ratings as grouped bars, fight history — opened by
   double-clicking a roster row or "View Profile"), Rankings & Champions (per-promotion
   selector — every org has its own belts), Event results. **Art** (`Wpf/Art`):
   `PortraitFactory` is a parametric vector face generator (200×200 canvas): shaded
   face silhouette, detailed eyes (iris gradient, eyelids, lashes, brow strokes), nose,
   two-tone lips, layered hair with strand texture + styles (buzz/short/receding/afro/
   mohawk/top-knot), textured beards/goatees/stubble, and attribute-driven touches
   (skin by nationality, greying + wrinkles by age, face width by weight class,
   cauliflower ears for grapplers, scars/crooked nose for brawlers). Kept as the
   *fallback*. **Fighter portraits are now photographic** (`FaceLibrary`): a bundled
   pool of AI-generated fictional faces (`Wpf/Assets/Faces/*.jpg`, ~55 curated adult
   males, StyleGAN via this-person-does-not-exist.com — see ATTRIBUTION.txt) assigned
   deterministically by fighter id, bucketed by nationality→ethnicity; vector portrait
   used only if the pool is missing. (Pool is personal-use; swap for a licensed pack
   before commercial use — loader unchanged.)
   `LogoFactory` has hand-designed vector logos for AFC/VMMA/ICF/CK + a generated fallback
   badge. Headless hooks: `--selftest`, `--renderart`, `--rendershot`, `--stylegallery`.
   Console `UI` project kept as sandbox.
8. ~~**Résumé-based rankings**~~ — *done.* See Roadmap step 5.
9. **Next candidates** (pick one): (a) *smarter AI booking* — activity-aware
   matchmaking + ability-based signing/cutting; (b) *player control in the UI* —
   book your own cards, sign/cut fighters; (c) deepen the UI (event detail, filtering,
   sorting, charts, storyline/rivalry surfacing).

Testing: `dotnet test` runs the xUnit suite in `Tests/` (119 tests: rng, generator,
engine balance, booking, development, world tick, persistence round-trip, résumé
rankings, bios). Keep it green as the regression gate.

Known smaller polish items (non-blocking): fighter name collisions in a large
roster (small name pools); knockdowns that don't finish are slightly frequent;
some champions hold modest records (ability-seeded rankings + low fight activity).

---

## Open decisions / things to watch

- **UI framework:** Avalonia vs WPF not finalised. Doesn't block sim work.
- **Save format:** serialise-the-graph first for velocity; move to SQLite once
  the model stabilises if queryable/moddable saves are wanted.
- **Reach & height:** stored on `Fighter` but the engine hasn't decided how much
  they weigh into striking exchanges. Decide during engine work.
- **Submission granularity:** currently one `SubmissionOffense`/`Defense` pair
  rather than per-submission (armbar vs choke). Revisit only if you want that
  depth surfaced.
- **CA/PA weighting:** how much each skill contributes to "overall ability"
  (and thus growth toward `PotentialAbility`) lives in the sim layer, not the
  model — tune it there.

---

## Kickoff prompt for Claude Code

> Read `PROJECT.md` and `Core.Model/README.md`. Scaffold `MmaManager.sln` with
> the four projects and dependency directions described, add the existing
> `Core.Model` files, and get `dotnet build` passing. Then we'll build the
> fighter generator.
