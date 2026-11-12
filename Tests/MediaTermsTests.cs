using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;

namespace MmaManager.Tests;

/// <summary>
/// The card-quality clauses in TV deals are written relative to one measured
/// number, <see cref="MediaService.CardQualityPar"/>. These pin the ladder to it
/// and the par to the engine, so an engine change that moves how fights rate,
/// or a quiet edit of a threshold, fails here instead of silently making every
/// broadcast deal unkeepable (the AFC's 50 floor was missed on 56% of its cards).
/// </summary>
public class MediaTermsTests : IClassFixture<FightBenchmarkFixture>
{
    private readonly FightBenchmarkFixture _fx;
    public MediaTermsTests(FightBenchmarkFixture fx) => _fx = fx;

    [Fact]
    public void ThePar_TracksTheEnginesCardQuality()
    {
        // The benchmark's synthetic 12-bout cards (11 x 3 rounds + a 5-round main),
        // averaged the way MinCardQuality reads a card. In-world International cards
        // ran 48.9 against a synthetic 49.6 on the #37 engine. If this fails, the
        // engine moved: re-measure (longsim compare prints card quality by tier)
        // and move CardQualityPar with it.
        double median = _fx.Report.CardQualityDist[3];
        Assert.InRange(median, MediaService.CardQualityPar - 2.5, MediaService.CardQualityPar + 2.5);
    }

    [Fact]
    public void TheLadder_WritesQualityFloorsRelativeToThePar()
    {
        int checkedDeals = 0;
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
            foreach (var deal in world.Promotions.Values.SelectMany(p => p.MediaDeals))
            {
                int? floor = deal.Requirements.FirstOrDefault(r => r.Kind == DealRequirementKind.MinCardQuality)?.Threshold;
                string tag = MediaService.TagOf(deal.Partner);
                int strictness = tag is "flagship" or "premium" ? 1 : tag == "standard" ? 0 : -1;
                int? rung = deal.Kind switch
                {
                    MediaKind.PrimeBroadcast => MediaService.CardQualityPar - 7,
                    MediaKind.CableSports => MediaService.CardQualityPar - 12,
                    _ => null,
                };
                if (rung is null || strictness < 0) Assert.Null(floor);   // no quality clause: other rungs, easy-going partners
                else Assert.Equal(rung + (strictness > 0 ? 2 : 0), floor);
                checkedDeals++;
            }
        }
        Assert.True(checkedDeals > 10);
    }

    [Fact]
    public void TheStrictestPrimeTimeFloor_IsWhatTheTopCardsClearAbout80To85PercentOfTheTime()
    {
        // The target the par was set for, stated against the measured International
        // distribution on the #37 engine: 86.4% of cards clear 42, 72.9% clear 45.
        int strictest = MediaService.CardQualityPar - 7 + 2;
        Assert.InRange(strictest, 42, 44);
    }
}
