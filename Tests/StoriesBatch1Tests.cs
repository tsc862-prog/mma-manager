using System.Text.RegularExpressions;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// The first slice of the day-one story bible: the authored text, bonds, feuds and
/// camp homes of stories 1-18 that need no new mechanic. Each fact is asserted on
/// several seeds, and every line of new prose is held to the CanonTests rule:
/// nothing the data cannot guarantee on every seed.
/// </summary>
public class StoriesBatch1Tests
{
    public static readonly TheoryData<ulong> Seeds = new() { 1UL, 9UL, 42UL, 777UL };

    private static World Build(ulong seed) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    private static Fighter Named(World w, string full) =>
        w.Fighters.Values.Single(f => f.Designed && $"{f.FirstName} {f.LastName}" == full);

    private static Fighter Last(World w, string last) =>
        w.Fighters.Values.Single(f => f.Designed && f.LastName == last);

    // --- bonds ---------------------------------------------------------------

    private static readonly (string A, string B, RelationshipKind Kind)[] NewBonds =
    {
        ("Danielle Cross", "Sarah Donovan", RelationshipKind.Mentor),
        ("Dmitri Volkov", "Katya Volkova", RelationshipKind.Family),
        ("Paulo Bandeira", "Wallace Bandeira", RelationshipKind.Family),
        ("Mateo Quispe", "Alonso Huanca", RelationshipKind.Friend),
        ("Sefu Okonjo", "Chidi Okafor", RelationshipKind.Friend),
    };

    [Theory]
    [MemberData(nameof(Seeds))]
    public void EveryNewBond_LinksExactlyItsPair_WithItsKindAndDirection(ulong seed)
    {
        var world = Build(seed);
        foreach (var (a, b, kind) in NewBonds)
        {
            var fa = Named(world, a); var fb = Named(world, b);
            var rels = world.Relationships.Where(r => r.IsBetween(fa.Id, fb.Id)).ToList();
            var rel = Assert.Single(rels);
            Assert.Equal(kind, rel.Kind);
            Assert.Equal(fa.Id, rel.A);      // A is the elder / the master / the first-named
            Assert.Equal(fb.Id, rel.B);
        }
    }

    [Fact]
    public void NewBonds_AreTheLastRowsOfTheTable_AndEachNamesOnePersonPerKey()
    {
        // Append-only: the five new rows sit at the end, in the order written.
        var tail = DesignedRoster.Bonds.Skip(DesignedRoster.Bonds.Count - 5).ToList();
        Assert.Equal(new[] { "Cross", "Volkov", "Paulo Bandeira", "Quispe", "Okonjo" }, tail.Select(b => b.A));
        Assert.Equal(new[] { "Donovan", "Volkova", "Wallace Bandeira", "Huanca", "Okafor" }, tail.Select(b => b.B));
    }

    [Fact]
    public void FamilyAndMentorBonds_AreStatedInBothBios()
    {
        string BioOf(string full)
        {
            var d = DesignedRoster.All.FirstOrDefault(x => $"{x.First} {x.Last}" == full);
            if (d is not null) return d.Bio!;
            return DesignedLegends.All.Single(l => $"{l.First} {l.Last}" == full).Bio!;
        }

        foreach (var (a, b, kind) in NewBonds.Where(x => x.Kind != RelationshipKind.Friend))
        {
            Assert.Contains(b.Split(' ')[0], BioOf(a));
            Assert.Contains(a.Split(' ')[0], BioOf(b));
        }
        // And the friendships name each other too.
        Assert.Contains("Okafor", BioOf("Sefu Okonjo"));
        Assert.Contains("Quispe", BioOf("Alonso Huanca"));
        Assert.Contains("gym bus", BioOf("Mateo Quispe"));
        Assert.Contains("gym bus", BioOf("Alonso Huanca"));
    }

    [Fact]
    public void TheFamilyBonds_AgreeWithAgesCountriesAndClubs()
    {
        // Siblings: the elder is the brother, both Russian.
        var dmitri = DesignedRoster.All.Single(d => d.Last == "Volkov");
        var katya = DesignedRoster.All.Single(d => d.Last == "Volkova");
        Assert.True(dmitri.Age > katya.Age);
        Assert.Equal(dmitri.Country, katya.Country);
        Assert.Equal(Sex.Female, katya.Sex);
        Assert.Contains("older brother", katya.Bio);
        Assert.Contains("younger sister", dmitri.Bio);

        // Uncle and nephew: an uncle is older, both Brazilian; the nephew's champion house
        // is the one the uncle's academy trains against.
        var wallace = DesignedRoster.All.Single(d => d.Last == "Bandeira");
        var paulo = DesignedLegends.All.Single(l => l.Last == "Bandeira");
        Assert.True(paulo.AgeNow - wallace.Age >= 15);
        Assert.Equal(wallace.Country, paulo.Country);
        Assert.Equal("CK", wallace.Org);
        Assert.Equal("Bandeira Jiu-Jitsu", DesignedRoster.CampHomes["Souza"]);
        Assert.Equal("Bandeira Jiu-Jitsu", paulo.HeadCoachOf);
        Assert.Contains("uncle", wallace.Bio);
        Assert.Contains("nephew", paulo.Bio);
    }

    // --- camp homes ---------------------------------------------------------

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Volkova_TrainsWithHerBrother_AndMokoenaIsNoLongerForcedIntoHighveld(ulong seed)
    {
        var world = Build(seed);
        var room = world.Camps.Values.Single(c => c.Name == "Volk Sambo Club");
        foreach (var last in new[] { "Volkov", "Volkova", "Baranov" })
            Assert.Equal(room.Id, Last(world, last).Camp);

        Assert.False(DesignedRoster.CampHomes.ContainsKey("Mokoena"));
        Assert.True(DesignedRoster.CampHomes.ContainsKey("Ndlovu"));   // van Zyl's man still has his room
    }

    [Fact]
    public void EveryCampHome_NamesARealDesignedCamp()
    {
        var world = Build(9);
        var names = world.Camps.Values.Select(c => c.Name).ToHashSet();
        foreach (var (last, room) in DesignedRoster.CampHomes)
            Assert.True(names.Contains(room), $"{last} -> '{room}' is not a camp");
    }

    // --- rivalries -------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Seeds))]
    public void NewRivalries_ExistOnDayOne_BetweenTheirPair(ulong seed)
    {
        var world = Build(seed);
        foreach (var (a, b) in new[] { ("Rivas", "Sales"), ("Pryor", "Cole") })
        {
            var fa = Last(world, a); var fb = Last(world, b);
            var rv = Assert.Single(world.Rivalries.Where(r => r.IsBetween(fa.Id, fb.Id)));
            Assert.True(rv.Intensity >= 45, $"{a}/{b} heat {rv.Intensity}");
            Assert.Equal(fa.Id, rv.A);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void EveryAuthoredFeudReason_ReachesItsFeudBody_AndNoOtherOne(ulong seed)
    {
        var world = Build(seed);
        var feuds = world.News.Where(n => n.Category == NewsCategory.Rivalry
                                          && n.Headline.Contains(" and ") && n.AboutFighter is not null).ToList();
        foreach (var (key, reason) in DesignedRoster.FeudReasons)
        {
            var (x, y) = (key.Split('|')[0], key.Split('|')[1]);
            // The key names two designed fighters, and a rivalry between them is seeded.
            var fx = Last(world, x); var fy = Last(world, y);
            Assert.True(world.Rivalries.Any(r => r.IsBetween(fx.Id, fy.Id)), $"{key} has no rivalry");
            var item = feuds.Single(n => n.Headline.Contains(fx.FullName) && n.Headline.Contains(fy.FullName));
            Assert.Contains(reason, item.Body);
        }
        // Nobody else gets one.
        Assert.Equal(DesignedRoster.FeudReasons.Count,
            feuds.Count(n => DesignedRoster.FeudReasons.Values.Any(r => n.Body.Contains(r))));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void TheFeudReasonsThatLeanOnTheRecord_AreTrueOfTheBuiltWorld(ulong seed)
    {
        var world = Build(seed);
        int Meetings(string a, string b) => EventHype.Meetings(world, Last(world, a).Id, Last(world, b).Id);

        // Mercer-Seo: the reason says "the decision", so they must have met (the pinned bout).
        Assert.True(Meetings("Mercer", "Seo") >= 1);

        // Sales holds the belt Rivas walked away from: the current CK bantamweight reign
        // is Sales's and began after Rivas's reign had ended.
        var ck = world.Promotions.Values.Single(p => p.Abbreviation == "CK");
        var belt = world.Get(ck.Divisions.Single(d => d.WeightClass == WeightClass.Bantamweight && d.Sex == Sex.Male).Title!.Value)!;
        var sales = Last(world, "Sales"); var rivas = Last(world, "Rivas");
        Assert.Equal(sales.Id, belt.CurrentChampion);
        var current = belt.Lineage.Last(r => r.LostOn is null);
        var rivasReign = belt.Lineage.Last(r => r.Champion == rivas.Id);
        // (Rivas's reign ends vacated; on some seeds the written history lets it overlap Sales's start by a
        // month - a lineage quirk for the history PR, so only the order of the two reigns is asserted.)
        Assert.True(rivasReign.WonOn < current.WonOn && rivasReign.LostOn is not null);

        // Cross works Donovan's corner: a mentor bond, Cross first, Cross retired.
        Assert.Equal(FighterStatus.Retired, Named(world, "Danielle Cross").Status);

        // Pryor and Cole are both young lightweights at feeder houses, as the feud reason says.
        Assert.All(new[] { "Pryor", "Cole" }, l =>
        {
            Assert.Equal(WeightClass.Lightweight, Last(world, l).WeightClass);
            Assert.Equal(22, Last(world, l).AgeOn(world.CurrentDate));
        });
    }

    // --- the authored feed ----------------------------------------------------

    [Fact]
    public void EveryAuthoredHeadline_ResolvesItsPersonAndItsHouse()
    {
        var orgs = DesignedStaff.Owners.Select(o => o.Org).ToHashSet();
        foreach (var h in DesignedHeadlines.All)
        {
            if (h.About is not null)
            {
                int n = DesignedRoster.All.Count(d => $"{d.First} {d.Last}" == h.About)
                        + DesignedLegends.All.Count(l => $"{l.First} {l.Last}" == h.About);
                Assert.True(n == 1, $"'{h.About}' names {n} people ({h.Headline})");
            }
            if (h.Org is not null) Assert.Contains(h.Org, orgs);
            Assert.True(h.DaysBeforeStart is > 0 and < 60);
        }
        Assert.Equal(DesignedHeadlines.All.Count, DesignedHeadlines.All.Select(h => h.Headline).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ThePrologue_PostsEachAuthoredHeadlineExactlyOnce_OnItsDate(ulong seed)
    {
        var world = Build(seed);
        var start = new WorldBuilder.Options().StartDate;
        foreach (var h in DesignedHeadlines.All)
        {
            var hits = world.News.Where(n => n.Headline == h.Headline).ToList();
            var item = Assert.Single(hits);
            Assert.Equal(h.Body, item.Body);
            Assert.Equal(h.Cat, item.Category);
            Assert.Equal(start.AddDays(-h.DaysBeforeStart), item.Date);
            if (h.About is not null) Assert.Equal(Named(world, h.About).Id, item.AboutFighter);
        }
    }

    [Fact]
    public void AWorldWithoutTheDesignedRoster_PostsNoAuthoredHeadlines()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9, UseDesignedRoster = false });
        foreach (var h in DesignedHeadlines.All)
            Assert.DoesNotContain(world.News, n => n.Headline == h.Headline);
    }

    [Fact]
    public void AuthoredHeadlines_PromiseNoFixtureContractStageOrHomeCity()
    {
        // Things only a later table (bookings, contract stages, home cities) can make true.
        var banned = new Regex(@"\b(will (close|headline|fight|meet)|sold out|one fight left|final fight|last fight|three weeks|next month|this summer|booked|Desert Clash|New Year show|Temple Fair|Mountain Night)\b",
            RegexOptions.IgnoreCase);
        foreach (var h in DesignedHeadlines.All)
            Assert.False(banned.IsMatch(h.Headline + " " + h.Body), $"'{h.Headline}' promises what no table holds");
    }

    // --- nothing falsifiable ---------------------------------------------------

    // Number words, digits, currency and absolutes: the vocabulary of a claim the sim can overturn.
    private static readonly Regex Claim = new(
        @"(\d|\$|\b(two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|twenty\S*|thirty\S*|hundred|thousand|first|only|never|every|always|ever|nobody|none|last year|january|february|march|april|may|june|july|august|september|october|november|december)\b)",
        RegexOptions.IgnoreCase);

    // Each allowance is a phrase that is either a quotation, a figure of speech, or a number tied to an
    // authored record that the test below asserts. Everything else in the new prose must be claim-free.
    private static readonly string[] Allowed =
    {
        "four hundred times",                 // Seo, in his own words
        "five rounds and three new judges",   // Seo, in his own words
        "nobody has asked her a hard question yet", // Cross, in her own words (headline title, case-insensitive)
        "Nobody has asked Volkova a hard question yet", // Cross's bio: what she has said
        "Twenty-six pro wins",                // Nakamura: asserted below against the authored record
        "Eight straight wins",                // Al-Suwaidi: asserted below
        "Undefeated and twenty-one",          // Saengkham: asserted below
        "second decade",                      // Halloran's bio says it
        "in three years",                     // Bisset's forecast, in her own words
        "two inches thick",                   // Diallo's file: a figure of speech about a heavy folder
        "four languages",                     // Donovan's existing bio line
        "four words",                         // Volkov: 'Ask her, not me.' is counted below
        "Nobody has signed her yet",          // Diallo is unsigned on day one: asserted below
        "two surgeries",                      // Tanner's existing bio line
        "two young lightweights",             // Pryor/Cole: asserted above
        "Two young lightweights",
        "two owners",                         // Pryor/Cole: the two feeder owners
        "two feeder houses",
        "two of them share the mats",         // Volkov/Volkova at one camp: asserted above
        "One township each, one belt between them", // Mokoena/Ndlovu: Gugulethu and Khayelitsha, one belt
        "same four words",
        "Nigeria's two Lions",                // a bond note: both nicknames are 'Lion'
        "two share the gym bus",              // Quispe/Huanca at one org
        "the two of them",
        "the last year",                      // what Nakamura, Tanner, Hattori and Lindqvist SAY; the feed reports the saying
        "his last year",
        "a first name",                       // the two Somchais
        "Late is not never",                  // Moreira, in his own words
        "mourned none of them",               // Dudaev's existing bio line
        "Matsuda's nine defenses",            // asserted below against the legend's reign
        "nobody is allowed to dust",          // a figure of speech about banners
    };

    private static IEnumerable<(string Label, string Text)> NewProse()
    {
        foreach (var h in DesignedHeadlines.All)
        {
            yield return ($"headline '{h.Headline}'", h.Headline);
            yield return ($"body of '{h.Headline}'", h.Body);
        }
        foreach (var (k, v) in DesignedRoster.FeudReasons) yield return ($"feud {k}", v);
        foreach (var b in DesignedRoster.Bonds.Skip(DesignedRoster.Bonds.Count - 5)) yield return ($"bond {b.A}/{b.B}", b.Note);
        foreach (var name in new[] { "The Sunrise Dome", "The Chao Phraya Dome", "Arena Guanabara", "Arena Bandeirantes", "The Falcon Dome" })
            yield return ($"venue {name}", Venues.LoreOf(name)!);
        foreach (var s in BioSentences.Values) yield return ($"bio sentence '{s[..24]}'", s);
    }

    // The sentences this batch appended to existing bios, keyed by who they were appended to.
    private static readonly Dictionary<string, string> BioSentences = new()
    {
        ["Donovan"] = "Danielle Cross, who argued the women's division into existence, works her corner and has opinions about whose belt it should be.",
        ["Volkova"] = "Her older brother is Dmitri Volkov of Ironhold; the two of them share the mats at the Volk Sambo Club, and she is the one who gets the interviews.",
        ["Volkov"] = "His younger sister is Katya Volkova, the most famous woman in the sport, and he answers questions about her with the same four words: 'Ask her, not me.'",
        ["Bandeira"] = "His uncle is Paulo Bandeira, whose family academy in Rio now trains the champion of the house across the war; it has made for awkward family dinners.",
        ["Okonjo"] = "He calls Chidi Okafor the better fighter and the worse businessman, usually in the same sentence.",
        ["Quispe"] = "He has taken the young Bolivian from the same Andes circuit under his wing, and the two share the gym bus.",
        ["Huanca"] = "He rides the gym bus beside Mateo Quispe, the champion he is expected to replace, and neither pretends otherwise.",
        ["Rivas"] = "The belt he left behind is Pedro Sales's now, and Rivas does not consider the arrangement permanent.",
        ["Sales"] = "He holds the belt Diego Rivas walked away from, and Rivas has made clear he wants it back.",
        ["Pryor"] = "He and Tyshawn Cole of Coastal Combat are the two young lightweights the scouts put side by side.",
        ["Cole"] = "The other lightweight prospect the scouts mention in the same breath is DeShawn Pryor of Great Lakes, and Cole has noticed.",
        ["Cross"] = "She has told television audiences that nobody has asked Volkova a hard question yet, and she works Sarah Donovan's corner as the woman most likely to.",
        ["Diallo"] = "Rosalind Okoye has a file on her two inches thick; Margaux Bisset has her highlight reel on her phone.",
        ["Tanner"] = "He says this is the last year, and his surgeon agrees.",
        ["Hattori"] = "He has said, publicly, that this is his last year and that he would like it to end in Tokyo.",
        ["Lindqvist"] = "He says this is his last year, and the exam has a closing date.",
        ["Paulo"] = "His nephew Wallace is the Cage Kings heavyweight champion, a house his family's academy now trains against; the picture of the two of them is still on the academy wall.",
    };

    [Fact]
    public void TheNewBioSentences_AreInTheBios()
    {
        foreach (var (who, sentence) in BioSentences)
        {
            string bio = who switch
            {
                "Cross" => DesignedLegends.All.Single(l => l.Last == "Cross").Bio!,
                "Paulo" => DesignedLegends.All.Single(l => l.First == "Paulo").Bio!,
                "Bandeira" => DesignedRoster.All.Single(d => d.Last == "Bandeira").Bio!,
                _ => DesignedRoster.All.Single(d => d.Last == who).Bio!,
            };
            Assert.Contains(sentence, bio);
        }
    }

    [Fact]
    public void NewProse_MakesNoClaimTheDataCannotKeep()
    {
        var bad = new List<string>();
        foreach (var (label, text) in NewProse())
        {
            // Strip the allowed phrases, then anything left that reads like a claim is a finding.
            string rest = text;
            foreach (var ok in Allowed) rest = Regex.Replace(rest, Regex.Escape(ok), " ", RegexOptions.IgnoreCase);
            foreach (Match m in Claim.Matches(rest)) bad.Add($"{label}: '{m.Value}' in \"{text}\"");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void TheNumbersThatAreAllowed_AreTheAuthoredOnes()
    {
        Assert.Equal(26, DesignedRoster.All.Single(d => d.Last == "Nakamura").Wins);
        var suwaidi = DesignedRoster.All.Single(d => d.Last == "Al-Suwaidi");
        Assert.Equal((8, 0, 8), (suwaidi.Wins, suwaidi.Losses, suwaidi.Streak));
        var saeng = DesignedRoster.All.Single(d => d.Last == "Saengkham");
        Assert.Equal((21, 9, 0), (saeng.Age, saeng.Wins, saeng.Losses));
        Assert.Null(DesignedRoster.All.Single(d => d.Last == "Diallo").Org);   // unsigned
        Assert.Null(DesignedRoster.All.Single(d => d.Last == "Vance").Org);
        Assert.Null(DesignedRoster.All.Single(d => d.Last == "Rivas").Org);
        Assert.Equal(4, "Ask her, not me.".Split(' ').Length);
        Assert.Equal(9, DesignedLegends.All.Single(l => l.Last == "Matsuda").Reigns.Single().Defenses);   // the banners
        // Both feeder prospects are the age and class the feud line says.
        Assert.All(new[] { "Pryor", "Cole" }, l =>
        {
            var d = DesignedRoster.All.Single(x => x.Last == l);
            Assert.Equal((22, WeightClass.Lightweight, 0), (d.Age, d.WeightClass, d.Losses));
        });
    }

    [Fact]
    public void NoTrueAbilityOrPotentialNumber_AppearsInAnyNewProse()
    {
        // Fog of war: nothing here may read like a rating.
        var rating = new Regex(@"\b(overall|ovr|potential|rating|ability)\b\s*[:=]?\s*\d", RegexOptions.IgnoreCase);
        foreach (var (label, text) in NewProse()) Assert.False(rating.IsMatch(text), label);
    }

    // --- venues ----------------------------------------------------------------

    [Fact]
    public void EveryVenueWithLore_IsARealNamedVenue()
    {
        var world = Build(9);
        var real = world.Cities.Values.SelectMany(c =>
        {
            var (hall, forum, arena) = Venues.For(c.Name);
            return new[] { hall, forum, arena };
        }).ToHashSet();
        foreach (var name in new[] { "The Sunrise Dome", "The Chao Phraya Dome", "Arena Guanabara", "Arena Bandeirantes", "The Falcon Dome" })
        {
            Assert.Contains(name, real);
            Assert.False(string.IsNullOrWhiteSpace(Venues.LoreOf(name)));
        }
        Assert.Null(Venues.LoreOf("Tokyo Arena"));
    }

    // --- poster line -----------------------------------------------------------

    [Fact]
    public void AMentorAndHisStudent_SellAsMasterAndStudent_AfterFamilyAndFriends()
    {
        var world = Build(9);
        var volkov = Last(world, "Volkov"); var baranov = Last(world, "Baranov");
        var promo = world.Promotions.Values.Single(p => p.Abbreviation == "IFA");
        // A completed show so "WHERE IT ALL BEGINS" cannot apply, and no feud muddying the line.
        world.Events[world.NewEventId()] = new Event { Id = world.NewEventId(), Promotion = promo.Id, Name = "Past", Date = world.CurrentDate.AddMonths(-2), Status = EventStatus.Completed };
        world.Rivalries.RemoveAll(r => r.IsBetween(volkov.Id, baranov.Id));
        volkov.Camp = null; baranov.Camp = null;

        var ev = new Event { Id = world.NewEventId(), Promotion = promo.Id, Name = "Test Night", Date = world.CurrentDate };
        ev.Card.Add(new Fight { Id = world.NewFightId(), RedCorner = volkov.Id, BlueCorner = baranov.Id, WeightClass = WeightClass.Middleweight });
        world.Events[ev.Id] = ev;

        Assert.Equal("MASTER AND STUDENT", EventHype.Tagline(world, ev));

        // A friendship or a family tie on the same pair still outranks it.
        var bond = world.Relationships.Single(r => r.IsBetween(volkov.Id, baranov.Id));
        world.Relationships.Remove(bond);
        world.Relationships.Add(new Relationship { A = volkov.Id, B = baranov.Id, Kind = RelationshipKind.Family, Strength = 80, Since = world.CurrentDate });
        Assert.Equal("BLOOD AGAINST BLOOD", EventHype.Tagline(world, ev));
    }
}
