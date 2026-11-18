using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// Nobody decides on a man's hidden ceiling. <see cref="ScoutingService.PerceivedPotential(World, Promotion, Fighter)"/>
/// is what a house's scouts think it is (a projection from public facts and its own read of his ability, with a
/// keyed-hash error; a share of the truth only on its own men), and the room's overload is what a head coach sees of
/// his members. Every decision that used to read <see cref="Fighter.PotentialAbility"/> now reads one of those, and
/// each call-site test below sets up a pair the read and the truth rank in OPPOSITE orders: put the site back on the
/// truth and it picks the other man. The last three tests pin call sites PR #81 put on the scouted ability read
/// (a mid-game startup's seeding, a crossover's price, an Origins pioneer's price) at the call site itself.
/// </summary>
public class FogPotentialTests
{
    // ---- the read itself ----------------------------------------------------------------------------------------

    [Fact]
    public void AStrangersCeilingRead_CarriesNothingOfTheTruth()
    {
        var w = FogKit.Modern();
        var house = AiHouses(w).First();
        var camp = w.Camps.Values.OrderBy(c => c.Id.Value).First();
        var f = w.FreeAgents().Where(x => x.Status == FighterStatus.Active && x.Camp != camp.Id).OrderBy(x => x.Id.Value).First();
        FogKit.SetTrue(f, 50);

        f.PotentialAbility = 51;
        int low = ScoutingService.PerceivedPotential(w, house, f), lowRoom = ScoutingService.PerceivedPotential(w, camp, f);
        f.PotentialAbility = 95;
        Assert.Equal(low, ScoutingService.PerceivedPotential(w, house, f));
        Assert.Equal(lowRoom, ScoutingService.PerceivedPotential(w, camp, f));
    }

    [Fact]
    public void AHousesOwnMan_ShowsSomeOfHisRealCeiling_ButNeverExactly()
    {
        var w = FogKit.Modern();
        var house = AiHouses(w).First();
        var f = w.RosterOf(house.Id).Where(x => x.Status == FighterStatus.Active).OrderBy(x => x.Id.Value).First();
        FogKit.SetTrue(f, 50);
        f.PotentialAbility = 55;
        int low = ScoutingService.PerceivedPotential(w, house, f);
        f.PotentialAbility = 95;
        int high = ScoutingService.PerceivedPotential(w, house, f);
        Assert.True(high > low, "his own house sees him train");
        Assert.True(high < 95 - 5, $"but only a share of it shows ({high})");
    }

    [Fact]
    public void AnUnknownKidsCeilingRead_IsWiderThanHisAbilityRead_AndNarrowsAsHeIsSeen()
    {
        // Same house, many unknown 20-year-olds at one true level: the spread of what it reads of their ceilings
        // (around the public projection) is wider than the spread of what it reads of their ability. Give them all
        // a full public record and the ceiling read is the projection alone: every hash error is gone.
        var w = FogKit.Modern();
        var house = AiHouses(w).OrderBy(p => p.Prestige.Value).ThenBy(p => p.Id.Value).First();
        var kids = w.Fighters.Values.Where(f => f.Status == FighterStatus.Active && f.Contract?.Promotion != house.Id
                                                && f.AgeOn(w.CurrentDate) == 20)
            .OrderBy(f => f.Id.Value).Take(40).ToList();
        Assert.True(kids.Count >= 20);
        foreach (var k in kids) { FogKit.Unknown(k); FogKit.SetTrue(k, 50); k.PotentialAbility = 64; }

        int abilitySpread = kids.Max(k => ScoutingService.PerceivedAbility(w, house, k)) - kids.Min(k => ScoutingService.PerceivedAbility(w, house, k));
        var ceilings = kids.Select(k => ScoutingService.PerceivedPotential(w, house, k) - ScoutingService.PerceivedAbility(w, house, k)).ToList();
        Assert.True(ceilings.Max() - ceilings.Min() > abilitySpread, $"ceiling spread {ceilings.Max() - ceilings.Min()} vs ability {abilitySpread}");

        foreach (var k in kids) { k.Record.Wins = 10; k.Record.Losses = 7; }   // 17 bouts: fully known
        var known = kids.Select(k => ScoutingService.PerceivedPotential(w, house, k)).Distinct().ToList();
        Assert.Single(known);   // 50 + headroom at 20 (11) + the record's word on a 10-7 kid, and nothing else
    }

    // ---- the front office: the marketing push and the camp stipend --------------------------------------------

    [Fact]
    public void ThePromoPush_GoesToTheKidTheHouseRatesHighest_NotTheTruesBest()
    {
        var (w, house, older, younger) = OwnPair();
        AiFrontOffice.BuildAProspect(w, house, new List<Fighter> { older, younger }, surplus: 50_000_000m);
        Assert.True(younger.MarketingPush);    // the house's read
        Assert.False(older.MarketingPush);     // the truth would have pushed the older man
    }

    [Fact]
    public void TheCampStipend_GoesToTheKidTheHouseRatesHighest_NotTheTruesBest()
    {
        var (w, house, older, younger) = OwnPair();
        AiFrontOffice.SendAProspectToCamp(w, house, new List<Fighter> { older, younger }, surplus: 50_000_000m);
        Assert.True(younger.CampStipend);
        Assert.False(older.CampStipend);
    }

    // Two of an AI house's own men at one true level: a 25-year-old on a losing run whose real ceiling is a point
    // HIGHER than an 18-year-old's who wins and finishes. The house reads the kid higher (seven more years of
    // headroom and the record's word outweigh the share of the truth it sees in its own men).
    private static (World W, Promotion House, Fighter Older, Fighter Younger) OwnPair()
    {
        var w = FogKit.Modern();
        var house = AiHouses(w).First();
        house.Prestige = 20;   // no scouting department: the smallest share of the truth shows through
        var (older, younger) = AgedPair(w, 25, 18);
        foreach (var f in new[] { older, younger })
        {
            FogKit.Unknown(f);
            FogKit.SetTrue(f, 60);
            f.Contract = new Contract { Promotion = house.Id, SignedOn = w.CurrentDate, FightsRemaining = 3 };
            f.Intangibles.Popularity = 30;
            f.MarketingPush = false;
            f.CampStipend = false;
        }
        Form(younger, wins: 3, losses: 0, finishes: 3);   // what the public sees: the kid wins and finishes...
        Form(older, wins: 0, losses: 3, finishes: 0);     // ...the older man has lost three straight
        older.PotentialAbility = 74;
        younger.PotentialAbility = 73;
        int Read(Fighter f) => ScoutingService.PerceivedPotential(w, house, f);
        Assert.True(Read(younger) > Read(older), $"read: younger {Read(younger)} must be ahead of older {Read(older)}");
        Assert.True(Read(younger) - ScoutingService.PerceivedAbility(w, house, younger) >= 8, "room to grow as the house sees it");
        return (w, house, older, younger);
    }

    // ---- who a house keeps and signs ----------------------------------------------------------------------------

    [Fact]
    public void WhoABigHouseKeepsOnALeash_IsWhoItThinksHasTheCeiling()
    {
        // Two young free agents with losing records a big stage would never carry on the record alone. The one
        // with a huge hidden ceiling but nothing showing is not wanted; the one at his ceiling who READS like a
        // prospect is. On the truth it was the other way round.
        var w = FogKit.Modern();
        var house = AiHouses(w).Where(p => OrgSizing.SizeOf(p) is OrgSize.International or OrgSize.Continental)
            .OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value).First();
        var (hidden, showy) = AgedPair(w, 22, 20, freeAgents: true);
        foreach (var f in new[] { hidden, showy })
        {
            FogKit.Unknown(f);
            f.Record.Wins = 2; f.Record.Losses = 4; f.Record.WinsByDecision = 2; f.Record.CurrentStreak = -1;
            f.Intangibles.Popularity = 0;
        }
        FogKit.SetTrue(hidden, 45); hidden.PotentialAbility = 92;
        FogKit.SetTrue(showy, 62); showy.PotentialAbility = 62;
        Assert.True(ScoutingService.PerceivedPotential(w, house, hidden) < 70);
        Assert.True(ScoutingService.PerceivedPotential(w, house, showy) >= 70);

        Assert.False(RosterPolicy.Wants(w, house, hidden));   // the truth would keep him (ceiling 92)
        Assert.True(RosterPolicy.Wants(w, house, showy));     // the truth would let him go (ceiling 62)
    }

    // ---- the camps: the player's prospect mail, a head coach's protégé ----------------------------------------

    [Fact]
    public void TheBiggerCampRequest_ComesFromTheKidThePlayersScoutsRateHighest()
    {
        var w = FogKit.Modern();
        var player = w.Get(w.PlayerPromotion)!;
        player.Prestige = 20;   // no scouting department: the smallest share of the truth shows through
        foreach (var f in w.RosterOf(player.Id)) f.CampStipend = true;   // nobody else is in the running
        var (older, younger) = AgedPair(w, 25, 18);
        foreach (var f in new[] { older, younger })
        {
            FogKit.Unknown(f);
            FogKit.SetTrue(f, 60);
            f.Contract = new Contract { Promotion = player.Id, SignedOn = w.CurrentDate, FightsRemaining = 3 };
            f.CampStipend = false;
        }
        Form(younger, wins: 3, losses: 0, finishes: 3);   // what the public sees: the kid wins and finishes...
        Form(older, wins: 0, losses: 3, finishes: 0);     // ...the older man has lost three straight
        older.PotentialAbility = 74;
        younger.PotentialAbility = 73;
        int Read(Fighter f) => ScoutingService.PerceivedPotential(w, player, f);
        Assert.True(Read(younger) > Read(older), $"read: younger {Read(younger)} must be ahead of older {Read(older)}");

        Camps.RunMonthly(w, w.CurrentDate, RngWhere(r => r.Chance(0.05)));

        var mail = w.Mail.Single(m => m.Subject.EndsWith("wants a bigger camp"));
        Assert.Equal(younger.Id, mail.AboutFighter);   // the truth would have sent the older man's letter
    }

    [Fact]
    public void AHeadCoachTakesTheProtegeHeRatesHighest_NotTheTruesBest()
    {
        var w = FogKit.Modern();
        var (older, younger) = AgedPair(w, 25, 18);
        var coach = w.Fighters.Values.Where(f => f != older && f != younger).OrderBy(f => f.Id.Value).First();
        var city = w.Cities.Values.OrderBy(c => c.Id.Value).First();
        w.Camps.Clear();   // one room in the world: only its coach is choosing
        var camp = new Camp
        {
            Id = w.NewCampId(), Name = "Fog Room", City = city.Id, Specialty = PrimaryStyle.WellRounded,
            CoachQuality = 40, Reputation = 0, HeadCoach = coach.Id,   // an ordinary eye: the smallest share of the truth
        };
        w.Camps[camp.Id] = camp;
        w.Relationships.RemoveAll(r => r.Kind == RelationshipKind.Mentor);
        foreach (var f in new[] { older, younger })
        {
            FogKit.Unknown(f);
            FogKit.SetTrue(f, 60);
            f.Camp = camp.Id;
        }
        Form(younger, wins: 3, losses: 0, finishes: 3);   // what the public sees: the kid wins and finishes...
        Form(older, wins: 0, losses: 3, finishes: 0);     // ...the older man has lost three straight
        older.PotentialAbility = 74;
        younger.PotentialAbility = 73;
        int Read(Fighter f) => ScoutingService.PerceivedPotential(w, camp, f);
        Assert.True(Read(younger) > Read(older), $"read: younger {Read(younger)} must be ahead of older {Read(older)}");

        // The month's draws: the prospect-mail roll misses, the coach's roll hits.
        Camps.RunMonthly(w, w.CurrentDate, RngWhere(r => !r.Chance(0.05) && r.Chance(0.5)));

        var bond = w.Relationships.Single(r => r.Kind == RelationshipKind.Mentor && r.A == coach.Id);
        Assert.Equal(younger.Id, bond.B);   // the truth would have taken the older man
    }

    // ---- PR #81's call sites, pinned where they are called --------------------------------------------------

    [Fact]
    public void AMidGameStartupFoundedByTheWorld_SeedsItsBeltsOnItsOwnRead()
    {
        // Every free agent at one true level with a blank record: on the truth every seed ties and the belt goes
        // to whoever signed first; on the house's read it goes to the man it rates highest. The founding is the
        // real one (WorldEvents), so dropping the seed argument there turns this red.
        var w = FogKit.Modern();
        foreach (var f in w.FreeAgents().Where(f => f.Status == FighterStatus.Active))
        {
            FogKit.Unknown(f);
            FogKit.SetTrue(f, 60);
        }
        var before = w.Promotions.Keys.ToHashSet();
        var date = (w.StartDate == default ? new DateOnly(2026, 1, 1) : w.StartDate).AddMonths(14);
        WorldEvents.MaybeNewPromotion(w, date, new SimRandom(77));
        var startup = w.Promotions.Values.Single(p => !before.Contains(p.Id));

        var seen = PromotionSetup.SeedScoreAsSeenBy(w, startup);
        var roster = w.RosterOf(startup.Id).ToList();
        int differs = 0;
        foreach (var div in startup.Divisions)
        {
            var members = roster.Where(div.Matches).ToList();
            if (members.Count == 0) continue;
            var title = w.Titles.Values.Single(t => t.Promotion == startup.Id && t.WeightClass == div.WeightClass);
            double bestRead = members.Max(seen);
            Assert.Equal(bestRead, seen(w.Get(title.CurrentChampion!.Value)!));   // crowned on the read

            // On the truth every pro ties at 60 (the tryout kids are below), so the belt would go to the first pro
            // signed: the founding's candidate order (home region first, then its read of him as a stranger, then id).
            double TheirRead(Fighter f)
            {
                var c = f.Contract; f.Contract = null;
                int r = ScoutingService.PerceivedAbility(w, startup, f);
                f.Contract = c;
                return r;
            }
            var firstPro = members.Where(f => PromotionSetup.SeedScore(f) == 60)
                .OrderByDescending(f => f.Nationality == startup.BaseRegion ? 1 : 0)
                .ThenByDescending(TheirRead).ThenBy(f => f.Id.Value).First();
            if (seen(firstPro) < bestRead) differs++;
        }
        Assert.True(differs > 0, "the read must separate men the truth ties");
    }

    [Fact]
    public void ACrossoverIsPricedOnTheHostsRead_AtTheCallSite()
    {
        // Replay the shared stream to find the one draw SignContract took (the crossover's last), then price the
        // man both ways: the contract on his books is the read's price, not the truth's.
        var w = FogKit.Modern(5);
        var date = (w.StartDate == default ? new DateOnly(2026, 1, 1) : w.StartDate).AddMonths(6);
        const ulong seed = 4242;
        var rng = new SimRandom(seed);
        var before = w.Fighters.Keys.ToHashSet();
        WorldEvents.MaybeCrossover(w, date, rng);
        var f = w.Fighters.Values.Single(x => !before.Contains(x.Id));
        var host = w.Get(f.Contract!.Promotion)!;

        ulong next = rng.NextULong();
        var probe = new SimRandom(seed);
        int n = 0;
        while (probe.NextULong() != next) n++;   // the call took n draws; SignContract's was the n-th
        SimRandom AtTheSigning() { var r = new SimRandom(seed); for (int i = 0; i < n - 1; i++) r.NextULong(); return r; }

        var signed = f.Contract;
        f.Contract = null;   // as he stood when the host read him
        int read = ScoutingService.PerceivedAbility(w, host, f);
        var asRead = PromotionSetup.SignContract(w, host, date, f, AtTheSigning(), read);
        var asTruth = PromotionSetup.SignContract(w, host, date, f, AtTheSigning());
        f.Contract = signed;

        Assert.NotEqual(asTruth.PursePerFight, asRead.PursePerFight);   // the read and the truth price him differently
        Assert.Equal(asRead.PursePerFight, signed.PursePerFight);
        Assert.Equal(asRead.FightsRemaining, signed.FightsRemaining);
    }

    [Fact]
    public void AnOriginsPioneerIsPricedOnTheHousesRead_AtTheCallSite()
    {
        // The one authored arrival who signs with a house (Moreira, DLB). His signing draws on its own keyed stream,
        // so the price can be rebuilt both ways; the deal on his books is the read's.
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(3, 0));
        var a = OriginsArrivals.All.Single(x => x.Pioneer?.Fighter.Org is not null && x.Pioneer.Fighter.Sex == Sex.Male);
        var house = w.Promotions.Values.Single(p => p.Abbreviation == a.Pioneer!.Fighter.Org);
        house.Prestige = 60;   // a house big enough that its cap does not flatten every price onto the floor
        foreach (var v in w.Venues.Values.Where(v => v.City == house.HomeCity)) v.Capacity = 30_000;
        var date = w.CurrentDate;
        SimRandom Keyed() => new(SimRandom.Mix(w.RngSeed, OriginsTimeline.KeyHash(a.Key) ^ OriginsFounding.ArrivalSalt));

        // The house's misjudgement is a hash of (house, man), and a price is rounded to $500: a small misread can
        // land on the truth's price. Let him arrive under the next fighter id until the two prices part (the same
        // man, the same draw, only the hash moves), then check the deal he signed.
        for (int tries = 0; tries < 40; tries++)
        {
            var before = w.Fighters.Keys.ToHashSet();
            OriginsFounding.Arrive(w, a, date);
            var f = w.Fighters.Values.Single(x => !before.Contains(x.Id));
            var signed = f.Contract!;
            Assert.Equal(house.Id, signed.Promotion);

            f.Contract = null;   // as he stood when the house read him
            int read = ScoutingService.PerceivedAbility(w, house, f);
            decimal Price(int? seen) =>
                Math.Max(PromotionSetup.SignContract(w, house, date, f, Keyed(), seen).PursePerFight.Amount, Contracts_OriginsFloor);
            decimal asRead = Price(read), asTruth = Price(null);
            f.Contract = signed;

            if (asRead != asTruth)
            {
                Assert.Equal(asRead, signed.PursePerFight.Amount);   // the truth's price would be asTruth
                return;
            }
            w.Fighters.Remove(f.Id);   // not this one: the next man through the door gets the next id
        }
        Assert.Fail("no fighter id within forty where the read and the truth price him differently");
    }

    private const decimal Contracts_OriginsFloor = MmaManager.Core.Sim.Contracts.Negotiator.OriginsPurseFloor;

    // ---- helpers --------------------------------------------------------------------------------------------------

    private static IEnumerable<Promotion> AiHouses(World w) =>
        w.Promotions.Values.Where(p => !p.Defunct && p.Id != w.PlayerPromotion).OrderBy(p => p.Id.Value);

    // Two active men of the given ages, from the same region (so the same rooms will take them), not champions.
    private static (Fighter A, Fighter B) AgedPair(World w, int ageA, int ageB, bool freeAgents = false)
    {
        bool Ok(Fighter f) => f.Status == FighterStatus.Active && (!freeAgents || f.Contract is null)
                              && !w.Titles.Values.Any(t => t.CurrentChampion == f.Id)
                              && !w.Promotions.Values.Any(p => p.Divisions.Any(d => d.Rankings.Contains(f.Id)));
        foreach (var region in Enum.GetValues<Region>())
        {
            var a = w.Fighters.Values.Where(f => Ok(f) && f.Nationality == region && f.AgeOn(w.CurrentDate) == ageA).OrderBy(f => f.Id.Value).FirstOrDefault();
            var b = w.Fighters.Values.Where(f => Ok(f) && f.Nationality == region && f.AgeOn(w.CurrentDate) == ageB).OrderBy(f => f.Id.Value).FirstOrDefault();
            if (a is not null && b is not null && w.Camps.Values.Any(c => w.Get(c.City)?.Region == region && c.CoachQuality > 10))
                return (a, b);
        }
        throw new InvalidOperationException("no pair of those ages");
    }

    private static void Form(Fighter f, int wins, int losses, int finishes)
    {
        f.Record.Wins = wins; f.Record.Losses = losses; f.Record.WinsByKo = finishes; f.Record.WinsByDecision = wins - finishes;
        f.Record.CurrentStreak = wins > 0 ? wins : -losses;
    }

    // A fresh stream whose first draws satisfy the test's need (found by trying seeds in order: deterministic).
    private static SimRandom RngWhere(Func<SimRandom, bool> wanted)
    {
        for (ulong s = 1; s < 10_000; s++)
            if (wanted(new SimRandom(s))) return new SimRandom(s);
        throw new InvalidOperationException("no such stream");
    }
}
