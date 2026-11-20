using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// What keeps a house on television (compare's MEDIA HEALTH block, per seed and summarised): the flagship's bank by
/// year and its lowest quarter-end, its broadcast income by quarter (DURABLE: ledger) and the quarters under half its
/// normal level; media strikes by clause and media deals lost by reason (for the AFC and for every org by its tier at
/// the time); the share of cards whose headliner misses the house's strictest live star clause and where a man who
/// would have cleared it was; and two side checks: the streaming lane after an AI house runs its own platform, and
/// the gate the flagship turns away at the room it picked.
/// <para>Strikes and the star check are judged on the week a card ran, against the deals the house held BEFORE that
/// week (a deal the card's miss tore up is still counted), with popularity as it stands at the end of the week (the
/// game judges it right after the fights; the drift inside a week is small). Deals lost are read off the deal
/// objects themselves: a deal gone from a live house's slate at 3 strikes was walked out on; at term end with a
/// strike it was not renewed; at term end clean it lost the coin flip (or the house fell below the rung); mid-term
/// with its lane now held by another deal it was replaced. Cadence strikes are the monthly clock moving a deal's
/// <c>LastAiredOn</c> to a day no card of the house ran. Observes only; no draws.</para>
/// </summary>
public sealed class MediaHealth
{
    public ulong Seed;
    public int Years;
    public readonly List<decimal> AfcBankByYear = new();
    public decimal AfcMinQuarterBank = decimal.MaxValue;
    public readonly List<decimal> AfcBroadcastByQuarter = new();
    public decimal AfcNormalQuarter;
    public int AfcStarvedQuarters, AfcUnder10M, AfcLongestStarvedRun;

    /// <summary>Keyed by "AFC" and by tier label (the AFC counts in its tier too).</summary>
    public readonly Dictionary<string, int> JudgedCards = new(), StarCards = new(), OrgWeeks = new();
    public readonly Dictionary<(string Who, string What), int> Strikes = new(), Lost = new(), StarMiss = new(), Stream = new();
    public int AfcTurnedAway, AfcRoomCards, AfcCardsOverRoom;
    public decimal AfcGateTurnedAway, AfcDemand, AfcCapacity;

    // Per org (for the acquisitions audit): dated strikes and losses, weeks for sale, last weekly tier and bank.
    private readonly Dictionary<PromotionId, List<(DateOnly Date, string What)>> _log = new();
    private readonly Dictionary<PromotionId, int> _forSaleWeeks = new();
    private readonly Dictionary<PromotionId, (OrgSize Tier, decimal Bank, DateOnly On)> _last = new();
    /// <summary>One line per acquisition and one per day-one National+ org's fate (printed raw, joined across runs by hand).</summary>
    public readonly List<string> AcqRows = new(), FateRows = new();
    public readonly Dictionary<(string Tier, string Why), int> AcqBy = new();

    private void Log(PromotionId p, DateOnly d, string what)
    {
        if (!_log.TryGetValue(p, out var l)) _log[p] = l = new();
        l.Add((d, what));
    }

    private sealed record DealSnap(MediaDeal Deal, int Strikes, DateOnly? LastAired, int Months);
    private sealed record OrgSnap(Promotion P, OrgSize Tier, bool HadStream, List<DealSnap> Deals);
    private Dictionary<PromotionId, OrgSnap> _before = new();
    private List<Event> _due = new();
    private int _week;

    private static IEnumerable<string> Who(Promotion p, OrgSize tier) =>
        p.Abbreviation == "AFC" ? new[] { "AFC", OrgSizing.Label(tier) } : new[] { OrgSizing.Label(tier) };

    private void Add<TK>(Dictionary<TK, int> d, TK k, int n = 1) where TK : notnull => d[k] = d.GetValueOrDefault(k) + n;

    public void BeforeWeek(World world)
    {
        _before = new();
        foreach (var p in world.Promotions.Values)
        {
            if (p.Defunct) continue;
            _before[p.Id] = new OrgSnap(p, OrgSizing.SizeOf(p), p.Stream is not null,
                p.MediaDeals.Select(d => new DealSnap(d, d.Strikes, d.LastAiredOn, d.MonthsRemaining)).ToList());
        }
        var horizon = world.CurrentDate.AddDays(8);
        _due = world.Events.Values.Where(e => e.Status == EventStatus.Scheduled && e.Date <= horizon).ToList();
    }

    public void AfterWeek(World world)
    {
        _week++;
        var ran = _due.Where(e => e.Status == EventStatus.Completed && e.Card.Count > 0).ToList();
        var ranBy = ran.GroupBy(e => e.Promotion).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var ev in ran)
        {
            if (!_before.TryGetValue(ev.Promotion, out var snap)) continue;
            var who = Who(snap.P, snap.Tier).ToList();
            var judged = snap.Deals.Where(d => !(d.Deal.Kind == MediaKind.PpvCarrier && ev.Tier < EventTier.Ppv)).ToList();
            if (judged.Count == 0) continue;
            foreach (var w in who) Add(JudgedCards, w);

            foreach (var d in judged)
            {
                var missed = DealCompliance.Audit(world, ev, d.Deal.Requirements).Where(c => !c.Met).Select(c => c.Requirement.Kind).ToHashSet();
                if (missed.Count == 0) continue;
                bool star = missed.Contains(DealRequirementKind.MinStarPower), quality = missed.Contains(DealRequirementKind.MinCardQuality);
                string what = star && quality ? "star+quality" : star ? "star" : quality ? "quality" : "other";
                foreach (var w in who) Add(Strikes, (w, what));
                Log(ev.Promotion, ev.Date, "strike:" + what);
            }

            // The strictest live star clause against the man on top.
            int? bar = judged.SelectMany(d => d.Deal.Requirements).Where(r => r.Kind == DealRequirementKind.MinStarPower)
                .Select(r => (int?)r.Threshold).Max();
            if (bar is not { } t) continue;
            foreach (var w in who) Add(StarCards, w);
            int Pop(FighterId id) => world.Get(id)?.Intangibles.Popularity.Value ?? 0;
            var main = ev.Card[^1];
            if (Math.Max(Pop(main.RedCorner), Pop(main.BlueCorner)) >= t) continue;
            int window = Math.Max(1, ev.EffectiveMainCard);
            var mainCard = ev.Card.Skip(Math.Max(0, ev.Card.Count - window)).Take(window - 1).ToList();
            var onCard = ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner }).ToHashSet();
            string where =
                mainCard.Any(f => Math.Max(Pop(f.RedCorner), Pop(f.BlueCorner)) >= t) ? "a main-card bout cleared it"
                : ev.Card.Any(f => Math.Max(Pop(f.RedCorner), Pop(f.BlueCorner)) >= t) ? "a prelim cleared it"
                : world.RosterOf(snap.P.Id).Any(f => !onCard.Contains(f.Id) && f.Status != FighterStatus.Retired && f.Intangibles.Popularity.Value >= t) ? "only a man off the card cleared it"
                : "nobody signed cleared it";
            foreach (var w in who)
            {
                Add(StarMiss, (w, where));
                Add(StarMiss, (w, main.IsTitleFight ? "(title on top)" : "(non-title on top)"));
            }
        }

        foreach (var snap in _before.Values)
        {
            var p = snap.P;
            if (p.Defunct) continue;
            var who = Who(p, snap.Tier).ToList();
            foreach (var w in who) Add(OrgWeeks, w);
            _last[p.Id] = (snap.Tier, p.Balance.Amount, world.CurrentDate);
            if (MmaManager.Core.Sim.Living.Acquisitions.IsForSale(world, p)) _forSaleWeeks[p.Id] = _forSaleWeeks.GetValueOrDefault(p.Id) + 1;
            var ladder = MediaService.LadderView(world, p);
            ranBy.TryGetValue(p.Id, out var cards);
            foreach (var d in snap.Deals)
            {
                // A cadence strike: the monthly clock moved the deal to a day no card of ours ran.
                if (d.Deal.LastAiredOn != d.LastAired && d.Deal.LastAiredOn is { } aired
                    && (cards is null || !cards.Any(e => e.Date == aired)))
                    foreach (var w in who) Add(Strikes, (w, "cadence"));
                if (p.MediaDeals.Contains(d.Deal)) continue;
                string why = d.Deal.Strikes >= MediaService.StrikeLimit ? "walked at 3 strikes"
                    : d.Deal.MonthsRemaining <= 0 ? (d.Deal.Strikes > 0 ? "not renewed (a strike)"
                        : ladder.FirstOrDefault(r => r.Kind == d.Deal.Kind).Qualified ? "not renewed (coin flip)" : "not renewed (below the bar)")
                    : p.MediaDeals.Any(x => MediaLanes.LaneOf(x.Kind) == MediaLanes.LaneOf(d.Deal.Kind)) ? "replaced"
                    : "other";
                foreach (var w in who) Add(Lost, (w, why));
                Log(p.Id, world.CurrentDate, "lost:" + why);
            }

            // The streaming lane at houses a streaming giant would sign.
            if (ladder.FirstOrDefault(r => r.Kind == MediaKind.MajorStream).Qualified)
            {
                bool major = p.MediaDeals.Any(x => x.Kind == MediaKind.MajorStream);
                string state = major ? "weeks with a MajorStream deal" : p.Stream is not null ? "weeks on own platform only" : "weeks with neither";
                foreach (var w in who) Add(Stream, (w, state));
            }
            foreach (var nd in p.MediaDeals.Where(x => x.Kind == MediaKind.MajorStream && !snap.Deals.Any(s => ReferenceEquals(s.Deal, x))))
                foreach (var w in who) Add(Stream, (w, snap.HadStream ? "MajorStream signed while running own platform" : "MajorStream signed (no own platform)"));
            if (snap.Deals.Any(s => s.Deal.Kind == MediaKind.MajorStream && !p.MediaDeals.Contains(s.Deal)))
                foreach (var w in who) Add(Stream, (w, "MajorStream deals lost"));
            if (!snap.HadStream && p.Stream is not null)
                foreach (var w in who) Add(Stream, (w, "own platform launched"));
        }

        // The flagship's rooms: the house it could have sold against the one it played.
        foreach (var ev in ran)
        {
            if (world.Get(ev.Promotion) is not { Abbreviation: "AFC" } afc || ev.Financials is not { } fin) continue;
            int cap = ev.VenueId is { } vid && world.Get(vid) is { } v ? v.Capacity : fin.Attendance;
            int demand = Financials.Demand(world, ev);
            int turned = Math.Max(0, demand - fin.Attendance);
            decimal mult = ev.Tier switch { EventTier.Ppv => 1.3m, EventTier.SuperCard => 1.6m, _ => 1.0m };
            AfcRoomCards++;
            AfcCapacity += cap;
            AfcDemand += demand;
            AfcTurnedAway += turned;
            if (demand > cap) AfcCardsOverRoom++;
            AfcGateTurnedAway += turned * Financials.TicketPrice(world, afc.Prestige.Value) * mult;
        }

        if (_week % 13 == 0 && world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == "AFC") is { } a)
            AfcMinQuarterBank = Math.Min(AfcMinQuarterBank, a.Balance.Amount);
    }

    public void YearEnd(World world)
    {
        Years++;
        AfcBankByYear.Add(world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == "AFC")?.Balance.Amount ?? 0m);
    }

    public void End(World world, DateOnly start)
    {
        int quarters = Years * 4;
        for (int q = 0; q < quarters; q++) AfcBroadcastByQuarter.Add(0m);
        if (world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == "AFC") is { } afc)
            foreach (var e in afc.Ledger)
            {
                if (e.Category != FinanceCategory.Broadcast || e.Date <= start) continue;
                int q = Math.Min(quarters - 1, (e.Date.DayNumber - start.DayNumber - 1) / 91);
                AfcBroadcastByQuarter[q] += e.Amount.Amount;
            }
        var y1 = AfcBroadcastByQuarter.Take(4).OrderBy(x => x).ToList();
        AfcNormalQuarter = y1.Count == 0 ? 0 : (y1[1] + y1[2]) / 2;
        int run = 0;
        foreach (var b in AfcBroadcastByQuarter)
        {
            if (b < 10_000_000m) AfcUnder10M++;
            if (b < AfcNormalQuarter / 2) { AfcStarvedQuarters++; run++; AfcLongestStarvedRun = Math.Max(AfcLongestStarvedRun, run); }
            else run = 0;
        }
        if (AfcMinQuarterBank == decimal.MaxValue) AfcMinQuarterBank = 0;

        // The acquisitions audit: every in-run sale, the seller's media trouble in the year before, and the fate of
        // every day-one org that was ever National+.
        var byName = world.Promotions.Values.ToDictionary(p => p.Name);
        string Media(PromotionId id, DateOnly from, DateOnly to)
        {
            var l = _log.GetValueOrDefault(id) ?? new();
            int N(string w) => l.Count(x => x.Date > from && x.Date <= to && x.What == w);
            return $"star {N("strike:star") + N("strike:star+quality")} qual {N("strike:quality")} lost3s {N("lost:walked at 3 strikes")} lost1s {N("lost:not renewed (a strike)")} coin {N("lost:not renewed (coin flip)")} repl {N("lost:replaced")}";
        }
        decimal Tv(Promotion p, DateOnly from, DateOnly to) => p.Ledger.Where(e => e.Category == FinanceCategory.Broadcast && e.Date > from && e.Date <= to).Sum(e => e.Amount.Amount);
        var sold = new HashSet<PromotionId>();
        foreach (var buyer in world.Promotions.Values)
            foreach (var e in buyer.Ledger)
            {
                if (e.Category != FinanceCategory.Acquisition || e.Amount.Amount >= 0 || e.Date <= start) continue;
                string name = e.Description.Replace("Acquisition of ", "");
                if (!byName.TryGetValue(name, out var seller)) continue;
                sold.Add(seller.Id);
                var last = _last.GetValueOrDefault(seller.Id);
                var yearBefore = e.Date.AddDays(-365);
                int star = (_log.GetValueOrDefault(seller.Id) ?? new()).Count(x => x.Date > yearBefore && x.Date <= e.Date && x.What.StartsWith("strike:star"));
                int walks = (_log.GetValueOrDefault(seller.Id) ?? new()).Count(x => x.Date > yearBefore && x.Date <= e.Date && (x.What == "lost:walked at 3 strikes" || x.What == "lost:not renewed (a strike)"));
                string why = star >= 3 || walks > 0 ? "star strikes/deal lost in the year before" : "no media trouble in the year before";
                AcqBy[(OrgSizing.Label(last.Tier), why)] = AcqBy.GetValueOrDefault((OrgSizing.Label(last.Tier), why)) + 1;
                AcqRows.Add($"ACQ seed {Seed} {seller.Abbreviation} ({OrgSizing.Label(last.Tier)}) bought {e.Date:yyyy-MM-dd} by {buyer.Abbreviation} for {-e.Amount.Amount / 1_000_000m:0.00}M"
                    + $" · last bank {last.Bank / 1_000_000m:0.00}M · weeks for sale {_forSaleWeeks.GetValueOrDefault(seller.Id)} · year before: {Media(seller.Id, yearBefore, e.Date)}"
                    + $" · broadcast year before {Tv(seller, yearBefore, e.Date) / 1_000_000m:0.00}M vs first year {Tv(seller, start, start.AddDays(365)) / 1_000_000m:0.00}M");
            }
        foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
        {
            if (!_last.TryGetValue(p.Id, out var l) || p.Abbreviation == "AFC") continue;
            if (!_log.ContainsKey(p.Id) && l.Tier < OrgSize.National && !_forSaleWeeks.ContainsKey(p.Id)) continue;
            string fate = sold.Contains(p.Id) ? "bought" : p.Defunct ? "bankrupt" : "alive";
            FateRows.Add($"FATE seed {Seed} {p.Abbreviation} {fate} · last tier {OrgSizing.Label(l.Tier)} · bank {(p.Defunct ? l.Bank : p.Balance.Amount) / 1_000_000m:0.00}M · weeks for sale {_forSaleWeeks.GetValueOrDefault(p.Id)} · run: {Media(p.Id, start, world.CurrentDate)}");
        }
    }

    // --- Report ---------------------------------------------------------------

    private static readonly string[] StrikeKinds = { "star", "quality", "star+quality", "other", "cadence" };
    private static readonly string[] LossKinds = { "walked at 3 strikes", "not renewed (a strike)", "not renewed (coin flip)", "not renewed (below the bar)", "replaced", "other" };
    private static readonly string[] MissKinds = { "a main-card bout cleared it", "a prelim cleared it", "only a man off the card cleared it", "nobody signed cleared it", "(title on top)", "(non-title on top)" };
    private static readonly string[] StreamKinds = { "weeks with a MajorStream deal", "weeks on own platform only", "weeks with neither", "MajorStream deals lost", "own platform launched", "MajorStream signed (no own platform)", "MajorStream signed while running own platform" };

    private static IEnumerable<string> Whos(IReadOnlyList<MediaHealth> seeds) =>
        new[] { "AFC" }.Concat(Enum.GetValues<OrgSize>().Reverse().Select(OrgSizing.Label))
            .Where(w => seeds.Any(s => s.OrgWeeks.ContainsKey(w)));

    public static void Print(IReadOnlyList<MediaHealth> seeds)
    {
        if (seeds.Count == 0) return;
        static decimal M(decimal x) => x / 1_000_000m;
        Console.WriteLine();
        Console.WriteLine("  --- MEDIA HEALTH (per seed: AFC bank at year end and lowest quarter-end (SAMPLED quarterly); broadcast by quarter DURABLE from the ledger, 'starved' = under half the seed's Y1 median quarter;"
            + " strikes/losses/star misses judged on the week a card ran, against the deals held before that week) ---");
        int ys = seeds.Max(s => s.AfcBankByYear.Count);
        Console.WriteLine($"  {"seed",4} | {string.Join(" ", Enumerable.Range(1, ys).Select(y => $"{"Y" + y,6}"))} | {"minQ",6} | {"TV/q",5} {"normal",6} {"starv",5} {"run",3} | {"starMiss",8} | {"strikes st/q/sq/c",17} | {"lost 3s/1s/coin/repl",20} | {"turned/q",8} {"gate lost/q",11}");
        foreach (var s in seeds.OrderBy(s => s.Seed))
        {
            int sc = s.StarCards.GetValueOrDefault("AFC");
            int miss = MissKinds.Take(4).Sum(k => s.StarMiss.GetValueOrDefault(("AFC", k)));
            int qs = Math.Max(1, s.AfcBroadcastByQuarter.Count);
            Console.WriteLine($"  {s.Seed,4} | {string.Join(" ", s.AfcBankByYear.Select(b => $"{M(b),6:0.0}"))} | {M(s.AfcMinQuarterBank),6:0.0} | {M(s.AfcBroadcastByQuarter.DefaultIfEmpty(0).Average()),5:0.0} {M(s.AfcNormalQuarter),6:0.0} {s.AfcStarvedQuarters,5} {s.AfcLongestStarvedRun,3} | "
                + $"{(sc == 0 ? "-" : $"{100.0 * miss / sc:0}% of {sc}"),8} | "
                + $"{s.Strikes.GetValueOrDefault(("AFC", "star")),3}/{s.Strikes.GetValueOrDefault(("AFC", "quality")),3}/{s.Strikes.GetValueOrDefault(("AFC", "star+quality")),3}/{s.Strikes.GetValueOrDefault(("AFC", "cadence")),3} | "
                + $"{s.Lost.GetValueOrDefault(("AFC", "walked at 3 strikes")),4}/{s.Lost.GetValueOrDefault(("AFC", "not renewed (a strike)")),3}/{s.Lost.GetValueOrDefault(("AFC", "not renewed (coin flip)")),4}/{s.Lost.GetValueOrDefault(("AFC", "replaced")),4}      | "
                + $"{s.AfcTurnedAway / (double)qs / 1000,7:0.0}k {M(s.AfcGateTurnedAway / qs),10:0.00}");
        }

        Console.WriteLine();
        Console.WriteLine("  AFC bank distribution over seeds ($M):");
        for (int y = 0; y < ys; y++)
        {
            var v = seeds.Where(s => s.AfcBankByYear.Count > y).Select(s => s.AfcBankByYear[y]).ToList();
            Console.WriteLine($"    Y{y + 1} end   {Dist(v)}");
        }
        var minq = seeds.Select(s => s.AfcMinQuarterBank).ToList();
        Console.WriteLine($"    min quarter-end {Dist(minq)} · seeds below $0: {minq.Count(x => x < 0)}");
        var y5 = seeds.Select(s => s.AfcBankByYear.LastOrDefault()).ToList();
        Console.WriteLine($"    final year: under $50M in {y5.Count(x => x < 50_000_000m)}/{seeds.Count} · under $30M {y5.Count(x => x < 30_000_000m)} · under $20M {y5.Count(x => x < 20_000_000m)}");
        int quarters = seeds.Sum(s => s.AfcBroadcastByQuarter.Count);
        Console.WriteLine($"  AFC broadcast: mean {M(seeds.SelectMany(s => s.AfcBroadcastByQuarter).DefaultIfEmpty(0).Average()):0.00}M/quarter · quarters under half normal {seeds.Sum(s => s.AfcStarvedQuarters)}/{quarters} ({100.0 * seeds.Sum(s => s.AfcStarvedQuarters) / Math.Max(1, quarters):0.0}%)"
            + $" · under $10M {seeds.Sum(s => s.AfcUnder10M)} ({100.0 * seeds.Sum(s => s.AfcUnder10M) / Math.Max(1, quarters):0.0}%) · seeds with a starved quarter {seeds.Count(s => s.AfcStarvedQuarters > 0)} · with 2+ in a row {seeds.Count(s => s.AfcLongestStarvedRun >= 2)}");

        Console.WriteLine("  media strikes by clause (per 100 judged cards; a card missing two clauses of one deal is one 'star+quality' strike; cadence = monthly clock):");
        foreach (var w in Whos(seeds))
        {
            int cards = seeds.Sum(s => s.JudgedCards.GetValueOrDefault(w));
            int total = StrikeKinds.Sum(k => seeds.Sum(s => s.Strikes.GetValueOrDefault((w, k))));
            Console.WriteLine($"    {w,-14} cards {cards,6} · strikes {total,5} ({100.0 * total / Math.Max(1, cards):0.0}/100) · "
                + string.Join(" · ", StrikeKinds.Select(k => { int n = seeds.Sum(s => s.Strikes.GetValueOrDefault((w, k))); return $"{k} {n} ({100.0 * n / Math.Max(1, cards):0.0})"; })));
        }
        Console.WriteLine("  media deals lost by reason (per org-year in the tier):");
        foreach (var w in Whos(seeds))
        {
            double orgYears = seeds.Sum(s => s.OrgWeeks.GetValueOrDefault(w)) / 52.0;
            int total = LossKinds.Sum(k => seeds.Sum(s => s.Lost.GetValueOrDefault((w, k))));
            Console.WriteLine($"    {w,-14} org-years {orgYears,7:0.0} · lost {total,5} ({total / Math.Max(0.01, orgYears):0.00}/org-yr) · "
                + string.Join(" · ", LossKinds.Select(k => { int n = seeds.Sum(s => s.Lost.GetValueOrDefault((w, k))); return $"{k} {n} ({n / Math.Max(0.01, orgYears):0.00})"; })));
        }
        Console.WriteLine("  headliner under the house's strictest live star clause (share of cards with a star clause; then where a man who'd clear it was):");
        foreach (var w in Whos(seeds))
        {
            int cards = seeds.Sum(s => s.StarCards.GetValueOrDefault(w));
            int miss = MissKinds.Take(4).Sum(k => seeds.Sum(s => s.StarMiss.GetValueOrDefault((w, k))));
            Console.WriteLine($"    {w,-14} cards {cards,6} · missed {miss,5} ({100.0 * miss / Math.Max(1, cards):0.0}%) · "
                + string.Join(" · ", MissKinds.Select(k => $"{k} {seeds.Sum(s => s.StarMiss.GetValueOrDefault((w, k)))}")));
        }
        Console.WriteLine("  streaming lane at houses a MajorStream partner would sign (SAMPLED weekly; signings/losses from deal objects):");
        foreach (var w in Whos(seeds))
            if (StreamKinds.Any(k => seeds.Any(s => s.Stream.ContainsKey((w, k)))))
                Console.WriteLine($"    {w,-14} " + string.Join(" · ", StreamKinds.Select(k => $"{k} {seeds.Sum(s => s.Stream.GetValueOrDefault((w, k)))}")));
        int rc = seeds.Sum(s => s.AfcRoomCards);
        if (rc > 0)
        {
            int qs = Math.Max(1, quarters);
            Console.WriteLine($"  AFC rooms (demand re-estimated at week end, uncapped): cards {rc} · mean room {seeds.Sum(s => s.AfcCapacity) / rc:N0} · mean demand {seeds.Sum(s => s.AfcDemand) / rc:N0}"
                + $" · demand over the room on {100.0 * seeds.Sum(s => s.AfcCardsOverRoom) / rc:0.0}% of cards · turned away {seeds.Sum(s => (double)s.AfcTurnedAway) / qs / 1000:0.0}k/quarter"
                + $" · gate turned away {M(seeds.Sum(s => s.AfcGateTurnedAway) / qs):0.00}M/quarter");
        }
    }

    public static void PrintAcquisitions(IReadOnlyList<MediaHealth> seeds)
    {
        Console.WriteLine();
        Console.WriteLine("  --- ACQUISITIONS AUDIT (seller tier = last weekly sample before the sale; 'media trouble' = 3+ star strikes or a deal lost to strikes (walked, or not renewed with a strike) in the 365 days before) ---");
        foreach (var g in seeds.SelectMany(s => s.AcqBy).GroupBy(kv => kv.Key).OrderBy(g => g.Key.Tier).ThenBy(g => g.Key.Why))
            Console.WriteLine($"    {g.Key.Tier,-14} {g.Key.Why,-45} {g.Sum(kv => kv.Value)}");
        int forSale = seeds.Sum(s => s._forSaleWeeks.Values.Sum());
        Console.WriteLine($"    org-weeks for sale (IsForSale, weekly, all orgs): {forSale}");
        foreach (var s in seeds.OrderBy(s => s.Seed)) foreach (var r in s.AcqRows) Console.WriteLine("    " + r);
        foreach (var s in seeds.OrderBy(s => s.Seed)) foreach (var r in s.FateRows) Console.WriteLine("    " + r);
    }

    private static string Dist(List<decimal> v)
    {
        if (v.Count == 0) return "-";
        v = v.OrderBy(x => x).ToList();
        decimal Q(double q) => v[Math.Clamp((int)Math.Round(q * (v.Count - 1)), 0, v.Count - 1)] / 1_000_000m;
        return $"min {Q(0):0.0} p10 {Q(0.1):0.0} p25 {Q(0.25):0.0} median {Q(0.5):0.0} mean {v.Average() / 1_000_000m:0.0} max {Q(1):0.0}";
    }
}
