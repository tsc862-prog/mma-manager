using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Engine;

public static partial class FightLab
{
    /// <summary>
    /// The engine's golden master: two hex hashes over a fixed set of bouts.
    /// <list type="bullet">
    /// <item><see cref="Results"/> — per bout: who won (red / blue / nobody), the method,
    /// the end round, the end time and both fighters' statlines. ANY change to who wins
    /// or how moves it.</item>
    /// <item><see cref="Scorecards"/> — per bout: the stored <see cref="FightOutcome.RoundScores"/>
    /// card, round by round. Kept separate so a change to which card is stored (but not
    /// to who won) can prove the results are untouched.</item>
    /// </list>
    /// </summary>
    public readonly record struct GoldenHashes(string Results, string Scorecards, int Bouts);

    /// <summary>Bouts in the golden set the tests pin.</summary>
    public const int GoldenBouts = 4000;

    /// <summary>
    /// Run the golden set and hash it. The fighters are built straight from a fixed
    /// <see cref="SimRandom"/> stream (not through <c>FighterGenerator</c>), so a change
    /// to generation, names or the designed roster never moves these hashes — only a
    /// change to the engine itself can. Text is off: the hashes describe outcomes,
    /// never the play-by-play wording (a separate test proves text on/off agree).
    /// </summary>
    public static GoldenHashes GoldenMaster(int bouts = GoldenBouts, bool playByPlay = false)
    {
        var pool = GoldenFighters(400);
        var rng = new SimRandom(0x601D_3A57_E200UL);
        var engine = new FightEngine();
        var results = new Fnv();
        var cards = new Fnv();

        for (int i = 0; i < bouts; i++)
        {
            // The pairing and rules come off the golden stream; the bout itself gets
            // its own sub-stream, exactly as a booked fight does.
            int r = rng.Next(0, pool.Count);
            int b = rng.Next(0, pool.Count - 1);
            if (b >= r) b++;
            int rounds = rng.Chance(0.2) ? 5 : 3;
            int roundSeconds = rng.Next(0, 10) switch { 0 => 180, 1 => 240, _ => 300 };
            var p = new FightParams { ScheduledRounds = rounds, RoundSeconds = roundSeconds, GeneratePlayByPlay = playByPlay };
            var o = engine.Simulate(pool[r], pool[b], p, SimRandom.Mix(0x601D_B007UL, (ulong)i));
            HashResult(results, o, pool[r].Id, pool[b].Id);
            HashCard(cards, o);
        }
        return new GoldenHashes(results.Hex, cards.Hex, bouts);
    }

    internal static void HashResult(Fnv h, FightOutcome o, FighterId red, FighterId blue)
    {
        h.Add(o.Winner == red ? 0 : o.Winner == blue ? 1 : 2);
        h.Add((int)o.Method);
        h.Add(o.EndRound);
        h.Add(o.EndTimeSeconds);
        foreach (var id in new[] { red, blue })
        {
            var s = o.Stats[id];
            h.Add(s.SignificantStrikesLanded); h.Add(s.SignificantStrikesAttempted);
            h.Add(s.TakedownsLanded); h.Add(s.TakedownsAttempted);
            h.Add(s.SubmissionAttempts); h.Add(s.KnockdownsScored); h.Add(s.ControlTimeSeconds);
        }
    }

    internal static void HashCard(Fnv h, FightOutcome o)
    {
        var rs = o.RoundScores;
        h.Add(rs?.Count ?? -1);
        if (rs is null) return;
        foreach (var s in rs) { h.Add(s.Red); h.Add(s.Blue); }
    }

    /// <summary>A fixed pool of varied fighters, built without the generator (see <see cref="GoldenMaster"/>).</summary>
    internal static List<Fighter> GoldenFighters(int n)
    {
        var rng = new SimRandom(0x601D_F16E_0000UL);
        var list = new List<Fighter>(n);
        var styles = Enum.GetValues<PrimaryStyle>();
        for (int i = 0; i < n; i++)
        {
            bool woman = rng.Chance(0.15);
            var f = new Fighter
            {
                Id = new FighterId(900_000 + i),
                FirstName = "Golden",
                LastName = $"Fighter{i}",
                BirthDate = new DateOnly(1998, 1, 1),
                Sex = woman ? Sex.Female : Sex.Male,
                Stance = (Stance)rng.Next(0, 3),
                HeightCm = rng.Range(155, 195),
                WeightClass = (WeightClass)rng.Next(0, woman ? 4 : 9),
                PrimaryStyle = styles[rng.Next(0, styles.Length)],
            };
            f.ReachCm = f.HeightCm + rng.Range(-6, 10);
            int level = rng.Range(35, 85);
            foreach (var a in Attributes) a.Set(f, Roll(rng, level, 12));
            foreach (var t in TendencyNames) SetTendency(f, t, rng.Range(10, 90));
            f.Intangibles.Popularity = rng.Range(1, 99);
            list.Add(f);
        }
        return list;
    }

    private static int Roll(SimRandom rng, int mean, double sd) =>
        Math.Clamp((int)Math.Round(rng.NextGaussian(mean, sd)), 1, 99);

    /// <summary>64-bit FNV-1a over a stream of ints. Stable across platforms and runs.</summary>
    internal sealed class Fnv
    {
        private ulong _h = 0xCBF29CE484222325UL;
        public void Add(int v)
        {
            unchecked
            {
                for (int k = 0; k < 4; k++)
                {
                    _h ^= (byte)(v >> (8 * k));
                    _h *= 0x100000001B3UL;
                }
            }
        }
        public string Hex => _h.ToString("x16");
    }
}
