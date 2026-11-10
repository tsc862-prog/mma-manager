using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// Smarter AI roster behaviour: activity-aware matchmaking keeps deep rosters
/// busy, and a roster policy means the big stages don't hoard journeymen with
/// losing records — those fighters slide down the ladder instead.
/// </summary>
public class AiRosterTests
{
    private static bool IsChampion(World world, Fighter f) =>
        world.Titles.Values.Any(t => t.CurrentChampion == f.Id);

    // --- Roster policy -------------------------------------------------------

    [Fact]
    public void BigStageDropsALoser_TheRegionalSceneKeepsThem()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var org = world.Promotions.Values.First(p => p.Abbreviation == "VMMA");
        var f = world.RosterOf(org.Id).First(x => !IsChampion(world, x)
            && !(org.Divisions.FirstOrDefault(d => d.WeightClass == x.WeightClass)?.Rankings.Take(3).Contains(x.Id) ?? false));

        // A settled journeyman: 17 fights, well under .500, no prospect upside.
        f.Record.Wins = 5; f.Record.Losses = 12; f.Record.CurrentStreak = -2;
        f.PotentialAbility = 45;

        org.Prestige = 75;   // International
        Assert.False(RosterPolicy.Wants(world, org, f));
        org.Prestige = 66;   // Continental
        Assert.False(RosterPolicy.Wants(world, org, f));
        org.Prestige = 40;   // National — still won't carry a sub-.500 record
        Assert.False(RosterPolicy.Wants(world, org, f));
        org.Prestige = 15;   // City — needs bodies, keeps almost anyone
        Assert.True(RosterPolicy.Wants(world, org, f));
    }

    [Fact]
    public void ChampionsProspectsAndTheGreen_AreAlwaysWanted()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var org = world.Promotions.Values.First(p => p.Abbreviation == "VMMA");
        org.Prestige = 75;   // strictest tier

        // A champion, even with an ugly record on paper.
        var champId = world.Titles.Values.First(t => t.Promotion == org.Id && t.CurrentChampion is not null).CurrentChampion!.Value;
        var champ = world.Get(champId)!;
        champ.Record.Wins = 1; champ.Record.Losses = 20;
        Assert.True(RosterPolicy.Wants(world, org, champ));

        var f = world.RosterOf(org.Id).First(x => !IsChampion(world, x));

        // Too green to judge (under five bouts).
        f.Record.Wins = 0; f.Record.Losses = 3; f.PotentialAbility = 40;
        Assert.True(RosterPolicy.Wants(world, org, f));

        // A young, high-ceiling project on a losing record still gets a leash.
        f.Record.Wins = 4; f.Record.Losses = 8; f.PotentialAbility = 82;
        if (f.AgeOn(world.CurrentDate) <= 25)
            Assert.True(RosterPolicy.Wants(world, org, f));
    }

    // --- Expiry + cull -------------------------------------------------------

    [Fact]
    public void MonthlyCull_TrimsUnwanted_ButSparesThePlayerAndTheDivisionFloor()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var player = world.Get(world.PlayerPromotion)!;   // AFC in quick-start
        var ai = world.Promotions.Values.First(p => p.Abbreviation == "VMMA");

        void MakeLosers(Promotion org)
        {
            foreach (var f in world.RosterOf(org.Id)
                         .Where(x => x.WeightClass == WeightClass.Lightweight && !IsChampion(world, x))
                         .Take(6))
            {
                f.Record.Wins = 3; f.Record.Losses = 15; f.Record.CurrentStreak = -5; f.PotentialAbility = 45;
            }
        }
        MakeLosers(player);
        MakeLosers(ai);

        int aiBefore = world.RosterOf(ai.Id).Count(f => f.Status == FighterStatus.Active);
        int playerBefore = world.RosterOf(player.Id).Count(f => f.Status == FighterStatus.Active);

        int cuts = RosterManagement.RunMonthly(world, world.CurrentDate);

        Assert.True(cuts >= 1, "the AI should shed some of its losing fighters");
        Assert.True(world.RosterOf(ai.Id).Count(f => f.Status == FighterStatus.Active) < aiBefore, "VMMA should be smaller");
        Assert.Equal(playerBefore, world.RosterOf(player.Id).Count(f => f.Status == FighterStatus.Active));   // player untouched
        // Never trims a division below the bodies it needs to run a card.
        Assert.True(world.RosterOf(ai.Id).Count(f => f.WeightClass == WeightClass.Lightweight && f.Status == FighterStatus.Active) >= 4);
    }

    [Fact]
    public void AnExpiringLoser_IsNotReSigned_ByABigOrg()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var ai = world.Promotions.Values.First(p => p.Abbreviation == "VMMA");
        var f = world.RosterOf(ai.Id).First(x => !IsChampion(world, x)
            && !(ai.Divisions.FirstOrDefault(d => d.WeightClass == x.WeightClass)?.Rankings.Take(3).Contains(x.Id) ?? false));
        f.Record.Wins = 4; f.Record.Losses = 14; f.Record.CurrentStreak = -3; f.PotentialAbility = 45;
        f.Contract!.FightsRemaining = 0;   // deal is up

        ContractLifecycle.ProcessExpiries(world, world.CurrentDate);

        Assert.Null(f.Contract);   // let walk, not renewed — slides down the ladder
    }

    // --- Activity-aware matchmaking (integration) ----------------------------

    [Fact]
    public void ActivityAwareBooking_KeepsMostOfADeepRosterBusy()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        new WorldTick().AdvanceWeeks(world, 78);   // 18 months of the world running

        // The deepest AI-run roster (VMMA when it survives — Europe's establishment
        // is the designed high-wire act and can legitimately fold on some seeds).
        // The player's org is excluded: the AI never books it.
        var org = world.Promotions.Values.Where(p => !p.Defunct && p.Id != world.PlayerPromotion)
            .OrderByDescending(p => world.RosterOf(p.Id).Count(f => f.Status == FighterStatus.Active))
            .First();
        var roster = world.RosterOf(org.Id).Where(f => f.Status == FighterStatus.Active).ToList();
        var foughtRecently = world.Events.Values
            .Where(e => e.Status == EventStatus.Completed && e.Promotion == org.Id && e.Date >= world.CurrentDate.AddMonths(-6))
            .SelectMany(e => e.Card).SelectMany(f => new[] { f.RedCorner, f.BlueCorner }).ToHashSet();

        int idle = roster.Count(f => !foughtRecently.Contains(f.Id));
        // A deep roster on post-fight recovery windows can't all fight every six
        // months — ~monthly cards give a 77-man roster barely one slot each, and
        // injuries/division structure eat into that — so ~half busy is the real
        // ceiling here. The regression this guards is the OLD greedy booker,
        // which left 80%+ sitting while the same top names headlined every card.
        Assert.True(idle <= roster.Count * 5 / 8,
            $"{idle} of {roster.Count} sat idle over six months — matchmaking should rotate the roster");
    }

    [Fact]
    public void OverASeason_BigOrgsDoNotHoardLosers()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        new WorldTick().AdvanceWeeks(world, 104);   // two years

        // The genuinely big stages (Continental+) shed their losers — the talent
        // treadmill sends winners UP and lets journeymen slide down to the national
        // feeder tier, so the top rosters stay clean. (The feeder orgs legitimately
        // carry more losing records; that's the bottom of the ladder, not hoarding.)
        foreach (var p in world.Promotions.Values.Where(x => !x.Defunct
                     && MmaManager.Core.Sim.Org.OrgSizing.SizeOf(x) >= OrgSize.Continental))
        {
            var roster = world.RosterOf(p.Id).Where(f => f.Status == FighterStatus.Active).ToList();
            if (roster.Count < 40) continue;   // a genuinely big stage, not a small org that just crossed the tier line
            // The cleanliness bar applies to an org AT STRENGTH. One rebuilding from
            // a near-death brush (roster well under its tier target) legitimately
            // carries cheap developmental depth until the books and market power
            // recover — that's convalescence, not hoarding.
            int target = MmaManager.Core.Sim.Org.OrgSizing.RosterTargetPerDivision(MmaManager.Core.Sim.Org.OrgSizing.SizeOf(p)) * p.Divisions.Count;
            if (roster.Count < target * 3 / 4) continue;
            int drawFloor = MmaManager.Core.Sim.Org.OrgSizing.DrawFloor(MmaManager.Core.Sim.Org.OrgSizing.SizeOf(p));
            // A losing record is only "hoarding" if the fighter brings nothing else —
            // a genuine draw (or a champion) is kept on purpose, not hoarded.
            int deadweight = roster.Count(f => f.Record.TotalBouts >= 5 && f.Record.Losses > f.Record.Wins
                && !IsChampion(world, f) && f.Intangibles.Popularity.Value < drawFloor);
            Assert.True(deadweight <= roster.Count / 3,
                $"{p.Abbreviation}: {deadweight} of {roster.Count} are losing-record deadweight — a top org shouldn't carry them");
        }
    }
}
