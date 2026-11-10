# MMA Manager

A single-player MMA promotion management sim in the vein of Football Manager and OOTP. The player owns
a fight promotion in an all-fictional world (real geography, fictional people and companies): sign
fighters, book cards, run the business, and climb from a tiny local show to the international stage.
C# on .NET 8, WPF front end, SQLite saves.

`PROJECT.md` is the long design log (2,100 lines, mostly a dated changelog). Do not read it whole; grep
it for the system you are working on. Stable architecture notes start at its "Stack" heading.

## Hard rules

- **The sim is headless.** `Core.Model` and `Core.Sim` never reference UI, files, the network or the
  clock. Everything the game does must be runnable from tests and `tools/longsim`.
- **Deterministic by seed.** Same seed and same code give the same world. Never use `Random.Shared`,
  `DateTime.Now` or unordered iteration where it feeds a roll. Do not add draws to an existing RNG
  stream: several rolls deliberately derive everything from ONE `NextULong`, and an extra draw
  reshuffles every tuned seed in the project. New randomness gets its own keyed stream
  (`Mix(seed, id ^ date ^ salt)`) or a hash roll off an id.
- **No save migrations, ever.** When the model changes shape, bump `World.LatestSaveVersion` and
  change schema, writer and reader to the new shape only. The one obligation: say "this breaks
  existing saves" in the PR and in the summary to the owner.
- **The market only knows what it has seen.** True ability and potential are never shown to the
  player and never handed to anything that decides on an org's behalf. Read through
  `ScoutingService` (`PerceivedAbility`, `Assess`).
- **Never trust one seed.** Balance claims need `longsim compare` across seeds, not a single run.
- **Designed content ships with lore.** New authored fighters, orgs, camps, staff and venues carry
  `Bio`/`Lore` text. All people and companies are fictional.
- **The interface is a desktop, and it is welcoming.** The OS-style shell (taskbar, start menu,
  floating app windows, toasts) is the game's identity and stays. Inside it, lead with people, art
  and story - portraits, posters, quotes, what to do next - and keep dense tables as a toggle, not
  the default (owner, 2026-11-10: "less of a boring spreadsheet all over").
- **Secrets stay out of the repo.** `hf-token.txt` and `.env` are ignored. This repo is public.

## Commands

```powershell
dotnet build MmaManager.sln -v q          # build everything
dotnet test MmaManager.sln -v q           # the regression gate (xUnit)
dotnet run --project Wpf                  # the game
cd tools/longsim; dotnet build -v q
dotnet run --no-build -- compare 10 5     # 10 seeds x 5 years: the balance regression check
dotnet run --no-build -- shoestring 42 20 # the zero-to-hero playthrough
```

`tools/longsim/README.md` says what a healthy world looks like.

## Layout

| Project | What it is |
| --- | --- |
| `Core.Model` | Domain entities. No dependencies. Relationships by id; `World` is the resolver. |
| `Core.Sim` | The simulation: `Engine` (fights), `Booking`, `Contracts`, `Finance`, `Living` (world tick, storylines, camps), `Org`, `Scouting`, `Geography`, `Generation` (the designed database and generators), `Flavor` (text), `Rng`. |
| `Persistence` | SQLite save and load of the whole `World`. |
| `Wpf` | The app: `Shell`, `Views`, `ViewModels`, `Mvvm`, `Art` (portrait, poster and logo rendering), `Assets`. |
| `UI` | Console smoke harness. |
| `Tests` | xUnit suite over Model, Sim and Persistence. |
| `tools/longsim` | Headless multi-year balance harness (not in the solution). |

The fighter portrait pack (`Wpf/Assets/Painted/*.png`, about 2.8 GB) is not in git. It is produced by
the separate `E:/FighterArt` repo and synced in. Without it the app falls back to the tracked
placeholders, so a fresh clone builds and runs.

## Agent team

Specialist subagents live in `.claude/agents/`. The lead session is the orchestrator: it plans, hands
each part to the owning agent, reviews what comes back, and is the only one that merges.

| Agent | Owns | Model |
| --- | --- | --- |
| `fight-engine` | `Core.Sim/Engine`, `Core.Sim/Ability`, `Flavor/MatchRating.cs`, fight tests | opus |
| `world-sim` | `Core.Sim/Living`, `Org`, `Finance`, `Contracts`, `Booking`, `Scouting`, `Geography`, `Rng` and their tests | opus |
| `database` | `Core.Sim/Generation` (designed roster, legends, names, generators), `Org/DesignedStaff.cs`, `Geography/Venues.cs`, `Flavor/Quotes.cs`, `Flavor/Biographer.cs`, lore | sonnet |
| `persistence` | `Persistence/`, `World.LatestSaveVersion`, `Tests/PersistenceTests.cs` | sonnet |
| `ui` | `Wpf/` except `Wpf/Art` and `Wpf/Assets` | opus |
| `art` | `Wpf/Art`, `Wpf/Assets`, and the `E:/FighterArt` pipeline | sonnet |
| `agent-builder` | Runtime AI agents: `Core.Agents` (policies, observations, tools) and the model host | opus |
| `balance-tester` | Read-only. Runs `tools/longsim` and reports. May add reporting to longsim only. | sonnet |
| `sim-qa` | Read-only for game code. Hunts correctness bugs by running the sim: monthly world invariants, a scripted headless player, save and reload soak, crash hunting. Owns the checking side of `tools/longsim` and invariant tests. | sonnet |
| `reviewer` | Read-only. Independent review of a branch before merge. | opus |

`Core.Model` is shared. Any agent may add a field its area needs, but must list it in the PR so
`persistence` can save it and the version gets bumped in the same PR.

## How work flows

1. **One task, one branch, one worktree.** Branch from `main` as `<area>/<short-name>`
   (`engine/standing-tko`, `ui/advance-shortcuts`). Agents doing parallel work each get their own
   worktree; two agents never edit one working tree. The lead creates it by hand and names the path
   in the agent's brief: `git worktree add E:\mma-wt\<name> -b <area>/<name> origin/main`. (The Agent
   tool's automatic `isolation: "worktree"` is refused on this machine and leaves locked empty
   worktrees behind.) An agent uses absolute paths inside its own worktree and never touches the
   main checkout or another agent's folder. A worktree has no fighter portrait PNGs, since they are
   not in git: fine for sim work, but for UI work that will be judged by eye, copy
   `Wpf/Assets/Painted/*.png` in from the main checkout first.
2. **Stay in your area.** Edit only files your area owns. If another area's file must change, keep
   the edit minimal and list it under "Out-of-area edits" in the PR.
3. **The agent proves its own work** before handing back: build, the tests for its area, and the
   full suite. It reports what it ran and the result, including failures, in plain words.
4. **The orchestrator's gate.** Nothing merges until the lead has:
   - read the whole diff itself, not the agent's summary of it;
   - run `dotnet build` and `dotnet test` on the branch and seen them pass - on the branch merged
     with current `main`, not as it was when the agent finished (two PRs that each passed alone
     failed together on 2026-11-10);
   - for anything touching fight outcomes, money, contracts, generation or tick order, a
     `balance-tester` report from `longsim compare`, branch against `main` (the README's healthy
     ranges are out of date; compare like with like, and use enough seeds - one bad seed in 20
     proved to be noise at 100);
   - for anything touching sim rules or saving, a `sim-qa` pass once its checks exist;
   - for a change of any size or risk, a `reviewer` pass, given the diff and the task but not the
     author's conclusions;
   - checked the save version was bumped if the model changed, and that the PR says so;
   - checked no RNG stream gained a draw.
   If the gate fails, the work goes back to the same agent with the specific finding.
5. **The lead merges what passes the gate** (owner, 2026-11-10: "you are able to merge anything you
   deem worthy; you manage the repos"). It tells the owner plainly when a merge breaks saves, and
   shows him matters of taste - a new look, a redesigned screen - before merging them.
6. **Shared tables are append-only during a feature.** Adding a designed fighter, a venue or a quote
   in parallel is fine; renaming or reordering existing rows is done alone on `main`, because row
   order feeds ids and ids feed seeds.

## Save-breaking changes

Say so in the PR title (`[breaks saves]`), in the PR body, and in the summary to the owner.
