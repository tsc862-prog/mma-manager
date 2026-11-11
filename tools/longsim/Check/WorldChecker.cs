using MmaManager.Core.Model;

namespace LongSim.Check;

/// <summary>
/// World invariants, evaluated at a month boundary (after the monthly pass of the tick that crossed it).
/// One instance follows ONE world through a run: it keeps the cross-month state some rules need
/// (ledger baselines, ids ever seen, counters). Violations go to a shared <see cref="ViolationLog"/>.
///
/// Nothing here mutates the world. Add every rule a bug teaches you, with a name, a description and a test.
/// </summary>
public sealed class WorldChecker
{
    // name -> description. The summary table lists every one of these, fired or not.
    public static readonly (string Name, string Desc)[] Catalog =
    {
        ("RANK-CONTRACT",      "every ranked fighter exists, is contracted to the ranking promotion and is not retired"),
        ("RANK-SHAPE",         "no fighter ranked twice in a division, none in a division he does not fit, none ranked while champion"),
        ("CHAMP-CONTRACT",     "every champion exists, is contracted to the belt's promotion and is not retired"),
        ("TITLE-LINK",         "titles, promotion title lists and division belts agree; no belt on a missing promotion"),
        ("LINEAGE",            "reigns do not overlap, at most one is open, the open reign is the current champion"),
        ("BOOK-DOUBLE-DATE",   "no fighter on two cards on one date, none twice on one card, none against himself"),
        ("EVENT-NAME",         "a promotion's events have distinct names (the local circuit, which belongs to nobody, repeats its names by design)"),
        ("BOOK-SCHEDULED",     "scheduled cards: corners exist and are not retired; no fighter on two scheduled cards; title fights belong to the host org"),
        ("CONTRACT-LINK",      "no contract points at a missing or defunct promotion; no retired fighter holds a contract"),
        ("DEFUNCT",            "a defunct promotion runs no scheduled event, holds no champion, keeps no rankings, moves no money"),
        ("LEDGER",             "balance = opening balance + sum of ledger (opening taken at first sight); ledger never shrinks; entry sign matches category"),
        ("RECORD-HISTORY",     "fighter record is no smaller than the tally of his completed bouts in world.Events, and equals it for fighters minted after day one"),
        ("RECORD-SHAPE",       "wins-by-method never exceed wins; streak agrees with the bout sequence"),
        ("BOUT-ORDER",         "a fighter's bouts are in date order (BoutHistory is appended as results land)"),
        ("EVENT-FUTURE",       "no card is Completed (results written) while the world clock is still before its date"),
        ("BOUT-HISTORY",       "BoutHistory lists exactly the completed bouts the fighter was a corner of, each once"),
        ("REF-DANGLING",       "every id referenced anywhere (contracts, rankings, titles, cards, bonds, camps, news, mail, desk, wars) resolves"),
        ("ID-REUSE",           "collections enumerate in id order; an id is never handed to a second entity; counters never fall below an id in use or move backwards"),
        ("STATE-FIGHTER",      "status agrees with injury/suspension/contract; ages and ratings in range"),
        ("STATE-WOMEN-CLASS",  "every active woman is in a weight class some women's division runs (strawweight..featherweight)"),
        ("STATE-STIPEND",      "a camp stipend is only on a fighter whose org can pay it (he holds a contract)"),
        ("STATE-PROMOTION",    "one player org at most and it is the flagged one; face of company and sponsored camp resolve and fit"),
        ("STALE-DESK",         "pending desk decisions and offers are not left past their deadline"),
        ("GLOBAL-RANK",        "world and pound-for-pound rankings list real, unretired fighters"),
    };

    private readonly ViolationLog _log;
    private readonly string _run;

    // cross-month state
    private bool _first = true;
    private readonly Dictionary<PromotionId, decimal> _openingBalance = new();
    private readonly Dictionary<PromotionId, int> _ledgerCount = new();
    private readonly Dictionary<PromotionId, int> _defunctLedgerAt = new();
    private readonly HashSet<FighterId> _day1Fighters = new();
    private readonly HashSet<EventId> _validatedEvents = new();
    private readonly Dictionary<string, Dictionary<int, object>> _idOwner = new();
    private readonly Dictionary<string, int> _maxSeen = new();
    private Dictionary<string, int> _lastCounters = new();

    /// <summary>Test hook: runs inside the check so a unit test can break the world first. Never set by the modes.</summary>
    public Action<World>? InjectBeforeCheck { get; set; }

    private readonly string _tag;

    /// <summary>Optional: text appended to every violation (the scripted player's recent actions), so a finding names the orders that led to it.</summary>
    public Func<string>? Context { get; set; }

    /// <param name="tag">Prefix for violation text (the loaded twin of a soak says so); the run label stays the same so the twin does not re-report what its original already did.</param>
    public WorldChecker(ViolationLog log, string run, string tag = "")
    {
        _log = log; _run = run; _tag = tag;
        foreach (var (n, d) in Catalog) log.Declare(n, d);
    }

    private DateOnly _date;
    private void V(string inv, string key, string detail) => _log.Report(inv, _run, _date, key, _tag + detail + (Context?.Invoke() is { Length: > 0 } c ? " || recent player actions: " + c : ""));

    private static string F(World w, FighterId id) => w.Get(id) is { } f ? $"{f.FullName} #{id.Value}" : $"<missing fighter #{id.Value}>";
    private static string P(World w, PromotionId id) => w.Get(id) is { } p ? $"{p.Abbreviation} #{id.Value}{(p.Defunct ? " (defunct)" : "")}" : $"<missing promotion #{id.Value}>";
    private static string T(World w, TitleId id) => w.Get(id) is { } t ? $"{t.Name} #{id.Value}" : $"<missing title #{id.Value}>";
    private static string E(World w, EventId id) => w.Get(id) is { } e ? $"{e.Name} #{id.Value} {e.Date:yyyy-MM-dd}" : $"<missing event #{id.Value}>";

    public void Check(World world)
    {
        _date = world.CurrentDate;
        InjectBeforeCheck?.Invoke(world);

        var fightIndex = BuildFightIndex(world);
        Ids(world);
        Rankings(world);
        Titles(world);
        Booking(world);
        Contracts(world);
        Defunct(world);
        Ledgers(world);
        Records(world, fightIndex);
        References(world, fightIndex);
        State(world);
        Stale(world);
        GlobalRanks(world);

        if (_first)
        {
            foreach (var id in world.Fighters.Keys) _day1Fighters.Add(id);
            _first = false;
        }
    }

    // ------------------------------------------------------------------ ids

    private void Ids(World world)
    {
        void Track<TId, TEnt>(string kind, IReadOnlyDictionary<TId, TEnt> map, Func<TId, int> value) where TId : notnull where TEnt : class
        {
            if (!_idOwner.TryGetValue(kind, out var owners)) _idOwner[kind] = owners = new();
            int max = _maxSeen.GetValueOrDefault(kind);
            foreach (var (id, ent) in map.Select(kv => (kv.Key, kv.Value)))
            {
                int v = value(id);
                if (owners.TryGetValue(v, out var prior)) { if (!ReferenceEquals(prior, ent)) V("ID-REUSE", $"{kind}:{v}", $"{kind} id {v} now belongs to a different entity than when first seen ({Name(prior)} -> {Name(ent)})"); }
                else owners[v] = ent;
                if (v > max) max = v;
            }
            _maxSeen[kind] = max;
            int prevId = int.MinValue;
            foreach (var kv in map) { int v = value(kv.Key); if (v <= prevId) { V("ID-REUSE", $"order:{kind}", $"world.{kind} collection enumerates out of id order ({prevId} then {v})"); break; } prevId = v; }
            _log.Checked("ID-REUSE", map.Count);
        }
        Track("fighter", world.Fighters, k => k.Value);
        Track("promotion", world.Promotions, k => k.Value);
        Track("title", world.Titles, k => k.Value);
        Track("event", world.Events, k => k.Value);
        Track("country", world.Countries, k => k.Value);
        Track("city", world.Cities, k => k.Value);
        Track("venue", world.Venues, k => k.Value);
        Track("camp", world.Camps, k => k.Value);

        // fights live inside cards
        if (!_idOwner.TryGetValue("fight", out var fo)) _idOwner["fight"] = fo = new();
        int maxFight = _maxSeen.GetValueOrDefault("fight");
        var inThisPass = new Dictionary<int, Fight>();
        foreach (var ev in world.Events.Values)
            foreach (var fight in ev.Card)
            {
                int v = fight.Id.Value;
                if (inThisPass.TryGetValue(v, out var other) && !ReferenceEquals(other, fight))
                    V("ID-REUSE", $"fight-dup:{v}", $"fight id {v} appears on two different bouts (second on {ev.Name} #{ev.Id.Value})");
                inThisPass[v] = fight;
                if (fo.TryGetValue(v, out var prior)) { if (!ReferenceEquals(prior, fight)) V("ID-REUSE", $"fight:{v}", $"fight id {v} was reissued to a different bout (now on {ev.Name} #{ev.Id.Value} {ev.Date:yyyy-MM-dd})"); }
                else fo[v] = fight;
                maxFight = Math.Max(maxFight, v);
            }
        _maxSeen["fight"] = maxFight;

        // counters never below an id in use, never moving backwards
        var counters = world.IdCounters;
        foreach (var (kind, seen) in _maxSeen)
        {
            if (counters.TryGetValue(kind, out var c) && c < seen)
                V("ID-REUSE", $"counter:{kind}", $"counter '{kind}' is {c} but id {seen} has been seen: the next id handed out will collide or be reused");
            if (_lastCounters.TryGetValue(kind, out var was) && counters.TryGetValue(kind, out var now) && now < was)
                V("ID-REUSE", $"counter-back:{kind}", $"counter '{kind}' moved backwards {was} -> {now}");
        }
        _lastCounters = counters;

        // desk decision ids are plain ints
        var dseen = new HashSet<int>();
        foreach (var d in world.Decisions)
            if (!dseen.Add(d.Id)) V("ID-REUSE", $"decision:{d.Id}", $"two desk decisions share id {d.Id}");
    }

    private static string BoutOf(Event ev, FighterId id) => ev.Card.FirstOrDefault(b => b.RedCorner == id || b.BlueCorner == id)?.Id.Value.ToString() ?? "?";

    private static string Name(object o) => o switch
    {
        Fighter f => $"{f.FullName}", Promotion p => p.Abbreviation, Title t => t.Name, Event e => e.Name,
        Fight f => $"bout {f.Id.Value}", Camp c => c.Name, Venue v => v.Name, City c => c.Name, Country c => c.Name, _ => o.GetType().Name,
    };

    // ------------------------------------------------------------- rankings

    private void Rankings(World world)
    {
        foreach (var promo in world.Promotions.Values)
        {
            foreach (var div in promo.Divisions)
            {
                string dn = $"{promo.Abbreviation} {(div.Sex == Sex.Female ? "W " : "")}{div.WeightClass}";
                FighterId? champ = div.Title is { } tid ? world.Get(tid)?.CurrentChampion : null;
                var seen = new HashSet<FighterId>();
                foreach (var id in div.Rankings)
                {
                    _log.Checked("RANK-CONTRACT", 1);
                    var f = world.Get(id);
                    if (f is null) { V("RANK-CONTRACT", $"{promo.Id.Value}:{id.Value}", $"{dn} rankings list {F(world, id)}"); continue; }
                    if (f.Status == FighterStatus.Retired)
                        V("RANK-CONTRACT", $"{promo.Id.Value}:{id.Value}:ret", $"{dn} rankings list RETIRED {F(world, id)}");
                    if (f.Contract?.Promotion != promo.Id)
                        V("RANK-CONTRACT", $"{promo.Id.Value}:{id.Value}:con", $"{dn} rankings list {F(world, id)} who is {(f.Contract is null ? "a free agent" : "contracted to " + P(world, f.Contract.Promotion))}");

                    _log.Checked("RANK-SHAPE", 1);
                    if (!seen.Add(id)) V("RANK-SHAPE", $"{promo.Id.Value}:{id.Value}:dup", $"{dn} rankings list {F(world, id)} twice");
                    if (!div.Matches(f))
                        V("RANK-SHAPE", $"{promo.Id.Value}:{id.Value}:fit", $"{dn} rankings list {F(world, id)} who is a {(f.Sex == Sex.Female ? "women's " : "")}{f.WeightClass}");
                    if (champ == id)
                        V("RANK-SHAPE", $"{promo.Id.Value}:{id.Value}:champ", $"{dn} rankings list {F(world, id)} who holds the belt");
                }
            }
        }
    }

    // --------------------------------------------------------------- titles

    private void Titles(World world)
    {
        foreach (var t in world.Titles.Values)
        {
            var promo = world.Get(t.Promotion);
            _log.Checked("TITLE-LINK", 1);
            if (promo is null) V("TITLE-LINK", $"t{t.Id.Value}:promo", $"{T(world, t.Id)} belongs to {P(world, t.Promotion)}");
            else
            {
                if (!promo.Titles.Contains(t.Id)) V("TITLE-LINK", $"t{t.Id.Value}:list", $"{T(world, t.Id)} is not in {P(world, promo.Id)}.Titles");
                if (!promo.Divisions.Any(d => d.Title == t.Id)) V("TITLE-LINK", $"t{t.Id.Value}:div", $"{T(world, t.Id)} is held by no division of {P(world, promo.Id)}");
            }

            _log.Checked("CHAMP-CONTRACT", 1);
            if (t.CurrentChampion is { } cid)
            {
                var c = world.Get(cid);
                if (c is null) V("CHAMP-CONTRACT", $"t{t.Id.Value}:{cid.Value}", $"{T(world, t.Id)} champion {F(world, cid)}");
                else
                {
                    if (c.Status == FighterStatus.Retired) V("CHAMP-CONTRACT", $"t{t.Id.Value}:{cid.Value}:ret", $"{T(world, t.Id)} champion {F(world, cid)} is RETIRED");
                    if (c.Contract?.Promotion != t.Promotion)
                        V("CHAMP-CONTRACT", $"t{t.Id.Value}:{cid.Value}:con", $"{T(world, t.Id)} ({P(world, t.Promotion)}) champion {F(world, cid)} is {(c.Contract is null ? "a free agent" : "contracted to " + P(world, c.Contract.Promotion))}");
                }
            }

            _log.Checked("LINEAGE", 1);
            var open = t.Lineage.Where(r => r.LostOn is null).ToList();
            if (open.Count > 1) V("LINEAGE", $"t{t.Id.Value}:open", $"{T(world, t.Id)} has {open.Count} open reigns ({string.Join(", ", open.Select(r => F(world, r.Champion)))})");
            if (t.CurrentChampion is { } cc)
            {
                if (open.Count == 0) V("LINEAGE", $"t{t.Id.Value}:noopen", $"{T(world, t.Id)} champion {F(world, cc)} has no open reign in the lineage");
                else if (open[^1].Champion != cc) V("LINEAGE", $"t{t.Id.Value}:mismatch", $"{T(world, t.Id)} champion is {F(world, cc)} but the open reign is {F(world, open[^1].Champion)}");
            }
            else if (open.Count > 0)
                V("LINEAGE", $"t{t.Id.Value}:vacant-open", $"{T(world, t.Id)} is VACANT but {F(world, open[^1].Champion)}'s reign since {open[^1].WonOn:yyyy-MM-dd} is still open");

            // overlap: each reign must start no earlier than the previous one ended
            for (int i = 1; i < t.Lineage.Count; i++)
            {
                var prev = t.Lineage[i - 1]; var cur = t.Lineage[i];
                if (prev.LostOn is null || cur.WonOn < prev.LostOn.Value)
                    V("LINEAGE", $"t{t.Id.Value}:overlap{i}", $"{T(world, t.Id)} reign {i} ({F(world, cur.Champion)} from {cur.WonOn:yyyy-MM-dd}) overlaps reign {i - 1} ({F(world, prev.Champion)} {prev.WonOn:yyyy-MM-dd} to {(prev.LostOn is { } l ? l.ToString("yyyy-MM-dd") : "open")})");
            }
            foreach (var r in t.Lineage)
                if (r.LostOn is { } lo && lo < r.WonOn)
                    V("LINEAGE", $"t{t.Id.Value}:neg:{r.WonOn.DayNumber}", $"{T(world, t.Id)} reign of {F(world, r.Champion)} ends {lo:yyyy-MM-dd} before it begins {r.WonOn:yyyy-MM-dd}");
            if (t.Lineage.Count > 0 && t.Lineage[^1].WonOn > world.CurrentDate)
                V("LINEAGE", $"t{t.Id.Value}:future", $"{T(world, t.Id)} has a reign dated in the future ({t.Lineage[^1].WonOn:yyyy-MM-dd})");
        }
        foreach (var promo in world.Promotions.Values)
        {
            foreach (var tid in promo.Titles)
                if (world.Get(tid) is not { } tt) V("TITLE-LINK", $"p{promo.Id.Value}:t{tid.Value}", $"{P(world, promo.Id)}.Titles lists missing title #{tid.Value}");
                else if (tt.Promotion != promo.Id) V("TITLE-LINK", $"p{promo.Id.Value}:t{tid.Value}:own", $"{P(world, promo.Id)}.Titles lists {T(world, tid)} which belongs to {P(world, tt.Promotion)}");
            foreach (var d in promo.Divisions)
                if (d.Title is { } dt && world.Get(dt) is null) V("TITLE-LINK", $"p{promo.Id.Value}:dt{dt.Value}", $"{P(world, promo.Id)} {d.WeightClass} division belt #{dt.Value} does not exist");
        }
    }

    // -------------------------------------------------------------- booking

    private void Booking(World world)
    {
        // every card not yet validated, plus every scheduled one (cards change while scheduled)
        var todo = world.Events.Values.Where(e => e.Status == EventStatus.Scheduled || !_validatedEvents.Contains(e.Id)).ToList();

        // date -> fighter -> first card seen (non-cancelled cards)
        var byDate = new Dictionary<(int Day, FighterId F), Event>();
        foreach (var ev in world.Events.Values.Where(e => e.Status != EventStatus.Cancelled))
            foreach (var fight in ev.Card)
                foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                {
                    var k = (ev.Date.DayNumber, id);
                    if (!byDate.TryGetValue(k, out var first)) byDate[k] = ev;
                    else if (first.Id != ev.Id && (!_validatedEvents.Contains(ev.Id) || ev.Status == EventStatus.Scheduled))
                        V("BOOK-DOUBLE-DATE", $"{ev.Date.DayNumber}:{id.Value}:{Math.Min(first.Id.Value, ev.Id.Value)}-{Math.Max(first.Id.Value, ev.Id.Value)}",
                          $"{F(world, id)} is on {E(world, first.Id)} ({P(world, first.Promotion)}, {first.Status}, bout {BoutOf(first, id)}) and {E(world, ev.Id)} ({P(world, ev.Promotion)}, {ev.Status}, bout {BoutOf(ev, id)}) on the same date{(ev.Date < world.StartDate ? " [pre-history]" : "")}");
                }

        foreach (var ev in world.Events.Values)
        {
            if (ev.Status == EventStatus.Completed && ev.Date > world.CurrentDate && !_validatedEvents.Contains(ev.Id))
                V("EVENT-FUTURE", $"future:{ev.Id.Value}", $"{E(world, ev.Id)} ({(ev.Promotion.Value == 0 ? "no promotion" : P(world, ev.Promotion))}) is Completed with {ev.Card.Count} results on {world.CurrentDate:yyyy-MM-dd}, {ev.Date.DayNumber - world.CurrentDate.DayNumber} days before its own date");
        }

        foreach (var ev in todo)
        {
            _log.Checked("BOOK-DOUBLE-DATE", ev.Card.Count);
            var onCard = new HashSet<FighterId>();
            foreach (var fight in ev.Card)
            {
                if (fight.RedCorner == fight.BlueCorner)
                    V("BOOK-DOUBLE-DATE", $"self:{ev.Id.Value}:{fight.Id.Value}", $"{F(world, fight.RedCorner)} fights himself on {E(world, ev.Id)}");
                foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                    if (!onCard.Add(id) && fight.RedCorner != fight.BlueCorner)
                        V("BOOK-DOUBLE-DATE", $"twice:{ev.Id.Value}:{id.Value}", $"{F(world, id)} is on {E(world, ev.Id)} twice");
            }
            if (ev.Status != EventStatus.Scheduled) _validatedEvents.Add(ev.Id);
        }

        // names: PlayerBooking numbers a new card by counting the org's cards, so cancelling any but the newest reissues a live name
        foreach (var g in world.Events.Values.Where(e => e.Promotion.Value != 0).GroupBy(e => (e.Promotion, e.Name)).Where(g => g.Count() > 1))
        {
            _log.Checked("EVENT-NAME", 1);
            var evs = g.OrderBy(e => e.Id.Value).ToList();
            V("EVENT-NAME", $"{g.Key.Promotion.Value}:{g.Key.Name}", $"{P(world, g.Key.Promotion)} has {evs.Count} events all named \"{g.Key.Name}\": {string.Join(", ", evs.Select(e => $"#{e.Id.Value} {e.Date:yyyy-MM-dd} {e.Status}"))}");
        }

        // scheduled cards only
        var onScheduled = new Dictionary<FighterId, Event>();
        foreach (var ev in world.Events.Values.Where(e => e.Status == EventStatus.Scheduled).OrderBy(e => e.Date).ThenBy(e => e.Id.Value))
        {
            _log.Checked("BOOK-SCHEDULED", ev.Card.Count);
            var host = world.Get(ev.Promotion);
            // a local-circuit club card (no promotion) is staged in the monthly pass and runs on its date: its pros are free
            // agents or moonlighters on non-exclusive deals, so only the who-is-on-it rules apply to it
            bool local = ev.Promotion.Value == 0;
            if (host is null && !local) V("BOOK-SCHEDULED", $"e{ev.Id.Value}:host", $"{E(world, ev.Id)} is run by {P(world, ev.Promotion)}");
            if (ev.Date < world.CurrentDate.AddDays(-35))
                V("BOOK-SCHEDULED", $"e{ev.Id.Value}:overdue", $"{E(world, ev.Id)} ({P(world, ev.Promotion)}) is still Scheduled {(world.CurrentDate.DayNumber - ev.Date.DayNumber)} days after its date");
            foreach (var fight in ev.Card)
            {
                foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                {
                    var f = world.Get(id);
                    if (f is null) V("BOOK-SCHEDULED", $"e{ev.Id.Value}:{id.Value}:gone", $"{E(world, ev.Id)} books {F(world, id)}");
                    else if (f.Status == FighterStatus.Retired) V("BOOK-SCHEDULED", $"e{ev.Id.Value}:{id.Value}:ret", $"{E(world, ev.Id)} books RETIRED {F(world, id)}");
                    if (onScheduled.TryGetValue(id, out var other) && other.Id != ev.Id)
                        V("BOOK-SCHEDULED", $"{id.Value}:{other.Id.Value}-{ev.Id.Value}", $"{F(world, id)} is on two scheduled cards: {E(world, other.Id)} and {E(world, ev.Id)}");
                    else onScheduled[id] = ev;
                    if (local && f is not null && !f.LocalTalent && f.Contract is { IsExclusive: true })
                        V("BOOK-SCHEDULED", $"e{ev.Id.Value}:{id.Value}:excl", $"local card {E(world, ev.Id)} books {F(world, id)} who is on an EXCLUSIVE deal with {P(world, f.Contract.Promotion)}");
                    else if (!local && f is not null && !f.LocalTalent && f.Status != FighterStatus.Retired && f.Contract?.Promotion != ev.Promotion)
                        V("BOOK-SCHEDULED", $"e{ev.Id.Value}:{id.Value}:unrostered", $"{E(world, ev.Id)} ({P(world, ev.Promotion)}) books {F(world, id)} who is {(f.Contract is null ? "a free agent" : "contracted to " + P(world, f.Contract.Promotion))}{(fight.IsTitleFight ? " in a TITLE fight" : "")}");
                    if (fight.IsTitleFight && f is not null && f.Contract?.Promotion != ev.Promotion && f.Status != FighterStatus.Retired)
                        V("BOOK-SCHEDULED", $"e{ev.Id.Value}:{id.Value}:title", $"title fight on {E(world, ev.Id)} ({P(world, ev.Promotion)}) includes {F(world, id)} who is {(f.Contract is null ? "a free agent" : "contracted to " + P(world, f.Contract.Promotion))}");
                }
                if (fight.IsTitleFight)
                {
                    if (fight.Title is not { } tid || world.Get(tid) is not { } title)
                        V("BOOK-SCHEDULED", $"e{ev.Id.Value}:f{fight.Id.Value}:notitle", $"title fight on {E(world, ev.Id)} points at a missing belt");
                    else if (title.Promotion != ev.Promotion)
                        V("BOOK-SCHEDULED", $"e{ev.Id.Value}:f{fight.Id.Value}:belt", $"{E(world, ev.Id)} ({P(world, ev.Promotion)}) stages a fight for {T(world, title.Id)} which belongs to {P(world, title.Promotion)}");
                }
            }
        }
    }

    // ------------------------------------------------------------ contracts

    private void Contracts(World world)
    {
        foreach (var f in world.Fighters.Values)
        {
            if (f.Contract is not { } c) continue;
            _log.Checked("CONTRACT-LINK", 1);
            var p = world.Get(c.Promotion);
            if (p is null) V("CONTRACT-LINK", $"{f.Id.Value}:missing", $"{F(world, f.Id)} is contracted to missing promotion #{c.Promotion.Value}");
            else if (p.Defunct) V("CONTRACT-LINK", $"{f.Id.Value}:defunct:{p.Id.Value}", $"{F(world, f.Id)} is contracted to DEFUNCT {P(world, p.Id)}");
            if (f.Status == FighterStatus.Retired) V("CONTRACT-LINK", $"{f.Id.Value}:retired", $"RETIRED {F(world, f.Id)} still holds a contract with {P(world, c.Promotion)}");
            if (c.FightsRemaining < 0 || c.PursePerFight.Amount < 0 || c.WinBonus.Amount < 0 || c.SigningBonus.Amount < 0)
                V("CONTRACT-LINK", $"{f.Id.Value}:neg", $"{F(world, f.Id)} contract has a negative term (fights {c.FightsRemaining}, purse {c.PursePerFight.Amount}, win {c.WinBonus.Amount})");
        }
    }

    private void Defunct(World world)
    {
        foreach (var p in world.Promotions.Values)
        {
            if (!p.Defunct) continue;
            _log.Checked("DEFUNCT", 1);
            foreach (var ev in world.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Scheduled))
                V("DEFUNCT", $"{p.Id.Value}:ev{ev.Id.Value}", $"defunct {P(world, p.Id)} still has scheduled {E(world, ev.Id)}");
            foreach (var t in world.Titles.Values.Where(t => t.Promotion == p.Id && t.CurrentChampion is not null))
                V("DEFUNCT", $"{p.Id.Value}:t{t.Id.Value}", $"defunct {P(world, p.Id)} still has champion {F(world, t.CurrentChampion!.Value)} on {T(world, t.Id)}");
            foreach (var d in p.Divisions.Where(d => d.Rankings.Count > 0))
                V("DEFUNCT", $"{p.Id.Value}:rank:{d.WeightClass}:{d.Sex}", $"defunct {P(world, p.Id)} {d.WeightClass} division still ranks {d.Rankings.Count} fighters");
            if (!_defunctLedgerAt.TryGetValue(p.Id, out var at)) _defunctLedgerAt[p.Id] = p.Ledger.Count;
            else if (p.Ledger.Count > at)
                V("DEFUNCT", $"{p.Id.Value}:ledger", $"defunct {P(world, p.Id)} ledger grew by {p.Ledger.Count - at} entries after it folded (latest: {p.Ledger[^1].Category} {p.Ledger[^1].Amount.Amount:N0} \"{p.Ledger[^1].Description}\")");
            if (p.IsPlayerControlled && world.PlayerPromotion == p.Id) { /* bankruptcy of the player is game over, not a state bug */ }
        }
    }

    // --------------------------------------------------------------- ledger

    private static readonly HashSet<FinanceCategory> IncomeOnly = new()
    {
        FinanceCategory.EventGate, FinanceCategory.EventPpv, FinanceCategory.Sponsorship,
        FinanceCategory.Broadcast, FinanceCategory.Streaming, FinanceCategory.Merchandise,
    };
    private static readonly HashSet<FinanceCategory> ExpenseOnly = new()
    {
        FinanceCategory.FighterPurse, FinanceCategory.SigningBonus, FinanceCategory.ContractBuyout, FinanceCategory.Production,
        FinanceCategory.Overhead, FinanceCategory.RetentionBonus, FinanceCategory.FighterMarketing, FinanceCategory.OrgMarketing,
        FinanceCategory.CampFees, FinanceCategory.StreamingOps, FinanceCategory.Fines,
    };

    /// <summary>
    /// The identity is balance = opening + sum(ledger). The opening balance is not stored anywhere, so it is taken the
    /// first time a promotion is seen (balance - sum) and every later month must hold it. A promotion born mid-run is
    /// baselined at birth. The Ledger is a complete record of money moved through Ledger.Record; the one writer of
    /// Balance in the sim is Ledger.Record, so a drift means a direct write (or a ledger edit) somewhere new.
    /// </summary>
    private void Ledgers(World world)
    {
        foreach (var p in world.Promotions.Values)
        {
            _log.Checked("LEDGER", 1);
            decimal sum = 0m;
            foreach (var e in p.Ledger) sum += e.Amount.Amount;
            if (!_openingBalance.TryGetValue(p.Id, out var open))
            {
                _openingBalance[p.Id] = p.Balance.Amount - sum;
                _ledgerCount[p.Id] = p.Ledger.Count;
                continue;
            }
            decimal drift = p.Balance.Amount - (open + sum);
            if (drift != 0m)
                V("LEDGER", $"{p.Id.Value}:drift", $"{P(world, p.Id)} balance {p.Balance.Amount:N2} != opening {open:N2} + ledger {sum:N2} (off by {drift:N2}; {p.Ledger.Count} entries)");
            if (p.Ledger.Count < _ledgerCount[p.Id])
                V("LEDGER", $"{p.Id.Value}:shrink", $"{P(world, p.Id)} ledger shrank from {_ledgerCount[p.Id]} to {p.Ledger.Count} entries");
            // sign sanity only on entries added since last look
            for (int i = _ledgerCount[p.Id]; i < p.Ledger.Count; i++)
            {
                var e = p.Ledger[i];
                if ((IncomeOnly.Contains(e.Category) && e.Amount.Amount < 0) || (ExpenseOnly.Contains(e.Category) && e.Amount.Amount > 0))
                    V("LEDGER", $"{p.Id.Value}:sign:{e.Category}", $"{P(world, p.Id)} {e.Category} entry has the wrong sign: {e.Amount.Amount:N0} \"{e.Description}\" on {e.Date:yyyy-MM-dd}");
                if (e.Date > world.CurrentDate)
                    V("LEDGER", $"{p.Id.Value}:future:{i}", $"{P(world, p.Id)} ledger entry dated in the future: {e.Date:yyyy-MM-dd} \"{e.Description}\"");
            }
            _ledgerCount[p.Id] = p.Ledger.Count;
        }
    }

    // -------------------------------------------------------------- records

    private sealed class Tally { public int W, L, D, NC, Ko, Sub, Dec; public int Bouts; }

    private static Dictionary<FightId, (Event Ev, Fight Fight)> BuildFightIndex(World world)
    {
        var map = new Dictionary<FightId, (Event, Fight)>();
        foreach (var ev in world.Events.Values)
            foreach (var f in ev.Card) map[f.Id] = (ev, f);
        return map;
    }

    private void Records(World world, Dictionary<FightId, (Event Ev, Fight Fight)> index)
    {
        var tally = new Dictionary<FighterId, Tally>();
        Tally Of(FighterId id) { if (!tally.TryGetValue(id, out var t)) tally[id] = t = new Tally(); return t; }
        var completedFor = new Dictionary<FighterId, HashSet<FightId>>();
        HashSet<FightId> Fights(FighterId id) { if (!completedFor.TryGetValue(id, out var s)) completedFor[id] = s = new(); return s; }

        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Completed) continue;
            foreach (var fight in ev.Card)
            {
                if (fight.Outcome is not { } o) continue;
                foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                {
                    var t = Of(id); t.Bouts++; Fights(id).Add(fight.Id);
                    if (o.Winner is null) { if (o.Method == FightMethod.NoContest) t.NC++; else t.D++; }
                    else if (fight.OverturnedNc) t.NC++;
                    else if (o.Winner == id)
                    {
                        t.W++;
                        switch (o.Method)
                        {
                            case FightMethod.Knockout or FightMethod.TechnicalKnockout: t.Ko++; break;
                            case FightMethod.SubmissionTap or FightMethod.TechnicalSubmission: t.Sub++; break;
                            case FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision: t.Dec++; break;
                        }
                    }
                    else t.L++;
                }
            }
        }

        foreach (var f in world.Fighters.Values)
        {
            _log.Checked("RECORD-HISTORY", 1);
            var r = f.Record;
            var t = tally.GetValueOrDefault(f.Id) ?? new Tally();
            bool minted = !_day1Fighters.Contains(f.Id) && !_first;
            string who = F(world, f.Id) + (minted ? " (minted after day one)" : "");

            if (r.Wins < t.W || r.Losses < t.L || r.Draws < t.D || r.NoContests < t.NC)
                V("RECORD-HISTORY", $"{f.Id.Value}:below", $"{who} record {r.Wins}-{r.Losses}-{r.Draws} ({r.NoContests} NC) is SMALLER than his completed bouts ({t.W}-{t.L}-{t.D}, {t.NC} NC)");
            else if (minted && !f.Designed && !f.LocalTalent && f.DiscoveredBy is null && (r.Wins != t.W || r.Losses != t.L || r.Draws != t.D || r.NoContests != t.NC))
                V("RECORD-HISTORY", $"{f.Id.Value}:exact", $"{who} record {r.Wins}-{r.Losses}-{r.Draws} ({r.NoContests} NC) != tally of his completed bouts {t.W}-{t.L}-{t.D} ({t.NC} NC); {t.Bouts} bouts in events, {f.BoutHistory.Count} in BoutHistory");

            _log.Checked("RECORD-SHAPE", 1);
            if (r.WinsByKo + r.WinsBySubmission + r.WinsByDecision > r.Wins)
                V("RECORD-SHAPE", $"{f.Id.Value}:methods", $"{who} has {r.WinsByKo} KO + {r.WinsBySubmission} sub + {r.WinsByDecision} dec wins but only {r.Wins} wins");
            if (r.Wins < 0 || r.Losses < 0 || r.Draws < 0 || r.NoContests < 0)
                V("RECORD-SHAPE", $"{f.Id.Value}:neg", $"{who} has a negative record component ({r.Wins}-{r.Losses}-{r.Draws}, {r.NoContests} NC)");

            // streak from the bout sequence (BoutHistory order) — the tail of a padded record is still the real tail
            int streak = 0;
            foreach (var fid in f.BoutHistory)
            {
                if (!index.TryGetValue(fid, out var x) || x.Fight.Outcome is not { } o) continue;
                if (o.Winner is null || x.Fight.OverturnedNc) streak = 0;
                else if (o.Winner == f.Id) streak = streak >= 0 ? streak + 1 : 1;
                else streak = streak <= 0 ? streak - 1 : -1;
            }
            if (f.BoutHistory.Count > 0 && streak != r.CurrentStreak)
                V("RECORD-SHAPE", $"{f.Id.Value}:streak", $"{who} streak {r.CurrentStreak} but his last {f.BoutHistory.Count} bouts say {streak}");

            // chronology: BoutHistory is appended as results land, so it must be date-ordered
            _log.Checked("BOUT-ORDER", 1);
            DateOnly prev = DateOnly.MinValue; FightId prevFight = default;
            foreach (var fid in f.BoutHistory)
            {
                if (!index.TryGetValue(fid, out var y)) continue;
                if (y.Ev.Date < prev)
                {
                    V("BOUT-ORDER", $"{f.Id.Value}:order:{fid.Value}", $"{who} bout {fid.Value} ({E(world, y.Ev.Id)}) is listed AFTER bout {prevFight.Value} which is dated {prev:yyyy-MM-dd}");
                    break;
                }
                prev = y.Ev.Date; prevFight = fid;
            }

            // BoutHistory vs events
            _log.Checked("BOUT-HISTORY", 1);
            var mine = Fights(f.Id);
            var seen = new HashSet<FightId>();
            foreach (var fid in f.BoutHistory)
            {
                if (!seen.Add(fid)) { V("BOUT-HISTORY", $"{f.Id.Value}:dup:{fid.Value}", $"{who} lists bout {fid.Value} twice"); continue; }
                if (!index.TryGetValue(fid, out var x)) { V("BOUT-HISTORY", $"{f.Id.Value}:gone:{fid.Value}", $"{who} lists bout {fid.Value} which is on no card"); continue; }
                if (x.Fight.RedCorner != f.Id && x.Fight.BlueCorner != f.Id)
                    V("BOUT-HISTORY", $"{f.Id.Value}:notme:{fid.Value}", $"{who} lists bout {fid.Value} ({E(world, x.Ev.Id)}) he was not in");
                else if (x.Ev.Status != EventStatus.Completed || x.Fight.Outcome is null)
                    V("BOUT-HISTORY", $"{f.Id.Value}:unfought:{fid.Value}", $"{who} lists bout {fid.Value} on {E(world, x.Ev.Id)} which is {x.Ev.Status}{(x.Fight.Outcome is null ? " with no result" : "")}");
            }
            foreach (var fid in mine)
                if (!seen.Contains(fid))
                    V("BOUT-HISTORY", $"{f.Id.Value}:missing:{fid.Value}", $"{who} fought bout {fid.Value} ({E(world, index[fid].Ev.Id)}) but it is not in his BoutHistory");
        }
    }

    // ----------------------------------------------------------- references

    private void References(World world, Dictionary<FightId, (Event Ev, Fight Fight)> index)
    {
        int n = 0;
        void Fi(FighterId? id, string what) { n++; if (id is { } x && x.Value != 0 && world.Get(x) is null) V("REF-DANGLING", $"{what}:f{x.Value}", $"{what} -> {F(world, x)}"); }
        void Pr(PromotionId? id, string what) { n++; if (id is { } x && x.Value != 0 && world.Get(x) is null) V("REF-DANGLING", $"{what}:p{x.Value}", $"{what} -> {P(world, x)}"); }
        void Ev(EventId? id, string what) { n++; if (id is { } x && x.Value != 0 && world.Get(x) is null) V("REF-DANGLING", $"{what}:e{x.Value}", $"{what} -> missing event #{x.Value}"); }
        void Ci(CityId? id, string what) { n++; if (id is { } x && x.Value != 0 && world.Get(x) is null) V("REF-DANGLING", $"{what}:c{x.Value}", $"{what} -> missing city #{x.Value}"); }
        void Ca(CampId? id, string what) { n++; if (id is { } x && x.Value != 0 && world.Get(x) is null) V("REF-DANGLING", $"{what}:camp{x.Value}", $"{what} -> missing camp #{x.Value}"); }

        foreach (var f in world.Fighters.Values)
        {
            string w = $"fighter {f.FullName} #{f.Id.Value}";
            if (f.Contract is { } c) Pr(c.Promotion, $"{w} contract");
            Pr(f.DiscoveredBy, $"{w} discovered-by");
            Ci(f.HomeCity, $"{w} home city"); Ca(f.Camp, $"{w} camp");
            if (f.Country.Value != 0 && world.Get(f.Country) is null) V("REF-DANGLING", $"{w}:country", $"{w} country -> missing #{f.Country.Value}");
            foreach (var city in f.CityReputation.Keys) Ci(city, $"{w} city reputation");
        }
        foreach (var p in world.Promotions.Values)
        {
            string w = $"promotion {p.Abbreviation} #{p.Id.Value}";
            Ci(p.HomeCity, $"{w} HQ"); Ca(p.SponsoredCamp, $"{w} sponsored camp"); Fi(p.FaceOfCompany, $"{w} face of company");
            foreach (var city in p.PopularityByCity.Keys) Ci(city, $"{w} popularity");
            foreach (var d in p.Divisions) foreach (var id in d.Rankings) Fi(id, $"{w} {d.WeightClass} rankings");
        }
        foreach (var t in world.Titles.Values)
        {
            Pr(t.Promotion, $"title {t.Name} #{t.Id.Value}"); Fi(t.CurrentChampion, $"title {t.Name} #{t.Id.Value} champion");
            foreach (var r in t.Lineage) Fi(r.Champion, $"title {t.Name} #{t.Id.Value} lineage");
        }
        foreach (var ev in world.Events.Values)
        {
            string w = $"event {ev.Name} #{ev.Id.Value} {ev.Date:yyyy-MM-dd}";
            Pr(ev.Promotion, w);
            n++;
            if (ev.VenueId is { } v && world.Get(v) is null) V("REF-DANGLING", $"{w}:venue", $"{w} venue -> missing #{v.Value}");
            foreach (var f in ev.Card)
            {
                Fi(f.RedCorner, $"{w} bout {f.Id.Value} red"); Fi(f.BlueCorner, $"{w} bout {f.Id.Value} blue"); Fi(f.WeightMissBy, $"{w} bout {f.Id.Value} weight miss");
                n++;
                if (f.Title is { } tid && world.Get(tid) is null) V("REF-DANGLING", $"{w}:t{tid.Value}", $"{w} bout {f.Id.Value} title -> missing #{tid.Value}");
                if (f.Outcome is { } o) { Fi(o.Winner, $"{w} bout {f.Id.Value} winner"); Fi(o.Loser, $"{w} bout {f.Id.Value} loser"); }
            }
        }
        foreach (var c in world.Camps.Values) { Fi(c.HeadCoach, $"camp {c.Name} head coach"); Ci(c.City, $"camp {c.Name} city"); }
        foreach (var r in world.Relationships) { Fi(r.A, $"{r.Kind} bond"); Fi(r.B, $"{r.Kind} bond"); }
        foreach (var r in world.Rivalries) { Fi(r.A, "rivalry"); Fi(r.B, "rivalry"); }
        foreach (var id in world.Shortlist) Fi(id, "shortlist");
        foreach (var d in world.Decisions) { Fi(d.Fighter, $"desk {d.Kind}"); Pr(d.Promotion, $"desk {d.Kind}"); }
        foreach (var b in world.BiddingWars) { Fi(b.Fighter, "bidding war"); foreach (var s in b.Suitors) Pr(s, "bidding war suitor"); }
        foreach (var m in world.Mail) { Fi(m.AboutFighter, $"mail \"{m.Subject}\" {m.Date:yyyy-MM-dd}"); Ev(m.AboutEvent, $"mail \"{m.Subject}\" {m.Date:yyyy-MM-dd}"); }
        foreach (var nw in world.News) { Fi(nw.AboutFighter, $"news \"{nw.Headline}\" {nw.Date:yyyy-MM-dd}"); Ev(nw.AboutEvent, $"news \"{nw.Headline}\" {nw.Date:yyyy-MM-dd}"); Pr(nw.Promotion, $"news \"{nw.Headline}\" {nw.Date:yyyy-MM-dd}"); }
        foreach (var l in world.GlobalRankings.Values) foreach (var id in l) Fi(id, "global rankings");
        foreach (var id in world.PoundForPound) Fi(id, "pound-for-pound");
        _log.Checked("REF-DANGLING", n);
    }

    // ----------------------------------------------------------- state, desk

    private void State(World world)
    {
        foreach (var f in world.Fighters.Values)
        {
            _log.Checked("STATE-FIGHTER", 1); _log.Checked("STATE-WOMEN-CLASS", 1); _log.Checked("STATE-STIPEND", 1);
            string w = F(world, f.Id);
            int age = f.AgeOn(world.CurrentDate);
            if (f.Status == FighterStatus.Injured && f.CurrentInjury is null) V("STATE-FIGHTER", $"{f.Id.Value}:inj", $"{w} is Injured with no injury on file");
            if (f.Status == FighterStatus.Suspended && f.SuspendedUntil is null) V("STATE-FIGHTER", $"{f.Id.Value}:susp", $"{w} is Suspended with no end date");
            if (f.Status == FighterStatus.Active && (f.CurrentInjury is not null)) V("STATE-FIGHTER", $"{f.Id.Value}:activeinj", $"{w} is Active but carries an injury ({f.CurrentInjury!.Description}, back {f.CurrentInjury.ExpectedReturn:yyyy-MM-dd})");
            if (f.Status != FighterStatus.Retired && age > 44) V("STATE-FIGHTER", $"{f.Id.Value}:age", $"{w} is {age} and still not retired ({f.Status})");
            if (f.Status != FighterStatus.Retired && age < 16) V("STATE-FIGHTER", $"{f.Id.Value}:young", $"{w} is {age}");
            if (f.Loyalty is < 0 or > 100) V("STATE-FIGHTER", $"{f.Id.Value}:loyalty", $"{w} loyalty {f.Loyalty}");
            if (f.Momentum is < -100 or > 100) V("STATE-FIGHTER", $"{f.Id.Value}:momentum", $"{w} momentum {f.Momentum}");
            if (f.WeightClass == default && false) { }
            if (f.Sex == Sex.Female && !WomensDivisions.Allows(f.WeightClass) && f.Status != FighterStatus.Retired)
                V("STATE-WOMEN-CLASS", $"{f.Id.Value}:wsex", $"{w} is a woman in {f.WeightClass}, which no women's division runs");
            if (f.Camp is { } camp && f.CampStipend && f.Contract is null && f.Status != FighterStatus.Retired)
                V("STATE-STIPEND", $"{f.Id.Value}:stipend", $"{w} is on a camp stipend with no org to pay it (camp #{camp.Value})");
        }

        _log.Checked("STATE-PROMOTION", world.Promotions.Count);
        var flagged = world.Promotions.Values.Where(p => p.IsPlayerControlled).ToList();
        if (flagged.Count > 1) V("STATE-PROMOTION", "multi-player", $"{flagged.Count} promotions are flagged player-controlled ({string.Join(", ", flagged.Select(p => p.Abbreviation))})");
        if (world.PlayerPromotion.Value != 0 && world.Get(world.PlayerPromotion) is { } pl && !pl.IsPlayerControlled)
            V("STATE-PROMOTION", "flag", $"world.PlayerPromotion is {pl.Abbreviation} but it is not flagged player-controlled");
        foreach (var p in world.Promotions.Values)
        {
            if (p.Defunct) continue;
            if (p.FaceOfCompany is { } face && world.Get(face) is { } ff && ff.Contract?.Promotion != p.Id)
                V("STATE-PROMOTION", $"{p.Id.Value}:face", $"{P(world, p.Id)} face of the company {F(world, face)} is {(ff.Contract is null ? (ff.Status == FighterStatus.Retired ? "retired" : "a free agent") : "contracted to " + P(world, ff.Contract.Promotion))}");
            if (p.PrestigeHundredths is < 0 or > 10_000) V("STATE-PROMOTION", $"{p.Id.Value}:prestige", $"{P(world, p.Id)} prestige accumulator {p.PrestigeHundredths}");
            if (Math.Abs(p.Balance.Amount) > 1e11m) V("STATE-PROMOTION", $"{p.Id.Value}:balance", $"{P(world, p.Id)} balance {p.Balance.Amount:N0} is out of any sane range");
        }
        // (A bidding war over a fighter who retired or signed is cleaned by BiddingWars.RunWeekly the following week;
        //  the monthly retirement/signing pass runs after it, so a one-week lag at the boundary is by design and not asserted.)
    }

    private void Stale(World world)
    {
        _log.Checked("STALE-DESK", world.Decisions.Count);
        foreach (var d in world.Decisions)
            if (d.State == DecisionState.Pending && d.Deadline < world.CurrentDate.AddDays(-35))
                V("STALE-DESK", $"d{d.Id}", $"desk decision #{d.Id} {d.Kind} (created {d.Created:yyyy-MM-dd}, deadline {d.Deadline:yyyy-MM-dd}) is still pending {world.CurrentDate.DayNumber - d.Deadline.DayNumber} days late: {d.Detail}");
        foreach (var p in world.Promotions.Values)
        {
            _log.Checked("STALE-DESK", p.Offers.Count);
            foreach (var o in p.Offers)
                if (o.ExpiresOn < world.CurrentDate.AddDays(-35))
                    V("STALE-DESK", $"{p.Id.Value}:{o.Partner}:{o.OfferedOn.DayNumber}", $"{P(world, p.Id)} still holds {(o.Media is null ? "sponsor" : o.Media + " media")} offer from {o.Partner} that expired {o.ExpiresOn:yyyy-MM-dd}");
        }
    }

    private void GlobalRanks(World world)
    {
        void L(string what, IEnumerable<FighterId> ids)
        {
            foreach (var id in ids)
            {
                _log.Checked("GLOBAL-RANK", 1);
                if (world.Get(id) is not { } f) V("GLOBAL-RANK", $"{what}:{id.Value}", $"{what} lists missing fighter #{id.Value}");
                else if (f.Status == FighterStatus.Retired) V("GLOBAL-RANK", $"{what}:{id.Value}:ret", $"{what} lists RETIRED {F(world, id)}");
            }
        }
        foreach (var (wc, ids) in world.GlobalRankings) L($"global {wc}", ids);
        L("pound-for-pound", world.PoundForPound);
    }
}
