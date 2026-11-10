using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>
/// The poster line — the one sentence that says why THIS card matters. Reads the
/// world's live stories (feuds, bonds, camps, belts, faces, milestones) off the
/// MAIN EVENT and picks the most specific one. Null = just another fight night.
/// Used by the poster art and anywhere else a card needs selling.
/// </summary>
public static class EventHype
{
    public static string? Tagline(World world, Event ev)
    {
        var promo = world.Get(ev.Promotion);
        // "The debut" belongs to the little guys — a garage org's (or startup's)
        // first-ever card. An established name's first card of the SAVE is just
        // the calendar starting, not a birth.
        bool firstShow = promo is not null && Org.OrgSizing.SizeOf(promo) == OrgSize.City
            && !world.Events.Values.Any(e =>
                e.Promotion == ev.Promotion && e.Id != ev.Id && e.Status == EventStatus.Completed);

        if (ev.Card.Count == 0) return firstShow ? "WHERE IT ALL BEGINS" : null;
        var main = ev.Card[^1];
        var red = world.Get(main.RedCorner);
        var blue = world.Get(main.BlueCorner);
        if (red is null || blue is null) return firstShow ? "WHERE IT ALL BEGINS" : null;

        int meetings = Meetings(world, red.Id, blue.Id);
        var feud = Living.Storylines.RivalryBetween(world, red.Id, blue.Id);
        var bond = Living.Bonds.Between(world, red.Id, blue.Id);

        // Most specific story first — one line, all caps, poster-ready.
        if (main.IsTitleFight && red.Camp is { } room && red.Camp == blue.Camp) return "TEAMMATES COLLIDE";
        if (bond is { Kind: RelationshipKind.Family }) return "BLOOD AGAINST BLOOD";
        if (bond is { Kind: RelationshipKind.Friend, Strength: >= 30 }) return "BUSINESS, NOT FRIENDSHIP";
        if (feud is not null && meetings >= 2) return "THE TRILOGY";
        if (feud is { Intensity: >= 80 }) return "BAD BLOOD";
        if (feud is not null && meetings == 1) return "THE REMATCH";
        if (red.Camp is { } ca && blue.Camp is { } cb && Living.Camps.AtWar(world, ca, cb)) return "GYM WAR";
        if (feud is not null) return "GRUDGE MATCH";
        if (firstShow) return "WHERE IT ALL BEGINS";
        if (IsChampion(world, red.Id) && IsChampion(world, blue.Id) && !main.IsTitleFight) return "CHAMPION VS CHAMPION";
        if (main.IsTitleFight && main.Title is { } tid && world.Get(tid)?.CurrentChampion is null)
            return $"THE VACANT {BeltName(world, main)} THRONE";
        if (red.Record is { Losses: 0, Wins: >= 5 } && blue.Record is { Losses: 0, Wins: >= 5 }) return "SOMEBODY'S 0 MUST GO";
        if (promo is not null && Org.Franchise.FaceOf(world, promo) is { } face
            && (face.Id == red.Id || face.Id == blue.Id)) return $"THE {face.LastName.ToUpperInvariant()} SHOW";
        if (ev.VenueId is { } vid && world.Get(vid)?.City is { } host
            && (red.HomeCity == host || blue.HomeCity == host)) return "THE HOMECOMING";
        int number = world.Events.Values.Count(e => e.Promotion == ev.Promotion && e.Status == EventStatus.Completed) + 1;
        if (number is 50 or 100 or 200 or 300) return $"THE {number}TH SHOW";
        if (main.IsTitleFight) return $"FOR THE {BeltName(world, main)} TITLE";
        return null;
    }

    /// <summary>The belt on the line, spelled out: "WOMEN'S BANTAMWEIGHT", "LIGHT HEAVYWEIGHT".</summary>
    public static string BeltName(World world, Fight main) =>
        (world.Get(main.RedCorner)?.Sex == Sex.Female ? "WOMEN'S " : "") + PrettyWeight(main.WeightClass);

    /// <summary>"LightHeavyweight" → "LIGHT HEAVYWEIGHT".</summary>
    public static string PrettyWeight(WeightClass wc)
    {
        var s = wc.ToString();
        var outp = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            if (char.IsUpper(c) && outp.Length > 0) outp.Append(' ');
            outp.Append(char.ToUpperInvariant(c));
        }
        return outp.ToString();
    }

    /// <summary>How many times these two have already shared a cage (completed bouts).</summary>
    public static int Meetings(World world, FighterId a, FighterId b) =>
        world.Events.Values.Where(e => e.Status == EventStatus.Completed)
            .SelectMany(e => e.Card)
            .Count(f => f.Outcome is not null
                        && ((f.RedCorner == a && f.BlueCorner == b) || (f.RedCorner == b && f.BlueCorner == a)));

    private static bool IsChampion(World world, FighterId id) =>
        world.Titles.Values.Any(t => t.CurrentChampion == id);
}
