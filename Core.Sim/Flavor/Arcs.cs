using MmaManager.Core.Model;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>What has to be true, in the world as it stands, for a beat of a story to be told.</summary>
public enum Cond
{
    /// <summary>The two are on one card that has been announced and not yet fought.</summary>
    Booked,
    /// <summary>They fought, lately (last two weeks), after the previous beat was told. Any result.</summary>
    Fought,
    /// <summary>The Lead beat the Foil, lately. With <see cref="Beat.Swap"/>: the Foil beat the Lead.</summary>
    Won,
    /// <summary>As Won, by knockout, stoppage or submission.</summary>
    Finished,
    /// <summary>They fought, lately, and it was not their first meeting.</summary>
    Rematch,
    /// <summary>The Lead took a belt off the Foil, lately (swap: the Foil off the Lead).</summary>
    TitleWonFrom,
    /// <summary>The Lead became champion, lately (swap: the Foil).</summary>
    TitleWon,
    /// <summary>The Lead's reign ended in a defeat, lately (swap: the Foil's).</summary>
    TitleLost,
    /// <summary>The Lead won a title fight as champion, lately (swap: the Foil).</summary>
    Defended,
    /// <summary>The Lead beat the Foil in a title fight as champion, lately (swap: the Foil as champion).</summary>
    DefendedVs,
    /// <summary>The Lead's reign has run to N defences or more.</summary>
    Defences,
    /// <summary>The Lead won again, lately, and the run stands at N or more.</summary>
    WonStreak,
    /// <summary>The Lead lost again, lately, and the skid stands at N or more.</summary>
    LostStreak,
    /// <summary>The Lead has retired.</summary>
    Retired,
    /// <summary>The Lead is no longer on the books of the house in <see cref="Beat.Org"/> (the house must exist).</summary>
    LeftOrg,
    /// <summary>The Lead is on the books of the house in <see cref="Beat.Org"/>, or of any house when null.</summary>
    SignedWith,
    /// <summary>The two have bad blood on the books, at N or more.</summary>
    FeudHot,
}

/// <summary>
/// One beat: the condition, the text, and the slot it holds. Beats that share a slot are alternatives (the
/// belt changed hands, or it did not): the first whose condition holds is told, and the slot is then done.
/// Text tokens: {AOrg} {BOrg} (a house's abbreviation, or "the open market"), {AWhere} {BWhere} ("with AFC" or "a free agent"), {ARec} {BRec} (win-loss-draw),
/// {Belt} (the title in play). Names are written in, since the cast is authored.
/// </summary>
public sealed record Beat(
    int Slot, Cond When, string Tag, string Headline, string Body,
    NewsCategory Category = NewsCategory.Milestone, bool Swap = false, int N = 0, string? Org = null);

/// <summary>A story with a cast of authored people and a run of beats. The Lead holds the record that it was told.</summary>
public sealed record Arc(string Id, string Title, WorldMode Mode, string Lead, string Foil, IReadOnlyList<Beat> Beats);

/// <summary>
/// The sport telling its own stories. Once a week it looks at the world, and for every authored arc whose next
/// beat has come true it posts the beat as news (MMA Weekly picks it up). Text only: it reads the world and
/// writes news and one accolade, and changes nothing a system decides on.
///
/// Told-once record: an accolade on the Lead, "Saga: {arc title}: {beat tag}". It is saved with the fighter, it is
/// never evicted (the news feed is capped at a thousand), and an accolade of that shape means nothing to
/// valuation (which reads "gold/silver/bronze" and "Discovered" only) or to any other system. No new field.
/// Everything is keyed off ids and dates: no draw from any stream.
/// </summary>
public static class Arcs
{
    public const string AccoladePrefix = "Saga: ";
    /// <summary>How far back a bout or a title change can be and still be told (four weekly passes see it, so a busy week cannot lose a beat).</summary>
    public const int WindowDays = 28;
    /// <summary>At most this many beats a week, so a long-quiet world does not print a column of them at once.</summary>
    public const int MaxPerWeek = 4;

    public static IReadOnlyList<Arc> All { get; } = ArcsModern.All.Concat(ArcsOrigins.All).ToArray();

    public static string AccoladeTitle(Arc arc, Beat beat) => $"{AccoladePrefix}{arc.Title}: {beat.Tag}";

    /// <summary>
    /// Whether an accolade is the told-once mark of a story beat. The marks are bookkeeping, not honours: screens that list
    /// a fighter's honours leave them out (a defeat is a beat too).
    /// </summary>
    public static bool IsSagaMark(string accoladeTitle) => accoladeTitle.StartsWith(AccoladePrefix, StringComparison.Ordinal);

    /// <summary>Tell every beat that has come true. Returns the beats told (for tests and logs).</summary>
    public static List<(Arc Arc, Beat Beat)> RunWeekly(World world, DateOnly date)
    {
        var told = new List<(Arc, Beat)>();
        Ctx? ctx = null;
        // The pass starts at a different arc each week (by the week number) so the cap can only delay a late arc,
        // never starve it.
        int n = All.Count, start = n == 0 ? 0 : (date.DayNumber / 7) % n;
        for (int k = 0; k < n; k++)
        {
            var arc = All[(start + k) % n];
            if (arc.Mode != world.Mode) continue;
            ctx ??= new Ctx(world, date);
            if (ctx.Find(arc.Lead) is not { } lead) continue;
            if (!Next(arc, lead, out var slot, out var prevDate)) continue;
            foreach (var beat in arc.Beats.Where(b => b.Slot == slot))
            {
                if (!Holds(world, ctx, arc, beat, prevDate, out var belt)) continue;
                Tell(world, ctx, arc, beat, lead, belt, date);
                told.Add((arc, beat));
                break;
            }
            if (told.Count >= MaxPerWeek) break;
        }
        return told;
    }

    /// <summary>Which slot is next for this arc (the first not yet told), and when the one before it was told.</summary>
    public static bool Next(Arc arc, Fighter lead, out int slot, out DateOnly? prevDate)
    {
        slot = 0; prevDate = null;
        foreach (int s in arc.Beats.Select(b => b.Slot).Distinct().OrderBy(x => x))
        {
            DateOnly? told = null;
            foreach (var b in arc.Beats.Where(b => b.Slot == s))
                foreach (var a in lead.Accolades)
                    if (a.Title == AccoladeTitle(arc, b)) { told = a.Date; break; }
            if (told is null) { slot = s; return true; }
            prevDate = told;
        }
        return false;   // every slot told
    }

    private static void Tell(World world, Ctx ctx, Arc arc, Beat beat, Fighter lead, string? belt, DateOnly date)
    {
        var foil = ctx.Find(arc.Foil);
        string Org(Fighter? f) => f?.Contract is { } c && world.Get(c.Promotion) is { } p ? p.Abbreviation : "the open market";
        string Rec(Fighter? f) => f is null ? "" : f.Record.ToString();
        string Where(Fighter? f) => f?.Contract is { } c && world.Get(c.Promotion) is { } p ? $"with {p.Abbreviation}" : "a free agent";
        string Fill(string s) => s.Replace("{AWhere}", Where(lead)).Replace("{BWhere}", Where(foil)).Replace("{AOrg}", Org(lead)).Replace("{BOrg}", Org(foil))
            .Replace("{ARec}", Rec(lead)).Replace("{BRec}", Rec(foil)).Replace("{Belt}", belt ?? "the belt");

        lead.Accolades.Add(new Accolade(date, AccoladeTitle(arc, beat)));
        News.Post(world, date, beat.Category, Fill(beat.Headline), Fill(beat.Body),
            lead.Contract?.Promotion ?? default, lead.Id);
    }

    /// <summary>Whether a beat's condition holds in the world now. <paramref name="belt"/> names the title in play, when there is one.</summary>
    public static bool Holds(World world, Ctx ctx, Arc arc, Beat b, DateOnly? after, out string? belt)
    {
        belt = null;
        var lead = ctx.Find(arc.Lead);
        var foil = ctx.Find(arc.Foil);
        if (lead is null) return false;
        // Single-person conditions look at the Lead, or at the Foil when swapped.
        var who = b.Swap ? foil : lead;
        var other = b.Swap ? lead : foil;
        if (who is null) return false;

        bool Later(Event ev) => after is null || ev.Date > after.Value;
        switch (b.When)
        {
            case Cond.Booked:
                return foil is not null && lead.Status != FighterStatus.Retired && foil.Status != FighterStatus.Retired
                       && ctx.BookedPairs.Contains(Pair(lead.Id, foil.Id));

            case Cond.Fought:
                return foil is not null && ctx.Recent.Any(r => Later(r.Event) && Between(r.Fight, lead.Id, foil.Id));

            case Cond.Rematch:
                return foil is not null && ctx.Recent.Any(r => Later(r.Event) && Between(r.Fight, lead.Id, foil.Id))
                       && Flavor.Ties.SeriesBetween(world, lead.Id, foil.Id).Meetings >= 2;

            case Cond.Won:
            case Cond.Finished:
                if (foil is null) return false;
                foreach (var r in ctx.Recent)
                {
                    if (!Later(r.Event) || !Between(r.Fight, lead.Id, foil.Id)) continue;
                    if (r.Fight.Outcome!.Winner != who.Id) continue;
                    if (b.When == Cond.Finished && !IsFinish(r.Fight.Outcome.Method)) continue;
                    belt = TitleName(world, r.Fight);
                    return true;
                }
                return false;

            // A belt is won in the cage. A champion who retires or leaves is replaced without a fight (the heir, or a vacancy
            // filled later): that is not "dethroned", and the loser of nothing has not "lost".
            case Cond.TitleWon:
            case Cond.TitleWonFrom:
                foreach (var r in ctx.Recent)
                {
                    if (!Later(r.Event) || !r.Fight.IsTitleFight || r.Fight.Outcome!.Winner != who.Id) continue;
                    if (b.When == Cond.TitleWonFrom && (other is null || r.Fight.Outcome.Loser != other.Id)) continue;
                    if (r.Fight.Title is not { } tid || world.Get(tid) is not { } title) continue;
                    // ...and it is a NEW reign, opened on the night.
                    if (!title.Lineage.Any(x => x.Champion == who.Id && x.WonOn >= r.Event.Date)) continue;
                    belt = title.Name;
                    return true;
                }
                return false;

            case Cond.TitleLost:
                foreach (var r in ctx.Recent)
                {
                    if (!Later(r.Event) || !r.Fight.IsTitleFight || r.Fight.Outcome!.Loser != who.Id) continue;
                    if (r.Fight.Title is not { } tid || world.Get(tid) is not { } title) continue;
                    if (!title.Lineage.Any(x => x.Champion == who.Id && x.LostOn is { } l && l >= r.Event.Date && !x.Vacated)) continue;
                    belt = title.Name;
                    return true;
                }
                return false;

            // A defence is a win by a man who was already champion BEFORE the night (the night a belt is won is not one).
            case Cond.Defended:
            case Cond.DefendedVs:
                foreach (var r in ctx.Recent)
                {
                    if (!Later(r.Event) || !r.Fight.IsTitleFight || r.Fight.Outcome!.Winner != who.Id) continue;
                    if (b.When == Cond.DefendedVs && (other is null || !Between(r.Fight, who.Id, other.Id))) continue;
                    if (r.Fight.Title is not { } tid || world.Get(tid) is not { } title) continue;
                    if (!title.Lineage.Any(x => x.Champion == who.Id && x.WonOn < r.Event.Date && (x.LostOn is null || x.LostOn >= r.Event.Date))) continue;
                    belt = title.Name;
                    return true;
                }
                return false;

            case Cond.Defences:
                foreach (var t in world.Titles.Values)
                    if (t.CurrentChampion == who.Id && t.Lineage.LastOrDefault(x => x.LostOn is null) is { } cur && cur.Defenses >= b.N)
                    { belt = t.Name; return true; }
                return false;

            case Cond.WonStreak:
                return who.Record.CurrentStreak >= b.N && ctx.Recent.Any(r => Later(r.Event) && r.Fight.Outcome!.Winner == who.Id && Has(r.Fight, who.Id));

            case Cond.LostStreak:
                return who.Record.CurrentStreak <= -b.N && ctx.Recent.Any(r => Later(r.Event) && r.Fight.Outcome!.Loser == who.Id && Has(r.Fight, who.Id));

            case Cond.Retired:
                return who.Status == FighterStatus.Retired;

            case Cond.LeftOrg:
                if (ctx.Org(b.Org) is not { } home) return false;
                return who.Status != FighterStatus.Retired && who.Contract?.Promotion != home.Id;

            case Cond.SignedWith:
                if (who.Contract is null) return false;
                if (b.Org is null) return true;
                return ctx.Org(b.Org) is { } o && who.Contract.Promotion == o.Id;

            case Cond.FeudHot:
                return foil is not null && Storylines.RivalryBetween(world, lead.Id, foil.Id) is { } r2 && r2.Intensity >= b.N;
        }
        return false;
    }

    private static (int, int) Pair(FighterId a, FighterId b) => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);
    private static bool Between(Fight f, FighterId a, FighterId b) =>
        (f.RedCorner == a && f.BlueCorner == b) || (f.RedCorner == b && f.BlueCorner == a);
    private static bool Has(Fight f, FighterId id) => f.RedCorner == id || f.BlueCorner == id;
    private static bool IsFinish(FightMethod m) => m is
        FightMethod.Knockout or FightMethod.TechnicalKnockout or FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;
    private static string? TitleName(World world, Fight f) =>
        f.IsTitleFight && f.Title is { } t ? world.Get(t)?.Name : null;

    /// <summary>What the weekly pass reads off the world once, so a hundred beats do not each walk it.</summary>
    public sealed class Ctx
    {
        private readonly World _world;
        private readonly Dictionary<string, Fighter> _byName = new(StringComparer.Ordinal);
        private Dictionary<string, Promotion>? _orgs;

        public List<(Event Event, Fight Fight)> Recent { get; } = new();
        public HashSet<(int, int)> BookedPairs { get; } = new();

        public Ctx(World world, DateOnly date)
        {
            _world = world;
            foreach (var f in world.Fighters.Values)
                if (f.Designed) _byName[$"{f.FirstName} {f.LastName}"] = f;

            int from = date.DayNumber - WindowDays;
            foreach (var ev in world.Events.Values)
            {
                if (ev.Status == EventStatus.Scheduled)
                {
                    foreach (var fight in ev.Card) BookedPairs.Add(Pair(fight.RedCorner, fight.BlueCorner));
                }
                else if (ev.Status == EventStatus.Completed && ev.Date.DayNumber > from && ev.Date.DayNumber <= date.DayNumber)
                {
                    foreach (var fight in ev.Card)
                        if (fight.Outcome is not null && !fight.OverturnedNc) Recent.Add((ev, fight));
                }
            }
        }

        public Fighter? Find(string fullName) => _byName.GetValueOrDefault(fullName);

        public Promotion? Org(string? abbreviation)
        {
            if (abbreviation is null) return null;
            _orgs ??= _world.Promotions.Values.Where(p => !p.Defunct).GroupBy(p => p.Abbreviation)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            return _orgs.GetValueOrDefault(abbreviation);
        }

        private static (int, int) Pair(FighterId a, FighterId b) => a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);
    }
}
