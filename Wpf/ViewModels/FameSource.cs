using System.Runtime.CompilerServices;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Geography;

namespace MmaManager.Wpf.ViewModels;

/// <summary>Why a city knows a name, as far as the public record can tell.</summary>
public enum FameSourceKind
{
    /// <summary>Nothing to say (no fame there, or no fighter).</summary>
    None,
    /// <summary>His home town.</summary>
    Home,
    /// <summary>He has fought in front of this city.</summary>
    Fought,
    /// <summary>He holds or held a belt of a house known here, and has never fought here.</summary>
    Belt,
    /// <summary>He has headlined or co-headlined televised cards of a house known here, and has never fought here.</summary>
    Television,
    /// <summary>Never here, but he has fought elsewhere in this region (word travels around a region).</summary>
    Nearby,
    /// <summary>A medal at the world championships or the Games, in his home region.</summary>
    Medal,
    /// <summary>Known a little, and the record cannot say how.</summary>
    Unseen,
}

/// <summary>One city's source note: the kind, a short phrase, and the glyph name (an <c>Icon.*</c> key, or "": home
/// has none, since every place that shows a city already marks the home town).</summary>
/// <param name="Short">The source alone, for a summary line ("as the VMMA champion", "from VMMA on TV"); "" for home and fought.</param>
public readonly record struct FameSourceNote(FameSourceKind Kind, string Text, string Glyph, string Short = "")
{
    public static readonly FameSourceNote Empty = new(FameSourceKind.None, "", "");
}

/// <summary>
/// The ONE place the app explains where a fighter's fame in a city came from. The sim stores only the number
/// (<see cref="Fighter.CityReputation"/>), not its source, so this reads the public record honestly and never claims
/// more than it can know:
/// <list type="bullet">
/// <item>his home town (<see cref="Fighter.HomeCity"/>);</item>
/// <item>"fought here N times": his bouts on file whose card was staged in this city (<see cref="WorldMap.CityOf"/>);</item>
/// <item>never fought here: a belt of a house known here (the sim's belt lift reaches every city where the house is at
/// least <see cref="BroadcastFame.BeltFloor"/>), else main-card bouts for a house known here (television), plus or instead
/// "word from his fights nearby" when he has fought elsewhere in the region (results echo round a region), else a medal in
/// his home region; and when none of those fits, "heard of, never seen live".</item>
/// </list>
/// Television and the belt cannot be told apart exactly (there is no stored marker), so the belt is named only where
/// it could have reached and the screen only where a screen could have; nothing claims a count it cannot see.
/// Public facts only: no ability or potential is read anywhere. Deterministic (no randomness, ties broken by id) and
/// cheap enough for hover: the fighter's facts are gathered once per world date and kept.
/// </summary>
public static class FameSource
{
    /// <summary>The brand floor below which no screen carries a card into a city (the lowest floor of any screen kind).</summary>
    private const int TvBrandFloor = 5;

    /// <summary>One fighter's public record, as the notes need it.</summary>
    private sealed class Facts
    {
        public int Bouts;                                           // BoutHistory.Count when gathered (a new bout = stale)
        public readonly Dictionary<CityId, int> FoughtIn = new();
        public readonly Dictionary<Region, (CityId City, int Count)> BestInRegion = new();
        public readonly HashSet<PromotionId> MainCardFor = new();   // houses whose main card he has been on
        public readonly HashSet<PromotionId> ChampOf = new();       // houses whose belt he holds now
        public readonly HashSet<PromotionId> FormerChampOf = new(); // houses whose belt he has held
        public bool Medalled;
    }

    private sealed class WorldCache
    {
        public DateOnly Date;
        public int Events;
        public Dictionary<FightId, (Fight Fight, Event Event)>? Index;
        public readonly Dictionary<FighterId, Facts> Fighters = new();
    }

    private static readonly ConditionalWeakTable<World, WorldCache> Caches = new();

    /// <summary>Why <paramref name="city"/> knows <paramref name="f"/> (Empty when it does not know him at all).</summary>
    public static FameSourceNote For(World w, Fighter f, City city) =>
        Reputation.FighterInCity(f, city.Id) <= 0 ? FameSourceNote.Empty : Explain(w, f, city, FactsOf(w, f));

    /// <summary>
    /// One line on the cities that know the name without ever having seen him fight, by what carried it there:
    /// "Never seen live in 37 cities: known as the VMMA champion in 36 · seen on TV in 1". "" when every city that knows him has
    /// seen him (or is home). <paramref name="known"/> is <see cref="Fame.KnownCities(World, Fighter)"/>.
    /// </summary>
    public static string RemoteLine(World w, Fighter f, List<(City City, int Fame)> known)
    {
        // Group by the source itself (a belt of one house and a belt of another are two sources), kind order then most cities.
        var groups = new Dictionary<(FameSourceKind Kind, string Short), int>();
        foreach (var (city, _) in known)
        {
            var note = For(w, f, city);
            if (note.Kind is FameSourceKind.None or FameSourceKind.Home or FameSourceKind.Fought) continue;
            groups[(note.Kind, note.Short)] = groups.GetValueOrDefault((note.Kind, note.Short)) + 1;
        }
        int total = groups.Values.Sum();
        if (total == 0) return "";
        var parts = groups.OrderBy(g => g.Key.Kind).ThenByDescending(g => g.Value).ThenBy(g => g.Key.Short, StringComparer.Ordinal)
            .Select(g => $"{g.Key.Short} in {g.Value}");
        return $"Never seen live in {(total == 1 ? "1 city" : $"{total} cities")}: {string.Join(" · ", parts)}";
    }

    private static FameSourceNote Explain(World w, Fighter f, City city, Facts facts)
    {
        string his = f.Sex == Sex.Female ? "her" : "his";
        int here = facts.FoughtIn.GetValueOrDefault(city.Id);

        if (f.HomeCity == city.Id)
            return new(FameSourceKind.Home, here > 0 ? $"Home town · fought here {Times(here)}" : "Home town", "");   // the home mark is already beside it
        if (here > 0)
            return new(FameSourceKind.Fought, $"Fought here {Times(here)}", "Cage");

        // Never seen here. What could have carried the name this far?
        string? nearby = facts.BestInRegion.TryGetValue(city.Region, out var best) && best.City != city.Id
                         && w.Get(best.City) is { } near ? near.Name : null;
        string Nearby(string lead) => nearby is null ? lead : $"{lead}; fought nearby in {nearby}";

        if (!f.LocalTalent)   // the sim's broadcast and belt lifts pass local talent by
        {
            if (BestHouse(w, facts.ChampOf, city, BroadcastFame.BeltFloor) is { } champ)
                return new(FameSourceKind.Belt, Nearby($"Known as the {champ.Abbreviation} champion"), "Crown", $"known as the {champ.Abbreviation} champion");
            if (BestHouse(w, facts.FormerChampOf, city, BroadcastFame.BeltFloor) is { } former)
                return new(FameSourceKind.Belt, Nearby($"Known as a former {former.Abbreviation} champion"), "Crown", $"known as a former {former.Abbreviation} champion");
            if (BestHouse(w, facts.MainCardFor, city, TvBrandFloor) is { } tv)
            {
                // Name the house only when he has headlined for just one house known here; otherwise it is a guess.
                string screen = HousesKnown(w, facts.MainCardFor, city, TvBrandFloor) == 1 ? $"{tv.Abbreviation} on TV" : "TV";
                return new(FameSourceKind.Television,
                    nearby is null ? $"Known from {screen}" : $"Known from {screen} and {his} fights in {nearby}", "Broadcast",
                    $"seen on {(screen == "TV" ? "TV" : $"{tv.Abbreviation} TV")}");
            }
        }
        if (nearby is not null)
            return new(FameSourceKind.Nearby, $"Word from {his} fights in {nearby}", "Cage", $"word of {his} fights nearby");
        if (facts.Medalled && city.Region == f.Nationality)
            return new(FameSourceKind.Medal, $"Known for {his} medals", "Star", $"known for {his} medals");
        return new(FameSourceKind.Unseen, "Heard of, never seen live", "", "just heard of");
    }

    /// <summary>The house in the set best known in this city, if it is known there at least <paramref name="floor"/> (ties by id).</summary>
    private static Promotion? BestHouse(World w, HashSet<PromotionId> houses, City city, int floor)
    {
        Promotion? pick = null;
        int pickBrand = -1;
        foreach (var id in houses.OrderBy(h => h.Value))
        {
            if (w.Get(id) is not { } p) continue;
            int brand = Reputation.OrgInCity(p, city.Id);
            if (brand >= floor && brand > pickBrand) { pick = p; pickBrand = brand; }
        }
        return pick;
    }

    private static int HousesKnown(World w, HashSet<PromotionId> houses, City city, int floor) =>
        houses.Count(id => w.Get(id) is { } p && Reputation.OrgInCity(p, city.Id) >= floor);

    private static string Times(int n) => n switch { 1 => "once", 2 => "twice", _ => $"{n} times" };

    // --- Gathering the record (once per fighter per world date) ------------------------------

    private static Facts FactsOf(World w, Fighter f)
    {
        var cache = Caches.GetValue(w, _ => new WorldCache());
        if (cache.Date != w.CurrentDate || cache.Events != w.Events.Count)
        {
            cache.Date = w.CurrentDate;
            cache.Events = w.Events.Count;
            cache.Index = null;
            cache.Fighters.Clear();
        }
        if (cache.Fighters.TryGetValue(f.Id, out var hit) && hit.Bouts == f.BoutHistory.Count) return hit;

        var facts = new Facts { Bouts = f.BoutHistory.Count };
        var index = cache.Index ??= BuildIndex(w);
        bool rebuilt = false;
        foreach (var id in f.BoutHistory)
        {
            if (!index.TryGetValue(id, out var entry))
            {
                // A card that completed since the index was built (same date, same event count): rebuild once.
                if (rebuilt) continue;
                index = cache.Index = BuildIndex(w);
                rebuilt = true;
                if (!index.TryGetValue(id, out entry)) continue;
            }
            var (fight, ev) = entry;
            if (fight.Outcome is null) continue;
            if (WorldMap.CityOf(w, ev) is { } host)
                facts.FoughtIn[host] = facts.FoughtIn.GetValueOrDefault(host) + 1;
            if (!ev.IsPrelim(fight)) facts.MainCardFor.Add(ev.Promotion);
        }
        // The city he fought in most in each region (ties by city id): the "nearby" a remote city heard about.
        foreach (var (cityId, n) in facts.FoughtIn.OrderBy(kv => kv.Key.Value))
            if (w.Get(cityId) is { } c && (!facts.BestInRegion.TryGetValue(c.Region, out var cur) || n > cur.Count))
                facts.BestInRegion[c.Region] = (cityId, n);

        foreach (var t in w.Titles.Values)
        {
            if (t.CurrentChampion == f.Id) facts.ChampOf.Add(t.Promotion);
            else if (t.Lineage.Any(r => r.Champion == f.Id)) facts.FormerChampOf.Add(t.Promotion);
        }
        facts.FormerChampOf.ExceptWith(facts.ChampOf);
        facts.Medalled = f.Accolades.Any(a => a.Title.EndsWith("— gold") || a.Title.EndsWith("— silver") || a.Title.EndsWith("— bronze"));

        cache.Fighters[f.Id] = facts;
        return facts;
    }

    private static Dictionary<FightId, (Fight Fight, Event Event)> BuildIndex(World w)
    {
        var map = new Dictionary<FightId, (Fight, Event)>();
        foreach (var e in w.Events.Values)
        {
            if (e.Status != EventStatus.Completed) continue;
            foreach (var fight in e.Card) if (fight.Outcome is not null) map[fight.Id] = (fight, e);
        }
        return map;
    }
}
