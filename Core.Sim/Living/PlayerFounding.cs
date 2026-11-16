using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The house rules a house founded in a city today may choose and the ones its era forces there: one long round
/// (he picks its length) or three of five with judges; the gloves; the ring or the cage. <see cref="UnifiedCode"/>:
/// the commissions' code binds every new house outside the second centre (three rounds a bout, five for a title,
/// today's gloves; only the enclosure is his). In the second centre the house fights in a ring and keeps the rest of
/// its era's rules. <see cref="Notes"/> says why, in words.
/// </summary>
public sealed record FoundingRules(
    bool RoundsAndJudges, bool UnifiedCode, IReadOnlyList<int> SingleRoundMinutes, IReadOnlyList<GloveType> Gloves,
    IReadOnlyList<Enclosure> Enclosures, int StandardRounds, int TitleRounds, IReadOnlyList<string> Notes);

/// <summary>
/// A city the player may (or may not) found his house in, with the honest reason when he may not, and the rules a
/// house there would fight under (<see cref="FoundingRules"/>; the second centre's differ).
/// </summary>
public sealed record FoundingCity(
    CityId Id, string Name, string Country, string CountryCode, Region Region, int Market, int MmaInterest, string? Refusal,
    FoundingRules Rules)
{
    public bool Open => Refusal is null;
}

/// <summary>
/// What founding a house today would mean, for the screen: whether he can at all (<see cref="Refusal"/>), the
/// cities, the house rules the era leaves to him and the ones it forces, the bank and prestige he opens with,
/// and the divisions he gets. Read-only. The top-level rule fields are those of a city outside the second centre;
/// each city carries its own (<see cref="FoundingCity.Rules"/>), and <see cref="SecondCentre"/> names the country
/// (code) whose houses fight in a ring, once there is one.
/// </summary>
public sealed record FoundingChoices(
    string? Refusal,
    IReadOnlyList<FoundingCity> Cities,
    bool RoundsAndJudges,
    IReadOnlyList<int> SingleRoundMinutes,
    IReadOnlyList<GloveType> Gloves,
    IReadOnlyList<Enclosure> Enclosures,
    Money Bank,
    int Prestige,
    IReadOnlyList<WeightClass> WeightLines,
    IReadOnlyList<string> Notes,
    bool UnifiedCode,
    string? SecondCentre)
{
    public bool CanFound => Refusal is null;

    /// <summary>One open division (no weight lines drawn yet).</summary>
    public bool OpenWeight => WeightLines.Count == 0;
}

/// <summary>
/// The house the player asks to found: its name and abbreviation, its home city, and the rules the era leaves
/// to him (null takes the first choice offered; a value the era forces may be given or left null).
/// </summary>
public sealed record PlayerFoundingSpec(
    string Name, string Abbreviation, CityId City,
    int? SingleRoundMinutes = null, GloveType? Gloves = null, Enclosure? Enclosure = null);

/// <summary>The free open tryout: whether he can call it, and what it is, in words.</summary>
public sealed record TryoutOffer(string? Refusal, int Turnout, string Detail)
{
    public bool Available => Refusal is null;
}

/// <summary>A man the tryout signed, as the player's scouts read him (never the truth).</summary>
public sealed record TryoutSigning(FighterId Fighter, string Name, int Age, WeightClass WeightClass, int ScoutedRead);

/// <summary>
/// Founding a promotion mid-game (Origins only): a player who began as an observer opens his own house on any
/// day he likes, even before the sport's story opens the door to newcomers. He gets what an AI newcomer of that
/// day gets (<see cref="OriginsFounding"/>): a city with a live scene that no ban has closed and no live house
/// calls home; the era's rules, choosing where the AI would roll (the single round's length and the gloves before
/// the sport has its own rules, the ring or the cage always) and taking what the era forces; one open division
/// with a vacant belt, or the weight lines the sport has drawn; a fixed pick inside the AI newcomer's bank range
/// (<see cref="BankSteps"/>) and prestige <see cref="Prestige"/>; the same popularity, tier clearance and hometown
/// sponsor. Unlike the AI he signs nobody at founding: his roster is empty, and he has ONE free open tryout of
/// local men (<see cref="HoldTryout"/>) whenever he calls it. His desks are empty (he hires his own back office),
/// his objectives run to the year's end, his inbox opens with a letter from the accountant. He is exempt from the
/// sport's ceiling on houses (<see cref="OriginsFounding.Ceiling"/>) but counts toward it from then on, and when
/// his house is the one that makes a "promotions at least" chapter happen, the story credits him.
///
/// Determinism: no draw on any existing stream. The sponsor pick comes off one keyed stream
/// (seed, founding day, salt), the tryout off another (seed, tryout day, salt). A world that never founds is
/// untouched.
/// </summary>
public static class PlayerFounding
{
    // Keyed streams of their own, with high bits unlike every other founding salt (OriginsFounding's are 0x0219_...),
    // so his founding on one day never shares a stream seed with an AI founding (or his tryout) on a nearby day.
    private const ulong FoundSalt = 0x5A4D_C3E1_0000_0001UL, TryoutSalt = 0x6B71_92F4_0000_0002UL;

    /// <summary>The player's pick inside the AI newcomer's bank range: two of <see cref="OriginsFounding.NewcomerBankSteps"/> steps.</summary>
    public const int BankSteps = 2;

    /// <summary>The player's prestige at founding (the AI newcomer rolls 4 to 8).</summary>
    public const int Prestige = 5;

    /// <summary>The local men the free open tryout signs.</summary>
    public const int TryoutTurnout = 6;

    /// <summary>
    /// The ledger line that records the free tryout: the durable mark that it has been held (<see cref="TryoutHeldOn"/>),
    /// a $0 <see cref="FinanceCategory.Other"/> entry. The ledger is the promotion's permanent, saved history and
    /// nothing trims it; that must stay so, because this line is the only record of the tryout (the men it signed
    /// can be released, retire or be bought away, and the news caps). Screens that sum money should skip $0 lines.
    /// </summary>
    public const string TryoutLedgerLine = "Open tryout (free)";

    /// <summary>Founded with fewer days than this left in the year, his objectives start on 1 January (the year's turn seeds them).</summary>
    public const int LateInYearDays = 70;

    /// <summary>The bank a house the player founds today opens with (an AI newcomer's, at <see cref="BankSteps"/>).</summary>
    public static Money Bank => new(OriginsFounding.NewcomerBank(BankSteps));

    /// <summary>
    /// The abbreviation a name suggests ("Lantern Street Open" gives "LSO"): its initials in plain letters (accents
    /// dropped), two to five of them, and never one <see cref="Found"/> would refuse: when the initials are taken
    /// (by a live or closed house, or one the story has still to bring) a letter, then a digit, is tried in the
    /// last place. Empty for a blank name.
    /// </summary>
    public static string SuggestAbbreviation(World world, string name)
    {
        var plain = new System.Text.StringBuilder();
        foreach (char ch in (name ?? "").Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            plain.Append(char.IsAsciiLetterOrDigit(ch) ? ch : ' ');
        }
        string folded = plain.ToString().Trim();
        if (folded.Length == 0) return "";
        var basis = new string(OriginsFounding.Initials(folded).Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToUpperInvariant).Take(5).ToArray());
        if (basis.Length < 2) basis = (basis + "XX")[..2];
        if (AbbreviationRefusal(world, basis) is null) return basis;
        string stem = basis[..2];
        foreach (char ch in Enumerable.Range('A', 26).Concat(Enumerable.Range('2', 8)).Select(i => (char)i))
            if (AbbreviationRefusal(world, stem + ch) is null) return stem + ch;
        for (int n = 10; n < 1000; n++)
            if (AbbreviationRefusal(world, stem + n) is null) return stem + n;
        return "";
    }

    /// <summary>The promotions the story has still to bring, and the founding three: their names and abbreviations are theirs.</summary>
    private static IEnumerable<(string Name, string Abbreviation, bool Arrival)> Reserved() =>
        OriginsArrivals.All.Where(a => a.Kind == OriginsArrivalKind.Promotion)
            .Select(a => (a.Name, a.Abbreviation ?? OriginsFounding.Initials(a.Name), true))
            .Concat(OriginsPromotions.All.Select(sp => (sp.Name, sp.Abbreviation, false)));

    /// <summary>Why a name can't be his (null when it can): another house's, or kept for one the story has still to bring.</summary>
    private static string? NameRefusal(World world, string name)
    {
        if (world.Promotions.Values.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"There is already a promotion called {name}.";
        foreach (var r in Reserved())
            if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                return r.Arrival
                    ? $"{r.Name} is a promotion the sport's story has still to bring; the name is spoken for."
                    : $"{r.Name} is one of the sport's founding promotions.";
        return null;
    }

    /// <summary>Why an abbreviation can't be his (null when it can): its shape, another house's, or kept for one still to come.</summary>
    private static string? AbbreviationRefusal(World world, string abbr)
    {
        if (abbr.Length is < 2 or > 5 || !abbr.All(char.IsAsciiLetterOrDigit))
            return "The abbreviation is two to five letters or digits.";
        if (world.Promotions.Values.Any(p => string.Equals(p.Abbreviation, abbr, StringComparison.OrdinalIgnoreCase)))
            return $"{abbr} is already another promotion's abbreviation.";
        foreach (var r in Reserved())
            if (string.Equals(r.Abbreviation, abbr, StringComparison.OrdinalIgnoreCase))
                return r.Arrival
                    ? $"{abbr} belongs to {r.Name}, a promotion the sport's story has still to bring."
                    : $"{abbr} belongs to {r.Name}, one of the sport's founding promotions.";
        return null;
    }

    // ------------------------------------------------------------------ reading the choices

    /// <summary>Why he cannot found anything today (null when he can): today's sport, or a house already his.</summary>
    public static string? Unavailable(World world)
    {
        if (world.Mode != WorldMode.Origins) return "Founding a house mid-game is part of an Origins start; today's sport is founded at the start screen.";
        if (world.HasPlayer)
            return world.Get(world.PlayerPromotion) is { } mine ? $"You already run {mine.Name}." : "You already run a promotion.";
        return null;
    }

    /// <summary>What founding today would mean: cities (with honest reasons), rules (chosen or forced), bank, prestige, divisions.</summary>
    public static FoundingChoices Choices(World world, DateOnly date)
    {
        var homes = HomesOfLiveHouses(world);
        var scenes = Era.Scenes(world).ToHashSet(StringComparer.Ordinal);
        var general = RulesFor(world, secondCentre: false);
        var centre = OriginsScenes.SecondCentre(world);
        var centreRules = centre is null ? general : RulesFor(world, secondCentre: true);
        var cities = world.Cities.Values.OrderBy(c => c.Id.Value)
            .Select(c =>
            {
                var k = world.Get(c.Country);
                return new FoundingCity(c.Id, c.Name, k?.Name ?? "", k?.Code ?? "", c.Region, c.Market, c.MmaInterest,
                    CityRefusal(world, c, date, homes, scenes), centre is not null && k?.Code == centre ? centreRules : general);
            }).ToList();

        var lines = OriginsFounding.DrawnLines(world);
        var notes = new List<string>(general.Notes);
        if (centre is not null)
            notes.Add($"Houses founded in {Geography.WorldMap.CountryByCode(world, centre)?.Name ?? centre}, the sport's second centre, fight in a ring and keep their own rules.");
        notes.Add(lines.Count == 0
            ? "No weight classes yet: one open division, a vacant belt, any man can fight any man."
            : $"The sport has drawn its weight lines: {string.Join(", ", lines.Select(OriginsReforms.Name).Select(s => s.ToLowerInvariant()))}, each with a vacant belt.");
        notes.Add($"You open with {Bank} in the bank, no fighters signed and one free open tryout of local men to call when you are ready.");

        return new FoundingChoices(
            Unavailable(world), cities, general.RoundsAndJudges, general.SingleRoundMinutes, general.Gloves, general.Enclosures,
            Bank, Prestige, lines, notes, general.UnifiedCode, centre);
    }

    /// <summary>The rules a house founded today in this city fights under (chosen or forced), with the reasons.</summary>
    public static FoundingRules RulesIn(World world, CityId city) => RulesFor(world, InSecondCentre(world, city));

    private static bool InSecondCentre(World world, CityId city) =>
        OriginsScenes.SecondCentre(world) is { } code && world.Get(city) is { } c && world.Get(c.Country)?.Code == code;

    /// <summary>
    /// The founding rules, mirroring what the AI newcomer's founding does: its era rules (OriginsFounding.EraRules,
    /// where he chooses what the AI rolls), then OriginsReforms.FoundedUnder (a ring in the second centre, which keeps
    /// its own rules; else the unified code once the commissions have one).
    /// </summary>
    private static FoundingRules RulesFor(World world, bool secondCentre)
    {
        bool code = !secondCentre && world.Mode == WorldMode.Origins && world.Era.HasFired(OriginsReforms.Unified);
        bool rounds = code || RoundsForced(world), gloves = GlovesForced(world);
        var notes = new List<string>();
        if (code)
            notes.Add($"The commissions have one code now, and every new house comes under it: {OriginsReforms.CardRounds} rounds of five minutes a bout, "
                      + $"{OriginsReforms.UnifiedTitleRounds} for a title, three judges, today's gloves. Only the cage or the ring is yours to choose.");
        else
        {
            notes.Add(rounds
                ? "The sport fights timed rounds with judges now: every house fights three rounds of five minutes."
                : "Before the sport has rounds, a fight is one long round with no judges: you choose how long it runs.");
            notes.Add(gloves
                ? "Gloves are the sport's rule now: every man wears the thin open-fingered glove."
                : "Gloves are not the sport's rule yet: bare hands, or each man chooses for himself.");
        }
        if (secondCentre)
            notes.Add("The second centre fights in a ring, as its own big houses do, and keeps its own rules; the commissions' code does not bind it.");
        return new FoundingRules(
            rounds, code,
            rounds ? Array.Empty<int>() : OriginsFounding.SingleRoundMinutes,
            code ? new[] { new FightRules().Gloves } : gloves ? new[] { GloveType.ThinOpenFinger } : new[] { GloveType.Bare, GloveType.Optional },
            secondCentre ? new[] { Enclosure.Ring } : new[] { Enclosure.Cage, Enclosure.Ring },
            code ? OriginsReforms.CardRounds : rounds ? 3 : 1,
            code ? OriginsReforms.UnifiedTitleRounds : rounds ? 3 : 1,
            notes);
    }

    /// <summary>Do new houses fight timed rounds with judges (the sport has made them its rules)?</summary>
    private static bool RoundsForced(World world) => world.Era.IsUnlocked(Capability.Rounds);

    /// <summary>Do new houses put every man in gloves?</summary>
    private static bool GlovesForced(World world) => world.Era.IsUnlocked(Capability.Gloves);

    private static Dictionary<CityId, Promotion> HomesOfLiveHouses(World world)
    {
        var homes = new Dictionary<CityId, Promotion>();
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct && p.HomeCity is not null).OrderBy(p => p.Id.Value))
            homes.TryAdd(p.HomeCity!.Value, p);
        return homes;
    }

    /// <summary>
    /// Why a house cannot open in this city today (null when it can): the AI newcomer's rule
    /// (<see cref="OriginsFounding.FoundableCities"/>), with the reason in words.
    /// </summary>
    private static string? CityRefusal(World world, City c, DateOnly date, Dictionary<CityId, Promotion> homes, HashSet<string> scenes)
    {
        var k = world.Get(c.Country);
        if (k is null || !scenes.Contains(k.Code))
        {
            string where = k?.Name ?? "That country";
            return k is not null && OriginsTimeline.ContestedScene(world, k.Code, date)
                ? $"{where} has no scene of its own yet. A first night there could wake it, but a house needs gyms, a crowd and fighters at home."
                : $"{where} has no MMA scene yet: no gyms, no crowd, no fighters to sign.";
        }
        if (Era.Banned(world, c.Id, date)) return $"The Backlash has closed the halls in {c.Name}: no house can open there while the ban lasts.";
        if (homes.TryGetValue(c.Id, out var house)) return $"{house.Name} already calls {c.Name} home.";
        return null;
    }

    // ------------------------------------------------------------------ founding

    /// <summary>
    /// Found the player's house today. Null, with the reason in words, when it cannot be founded: not an Origins
    /// world, a house already his, a name or abbreviation that is missing, malformed or taken, a city he may not
    /// found in (<see cref="Choices"/> says why), or a rule the era does not allow.
    /// </summary>
    public static Promotion? Found(World world, DateOnly date, PlayerFoundingSpec spec, out string? refused)
    {
        refused = Validate(world, date, spec, out var name, out var abbr, out var city, out var rules);
        if (refused is not null) return null;
        var country = world.Get(city!.Country)!;

        var promo = new Promotion
        {
            Id = world.NewPromotionId(),
            Name = name,
            Abbreviation = abbr,
            BaseRegion = city.Region,
            IsPlayerControlled = true,
            Balance = Bank,
            Prestige = Prestige,
            Reputation = 50,
            HomeCity = city.Id,
            FoundedYear = date.Year,
        };
        promo.Rules.Fight = rules!;
        promo.Rules.RoundMinutes = rules!.RoundMinutes ?? 5;
        promo.Rules.StandardRounds = rules.Rounds ?? 3;
        promo.Rules.TitleRounds = promo.Rules.StandardRounds;
        OriginsReforms.FoundedUnder(world, promo);   // as the AI newcomer: the unified code once there is one; a ring in the second centre
        promo.TierClearance = Org.OrgSizing.SizeOf(promo);
        int aliveBefore = world.Promotions.Values.Count(p => !p.Defunct);
        world.Promotions[promo.Id] = promo;
        world.PlayerPromotion = promo.Id;

        OriginsFounding.SeedPopularity(world, promo, city, Prestige);
        OriginsFounding.OpenDivisions(world, promo, Prestige, date);
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)date.DayNumber ^ FoundSalt));
        Finance.Sponsorships.SeedFounding(world, promo, date, rng);
        RankingService.RecomputePromotion(world, promo, date);
        // His objectives run to the year's end; founded in its last weeks they start on 1 January instead (the year's
        // turn seeds them, as it does every player's), so he is never set a target he cannot meet.
        bool late = LateInYear(date);
        if (!late) Org.Objectives.Seed(promo, date, new DateOnly(date.Year, 12, 31), world);

        News.Post(world, date, NewsCategory.Finance, $"{name} opens in {city.Name}",
            $"A new promotion has opened in {city.Name}, {country.Name}, with {OriginsFounding.Describe(promo)}. Nobody is signed yet.",
            promo.Id);
        Mail.Post(world, date, MailCategory.BoardNote, "The Accountant", $"{name} is open for business", Welcome(world, promo, city, country, late));
        TellOpenReforms(world, promo, date);

        CreditChapters(world, date, aliveBefore);
        return promo;
    }

    /// <summary>Everything the founding checks, in the order the screen would want them answered.</summary>
    private static string? Validate(World world, DateOnly date, PlayerFoundingSpec spec, out string name, out string abbr,
        out City? city, out FightRules? rules)
    {
        name = (spec.Name ?? "").Trim();
        abbr = (spec.Abbreviation ?? "").Trim().ToUpperInvariant();
        city = null;
        rules = null;
        if (Unavailable(world) is { } no) return no;

        if (name.Length < 3) return "Give the promotion a name of at least three letters.";
        if (name.Length > 40) return "Keep the name to forty characters or fewer.";
        if (NameRefusal(world, name) is { } badName) return badName;
        if (AbbreviationRefusal(world, abbr) is { } badAbbr) return badAbbr;

        if (world.Get(spec.City) is not { } c) return "Pick a city for the promotion's home.";
        if (CityRefusal(world, c, date, HomesOfLiveHouses(world), Era.Scenes(world).ToHashSet(StringComparer.Ordinal)) is { } why)
            return why;
        city = c;

        var allowed = RulesIn(world, c.Id);
        if (allowed.UnifiedCode)
        {
            // The code leaves him the enclosure only (FoundedUnder writes the rest after his era rules are set).
            if (spec.SingleRoundMinutes is { } cm && cm != 5)
                return $"The commissions' code binds every new house: {OriginsReforms.CardRounds} rounds of five minutes a bout, {OriginsReforms.UnifiedTitleRounds} for a title.";
            if (spec.Gloves is { } cg && !allowed.Gloves.Contains(cg))
                return "The commissions' code binds every new house: today's gloves for every man.";
        }
        if (spec.Enclosure is { } want && want is Enclosure.Cage or Enclosure.Ring && !allowed.Enclosures.Contains(want))
            return "Houses in the sport's second centre fight in a ring.";
        bool rounds = RoundsForced(world), gloves = GlovesForced(world);
        int minutes;
        if (allowed.UnifiedCode && !rounds) minutes = OriginsFounding.SingleRoundMinutes[0];   // (overwritten by the code)
        else if (rounds)
        {
            if (spec.SingleRoundMinutes is { } m && m != 5)
                return "The sport fights three rounds of five minutes with judges now; a single long round is a thing of the past.";
            minutes = 5;
        }
        else
        {
            minutes = spec.SingleRoundMinutes ?? OriginsFounding.SingleRoundMinutes[0];
            if (!OriginsFounding.SingleRoundMinutes.Contains(minutes))
                return $"Before the sport has rounds, a fight is one round of {string.Join(" or ", OriginsFounding.SingleRoundMinutes)} minutes.";
        }
        GloveType glove;
        if (allowed.UnifiedCode) glove = gloves ? GloveType.ThinOpenFinger : GloveType.Bare;   // (overwritten by the code)
        else if (gloves)
        {
            glove = spec.Gloves ?? GloveType.ThinOpenFinger;
            if (glove != GloveType.ThinOpenFinger) return "Gloves are the sport's rule now: every man wears the thin open-fingered glove.";
        }
        else
        {
            glove = spec.Gloves ?? GloveType.Bare;
            if (glove is not (GloveType.Bare or GloveType.Optional))
                return "Gloves are not the sport's rule yet: bare hands, or each man's choice. A house can adopt gloves once the sport asks for them.";
        }
        var enclosure = spec.Enclosure ?? allowed.Enclosures[0];
        if (enclosure is not (Enclosure.Cage or Enclosure.Ring)) return "A house fights in a cage or a ring.";

        rules = OriginsFounding.NewcomerRules(rounds, minutes, glove, enclosure);
        return null;
    }

    /// <summary>The accountant's first letter: the money, the empty roster, the tryout, the year's asks.</summary>
    private static string Welcome(World world, Promotion promo, City city, Country country, bool late)
    {
        var r = promo.Rules.Fight!;
        var hr = promo.Rules;
        string time = OriginsReforms.HasUnified(promo)
            ? $"the commissions' code, {hr.StandardRounds} rounds of {hr.RoundMinutes} minutes a bout and {hr.TitleRounds} for a title, with judges"
            : r.Rounds is > 1 ? $"{hr.StandardRounds} rounds of {hr.RoundMinutes} minutes with judges" : $"one round of {hr.RoundMinutes} minutes, no judges";
        string hands = r.Gloves switch
        {
            GloveType.Bare => "bare hands",
            GloveType.Optional => "gloves if a man wants them",
            GloveType.Modern => "today's gloves",
            _ => "thin open-fingered gloves",
        };
        string where = r.Enclosure == Enclosure.Ring ? "a ring" : "a cage";
        int shows = promo.Objectives.FirstOrDefault(o => o.Kind == ObjectiveKind.RunEvents)?.Target ?? 1;
        return $"The books are open: {promo.Balance} in the bank, a home in {city.Name}, {country.Name}, and {OriginsFounding.Describe(promo)}. "
             + $"House rules: {time}, {hands}, {where}. "
             + "Nobody is signed. We have one open tryout of local men, free, whenever you call it; the rest come off the free-agent market at their asking terms. "
             + (late
                 ? $"There's too little of {promo.FoundedYear} left to judge us on it: the owner's targets start on 1 January."
                 : $"For the rest of {promo.FoundedYear} I'd like {shows} show{(shows == 1 ? "" : "s")} and the year finished in the black.");
    }

    /// <summary>Too few days of the year left to set him a year's objectives (<see cref="LateInYearDays"/>).</summary>
    public static bool LateInYear(DateOnly date) => new DateOnly(date.Year, 12, 31).DayNumber - date.DayNumber + 1 < LateInYearDays;

    /// <summary>
    /// The rule reforms already open to his house on the day he founds it. The monthly desk note
    /// (OriginsReforms.TellThePlayer) tells only reforms that opened since the previous month's turn, so a reform
    /// open before then would never reach him: those are listed here, once. Any that opened since are told at the
    /// next month's turn as usual, so nothing is told twice.
    /// </summary>
    private static void TellOpenReforms(World world, Promotion promo, DateOnly date)
    {
        var next = date.AddDays(7);
        while (next.AddDays(-7).Month == next.Month) next = next.AddDays(7);
        var told = OriginsBacklash.PreviousMonthlyTick(next);
        var open = OriginsReforms.Options(world, promo, date).Where(o => OriginsReforms.OpenOn(world, o.Chapter, told)).ToList();
        if (open.Count == 0) return;
        Mail.Post(world, date, MailCategory.BoardNote, "The Accountant", "House rules we could change",
            "The sport has moved on since our rules were written. " + string.Join(" ", open.Select(o => $"{o.Label}: {o.Detail}"))
            + " It's our call; nobody will make it for us.");
    }

    /// <summary>
    /// A live "promotions at least" chapter whose count this house's opening reaches fires now, with the doer the
    /// weekly detection would name (<see cref="OriginsTimeline.CountDoer"/>, one rule for both). That is his house
    /// by construction: it is the newest, so its place in id order is the last, at or past the count's last place.
    /// A count already met before he opened is left to the weekly detection.
    /// </summary>
    private static void CreditChapters(World world, DateOnly date, int aliveBefore)
    {
        foreach (var c in OriginsChapters.All)
        {
            if (c.Structured.Condition is not ("PromotionsAtLeast" or "PromotionsOnTheMapAtLeast")) continue;
            if (aliveBefore >= c.Structured.Value || !OriginsTimeline.IsLive(world, c, date)) continue;
            if (OriginsTimeline.CountDoer(world, c.Structured.Value) is { } doer)
                OriginsTimeline.FireNow(world, c.Key, doer, date);
        }
    }

    // ------------------------------------------------------------------ the open tryout

    /// <summary>The day the free tryout was held, or null (read off the ledger: the durable mark).</summary>
    public static DateOnly? TryoutHeldOn(Promotion promo) =>
        promo.Ledger.FirstOrDefault(e => e.Category == FinanceCategory.Other && e.Description == TryoutLedgerLine)?.Date;

    /// <summary>Can he call the free open tryout today, and what is it?</summary>
    public static TryoutOffer TryoutOptions(World world, DateOnly date)
    {
        string detail = $"One free open tryout: about {TryoutTurnout} local men who have trained for this turn up, and every one who fits a division signs on honest minimum terms (the sport's purse floor, three fights). Green, cheap and yours.";
        return new TryoutOffer(TryoutRefusal(world), TryoutTurnout, detail);
    }

    private static string? TryoutRefusal(World world)
    {
        if (world.Mode != WorldMode.Origins) return "The open tryout belongs to a house founded in an Origins world.";
        if (world.Get(world.PlayerPromotion) is not { } mine) return PlayerBooking.NoHouseText;
        if (mine.Defunct) return "The promotion has closed.";
        if (TryoutHeldOn(mine) is { } on) return $"You held your one free open tryout on {on:d MMMM yyyy}.";
        return null;
    }

    /// <summary>
    /// Hold the free open tryout today: local men of the home country, green and glad of the chance, each signed on
    /// the tryout terms when the house has a division for him; the house is their discoverer. Once only. Returns the
    /// men signed, as his scouts read them (empty, with the reason, when refused).
    /// </summary>
    public static IReadOnlyList<TryoutSigning> HoldTryout(World world, DateOnly date, out string? refused)
    {
        refused = TryoutRefusal(world);
        if (refused is not null) return Array.Empty<TryoutSigning>();
        var promo = world.Get(world.PlayerPromotion)!;
        var home = promo.HomeCity is { } hc ? world.Get(hc) : null;
        string code = home is not null && world.Get(home.Country) is { } k ? k.Code : "USA";

        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)date.DayNumber ^ TryoutSalt));
        var gen = FighterGenerator.ForWorld(world);
        var signed = new List<Fighter>();
        for (int guard = 0; signed.Count < TryoutTurnout && guard < TryoutTurnout * 2; guard++)
            if (OriginsFounding.TryoutMan(world, gen, promo, code, date, rng) is { } man)
            {
                man.Accolades.Add(new Accolade(date, $"Signed at {promo.Name}'s open tryout"));
                signed.Add(man);
            }
        Finance.Ledger.Record(promo, date, FinanceCategory.Other, new Money(0m), TryoutLedgerLine);
        RankingService.RecomputePromotion(world, promo, date);

        var reads = signed.Select(f => new TryoutSigning(f.Id, f.FullName, f.AgeOn(date), f.WeightClass,
            Scouting.ScoutingService.PerceivedAbility(world, promo, f))).ToList();
        Mail.Post(world, date, MailCategory.General, "Matchmaking", "The open tryout",
            signed.Count == 0
                ? "Nobody we could use turned up. The free-agent market is where the fighters are."
                : $"{signed.Count} local men signed on the tryout terms: {string.Join("; ", reads.Select(r => $"{r.Name} ({r.Age})"))}. Green, all of them. Book them carefully.");
        return reads;
    }
}
