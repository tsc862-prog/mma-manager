using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Generation;

namespace MmaManager.Tests;

public class GeneratorTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);

    [Fact]
    public void SameSeedAndId_ProducesIdenticalFighter()
    {
        var g1 = new FighterGenerator(555);
        var g2 = new FighterGenerator(555);
        var f1 = g1.Generate(new FighterId(42), Today);
        var f2 = g2.Generate(new FighterId(42), Today);

        Assert.Equal(f1.FullName, f2.FullName);
        Assert.Equal(f1.WeightClass, f2.WeightClass);
        Assert.Equal(f1.PrimaryStyle, f2.PrimaryStyle);
        Assert.Equal(f1.Striking.PunchPower.Value, f2.Striking.PunchPower.Value);
        Assert.Equal(f1.PotentialAbility.Value, f2.PotentialAbility.Value);
        Assert.Equal(f1.Record.ToString(), f2.Record.ToString());
    }

    [Fact]
    public void GeneratedRoster_HasNoDuplicateNames()
    {
        // A deep, deterministic world: every non-local fighter's first+last is unique.
        var world = new MmaManager.Core.Sim.Booking.LeagueBuilder()
            .Build(new MmaManager.Core.Sim.Booking.LeagueBuilder.Options { Seed = 4, RosterSize = 400 });

        var names = world.Fighters.Values
            .Where(f => !f.LocalTalent)
            .Select(f => $"{f.FirstName}|{f.LastName}".ToLowerInvariant())
            .ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void EnsureUniqueName_RenamesACollider_ButLeavesTheFieldAlone()
    {
        var world = new MmaManager.Core.Sim.Booking.LeagueBuilder()
            .Build(new MmaManager.Core.Sim.Booking.LeagueBuilder.Options { Seed = 6, RosterSize = 40 });
        var gen = FighterGenerator.ForWorld(world);
        var existing = world.Fighters.Values.First(f => !f.LocalTalent);

        // A newcomer deliberately clashing with someone already in the world.
        var clash = gen.Generate(new FighterId(99_999), Today);
        clash.FirstName = existing.FirstName;
        clash.LastName = existing.LastName;
        world.Fighters[clash.Id] = clash;

        gen.EnsureUniqueName(world, clash);

        Assert.NotEqual($"{existing.FirstName}|{existing.LastName}",
                        $"{clash.FirstName}|{clash.LastName}");   // the collider was renamed
        Assert.Contains(world.Fighters.Values, f => f.Id == existing.Id
            && f.FirstName == existing.FirstName && f.LastName == existing.LastName);   // the original is untouched
    }

    [Fact]
    public void PotentialAbility_NeverBelowCurrentAbility()
    {
        var g = new FighterGenerator(1);
        for (int id = 1; id <= 500; id++)
        {
            var f = g.Generate(new FighterId(id), Today);
            Assert.True(f.PotentialAbility.Value >= FighterAbility.CurrentAbility(f),
                $"Fighter {id}: PA {f.PotentialAbility.Value} < CA {FighterAbility.CurrentAbility(f)}");
        }
    }

    [Fact]
    public void Wrestlers_LookLikeWrestlers_OnAverage()
    {
        var g = new FighterGenerator(2);
        double wrestlerTd = 0, strikerTd = 0;
        int n = 200;
        for (int i = 0; i < n; i++)
        {
            var w = g.Generate(new FighterId(i + 1), Today, new GenOptions { Style = PrimaryStyle.Wrestler, Overall = 70 });
            var s = g.Generate(new FighterId(i + 1_000), Today, new GenOptions { Style = PrimaryStyle.Striker, Overall = 70 });
            wrestlerTd += w.Grappling.TakedownOffense.Value;
            strikerTd += s.Grappling.TakedownOffense.Value;
        }
        // A wrestler's takedown offense should clearly exceed a striker's.
        Assert.True(wrestlerTd / n > strikerTd / n + 15,
            $"wrestler TD avg {wrestlerTd / n:0.0} vs striker {strikerTd / n:0.0}");
    }

    [Fact]
    public void PinnedOptions_AreHonoured()
    {
        var g = new FighterGenerator(3);
        var f = g.Generate(new FighterId(1), Today, new GenOptions
        {
            WeightClass = WeightClass.Heavyweight,
            Style = PrimaryStyle.BrazilianJiuJitsu,
            Nationality = Region.Asia,
            Age = 24,
        });
        Assert.Equal(WeightClass.Heavyweight, f.WeightClass);
        Assert.Equal(PrimaryStyle.BrazilianJiuJitsu, f.PrimaryStyle);
        Assert.Equal(Region.Asia, f.Nationality);
        Assert.Equal(24, f.AgeOn(Today));
    }

    [Fact]
    public void Populate_AddsFightersToWorldAsFreeAgents()
    {
        var world = new World { CurrentDate = Today, RngSeed = 10 };
        var gen = FighterGenerator.ForWorld(world);
        var made = gen.Populate(world, 50);

        Assert.Equal(50, made.Count);
        Assert.Equal(50, world.Fighters.Count);
        Assert.All(made, f => Assert.Null(f.Contract));
        Assert.Equal(50, world.FreeAgents().Count());
    }
}
