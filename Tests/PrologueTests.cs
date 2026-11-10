using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;

namespace MmaManager.Tests;

/// <summary>
/// The day-one world beyond the roster sheet: the era before the player (retired
/// greats sitting at the head of the belts' lineages and running the famous
/// rooms), the warm half of the relationship graph the bios always claimed, the
/// rooms fighters' stories put them in, unique fight names, and a news feed with
/// something in it before the first tick.
/// </summary>
public class PrologueTests
{
    private static World Build(ulong seed = 9) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    // --- the era before -----------------------------------------------------

    [Fact]
    public void TheBeltsHaveAPast_NotJustAPresent()
    {
        var world = Build();

        // Matsuda's nine defenses sit at the head of the ICF lightweight lineage,
        // ahead of everything the history builder wrote.
        var icf = world.Promotions.Values.First(p => p.Abbreviation == "ICF");
        var lw = world.Titles.Values.First(t => t.Promotion == icf.Id
            && t.WeightClass == WeightClass.Lightweight
            && world.Fighters[t.CurrentChampion!.Value].Sex == Sex.Male);

        Assert.True(lw.Lineage.Count >= 2, "a belt with a legend on it is a line of succession");
        var first = lw.Lineage[0];
        Assert.Equal("Matsuda", world.Fighters[first.Champion].LastName);
        Assert.Equal(9, first.Defenses);
        Assert.NotNull(first.LostOn);

        // A line of succession runs one way: every reign starts after the one
        // before it ended.
        for (int i = 1; i < lw.Lineage.Count; i++)
            Assert.True(lw.Lineage[i].WonOn >= lw.Lineage[i - 1].LostOn,
                "reigns must not overlap");
    }

    [Fact]
    public void NobodyWinsAWorldTitleOnTheirDebut()
    {
        var world = Build();
        var fightDates = world.Events.Values
            .SelectMany(e => e.Card.Select(f => (f.Id, e.Date)))
            .ToDictionary(x => x.Id, x => x.Date);

        // An UNBEATEN champion's "current run" is their whole career, which used
        // to hand them the belt in their first professional fight — the AFC
        // featherweight champion opened the game as an eight-year, thirteen-
        // defense champion who had never fought a non-title bout.
        foreach (var title in world.Titles.Values)
        {
            var reign = title.Lineage.LastOrDefault(r => r.LostOn is null);
            if (reign is null || world.Get(reign.Champion) is not { } champ) continue;
            if (champ.BoutHistory.Count == 0) continue;   // legends and day-one crownings

            Assert.True(reign.Defenses <= 8,
                $"{champ.FullName} opens on a {reign.Defenses}-defense reign of the {title.Name}");

            // They fought their way up to it: real bouts on the record before the
            // belt-winning night. (A regional feeder belt at nineteen is fine —
            // a title in your professional debut is not.)
            int before = champ.BoutHistory.Count(id => fightDates.TryGetValue(id, out var d) && d < reign.WonOn);
            Assert.True(before >= 2,
                $"{champ.FullName} won the {title.Name} with {before} prior bouts");
        }
    }

    [Fact]
    public void Legends_AreRetired_AndNeverTakeARosterSlotOrARanking()
    {
        var world = Build();
        var legends = world.Fighters.Values.Where(f =>
            f.Designed && DesignedLegends.All.Any(l => l.Last == f.LastName && l.First == f.FirstName)).ToList();

        Assert.Equal(DesignedLegends.All.Count, legends.Count);
        Assert.All(legends, f =>
        {
            Assert.Equal(FighterStatus.Retired, f.Status);
            Assert.Null(f.Contract);
            Assert.NotNull(f.Bio);
            Assert.Empty(f.BoutHistory);                       // their fights predate the staged window
        });

        // Nobody retired shows up in a live ranking.
        var ranked = world.PoundForPound.Concat(world.GlobalRankings.Values.SelectMany(x => x)).ToHashSet();
        Assert.All(legends, f => Assert.DoesNotContain(f.Id, ranked));
    }

    [Fact]
    public void TheFamousRooms_HaveAFamousNameOnTheDoor()
    {
        var world = Build();

        foreach (var spec in DesignedLegends.All.Where(l => l.HeadCoachOf is not null))
        {
            var camp = world.Camps.Values.FirstOrDefault(c => c.Name == spec.HeadCoachOf);
            Assert.NotNull(camp);
            Assert.NotNull(camp!.HeadCoach);
            Assert.Equal(spec.Last, world.Fighters[camp.HeadCoach!.Value].LastName);
        }
    }

    // --- the warm half of the graph -----------------------------------------

    [Fact]
    public void TheBondsTheBiosClaim_AreRealFromDayOne()
    {
        var world = Build();

        Assert.NotEmpty(world.Relationships);
        foreach (var bond in DesignedRoster.Bonds)
        {
            var a = world.Fighters.Values.FirstOrDefault(f => f.Designed && f.LastName == bond.A);
            var b = world.Fighters.Values.FirstOrDefault(f => f.Designed && f.LastName == bond.B);
            Assert.True(a is not null && b is not null, $"{bond.A}/{bond.B} must both exist");

            var rel = world.Relationships.FirstOrDefault(r => r.IsBetween(a!.Id, b!.Id));
            Assert.True(rel is not null, $"{bond.A} and {bond.B} should be bonded on day one");
            Assert.Equal(bond.Kind, rel!.Kind);
        }

        // Volkov really did drill Baranov — and the mentorship pays development.
        var volkov = world.Fighters.Values.First(f => f.LastName == "Volkov" && f.Designed);
        var baranov = world.Fighters.Values.First(f => f.LastName == "Baranov");
        var mentor = world.Relationships.First(r => r.IsBetween(volkov.Id, baranov.Id));
        Assert.Equal(RelationshipKind.Mentor, mentor.Kind);
        Assert.Equal(volkov.Id, mentor.A);      // A is the elder
    }

    [Fact]
    public void FightersTrainWhereTheirStorySaysTheyDo()
    {
        var world = Build();

        foreach (var (last, room) in DesignedRoster.CampHomes)
        {
            var f = world.Fighters.Values.FirstOrDefault(x => x.Designed && x.LastName == last);
            Assert.True(f is not null, $"{last} should be in the designed roster");
            Assert.NotNull(f!.Camp);
            Assert.Equal(room, world.Get(f.Camp!.Value)!.Name);
        }
    }

    [Fact]
    public void TheFamousRooms_OpenInTheTownTheirStoryNames()
    {
        var world = Build();

        string CityOf(string camp) =>
            world.Get(world.Camps.Values.First(c => c.Name == camp).City)!.Name;

        // Three American rooms used to pile into whichever city rolled the
        // biggest market — all of them Las Vegas, including the coastal one.
        Assert.Equal("Las Vegas", CityOf("Apex Combat Club"));
        Assert.Equal("Chicago", CityOf("Iron Range Wrestling"));
        Assert.Equal("Los Angeles", CityOf("Sunset Striking Lab"));
        Assert.Equal("Sao Paulo", CityOf("Aço Paulista"));
        Assert.Equal("Curitiba", CityOf("Furia Curitiba"));
        Assert.Equal("Rio de Janeiro", CityOf("Bandeira Jiu-Jitsu"));

        // No two designed rooms share an address.
        var designedCities = world.Camps.Values.Where(c => c.Lore is not null).Select(c => c.City).ToList();
        Assert.Equal(designedCities.Count, designedCities.Distinct().Count());
    }

    // --- fight names --------------------------------------------------------

    [Fact]
    public void NicknamesAreUnique_AndTheAuthoredOnesAreReserved()
    {
        var world = Build();
        var named = world.Fighters.Values.Where(f => !f.LocalTalent && f.Nickname is not null).ToList();

        Assert.True(named.Count > 200, "most of the sport carries a fight name");
        var dupes = named.GroupBy(f => f.Nickname!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ×{g.Count()}")
            .ToList();
        Assert.True(dupes.Count == 0, "duplicate fight names: " + string.Join(", ", dupes));

        // The IFA franchise keeps his own name — forty other Hammers used to
        // share it with him.
        var volkov = world.Fighters.Values.First(f => f.LastName == "Volkov" && f.Designed);
        Assert.Equal("The Hammer", volkov.Nickname);
    }

    [Fact]
    public void FightNames_FollowTheFightersCulture()
    {
        var world = Build();

        // Brazilians draw from a Brazilian pool, not a region-neutral one — so
        // at least some of the sport's Brazilians carry Portuguese fight names.
        var brazilians = world.Fighters.Values
            .Where(f => !f.LocalTalent && f.Nickname is not null && f.OriginCountryCode == "BRA")
            .Select(f => f.Nickname!)
            .ToList();
        Assert.NotEmpty(brazilians);
        Assert.Contains(brazilians, n => n is "Pitbull" or "O Predador" or "Cavalo" or "Trator"
            or "O Tubarão" or "Furacão" or "Pedreiro" or "O Feiticeiro" or "Correnteza" or "Bagre"
            or "O Carrasco" or "Minotauro" or "Serrote" or "O Ferro" or "Zumbi" or "Passarinho"
            or "O Dentista" or "Marreta" or "Capitão" or "O Silencioso" or "Rocha" or "Formiga");
    }

    // --- the feed -----------------------------------------------------------

    [Fact]
    public void DayOne_OpensOnAFeedWithHistoryInIt()
    {
        var world = Build();

        Assert.True(world.News.Count >= 15, $"the sport should have been reporting on itself ({world.News.Count})");
        Assert.All(world.News, n => Assert.True(n.Date <= world.CurrentDate, "nothing is reported before it happened"));

        // Appended oldest-first, because the feed renders the list reversed.
        for (int i = 1; i < world.News.Count; i++)
            Assert.True(world.News[i].Date >= world.News[i - 1].Date);

        // The categories a player would expect to see on arrival.
        Assert.Contains(world.News, n => n.Category == NewsCategory.TitleChange);
        Assert.Contains(world.News, n => n.Category == NewsCategory.Rivalry);
        Assert.Contains(world.News, n => n.Category == NewsCategory.Award);   // the hall of fame class
    }

    [Fact]
    public void TheBacklogReportsTheWorldThatWasActuallyBuilt()
    {
        var world = Build();

        // Every title-change headline matches a real reign on a real lineage.
        foreach (var item in world.News.Where(n => n.Category == NewsCategory.TitleChange))
        {
            Assert.NotNull(item.AboutFighter);
            var champ = world.Fighters[item.AboutFighter!.Value];
            Assert.Contains(world.Titles.Values, t =>
                t.Lineage.Any(r => r.Champion == champ.Id && r.WonOn == item.Date));
        }

        // Every free-agency headline is about somebody genuinely unsigned.
        foreach (var item in world.News.Where(n => n.Headline.EndsWith("is a free agent")))
            Assert.Null(world.Fighters[item.AboutFighter!.Value].Contract);
    }

    [Fact]
    public void ATurnedOffDesignedRoster_StillBuildsACleanWorld()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3, UseDesignedRoster = false });

        Assert.Empty(world.Fighters.Values.Where(f => f.Designed));
        Assert.DoesNotContain(world.Camps.Values, c => c.HeadCoach is not null);
        Assert.NotEmpty(world.News);   // the backlog is derived, so it works either way
    }
}
