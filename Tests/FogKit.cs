using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// Shared set-up for the "a house decides on what it has seen" tests. A scouted read is the truth plus a fixed,
/// per-(house, fighter) error, so it moves with the truth; what these tests pin instead is the case where the read
/// and the truth DISAGREE about who is better: the decision must follow the read. Each test was run against the
/// code before its fix and failed there (the decision followed the truth).
/// </summary>
internal static class FogKit
{
    public static World Modern(ulong seed = 5) => new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    /// <summary>Every fighting skill at <paramref name="v"/>, so <see cref="FighterAbility.CurrentAbility"/> is exactly v.</summary>
    public static void SetTrue(Fighter f, int v)
    {
        f.Striking.PunchPower = v; f.Striking.PunchTechnique = v; f.Striking.KickPower = v; f.Striking.KickTechnique = v;
        f.Striking.ClinchStriking = v; f.Striking.StrikingDefense = v; f.Striking.Footwork = v;
        f.Grappling.TakedownOffense = v; f.Grappling.TakedownDefense = v; f.Grappling.TopControl = v; f.Grappling.BottomGame = v;
        f.Grappling.SubmissionOffense = v; f.Grappling.SubmissionDefense = v; f.Grappling.Scrambling = v; f.Grappling.ClinchControl = v;
        f.Physical.Strength = v; f.Physical.Cardio = v; f.Physical.Speed = v; f.Physical.Chin = v;
        f.Physical.Recovery = v; f.Physical.Flexibility = v;
        f.Mental.FightIQ = v; f.Mental.Composure = v; f.Mental.Heart = v; f.Mental.Discipline = v;
        Assert.Equal(v, FighterAbility.CurrentAbility(f));
    }

    /// <summary>A blank public record and no famous room: whatever a house reads of him carries its full error.</summary>
    public static void Unknown(Fighter f)
    {
        var r = f.Record;
        r.Wins = 0; r.Losses = 0; r.Draws = 0; r.NoContests = 0;
        r.WinsByKo = 0; r.WinsBySubmission = 0; r.WinsByDecision = 0; r.CurrentStreak = 0;
        f.Camp = null;
        f.CurrentInjury = null;
        f.ReadyToFightOn = null;
    }

    /// <summary>
    /// Two of <paramref name="pool"/> the reader misjudges in opposite directions by at least two points, measured
    /// at <paramref name="level"/> (both left at that true level). Null when the pool has no such pair.
    /// </summary>
    public static (Fighter Up, Fighter Down)? Misread(IEnumerable<Fighter> pool, Func<Fighter, int> read, int level = 60)
    {
        var offs = new List<(Fighter F, int Off)>();
        foreach (var f in pool.OrderBy(f => f.Id.Value))
        {
            SetTrue(f, level);
            offs.Add((f, read(f) - level));
        }
        if (offs.Count < 2) return null;
        var up = offs.OrderByDescending(o => o.Off).ThenBy(o => o.F.Id.Value).First();
        var down = offs.OrderBy(o => o.Off).ThenBy(o => o.F.Id.Value).First();
        return up.Off - down.Off >= 2 ? (up.F, down.F) : null;
    }

    /// <summary>
    /// Leave <paramref name="up"/> one point BELOW <paramref name="down"/> in truth, while the reader still sees
    /// <paramref name="up"/> ahead: the read and the truth now disagree about who is better.
    /// </summary>
    public static void Invert(Fighter up, Fighter down, Func<Fighter, int> read, int level = 60)
    {
        SetTrue(up, level);
        SetTrue(down, level + 1);
        Assert.True(FighterAbility.CurrentAbility(down) > FighterAbility.CurrentAbility(up), "truth: down is better");
        Assert.True(read(up) > read(down), $"read: up {read(up)} must be ahead of down {read(down)}");
    }

    /// <summary>Fighters not already on a scheduled card and not holding a belt.</summary>
    public static bool Unbooked(World world, Fighter f) =>
        !world.Titles.Values.Any(t => t.CurrentChampion == f.Id)
        && !world.Events.Values.Any(e => e.Status == EventStatus.Scheduled
                                         && e.Card.Any(b => b.RedCorner == f.Id || b.BlueCorner == f.Id));

    /// <summary>Nothing between these two that would stop or force a pairing (a bond, a feud).</summary>
    public static bool Neutral(World world, Fighter a, Fighter b) =>
        !Bonds.WontFight(world, a.Id, b.Id) && Storylines.RivalryBetween(world, a.Id, b.Id) is null;
}
