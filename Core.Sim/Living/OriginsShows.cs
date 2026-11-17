using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The nights the sport's first promotions actually run (Origins mode), in place of today's forward
/// calendar (which an Origins world never runs):
/// <list type="bullet">
/// <item><see cref="ShowFormat.OneNightTournament"/> (the Crucible): a one-night open-weight bracket a few
/// times a year, announced with an empty card; on the night the field is seeded by what the house THINKS
/// of its men (<see cref="ScoutingService"/>), the next man is the alternate, and
/// <see cref="TournamentService"/> fights it under the house rules. The winner holds the house's
/// tournament belt until the next bracket crowns somebody else; a night with no champion leaves it where
/// it was.</item>
/// <item><see cref="ShowFormat.Card"/> (the Association): a short card of single bouts every couple of
/// months, the open-weight belt on the line when it can be.</item>
/// <item><see cref="ShowFormat.GymChallenge"/> (the Garage): a night of challenge matches between rival
/// academies, the feuding gyms first; a man never fights his own gym-mate, and there are no belts.</item>
/// </list>
/// Cards are matched on scouted ability, never the truth. No club shows exist in 1993 (the local
/// circuit is off in an Origins world), no card is padded with local filler, and nobody weighs in.
/// Deterministic and draw-free: every choice is an ordering with id as the last word.
/// The player books the same nights through <see cref="ScheduleTournament"/>, <see cref="SuggestCard"/>
/// and <see cref="ScheduleCard"/>.
/// </summary>
public static class OriginsShows
{
    /// <summary>A night goes on the books this far ahead.</summary>
    public const int LeadWeeks = 4;

    /// <summary>Inside this many days a card short of bouts is topped up.</summary>
    public const int TopUpDays = 14;

    /// <summary>Weeks between a house's nights.</summary>
    public static int CadenceWeeks(Promotion p) => p.Rules.ShowFormat switch
    {
        ShowFormat.OneNightTournament => 13,   // a bracket a season: eight men need the months between
        ShowFormat.GymChallenge => 8,          // the garage runs whenever two gyms have an argument (about every two months)
        _ => 10,                               // the ring five times a year
    };

    /// <summary>How many bouts a night of single bouts carries.</summary>
    public static int CardBouts(Promotion p) => p.Rules.ShowFormat == ShowFormat.GymChallenge ? 5 : 6;

    /// <summary>
    /// The fewest men this house must hold to put on its night: a full bracket, an alternate and one spare;
    /// two cards' worth of single bouts less a little (a man rests between nights).
    /// </summary>
    public static int MinRoster(Promotion p) => p.Rules.ShowFormat == ShowFormat.OneNightTournament
        ? Math.Max(2, p.Rules.TournamentSize) + 2
        : CardBouts(p) * 2 - 2;

    /// <summary>
    /// How many men an Origins house keeps in each of its divisions. One open division: its night's field,
    /// or its tier's depth when it can afford it (phase 1). Split by weight: the night's field and a few
    /// more, shared among the divisions, at least six a division (a belt and two bouts); a house in the red
    /// keeps four.
    /// </summary>
    public static int DivisionRosterTarget(Promotion p, bool distressed)
    {
        int n = Math.Max(1, p.Divisions.Count(d => d.Sex == Sex.Male));
        int floor = MinRoster(p);
        if (n == 1) return distressed ? floor : Math.Max(floor, Org.OrgSizing.RosterTargetPerDivision(Org.OrgSizing.SizeOf(p)));
        int each = (int)Math.Ceiling((floor + (distressed ? 0 : 6)) / (double)n);
        return Math.Max(distressed ? 4 : 6, each);
    }

    /// <summary>The men a house wants in all: its divisions' targets together.</summary>
    public static int RosterDemand(Promotion p) =>
        DivisionRosterTarget(p, false) * Math.Max(1, p.Divisions.Count(d => d.Sex == Sex.Male));

    /// <summary>The first night's offset from the start: the Garage has been running since 1992; the
    /// Association opens in February; the Crucible needs the winter to sell its carrier.</summary>
    private static int FirstNightWeeks(Promotion p) => p.Rules.ShowFormat switch
    {
        ShowFormat.GymChallenge => 3,
        ShowFormat.OneNightTournament => 10,
        _ => 6,
    };

    /// <summary>Is this world one whose houses run these nights (an Origins world)?</summary>
    public static bool Applies(World world) => world.Mode == WorldMode.Origins;

    /// <summary>Day one: the first nights inside the lead window go on the books (no news: nothing has happened yet).</summary>
    public static void SeedOpening(World world) => RunWeekly(world, world.CurrentDate, new EventService());

    /// <summary>
    /// The weekly pass (after the forward calendar's audit has taken departed men off the cards): announce
    /// each AI house's next night once it is due inside the lead window, and top up the short cards.
    /// </summary>
    public static void RunWeekly(World world, DateOnly today, EventService events)
    {
        foreach (var promo in world.Promotions.Values
                     .Where(p => !p.Defunct && p.Id != world.PlayerPromotion)
                     .OrderBy(p => p.Id.Value).ToList())
        {
            var due = NextNightDue(world, promo);
            for (int k = 1; k <= LeadWeeks; k++)
            {
                var when = today.AddDays(7 * k);
                if (when < due) continue;
                if (Announce(world, promo, when, events, today) is not null) break;
            }
        }
        TopUp(world, today);
    }

    /// <summary>The earliest date this house's next night may be: its cadence after its last one, else its opening night.</summary>
    public static DateOnly NextNightDue(World world, Promotion promo)
    {
        DateOnly? last = null;
        foreach (var e in world.Events.Values)
            if (e.Promotion == promo.Id && e.Status != EventStatus.Cancelled && (last is null || e.Date > last)) last = e.Date;
        // A cancelled night is retried a cadence after it was due, not at once.
        foreach (var e in world.Events.Values)
            if (e.Promotion == promo.Id && e.Status == EventStatus.Cancelled && (last is null || e.Date > last)) last = e.Date;
        return last is { } l ? l.AddDays(7 * CadenceWeeks(promo)) : world.StartDate.AddDays(7 * FirstNightWeeks(promo));
    }

    private static Event? Announce(World world, Promotion promo, DateOnly when, EventService events, DateOnly today)
    {
        int number = world.Events.Values.Count(e => e.Promotion == promo.Id && e.Status != EventStatus.Cancelled) + 1;
        // Where the night goes: home, a night abroad, or exile when the Backlash has closed the home room.
        // Nowhere at all (every hall it could reach is closed) and the house waits.
        if (NightVenue(world, promo, when, number) is not { } room) return null;
        Event ev;
        if (promo.Rules.ShowFormat == ShowFormat.OneNightTournament)
        {
            // A licensed house the code now binds runs no new bracket: it takes the code once its last one is fought.
            if (OriginsReforms.BoundByCode(world, promo)) return null;
            int size = Math.Max(2, promo.Rules.TournamentSize);
            // Only a house that could field the bracket today puts one on the books.
            if (!TournamentService.PreviewField(world, promo, when, null, size).Full) return null;   // (the one field rule: TournamentService)
            ev = NewTournament(world, promo, when, $"{promo.Name} {number}", room);
        }
        else
        {
            var card = SuggestCard(world, promo.Id, when);
            if (card.Count < 2) return null;
            ev = events.CreateEvent(world, promo.Id, NightName(promo, number), when, card, room.City);
            ev.Venue = room.Name; ev.VenueId = room.Id;
        }
        if (ev.Date > today)
            News.Post(world, today, NewsCategory.General, Announcement(world, promo, ev),
                ev.Format == EventFormat.Tournament
                    ? $"{promo.Rules.TournamentSize} men, one night, {Rules(promo)}. The winner takes {promo.Rules.TournamentPrize}."
                    : $"{ev.Card.Count} bouts, {Rules(promo)}.",
                promo.Id, ev.Card.Count > 0 ? ev.Card[^1].RedCorner : null, ev.Id);
        return ev;
    }

    private static string NightName(Promotion promo, int number) => promo.Rules.ShowFormat switch
    {
        ShowFormat.GymChallenge => $"{promo.Name}: Challenge Night {number}",
        _ => $"{promo.Abbreviation} {number}",
    };

    private static string Announcement(World world, Promotion promo, Event ev)
    {
        if (ev.Format == EventFormat.Tournament)
            return $"{promo.Abbreviation} announces {ev.Name} for {ev.Date:d MMMM}";
        var main = ev.Card[^1];
        string a = world.Get(main.RedCorner)?.LastName ?? "?", b = world.Get(main.BlueCorner)?.LastName ?? "?";
        return $"{promo.Abbreviation} sets {a} vs {b} for {ev.Date:d MMMM}";
    }

    private static string Rules(Promotion promo)
    {
        var r = promo.Rules.Fight ?? FightRules.Modern;
        int rounds = r.Rounds ?? promo.Rules.StandardRounds, minutes = r.RoundMinutes ?? promo.Rules.RoundMinutes;
        string time = rounds == 1 ? $"one {minutes}-minute round" : $"{rounds} rounds of {minutes} minutes";
        int lines = promo.Divisions.Count(d => !d.Open);
        string weights = lines == 0 ? "no weight classes" : $"{lines} weight divisions";
        return $"{time}, {(r.Judges ? "judges at the limit" : "no judges")}, {weights}";
    }

    // --- Where a night goes ---------------------------------------------------------

    private const ulong AwaySalt = 0x0219_A3A7_0006UL;

    /// <summary>An AI house's chance of taking a night to a country whose scene is up for grabs (a chapter is live).</summary>
    public const double AwayNightChance = 0.12;

    /// <summary>The least an AI house banks before it gambles a night abroad.</summary>
    public const decimal AwayNightBank = 100_000m;

    /// <summary>
    /// The room a house's night goes in. Its own room, unless:
    /// <list type="bullet">
    /// <item>the Backlash has closed it: exile (<see cref="ExileVenue"/>), or nowhere (null) when every hall it
    /// could reach is closed;</item>
    /// <item>an AI house with money in the bank takes the occasional night (a keyed roll per night) to a country
    /// whose scene is up for grabs: the first night there wakes the scene for the sport;</item>
    /// </list>
    /// Today's mode never comes here (no Origins nights exist).
    /// </summary>
    public static Venue? NightVenue(World world, Promotion promo, DateOnly date, int number)
    {
        var home = HomeVenue(world, promo);
        bool homeOpen = home is not null && !Era.Banned(world, home.City, date);
        if (!homeOpen) return ExileVenue(world, promo, date);
        if (promo.Id != world.PlayerPromotion)
        {
            if (promo.Rules.ShowFormat != ShowFormat.GymChallenge && promo.Balance.Amount >= AwayNightBank
                && OriginsTimeline.Roll(world, AwaySalt, (ulong)promo.Id.Value, (ulong)number) < AwayNightChance
                && AwayCity(world, promo, date) is { } away
                && RoomIn(world, promo, away.Id) is { } abroad)
                return abroad;
        }
        return home;
    }

    /// <summary>
    /// Where a house whose home room is closed takes its nights: the biggest open city in its own country,
    /// else a country whose scene is up for grabs (a welcome abroad), else any open city with a scene. Null
    /// when nowhere will have it.
    /// </summary>
    public static Venue? ExileVenue(World world, Promotion promo, DateOnly date)
    {
        var homeCountry = promo.HomeCity is { } hc ? world.Get(hc)?.Country : null;
        var contested = OriginsTimeline.ContestedScenes(world).ToHashSet(StringComparer.Ordinal);
        var city = world.Cities.Values
            .Where(c => !Era.Banned(world, c.Id, date) && Era.AllowsSceneIn(world, c.Id))
            .OrderBy(c => c.Country == homeCountry ? 0 : world.Get(c.Country) is { } k && contested.Contains(k.Code) ? 1 : 2)
            .ThenBy(c => c.Region == promo.BaseRegion ? 0 : 1)
            .ThenByDescending(c => c.Market).ThenBy(c => c.Id.Value)
            .FirstOrDefault();
        return city is null ? null : RoomIn(world, promo, city.Id);
    }

    /// <summary>The city of a country whose scene is up for grabs that this house would tour to (its own continent first), or null.</summary>
    private static City? AwayCity(World world, Promotion promo, DateOnly date)
    {
        foreach (var code in OriginsTimeline.ContestedScenes(world)
                     .OrderBy(code => Geography.WorldMap.CountryByCode(world, code)?.Continent == promo.BaseRegion ? 0 : 1)
                     .ThenBy(code => code, StringComparer.Ordinal))
        {
            if (Geography.WorldMap.CountryByCode(world, code) is not { } country) continue;
            var city = world.Cities.Values.Where(c => c.Country == country.Id && !Era.Banned(world, c.Id, date))
                .OrderByDescending(c => c.Market).ThenBy(c => c.Id.Value).FirstOrDefault();
            if (city is not null) return city;
        }
        return null;
    }

    /// <summary>
    /// The room for a night the player books: in <paramref name="city"/> when he names one his house can take a
    /// night to (a city whose scene is awake or up for grabs, not closed by a ban), else his own room or, when
    /// the Backlash has closed it, exile. Null when there is nowhere.
    /// </summary>
    public static Venue? PlayerVenue(World world, Promotion promo, DateOnly date, CityId? city)
    {
        if (city is { } c && world.Get(c) is not null && Era.AllowsSceneIn(world, c) && !Era.Banned(world, c, date))
            return RoomIn(world, promo, c);
        var home = HomeVenue(world, promo);
        return home is not null && !Era.Banned(world, home.City, date) ? home : ExileVenue(world, promo, date);
    }

    /// <summary>
    /// The house's own room: its authored home venue when it has one (a founder), else the best fit in its home
    /// town. An AI founder in an Origins world moves down to a smaller room in its own town once it has run
    /// <see cref="SeenNights"/> nights and those nights, read as a home crowd, say that room leaves more of the gate
    /// (<see cref="SettledRoom"/>); the player keeps his hall (he can name his town for a night, as he always could).
    /// </summary>
    public static Venue? HomeVenue(World world, Promotion promo) => HomeRoom(world, promo, out _);

    /// <summary>
    /// <see cref="HomeVenue"/>, with the home crowd an AI founder expects from its last <see cref="SeenNights"/>
    /// completed nights (null for any other house, the player's, or a founder with fewer nights): each night is read as
    /// <see cref="Drew"/> reads it, the gate itself for a home night with seats left over, else the box-office model's
    /// crowd for the home town (<see cref="Financials.DemandAt"/>). <see cref="Contracts.Negotiator.PurseCap"/> prices
    /// the founder's men off it (one pass over the calendar for both).
    /// </summary>
    public static Venue? HomeRoom(World world, Promotion promo, out int? seenCrowd)
    {
        seenCrowd = null;
        var spec = Generation.Origins.OriginsPromotions.All.FirstOrDefault(s => s.Abbreviation == promo.Abbreviation);
        if (spec is not null && world.Venues.Values.Where(v => v.Name == spec.HomeVenue && v.City == promo.HomeCity)
                .OrderBy(v => v.Id.Value).FirstOrDefault() is { } authored)
            return world.Mode == WorldMode.Origins ? SettledRoom(world, promo, authored, out seenCrowd) : authored;
        if (world.Mode == WorldMode.Origins && promo.HomeCity is { } home) return RoomIn(world, promo, home);
        return promo.HomeCity is { } hc ? Geography.WorldMap.PickVenueIn(world, promo, hc) : Geography.WorldMap.PickVenue(world, promo);
    }

    /// <summary>
    /// How many completed nights a founder must have run before it reads them as its home crowd: its last ones, wherever
    /// they were (home, a night away, exile, a bracket). Until then it keeps its hall and its hall-based purse budget.
    /// </summary>
    public const int SeenNights = 3;

    /// <summary>
    /// A founder's room, once it has a record (Origins): history built its hall (the Crucible's four-thousand-seat
    /// pavilion), not its crowd. Once it has run <see cref="SeenNights"/> nights, the house reads its last three
    /// completed nights anywhere as a home crowd, as <see cref="RoomIn"/> reads a founded house's (<see cref="Drew"/>:
    /// the gate itself for a night in its home town with seats left over; for a sell-out, or a night away or in exile,
    /// the crowd the box-office model says the home town would have sent, <see cref="Financials.DemandAt"/>, so this is
    /// partly the model's estimate, not only gates seen). It then takes the smaller room in its town
    /// that leaves the most of the gate once the walls and the production are paid, when one beats its hall; never a
    /// bigger one (a founder's hall is its ceiling: a house that outdraws it stays, as it always did). The Crucible in
    /// a bad decade drew 1,500-2,300 into 4,000 seats and paid 16,000 a night for the empty rows.
    /// </summary>
    private static Venue SettledRoom(World world, Promotion promo, Venue authored, out int? seenCrowd)
    {
        seenCrowd = null;
        // The player's house keeps its hall and its budget: where his nights go and what he pays are his calls (a
        // scripted player whose purse budget read the crowd while he kept the hall re-signed fewer men and did worse).
        if (promo.Id == world.PlayerPromotion) return authored;
        var recent = LastNights(world, promo.Id, SeenNights);
        if (recent.Count < SeenNights) return authored;
        var rooms = world.Venues.Values.Where(v => v.City == authored.City).OrderBy(v => v.Id.Value).ToList();
        int expected = (int)recent.Average(e => Drew(world, e, authored.City, rooms[0]));
        seenCrowd = expected;
        decimal seat = SeatPrice(world, promo);
        decimal Left(Venue v)
        {
            int crowd = Math.Min(v.Capacity, expected);
            return crowd * seat - Financials.OriginsProduction(crowd) - Financials.RentOf(world, v).Amount;
        }
        decimal hall = Left(authored);
        var smaller = rooms.Where(v => v.Capacity < authored.Capacity && Left(v) > hall)
            .OrderByDescending(Left).ThenByDescending(v => v.Capacity).ThenBy(v => v.Id.Value).FirstOrDefault();
        return smaller ?? authored;
    }

    /// <summary>What a seat at this house's night sells for (a bracket's premium included).</summary>
    private static decimal SeatPrice(World world, Promotion promo) =>
        Financials.TicketPrice(world, promo.Prestige.Value)
        * (promo.Rules.ShowFormat == ShowFormat.OneNightTournament ? Financials.OriginsBracketTicketPremium : 1m);

    /// <summary>
    /// The room an Origins house takes in a city: the one that leaves the most of the night's gate once the
    /// walls and the production are paid, for the crowd it can expect there. Before its first night that is
    /// 600 and 100 a prestige point (a young sport); after, it is what its last three nights' cards would
    /// have drawn in this city with no walls (the queue outside a sold-out night is a crowd it has seen; a
    /// house in exile draws what a strange city gives it). Today's estimate fills halls the sport can't.
    /// Ties on the closer fit, then id.
    /// </summary>
    public static Venue? RoomIn(World world, Promotion promo, CityId city)
    {
        var rooms = world.Venues.Values.Where(v => v.City == city).OrderBy(v => v.Id.Value).ToList();
        if (rooms.Count == 0) return null;
        var recent = LastNights(world, promo.Id, 3);
        int expected = recent.Count == 0
            ? 600 + 100 * promo.Prestige.Value
            : (int)recent.Average(e => Drew(world, e, city, rooms[0]));
        decimal seat = SeatPrice(world, promo);
        decimal Left(Venue v)
        {
            int crowd = Math.Min(v.Capacity, expected);
            return crowd * seat - Financials.OriginsProduction(crowd) - Financials.RentOf(world, v).Amount;
        }
        return rooms.OrderByDescending(Left).ThenBy(v => Math.Abs(v.Capacity - expected)).ThenBy(v => v.Id.Value).First();
    }

    /// <summary>
    /// What a past night tells a house about a crowd in <paramref name="city"/>: the crowd itself when the night
    /// was there and seats were left over, else what its card would have drawn there with no walls.
    /// </summary>
    private static int Drew(World world, Event e, CityId city, Venue roomThere)
    {
        if (e.VenueId is { } v && world.Get(v) is { } room && room.City == city && e.Financials!.Attendance < room.Capacity)
            return e.Financials.Attendance;
        return Financials.DemandAt(world, e, roomThere);
    }

    /// <summary>
    /// The house's last <paramref name="n"/> completed nights with a box office, newest first (ties on the
    /// higher id): one pass over the calendar, no sort (the purse cap asks for the home room very often).
    /// </summary>
    private static List<Event> LastNights(World world, PromotionId promo, int n)
    {
        var top = new List<Event>(n + 1);
        foreach (var e in world.Events.Values)
        {
            if (e.Promotion != promo || e.Status != EventStatus.Completed || e.Financials is null) continue;
            int i = top.Count;
            while (i > 0 && (top[i - 1].Date < e.Date || (top[i - 1].Date == e.Date && top[i - 1].Id.Value < e.Id.Value))) i--;
            if (i >= n) continue;
            top.Insert(i, e);
            if (top.Count > n) top.RemoveAt(n);
        }
        return top;
    }

    // --- Tournaments -------------------------------------------------------------

    /// <summary>A one-night bracket on the books: the card is empty until the night draws it.</summary>
    private static Event NewTournament(World world, Promotion promo, DateOnly date, string name, Venue? venue)
    {
        var ev = new Event
        {
            Id = world.NewEventId(),
            Promotion = promo.Id,
            Name = name,
            Date = date,
            Region = venue is not null && world.Get(venue.City) is { } c ? c.Region : promo.BaseRegion,
            Venue = venue?.Name,
            VenueId = venue?.Id,
            // A 1993 bracket is an ordinary night at the box office: no super-card pricing, no TV production.
            Tier = EventTier.FightNight,
            Status = EventStatus.Scheduled,
            Format = EventFormat.Tournament,
        };
        world.Events[ev.Id] = ev;
        return ev;
    }

    /// <summary>
    /// The player books his house's next bracket (an Origins house whose nights are tournaments). Null when
    /// the house does not run brackets, the date is not ahead, or another night of his is already on it.
    /// </summary>
    public static Event? ScheduleTournament(World world, PromotionId promoId, DateOnly date, string? name = null, CityId? city = null) =>
        ScheduleTournament(world, promoId, date, out _, name, city);

    /// <summary>
    /// As above, with the reason a night was refused, in words the screen can show: not a bracket house,
    /// a date not ahead, a night already on that date, or not enough men to fill the bracket (the same
    /// check the AI makes before it announces one).
    /// </summary>
    public static Event? ScheduleTournament(World world, PromotionId promoId, DateOnly date, out string? reason, string? name = null, CityId? city = null)
    {
        reason = null;
        // Called for the player when he has no house (an observer): say so. (Tests also book AI houses' nights here.)
        if (promoId == world.PlayerPromotion && Booking.PlayerBooking.NoHouse(world, out reason)) return null;
        if (world.Get(promoId) is not { Defunct: false } promo) { reason = "That promotion isn't running."; return null; }
        if (promo.Rules.ShowFormat != ShowFormat.OneNightTournament) { reason = $"{promo.Abbreviation} doesn't run one-night tournaments."; return null; }
        if (OriginsReforms.BoundByCode(world, promo))
        { reason = "Your licence now binds you to the commissions' code: no new one-night brackets. The house fights cards under the code once the bracket on the books is fought."; return null; }
        if (date <= world.CurrentDate) { reason = "Pick a date that hasn't happened yet."; return null; }
        if (world.Events.Values.Any(e => e.Promotion == promoId && e.Date == date && e.Status == EventStatus.Scheduled))
        { reason = "There's already a night on the books that day."; return null; }
        int size = Math.Max(2, promo.Rules.TournamentSize);
        int fit = TournamentService.PreviewField(world, promo, date, null, size).Seeds.Count;   // the field rule the night itself draws by
        if (fit < size)
        { reason = $"You can field {fit} of the {size} men a bracket needs on that date. Sign more, or pick a later night."; return null; }
        if (PlayerVenue(world, promo, date, city) is not { } room)
        { reason = "Every hall you could take the night to is closed by a ban."; return null; }
        int number = world.Events.Values.Count(e => e.Promotion == promoId && e.Status != EventStatus.Cancelled) + 1;
        return NewTournament(world, promo, date, name ?? $"{promo.Name} {number}", room);
    }

    /// <summary>
    /// Fight a tournament night (the tick's match-day pipeline calls this for a <see cref="EventFormat.Tournament"/>
    /// card): draw the field, run the bracket, settle, crown. False when the bracket could not be filled:
    /// the night is cancelled (a black mark on the house) and the news says so.
    /// </summary>
    public static bool RunTournament(World world, Event ev, FightEngine engine)
    {
        if (world.Get(ev.Promotion) is not { } promo) { ev.Status = EventStatus.Cancelled; return false; }
        int size = Math.Max(2, promo.Rules.TournamentSize > 0 ? promo.Rules.TournamentSize : 8);
        var result = new TournamentService(engine).RunOneNight(world, ev, size, promo.Rules.TournamentPrize, wc: null, rules: null);
        if (result is null)
        {
            ev.Status = EventStatus.Cancelled;
            Org.Standing.OnCancelled(world, promo, ev);
            News.Post(world, ev.Date, NewsCategory.Withdrawal, $"{ev.Name} is called off",
                $"{promo.Abbreviation} could not find {size} fit men for the bracket.", promo.Id, aboutEvent: ev.Id);
            return false;
        }
        Crown(world, promo, ev, result.Champion);
        return true;
    }

    /// <summary>
    /// The bracket's winner takes the house's tournament belt (a new reign, or a defence when the holder
    /// won it again). A night without a champion leaves the belt with whoever held it.
    /// </summary>
    private static void Crown(World world, Promotion promo, Event ev, Fighter? champion)
    {
        if (champion is null) return;
        champion.Accolades.Add(new Accolade(ev.Date, $"Won {ev.Name}"));   // every bracket won, a repeat included (the durable record)
        var division = promo.Divisions.FirstOrDefault(d => d.Open && d.Title is not null);
        if (division?.Title is not { } tid || world.Get(tid) is not { } title) return;
        var previous = title.CurrentChampion;
        var reign = title.Lineage.LastOrDefault(r => r.LostOn is null);
        if (previous == champion.Id)
        {
            if (reign is not null) reign.Defenses++;
            News.TitleDefended(world, title, champion, ev.Date);
            return;
        }
        if (reign is not null) reign.LostOn = ev.Date;
        title.CurrentChampion = champion.Id;
        title.Lineage.Add(new TitleReign { Champion = champion.Id, WonOn = ev.Date });
        Contracts.ContractLifecycle.ApplyChampionsClause(champion);
        RankingService.Remove(division, champion.Id);
        if (previous is { } old && world.Get(old) is { } oldChamp)
        {
            bool inField = ev.Card.Any(f => f.RedCorner == old || f.BlueCorner == old);
            if (inField) News.TitleChanged(world, title, champion, oldChamp, ev.Date);
            else
            {
                // The bracket winner IS the champion, but nobody beat the holder: say so honestly.
                if (reign is not null) reign.Vacated = true;
                News.Post(world, ev.Date, NewsCategory.TitleChange, NotInFieldHeadline(champion, ev, title, oldChamp),
                    $"{oldChamp.FullName} held the belt going in and was not in the field; the night's winner takes it.",
                    promo.Id, champion.Id);
            }
            if (oldChamp.Contract?.Promotion == promo.Id && oldChamp.Status == FighterStatus.Active)
                RankingService.EnterAtTop(division, old);
        }
        else News.TitleClaimed(world, title, champion, ev.Date);
        Mail.TitleWon(world, title, champion, ev.Date);
    }

    /// <summary>"X wins the {night} and the tournament championship; {holder} was not in the field".</summary>
    public static string NotInFieldHeadline(Fighter winner, Event ev, Title title, Fighter holder) =>
        $"{winner.FullName} wins the {ev.Name} and the {title.Name}; {holder.FullName} was not in the field";

    // --- Cards of single bouts and challenge nights --------------------------------

    /// <summary>
    /// The card this house would make for <paramref name="date"/> (running order, main event last): its
    /// belt on the line when it can be, then the feuds, then competitive pairings by what its scouts
    /// think of each man. A challenge house matches gym against gym (never two men of one room; the
    /// feuding academies first). Read-only and draw-free; the player can book it as it is.
    /// </summary>
    public static IReadOnlyList<Matchup> SuggestCard(World world, PromotionId promoId, DateOnly date, int? bouts = null)
    {
        if (world.Get(promoId) is not { } promo) return Array.Empty<Matchup>();
        int want = bouts ?? CardBouts(promo);
        var taken = ForwardCalendar.Committed(world);
        var pool = Bookable(world, promo, date, taken);
        var perceived = pool.ToDictionary(f => f.Id, f => ScoutingService.PerceivedAbility(world, promo, f));
        var ordered = pool.OrderByDescending(f => perceived[f.Id]).ThenBy(f => f.Id.Value).ToList();
        int rounds = promo.Rules.Fight?.Rounds ?? promo.Rules.StandardRounds;
        // (House rules that fix the rounds fix them for every bout; the unified code leaves them to the booking: five for a title.)
        int titleRounds = promo.Rules.Fight?.Rounds ?? promo.Rules.TitleRounds;
        bool challenge = promo.Rules.ShowFormat == ShowFormat.GymChallenge;
        var used = new HashSet<FighterId>();
        // Each man's division: the house's one open division, or (once it has split by weight) the band his
        // natural weight puts him in. Men are only matched inside a division.
        var men = promo.Divisions.Where(d => d.Sex == Sex.Male).ToList();
        var divisionOf = ordered.ToDictionary(f => f.Id, f => men.FirstOrDefault(d => d.Matches(f)));

        // Nobody fights the same man twice inside the cooldown (the grudge waits for its rematch).
        var lastMet = LastMeetings(world);
        bool Fresh(Fighter a, Fighter b) =>
            !lastMet.TryGetValue(Pair(a.Id, b.Id), out var met) || date.DayNumber - met.DayNumber > RematchDays;

        // The belts first, when they can be fought for: the heaviest is the main event.
        var titleBouts = new List<Matchup>();
        foreach (var division in challenge ? new List<Division>() : men.Where(d => d.Title is not null).OrderBy(d => d.WeightClass).ToList())
        {
            if (division.Title is not { } tid || world.Get(tid) is not { } title) continue;
            if (titleBouts.Count >= Math.Max(1, want / 2)) break;
            var ranked = division.Rankings.Where(id => !used.Contains(id) && ordered.Any(f => f.Id == id)).ToList();
            var champ = title.CurrentChampion is { } cid ? ordered.FirstOrDefault(f => f.Id == cid && !used.Contains(f.Id)) : null;
            Matchup? titleBout = null;
            if (champ is not null)
            {
                var eligible = ranked.Select(world.Get).OfType<Fighter>()
                    .Where(c => c.Id != champ.Id && Matchmaker.Worthy(c) && !Bonds.WontFight(world, champ.Id, c.Id)).ToList();
                var challenger = eligible.FirstOrDefault(c => Fresh(champ, c)) ?? eligible.FirstOrDefault();
                if (challenger is not null) titleBout = new Matchup(champ.Id, challenger.Id, division.WeightClass, titleRounds, true, tid);
            }
            else if (title.CurrentChampion is null && ranked.Count >= 2 && !Bonds.WontFight(world, ranked[0], ranked[1]))
                titleBout = new Matchup(ranked[0], ranked[1], division.WeightClass, titleRounds, true, tid);
            if (titleBout is { } tb) { used.Add(tb.Red); used.Add(tb.Blue); titleBouts.Add(tb); }
        }

        // Every acceptable pairing among near neighbours in the scouts' order, with its tier.
        var feudingGyms = FeudingGyms(world);
        var candidates = new List<(Fighter A, Fighter B, int Tier, int Gap)>();
        // (Neighbours within a division: two divisions never meet. One open division is the whole order.)
        foreach (var inDivision in ordered.Where(f => divisionOf[f.Id] is not null).GroupBy(f => divisionOf[f.Id]!).Select(g => g.ToList()))
        for (int i = 0; i < inDivision.Count; i++)
            for (int j = i + 1; j < inDivision.Count && j <= i + 4; j++)
            {
                var a = inDivision[i]; var b = inDivision[j];
                if (a.Camp is { } room && room == b.Camp) continue;            // a man never fights his own gym-mate
                if (Bonds.WontFight(world, a.Id, b.Id) || !Fresh(a, b)) continue;
                bool feud = Storylines.RivalryBetween(world, a.Id, b.Id) is { Intensity: >= 40 };
                bool gymWar = a.Camp is { } ca && b.Camp is { } cb && feudingGyms.Contains(GymKey(ca, cb));
                int tier = feud ? 3 : gymWar ? 2 : challenge && a.Camp is not null && b.Camp is not null ? 1 : 0;
                candidates.Add((a, b, tier, Math.Abs(perceived[a.Id] - perceived[b.Id])));
            }
        // Feuds can reach further than four places: a grudge is booked whatever the gap.
        foreach (var rv in world.Rivalries.Where(r => r.Intensity >= 40).OrderByDescending(r => r.Intensity).ThenBy(r => r.A.Value).ThenBy(r => r.B.Value))
        {
            var a = ordered.FirstOrDefault(f => f.Id == rv.A); var b = ordered.FirstOrDefault(f => f.Id == rv.B);
            if (a is null || b is null || candidates.Any(c => (c.A == a && c.B == b) || (c.A == b && c.B == a))) continue;
            if (divisionOf[a.Id] is null || divisionOf[a.Id] != divisionOf[b.Id]) continue;   // a grudge across a weight line waits
            if ((a.Camp is { } room && room == b.Camp) || !Fresh(a, b)) continue;
            candidates.Add((a, b, 3, Math.Abs(perceived[a.Id] - perceived[b.Id])));
        }

        var picked = new List<(Matchup Bout, int Tier, int Strength)>();
        foreach (var c in candidates.OrderByDescending(c => c.Tier).ThenBy(c => c.Gap)
                     .ThenByDescending(c => perceived[c.A.Id] + perceived[c.B.Id]).ThenBy(c => c.A.Id.Value).ThenBy(c => c.B.Id.Value))
        {
            if (picked.Count + titleBouts.Count >= want) break;
            if (used.Contains(c.A.Id) || used.Contains(c.B.Id)) continue;
            used.Add(c.A.Id); used.Add(c.B.Id);
            picked.Add((new Matchup(c.A.Id, c.B.Id, divisionOf[c.A.Id]?.WeightClass ?? Division.OpenClass, rounds, false, null),
                c.Tier, perceived[c.A.Id] + perceived[c.B.Id]));
        }

        // Running order: the weakest first, the hottest and best last; the belts (if any) close the night, the heaviest last.
        var card = picked.OrderBy(p => p.Tier).ThenBy(p => p.Strength).ThenBy(p => p.Bout.Red.Value)
            .Select(p => p.Bout).ToList();
        card.AddRange(titleBouts);
        return card;
    }

    /// <summary>The player books a card of these bouts on his house's night (an ordinary booked event; the house rules fight it).</summary>
    public static Event? ScheduleCard(World world, PromotionId promoId, DateOnly date, IReadOnlyList<Matchup> card, string? name = null,
        CityId? city = null)
    {
        if (world.Get(promoId) is not { Defunct: false } promo || card.Count == 0 || date <= world.CurrentDate) return null;
        // His own room, a city he names (one with a scene, awake or up for grabs), or exile when a ban closed his room.
        if (PlayerVenue(world, promo, date, city) is not { } room) return null;
        int number = world.Events.Values.Count(e => e.Promotion == promoId && e.Status != EventStatus.Cancelled) + 1;
        var ev = new EventService().CreateEvent(world, promoId, name ?? NightName(promo, number), date, card, room.City);
        ev.Venue = room.Name; ev.VenueId = room.Id;
        return ev;
    }

    /// <summary>The house's men who can be booked for the date: signed, active, ready, deal not spent, not committed elsewhere.</summary>
    private static List<Fighter> Bookable(World world, Promotion promo, DateOnly date, HashSet<FighterId> taken) =>
        world.RosterOf(promo.Id).Where(f => !taken.Contains(f.Id) && CanBook(world, promo, f, date)).ToList();

    /// <summary>
    /// The one bookability rule every Origins night uses (cards, challenge nights and the bracket's field):
    /// signed here, active, fit on the night, a fight left on his deal and not in the player's re-sign window.
    /// Commitments to other scheduled cards are the caller's (a set, or <see cref="ForwardCalendar.CommittedTo"/>).
    /// </summary>
    public static bool CanBook(World world, Promotion promo, Fighter f, DateOnly date) =>
        f.Contract?.Promotion == promo.Id && f.Status == FighterStatus.Active && f.IsMatchReady(date)
        && f.Contract is not { FightsRemaining: <= 0 } && !Contracts.DealWindow.OutOfDeal(world, f);

    /// <summary>Two men who have met are not matched again inside this many days.</summary>
    public const int RematchDays = 150;

    private static (int, int) Pair(FighterId a, FighterId b) => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);

    /// <summary>When each pair of men last met (completed bouts; the sport is small enough to scan).</summary>
    private static Dictionary<(int, int), DateOnly> LastMeetings(World world)
    {
        var met = new Dictionary<(int, int), DateOnly>();
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Completed) continue;
            foreach (var f in e.Card)
            {
                var key = Pair(f.RedCorner, f.BlueCorner);
                if (!met.TryGetValue(key, out var d) || e.Date > d) met[key] = e.Date;
            }
        }
        return met;
    }

    private static (int, int) GymKey(CampId a, CampId b) => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);

    /// <summary>Pairs of gyms with bad blood between their members (one live feud is enough for a challenge).</summary>
    private static HashSet<(int, int)> FeudingGyms(World world)
    {
        var set = new HashSet<(int, int)>();
        foreach (var rv in world.Rivalries)
        {
            if (rv.Intensity < 40) continue;
            if (world.Get(rv.A)?.Camp is { } a && world.Get(rv.B)?.Camp is { } b && a != b) set.Add(GymKey(a, b));
        }
        return set;
    }

    /// <summary>Short cards close to the night get topped up from whoever is still free.</summary>
    private static void TopUp(World world, DateOnly today)
    {
        foreach (var ev in world.Events.Values
                     .Where(e => e.Status == EventStatus.Scheduled && e.Format == EventFormat.Card && e.Promotion != world.PlayerPromotion
                                 && e.Date >= today && e.Date.DayNumber - today.DayNumber <= TopUpDays)
                     .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).ToList())
        {
            if (world.Get(ev.Promotion) is not { Defunct: false } promo) continue;
            int need = CardBouts(promo) - ev.Card.Count;
            if (need <= 0) continue;
            // (Ask for one more: a belt the suggestion would put on the line takes a slot we don't use.)
            var extra = SuggestCard(world, promo.Id, ev.Date, need + 1).Where(m => !m.IsTitle).Take(need).ToList();
            if (extra.Count == 0) continue;
            ev.Card.InsertRange(0, extra.Select(m => new Fight
            {
                Id = world.NewFightId(), RedCorner = m.Red, BlueCorner = m.Blue, WeightClass = m.WeightClass,
                ScheduledRounds = m.Rounds, OpenWeight = promo.Rules.Fight?.OpenWeight == true,
            }));
        }
    }
}
