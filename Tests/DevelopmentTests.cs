using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

public class DevelopmentTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    private static void AdvanceMonths(Fighter f, int months, ulong seed = 1)
    {
        var rng = new SimRandom(seed);
        for (int m = 0; m < months; m++)
            FighterDevelopment.Advance(f, Start.AddMonths(m), rng);
    }

    [Fact]
    public void YoungProspect_ClimbsTowardPotential()
    {
        var f = new FighterGenerator(10).Generate(new FighterId(1), Start, new GenOptions { Age = 20 });
        Assume(f.PotentialAbility.Value > FighterAbility.CurrentAbility(f));  // headroom to grow

        int before = FighterAbility.CurrentAbility(f);
        AdvanceMonths(f, 24);
        int after = FighterAbility.CurrentAbility(f);

        Assert.True(after > before, $"young fighter didn't grow: {before} -> {after}");
        Assert.True(after <= f.PotentialAbility.Value, $"grew past potential: {after} > {f.PotentialAbility.Value}");
    }

    [Fact]
    public void Development_NeverExceedsPotential_OverALongCareer()
    {
        var f = new FighterGenerator(11).Generate(new FighterId(1), Start, new GenOptions { Age = 19 });
        AdvanceMonths(f, 120);   // ten years
        Assert.True(FighterAbility.CurrentAbility(f) <= f.PotentialAbility.Value);
    }

    [Fact]
    public void AgingVeteran_Declines()
    {
        var f = new FighterGenerator(12).Generate(new FighterId(1), Start, new GenOptions { Age = 37, Overall = 75 });
        int before = FighterAbility.CurrentAbility(f);
        AdvanceMonths(f, 24);
        Assert.True(FighterAbility.CurrentAbility(f) < before, "veteran didn't decline");
    }

    [Fact]
    public void RetiredFighters_DoNotDevelop()
    {
        var f = new FighterGenerator(13).Generate(new FighterId(1), Start, new GenOptions { Age = 21 });
        f.Status = FighterStatus.Retired;
        int before = FighterAbility.CurrentAbility(f);
        AdvanceMonths(f, 24);
        Assert.Equal(before, FighterAbility.CurrentAbility(f));
    }

    // xUnit has no built-in Assume; treat an unmet precondition as a hard signal
    // so we notice if the generator ever stops giving prospects headroom.
    private static void Assume(bool condition) => Assert.True(condition, "test precondition not met");
}
