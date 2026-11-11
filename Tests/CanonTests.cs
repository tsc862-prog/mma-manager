using System.Text.RegularExpressions;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// The authored world made true to itself: keys that name exactly one person,
/// retired legends who read as retired, founded starts with no repeated names,
/// quotes that agree with who is being talked about, a day-one feed derived from
/// what the build actually holds, and bios whose numbers survive the sim.
/// </summary>
public class CanonTests
{
    private static World Build(ulong seed = 9) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    // --- keys that name one person -------------------------------------------

    /// <summary>How many authored people (roster and legends) a bond key picks out: full name first, then surname.</summary>
    private static int Matches(string key)
    {
        int full = DesignedRoster.All.Count(d => $"{d.First} {d.Last}" == key)
                   + DesignedLegends.All.Count(l => $"{l.First} {l.Last}" == key);
        return full > 0 ? full
            : DesignedRoster.All.Count(d => d.Last == key) + DesignedLegends.All.Count(l => l.Last == key);
    }

    [Fact]
    public void EveryAuthoredBondEndpoint_NamesExactlyOnePerson()
    {
        foreach (var bond in DesignedRoster.Bonds)
        {
            Assert.True(Matches(bond.A) == 1, $"bond endpoint '{bond.A}' names {Matches(bond.A)} people");
            Assert.True(Matches(bond.B) == 1, $"bond endpoint '{bond.B}' names {Matches(bond.B)} people");
        }
    }

    [Fact]
    public void EveryLastNameKeyedTable_NamesExactlyOneActiveDesignedFighter()
    {
        // These tables are applied by last name to the designed ROSTER; a shared
        // surname would silently hit the wrong person (or both).
        var keys = DesignedRoster.Dispositions.Keys
            .Concat(DesignedRoster.CampHomes.Keys)
            .Concat(DesignedRoster.PurseFactors.Keys)
            .Concat(DesignedRoster.ContractStages.Keys)
            .Concat(DesignedRoster.All.Where(d => d.RivalOf is not null).Select(d => d.RivalOf!));
        foreach (var key in keys.Distinct())
        {
            int n = DesignedRoster.All.Count(d => d.Last == key);
            Assert.True(n == 1, $"'{key}' names {n} designed fighters");
        }

        // The only surname two authored people share today.
        var shared = DesignedRoster.All.Select(d => d.Last)
            .Concat(DesignedLegends.All.Select(l => l.Last))
            .GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Equal(new[] { "Bandeira" }, shared);
    }

    [Theory]
    [InlineData(9UL)]
    [InlineData(42UL)]
    [InlineData(777UL)]
    public void Souza_IsMentoredByThe_Legend_NotTheRivalChampion(ulong seed)
    {
        var world = Build(seed);
        var souza = world.Fighters.Values.Single(f => f.Designed && f.LastName == "Souza");
        var paulo = world.Fighters.Values.Single(f => f.Designed && f.FirstName == "Paulo" && f.LastName == "Bandeira");
        var wallace = world.Fighters.Values.Single(f => f.Designed && f.FirstName == "Wallace" && f.LastName == "Bandeira");

        var mentor = world.Relationships.Single(r => r.Kind == RelationshipKind.Mentor && r.B == souza.Id);
        Assert.Equal(paulo.Id, mentor.A);
        Assert.Equal(FighterStatus.Retired, paulo.Status);
        // Wallace has no mentor: his only bond is the family tie to his uncle, the same man.
        Assert.DoesNotContain(world.Relationships, r => r.Kind == RelationshipKind.Mentor && r.Involves(wallace.Id));
        var tie = Assert.Single(world.Relationships, r => r.Involves(wallace.Id));
        Assert.Equal(RelationshipKind.Family, tie.Kind);
        Assert.True(tie.IsBetween(wallace.Id, paulo.Id));
    }

    [Fact]
    public void EveryAuthoredBond_ResolvesToItsPair_OnDayOne()
    {
        var world = Build();
        Fighter One(string key)
        {
            var authored = world.Fighters.Values.Where(f => f.Designed).ToList();
            var full = authored.Where(f => $"{f.FirstName} {f.LastName}" == key).ToList();
            return full.Count == 1 ? full[0] : authored.Single(f => f.LastName == key);
        }
        foreach (var bond in DesignedRoster.Bonds)
        {
            var rel = world.Relationships.SingleOrDefault(r => r.IsBetween(One(bond.A).Id, One(bond.B).Id));
            Assert.True(rel is not null, $"{bond.A}/{bond.B} should be bonded on day one");
            Assert.Equal(bond.Kind, rel!.Kind);
            Assert.Equal(One(bond.A).Id, rel.A);
        }
    }

    // --- founded starts ------------------------------------------------------

    [Theory]
    [InlineData(1UL)]
    [InlineData(42UL)]
    public void FoundedStart_HasNoRepeatedFightNames_AndNoCopiesOfAuthoredOnes(ulong seed)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed,
            Founding = new WorldBuilder.FoundingSpec(),
        });
        var real = world.Fighters.Values.Where(f => !f.LocalTalent).ToList();

        var dupNicks = real.Where(f => f.Nickname is not null)
            .GroupBy(f => f.Nickname!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => $"{g.Key} x{g.Count()}").ToList();
        Assert.True(dupNicks.Count == 0, "duplicate fight names: " + string.Join(", ", dupNicks.Take(10)));

        // Nobody borrowed an authored fight name (they used to: "The Lion", "The Viper",
        // "Hollywood" all turned up on founding talent beside the headliner who owns them).
        var authored = DesignedRoster.All.Where(d => d.Nickname is not null).Select(d => d.Nickname!)
            .Concat(DesignedLegends.All.Where(l => l.Nickname is not null).Select(l => l.Nickname!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(real, f => !f.Designed && f.Nickname is not null && authored.Contains(f.Nickname));
    }

    // --- retired people ------------------------------------------------------

    [Fact]
    public void RetiredLegends_ReadAsRetired_NotAsFreeAgents()
    {
        var world = Build();
        foreach (var spec in DesignedLegends.All)
        {
            var f = world.Fighters.Values.Single(x => x.Designed && x.FirstName == spec.First && x.LastName == spec.Last);
            var text = Biographer.Fighter(world, f);
            Assert.DoesNotContain("free agent", text);
            Assert.DoesNotContain("highest bidder", text);
            Assert.Contains("Retired", text);

            if (spec.HeadCoachOf is { } room)
                Assert.Contains($"head coach of {room}", text);
            else
                Assert.DoesNotContain("head coach", text);

            foreach (var reign in spec.Reigns)
                Assert.Contains($"{reign.Defenses} defense", text);
            Assert.Contains($"Finished {spec.Wins}-{spec.Losses}.", text);
        }
    }

    [Fact]
    public void LegendsAndTheFiveBios_CarryTheirAuthoredDisposition()
    {
        var world = Build();
        Assert.Equal(DesignedLegends.All.Count, DesignedLegends.Dispositions.Count);
        foreach (var spec in DesignedLegends.All)
        {
            var f = world.Fighters.Values.Single(x => x.Designed && x.FirstName == spec.First && x.LastName == spec.Last);
            Assert.Equal(DesignedLegends.Dispositions[$"{spec.First} {spec.Last}"], f.Disposition);
        }

        foreach (var last in new[] { "Volkova", "Reyes", "Seo", "Carvalho", "Fujimoto" })
        {
            var f = world.Fighters.Values.Single(x => x.Designed && x.LastName == last);
            Assert.Equal(DesignedRoster.Dispositions[last], f.Disposition);
        }
    }

    // --- quotes --------------------------------------------------------------

    private static readonly Regex MaleWords = new(@"\b(he|him|his|he's|himself|a man)\b", RegexOptions.IgnoreCase);

    [Fact]
    public void QuotesAboutAWoman_NeverCallHerHe()
    {
        var world = Build();
        var speaker = world.Fighters.Values.First(f => f.Designed && f.Sex == Sex.Female && f.Status == FighterStatus.Active);
        var woman = world.Fighters.Values.First(f => f.Sex == Sex.Female && f.Id != speaker.Id);
        var man = world.Fighters.Values.First(f => f.Sex == Sex.Male);
        bool flipped = false;

        foreach (var d in Enum.GetValues<Disposition>())
        foreach (var m in Enum.GetValues<Moment>())
        {
            speaker.Disposition = d;
            for (int day = 0; day < 400; day++)
            {
                var on = new DateOnly(2026, 1, 1).AddDays(day);
                string about = Quotes.Say(speaker, m, on, woman);
                Assert.False(MaleWords.IsMatch(about), $"{d}/{m}: {about}");

                // Telling the line who it is about must never change WHICH line is picked.
                string baseline = Quotes.Say(speaker, m, on);
                Assert.Equal(baseline, Quotes.Say(speaker, m, on, man));
                flipped |= baseline != about;
            }
        }
        Assert.True(flipped, "at least some lines talk about the other person and must flip");
    }

    // --- the day-one feed ----------------------------------------------------

    [Fact]
    public void TitleHeadlines_AreNotOneSentenceOnRepeat_AndLabelDivisionsProperly()
    {
        var world = Build();
        var titles = world.News.Where(n => n.Category == NewsCategory.TitleChange).ToList();
        Assert.True(titles.Count >= 20, $"day one reports plenty of belts ({titles.Count})");

        int claims = titles.Count(n => n.Headline.Contains(" claims the "));
        Assert.True(claims * 2 <= titles.Count, $"{claims} of {titles.Count} read 'claims the'");
        var verbs = titles.Select(n => Regex.Match(n.Headline, @"\b(takes|wins|outpoints|stops|submits|beats|claims)\b").Value).Distinct().ToList();
        Assert.True(verbs.Count >= 3, "headlines vary with what the lineage and bout show: " + string.Join(",", verbs));

        Assert.DoesNotContain(world.News, n => n.Body.Contains("LightHeavyweight") || n.Body.Contains("Strawweight division") || n.Body.Contains("Bantamweight division"));
        foreach (var n in titles.Where(n => n.Headline.Contains("Women's")))
            Assert.True(n.Body.Contains("women's"), $"a women's belt reads as one: {n.Body}");
    }

    [Fact]
    public void FeudBodies_AreEachDerivedFromWhatTheWorldHolds()
    {
        var world = Build();
        var feuds = world.News.Where(n => n.Category == NewsCategory.Rivalry).ToList();
        Assert.True(feuds.Count >= 6);
        Assert.Equal(feuds.Count, feuds.Select(n => n.Body).Distinct().Count());
        Assert.DoesNotContain(feuds, n => n.Body.Contains("a grudge nobody has to sell"));

        // Seo and Mercer have a pinned meeting: the body says so and says who won.
        var seo = feuds.Single(n => n.Headline.Contains("Seo") && n.Headline.Contains("Mercer"));
        Assert.Contains("Mercer beat Seo", seo.Body);
        Assert.Contains("split decision", seo.Body);
    }

    [Fact]
    public void EveryMentorship_GetsAHeadline_AndOnlyHeadCoachesAreCalledThat()
    {
        var world = Build();
        int mentorships = world.Relationships.Count(r => r.Kind == RelationshipKind.Mentor);
        var lines = world.News.Where(n => n.Headline.Contains("'s corner")).ToList();
        Assert.Equal(mentorships, lines.Count);
        Assert.DoesNotContain(lines, n => n.Body.Contains("head coach"));

        // Volkov trains at Volk Sambo Club, which Antonov runs: he is not its head coach.
        var volkov = lines.Single(n => n.Headline.StartsWith("Dmitri"));
        Assert.Contains("works out of Volk Sambo Club", volkov.Body);
        // Paulo Bandeira does run his room.
        var paulo = lines.Single(n => n.Headline.StartsWith("Paulo"));
        Assert.Contains("runs Bandeira Jiu-Jitsu", paulo.Body);
    }

    // --- bios that must survive the sim --------------------------------------

    [Theory]
    [InlineData("Moyo", 11, 0, "Eleven fights")]
    [InlineData("Vance", 8, 0, "Eight fights")]
    [InlineData("Tsereteli", 7, 0, "seven fights")]
    [InlineData("Weiss", 13, 1, "once in fourteen fights")]
    [InlineData("Nowak", 18, 4, "four times in twenty-two fights")]
    public void BiosThatQuoteARecord_QuoteTheRecordTheyHave(string last, int wins, int losses, string phrase)
    {
        var f = DesignedRoster.All.Single(d => d.Last == last);
        Assert.Equal((wins, losses), (f.Wins, f.Losses));
        Assert.Contains(phrase, f.Bio);
        // And no bio claims a finish count or an unbroken defence the sim will not keep.
        Assert.DoesNotContain("finishes", f.Bio);
        Assert.DoesNotContain("never been finished", f.Bio);
        Assert.DoesNotContain("never been knocked down", f.Bio);
    }

    [Fact]
    public void LegendReigns_MatchTheirBios_AndNobodyReignsBeforeTheHouseExisted()
    {
        // The reign lengths the bios state.
        int Years(string last, string org)
        {
            var r = DesignedLegends.All.Single(l => l.Last == last).Reigns.Single(x => x.Org == org);
            return r.WonYearsAgo - r.LostYearsAgo;
        }
        Assert.Equal(6, Years("Matsuda", "ICF"));       // "six years"
        Assert.Equal(5, Years("Wongsawat", "ICF"));     // "five years"
        Assert.Equal(4, Years("Kroll", "AFC"));         // "four years"
        Assert.Equal(5, Years("Vos", "VMMA"));          // "for five years"

        // A belt cannot have a champion before its promotion was founded.
        int startYear = new WorldBuilder.Options().StartDate.Year;
        foreach (var legend in DesignedLegends.All)
            foreach (var reign in legend.Reigns)
            {
                var owner = DesignedStaff.Owners.Single(o => o.Org == reign.Org);
                Assert.True(startYear - reign.WonYearsAgo >= owner.FoundedYear,
                    $"{legend.Last} won the {reign.Org} belt in {startYear - reign.WonYearsAgo}, before it was founded in {owner.FoundedYear}");
            }
    }
}
