# longsim — headless balance & simulation harness

A console harness over `Core.Sim` for long-horizon world simulations: balance
sweeps across many seeds, org-finance diagnosis, and simulated playthroughs.
Not part of `MmaManager.sln` — build/run it directly. This is the tool behind
the 2026-07-08 balance passes (solvency gates, retainer restructure,
designed-world sizing); see PROJECT.md "Done recently" for what it caught.

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
cd "e:\MMA Manager\tools\longsim"
dotnet build -v q

# 1) Single world, yearly breakdowns (default: seed 2026, 30 years)
dotnet run --no-build -- 2026 30

# 2) Multi-seed balance sweep: N seeds (1..N) x YEARS, in parallel, aggregate report
dotnet run --no-build -- compare 10 5

# 3) Quarterly P&L trace of the designed mid-tier orgs (why is X bleeding?)
dotnet run --no-build -- diagnose 7 3        # seed 7, 3 years

# 4) Shoestring playthrough: found NWF (prestige 5, $250k, empty roster), 20 years
dotnet run --no-build -- shoestring 42 20    # seed 42
```

Long runs: pipe to a log (`> sim.log 2>&1`) — a 30-year world takes ~5–6 min,
a 10-seed × 5-yr sweep ~2–4 min (parallel), a 20-yr shoestring ~2–3 min.

## What each mode reports

- **Single** (`<seed> <years>`): per year — events/fights/finish rate, retirements,
  newgens, active fighters, org pyramid by tier, foundings/folds, top-6 orgs with
  bank, top draws and p4p; final section — every living org, the #1 org's champions,
  biggest stars, designed-fighter survival.
- **compare** (`compare <n> <years>`): one row per seed (actives, avg pop, pyramid,
  AFC prestige+roster, top 3, IFA bank, zombie orgs, designed folds with year) and
  an AGGREGATE block (fold rates per org, min/avg/max ranges). This is the mode
  that catches balance regressions — **never trust a single seed** (2026 and 29
  were survivor outliers that hid a 9/10 ICF fold rate).
- **diagnose** (`diagnose <seed> <years>`): quarterly per-org P&L for the designed
  National+/Continental orgs — bank, prestige, roster, events, avg house, income
  split (gate/TV/sponsorship) vs expense split (purse/production/overhead/signing).
  Use when compare shows someone dying and you need to know which line item kills them.
- **shoestring** (`shoestring <seed> <years>`): the zero-to-hero fantasy simulated —
  found New Wave Fighting (prestige 5, $250k, LW/WW/MW + women's straw/fly, empty
  roster) and run it for N years. Yearly: prestige/tier + % to next tier, bank,
  year P&L, events + avg house, TIER CROSSING banners, deals, champions, ace/draw,
  and world rank.

## How the harness drives the sim (gotchas)

- **All-AI worlds**: after `WorldBuilder.Build`, set `world.PlayerPromotion = default`.
  The AI matchmaker skips whichever org is flagged as the player's, so leaving it
  set leaves that org dark forever and distorts everything.
- **The shoestring needs a player agent.** At prestige 5 the org's `PurseCap` is
  ~zero, so AI contract negotiation (`AiOpeningOffer` → `Negotiator.Evaluate`)
  can NEVER land a fighter — an AI-run founded org sits at 0 roster / 0 events
  literally forever (it survives as a zombie on sponsorship). This is the known
  founded-org artifact: a human hires manually. NOTE: the passing
  `ClimbEngineTests.AShoestring_SurvivesItsFirstYear` relies on this zombie
  behavior (the org never operates, so it trivially survives) — it does not test
  a RUNNING shoestring.
- **The agent must bargain-hunt or the org dies in year one.** Hiring at the
  signing advisor's picks ($8–10k asks) folds NWF inside 12 months: payroll
  alone exceeds a prestige-5 gate. The viable strategy (verified, seed 42):
  each month sign up to 3 free agents into thin divisions at 4/division depth
  (City cards pad with $1k local-vs-local filler), but ONLY fighters asking
  ≤ `$3,000 + prestige × $300` (~$4.5k day one), preferring youth/upside among
  the cheap; deepen to 5–6/division as the bank passes $600k/$1.5M; keep a
  $100k runway. Booking, marketing, retention and promotion leaps all stay
  AI-run (`AiFrontOffice`, `PromotionLadder`).
- **Reference arc (seed 42, 20 years, résumé-priced market of 2026-07-09):**
  a REAL year one — roster ~16 cheap rookies/journeymen, 13 events, ≈break-even
  on the runway → Regional Y5 → years pinned at the Regional prestige cap (31)
  hoarding the promotion cushion → National leap mid-teens, first TV deal →
  Y20: prestige 44, $6.5M bank, #9 of 34 orgs. The zero-to-hero ladder works
  end-to-end at the designed career-long pace. (Before résumé pricing the only
  survivable opening was a degenerate 2–3-fighter sponsorship farm.)
- **Expiry churn is expected on a shoestring**: AI renewals go through `PurseCap`,
  so a tiny org's expiring fighters mostly walk and the agent re-hires from the
  market. The churn fades as prestige (and the cap) grows.
- **Determinism**: same seed + same code = same world. Any change to generation,
  the designed DB, or tick order shifts all results — re-baseline with `compare`
  after such changes rather than comparing against old logs.
- Fighter counts in reports exclude `LocalTalent` (the unnamed local-circuit filler).

## Reading results — what "healthy" looks like (as of 2026-07-08)

From the tuned baseline (2026-07-09, résumé-priced market; 10 seeds × 5 years):
AFC #1 in 10/10 (prestige 76–80), pyramid holds, ~895–920 active fighters, avg
popularity 38–40 (flat over decades), org count growing ~19 → 23 by Y5 (~27 by
Y10, 2 Internationals). Expected mortality: VMMA ~1–2/10 (year 2 — widest
product below the summit; when it falls, IFA inherits Europe), occasional
procedural startups. Anything beyond that — CK/ICF/IFA/UFL/SCM folding at all,
or IFA deep in the red — is a regression.
