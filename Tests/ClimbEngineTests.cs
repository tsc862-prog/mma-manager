using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using Standing = MmaManager.Core.Sim.Org.Standing;

namespace MmaManager.Tests;

/// <summary>
/// The climb engine (<see cref="Standing"/>): prestige is earned show by show
/// against a bar that rises with the promotion's current level, so good shows
/// climb, weak shows slip, tier crossings make news, and silence fades a brand.
/// </summary>
public class ClimbEngineTests
{
    [Fact]
    public void Apply_MovesPrestige_AndAnnouncesATierCrossing()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 5,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 19 },   // top of City, one below Regional
        });
        var promo = world.Get(world.PlayerPromotion)!;
        promo.TierClearance = OrgSize.Regional;   // cleared to grow into Regional (not holding at City)
        int newsBefore = world.News.Count;

        Standing.Apply(world, promo, +1, world.CurrentDate, "test");   // 19 -> 20 crosses into Regional

        Assert.Equal(20, promo.Prestige.Value);
        Assert.Equal(OrgSize.Regional, OrgSizing.SizeOf(promo));
        Assert.Contains(world.News.Skip(newsBefore), n => n.Category == NewsCategory.Milestone && n.Headline.Contains("Regional"));
        Assert.Contains(world.Mail, m => m.Subject.Contains("Regional promotion now"));
    }

    [Fact]
    public void Apply_WithinATier_MovesQuietly()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 5,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 40 },   // mid-National
        });
        var promo = world.Get(world.PlayerPromotion)!;
        int newsBefore = world.News.Count;

        Standing.Apply(world, promo, +1, world.CurrentDate, "test");

        Assert.Equal(41, promo.Prestige.Value);
        Assert.DoesNotContain(world.News.Skip(newsBefore), n => n.Category == NewsCategory.Milestone);
    }

    [Fact]
    public void AStrongShow_ClimbsALowPrestigeOrg()
    {
        // An AI org near the bottom running a real, competitive card should grow.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7, UseDesignedRoster = false });
        var promo = world.Promotions.Values.OrderBy(p => p.Prestige.Value).First(p => p.Id != world.PlayerPromotion);
        promo.Prestige = 12;
        int before = promo.Prestige.Value;

        // Several cards over the "season" — a low bar means good shows lift it.
        for (int i = 0; i < 5; i++)
            new EventService().BookAndRun(world, promo.Id, $"Show {i}", world.CurrentDate.AddDays(i * 14), new Matchmaker());

        Assert.True(promo.Prestige.Value > before, $"prestige stalled at {promo.Prestige.Value}");
    }

    [Fact]
    public void PrestigeFindsEquilibrium_ItDoesNotRunawayToTheCeiling()
    {
        // A big established org running a full season of its usual shows should
        // hold its level, not pin itself to 100 — the bar rises with prestige.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9, UseDesignedRoster = false });
        var afc = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        int start = afc.Prestige.Value;

        for (int i = 0; i < 12; i++)
            new EventService().BookAndRun(world, afc.Id, $"AFC {i}", world.CurrentDate.AddDays(i * 21), new Matchmaker());

        Assert.InRange(afc.Prestige.Value, start - 10, 99);   // moved with the product, never pinned to the ceiling
    }

    [Fact]
    public void SubPointShows_AccrueOverTime_InsteadOfRoundingAway()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 5,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 30 },
        });
        var promo = world.Get(world.PlayerPromotion)!;
        promo.TierClearance = OrgSize.National;   // cleared above Regional so the accrual isn't capped at the tier ceiling
        int startHundredths = promo.PrestigeHundredths;   // 3000

        // Four shows each a third of a point over the bar — under the old
        // integer model every one rounded to +0; now they add up to a full point.
        for (int i = 0; i < 4; i++)
            Standing.Apply(world, promo, 0.34, world.CurrentDate.AddDays(i), "sliver of a show");

        Assert.True(promo.PrestigeHundredths > startHundredths + 100,
            $"sub-point deltas should accumulate (was {startHundredths}, now {promo.PrestigeHundredths})");
        Assert.Equal(31, promo.Prestige.Value);   // 30.00 + 4×0.34 = 31.36 → floors to 31
    }

    [Fact]
    public void AShoestringPromotion_TakesYearsToClimb_NotMonths()
    {
        // A brand-new promotion, handed to the AI to run, should still be scraping
        // around the lower tiers after three years — building a name is a slog, and
        // a hot start can't rocket a nobody to National level in a season or two.
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 11,
            Founding = new WorldBuilder.FoundingSpec { Abbreviation = "NWF", StartingBalance = new Money(250_000m), TalentPool = 22 },
        });
        var nwf = world.PlayerPromotion;
        world.PlayerPromotion = new PromotionId(999_999);   // exclude nobody → the AI runs NWF like any org

        new WorldTick().AdvanceWeeks(world, 156);           // three years
        var p = world.Get(nwf)!;

        Assert.True(OrgSizing.SizeOf(p) < OrgSize.National,
            $"a three-year-old shoestring shouldn't be National yet (prestige {p.Prestige.Value})");
    }

    [Fact]
    public void AShoestring_SurvivesItsFirstYear_OnItsRunway()
    {
        // A brand-new promotion has no fanbase, so its early shows draw poorly — it
        // scrapes by on its starting runway while it builds a name. It shouldn't fold
        // in year one, and it shouldn't be rolling in money either: still a shoestring.
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 11,
            Founding = new WorldBuilder.FoundingSpec { Abbreviation = "NWF", StartingBalance = new Money(250_000m), TalentPool = 22 },
        });
        var nwf = world.PlayerPromotion;
        decimal startBank = world.Get(nwf)!.Balance.Amount;
        world.PlayerPromotion = new PromotionId(999_999);   // hand it to the AI to run

        new WorldTick().AdvanceWeeks(world, 52);            // its first year of shows
        var p = world.Get(nwf)!;

        Assert.False(p.Defunct, "it shouldn't fold in year one — the runway covers the early costs");
        Assert.True(p.Balance.Amount < startBank * 4m, "a first-year no-name isn't a cash machine yet");
    }

    [Fact]
    public void ACollapsedCard_CostsPrestigeAndReputation()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 5,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 40 },
        });
        var promo = world.Get(world.PlayerPromotion)!;
        int prestige = promo.Prestige.Value, rep = promo.Reputation.Value;
        var ev = new Event { Id = world.NewEventId(), Promotion = promo.Id, Name = "Doomed Card", Date = world.CurrentDate, Status = EventStatus.Cancelled };

        Standing.OnCancelled(world, promo, ev);

        Assert.True(promo.Prestige.Value < prestige);
        Assert.True(promo.Reputation.Value < rep);
        Assert.Contains(world.Mail, m => m.Subject.Contains("Doomed Card is off"));
    }

    [Fact]
    public void MonthlyDrift_FadesADarkBrand_ButSparesAnActiveOne()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9, UseDesignedRoster = false });
        var idle = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var active = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).ElementAt(1);

        // Both ran a show; measured from a probe date five months on, idle's last
        // card (and all its pre-history) is long past while active's was last week.
        var probe = world.CurrentDate.AddDays(150);
        var svc = new EventService();
        svc.BookAndRun(world, idle.Id, "Idle Last Show", world.CurrentDate, new Matchmaker());
        svc.BookAndRun(world, active.Id, "Active Show", probe.AddDays(-7), new Matchmaker());
        int idleBefore = idle.Prestige.Value, activeBefore = active.Prestige.Value;

        Standing.MonthlyDrift(world, probe);

        Assert.True(idle.Prestige.Value < idleBefore, "a brand dark for 5 months should fade");
        Assert.Equal(activeBefore, active.Prestige.Value);
    }
}
