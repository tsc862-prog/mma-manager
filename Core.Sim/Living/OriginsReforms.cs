using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation.Origins;

namespace MmaManager.Core.Sim.Living;

/// <summary>A change of house rules a promotion can make now (Origins): the chapter it belongs to and what it does, in words.</summary>
public sealed record ReformOption(string Chapter, string Label, string Detail);

/// <summary>
/// The rule reforms of an Origins world: a house trades the single long round for timed rounds and judges,
/// puts every man in gloves, and splits its open division by weight, one line at a time (heavy and light
/// first, the full ladder over the years). Each reform is open once its chapter is (live or fired); the
/// first house to make it IS the chapter for rounds and gloves, and the first champion crowned in a new
/// class is the chapter for a weight line (<see cref="OriginsTimeline"/>). AI houses decide on a keyed
/// monthly roll (pressed harder when the Backlash has closed their home); the player decides through
/// <see cref="Options"/> and <see cref="Adopt"/>, and the AI never decides for him.
///
/// A split keeps every man in his natural class: divisions become BANDS (<see cref="Division.Lightest"/>),
/// the lightest band taking everyone under the first line (the light division is "middleweight" at first).
/// The open-weight belt is retired honestly (its last reign ends vacated and the news says so), every line
/// gets a vacant belt, and a champion a new line leaves outside his belt's band gives it up and starts at
/// the top of the band he now belongs to.
/// </summary>
public static class OriginsReforms
{
    public const string Rounds = "rounds-and-judges";
    public const string Gloves = "gloves-adopted";

    /// <summary>
    /// The commissions' one code (Era 4's chapter): today's rules, every field (<see cref="FightRules.Modern"/>:
    /// five-minute rounds, three judges, today's gloves, no rope escapes), three rounds a bout and five for a
    /// title. Once it fires every house whose home region a commission licenses fights under it (the second
    /// centre's houses keep their own: <see cref="OriginsScenes"/>), any other house may adopt it, and the
    /// sport's rankings across promotions are recognised (<see cref="RankingsRecognised"/>).
    /// </summary>
    public const string Unified = "unified-rules";

    /// <summary>The rounds of a title bout under the unified code.</summary>
    public const int UnifiedTitleRounds = 5;

    /// <summary>The weight-line chapters, in the order a house adopts them.</summary>
    public static readonly string[] Ladder =
        { "first-weight-split", "weight-ladder-middle", "weight-ladder-lower", "weight-ladder-bantam", "weight-ladder-flyweight" };

    private const ulong AdoptSalt = 0x0219_AD07_0003UL;

    /// <summary>The rounds a card house fights once it adopts rounds (every bout, title bouts too).</summary>
    public const int CardRounds = 3;

    /// <summary>The rounds a bracket house fights once it adopts rounds: its man fights three times a night.</summary>
    public const int BracketRounds = 2;

    /// <summary>How much likelier a house the Backlash has closed at home is to adopt the rules the critics ask for, a month.</summary>
    public const double BannedPressure = 0.10;

    /// <summary>The men's classes a weight-line chapter draws (strawweight is the women's: never a men's line).</summary>
    public static IReadOnlyList<WeightClass> LinesOf(string chapter) =>
        OriginsChapters.ByKey(chapter).Unlocks
            .Where(u => u.StartsWith("WeightClass:", StringComparison.Ordinal))
            .Select(u => Enum.Parse<WeightClass>(u["WeightClass:".Length..]))
            .Where(wc => wc != WeightClass.Strawweight)
            .OrderBy(wc => wc).ToList();

    /// <summary>Does the house fight timed rounds with judges at the limit (today's rules count)?</summary>
    public static bool HasRounds(Promotion p) =>
        p.Rules.Fight is not { } r || ((r.Rounds is null || r.Rounds > 1) && r.Judges);

    /// <summary>Does the house put every man in gloves (today's rules count)?</summary>
    public static bool HasGloves(Promotion p) =>
        p.Rules.Fight is not { } r || r.Gloves is GloveType.Modern or GloveType.ThinOpenFinger;

    /// <summary>Does the house fight under the unified code (today's rules in every field but its weight and its ring)?</summary>
    public static bool HasUnified(Promotion p) =>
        p.Rules.Fight is not { } r || (r with { OpenWeight = false, Enclosure = Enclosure.Cage }).IsModern;

    /// <summary>
    /// Is the house bound by the commissions' code: the code is the sport's, its home region is licensed, and
    /// it is not a house of the second centre (which keeps its own rules)? Never in today's mode.
    /// </summary>
    public static bool BoundByCode(World world, Promotion p)
    {
        if (world.Mode != WorldMode.Origins || p.Defunct || !world.Era.HasFired(Unified)) return false;
        if (OriginsScenes.InSecondCentre(world, p)) return false;
        if (p.HomeCity is not { } hc || world.Get(hc) is not { } city || world.Get(city.Country) is not { } country) return false;
        return world.Era.Sanctions.Any(s => s.Country == country.Code && (s.Area is null || s.Area == city.Area));
    }

    /// <summary>
    /// Are the sport's rankings across promotions recognised (the cross-promotion and pound-for-pound lists
    /// are published)? Always in today's mode; in an Origins world once the commissions have one code. Before
    /// then each house ranks its own men and nobody else's list means anything.
    /// </summary>
    public static bool RankingsRecognised(World world) =>
        world.Mode != WorldMode.Origins || world.Era.HasFired(Unified);

    /// <summary>Has the house drawn every line this weight chapter draws?</summary>
    public static bool HasLines(Promotion p, string chapter) =>
        LinesOf(chapter).All(wc => p.Divisions.Any(d => !d.Open && d.Sex == Sex.Male && d.WeightClass == wc));

    /// <summary>
    /// A house that crowns champions: a card of single bouts, or a bracket house (which gives up its
    /// brackets for cards when it splits by weight, as the bracket houses did). A gym challenge never splits.
    /// </summary>
    private static bool BeltHouse(Promotion p) =>
        p.Rules.ShowFormat is ShowFormat.Card or ShowFormat.OneNightTournament
        && p.Divisions.Any(d => d.Sex == Sex.Male && d.Title is not null);

    /// <summary>The reforms this house can make today, in the order a house makes them (empty in today's mode).</summary>
    public static IReadOnlyList<ReformOption> Options(World world, Promotion p, DateOnly date)
    {
        var list = new List<ReformOption>();
        if (world.Mode != WorldMode.Origins || p.Defunct) return list;
        if (!HasRounds(p) && OriginsTimeline.IsOpen(world, Rounds, date))
            list.Add(new(Rounds, "Rounds and judges", "Five-minute rounds with a minute's rest; three judges score a fight that goes the distance."));
        if (!HasGloves(p) && OriginsTimeline.IsOpen(world, Gloves, date))
            list.Add(new(Gloves, "Gloves for everyone", "Every man wears the same thin, open-fingered glove."));
        if (!HasUnified(p) && world.Era.HasFired(Unified) && !BracketBooked(world, p))
            list.Add(new(Unified, "The unified rules", UnifiedDetail(p)));
        if (BeltHouse(p) && HasRounds(p) && HasGloves(p))
            foreach (var chapter in Ladder)
            {
                if (HasLines(p, chapter)) continue;
                if (OriginsTimeline.IsOpen(world, chapter, date)
                    && OriginsChapters.ByKey(chapter).Requires.Where(r => Ladder.Contains(r)).All(r => HasLines(p, r)))
                    list.Add(new(chapter, LineLabel(chapter), LineDetail(p, chapter)));
                break;   // one line at a time
            }
        return list;
    }

    private static string UnifiedDetail(Promotion p) =>
        $"The commissions' code: five-minute rounds, three judges, today's gloves and no rope escapes; {CardRounds} rounds a bout, {UnifiedTitleRounds} for a title."
        + (p.Rules.ShowFormat == ShowFormat.OneNightTournament ? " The house gives up its one-night brackets for cards of single bouts." : "");

    private static string LineLabel(string chapter) => chapter == Ladder[0]
        ? "Split heavy and light"
        : "Add " + string.Join(" and ", LinesOf(chapter).Select(Name)) + " divisions";

    private static string LineDetail(Promotion p, string chapter)
    {
        var bands = BandsAfter(p, chapter);
        string lines;
        if (bands.Count == 2)
            lines = $"Men over {bands[0].Top} pounds fight at {Name(bands[1].Class).ToLowerInvariant()}, everyone else at {Name(bands[0].Class).ToLowerInvariant()}";
        else
            lines = "The divisions will be " + string.Join(", ", bands.Select((b, i) =>
                $"{Name(b.Class).ToLowerInvariant()} {(i == 0 ? $"up to {b.Top}" : i == bands.Count - 1 ? $"over {bands[i - 1].Top}" : $"{bands[i - 1].Top + 1} to {b.Top}")} pounds"));
        string bracket = p.Rules.ShowFormat == ShowFormat.OneNightTournament
            ? $" The house gives up its one-night brackets for cards of single bouts, every bout over {CardRounds} rounds." : "";
        return chapter == Ladder[0]
            ? $"The open-weight belt is retired.{bracket} {lines}; each division gets a vacant belt."
            : $"{lines}. Each new division gets a vacant belt; men move to the division their natural weight puts them in.{bracket}";
    }

    /// <summary>
    /// The men's divisions this house would run after drawing a chapter's lines, lightest first, each with the
    /// heaviest natural weight it takes (<see cref="WeightClasses.LimitLbs"/>); a band takes everyone over the
    /// line below it, the lightest takes everyone under its own, the heaviest has no top.
    /// </summary>
    public static IReadOnlyList<(WeightClass Class, int Top)> BandsAfter(Promotion p, string chapter) =>
        p.Divisions.Where(d => !d.Open && d.Sex == Sex.Male).Select(d => d.WeightClass)
            .Concat(LinesOf(chapter)).Distinct().OrderBy(wc => wc)
            .Select(wc => (wc, WeightClasses.LimitLbs(wc))).ToList();

    /// <summary>
    /// Make a reform at this house now. False when it is not one of <see cref="Options"/> today. Rounds and
    /// gloves fire their chapter when it is live (this house was first); a weight line fires its chapter
    /// when a champion is crowned in it.
    /// </summary>
    public static bool Adopt(World world, Promotion p, string chapter, DateOnly date) =>
        TryAdopt(world, p, chapter, date, out _);

    /// <summary>
    /// Why this house cannot make this reform today, in words for the screen; null when it can (it is one
    /// of <see cref="Options"/>).
    /// </summary>
    public static string? Refusal(World world, Promotion p, string chapter, DateOnly date)
    {
        if (Options(world, p, date).Any(o => o.Chapter == chapter)) return null;
        if (world.Mode != WorldMode.Origins) return "Today's sport has settled its rules.";
        if (p.Defunct) return "The promotion has closed.";
        bool weight = Ladder.Contains(chapter);
        if (chapter != Rounds && chapter != Gloves && chapter != Unified && !weight) return "That is not a rule a house can change.";
        if (chapter == Rounds && HasRounds(p)) return "You already fight in rounds with judges.";
        if (chapter == Gloves && HasGloves(p)) return "Every man on your cards already wears gloves.";
        if (chapter == Unified && HasUnified(p)) return "You already fight under the commissions' code.";
        if (chapter == Unified && world.Era.HasFired(Unified) && BracketBooked(world, p))
            return "Your one-night bracket on the books was announced under your own rules and runs as announced: take up the code after it is fought.";
        if (chapter == Unified) return "There is no one code yet: every house and every commission still writes its own rules.";
        if (weight && HasLines(p, chapter)) return "You have already drawn those weight lines.";
        if (weight && p.Rules.ShowFormat == ShowFormat.GymChallenge) return "Challenge nights settle arguments between gyms; there are no belts to split.";
        if (weight && !BeltHouse(p))
            return p.Rules.ShowFormat == ShowFormat.OneNightTournament
                ? "Your brackets crown a night's winner, not a champion: weight lines divide a belt, and the house has no men's belt to split."
                : "Weight lines divide a belt, and the house has no men's belt to split.";
        if (weight && (!HasRounds(p) || !HasGloves(p))) return "Adopt rounds, judges and gloves first: nobody splits a sport that still fights to a standstill bare-handed.";
        if (weight && OriginsChapters.ByKey(chapter).Requires.Where(r => Ladder.Contains(r)).Any(r => !HasLines(p, r)))
            return "One line at a time: draw the earlier weight lines first.";
        return "The sport isn't ready for that yet: nobody is asking for it.";
    }

    /// <summary>As <see cref="Adopt"/>, with the reason in words when it refuses (<see cref="Refusal"/>).</summary>
    public static bool TryAdopt(World world, Promotion p, string chapter, DateOnly date, out string? refused)
    {
        refused = Refusal(world, p, chapter, date);
        if (refused is not null) return false;
        var rules = p.Rules.Fight ?? FightRules.Modern;
        switch (chapter)
        {
            case Rounds:
            {
                int rounds = p.Rules.ShowFormat == ShowFormat.OneNightTournament ? BracketRounds : CardRounds;
                p.Rules.Fight = rules with { Rounds = rounds, RoundMinutes = 5, Judges = true };
                p.Rules.StandardRounds = rounds;
                p.Rules.TitleRounds = rounds;
                p.Rules.RoundMinutes = 5;
                News.Post(world, date, NewsCategory.General, $"{p.Name} adopts rounds and judges",
                    $"From the next night, {p.Abbreviation} fights {rounds} rounds of five minutes with a minute's rest, and three judges score any fight that reaches the limit.", p.Id);
                OriginsTimeline.FireNow(world, Rounds, new ChapterDoer(p.Id, City: p.HomeCity), date);
                break;
            }
            case Gloves:
                p.Rules.Fight = rules with { Gloves = GloveType.ThinOpenFinger };
                News.Post(world, date, NewsCategory.General, $"{p.Name} makes gloves compulsory",
                    $"Every man on a {p.Abbreviation} card will wear the same thin, open-fingered glove.", p.Id);
                OriginsTimeline.FireNow(world, Gloves, new ChapterDoer(p.Id, City: p.HomeCity), date);
                break;
            case Unified:
                TakeUnified(world, p, date, bound: false);
                break;
            default:
                if (p.Rules.ShowFormat == ShowFormat.OneNightTournament)
                {
                    // Eight men of every size in one night was the open-weight idea; weight classes end it.
                    // (A bracket already on the books is the house's last: it runs as announced, with no belt.)
                    p.Rules.ShowFormat = ShowFormat.Card;
                    // A bracket's short rounds were for a man fighting three times a night; a card house fights
                    // one bout a night, at the card standard (a split needs rounds, so the house has them).
                    bool longer = p.Rules.Fight is { Rounds: { } had } && had < CardRounds;
                    if (longer) TakeCardRounds(p);
                    News.Post(world, date, NewsCategory.General, $"{p.Name} ends its one-night brackets",
                        $"No more eight-man nights: {p.Abbreviation} will run cards of single bouts, a champion in each weight class"
                        + (longer ? $", every bout over {CardRounds} rounds." : "."), p.Id);
                }
                DrawLines(world, p, LinesOf(chapter), date);
                break;
        }
        return true;
    }

    /// <summary>A house that has become a card house fights the card standard: <see cref="CardRounds"/> for every bout.</summary>
    private static void TakeCardRounds(Promotion p)
    {
        if (p.Rules.Fight is { } rules) p.Rules.Fight = rules with { Rounds = CardRounds };
        p.Rules.StandardRounds = CardRounds;
        p.Rules.TitleRounds = CardRounds;
    }

    /// <summary>
    /// A house takes the unified code: today's rules in every field (its weight lines and its ring or fence stay),
    /// three rounds a bout and five for a title; a bracket house becomes a card house. Every card it already has
    /// on the books is rewritten to the code (the round count is the booking's under the code, and round length is
    /// the house's at fight time, so a card left alone would be fought as the old rounds at five minutes). A house
    /// with a one-night bracket on the books takes the code only after the bracket is fought
    /// (<see cref="BracketBooked"/>): a bracket runs as announced. <paramref name="bound"/>: the commission
    /// requires it (the player is told it is no longer his call).
    /// </summary>
    private static void TakeUnified(World world, Promotion p, DateOnly date, bool bound)
    {
        var had = p.Rules.Fight ?? FightRules.Modern;
        p.Rules.Fight = new FightRules { OpenWeight = had.OpenWeight, Enclosure = had.Enclosure };
        p.Rules.StandardRounds = CardRounds;
        p.Rules.TitleRounds = UnifiedTitleRounds;
        p.Rules.RoundMinutes = 5;
        if (p.Rules.ShowFormat == ShowFormat.OneNightTournament) p.Rules.ShowFormat = ShowFormat.Card;
        int rewritten = RewriteBookedCards(world, p, date);
        News.Post(world, date, NewsCategory.General, $"{p.Name} fights under the unified rules",
            bound
                ? $"The commission that licenses {p.Abbreviation} holds every house it licenses to one code: {CardRounds} rounds a bout, {UnifiedTitleRounds} for a title, three judges and today's gloves."
                : $"{p.Abbreviation} takes up the commissions' code of its own accord: {CardRounds} rounds a bout, {UnifiedTitleRounds} for a title, three judges and today's gloves.",
            p.Id);
        if (bound && p.Id == world.PlayerPromotion)
            Mail.Post(world, date, MailCategory.BoardNote, Mail.Owner(world), "The commission's code is now our rulebook",
                $"Our licence comes with one condition now: every licensed house fights under the same code. From our next night it's {CardRounds} rounds of five minutes a bout and {UnifiedTitleRounds} for a title, three judges, today's gloves and no rope escapes"
                + (rewritten > 0 ? $"; the {rewritten} {(rewritten == 1 ? "bout" : "bouts")} already on our cards are rebooked to it." : ".")
                + " It wasn't our call to make.");
    }

    /// <summary>
    /// The house's announced cards of single bouts, rebooked to the code: every bout at <see cref="CardRounds"/>,
    /// a title bout at <see cref="UnifiedTitleRounds"/>. Returns how many bouts changed. (A bracket on the books
    /// never reaches here: the house waits for it.)
    /// </summary>
    private static int RewriteBookedCards(World world, Promotion p, DateOnly date)
    {
        int changed = 0;
        foreach (var ev in world.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Scheduled
                     && e.Format == EventFormat.Card && e.Date >= date).OrderBy(e => e.Date).ThenBy(e => e.Id.Value))
            foreach (var f in ev.Card)
            {
                int rounds = f.IsTitleFight ? UnifiedTitleRounds : CardRounds;
                if (f.ScheduledRounds == rounds) continue;
                f.ScheduledRounds = rounds;
                changed++;
            }
        return changed;
    }

    /// <summary>Has the house a one-night bracket on the books? It runs as announced, so the house takes the code after it.</summary>
    public static bool BracketBooked(World world, Promotion p) =>
        world.Events.Values.Any(e => e.Promotion == p.Id && e.Status == EventStatus.Scheduled && e.Format == EventFormat.Tournament
                                    && e.Date >= world.CurrentDate);

    /// <summary>The commissions publish one code (the chapter's effect): every bound house takes it now, the player's included.</summary>
    internal static void UnifiedRulesBegin(World world, DateOnly date)
    {
        // (A house with a bracket on the books takes it after the bracket: the monthly pass binds it then.)
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct && BoundByCode(world, p) && !HasUnified(p) && !BracketBooked(world, p))
                     .OrderBy(p => p.Id.Value).ToList())
            TakeUnified(world, p, date, bound: true);
    }

    /// <summary>
    /// A house founded now is of its era: under the unified code once there is one (unless it opens in the
    /// second centre), and in a ring when it opens in the second centre. (Applied after its era rules were
    /// rolled, so its founding draws are untouched.)
    /// </summary>
    internal static void FoundedUnder(World world, Promotion p)
    {
        if (world.Mode != WorldMode.Origins) return;
        if (OriginsScenes.InSecondCentre(world, p))
        {
            if (p.Rules.Fight is { } r) p.Rules.Fight = r with { Enclosure = Enclosure.Ring };
            return;
        }
        if (!world.Era.HasFired(Unified)) return;
        var had = p.Rules.Fight ?? FightRules.Modern;
        p.Rules.Fight = new FightRules { OpenWeight = had.OpenWeight, Enclosure = had.Enclosure };
        p.Rules.StandardRounds = CardRounds;
        p.Rules.TitleRounds = UnifiedTitleRounds;
        p.Rules.RoundMinutes = 5;
    }

    /// <summary>
    /// How much of a house's purse cap staffs one weight division: a house at the floor purse
    /// (<see cref="Contracts.Negotiator.OriginsPurseFloor"/>, 500) runs two (heavy and light), every 250 more buys
    /// one more, and a house paying 2,000 a purse runs the whole ladder. (longsim origins 40 20: the founders and
    /// the authored giants budget 2,000-4,000 a purse; the small halls of the Boom that ran out of men, 500-1,250,
    /// had opened all seven or eight lines at once, a signing target of 42-48 men for a 900-seat hall.)
    /// </summary>
    public const decimal PurseCapPerDivision = 250m;

    /// <summary>
    /// The men's weight divisions an AI house can staff (Origins): its purse cap over
    /// <see cref="PurseCapPerDivision"/>, at least the first split's two, at most the whole ladder. A small house
    /// runs a few wide divisions, not eight thin ones. (A rule for the AI's own choices: the player's house may
    /// draw every line the sport has.)
    /// </summary>
    public static int DivisionsItCanStaff(World world, Promotion p)
    {
        int ladder = Ladder.Sum(ch => LinesOf(ch).Count);
        return Math.Clamp((int)(Contracts.Negotiator.PurseCap(world, p) / PurseCapPerDivision), LinesOf(Ladder[0]).Count, ladder);
    }

    /// <summary>
    /// Of <paramref name="drawn"/> (lines the sport has drawn), the ones this house can staff: whole ladder chapters
    /// in the sport's order, while the count stays within <see cref="DivisionsItCanStaff"/> (the first split always).
    /// </summary>
    internal static List<WeightClass> LinesItCanStaff(World world, Promotion p, IReadOnlyList<WeightClass> drawn)
    {
        int room = DivisionsItCanStaff(world, p);
        var keep = new List<WeightClass>();
        foreach (var chapter in Ladder)
        {
            var add = LinesOf(chapter).Where(drawn.Contains).ToList();
            if (add.Count == 0 || (keep.Count > 0 && keep.Count + add.Count > room)) break;
            keep.AddRange(add);
        }
        return keep;
    }

    /// <summary>Would drawing this weight chapter's lines take the AI house past what it can staff? (Never the first split.)</summary>
    private static bool BeyondItsStaff(World world, Promotion p, string chapter)
    {
        if (!Ladder.Contains(chapter)) return false;
        int have = p.Divisions.Count(d => !d.Open && d.Sex == Sex.Male);
        int add = LinesOf(chapter).Count(wc => !p.Divisions.Any(d => !d.Open && d.Sex == Sex.Male && d.WeightClass == wc));
        return have > 0 && have + add > DivisionsItCanStaff(world, p);
    }

    /// <summary>
    /// The month's AI reforms: each AI house weighs its open reforms on a keyed roll (one reform a month at most).
    /// A weight line waits until the house can staff it (<see cref="DivisionsItCanStaff"/>); the first split never waits.
    /// </summary>
    public static void RunMonthly(World world, DateOnly date)
    {
        int month = OriginsTimeline.Month(world, date);
        // (A house a commission licenses after the code was published comes under it in the timeline's weekly pass.)
        TellThePlayer(world, date);
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct && p.Id != world.PlayerPromotion).OrderBy(p => p.Id.Value).ToList())
        {
            foreach (var option in Options(world, p, date))
            {
                if (BeyondItsStaff(world, p, option.Chapter)) continue;   // (keyed rolls: passing one moves no other)
                if (OriginsTimeline.Roll(world, AdoptSalt, (ulong)p.Id.Value, (ulong)month, OriginsTimeline.KeyHash(option.Chapter)) >= Chance(world, p, option.Chapter, date))
                    continue;
                Adopt(world, p, option.Chapter, date);
                break;
            }
        }
    }

    /// <summary>
    /// The player's desk hears of a reform the month it becomes open to his house (never twice: an option is
    /// told when its chapter opened since the last monthly pass): what it is, what it does, and what it buys.
    /// The AI never adopts for him; he answers through <see cref="Options"/> / <see cref="TryAdopt"/>.
    /// </summary>
    private static void TellThePlayer(World world, DateOnly date)
    {
        if (world.Get(world.PlayerPromotion) is not { Defunct: false } me) return;
        var prev = OriginsBacklash.PreviousMonthlyTick(date);
        foreach (var o in Options(world, me, date))
        {
            if (OpenOn(world, o.Chapter, prev)) continue;
            string buys = o.Chapter switch
            {
                Rounds => " It answers the critics, and with gloves it is what a commission asks before it will license our cards.",
                Gloves => " With rounds and judges it is what a commission asks before it will license our cards; equipment money follows the gloves.",
                Unified => " No commission holds us to it, but fighters, judges and the rankings all speak this code now.",
                _ => " A belt for every weight: smaller men get fights they can win, and the first champion crowned in a new class makes history.",
            };
            Mail.Post(world, date, MailCategory.BoardNote, Mail.Owner(world), $"We could adopt: {o.Label}",
                o.Detail + buys + " It's our call; nobody will make it for us.");
        }
    }

    /// <summary>Was this chapter open (fired, or live) on a date? Read off the durable record, so the past answers the same.</summary>
    internal static bool OpenOn(World world, string chapter, DateOnly on)
    {
        if (world.Era.Chapter(chapter) is { } rec) return rec.FiredOn <= on;
        var c = OriginsChapters.ByKey(chapter);
        return OriginsTimeline.Month(world, on) >= c.EarliestMonth
               && c.Requires.All(r => world.Era.Chapter(r) is { } rr && rr.FiredOn < on);
    }

    /// <summary>
    /// A house's monthly chance of making a reform: a pioneer's 2.5% while nobody has (rising to 5.5% as the
    /// window closes), 6% once the sport has; and the rules the critics ask for are 10% likelier for a house
    /// the Backlash has closed at home.
    /// </summary>
    public static double Chance(World world, Promotion p, string chapter, DateOnly date)
    {
        // The second centre keeps its own house rules: its AI houses never take the commissions' code.
        if (chapter == Unified) return OriginsScenes.InSecondCentre(world, p) ? 0 : 0.06;
        var c = OriginsChapters.ByKey(chapter);
        int month = OriginsTimeline.Month(world, date);
        double progress = Math.Clamp((month - c.EarliestMonth) / (double)Math.Max(1, c.LatestMonth - c.EarliestMonth), 0, 1);
        double chance = world.Era.HasFired(chapter) ? 0.06 : 0.025 + 0.03 * progress;
        if ((chapter == Rounds || chapter == Gloves) && p.HomeCity is { } hc && Era.Banned(world, hc, date)) chance += BannedPressure;
        return chance;
    }

    // ------------------------------------------------------------------ the weight lines

    /// <summary>
    /// Draw new weight lines at a house: the open division (if any) gives way, every line gets a division and
    /// a vacant belt, the bands are re-cut, and champions left outside their band give their belts up. Bouts already
    /// on announced cards are fought as booked (the lines bind from the next night booked); only a belt that no longer
    /// fits comes off its bout.
    /// </summary>
    internal static void DrawLines(World world, Promotion p, IReadOnlyList<WeightClass> add, DateOnly date, bool announce = true)
    {
        var enterAtTop = new List<FighterId>();
        if (p.Divisions.FirstOrDefault(d => d.Open && d.Sex == Sex.Male) is { } open)
        {
            if (open.Title is { } tid && world.Get(tid) is { } belt)
            {
                var reign = belt.Lineage.LastOrDefault(r => r.LostOn is null);
                var last = belt.CurrentChampion is { } cid ? world.Get(cid) : null;
                if (reign is not null) { reign.LostOn = date; reign.Vacated = true; }
                belt.CurrentChampion = null;
                if (last is not null) enterAtTop.Add(last.Id);
                News.Post(world, date, NewsCategory.TitleChange, $"{p.Abbreviation} retires its open-weight belt",
                    last is null
                        ? $"{p.Name} splits by weight; the {belt.Name}, never won, is retired with it."
                        : $"{p.Name} splits by weight, and the {belt.Name} is retired with {last.FullName} as its last holder. He will start at the top of his new division.",
                    p.Id, last?.Id);
            }
            p.Divisions.Remove(open);
        }

        foreach (var wc in add.OrderBy(w => w))
        {
            if (p.Divisions.Any(d => !d.Open && d.Sex == Sex.Male && d.WeightClass == wc)) continue;
            var title = new Title
            {
                Id = world.NewTitleId(), Promotion = p.Id, WeightClass = wc,
                Name = $"{p.Abbreviation} {Name(wc)} Championship", Prestige = p.Prestige.Value,
            };
            world.Titles[title.Id] = title;
            p.Titles.Add(title.Id);
            p.Divisions.Add(new Division { WeightClass = wc, Sex = Sex.Male, Title = title.Id });
        }

        // Re-cut the bands: each line takes every class above the line below it.
        var lines = p.Divisions.Where(d => !d.Open && d.Sex == Sex.Male).OrderBy(d => d.WeightClass).ToList();
        WeightClass? below = null;
        foreach (var d in lines)
        {
            var lightest = below is { } b ? b + 1 : WeightClass.Strawweight;
            d.Lightest = lightest == d.WeightClass ? null : lightest;
            below = d.WeightClass;
        }
        // A house with weight lines is no longer open weight: its bouts are booked inside a division and weigh in.
        // (The size term still applies inside a band: FightParams.BandedSizeTerm.)
        if (p.Rules.Fight is { OpenWeight: true } rules) p.Rules.Fight = rules with { OpenWeight = false };

        // Keep the divisions in weight order (heaviest last), as a card is read.
        var others = p.Divisions.Where(d => d.Open || d.Sex != Sex.Male).ToList();
        p.Divisions.Clear();
        p.Divisions.AddRange(lines);
        p.Divisions.AddRange(others);

        // A champion the new line leaves outside his belt's band gives it up.
        foreach (var d in lines)
        {
            if (d.Title is not { } tid || world.Get(tid) is not { } belt || belt.CurrentChampion is not { } cid || world.Get(cid) is not { } champ) continue;
            if (d.Matches(champ)) continue;
            var reign = belt.Lineage.LastOrDefault(r => r.LostOn is null);
            if (reign is not null) { reign.LostOn = date; reign.Vacated = true; }
            belt.CurrentChampion = null;
            enterAtTop.Add(champ.Id);
            var home = lines.FirstOrDefault(x => x.Matches(champ));
            News.Post(world, date, NewsCategory.TitleChange, $"{champ.FullName} gives up the {belt.Name}",
                $"A natural {Name(champ.WeightClass).ToLowerInvariant()}, {champ.LastName} now belongs in {p.Abbreviation}'s {(home is null ? "lighter" : Name(home.WeightClass).ToLowerInvariant())} division. The belt is vacant; he starts at the top of his new division.",
                p.Id, champ.Id);
        }

        // A title-shot promise names a belt: one the split retired, or a band the man no longer fights in, is
        // re-pointed to the belt of his division now; with no belt there it is released, and he is told plainly.
        foreach (var pr in world.Promises.Where(x => x.Live && x.Promotion == p.Id && x.Kind == PromiseKind.TitleShot && x.Title is not null)
                     .OrderBy(x => x.Id).ToList())
        {
            if (world.Get(pr.Fighter) is not { } man) continue;
            var now = lines.FirstOrDefault(d => d.Matches(man));
            if (now?.Title == pr.Title) continue;
            if (now?.Title is { } belt)
            {
                pr.Title = belt;
                pr.Note = $"re-pointed to the {world.Get(belt)?.Name} when {p.Abbreviation} split by weight";
                if (p.Id == world.PlayerPromotion)
                    Mail.Post(world, date, MailCategory.FighterRequest, man.FullName, "My title shot moves with my weight",
                        $"You promised me a shot at a belt that is gone or no longer mine to fight for. The {world.Get(belt)?.Name} is my division's now: I'll hold you to that one, same deadline.",
                        aboutFighter: man.Id);
            }
            else
            {
                pr.State = PromiseState.Void;
                pr.Resolved = date;
                pr.Note = $"released: {p.Abbreviation}'s split left his division without a belt";
                if (p.Id == world.PlayerPromotion)
                    Mail.Post(world, date, MailCategory.FighterRequest, man.FullName, "The title shot you promised me is off",
                        "The split left my division without a belt to fight for, so that promise can't be kept. Nobody's fault; I'm not holding it against you.",
                        aboutFighter: man.Id);
            }
        }

        // Bouts already on the books on announced nights are fought as booked (grandfathered): the new lines bind from
        // the next night the house books. Only a belt can't be kept where it no longer belongs: a bout for a belt that
        // no division of the house carries now (the retired open-weight belt), or for a division's belt with a man the
        // new lines put outside that division, goes on as a non-title bout, so no belt is ever won outside its division.
        var unbelted = new List<string>();
        foreach (var ev in world.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Scheduled && e.Format == EventFormat.Card)
                     .OrderBy(e => e.Date).ThenBy(e => e.Id.Value))
        {
            foreach (var f in ev.Card)
            {
                if (!f.IsTitleFight && f.Title is null) continue;
                var a = world.Get(f.RedCorner); var b = world.Get(f.BlueCorner);
                var division = f.Title is { } belt ? lines.FirstOrDefault(d => d.Title == belt) : null;
                if (division is not null && a is not null && b is not null && division.Matches(a) && division.Matches(b)) continue;
                unbelted.Add($"{a?.FullName ?? "?"} vs {b?.FullName ?? "?"} ({ev.Name})");
                f.IsTitleFight = false;
                f.Title = null;
            }
        }
        if (unbelted.Count > 0 && p.Id == world.PlayerPromotion)
            Mail.Post(world, date, MailCategory.General, "Matchmaking", "The new weight lines and the bouts on our cards",
                "Every bout already on our cards goes ahead as booked; the new lines bind from the next night we book. "
                + $"These were for a belt the split has retired or moved, so they go ahead without it: {string.Join("; ", unbelted)}.");

        RankingService.RecomputePromotion(world, p, date);
        foreach (var id in enterAtTop)
            if (world.Get(id) is { Status: FighterStatus.Active } f && f.Contract?.Promotion == p.Id
                && p.Divisions.FirstOrDefault(d => d.Matches(f)) is { } div)
                RankingService.EnterAtTop(div, f.Id);

        if (!announce) return;
        News.Post(world, date, NewsCategory.General,
            $"{p.Name} draws {(add.Count == 1 ? "a new weight line" : "new weight lines")}: {string.Join(" and ", add.Select(Name).Select(s => s.ToLowerInvariant()))}",
            $"{p.Abbreviation} now runs {string.Join(", ", lines.Select(d => Name(d.WeightClass).ToLowerInvariant()))}, each with a vacant belt. Every man fights in the division his natural weight puts him in.",
            p.Id);
    }

    /// <summary>A class in words ("Light Heavyweight").</summary>
    public static string Name(WeightClass wc) => wc switch
    {
        WeightClass.LightHeavyweight => "Light Heavyweight",
        _ => wc.ToString(),
    };
}
