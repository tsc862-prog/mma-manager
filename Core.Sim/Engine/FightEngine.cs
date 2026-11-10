using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Engine;

/// <summary>
/// The fight engine: two fighters in, a <see cref="FightOutcome"/> out. Stateless
/// and headless — all randomness flows through the supplied <see cref="SimRandom"/>,
/// so a given seed reproduces a bout exactly. The per-fight state lives in
/// <see cref="Bout"/>; this type is just the entry point and world glue.
/// </summary>
public sealed class FightEngine
{
    /// <summary>Simulate a bout between two fighters with an explicit RNG.</summary>
    public FightOutcome Simulate(Fighter red, Fighter blue, FightParams options, SimRandom rng) =>
        new Bout(red, blue, options, rng).Run();

    /// <summary>Simulate with a bare seed (convenience for tests / mass sims).</summary>
    public FightOutcome Simulate(Fighter red, Fighter blue, FightParams options, ulong seed) =>
        Simulate(red, blue, options, new SimRandom(seed));

    /// <summary>
    /// Simulate a booked <see cref="Fight"/> within a world, resolving corners
    /// from the roster, deriving a reproducible seed from the world seed + fight
    /// id, and writing the result back onto the fight. Returns the outcome.
    /// </summary>
    public FightOutcome Simulate(Fight fight, World world, FightParams? options = null)
    {
        var red = world.Get(fight.RedCorner) ?? throw new InvalidOperationException($"Red corner {fight.RedCorner} not in world.");
        var blue = world.Get(fight.BlueCorner) ?? throw new InvalidOperationException($"Blue corner {fight.BlueCorner} not in world.");

        var opts = options ?? new FightParams { ScheduledRounds = fight.ScheduledRounds };
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, 0x1234_5678_0000UL ^ (ulong)fight.Id.Value));

        var outcome = Simulate(red, blue, opts, rng);
        fight.Outcome = outcome;
        return outcome;
    }
}
