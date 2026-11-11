using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;

namespace MmaManager.Core.Sim.Booking;

// Fog of war: everything here advises the PLAYER, so every ability figure is
// the player's scouted read (MatchmakingAdvisor.ScoutedRead), never the truth.

/// <summary>Where a fighter sits in a division: champion, ranked contender, or unranked.</summary>
public enum Standing { Champion, Ranked, Unranked }

/// <summary>A fighter's standing plus their numeric rank when they have one.</summary>
public readonly record struct FighterStanding(Standing Kind, int? Rank)
{
    /// <summary>Short badge for the UI: "C", "#3", or "NR".</summary>
    public string Label => Kind switch
    {
        Standing.Champion => "C",
        Standing.Ranked => $"#{Rank}",
        _ => "NR",
    };
}

/// <summary>What a bout is worth on a card, from a title fight down to a mismatch.</summary>
public enum Stakes { TitleFight, TitleEliminator, RankedBout, Competitive, Showcase, Mismatch }

/// <summary>An appraisal of a proposed bout: how even it is and how much it draws.</summary>
public sealed class MatchupAppraisal
{
    public required int Competitiveness { get; init; }   // 0..100 — even fight scores high
    public required int Appeal { get; init; }            // 0..100 — marquee fight scores high
    public required Stakes Stakes { get; init; }
    public required bool TitleImplied { get; init; }     // champion in the bout + a belt exists
    public required string Summary { get; init; }        // one-line description for the UI

    public string StakesTag => Stakes switch
    {
        Stakes.TitleFight => "Title",
        Stakes.TitleEliminator => "Eliminator",
        Stakes.RankedBout => "Ranked",
        Stakes.Competitive => "Competitive",
        Stakes.Mismatch => "Mismatch",
        _ => "Showcase",
    };
}

/// <summary>
/// The player's matchmaking brain: reads a fighter's standing in their division,
/// appraises how competitive and how compelling a proposed bout is, and can pick
/// the most sensible opponent from an eligible pool. Read-only over the World —
/// it advises the Book Event screen, it doesn't mutate anything.
/// </summary>
public sealed class MatchmakingAdvisor
{
    /// <summary>The fighter's standing within the player promotion's division.</summary>
    public FighterStanding StandingOf(World world, FighterId fighterId)
    {
        var f = world.Get(fighterId);
        var div = DivisionFor(world, f);
        if (f is null || div is null) return new(Standing.Unranked, null);

        if (div.Title is { } tid && world.Get(tid)?.CurrentChampion == fighterId)
            return new(Standing.Champion, null);

        int idx = div.Rankings.IndexOf(fighterId);
        return idx >= 0 ? new(Standing.Ranked, idx + 1) : new(Standing.Unranked, null);
    }

    /// <summary>
    /// Order fighters the way a matchmaker scans a division: champ, #1, #2 … then
    /// the unranked by the player's scouted read, id as the last word.
    /// </summary>
    public IEnumerable<Fighter> ByStanding(World world, IEnumerable<Fighter> fighters) =>
        fighters
            .OrderBy(f => StandingSortKey(world, f.Id))
            .ThenByDescending(f => ScoutedRead(world, f))
            .ThenBy(f => f.Id.Value);

    /// <summary>
    /// The ability number the player's matchmaking desk works from: the player
    /// promotion's scouted read (<see cref="Scouting.ScoutingService.PerceivedAbility(World?, Promotion, Fighter)"/>),
    /// the same "Sct" figure the screens show. Never the true rating — two
    /// fighters the scouts read the same are the same to this advisor. A world
    /// with no player promotion (a headless harness) has no scout, so it falls
    /// back to the rating itself.
    /// </summary>
    public static int ScoutedRead(World world, Fighter f) =>
        world.Get(world.PlayerPromotion) is { } mine
            ? Scouting.ScoutingService.PerceivedAbility(world, mine, f)
            : FighterAbility.CurrentAbility(f);

    /// <summary>Appraise a potential red-vs-blue bout, from the player's scouted reads (never true ability).</summary>
    public MatchupAppraisal Appraise(World world, FighterId redId, FighterId blueId)
    {
        var red = world.Get(redId);
        var blue = world.Get(blueId);
        if (red is null || blue is null)
            return new() { Competitiveness = 0, Appeal = 0, Stakes = Stakes.Showcase, TitleImplied = false, Summary = "—" };

        return Appraise(world, red, blue, ScoutedRead(world, red), ScoutedRead(world, blue));
    }

    /// <summary>Appraise with the scouted reads already in hand (a matchmaker scanning many pairs reads each fighter once).</summary>
    internal MatchupAppraisal Appraise(World world, FighterId redId, FighterId blueId, int readRed, int readBlue) =>
        world.Get(redId) is { } red && world.Get(blueId) is { } blue
            ? Appraise(world, red, blue, readRed, readBlue)
            : new() { Competitiveness = 0, Appeal = 0, Stakes = Stakes.Showcase, TitleImplied = false, Summary = "—" };

    private MatchupAppraisal Appraise(World world, Fighter red, Fighter blue, int ovrR, int ovrB)
    {
        var redId = red.Id;
        var blueId = blue.Id;
        int gap = Math.Abs(ovrR - ovrB);
        int competitiveness = Math.Clamp(100 - gap * 5, 0, 100);

        var sR = StandingOf(world, redId);
        var sB = StandingOf(world, blueId);
        bool champInvolved = sR.Kind == Standing.Champion || sB.Kind == Standing.Champion;
        bool eitherRanked = sR.Kind == Standing.Ranked || sB.Kind == Standing.Ranked;
        bool topEliminator = sR is { Kind: Standing.Ranked, Rank: <= 5 } && sB is { Kind: Standing.Ranked, Rank: <= 5 };
        bool hasBelt = DivisionFor(world, red)?.Title is not null;
        bool titleImplied = champInvolved && hasBelt;

        Stakes stakes =
            titleImplied ? Stakes.TitleFight :
            topEliminator ? Stakes.TitleEliminator :
            eitherRanked ? Stakes.RankedBout :
            competitiveness < 30 ? Stakes.Mismatch :
            competitiveness >= 60 ? Stakes.Competitive :
            Stakes.Showcase;

        double avgPop = (red.Intangibles.Popularity + blue.Intangibles.Popularity) / 2.0;
        double avgOvr = (ovrR + ovrB) / 2.0;
        int stakesBonus = stakes switch
        {
            Stakes.TitleFight => 22,
            Stakes.TitleEliminator => 14,
            Stakes.RankedBout => 7,
            _ => 0,
        };
        int appeal = Math.Clamp(
            (int)Math.Round(0.45 * avgPop + 0.30 * avgOvr + 0.10 * competitiveness) + stakesBonus, 0, 100);

        return new()
        {
            Competitiveness = competitiveness,
            Appeal = appeal,
            Stakes = stakes,
            TitleImplied = titleImplied,
            Summary = Describe(stakes, sR, sB, competitiveness),
        };
    }

    /// <summary>The most compelling opponent from an eligible pool — balances an even fight against a marquee one.</summary>
    public FighterId? SuggestOpponent(World world, FighterId forFighter, IEnumerable<Fighter> pool)
    {
        FighterId? best = null;
        double bestScore = double.NegativeInfinity;
        foreach (var opp in pool.OrderBy(o => o.Id.Value))   // deterministic tie-break
        {
            var a = Appraise(world, forFighter, opp.Id);
            double score = 0.5 * a.Competitiveness + 0.5 * a.Appeal;
            if (score > bestScore) { bestScore = score; best = opp.Id; }
        }
        return best;
    }

    private static string Describe(Stakes stakes, FighterStanding r, FighterStanding b, int comp)
    {
        string edge = comp >= 70 ? "toss-up" : comp >= 45 ? "competitive" : comp >= 25 ? "clear favourite" : "lopsided";
        return stakes switch
        {
            Stakes.TitleFight => $"Title fight ({r.Label} vs {b.Label}) · {edge}",
            Stakes.TitleEliminator => $"Title eliminator ({r.Label} vs {b.Label}) · {edge}",
            Stakes.RankedBout => $"Ranked bout ({r.Label} vs {b.Label}) · {edge}",
            Stakes.Competitive => $"Competitive matchup · {edge}",
            Stakes.Mismatch => $"Mismatch on paper · {edge}",
            _ => $"Showcase bout · {edge}",
        };
    }

    private int StandingSortKey(World world, FighterId id)
    {
        var s = StandingOf(world, id);
        return s.Kind switch { Standing.Champion => 0, Standing.Ranked => s.Rank!.Value, _ => 1000 };
    }

    private static Division? DivisionFor(World world, Fighter? f) =>
        f is null ? null : world.Get(world.PlayerPromotion)?.Divisions.FirstOrDefault(d => d.Matches(f));
}
