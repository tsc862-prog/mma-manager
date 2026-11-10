using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// The zero-to-hero start: founding a brand-new player promotion at the bottom
/// of the ladder via <see cref="WorldBuilder.FoundingSpec"/>. A founded org
/// starts with an empty gym — the player hires every fighter — and vacant
/// belts that the first title fights crown.
/// </summary>
public class FoundingTests
{
    private static WorldBuilder.Options Founded(ulong seed = 11) => new()
    {
        Seed = seed,
        Founding = new WorldBuilder.FoundingSpec
        {
            Name = "New Wave Fighting",
            Abbreviation = "NWF",
            Region = Region.Europe,
            Prestige = 15,
            StartingBalance = new Money(250_000m),
            TalentPool = 20,
        },
    };

    [Fact]
    public void Founding_HonoursTheChosenHomeTown_AndFallsBackToTheFightCapital()
    {
        // A deliberate choice: not the region's capital, a smaller room.
        var chosen = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 11,
            Founding = new WorldBuilder.FoundingSpec
            {
                Name = "Northern Lights FC", Abbreviation = "NLF",
                Region = Region.Europe, HomeCityName = "Manchester",
                Prestige = 15, StartingBalance = new Money(250_000m), TalentPool = 20,
            },
        });
        var promo = chosen.Get(chosen.PlayerPromotion)!;
        Assert.Equal("Manchester", chosen.Get(promo.HomeCity!.Value)!.Name);
        // Home town is where the brand is strongest — that's what a home town IS.
        int home = promo.PopularityByCity[promo.HomeCity!.Value];
        Assert.True(chosen.Cities.Values.Where(c => c.Id != promo.HomeCity)
            .All(c => promo.PopularityByCity.GetValueOrDefault(c.Id) <= home));

        // No choice (and a name that isn't in the region) → the fight capital.
        var fallback = new WorldBuilder().Build(Founded());
        var fp = fallback.Get(fallback.PlayerPromotion)!;
        Assert.Equal(Core.Sim.Geography.WorldMap.AnchorCity(fallback, Region.Europe)!.Id, fp.HomeCity);
    }

    /// <summary>Sign a free agent to the player at whatever they're asking.</summary>
    private static void Hire(World world, Fighter f)
    {
        var exp = new FighterValuation().Evaluate(world, f);
        ContractLifecycle.Sign(world, f, new ContractOffer
        {
            Fighter = f.Id,
            Promotion = world.PlayerPromotion,
            PursePerFight = exp.AskingPurse,
            WinBonus = exp.AskingWinBonus,
            Fights = exp.DesiredFights,
            SigningBonus = exp.WantsSigningBonus ? new Money(5_000m) : Money.Zero,
        }, world.CurrentDate);
    }

    [Fact]
    public void Founding_CreatesThePlayerPromotionAsSpecified()
    {
        var world = new WorldBuilder().Build(Founded());
        var player = world.Get(world.PlayerPromotion);

        Assert.NotNull(player);
        Assert.True(player!.IsPlayerControlled);
        Assert.Equal("New Wave Fighting", player.Name);
        Assert.Equal("NWF", player.Abbreviation);
        Assert.Equal(Region.Europe, player.BaseRegion);
        Assert.Equal(15, player.Prestige.Value);
        Assert.Equal(new Money(250_000m), player.Balance);

        // The established promotions are all still there, all AI-run.
        Assert.Equal(WorldBuilder.Options.DefaultPromotions.Length + 1, world.Promotions.Count);
        Assert.Single(world.Promotions.Values, p => p.IsPlayerControlled);
    }

    [Fact]
    public void Founding_StartsWithAnEmptyRosterAndVacantBelts()
    {
        var world = new WorldBuilder().Build(Founded());
        var player = world.Get(world.PlayerPromotion)!;

        Assert.Empty(world.RosterOf(player.Id));
        // The men's divisions plus the two women's divisions a founder also runs.
        Assert.Equal(WorldBuilder.FoundingSpec.DefaultDivisions.Length + 2, player.Divisions.Count);
        Assert.Contains(player.Divisions, d => d.Sex == Sex.Female);
        foreach (var division in player.Divisions)
        {
            Assert.NotNull(division.Title);
            var title = world.Get(division.Title!.Value);
            Assert.NotNull(title);
            Assert.Null(title!.CurrentChampion);
            Assert.Empty(title.Lineage);
        }
    }

    [Fact]
    public void Founding_LeavesADeepHiringMarket()
    {
        var options = Founded();
        var world = new WorldBuilder().Build(options);
        // The founding generates extra unsigned talent on top of the normal pool,
        // and the women's scene leaves free agents too — so the market is deeper.
        int expected = options.FreeAgentPool + options.Founding!.TalentPool;
        Assert.True(world.FreeAgents().Count() >= expected - 10, $"only {world.FreeAgents().Count()} free agents");
    }

    [Fact]
    public void Founding_HireBookAndCrownAChampion()
    {
        var world = new WorldBuilder().Build(Founded(seed: 17));
        var booking = new PlayerBooking();

        // Hire two lightweights off the market.
        var pair = world.FreeAgents()
            .Where(f => f.WeightClass == WeightClass.Lightweight && f.Status == FighterStatus.Active)
            .Take(2).ToList();
        Assert.Equal(2, pair.Count);
        foreach (var f in pair) Hire(world, f);
        Assert.Equal(2, world.RosterOf(world.PlayerPromotion).Count());

        // Book them for the vacant belt — allowed with no champion.
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(14), "NWF 1");
        Assert.True(booking.AddBout(world, ev, pair[0].Id, pair[1].Id, 5, titleFight: true, out var err), err);

        new WorldTick().AdvanceWeeks(world, 4);

        Assert.Equal(EventStatus.Completed, ev.Status);
        var title = world.Get(world.Get(world.PlayerPromotion)!
            .Divisions.Single(d => d.WeightClass == WeightClass.Lightweight).Title!.Value)!;
        Assert.NotNull(title.CurrentChampion);
        Assert.Contains(title.CurrentChampion!.Value, pair.Select(f => f.Id));
        Assert.Single(title.Lineage);
        Assert.Contains(world.News, n => n.Headline.Contains("claims the vacant"));
    }

    [Fact]
    public void Founding_ScoutSuggestsAnAffordableHireInYourDivisions()
    {
        var world = new WorldBuilder().Build(Founded());
        var player = world.Get(world.PlayerPromotion)!;

        var pick = SigningAdvisor.Suggest(world, player);

        Assert.NotNull(pick);
        Assert.Null(pick!.Fighter.Contract);   // actually a free agent
        Assert.Contains(pick.Fighter.WeightClass, player.Divisions.Select(d => d.WeightClass));
        // A shoestring org shouldn't be pointed at money it can't spend.
        Assert.True(pick.Expectations.AskingPurse.Amount <= player.Balance.Amount / 10m,
            $"scout suggested {pick.Expectations.AskingPurse} on a {player.Balance} bankroll");
    }

    [Fact]
    public void Founding_WorldTicksAndPlayerIsLeftAlone()
    {
        var world = new WorldBuilder().Build(Founded(seed: 23));
        var player = world.PlayerPromotion;

        new WorldTick().AdvanceWeeks(world, 12);

        // AI world keeps moving; nobody books or signs for the player.
        Assert.True(world.Events.Values.Count(e => e.Promotion != player) > 0);
        Assert.Equal(0, world.Events.Values.Count(e => e.Promotion == player));
        Assert.Empty(world.RosterOf(player));
    }
}
