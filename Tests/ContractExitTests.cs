using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// Every door out of a promotion — a deal running out, a cut, a poach, the
/// board's veteran cut, a merger, a closure — must leave that org's books
/// clean: the fighter is out of its rankings (so its matchmaker can't book him
/// into a title fight and burn a bout off someone else's deal), any belt of
/// that org he holds is vacated, and guaranteed money is paid.
/// </summary>
public class ContractExitTests
{
    private static World Build(ulong seed = 5) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    // The two invariants the rest of the sim assumes. Returns the violations
    // (empty = clean) so a failure names the fighter and the org.
    private static List<string> Violations(World world)
    {
        var bad = new List<string>();
        foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
        {
            foreach (var d in p.Divisions)
                foreach (var id in d.Rankings)
                {
                    var f = world.Get(id);
                    if (f is null) { bad.Add($"{p.Abbreviation} {d.WeightClass}: ranked id {id.Value} doesn't exist"); continue; }
                    if (f.Status == FighterStatus.Retired)
                        bad.Add($"{p.Abbreviation} {d.WeightClass}: {f.FullName} is ranked but retired");
                    if (f.Contract?.Promotion != p.Id)
                        bad.Add($"{p.Abbreviation} {d.WeightClass}: {f.FullName} is ranked but contracted to " +
                                $"{(f.Contract is { } c ? world.Get(c.Promotion)?.Abbreviation : "nobody")}");
                }
        }
        // The rule: a belt's holder is an active-or-sidelined (never retired)
        // fighter under contract to the promotion that owns the belt. A champion
        // who leaves the org — walks, is poached, is cut — vacates it; one who
        // retires hands it to the top contender; a dead org's belts go vacant.
        foreach (var t in world.Titles.Values.OrderBy(t => t.Id.Value))
        {
            if (t.CurrentChampion is not { } cid) continue;
            var champ = world.Get(cid);
            if (champ is null) { bad.Add($"{t.Name}: champion id {cid.Value} doesn't exist"); continue; }
            if (champ.Status == FighterStatus.Retired) bad.Add($"{t.Name}: held by retired {champ.FullName}");
            if (champ.Contract?.Promotion != t.Promotion)
                bad.Add($"{t.Name}: held by {champ.FullName}, contracted to " +
                        $"{(champ.Contract is { } c ? world.Get(c.Promotion)?.Abbreviation : "nobody")}");
            if (world.Get(t.Promotion) is { Defunct: true }) bad.Add($"{t.Name}: a dead org's belt still has a holder");
        }
        return bad;
    }

    [Fact]
    public void ExpiringDeals_ThatWalk_LeaveTheOldOrgsRankings()
    {
        var world = Build();
        Assert.Empty(Violations(world));   // the built world starts clean

        // Run every ranked deal at one AI org out at once: some re-sign, some
        // are poached, some retire — and some walk to the open market.
        var promo = world.Promotions.Values
            .Where(p => p.Id != world.PlayerPromotion && !p.Defunct && p.Divisions.Any(d => d.Rankings.Count > 0))
            .OrderByDescending(p => p.Divisions.Sum(d => d.Rankings.Count)).ThenBy(p => p.Id.Value)
            .First();
        var ranked = promo.Divisions.SelectMany(d => d.Rankings).Select(id => world.Get(id)!).ToList();
        foreach (var f in ranked) f.Contract!.FightsRemaining = 0;

        ContractLifecycle.ProcessExpiries(world, world.CurrentDate);

        Assert.Contains(ranked, f => f.Contract?.Promotion != promo.Id);   // somebody actually left
        var v = Violations(world);
        Assert.True(v.Count == 0, string.Join("\n", v));
    }

    [Fact]
    public void Matchmaker_NeverBooksARankedFighterTheOrgNoLongerHolds()
    {
        var world = Build();
        // An AI org with a contested-able belt: make it vacant, so the top two
        // ranked names are booked for it ahead of everything else on the card.
        var (promo, div) = world.Promotions.Values
            .Where(p => p.Id != world.PlayerPromotion && !p.Defunct)
            .OrderBy(p => p.Id.Value)
            .SelectMany(p => p.Divisions.Where(d => d.Title is not null
                && d.Rankings.Count(id => world.Get(id)!.IsMatchReady(world.CurrentDate)) >= 4).Select(d => (p, d)))
            .First();
        world.Get(div.Title!.Value)!.CurrentChampion = null;

        // Stale rankings: every ranked fighter in that division has left the org
        // (the state the old expiry path left behind until the org's next Settle).
        var gone = div.Rankings.ToList();
        foreach (var id in gone) world.Get(id)!.Contract = null;

        var card = new Matchmaker().BuildCard(world, promo.Id, targetFights: 13);

        var booked = card.SelectMany(m => new[] { m.Red, m.Blue }).ToList();
        Assert.DoesNotContain(booked, id => gone.Contains(id));
        Assert.All(booked, id => Assert.Equal(promo.Id, world.Get(id)!.Contract?.Promotion));
    }

    [Fact]
    public void ReleasingAChampion_VacatesTheBelt()
    {
        var world = Build();
        var title = world.Titles.Values
            .Where(t => t.CurrentChampion is not null && world.Get(t.Promotion) is { Defunct: false })
            .OrderBy(t => t.Id.Value).First();
        var champ = world.Get(title.CurrentChampion!.Value)!;

        ContractLifecycle.Release(world, champ);

        Assert.Null(champ.Contract);
        Assert.Null(title.CurrentChampion);                                   // a cut champion's belt goes vacant
        Assert.NotNull(title.Lineage.Last(r => r.Champion == champ.Id).LostOn);  // and the reign is closed
        Assert.Empty(Violations(world));
    }

    [Fact]
    public void BoardVeteranCut_PaysTheGuaranteedBuyout_AndClearsTheRankings()
    {
        var world = Build();
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d => d.Rankings.Count >= 3);
        var vet = world.Get(div.Rankings[1])!;
        vet.Contract!.Guaranteed = true;
        vet.Contract.FightsRemaining = 3;
        vet.Contract.PursePerFight = new Money(10_000m);
        decimal before = promo.Balance.Amount;

        var cut = new Decision
        {
            Id = 1, Kind = DecisionKind.VeteranCut, Created = world.CurrentDate,
            Deadline = world.CurrentDate.AddDays(45), Fighter = vet.Id,
        };
        world.Decisions.Add(cut);
        Decisions.Accept(world, cut, world.CurrentDate);

        Assert.Null(vet.Contract);
        Assert.DoesNotContain(vet.Id, div.Rankings);
        Assert.Contains(promo.Ledger, e => e.Category == FinanceCategory.ContractBuyout && e.Amount.Amount == -30_000m);
        Assert.Equal(before - 30_000m, promo.Balance.Amount);
        Assert.Empty(world.Decisions);
    }

    [Fact]
    public void BoardVeteranCut_OfAFighterWhoAlreadyLeft_DoesNotCutHimFromHisNewOrg()
    {
        var world = Build();
        var promo = world.Get(world.PlayerPromotion)!;
        var vet = world.RosterOf(promo.Id).OrderBy(f => f.Id.Value).First(f => f.Status == FighterStatus.Active);
        var other = world.Promotions.Values.First(p => p.Id != promo.Id && !p.Defunct);

        var cut = new Decision
        {
            Id = 1, Kind = DecisionKind.VeteranCut, Created = world.CurrentDate,
            Deadline = world.CurrentDate.AddDays(45), Fighter = vet.Id,
        };
        world.Decisions.Add(cut);

        // He was poached before the owner got round to the desk.
        ContractLifecycle.Release(world, vet, dentReputation: false);
        ContractLifecycle.Sign(world, vet, new ContractOffer
        {
            Fighter = vet.Id, Promotion = other.Id, PursePerFight = new Money(5_000m), Fights = 3,
        }, world.CurrentDate);

        Decisions.Accept(world, cut, world.CurrentDate);

        Assert.Equal(other.Id, vet.Contract?.Promotion);   // still theirs
        Assert.Empty(world.Decisions);
    }

    [Fact]
    public void OverTwoYears_RankingsAndBelts_OnlyHoldTheOrgsOwnFighters()
    {
        // All-AI world (the longsim setup): the AI runs every org, so every exit
        // path — expiries, cuts, poaches, wars, mergers, closures — gets exercised.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        world.PlayerPromotion = default;
        var tick = new WorldTick();

        Assert.Empty(Violations(world));
        int titleFights = 0;
        for (int week = 0; week < 104; week++)
        {
            var before = world.CurrentDate;
            tick.AdvanceWeek(world);

            if (world.CurrentDate.Month == before.Month)
            {
                // No contract moved after this week's cards ran (the monthly
                // passes didn't fire), so both corners of every TITLE fight on
                // them must still be under contract to the promotion that booked
                // them. (Undercards legitimately carry unsigned local filler —
                // a local who wins is discovered as a free agent — and the
                // local circuit's nights belong to no org at all.)
                foreach (var ev in world.Events.Values.Where(e => e.Status == EventStatus.Completed
                             && e.Date > before && e.Date <= world.CurrentDate
                             && world.Get(e.Promotion) is not null).OrderBy(e => e.Id.Value))
                    foreach (var fight in ev.Card.Where(f => f.IsTitleFight))
                    {
                        titleFights++;
                        foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                        {
                            var f = world.Get(id)!;
                            if (f.Status == FighterStatus.Retired) continue;   // a career-ending night, not a booking error
                            Assert.True(f.Contract?.Promotion == ev.Promotion,
                                $"{world.CurrentDate}: {f.FullName} fought for a belt on {ev.Name} without a contract there");
                        }
                    }
                continue;
            }

            var v = Violations(world);
            Assert.True(v.Count == 0, $"{world.CurrentDate}: {v.Count} violations\n" + string.Join("\n", v.Take(20)));
        }
        Assert.True(titleFights > 0, "the check above should have seen some title fights");
    }
}
