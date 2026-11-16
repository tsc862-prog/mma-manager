using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation.Origins;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// Era 2 of an Origins world, the Backlash, as the world feels it (the chapters themselves are fired by
/// <see cref="OriginsTimeline"/>):
/// <list type="bullet">
/// <item>the press turns: interest in the sport dips in the home countries of the founding houses;</item>
/// <item>bans: regions of the United States close their halls (<see cref="SceneBan"/>). One region always
/// stays open. A house whose home room is closed takes its nights elsewhere
/// (<see cref="OriginsShows.NightVenue"/>); cards already announced in a closed region are moved or called off;</item>
/// <item>the lean years: two years in which American interest sags and only local backers stay;</item>
/// <item>licences: a house that has adopted rounds, judges and gloves can win a commission's licence for a
/// closed region (<see cref="Sanction"/>), which lifts the ban there; after the first, commissions follow
/// on their own. Bans also lapse.</item>
/// </list>
/// Lost pay-per-view carriers have nothing to take away yet: no house holds a carrier deal before the era
/// media ladder (a later phase), so <see cref="CarriersDark"/> is recorded for that phase to read.
/// Keyed rolls only (never a shared stream); every ordering ends on an id.
/// </summary>
public static class OriginsBacklash
{
    /// <summary>The countries the press campaign and the bans hit (the bans are American; the dip also reaches Brazil).</summary>
    public static readonly IReadOnlySet<string> Countries = new HashSet<string>(StringComparer.Ordinal) { "USA" };

    private static readonly string[] PressCountries = { "USA", "BRA" };

    /// <summary>How far interest falls in the home countries when the press turns.</summary>
    public const int PressDip = 6;

    /// <summary>How long the lean years last.</summary>
    public const int LeanMonths = 24;

    /// <summary>How far below its baseline a city's interest can sink in the lean years.</summary>
    public const int LeanFloor = 12;

    /// <summary>A rules-following house's monthly chance of winning a licence for the closed region it lives in.</summary>
    public const double LicenceChance = 0.10;

    /// <summary>After the first licence, a closed region's monthly chance that its commission says yes on its own.</summary>
    public const double CommissionFollowChance = 0.03;

    /// <summary>After the first licence, an open region's monthly chance of a licence where a rules-following house lives.</summary>
    public const double LicenceSpreadChance = 0.015;

    private const ulong BanSalt = 0x0219_BA45_0001UL, LicenceSalt = 0x0219_11CE_0002UL;

    // ------------------------------------------------------------------ queries

    /// <summary>Are the lean years on (the two years after they begin)?</summary>
    public static bool LeanOn(World world, DateOnly date) =>
        world.Era.Chapter("lean-years") is { } r && date >= r.FiredOn && date < r.FiredOn.AddMonths(LeanMonths);

    /// <summary>
    /// The carriers have dropped the sport and this house has no licence: no pay-per-view carrier will take
    /// its cards. Recorded for the era media ladder (a later phase); nothing sells pay-per-view in an Origins
    /// world yet.
    /// </summary>
    public static bool CarriersDark(World world, Promotion promo) =>
        world.Era.HasFired("carriers-drop") && !world.Era.Sanctions.Any(s => s.Promotion == promo.Id);

    /// <summary>Does this house fight under the rules a commission will license (rounds, judges and gloves)?</summary>
    public static bool FollowsRules(Promotion p) => OriginsReforms.HasRounds(p) && OriginsReforms.HasGloves(p);

    /// <summary>"the West of the United States", "Brazil": a region in words.</summary>
    public static string RegionName(World world, string countryCode, int? area)
    {
        var country = Geography.WorldMap.CountryByCode(world, countryCode);
        string name = country?.Name ?? countryCode;
        if (name == "United States") name = "the United States";
        return area is { } a && country is not null && a >= 0 && a < country.Areas.Count
            ? $"the {country.Areas[a]} of {name}"
            : name;
    }

    // ------------------------------------------------------------------ the chapters' effects

    /// <summary>The newspapers turn: interest in the sport drops in its first homes.</summary>
    public static void PressTurns(World world, DateOnly date)
    {
        foreach (var code in PressCountries)
        {
            if (Geography.WorldMap.CountryByCode(world, code) is not { } country) continue;
            country.MmaInterest = Math.Max(5, country.MmaInterest - PressDip);
            foreach (var c in world.Cities.Values.Where(c => c.Country == country.Id).OrderBy(c => c.Id.Value))
                c.MmaInterest = Math.Max(5, c.MmaInterest - PressDip);
        }
    }

    /// <summary>
    /// A wave of bans across the United States. The first wave closes every region a house calls home and
    /// about half of the rest; the second (when the carriers go dark) closes one more while two are open.
    /// One region always stays open (a keyed pick among those no house calls home): somewhere the sport
    /// can still run. Bans last three to five years unless a commission lifts them first.
    /// </summary>
    public static void BanWave(World world, DateOnly date, bool first)
    {
        if (Geography.WorldMap.CountryByCode(world, "USA") is not { } usa || usa.Areas.Count == 0) return;
        int areas = usa.Areas.Count;
        var homes = world.Promotions.Values.Where(p => !p.Defunct && p.HomeCity is { } hc && world.Get(hc) is { } c && c.Country == usa.Id)
            .Select(p => world.Get(p.HomeCity!.Value)!.Area).OfType<int>().ToHashSet();
        var bannedNow = Enumerable.Range(0, areas).Where(a => world.Era.Bans.Any(b => b.Country == "USA" && b.Area == a && b.ActiveOn(date))).ToHashSet();

        // The open region: the same one for every wave (a keyed pick among the regions no house calls home).
        var refuges = Enumerable.Range(0, areas).Where(a => !homes.Contains(a)).ToList();
        if (refuges.Count == 0) refuges = Enumerable.Range(0, areas).ToList();
        int refuge = refuges[(int)(OriginsTimeline.Roll(world, BanSalt, 0xAE) * refuges.Count)];

        var close = new List<int>();
        if (first)
        {
            foreach (int a in Enumerable.Range(0, areas))
            {
                if (a == refuge || bannedNow.Contains(a)) continue;
                if (homes.Contains(a) || OriginsTimeline.Roll(world, BanSalt, (ulong)a, 1) < 0.5) close.Add(a);
            }
        }
        else
        {
            var open = Enumerable.Range(0, areas).Where(a => a != refuge && !bannedNow.Contains(a)).ToList();
            if (open.Count > 0) close.Add(open[(int)(OriginsTimeline.Roll(world, BanSalt, 2) * open.Count)]);
        }

        foreach (int a in close)
        {
            int months = (first ? 36 : 30) + (int)(OriginsTimeline.Roll(world, BanSalt, (ulong)a, first ? 3UL : 4UL) * 25);
            // (In force from tomorrow: a card fought tonight, before the news broke, was fought before the ban.)
            world.Era.Bans.Add(new SceneBan { Country = "USA", Area = a, Since = date.AddDays(1), Until = date.AddMonths(months) });
            string region = RegionName(world, "USA", a);
            var hit = world.Promotions.Values.Where(p => !p.Defunct && p.HomeCity is { } hc && world.Get(hc)?.Area == a
                    && world.Get(hc)!.Country == usa.Id).OrderBy(p => p.Id.Value).ToList();
            News.Post(world, date, NewsCategory.General, $"Banned in {region}: the halls close to the sport",
                hit.Count == 0
                    ? $"Legislators in {region} have closed every hall to the new fights. No promotion calls the region home, but nobody can run a card there now."
                    : $"Legislators in {region} have closed every hall to the new fights. {string.Join(" and ", hit.Select(p => p.Name))} must take {(hit.Count == 1 ? "its" : "their")} nights somewhere else until the ban lapses or a commission licenses the sport.",
                hit.FirstOrDefault()?.Id ?? default);
            if (hit.Any(p => p.Id == world.PlayerPromotion))
                Mail.Post(world, date, MailCategory.BoardNote, Mail.Owner(world), $"We're banned at home",
                    $"{Cap(region)} has closed its halls to us. We can still run nights: in a region that hasn't banned the sport, or abroad where we're welcome. "
                    + "A house that fights in rounds, with judges and gloves, can apply to a commission for a licence that lifts the ban.");
        }
        RelocateClosedCards(world, date);
    }

    /// <summary>The lean years begin: the bigger backers leave the American houses; only the local ones stay.</summary>
    public static void LeanYearsBegin(World world, DateOnly date)
    {
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value))
        {
            if (!OriginsTimeline.LeanFor(world, p, date)) continue;
            var gone = p.SponsorDeals.Where(d => Finance.Sponsorships.ArchetypeOf(d.Partner) is { } a && a != Capability.SponsorLocal).ToList();
            p.Offers.RemoveAll(o => o.Media is null && Finance.Sponsorships.ArchetypeOf(o.Partner) is { } a && a != Capability.SponsorLocal);
            if (gone.Count == 0) continue;
            foreach (var d in gone) p.SponsorDeals.Remove(d);
            News.Post(world, date, NewsCategory.Finance, $"{string.Join(" and ", gone.Select(d => d.Partner))} pull{(gone.Count == 1 ? "s" : "")} out of {p.Name}",
                "With the sport under fire, the bigger backers want their names off the canvas. Only the hometown sponsors are staying.", p.Id);
        }
    }

    // ------------------------------------------------------------------ the month

    /// <summary>The Backlash's month: interest sags or recovers, bans lapse, commissions consider licences.</summary>
    public static void RunMonthly(World world, DateOnly date)
    {
        Interest(world, date);
        Lapses(world, date);
        Licences(world, date);
    }

    /// <summary>In the lean years American interest sinks a point a month (to a floor); otherwise a dip recovers a point a month.</summary>
    private static void Interest(World world, DateOnly date)
    {
        bool lean = LeanOn(world, date);
        foreach (var country in world.Countries.Values.OrderBy(c => c.Id.Value))
        {
            bool hit = lean && Countries.Contains(country.Code);
            country.MmaInterest = Step(country.MmaInterest, country.MmaInterestBase, hit);
        }
        foreach (var c in world.Cities.Values.OrderBy(c => c.Id.Value))
        {
            bool hit = lean && world.Get(c.Country) is { } k && Countries.Contains(k.Code);
            c.MmaInterest = Step(c.MmaInterest, c.MmaInterestBase, hit);
        }

        static int Step(int now, int baseline, bool sinking) =>
            sinking ? (now > baseline - LeanFloor ? now - 1 : now)
            : now < baseline ? now + 1 : now;
    }

    private static void Lapses(World world, DateOnly date)
    {
        var monthAgo = date.AddMonths(-1);
        foreach (var ban in world.Era.Bans)
            if (ban.LiftedOn is null && ban.Until > monthAgo && ban.Until <= date)
                News.Post(world, date, NewsCategory.General, $"The ban lapses in {RegionName(world, ban.Country, ban.Area)}",
                    "The law that closed the halls has run out and nobody has renewed it. The sport may come back.", default);
    }

    private static void Licences(World world, DateOnly date)
    {
        const string chapter = "first-sanctioned-state";
        if (!OriginsTimeline.IsOpen(world, chapter, date)) return;
        int month = OriginsTimeline.Month(world, date);

        // A house that fights by the rules applies for the closed region it lives in.
        for (int i = 0; i < world.Era.Bans.Count; i++)
        {
            var ban = world.Era.Bans[i];
            if (!ban.ActiveOn(date)) continue;
            foreach (var p in world.Promotions.Values.Where(p => !p.Defunct && FollowsRules(p)).OrderBy(p => p.Id.Value))
            {
                if (p.HomeCity is not { } hc || world.Get(hc) is not { } home || world.Get(home.Country)?.Code != ban.Country) continue;
                if (ban.Area is { } a && home.Area != a) continue;
                if (OriginsTimeline.Roll(world, LicenceSalt, (ulong)p.Id.Value, (ulong)i, (ulong)month) >= LicenceChance) continue;
                Grant(world, ban.Country, ban.Area, p, date);
                break;
            }
        }
        if (!world.Era.HasFired(chapter)) return;

        // After the first licence the commissions follow: closed regions on their own...
        for (int i = 0; i < world.Era.Bans.Count; i++)
        {
            var ban = world.Era.Bans[i];
            if (ban.ActiveOn(date) && OriginsTimeline.Roll(world, LicenceSalt, 0xC0, (ulong)i, (ulong)month) < CommissionFollowChance)
                Grant(world, ban.Country, ban.Area, null, date);
        }
        // ...and open ones where a house that fights by the rules lives.
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct && FollowsRules(p)).OrderBy(p => p.Id.Value))
        {
            if (p.HomeCity is not { } hc || world.Get(hc) is not { } home || world.Get(home.Country) is not { } country) continue;
            int? area = country.Areas.Count > 0 ? home.Area : null;
            if (world.Era.Sanctions.Any(s => s.Country == country.Code && s.Area == area)) continue;
            if (OriginsTimeline.Roll(world, LicenceSalt, 0x5B, (ulong)p.Id.Value, (ulong)month) < LicenceSpreadChance)
                Grant(world, country.Code, area, p, date);
        }
    }

    /// <summary>A first licence nobody won: the commission of the first closed region moves on its own. The city it opened, or null.</summary>
    internal static CityId? CommissionMoves(World world, DateOnly date)
    {
        var ban = world.Era.Bans.FirstOrDefault(b => b.ActiveOn(date)) ?? world.Era.Bans.FirstOrDefault();
        if (ban is null) return null;
        return Grant(world, ban.Country, ban.Area, null, date, fireChapter: false);
    }

    /// <summary>A house founded with its paperwork done: its home region is licensed the day it opens (no chapter: that one was somebody else's).</summary>
    internal static void LicenceAtFounding(World world, Promotion p, DateOnly date)
    {
        if (p.HomeCity is not { } hc || world.Get(hc) is not { } home || world.Get(home.Country) is not { } country) return;
        Grant(world, country.Code, country.Areas.Count > 0 ? home.Area : null, p, date, fireChapter: false);
    }

    /// <summary>A commission licenses a region: its ban lifts and the region counts as sanctioned. Fires the first licence's chapter when it is live.</summary>
    private static CityId? Grant(World world, string country, int? area, Promotion? by, DateOnly date, bool fireChapter = true)
    {
        if (world.Era.Sanctions.Any(s => s.Country == country && s.Area == area)) return null;
        foreach (var ban in world.Era.Bans.Where(b => b.Country == country && b.Area == area && b.ActiveOn(date)))
            ban.LiftedOn = date;
        world.Era.Sanctions.Add(new Sanction { Country = country, Area = area, On = date, Promotion = by?.Id });

        var k = Geography.WorldMap.CountryByCode(world, country);
        var city = by?.HomeCity is { } hc && world.Get(hc) is { } home && (area is null || home.Area == area) ? home
            : world.Cities.Values.Where(c => k is not null && c.Country == k.Id && (area is null || c.Area == area))
                .OrderByDescending(c => c.Market).ThenBy(c => c.Id.Value).FirstOrDefault();
        string region = RegionName(world, country, area);
        News.Post(world, date, NewsCategory.General,
            by is null ? $"A commission licenses the sport in {region}" : $"{by.Name} wins a licence in {region}",
            by is null
                ? "Rounds, judges, gloves and a doctor at ringside: the commission has written the rules and opened the halls to any card that keeps them."
                : $"Rounds, judges, gloves and a doctor at ringside: {by.Name} kept every condition, and the commission has opened the halls to it and to any card that does the same.",
            by?.Id ?? default);
        if (fireChapter && by is not null)
            OriginsTimeline.FireNow(world, "first-sanctioned-state", new ChapterDoer(by.Id, City: city?.Id), date);
        return city?.Id;
    }

    /// <summary>Announced cards in a region that has just closed move with their house, or are called off when there is nowhere to go.</summary>
    private static void RelocateClosedCards(World world, DateOnly date)
    {
        foreach (var ev in world.Events.Values
                     .Where(e => e.Status == EventStatus.Scheduled && e.Date >= date)
                     .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).ToList())
        {
            if (OriginsTimeline.CityOf(world, ev) is not { } city || !Era.Banned(world, city, ev.Date)) continue;
            if (world.Get(ev.Promotion) is not { } promo) continue;
            var room = OriginsShows.ExileVenue(world, promo, ev.Date);
            if (room is not null && world.Get(room.City) is { } to)
            {
                ev.Venue = room.Name; ev.VenueId = room.Id; ev.Region = to.Region;
                News.Post(world, date, NewsCategory.General, $"{ev.Name} moves to {to.Name}",
                    $"The ban closed the hall {promo.Abbreviation} had booked. The night goes on in {to.Name} instead.", promo.Id, aboutEvent: ev.Id);
            }
            else
            {
                ev.Status = EventStatus.Cancelled;
                News.Post(world, date, NewsCategory.Withdrawal, $"{ev.Name} is called off",
                    $"The ban closed the hall {promo.Abbreviation} had booked, and there is nowhere left to take the night.", promo.Id, aboutEvent: ev.Id);
            }
        }
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
