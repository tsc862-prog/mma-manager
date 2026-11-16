using MmaManager.Core.Model;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>How two people are tied, from the first person's side. The order is the order of precedence.</summary>
public enum TieKind
{
    /// <summary>Blood.</summary>
    Family,
    /// <summary>The first person is the mentor of the second.</summary>
    Mentor,
    /// <summary>The first person is the second's student (the mentor is the other).</summary>
    Student,
    /// <summary>Bad blood, authored or earned in the cage.</summary>
    Rival,
    /// <summary>They have shared a cage before.</summary>
    Rematch,
    /// <summary>Friends, whatever banner each fights under.</summary>
    Friend,
    /// <summary>The same gym.</summary>
    Teammate,
    /// <summary>The same country.</summary>
    Countryman,
}

/// <summary>Where the first person stands in what they have already fought out.</summary>
public enum SeriesLean { Level, Leads, Trails }

/// <summary>One tie between two fighters, read off the world as it stands.</summary>
public sealed record Tie(TieKind Kind, SeriesLean Series, Relationship? Bond, Rivalry? Feud);

/// <summary>What two fighters have fought out between them: bouts counted, wins for the first, wins for the other.</summary>
public readonly record struct Series(int Meetings, int Wins, int Losses)
{
    /// <summary>Draws and no contests.</summary>
    public int Unsettled => Meetings - Wins - Losses;
    public SeriesLean Lean => Wins > Losses ? SeriesLean.Leads : Losses > Wins ? SeriesLean.Trails : SeriesLean.Level;
}

/// <summary>
/// Who is tied to whom, read straight off the world (bonds, rivalries, camps, countries and the
/// real fight history) so a line or a profile never claims a tie the world cannot back. Text only:
/// nothing in the sim reads this.
/// </summary>
public static class Ties
{
    /// <summary>
    /// A meeting inside this many days of the moment being talked about is the bout the moment is
    /// ABOUT (news is posted a few days after a card), not history.
    /// </summary>
    public const int RecentDays = 7;

    /// <summary>
    /// What they have fought out. With <paramref name="before"/>, only bouts on a card dated
    /// <see cref="RecentDays"/> or more before it count; without, every completed bout does.
    /// Overturned results are not meetings.
    /// </summary>
    public static Series SeriesBetween(World world, FighterId me, FighterId other, DateOnly? before = null)
    {
        int meetings = 0, wins = 0, losses = 0;
        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Completed) continue;
            if (before is { } cut && ev.Date.DayNumber > cut.DayNumber - RecentDays) continue;
            foreach (var fight in ev.Card)
            {
                if (fight.Outcome is not { } o || fight.OverturnedNc) continue;
                if (!((fight.RedCorner == me && fight.BlueCorner == other) || (fight.RedCorner == other && fight.BlueCorner == me))) continue;
                meetings++;
                if (o.Winner == me) wins++;
                else if (o.Winner == other) losses++;
            }
        }
        return new Series(meetings, wins, losses);
    }

    /// <summary>The strongest tie between two fighters as of a date, or null when they have none.</summary>
    public static Tie? Strongest(World world, Fighter me, Fighter other, DateOnly on)
    {
        if (me.Id == other.Id) return null;
        var bond = Bonds.Between(world, me.Id, other.Id);
        if (bond is { Kind: RelationshipKind.Family }) return new Tie(TieKind.Family, SeriesLean.Level, bond, null);
        if (bond is { Kind: RelationshipKind.Mentor })
            return new Tie(bond.A == me.Id ? TieKind.Mentor : TieKind.Student, SeriesLean.Level, bond, null);

        var feud = Storylines.RivalryBetween(world, me.Id, other.Id);
        if (feud is not null) return new Tie(TieKind.Rival, SeriesLean.Level, null, feud);

        var series = SeriesBetween(world, me.Id, other.Id, on);
        if (series.Meetings > 0) return new Tie(TieKind.Rematch, series.Lean, null, null);

        if (bond is { Kind: RelationshipKind.Friend }) return new Tie(TieKind.Friend, SeriesLean.Level, bond, null);
        if (me.Camp is { } room && room == other.Camp) return new Tie(TieKind.Teammate, SeriesLean.Level, null, null);
        if (me.Country.Value != 0 && me.Country == other.Country) return new Tie(TieKind.Countryman, SeriesLean.Level, null, null);
        return null;
    }
}
