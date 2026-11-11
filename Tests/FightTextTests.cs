using System.Text.RegularExpressions;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// The play-by-play and the stored card read true: right pronouns, the right round on the
/// decision, no doubled verbs, holds that fit the position, and a card that never says the
/// loser won. (Plan step 2; outcomes are pinned separately by the golden master.)
/// </summary>
public class FightTextTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);
    private static readonly Regex Stamp = new(@"^R(\d+) (\d+:\d\d) — ");

    private static List<(Fighter A, Fighter B, FightOutcome O, int Rounds)> Bouts(int n, Sex sex, ulong seed, Func<int, int>? rounds = null)
    {
        var gen = new FighterGenerator(seed);
        var styles = Enum.GetValues<PrimaryStyle>();
        var wc = sex == Sex.Female ? WeightClass.Flyweight : WeightClass.Lightweight;
        var engine = new FightEngine();
        var list = new List<(Fighter, Fighter, FightOutcome, int)>();
        for (int i = 0; i < n; i++)
        {
            Fighter Make(int id) => gen.Generate(new FighterId(id), Today, new GenOptions
            { Overall = 45 + (id * 13) % 40, Style = styles[id % styles.Length], Sex = sex, WeightClass = wc, WithRecord = false });
            var a = Make(2 * i + 1); var b = Make(2 * i + 2);
            int r = rounds?.Invoke(i) ?? (i % 4 == 0 ? 5 : 3);
            var o = engine.Simulate(a, b, new FightParams { ScheduledRounds = r, TextSeed = (ulong)i }, SimRandom.Mix(seed, (ulong)i));
            list.Add((a, b, o, r));
        }
        return list;
    }

    private static string Strip(string line, Fighter a, Fighter b) =>
        Stamp.Replace(line, "").Replace(a.FullName, "A").Replace(b.FullName, "B");

    [Fact]
    public void WomensBouts_UseNoMalePronouns()
    {
        var male = new Regex(@"\b(he|him|his|himself|men|man)\b", RegexOptions.IgnoreCase);
        foreach (var (a, b, o, _) in Bouts(400, Sex.Female, 11))
            foreach (var line in o.PlayByPlay!)
            {
                string t = Strip(line, a, b);
                Assert.False(male.IsMatch(t), $"male word in a women's bout: {line}");
            }
    }

    [Fact]
    public void MensBouts_UseNoFemalePronouns()
    {
        var female = new Regex(@"\b(she|her|hers|herself)\b", RegexOptions.IgnoreCase);
        foreach (var (a, b, o, _) in Bouts(300, Sex.Male, 12))
            foreach (var line in o.PlayByPlay!)
                Assert.False(female.IsMatch(Strip(line, a, b)), $"female word in a men's bout: {line}");
    }

    [Fact]
    public void EveryLine_IsStampedInARoundThatHappened_AndDecisionsInTheLast()
    {
        int decisions = 0;
        foreach (var (a, b, o, rounds) in Bouts(500, Sex.Male, 13, i => 1 + i % 5))
        {
            foreach (var line in o.PlayByPlay!)
            {
                var m = Stamp.Match(line);
                if (m.Success) Assert.InRange(int.Parse(m.Groups[1].Value), 1, o.EndRound);
            }
            if (o.Method is FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision or FightMethod.Draw)
            {
                decisions++;
                string last = o.PlayByPlay![^1];
                Assert.StartsWith($"R{rounds} 0:00 — The judges' scorecards", last);
            }
        }
        Assert.True(decisions > 50);
    }

    [Fact]
    public void NoLine_DoublesAVerbOrAWord()
    {
        var doubledLands = new Regex(@"\blands\b.*\blands\b");
        var doubledWord = new Regex(@"\b(\w+) \1\b", RegexOptions.IgnoreCase);
        foreach (var (a, b, o, _) in Bouts(400, Sex.Male, 14).Concat(Bouts(200, Sex.Female, 15)))
            foreach (var line in o.PlayByPlay!)
            {
                string t = Strip(line, a, b);
                Assert.False(doubledLands.IsMatch(t), line);
                Assert.False(doubledWord.IsMatch(t), line);
            }
    }

    [Fact]
    public void HoldTable_FitsThePosition()
    {
        string[] bottomOnly = { "triangle", "omoplata", "heel hook", "kneebar", "wrist lock" };
        foreach (GroundPos pos in Enum.GetValues<GroundPos>())
        {
            var top = Bout.HoldsFor(true, pos);
            var bottom = Bout.HoldsFor(false, pos);
            Assert.NotEmpty(top); Assert.NotEmpty(bottom);
            // A rear-naked choke needs the back: the TOP fighter at back control, nowhere else.
            Assert.Equal(pos == GroundPos.BackControl, top.Contains("rear-naked choke"));
            Assert.DoesNotContain("rear-naked choke", bottom);
            foreach (var h in bottomOnly) Assert.DoesNotContain(h, top);
            // Pinned under side control, mount or the back: no guard attacks.
            if (pos >= GroundPos.SideControl)
                foreach (var h in new[] { "triangle", "omoplata", "armbar", "heel hook", "kneebar" })
                    Assert.DoesNotContain(h, bottom);
        }
    }

    [Fact]
    public void BackHolds_OnlyAfterTakingTheBack()
    {
        // Back control is rare in today's engine (reached only by passing on from full mount),
        // so these holds are rare too; the table test above is the main guarantee.
        string[] backOnly = { "rear-naked choke", "short choke", "armbar from the back", "body-triangle choke" };
        int backLines = 0;
        foreach (var (a, b, o, _) in Bouts(3000, Sex.Male, 16))
        {
            var pbp = o.PlayByPlay!;
            backLines += pbp.Count(l => l.Contains("back control"));
            for (int i = 0; i < pbp.Count; i++)
            {
                if (!backOnly.Any(pbp[i].Contains)) continue;
                string round = pbp[i][..pbp[i].IndexOf(' ')];
                bool back = false;
                for (int k = i - 1; k >= 0 && pbp[k].StartsWith(round + " "); k--)
                    if (pbp[k].Contains("back control")) { back = true; break; }
                Assert.True(back, $"a back-only hold without the back: {pbp[i]}");
            }
        }
        Assert.True(backLines > 0, "back control never reached in the sample: the check is vacuous");
    }

    [Fact]
    public void StoredCard_NeverFavoursTheLoser()
    {
        int decisions = 0, level = 0;
        foreach (var (a, b, o, _) in Bouts(2000, Sex.Male, 17))
        {
            if (o.Method is not (FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision)) continue;
            decisions++;
            int red = o.RoundScores!.Sum(s => s.Red), blue = o.RoundScores!.Sum(s => s.Blue);
            int winnerSign = o.Winner == a.Id ? 1 : -1;
            Assert.NotEqual(-winnerSign, Math.Sign(red - blue));
            if (red == blue) level++;
        }
        Assert.True(decisions > 500);
        // No tie-break on raw merit (plan step 3): a judge with a level card votes draw, so the
        // stored card of a judged WIN is never level.
        Assert.Equal(0, level);
    }

    [Fact]
    public void Recovery_IsNeverStampedOnTheKnockdownSecond()
    {
        foreach (var (a, b, o, _) in Bouts(600, Sex.Male, 18))
        {
            var pbp = o.PlayByPlay!;
            for (int i = 1; i < pbp.Count; i++)
            {
                string t = Strip(pbp[i], a, b);
                if (!(t.Contains("has recovered") || t.Contains("cobwebs") || t.Contains("legs are back"))) continue;
                var now = Stamp.Match(pbp[i]); var before = Stamp.Match(pbp[i - 1]);
                Assert.False(before.Success && before.Value == now.Value && pbp[i - 1].Contains("hurt"),
                    $"recovery stamped on the same second as the knockdown: {pbp[i - 1]} / {pbp[i]}");
            }
        }
    }
}
