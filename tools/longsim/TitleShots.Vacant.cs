using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace LongSim;

/// <summary>
/// VACANT BELTS (reporting only, for <c>compare</c>): how an empty belt gets a champion.
/// <list type="bullet">
/// <item>Vacancy spells per belt at AI houses, sampled after each tick: when the belt went empty, when a
/// crowning bout for it first went on a card, when someone held it again (or the house went, or the run
/// ended). Belts already empty on day one are counted apart.</item>
/// <item>Every crowning bout booked in-run, classified the week it first appeared on a card against the
/// PRE-tick world (what the matchmaker read): both men's ranks at booking, and for each of the house's
/// #1 and #2 who is NOT in it, why (the same reasons as the title-shot split, with "booked elsewhere"
/// broken down: a title fight, or a non-title bout booked before / since the belt went vacant).</item>
/// <item>The two men's ranks the week before the night, and whether the bout was fought.</item>
/// </list>
/// Split by the house's size tier at booking. Read-only.
/// </summary>
public sealed partial class TitleShots
{
    public sealed record VacantBooking(string Tier, string Cause, int RankA, int RankB, int RankedCount, bool OnTop,
        int DaysVacant, int TopFiveFree, List<string> Missing, List<int> BookedGapDays)
    {
        public int NightA { get; set; } = -1;
        public int NightB { get; set; } = -1;
        public bool BothTop2 => Math.Min(RankA, RankB) == 1 && Math.Max(RankA, RankB) == 2;
        public bool BothTop5 => RankA is >= 1 and <= 5 && RankB is >= 1 and <= 5;
    }

    private sealed class VacSpell
    {
        public DateOnly Start; public string Tier = ""; public bool DayOne; public DateOnly? Booked; public int Bookings;
        public HashSet<FighterId> CommittedAtStart = new();
    }

    private readonly Dictionary<TitleId, VacSpell> _vac = new();
    private readonly Dictionary<FightId, VacantBooking> _vacPending = new();
    private HashSet<TitleId>? _vacantDayOne;

    /// <summary>Crowning bouts booked in-run (not day one) that were fought.</summary>
    public List<VacantBooking> VacantFought { get; } = new();
    /// <summary>Crowning bouts booked in-run that never ran (cancelled or the bout came off).</summary>
    public int VacantLost { get; private set; }
    /// <summary>Closed vacancy spells: tier, day-one, days to first booking (-1 never), days to a champion (-1 never), how it ended, bookings made.</summary>
    public List<(string Tier, bool DayOne, int ToBook, int ToCrown, string End, int Bookings)> VacantSpells { get; } = new();

    private static bool LiveAi(World world, Title t, out Promotion promo)
    {
        promo = world.Get(t.Promotion)!;
        return promo is { Defunct: false } && promo.Id != world.PlayerPromotion;
    }

    private void VacantNightRanks(World world)
    {
        if (_vacantDayOne is null)
        {
            _vacantDayOne = new HashSet<TitleId>();
            foreach (var t in world.Titles.Values)
                if (t.CurrentChampion is null && LiveAi(world, t, out _)) _vacantDayOne.Add(t.Id);
        }
        if (_vacPending.Count == 0) return;
        var horizon = world.CurrentDate.AddDays(7);
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Scheduled || e.Date > horizon || world.Get(e.Promotion) is not { } promo) continue;
            foreach (var fi in e.Card)
            {
                if (!_vacPending.TryGetValue(fi.Id, out var vb) || vb.NightA >= 0 || fi.Title is not { } tid) continue;
                var div = promo.Divisions.FirstOrDefault(d => d.Title == tid);
                int R(FighterId id) => div is null ? 0 : div.Rankings.IndexOf(id) + 1;
                vb.NightA = R(fi.RedCorner);
                vb.NightB = R(fi.BlueCorner);
            }
        }
    }

    private void RecordVacantBooking(World world, Event e, Fight fi, Promotion promo, TitleId tid, List<FighterId> ranks,
        HashSet<EventId> newEvents, bool dayOne)
    {
        if (dayOne) return;
        int RankOf(FighterId id) => ranks.IndexOf(id) + 1;
        _vac.TryGetValue(tid, out var spell);
        if (spell is not null) { spell.Booked ??= world.CurrentDate; spell.Bookings++; }

        string cause = ForwardCalendar.AuthoredBookingOn(world, e) is not null && ReferenceEquals(e.Card[^1], fi) ? "authored"
            : !newEvents.Contains(e.Id) && _preEventBelts.TryGetValue(e.Id, out var had) && had.Contains(tid) ? "stand-in (bout saved)"
            : !newEvents.Contains(e.Id) ? "card rebuilt/refilled"
            : "new card";

        var missing = new List<string>();
        var gaps = new List<int>();
        for (int i = 0; i < 2 && i < ranks.Count; i++)
        {
            var id = ranks[i];
            if (id == fi.RedCorner || id == fi.BlueCorner) continue;
            missing.Add($"#{i + 1}: " + WhyNotCrowned(world, e, id, newEvents, spell, gaps));
        }
        if (ranks.Count < 2) missing.Add(ranks.Count == 0 ? "division unranked" : "division has 1 ranked");

        int topFiveFree = 0;
        for (int i = 0; i < 5 && i < ranks.Count; i++)
        {
            var id = ranks[i];
            if (id == fi.RedCorner || id == fi.BlueCorner) { topFiveFree++; continue; }
            if (WhyNotCrowned(world, e, id, newEvents, spell, null).StartsWith("available")) topFiveFree++;
        }

        var vb = new VacantBooking(TierOf(promo), cause, RankOf(fi.RedCorner), RankOf(fi.BlueCorner), ranks.Count,
            ReferenceEquals(e.Card[^1], fi), spell is null ? -1 : world.CurrentDate.DayNumber - spell.Start.DayNumber,
            topFiveFree, missing, gaps);
        _vacPending[fi.Id] = vb;
    }

    // Why a ranked man is not one of the two men crowned, read against the pre-tick world.
    private string WhyNotCrowned(World world, Event e, FighterId id, HashSet<EventId> newEvents, VacSpell? spell, List<int>? gaps)
    {
        if (!_preState.TryGetValue(id, out var s)) return "gone";
        if (s.Status == FighterStatus.Retired) return "retired";
        if (s.Org != e.Promotion) return "left the house/unsigned";
        var tick = _preDate.AddDays(7);
        if (s.Status == FighterStatus.Injured && !(s.InjuryBack is { } back && back <= tick))
            return s.InjuryBack is { } b2 && b2 <= e.Date ? "hurt (back by the night)" : "hurt";
        if (s.Status == FighterStatus.Suspended && !(s.SuspendedUntil is { } su && su <= tick)) return "suspended";
        if (s.ReadyOn is { } ro && ro > e.Date) return "recovering (just fought)";
        if (s.FightsLeft is <= 0) return "deal spent";
        if (_preCommitted.Contains(id))
        {
            var other = world.Events.Values.FirstOrDefault(x => x.Id != e.Id && x.Status is EventStatus.Scheduled or EventStatus.Completed
                && x.Card.Any(f => f.RedCorner == id || f.BlueCorner == id) && x.Date >= _preDate);
            var bout = other?.Card.FirstOrDefault(f => f.RedCorner == id || f.BlueCorner == id);
            if (other is not null) gaps?.Add(e.Date.DayNumber - other.Date.DayNumber);
            if (bout is { IsTitleFight: true }) return "booked elsewhere: a title fight";
            if (other is not null && other.Promotion != e.Promotion) return "booked elsewhere: another house";
            return spell is not null && spell.CommittedAtStart.Contains(id)
                ? "booked elsewhere: before the vacancy"
                : "booked elsewhere: since the vacancy";
        }
        if (world.Events.Values.Any(x => x.Id != e.Id && newEvents.Contains(x.Id) && x.Card.Any(f => f.RedCorner == id || f.BlueCorner == id)))
            return "booked elsewhere: same week";
        if (e.Card.Any(f => f.RedCorner == id || f.BlueCorner == id)) return "on this card, another bout";
        return s.Worthy ? "available (skipped)" : "available, not in form (skipped)";
    }

    private void VacantWeek(World world)
    {
        // The night: fought or lost.
        foreach (var (id, vb) in _vacPending.ToList())
        {
            var ev = world.Events.Values.FirstOrDefault(x => x.Card.Any(f => f.Id == id));
            if (ev is null || ev.Status == EventStatus.Cancelled) { VacantLost++; _vacPending.Remove(id); continue; }
            if (ev.Status != EventStatus.Completed) continue;
            if (ev.Card.First(f => f.Id == id).Outcome is not null) VacantFought.Add(vb); else VacantLost++;
            _vacPending.Remove(id);
        }

        // Spells.
        var today = world.CurrentDate;
        HashSet<FighterId>? committed = null;
        foreach (var t in world.Titles.Values.OrderBy(t => t.Id.Value))
        {
            bool live = LiveAi(world, t, out var promo);
            if (_vac.TryGetValue(t.Id, out var sp))
            {
                string? end = !live ? "house gone/player" : t.CurrentChampion is not null ? "crowned" : null;
                if (end is null) continue;
                VacantSpells.Add((sp.Tier, sp.DayOne, sp.Booked is { } b ? b.DayNumber - sp.Start.DayNumber : -1,
                    end == "crowned" ? today.DayNumber - sp.Start.DayNumber : -1, end, sp.Bookings));
                _vac.Remove(t.Id);
                continue;
            }
            if (!live || t.CurrentChampion is not null) continue;
            var div = promo.Divisions.FirstOrDefault(d => d.Title == t.Id);
            if (div is null) continue;   // a retired belt: no division carries it
            committed ??= ForwardCalendar.Committed(world);
            var nsp = new VacSpell { Start = today, Tier = TierOf(promo), DayOne = _vacantDayOne?.Contains(t.Id) == true };
            foreach (var id in div.Rankings.Take(5)) if (committed.Contains(id)) nsp.CommittedAtStart.Add(id);
            if (world.Events.Values.Any(x => x.Status == EventStatus.Scheduled && x.Card.Any(f => f.IsTitleFight && f.Title == t.Id)))
                { nsp.Booked = today; nsp.Bookings++; }   // vacated and re-booked inside one tick
            _vac[t.Id] = nsp;
        }
    }

    private void CloseVacant(World world)
    {
        foreach (var sp in _vac.Values)
            VacantSpells.Add((sp.Tier, sp.DayOne, sp.Booked is { } b ? b.DayNumber - sp.Start.DayNumber : -1, -1, "still vacant at the end", sp.Bookings));
        _vac.Clear();
    }

    public static void PrintVacant(IReadOnlyList<TitleShots> runs)
    {
        Console.WriteLine("  --- VACANT BELTS (AI houses; booked in-run, classified at booking against the pre-tick world; tier at booking) ---");
        var fought = runs.SelectMany(r => r.VacantFought).ToList();
        int lost = runs.Sum(r => r.VacantLost);
        static string Pc(int n, int of) => $"{100.0 * n / Math.Max(1, of),5:0.0}%";
        static int Q(List<int> xs, double q) => xs.Count == 0 ? 0 : xs[Math.Clamp((int)Math.Round(q * (xs.Count - 1)), 0, xs.Count - 1)];
        foreach (var tier in new[] { "all", "City", "Regional", "National+" })
        {
            var v = fought.Where(x => tier == "all" || x.Tier == tier).ToList();
            if (v.Count == 0) { Console.WriteLine($"  {tier,-10} none"); continue; }
            var night = v.Where(x => x.NightA >= 0).ToList();
            Console.WriteLine($"  {tier,-10} crownings fought {v.Count,5} · both top-2 at booking {Pc(v.Count(x => x.BothTop2), v.Count)} · both top-5 {Pc(v.Count(x => x.BothTop5), v.Count)}"
                + $" · a man outside the top 5 or unranked {Pc(v.Count(x => !x.BothTop5), v.Count)} · thin (<5 ranked) {Pc(v.Count(x => x.RankedCount < 5), v.Count)}"
                + $" · on the night: both top-2 {Pc(night.Count(x => Math.Min(x.NightA, x.NightB) == 1 && Math.Max(x.NightA, x.NightB) == 2), night.Count)}, both top-5 {Pc(night.Count(x => x.NightA is >= 1 and <= 5 && x.NightB is >= 1 and <= 5), night.Count)}"
                + $" · main event {Pc(v.Count(x => x.OnTop), v.Count)} · top-5 men free at booking mean {v.Average(x => x.TopFiveFree):0.00}");
        }
        Console.WriteLine($"  crowning bouts booked in-run but never fought: {lost}");
        if (fought.Count > 0)
        {
            Console.WriteLine("    cause at booking: " + string.Join(" · ", fought.GroupBy(x => x.Cause).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key} {g.Count()} ({100.0 * g.Count() / fought.Count:0.0}%, both top-2 {100.0 * g.Count(x => x.BothTop2) / g.Count():0}%)")));
            var miss = fought.SelectMany(x => x.Missing.Select(m => (x.Tier, Why: m))).ToList();
            Console.WriteLine($"    why the house's #1 / #2 was NOT in the crowning bout ({miss.Count} missing places over {fought.Count} crownings; share of missing places, then by tier City/Regional/National+):");
            foreach (var g in miss.GroupBy(m => m.Why[(m.Why.IndexOf(':') + 2)..]).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
                Console.WriteLine($"      {g.Key,-40} {g.Count(),6} {Pc(g.Count(), miss.Count)} | #1 {g.Count(m => m.Why.StartsWith("#1")),5} #2 {g.Count(m => m.Why.StartsWith("#2")),5}"
                    + $" | {g.Count(m => m.Tier == "City"),5} {g.Count(m => m.Tier == "Regional"),5} {g.Count(m => m.Tier == "National+"),5}");
            var gaps = fought.SelectMany(x => x.BookedGapDays).OrderBy(x => x).ToList();
            if (gaps.Count > 0)
                Console.WriteLine($"    a missing man booked elsewhere: days from his other bout to the crowning night median {Q(gaps, 0.5)} p10 {Q(gaps, 0.1)} p90 {Q(gaps, 0.9)} · his bout before the night {Pc(gaps.Count(g => g > 0), gaps.Count)}");
        }
        var spells = runs.SelectMany(r => r.VacantSpells).ToList();
        foreach (var dayOne in new[] { false, true })
        {
            foreach (var tier in new[] { "all", "City", "Regional", "National+" })
            {
                var s = spells.Where(x => x.DayOne == dayOne && (tier == "all" || x.Tier == tier)).ToList();
                if (s.Count == 0) continue;
                var book = s.Where(x => x.ToBook >= 0).Select(x => x.ToBook).OrderBy(x => x).ToList();
                var crown = s.Where(x => x.ToCrown >= 0).Select(x => x.ToCrown).OrderBy(x => x).ToList();
                Console.WriteLine($"  vacancy spells{(dayOne ? " (vacant on day one)" : "")} {tier,-10} {s.Count,5} · crowned {Pc(s.Count(x => x.End == "crowned"), s.Count)} · house gone {Pc(s.Count(x => x.End == "house gone/player"), s.Count)} · still vacant at the end {Pc(s.Count(x => x.End == "still vacant at the end"), s.Count)}"
                    + $" · days to first crowning booking median {Q(book, 0.5)} p90 {Q(book, 0.9)} · days vacant until crowned median {Q(crown, 0.5)} mean {(crown.Count == 0 ? 0 : crown.Average()):0} p90 {Q(crown, 0.9)} · over 120 {Pc(crown.Count(x => x > 120), crown.Count)} · bookings per spell {s.Average(x => x.Bookings):0.00}");
            }
        }
    }
}
