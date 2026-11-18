using System.Security.Cryptography;
using System.Text;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using Xunit.Abstractions;

namespace MmaManager.Tests;

/// <summary>
/// A one-night bracket asks for the size term on the card bout's rule (<see cref="EventService.SizeTermFor"/>): in
/// Origins, an open-field bracket or a class inside a banded division. Before, only a card bout asked, so the last
/// bracket night of a house that had just split by weight (the one already on the books, run as announced) was fought
/// with no size term at all: its rules are no longer open weight. Today's mode never sets it.
/// </summary>
public class BracketSizeTermTests(ITestOutputHelper output)
{
    /// <summary>An Origins observer world whose bracket house (the Crucible) has drawn its first weight line.</summary>
    private static (World W, Promotion House) SplitBracketHouse(ulong seed)
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(seed));
        var house = w.Promotions.Values.First(p => p.Rules.ShowFormat == ShowFormat.OneNightTournament);
        OriginsReforms.DrawLines(w, house, OriginsReforms.LinesOf(OriginsReforms.Ladder[0]), w.CurrentDate, announce: false);
        Assert.False(house.Rules.Fight!.OpenWeight);              // the house's rules weigh in now
        Assert.DoesNotContain(house.Divisions, d => d.Open && d.Sex == Sex.Male);
        return (w, house);
    }

    /// <summary>Only these men can be drawn into the field: every other man has no fight left on his deal.</summary>
    private static List<Fighter> OnlyThese(World w, Promotion house, int n)
    {
        var men = w.RosterOf(house.Id).Where(f => f.Sex == Sex.Male && OriginsShows.CanBook(w, house, f, w.CurrentDate))
            .OrderBy(f => f.Id.Value).ToList();
        Assert.True(men.Count >= n, $"{house.Abbreviation} has {men.Count} bookable men");
        foreach (var f in men.Skip(n)) f.Contract!.FightsRemaining = 0;
        return men.Take(n).ToList();
    }

    private static void Ready(Fighter f)
    {
        f.Status = FighterStatus.Active;
        f.SuspendedUntil = null;
        f.CurrentInjury = null;
        f.ReadyToFightOn = null;
        f.Contract!.FightsRemaining = 5;
    }

    [Fact]
    public void ABracketAtASplitHouse_GivesTheBiggerManHisEdge()
    {
        // One bout a night (a two-man bracket) between the same two men, the heavy and the light role swapped every
        // night so skill cancels. Fought under the house's own rules, now not open weight: only the bout's flag
        // turns the size term on. The same nights with the flag off (the world read as today's mode) are a coin.
        double BigWins(bool origins)
        {
            var (w, house) = SplitBracketHouse(7);
            var men = OnlyThese(w, house, 2);
            if (!origins) w.Mode = WorldMode.Modern;
            int big = 0, n = 0;
            for (int i = 0; i < 300; i++)
            {
                var heavy = men[i % 2]; var light = men[1 - i % 2];
                heavy.NaturalWeightLbs = 249; light.NaturalWeightLbs = 170;
                foreach (var f in men) Ready(f);
                var night = new TournamentService().RunOneNight(w, house.Id, 2, w.CurrentDate, Money.Zero, wc: null, rules: null);
                Assert.NotNull(night);
                var bout = Assert.Single(night!.Event.Card);
                Assert.True(bout.OpenWeight);
                n++;
                if (bout.Outcome!.Winner == heavy.Id) big++;
            }
            return 100.0 * big / n;
        }
        double on = BigWins(origins: true), off = BigWins(origins: false);
        output.WriteLine($"bigger man's win %: Origins {on:0.0}, flag off {off:0.0}");
        Assert.True(on > off + 12, $"bigger man's win %: Origins {on:0.0}, flag off {off:0.0}");
        Assert.True(on > 62, $"bigger man's win % {on:0.0}");
        Assert.InRange(off, 35.0, 65.0);
    }

    /// <summary>Every bout of the night, result and stats (not the commentary).</summary>
    private static string NightPrint(OneNightResult night) =>
        string.Join("|", night.Event.Card.Select(f =>
        {
            var o = f.Outcome!;
            string S(FighterId id) { var s = o.Stats[id]; return $"{s.SignificantStrikesLanded}/{s.SignificantStrikesAttempted}/{s.TakedownsLanded}/{s.SubmissionAttempts}/{s.KnockdownsScored}/{s.ControlTimeSeconds}"; }
            return $"{f.RedCorner.Value}-{f.BlueCorner.Value}:{o.Winner?.Value}:{o.Method}:{o.EndRound}:{o.EndTimeSeconds}:{S(f.RedCorner)}:{S(f.BlueCorner)}";
        })) + $"#{night.Champion?.Id.Value}";

    [Fact]
    public void ASameSizeBracket_UnderTodaysRules_IsUnaffected()
    {
        // Eight men of one natural weight at the split house, fought under today's rules: the opening round on the
        // modern path, the later rounds on the custom path (carried condition). The flag adds only the relative edge,
        // which is exactly nothing between men of one size, on both paths: the night is the same bout for bout.
        string Night(bool origins)
        {
            var (w, house) = SplitBracketHouse(7);
            foreach (var f in OnlyThese(w, house, 8)) f.NaturalWeightLbs = 205;
            if (!origins) w.Mode = WorldMode.Modern;
            var night = new TournamentService().RunOneNight(w, house.Id, 8, w.CurrentDate, Money.Zero, wc: null, rules: FightRules.Modern);
            Assert.NotNull(night);
            Assert.Contains(night!.Night.Bouts, b => b.Round > 1 && b.Outcome is not null);   // the carried path ran
            return NightPrint(night);
        }
        Assert.Equal(Night(origins: false), Night(origins: true));
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16];

    [Fact]
    public void TodaysModeBrackets_AreByteIdenticalToBefore()
    {
        // Pinned on main before the change (3da347c): an open-weight and a class bracket at today's top house.
        string Night(WeightClass? wc)
        {
            var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
            var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
            var night = new TournamentService().RunOneNight(world, promo.Id, 8, world.CurrentDate, new Money(50_000m), wc, rules: null);
            Assert.NotNull(night);
            return Hash(NightPrint(night!));
        }
        Assert.Equal("B7446ED2C6E6F1F0", Night(null));
        Assert.Equal("804412391F03436B", Night(WeightClass.Lightweight));
    }
}
