using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// The alive-world layer: champ-vs-champ conquests, weigh-in drama, the weekly
/// digest, signature annual shows, and the owner's desk.
/// </summary>
public class AliveWorldTests
{
    // A world where the player exists but the AI runs everything of interest.
    private static World Build(ulong seed = 5) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    [Fact]
    public void ChampChampConquest_MovesTheWinnerUp_AndPassesTheOldBeltOn()
    {
        var world = Build();
        // Find an org with champions in two ADJACENT divisions (same sex).
        var (promo, lower, upper) = world.Promotions.Values
            .SelectMany(p => p.Divisions.Select(d => (p, d)))
            .Where(x => x.d.Title is { } t && world.Get(t)?.CurrentChampion is not null)
            .SelectMany(x => x.p.Divisions
                .Where(u => (int)u.WeightClass == (int)x.d.WeightClass + 1 && u.Sex == x.d.Sex
                    && u.Title is { } ut && world.Get(ut)?.CurrentChampion is not null)
                .Select(u => (x.p, x.d, u)))
            .First();

        var lowBelt = world.Get(lower.Title!.Value)!;
        var upBelt = world.Get(upper.Title!.Value)!;
        var challenger = world.Get(lowBelt.CurrentChampion!.Value)!;
        var champ = world.Get(upBelt.CurrentChampion!.Value)!;

        // Stage the superfight and hand the challenger the win.
        var ev = new EventService().CreateEvent(world, promo.Id, "Champ vs Champ", world.CurrentDate,
            new[] { new Matchup(champ.Id, challenger.Id, upper.WeightClass, 5, true, upBelt.Id) });
        var fight = ev.Card[0];
        fight.Outcome = new FightOutcome
        {
            Winner = challenger.Id, Loser = champ.Id,
            Method = FightMethod.Knockout, EndRound = 3, EndTimeSeconds = 100,
        };
        ResultsProcessor.Apply(world, ev, fight);

        Assert.Equal(upper.WeightClass, challenger.WeightClass);                 // moved up for good
        Assert.Equal(challenger.Id, upBelt.CurrentChampion);                     // the second belt
        Assert.NotEqual(challenger.Id, lowBelt.CurrentChampion);                 // the old one passed on
        Assert.Contains(challenger.Accolades, a => a.Title.Contains("second division"));
        Assert.Contains(world.News, n => n.Headline.Contains("TWO-DIVISION"));
        // The dethroned champ tops the UPPER division's contender list.
        Assert.Equal(champ.Id, upper.Rankings.First());
    }

    [Fact]
    public void WeightMiss_Fines_Catchweight_AndStripsAChampWhoBlowsIt()
    {
        var world = Build();
        var promo = world.Promotions.Values.First(p => p.Divisions.Any(d => d.Title is not null));
        var div = promo.Divisions.First(d => d.Title is { } t && world.Get(t)?.CurrentChampion is not null);
        var belt = world.Get(div.Title!.Value)!;
        var champ = world.Get(belt.CurrentChampion!.Value)!;
        var contender = world.RosterOf(promo.Id).First(f => div.Matches(f) && f.Id != champ.Id && f.Status == FighterStatus.Active);

        var ev = new EventService().CreateEvent(world, promo.Id, "Scale Night", world.CurrentDate,
            new[] { new Matchup(champ.Id, contender.Id, div.WeightClass, 5, true, belt.Id) });
        var fight = ev.Card[0];

        // Roll seeds until the scale claims the CHAMPION (deterministic hunt).
        bool stripped = false;
        for (ulong s = 1; s < 400 && !stripped; s++)
        {
            fight.WeightMissBy = null;
            fight.CatchweightLbs = null;
            fight.IsTitleFight = true;
            fight.Title = belt.Id;
            Storylines.RollWeightMisses(world, ev, world.CurrentDate, new SimRandom(s));
            stripped = fight.WeightMissBy == champ.Id;
        }
        Assert.True(stripped, "the hunt should find a seed where the champion misses weight");

        Assert.Null(belt.CurrentChampion);                                       // stripped on the scale
        Assert.False(fight.IsTitleFight);                                        // nothing on the line tonight
        Assert.NotNull(fight.CatchweightLbs);
        Assert.Contains(promo.Ledger, e => e.Description.Contains("Weight-miss fine"));
    }

    [Fact]
    public void TheWorld_PrintsItsWeekly_AndKeepsItsTraditions()
    {
        var world = Build(seed: 9);
        new WorldTick().AdvanceWeeks(world, 60);   // through a full calendar

        Assert.Contains(world.Mail, m => m.From == "MMA Weekly");                // the paper prints
        // Somebody's flagship annual show happened (the player runs AFC in this
        // world, so its signature is skipped — the others carry the calendar).
        Assert.Contains(world.Events.Values, e => e.Name.Contains("Emperor's New Year")
            || e.Name.Contains("Crown of Europe") || e.Name.Contains("Desert Clash")
            || e.Name.Contains("Reinado") || e.Name.Contains("Carnaval"));
    }

    [Fact]
    public void TheDesk_TitleShot_YesJumpsTheQueue_NoBurnsLoyalty()
    {
        var world = Build();
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d => d.Rankings.Count >= 3);
        var f = world.Get(div.Rankings[2])!;

        var yes = new Decision
        {
            Id = 1, Kind = DecisionKind.TitleShotRequest, Created = world.CurrentDate,
            Deadline = world.CurrentDate.AddDays(45), Fighter = f.Id,
        };
        world.Decisions.Add(yes);
        Decisions.Accept(world, yes, world.CurrentDate);
        Assert.Equal(f.Id, div.Rankings[0]);                                     // front of the line
        Assert.Empty(world.Decisions);

        int loyaltyBefore = f.Loyalty;
        var no = new Decision
        {
            Id = 2, Kind = DecisionKind.TitleShotRequest, Created = world.CurrentDate,
            Deadline = world.CurrentDate.AddDays(45), Fighter = f.Id,
        };
        world.Decisions.Add(no);
        Decisions.Decline(world, no, world.CurrentDate);
        Assert.True(f.Loyalty < loyaltyBefore, "a public no costs loyalty");
    }

    [Fact]
    public void TheDesk_SponsorShowcase_PaysWhenDelivered_StrikesWhenNot()
    {
        var world = Build();
        var promo = world.Get(world.PlayerPromotion)!;
        promo.SponsorDeals.Clear();
        var deal = new SponsorDeal { Partner = "Main Street Motors", FeePerEvent = new Money(10_000m), EventsRemaining = 6, SignedOn = world.CurrentDate };
        promo.SponsorDeals.Add(deal);

        Event Settled(Fighter star)
        {
            var opp = world.Fighters.Values.First(x => x.Id != star.Id && x.Status == FighterStatus.Active && x.Sex == star.Sex);
            var ev = new Event
            {
                Id = world.NewEventId(), Promotion = promo.Id, Name = "Showcase Night",
                Date = world.CurrentDate, Region = promo.BaseRegion, Status = EventStatus.Completed,
            };
            ev.Card.Add(new Fight { Id = world.NewFightId(), RedCorner = star.Id, BlueCorner = opp.Id, WeightClass = star.WeightClass });
            world.Events[ev.Id] = ev;
            return ev;
        }

        // Delivered: a home-region fighter on the (whole-card) main card.
        var local = world.Fighters.Values.First(x => x.Status == FighterStatus.Active && x.Nationality == promo.BaseRegion);
        world.Decisions.Add(new Decision
        {
            Id = 1, Kind = DecisionKind.SponsorShowcase, Created = world.CurrentDate,
            Deadline = world.CurrentDate.AddDays(75), Sponsor = deal.Partner, State = DecisionState.Accepted,
        });
        Decisions.OnPlayerEventSettled(world, promo, Settled(local));
        Assert.Contains(promo.Ledger, e => e.Description.Contains("showcase bonus"));
        Assert.Equal(0, deal.Strikes);

        // Broken promise: no home-region name anywhere near the card.
        var foreigner = world.Fighters.Values.First(x => x.Status == FighterStatus.Active
            && x.Nationality != promo.BaseRegion && (x.HomeCity is not { } hc || world.Get(hc)?.Region != promo.BaseRegion));
        world.Decisions.Add(new Decision
        {
            Id = 2, Kind = DecisionKind.SponsorShowcase, Created = world.CurrentDate,
            Deadline = world.CurrentDate.AddDays(75), Sponsor = deal.Partner, State = DecisionState.Accepted,
        });
        var miss = Settled(foreigner);
        // Make sure the opponent isn't accidentally local either.
        if (world.Get(miss.Card[0].BlueCorner) is { } opp2
            && (opp2.Nationality == promo.BaseRegion || (opp2.HomeCity is { } h2 && world.Get(h2)?.Region == promo.BaseRegion)))
            miss.Card[0] = new Fight { Id = world.NewFightId(), RedCorner = foreigner.Id, BlueCorner = foreigner.Id, WeightClass = foreigner.WeightClass };
        Decisions.OnPlayerEventSettled(world, promo, miss);
        Assert.Equal(1, deal.Strikes);
        Assert.Empty(world.Decisions);
    }
}
