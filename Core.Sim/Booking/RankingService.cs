using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// Ranks a division's contenders by a résumé score built from their fight
/// results: who they beat and how good those opponents are ON THE RECORD
/// (beating a champion, a top-ranked contender or a fighter with a long winning
/// record is worth far more than beating a nobody), how recent the wins are, and
/// whether they finished. No ratings: a ranking is public and feeds prices. Rankings are recomputed after each card rather
/// than nudged, so the order always reflects the current body of work. Champions
/// sit on the belt, not in this list.
/// </summary>
public static class RankingService
{
    public static Division? DivisionFor(Promotion promo, WeightClass wc, Sex sex = Sex.Male) =>
        promo.Divisions.FirstOrDefault(d => d.WeightClass == wc && d.Sex == sex);

    /// <summary>The division a fighter belongs to (weight AND sex).</summary>
    public static Division? DivisionFor(Promotion promo, Fighter f) =>
        promo.Divisions.FirstOrDefault(d => d.Matches(f));

    /// <summary>Rebuild every division's contender ranking for a promotion from résumé scores.</summary>
    public static void RecomputePromotion(World world, Promotion promo, DateOnly asOf, int depth = 15)
    {
        var results = GatherResults(world, promo, asOf);
        var (champions, elite) = EliteSnapshot(world, promo);

        foreach (var div in promo.Divisions)
        {
            FighterId? champ = div.Title is { } tid ? world.Get(tid)?.CurrentChampion : null;

            var ranked = world.RosterOf(promo.Id)
                .Where(f => div.Matches(f) && f.Status != FighterStatus.Retired && f.Id != champ)
                .Select(f => (id: f.Id, score: ResumeScore(world, f, results, champions, elite, asOf)))
                .OrderByDescending(x => x.score).ThenBy(x => x.id.Value)
                .Take(depth)
                .Select(x => x.id)
                .ToList();

            div.Rankings.Clear();
            div.Rankings.AddRange(ranked);
        }
    }

    /// <summary>Recompute rankings for every promotion in the world.</summary>
    public static void RecomputeAll(World world, DateOnly asOf, int depth = 15)
    {
        foreach (var promo in world.Promotions.Values)
            RecomputePromotion(world, promo, asOf, depth);
    }

    /// <summary>
    /// Rebuild the cross-org weight-class rankings and the pound-for-pound list from
    /// the same résumé scoring the per-promotion rankings use — so a ranking is
    /// earned in the cage, not handed out for raw ability. Holding a belt (weighted
    /// by the promotion's prestige) is the biggest single boost; nobody without a
    /// win (and no belt) is ranked at all.
    /// </summary>
    public static void RecomputeGlobal(World world, int depth = 25)
    {
        var asOf = world.CurrentDate;
        var results = GatherAllResults(world, asOf);
        var (champions, elite) = GlobalElite(world);

        var champBonus = new Dictionary<FighterId, double>();
        foreach (var t in world.Titles.Values)
            if (t.CurrentChampion is { } cid)
            {
                double bonus = 15 + (world.Get(t.Promotion)?.Prestige.Value ?? 40) * 0.4;
                champBonus[cid] = Math.Max(champBonus.GetValueOrDefault(cid), bonus);
            }

        double Score(Fighter f)
        {
            double s = ResumeScore(world, f, results, champions, elite, asOf);
            if (champBonus.TryGetValue(f.Id, out double b)) s += b;
            return s;
        }

        // You must have won a fight (or hold a belt) to be ranked at all.
        var ranked = world.Fighters.Values
            .Where(f => f.Status != FighterStatus.Retired && (champions.Contains(f.Id) || f.Record.Wins >= 1))
            .ToList();

        world.GlobalRankings.Clear();
        foreach (var wc in Enum.GetValues<WeightClass>())
            world.GlobalRankings[wc] = ranked
                .Where(f => f.WeightClass == wc)
                .OrderByDescending(Score).ThenBy(f => f.Id.Value)
                .Take(depth).Select(f => f.Id).ToList();

        world.PoundForPound.Clear();
        world.PoundForPound.AddRange(ranked
            .OrderByDescending(Score).ThenBy(f => f.Id.Value)
            .Take(depth).Select(f => f.Id));
    }

    // --- Résumé scoring -----------------------------------------------------

    private static double ResumeScore(World world, Fighter f,
        IReadOnlyDictionary<FighterId, List<Result>> results,
        IReadOnlySet<FighterId> champions, IReadOnlySet<FighterId> elite, DateOnly asOf)
    {
        // Small baseline: the career win/loss aggregate. (No ability term: a
        // ranking is published, and it feeds the asking price — it is earned in
        // the cage and read off the record, never off ratings the fog hides.)
        double score = f.Record.Wins * 0.8 - f.Record.Losses * 0.5;

        if (!results.TryGetValue(f.Id, out var history)) return score;

        foreach (var r in history)
        {
            double recency = Recency(asOf, r.Date);
            // The opponent's quality as the sport can see it: his own public
            // record, put on the 0-100 scale the weights below were tuned on.
            double opp = OpponentLevel(world, r.Opponent, r.Stage);

            if (r.Won)
            {
                double value = 12 + opp * 0.6;                       // a win over a good fighter is worth more
                if (champions.Contains(r.Opponent)) value += 25;     // beating a champion is a statement
                else if (elite.Contains(r.Opponent)) value += 8;     // beating a ranked contender counts
                if (r.Finish) value += 4;
                score += recency * value;
            }
            else
            {
                double penalty = 10 + (100 - opp) * 0.4;             // losing to a weak fighter hurts more
                if (champions.Contains(r.Opponent) || elite.Contains(r.Opponent)) penalty *= 0.6;
                score -= recency * penalty;
            }
        }
        return score;
    }

    /// <summary>
    /// An opponent's level for ranking purposes, as the sport can see it: the
    /// stage the bout was on (<see cref="Contracts.PublicStanding.StageLevel"/>
    /// of the promotion that staged it — beating an AFC regular means more than
    /// beating a club fighter) plus or minus his own public record. The same
    /// 0-100 scale the weights below were tuned on with true ability.
    /// </summary>
    private static double OpponentLevel(World world, FighterId id, int stagePrestige) =>
        Contracts.PublicStanding.StageLevel(stagePrestige) + 8 * Contracts.PublicStanding.OpponentQuality(world, id);

    // Recent bouts count for full; form fades over roughly four years.
    private static double Recency(DateOnly asOf, DateOnly when)
    {
        double years = Math.Max(0, (asOf.DayNumber - when.DayNumber) / 365.0);
        return Math.Clamp(1.2 - 0.25 * years, 0.2, 1.2);
    }

    private readonly record struct Result(FighterId Opponent, bool Won, bool Finish, DateOnly Date, int Stage);

    // Decisive results within the form window, indexed by fighter.
    private static Dictionary<FighterId, List<Result>> GatherResults(World world, Promotion promo, DateOnly asOf)
    {
        var since = asOf.AddYears(-4);
        var map = new Dictionary<FighterId, List<Result>>();

        foreach (var ev in world.Events.Values)
        {
            if (ev.Promotion != promo.Id || ev.Date < since) continue;
            foreach (var fight in ev.Card)
            {
                if (fight.Outcome is not { Winner: { } winner, Loser: { } loser } o) continue;
                bool finish = IsFinish(o.Method);
                int stage = world.Get(ev.Promotion)?.Prestige.Value ?? 0;
                Add(map, winner, new Result(loser, true, finish, ev.Date, stage));
                Add(map, loser, new Result(winner, false, finish, ev.Date, stage));
            }
        }
        return map;

        static void Add(Dictionary<FighterId, List<Result>> m, FighterId id, Result r)
        {
            if (!m.TryGetValue(id, out var list)) m[id] = list = new List<Result>();
            list.Add(r);
        }
    }

    // Every fighter's decisive results within the form window, across all promotions.
    private static Dictionary<FighterId, List<Result>> GatherAllResults(World world, DateOnly asOf)
    {
        var since = asOf.AddYears(-4);
        var map = new Dictionary<FighterId, List<Result>>();

        foreach (var ev in world.Events.Values)
        {
            if (ev.Date < since) continue;
            foreach (var fight in ev.Card)
            {
                if (fight.Outcome is not { Winner: { } winner, Loser: { } loser } o) continue;
                bool finish = IsFinish(o.Method);
                int stage = world.Get(ev.Promotion)?.Prestige.Value ?? 0;
                Add(map, winner, new Result(loser, true, finish, ev.Date, stage));
                Add(map, loser, new Result(winner, false, finish, ev.Date, stage));
            }
        }
        return map;

        static void Add(Dictionary<FighterId, List<Result>> m, FighterId id, Result r)
        {
            if (!m.TryGetValue(id, out var list)) m[id] = list = new List<Result>();
            list.Add(r);
        }
    }

    // Quality opponents worldwide: every champion plus each division's current top contenders.
    private static (HashSet<FighterId> champions, HashSet<FighterId> elite) GlobalElite(World world)
    {
        var champions = new HashSet<FighterId>();
        var elite = new HashSet<FighterId>();
        foreach (var t in world.Titles.Values)
            if (t.CurrentChampion is { } cid) { champions.Add(cid); elite.Add(cid); }
        foreach (var promo in world.Promotions.Values)
            foreach (var div in promo.Divisions)
                foreach (var id in div.Rankings.Take(5))
                    elite.Add(id);
        return (champions, elite);
    }

    // Who counts as a "quality" opponent right now: champions and the current top contenders.
    private static (HashSet<FighterId> champions, HashSet<FighterId> elite) EliteSnapshot(World world, Promotion promo)
    {
        var champions = new HashSet<FighterId>();
        var elite = new HashSet<FighterId>();
        foreach (var div in promo.Divisions)
        {
            if (div.Title is { } tid && world.Get(tid)?.CurrentChampion is { } cid)
            {
                champions.Add(cid);
                elite.Add(cid);
            }
            foreach (var id in div.Rankings.Take(5)) elite.Add(id);
        }
        return (champions, elite);
    }

    private static bool IsFinish(FightMethod m) => m is
        FightMethod.Knockout or FightMethod.TechnicalKnockout or
        FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;

    // --- Direct edits still used by the lifecycle (retirement / belt handoff) ---

    public static void EnterAtTop(Division div, FighterId fighter, int depth = 15)
    {
        div.Rankings.Remove(fighter);
        div.Rankings.Insert(0, fighter);
        if (div.Rankings.Count > depth) div.Rankings.RemoveRange(depth, div.Rankings.Count - depth);
    }

    public static void Remove(Division div, FighterId fighter) => div.Rankings.Remove(fighter);
}
