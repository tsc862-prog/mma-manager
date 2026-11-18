using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// Who gets the title shots, as the rankings deliver them (reporting only, for
/// <c>compare</c>). Each week, before the tick, every title fight on a card
/// within the coming week that a reigning champion defends is snapshotted from
/// the challenger's side: his rank in the promotion's division going in (0 =
/// unranked), his record, his current streak and his wins (among his last 8)
/// over fighters ranked or holding a belt at the time of the snapshot. Kept
/// only if the bout was actually fought. Also counts, once a month, the lifers
/// whose standing grievance is "passed over" for a title shot.
///
/// <para>WHY he got it: every title fight is also caught the week it first
/// appears on a card (the booking), and classified against the world as it
/// stood BEFORE that tick (the calendar books at the top of the tick, so the
/// pre-tick rankings, rivalries, last bouts and commitments are what the
/// matchmaker read): a superfight, an authored booking, a stand-in saving a
/// lost bout, a card rebuilt after it emptied, an immediate rematch (the
/// dethroned champion, or a draw/other), a feud (heat 50+ = the calendar's
/// feud headliner level, 40-49 = the matchmaker's grudge line), or the queue
/// (the best ranked man free on the date), and for a queue shot that is not
/// #1, why each man above him was passed over. Read-only.</para>
/// </summary>
public sealed class TitleShots
{
    public sealed record Shot(int Rank, int Wins, int Losses, int Streak, int WinsOverRanked)
    {
        public Booking? At { get; init; }
        public bool Retained { get; init; }
        public int Rating { get; init; }
    }

    /// <summary>How a title fight came to be booked, read the week it first appeared on a card.</summary>
    public sealed record Booking(string Cause, int RankAtBooking, int RankedCount, int Heat, bool QueuePick,
        string? PassedOverWhy, int? OtherRank, bool Vacant, int DayOne);

    private readonly Dictionary<FightId, Shot> _pending = new();
    private readonly Dictionary<FightId, Fight> _fights = new();
    private readonly HashSet<EventId> _indexed = new();
    public List<Shot> Shots { get; } = new();
    public int PassedOverLiferMonths { get; private set; }
    public HashSet<FighterId> PassedOverLifers { get; } = new();

    // --- booking-side state ---
    private readonly Dictionary<FightId, Booking> _booked = new();         // every title fight, the week it appeared
    private readonly HashSet<FightId> _seenTitle = new();
    private readonly Dictionary<FightId, (bool Vacant, FighterId Champ)> _pendingAll = new();   // all title fights awaiting the night
    public List<(Booking? At, bool Retained, int Rating, bool Vacant, int Heat)> AllTitleFights { get; } = new();
    public long BeltWeeks { get; private set; }
    private bool _first = true;

    // Pre-tick snapshot.
    private Dictionary<TitleId, List<FighterId>> _preRank = new();
    private HashSet<FighterId> _preCommitted = new();
    private Dictionary<(int, int), int> _preHeat = new();
    private Dictionary<EventId, HashSet<TitleId>> _preEventBelts = new();
    private Dictionary<FighterId, (DateOnly Date, FighterId Opp, bool Won)> _preLast = new();
    private Dictionary<FighterId, Snap> _preState = new();
    private DateOnly _preDate;

    private readonly record struct Snap(FighterStatus Status, DateOnly? InjuryBack, DateOnly? SuspendedUntil, DateOnly? ReadyOn,
        int? FightsLeft, PromotionId? Org, bool Worthy);

    public void BeforeWeek(World world)
    {
        // Fights by id, for completed cards (indexed once).
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Completed || !_indexed.Add(e.Id)) continue;
            foreach (var fi in e.Card) { _fights[fi.Id] = fi; _eventOf.TryAdd(fi.Id, e.Date); }
        }
        var horizon = world.CurrentDate.AddDays(7);
        HashSet<FighterId>? rankedNow = null;
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Scheduled || e.Date > horizon || world.Get(e.Promotion) is not { } promo) continue;
            foreach (var fi in e.Card)
            {
                if (!fi.IsTitleFight || fi.Title is not { } tid || world.Get(tid)?.CurrentChampion is not { } champ) continue;
                if (fi.RedCorner != champ && fi.BlueCorner != champ) continue;
                var challenger = world.Get(fi.RedCorner == champ ? fi.BlueCorner : fi.RedCorner);
                if (challenger is null || _pending.ContainsKey(fi.Id)) continue;
                rankedNow ??= RankedOrChampions(world);
                var div = promo.Divisions.FirstOrDefault(d => d.Matches(challenger));
                int rank = div is null ? 0 : div.Rankings.IndexOf(challenger.Id) + 1;
                int overRanked = 0;
                for (int i = challenger.BoutHistory.Count - 1, seen = 0; i >= 0 && seen < 8; i--, seen++)
                    if (_fights.TryGetValue(challenger.BoutHistory[i], out var past) && past.Outcome?.Winner == challenger.Id
                        && rankedNow.Contains(past.RedCorner == challenger.Id ? past.BlueCorner : past.RedCorner))
                        overRanked++;
                _pending[fi.Id] = new Shot(rank, challenger.Record.Wins, challenger.Record.Losses, challenger.Record.CurrentStreak, overRanked);
            }
        }
        Snapshot(world);
    }

    private void Snapshot(World world)
    {
        _preDate = world.CurrentDate;
        _preRank = new();
        var watch = new HashSet<FighterId>();
        foreach (var p in world.Promotions.Values)
            foreach (var d in p.Divisions)
                if (d.Title is { } tid)
                {
                    _preRank[tid] = d.Rankings.ToList();
                    watch.UnionWith(d.Rankings);
                    if (world.Get(tid)?.CurrentChampion is { } c) watch.Add(c);
                }
        foreach (var sp in _spells.Values) watch.Add(sp.Man);   // a man who fell out of the rankings is still followed to the end of his spell
        _preCommitted = ForwardCalendar.Committed(world);
        _preHeat = new();
        foreach (var r in world.Rivalries) _preHeat.TryAdd(Key(r.A, r.B), r.Intensity);
        _preEventBelts = new();
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Scheduled) continue;
            var set = new HashSet<TitleId>();
            foreach (var f in e.Card) if (f.IsTitleFight && f.Title is { } t) set.Add(t);
            _preEventBelts[e.Id] = set;
        }
        _preState = new();
        foreach (var id in watch)
            if (world.Get(id) is { } f)
                _preState[id] = new Snap(f.Status, f.CurrentInjury?.ExpectedReturn, f.SuspendedUntil, f.ReadyToFightOn,
                    f.Contract?.FightsRemaining, f.Contract?.Promotion, Matchmaker.Worthy(f));
        // Each watched fighter's last completed bout (only the men who matter, so the walk is cheap enough weekly).
        _preLast = new();
        foreach (var id in watch)
        {
            if (world.Get(id) is not { } f) continue;
            for (int i = f.BoutHistory.Count - 1; i >= 0; i--)
            {
                if (!_fights.TryGetValue(f.BoutHistory[i], out var b) || b.Outcome is null) continue;
                var ev = _eventOf.GetValueOrDefault(b.Id);
                _preLast[id] = (ev, b.RedCorner == id ? b.BlueCorner : b.RedCorner, b.Outcome.Winner == id);
                break;
            }
        }
        TrackNumberOnes(world);
    }

    private readonly Dictionary<FightId, DateOnly> _eventOf = new();

    // --- What waiting costs the #1 contender: spells at #1 of a division with a reigning champion (AI houses) ---
    private sealed class Spell
    {
        public FighterId Man; public DateOnly Start; public DateOnly LastBout; public int MaxIdle; public int Bouts; public bool Shot;
    }
    private readonly Dictionary<TitleId, Spell> _spells = new();
    /// <summary>Closed spells: max days idle inside the spell, bouts fought while #1, and how it ended.</summary>
    public List<(int MaxIdle, int Bouts, string End, int Days)> Spells { get; } = new();
    /// <summary>Days between a #1 contender's bouts: each bout he fought while ranked #1 the week before, from his previous one.</summary>
    public List<int> NumberOneGaps { get; } = new();

    private void TrackNumberOnes(World world)
    {
        var today = world.CurrentDate;
        foreach (var t in world.Titles.Values.OrderBy(t => t.Id.Value))
        {
            var promo = world.Get(t.Promotion);
            bool live = promo is { Defunct: false } && promo.Id != world.PlayerPromotion && t.CurrentChampion is not null;
            var ranks = _preRank.GetValueOrDefault(t.Id);
            FighterId? top = live && ranks is { Count: > 0 } ? ranks[0] : null;
            if (_spells.TryGetValue(t.Id, out var sp))
            {
                // A bout since the last look?
                if (_preLast.TryGetValue(sp.Man, out var lb) && lb.Date > sp.LastBout && lb.Date != default)
                {
                    NumberOneGaps.Add(lb.Date.DayNumber - sp.LastBout.DayNumber);
                    sp.LastBout = lb.Date; sp.Bouts++;
                }
                if (top == sp.Man) { sp.MaxIdle = Math.Max(sp.MaxIdle, today.DayNumber - sp.LastBout.DayNumber); continue; }
                string end = sp.Shot ? "got the shot"
                    : t.CurrentChampion == sp.Man ? "champion (vacancy/other)"
                    : !live ? "belt vacant / house gone"
                    : !_preState.TryGetValue(sp.Man, out var st) || st.Org != t.Promotion || st.Status == FighterStatus.Retired ? "left the house / retired"
                    : sp.Bouts == 0 ? "lost #1 without fighting (idle)"
                    : "lost #1 after a non-title bout";
                Spells.Add((sp.MaxIdle, sp.Bouts, end, today.DayNumber - sp.Start.DayNumber));
                _spells.Remove(t.Id);
            }
            if (top is { } man && _preLast.TryGetValue(man, out var last) && last.Date != default)
                _spells[t.Id] = new Spell { Man = man, Start = today, LastBout = last.Date, MaxIdle = today.DayNumber - last.Date.DayNumber };
        }
    }

    // --- Small houses: belts by org size tier (City / Regional / National+), sampled each week ---
    public sealed class TierStats
    {
        public int Cards, TitleCards, TitleFights;
        public long BeltWeeks;
        public List<int> ChampIdle = new();
        public int ChampMonths, ChampMonthsIdle120, BenchedChampMonths, LiferChampMonths;
    }
    public Dictionary<string, TierStats> Tiers { get; } = new();
    private readonly HashSet<EventId> _tierCounted = new();

    public static string TierOf(Promotion p) => OrgSizing.SizeOf(p) switch
    {
        OrgSize.City => "City",
        OrgSize.Regional => "Regional",
        _ => "National+",
    };

    private TierStats T(string tier) => Tiers.TryGetValue(tier, out var t) ? t : Tiers[tier] = new TierStats();

    private void TierWeek(World world, bool monthBoundary)
    {
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Completed || e.Card.Count == 0 || !_tierCounted.Add(e.Id)) continue;
            if (e.Date <= _preDate || world.Get(e.Promotion) is not { } p || p.Id == world.PlayerPromotion) continue;
            var t = T(TierOf(p));
            t.Cards++;
            if (e.Card[^1].IsTitleFight || e.Card[^1].Title is not null) t.TitleCards++;
            t.TitleFights += e.Card.Count(f => f.IsTitleFight && f.Outcome is not null);
        }
        foreach (var title in world.Titles.Values)
        {
            if (world.Get(title.Promotion) is not { Defunct: false } p || p.Id == world.PlayerPromotion) continue;
            var t = T(TierOf(p));
            t.BeltWeeks++;
            if (title.CurrentChampion is not { } cid || world.Get(cid) is not { } champ) continue;
            int idle = _preLast.TryGetValue(cid, out var lb) && lb.Date != default ? world.CurrentDate.DayNumber - lb.Date.DayNumber : -1;
            if (idle >= 0) t.ChampIdle.Add(idle);
            if (!monthBoundary) continue;
            t.ChampMonths++;
            if (idle >= 120) t.ChampMonthsIdle120++;
            if (champ.Status == FighterStatus.Active && champ.Contract is not null && Lifers.IsLifer(world, champ))
            {
                t.LiferChampMonths++;
                if (Lifers.GrievanceOf(world, champ, world.CurrentDate) is { Kind: Lifers.GrievanceKind.Benched }) t.BenchedChampMonths++;
            }
        }
    }

    public static void PrintTiers(IReadOnlyList<TitleShots> runs)
    {
        Console.WriteLine("  --- BELTS BY ORG SIZE (AI houses; tier at the time: card completed / weekly sample / monthly sample) ---");
        foreach (var tier in new[] { "City", "Regional", "National+" })
        {
            var ts = runs.Select(r => r.Tiers.GetValueOrDefault(tier)).Where(t => t is not null).Select(t => t!).ToList();
            if (ts.Count == 0) { Console.WriteLine($"  {tier,-10} none"); continue; }
            int cards = ts.Sum(t => t.Cards), tc = ts.Sum(t => t.TitleCards), tf = ts.Sum(t => t.TitleFights);
            double by = ts.Sum(t => t.BeltWeeks) / 52.0;
            var idle = ts.SelectMany(t => t.ChampIdle).OrderBy(x => x).ToList();
            int Q(double q) => idle.Count == 0 ? 0 : idle[Math.Clamp((int)Math.Round(q * (idle.Count - 1)), 0, idle.Count - 1)];
            int cm = ts.Sum(t => t.ChampMonths), lm = ts.Sum(t => t.LiferChampMonths);
            Console.WriteLine($"  {tier,-10} cards {cards,6} · title fight on top {100.0 * tc / Math.Max(1, cards),5:0.0}% · title fights/belt-year {tf / Math.Max(1, by):0.000} ({by:0} belt-years)"
                + $" · champion idle days median {Q(0.5)} p90 {Q(0.9)} · over 120 {100.0 * idle.Count(x => x > 120) / Math.Max(1, idle.Count):0.0}% · over 180 {100.0 * idle.Count(x => x > 180) / Math.Max(1, idle.Count):0.0}%"
                + $" · champion-months idle 120+ {ts.Sum(t => t.ChampMonthsIdle120)} of {cm} · Benched grievance champion-months {ts.Sum(t => t.BenchedChampMonths)} (of {lm} lifer champion-months)");
        }
    }

    private void MarkShot(TitleId tid, FighterId challenger)
    {
        if (_spells.TryGetValue(tid, out var sp) && sp.Man == challenger) sp.Shot = true;
    }

    /// <summary>Spells still open at the end of the run (censored): their idle so far.</summary>
    public void Close(World world)
    {
        foreach (var sp in _spells.Values)
            Spells.Add((Math.Max(sp.MaxIdle, world.CurrentDate.DayNumber - sp.LastBout.DayNumber), sp.Bouts, sp.Shot ? "got the shot" : "still #1 at the end", world.CurrentDate.DayNumber - sp.Start.DayNumber));
        _spells.Clear();
    }

    private static (int, int) Key(FighterId a, FighterId b) => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);

    public void AfterWeek(World world, bool monthBoundary)
    {
        foreach (var e in world.Events.Values)
            if (e.Status == EventStatus.Completed)
                foreach (var fi in e.Card) _eventOf.TryAdd(fi.Id, e.Date);

        ClassifyNewBookings(world);
        TierWeek(world, monthBoundary);

        foreach (var (id, shot) in _pending.ToList())
        {
            var ev = world.Events.Values.FirstOrDefault(e => e.Card.Any(x => x.Id == id));
            if (ev is null || ev.Status == EventStatus.Cancelled) { _pending.Remove(id); continue; }
            if (ev.Status != EventStatus.Completed) continue;
            var fight = ev.Card.First(x => x.Id == id);
            if (fight.Outcome is { } done)
            {
                bool retained = _pendingAll.TryGetValue(id, out var meta) && !meta.Vacant && done.Winner == meta.Champ;
                Shots.Add(shot with { At = _booked.GetValueOrDefault(id), Retained = retained, Rating = MmaManager.Core.Sim.Flavor.MatchRating.Of(fight) });
            }
            _pending.Remove(id);
        }

        // Every title fight (vacant crownings too), to the night.
        foreach (var (id, meta) in _pendingAll.ToList())
        {
            var ev = world.Events.Values.FirstOrDefault(e => e.Card.Any(x => x.Id == id));
            if (ev is null || ev.Status == EventStatus.Cancelled) { _pendingAll.Remove(id); continue; }
            if (ev.Status != EventStatus.Completed) continue;
            var fight = ev.Card.First(x => x.Id == id);
            if (fight.Outcome is { } o)
            {
                bool retained = !meta.Vacant && o.Winner == meta.Champ;
                int rating = MmaManager.Core.Sim.Flavor.MatchRating.Of(fight);
                var at = _booked.GetValueOrDefault(id);
                AllTitleFights.Add((at, retained, rating, meta.Vacant, at?.Heat ?? 0));
            }
            _pendingAll.Remove(id);
        }

        foreach (var t in world.Titles.Values)
            if (world.Get(t.Promotion) is { Defunct: false } p && p.Id != world.PlayerPromotion) BeltWeeks++;

        if (!monthBoundary) return;
        foreach (var f in world.Fighters.Values)
        {
            if (f.Status != FighterStatus.Active || f.Contract is null || !Lifers.IsLifer(world, f)) continue;
            if (Lifers.GrievanceOf(world, f, world.CurrentDate) is { Kind: Lifers.GrievanceKind.PassedOver })
            {
                PassedOverLiferMonths++;
                PassedOverLifers.Add(f.Id);
            }
        }
    }

    private void ClassifyNewBookings(World world)
    {
        bool dayOne = _first;
        _first = false;
        var newEvents = new HashSet<EventId>();
        foreach (var e in world.Events.Values)
            if (e.Status == EventStatus.Scheduled && !_preEventBelts.ContainsKey(e.Id)) newEvents.Add(e.Id);

        foreach (var e in world.Events.Values.OrderBy(e => e.Id.Value))
        {
            if (e.Status != EventStatus.Scheduled || world.Get(e.Promotion) is not { } promo) continue;
            if (promo.Id == world.PlayerPromotion) continue;
            foreach (var fi in e.Card)
            {
                if (!fi.IsTitleFight || fi.Title is not { } tid || !_seenTitle.Add(fi.Id)) continue;
                var belt = world.Get(tid);
                var champ = belt?.CurrentChampion;
                bool vacant = champ is null || (fi.RedCorner != champ && fi.BlueCorner != champ);
                _pendingAll[fi.Id] = (vacant, champ ?? default);
                var ranks = _preRank.GetValueOrDefault(tid) ?? new List<FighterId>();
                int RankOf(FighterId id) => ranks.IndexOf(id) + 1;

                if (vacant)
                {
                    int a = RankOf(fi.RedCorner), b = RankOf(fi.BlueCorner);
                    _booked[fi.Id] = new Booking(dayOne ? "day-one calendar" : "vacant", Math.Min(a == 0 ? 99 : a, b == 0 ? 99 : b),
                        ranks.Count, 0, true, null, Math.Max(a == 0 ? 99 : a, b == 0 ? 99 : b), true, dayOne ? 1 : 0);
                    continue;
                }
                var chId = fi.RedCorner == champ ? fi.BlueCorner : fi.RedCorner;
                MarkShot(tid, chId);
                int rank = RankOf(chId);
                int heat = _preHeat.GetValueOrDefault(Key(champ!.Value, chId));

                // The queue as the matchmaker reads it: the first ranked man free on the date, in form,
                // and not the man the champion just beat.
                FighterId? stale = _preLast.TryGetValue(champ.Value, out var cl) && cl.Won ? cl.Opp : null;
                string? Why(FighterId id, bool requireWorthy)
                {
                    if (!_preState.TryGetValue(id, out var s)) return "gone";
                    if (s.Org != promo.Id) return "left/unsigned";
                    if (s.Status == FighterStatus.Retired) return "retired";
                    var date = e.Date;
                    // HealReturning runs before the calendar: an injury back by tick day is healed.
                    var tick = _preDate.AddDays(7);
                    if (s.Status == FighterStatus.Injured && !(s.InjuryBack is { } back && back <= tick)) return "injured";
                    if (s.Status == FighterStatus.Suspended && !(s.SuspendedUntil is { } su && su <= tick)) return "suspended";
                    if (s.ReadyOn is { } ro && ro > date) return "recovering (just fought)";
                    if (s.FightsLeft is <= 0) return "deal spent";
                    if (_preCommitted.Contains(id)) return "booked elsewhere";
                    if (world.Events.Values.Any(x => x.Id != e.Id && newEvents.Contains(x.Id) && x.Card.Any(f => f.RedCorner == id || f.BlueCorner == id)))
                        return "booked elsewhere (same week)";
                    if (stale == id) return "champ just beat him";
                    if (requireWorthy && !s.Worthy) return "not in form";
                    return null;
                }
                FighterId? queue = ranks.FirstOrDefault(id => Why(id, true) is null);
                if (queue is { Value: 0 }) queue = null;
                bool queuePick = queue == chId;
                string? passedWhy = null;
                if (rank != 1 && ranks.Count > 0)
                {
                    var top = ranks[0];
                    passedWhy = top == chId ? null : Why(top, true) ?? "available (jumped)";
                }

                string cause;
                if (dayOne) cause = "day-one calendar";
                else if (Storylines.IsSuperfight(e)) cause = "superfight";
                else if (ForwardCalendar.AuthoredBookingOn(world, e) is not null && ReferenceEquals(e.Card[^1], fi)) cause = "authored";
                else if (!newEvents.Contains(e.Id) && _preEventBelts.TryGetValue(e.Id, out var had) && had.Contains(tid)) cause = "stand-in (bout saved)";
                else if (!newEvents.Contains(e.Id)) cause = "card rebuilt/refilled";
                else if (_preLast.TryGetValue(chId, out var last) && last.Opp == champ)
                {
                    bool wasChamp = belt!.Lineage.Count >= 2 && belt.Lineage[^2].Champion == chId;
                    cause = wasChamp ? "rematch: dethroned champ" : "rematch: other (draw/lost)";
                }
                else if (heat >= 50) cause = "feud 50+";
                else if (heat >= 40) cause = "feud 40-49";
                else cause = queuePick ? "queue" : "queue (other)";
                _booked[fi.Id] = new Booking(cause, rank, ranks.Count, heat, queuePick, passedWhy, null, false, dayOne ? 1 : 0);
            }
        }
    }

    private static HashSet<FighterId> RankedOrChampions(World world)
    {
        var set = new HashSet<FighterId>();
        foreach (var t in world.Titles.Values) if (t.CurrentChampion is { } c) set.Add(c);
        foreach (var p in world.Promotions.Values)
            foreach (var d in p.Divisions) set.UnionWith(d.Rankings);
        return set;
    }

    public static void Print(IReadOnlyList<(TitleShots Ts, int Years)> runs)
    {
        var shots = runs.SelectMany(r => r.Ts.Shots).ToList();
        double years = runs.Sum(r => r.Years);
        Console.WriteLine();
        Console.WriteLine("  --- TITLE SHOTS (challengers of a reigning champion, snapshotted the week before; DURABLE: fought bouts only) ---");
        if (shots.Count == 0) { Console.WriteLine("  none"); return; }
        double P(Func<Shot, bool> pred) => 100.0 * shots.Count(pred) / shots.Count;
        var ranked = shots.Where(s => s.Rank > 0).ToList();
        Console.WriteLine($"  title shots: {shots.Count} ({shots.Count / years:0.0}/run-year) · challenger #1 {P(s => s.Rank == 1):0.0}% · top-3 {P(s => s.Rank is >= 1 and <= 3):0.0}% · top-5 {P(s => s.Rank is >= 1 and <= 5):0.0}% · unranked {P(s => s.Rank == 0):0.0}% · mean rank (ranked) {(ranked.Count == 0 ? 0 : ranked.Average(s => s.Rank)):0.0}");
        Console.WriteLine($"  challenger record: wins {shots.Average(s => s.Wins):0.0} · losses {shots.Average(s => s.Losses):0.0} · win share {shots.Average(s => s.Wins / (double)Math.Max(1, s.Wins + s.Losses)):0.000} · on a 2+ win streak {P(s => s.Streak >= 2):0.0}% · on a loss {P(s => s.Streak < 0):0.0}% · wins over ranked/champions in last 8 {shots.Average(s => s.WinsOverRanked):0.00}");
        Console.WriteLine($"  lifers 'passed over' (monthly sample): {runs.Sum(r => r.Ts.PassedOverLiferMonths) / years:0.00} lifer-months/run-year · distinct lifers {runs.Sum(r => r.Ts.PassedOverLifers.Count) / years:0.00}/run-year");

        // --- Why: the booking-side split (fought shots at a reigning champion, classified the week they were booked) ---
        var known = shots.Where(s => s.At is not null).ToList();
        Console.WriteLine($"  WHY (fought shots at a reigning champion, classified at booking against the pre-tick world; {known.Count} of {shots.Count} seen booked):");
        if (known.Count > 0)
        {
            double Pk(Func<Shot, bool> f) => 100.0 * known.Count(f) / known.Count;
            Console.WriteLine($"    rank AT BOOKING: #1 {Pk(s => s.At!.RankAtBooking == 1):0.0}% · top-3 {Pk(s => s.At!.RankAtBooking is >= 1 and <= 3):0.0}% · top-5 {Pk(s => s.At!.RankAtBooking is >= 1 and <= 5):0.0}% · 6+ {Pk(s => s.At!.RankAtBooking > 5):0.0}% · unranked {Pk(s => s.At!.RankAtBooking == 0):0.0}%"
                + $" · rank moved booking->fight {Pk(s => s.At!.RankAtBooking != s.Rank):0.0}% · queue pick {Pk(s => s.At!.QueuePick):0.0}%");
            int outside = known.Count(s => s.Rank is not (>= 1 and <= 3));
            Console.WriteLine($"    {"cause",-28} {"n",6} {"share",6} | {"#1",5} {"top3",5} {"top5",5} | {"of non-top-3 (fight rank)",8}");
            foreach (var g in known.GroupBy(s => s.At!.Cause).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                double n = g.Count();
                int nonTop3 = g.Count(s => s.Rank is not (>= 1 and <= 3));
                Console.WriteLine($"    {g.Key,-28} {g.Count(),6} {100.0 * n / known.Count,5:0.0}% | {100.0 * g.Count(s => s.Rank == 1) / n,4:0}% {100.0 * g.Count(s => s.Rank is >= 1 and <= 3) / n,4:0}% {100.0 * g.Count(s => s.Rank is >= 1 and <= 5) / n,4:0}% | {100.0 * nonTop3 / Math.Max(1, outside),5:0.0}%");
            }
            Console.WriteLine("    when the challenger was not #1 at booking, why #1 was passed over (share of those shots):");
            var notFirst = known.Where(s => s.At!.RankAtBooking != 1 && s.At.PassedOverWhy is not null).ToList();
            foreach (var g in notFirst.GroupBy(s => s.At!.PassedOverWhy!).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
                Console.WriteLine($"      {g.Key,-30} {g.Count(),6} {100.0 * g.Count() / Math.Max(1, notFirst.Count),5:0.0}%");
            var thin = known.Where(s => s.At!.RankedCount < 5).ToList();
            Console.WriteLine($"    thin divisions (<5 ranked at booking): {thin.Count} shots ({100.0 * thin.Count / known.Count:0.0}%); ranked 6+ or unranked at booking in them {thin.Count(s => s.At!.RankAtBooking is 0 or > 5)}");
            Console.WriteLine($"    feud at booking (heat 40+): {Pk(s => s.At!.Heat >= 40):0.0}% of shots ({known.Count(s => s.At!.Heat >= 40) / years:0.00}/run-year); any rivalry {Pk(s => s.At!.Heat > 0):0.0}%; heat 40+ and ALSO the queue pick {known.Count(s => s.At!.Heat >= 40 && s.At.QueuePick)}");
        }

        // --- Belts: rates, outcomes, quality ---
        var all = runs.SelectMany(r => r.Ts.AllTitleFights).ToList();
        double beltYears = runs.Sum(r => r.Ts.BeltWeeks) / 52.0;
        if (all.Count > 0 && beltYears > 0)
        {
            var defs = all.Where(x => !x.Vacant).ToList();
            Console.WriteLine($"  BELTS (AI orgs; {beltYears:0} belt-years sampled weekly): title fights {all.Count / beltYears:0.000}/belt-year · at a champion {defs.Count / beltYears:0.000} · successful defenses {defs.Count(x => x.Retained) / beltYears:0.000}/belt-year ({100.0 * defs.Count(x => x.Retained) / Math.Max(1, defs.Count):0.0}% retained) · vacant crownings {all.Count(x => x.Vacant) / beltYears:0.000}");
            Console.WriteLine($"    title-fight MatchRating mean {all.Average(x => x.Rating):0.0} (at a champion {(defs.Count == 0 ? 0 : defs.Average(x => x.Rating)):0.0}) · feud (heat 40+ at booking) title fights {all.Count(x => x.Heat >= 40)} ({all.Count(x => x.Heat >= 40) / beltYears:0.000}/belt-year, {100.0 * all.Count(x => x.Heat >= 40) / all.Count:0.0}%), their rating {(all.Any(x => x.Heat >= 40) ? all.Where(x => x.Heat >= 40).Average(x => x.Rating) : 0):0.0}");
            var vac = all.Where(x => x.Vacant && x.At is { DayOne: 0 }).ToList();
            if (vac.Count > 0)
                Console.WriteLine($"    vacant crownings booked in-run: {vac.Count} · both men top-2 {100.0 * vac.Count(x => x.At!.RankAtBooking == 1 && x.At.OtherRank == 2) / vac.Count:0.0}% · both top-3 {100.0 * vac.Count(x => x.At!.OtherRank <= 3) / vac.Count:0.0}% · a man outside the top 5 {100.0 * vac.Count(x => x.At!.OtherRank > 5) / vac.Count:0.0}%");
        }
    }

    public static void PrintWaiting(IReadOnlyList<TitleShots> runs)
    {
        var gaps = runs.SelectMany(r => r.NumberOneGaps).OrderBy(x => x).ToList();
        var spells = runs.SelectMany(r => r.Spells).ToList();
        Console.WriteLine("  --- THE #1 CONTENDER'S WAIT (AI houses, divisions with a reigning champion; #1 = house ranking before each tick) ---");
        if (gaps.Count > 0)
        {
            int Q(double q) => gaps[Math.Clamp((int)Math.Round(q * (gaps.Count - 1)), 0, gaps.Count - 1)];
            Console.WriteLine($"  days between bouts for a man ranked #1 going in: n={gaps.Count} median {Q(0.5)} p75 {Q(0.75)} p90 {Q(0.9)} p99 {Q(0.99)} max {gaps[^1]} · over 180 {100.0 * gaps.Count(g => g > 180) / gaps.Count:0.0}% · over 270 {100.0 * gaps.Count(g => g > 270) / gaps.Count:0.0}%");
        }
        if (spells.Count > 0)
        {
            var idle = spells.Select(s => s.MaxIdle).OrderBy(x => x).ToList();
            int Q(double q) => idle[Math.Clamp((int)Math.Round(q * (idle.Count - 1)), 0, idle.Count - 1)];
            Console.WriteLine($"  spells at #1: {spells.Count} · longest idle inside a spell median {Q(0.5)} p90 {Q(0.9)} max {idle[^1]} · spells idle past 180 days {spells.Count(s => s.MaxIdle > 180)} ({100.0 * spells.Count(s => s.MaxIdle > 180) / spells.Count:0.0}%) · past 270 {spells.Count(s => s.MaxIdle > 270)} ({100.0 * spells.Count(s => s.MaxIdle > 270) / spells.Count:0.0}%) · mean spell {spells.Average(s => s.Days):0} days");
            foreach (var g in spells.GroupBy(s => s.End).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
                Console.WriteLine($"    {g.Key,-34} {g.Count(),6} {100.0 * g.Count() / spells.Count,5:0.0}% · idle past 180 {g.Count(s => s.MaxIdle > 180),5} · past 270 {g.Count(s => s.MaxIdle > 270),5} · mean longest idle {g.Average(s => s.MaxIdle):0}");
        }
    }

    /// <summary>Reign lengths and defenses for reigns that started in the run and ended (DURABLE: lineages).</summary>
    public static (int Ended, double Days, double Defenses, int Ongoing, double OngoingDefenses) Reigns(World world, DateOnly start)
    {
        var ended = new List<TitleReign>();
        var ongoing = new List<TitleReign>();
        foreach (var t in world.Titles.Values)
        {
            if (world.Get(t.Promotion) is not { } p || p.Id == world.PlayerPromotion) continue;
            foreach (var r in t.Lineage.Where(r => r.WonOn > start))
                (r.LostOn is null ? ongoing : ended).Add(r);
        }
        return (ended.Count, ended.Count == 0 ? 0 : ended.Average(r => r.LostOn!.Value.DayNumber - r.WonOn.DayNumber),
            ended.Count == 0 ? 0 : ended.Average(r => r.Defenses), ongoing.Count, ongoing.Count == 0 ? 0 : ongoing.Average(r => r.Defenses));
    }
}
