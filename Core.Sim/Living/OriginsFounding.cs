using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Rng;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// New houses and scheduled arrivals in an Origins world (today's mode never comes here; its startups are
/// <see cref="WorldEvents"/>' and never run in an Origins world).
/// <list type="bullet">
/// <item>Founding: once the "rivals appear" chapter is live, a keyed monthly roll may open a new house in a
/// city with a live scene; when the chapter's time is up and nobody has, one opens anyway (the sport is
/// never three rooms for ever). After that the sport keeps growing at a modest rate (faster in the Boom),
/// up to a ceiling that rises with the era. A new house is of its era: open weight (or the weight lines
/// the sport has drawn), the era's rules, a card of single bouts, a small bank, and a roster of free agents
/// its own scouts rate, topped up from open tryouts of local men.</item>
/// <item>Arrivals (<see cref="OriginsArrivals"/>): the authored promotions, the first famous camp and the
/// later fighters join the world once their chapter is open and their own keyed month has come. The
/// women and the all-women's promotion wait for women's MMA (a later phase).</item>
/// </list>
/// Its own keyed streams (seed, date, salt); nothing on a shared stream.
/// </summary>
public static class OriginsFounding
{
    private const ulong FoundSalt = 0x0219_F0D0_0004UL;
    internal const ulong ArrivalSalt = 0x0219_A441_0005UL;

    /// <summary>A new house's monthly chance while the first rival is still to come.</summary>
    public const double FirstRivalChance = 0.06;

    /// <summary>A new house's monthly chance once new promotions are part of the sport (and in the Boom).</summary>
    public const double GrowthChance = 0.03, BoomChance = 0.10;

    /// <summary>
    /// The chapter that starts the Boom's wave of foundings: the breakout night. (The wave itself is what fires
    /// "boom-new-promotions", which needs fourteen houses alive: the ceiling must be above that BEFORE it fires.)
    /// </summary>
    public const string BoomStarts = "breakout-tv-moment";

    /// <summary>The most houses alive at once: three plus two an era, eighteen once the Boom has begun.</summary>
    public static int Ceiling(World world) =>
        world.Era.HasFired(BoomStarts) ? 18 : 3 + 2 * OriginsTimeline.CurrentEra(world);

    public static void RunMonthly(World world, DateOnly date)
    {
        bool rivalsLive = OriginsTimeline.IsLive(world, OriginsChapters.ByKey("rivals-appear"), date);
        if (!world.Era.IsUnlocked(Capability.NewPromotions) && !rivalsLive) return;
        if (world.Promotions.Values.Count(p => !p.Defunct) >= Ceiling(world)) return;
        double chance = rivalsLive ? FirstRivalChance
            : world.Era.HasFired(BoomStarts) ? BoomChance : GrowthChance;
        if (OriginsTimeline.Roll(world, FoundSalt, (ulong)date.DayNumber) >= chance) return;
        Found(world, date, null);
    }

    // ------------------------------------------------------------------ a new house

    private static readonly string[] Prefixes =
    {
        "Ironside", "Copperline", "Granite", "Longshot", "Crossroads", "Highline", "Lionheart", "Northstar",
        "Ridgeback", "Sandstorm", "Wildcard", "Bulldog", "Harbourside", "Foundry", "Lantern Street", "Old Mill",
    };
    private static readonly string[] Kinds =
    {
        "Fighting Championship", "Combat League", "Fight Night", "Challenge", "Open", "Fighting Association",
    };

    /// <summary>
    /// A new house's opening bank: <see cref="FoundingBankBase"/> and zero to <see cref="NewcomerBankSteps"/> steps
    /// of <see cref="FoundingBankStep"/> (<see cref="NewcomerBank"/>; one keyed draw for the AI, a fixed pick for the
    /// player): the money side's lever on how many new houses last.
    /// </summary>
    public const decimal FoundingBankBase = 50_000m, FoundingBankStep = 25_000m;

    /// <summary>The authored terms of an arriving promotion: its bank and prestige.</summary>
    private static readonly Dictionary<string, (decimal Bank, int Prestige)> Authored = new(StringComparer.Ordinal)
    {
        ["SGC"] = (1_500_000m, 16),   // the dome promotion: a concession king's money, a stadium's ambitions
        ["BFC"] = (1_200_000m, 12),   // the casino brothers: patient money, licensed from night one
    };

    /// <summary>
    /// Open a new house today (an authored arrival when <paramref name="arrival"/> is given). Null when no city
    /// with a live scene can take one. The house signs free agents its own scouts rate (home country first),
    /// tops up with tryouts of local men, and opens with a vacant belt in every division it runs.
    /// </summary>
    public static Promotion? Found(World world, DateOnly date, OriginsArrival? arrival)
    {
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed,
            (ulong)date.DayNumber ^ FoundSalt ^ (arrival is null ? 0UL : OriginsTimeline.KeyHash(arrival.Key))));

        // --- where -----------------------------------------------------------------
        City? city;
        if (arrival is not null)
            city = world.Cities.Values.Where(c => c.Name == arrival.City).OrderBy(c => c.Id.Value).FirstOrDefault();
        else
        {
            var options = FoundableCities(world, date);
            if (options.Count == 0) return null;
            Span<double> weights = stackalloc double[options.Count];
            for (int i = 0; i < options.Count; i++)
                weights[i] = Math.Max(1, options[i].Market) * Math.Max(1, options[i].MmaInterest - 20);
            city = options[rng.WeightedIndex(weights)];
        }
        if (city is null || world.Get(city.Country) is not { } country) return null;

        // --- who --------------------------------------------------------------------
        string name, abbr;
        if (arrival is not null) { name = arrival.Name; abbr = arrival.Abbreviation ?? Initials(arrival.Name); }
        else
        {
            name = rng.Chance(0.4) ? $"{city.Name} {rng.Pick(Kinds)}" : $"{rng.Pick(Prefixes)} {rng.Pick(Kinds)}";
            for (int guard = 0; world.Promotions.Values.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) && guard < 20; guard++)
                name = $"{rng.Pick(Prefixes)} {rng.Pick(Kinds)}";
            abbr = Initials(name);
        }
        for (int guard = 0; world.Promotions.Values.Any(p => string.Equals(p.Abbreviation, abbr, StringComparison.OrdinalIgnoreCase)) && guard < 60; guard++)
            abbr = abbr[..2] + (char)('A' + rng.Range(0, 25));

        var (bank, prestige) = arrival?.Abbreviation is { } ab && Authored.TryGetValue(ab, out var auth)
            ? auth : (NewcomerBank(rng.Range(0, NewcomerBankSteps)), rng.Range(4, 8));
        var (first, last) = Generation.NameData.Pool(country.Continent);
        string owner = arrival?.Lead ?? $"{rng.Pick(first)} {rng.Pick(last)}";

        var promo = new Promotion
        {
            Id = world.NewPromotionId(),
            Name = name,
            Abbreviation = abbr,
            BaseRegion = city.Region,
            Balance = new Money(bank),
            Prestige = prestige,
            Reputation = 50,
            HomeCity = city.Id,
            FoundedYear = date.Year,
            OwnerName = owner,
            OwnerNote = arrival?.Niche,
            Lore = arrival?.Lore,
        };
        promo.Rules.Fight = EraRules(world, rng, arrival is not null);
        promo.Rules.RoundMinutes = promo.Rules.Fight.RoundMinutes ?? 5;
        promo.Rules.StandardRounds = promo.Rules.Fight.Rounds ?? 3;
        promo.Rules.TitleRounds = promo.Rules.StandardRounds;
        OriginsReforms.FoundedUnder(world, promo);   // the unified code once there is one; a ring in the second centre
        promo.TierClearance = Org.OrgSizing.SizeOf(promo);
        world.Promotions[promo.Id] = promo;

        // Known at home, a little around its country; the lines the sport has drawn, or one open division.
        SeedPopularity(world, promo, city, prestige);
        OpenDivisions(world, promo, prestige, date, sizedToHouse: true);

        // --- its men ---------------------------------------------------------------------
        int signed = SignFoundingRoster(world, promo, country.Code, date, rng);
        Finance.Sponsorships.SeedFounding(world, promo, date, rng);
        RankingService.RecomputePromotion(world, promo, date);

        // The licensed house of the Boom opens with its paperwork done.
        if (arrival?.Abbreviation == "BFC")
            OriginsBacklash.LicenceAtFounding(world, promo, date);

        News.Post(world, date, NewsCategory.Finance,
            $"{name} opens in {city.Name}",
            arrival is not null
                ? $"{arrival.Niche} {owner} has signed {signed} men for the first night."
                : $"{owner} has opened a new promotion in {city.Name}, {country.Name}, with {signed} men signed and {Describe(promo)}.",
            promo.Id);
        return promo;
    }

    /// <summary>The house rules a promotion founded now starts with: the sport's rules once they are its own, the old ways before.</summary>
    private static FightRules EraRules(World world, SimRandom rng, bool authored)
    {
        bool rounds = authored || world.Era.IsUnlocked(Capability.Rounds);
        bool gloves = authored || world.Era.IsUnlocked(Capability.Gloves);
        // The draws stay in this order (the round's length, the gloves, the enclosure), each only when it is the house's choice.
        int minutes = rounds ? 5 : 15 + 5 * rng.Range(0, 1);
        var glove = gloves ? GloveType.ThinOpenFinger : rng.Chance(0.5) ? GloveType.Optional : GloveType.Bare;
        var enclosure = rng.Chance(0.5) ? Enclosure.Ring : Enclosure.Cage;
        return NewcomerRules(rounds, minutes, glove, enclosure);
    }

    /// <summary>
    /// A new house's fight rules from its choices: three timed rounds of five minutes with judges once the sport
    /// fights that way, else one long round and no judges; its gloves; its enclosure; open weight (a house opened
    /// after the sport has drawn weight lines takes them in <see cref="OpenDivisions"/>).
    /// </summary>
    internal static FightRules NewcomerRules(bool rounds, int singleRoundMinutes, GloveType gloves, Enclosure enclosure) => new()
    {
        Rounds = rounds ? 3 : 1,
        RoundMinutes = rounds ? 5 : singleRoundMinutes,
        Judges = rounds,
        Gloves = gloves,
        OpenWeight = true,
        Enclosure = enclosure,
    };

    /// <summary>What a single long round runs before the sport has rounds (the AI rolls one; the player picks).</summary>
    public static IReadOnlyList<int> SingleRoundMinutes { get; } = new[] { 15, 20 };

    /// <summary>
    /// The cities a new house may open in today, in id order: its country has a live scene, the Backlash has not
    /// closed its halls, and no live house already calls it home.
    /// </summary>
    internal static List<City> FoundableCities(World world, DateOnly date)
    {
        var taken = TakenHomes(world);
        var scenes = Era.Scenes(world).ToHashSet(StringComparer.Ordinal);
        return world.Cities.Values
            .Where(c => !taken.Contains(c.Id) && world.Get(c.Country) is { } k && scenes.Contains(k.Code) && !Era.Banned(world, c.Id, date))
            .OrderBy(c => c.Id.Value).ToList();
    }

    /// <summary>The home cities of the live houses.</summary>
    internal static HashSet<CityId> TakenHomes(World world) =>
        world.Promotions.Values.Where(p => !p.Defunct && p.HomeCity is not null).Select(p => p.HomeCity!.Value).ToHashSet();

    /// <summary>How many steps an AI newcomer's opening bank rolls over (zero to this many).</summary>
    public const int NewcomerBankSteps = 6;

    /// <summary>A newcomer's opening bank after <paramref name="steps"/> steps (the AI rolls them; the player gets a fixed pick).</summary>
    public static decimal NewcomerBank(int steps) => FoundingBankBase + steps * FoundingBankStep;

    /// <summary>Known at home, a little around its country.</summary>
    internal static void SeedPopularity(World world, Promotion promo, City city, int prestige)
    {
        foreach (var c in world.Cities.Values.OrderBy(c => c.Id.Value))
        {
            int value = c.Id == city.Id ? prestige + 10 : c.Country == city.Country ? prestige * 3 / 5 : 0;
            if (value > 0) promo.PopularityByCity[c.Id] = Math.Clamp(value, 0, 100);
        }
    }

    /// <summary>The men's weight lines the sport has drawn (none before the first split).</summary>
    internal static List<WeightClass> DrawnLines(World world) =>
        Enum.GetValues<WeightClass>()
            .Where(wc => wc != WeightClass.Strawweight && world.Era.IsUnlocked(Capability.Class(wc))).ToList();

    /// <summary>
    /// A new house's divisions: the lines the sport has drawn, each with a vacant belt, else one open division with a
    /// vacant belt. <paramref name="sizedToHouse"/> (an AI house): only as many of the lines as its purse can staff
    /// (<see cref="OriginsReforms.DivisionsItCanStaff"/>), whole ladder chapters in the order the sport drew them
    /// (heavy and light first); the rest it draws later, when its gate grows (<see cref="OriginsReforms.RunMonthly"/>).
    /// The player's house opens the lines he chose (<paramref name="chosen"/>, of the drawn lines; PlayerFounding
    /// validates and defaults them to the same staffing rule).
    /// </summary>
    internal static void OpenDivisions(World world, Promotion promo, int prestige, DateOnly date, bool sizedToHouse = false,
        IReadOnlyCollection<WeightClass>? chosen = null)
    {
        var lines = DrawnLines(world);
        if (sizedToHouse && lines.Count > 0) lines = OriginsReforms.LinesItCanStaff(world, promo, lines);
        if (chosen is not null && lines.Count > 0) lines = lines.Where(chosen.Contains).ToList();
        if (lines.Count > 0) OriginsReforms.DrawLines(world, promo, lines, date, announce: false);
        else
        {
            var title = new Title
            {
                Id = world.NewTitleId(), Promotion = promo.Id, WeightClass = Division.OpenClass,
                Name = $"{promo.Abbreviation} Open-weight Championship", Prestige = prestige,
            };
            world.Titles[title.Id] = title;
            promo.Titles.Add(title.Id);
            promo.Divisions.Add(new Division { WeightClass = Division.OpenClass, Sex = Sex.Male, Open = true, Title = title.Id });
        }
    }

    internal static string Describe(Promotion p) =>
        p.Divisions.Any(d => d.Open) ? "a vacant open-weight belt to fight for"
        : $"{p.Divisions.Count} weight divisions, each with a vacant belt";

    internal static string Initials(string name)
    {
        var letters = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])).ToList();
        while (letters.Count < 3) letters.Add(char.ToUpperInvariant(name.Where(char.IsLetter).Skip(letters.Count).FirstOrDefault('X')));
        return new string(letters.Take(3).ToArray());
    }

    /// <summary>
    /// The founding roster: the free agents its own scouts rate best (home country first) at their asking
    /// terms at this house (<see cref="Negotiator.TableAsk"/>'s price) while they fit its purse budget, then open
    /// tryouts of young local men (on the Origins floor purse, untouched by the tier) until it can fill a night.
    /// </summary>
    private static int SignFoundingRoster(World world, Promotion promo, string countryCode, DateOnly date, SimRandom rng)
    {
        int target = OriginsShows.MinRoster(promo) + 2;
        decimal cap = Negotiator.PurseCap(world, promo);
        var valuation = new FighterValuation();
        int signed = 0;
        var pool = world.FreeAgents()
            .Where(f => f.Status == FighterStatus.Active && f.Sex == Sex.Male && f.IsMatchReady(date) && promo.Divisions.Any(d => d.Matches(f)))
            .Select(f => (F: f, Seen: ScoutingService.PerceivedAbility(world, promo, f)))
            .OrderBy(x => x.F.OriginCountryCode == countryCode ? 0 : 1)
            .ThenByDescending(x => x.Seen).ThenBy(x => x.F.Id.Value)
            .Select(x => x.F).ToList();
        bool exclusive = Negotiator.WantsExclusivity(promo);
        OriginsMarket? read = null;
        foreach (var f in pool)
        {
            if (signed >= target) break;
            // His asking terms AT THIS HOUSE: his open-market ask on its terms of exclusivity times the table factor
            // (Negotiator.TableAsk: the size of the house, HouseTier, or his personal discount), rounded up to the
            // Origins step, as the AI's opening offer pitches it. Signed at the open-market ask, a newly founded small
            // house priced its first men off the giant's purse and filled on day one only with men under its cap there.
            // (One market read while the loop only reads, dropped at every signing: OriginsMarket.)
            var exp = valuation.EvaluateWith(world, f, ref read);
            decimal factor = (exclusive ? 1m : Negotiator.NonExclusiveMoneyFactor) * Negotiator.TableFactor(world, f, promo.Id, exp, read);
            decimal purse = NegotiationSession.UpToStep(world, exp.AskingPurse.Amount * factor);
            if (purse > cap) continue;
            ContractLifecycle.Sign(world, f, new ContractOffer
            {
                Fighter = f.Id, Promotion = promo.Id, PursePerFight = new Money(purse),
                WinBonus = new Money(NegotiationSession.UpToStep(world, exp.AskingWinBonus.Amount * factor)),
                Fights = exp.DesiredFights, Exclusive = exclusive,
            }, date);
            read = null;
            signed++;
        }

        // Open tryouts: local men who have trained for this, green and glad of the chance.
        var gen = FighterGenerator.ForWorld(world);
        for (int guard = 0; signed < target && guard < target; guard++)
            if (TryoutMan(world, gen, promo, countryCode, date, rng) is not null) signed++;
        return signed;
    }

    /// <summary>
    /// One man from an open tryout of local men, green and glad of the chance, the house his discoverer: signed on
    /// the tryout terms (the Origins purse floor, three fights; exclusive unless <paramref name="exclusive"/> says
    /// otherwise) when the house has a division for him, else null (he walks back out a free agent). Two draws on
    /// <paramref name="rng"/>: his age, then his level.
    /// </summary>
    internal static Fighter? TryoutMan(World world, FighterGenerator gen, Promotion promo, string countryCode, DateOnly date, SimRandom rng,
        bool exclusive = true)
    {
        var f = OriginsTalent.Create(world, gen, date, countryCode, rng.Range(20, 27), rng.GaussianInt(42, 6, 30, 56));
        OriginsTalent.FindGym(world, f);
        f.DiscoveredBy = promo.Id;
        if (!promo.Divisions.Any(d => d.Matches(f))) return null;   // a man the house has no division for walks back out
        ContractLifecycle.Sign(world, f, new ContractOffer
        {
            Fighter = f.Id, Promotion = promo.Id,
            PursePerFight = new Money(Negotiator.OriginsPurseFloor), WinBonus = new Money(Negotiator.OriginsPurseFloor / 2),
            Fights = 3, Exclusive = exclusive,
        }, date);
        return f;
    }

    // ------------------------------------------------------------------ a small house's open tryout

    private const ulong TryoutSalt = 0x0219_7A10_0007UL;

    /// <summary>An AI house holds an open tryout at most this often (days).</summary>
    public const int TryoutEveryDays = 182;   // (once a year left 6 of 40 seeds with a house at 7-9 men between tryouts: longsim origins 40 20)

    /// <summary>The ledger line that marks an AI house's open tryout (the durable record of when it last held one).</summary>
    public const string TryoutLedgerLine = "Open tryout";

    /// <summary>
    /// The month's open tryouts (Origins, AI houses; after the month's free-agent pass): a house with fewer men who
    /// could fight its next night than that night needs (<see cref="OriginsShows.MinRoster"/>; counted by
    /// <see cref="NightReady"/>: men the house could book a lead window from now, so the hurt and the suspended do not
    /// count) and fewer than its founding roster on the books (<see cref="OnTheBooks"/>) after the market has had its
    /// turn calls in local men, as it did the month it opened, and signs them on the tryout terms (<see cref="TryoutMan"/>)
    /// until its solvent signing target (<see cref="OriginsShows.RosterDemand"/>, also for a house in the red) is on the
    /// books, at most twice a year. This is how a small hall
    /// whose purse no free agent will take (the Boom's 500-a-purse houses, while every man left on the market asks 750 or
    /// more) keeps putting on its nights. A house in the red holds one too: a tryout costs nothing up front, and a
    /// distressed house's offers (a fraction of its purse cap) reach nobody on the market, so without it a house in debt
    /// could never stage the nights that would pay it back. The men sign on the house's own terms of exclusivity
    /// (<see cref="Negotiator.WantsExclusivity"/>: a small hall deals non-exclusive, no monthly retainer), not the
    /// founding tryout's. Its own keyed stream (seed, day, house).
    /// The player's house is never touched: he has the market, his free tryout, and this same rule as an option he calls
    /// himself (<see cref="PlayerFounding.TryoutOptions"/>, <see cref="PlayerFounding.HoldTryout"/>).
    /// </summary>
    public static void RunTryouts(World world, DateOnly date)
    {
        FighterGenerator? gen = null;
        var probe = TickProbe.Current;   // (reporting only: longsim origins' staffing counts; never read by the sim)
        foreach (var promo in world.Promotions.Values
                     .Where(p => !p.Defunct && p.Id != world.PlayerPromotion)
                     .OrderBy(p => p.Id.Value).ToList())
        {
            // Short when too few men could fight its next night AND it holds less than its founding roster: a full
            // roster with men hurt waits for them to heal (a tryout then only swelled the sport: 39 of 40 seeds over
            // the actives band at 12 years). It signs up to its solvent signing target (OriginsShows.RosterDemand;
            // filling only to the founding roster left houses at 8-9 men before the half-year was up). A house in the
            // red fills to that solvent target too, not the smaller distressed one the free-agent pass aims for: the
            // tryout men cost nothing up front and sign on the floor purse, and the rule as written measured healthy
            // (longsim origins 40 12 and 40 20, all seeds).
            if (!ShortHanded(world, promo, date, out int onBooks))
            {
                if (probe is not null && onBooks < TryoutTarget(promo)) probe.Count($"staff|tryout|{promo.Id.Value}|under-target-not-short");
                continue;
            }
            int target = TryoutTarget(promo);
            if (promo.Ledger.Any(e => e.Category == FinanceCategory.Other && e.Description == TryoutLedgerLine
                                      && date.DayNumber - e.Date.DayNumber < TryoutEveryDays))
            {
                probe?.Count($"staff|tryout|{promo.Id.Value}|short-spacing-blocked");
                continue;
            }
            probe?.Count($"staff|tryout|{promo.Id.Value}|held");
            var home = promo.HomeCity is { } hc ? world.Get(hc) : null;
            if (home is null || world.Get(home.Country) is not { } country) continue;

            var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)date.DayNumber ^ TryoutSalt ^ ((ulong)promo.Id.Value << 32)));
            gen ??= FighterGenerator.ForWorld(world);
            int signed = CallInLocals(world, gen, promo, country.Code, date, rng, onBooks, target).Count;
            Finance.Ledger.Record(promo, date, FinanceCategory.Other, new Money(0m), TryoutLedgerLine);
            if (signed == 0) continue;
            RankingService.RecomputePromotion(world, promo, date);
            News.Post(world, date, NewsCategory.General, $"{promo.Name} holds an open tryout",
                $"Short of men and with no purse the market will take, {promo.Abbreviation} called in the local hopefuls: {signed} signed on the tryout terms.",
                promo.Id);
        }
    }

    /// <summary>
    /// Is the house short of men, as an open tryout means it (the one rule for an AI house's tryout and the player's
    /// recurring one): fewer men who could fight its next night than that night needs (<see cref="NightReady"/> under
    /// <see cref="OriginsShows.MinRoster"/>) AND fewer than its founding roster on the books (<see cref="OnTheBooks"/>
    /// under MinRoster + 2). <paramref name="onBooks"/> is the second count.
    /// </summary>
    internal static bool ShortHanded(World world, Promotion promo, DateOnly date, out int onBooks)
    {
        onBooks = OnTheBooks(world, promo);
        return NightReady(world, promo, date) < OriginsShows.MinRoster(promo) && onBooks < OriginsShows.MinRoster(promo) + 2;
    }

    /// <summary>The men on the books a tryout fills the house to: its solvent signing target, at least its founding roster.</summary>
    internal static int TryoutTarget(Promotion promo) => Math.Max(OriginsShows.MinRoster(promo) + 2, OriginsShows.RosterDemand(promo));

    /// <summary>
    /// The open tryout itself: local men of <paramref name="countryCode"/> called in and signed on the tryout terms
    /// (<see cref="TryoutMan"/>, the house's own terms of exclusivity, <see cref="Negotiator.WantsExclusivity"/>) until
    /// <paramref name="target"/> men are on the books, at most twice that many tries; each signed man's accolade names the
    /// tryout (<see cref="OriginsTalent.TryoutDeduction"/> finds them by it). Returns the men signed.
    /// </summary>
    internal static List<Fighter> CallInLocals(World world, FighterGenerator gen, Promotion promo, string countryCode, DateOnly date,
        SimRandom rng, int onBooks, int target)
    {
        var signed = new List<Fighter>();
        for (int guard = 0; onBooks + signed.Count < target && guard < target * 2; guard++)
            if (TryoutMan(world, gen, promo, countryCode, date, rng, Negotiator.WantsExclusivity(promo)) is { } man)
            {
                man.Accolades.Add(new Accolade(date, $"Signed at {promo.Name}'s open tryout"));
                signed.Add(man);
            }
        return signed;
    }

    /// <summary>
    /// The house's men who could fight the next night it can put on the books (<see cref="OriginsShows.LeadWeeks"/>
    /// from <paramref name="date"/>): the one bookability rule its nights use (<see cref="OriginsShows.CanBook"/>:
    /// active, fit by then, a fight left on his deal, deal not run out).
    /// </summary>
    internal static int NightReady(World world, Promotion promo, DateOnly date)
    {
        var night = date.AddDays(7 * OriginsShows.LeadWeeks);
        return world.RosterOf(promo.Id).Count(f => OriginsShows.CanBook(world, promo, f, night));
    }

    /// <summary>The house's men on the books: not retired, deal not run out (hurt or not).</summary>
    internal static int OnTheBooks(World world, Promotion promo) =>
        world.RosterOf(promo.Id).Count(f => f.Status != FighterStatus.Retired && !DealWindow.OutOfDeal(world, f));

    // ------------------------------------------------------------------ scheduled arrivals

    /// <summary>
    /// The authored arrivals whose time has come: each joins once its chapter is open (a promotion or a camp
    /// can be what MAKES its chapter happen; a fighter waits for it to have happened), its country's scene
    /// is awake, and its own keyed month inside its window has come. Women wait for women's MMA (a later phase).
    /// </summary>
    public static void RunArrivals(World world, DateOnly date)
    {
        int month = OriginsTimeline.Month(world, date);
        foreach (var a in OriginsArrivals.All)
        {
            if (Arrived(world, a) || month < DueMonth(world, a)) continue;
            if (a.Pioneer?.Fighter.Sex == Sex.Female || a.Chapter.StartsWith("womens-", StringComparison.Ordinal)) continue;
            bool fired = world.Era.HasFired(a.Chapter);
            bool makesIt = a.Kind != OriginsArrivalKind.Fighter && OriginsTimeline.IsLive(world, OriginsChapters.ByKey(a.Chapter), date);
            if (!fired && !makesIt) continue;
            if (!Era.AllowsScene(world, a.Country)) continue;
            Arrive(world, a, date);
        }
    }

    /// <summary>An arrival's keyed month: somewhere in the first half of its window.</summary>
    public static int DueMonth(World world, OriginsArrival a)
    {
        int span = Math.Max(1, (a.LatestMonth - a.EarliestMonth) / 2);
        return a.EarliestMonth + (int)(OriginsTimeline.Roll(world, ArrivalSalt, OriginsTimeline.KeyHash(a.Key)) * span);
    }

    /// <summary>Is this arrival already in the world (by its name)?</summary>
    public static bool Arrived(World world, OriginsArrival a) => a.Kind switch
    {
        OriginsArrivalKind.Promotion => world.Promotions.Values.Any(p => p.Name == a.Name),
        OriginsArrivalKind.Camp => world.Camps.Values.Any(c => c.Name == a.Name),
        _ => a.Pioneer is { } row && world.Fighters.Values.Any(f => f.Designed && f.FirstName == row.Fighter.First && f.LastName == row.Fighter.Last),
    };

    internal static void Arrive(World world, OriginsArrival a, DateOnly date)
    {
        var city = world.Cities.Values.Where(c => c.Name == a.City).OrderBy(c => c.Id.Value).FirstOrDefault();
        if (city is null) return;
        switch (a.Kind)
        {
            case OriginsArrivalKind.Promotion:
                Found(world, date, a);
                break;
            case OriginsArrivalKind.Camp:
            {
                var camp = new Camp
                {
                    Id = world.NewCampId(), Name = a.Name, City = city.Id, Specialty = PrimaryStyle.WellRounded,
                    CoachQuality = 76, Reputation = 56, Capacity = 30, MonthlyFee = new Money(600m), Lore = a.Lore,
                };
                world.Camps[camp.Id] = camp;
                News.Post(world, date, NewsCategory.General, $"{a.Name} opens its doors in {city.Name}",
                    $"{a.Niche} {a.Lead} teaches striking, takedowns and submissions in one room, and the waiting list is already long.", default);
                break;
            }
            case OriginsArrivalKind.Fighter when a.Pioneer is { } row:
            {
                var f = OriginsWorldBuilder.CreatePioneer(world, FighterGenerator.ForWorld(world), row, date);
                var room = world.Camps.Values.Where(c => c.City == city.Id).OrderByDescending(c => c.Reputation).ThenBy(c => c.Id.Value).FirstOrDefault();
                if (room is not null) f.Camp = room.Id; else OriginsTalent.FindGym(world, f);
                // He signs where his story puts him, when that house is an AI house still running; else he is a free agent.
                var house = row.Fighter.Org is { } org
                    ? world.Promotions.Values.FirstOrDefault(p => !p.Defunct && p.Abbreviation == org && p.Id != world.PlayerPromotion && p.Divisions.Any(d => d.Matches(f)))
                    : null;
                if (house is not null)
                {
                    var rng = new SimRandom(SimRandom.Mix(world.RngSeed, OriginsTimeline.KeyHash(a.Key) ^ ArrivalSalt));
                    f.Contract = PromotionSetup.SignContract(world, house, date, f, rng,
                        ScoutingService.PerceivedAbility(world, house, f));   // priced on the house's read (no draw)
                    if (f.Contract.PursePerFight.Amount < Negotiator.OriginsPurseFloor)
                    {
                        f.Contract.PursePerFight = new Money(Negotiator.OriginsPurseFloor);
                        f.Contract.WinBonus = new Money(Negotiator.OriginsPurseFloor / 2);
                    }
                    // (A bracket house pays no win bonuses: the cheque is the bonus. The signing's draws are kept.)
                    f.Contract.WinBonus = Negotiator.WinBonusAt(world, house.Id, f.Contract.WinBonus);
                    RankingService.RecomputePromotion(world, house, date);
                }
                News.Post(world, date, NewsCategory.Debut, $"{f.FullName} turns professional",
                    a.Pioneer.Hook + (house is null ? " He is a free agent." : $" He signs with {house.Name}."), house?.Id ?? default, f.Id);
                break;
            }
        }
    }
}
