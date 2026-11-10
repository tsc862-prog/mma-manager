using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

public class MailTests
{
    [Fact]
    public void MonthlyReview_AlertsOnAnExpiringPlayerContract()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        var f = world.RosterOf(world.PlayerPromotion).First(x => x.Contract is not null && x.Status == FighterStatus.Active);
        f.Contract!.FightsRemaining = 0;

        Mail.MonthlyReview(world, world.CurrentDate);

        Assert.Contains(world.Mail, m => m.Category == MailCategory.ContractAlert && m.Subject.Contains(f.FullName));
    }

    [Fact]
    public void Post_DoesNotResendAnUnreadDuplicate()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        Mail.Post(world, world.CurrentDate, MailCategory.BoardNote, "Owner", "Same subject", "body");
        Mail.Post(world, world.CurrentDate, MailCategory.BoardNote, "Owner", "Same subject", "body");
        Assert.Equal(1, world.Mail.Count(m => m.Subject == "Same subject"));
    }

    [Fact]
    public void LowBalance_TriggersABoardNote()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        world.Get(world.PlayerPromotion)!.Balance = new Money(10_000m);

        Mail.MonthlyReview(world, world.CurrentDate);

        Assert.Contains(world.Mail, m => m.Category == MailCategory.BoardNote);
    }
}
