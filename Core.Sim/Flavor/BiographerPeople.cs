using System.Text;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>The kind of line in a fighter's "people" passage.</summary>
public enum PeopleKind { Mentor, Student, Family, Friend, Rival, Series, Belt, Camp }

/// <summary>One sentence of the "people" passage, with who it is about when it is about one person.</summary>
public sealed record PeopleLine(PeopleKind Kind, FighterId? Other, string Text);

public static partial class Biographer
{
    /// <summary>
    /// The people around a fighter, derived from the world as it stands: his mentor or whom he
    /// mentors, blood, friends, rivals (with the authored feud reason when there is one), what
    /// has been fought out between them since (real bouts, so it stays true ten years in), and the
    /// belt a mentor once held when his student holds it now. Only public things: no ability, no
    /// potential. Empty for somebody with no ties and no history.
    /// </summary>
    public static IReadOnlyList<PeopleLine> People(World world, Fighter f)
    {
        var lines = new List<PeopleLine>();
        var told = new HashSet<FighterId>();
        string He = f.Sex == Sex.Female ? "She" : "He";
        string His = f.Sex == Sex.Female ? "Her" : "His";

        var bonds = world.Relationships.Where(r => r.Involves(f.Id))
            .Select(r => (Bond: r, Other: world.Get(r.Other(f.Id))))
            .Where(x => x.Other is not null)
            .OrderBy(x => (int)x.Bond.Kind).ThenByDescending(x => x.Bond.Strength).ThenBy(x => x.Other!.Id.Value)
            .ToList();

        // Mentors first (his own, then his students), then blood, then friends.
        foreach (var (bond, o) in bonds.Where(x => x.Bond.Kind == RelationshipKind.Mentor && x.Bond.B == f.Id))
            lines.Add(new(PeopleKind.Mentor, o!.Id,
                $"{His} mentor is {Name(world, o, bond)}{Status(world, o)}.{SeriesText(world, f, o, told)}"));
        foreach (var (bond, o) in bonds.Where(x => x.Bond.Kind == RelationshipKind.Mentor && x.Bond.A == f.Id))
            lines.Add(new(PeopleKind.Student, o!.Id,
                $"{He} mentors {Name(world, o, bond)}{Status(world, o)}.{SeriesText(world, f, o, told)}"));
        var kin = bonds.Where(x => x.Bond.Kind == RelationshipKind.Family).ToList();
        if (kin.Count > 0)
        {
            var (bond, o) = kin[0];
            string text = $"Blood ties {(f.Sex == Sex.Female ? "her" : "him")} to {Name(world, o!, bond)}{Status(world, o!)}.{SeriesText(world, f, o!, told)}";
            if (kin.Count > 1)
            {
                var rest = kin.Skip(1).Take(3).ToList();
                text += $" Family in the sport too: {Join(rest.Select(x => $"{x.Other!.FullName}{Where(world, x.Other)}").ToList())}.";
                foreach (var (_, ko) in rest) text += SeriesText(world, f, ko!, told);
            }
            lines.Add(new(PeopleKind.Family, kin.Count == 1 ? o!.Id : null, text));
        }

        var friends = bonds.Where(x => x.Bond.Kind == RelationshipKind.Friend).Take(3).ToList();
        if (friends.Count > 0)
        {
            var sb = new StringBuilder($"{He} counts ");
            sb.Append(Join(friends.Select(x => $"{x.Other!.FullName}{Where(world, x.Other)}").ToList()));
            sb.Append(friends.Count == 1 ? " as a friend." : " as friends.");
            foreach (var (_, o) in friends) sb.Append(SeriesText(world, f, o!, told));
            lines.Add(new(PeopleKind.Friend, friends.Count == 1 ? friends[0].Other!.Id : null, sb.ToString()));
        }

        // Rivals: the hottest two, with the authored reason where one is written for these two.
        var rivals = world.Rivalries.Where(r => r.Involves(f.Id))
            .Select(r => (Feud: r, Other: world.Get(r.Other(f.Id))))
            .Where(x => x.Other is not null)
            .OrderByDescending(x => x.Feud.Intensity).ThenBy(x => x.Other!.Id.Value)
            .Take(2).ToList();
        foreach (var (feud, o) in rivals)
        {
            string text = $"{His} {(feud.Intensity >= 70 ? "bitter rival" : "rival")} is {o!.FullName}{Status(world, o)}.";
            if (FeudReasonFor(world, f, o) is { } why) text += " " + why.TrimEnd();
            lines.Add(new(PeopleKind.Rival, o.Id, text + SeriesText(world, f, o, told)));
        }

        // The belt a mentor once held, held now by his student (told from either side).
        foreach (var (bond, o) in bonds.Where(x => x.Bond.Kind == RelationshipKind.Mentor))
        {
            var mentor = bond.A == f.Id ? f : o!;
            var student = bond.A == f.Id ? o! : f;
            foreach (var title in world.Titles.Values.OrderBy(t => t.Id.Value))
                if (title.CurrentChampion == student.Id && title.Lineage.Any(r => r.Champion == mentor.Id))
                    lines.Add(new(PeopleKind.Belt, o!.Id,
                        $"{student.FullName} now holds the {title.Name} that {mentor.FullName} once held."));
        }

        // The series that no bond or feud already told: the two men he has fought most.
        foreach (var other in MostFought(world, f, told).Take(2))
        {
            var text = SeriesText(world, f, other, told).Trim();
            if (text.Length > 0) lines.Add(new(PeopleKind.Series, other.Id, text));
        }

        // The room he trains in and who runs it.
        if (f.Camp is { } cid && world.Get(cid) is { } camp)
        {
            var coach = camp.HeadCoach is { } hc && hc != f.Id ? world.Get(hc) : null;
            lines.Add(new(PeopleKind.Camp, coach?.Id,
                coach is null ? $"{He} trains at {camp.Name}." : $"{He} trains at {camp.Name}, under head coach {coach.FullName}."));
        }
        return lines;
    }

    /// <summary>The passage as running text (what <see cref="Fighter"/> appends to the profile bio).</summary>
    public static string PeopleText(World world, Fighter f) =>
        string.Join(" ", People(world, f).Select(l => l.Text));

    // "Name (note)" with the authored or earned note when the bond carries one.
    private static string Name(World world, Fighter o, Relationship bond) =>
        bond.Note is { Length: > 0 } n ? $"{o.FullName} ({n.TrimEnd().TrimEnd('.')})" : o.FullName;

    // ", now retired and head coach of X" / ", the reigning Y champion" / ", fighting for ABBR".
    private static string Status(World world, Fighter o)
    {
        var belt = world.Titles.Values.OrderBy(t => t.Id.Value).FirstOrDefault(t => t.CurrentChampion == o.Id);
        if (belt is not null) return $", who holds the {belt.Name}";
        if (o.Status == FighterStatus.Retired)
        {
            var room = world.Camps.Values.FirstOrDefault(c => c.HeadCoach == o.Id);
            return room is null ? ", now retired" : $", now retired and head coach of {room.Name}";
        }
        return o.Contract is { } c && world.Get(c.Promotion) is { } p ? $", fighting for {p.Abbreviation}" : ", a free agent";
    }

    // " (fighting for ABBR)" style tail for a friend list; empty for a free agent.
    private static string Where(World world, Fighter o)
    {
        if (o.Status == FighterStatus.Retired) return " (retired)";
        return o.Contract is { } c && world.Get(c.Promotion) is { } p ? $" (fighting for {p.Abbreviation})" : " (a free agent)";
    }

    // The authored reason two people cannot stand each other: only for two authored people, and only
    // from the table for the world's mode.
    private static string? FeudReasonFor(World world, Fighter a, Fighter b)
    {
        if (!a.Designed || !b.Designed) return null;
        return world.Mode == WorldMode.Origins
            ? OriginsPioneers.FeudReason(a.LastName, b.LastName)
            : DesignedRoster.FeudReason(a.LastName, b.LastName);
    }

    // What they have fought out, once per pair, named so it reads on its own wherever it lands.
    private static string SeriesText(World world, Fighter f, Fighter o, HashSet<FighterId> told)
    {
        if (!told.Add(o.Id)) return "";
        var s = Ties.SeriesBetween(world, f.Id, o.Id);
        if (s.Meetings == 0) return "";
        string pair = $"{f.LastName} and {o.LastName}";
        if (s.Meetings == 1)
            return s.Wins == 1 ? $" {pair} have met once, and {f.LastName} won."
                : s.Losses == 1 ? $" {pair} have met once, and {o.LastName} won."
                : $" {pair} have met once, and it ended without a winner.";
        string who = s.Wins > s.Losses ? $"{f.LastName} leads {s.Wins}-{s.Losses}"
            : s.Losses > s.Wins ? $"{o.LastName} leads {s.Losses}-{s.Wins}"
            : $"the series is level at {s.Wins}-{s.Losses}";
        string rest = s.Unsettled > 0 ? $", with {s.Unsettled} drawn or ruled no contest" : "";
        return $" {pair} have met {s.Meetings} times; {who}{rest}.";
    }

    // Everybody he has fought twice or more, most-fought first, whom the passage has not already covered.
    private static IEnumerable<Fighter> MostFought(World world, Fighter f, HashSet<FighterId> told)
    {
        var counts = new Dictionary<FighterId, int>();
        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Completed) continue;
            foreach (var fight in ev.Card)
            {
                if (fight.Outcome is null || fight.OverturnedNc) continue;
                FighterId? opp = fight.RedCorner == f.Id ? fight.BlueCorner : fight.BlueCorner == f.Id ? fight.RedCorner : null;
                if (opp is { } id && !told.Contains(id)) counts[id] = counts.GetValueOrDefault(id) + 1;
            }
        }
        return counts.Where(kv => kv.Value >= 2)
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Value)
            .Select(kv => world.Get(kv.Key)).Where(o => o is not null).Select(o => o!);
    }
}
