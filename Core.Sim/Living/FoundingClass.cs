using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// A new promotion's FOUNDING CLASS: the local kids who turn up to its open
/// tryouts when it announces itself. A handful of fresh prospects per division
/// it plans to run, from its home region and mostly its home metro — green
/// (0-0, 18-23), cheap, mostly ordinary with the occasional real talent, and
/// fogged like any unknown (nothing about them is public but where they're
/// from). They go into the free-agent pool with the founding house as their
/// discoverer, so they deal with it at the gratitude rate
/// (<see cref="Contracts.Negotiator.GratitudeFactor"/>) while anyone else pays
/// the open market's price.
///
/// A class alone can't launch a promotion: it is
/// <see cref="PerDivision"/> a division, under the
/// <see cref="WorldEvents.StartupMinPerDivision"/> a division needs, so a
/// startup still has to sign real fighters off the market to open its doors.
///
/// RNG: everything comes off a stream keyed on (world seed, promotion id,
/// date) — never the shared weekly stream — and the generator's own per-id
/// streams. A launch that falls through takes its class back out of the world.
/// </summary>
public static class FoundingClass
{
    /// <summary>Prospects per division the startup plans to open.</summary>
    public const int PerDivision = 3;

    /// <summary>The share of a class that is a real talent (a higher starting level; the potential is the generator's).</summary>
    public const double TalentShare = 0.10;

    private const ulong Salt = 0xF0_0D_C1A5_5UL;

    /// <summary>Generate the class for <paramref name="promo"/>'s planned divisions and add it to the world as free agents.</summary>
    public static List<Fighter> Generate(World world, Promotion promo, Region home, IReadOnlyList<WeightClass> classes, DateOnly date)
    {
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed,
            (ulong)promo.Id.Value * 0x9E37_79B9UL ^ (ulong)date.DayNumber ^ Salt));
        var gen = FighterGenerator.ForWorld(world);
        var anchor = Geography.WorldMap.AnchorCity(world, home);
        var made = new List<Fighter>();

        foreach (var wc in classes)
        {
            for (int i = 0; i < PerDivision; i++)
            {
                bool talent = rng.Chance(TalentShare);
                var id = world.NewFighterId();
                var f = gen.Generate(id, date, new GenOptions
                {
                    WeightClass = wc,
                    Sex = Sex.Male,
                    Nationality = home,
                    Age = rng.Range(18, 23),
                    Overall = talent ? rng.Range(48, 58) : rng.GaussianInt(38, 6, 26, 50),
                    WithRecord = false,   // a debut is 0-0: the record is written in the cage from here
                });
                gen.AssignHometown(world, f);
                // Most of the class came up in the founding metro's gyms.
                if (anchor is not null && f.Nationality == home && rng.Chance(0.7) && f.HomeCity != anchor.Id)
                {
                    if (f.HomeCity is { } old) f.CityReputation.Remove(old);
                    f.HomeCity = anchor.Id;
                    f.CityReputation[anchor.Id] = Math.Min(60, f.Intangibles.Popularity.Value);
                }
                f.Momentum = 0;
                f.DiscoveredBy = promo.Id;
                f.Accolades.Add(new Accolade(date, $"Founding class — tried out for {promo.Name}"));
                world.Fighters[id] = f;
                gen.EnsureUniqueName(world, f);
                Camps.JoinLocalCamp(world, f, rng);
                made.Add(f);
            }
        }
        return made;
    }

    /// <summary>Take an unsigned class back out of the world (the launch fell through: there were no tryouts).</summary>
    public static void Withdraw(World world, IEnumerable<Fighter> cls)
    {
        foreach (var f in cls)
            if (f.Contract is null) world.Fighters.Remove(f.Id);
    }
}
