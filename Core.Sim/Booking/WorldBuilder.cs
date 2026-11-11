using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// Builds a full living world: several promotions of descending prestige, the
/// talent pool tiered across them (the best fighters sign with the biggest
/// promotion), plus a pool of unsigned free agents. This is the "new game" seed
/// for the world tick. Deterministic from the world seed.
/// The player either controls one of the established promotions
/// (<see cref="Options.PlayerPromotionIndex"/>) or founds a brand-new one below
/// them all (<see cref="Options.Founding"/>) — the zero-to-hero start.
/// </summary>
public sealed class WorldBuilder
{
    /// <summary>
    /// A designed promotion. <paramref name="Divisions"/> is its NICHE — the weight
    /// classes it actually runs (null = the full slate): a Japanese lighter-weights
    /// house, a Baltic heavyweight hall, a three-division regional. <paramref name="Women"/>
    /// says whether it runs women's divisions (null = legacy rule: prestige ≥ 32).
    /// Niches concentrate an org's roster into fewer, deeper divisions and give the
    /// world texture — a heavyweight has nowhere to go at a flyweight house.
    /// </summary>
    public sealed record PromotionSpec(string Name, string Abbreviation, int Prestige, int RosterSize, Money Balance, Region Region,
        WeightClass[]? Divisions = null, bool? Women = null,
        string? Lore = null,    // authored identity prose shown on the Org page
        string? HomeCity = null);   // the atlas city it calls home; null (or not in its region) = the region's fight capital

    /// <summary>
    /// The player's own brand-new promotion: identity, home region, and how much
    /// of a shoestring it starts on. A founded promotion starts with an EMPTY
    /// roster and vacant belts — the player hires every fighter themselves from
    /// the free-agent market, and the first title fights crown the champions.
    /// </summary>
    public sealed record FoundingSpec
    {
        public string Name { get; init; } = "New Wave Fighting";
        public string Abbreviation { get; init; } = "NWF";
        public Region Region { get; init; } = Region.NorthAmerica;

        /// <summary>
        /// The town the promotion calls home — where its fans are, where it draws
        /// best, and the default stop on every card. Null (or a name that isn't in
        /// the region) falls back to the region's fight capital, which is what
        /// every founding did before the choice existed.
        /// </summary>
        public string? HomeCityName { get; init; }
        // A true shoestring — a rock-bottom City-tier unknown. This is the practical
        // FLOOR: below ~5 the projected gate can't cover professional purses, so the
        // promotion can't field a roster or run a card, and it dies without ever
        // getting off the ground. From 5 it can just scrape together shows and begin
        // the years-long climb.
        public int Prestige { get; init; } = 5;
        public Money StartingBalance { get; init; } = new(250_000m);

        /// <summary>
        /// Extra fighters generated UNSIGNED for the founding org to hire —
        /// targeted at the divisions it actually runs (women's included), a mix
        /// of raw prospects and affordable journeymen, biased toward the home
        /// region so the early cards have local names. (Was 18 generic-pool
        /// fighters spread over every weight class — the hiring screen felt
        /// empty because most landed in divisions the org didn't run.)
        /// </summary>
        public int TalentPool { get; init; } = 90;

        /// <summary>Weight classes the new promotion runs; null => the three-division core below.</summary>
        public WeightClass[]? Divisions { get; init; }

        public static WeightClass[] DefaultDivisions { get; } =
            { WeightClass.Lightweight, WeightClass.Welterweight, WeightClass.Middleweight };
    }

    public sealed record Options
    {
        public DateOnly StartDate { get; init; } = new(2026, 1, 1);
        public ulong Seed { get; init; } = 1;
        // A bench of unsigned talent for the bigger world (and a founding org to
        // hire from). Deliberately BIMODAL, like a real open market: the tier
        // sort's leftover veterans below, plus a reserved green-intake slice
        // (young, a handful of fights, real upside) the AI orgs passed on for
        // being unproven — not for being bad.
        public int FreeAgentPool { get; init; } = 140;
        internal int GreenIntake => FreeAgentPool * 2 / 5;
        public int RankingsDepth { get; init; } = 15;

        /// <summary>
        /// Seed the world's headliners from the hand-designed initial database
        /// (<see cref="Generation.DesignedRoster"/>): every champion, the AFC's
        /// top contenders, and a few free agents with hooks. Depth talent and all
        /// newgens stay procedural. Designed org tags match promotions by
        /// abbreviation, so custom <see cref="Promotions"/> lists simply leave
        /// unmatched designed fighters on the open market.
        /// </summary>
        public bool UseDesignedRoster { get; init; } = true;

        /// <summary>Which promotion (index into <see cref="Promotions"/>) the player controls.</summary>
        public int PlayerPromotionIndex { get; init; }

        /// <summary>
        /// When set, the player founds this brand-new promotion instead of taking
        /// over an established one; <see cref="PlayerPromotionIndex"/> is ignored.
        /// Its roster signs from whatever free agents are left once the AI
        /// promotions have tiered off the best talent.
        /// </summary>
        public FoundingSpec? Founding { get; init; }

        /// <summary>Promotions from biggest to smallest; the best talent tiers into the top of the list.</summary>
        public PromotionSpec[]? Promotions { get; init; }

        /// <summary>
        /// Portrait-first generation: when supplied (the UI builds it from the art
        /// pack manifest), generated fighters are built to match a real portrait
        /// identity — culture, nationality, style. Null => procedural generation.
        /// </summary>
        public Generation.IdentityCatalog? IdentityCatalog { get; init; }

        /// <summary>Include women's divisions (the lighter classes) on the promotions that run them.</summary>
        public bool IncludeWomensDivisions { get; init; } = true;

        /// <summary>
        /// Turn every fighter's generated tally into real back-dated events —
        /// full fight histories, résumé-fed day-one rankings, pre-save title
        /// lineages, and event numbering that continues each promotion's past
        /// (see <see cref="Generation.HistoryBuilder"/>).
        /// </summary>
        public bool GenerateHistory { get; init; } = true;

        // --- The niche profiles the designed world is built from -------------------
        // (Each org's designed headliners fit inside its niche — checked by test.)
        public static readonly WeightClass[] LighterHouse =   // the Shooto-tradition house
            { WeightClass.Strawweight, WeightClass.Flyweight, WeightClass.Bantamweight, WeightClass.Featherweight, WeightClass.Lightweight };
        public static readonly WeightClass[] FeederLight =
            { WeightClass.Flyweight, WeightClass.Bantamweight, WeightClass.Featherweight, WeightClass.Lightweight };
        public static readonly WeightClass[] TinyLight =
            { WeightClass.Flyweight, WeightClass.Bantamweight, WeightClass.Featherweight };
        public static readonly WeightClass[] HeavyHall =      // big-man fights only
            { WeightClass.Middleweight, WeightClass.LightHeavyweight, WeightClass.Heavyweight };
        public static readonly WeightClass[] BigFive =
            { WeightClass.Lightweight, WeightClass.Welterweight, WeightClass.Middleweight, WeightClass.LightHeavyweight, WeightClass.Heavyweight };
        public static readonly WeightClass[] MidSix =
            { WeightClass.Featherweight, WeightClass.Lightweight, WeightClass.Welterweight, WeightClass.Middleweight, WeightClass.LightHeavyweight, WeightClass.Heavyweight };
        public static readonly WeightClass[] CoreThree =
            { WeightClass.Lightweight, WeightClass.Welterweight, WeightClass.Middleweight };
        public static readonly WeightClass[] MidFour =
            { WeightClass.Lightweight, WeightClass.Welterweight, WeightClass.Middleweight, WeightClass.LightHeavyweight };

        private static readonly PromotionSpec[] DesignedSpecs =
        {
            // The world ladder, biggest to smallest — talent tiers into the top. Two
            // fight-mad regions run head to head: VMMA vs IFA in Europe and CK vs RCS
            // in Brazil — turf wars the market can only just support. Every org has a
            // NICHE: the full-service giants run everything; the rest pick a lane —
            // lighter weights, big men, a tight core — and some run women, some don't.
            // LORE is the authored identity shown on the Org page (Biographer leads
            // with it, then appends the live facts).
            // Roster sizes for the National+ orgs (exclusive contracts, real payroll)
            // sit AT their tier roster target (RosterTargetPerDivision × male
            // divisions, small slack at the summit) — the designed start must not
            // open over-target, hoard-taxed and committed beyond its own economics.
            // The feeders below sign non-exclusive, so their looser sizes are free.
            new("Apex Fighting Championship", "AFC", 74, 230, new Money(12_000_000m), Region.NorthAmerica, Women: true,
                Lore: "Born in a Las Vegas casino ballroom back in the sport's outlaw days, the Apex Fighting Championship outgrew, outbought and outlasted every rival to become the summit of mixed martial arts. An AFC belt is the sport's only universal currency — every champion on earth is measured by whether they could hold one here."),
            new("Vanguard MMA", "VMMA", 58, 190, new Money(6_000_000m), Region.Europe, Women: true,
                Lore: "Vanguard MMA is Europe's establishment: opera-house production, old federation money, and a talent pipeline that drains every gym from Lisbon to Bucharest. It sells itself as the continent's answer to the AFC — and privately resents that the phrase concedes the question."),
            new("Ironhold Fighting Alliance", "IFA", 46, 130, new Money(2_600_000m), Region.Europe, MidSix, Women: false,
                Lore: "The Ironhold Fighting Alliance came up the hard way — industrial-town gyms, wrestling mats in converted warehouses, purses paid in cash. It has no interest in small men or light entertainment; it sells hard fights to hard crowds, and it regards VMMA's velvet-rope establishment as everything wrong with the sport."),
            new("Iron Circle FC", "ICF", 49, 180, new Money(4_000_000m), Region.Asia, Women: true,
                Lore: "Iron Circle FC carries Japan's fight tradition on its shoulders: bow-before-battle ceremony, a Tokyo dome show every New Year, and an audience that prizes craft over trash talk. Its belts have lineage older than most promotions' existence, and its stars famously turn down richer offers to keep wearing them."),
            new("Gulf Fighting Championship", "GFC", 40, 95, new Money(4_400_000m), Region.MiddleEast, BigFive, Women: false,
                Lore: "The Gulf Fighting Championship is sovereign wealth with a cage: launched as a statement project, it pays the richest purses in the sport and flies in name talent for big-man superfights under stadium lights. The one thing the money hasn't bought is loyalty — nobody stays when a bigger cheque calls."),
            new("United Fight League", "UFL", 37, 95, new Money(2_000_000m), Region.NorthAmerica, BigFive, Women: true,
                Lore: "The United Fight League is America's proving ground — the national-television tier where regional champions find out whether they're AFC material. Half its roster is climbing and the other half is content being somebody at home, and the UFL has learned to sell both stories."),
            new("Rio Combat Sport", "RCS", 35, 90, new Money(1_400_000m), Region.SouthAmerica, BigFive, Women: false,
                Lore: "Rio Combat Sport was founded by three cariocas with a truck-mounted cage and a grievance: Brazil's fight scene, they argued, had gone soft under Cage Kings' stewardship. Young, loud and favela-proud, RCS books wars, pays late, and dares São Paulo to look down its nose again."),
            new("Southern Cross MMA", "SCM", 34, 95, new Money(1_100_000m), Region.Oceania, BigFive, Women: true,
                Lore: "Southern Cross MMA runs the Australia–New Zealand scene from harbourside arenas — sunshine, cold beer, and genuinely horrible people to fight. Isolation bred a self-sufficient circuit: its champions are homegrown, its crowds are loyal, and every few years it exports a monster to the northern hemisphere."),
            new("Cage Kings", "CK", 35, 145, new Money(2_800_000m), Region.SouthAmerica, Women: true,   // the establishment carries a real chest into the RCS war
                Lore: "Cage Kings is Brazil's grand old house — the promotion that carried vale tudo out of the dust and into the arenas. Its belts made legends and its name still opens every gym door in the country, but a hungry Rio upstart is currently trying to burn the family estate down."),
            new("Coastal Combat", "CC", 27, 95, new Money(650_000m), Region.NorthAmerica, FeederLight, Women: false,
                Lore: "Coastal Combat works the American small-hall circuit for the little guys the big shows overlook — flyweights through lightweights, matched hard and paid honest. Scouts treat its cards like a farmers' market, and its owners long ago made peace with being everyone's favourite feeder."),
            new("Sahara Combat League", "SCL", 24, 85, new Money(480_000m), Region.Africa, MidFour, Women: false,
                Lore: "The Sahara Combat League is a bet on a continent: pan-African cards from Lagos to Nairobi, staged on shoestrings and carried by the sheer noise of crowds discovering the sport in real time. It can't pay its stars what they're worth — it pays them in home-soil glory and prays that's enough to keep them."),

            // A wider base of regional feeders and tiny local halls, each with a lane,
            // giving the world a pyramid AND a texture from day one. Most grind along
            // or fold; a rare few climb. Startups keep the bottom churning (WorldEvents).
            new("Great Lakes Fighting", "GLF", 28, 80, new Money(680_000m), Region.NorthAmerica, CoreThree, Women: false,
                Lore: "Great Lakes Fighting is Midwest wrestling country distilled into a promotion: high-school gymnasiums, folding chairs, and an endless supply of iron-jawed grinders. It has sent more contenders to the national stage than outfits three times its size — and never once kept one."),
            new("Kansai Cage", "KAC", 26, 75, new Money(600_000m), Region.Asia, LighterHouse, Women: true,
                Lore: "Kansai Cage is Osaka's temple of the small and the fearless — a lighter-weights house in the old Shooto tradition and the beating heart of Japanese women's MMA. Its shows are intimate, its craft is obsessive, and its faithful believe, correctly, that the best pound-for-pound fighting on earth happens in rooms this size."),
            new("Cordillera Combat", "CDC", 24, 70, new Money(500_000m), Region.SouthAmerica, TinyLight, Women: false,
                Lore: "Cordillera Combat stages its cards in Andean gyms two miles above sea level, where visiting fighters gas out in a round and the locals never seem to breathe. A tiny house with a single famous export: flyweights with bottomless lungs."),
            new("Motor City Cage", "MCC", 17, 60, new Money(320_000m), Region.NorthAmerica, HeavyHall, Women: false,
                Lore: "Motor City Cage puts on heavy-handed fights for a heavy-handed town — middleweight and up, judges optional. Detroit crowds don't pay to watch footwork, and the promotion has honoured that contract since its first card in an east-side warehouse."),
            new("Baltic Brawl", "BLB", 12, 50, new Money(220_000m), Region.Europe, HeavyHall, Women: false,
                Lore: "Baltic Brawl is the old school preserved in amber: big men, small halls, sambo pedigrees, and an announcer who sounds like a dockyard foreman. It survives on stubbornness and the region's inexhaustible supply of enormous, uncomplaining heavyweights."),
            new("Mekong Fight Club", "MFC", 14, 50, new Money(250_000m), Region.Asia, FeederLight, Women: false,
                Lore: "Mekong Fight Club grew out of the temple-fair circuit, where fighters have traded leather at festivals up and down the river for generations. It runs the lighter weights the region breeds best, and its cards double as open auditions for every scout in Asia."),
            new("Cape Fury", "CPF", 11, 48, new Money(210_000m), Region.Africa, HeavyHall, Women: false,
                Lore: "Cape Fury throws big men from the townships into a cage at the foot of Table Mountain and lets the neighbourhood settle its arguments legally. The purses are small and the heavyweights are not; every card sells out on local pride alone."),
            new("Pampas Fight League", "PMP", 15, 60, new Money(270_000m), Region.SouthAmerica, CoreThree, Women: false,
                Lore: "The Pampas Fight League is Argentine showmanship applied to cage fighting — entrances longer than some of the fights, crowds that sing in full voice, and a soft spot for brawlers who play to the cheap seats. Buenos Aires adores it; its accountants do not."),
        };

        // Where each designed promotion LIVES. Every org used to default to its
        // region's fight capital, so three US promotions shared Las Vegas, Rio's
        // upstart lived in São Paulo and Cape Fury in Johannesburg — a "local" show
        // with no locality. Atlas cities only (a new atlas city draws from the
        // builder stream and reshuffles every seed); where the lore's town isn't in
        // the atlas, the nearest one stands in and the lore says why.
        private static readonly Dictionary<string, string> DesignedHomes = new(StringComparer.Ordinal)
        {
            ["AFC"] = "Las Vegas",        // born in a Las Vegas casino ballroom
            ["VMMA"] = "London",          // the establishment's opera-house shows; nothing in the lore moves it
            ["IFA"] = "Moscow",           // the Volk Sambo Club's school: its franchise, its heir and its first champion
            ["ICF"] = "Tokyo",            // the Tokyo dome every New Year
            ["GFC"] = "Abu Dhabi",        // the Falcon Dome, built for its opening night
            ["UFL"] = "Los Angeles",      // a television man's league; Hollywood Jackson holds its welterweight belt
            ["RCS"] = "Rio de Janeiro",   // three cariocas with a truck
            ["SCM"] = "Sydney",           // harbourside arenas; Harbour City is its gym
            ["CK"] = "Sao Paulo",         // Brazil's grand old house, São Paulo's arenas
            ["CC"] = "New York",          // the East Coast small-hall circuit
            ["SCL"] = "Lagos",            // the Lion of Lagos; cards from Lagos to Nairobi
            ["GLF"] = "Chicago",          // Midwest wrestling country
            ["KAC"] = "Tokyo",            // Osaka isn't in the atlas: a Tokyo basement the rent could afford
            ["CDC"] = "Lima",             // Peru's Andean house (La Paz/Cusco aren't in the atlas)
            ["MCC"] = "Chicago",          // Detroit isn't in the atlas: the act moved down I-94
            ["BLB"] = "Stockholm",        // no Baltic city in the atlas: the ferry port the cards sail from
            ["MFC"] = "Bangkok",          // the temple-fair circuit's big city
            ["CPF"] = "Cape Town",        // the foot of Table Mountain
            ["PMP"] = "Buenos Aires",     // Buenos Aires adores it
        };

        public static PromotionSpec[] DefaultPromotions { get; } =
            DesignedSpecs.Select(s => s with { HomeCity = DesignedHomes.GetValueOrDefault(s.Abbreviation) }).ToArray();

        /// <summary>Authored hometowns for designed fighters and legends (off only in the RNG-guard test).</summary>
        internal bool AuthoredHomeCities { get; init; } = true;
    }

    public World Build(Options? options = null)
    {
        var o = options ?? new Options();
        var specs = o.Promotions ?? Options.DefaultPromotions;
        var world = new World { CurrentDate = o.StartDate, StartDate = o.StartDate, RngSeed = o.Seed, ArtCatalog = o.IdentityCatalog };
        var rng = new SimRandom(SimRandom.Mix(o.Seed, 0xB00C_1234UL));
        var classes = Enum.GetValues<WeightClass>();

        Geography.WorldMap.Build(world, rng);

        var gen = new FighterGenerator(o.Seed, o.IdentityCatalog) { AuthoredHomes = o.AuthoredHomeCities };

        // The designed initial database enters first: the world's headliners are
        // authored, and org-tagged ones claim slots on their promotion's roster.
        var designed = o.UseDesignedRoster
            ? Generation.DesignedRoster.All
            : Array.Empty<DesignedFighter>();
        var designedFighters = designed.Select(d => (Spec: d, Fighter: gen.CreateDesigned(world, d))).ToList();
        int OrgIndexOf(string? abbr) => abbr is null ? -1 : Array.FindIndex(specs, s => s.Abbreviation == abbr);

        // Day-one bad blood: designed feuds enter the world already burning, so the
        // first cards have grudges to book and the news has stories to tell.
        foreach (var (spec, fighter) in designedFighters.Where(x => x.Spec.RivalOf is not null))
            if (designedFighters.FirstOrDefault(x => x.Spec.Last == spec.RivalOf).Fighter is { } enemy)
                world.Rivalries.Add(new Rivalry { A = fighter.Id, B = enemy.Id, Intensity = 60, Since = o.StartDate });

        var rostersByPromo = specs.Select(_ => new List<Fighter>()).ToArray();
        foreach (var (spec, fighter) in designedFighters.Where(x => x.Spec.Sex == Sex.Male))
            if (OrgIndexOf(spec.Org) is var p && p >= 0)
                rostersByPromo[p].Add(fighter);

        // One big generated talent pool fills the depth beneath the designed
        // headliners. (A founded player promotion's hiring pool is generated
        // separately in FoundPlayerPromotion, targeted at its own divisions.)
        // The green-intake slice of the free-agent pool stays OUT of the tier
        // sort — it enters unsigned in SeedGreenIntake below.
        int totalSigned = specs.Sum(s => s.RosterSize);
        int tierLeftovers = o.FreeAgentPool - o.GreenIntake;
        var everyone = gen.Populate(world, Math.Max(0, totalSigned + tierLeftovers - designedFighters.Count));

        // Tier by weight class so each promotion fields a full, balanced roster:
        // within every division the best fighters go to the biggest promotion.
        foreach (var wc in classes)
        {
            var inClass = everyone
                .Where(f => f.WeightClass == wc)
                .OrderByDescending(FighterAbility.CurrentAbility)
                .ToList();

            // Hand out this division's talent top-down, proportional to the SEATS
            // each org budgets for this class (its roster spread over the divisions
            // it actually runs — a niche org fields fewer, deeper divisions, and an
            // org that doesn't run the class takes nobody). Designed fighters already
            // on a roster count against its quota.
            int designedHere = rostersByPromo.Sum(r => r.Count(f => f.WeightClass == wc));
            var quotas = DivisionQuotas(specs, wc, inClass.Count + designedHere, tierLeftovers, classes.Length);
            int cursor = 0;
            for (int p = 0; p < specs.Length; p++)
            {
                int already = rostersByPromo[p].Count(f => f.WeightClass == wc);
                for (int k = already; k < quotas[p] && cursor < inClass.Count; k++, cursor++)
                    rostersByPromo[p].Add(inClass[cursor]);
            }
            // Everything left in this class stays a free agent.
        }

        // Local promotions are LOCAL: the small halls trade their outsiders for
        // fighters of the same level from their own city, country and continent
        // (ability-for-ability swaps — no new fighters, no draws).
        var homes = specs.Select(s => HomeCityFor(world, s.Region, s.HomeCity)).ToArray();
        RosterNativity.Apply(specs, homes, rostersByPromo, everyone, classes, Sex.Male);

        var promos = new Promotion[specs.Length];
        for (int p = 0; p < specs.Length; p++)
        {
            var spec = specs[p];
            var promo = new Promotion
            {
                Id = world.NewPromotionId(),
                Name = spec.Name,
                Abbreviation = spec.Abbreviation,
                BaseRegion = spec.Region,
                IsPlayerControlled = o.Founding is null && p == o.PlayerPromotionIndex,
                Balance = spec.Balance,
                Prestige = spec.Prestige,
                Lore = spec.Lore,
            };
            promo.TierClearance = Org.OrgSizing.SizeOf(promo);   // cleared to operate at its starting tier
            world.Promotions[promo.Id] = promo;
            promos[p] = promo;
            if (promo.IsPlayerControlled) world.PlayerPromotion = promo.Id;

            foreach (var f in rostersByPromo[p])
                f.Contract = PromotionSetup.SignContract(world, promo, o.StartDate, f, rng);
            ApplyAuthoredPurses(rostersByPromo[p]);

            var designedChampions = designedFighters
                .Where(x => x.Spec.Champion && x.Spec.Sex == Sex.Male && OrgIndexOf(x.Spec.Org) == p)
                .ToDictionary(x => x.Spec.WeightClass, x => x.Fighter.Id);
            PromotionSetup.ConfigureDivisions(world, promo, rostersByPromo[p], spec.Divisions ?? classes, o.StartDate, o.RankingsDepth, designedChampions);
            Finance.RevenueService.SeedEstablished(world, promo, o.StartDate, rng);
            SeedCityPopularity(world, promo, spec.HomeCity);
        }

        if (o.IncludeWomensDivisions)
            AddWomensDivisions(world, specs, promos, gen, o, rng,
                designedFighters.Where(x => x.Spec.Sex == Sex.Female).ToList());

        SeedGreenIntake(world, gen, o, rng);

        if (o.Founding is { } start) FoundPlayerPromotion(world, start, o, rng);

        // The world's PAST: every fighter's tally becomes a real career of
        // back-dated events, so records, histories, title lineages and event
        // numbering all describe the same facts. A founded player promotion is
        // brand new — it has no history to write.
        if (o.GenerateHistory)
        {
            Generation.HistoryBuilder.Build(world, o.StartDate,
                o.Founding is not null ? world.PlayerPromotion : null);
            // Day-one contender rankings judge the real résumés, not just ability.
            RankingService.RecomputeAll(world, o.StartDate, o.RankingsDepth);
        }

        // The era BEFORE the player: the retired greats, their reigns at the head
        // of the belts they held. After HistoryBuilder on purpose — a man who last
        // fought fifteen years ago must not be given bouts in the recent window.
        if (o.UseDesignedRoster) Generation.LegendBuilder.Build(world, o.StartDate, o.AuthoredHomeCities);

        // With every fighter in the world (rosters, free agents, women, the
        // founding pool), the designed fight camps open and the sport sorts
        // itself into rooms — the best talent claims the best mats first. The
        // authored rooms are claimed BEFORE the sort, so the fighters whose story
        // names a gym are actually in it and everyone else fills in around them.
        Living.Camps.Open(world, rng);
        if (o.UseDesignedRoster) SeedDesignedCamps(world, designedFighters);
        Living.Camps.Assign(world, rng);

        if (o.UseDesignedRoster)
        {
            Generation.LegendBuilder.StaffCamps(world, o.StartDate);   // the legends who coach take their offices
            SeedDesignedBonds(world, designedFighters, o.StartDate);   // the friendships and mentorships the bios already claim
        }

        Org.StaffService.SeedMarket(world, rng);
        Org.StaffService.SeedFrontOffices(world, rng, o.UseDesignedRoster);   // every house has people behind the desks
        if (o.UseDesignedRoster) Org.StaffService.SeedOwners(world);          // and somebody whose money it is
        if (world.Get(world.PlayerPromotion) is { } player)
            Org.Objectives.Seed(player, o.StartDate, new DateOnly(o.StartDate.Year, 12, 31));

        GlobalRankings.Recompute(world);

        // Last: the sport reports on the world that was just built, so day one
        // opens on a feed with eighteen months of history in it instead of a
        // blank page. Everything in it is derived from what actually exists.
        Living.Prologue.Write(world, o.StartDate);
        return world;
    }

    /// <summary>
    /// Re-price the deals the LORE already describes: the Gulf's hired guns on
    /// their famous money, the believers fighting for the cause on scraps, and
    /// the prospects whose whole story is that they're worth more than they're
    /// being paid. Applied after the formula so the ordinary roster is untouched.
    /// </summary>
    private static void ApplyAuthoredPurses(List<Fighter> roster)
    {
        foreach (var f in roster)
        {
            if (!f.Designed || f.Contract is not { } c) continue;
            if (!Generation.DesignedRoster.PurseFactors.TryGetValue(f.LastName, out var factor)) continue;
            c.PursePerFight = new Money(Math.Round(c.PursePerFight.Amount * factor / 500m) * 500m);
            c.WinBonus = new Money(Math.Round(c.PursePerFight.Amount * 0.75m / 500m) * 500m);
        }
    }

    /// <summary>
    /// Put the authored fighters in the rooms their stories name, before the
    /// general camp sort runs — a Bangkok muay thai lifer belongs at Emerald
    /// Tiger, not wherever the ability queue happened to place him.
    /// </summary>
    private static void SeedDesignedCamps(World world, List<(DesignedFighter Spec, Fighter Fighter)> designed)
    {
        foreach (var (spec, fighter) in designed)
        {
            if (!Generation.DesignedRoster.CampHomes.TryGetValue(spec.Last, out var room)) continue;
            if (world.Camps.Values.FirstOrDefault(c => c.Name == room) is { } camp) fighter.Camp = camp.Id;
        }
    }

    /// <summary>
    /// The authored bonds: the mentorships and friendships the bios already
    /// assert, entering the world as real relationships so the matchmaker, the
    /// development model and the storyline engine can all see them from day one.
    /// A bond can name a LEGEND (the retired greats) as easily as a designed
    /// fighter — that's how a master ends up in a prospect's corner.
    /// </summary>
    private static void SeedDesignedBonds(World world, List<(DesignedFighter Spec, Fighter Fighter)> designed, DateOnly on)
    {
        // A bond names a last name, or "First Last" where a surname is shared
        // (Wallace Bandeira, a living champion, and the legend Paulo Bandeira).
        // The key must pick exactly ONE authored person — a shared bare surname
        // resolves to nobody rather than to whoever was written first, which is
        // how a rival org's heavyweight champion became Souza's mentor.
        Fighter? Resolve(string key)
        {
            var authored = world.Fighters.Values.Where(f => f.Designed).ToList();
            var full = authored.Where(f => $"{f.FirstName} {f.LastName}" == key).ToList();
            if (full.Count == 1) return full[0];
            var byLast = authored.Where(f => f.LastName == key).ToList();
            return byLast.Count == 1 ? byLast[0] : null;
        }

        foreach (var bond in Generation.DesignedRoster.Bonds)
        {
            if (Resolve(bond.A) is not { } a || Resolve(bond.B) is not { } b || a.Id == b.Id) continue;
            if (world.Relationships.Any(r => r.IsBetween(a.Id, b.Id))) continue;
            world.Relationships.Add(new Relationship
            {
                A = a.Id, B = b.Id, Kind = bond.Kind,
                Strength = bond.Strength, Since = on, Note = bond.Note,
            });
        }
    }

    /// <summary>The women's weight classes the world runs — the established lighter divisions.</summary>
    private static readonly WeightClass[] WomensClasses =
        { WeightClass.Strawweight, WeightClass.Flyweight, WeightClass.Bantamweight };

    private const int WomenPerOrgPerClass = 22;

    /// <summary>
    /// Bolt a women's scene onto the built world: generate a female talent pool,
    /// tier it top-down into each promotion's women's divisions (deepest at the
    /// biggest org, like the men), sign them, and crown the belts. Left as a
    /// separate pass so the men's world is untouched.
    /// </summary>
    // Which orgs run women is a DESIGN choice per spec (a niche in itself — some
    // scenes embrace it, some don't); specs that don't say fall back to the old
    // rule: National-tier promotions and up.
    private const int WomensDivisionPrestigeFloor = 32;
    private static bool RunsWomen(PromotionSpec s) => s.Women ?? s.Prestige >= WomensDivisionPrestigeFloor;

    // Women's depth scales with the org — a giant fields full divisions, a small
    // women-friendly house (a Kansai Cage) runs shallower ones.
    private static int WomenPerClass(PromotionSpec s) => Math.Clamp(s.RosterSize / 8, 8, WomenPerOrgPerClass);

    private static void AddWomensDivisions(World world, PromotionSpec[] specs, Promotion[] promos, FighterGenerator gen, Options o, SimRandom rng,
        List<(DesignedFighter Spec, Fighter Fighter)> designedWomen)
    {
        var runners = Enumerable.Range(0, specs.Length)
            .Where(p => RunsWomen(specs[p])).ToArray();
        if (runners.Length == 0) return;

        // Enough depth to stock every running org's divisions plus free agents.
        int perClass = runners.Sum(p => WomenPerClass(specs[p])) + 10;
        var women = gen.PopulateWomen(world, WomensClasses.Length * perClass, WomensClasses);

        // The AUTHORED women's headliners claim their org's slots first — the
        // women's scene opens with real stars, not anonymous belts.
        var femaleRosters = specs.Select(_ => new List<Fighter>()).ToArray();
        foreach (var (dSpec, fighter) in designedWomen)
            if (Array.FindIndex(specs, s => s.Abbreviation == dSpec.Org) is var p && p >= 0)
                femaleRosters[p].Add(fighter);

        foreach (var wc in WomensClasses)
        {
            var inClass = women.Where(f => f.WeightClass == wc)
                .OrderByDescending(FighterAbility.CurrentAbility).ToList();
            int cursor = 0;
            foreach (var p in runners)                    // best women to the biggest women's org
            {
                int already = femaleRosters[p].Count(f => f.WeightClass == wc);
                for (int k = already; k < WomenPerClass(specs[p]) && cursor < inClass.Count; k++, cursor++)
                    femaleRosters[p].Add(inClass[cursor]);
            }
            // The rest of this class stays on the open market.
        }

        // Same nativity pass as the men's (a Kansai house's women are Japanese first).
        RosterNativity.Apply(specs, promos.Select(p => p.HomeCity is { } h ? world.Get(h) : null).ToArray(),
            femaleRosters, women, WomensClasses, Sex.Female);

        foreach (var p in runners)
        {
            foreach (var f in femaleRosters[p])
                f.Contract = PromotionSetup.SignContract(world, promos[p], o.StartDate, f, rng);
            ApplyAuthoredPurses(femaleRosters[p]);
            var champions = designedWomen
                .Where(x => x.Spec.Champion && x.Spec.Org == specs[p].Abbreviation)
                .ToDictionary(x => x.Spec.WeightClass, x => x.Fighter.Id);
            PromotionSetup.ConfigureDivisions(world, promos[p], femaleRosters[p], WomensClasses,
                o.StartDate, o.RankingsDepth, champions, Sex.Female);
        }
    }

    /// <summary>
    /// Stand up the player's brand-new promotion with nothing but a bankroll:
    /// the roster starts empty (you hire every fighter yourself from the
    /// free-agent market) and every belt starts vacant, waiting for its first
    /// title fight. The extra talent generated for the founding sits unsigned.
    /// </summary>
    private static void FoundPlayerPromotion(World world, FoundingSpec spec, Options o, SimRandom rng)
    {
        var promo = new Promotion
        {
            Id = world.NewPromotionId(),
            Name = spec.Name,
            Abbreviation = spec.Abbreviation,
            BaseRegion = spec.Region,
            IsPlayerControlled = true,
            Balance = spec.StartingBalance,
            Prestige = spec.Prestige,
        };
        promo.TierClearance = Org.OrgSizing.SizeOf(promo);   // a shoestring starts cleared for City only
        world.Promotions[promo.Id] = promo;
        world.PlayerPromotion = promo.Id;

        PromotionSetup.ConfigureVacantDivisions(world, promo, spec.Divisions ?? FoundingSpec.DefaultDivisions);
        if (o.IncludeWomensDivisions)
            PromotionSetup.ConfigureVacantDivisions(world, promo, WomensClasses.Take(2).ToArray(), Sex.Female);
        SeedFoundingTalent(world, promo, spec, o, rng);
        Finance.RevenueService.SeedFounding(world, promo, o.StartDate, rng);
        SeedCityPopularity(world, promo, spec.HomeCityName);
    }

    /// <summary>
    /// The founding org's hiring market: unsigned talent generated INTO the
    /// divisions it actually runs (a quarter of it for the women's divisions
    /// when they're on), as a mix of raw prospects and affordable journeymen —
    /// the kind of talent a prestige-5 gate can actually pay — with most of it
    /// from the home region, so the first cards carry local names. Generated
    /// before HistoryBuilder runs, so every rolled record becomes a real career.
    /// </summary>
    private static void SeedFoundingTalent(World world, Promotion promo, FoundingSpec spec, Options o, SimRandom rng)
    {
        var gen = Generation.FighterGenerator.ForWorld(world);
        var mens = spec.Divisions ?? FoundingSpec.DefaultDivisions;
        var womens = o.IncludeWomensDivisions ? WomensClasses.Take(2).ToArray() : Array.Empty<WeightClass>();

        for (int i = 0; i < spec.TalentPool; i++)
        {
            bool woman = womens.Length > 0 && i % 4 == 3;
            var wc = woman ? womens[i / 4 % womens.Length] : mens[i % mens.Length];
            // Mostly green: the sport's base is kids with short records who'll
            // grow into the world if they can keep winning (the washout filter
            // quietly removes the ones who can't) — plus enough affordable
            // journeymen to carry the first cards. Green means green ON PAPER
            // too: a bout cap keeps the prospects at a handful of fights, so
            // the hiring screen reads like a real regional scene instead of a
            // row of 24-year-olds already carrying 4-6 records.
            bool journeyman = rng.Chance(0.3);

            var id = world.NewFighterId();
            var f = gen.Generate(id, o.StartDate, new Generation.GenOptions
            {
                WeightClass = wc,
                Sex = woman ? Sex.Female : Sex.Male,
                Nationality = rng.Chance(0.6) ? spec.Region : null,
                Age = journeyman ? rng.Range(26, 34) : rng.Range(19, 24),
                Overall = journeyman ? rng.Range(42, 58) : rng.Range(32, 50),
                MaxBouts = journeyman ? null : rng.Range(0, 4),
            });
            gen.AssignHometown(world, f);
            // Same guard every other pool has: without it a founded start carried
            // dozens of repeated fight names, including copies of authored ones
            // ("The Lion", "The Viper"). Re-rolls come off a keyed per-fighter
            // stream inside EnsureUniqueName, never the shared build stream.
            gen.EnsureUniqueName(world, f);
            world.Fighters[id] = f;
        }
    }

    /// <summary>
    /// The open market's PROSPECT lane: the green-intake slice of the free-agent
    /// pool, spread across every division (a quarter of it women when they're
    /// on) — young, a handful of fights at most, and real headroom. The AI
    /// promotions passed on them for being unproven, not for being bad. Without
    /// this slice the day-one market is purely the tier sort's leftovers: an
    /// all-washed shelf with nothing green on it. Generated before
    /// HistoryBuilder runs, so the few fights they do have become real bouts.
    /// </summary>
    private static void SeedGreenIntake(World world, FighterGenerator gen, Options o, SimRandom rng)
    {
        var classes = Enum.GetValues<WeightClass>();
        for (int i = 0; i < o.GreenIntake; i++)
        {
            bool woman = o.IncludeWomensDivisions && i % 4 == 3;
            var id = world.NewFighterId();
            var f = gen.Generate(id, o.StartDate, new GenOptions
            {
                WeightClass = woman ? WomensClasses[i / 4 % WomensClasses.Length] : classes[i % classes.Length],
                Sex = woman ? Sex.Female : Sex.Male,
                Age = rng.Range(18, 23),
                Overall = rng.GaussianInt(40, 9, 26, 60),
                MaxBouts = rng.Range(0, 3),
            });
            gen.AssignHometown(world, f);
            gen.EnsureUniqueName(world, f);
            world.Fighters[id] = f;
        }
    }

    /// <summary>
    /// Seed how well-known a promotion is city by city: strongest in its HQ
    /// metro, strong around the home region, and (the bigger the brand)
    /// progressively more reach abroad — bigger foreign markets hear of you first.
    /// </summary>
    private static void SeedCityPopularity(World world, Promotion promo, string? homeCityName = null)
    {
        // An explicitly chosen home town wins; otherwise the region's fight capital.
        promo.HomeCity = HomeCityFor(world, promo.BaseRegion, homeCityName)?.Id;
        int prestige = promo.Prestige.Value;
        double reach = 0.30 + prestige / 100.0 * 0.40;   // big promotions travel; small ones stay local

        foreach (var c in world.Cities.Values)
        {
            int value = c.Region == promo.BaseRegion
                ? (c.Id == promo.HomeCity ? Math.Min(100, prestige + 10) : (int)Math.Round(prestige * 0.85))
                : (int)Math.Round(prestige * reach * (0.5 + c.Market / 180.0));
            if (value > 0) promo.PopularityByCity[c.Id] = Math.Clamp(value, 0, 100);
        }
    }

    /// <summary>The named town in the region if the atlas has it, else the region's fight capital.</summary>
    private static City? HomeCityFor(World world, Region region, string? name) =>
        (name is { Length: > 0 }
            ? world.Cities.Values.Where(c => c.Region == region
                    && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Id.Value).FirstOrDefault()
            : null)
        ?? Geography.WorldMap.AnchorCity(world, region);

    /// <summary>
    /// How many fighters in a single division each promotion gets, top promotion
    /// first, proportional to roster size, leaving a slice unsigned (free agents,
    /// plus a founded player promotion's starter roster).
    /// </summary>
    private static int[] DivisionQuotas(PromotionSpec[] specs, WeightClass wc, int available, int unsignedPool, int classCount)
    {
        // Seats each org budgets for THIS class: its roster spread over the classes
        // it runs. With every org on the full slate this reduces exactly to the old
        // roster-size-proportional split; a niche org concentrates instead.
        var seats = new double[specs.Length];
        for (int p = 0; p < specs.Length; p++)
        {
            var div = specs[p].Divisions;
            bool runs = div is null || Array.IndexOf(div, wc) >= 0;
            seats[p] = runs ? specs[p].RosterSize / (double)(div?.Length ?? classCount) : 0;
        }
        double capacity = seats.Sum() + unsignedPool / (double)classCount;
        var quotas = new int[specs.Length];
        for (int p = 0; p < specs.Length; p++)
            quotas[p] = (int)Math.Round(available * seats[p] / capacity);
        return quotas;
    }
}
