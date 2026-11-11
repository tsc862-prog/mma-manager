using MmaManager.Core.Model;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Geography;

namespace MmaManager.Core.Sim.Booking;

// ============================================================================
// The Card Desk: everything the Book Event screen needs to edit and read a
// player card through the sim, so no view model ever writes ev.Card or
// ev.MainCardFights itself. Mutations validate and report a reason; the rest is
// read-only and draw-free (no RNG stream is touched, no id counter advanced).
//
// Fog of war: every ability figure here is the player's scouted read
// (MatchmakingAdvisor.ScoutedRead). Rival promotions appear only through public
// facts: their card dates and names, their prestige.
// ============================================================================

/// <summary>Where on the bill a suggested bout is meant to go.</summary>
public enum BoutSlot { MainEvent, CoMain, MainCard, Prelim }

/// <summary>A proposed bout for an open slot, with the one-line reason a matchmaker would give.</summary>
public sealed record BoutProposal(
    FighterId Red, FighterId Blue, WeightClass WeightClass, int Rounds, bool TitleFight, TitleId? Title,
    string Reason, int Score, MatchupAppraisal Appraisal);

/// <summary>A room the card could be staged in, and what the night would look like there.</summary>
public sealed record VenueOption(
    VenueId Venue, string Name, CityId City, int Capacity, Money Rent,
    int ProjectedAttendance, int Demand, Money ProjectedGate, Money ProjectedPpv,
    Money ProductionWithRent, Money BoxOffice, bool SimPick, bool Selected)
{
    /// <summary>People who want in but won't fit (0 when the room holds the demand).</summary>
    public int TurnedAway => Math.Max(0, Demand - Capacity);
}

/// <summary>How a partner's clause stands against the card as it will run.</summary>
public enum TermStatus { Met, Missed, JudgedOnTheNight }

/// <summary>One clause of a partner's term sheet, checked against the projected card.</summary>
public sealed record TermCheck(DealRequirement Requirement, string Text, TermStatus Status, string? Gap);

/// <summary>
/// What one sponsor or rights partner would pay for this card, and why.
/// <see cref="PendingStrikes"/> are the cadence strikes the monthly checks will
/// add before the card airs (media only): enough of them and the partner has
/// walked before the night (<see cref="GoneBeforeCard"/>, fee zero).
/// </summary>
public sealed record PartnerProjection(
    string Partner, bool IsMedia, Money FullFee, Money ExpectedFee, int Strikes, int StrikeLimit,
    IReadOnlyList<TermCheck> Terms, int PendingStrikes = 0)
{
    public bool AtRisk => Terms.Any(t => t.Status == TermStatus.Missed);

    /// <summary>Strikes on the deal by the time the card airs.</summary>
    public int StrikesBeforeCard => Strikes + PendingStrikes;

    /// <summary>The cadence strikes alone end the deal before this card airs.</summary>
    public bool GoneBeforeCard => StrikesBeforeCard >= StrikeLimit;

    /// <summary>The deal ends by this card: gone before it, or a miss on it is the last strike.</summary>
    public bool WouldWalk => GoneBeforeCard || (AtRisk && StrikesBeforeCard + 1 >= StrikeLimit);

    /// <summary>Clauses only the night can settle (card quality, a house or a star too close to call).</summary>
    public bool HasClausesOnTheNight => Terms.Any(t => t.Status == TermStatus.JudgedOnTheNight);
}

/// <summary>The kinds of trouble the desk can see coming.</summary>
public enum BookingRiskKind
{
    EmptyCard, LastFightOnDeal, NotUnderContract, NotMatchReady, PartnerTermMissed, CadenceBroken,
    RivalCardClash, RoomTooSmall, RoomTooBig, CardPadded,
}

/// <summary>A plain-language risk on a card, with what it concerns.</summary>
public sealed record BookingRisk(BookingRiskKind Kind, string Text,
    FighterId? Fighter = null, EventId? OtherEvent = null, string? Partner = null);

/// <summary>
/// The all-in projection of a scheduled card: the box office, the partners'
/// money, every cost the night will carry, and the risks. Figures are what the
/// sim's own settlement would do with the card as it stands, padded the way
/// fight night would pad it, with winners unknown (win bonuses as a range).
/// Not included: the in-house platform's subscriber bump (it is a monthly
/// figure, not a per-card one) and anything decided by how the fights go
/// (card-quality clauses are flagged as judged on the night).
/// </summary>
public sealed class CardProjection
{
    // The house
    public int Attendance { get; init; }
    public int Demand { get; init; }
    public int? Capacity { get; init; }
    public long PpvBuys { get; init; }
    public bool Clash { get; init; }

    // Income
    public Money Gate { get; init; }
    public Money Ppv { get; init; }
    public Money MediaRights { get; init; }
    public Money Sponsors { get; init; }
    public Money Merch { get; init; }
    public Money Income => Gate + Ppv + MediaRights + Sponsors + Merch;

    // Costs
    public Money Production { get; init; }          // staging, without the room
    public Money Rent { get; init; }
    public Money Purses { get; init; }              // the booked card's show money (locals' appearance fees included)
    public Money PpvPoints { get; init; }
    public Money WinBonusMin { get; init; }         // every bout won by the cheaper corner
    public Money WinBonusMax { get; init; }         // every bout won by the dearer corner
    public Money WinBonusExpected => new((WinBonusMin.Amount + WinBonusMax.Amount) / 2m);
    public Money FightOfTheNight { get; init; }     // both corners of the best bout, if it rates
    public Money Filler { get; init; }              // what the house's padding is expected to cost
    public Money FillerFloor { get; init; }         // padding purses only (no bonus won)
    public Money FillerCeiling { get; init; }       // padding purses plus the dearer bonus in every padded bout

    /// <summary>What the clauses judged on the night could cost if they all went against us (half those partners' fees).</summary>
    public Money PartnerFeesAtStakeOnTheNight { get; init; }

    /// <summary>The expected night: average win bonuses, Fight of the Night paid, every partner as projected.</summary>
    public Money Net => Income - Production - Rent - Purses - PpvPoints - WinBonusExpected - FightOfTheNight - Filler;

    /// <summary>
    /// The worst the fights can make it, at the projected house: every bout won
    /// by the dearer corner (padding too), Fight of the Night paid, and every
    /// clause judged on the night missed.
    /// </summary>
    public Money NetLow => Income - PartnerFeesAtStakeOnTheNight - Production - Rent - Purses - PpvPoints
                           - WinBonusMax - FightOfTheNight - FillerCeiling;

    /// <summary>
    /// The best the fights can make it, at the projected house: no win bonus paid
    /// anywhere (draws and no-contests pay none), no Fight of the Night, every
    /// clause judged on the night met.
    /// </summary>
    public Money NetHigh => Income - Production - Rent - Purses - PpvPoints - FillerFloor;

    // The card as it will run
    public int BoutsBooked { get; init; }
    public int BoutsOnTheNight { get; init; }
    public int MainCardOnTheNight { get; init; }
    public FillerPreview Padding { get; init; } = FillerPreview.None(0);

    public IReadOnlyList<PartnerProjection> Partners { get; init; } = Array.Empty<PartnerProjection>();
    public IReadOnlyList<Event> ClashingCards { get; init; } = Array.Empty<Event>();
    public IReadOnlyList<BookingRisk> Risks { get; init; } = Array.Empty<BookingRisk>();
}

public sealed partial class PlayerBooking
{
    /// <summary>The largest main card a booker may set (0 = auto-fit to the media deals).</summary>
    public const int MaxMainCardSize = 15;

    // --- Card order and the main-card split ---------------------------------

    /// <summary>
    /// Move a bout to a new place in the running order. Index 0 is the first
    /// prelim; the last index is the main event. The sim has no position rules
    /// beyond that (a title fight or a five-rounder may sit anywhere), so the
    /// only refusals are a card that isn't yours to edit and an index off the card.
    /// </summary>
    public bool MoveBout(World world, Event ev, FightId fight, int newIndex, out string? error)
    {
        if (!Editable(world, ev, out error)) return false;
        int from = ev.Card.FindIndex(f => f.Id == fight);
        if (from < 0) { error = "That bout isn't on this card."; return false; }
        return MoveBout(world, ev, from, newIndex, out error);
    }

    /// <summary>Move the bout at <paramref name="fromIndex"/> to <paramref name="newIndex"/> (see the id overload).</summary>
    public bool MoveBout(World world, Event ev, int fromIndex, int newIndex, out string? error)
    {
        if (!Editable(world, ev, out error)) return false;
        if (fromIndex < 0 || fromIndex >= ev.Card.Count) { error = "That bout isn't on this card."; return false; }
        if (newIndex < 0 || newIndex >= ev.Card.Count) { error = "That's off the end of the card."; return false; }
        if (fromIndex == newIndex) return true;
        var bout = ev.Card[fromIndex];
        ev.Card.RemoveAt(fromIndex);
        ev.Card.Insert(newIndex, bout);
        return true;
    }

    /// <summary>
    /// Set how many bouts at the top of the order are the main card: 0 = auto
    /// (fight night fits it to the media deals' broadcast window), otherwise
    /// 1 to <see cref="MaxMainCardSize"/>. A size larger than the card simply
    /// means the whole card is the main card, as <see cref="Event.EffectiveMainCard"/> reads it.
    /// </summary>
    public bool SetMainCardSize(World world, Event ev, int size, out string? error)
    {
        if (!Editable(world, ev, out error)) return false;
        if (size < 0 || size > MaxMainCardSize)
        { error = $"The main card can be Auto or 1 to {MaxMainCardSize} fights."; return false; }
        ev.MainCardFights = size;
        return true;
    }

    /// <summary>
    /// The main-card size the card will actually run with: an explicit split as
    /// set, or the auto-fit fight night applies (the strictest broadcast window
    /// across the media deals, else the whole card), over the padded card.
    /// </summary>
    public int MainCardOnTheNight(World world, Event ev) =>
        IsMine(world, ev) ? MainCardOnTheNight(world, ev, CardFiller.Preview(world, ev).BoutsAfter) : 0;

    private static int MainCardOnTheNight(World world, Event ev, int boutsOnTheNight)
    {
        if (ev.MainCardFights > 0) return Math.Min(ev.MainCardFights, boutsOnTheNight);
        return world.Get(ev.Promotion) is { } p && MediaService.BroadcastWindow(p) is { } window
            ? Math.Min(boutsOnTheNight, window)
            : boutsOnTheNight;
    }

    // --- Replacing a corner --------------------------------------------------

    /// <summary>
    /// Swap one corner of a booked bout for another of your fighters, keeping the
    /// bout's place on the bill, its id, its rounds and its catchweight. The belt
    /// stays on the line while the bout still includes the champion (or the title
    /// is vacant); otherwise it comes off, as <see cref="AddBout"/> would refuse it.
    /// The incoming fighter is checked exactly as <see cref="AddBout"/> checks a corner.
    /// </summary>
    public bool ReplaceFighter(World world, Event ev, FightId fight, FighterId outgoing, FighterId incoming, out string? error) =>
        ReplaceFighter(world, ev, fight, outgoing, incoming, out error, out _, out _);

    /// <inheritdoc cref="ReplaceFighter(World, Event, FightId, FighterId, FighterId, out string?)"/>
    /// <param name="titleKept">False when the bout was a title fight and the belt had to come off.</param>
    public bool ReplaceFighter(World world, Event ev, FightId fight, FighterId outgoing, FighterId incoming,
        out string? error, out bool titleKept) =>
        ReplaceFighter(world, ev, fight, outgoing, incoming, out error, out titleKept, out _);

    /// <inheritdoc cref="ReplaceFighter(World, Event, FightId, FighterId, FighterId, out string?)"/>
    /// <param name="titleKept">False when the bout was a title fight and the belt had to come off.</param>
    /// <param name="note">
    /// What the swap did to the belt, for the screen to say (null when nothing
    /// to say): it came off, or it stays on the line against an unranked challenger.
    /// </param>
    public bool ReplaceFighter(World world, Event ev, FightId fight, FighterId outgoing, FighterId incoming,
        out string? error, out bool titleKept, out string? note)
    {
        titleKept = false;
        note = null;
        if (!Editable(world, ev, out error)) return false;
        int idx = ev.Card.FindIndex(f => f.Id == fight);
        if (idx < 0) { error = "That bout isn't on this card."; return false; }
        var bout = ev.Card[idx];
        if (bout.RedCorner != outgoing && bout.BlueCorner != outgoing)
        { error = "That fighter isn't in this bout."; return false; }
        var stayId = bout.RedCorner == outgoing ? bout.BlueCorner : bout.RedCorner;
        if (incoming == outgoing) { error = "That's the same fighter."; return false; }
        if (incoming == stayId) { error = "A fighter can't fight themselves."; return false; }

        var inF = world.Get(incoming);
        var stay = world.Get(stayId);
        if (inF is null || stay is null) { error = "Fighter not found."; return false; }
        if (inF.Contract?.Promotion != world.PlayerPromotion) { error = "The replacement must be on your roster."; return false; }
        if (inF.WeightClass != bout.WeightClass) { error = "The replacement must be in the bout's weight class."; return false; }
        if (inF.Sex != stay.Sex) { error = "A bout can't cross men's and women's divisions."; return false; }
        if (!inF.IsMatchReady(ev.Date)) { error = "The replacement is injured or still recovering."; return false; }
        if (BookedOnCard(ev, incoming)) { error = "The replacement is already on this card."; return false; }
        if (BookedElsewhere(world, ev, incoming)) { error = "The replacement is already booked on another upcoming event."; return false; }

        bool keepTitle = false;
        if (bout.IsTitleFight && bout.Title is { } tid && world.Get(tid) is { } title)
        {
            keepTitle = title.CurrentChampion is not { } champ || champ == stayId || champ == incoming;
            if (!keepTitle)
                note = "The belt comes off: the champion is no longer in the bout.";
            else
            {
                // Same rule as AddBout (any challenger may face the champion), but
                // the screen should say when the challenger hasn't earned a ranking.
                var rankings = world.Get(world.PlayerPromotion)?.Divisions.FirstOrDefault(d => d.Title == tid)?.Rankings;
                var challengers = new[] { stayId, incoming }.Where(id => id != title.CurrentChampion);
                var unranked = challengers.Where(id => rankings is null || !rankings.Contains(id))
                    .Select(id => world.Get(id)?.FullName ?? "the challenger").ToList();
                if (unranked.Count > 0)
                    note = $"The belt stays on the line, but {string.Join(" and ", unranked)} {(unranked.Count == 1 ? "is" : "are")} unranked.";
            }
        }
        titleKept = keepTitle;

        ev.Card[idx] = new Fight
        {
            Id = bout.Id,   // the same slot: picks held by id survive the swap
            RedCorner = bout.RedCorner == outgoing ? incoming : bout.RedCorner,
            BlueCorner = bout.BlueCorner == outgoing ? incoming : bout.BlueCorner,
            WeightClass = bout.WeightClass,
            ScheduledRounds = bout.ScheduledRounds,
            CatchweightLbs = bout.CatchweightLbs,
            IsTitleFight = keepTitle,
            Title = keepTitle ? bout.Title : null,
        };
        // An unfought local minted for this booking goes back into the scene.
        if (world.Get(outgoing) is { LocalTalent: true } jobber && jobber.BoutHistory.Count == 0)
            world.Fighters.Remove(outgoing);
        return true;
    }

    // --- The room ------------------------------------------------------------

    /// <summary>
    /// The rooms in the card's host city, smallest first, each with the house it
    /// would hold, the gate and PPV it would take, production with rent, and the
    /// box office (gate + PPV − production − rent; purses are the same in every
    /// room). <see cref="VenueOption.SimPick"/> marks the room the sim would choose
    /// (<see cref="WorldMap.PickVenueIn"/>); <see cref="VenueOption.Selected"/> the current one.
    /// </summary>
    public IReadOnlyList<VenueOption> VenueOptions(World world, Event ev)
    {
        var promo = world.Get(ev.Promotion);
        if (!IsMine(world, ev) || promo is null || WorldMap.CityOf(world, ev) is not { } city) return Array.Empty<VenueOption>();
        var simPick = WorldMap.PickVenueIn(world, promo, city)?.Id;
        var list = new List<VenueOption>();
        int demand = -1;
        foreach (var v in world.Venues.Values.Where(v => v.City == city).OrderBy(v => v.Capacity).ThenBy(v => v.Id.Value))
        {
            var at = InRoom(world, ev, v);
            if (demand < 0) demand = Financials.Demand(world, at);
            var fin = Financials.Compute(world, at);
            list.Add(new VenueOption(v.Id, v.Name, v.City, v.Capacity, v.Rent,
                fin.Attendance, demand, fin.GateRevenue, fin.PayPerViewRevenue, fin.ProductionCost,
                fin.GateRevenue + fin.PayPerViewRevenue - fin.ProductionCost,
                v.Id == simPick, ev.VenueId == v.Id));
        }
        return list;
    }

    /// <summary>
    /// Stage the card in a different room: any venue in a city the promotion can
    /// book (see <see cref="Org.OrgReach.CanBookIn"/>). The date, tier and card are unchanged.
    /// </summary>
    public bool SetVenue(World world, Event ev, VenueId venue, out string? error)
    {
        if (!Editable(world, ev, out error)) return false;
        if (world.Get(venue) is not { } v) { error = "That venue doesn't exist."; return false; }
        if (world.Get(ev.Promotion) is not { } promo) { error = "Promotion not found."; return false; }
        if (!Org.OrgReach.CanBookIn(world, promo, v.City)) { error = "That city is beyond the promotion's reach."; return false; }
        ev.VenueId = v.Id;
        ev.Venue = v.Name;
        if (world.Get(v.City) is { } c) ev.Region = c.Region;
        return true;
    }

    // A stand-in for the card in another room, for read-only projections. It
    // shares the card list (never mutated) and the id, so the clash rule still
    // skips the real card.
    private static Event InRoom(World world, Event ev, Venue v) => new()
    {
        Id = ev.Id,
        Promotion = ev.Promotion,
        Name = ev.Name,
        Date = ev.Date,
        Venue = v.Name,
        VenueId = v.Id,
        Region = world.Get(v.City)?.Region ?? ev.Region,
        Tier = ev.Tier,
        Status = ev.Status,
        Card = ev.Card,
        MainCardFights = ev.MainCardFights,
        HypeBonus = ev.HypeBonus,
    };

    // --- Filler ----------------------------------------------------------------

    /// <summary>What the house would add on fight night to bring a short card up to length, and its cost.</summary>
    /// <remarks>Your cards only: another promotion's padding would name its roster's best by true ability. Empty for theirs.</remarks>
    public FillerPreview PreviewPadding(World world, Event ev) =>
        IsMine(world, ev) ? CardFiller.Preview(world, ev) : FillerPreview.None(ev.Card.Count);

    // --- The all-in projection --------------------------------------------------

    /// <summary>
    /// The whole night as the sim would settle it, from what the player may know:
    /// the box office (<see cref="Financials"/>), each partner's fee checked
    /// clause by clause against the card as it will run (padded, with the
    /// main-card split fight night will apply), purses, PPV points, win bonuses
    /// as a range, production and rent, filler, and the risks in plain words.
    /// Read-only and draw-free.
    /// </summary>
    /// <remarks>
    /// Your cards only: another promotion's purses, partner terms and strikes are
    /// its private business, so its card projects as empty.
    /// </remarks>
    public CardProjection Project(World world, Event ev)
    {
        if (!IsMine(world, ev)) return new CardProjection();
        var promo = world.Get(ev.Promotion);
        var padding = CardFiller.Preview(world, ev);
        var fin = Financials.Compute(world, ev);
        int demand = Financials.Demand(world, ev);
        var venue = ev.VenueId is { } vid ? world.Get(vid) : null;
        int mainCard = MainCardOnTheNight(world, ev, padding.BoutsAfter);
        var clashes = Financials.ClashingCards(world, ev);

        // Fighter money on the booked card.
        Money purses = Money.Zero, bonusMin = Money.Zero, bonusMax = Money.Zero, points = Money.Zero;
        foreach (var bout in ev.Card)
        {
            var r = world.Get(bout.RedCorner);
            var b = world.Get(bout.BlueCorner);
            purses += Purse(r) + Purse(b);
            decimal br = r?.Contract?.WinBonus.Amount ?? 0m, bb = b?.Contract?.WinBonus.Amount ?? 0m;
            bonusMin += new Money(Math.Min(br, bb));
            bonusMax += new Money(Math.Max(br, bb));
        }
        var onTheNight = ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner })
            .Concat(padding.Bouts.Where(p => !p.LocalFiller).SelectMany(p => new[] { p.Red!.Value, p.Blue!.Value }));
        if (fin.PayPerViewRevenue.Amount > 0)
            foreach (var id in onTheNight)
                if (world.Get(id)?.Contract is { PpvPoints: > 0 } c && c.Promotion == ev.Promotion)
                    points += Contracts.Negotiator.PpvCut(c, fin.PayPerViewRevenue);

        var rent = venue?.Rent ?? Money.Zero;
        var partners = promo is null ? new List<PartnerProjection>()
            : Partners(world, promo, ev, fin.Attendance, padding, mainCard);
        var media = partners.Where(p => p.IsMedia).Aggregate(Money.Zero, (s, p) => s + p.ExpectedFee);
        var sponsors = partners.Where(p => !p.IsMedia).Aggregate(Money.Zero, (s, p) => s + p.ExpectedFee);
        var merch = promo is null || fin.Attendance <= 0 ? Money.Zero
            : new Money(fin.Attendance * (6m + promo.Prestige.Value * 0.08m));
        var fotn = promo is null || ev.Card.Count == 0 ? Money.Zero : promo.Rules.FightOfNightBonus * 2m;

        var risks = Risks(world, promo, ev, fin.Attendance, demand, venue, padding, partners, clashes);

        return new CardProjection
        {
            Attendance = fin.Attendance,
            Demand = demand,
            Capacity = venue?.Capacity,
            PpvBuys = fin.PayPerViewBuys,
            Clash = clashes.Count > 0,
            Gate = fin.GateRevenue,
            Ppv = fin.PayPerViewRevenue,
            MediaRights = media,
            Sponsors = sponsors,
            Merch = merch,
            Production = fin.ProductionCost - rent,
            Rent = rent,
            Purses = purses,
            PpvPoints = points,
            WinBonusMin = bonusMin,
            WinBonusMax = bonusMax,
            FightOfTheNight = fotn,
            Filler = padding.Cost,
            FillerFloor = padding.Purses,
            FillerCeiling = padding.Purses + padding.WinBonusMax,
            PartnerFeesAtStakeOnTheNight = partners
                .Where(p => !p.GoneBeforeCard && !p.AtRisk && p.HasClausesOnTheNight)
                .Aggregate(Money.Zero, (s, p) => s + p.FullFee * 0.5m),
            BoutsBooked = ev.Card.Count,
            BoutsOnTheNight = padding.BoutsAfter,
            MainCardOnTheNight = mainCard,
            Padding = padding,
            Partners = partners,
            ClashingCards = clashes,
            Risks = risks,
        };
    }

    /// <summary>A projected house within this share of an attendance clause is judged on the night.</summary>
    public const double AttendanceMargin = 0.10;

    /// <summary>A main-eventer within this many popularity points of a star clause is judged on the night.</summary>
    public const int StarPowerMargin = 3;

    private static Money Purse(Fighter? f) =>
        f?.Contract is { } c ? c.PursePerFight
        : f?.LocalTalent == true ? new Money(Financials.LocalAppearanceFee)
        : Money.Zero;

    // Every partner's fee for this card, as Sponsorships/MediaService.SettleEvent
    // would pay it: full when the terms hold, half on a miss.
    private static List<PartnerProjection> Partners(World world, Promotion promo, Event ev, int attendance,
        FillerPreview padding, int mainCard)
    {
        var list = new List<PartnerProjection>();
        decimal tierMult = ev.Tier switch { EventTier.Ppv => 1.5m, EventTier.SuperCard => 2.0m, _ => 1.0m };
        decimal premium = Org.OrgPerks.PartnerPremium(promo) * Org.Franchise.PartnerFactor(world, promo);
        decimal mediaPremium = premium * (decimal)Org.MarketShare.Factor(world, promo);
        // Locals on the card as it will run: the booked card, the roster bouts
        // the house adds, and two per local-vs-local filler pair.
        var paddedIds = padding.Bouts.Where(p => !p.LocalFiller).SelectMany(p => new[] { p.Red!.Value, p.Blue!.Value });
        int locals = CountLocals(world, ev, paddedIds) + 2 * padding.LocalPairs;

        foreach (var deal in promo.SponsorDeals)
        {
            var terms = Terms(world, ev, deal.Requirements, attendance, locals, mainCard);
            var full = new Money(deal.FeePerEvent.Amount * tierMult * premium);
            list.Add(new PartnerProjection(deal.Partner, false, full, Expected(full, terms),
                deal.Strikes, Sponsorships.StrikeLimit, terms));
        }
        bool ppvNight = ev.Tier >= EventTier.Ppv;
        foreach (var deal in promo.MediaDeals)
        {
            if (deal.Kind == MediaKind.PpvCarrier && !ppvNight) continue;   // the carrier only judges PPV nights
            var terms = Terms(world, ev, deal.Requirements, attendance, locals, mainCard);
            var full = new Money(deal.RightsFeePerEvent.Amount * mediaPremium);
            int pending = CadenceBeforeCard(world, ev, deal).Strikes;
            bool gone = deal.Strikes + pending >= MediaService.StrikeLimit;
            list.Add(new PartnerProjection(deal.Partner, true, full, gone ? Money.Zero : Expected(full, terms),
                deal.Strikes, MediaService.StrikeLimit, terms, pending));
        }
        return list;
    }

    private static Money Expected(Money full, IReadOnlyList<TermCheck> terms) =>
        terms.Any(t => t.Status == TermStatus.Missed) ? full * 0.5m : full;

    /// <summary>
    /// The cadence strikes a media deal takes before this card airs, walking the
    /// weekly ticks from today the way <see cref="Living.WorldTick"/> runs them:
    /// each week our cards that fall due air first (resetting the clock), then on
    /// a month turn <see cref="MediaService"/>'s cadence check strikes a deal that
    /// has gone more than its threshold without a card and resets the clock to
    /// that day. The card itself runs on the first tick on or after its date, so
    /// only ticks strictly before it count. Stops at the strike that ends the deal.
    /// </summary>
    private static (int Strikes, DealRequirement? Cadence, DateOnly Clock) CadenceBeforeCard(World world, Event ev, MediaDeal deal)
    {
        var clock = deal.LastAiredOn ?? deal.SignedOn;
        if (deal.Requirements.FirstOrDefault(r => r.Kind == DealRequirementKind.EventCadence) is not { } cadence)
            return (0, null, clock);
        var ours = world.Events.Values
            .Where(e => e.Promotion == ev.Promotion && e.Id != ev.Id && e.Status == EventStatus.Scheduled
                        && e.Card.Count > 0 && e.Date < ev.Date)   // an empty card never airs
            .Select(e => e.Date).OrderBy(d => d).ToList();
        int strikes = 0;
        var prev = world.CurrentDate;
        for (var t = prev.AddDays(7); t < ev.Date && deal.Strikes + strikes < MediaService.StrikeLimit; t = t.AddDays(7))
        {
            foreach (var d in ours)
                if (d <= t && d > clock) clock = d;
            if (t.Month != prev.Month && t.DayNumber - clock.DayNumber > cadence.Threshold)
            {
                strikes++;
                clock = t;
            }
            prev = t;
        }
        return (strikes, cadence, clock);
    }

    private static List<TermCheck> Terms(World world, Event ev, IEnumerable<DealRequirement> reqs,
        int attendance, int locals, int mainCard)
    {
        var list = new List<TermCheck>();
        foreach (var req in reqs)
        {
            if (req.Kind == DealRequirementKind.EventCadence) continue;   // a calendar clause; see the cadence risk
            string text = DealCompliance.Describe(req);
            TermCheck Check(bool met, string? gap) =>
                new(req, text, met ? TermStatus.Met : TermStatus.Missed, met ? null : gap);
            switch (req.Kind)
            {
                case DealRequirementKind.MinCardQuality:
                    list.Add(new TermCheck(req, text, TermStatus.JudgedOnTheNight, "judged on how the fights go"));
                    break;
                case DealRequirementKind.MinAttendance:
                    // The house is settled after the fights (popularity moves with
                    // results), so a projection this close to the line is a coin-flip.
                    if (Math.Abs(attendance - req.Threshold) <= req.Threshold * AttendanceMargin)
                        list.Add(new TermCheck(req, text, TermStatus.JudgedOnTheNight,
                            $"projected house {attendance:N0} against {req.Threshold:N0}: too close to call"));
                    else
                        list.Add(Check(attendance >= req.Threshold,
                            $"projected house {attendance:N0}, {req.Threshold - attendance:N0} short"));
                    break;
                case DealRequirementKind.MinLocalFighters:
                    list.Add(Check(locals >= req.Threshold, $"{locals} local, {req.Threshold - locals} short"));
                    break;
                case DealRequirementKind.MinMainCard:
                    list.Add(Check(mainCard >= req.Threshold, $"main card of {mainCard}, {req.Threshold - mainCard} short"));
                    break;
                case DealRequirementKind.MaxMainCard:
                    list.Add(Check(mainCard <= req.Threshold, $"main card of {mainCard}, {mainCard - req.Threshold} over"));
                    break;
                case DealRequirementKind.MinStarPower:
                    int top = ev.Card.Count == 0 ? 0 : Math.Max(
                        world.Get(ev.Card[^1].RedCorner)?.Intangibles.Popularity.Value ?? 0,
                        world.Get(ev.Card[^1].BlueCorner)?.Intangibles.Popularity.Value ?? 0);
                    // Popularity is read after the main event is fought, so a name
                    // a few points either side of the bar is decided on the night.
                    if (ev.Card.Count > 0 && Math.Abs(top - req.Threshold) <= StarPowerMargin)
                        list.Add(new TermCheck(req, text, TermStatus.JudgedOnTheNight,
                            $"main event's best name is at {top} against {req.Threshold}: too close to call"));
                    else
                        list.Add(Check(top >= req.Threshold,
                            ev.Card.Count == 0 ? "no main event yet" : $"main event's best name is at {top}, {req.Threshold - top} short"));
                    break;
                case DealRequirementKind.TitleFight:
                    list.Add(Check(DealCompliance.Meets(world, ev, req), "no belt on the card"));
                    break;
                default:
                    list.Add(Check(DealCompliance.Meets(world, ev, req), "the house rules don't fit"));
                    break;
            }
        }
        return list;
    }

    // DealCompliance's local count (host region or local-scene talent).
    private static int CountLocals(World world, Event ev, IEnumerable<FighterId> padded) =>
        ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner })
            .Concat(padded)
            .Distinct()
            .Select(id => world.Get(id))
            .Count(f => f is not null && (f.LocalTalent
                || f.Nationality == ev.Region
                || (f.HomeCity is { } hc && world.Get(hc)?.Region == ev.Region)));

    private List<BookingRisk> Risks(World world, Promotion? promo, Event ev, int attendance, int demand,
        Venue? venue, FillerPreview padding, IReadOnlyList<PartnerProjection> partners, IReadOnlyList<Event> clashes)
    {
        var risks = new List<BookingRisk>();
        if (ev.Card.Count == 0)
            risks.Add(new(BookingRiskKind.EmptyCard, "No bouts booked yet. A card with no bouts doesn't run."));

        // The fighters, main event first.
        for (int i = ev.Card.Count - 1; i >= 0; i--)
            foreach (var id in new[] { ev.Card[i].RedCorner, ev.Card[i].BlueCorner })
            {
                if (world.Get(id) is not { } f || f.LocalTalent) continue;
                if (f.Contract?.Promotion != ev.Promotion)
                    risks.Add(new(BookingRiskKind.NotUnderContract,
                        $"{f.FullName} is no longer under contract with you.", id));
                else if (f.Contract.FightsRemaining <= 1)
                    risks.Add(new(BookingRiskKind.LastFightOnDeal,
                        $"{f.FullName} is on the last fight of the deal: after this bout the contract is up, and rivals can bid.", id));
                if (!f.IsMatchReady(ev.Date))
                    risks.Add(new(BookingRiskKind.NotMatchReady, $"{f.FullName} {NotReadyWhy(f, ev.Date)}", id));
            }

        // Cadence first: those strikes land before the card, and can end a deal
        // before any clause on the card is judged.
        if (promo is not null)
            foreach (var deal in promo.MediaDeals)
            {
                var (strikes, cadence, _) = CadenceBeforeCard(world, ev, deal);
                if (strikes == 0 || cadence is null) continue;
                int total = deal.Strikes + strikes;
                risks.Add(new(BookingRiskKind.CadenceBroken,
                    $"{deal.Partner} wants {DealCompliance.Describe(cadence)}, and nothing of ours airs in time: "
                    + $"{(strikes == 1 ? "a strike lands" : $"{strikes} strikes land")} on the monthly check before this card"
                    + (total >= MediaService.StrikeLimit
                        ? $", which ends the deal before the card airs (they won't pay for it)."
                        : $" (strike {total} of {MediaService.StrikeLimit})."),
                    Partner: deal.Partner));
            }

        foreach (var p in partners.Where(p => p.AtRisk && !p.GoneBeforeCard))
        {
            var missed = p.Terms.Where(t => t.Status == TermStatus.Missed)
                .Select(t => $"{t.Text} ({t.Gap})");
            string consequence = p.WouldWalk
                ? "they pay half, and that strike ends the deal"
                : $"they pay half and it is strike {p.StrikesBeforeCard + 1} of {p.StrikeLimit}";
            risks.Add(new(BookingRiskKind.PartnerTermMissed,
                $"{p.Partner}: {string.Join("; ", missed)}. On this card {consequence}.", Partner: p.Partner));
        }

        foreach (var o in clashes)
        {
            int days = Math.Abs(o.Date.DayNumber - ev.Date.DayNumber);
            string who = world.Get(o.Promotion)?.Abbreviation ?? "another promotion";
            risks.Add(new(BookingRiskKind.RivalCardClash,
                $"{o.Name} ({who}) is {(days == 0 ? "the same day" : $"{days} day{(days == 1 ? "" : "s")} away")} "
                + $"on {o.Date:d MMM}: a clash costs about {Financials.ClashPenalty:P0} of the house.", OtherEvent: o.Id));
        }

        if (venue is not null && ev.Card.Count > 0)
        {
            if (demand > venue.Capacity * 115 / 100)
                risks.Add(new(BookingRiskKind.RoomTooSmall,
                    $"{venue.Name} holds {venue.Capacity:N0}; about {demand - venue.Capacity:N0} more want in. A bigger room would sell them."));
            else if (attendance < venue.Capacity / 2)
                risks.Add(new(BookingRiskKind.RoomTooBig,
                    $"{venue.Name} will be about {100 - attendance * 100 / Math.Max(1, venue.Capacity)}% empty ({attendance:N0} of {venue.Capacity:N0}); the rent is {venue.Rent} either way."));
        }

        if (padding.WouldPad)
            risks.Add(new(BookingRiskKind.CardPadded,
                $"The card is short: on fight night the house adds {padding.Bouts.Count} bout{(padding.Bouts.Count == 1 ? "" : "s")}"
                + (padding.LocalPairs > 0 ? $" ({padding.LocalPairs} local-vs-local)" : "")
                + $" at about {padding.Cost}."));
        return risks;
    }

    private static string NotReadyWhy(Fighter f, DateOnly date) =>
        f.Status == FighterStatus.Retired ? "has retired."
        : f.Status == FighterStatus.Suspended ? $"is suspended{(f.SuspendedUntil is { } s ? $" until {s:d MMM yyyy}" : "")}."
        : f.CurrentInjury is { } inj ? $"is injured ({inj.Description}), expected back {inj.ExpectedReturn:d MMM yyyy}."
        : f.ReadyToFightOn is { } r && r > date ? $"won't be recovered until {r:d MMM yyyy}, after the card."
        : "is not cleared to fight on this date.";

    // --- Suggestions for an open slot ----------------------------------------

    /// <summary>
    /// Ranked proposals for an open slot on this card, best first, each with a
    /// one-line reason (a feud, a title due, an eliminator, a local draw, an even
    /// fight). Built on the matchmaker's rules (champions only in title fights,
    /// teammates and friends don't fight for an undercard purse, contenders meet
    /// contenders) but read through the player's scouts: every ability figure is
    /// the scouted read. Only pairs <see cref="AddBout"/> would accept are offered.
    /// Main events and co-mains favour the belt and the bigger names; prelims
    /// favour even fights and save the names for the main card.
    /// </summary>
    public IReadOnlyList<BoutProposal> Suggest(World world, Event ev, BoutSlot slot, int count)
    {
        if (count <= 0 || world.Get(world.PlayerPromotion) is not { } promo || ev.Promotion != promo.Id)
            return Array.Empty<BoutProposal>();
        var advisor = new MatchmakingAdvisor();
        var host = WorldMap.CityOf(world, ev);
        string? hostName = host is { } hc ? world.Get(hc)?.Name : null;

        var champions = new HashSet<FighterId>();
        foreach (var d in promo.Divisions)
            if (d.Title is { } tid && world.Get(tid)?.CurrentChampion is { } cid) champions.Add(cid);

        var proposals = new List<BoutProposal>();
        foreach (var div in promo.Divisions.OrderBy(d => d.Sex).ThenBy(d => d.WeightClass))
        {
            var pool = world.RosterOf(promo.Id)
                .Where(f => div.Matches(f) && f.IsMatchReady(ev.Date)
                            && !BookedOnCard(ev, f.Id) && !BookedElsewhere(world, ev, f.Id))
                .OrderBy(f => f.Id.Value)
                .ToList();
            if (pool.Count < 2) continue;
            var reads = pool.ToDictionary(f => f.Id, f => MatchmakingAdvisor.ScoutedRead(world, f));
            var title = div.Title is { } t ? world.Get(t) : null;
            int Rank(FighterId id) { int i = div.Rankings.IndexOf(id); return i < 0 ? 999 : i + 1; }

            for (int i = 0; i < pool.Count; i++)
                for (int j = i + 1; j < pool.Count; j++)
                {
                    var a = pool[i];
                    var b = pool[j];
                    // A belt goes to a contender: the champion against someone in
                    // the top eight (the bar the AI's short-notice saves use).
                    bool champBout = title is not null
                        && ((title.CurrentChampion == a.Id && Rank(b.Id) <= ContenderDepth)
                            || (title.CurrentChampion == b.Id && Rank(a.Id) <= ContenderDepth));
                    bool vacantBout = title is not null && title.CurrentChampion is null && Rank(a.Id) <= 2 && Rank(b.Id) <= 2;
                    bool titleBout = champBout || vacantBout;
                    // Champions defend; they don't work the undercard.
                    if (!titleBout && (champions.Contains(a.Id) || champions.Contains(b.Id))) continue;
                    // Only a belt puts teammates and friends in the same cage.
                    if (!titleBout && ((a.Camp is { } room && room == b.Camp) || Living.Bonds.WontFight(world, a.Id, b.Id))) continue;
                    if (titleBout && slot == BoutSlot.Prelim) continue;   // no belt on the prelims

                    // Champion (or the better-ranked corner) in red.
                    var (red, blue) = champBout && title!.CurrentChampion == b.Id || (!champBout && Rank(b.Id) < Rank(a.Id))
                        ? (b, a) : (a, b);
                    var appraisal = advisor.Appraise(world, red.Id, blue.Id, reads[red.Id], reads[blue.Id]);

                    var reasons = new List<string>();
                    double score = slot == BoutSlot.Prelim
                        ? 0.7 * appraisal.Competitiveness + 0.3 * (100 - appraisal.Appeal)
                        : 0.5 * appraisal.Competitiveness + 0.5 * appraisal.Appeal;

                    if (titleBout)
                    {
                        int challenger = vacantBout ? 0 : Rank(title!.CurrentChampion == a.Id ? b.Id : a.Id);
                        reasons.Add(vacantBout ? $"the vacant belt ({Label(Rank(red.Id))} vs {Label(Rank(blue.Id))})"
                            : $"title due ({advisor.StandingOf(world, red.Id).Label} vs {advisor.StandingOf(world, blue.Id).Label})");
                        score += slot switch { BoutSlot.MainEvent => 35, BoutSlot.CoMain => 12, _ => 0 };
                        score += vacantBout ? 8 : Math.Max(0, 9 - challenger);   // the #1 contender is owed it most
                    }
                    else if (Rank(a.Id) <= 5 && Rank(b.Id) <= 5)
                    {
                        reasons.Add($"eliminator ({Label(Rank(red.Id))} vs {Label(Rank(blue.Id))}): the winner is next");
                        score += slot == BoutSlot.Prelim ? -15 : 12;
                    }
                    var feud = Living.Storylines.RivalryBetween(world, a.Id, b.Id);
                    if (feud is not null && feud.Intensity >= 20)
                    {
                        reasons.Add(feud.Intensity >= 70 ? "a bitter feud, the fight the fans want" : "bad blood between them");
                        score += feud.Intensity / 4.0;
                    }
                    if (host is { } city)
                    {
                        var local = new[] { red, blue }.FirstOrDefault(f => f.HomeCity == city)
                                    ?? new[] { red, blue }.Where(f => Reputation.FighterInCity(f, city) >= 40)
                                        .OrderByDescending(f => Reputation.FighterInCity(f, city)).ThenBy(f => f.Id.Value).FirstOrDefault();
                        if (local is not null)
                        {
                            reasons.Add(local.HomeCity == city ? $"{local.LastName} fights at home in {hostName}" : $"{local.LastName} draws in {hostName}");
                            score += slot is BoutSlot.MainEvent or BoutSlot.CoMain ? 10 : 6;
                        }
                    }
                    if (a.BoutHistory.Intersect(b.BoutHistory).Any()) reasons.Add("a rematch");
                    if (reasons.Count == 0)
                        reasons.Add(appraisal.Competitiveness >= 70 ? "an even fight on our read"
                            : appraisal.Competitiveness >= 45 ? "a competitive fight on our read"
                            : "a showcase for the favourite");

                    int rounds = titleBout || slot == BoutSlot.MainEvent ? promo.Rules.TitleRounds : promo.Rules.StandardRounds;
                    string reason = char.ToUpperInvariant(reasons[0][0]) + reasons[0][1..]
                                    + (reasons.Count > 1 ? "; " + string.Join("; ", reasons.Skip(1)) : "") + ".";
                    proposals.Add(new BoutProposal(red.Id, blue.Id, div.WeightClass, rounds, titleBout,
                        titleBout ? title!.Id : null, reason, (int)Math.Round(score), appraisal));
                }
        }

        return proposals
            .OrderByDescending(p => p.Score)
            .ThenBy(p => Math.Min(p.Red.Value, p.Blue.Value))
            .ThenBy(p => Math.Max(p.Red.Value, p.Blue.Value))
            .Take(count)
            .ToList();
    }

    private static string Label(int rank) => rank >= 999 ? "NR" : $"#{rank}";

    // How deep in the rankings a title challenger may come from.
    private const int ContenderDepth = 8;

    // --- Shared guards ---------------------------------------------------------

    // The read-only desk helpers answer for the player's own cards only.
    private static bool IsMine(World world, Event ev) => ev.Promotion == world.PlayerPromotion;

    private static bool Editable(World world, Event ev, out string? error)
    {
        error = null;
        if (ev.Promotion != world.PlayerPromotion) { error = "That isn't your card."; return false; }
        if (ev.Status != EventStatus.Scheduled) { error = "That card is no longer open for booking."; return false; }
        return true;
    }
}
