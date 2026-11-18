using MmaManager.Core.Model;

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
        foreach (var c in Candidates(world, promo, fightDate, exclude, recent, booked)
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
                double s = Combined(world, promo, card[i]);
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
        using var _ = Living.TickProbe.Phase("mm/title-bouts");
        bool Free(Fighter f) => f.IsMatchReady(fightDate) && (exclude is null || !exclude.Contains(f.Id));
        // Booking around the calendar (an exclusion set given): a belt already
        // on the line on an announced card isn't put up twice.
        var onTheLine = exclude is null ? new HashSet<TitleId>() : world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == promo.Id)
            .SelectMany(e => e.Card).Where(f => f.IsTitleFight && f.Title is not null)
            .Select(f => f.Title!.Value).ToHashSet();
        var titleBouts = new List<(Title Title, WeightClass Wc, Fighter Red, Fighter Blue, long Priority)>();
        Dictionary<(int, int), Rivalry>? feuds = null;   // by pair, built for the first champion with contenders (nothing here adds a rivalry)
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
                if (PickChallenger(world, champ, title, division.Rankings, contenders, recent, contenders.Count == 0 ? null : feuds ??= Living.Storylines.RivalriesByPair(world)) is { } challenger)
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
        IReadOnlySet<FighterId>? exclude, Dictionary<FighterId, (DateOnly Date, FighterId Opponent, bool Won)> recent,
        IReadOnlySet<FighterId>? sameCard = null)
    {
        using var _ = Living.TickProbe.Phase("mm/candidates");
        // Champions only fight for the belt — keep them out of the undercard pool.
        var champions = new HashSet<FighterId>();
        foreach (var d in promo.Divisions)
            if (d.Title is { } tid && world.Get(tid)?.CurrentChampion is { } cid)
                champions.Add(cid);

        // One pass over the world's fighters for the org's free roster (the
        // divisions then split it), ordered strongest first BY THE HOUSE'S OWN
        // READ (the fog: a matchmaker pairs the fighters he thinks are close, not
        // the ones who truly are), id as the last word.
        var nextInLine = NextInLine(world, promo, fightDate, exclude, sameCard);
        var free = world.RosterOf(promo.Id)
            .Where(f => f.IsMatchReady(fightDate) && !champions.Contains(f.Id) && !nextInLine.Contains(f.Id)
                        && (exclude is null || !exclude.Contains(f.Id)))
            .ToList();
        var seen = free.ToDictionary(f => f.Id, f => Seen(world, promo, f));
        free = free.OrderByDescending(f => seen[f.Id]).ThenBy(f => f.Id.Value).ToList();

        // Every pair below asks after a bond and a feud: look them up by pair, not by walking both lists for each
        // (they grow with the world). Nothing here adds or removes either.
        var bonds = Living.Bonds.ByPair(world);
        var feuds = Living.Storylines.RivalriesByPair(world);

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
                    var pair = Living.Bonds.PairKey(a.Id, b.Id);
                    if (Living.Bonds.WontFight(bonds.GetValueOrDefault(pair))) continue;
                    bool feud = feuds.GetValueOrDefault(pair) is { Intensity: >= 40 };
                    int tier = feud ? 2 : Contender(a.Id) && Contender(b.Id) ? 1 : 0;
                    double idle = DaysIdle(world, recent, a.Id) + DaysIdle(world, recent, b.Id);
                    double ability = seen[a.Id] + seen[b.Id];
                    candidates.Add(new Candidate(a.Id, b.Id, division.WeightClass, tier, idle, ability));
                }
        }
        return candidates;
    }

    /// <summary>
    /// The men a house keeps OFF its non-title bouts on <paramref name="fightDate"/>
    /// because the belt is next for them: each titled division's #1 contender (the
    /// house's own ranking), while that division's champion will be fit on the date
    /// and has no defense booked on this card or a later one. A #1 booked into an
    /// eliminator was the commonest reason a title shot went down the rankings (he
    /// was on another card when the champion's next defense was made: 58% of the
    /// shots that did not go to #1, longsim at 40 seeds). He still fights while the
    /// champion is hurt, or is already booked to defend on or after the date. The
    /// player's house is never asked (its bookings are the player's). Draws nothing.
    /// </summary>
    private static HashSet<FighterId> NextInLine(World world, Promotion promo, DateOnly fightDate,
        IReadOnlySet<FighterId>? exclude, IReadOnlySet<FighterId>? sameCard)
    {
        var held = new HashSet<FighterId>();
        if (promo.Id == world.PlayerPromotion) return held;
        foreach (var d in promo.Divisions)
        {
            if (d.Rankings.Count == 0 || d.Title is not { } tid || world.Get(tid)?.CurrentChampion is not { } cid) continue;
            if (world.Get(cid) is not { } champ || !champ.IsMatchReady(fightDate)) continue;
            if (sameCard is not null && sameCard.Contains(cid)) continue;   // defending on this very card
            if (exclude is not null && exclude.Contains(cid))
            {
                // Taken: booked on an announced card (the #1 waits only if that defense comes BEFORE this date),
                // or spoken for on the card being built (no announced card yet), or out of deal.
                if (Living.ForwardCalendar.CommittedTo(world, cid) is not { } card || card.Date >= fightDate) continue;
            }
            held.Add(d.Rankings[0]);
        }
        return held;
    }

    /// <summary>
    /// How deep in the house's rankings a title shot may come from while the
    /// champion can afford to wait. Bad blood lifts a man over the others inside
    /// this window and never reaches past it; the queue itself reaches no deeper
    /// while the champion has been out less than <see cref="ChampionWaitDays"/>.
    /// </summary>
    internal const int TitleShotWindow = 5;

    /// <summary>
    /// A champion out this long (days since his last bout) is booked against the
    /// best man free on the date, wherever he ranks: the belt is never frozen
    /// waiting for the top of the queue to come free.
    /// </summary>
    internal const int ChampionWaitDays = 240;

    /// <summary>
    /// A dethroned champion is owed his one immediate rematch only if his reign had this many defenses: an
    /// ESTABLISHED champion, so the rematch stays an occasion (at 1 defense it was 6% of every title shot).
    /// </summary>
    internal const int RematchClauseDefenses = 3;

    /// <summary>Bad blood at least this hot is a grudge the matchmaker books (a drawn title fight leaves <c>Storylines.DrawnTitleHeat</c>).</summary>
    internal const int GrudgeHeat = 40;

    /// <summary>
    /// The champion's next challenger, read off the house's rankings (public
    /// facts: résumés, not ratings):
    /// <list type="number">
    /// <item>the champion he just dethroned, if that man had a real reign (at least
    /// <see cref="RematchClauseDefenses"/> defenses) — the immediate rematch is a
    /// convention, owed ONCE: it is only owed while the new reign has no defense,
    /// so a rematch the new champion wins is settled, and one he loses leaves a
    /// defenseless reign that is owed nothing;</item>
    /// <item>a HEATED rival (<see cref="GrudgeHeat"/>+) ranked inside the top
    /// <see cref="TitleShotWindow"/> — bad blood is a tie-breaker among credible
    /// contenders, not a pass from anywhere in the division (at trilogy heat, 70+,
    /// even the man the champion just beat);</item>
    /// <item>the best-ranked contender free on the date, in form, and not the man
    /// the champion just beat — from inside the window. When nobody inside it is
    /// free the champion WAITS for a later card (null: the belt sits this one
    /// out) rather than reach far down, until he has been out
    /// <see cref="ChampionWaitDays"/>; then the old fallbacks apply, so no belt
    /// stalls.</item>
    /// </list>
    /// <paramref name="contenders"/> is the free, signed part of
    /// <paramref name="rankings"/>, in ranking order. Draws nothing; every
    /// ordering ends on id.
    /// </summary>
    private static Fighter? PickChallenger(World world, Fighter champ, Title title, IReadOnlyList<FighterId> rankings,
        List<Fighter> contenders,
        IReadOnlyDictionary<FighterId, (DateOnly Date, FighterId Opponent, bool Won)> recent,
        Dictionary<(int, int), Rivalry>? feuds)
    {
        if (contenders.Count == 0 || feuds is null) return null;

        // 1) The dethroned champion's one rematch.
        // (He lost the belt in the cage, to this man, in his last bout: a belt handed down on a vacancy owes nothing.)
        if (OwedRematch(title, champ.Id) is { } owed && contenders.FirstOrDefault(c => c.Id == owed) is { } former
            && recent.TryGetValue(owed, out var lost) && lost.Opponent == champ.Id && !lost.Won)
            return former;

        // Whoever the champ most recently beat isn't owed an instant rematch.
        FighterId? staleRematch =
            recent.TryGetValue(champ.Id, out var r) && r.Won ? r.Opponent : null;

        int RankOf(Fighter c) { int i = IndexOf(rankings, c.Id); return i < 0 ? int.MaxValue : i + 1; }
        bool InWindow(Fighter c) => RankOf(c) <= TitleShotWindow;
        int HeatWith(Fighter c) => feuds.GetValueOrDefault(Living.Bonds.PairKey(champ.Id, c.Id))?.Intensity ?? 0;

        // 2) Bad blood, inside the window.
        var grudge = contenders
            .Where(c => InWindow(c) && Worthy(c) && HeatWith(c) >= GrudgeHeat && (c.Id != staleRematch || HeatWith(c) >= 70))
            .OrderByDescending(HeatWith).ThenBy(RankOf).ThenBy(c => c.Id.Value)
            .FirstOrDefault();
        if (grudge is not null) return grudge;                                     // the fight that sells itself

        // 3) The queue: the best-ranked man free on the date, in form, fresh.
        var next = contenders.FirstOrDefault(c => c.Id != staleRematch && Worthy(c));
        if (next is not null && InWindow(next)) return next;

        // Nobody credible inside the window is free: the champion waits for a later card while he can.
        int idle = recent.TryGetValue(champ.Id, out var last) ? world.CurrentDate.DayNumber - last.Date.DayNumber : 3650;
        if (idle < ChampionWaitDays) return null;

        return next                                                                 // in form, fresh, wherever he ranks
            ?? contenders.FirstOrDefault(c => c.Id != staleRematch)                 // at least not a rematch
            ?? contenders[0];                                                       // last resort
    }

    private static int IndexOf(IReadOnlyList<FighterId> list, FighterId id)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == id) return i;
        return -1;
    }

    /// <summary>
    /// The former champion this reigning champion owes an immediate rematch, if
    /// any: the man he took the belt from, in the bout that began this reign (not
    /// a vacated belt), whose own reign had at least <see cref="RematchClauseDefenses"/>
    /// defense, while the new reign has no defense yet. Read off the belt's
    /// lineage (public record).
    /// </summary>
    internal static FighterId? OwedRematch(Title title, FighterId champ)
    {
        int n = title.Lineage.Count;
        if (n < 2) return null;
        var now = title.Lineage[n - 1];
        var before = title.Lineage[n - 2];
        if (now.Champion != champ || now.LostOn is not null || now.Defenses > 0) return null;
        if (before.LostOn != now.WonOn || before.Vacated || before.Champion == champ) return null;
        if (before.Defenses < RematchClauseDefenses) return null;
        return before.Champion;
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
        using (Living.TickProbe.Phase("mm/recent-bouts")) _recent = ComputeRecentBouts(world);
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

    private static double Combined(World world, Promotion promo, Matchup m)
    {
        var a = world.Get(m.Red);
        var b = world.Get(m.Blue);
        return (a is null ? 0 : Seen(world, promo, a))
             + (b is null ? 0 : Seen(world, promo, b));
    }

    /// <summary>
    /// What this house's matchmaker thinks a fighter is worth in the cage: its scouted read
    /// (<see cref="Scouting.ScoutingService.PerceivedAbility(World?, Promotion, Fighter)"/>), never the truth.
    /// A pure hash of (house, fighter): no RNG stream is touched.
    /// </summary>
    private static int Seen(World world, Promotion promo, Fighter f) =>
        Scouting.ScoutingService.PerceivedAbility(world, promo, f);
}
