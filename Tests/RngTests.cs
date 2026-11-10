using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

public class RngTests
{
    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        var a = new SimRandom(12345);
        var b = new SimRandom(12345);
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        var a = new SimRandom(1);
        var b = new SimRandom(2);
        Assert.NotEqual(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void NextDouble_StaysInUnitInterval()
    {
        var r = new SimRandom(99);
        for (int i = 0; i < 10_000; i++)
        {
            double d = r.NextDouble();
            Assert.InRange(d, 0.0, 0.9999999999);
        }
    }

    [Fact]
    public void Range_RespectsInclusiveBounds()
    {
        var r = new SimRandom(7);
        for (int i = 0; i < 10_000; i++)
            Assert.InRange(r.Range(3, 8), 3, 8);
    }

    [Fact]
    public void WeightedIndex_NeverPicksZeroWeightBucket()
    {
        var r = new SimRandom(42);
        double[] weights = { 1.0, 0.0, 2.0 };
        for (int i = 0; i < 5_000; i++)
            Assert.NotEqual(1, r.WeightedIndex(weights));
    }

    [Fact]
    public void Rating_ClampsToRange()
    {
        Assert.Equal(0, new Rating(-50).Value);
        Assert.Equal(100, new Rating(250).Value);
        Assert.Equal(63, new Rating(63).Value);
    }
}
