using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;

namespace MmaManager.Core.Sim.Booking;

/// <summary>A proposed bout: two fighters, format, and whether a belt is on the line.</summary>
public readonly record struct Matchup(
    FighterId Red, FighterId Blue, WeightClass WeightClass, int Rounds, bool IsTitle, TitleId? Title);

/// <summary>
/// Builds a fight card for a promotion like a real matchmaker would:
/// <list type="bullet">
/// <item>a belt on the line every card — the champion who has gone longest without
/// defending headlines, against a <em>worthy</em>, non-rematch #1 contender;</item>
/// <item>a vacant belt is contested (top two contenders fight for it), never left
/// to gather dust;</item>
/// <item>the undercard builds the division — ranked contenders are matched against
/// each other (eliminators) so a clear next challenger emerges — while still
/// keeping the idle rank-and-file busy;</item>
/// <item>every pairing stays competitive (near neighbours in ability), champions
/// are kept out of the undercard, and the strongest bout runs last as the main
/// event.</item>
/// </list>
/// Fully deterministic: rotation is driven by how long each belt has gone
/// undefended, not by chance. Every build can be made for a FUTURE fight date
/// (readiness is judged on that day) and around fighters already committed to
/// other scheduled cards — that is what lets the forward calendar book ahead.
/// </summary>
public sealed class Matchmaker
{
    // A fighter inside this many rankings slots counts as a genuine contender —
    // matching two of them together is a title eliminator.
    private const int ContenderDepth = 6;
    private const int VacantBeltPriority = 1_000_000;

    private readonly record struct Candidate(FighterId A, FighterId B, WeightClass Wc, int Tier, double Idle, double Ability);

    public IReadOnlyList<Matchup> BuildCard(World world, PromotionId promoId, int targetFights = 9) =>
        BuildCard(world, promoId, targetFights, world.CurrentDate, null);

    /// <summary>
    /// Build a card for a show on <paramref name="fightDate"/> (fighters must be
    /// match-ready THEN, not today), leaving out anyone in <paramref name="exclude"/>
    /// — fighters already committed to another scheduled card.
    /// </summary>
    public IReadOnlyList<Matchup> BuildCard(World world, PromotionId promoId, int targetFights, DateOnly fightDate,
        IReadOnlySet<FighterId>? exclude) =>
        Build(world, promoId, targetFights, fightDate, exclude, belts: true);

    /// <summary>
    /// The undercard only: up to <paramref name="count"/> non-title bouts at the
    /// standard distance, chosen exactly as <see cref="BuildCard(World, PromotionId, int)"/>
    /// chooses its undercard (feuds, then eliminators, then the most idle).
    /// Used to fill in an announced card closer to fight night.
    /// </summary>
    public IReadOnlyList<Matchup> BuildUndercard(World world, PromotionId promoId, int count, DateOnly fightDate,
        IReadOnlySet<FighterId>? exclude) =>
        count <= 0 ? Array.Empty<Matchup>() : Build(world, promoId, count, fightDate, exclude, belts: false);

    /// <summary>
    /// The top of a card to ANNOUNCE: the main event the matchmaker would build
    /// (the most pressing belt, else the strongest pairing at title length) and a
    /// co-main (the best remaining feud or eliminator, else the strongest pair).
    /// Returned in running order (co-main first, main event last); empty when the
    /// roster can't make a fight. Headliners are picked on QUALITY, not idleness:
    /// idle rotation is the undercard's job.
    /// </summary>
    public IReadOnlyList<Matchup> Headliners(World world, PromotionId promoId, DateOnly fightDate,
        IReadOnlySet<FighterId>? exclude)
    {
        if (world.Get(promoId) is not { } promo) return Array.Empty<Matchup>();
        if (BestBout(world, promo, fightDate, exclude, titleLength: true) is not { } main) return Array.Empty<Matchup>();

        var taken = new HashSet<FighterId>(exclude ?? (IEnumerable<FighterId>)Array.Empty<FighterId>()) { main.Red, main.Blue };
        return BestPairing(world, promo, fightDate, taken, RecentBouts(world)) is { } co
            ? new[] { new Matchup(co.A, co.B, co.Wc, promo.Rules.StandardRounds, false, null), main }
            : new[] { main };
    }

    /// <summary>
    /// The best single bout this org can make on the date: its most pressing belt,
    /// else its strongest pairing (at title length when <paramref name="titleLength"/>,
    /// e.g. a headliner; standard rounds otherwise, e.g. a co-main). Null if none.
    /// </summary>
    public Matchup? BestBout(World world, Promotion promo, DateOnly fightDate, IReadOnlySet<FighterId>? exclude, bool titleLength)
    {
        var recent = RecentBouts(world);
        var belts = TitleBouts(world, promo, fightDate, exclude, recent);
        if (belts.Count > 0)
        {
            var t = belts.OrderByDescending(t => t.Priority).ThenBy(t => (int)t.Wc).First();
            return new Matchup(t.Red.Id, t.Blue.Id, t.Wc, promo.Rules.TitleRounds, true, t.Title.Id);
        }
        return BestPairing(world, promo, fightDate, exclude, recent) is { } top
            ? new Matchup(top.A, top.B, top.Wc, titleLength ? promo.Rules.TitleRounds : promo.Rules.StandardRounds, false, null)
            : null;
    }

    private static Candidate? BestPairing(World world, Promotion promo, DateOnly fightDate, IReadOnlySet<FighterId>? exclude,
        Dictionary<FighterId, (DateOnly Date, FighterId Opponent, bool Won)> recent)
    {
        Candidate? best = null;
        foreach (var c in Candidates(world, promo, fightDate, exclude, recent)
                     .OrderByDescending(c => c.Tier).ThenByDescending(c => c.Ability)
                     .ThenBy(c => c.A.Value).ThenBy(c => c.B.Value))
        {
            best = c;
            break;
        }
        return best;
    }

    private IReadOnlyList<Matchup> Build(World world, PromotionId promoId, int targetFights, DateOnly fightDate,
        IReadOnlySet<FighterId>? exclude, bool belts)
    {
        var promo = world.Get(promoId);
        if (promo is null) return Array.Empty<Matchup>();

        var recent = RecentBouts(world);
        var booked = new HashSet<FighterId>();

        // --- Belts: headline with the most pressing one ------------------------
        Matchup? mainEvent = null;
        if (belts)
        {
            var titleBouts = TitleBouts(world, promo, fightDate, exclude, recent);
            if (titleBouts.Count > 0)
            {
                var headliner = titleBouts.OrderByDescending(t => t.Priority).ThenBy(t => (int)t.Wc).First();
                mainEvent = new Matchup(headliner.Red.Id, headliner.Blue.Id, headliner.Wc,
                    promo.Rules.TitleRounds, true, headliner.Title.Id);
                booked.Add(headliner.Red.Id);
                booked.Add(headliner.Blue.Id);
            }
        }

        // --- Undercard: eliminators first, then keep the roster busy ------------
        // Each pairing gets a tier: matching two ranked contenders (an eliminator
        // that produces the next challenger) outranks a routine "keep them active"
        // bout. Within a tier, the most idle pair goes first, ability as tiebreak.
        var undercard = new List<Matchup>();
        foreach (var c in Candidates(world, promo, fightDate, exclude, recent)
                     .OrderByDescending(c => c.Tier)
                     .ThenByDescending(c => c.Idle)
                     .ThenByDescending(c => c.Ability))
        {
            if (undercard.Count >= targetFights - (mainEvent is null ? 0 : 1)) break;
            if (booked.Contains(c.A) || booked.Contains(c.B)) continue;
            booked.Add(c.A);
            booked.Add(c.B);
            undercard.Add(new Matchup(c.A, c.B, c.Wc, promo.Rules.StandardRounds, false, null));
        }

        // An undercard-only build stops here: no belt, no promoted headliner.
        if (!belts) return undercard;

        // Prelims first, headliner last. Without a title fight, promote the
        // strongest undercard bout to a title-length main event.
        var card = new List<Matchup>(undercard);
        if (mainEvent is { } me)
        {
            card.Add(me);
        }
        else if (card.Count > 0)
        {
            int best = 0;
            double bestScore = -1;
            for (int i = 0; i < card.Count; i++)
            {
                double s = Combined(world, card[i]);
                if (s > bestScore) { bestScore = s; best = i; }
            }
            var promoted = card[best] with { Rounds = promo.Rules.TitleRounds };
            card.RemoveAt(best);
            card.Add(promoted);
        }

        return card;
    }

    // --- Belts: title defences and vacant-belt crownings ------------------------
    // Every belt that can be contested on the date, with its urgency: a vacant
    // belt first, otherwise the longest wait.
    private static List<(Title Title, WeightClass Wc, Fighter Red, Fighter Blue, long Priority)> TitleBouts(
        World world, Promotion promo, DateOnly fightDate, IReadOnlySet<FighterId>? exclude,
        Dictionary<FighterId, (DateOnly Date, FighterId Opponent, bool Won)> recent)
    {
        bool Free(Fighter f) => f.IsMatchReady(fightDate) && (exclude is null || !exclude.Contains(f.Id));
        // Booking around the calendar (an exclusion set given): a belt already
        // on the line on an announced card isn't put up twice.
        var onTheLine = exclude is null ? new HashSet<TitleId>() : world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == promo.Id)
            .SelectMany(e => e.Card).Where(f => f.IsTitleFight && f.Title is not null)
            .Select(f => f.Title!.Value).ToHashSet();
        var titleBouts = new List<(Title Title, WeightClass Wc, Fighter Red, Fighter Blue, long Priority)>();
        foreach (var division in promo.Divisions.Where(d => d.Title is not null))
        {
            var title = world.Get(division.Title!.Value);
            if (title is null || onTheLine.Contains(title.Id)) continue;

            // Only fighters this org still holds: rankings rebuild at the org's
            // next card, and a stale name must never be booked for its belt.
            var contenders = division.Rankings
                .Select(world.Get)
                .Where(f => f is not null && f.Contract?.Promotion == promo.Id && Free(f))
                .Cast<Fighter>()
                .ToList();

            var champ = title.CurrentChampion is { } cid ? world.Get(cid) : null;
            if (champ is not null && Free(champ))
            {
                if (PickChallenger(world, champ, contenders, recent) is { } challenger)
                    titleBouts.Add((title, division.WeightClass, champ, challenger, DaysIdle(world, recent, champ.Id)));
            }
            else if (champ is null && contenders.Count >= 2)
            {
                // A vacant belt jumps the queue — crown a champion at the top two.
                titleBouts.Add((title, division.WeightClass, contenders[0], contenders[1], VacantBeltPriority));
            }
        }
        return titleBouts;
    }

    private static int DaysIdle(World world, Dictionary<FighterId, (DateOnly Date, FighterId Opponent, bool Won)> recent, FighterId id) =>
        recent.TryGetValue(id, out var r)
            ? world.CurrentDate.DayNumber - r.Date.DayNumber
            : 3650;   // never fought (or long since) — a debut is overdue

    // Every competitive same-division pairing among the org's free, non-champion
    // fighters, with its booking tier, idleness and combined ability.
    private static List<Candidate> Candidates(World world, Promotion promo, DateOnly fightDate,
        IReadOnlySet<FighterId>? exclude, Dictionary<FighterId, (DateOnly Date, FighterId Opponent, bool Won)> recent)
    {
        // Champions only fight for the belt — keep them out of the undercard pool.
        var champions = new HashSet<FighterId>();
        foreach (var d in promo.Divisions)
            if (d.Title is { } tid && world.Get(tid)?.CurrentChampion is { } cid)
                champions.Add(cid);

        // One pass over the world's fighters for the org's free roster (the
        // divisions then split it), ordered strongest first.
        var free = world.RosterOf(promo.Id)
            .Where(f => f.IsMatchReady(fightDate) && !champions.Contains(f.Id)
                        && (exclude is null || !exclude.Contains(f.Id)))
            .OrderByDescending(FighterAbility.CurrentAbility)
            .ToList();

        var candidates = new List<Candidate>();
        foreach (var division in promo.Divisions)
        {
            var pool = free.Where(division.Matches).ToList();

            var rank = division.Rankings;
            bool Contender(FighterId id) { int i = rank.IndexOf(id); return i >= 0 && i < ContenderDepth; }

            // Pair each fighter with its nearest neighbours in ability: close
            // enough to be competitive, with a little slack for idle matchmaking —
            // the window is 3 deep so the matchmaker can route AROUND blocked pairs
            // (teammates and friends refuse each other; without the slack, a
            // camp-heavy roster leaves fighters idle). A LIVE FEUD between two
            // rostered fighters outranks everything — the grudge match sells
            // (+gate) and settles the story, so the matchmaker books it on purpose
            // instead of waiting for the rankings to collide.
            for (int i = 0; i + 1 < pool.Count; i++)
                for (int j = i + 1; j <= Math.Min(i + 3, pool.Count - 1); j++)
                {
                    var a = pool[i];
                    var b = pool[j];
                    // Friends, family and TEAMMATES don't fight for an undercard
                    // purse — training partners hardest of all. Only a belt puts
                    // them in the same cage (and the friendship pays).
                    if (a.Camp is { } room && a.Camp == b.Camp) continue;
                    if (Living.Bonds.WontFight(world, a.Id, b.Id)) continue;
                    bool feud = Living.Storylines.RivalryBetween(world, a.Id, b.Id) is { Intensity: >= 40 };
                    int tier = feud ? 2 : Contender(a.Id) && Contender(b.Id) ? 1 : 0;
                    double idle = DaysIdle(world, recent, a.Id) + DaysIdle(world, recent, b.Id);
                    double ability = FighterAbility.CurrentAbility(a) + FighterAbility.CurrentAbility(b);
                    candidates.Add(new Candidate(a.Id, b.Id, division.WeightClass, tier, idle, ability));
                }
        }
        return candidates;
    }

    /// <summary>
    /// The champion's next challenger: the best available ranked contender who has
    /// earned it — skipping an immediate rematch the champ already won, and
    /// preferring a contender in form. A HEATED rival jumps the queue (bad blood is
    /// the fight the fans demand), and at trilogy-level heat (70+) even an instant
    /// rematch gets booked — that's how wars get settled. Falls back to the top
    /// contender rather than freeze the belt when nobody else is available.
    /// </summary>
    private static Fighter? PickChallenger(World world, Fighter champ, List<Fighter> contenders,
        IReadOnlyDictionary<FighterId, (DateOnly Date, FighterId Opponent, bool Won)> recent)
    {
        if (contenders.Count == 0) return null;

        // Whoever the champ most recently beat isn't owed an instant rematch.
        FighterId? staleRematch =
            recent.TryGetValue(champ.Id, out var r) && r.Won ? r.Opponent : null;

        int HeatWith(Fighter c) => Living.Storylines.RivalryBetween(world, champ.Id, c.Id)?.Intensity ?? 0;
        var grudge = contenders.Where(c => Worthy(c) && HeatWith(c) >= 40)
            .OrderByDescending(HeatWith).FirstOrDefault();
        if (grudge is not null && (grudge.Id != staleRematch || HeatWith(grudge) >= 70))
            return grudge;                                                          // the fight that sells itself

        return contenders.FirstOrDefault(c => c.Id != staleRematch && Worthy(c))   // in form, fresh
            ?? contenders.FirstOrDefault(c => c.Id != staleRematch)                 // at least not a rematch
            ?? contenders[0];                                                       // last resort
    }

    // A credible challenger: riding a win, or at worst a winning/even career.
    public static bool Worthy(Fighter f) =>
        f.Record.CurrentStreak > 0 || f.Record.Wins >= f.Record.Losses;

    /// <summary>
    /// Each fighter's most recent completed bout: when, against whom, and whether
    /// they won — the basis for activity-aware booking and rematch avoidance.
    /// </summary>
    // The forward calendar asks for several builds a tick (headliners, co-mains,
    // undercards); the scan over every completed bout is the expensive part, so
    // it's memoised for as long as the world's completed history is unchanged
    // (announcing a card adds a SCHEDULED event, which this scan ignores).
    private (World? World, DateOnly Date, int Completed) _recentKey;
    private Dictionary<FighterId, (DateOnly Date, FighterId Opponent, bool Won)>? _recent;

    private Dictionary<FighterId, (DateOnly Date, FighterId Opponent, bool Won)> RecentBouts(World world)
    {
        int completed = 0;
        foreach (var e in world.Events.Values) if (e.Status == EventStatus.Completed) completed++;
        var key = (world, world.CurrentDate, completed);
        if (_recent is not null && ReferenceEquals(_recentKey.World, world) && _recentKey == key) return _recent;
        _recent = ComputeRecentBouts(world);
        _recentKey = key;
        return _recent;
    }

    private static Dictionary<FighterId, (DateOnly Date, FighterId Opponent, bool Won)> ComputeRecentBouts(World world)
    {
        var last = new Dictionary<FighterId, (DateOnly Date, FighterId Opponent, bool Won)>();
        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Completed) continue;
            foreach (var fight in ev.Card)
            {
                Record(fight.RedCorner, fight.BlueCorner);
                Record(fight.BlueCorner, fight.RedCorner);

                void Record(FighterId self, FighterId opponent)
                {
                    if (last.TryGetValue(self, out var prev) && prev.Date >= ev.Date) return;
                    bool won = fight.Outcome?.Winner == self;
                    last[self] = (ev.Date, opponent, won);
                }
            }
        }
        return last;
    }

    private static double Combined(World world, Matchup m)
    {
        var a = world.Get(m.Red);
        var b = world.Get(m.Blue);
        return (a is null ? 0 : FighterAbility.CurrentAbility(a))
             + (b is null ? 0 : FighterAbility.CurrentAbility(b));
    }
}
