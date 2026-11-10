using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// Stands up a ready-to-book world with a single promotion: a signed roster
/// spread across weight classes, a division per class with ability-seeded
/// contender rankings, and a belt in each division held by its top fighter.
/// Handy for focused tests and demos; see <see cref="WorldBuilder"/> for a full
/// multi-promotion world. Deterministic from the world seed.
/// </summary>
public sealed class LeagueBuilder
{
    public sealed record Options
    {
        public DateOnly StartDate { get; init; } = new(2026, 1, 1);
        public ulong Seed { get; init; } = 1;
        public string PromotionName { get; init; } = "Apex Fighting Championship";
        public string Abbreviation { get; init; } = "AFC";
        public Region BaseRegion { get; init; } = Region.NorthAmerica;
        public int Prestige { get; init; } = 62;
        public int RosterSize { get; init; } = 120;
        public Money StartingBalance { get; init; } = new(5_000_000m);

        /// <summary>Weight classes the promotion runs; null => all of them.</summary>
        public WeightClass[]? Divisions { get; init; }

        /// <summary>How many contenders to rank per division (excludes the champion).</summary>
        public int RankingsDepth { get; init; } = 15;
    }

    public World Build(Options? options = null)
    {
        var o = options ?? new Options();
        var world = new World { CurrentDate = o.StartDate, RngSeed = o.Seed };
        var rng = new SimRandom(SimRandom.Mix(o.Seed, 0xB00C_1234UL));

        var promo = new Promotion
        {
            Id = world.NewPromotionId(),
            Name = o.PromotionName,
            Abbreviation = o.Abbreviation,
            BaseRegion = o.BaseRegion,
            IsPlayerControlled = true,
            Balance = o.StartingBalance,
            Prestige = o.Prestige,
        };
        world.Promotions[promo.Id] = promo;
        world.PlayerPromotion = promo.Id;

        var classes = o.Divisions ?? Enum.GetValues<WeightClass>();

        // Generate the roster (as free agents) and sign everyone to the promotion.
        var gen = new FighterGenerator(o.Seed);
        var roster = gen.Populate(world, o.RosterSize);
        foreach (var f in roster) f.Contract = PromotionSetup.SignContract(world, promo, o.StartDate, f, rng);

        PromotionSetup.ConfigureDivisions(world, promo, roster, classes, o.StartDate, o.RankingsDepth);
        return world;
    }
}
