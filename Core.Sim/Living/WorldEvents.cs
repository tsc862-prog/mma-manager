using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The macro living world — the beats that make the sport feel like it's growing
/// around the player. A famous outsider (a boxing champ, an Olympian, an
/// influencer) crosses over to MMA with enormous hype; new promotions launch,
/// sometimes lighting up a region that had no scene at all. Everything is
/// scheduled off the calendar (deterministic, idempotent), so the first beats
/// are authored "things to follow" and the later ones keep the world evolving —
/// no saved state needed.
/// </summary>
public static class WorldEvents
{
    // Absolute month index since the game's January 2026 epoch.
    private static int Epoch(DateOnly d) => (d.Year - 2026) * 12 + (d.Month - 1);

    public static void RunMonthly(World world, DateOnly date, SimRandom rng)
    {
        MaybeCrossover(world, date, rng);
        MaybeNewPromotion(world, date, rng);
    }

    // --- Celebrity crossover -------------------------------------------------

    /// <summary>A famous outsider crossing to MMA: the sport, the story, the raw skill.</summary>
    private sealed record Crossover(
        string First, string Last, string Nick, string Backstory,
        Region Region, string Country, WeightClass WeightClass, PrimaryStyle Style, int Overall, int Popularity);

    // Fires first at month 6 (mid-2026 — the authored beat to follow), then every 18 months.
    private const int FirstCrossoverMonth = 6;
    private const int CrossoverEveryMonths = 18;

    // Popularity here is the crossover's STARTING draw — a real boost that makes
    // them an instant attraction, but not an automatic #1: the sport's biggest
    // homegrown stars still sit at ~79-85, and a crossover has to win to climb
    // past them. The influencer (pure casual hype) draws highest; the niche
    // Olympians least. A small seeded jitter is added at debut (see MaybeCrossover).
    private static readonly Crossover[] Crossovers =
    {
        new("Bruno", "Castellano", "Iron Fist", "former undisputed boxing world champion",
            Region.Europe, "ITA", WeightClass.Middleweight, PrimaryStyle.Striker, 66, 66),
        new("Jaylen", "Cross", "Clickbait", "the internet's most-followed influencer-boxer",
            Region.NorthAmerica, "USA", WeightClass.Welterweight, PrimaryStyle.Brawler, 51, 70),
        new("Aleksei", "Morozov", "The Bear", "an Olympic wrestling gold medalist",
            Region.Europe, "RUS", WeightClass.Heavyweight, PrimaryStyle.Wrestler, 69, 52),
        new("Kwame", "Mensah", "The General", "an Olympic boxing champion",
            Region.Africa, "GHA", WeightClass.Welterweight, PrimaryStyle.Kickboxer, 65, 50),
        new("Tavita", "Faleolo", "Earthquake", "a rugby international turned fighter",
            Region.Oceania, "SAM", WeightClass.Heavyweight, PrimaryStyle.Brawler, 58, 52),
        new("Ryu", "Takahashi", "Neon", "a legendary kickboxing champion",
            Region.Asia, "JPN", WeightClass.Featherweight, PrimaryStyle.Kickboxer, 68, 54),
        new("Diego", "Salcedo", "El Rey", "a retired football superstar chasing a second life",
            Region.SouthAmerica, "ARG", WeightClass.Lightweight, PrimaryStyle.Kickboxer, 54, 64),
        new("Chase", "Donovan", "Hollywood", "a reality-TV celebrity with more followers than sense",
            Region.NorthAmerica, "USA", WeightClass.Middleweight, PrimaryStyle.WellRounded, 50, 62),
    };

    private static void MaybeCrossover(World world, DateOnly date, SimRandom rng)
    {
        int m = Epoch(date);
        if (m < FirstCrossoverMonth || (m - FirstCrossoverMonth) % CrossoverEveryMonths != 0) return;

        int occurrence = (m - FirstCrossoverMonth) / CrossoverEveryMonths;
        var c = Crossovers[occurrence % Crossovers.Length];
        string full = $"{c.First} {c.Last}";
        if (world.Fighters.Values.Any(f => f.FirstName == c.First && f.LastName == c.Last)) return;   // once

        // A boost, not a fixed maximum — the buzz varies from one crossover to the next.
        int pop = Math.Clamp(c.Popularity + rng.Range(-5, 5), 30, 78);
        var gen = FighterGenerator.ForWorld(world);
        var f = gen.CreateDesigned(world, new DesignedFighter(
            c.First, c.Last, c.Nick, c.Region, c.WeightClass, c.Style,
            Age: 29, Overall: c.Overall, Wins: 0, Losses: 0, Popularity: pop, Country: c.Country));
        f.Momentum = 0;
        f.Accolades.Add(new Accolade(date, $"MMA crossover — {c.Backstory}"));

        // A marquee outsider debuts on the biggest AI stage that will have them —
        // never the player's org (the player signs their own talent).
        var host = world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != world.PlayerPromotion && p.Divisions.Any(d => d.Matches(f)))
            .OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value)
            .FirstOrDefault();
        if (host is not null)
        {
            f.Contract = PromotionSetup.SignContract(world, host, date, f, rng);
            if (RankingService.DivisionFor(host, f) is { } div && !div.Rankings.Contains(f.Id))
                div.Rankings.Insert(0, f.Id);   // the hype puts them straight in the mix
        }

        // A famous outsider crossing over drags mainstream eyes to the sport — MMA
        // interest ticks up across the region they land in.
        Geography.MmaInterest.BumpRegion(world, c.Region, 3);

        string where = host?.Abbreviation ?? "the free-agent market";
        News.Post(world, date, NewsCategory.Signing,
            $"{full} is coming to MMA — and signs with {where}",
            $"\"{c.Nick}\" — {Capitalize(c.Backstory)} makes the leap to the cage. A real draw the moment they sign — and wholly unproven. Now they have to fight.",
            host?.Id ?? default, f.Id);
        Mail.Post(world, date, MailCategory.General, "Matchmaking",
            $"The whole world is talking about {full}",
            $"{Capitalize(c.Backstory)} just signed with {where} for an MMA debut. Massive casual draw, big questions about the skills. Whatever we think of it, it's rocket fuel for the sport's audience.",
            aboutFighter: f.Id);
    }

    // --- New promotions & regional expansion ---------------------------------

    private sealed record NewOrg(string Name, string Abbrev, Region Region, int Prestige);

    // The world already STARTS as a big pyramid (see WorldBuilder.DefaultPromotions),
    // so new startups arrive gradually — roughly once a year — just to keep the mortal
    // City base replenished as halls fold. Slow enough that the gameworld doesn't churn
    // faster than a player can follow.
    private const int FirstNewOrgMonth = 14;
    private const int NewOrgEveryMonths = 12;
    private const int MaxActiveOrgs = 34;
    private const int GrassrootsFloor = 4;   // the City/Regional base the world never drops below

    // Procedural startup names, so the born-and-die churn at the bottom never runs out
    // of new blood (the old fixed pool could each launch only once, then the base
    // couldn't be replenished and the world went top-heavy).
    private static readonly string[] OrgPrefixes =
    {
        "Apex", "Fury", "Iron", "Titan", "Vanguard", "Frontier", "Summit", "Phoenix", "Rogue", "Savage",
        "Elite", "Prime", "Onyx", "Crimson", "Vortex", "Legion", "Rebel", "Storm", "Blitz", "Valor",
        "Nova", "Fang", "Grit", "Havoc", "Ronin", "Gladiator", "Steel", "Thunder", "Wolf", "Ember",
        "Kraken", "Talon", "Anvil", "Rampart", "Zenith", "Outlaw", "Maverick", "Bastion", "Cobra", "Dynamo",
    };
    private static readonly string[] OrgTypes =
    {
        "Fighting Championship", "MMA", "Cage Combat", "Fight League", "Fight Club", "Combat",
        "Championship", "Fights", "Fighting Alliance", "Cage Wars", "Fight Series", "Combat League",
    };

    private static NewOrg GenerateStartup(World world, Region region, SimRandom rng)
    {
        string name = $"{rng.Pick(OrgPrefixes)} {rng.Pick(OrgTypes)}";
        // A 3-letter code from the name, made unique against every org that's ever
        // existed (defunct included) — bump letters until it doesn't collide.
        string letters = new string(name.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        char Rand() => (char)('A' + rng.Range(0, 25));
        string abbrev = new string(new[] { letters[0], letters[rng.Range(1, letters.Length - 1)], Rand() });
        for (int guard = 0; world.Promotions.Values.Any(p => p.Abbreviation == abbrev) && guard < 60; guard++)
            abbrev = new string(new[] { letters[0], Rand(), Rand() });
        return new NewOrg(name, abbrev, region, rng.Range(5, 12));
    }

    // Startups pick a LANE like the designed world's orgs have: a tight core, a
    // lighter-weights house, a big-man hall, or the wide-slate gamble. The lane
    // shapes their roster, their identity — and their payroll (fewer divisions is
    // how a shoestring survives).
    private static readonly (WeightClass[] Classes, string Flavor)[] StartupNiches =
    {
        (new[] { WeightClass.Lightweight, WeightClass.Welterweight, WeightClass.Middleweight },
            "a tight three-division core"),
        (new[] { WeightClass.Flyweight, WeightClass.Bantamweight, WeightClass.Featherweight, WeightClass.Lightweight },
            "a lighter-weights house"),
        (new[] { WeightClass.Middleweight, WeightClass.LightHeavyweight, WeightClass.Heavyweight },
            "big-man fights only"),
        (new[] { WeightClass.Flyweight, WeightClass.Bantamweight, WeightClass.Featherweight, WeightClass.Lightweight,
                 WeightClass.Welterweight, WeightClass.Middleweight, WeightClass.LightHeavyweight },
            "a full slate from day one"),
    };

    private static void MaybeNewPromotion(World world, DateOnly date, SimRandom rng)
    {
        int m = Epoch(date);
        if (m < FirstNewOrgMonth || (m - FirstNewOrgMonth) % NewOrgEveryMonths != 0) return;

        // The market saturates on total size — but a LOCAL SCENE always exists.
        // With the ladder's doors open (v39 rate gate), the whole bottom rung can
        // climb out over the decades; when the City/Regional base thins past the
        // floor, somebody crazy enough to start a fight promotion appears anyway,
        // cap or no cap. Self-limiting: each founding IS a grassroots org, so the
        // overage stops the moment the base is healthy again.
        int active = world.Promotions.Values.Count(p => !p.Defunct);
        int grassroots = world.Promotions.Values.Count(p => !p.Defunct && OrgSizing.SizeOf(p) <= OrgSize.Regional);
        if (active >= MaxActiveOrgs && grassroots >= GrassrootsFloor) return;    // market is saturated

        // New promotions gravitate to fight-mad markets: pick a region weighted by its
        // MMA interest (squared, minus a floor, to bias hard toward hot regions), so
        // high-interest areas draw more startups — and grow crowded — while cool ones
        // stay sparse. Then a brand-new procedural startup is stood up there.
        var regions = Enum.GetValues<Region>();
        Span<double> weights = stackalloc double[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            double keen = Math.Max(1, Geography.MmaInterest.InRegion(world, regions[i]) - 25);
            weights[i] = keen * keen;
        }
        var spec = GenerateStartup(world, regions[rng.WeightedIndex(weights)], rng);

        bool firstInRegion = !world.Promotions.Values.Any(p => !p.Defunct && p.BaseRegion == spec.Region);

        var promo = new Promotion
        {
            Id = world.NewPromotionId(),
            Name = spec.Name,
            Abbreviation = spec.Abbrev,
            BaseRegion = spec.Region,
            Balance = new Money(200_000m),   // a shoestring — survival isn't guaranteed (sized for the deeper-roster era)
            Prestige = spec.Prestige,
            Reputation = 50,
        };
        promo.TierClearance = Org.OrgSizing.SizeOf(promo);
        world.Promotions[promo.Id] = promo;

        // The startup's lane: weighted toward lean niches — that's how shoestrings
        // survive — with the occasional wide-slate gambler.
        var niche = StartupNiches[rng.WeightedIndex(stackalloc double[] { 3, 3, 2, 2 })];

        // Stock it from the free agents, region-first (local heroes headline the
        // new scene), and stand up its divisions + vacant belts.
        var signed = StockStartup(world, promo, spec.Region, niche.Classes, date, rng);
        // Not enough fighters would sign for this money to run a card: the
        // launch falls through (nobody was signed, nothing more is drawn).
        if (signed.Count == 0) { world.Promotions.Remove(promo.Id); return; }
        // It opens the divisions it could actually staff.
        var opened = niche.Classes.Where(wc => signed.Any(f => f.WeightClass == wc)).ToArray();
        PromotionSetup.ConfigureDivisions(world, promo, signed, opened, date, 15, null, Sex.Male);
        Finance.RevenueService.SeedEstablished(world, promo, date, rng);
        // A little founding buzz in its home metro to build on.
        if (Geography.WorldMap.AnchorCity(world, spec.Region)?.Id is { } anchor)
            Geography.Reputation.BumpOrg(world, promo, anchor, 3);

        News.Post(world, date, NewsCategory.Finance,
            firstInRegion
                ? $"MMA arrives: {spec.Name} launches in {Pretty(spec.Region)}"
                : $"A new promotion is born: {spec.Name} launches",
            firstInRegion
                ? $"For the first time, {Pretty(spec.Region)} has a professional MMA promotion of its own — {spec.Abbrev} opens its doors with {signed.Count} fighters, {niche.Flavor}."
                : $"{spec.Abbrev} enters the scene with {signed.Count} fighters — {niche.Flavor}.",
            promo.Id);
        if (firstInRegion)
            Mail.Post(world, date, MailCategory.General, Mail.Owner(world),
                $"MMA is spreading — {Pretty(spec.Region)} gets its first promotion",
                $"{spec.Name} just launched in {Pretty(spec.Region)}, a market that had no scene at all. The sport's map is growing — new talent, new fans, new places to run.");
    }

    /// <summary>How many fighters a startup signs per division it opens.</summary>
    public const int StartupSigningsPerDivision = 12;

    /// <summary>A free agent asking more than this multiple of the founding cap isn't worth the new house's call.</summary>
    public const decimal StartupAskCeiling = 2m;

    /// <summary>
    /// A startup's purse budget per division, in founding caps: it may pay one
    /// fighter up to <see cref="StartupAskCeiling"/>x the cap, but the division's
    /// purses together stay within this many caps — an average of one cap a head
    /// over a full division, so a card it books costs what its gate can carry.
    /// </summary>
    public const int StartupDivisionBudgetCaps = StartupSigningsPerDivision;

    /// <summary>A division a startup opens needs at least this many signings (two bouts' worth).</summary>
    public const int StartupMinPerDivision = 4;

    /// <summary>A startup launches only with at least this many signings across the divisions it opens.</summary>
    public const int StartupMinRoster = 8;

    /// <summary>The share of its opening bank a startup will spend on signing bonuses, all told.</summary>
    public const decimal StartupSigningBonusShare = 0.15m;

    private static readonly Contracts.Negotiator StartupNegotiator = new();
    private static readonly Contracts.FighterValuation StartupValuation = new();

    /// <summary>
    /// A new promotion's founding signings, through the market like anyone's: it
    /// ranks each division's free agents on its OWN read of them (fog of war — a
    /// brand-new house scouts poorly, so it misjudges), home region first; skips
    /// anyone asking more than <see cref="StartupAskCeiling"/>x what it can pay
    /// (<see cref="Contracts.Negotiator.FoundingPurseCap"/>) and anyone with a
    /// public bidding war running; offers the rest his asking purse (rounded UP
    /// to the market's $500 step — at a $1,500 ask, rounding down is a lowball
    /// he counters) while the division's purse budget lasts
    /// (<see cref="StartupDivisionBudgetCaps"/> founding caps), a win bonus of up to three
    /// quarters of it, and the smallest signing bonus he'd call real when he
    /// wants one, while the bonus pot (<see cref="StartupSigningBonusShare"/> of
    /// the bank) lasts — a shoestring can't pay the established houses' half-
    /// purse bonus — and signs only those whose answer is Accept, via
    /// <see cref="Contracts.ContractLifecycle.Sign"/>. Up to
    /// <see cref="StartupSigningsPerDivision"/> per division. The deals are drawn
    /// up first and signed only where they staff a division
    /// (<see cref="StartupMinPerDivision"/>) and the whole roster can run cards
    /// (<see cref="StartupMinRoster"/>); otherwise nobody signs and the list is
    /// empty — the free-agent market prices most journeymen far above a new
    /// small hall's budget, and a promotion launched with two fighters only
    /// paid overhead until it went bankrupt.
    ///
    /// RNG: one draw on the shared stream per fighter signed, exactly as the old
    /// founding path took (it priced each deal off that draw); the draw is now
    /// unused, kept so a founding with the same number of signings leaves the
    /// stream where it always did.
    /// </summary>
    internal static List<Fighter> StockStartup(World world, Promotion promo, Region home, WeightClass[] classes,
        DateOnly date, SimRandom rng)
    {
        decimal foundingCap = Contracts.Negotiator.FoundingPurseCap(world, promo);
        decimal bonusPot = Math.Max(0m, promo.Balance.Amount) * StartupSigningBonusShare;

        var plan = new List<(Fighter Fighter, ContractOffer Offer)>();
        foreach (var wc in classes)
        {
            var pool = world.FreeAgents()
                .Where(f => f.WeightClass == wc && f.Sex == Sex.Male && f.Status == FighterStatus.Active)
                .OrderByDescending(f => f.Nationality == home ? 1 : 0)
                .ThenByDescending(f => Scouting.ScoutingService.PerceivedAbility(world, promo, f))
                .ThenBy(f => f.Id.Value)
                .ToList();
            var division = new List<(Fighter, ContractOffer)>();
            decimal budget = foundingCap * StartupDivisionBudgetCaps;
            foreach (var fa in pool)
            {
                if (division.Count >= StartupSigningsPerDivision) break;
                if (world.BiddingWars.Any(w => w.Fighter == fa.Id)) continue;

                var exp = StartupValuation.Evaluate(world, fa);
                var ask = Contracts.Negotiator.EffectiveAskingPurse(world, promo, fa, exp);
                if (ask.Amount > foundingCap * StartupAskCeiling) continue;

                // His asking purse (rounded UP to the $500 step), within what is
                // left of the division's budget.
                decimal purse = Up500(ask.Amount);
                if (purse > budget) continue;
                decimal signing = exp.WantsSigningBonus ? Up500(ask.Amount * 0.5m * Contracts.Negotiator.SigningBonusMinFraction) : 0m;
                if (signing > bonusPot) continue;
                var offer = new ContractOffer
                {
                    Fighter = fa.Id, Promotion = promo.Id,
                    PursePerFight = new Money(purse),
                    WinBonus = new Money(Math.Min(exp.AskingWinBonus.Amount, Up500(purse * 0.75m))),
                    SigningBonus = new Money(signing),
                    Fights = exp.DesiredFights,
                    Exclusive = Contracts.Negotiator.WantsExclusivity(promo),
                };
                if (StartupNegotiator.Evaluate(world, fa, offer, exp).Verdict != NegotiationVerdict.Accept) continue;
                bonusPot -= offer.SigningBonus.Amount;
                budget -= purse;
                division.Add((fa, offer));
            }
            if (division.Count >= StartupMinPerDivision) plan.AddRange(division);
        }

        var signed = new List<Fighter>();
        if (plan.Count < StartupMinRoster) return signed;
        foreach (var (fa, offer) in plan)
        {
            rng.NextULong();   // the founding path's per-signing draw, kept (see summary)
            Contracts.ContractLifecycle.Sign(world, fa, offer, date);
            signed.Add(fa);
        }
        return signed;
    }

    // --- helpers -------------------------------------------------------------

    private static decimal Up500(decimal amount) => Math.Ceiling(amount / 500m) * 500m;

    private static string Pretty(Region r) => r switch
    {
        Region.NorthAmerica => "North America",
        Region.SouthAmerica => "South America",
        Region.MiddleEast => "the Middle East",
        _ => r.ToString(),
    };

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
