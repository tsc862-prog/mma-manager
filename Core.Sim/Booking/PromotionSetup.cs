using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// Shared building blocks for standing a promotion up from a roster: signing
/// contracts, creating a division + belt per weight class, and seeding the
/// contender rankings. Used by both the single-promotion <see cref="LeagueBuilder"/>
/// and the multi-promotion <see cref="WorldBuilder"/> so the setup stays identical.
/// </summary>
public static class PromotionSetup
{
    /// <summary>
    /// Create a division per weight class the promotion runs, crown its champion
    /// (the designed one when the initial database names it, else the top fighter
    /// by seed score), and rank the rest as contenders. Fighters are matched to
    /// divisions by their own weight class.
    /// </summary>
    // With three bouts on the record HistoryBuilder stages the belt on the third,
    // after two real fights; with one, the belt lands in the pro debut.
    private const int MinBoutsForADayOneBelt = 3;

    public static void ConfigureDivisions(
        World world, Promotion promo, IReadOnlyCollection<Fighter> roster,
        IEnumerable<WeightClass> classes, DateOnly on, int rankingsDepth,
        IReadOnlyDictionary<WeightClass, FighterId>? designedChampions = null, Sex sex = Sex.Male,
        bool dayOneBuild = false)
    {
        foreach (var wc in classes)
        {
            var division = new Division { WeightClass = wc, Sex = sex };
            promo.Divisions.Add(division);

            var byMerit = roster
                .Where(f => f.WeightClass == wc && f.Sex == sex)
                .OrderByDescending(SeedScore)
                .ToList();
            if (byMerit.Count == 0) continue;

            // The champion (kept out of the contender list); the rest rank.
            // A belt isn't won on debut: the day-one champion is the best seed who
            // has actually fought a few times and won (HistoryBuilder stages the
            // reign inside their record — a one-fight career puts it in the pro
            // debut, a winless one leaves no night to stage it on at all). Only a
            // division with no such fighter crowns a novice. Applied only in the
            // day-one build (dayOneBuild, passed by WorldBuilder's men's and women's
            // passes) at the local stages, whose rosters RosterNativity reshapes and
            // where a 1-0 kid can top the seed list. Every other caller (mid-game
            // startups, LeagueBuilder) and the big stages keep the plain top seed.
            bool local = dayOneBuild && RosterNativity.IsLocalStage(promo.Prestige.Value);
            var champion = designedChampions is not null
                           && designedChampions.TryGetValue(wc, out var cid)
                           && byMerit.FirstOrDefault(f => f.Id == cid) is { } dc
                ? dc
                : (local ? byMerit.FirstOrDefault(f => f.Record.TotalBouts >= MinBoutsForADayOneBelt && f.Record.Wins >= 2) : null)
                  ?? byMerit[0];
            var title = CreateTitle(world, promo, wc, sex, champion, on);
            division.Title = title.Id;

            foreach (var contender in byMerit.Where(f => f.Id != champion.Id).Take(rankingsDepth))
                division.Rankings.Add(contender.Id);
        }
    }

    /// <summary>
    /// Divisions for a promotion that starts with no fighters: each weight class
    /// gets a division and a vacant belt — the first title fight crowns a champion.
    /// </summary>
    public static void ConfigureVacantDivisions(World world, Promotion promo, IEnumerable<WeightClass> classes, Sex sex = Sex.Male)
    {
        foreach (var wc in classes)
        {
            var division = new Division { WeightClass = wc, Sex = sex };
            promo.Divisions.Add(division);

            var title = new Title
            {
                Id = world.NewTitleId(),
                Promotion = promo.Id,
                WeightClass = wc,
                Name = TitleName(promo, wc, sex),
                CurrentChampion = null,
                Prestige = promo.Prestige.Value,   // a new belt is born with its org's name — its lineage takes it from there
            };
            world.Titles[title.Id] = title;
            promo.Titles.Add(title.Id);
            division.Title = title.Id;
        }
    }

    /// <summary>The cost of commissioning a new belt — the strap, the sanctioning, the ceremony.</summary>
    public const decimal BeltCost = 15_000m;

    /// <summary>
    /// Commission a vacant belt for a weight class — the owner's call (the Org
    /// screen). Creates the DIVISION too if the org never ran one (a class
    /// staffed after founding has fighters but no room of its own yet). Born
    /// with the org's prestige as its name value; the first title fight booked
    /// for it crowns the champion. Returns the existing belt if one is already
    /// sanctioned.
    /// </summary>
    public static Title CommissionTitle(World world, Promotion promo, WeightClass wc, Sex sex, DateOnly on)
    {
        var division = promo.Divisions.FirstOrDefault(d => d.WeightClass == wc && d.Sex == sex);
        if (division is null)
        {
            division = new Division { WeightClass = wc, Sex = sex };
            promo.Divisions.Add(division);
        }
        if (division.Title is { } existing && world.Get(existing) is { } already) return already;

        var title = new Title
        {
            Id = world.NewTitleId(),
            Promotion = promo.Id,
            WeightClass = wc,
            Name = TitleName(promo, wc, sex),
            CurrentChampion = null,
            Prestige = promo.Prestige.Value,
        };
        world.Titles[title.Id] = title;
        promo.Titles.Add(title.Id);
        division.Title = title.Id;
        Finance.Ledger.Expense(promo, on, FinanceCategory.Other, new Money(BeltCost), $"Commissioned the {title.Name}");
        Living.News.Post(world, on, NewsCategory.Milestone,
            $"{promo.Abbreviation} introduces the {title.Name}",
            "A new belt, vacant until somebody takes it — the first title fight crowns the champion.", promo.Id);
        return title;
    }

    /// <summary>Belt name — women's belts read "Women's Bantamweight Championship".</summary>
    public static string TitleName(Promotion promo, WeightClass wc, Sex sex) =>
        sex == Sex.Female
            ? $"{promo.Abbreviation} Women's {wc} Championship"
            : $"{promo.Abbreviation} {wc} Championship";

    /// <summary>
    /// Sign a fighter to a promotion with ability-scaled money on the same curve
    /// the open market uses, capped at what the promotion's box office can carry.
    ///
    /// Deals also arrive at DIFFERENT STAGES OF THEIR LIFE. Every fighter in the
    /// world used to open on 3-6 fights remaining at the identical formula price,
    /// so day one had no business drama in it at all: nobody's deal was running
    /// out, nobody was overpaid, and no champion was quietly on below-market
    /// money with one fight left. Now a slice of the world is already at the
    /// table — which is where a contracts screen gets interesting.
    /// </summary>
    public static Contract SignContract(World world, Promotion promo, DateOnly on, Fighter f, SimRandom rng)
    {
        int ability = FighterAbility.CurrentAbility(f);
        // Small halls sign non-exclusive (a cheaper slice of the fighter); the
        // bigger stages tie fighters down and pay the full price for it.
        bool exclusive = Contracts.Negotiator.WantsExclusivity(promo);
        decimal purse = 2_500m + Math.Max(0, ability - 25) * 350m + Math.Max(0, ability - 50) * 1_800m;
        if (!exclusive) purse *= 0.80m;

        // ONE draw, as this method has always taken — the shape of the deal is
        // then derived from it, so adding contract variety doesn't reshuffle the
        // shared generation stream and re-roll every tuned seed in the project.
        ulong roll = rng.NextULong();

        // What the deal was worth WHEN IT WAS SIGNED, which is not the same as
        // what this fighter is worth today. A prospect who has come on since put
        // pen to paper is a bargain the org is enjoying; a name signed on the
        // strength of a run that has since gone cold is an albatross on the books.
        decimal bargain = (int)(roll % 100) switch
        {
            < 18 => 0.55m + (int)((roll >> 8) % 20) / 100m,   // signed cheap and outgrown it
            < 30 => 1.20m + (int)((roll >> 8) % 35) / 100m,   // overpaid on last year's form
            _ => 0.85m + (int)((roll >> 8) % 31) / 100m,      // and no two ordinary deals are identical
        };

        // Where they sit in the pecking order still shows up in the money. Without
        // this the cap flattens a big roster completely — a champion and a prelim
        // fighter on precisely the same number.
        decimal standing = 1m + Math.Clamp(ability - 60, -20, 20) / 125m;
        // The cap first, THEN the deal's own history. Capping last flattened every
        // fighter at a big promotion onto the identical number — the AFC's whole
        // roster, champion and prelim alike, was paid exactly $65,500 — because
        // the formula clears the cap for almost all of them. Spreading around the
        // cap is what gives a big org's books a shape.
        decimal cap = Contracts.Negotiator.PurseCap(world, promo);
        purse = Math.Min(purse, cap) * bargain * standing;
        purse = Math.Min(purse, cap * 1.6m);   // even an albatross has a ceiling
        purse = Math.Round(purse / 500m) * 500m;

        // How far through the deal they are. Most of the world is mid-contract;
        // a real slice is at the end of one, and those are the conversations the
        // player walks into on day one.
        int fights = (int)((roll >> 24) % 100) switch
        {
            < 12 => 1,                              // final fight of the deal — decide now
            < 22 => 2,
            < 78 => 3 + (int)((roll >> 40) % 3),
            _ => 6 + (int)((roll >> 48) % 3),       // locked up long term
        };

        return new Contract
        {
            Promotion = promo.Id,
            SignedOn = on,
            Type = ContractType.PerFight,
            PursePerFight = new Money(purse),
            WinBonus = new Money(Math.Round(purse * 0.75m / 500m) * 500m),
            FightsRemaining = fights,
            IsExclusive = exclusive,
        };
    }

    /// <summary>
    /// Cold-start seeding score: ability dominates, but a proven win/loss résumé
    /// nudges fighters up or down so an unbeaten prospect with no fights doesn't
    /// outrank established contenders on day one.
    /// </summary>
    public static double SeedScore(Fighter f) =>
        FighterAbility.CurrentAbility(f) + Math.Min(f.Record.Wins, 15) * 0.5 - f.Record.Losses * 0.25;

    private static Title CreateTitle(World world, Promotion promo, WeightClass wc, Sex sex, Fighter champion, DateOnly on)
    {
        var title = new Title
        {
            Id = world.NewTitleId(),
            Promotion = promo.Id,
            WeightClass = wc,
            Name = TitleName(promo, wc, sex),
            CurrentChampion = champion.Id,
            Prestige = promo.Prestige.Value,   // a new belt is born with its org's name — its lineage takes it from there
        };
        title.Lineage.Add(new TitleReign { Champion = champion.Id, WonOn = on });
        world.Titles[title.Id] = title;
        promo.Titles.Add(title.Id);
        return title;
    }
}
