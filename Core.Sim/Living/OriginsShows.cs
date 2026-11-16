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
        Event ev;
        if (promo.Rules.ShowFormat == ShowFormat.OneNightTournament)
        {
            int size = Math.Max(2, promo.Rules.TournamentSize);
            // Only a house that could field the bracket today puts one on the books.
            if (Bookable(world, promo, when, ForwardCalendar.Committed(world)).Count < size) return null;
            ev = NewTournament(world, promo, when, $"{promo.Name} {number}");
        }
        else
        {
            var card = SuggestCard(world, promo.Id, when);
            if (card.Count < 2) return null;
            ev = events.CreateEvent(world, promo.Id, NightName(promo, number), when, card, promo.HomeCity);
            if (HomeVenue(world, promo) is { } room) { ev.Venue = room.Name; ev.VenueId = room.Id; }
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
        return $"{time}, {(r.Judges ? "judges at the limit" : "no judges")}, no weight classes";
    }

    /// <summary>The house's own room (its authored home venue when it has one, else the best fit in its home town).</summary>
    public static Venue? HomeVenue(World world, Promotion promo)
    {
        var spec = Generation.Origins.OriginsPromotions.All.FirstOrDefault(s => s.Abbreviation == promo.Abbreviation);
        if (spec is not null && world.Venues.Values.Where(v => v.Name == spec.HomeVenue && v.City == promo.HomeCity)
                .OrderBy(v => v.Id.Value).FirstOrDefault() is { } authored)
            return authored;
        return promo.HomeCity is { } hc ? Geography.WorldMap.PickVenueIn(world, promo, hc) : Geography.WorldMap.PickVenue(world, promo);
    }

    // --- Tournaments -------------------------------------------------------------

    /// <summary>A one-night bracket on the books: the card is empty until the night draws it.</summary>
    private static Event NewTournament(World world, Promotion promo, DateOnly date, string name)
    {
        var venue = HomeVenue(world, promo);
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
    public static Event? ScheduleTournament(World world, PromotionId promoId, DateOnly date, string? name = null) =>
        ScheduleTournament(world, promoId, date, out _, name);

    /// <summary>
    /// As above, with the reason a night was refused, in words the screen can show: not a bracket house,
    /// a date not ahead, a night already on that date, or not enough men to fill the bracket (the same
    /// check the AI makes before it announces one).
    /// </summary>
    public static Event? ScheduleTournament(World world, PromotionId promoId, DateOnly date, out string? reason, string? name = null)
    {
        reason = null;
        if (world.Get(promoId) is not { Defunct: false } promo) { reason = "That promotion isn't running."; return null; }
        if (promo.Rules.ShowFormat != ShowFormat.OneNightTournament) { reason = $"{promo.Abbreviation} doesn't run one-night tournaments."; return null; }
        if (date <= world.CurrentDate) { reason = "Pick a date that hasn't happened yet."; return null; }
        if (world.Events.Values.Any(e => e.Promotion == promoId && e.Date == date && e.Status == EventStatus.Scheduled))
        { reason = "There's already a night on the books that day."; return null; }
        int size = Math.Max(2, promo.Rules.TournamentSize);
        int fit = Bookable(world, promo, date, ForwardCalendar.Committed(world)).Count;
        if (fit < size)
        { reason = $"You can field {fit} of the {size} men a bracket needs on that date. Sign more, or pick a later night."; return null; }
        int number = world.Events.Values.Count(e => e.Promotion == promoId && e.Status != EventStatus.Cancelled) + 1;
        return NewTournament(world, promo, date, name ?? $"{promo.Name} {number}");
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
        var division = promo.Divisions.FirstOrDefault(d => d.Open);
        int rounds = promo.Rules.Fight?.Rounds ?? promo.Rules.StandardRounds;
        bool challenge = promo.Rules.ShowFormat == ShowFormat.GymChallenge;
        var used = new HashSet<FighterId>();

        // Nobody fights the same man twice inside the cooldown (the grudge waits for its rematch).
        var lastMet = LastMeetings(world);
        bool Fresh(Fighter a, Fighter b) =>
            !lastMet.TryGetValue(Pair(a.Id, b.Id), out var met) || date.DayNumber - met.DayNumber > RematchDays;

        // The belt first, when one can be fought for.
        Matchup? titleBout = null;
        if (!challenge && division?.Title is { } tid && world.Get(tid) is { } title)
        {
            var ranked = division.Rankings.Where(id => ordered.Any(f => f.Id == id)).ToList();
            var champ = title.CurrentChampion is { } cid ? ordered.FirstOrDefault(f => f.Id == cid) : null;
            if (champ is not null)
            {
                var eligible = ranked.Select(world.Get).OfType<Fighter>()
                    .Where(c => c.Id != champ.Id && Matchmaker.Worthy(c) && !Bonds.WontFight(world, champ.Id, c.Id)).ToList();
                var challenger = eligible.FirstOrDefault(c => Fresh(champ, c)) ?? eligible.FirstOrDefault();
                if (challenger is not null) titleBout = new Matchup(champ.Id, challenger.Id, division.WeightClass, rounds, true, tid);
            }
            else if (title.CurrentChampion is null && ranked.Count >= 2 && !Bonds.WontFight(world, ranked[0], ranked[1]))
                titleBout = new Matchup(ranked[0], ranked[1], division.WeightClass, rounds, true, tid);
            if (titleBout is { } tb) { used.Add(tb.Red); used.Add(tb.Blue); }
        }

        // Every acceptable pairing among near neighbours in the scouts' order, with its tier.
        var feudingGyms = FeudingGyms(world);
        var candidates = new List<(Fighter A, Fighter B, int Tier, int Gap)>();
        for (int i = 0; i < ordered.Count; i++)
            for (int j = i + 1; j < ordered.Count && j <= i + 4; j++)
            {
                var a = ordered[i]; var b = ordered[j];
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
            if ((a.Camp is { } room && room == b.Camp) || !Fresh(a, b)) continue;
            candidates.Add((a, b, 3, Math.Abs(perceived[a.Id] - perceived[b.Id])));
        }

        var picked = new List<(Matchup Bout, int Tier, int Strength)>();
        foreach (var c in candidates.OrderByDescending(c => c.Tier).ThenBy(c => c.Gap)
                     .ThenByDescending(c => perceived[c.A.Id] + perceived[c.B.Id]).ThenBy(c => c.A.Id.Value).ThenBy(c => c.B.Id.Value))
        {
            if (picked.Count + (titleBout is null ? 0 : 1) >= want) break;
            if (used.Contains(c.A.Id) || used.Contains(c.B.Id)) continue;
            used.Add(c.A.Id); used.Add(c.B.Id);
            picked.Add((new Matchup(c.A.Id, c.B.Id, division?.WeightClass ?? Division.OpenClass, rounds, false, null),
                c.Tier, perceived[c.A.Id] + perceived[c.B.Id]));
        }

        // Running order: the weakest first, the hottest and best last; the belt (if any) is the main event.
        var card = picked.OrderBy(p => p.Tier).ThenBy(p => p.Strength).ThenBy(p => p.Bout.Red.Value)
            .Select(p => p.Bout).ToList();
        if (titleBout is { } main) card.Add(main);
        return card;
    }

    /// <summary>The player books a card of these bouts on his house's night (an ordinary booked event; the house rules fight it).</summary>
    public static Event? ScheduleCard(World world, PromotionId promoId, DateOnly date, IReadOnlyList<Matchup> card, string? name = null)
    {
        if (world.Get(promoId) is not { Defunct: false } promo || card.Count == 0 || date <= world.CurrentDate) return null;
        int number = world.Events.Values.Count(e => e.Promotion == promoId && e.Status != EventStatus.Cancelled) + 1;
        var ev = new EventService().CreateEvent(world, promoId, name ?? NightName(promo, number), date, card, promo.HomeCity);
        if (HomeVenue(world, promo) is { } room) { ev.Venue = room.Name; ev.VenueId = room.Id; }
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
