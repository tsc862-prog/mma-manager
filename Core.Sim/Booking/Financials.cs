using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// A light first-pass box office for a completed card: attendance and gate scale
/// with promotion prestige and the main event's drawing power; PPV only matters
/// once a promotion is big enough. Deep finances (sponsorship, tv deals, venue
/// costs) belong to the later management layer — this just makes running events
/// move the promotion's balance in a believable direction.
/// </summary>
public static class Financials
{
    private const decimal TicketBase = 55m;
    private const decimal PpvPrice = 45m;

    /// <summary>
    /// Projected box office for a still-scheduled card: same attendance/gate/
    /// production model as settlement, with payroll estimated at purses plus
    /// half the win bonuses (winners unknown). Lets the booker see whether a
    /// card makes money BEFORE committing to it.
    /// </summary>
    public static EventFinancials Estimate(World world, Event ev)
    {
        var fin = Compute(world, ev);
        var payroll = Money.Zero;
        foreach (var fight in ev.Card)
        {
            foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
            {
                var f = world.Get(id);
                if (f?.Contract is { } c)
                    payroll += c.PursePerFight + new Money(c.WinBonus.Amount * 0.5m);
                else if (f?.LocalTalent == true)
                    payroll += new Money(LocalAppearanceFee);
            }
        }
        payroll += PpvPointsPayroll(world, ev, fin.PayPerViewRevenue);
        return new EventFinancials
        {
            Attendance = fin.Attendance,
            GateRevenue = fin.GateRevenue,
            PayPerViewBuys = fin.PayPerViewBuys,
            PayPerViewRevenue = fin.PayPerViewRevenue,
            ProductionCost = fin.ProductionCost,
            FighterPayroll = payroll,
        };
    }

    public static EventFinancials Compute(World world, Event ev) => Compute(world, ev, capByRoom: true);

    /// <summary>
    /// The house this card would draw with no walls: the same model as
    /// <see cref="Compute(World, Event)"/> without the room's capacity cap, so a
    /// booker can see how much a small room leaves outside. Read-only.
    /// </summary>
    public static int Demand(World world, Event ev) => Compute(world, ev, capByRoom: false).Attendance;

    /// <summary>Days either side of a card inside which another promotion's card splits the audience.</summary>
    public const int ClashWindowDays = 3;

    /// <summary>What a clash costs the house (a share of attendance).</summary>
    public const double ClashPenalty = 0.08;

    /// <summary>
    /// The other promotions' cards that clash with this one under the box
    /// office's rule (<see cref="IsClash"/>), ordered by date then id: exactly
    /// what the house is charged for. Card dates are public, so a booker may see this.
    /// </summary>
    public static IReadOnlyList<Event> ClashingCards(World world, Event ev) =>
        ClashingCards(world, ev.Promotion, ev.Date, ev.Id);

    /// <summary>The clashing cards for a show <paramref name="promotion"/> would stage on <paramref name="date"/>.</summary>
    public static IReadOnlyList<Event> ClashingCards(World world, PromotionId promotion, DateOnly date, EventId? except = null) =>
        world.Events.Values
            .Where(o => IsClash(world, promotion, date, except, o))
            .OrderBy(o => o.Date).ThenBy(o => o.Id.Value)
            .ToList();

    /// <summary>
    /// The box office's clash rule, for a show <paramref name="promotion"/>
    /// stages on <paramref name="date"/>: another promotion's non-cancelled card
    /// (anywhere, scheduled or already run, the promotion-less local circuit
    /// included) within <see cref="ClashWindowDays"/>, EXCEPT an AI card still
    /// scheduled for a LATER date. Before the forward calendar an AI card didn't
    /// exist until its own night, so no card was ever charged for AI shows dated
    /// after it; that is kept until the clash model is redesigned. The rule has
    /// no region test, so it nearly always fires.
    /// </summary>
    public static bool IsClash(World world, PromotionId promotion, DateOnly date, EventId? except, Event o) =>
        o.Id != except && o.Promotion != promotion
        && o.Status != EventStatus.Cancelled && Math.Abs(o.Date.DayNumber - date.DayNumber) <= ClashWindowDays
        && !(o.Status == EventStatus.Scheduled && o.Date > date && o.Promotion != world.PlayerPromotion
             // a club card the local circuit has staged for later in the month is charged as it always was
             // (it used to be run, and so exist, from the day it was staged)
             && !Living.LocalCircuit.IsLocalCard(o));

    private static EventFinancials Compute(World world, Event ev, bool capByRoom)
    {
        var promo = world.Get(ev.Promotion);
        int prestige = promo?.Prestige.Value ?? 40;
        int drawPower = MainEventDraw(world, ev);
        var hostCity = Geography.WorldMap.CityOf(world, ev);
        int cityPop = promo is not null && hostCity is { } hc ? Geography.Reputation.OrgInCity(promo, hc) : 0;
        int regionPop = promo is null ? 0 : Geography.Reputation.OrgInRegion(world, promo, ev.Region);

        // Marketing lifts the house: the hired department (staff) plus whatever the
        // promotion is pouring into promoting itself this month (the brand campaign).
        double marketing = promo is null ? 0 : Org.StaffService.MarketingBonus(promo) + Org.OrgMarketing.AttendanceBonus(promo);

        // A grudge main event draws better — and the hotter the feud, the bigger the
        // house: a fresh beef adds a little, a trilogy-level war (intensity 100) is
        // the easiest ticket in the sport to sell. A GYM WAR on top (two camps whose
        // fighters keep feuding) brings both rooms' fanbases. A rival's card the
        // same week eats into the house.
        var main = ev.Card.Count > 0 ? ev.Card[^1] : null;
        double grudge = main is not null && Living.Storylines.RivalryBetween(world, main.RedCorner, main.BlueCorner) is { } feud
            ? 0.05 + 0.20 * feud.Intensity / 100.0 : 0;
        if (main is not null && world.Get(main.RedCorner)?.Camp is { } warA && world.Get(main.BlueCorner)?.Camp is { } warB
            && Living.Camps.AtWar(world, warA, warB))
            grudge += 0.05;

        // A belt on the line sells the fight in proportion to the BELT's own
        // name (Title.Prestige): a storied strap headlines a market; a club
        // trinket adds a line to the poster.
        if (main is { IsTitleFight: true, Title: { } beltId } && world.Get(beltId) is { } belt)
            drawPower += (int)(belt.Prestige.Value * 0.3);
        bool clash = world.Events.Values.Any(o => IsClash(world, ev.Promotion, ev.Date, ev.Id, o));
        double counterProgramming = clash ? -ClashPenalty : 0;

        // Head-to-head market split: sharing a region with same-or-bigger promotions
        // carves up the fanbase, so each gets a thinner house.
        double competition = promo is null ? 1.0 : RegionalCompetition(world, promo);

        // A fight-mad market fills seats; an indifferent one leaves them empty.
        double interest = hostCity is { } ic
            ? Geography.MmaInterest.Factor(Geography.MmaInterest.InCity(world, ic), 0.4) : 1.0;

        // The house is FANBASE-driven: an established brand fills its building, but
        // a promotion nobody's heard of yet (low city/region popularity) can't
        // paper a small hall — so a fresh startup with no following draws poorly
        // and runs thin, while a big draw (own fans, star power) sells out. The flat
        // floor is kept SMALL on purpose: a brand with no fanbase can't cover a card's
        // fixed costs, so its first shows lose money until it builds a following (the
        // established orgs draw off their big cityPop term, so this barely moves them).
        // A crowded band thins everyone's slice (MarketShare) — and a hot press
        // tour (an electric presser, a face-off brawl) puts people in seats.
        double market = promo is null ? 1.0 : Org.MarketShare.Factor(world, promo);
        int attendance = (int)((190 + prestige * 38 + drawPower * 30 + cityPop * 78 + regionPop * 14)
            * (1 + marketing + grudge + counterProgramming + ev.HypeBonus * 0.6) * competition * interest * market);
        // The building caps the house — a big draw in a small room leaves money on the table.
        var venue = ev.VenueId is { } vid ? world.Get(vid) : null;
        if (venue is not null && capByRoom) attendance = Math.Min(attendance, venue.Capacity);

        // Card scale: a marquee super-card commands premium pricing and PPV but costs more to stage.
        decimal gateMult = ev.Tier switch { EventTier.Ppv => 1.3m, EventTier.SuperCard => 1.6m, _ => 1.0m };
        decimal prodMult = ev.Tier switch { EventTier.Ppv => 1.4m, EventTier.SuperCard => 1.8m, _ => 1.0m };

        decimal ticketPrice = TicketBase + prestige * 1.4m;
        var gate = new Money(attendance * ticketPrice * gateMult);

        long ppvBuys = 0;
        var ppvRevenue = Money.Zero;
        // Selling a pay-per-view takes a CARRIER — the signed pipe to people's
        // screens (see MediaService). Prestige and star power set the buyrate.
        if (ev.Tier >= EventTier.Ppv && promo is not null && Finance.MediaService.HasPpvCarrier(promo))
        {
            ppvBuys = Math.Max(0, prestige - 45) * 2_000L + drawPower * 3_000L;
            if (ev.Tier == EventTier.SuperCard) ppvBuys = (long)(ppvBuys * 1.5);
            ppvBuys = (long)(ppvBuys * (1 + ev.HypeBonus));   // a hot press tour sells screens too
            // The promotion's cut depends on WHICH carrier signed them (50-60%).
            ppvRevenue = new Money(ppvBuys * PpvPrice * Finance.MediaService.PpvShare(promo));
        }

        // Production scales with the actual house — a small-hall show is a few
        // staff and a rented cage, a packed arena is a television production.
        // (Rent rolls in so it flows through the box office and ledger.)
        var rent = venue?.Rent ?? Money.Zero;
        var production = new Money((10_000m + attendance * 12m) * prodMult) + rent;
        var payroll = Payroll(world, ev) + PpvPointsPayroll(world, ev, ppvRevenue);

        return new EventFinancials
        {
            Attendance = attendance,
            GateRevenue = gate,
            PayPerViewBuys = ppvBuys,
            PayPerViewRevenue = ppvRevenue,
            ProductionCost = production,
            FighterPayroll = payroll,
        };
    }

    /// <summary>
    /// The PPV points owed on this card: every fighter on it whose deal is with
    /// the promotion staging it takes his points' share of its pay-per-view take
    /// (<see cref="Contracts.Negotiator.PpvCut"/>). Nothing on a card that sold no
    /// pay-per-view.
    /// </summary>
    private static Money PpvPointsPayroll(World world, Event ev, Money ppvRevenue)
    {
        var total = Money.Zero;
        if (ppvRevenue.Amount <= 0) return total;
        foreach (var fight in ev.Card)
            foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                if (world.Get(id)?.Contract is { PpvPoints: > 0 } c && c.Promotion == ev.Promotion)
                    total += Contracts.Negotiator.PpvCut(c, ppvRevenue);
        return total;
    }

    private static Money Payroll(World world, Event ev)
    {
        var total = Money.Zero;
        foreach (var fight in ev.Card)
        {
            total += PursePlusBonus(world.Get(fight.RedCorner), fight);
            total += PursePlusBonus(world.Get(fight.BlueCorner), fight);
        }
        return total;
    }

    // Local talent fights for a flat appearance fee, no contract needed.
    internal const decimal LocalAppearanceFee = 1_000m;

    private static Money PursePlusBonus(Fighter? f, Fight fight)
    {
        if (f?.Contract is not { } c)
            return f?.LocalTalent == true ? new Money(LocalAppearanceFee) : Money.Zero;
        bool won = fight.Outcome?.Winner == f.Id;
        return won ? c.PursePerFight + c.WinBonus : c.PursePerFight;
    }

    /// <summary>
    /// The market-split penalty from sharing a region with rival promotions. A
    /// same-tier rival is a direct competitor and carves the gate hardest; a bigger
    /// promotion in your backyard overshadows you somewhat; smaller ones don't move
    /// the needle. The more (and bigger) the neighbours, the thinner your house — the
    /// pressure that eventually folds the weaker of two promotions going head to head.
    /// </summary>
    private static double RegionalCompetition(World world, Promotion promo)
    {
        int myTier = (int)Org.OrgSizing.SizeOf(promo);
        double myStrength = Math.Max(1, promo.Prestige.Value);
        double pressure = 0;
        foreach (var rival in world.Promotions.Values)
        {
            if (rival.Id == promo.Id || rival.Defunct || rival.BaseRegion != promo.BaseRegion) continue;
            int diff = (int)Org.OrgSizing.SizeOf(rival) - myTier;
            if (diff < 0) continue;   // smaller orgs are a different market — no competition
            // Competition bites hardest at your own tier; a promotion far above you
            // plays to a different crowd. Within that, a stronger rival carves off
            // more of the market and an equal splits it — so the bigger brand wins
            // the war and the weaker of two head-to-head promotions bleeds out.
            double proximity = diff == 0 ? 1.0 : diff == 1 ? 0.6 : 0.35;
            pressure += 0.35 * proximity * Math.Min(2.0, rival.Prestige.Value / myStrength);
        }
        return 1.0 / (1.0 + pressure);
    }

    private static int MainEventDraw(World world, Event ev)
    {
        if (ev.Card.Count == 0) return 0;
        var main = ev.Card[^1];
        var host = Geography.WorldMap.CityOf(world, ev);
        return Math.Max(Draw(world, world.Get(main.RedCorner), main, host),
                        Draw(world, world.Get(main.BlueCorner), main, host));
    }

    // A fighter's drawing power: popularity, the marketable look baked into
    // their portrait (a menace sells stakes), how well the host city knows
    // them, and the hometown-hero surge when they fight where they're from.
    private static int Draw(World world, Fighter? f, Fight main, CityId? host)
    {
        if (f is null) return 0;
        int persona = f.PersonaTag switch
        {
            "glam" or "adonis" => 10,
            "babyface" => 2,
            "menace" => main.IsTitleFight
                        || Living.Storylines.RivalryBetween(world, main.RedCorner, main.BlueCorner) is not null
                ? 10 : 4,
            _ => 0,
        };
        int local = host is { } h ? Geography.Reputation.FighterInCity(f, h) / 4 : 0;
        int hometown = host is { } hc && f.HomeCity == hc ? 12 : 0;
        // Star power compounds above ~60 pop — the rare name that sells a building.
        return f.Intangibles.Popularity.Value + persona + local + hometown + Org.Franchise.DrawBonus(f);
    }
}
