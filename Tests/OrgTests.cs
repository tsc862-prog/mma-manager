using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

public class OrgTests
{
    [Fact]
    public void Build_SeedsAStaffMarketAndOwnerObjectives()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });

        Assert.NotEmpty(world.StaffMarket);
        Assert.Contains(world.StaffMarket, s => s.Role == StaffRole.Marketing);
        Assert.NotEmpty(world.Get(world.PlayerPromotion)!.Objectives);
    }

    [Fact]
    public void HireAndFire_MovesStaffAndBillsSalaryAsOverhead()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var promo = world.Get(world.PlayerPromotion)!;
        var candidate = world.StaffMarket.First(s => s.Role == StaffRole.Marketing);

        StaffService.Hire(world, promo, candidate);
        Assert.Contains(candidate, promo.Staff);
        Assert.DoesNotContain(candidate, world.StaffMarket);

        decimal before = promo.Balance.Amount;
        MmaManager.Core.Sim.Finance.Ledger.ChargeMonthlyOverhead(world, world.CurrentDate);
        // Overhead now includes the salary, so the drop is at least the salary.
        Assert.True(before - promo.Balance.Amount >= candidate.MonthlySalary.Amount);

        StaffService.Fire(world, promo, StaffRole.Marketing);
        Assert.DoesNotContain(candidate, promo.Staff);
        Assert.Contains(candidate, world.StaffMarket);
    }

    [Fact]
    public void Objective_RunEvents_IsMetOnceEnoughShowsRun()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var promo = world.Get(world.PlayerPromotion)!;
        var obj = promo.Objectives.First(o => o.Kind == ObjectiveKind.RunEvents);

        var svc = new EventService();
        for (int i = 0; i < obj.Target; i++)
        {
            var ev = svc.BookAndRun(world, promo.Id, $"Show {i}", world.CurrentDate.AddDays(i), new Matchmaker());
        }

        Objectives.Evaluate(world, world.CurrentDate);
        Assert.Equal(ObjectiveStatus.Met, obj.Status);
    }

    [Fact]
    public void MarketingStaff_LiftsTheGate()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var promo = world.Get(world.PlayerPromotion)!;

        var plainEv = new EventService().BookAndRun(world, promo.Id, "Plain", world.CurrentDate, new Matchmaker());
        int plainAttendance = plainEv.Financials!.Attendance;

        // Hire the best marketer and run an identical-scale card.
        var marketer = world.StaffMarket.Where(s => s.Role == StaffRole.Marketing).OrderByDescending(s => s.Quality).First();
        StaffService.Hire(world, promo, marketer);
        var boostedEv = new EventService().BookAndRun(world, promo.Id, "Boosted", world.CurrentDate, new Matchmaker());

        Assert.True(boostedEv.Financials!.Attendance >= plainAttendance);
    }
}
