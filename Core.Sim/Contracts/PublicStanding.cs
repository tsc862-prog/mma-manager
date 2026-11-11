using System.Runtime.CompilerServices;
using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// What the world can SEE of a fighter: the stages he has fought on, what he
/// did there and against whom, the belts and rankings he holds, how famous he
/// is and how old. Everything here is read off persisted public facts (events,
/// fight outcomes, titles and their lineages, rankings, accolades, popularity),
/// never off a fighter's ratings or potential. <see cref="FighterValuation"/>
/// builds every asking price from this, so the price itself can't tell anyone
/// what fog of war hides.
///
/// The reputation is an estimate on the ability scale (0-100) — "what the
/// sport takes him to be":
/// <code>
///   read       = stage + results + honours − age − rust
///   reputation = 40 + (read − 40) × (0.35 + 0.65 × proof)
/// </code>
/// <list type="bullet">
/// <item><b>stage</b>: <see cref="StageLevel"/> of the promotions he fought for
/// in his last <see cref="RecentBouts"/> bouts (newest weighs most) blended
/// evenly with the stage he is signed to now; an unsigned fighter is blended
/// with the open market's level instead (nobody has put him on a card).</item>
/// <item><b>results</b>: how dominant his sheet is — recent bouts weighted by
/// recency and by the opponent's own public record, blended with the career —
/// up to +9 for a fighter who keeps winning, down to −5 for one who keeps
/// losing; title-fight wins and finishes add a little.</item>
/// <item><b>honours</b>: a belt he holds (5 + 0.07 × the belt's prestige), a
/// belt he held in the last four years, a top-three ranking.</item>
/// <item><b>proof</b>: how many decisive bouts back it up (full at 8). A blank
/// sheet is read mostly as an unknown.</item>
/// </list>
/// </summary>
public static class PublicStanding
{
    /// <summary>One completed pro bout as the sport saw it.</summary>
    public readonly record struct Bout(
        DateOnly Date, int StagePrestige, bool HasStage, bool Title, bool Won, bool Lost, bool Finish, FighterId Opponent);

    /// <summary>The public facts the asking price is built from.</summary>
    public sealed record Facts
    {
        /// <summary>The stage-level read, in ability points: what fighters who fight where he fights are.</summary>
        public double Stage { get; init; }
        /// <summary>What his results add to or take from the stage read, in ability points.</summary>
        public double Results { get; init; }
        /// <summary>Belts and rankings, in ability points.</summary>
        public double Honours { get; init; }
        /// <summary>How much of the read the record actually supports (0 = a blank sheet, 1 = a long career).</summary>
        public double Proof { get; init; }
        /// <summary>The public reputation: the market's estimate of his level, 0-100 on the ability scale.</summary>
        public double Reputation { get; init; }
        /// <summary>The win share the results read is built from (recent, opponent-weighted, blended with the career).</summary>
        public double WinShare { get; init; }
        /// <summary>Recent bouts the reputation was read from.</summary>
        public int BoutsRead { get; init; }
        /// <summary>Prestige of the stage he is signed to (0 for a free agent).</summary>
        public int ContractStage { get; init; }
        /// <summary>Days since his last bout (or -1 if he has none on record).</summary>
        public int DaysIdle { get; init; }
        /// <summary>He holds a belt right now.</summary>
        public bool Champion { get; init; }
    }

    /// <summary>How many recent bouts the stage and results reads look at.</summary>
    public const int RecentBouts = 8;

    /// <summary>The reputation of a blank sheet: a kid nobody has seen fight.</summary>
    public const double Unknown = 40;

    /// <summary>
    /// The ability level the market expects of a fighter who fights on a stage of
    /// this prestige. Promotions sign on what they see, so over a career a
    /// stage's typical fighter is a fair public estimate: a City hall (~15)
    /// reads 40, a Regional house (~26) 47, National (~38) 53, Continental (~52)
    /// 61, the International summit (~74) 73. Fitted to the generated world
    /// (contracted fighters' mean true ability by their promotion's prestige,
    /// day one); a public, prestige-only curve. A club show with no promotion
    /// of record reads as the floor.
    /// </summary>
    public static double StageLevel(int prestige) =>
        Math.Clamp(32 + prestige * 0.56, 30, 82);

    /// <summary>The public read of one fighter today.</summary>
    public static Facts Read(World world, Fighter f)
    {
        var date = world.CurrentDate;
        var bouts = RecentPublicBouts(world, f, RecentBouts);

        // --- Stage: where he fights. The current contract is the freshest
        // public verdict (a promotion signed him to fight at that level); the
        // stages of his recent bouts are the rest of the story. Nobody signing
        // him is a verdict too: an unsigned fighter is read halfway toward the
        // open market's floor.
        int contractStage = f.Contract is { } c && world.Get(c.Promotion) is { } cp ? cp.Prestige.Value : 0;
        double stageSum = 0, stageW = 0;
        for (int i = 0; i < bouts.Count; i++)
        {
            var b = bouts[i];
            double w = Math.Pow(0.82, i);          // the newest bout weighs most
            stageSum += w * (b.HasStage ? StageLevel(b.StagePrestige) : StageLevel(0));
            stageW += w;
        }
        if (f.Contract is not null)
        {
            double w = stageW == 0 ? 1.0 : stageW;
            stageSum += w * StageLevel(contractStage);
            stageW += w;
        }
        else if (stageW > 0)
        {
            double w = stageW * FreeAgentDrag;
            stageSum += w * StageLevel(0);
            stageW += w;
        }
        double stage = stageW > 0 ? stageSum / stageW : Unknown;

        // --- Results: how dominant the sheet is. Recent bouts count most, and
        // a win over a fighter with a long winning record counts for more
        // than one over a novice (opponents read off THEIR public sheet, never
        // their ratings, and never through their own reputation).
        double recentWon = 0, recentAll = 0, titleWins = 0, finishes = 0, wins = 0;
        for (int i = 0; i < bouts.Count; i++)
        {
            var b = bouts[i];
            if (!b.Won && !b.Lost) continue;
            double w = Math.Pow(0.85, i);
            double opp = OpponentQuality(world, b.Opponent);   // -1..1
            double weight = w * (1 + 0.35 * opp);
            recentAll += weight;
            if (b.Won)
            {
                recentWon += weight;
                wins++;
                if (b.Finish) finishes++;
                if (b.Title) titleWins++;
            }
        }
        int decisive = f.Record.Wins + f.Record.Losses;
        double career = decisive == 0 ? 0.5 : f.Record.Wins / (double)decisive;
        double share = recentAll > 0 ? 0.6 * (recentWon / recentAll) + 0.4 * career : career;
        double results = share >= 0.55
            ? DominanceBonus * Math.Pow((share - 0.55) / 0.45, 1.5)
            : -LosingPenalty * (0.55 - share) / 0.55;
        results += Math.Min(2, titleWins) * 1.0;
        if (wins >= 3) results += Math.Clamp((finishes / wins - 0.5) * 2, -0.5, 1);

        // --- Honours: a belt held, a belt held lately, a contender's ranking.
        double honours = 0;
        bool champion = false;
        foreach (var t in world.Titles.Values)
        {
            if (t.CurrentChampion == f.Id)
            {
                champion = true;
                honours = Math.Max(honours, 5 + t.Prestige.Value * 0.07);
            }
            else if (t.Lineage.Any(r => r.Champion == f.Id && r.LostOn is { } lost && lost > date.AddYears(-4)))
                honours = Math.Max(honours, 1.5 + t.Prestige.Value * 0.03);   // a recent former champion
        }
        if (!champion && f.Contract is { } cc && world.Get(cc.Promotion) is { } org
            && org.Divisions.FirstOrDefault(d => d.Matches(f)) is { } div)
        {
            int rank = div.Rankings.IndexOf(f.Id);
            if (rank is >= 0 and < 3) honours += 1.5;
        }

        // --- Age and rust: the market knows fighters slow down, and that a
        // fighter nobody has seen in a year and a half is a question mark.
        int age = f.AgeOn(date);
        double ageDrag = age > 34 ? (age - 34) * 0.6 : 0;
        int idle = bouts.Count > 0 ? date.DayNumber - bouts[0].Date.DayNumber : -1;
        double rust = idle > 450 ? Math.Min(6, (idle - 450) / 120.0) : 0;

        // --- Proof: a blank sheet is a guess; a long one is a reputation.
        double proof = Math.Min(1.0, (decisive + f.Record.Draws * 0.5) / 8.0);
        double read = stage + results + honours - ageDrag - rust;
        double reputation = Unknown + (read - Unknown) * (0.35 + 0.65 * proof);

        return new Facts
        {
            Stage = stage, Results = results, Honours = honours, Proof = proof, WinShare = share,
            Reputation = Math.Clamp(reputation, 20, 95),
            BoutsRead = bouts.Count, ContractStage = contractStage, DaysIdle = idle, Champion = champion,
        };
    }

    /// <summary>How hard an unsigned fighter's stage read is pulled toward the open market's floor (weight relative to his bouts).</summary>
    public const double FreeAgentDrag = 0.5;

    /// <summary>The most a dominant sheet adds to the stage read (a near-perfect win share).</summary>
    public const double DominanceBonus = 9;

    /// <summary>The most a losing sheet takes off the stage read (a winless one).</summary>
    public const double LosingPenalty = 5;

    /// <summary>The public reputation alone (see <see cref="Read"/>).</summary>
    public static double Reputation(World world, Fighter f) => Read(world, f).Reputation;

    // An opponent's public quality from his own sheet, -1 (a losing novice) to
    // +1 (a long winning record): no ratings, and no recursion into his own read.
    private static double OpponentQuality(World world, FighterId id)
    {
        if (world.Get(id) is not { } o) return 0;
        int dec = o.Record.Wins + o.Record.Losses;
        if (dec == 0) return -0.3;
        double pct = o.Record.Wins / (double)dec;
        double depth = Math.Min(1.0, dec / 12.0);
        return Math.Clamp((pct - 0.5) * 2 * depth, -1, 1);
    }

    /// <summary>His most recent completed pro bouts, newest first, as the public saw them.</summary>
    public static List<Bout> RecentPublicBouts(World world, Fighter f, int max)
    {
        var list = new List<Bout>(Math.Min(max, f.BoutHistory.Count));
        var index = IndexFor(world);
        for (int i = f.BoutHistory.Count - 1; i >= 0 && list.Count < max; i--)
        {
            if (!index.TryGet(world, f.BoutHistory[i], out var ev, out var fight)) continue;
            if (fight.Outcome is not { } o) continue;
            var promo = world.Get(ev.Promotion);
            list.Add(new Bout(
                ev.Date,
                promo?.Prestige.Value ?? 0,
                promo is not null,
                fight.IsTitleFight || fight.Title is not null,
                o.Winner == f.Id,
                o.Loser == f.Id,
                o.Method is FightMethod.Knockout or FightMethod.TechnicalKnockout
                    or FightMethod.SubmissionTap or FightMethod.TechnicalSubmission,
                fight.RedCorner == f.Id ? fight.BlueCorner : fight.RedCorner));
        }
        // Bout history is appended in fight order; sort on date to be sure (a
        // stable sort, so same-day bouts keep that order).
        return list.OrderByDescending(b => b.Date).ToList();
    }

    // --- fight -> event index ---------------------------------------------------
    // A fighter's history is a list of fight ids; the event (date, promotion) is
    // found through a per-world index, filled lazily. A completed or cancelled
    // event never changes again, so it is indexed once and skipped after; a
    // lookup that misses rescans only the events still open. Pure cache: it
    // never feeds back into the world and holds no state a save needs.
    private static readonly ConditionalWeakTable<World, BoutIndex> Indexes = new();

    private static BoutIndex IndexFor(World world) => Indexes.GetValue(world, _ => new BoutIndex());

    private sealed class BoutIndex
    {
        private readonly Dictionary<FightId, (Event Ev, Fight Fight)> _map = new();
        private readonly HashSet<EventId> _sealed = new();
        private readonly HashSet<FightId> _missing = new();
        private int _eventCountAtLastScan = -1;

        public bool TryGet(World world, FightId id, out Event ev, out Fight fight)
        {
            lock (this)
            {
                if (_map.TryGetValue(id, out var hit) && world.Events.ContainsKey(hit.Ev.Id))
                {
                    (ev, fight) = hit;
                    return true;
                }
                // A fight already looked for and not found is only looked for
                // again once the world has a different number of events.
                if (!_missing.Contains(id) || world.Events.Count != _eventCountAtLastScan)
                {
                    Scan(world);
                    if (_map.TryGetValue(id, out hit))
                    {
                        _missing.Remove(id);
                        (ev, fight) = hit;
                        return true;
                    }
                    _missing.Add(id);
                }
                ev = null!; fight = null!;
                return false;
            }
        }

        private void Scan(World world)
        {
            foreach (var e in world.Events.Values)
            {
                if (_sealed.Contains(e.Id)) continue;
                foreach (var fi in e.Card) _map[fi.Id] = (e, fi);
                if (e.Status != EventStatus.Scheduled) _sealed.Add(e.Id);
            }
            _eventCountAtLastScan = world.Events.Count;
        }
    }
}
