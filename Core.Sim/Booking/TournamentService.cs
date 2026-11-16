using MmaManager.Core.Model;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Core.Sim.Booking;

/// <summary>What a one-night tournament produced: the card, the champion (null when the final was drawn
/// even after overtime, or nobody was left to fight it) and how every slot of the bracket was filled.</summary>
public sealed record OneNightResult(Event Event, Fighter? Champion, BracketNight Night);

/// <summary>
/// Who a bracket night would draw today (<see cref="TournamentService.PreviewField"/>): the seeds in order
/// (seed 1 first), the alternate, the men who are in because the house promised them a place, and
/// whether the bracket fills. The one rule the night itself, the AI's announcement, the player's booking
/// and the screen all read.
/// </summary>
public sealed record FieldPreview(IReadOnlyList<Fighter> Seeds, Fighter? Alternate, IReadOnlyList<FighterId> Promised, int Size)
{
    public bool Full => Seeds.Count >= Size;
}

/// <summary>
/// A one-night tournament: a single-elimination bracket, decided on one super-card. The whole thing
/// runs at once — winners advance round by round on the same night — then settles like any other
/// event and crowns a Grand Prix champion (prize money out of the promotion's pocket, and a headline).
/// The bracket's own rules (seeding, carried condition, withdrawals, the alternate, draws and the
/// final's overtime) live in <see cref="OneNightBracket"/>, shared with the lab.
/// </summary>
public sealed class TournamentService
{
    private readonly FightEngine _engine;
    private readonly EventService _events;

    public TournamentService(FightEngine? engine = null)
    {
        _engine = engine ?? new FightEngine();
        _events = new EventService(_engine);
    }

    /// <summary>
    /// Run a <paramref name="size"/>-fighter (power of two) one-night tournament in a weight class,
    /// under the promotion's own rules. Returns the card, or null if it can't be filled. (Nothing in
    /// the game calls this yet; it now runs through <see cref="OneNightBracket"/> like the overload below,
    /// so its seeding, round length and carried condition changed with the Origins work.)
    /// </summary>
    public Event? RunOneNight(World world, PromotionId promoId, WeightClass wc, int size, DateOnly date, Money prize) =>
        RunOneNight(world, promoId, size, date, prize, wc, rules: null)?.Event;

    /// <summary>
    /// Run a <paramref name="size"/>-fighter (power of two) one-night tournament. Returns the result,
    /// or null if the field can't be filled.
    /// <list type="bullet">
    /// <item><paramref name="wc"/> null = open weight: any of the promotion's match-ready men.</item>
    /// <item><paramref name="rules"/> null = the promotion's house rules (<c>OrgRules.Fight</c>, else
    /// today's). Rounds and minutes come from the rules (else 3 rounds of the house's minutes).</item>
    /// <item>The field is seeded by what the promotion THINKS of each man (scouting, not true ability).
    /// The next man in that order, if there is one, is the alternate.</item>
    /// <item>The rest is <see cref="OneNightBracket.Run"/>; "injured" means the results processor gave
    /// the winner an injury in the bout.</item>
    /// <item>No champion (a final drawn after overtime): the prize is split between the finalists and the
    /// news says so. Nobody left for the final: no prize, and the news says that too.</item>
    /// </list>
    /// Deterministic: every bout is seeded from its fight id, as any booked fight is.
    /// </summary>
    public OneNightResult? RunOneNight(World world, PromotionId promoId, int size, DateOnly date, Money prize,
        WeightClass? wc, FightRules? rules)
    {
        if (size < 2 || (size & (size - 1)) != 0) return null;   // must be a power of two
        if (world.Get(promoId) is not { } promo) return null;
        if (Field(world, promo, size, date, wc, except: null) is not { } field) return null;

        var venue = Geography.WorldMap.PickVenue(world, promo);
        string label = wc is { } c0 ? FriendlyClass(c0) : "Open";
        var ev = new Event
        {
            Id = world.NewEventId(),
            Promotion = promoId,
            Name = $"{promo.Abbreviation} {label} Grand Prix",
            Date = date,
            Region = venue is not null && world.Get(venue.City) is { } c ? c.Region : promo.BaseRegion,
            Venue = venue?.Name,
            VenueId = venue?.Id,
            Tier = EventTier.SuperCard,
            Status = EventStatus.Scheduled,
        };
        world.Events[ev.Id] = ev;
        return Run(world, promo, ev, field.Seeded, field.Alternate, prize, wc, rules);
    }

    /// <summary>
    /// Run a bracket into a night already on the calendar (an Origins tournament night: announced with an
    /// empty card, drawn and fought on its date). Same rules as the overload above; the event's own date
    /// and venue are kept. Null, and the event untouched, when the field can't be filled.
    /// </summary>
    public OneNightResult? RunOneNight(World world, Event ev, int size, Money prize, WeightClass? wc, FightRules? rules)
    {
        if (size < 2 || (size & (size - 1)) != 0) return null;
        if (world.Get(ev.Promotion) is not { } promo) return null;
        if (Field(world, promo, size, ev.Date, wc, except: ev.Id) is not { } field) return null;
        return Run(world, promo, ev, field.Seeded, field.Alternate, prize, wc, rules);
    }

    /// <summary>
    /// The field a bracket night would draw on <paramref name="date"/>, seeded by what the promotion THINKS of
    /// each man, with the alternate (read-only; a short field comes back with <see cref="FieldPreview.Full"/>
    /// false). Only men the house could book that night (<see cref="OriginsShows.CanBook"/>: fit, a fight left
    /// on the deal, not in a re-sign window) and who aren't booked on another scheduled card (other than
    /// <paramref name="except"/>, the night itself). An open-weight bracket is the men's; a class bracket its
    /// class. A man the house has promised a place (an open title-shot promise) gets in ahead of the order;
    /// seeding is still by the scouts' read. <paramref name="size"/> defaults to the house's bracket size.
    /// </summary>
    public static FieldPreview PreviewField(World world, Promotion promo, DateOnly date, EventId? except,
        int? size = null, WeightClass? wc = null)
    {
        int n = size ?? Math.Max(2, promo.Rules.TournamentSize > 0 ? promo.Rules.TournamentSize : 8);
        var committed = new HashSet<FighterId>();
        foreach (var e in world.Events.Values)
            if (e.Status == EventStatus.Scheduled && e.Id != except && !LocalCircuit.IsLocalCard(e))
                foreach (var f in e.Card) { committed.Add(f.RedCorner); committed.Add(f.BlueCorner); }
        var pool = world.RosterOf(promo.Id)
            .Where(f => (wc is { } w ? f.WeightClass == w : f.Sex == Sex.Male)
                        && OriginsShows.CanBook(world, promo, f, date) && !committed.Contains(f.Id))
            .Select(f => (F: f, Read: Scouting.ScoutingService.PerceivedAbility(world, promo, f),
                          Promised: world.Promises.Any(p => p.Live && p.Fighter == f.Id && p.Promotion == promo.Id && p.Kind == PromiseKind.TitleShot)))
            .ToList();
        var chosen = pool.OrderByDescending(x => x.Promised).ThenByDescending(x => x.Read).ThenBy(x => x.F.Id.Value)
            .Take(n + 1).ToList();
        var seeds = chosen.Take(n).OrderByDescending(x => x.Read).ThenBy(x => x.F.Id.Value).ToList();
        return new FieldPreview(seeds.Select(x => x.F).ToList(), chosen.Count > n ? chosen[n].F : null,
            seeds.Where(x => x.Promised).Select(x => x.F.Id).ToList(), n);
    }

    /// <summary>The field the night draws, or null when it can't be filled (<see cref="PreviewField"/>).</summary>
    private static (List<Fighter> Seeded, Fighter? Alternate)? Field(World world, Promotion promo, int size, DateOnly date,
        WeightClass? wc, EventId? except)
    {
        var preview = PreviewField(world, promo, date, except, size, wc);
        return preview.Full ? (preview.Seeds.ToList(), preview.Alternate) : null;
    }

    private OneNightResult Run(World world, Promotion promo, Event ev, List<Fighter> field, Fighter? alternate, Money prize,
        WeightClass? wc, FightRules? rules)
    {
        rules ??= promo.Rules.Fight ?? FightRules.Modern;
        var promoId = promo.Id;
        var date = ev.Date;
        int roundSeconds = (rules.RoundMinutes ?? promo.Rules.RoundMinutes) * 60;
        var night = OneNightBracket.Run(field, alternate, rules, roundSeconds,
            (red, blue, p) =>
            {
                var fight = new Fight
                {
                    Id = world.NewFightId(),
                    RedCorner = red.Id,
                    BlueCorner = blue.Id,
                    // Open weight is filed under the open division's nominal class and labelled as such.
                    WeightClass = wc ?? Division.OpenClass,
                    OpenWeight = wc is null,
                    ScheduledRounds = p.ScheduledRounds,
                };
                LocalCircuit.YieldTo(world, new[] { fight.RedCorner, fight.BlueCorner });
                var o = _engine.Simulate(fight, world, p, out var redEnd, out var blueEnd);   // no screen shows these bouts' text
                ResultsProcessor.Apply(world, ev, fight);
                ev.Card.Add(fight);
                return new BracketBoutResult(o, redEnd, blueEnd, fight.Id);
            },
            injured: f => f.CurrentInjury is not null);

        _events.Settle(world, ev);

        // The bracket as it ran, saved with the night (the screen draws it, walkovers and the alternate included).
        ev.BracketAlternate = alternate?.Id;
        ev.Bracket.Clear();
        var slotInRound = new Dictionary<int, int>();
        foreach (var b in night.Bouts)
        {
            int slot = slotInRound.GetValueOrDefault(b.Round);
            slotInRound[b.Round] = slot + 1;
            ev.Bracket.Add(new BracketSlot
            {
                Round = b.Round, Slot = slot, Red = b.Red, Blue = b.Blue, Fight = b.Fight, Advanced = b.Advanced,
                How = Enum.Parse<BracketHow>(b.How.ToString()), Withdrew = b.Withdrew,
            });
            if (b.Fight is { } fid && ev.Card.FirstOrDefault(f => f.Id == fid) is { } fought) fought.BracketRound = b.Round;
        }

        foreach (var b in night.Bouts)
            if (b.Withdrew is { } wid && world.Get(wid) is { } w)
                News.Post(world, date, NewsCategory.Result,
                    $"{w.FullName} cannot continue in the {ev.Name}",
                    "Won the bout but is in no state to fight again tonight.", promoId, wid);

        if (night.Champion is { } champion)
        {
            if (prize.Amount > 0)
                Finance.Ledger.Expense(promo, date, FinanceCategory.Other, prize, $"{ev.Name} — winner's purse");
            News.Post(world, date, NewsCategory.Result,
                $"{champion.FullName} wins the {ev.Name}",
                wc is { } c1 ? $"Ran the table to take the {FriendlyClass(c1)} Grand Prix." : "Ran the table on a one-night open-weight bracket.",
                promoId, champion.Id);
        }
        else if (night.FinalDrawn && night.Bouts[^1] is { Red: { } r, Blue: { } bl })
        {
            var a = world.Get(r); var b = world.Get(bl);
            if (prize.Amount > 0)
            {
                var half = new Money(prize.Amount / 2);
                Finance.Ledger.Expense(promo, date, FinanceCategory.Other, half, $"{ev.Name} — purse split ({a?.FullName})");
                Finance.Ledger.Expense(promo, date, FinanceCategory.Other, new Money(prize.Amount - half.Amount), $"{ev.Name} — purse split ({b?.FullName})");
            }
            News.Post(world, date, NewsCategory.Result,
                $"No winner in the {ev.Name}",
                $"{a?.FullName} and {b?.FullName} fought the final to a standstill, overtime included. The purse is split and nobody takes the title.",
                promoId, r);
        }
        else
        {
            News.Post(world, date, NewsCategory.Result,
                $"The {ev.Name} ends without a champion",
                "Nobody was left fit to fight the final.", promoId);
        }

        return new OneNightResult(ev, night.Champion, night);
    }

    private static string FriendlyClass(WeightClass wc) =>
        System.Text.RegularExpressions.Regex.Replace(wc.ToString(), "(?<=.)([A-Z])", " $1");
}
