using System.Text.RegularExpressions;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// The narratives pass: connective tissue written into existing bios, board notes, staff
/// histories, day-one headlines and venue lore. Every table here is text. These tests keep the
/// text honest: each name a bio drops is a real authored person, the stories are told from both
/// sides, and nothing in the new feed prose makes a claim the sim can overturn.
/// </summary>
public class NarrativesPassTests
{
    // Everything that can be named in a bio, by full name, to the text that person's own page carries.
    private static IReadOnlyDictionary<string, string> Texts()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in DesignedRoster.All) d[$"{f.First} {f.Last}"] = f.Bio ?? "";
        foreach (var l in DesignedLegends.All) d[$"{l.First} {l.Last}"] = l.Bio ?? "";
        foreach (var s in DesignedStaff.Staff) d[s.Name] = s.Background;
        foreach (var o in DesignedStaff.Owners) d[o.Name] = o.Note;
        foreach (var p in OriginsPioneers.All) d[p.FullName] = p.Fighter.Bio ?? "";
        foreach (var o in OriginsPromotions.All) d[o.Founder.Name] = o.Founder.Bio;
        return d;
    }

    // Stories told from both sides: each name appears in the other's text.
    public static readonly TheoryData<string, string, string, string> BothWays = new()
    {
        { "Luka Petrovic", "Kovac", "Miron Kovac", "Petrovic" },
        { "Emil Toma", "Codrea", "Andrei Codrea", "Toma" },
        { "Diego Fuentes", "Quiroga", "Mateo Quiroga", "Fuentes" },
        { "Yuna Park", "Fujimoto", "Sakura Fujimoto", "Park" },
        { "Timur Zhaksylyk", "Batyrov", "Aslan Batyrov", "Zhaksylyk" },
        { "Callum Brody", "Rooke", "Alfie Rooke", "Brody" },
        { "Marcus Hale", "Kroll", "Nathan Kroll", "Hale" },
        { "Bram Kowalczyk", "Baran", "Marek Baran", "Kowalczyk" },
        { "Amanda Silva", "Vieira", "Ronaldo Vieira", "Silva" },
        { "Rafael Souza", "Ferraz", "Osvaldo Ferraz", "Souza" },
        { "Maximo Duarte", "Pires", "Waldemar Pires", "Duarte" },
        { "Kirill Baranov", "Antonov", "Grigori Antonov", "Baranov" },
        { "Jaylen Brooks", "Deene", "Bobby Deene", "Brooks" },
        { "Cian O'Rourke", "Moyo", "Tendai Moyo", "O'Rourke" },
        { "Kane Te Ariki", "Solomona", "Tama Solomona", "Te Ariki" },
        { "Manaia Rahiri", "Solomona", "Tama Solomona", "Rahiri" },
        { "Ossie Blackwood", "Rooke", "Alfie Rooke", "Blackwood" },
        { "Yusuf Bakr", "Pemberton", "Gloria Pemberton", "Bakr" },
        { "Masashi Tsukamoto", "Thibodeau", "Gaston Thibodeau", "Tsukamoto" },
        { "Boris Khlebnikov", "Orekhov", "Vadim Orekhov", "Khlebnikov" },
    };

    [Theory]
    [MemberData(nameof(BothWays))]
    public void StoriesAreToldFromBothSides(string a, string bLast, string b, string aLast)
    {
        var t = Texts();
        Assert.True(t.ContainsKey(a), $"{a} is not an authored person");
        Assert.True(t.ContainsKey(b), $"{b} is not an authored person");
        Assert.Contains(bLast, t[a]);
        Assert.Contains(aLast, t[b]);
    }

    [Fact]
    public void EveryNameDroppedInTheNewHooks_IsARealAuthoredPerson()
    {
        // The people the new sentences name by full name; each must exist exactly once.
        var names = new[]
        {
            "Nathan Kroll", "Emiliano Vasquez", "Tyrell Boone", "Alfie Rooke", "Tendai Moyo", "Hendrik Vos", "Kenji Morita",
            "Tama Solomona", "Marek Baran", "Miron Kovac", "Luka Petrovic", "Andrei Codrea", "Emil Toma", "Bohdan Marchenko",
            "Zurab Kipiani", "Ronaldo Vieira", "Amanda Silva", "Waldemar Pires", "Osvaldo Ferraz", "Hugo Sosa", "Diego Fuentes",
            "Mateo Quiroga", "Ayumi Sekine", "Yusuf Haddad", "Grigori Antonov", "Yerlan Amanov", "Aslan Batyrov", "Timur Zhaksylyk",
            "Sakura Fujimoto", "Yuna Park", "Danielle Cross", "Bobby Deene", "Movsar Dudaev", "Marcus Hale", "Brock Tanner",
            "Damon Pruitt", "Viktor Roth", "Rafael Souza", "Wiremu Tane", "Gloria Pemberton", "Yusuf Bakr", "Michiko Arai",
            "Waldemar Pires", "Bill Hennessy", "Katherine Ross", "Pavel Ilic",
        };
        var t = Texts();
        foreach (var n in names.Distinct())
        {
            Assert.True(t.ContainsKey(n), $"'{n}' is named in new prose but is not an authored person");
        }
    }

    // The vocabulary of a claim the sim can overturn: figures, currency and absolutes.
    private static readonly Regex Claim = new(
        @"(\d|\$|\b(two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|twenty\S*|thirty\S*|hundred|thousand|first|only|never|every|always|ever|nobody|none|last year|january|february|march|april|may|june|july|august|september|october|november|december)\b)",
        RegexOptions.IgnoreCase);

    [Fact]
    public void TheNewFeedLinesAndFeudReasons_MakeNoUnbackedClaim()
    {
        var allowed = new[] { "his last year", "the last year" };   // what a man SAYS; the feed reports the saying
        var fresh = DesignedHeadlines.All.Skip(DesignedHeadlines.All.Count - 14)
            .SelectMany(h => new[] { h.Headline, h.Body })
            .Concat(new[] { "Marchand|Weiss", "Reyes|Zhang", "Ozols|Urbonas", "Barone|Weeks" }.Select(k => DesignedRoster.FeudReasons[k]))
            .ToList();
        var bad = new List<string>();
        foreach (var text in fresh)
        {
            string rest = text;
            foreach (var ok in allowed) rest = Regex.Replace(rest, Regex.Escape(ok), " ", RegexOptions.IgnoreCase);
            bad.AddRange(Claim.Matches(rest).Select(m => $"'{m.Value}' in \"{text}\""));
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void TheFourFeudsThatHadNoReason_NowHaveOne()
    {
        foreach (var (a, b) in new[] { ("Marchand", "Weiss"), ("Reyes", "Zhang"), ("Ozols", "Urbonas"), ("Barone", "Weeks") })
        {
            Assert.False(string.IsNullOrWhiteSpace(DesignedRoster.FeudReason(a, b)), $"{a}|{b}");
            // ...and it is a day-one feud, not an orphan reason.
            Assert.Contains(DesignedRoster.All, d => d.RivalOf == a || d.RivalOf == b);
        }
    }

    [Fact]
    public void TheNewVenueLines_NameRealVenues_AndSayWhatTheBuildingIsNotWhatItStaged()
    {
        var real = new[] { "Chicago", "Accra", "Cape Town", "Sydney", "Buenos Aires", "Rio de Janeiro", "Stockholm", "Bangkok" }
            .SelectMany(c => { var (h, f, a) = Venues.For(c); return new[] { h, f, a }; }).ToHashSet();
        foreach (var name in new[] { "Kelly's Gym", "The Bukom Room", "The Dockside", "The Woolshed", "El Galpón de Boedo", "O Barracão", "Källaren", "Soi Nine Stadium" })
        {
            Assert.Contains(name, real);
            var lore = Venues.LoreOf(name);
            Assert.False(string.IsNullOrWhiteSpace(lore), name);
            Assert.DoesNotMatch(@"\b(won|lost|defended|stopped|knocked)\b", lore!);   // a building has a character, not a results sheet
        }
    }

    [Fact]
    public void TheOwnerRivalries_AreToldByBothOwners()
    {
        string Note(string org) => DesignedStaff.Owners.Single(o => o.Org == org).Note;
        Assert.Contains("Baron", Note("IFA"));
        Assert.Contains("Ilic", Note("VMMA"));
        Assert.Contains("Hennessy", Note("AFC"));
        Assert.Contains("Ross", Note("UFL"));
        Assert.Contains("Pires", Note("CK"));
        Assert.Contains("Arai", Note("KAC"));
    }

    [Fact]
    public void BiosThatQuoteARecord_StillQuoteTheRecordTheyHave()
    {
        // The pass appended to bios that carry numbers; the numbers must have survived untouched.
        string Bio(string last) => DesignedRoster.All.Single(d => d.Last == last).Bio!;
        Assert.Contains("Eleven fights", Bio("Moyo"));
        Assert.Contains("once in fourteen fights", Bio("Weiss"));
        Assert.Contains("four times in twenty-two fights", Bio("Nowak"));
        Assert.Contains("seven fights", Bio("Tsereteli"));
        Assert.Contains("He says this is the last year, and his surgeon agrees.", Bio("Tanner"));
        Assert.Contains("end in Tokyo", Bio("Hattori"));
        Assert.Contains("former ICF title challenger", Bio("Hattori"));
    }
}
