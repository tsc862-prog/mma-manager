using MmaManager.Core.Model;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>What happened to an owner's house, as the news told it.</summary>
public enum OwnerMoment { Signs, Crowns, Left, Retired, Outbid }

/// <summary>
/// The owners speak. Once a week it reads the week's news and, when a designed owner's house signed a name, crowned
/// a champion, lost a star or was outbid, a share of those moments gets a line (new deals only: a renewal is not "signing a name") from the owner, in their own agenda's
/// voice, posted as news. Text only: it reads headlines the sim already wrote and writes a General item; no system
/// reads it, nothing it does changes an outcome. The roll is a hash of the seed and the item (no draw from any
/// stream): the same world says the same things.
/// </summary>
public static class OwnerVoices
{
    /// <summary>Out of 100: how often each moment gets a quote. Signings are common, so they are rationed.</summary>
    public static int Share(OwnerMoment m) => m switch
    {
        OwnerMoment.Signs => 35,
        OwnerMoment.Crowns => 70,
        OwnerMoment.Left => 70,
        OwnerMoment.Retired => 70,
        _ => 70,
    };

    public static void RunWeekly(World world, DateOnly date)
    {
        // Snapshot first: posting adds to the feed.
        var week = world.News.Where(n => n.Date > date.AddDays(-7) && n.Date <= date && n.AboutFighter is not null).ToList();
        if (week.Count == 0) return;
        foreach (var n in week)
            foreach (var (promo, moment, other) in Moments(world, n))
                Speak(world, date, n, promo, moment, other);
    }

    /// <summary>Which houses a news item is a moment for, and who the other house in it was.</summary>
    public static IEnumerable<(Promotion Promo, OwnerMoment Moment, Promotion? Other)> Moments(World world, NewsItem n)
    {
        var home = world.Get(n.Promotion);
        string h = n.Headline;
        switch (n.Category)
        {
            case NewsCategory.Signing:
                int away = h.IndexOf(" away from ", StringComparison.Ordinal);
                if (away > 0 && h.Contains(" signs ", StringComparison.Ordinal) && home is not null)
                {
                    string abbr = h[(away + " away from ".Length)..].Trim();
                    var from = world.Promotions.Values.FirstOrDefault(p => !p.Defunct && p.Abbreviation == abbr);
                    yield return (home, OwnerMoment.Signs, from);
                    if (from is not null) yield return (from, OwnerMoment.Outbid, home);
                }
                else if (home is not null && h.Contains(" signs with ", StringComparison.Ordinal))
                    yield return (home, OwnerMoment.Signs, null);
                break;
            case NewsCategory.TitleChange:
                if (home is null) break;
                if (h.Contains(" dethrones ", StringComparison.Ordinal) || h.Contains(" claims the vacant ", StringComparison.Ordinal))
                    yield return (home, OwnerMoment.Crowns, null);
                else if (h.Contains(" vacates the ", StringComparison.Ordinal))
                    yield return (home, OwnerMoment.Left, null);
                break;
            case NewsCategory.Retirement:
                if (home is not null && h.EndsWith(" announces retirement", StringComparison.Ordinal))
                    yield return (home, OwnerMoment.Retired, null);
                break;
        }
    }

    private static void Speak(World world, DateOnly date, NewsItem n, Promotion promo, OwnerMoment moment, Promotion? other)
    {
        if (world.HasPlayer && promo.Id == world.PlayerPromotion) return;   // the player is the owner of his own house
        if (OwnerAgendas.Of(promo, world.Mode) is not { } agenda) return;
        if (world.Get(n.AboutFighter!.Value) is not { } f) return;

        ulong h = Roll(world, n, promo, moment);
        if ((int)(h % 100) >= Share(moment)) return;
        var lines = Lines(agenda, moment);
        if (lines.Length == 0) return;
        string line = lines[(int)((h >> 20) % (ulong)lines.Length)];
        string text = line.Replace("{F}", $"{f.FirstName} {f.LastName}").Replace("{L}", f.LastName).Replace("{Org}", other?.Abbreviation ?? "Another house");

        News.Post(world, date, NewsCategory.General,
            $"{agenda.Owner} on {f.LastName}", $"“{text}”", promo.Id, f.Id);
    }

    public static string[] Lines(OwnerAgenda a, OwnerMoment m) => m switch
    {
        OwnerMoment.Signs => a.Signs,
        OwnerMoment.Crowns => a.Crowns,
        OwnerMoment.Left => a.Left,
        OwnerMoment.Retired => a.Retired,
        _ => a.Outbid,
    };

    /// <summary>A stable hash roll off the seed, the item and the house: no stream involved.</summary>
    public static ulong Roll(World world, NewsItem n, Promotion promo, OwnerMoment moment)
    {
        ulong k = Fnv(n.Headline) ^ ((ulong)n.Date.DayNumber << 32) ^ ((ulong)promo.Id.Value << 8) ^ (ulong)(int)moment;
        return SimRandom.Mix(world.RngSeed, k ^ 0x0A9E_17DAUL);
    }

    private static ulong Fnv(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h;
    }
}
