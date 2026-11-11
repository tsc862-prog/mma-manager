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

    /// <summary>How close (days either side) another card has to be to count as counter-programming.</summary>
    public const int CounterProgrammingDays = 3;

    /// <summary>The attendance cut a counter-programmed card takes (a flat 8%, however many rivals).</summary>
    public const double CounterProgrammingPenalty = 0.08;

    /// <summary>
    /// The cards that count as counter-programming against a show on this date:
    /// any other promotion's card (anywhere in the world, scheduled or already
    /// run, and the promotion-less local-circuit nights too) within
    /// <see cref="CounterProgrammingDays"/> days, EXCEPT an AI card still
    /// scheduled for a later date. Before the forward calendar an AI card
    /// didn't exist until its own night, so a card was never cut by AI shows
    /// dated after it; that rule is kept until the clash model is redesigned.
    /// The rule has no region test, so it nearly always fires.
    /// </summary>
    public static IEnumerable<Event> ClashingEvents(World world, PromotionId promotion, DateOnly date, EventId? except = null) =>
        world.Events.Values.Where(o => o.Id != except && o.Promotion != promotion
            && o.Status != EventStatus.Cancelled && Math.Abs(o.Date.DayNumber - date.DayNumber) <= CounterProgrammingDays
            && !(o.Status == EventStatus.Scheduled && o.Date > date && o.Promotion != world.PlayerPromotion));

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

    public static EventFinancials Compute(World world, Event ev)
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
        bool clash = ClashingEvents(world, ev.Promotion, ev.Date, ev.Id).Any();
        double counterProgramming = clash ? -CounterProgrammingPenalty : 0;

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
        if (venue is not null) attendance = Math.Min(attendance, venue.Capacity);

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
    private const decimal LocalAppearanceFee = 1_000m;

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
