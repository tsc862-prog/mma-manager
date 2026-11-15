using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;

namespace MmaManager.Tests;

public class SpecialEventTests
{
    [Fact]
    public void HigherTier_GrossesMoreThanAFightNight()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();

        var night = new EventService().BookAndRun(world, promo.Id, "FN", world.CurrentDate, new Matchmaker());

        var world2 = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var promo2 = world2.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var svc = new EventService();
        var card = new Matchmaker().BuildCard(world2, promo2.Id);
        var superCard = svc.CreateEvent(world2, promo2.Id, "PPV", world2.CurrentDate, card);
        superCard.Tier = EventTier.SuperCard;
        svc.RunEvent(world2, superCard);

        Assert.True(superCard.Financials!.GateRevenue.Amount > night.Financials!.GateRevenue.Amount);
    }

    [Fact]
    public void OneNightTournament_CrownsAWinner_AndPostsNews()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        // The org's deepest men's division — an 8-man bracket needs 8 ready bodies.
        var wc = promo.Divisions.Where(d => d.Sex == Sex.Male)
            .Select(d => d.WeightClass)
            .OrderByDescending(w => world.RosterOf(promo.Id)
                .Count(f => f.WeightClass == w && f.Sex == Sex.Male && f.Status == FighterStatus.Active))
            .First();
        int newsBefore = world.News.Count;

        var result = new TournamentService().RunOneNight(world, promo.Id, size: 8, world.CurrentDate, new Money(200_000m), wc, rules: null);

        Assert.NotNull(result);
        var ev = result!.Event;
        Assert.Equal(EventTier.SuperCard, ev.Tier);
        // 8-fighter single elim = 4 + 2 + 1 slots; a walkover (a withdrawal with no alternate left) fights no bout.
        Assert.Equal(7, result.Night.Bouts.Count);
        Assert.Equal(7 - result.Night.Bouts.Count(b => b.Outcome is null), ev.Card.Count);
        Assert.All(ev.Card, f => Assert.NotNull(f.Outcome));
        Assert.True(world.News.Count > newsBefore);
        // A champion is crowned, or the night says plainly that it has none.
        Assert.Contains(world.News, n => n.Headline.Contains("Grand Prix")
            && (result.Champion is not null ? n.Headline.Contains("wins") : n.Headline.Contains("No winner") || n.Headline.Contains("without a champion")));
    }
}
