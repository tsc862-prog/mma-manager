using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// The second slice of the day-one story bible: authored contract stages, feud
/// heat, faces of the companies, signature shows and the lines the first slice
/// skipped. Every table is applied after the shared streams have drawn, so one
/// guard test proves the world is the same world with the tables off.
/// </summary>
public class StoriesBatch1BTests
{
    public static readonly TheoryData<ulong> Seeds = new() { 1UL, 9UL, 42UL, 777UL };

    private static World Build(ulong seed, bool stories = true, int player = 0) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed, AuthoredStories = stories, PlayerPromotionIndex = player });

    private static Fighter Last(World w, string last) =>
        w.Fighters.Values.Single(f => f.Designed && f.LastName == last && f.Status != FighterStatus.Retired);

    private static Fighter Named(World w, string full) =>
        w.Fighters.Values.Single(f => f.Designed && $"{f.FirstName} {f.LastName}" == full);

    // --- contract stages -----------------------------------------------------

    [Theory]
    [MemberData(nameof(Seeds))]
    public void EveryContractStage_LandsOnTheNamedFighter_AtTheirOwnHouse(ulong seed)
    {
        var world = Build(seed);
        foreach (var (last, fights) in DesignedRoster.ContractStages)
        {
            var f = Last(world, last);
            var spec = DesignedRoster.All.Single(d => d.Last == last);
            var contract = Assert.IsType<Contract>(f.Contract);
            Assert.Equal(fights, contract.FightsRemaining);
            Assert.Equal(spec.Org, world.Get(contract.Promotion)!.Abbreviation);
        }
    }

    [Fact]
    public void TheStoriesThatLeanOnAContractStage_AreTheStagesWritten()
    {
        // "the offer is ready the day the champion's deal runs out" (Sheikh Faisal on Mercer)
        Assert.Equal(1, DesignedRoster.ContractStages["Mercer"]);
        // Every stage names a signed designed fighter, and the underpaid ones are priced down.
        foreach (var last in DesignedRoster.ContractStages.Keys)
            Assert.NotNull(DesignedRoster.All.Single(d => d.Last == last).Org);
        Assert.All(new[] { "Cole", "Huanca", "Pryor", "Okafor", "Kondo" },
            l => Assert.True(DesignedRoster.PurseFactors[l] < 1m, l));
    }

    // --- feud heat -------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Seeds))]
    public void TheTwoFeudsMeantToBurnOn_StartHot_AndTheRestStayAtTheOrdinaryHeat(ulong seed)
    {
        var world = Build(seed);
        foreach (var (a, b) in new[] { ("Rivas", "Sales"), ("Pryor", "Cole") })
        {
            var rv = Assert.Single(world.Rivalries.Where(r => r.IsBetween(Last(world, a).Id, Last(world, b).Id)));
            Assert.Equal(80, rv.Intensity);
        }
        var hot = DesignedRoster.RivalHeat.Keys.SelectMany(k => k.Split('|')).ToHashSet();
        foreach (var spec in DesignedRoster.All.Where(d => d.RivalOf is not null))
        {
            var rv = world.Rivalries.Single(r => r.IsBetween(Last(world, spec.Last).Id, Last(world, spec.RivalOf!).Id));
            if (!(hot.Contains(spec.Last) && hot.Contains(spec.RivalOf!))) Assert.Equal(60, rv.Intensity);
        }
    }

    [Fact]
    public void EveryRivalHeatKey_NamesAnAuthoredFeud()
    {
        foreach (var key in DesignedRoster.RivalHeat.Keys)
        {
            var (a, b) = (key.Split('|')[0], key.Split('|')[1]);
            Assert.Contains(DesignedRoster.All, d => (d.Last == a && d.RivalOf == b) || (d.Last == b && d.RivalOf == a));
        }
    }

    // --- faces -------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Seeds))]
    public void EveryAuthoredFace_IsAnActiveFighterOfThatHouse_AndTheBiographerNamesThem(ulong seed)
    {
        // The player (index 0 here) picks their own face; every AI house gets the authored one,
        // so the player sits at the bottom of the ladder, in a house that has no authored face.
        var world = Build(seed, player: WorldBuilder.Options.DefaultPromotions.Length - 1);
        foreach (var (org, name) in DesignedRoster.Faces)
        {
            var house = world.Promotions.Values.Single(p => p.Abbreviation == org);
            var face = Named(world, name);
            Assert.Equal(FighterStatus.Active, face.Status);
            Assert.Equal(house.Id, face.Contract!.Promotion);
            Assert.Equal(face.Id, house.FaceOfCompany);
            Assert.Same(face, Franchise.FaceOf(world, house));
            Assert.Contains($"Its marquee attraction is {face.FullName}.", Biographer.Promotion(world, house));
        }
        // The AFC's face is the courted champion, not merely its most popular name.
        Assert.Equal("Dane Mercer", DesignedRoster.Faces["AFC"]);
    }

    [Fact]
    public void TheHouseThePlayerOwns_IsLeftToThePlayerToCrown()
    {
        var world = Build(9);   // the player owns the AFC, whose authored face is Mercer
        Assert.Null(world.Get(world.PlayerPromotion)!.FaceOfCompany);
        Assert.NotNull(world.Promotions.Values.Single(p => p.Abbreviation == "ICF").FaceOfCompany);
    }

    [Fact]
    public void EveryFace_IsADesignedFighterAtTheOrgTheyAreFaceOf()
    {
        foreach (var (org, name) in DesignedRoster.Faces)
        {
            var spec = Assert.Single(DesignedRoster.All, d => $"{d.First} {d.Last}" == name);
            Assert.Equal(org, spec.Org);
        }
    }

    // --- the RNG guard ---------------------------------------------------------------

    /// <summary>
    /// With the stage, heat and face tables off the world is the SAME world: every
    /// fighter is the same fighter on the same deal at the same house with the same
    /// record, and the only differences are the authored fields themselves.
    /// </summary>
    [Fact]
    public void AuthoredStories_ChangeOnlyTheAuthoredFields()
    {
        var on = Build(11);
        var off = Build(11, stories: false);

        Assert.Equal(off.Fighters.Count, on.Fighters.Count);
        Assert.Equal(off.Events.Count, on.Events.Count);
        Assert.Equal(off.Rivalries.Count, on.Rivalries.Count);
        Assert.Equal(off.Relationships.Count, on.Relationships.Count);
        int staged = 0;
        foreach (var a in on.Fighters.Values)
        {
            var b = off.Fighters[a.Id];
            Assert.Equal(b.FirstName, a.FirstName);
            Assert.Equal(b.LastName, a.LastName);
            Assert.Equal(b.Nickname, a.Nickname);
            Assert.Equal(b.BirthDate, a.BirthDate);
            Assert.Equal(b.HomeCity, a.HomeCity);
            Assert.Equal(b.Camp, a.Camp);
            Assert.Equal(b.PotentialAbility, a.PotentialAbility);
            Assert.Equal(b.Striking.PunchPower, a.Striking.PunchPower);
            Assert.Equal(b.Mental.FightIQ, a.Mental.FightIQ);
            Assert.Equal(b.Loyalty, a.Loyalty);
            Assert.Equal(b.Contract?.Promotion, a.Contract?.Promotion);
            Assert.Equal(b.Contract?.PursePerFight, a.Contract?.PursePerFight);
            Assert.Equal(b.Contract?.WinBonus, a.Contract?.WinBonus);
            Assert.Equal(b.Record.Wins, a.Record.Wins);
            Assert.Equal(b.Record.Losses, a.Record.Losses);
            Assert.Equal(b.Record.TotalBouts, a.Record.TotalBouts);

            bool authored = a.Designed && DesignedRoster.ContractStages.ContainsKey(a.LastName);
            if (a.Contract is not null && !authored)
                Assert.Equal(b.Contract!.FightsRemaining, a.Contract.FightsRemaining);
            if (authored && a.Contract is not null && a.Contract.FightsRemaining != b.Contract!.FightsRemaining) staged++;
        }
        Assert.True(staged > 0, "the stage table moved nobody: is it wired in?");

        // Rivalries: the same pairs, differing only in heat on the authored ones.
        foreach (var r in on.Rivalries)
        {
            var twin = Assert.Single(off.Rivalries.Where(x => x.IsBetween(r.A, r.B)));
            if (r.Intensity != twin.Intensity)
            {
                var (x, y) = (on.Get(r.A)!.LastName, on.Get(r.B)!.LastName);
                Assert.Contains(DesignedRoster.RivalHeat.Keys, k => k == $"{x}|{y}" || k == $"{y}|{x}");
            }
        }

        // Faces: only the authored houses differ.
        foreach (var p in on.Promotions.Values)
            if (p.FaceOfCompany != off.Promotions[p.Id].FaceOfCompany)
                Assert.Contains(p.Abbreviation, DesignedRoster.Faces.Keys);
        Assert.All(off.Promotions.Values, p => Assert.Null(p.FaceOfCompany));
    }

    // --- signature shows -------------------------------------------------------------

    [Fact]
    public void TheTempleFairAndMountainNights_HaveTheirMonths_AndTheirHousesSayWhen()
    {
        var world = Build(9);
        var mfc = world.Promotions.Values.Single(p => p.Abbreviation == "MFC");
        var cpf = world.Promotions.Values.Single(p => p.Abbreviation == "CPF");
        Assert.Equal("MFC Temple Fair Night", SignatureEvents.For(mfc, new DateOnly(2027, 11, 8)));
        Assert.Null(SignatureEvents.For(mfc, new DateOnly(2027, 10, 8)));
        Assert.Equal("CPF Mountain Night", SignatureEvents.For(cpf, new DateOnly(2027, 12, 3)));
        Assert.Null(SignatureEvents.For(cpf, new DateOnly(2027, 11, 3)));
        Assert.Contains("Each November it stages MFC Temple Fair Night", mfc.Lore);
        Assert.Contains("Each December it stages CPF Mountain Night", cpf.Lore);
        // The flagship goes to the biggest house in the org's own town, and that house has lore.
        Assert.Equal("Bangkok", world.Get(mfc.HomeCity!.Value)!.Name);
        Assert.Equal("The Chao Phraya Dome", Venues.For("Bangkok").Item3);
        Assert.NotNull(Venues.LoreOf("The Chao Phraya Dome"));
    }

    // --- homes -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Seeds))]
    public void TheOriginal_LivesWhereTheAFCWasBorn(ulong seed)
    {
        var world = Build(seed);
        var cross = Named(world, "Danielle Cross");
        Assert.Equal("Las Vegas", world.Get(cross.HomeCity!.Value)!.Name);
    }

    // --- the lines the first slice skipped ---------------------------------------------

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Okoyes_Background_AgreesWithTheWorldItNames(ulong seed)
    {
        var world = Build(seed);
        var okoye = Assert.Single(world.StaffMarket, s => s.Name == "Rosalind Okoye");   // on the market on day one
        Assert.Equal(StaffRole.Scouting, okoye.Role);
        Assert.Contains("Fatima Diallo", okoye.Background);

        // "a Nigerian welterweight off a university mat ... the AFC champion now": Okonjo.
        var okonjo = Last(world, "Okonjo");
        Assert.Equal("NGA", okonjo.OriginCountryCode);
        Assert.Equal(WeightClass.Welterweight, okonjo.WeightClass);
        Assert.Contains("university", okonjo.Bio);
        var afc = world.Promotions.Values.Single(p => p.Abbreviation == "AFC");
        var belt = world.Get(afc.Divisions.Single(d => d.WeightClass == WeightClass.Welterweight && d.Sex == Sex.Male).Title!.Value)!;
        Assert.Equal(okonjo.Id, belt.CurrentChampion);

        // "Left Vanguard": the house exists.
        Assert.Contains("Vanguard", okoye.Background);
        Assert.Contains(world.Promotions.Values, p => p.Abbreviation == "VMMA");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Diallos_Bio_NamesTwoPeopleWhoExist_AndSheIsStillUnsigned(ulong seed)
    {
        var world = Build(seed);
        var diallo = Named(world, "Fatima Diallo");
        Assert.Contains("Rosalind Okoye", diallo.Bio);
        Assert.Contains("Margaux Bisset", diallo.Bio);
        Assert.Contains(world.StaffMarket, s => s.Name == "Rosalind Okoye");
        var bisset = Named(world, "Margaux Bisset");
        Assert.Equal(FighterStatus.Retired, bisset.Status);
        Assert.Equal(Sex.Female, bisset.Sex);
        Assert.Equal(WeightClass.Flyweight, bisset.WeightClass);
        Assert.Equal(WeightClass.Flyweight, diallo.WeightClass);
        Assert.Null(diallo.Contract);
        // "a file ... since early in Diallo's career": her whole career is six fights.
        Assert.Equal(6, diallo.Record.TotalBouts);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void TheThreeLastYearBios_AgreeWithTheirRecordsAndTheirFeedLines(ulong seed)
    {
        var world = Build(seed);
        // Tanner: the dethroned AFC king ("one more chapter"), two surgeries past the reign.
        var tanner = Named(world, "Brock Tanner");
        var afc = world.Promotions.Values.Single(p => p.Abbreviation == "AFC");
        var lhw = world.Get(afc.Divisions.Single(d => d.WeightClass == WeightClass.LightHeavyweight && d.Sex == Sex.Male).Title!.Value)!;
        Assert.Contains(lhw.Lineage, r => r.Champion == tanner.Id && r.LostOn is not null);   // dethroned
        Assert.Contains("He says this is the last year, and his surgeon agrees.", tanner.Bio);

        // Lindqvist: twenty-five fights, and "the exam" is the Test.
        var lindqvist = Named(world, "Oskar Lindqvist");
        Assert.Equal(25, lindqvist.Record.TotalBouts);
        Assert.Equal("The Test", lindqvist.Nickname);
        Assert.Contains("the exam has a closing date", lindqvist.Bio);

        // Hattori: a Japanese former ICF challenger who wants a TOKYO goodbye.
        var hattori = Named(world, "Kazuki Hattori");
        Assert.Equal("JPN", hattori.OriginCountryCode);
        Assert.Contains("former ICF title challenger", hattori.Bio);
        Assert.Contains("end in Tokyo", hattori.Bio);

        // Each line has its matching authored headline in the feed, about the same man.
        foreach (var (name, headline) in new[]
        {
            ("Brock Tanner", "Tanner: 'One more run, then the farm'"),
            ("Kazuki Hattori", "Hattori wants a Tokyo goodbye"),
            ("Oskar Lindqvist", "The Test sets an exam date"),
        })
            Assert.Equal(Named(world, name).Id, world.News.Single(n => n.Headline == headline).AboutFighter);
    }

    [Fact]
    public void TheSecondSliceHeadlines_AreAppendedAfterTheFirst()
    {
        var all = DesignedHeadlines.All.Select(h => h.Headline).ToList();
        Assert.Equal("Second Bell is answering the phone", all[all.Count - 7]);
        Assert.Equal("Mokoena: 'Van Zyl picked a side'", all[all.Count - 6]);
        Assert.Equal("Pryor headlines for gas money, and says he is fine with it", all[^1]);
    }
}
