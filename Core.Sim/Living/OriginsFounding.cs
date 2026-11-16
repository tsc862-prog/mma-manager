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
    private const ulong FoundSalt = 0x0219_F0D0_0004UL, ArrivalSalt = 0x0219_A441_0005UL;

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
            var taken = world.Promotions.Values.Where(p => !p.Defunct && p.HomeCity is not null).Select(p => p.HomeCity!.Value).ToHashSet();
            var scenes = Era.Scenes(world).ToHashSet(StringComparer.Ordinal);
            var options = world.Cities.Values
                .Where(c => !taken.Contains(c.Id) && world.Get(c.Country) is { } k && scenes.Contains(k.Code) && !Era.Banned(world, c.Id, date))
                .OrderBy(c => c.Id.Value).ToList();
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
            for (int guard = 0; world.Promotions.Values.Any(p => p.Name == name) && guard < 20; guard++)
                name = $"{rng.Pick(Prefixes)} {rng.Pick(Kinds)}";
            abbr = Initials(name);
        }
        for (int guard = 0; world.Promotions.Values.Any(p => p.Abbreviation == abbr) && guard < 60; guard++)
            abbr = abbr[..2] + (char)('A' + rng.Range(0, 25));

        var (bank, prestige) = arrival?.Abbreviation is { } ab && Authored.TryGetValue(ab, out var auth)
            ? auth : (150_000m + rng.Range(0, 6) * 25_000m, rng.Range(4, 8));
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
        promo.TierClearance = Org.OrgSizing.SizeOf(promo);
        world.Promotions[promo.Id] = promo;

        // Known at home, a little around its country.
        foreach (var c in world.Cities.Values.OrderBy(c => c.Id.Value))
        {
            int value = c.Id == city.Id ? prestige + 10 : c.Country == city.Country ? prestige * 3 / 5 : 0;
            if (value > 0) promo.PopularityByCity[c.Id] = Math.Clamp(value, 0, 100);
        }

        // --- its divisions: the lines the sport has drawn, or one open division --------
        var lines = Enum.GetValues<WeightClass>()
            .Where(wc => wc != WeightClass.Strawweight && world.Era.IsUnlocked(Capability.Class(wc))).ToList();
        if (lines.Count > 0) OriginsReforms.DrawLines(world, promo, lines, date, announce: false);
        else
        {
            var title = new Title
            {
                Id = world.NewTitleId(), Promotion = promo.Id, WeightClass = Division.OpenClass,
                Name = $"{abbr} Open-weight Championship", Prestige = prestige,
            };
            world.Titles[title.Id] = title;
            promo.Titles.Add(title.Id);
            promo.Divisions.Add(new Division { WeightClass = Division.OpenClass, Sex = Sex.Male, Open = true, Title = title.Id });
        }

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
        return new FightRules
        {
            Rounds = rounds ? 3 : 1,
            RoundMinutes = rounds ? 5 : 15 + 5 * rng.Range(0, 1),
            Judges = rounds,
            Gloves = gloves ? GloveType.ThinOpenFinger : rng.Chance(0.5) ? GloveType.Optional : GloveType.Bare,
            OpenWeight = true,
            Enclosure = rng.Chance(0.5) ? Enclosure.Ring : Enclosure.Cage,
        };
    }

    private static string Describe(Promotion p) =>
        p.Divisions.Any(d => d.Open) ? "a vacant open-weight belt to fight for"
        : $"{p.Divisions.Count} weight divisions, each with a vacant belt";

    private static string Initials(string name)
    {
        var letters = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])).ToList();
        while (letters.Count < 3) letters.Add(char.ToUpperInvariant(name.Where(char.IsLetter).Skip(letters.Count).FirstOrDefault('X')));
        return new string(letters.Take(3).ToArray());
    }

    /// <summary>
    /// The founding roster: the free agents its own scouts rate best (home country first) at their asking
    /// terms while they fit its purse budget, then open tryouts of young local men until it can fill a night.
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
        foreach (var f in pool)
        {
            if (signed >= target) break;
            var exp = valuation.Evaluate(world, f);
            if (exp.AskingPurse.Amount > cap) continue;
            ContractLifecycle.Sign(world, f, new ContractOffer
            {
                Fighter = f.Id, Promotion = promo.Id, PursePerFight = exp.AskingPurse,
                WinBonus = exp.AskingWinBonus, Fights = exp.DesiredFights, Exclusive = Negotiator.WantsExclusivity(promo),
            }, date);
            signed++;
        }

        // Open tryouts: local men who have trained for this, green and glad of the chance.
        var gen = FighterGenerator.ForWorld(world);
        for (int guard = 0; signed < target && guard < target; guard++)
        {
            var f = OriginsTalent.Create(world, gen, date, countryCode, rng.Range(20, 27), rng.GaussianInt(42, 6, 30, 56));
            OriginsTalent.FindGym(world, f);
            f.DiscoveredBy = promo.Id;
            if (!promo.Divisions.Any(d => d.Matches(f))) continue;   // a man the house has no division for walks back out
            ContractLifecycle.Sign(world, f, new ContractOffer
            {
                Fighter = f.Id, Promotion = promo.Id,
                PursePerFight = new Money(Negotiator.OriginsPurseFloor), WinBonus = new Money(Negotiator.OriginsPurseFloor / 2),
                Fights = 3, Exclusive = true,
            }, date);
            signed++;
        }
        return signed;
    }

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

    private static void Arrive(World world, OriginsArrival a, DateOnly date)
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
                    f.Contract = PromotionSetup.SignContract(world, house, date, f, rng);
                    if (f.Contract.PursePerFight.Amount < Negotiator.OriginsPurseFloor)
                    {
                        f.Contract.PursePerFight = new Money(Negotiator.OriginsPurseFloor);
                        f.Contract.WinBonus = new Money(Negotiator.OriginsPurseFloor / 2);
                    }
                    RankingService.RecomputePromotion(world, house, date);
                }
                News.Post(world, date, NewsCategory.Debut, $"{f.FullName} turns professional",
                    a.Pioneer.Hook + (house is null ? " He is a free agent." : $" He signs with {house.Name}."), house?.Id ?? default, f.Id);
                break;
            }
        }
    }
}
