using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Engine;

/// <summary>
/// Balance-testing harness: run the same matchup many times and report how the
/// results split. This is the whole reason the sim is headless — "sim these two
/// 10,000 times, does the favourite win about the right share?"
/// </summary>
public static class FightLab
{
    public readonly record struct WinSplit(
        int Fights,
        int RedWins, int BlueWins, int Draws,
        int Kos, int Tkos, int Submissions, int Decisions,
        double AvgEndRound)
    {
        public double RedWinPct => Fights == 0 ? 0 : 100.0 * RedWins / Fights;
        public double BlueWinPct => Fights == 0 ? 0 : 100.0 * BlueWins / Fights;
        public double FinishPct => Fights == 0 ? 0 : 100.0 * (Kos + Tkos + Submissions) / Fights;
    }

    public static WinSplit Compare(Fighter red, Fighter blue, int fights, ulong seed, FightParams? options = null)
    {
        var engine = new FightEngine();
        var opts = options ?? FightParams.FastThreeRound;

        int rw = 0, bw = 0, dr = 0, ko = 0, tko = 0, sub = 0, dec = 0;
        long endRoundSum = 0;

        for (int i = 0; i < fights; i++)
        {
            // Each fight gets its own deterministic sub-stream off the base seed.
            var rng = new SimRandom(SimRandom.Mix(seed, (ulong)i));
            var o = engine.Simulate(red, blue, opts, rng);

            if (o.Winner == red.Id) rw++;
            else if (o.Winner == blue.Id) bw++;
            else dr++;

            switch (o.Method)
            {
                case FightMethod.Knockout: ko++; break;
                case FightMethod.TechnicalKnockout: tko++; break;
                case FightMethod.SubmissionTap or FightMethod.TechnicalSubmission: sub++; break;
                default: dec++; break;
            }
            endRoundSum += o.EndRound;
        }

        return new WinSplit(fights, rw, bw, dr, ko, tko, sub, dec,
            fights == 0 ? 0 : (double)endRoundSum / fights);
    }
}
