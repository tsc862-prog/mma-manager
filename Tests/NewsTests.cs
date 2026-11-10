using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

public class NewsTests
{
    [Fact]
    public void RunningASeason_GeneratesNews()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2 });
        new WorldTick().AdvanceWeeks(world, 52);

        Assert.NotEmpty(world.News);
        // A year of cards should turn over at least one belt.
        Assert.Contains(world.News, n => n.Category == NewsCategory.TitleChange);
    }

    [Fact]
    public void TitleChange_PostsAHeadline()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2 });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var div = promo.Divisions.First(d => d.Title is not null && d.Rankings.Count > 0);
        var title = world.Get(div.Title!.Value)!;
        var champ = world.Get(title.CurrentChampion!.Value)!;
        var challenger = world.Get(div.Rankings[0])!;

        int newsBefore = world.News.Count;

        // Stage a title fight the challenger wins by handing them the result directly.
        var ev = new Event
        {
            Id = world.NewEventId(), Promotion = promo.Id, Name = "Title Night",
            Date = world.CurrentDate, Region = promo.BaseRegion, Status = EventStatus.Scheduled,
        };
        var fight = new Fight
        {
            Id = world.NewFightId(), RedCorner = challenger.Id, BlueCorner = champ.Id,
            WeightClass = div.WeightClass, ScheduledRounds = 5, IsTitleFight = true, Title = title.Id,
            Outcome = new FightOutcome { Winner = challenger.Id, Loser = champ.Id, Method = FightMethod.Knockout, EndRound = 1 },
        };
        ev.Card.Add(fight);
        world.Events[ev.Id] = ev;
        ResultsProcessor.Apply(world, ev, fight);

        Assert.Equal(challenger.Id, title.CurrentChampion);
        Assert.True(world.News.Count > newsBefore);
        Assert.Contains(world.News, n => n.Category == NewsCategory.TitleChange && n.Date == world.CurrentDate);
    }
}
