using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// Bookable local talent — the small-org special. A City/Regional promotion can
/// put one of its fighters against an unnamed hometown opponent ("Local
/// Striker", "Local Wrestler") to build the prospect up cheaply. Locals are
/// clearly amateur-tier and retire back into the scene after the bout — but on
/// the rare upset, or a show-stealing (4+ star) win over a fellow local, they're
/// DISCOVERED: renamed into a real prospect with a raw ceiling, a profile
/// accolade telling the story, and the org that gave them the shot moving to
/// sign them (a letter to the player; an AI host offers a deal on the spot).
/// </summary>
public static class LocalScene
{
    /// <summary>The archetypes a booker can order up from the local scene.</summary>
    public static readonly PrimaryStyle[] OfferedStyles =
    {
        PrimaryStyle.Striker, PrimaryStyle.Wrestler, PrimaryStyle.Grappler,
        PrimaryStyle.Kickboxer, PrimaryStyle.Brawler,
    };

    public static string Label(PrimaryStyle style) => style switch
    {
        PrimaryStyle.Striker => "Striker",
        PrimaryStyle.Wrestler => "Wrestler",
        PrimaryStyle.Grappler => "Grappler",
        PrimaryStyle.Kickboxer => "Kickboxer",
        PrimaryStyle.Brawler => "Brawler",
        PrimaryStyle.BrazilianJiuJitsu => "Grappler",
        PrimaryStyle.ClinchFighter => "Clinch Fighter",
        _ => "Fighter",
    };

    /// <summary>
    /// Mint the unnamed local opponent for a card: from the host city (their
    /// hometown), amateur-tier, in the given style and weight class.
    /// </summary>
    public static Fighter CreateOpponent(World world, Promotion? promo, Event ev, PrimaryStyle style, WeightClass weightClass, Sex sex = Sex.Male)
    {
        var city = WorldMap.CityOf(world, ev) ?? promo?.HomeCity
            ?? (promo is not null ? WorldMap.AnchorCity(world, promo.BaseRegion)?.Id : null);
        var home = city is { } c ? world.Get(c) : null;
        string? code = home is not null ? world.Get(home.Country)?.Code : null;

        var id = world.NewFighterId();
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)id.Value ^ 0x10CA_1F17UL));

        var gen = FighterGenerator.ForWorld(world);
        var f = gen.Generate(id, world.CurrentDate, new GenOptions
        {
            WeightClass = weightClass,
            Style = style,
            Sex = sex,
            Nationality = home?.Region ?? promo?.BaseRegion ?? ev.Region,
            CountryCode = code,
            Age = rng.Range(20, 31),
            Overall = rng.GaussianInt(24, 3, 18, 30),   // clearly amateur-tier: any real pro should handle them
        });

        f.FirstName = "Local";
        f.LastName = Label(style);
        f.Nickname = null;
        f.LocalTalent = true;

        // What actually separates a club fighter from a pro: no gas tank, no
        // championship pace. Their raw technique roll stands (that's the upset
        // potential), but rounds two and three belong to the professional.
        f.Physical.Cardio = rng.Range(8, 20);
        f.Physical.Recovery = rng.Range(8, 22);
        f.Tendencies.Pace = Math.Min(f.Tendencies.Pace.Value, 35);
        f.Tendencies.Aggression = Math.Min(f.Tendencies.Aggression.Value, 40);
        f.HomeCity = city;
        gen.AssignHometown(world, f, code);             // resolves Country; hometown already set
        if (city is { } hc) f.CityReputation[hc] = rng.Range(3, 8);
        f.Intangibles.Popularity = rng.Range(3, 10);
        f.Momentum = 0;

        // A believable club-show record instead of the generator's pro career.
        var rec = f.Record;
        rec.Wins = rng.Range(1, 5);
        rec.Losses = rng.Range(1, 4);
        rec.Draws = 0; rec.NoContests = 0; rec.CurrentStreak = 0;
        rec.WinsByKo = rec.Wins / 2;
        rec.WinsBySubmission = 0;
        rec.WinsByDecision = rec.Wins - rec.WinsByKo;

        world.Fighters[id] = f;
        return f;
    }

    /// <summary>
    /// Two unnamed locals for an all-local undercard bout — how a small promotion
    /// papers a card it can't fill from its own roster. Pure filler: neither is a
    /// real pro, so the result changes nothing (see the ResultsProcessor).
    /// </summary>
    public static (Fighter Red, Fighter Blue) CreateLocalPair(World world, Promotion promo, Event ev, WeightClass wc)
    {
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed,
            (ulong)ev.Id.Value * 131UL + (ulong)ev.Card.Count ^ 0x10CA_2A2AUL));
        var red = CreateOpponent(world, promo, ev, OfferedStyles[rng.Range(0, OfferedStyles.Length - 1)], wc);
        var blue = CreateOpponent(world, promo, ev, OfferedStyles[rng.Range(0, OfferedStyles.Length - 1)], wc);
        return (red, blue);
    }

    /// <summary>
    /// A star is born: the local becomes a real fighter — by upsetting a pro, or
    /// by winning an all-local bout so exciting (4+ stars) the room took notice.
    /// A real name from their own country, a raw prospect's ceiling, the story on
    /// their profile forever — and the org that gave them the shot moves to sign
    /// them: a letter to the player, or an AI host offering a deal on the spot.
    /// </summary>
    public static void Discover(World world, Fighter f, Fighter beaten, Event ev, int rating = 0)
    {
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)f.Id.Value ^ 0xD15C_0FE2UL));
        bool beatPro = !beaten.LocalTalent;
        bool exciting = rating >= Flavor.MatchRating.FourStar;

        string culture = world.Get(f.Country) is { } country
            ? (WorldAtlas.ByCode(country.Code)?.Culture ?? "anglo")
            : "anglo";
        var (firsts, lasts) = NameData.Pool(culture, f.Nationality);
        f.FirstName = rng.Pick(firsts);
        f.LastName = rng.Pick(lasts);
        f.Nickname = rng.Chance(0.4) ? NameData.RollNickname(rng.NextULong(), culture, f.Nationality, f.Sex) : null;

        f.LocalTalent = false;
        f.DiscoveredBy = world.Get(ev.Promotion) is not null ? ev.Promotion : null;   // gratitude has an address (club shows have none)
        FighterGenerator.ForWorld(world).EnsureUniqueName(world, f);   // a real name — and a distinct one
        // The club years STAY in the clubs: a discovered pro's ledger opens the
        // night everyone saw — one real fight, one real record (1-0, however
        // they won it). The flavour tally they carried as an unnamed local is
        // wiped; the profile tells the discovery story through the accolade.
        var bout = ev.Card.First(x => x.Outcome is not null && (x.RedCorner == f.Id || x.BlueCorner == f.Id));
        var rec = f.Record;
        rec.Wins = 1; rec.Losses = 0; rec.Draws = 0; rec.NoContests = 0;
        rec.WinsByKo = bout.Outcome!.Method is FightMethod.Knockout or FightMethod.TechnicalKnockout ? 1 : 0;
        rec.WinsBySubmission = bout.Outcome.Method is FightMethod.SubmissionTap or FightMethod.TechnicalSubmission ? 1 : 0;
        rec.WinsByDecision = 1 - rec.WinsByKo - rec.WinsBySubmission;
        rec.CurrentStreak = 1;
        f.BoutHistory.Clear();
        f.BoutHistory.Add(bout.Id);
        // Raw and unpolished, but a real ceiling appears. Beating a pro is the
        // strongest proof; lighting up a fellow local promises a notch less.
        f.PotentialAbility = Math.Max(f.PotentialAbility.Value, beatPro ? rng.Range(58, 76) : rng.Range(52, 70));
        // The discovery makes a small name; an EXCITING one makes a bigger name,
        // and the hometown crowd that saw it live remembers.
        f.Intangibles.Popularity = f.Intangibles.Popularity.Value + (exciting ? 18 : 10);
        f.Momentum = Math.Clamp(f.Momentum + (exciting ? 40 : 30), -100, 100);
        if (exciting && f.HomeCity is { } hometown) Geography.Reputation.BumpFighter(world, f, hometown, 5);
        f.Morale = 85;
        f.Accolades.Add(new Accolade(ev.Date, beatPro
            ? $"Discovered on the local circuit — upset {beaten.FullName} at {ev.Name}"
            : $"Discovered on the local circuit — stole the show at {ev.Name}"));

        var promo = world.Get(ev.Promotion);
        string scene = world.Get(f.HomeCity ?? default)?.Name ?? "local";
        if (beatPro)
            News.Post(world, ev.Date, NewsCategory.Debut,
                $"Unknown local shocks {beaten.FullName} at {ev.Name}",
                $"{f.FullName} came off the {scene} club scene and beat a pro. They want to turn pro with {promo?.Abbreviation ?? "the promotion"}.",
                ev.Promotion, f.Id);
        else
            News.Post(world, ev.Date, NewsCategory.Debut,
                $"Unknown local steals the show at {ev.Name}",
                $"{f.FullName} came off the {scene} club scene and turned a filler bout into the fight of the night. They want to turn pro with {promo?.Abbreviation ?? "the promotion"}.",
                ev.Promotion, f.Id);

        if (promo is null) return;
        if (ev.Promotion == world.PlayerPromotion)
            Mail.Post(world, ev.Date, MailCategory.ContractAlert, "Matchmaking",
                $"{f.FullName} wants to sign with {promo.Abbreviation}",
                (beatPro
                    ? $"The local who upset {beaten.FullName} on our card wants to turn pro with us"
                    : $"The local who stole the show on our card wants to turn pro with us")
                + " — but the whole town saw it happen, and their agent is pricing the hype. Expect a real ask for an unproven kid. "
                + "They're in the free-agent pool; scouting says the ceiling is real.",
                aboutFighter: f.Id);
        else
            TryHostSigning(world, promo, f, ev.Date);
    }

    private static readonly Contracts.Negotiator Negotiator = new();
    private static readonly Contracts.FighterValuation Valuation = new();

    // "Sign that kid tonight": an AI host that just watched the discovery happen
    // on its own card moves before the monthly free-agency pass, and the fighter
    // — grateful for the shot — takes the org's standard opening offer as long
    // as it isn't insulting. Solvent orgs only, and only into a division with
    // room; otherwise the prospect hits the open market like anyone else.
    private static void TryHostSigning(World world, Promotion promo, Fighter f, DateOnly date)
    {
        if (promo.Defunct || Org.Solvency.Distress(promo) > 0) return;
        var division = promo.Divisions.FirstOrDefault(d => d.Matches(f));
        if (division is null) return;
        int target = Org.OrgSizing.RosterTargetPerDivision(Org.OrgSizing.SizeOf(promo));
        if (world.RosterOf(promo.Id).Count(x => division.Matches(x) && x.Status != FighterStatus.Retired) >= target) return;

        var exp = Valuation.Evaluate(world, f);
        var offer = Negotiator.AiOpeningOffer(world, promo, f, exp);
        if (exp.AskingPurse.Amount > 0 && offer.PursePerFight.Amount < exp.AskingPurse.Amount * 0.5m) return;
        Contracts.ContractLifecycle.Sign(world, f, offer, date);
    }
}
