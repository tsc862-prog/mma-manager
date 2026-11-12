# longsim — headless balance & simulation harness

A console harness over `Core.Sim` for long-horizon world simulations: balance
sweeps across many seeds, org-finance diagnosis, and simulated playthroughs.
Not part of `MmaManager.sln` — build/run it directly. This is the tool behind
the 2026-07-08 balance passes (solvency gates, retainer restructure,
designed-world sizing); see PROJECT.md "Done recently" for what it caught.

```powershell
cd tools/longsim
dotnet build -v q -c Release

# 1) Single world, yearly breakdowns (default: seed 2026, 30 years)
dotnet run --no-build -c Release -- 2026 30

# 2) Multi-seed balance sweep: N seeds (1..N) x YEARS, in parallel, aggregate report
dotnet run --no-build -c Release -- compare 10 5

# 3) Quarterly P&L trace of the designed mid-tier orgs (why is X bleeding?)
dotnet run --no-build -c Release -- diagnose 7 3        # seed 7, 3 years

# 4) Shoestring playthrough: found NWF (prestige 5, $250k, empty roster), 20 years
dotnet run --no-build -c Release -- shoestring 42 20    # seed 42

# 5) Simulation QA (is the sim CORRECT, not "are the numbers healthy"): see "Checking modes" below
dotnet run --no-build -c Release -- check 20 10             # invariants, all-AI worlds, every month boundary
dotnet run --no-build -c Release -- playcheck 12 10         # a scripted human plays the same worlds
dotnet run --no-build -c Release -- check 6 10 --soak       # + save/reload twin soak
dotnet run --no-build -c Release -- determinism 3 3         # same seed twice
dotnet run --no-build -c Release -- digest 3 3              # end-state digests (run under Debug and Release, diff the text)
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
  and world rank. The founded org plays by the PLAYER's deal rules (since v45): a
  deal fought out opens a 30-day exclusive re-sign window and then free agency
  (`Contracts.DealWindow`), and the script re-signs inside the window at his table
  terms when they fit its budget (`ShoeSweep.ReSignInWindow`). `--ai-renewals`
  restores the old harness (the world AI renews or lets walk, with poaching at
  expiry); `--no-resign` lets every deal end. Same flags for `shoesweep`.
  Since v46 rival offers reach the founded org too (`Contracts.OfferMarket`: a bigger
  promotion's real offer on a fighter's last fight; let go, he signs there when his
  window closes). The script answers each one first (`ShoeSweep.AnswerOffers`): MATCH
  when he matters (champion, top two in his division, or popularity 40+) and the
  match fits the bank and 1.25x the larger of its budget and its purse cap, else LET
  HIM GO. `DIAG rival offers` counts offers lodged per seed-year, how the script
  answered, how each ended (matched, beaten at the table, lost to the rival,
  withdrawn) and lifer refusals.

## Checking modes (simulation QA)

`compare` asks "are the numbers healthy?". These ask "is the sim **correct**?" - they find the bug, pin it to a seed and
an in-game date, and print the entities involved by name and id. They live in `Check/` and never change the world (the
one exception is `--selftest`, below). All of them exit non-zero when something fails.

| mode | what it runs |
| --- | --- |
| `check <seeds> <years>` | All-AI worlds, seeds `1..N` (`--seed-from K` starts at K), the invariants below at **every month boundary** (after the monthly pass of the tick that crossed it). One run per seed, in parallel (`--par N`, default cores/4). |
| `playcheck <seeds> <years>` | The same worlds with a **scripted human** (`Check/PlayerScript.cs`): odd seeds found a promotion (prestige 5-17, $250k), even seeds take over a designed mid-size house (IFA, ICF, GFC, UFL, RCS, SCM, CK, CC, SCL, by seed). Founding seeds divisible by 3 play *recklessly* on purpose (staff, streams, marketing, no affordability checks) to reach bankruptcy and the code after it. The same invariants run, plus the `PLAY-*` ones. |
| `--soak` (either mode) | At up to three pseudo-random months per run: save to a temp file, load, and continue the loaded copy beside the never-saved world. Digest-compared every `--soak-every` months (default 6) and at the end; on a difference the field-level diff names the first diverging field. The world is also compared with its own reload at the moment of the save. Scratch files go to `--tmp DIR` and are deleted. |
| `determinism <seeds> <years>` | Every scenario (all-AI and player) twice in one process: strict digest (dictionary order counts) and canonical digest (keys sorted) of the end state, plus half-yearly checkpoints to find the first divergence. |
| `digest <seeds> <years>` | Prints end-state digests only. Run it under `-c Debug` and `-c Release` and diff the text: that is the Debug/Release agreement check. |

Other flags: `--quiet` (no per-run lines), `--samples N` (violations shown per invariant, default 4), `--only ai|founded|takeover`,
`--hang SECS` (watchdog: a run with no completed week for this long is reported as `HANG` and the process exits 4),
`--selftest` (see below).

**Reading the output.** A per-run table, then one row per invariant (`checked`, distinct `violations`, `recur` = the same
violation seen again on a later month, `runs` = how many runs it hit), then the first samples of every invariant that
fired, earliest date first. A violation is identified by (invariant, run, entity key), so a bad row that persists for ten
years is **one** finding. Player findings carry the script's last actions (`recent player actions`), which is usually the
whole reproduction. Everything is keyed on the seed, so a finding reproduces exactly; for anything claiming a regression, run
the identical check on `main` before reporting it.

**The scripted player** drives the same calls the WPF view models make (read `Wpf/ViewModels/*.cs`): `PlayerBooking.CreateEvent /
AddBout / AddLocalBout / RemoveBout / CancelEvent` and the direct card-order swap, `NegotiationSession` (their terms, a lowball that
walks them out, haggling, walking away) then `ContractLifecycle.Sign / Release`, renewals, `Decisions.Accept / Decline`,
`Sponsorships.Accept`, `MediaService.Accept / LaunchStream`, offer declines, `RetentionService.TryPayBonus`, `Camps.PayStipend /
StopStipend / BuyReport` and the team-deal write, `StaffService.Hire / Fire`, `PromotionLadder.Promote`, `Franchise.SetFace`,
`PromotionSetup.CommissionTitle`, the house-rule and marketing-budget writes, and time via both `WorldTick.AdvanceWeek` (the world runs
the player's card itself) and `AdvanceWeeksStoppingForPlayer` + `RunEventLive` (the path the UI uses). The awkward orders it takes on
purpose: cancel a card that is not the newest, remove a bout then add another, release a champion, release a fighter who is booked on a
future card, sign a fighter in the middle of building a card, answer a veteran-cut or title-shot decision after the fighter has already
been released, let desk decisions and offers lapse, take (or ignore) the tier promotion, and go broke. Every choice comes from a stream keyed on
(world seed, in-game date, routine name) - never the sim's own streams - so a run reproduces from the seed alone and a loaded twin
makes the same choices as the world it was saved from. Every action runs under a ledger watch: a balance that moves without an equal ledger
line is `PLAY-LEDGER`, attributed to the action. An exception inside an action is a `CRASH` finding with the action and the
last orders, not a stop.

**Invariants** (`Check/WorldChecker.cs`; the catalogue is printed with every run, fired or not):

| name | rule |
| --- | --- |
| `RANK-CONTRACT` | every ranked fighter exists, is contracted to the ranking promotion, is not retired |
| `RANK-SHAPE` | no duplicates in a division, no fighter in a division he does not fit, no champion in his own ranking |
| `CHAMP-CONTRACT` | every champion exists, is contracted to the belt's promotion, is not retired |
| `TITLE-LINK` | titles, `Promotion.Titles` and division belts agree; no belt on a missing promotion |
| `LINEAGE` | no overlapping reigns, no reign that ends before it begins, at most one open, the open reign is `CurrentChampion` |
| `BOOK-DOUBLE-DATE` | nobody on two cards on one date (all cards, including the back-dated history and the local circuit), twice on one card, or against himself |
| `BOOK-SCHEDULED` | scheduled cards: corners exist, none retired, nobody on two scheduled cards, every non-local corner contracted to the host, title fights on the host's own belt, no card left Scheduled 35 days past its date |
| `EVENT-NAME` | a promotion's events have distinct names (the local circuit belongs to nobody and repeats its names) |
| `EVENT-FUTURE` | no card is Completed while the world clock is still before its date |
| `CONTRACT-LINK` | no contract on a missing or defunct promotion, none on a retired fighter, no negative terms |
| `DEFUNCT` | a defunct promotion has no scheduled event, champion or ranking, and its ledger stops growing |
| `LEDGER` | `Balance = opening + sum(Ledger)`. The opening balance is **not stored**, so it is taken the first time a promotion is seen and every later month must hold it; the ledger never shrinks; entry sign matches category (income-only and expense-only categories; `Acquisition` and `Other` are two-way). `Ledger.Record` is the only writer of `Balance` in `Core.*`, so the ledger is a complete record and a drift means a new direct write. A ledger line has no counterparty field, so "every payment has a counterparty that existed on that date" cannot be asserted from the model. |
| `RECORD-HISTORY` | a fighter's record is **at least** the tally of his completed bouts in `world.Events` (overturned results count as NC). It must **equal** it for fighters minted after day one. Exempt, by design: day-one fighters and legends carry back-filled tallies (`HistoryBuilder` pads minted opposition, `LegendBuilder` authors the tally), and `LocalTalent` / discovered locals carry a flavour tally (`LocalScene`) that is wiped on discovery. |
| `RECORD-SHAPE` | wins by method never exceed wins; no negative component; `CurrentStreak` agrees with the tail of the bout sequence |
| `BOUT-HISTORY`, `BOUT-ORDER` | `BoutHistory` lists exactly the completed bouts the fighter was a corner of, each once, in date order |
| `REF-DANGLING` | every id referenced anywhere (contracts, rankings, titles, cards and results, bonds, rivalries, camps, venues, cities, news, mail, desk, bidding wars, shortlist, global rankings) resolves |
| `ID-REUSE` | an id never goes to a second entity (tracked by object identity), counters never fall below an id in use or move backwards, fight ids are unique, desk ids are unique |
| `STATE-FIGHTER` | status agrees with injury/suspension, age 16..44 unless retired, loyalty 0..100, momentum -100..100 |
| `STATE-WOMEN-CLASS` | every woman is in strawweight..featherweight |
| `STATE-STIPEND` | a camp stipend is only on a fighter whose org can pay it |
| `STATE-PROMOTION` | at most one player org and it is the flagged one; face of the company is on the roster; balances sane |
| `STALE-DESK` | no pending desk decision or offer is left 35+ days past its deadline |
| `GLOBAL-RANK` | world and pound-for-pound rankings list real, unretired fighters |
| `PLAY-LEDGER`, `PLAY-RAN-UNAVAILABLE`, `PLAY-EVENT-GONE` | per action: money without a ledger line; a due card runs a bout with a retired, released or signed-away fighter (live path also: injured or suspended) without the sim pulling the bout; a cancelled card stays in the world |
| `SOAK-ROUNDTRIP`, `SOAK-VALUE`, `SOAK-ORDER` | a save/load changes a value; the loaded twin diverges from the never-saved world in a **value**; the values match but a dictionary enumerates in a different order. The last is the documented difference after a non-newest removal (`CancelEvent`, `ReleaseLocals`): reported separately and not counted as failing. |
| `NONDETERMINISM`, `CRASH`, `HANG` | same seed gave two worlds; an exception; a run that stopped making progress |

One deliberate non-rule: a bidding war over a fighter who has just retired or signed is cleaned by `BiddingWars.RunWeekly` the
following week (the monthly pass runs after it in the same tick), so that one-week lag at a month boundary is by design and not asserted.

`--selftest` proves the checker can fail: in month 3 of every run it breaks the world in memory five ways (a retired fighter
in a ranking, a balance moved with no ledger line, an id that resolves to nothing, two open reigns, a bout against himself)
and the run must report each. Use it after changing `WorldChecker`; never report numbers from a `--selftest` run.

The comparer behind `--soak` and `determinism` is a trimmed copy of `Tests/PersistenceGraph.cs` (an exe cannot reference the test
assembly), with key-order differences returned apart from value differences and a canonical digest. A field added to that
file's not-saved list must be added to `Check/WorldGraph.cs` too.

**Pinned findings.** Findings that are cheap to pin live in `Tests/SimQaFindingsTests.cs`, skipped with their `SQA-nn` id so the suite
stays green; the agent that fixes the bug un-skips its test.

**Harness note (all-AI worlds).** `compare`, `diagnose` and the single-world mode set `world.PlayerPromotion = default` but leave AFC flagged
`IsPlayerControlled`, so AFC alone gets no designed front office (`StaffService.SeedFrontOffices` skips the flagged org). The effect on those
runs is nil today (player-only staff effects read `promo.Staff`, which is empty either way), but `check` clears the flag so an all-AI world is
all AI.

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
  market. The churn fades as prestige (and the cap) grows. (With `--ai-renewals`
  only; by default the script renews in the deal window — see shoestring above.)
- **Determinism**: same seed + same code = same world. Any change to generation,
  the designed DB, or tick order shifts all results — re-baseline with `compare`
  after such changes rather than comparing against old logs.
- Fighter counts in reports exclude `LocalTalent` (the unnamed local-circuit filler).

## Reading results — what "healthy" looks like (as of 2026-07-08)

> **Stale (flagged 2026-11-11, sim-qa).** The ranges below were tuned in July and no longer match `main`: a balance run found about
> 2,420 active fighters and average popularity about 36 at year 5 (this text says ~900 and 38-40), and the shoestring reference arc no longer
> matches. They were NOT re-measured here because longsim runs are done on GitHub now (`balance.yml`), not on the owner's PC. Treat every figure
> in this section as unverified until a `compare 20 5` from `balance.yml` replaces it.

**Running the checks on GitHub.** `gh workflow run simqa.yml -f ref=<branch> -f seeds=20 -f years=10` runs `check`, `playcheck`, both `--soak`
variants and `determinism` on a windows-latest runner and uploads the reports (`gh run download <id>`). Locally, run only a tiny smoke
(`check 2 1`), one process at a time.


From the tuned baseline (2026-07-09, résumé-priced market; 10 seeds × 5 years):
AFC #1 in 10/10 (prestige 76–80), pyramid holds, ~895–920 active fighters, avg
popularity 38–40 (flat over decades), org count growing ~19 → 23 by Y5 (~27 by
Y10, 2 Internationals). Expected mortality: VMMA ~1–2/10 (year 2 — widest
product below the summit; when it falls, IFA inherits Europe), occasional
procedural startups. Anything beyond that — CK/ICF/IFA/UFL/SCM folding at all,
or IFA deep in the red — is a regression.
