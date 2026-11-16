using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// Bonds that speak and show: relationship-keyed quote lines and the Biographer's "people" passage.
/// Display text only. These tests pin that the no-tie path is exactly what it was, that a tie line is
/// only ever chosen for a tie the world can prove, and that Origins never reads era-unsafe.
/// </summary>
public class BondVoicesTests
{
    private static readonly Lazy<World> DayOne = new(() => new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 }));
    private static World Fresh() => new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });

    private static Fighter By(World w, string first, string last) =>
        w.Fighters.Values.Single(f => f.Designed && f.FirstName == first && f.LastName == last);
    private static Fighter ByLast(World w, string last) =>
        w.Fighters.Values.Single(f => f.Designed && f.LastName == last);

    private static IEnumerable<DateOnly> Days(int n = 120) =>
        Enumerable.Range(0, n).Select(i => new DateOnly(2026, 1, 1).AddDays(i * 3));

    private static string Strip(string quote) => quote.Trim('“', '”');

    // --- the no-tie path is exactly what it was ---------------------------------------------------

    [Fact]
    public void TheWorldlessSay_IsExactlyWhatItWasBeforeTies()
    {
        // Pinned from the code as it stood BEFORE this change (84 calls over the first dozen designed fighters).
        var w = DayOne.Value;
        var fs = w.Fighters.Values.Where(f => f.Designed).OrderBy(f => f.Id.Value).Take(12).ToList();
        var sb = new StringBuilder();
        int i = 0;
        foreach (var f in fs)
            foreach (var m in new[] { Moment.WinFinish, Moment.WinDecision, Moment.Loss, Moment.TitleWin, Moment.TitleLoss, Moment.Cut, Moment.Signed })
            {
                var other = fs[(i++ * 7 + 1) % fs.Count];
                sb.Append($"{f.Id.Value}|{m}|{f.Disposition}|{other.Sex}|{Quotes.Say(f, m, new DateOnly(2026, 3, 1 + i % 20), other)}").Append('\n');
            }
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        Assert.Equal("22cce63be3522936fc722223be53ae6c5276bb74b86a4b81ee7a3413704fdfe8", hash);

        var lines = sb.ToString().Split('\n');
        Assert.Equal("1|WinFinish|Student|Male|“He telegraphed the level change all round. Once I saw it twice, it was over.”", lines[0]);
        Assert.Equal("4|TitleWin|Loudmouth|Male|“I said it when nobody was listening and I'll say it with this on my shoulder. Line them up.”", lines[24]);
        Assert.Equal("12|Loss|Student|Male|“I know exactly which round I lost that in, and I know why. That's useful.”", lines[79]);
    }

    [Fact]
    public void WithNoTie_TheWorldAwareSay_IsIdenticalToTheOldOne()
    {
        var w = Fresh();
        var fs = w.Fighters.Values.Where(f => f.Status == FighterStatus.Active && !f.Designed).OrderBy(f => f.Id.Value).Take(40).ToList();
        int compared = 0;
        foreach (var a in fs)
            foreach (var b in fs.Take(8))
            {
                if (a.Id == b.Id || Ties.Strongest(w, a, b, new DateOnly(2026, 3, 1)) is not null) continue;
                foreach (var m in new[] { Moment.WinFinish, Moment.Loss, Moment.TitleWin, Moment.Cut })
                {
                    Assert.Equal(Quotes.Say(a, m, new DateOnly(2026, 3, 9), b), Quotes.Say(w, a, m, new DateOnly(2026, 3, 9), b));
                    compared++;
                }
            }
        Assert.True(compared > 200, $"only {compared} no-tie calls compared");
    }

    [Fact]
    public void MomentsThatAreNotResults_NeverTakeATieLine()
    {
        var w = Fresh();
        var volkov = ByLast(w, "Volkov");
        var baranov = ByLast(w, "Baranov");
        foreach (var d in Days())
            foreach (var m in new[] { Moment.Cut, Moment.Signed, Moment.Lowball, Moment.Retiring, Moment.MissedWeight, Moment.PassedOver })
                Assert.Equal(Quotes.Say(volkov, m, d, baranov), Quotes.Say(w, volkov, m, d, baranov));
    }

    [Fact]
    public void Determinism_SameInputsGiveTheSameLine()
    {
        var w = Fresh();
        var a = ByLast(w, "Volkov"); var b = ByLast(w, "Baranov");
        foreach (var d in Days(40))
            Assert.Equal(Quotes.Say(w, a, Moment.WinFinish, d, b), Quotes.Say(w, a, Moment.WinFinish, d, b));
        Assert.Equal(Quotes.Attributed(w, a, Moment.Loss, new DateOnly(2026, 5, 5), b), Quotes.Attributed(w, a, Moment.Loss, new DateOnly(2026, 5, 5), b));
    }

    // --- a line that knows ------------------------------------------------------------------------

    private static int TieHits(World w, Fighter speaker, Fighter other, TieKind expected, bool won)
    {
        var tie = Ties.Strongest(w, speaker, other, new DateOnly(2026, 6, 1));
        Assert.NotNull(tie);
        Assert.Equal(expected, tie!.Kind);
        var pool = TieLines.Pool(tie, TieLines.IsHot(speaker.Disposition), won).ToHashSet();
        Assert.NotEmpty(pool);
        int hits = 0;
        foreach (var d in Days())
        {
            var moment = won ? Moment.WinFinish : Moment.Loss;
            string say = Strip(Quotes.Say(w, speaker, moment, d, other));
            if (pool.Any(p => (other.Sex == Sex.Female ? FemaleOf(p) : p) == say)) hits++;
        }
        return hits;
    }

    // The same pronoun swap the engine applies to a woman across the cage.
    private static string FemaleOf(string line) => Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(line,
        @"\bHe's\b", "She's"), @"\bhe's\b", "she's"), @"\bHe\b", "She"), @"\bhe\b", "she"), @"\bhim\b", "her"), @"\bhis\b", "her"), @"\ba man\b", "a woman");

    [Fact]
    public void AMentorAndHisStudent_SpeakAboutEachOther_OftenButNotAlways()
    {
        var w = Fresh();
        var volkov = ByLast(w, "Volkov"); var baranov = ByLast(w, "Baranov");
        foreach (bool won in new[] { true, false })
        {
            int asMentor = TieHits(w, volkov, baranov, TieKind.Mentor, won);
            int asStudent = TieHits(w, baranov, volkov, TieKind.Student, won);
            foreach (int h in new[] { asMentor, asStudent })
            {
                Assert.InRange(h, 60, 110);   // 75% of 120 days, but never all of them
            }
        }
    }

    [Fact]
    public void AuthoredRivals_SpeakAboutTheFeud()
    {
        var w = Fresh();
        var mercer = ByLast(w, "Mercer"); var seo = ByLast(w, "Seo");
        Assert.InRange(TieHits(w, mercer, seo, TieKind.Rival, won: true), 50, 110);
        Assert.InRange(TieHits(w, seo, mercer, TieKind.Rival, won: false), 50, 110);
    }

    [Fact]
    public void AFamilyTie_AndAFriendship_AreHeard()
    {
        var w = Fresh();
        Assert.True(TieHits(w, ByLast(w, "Volkova"), ByLast(w, "Volkov"), TieKind.Family, won: true) > 40);
        // Friends on different banners: Fuentes and Quiroga (no feud, no meetings).
        Assert.True(TieHits(w, ByLast(w, "Fuentes"), ByLast(w, "Quiroga"), TieKind.Friend, won: false) > 30);
    }

    private static void AddBout(World w, Fighter red, Fighter blue, Fighter? winner, DateOnly date, bool decision = true)
    {
        var promo = w.Promotions.Values.First(p => !p.Defunct);
        var ev = new Event { Id = w.NewEventId(), Promotion = promo.Id, Name = $"Test Night {date}", Date = date, Status = EventStatus.Completed };
        ev.Card.Add(new Fight
        {
            Id = w.NewFightId(), RedCorner = red.Id, BlueCorner = blue.Id, WeightClass = red.WeightClass,
            Outcome = new FightOutcome
            {
                Winner = winner?.Id, Loser = winner is null ? null : (winner.Id == red.Id ? blue.Id : red.Id),
                Method = decision ? FightMethod.UnanimousDecision : FightMethod.Knockout, EndRound = 3, EndTimeSeconds = 300,
            },
        });
        w.Events[ev.Id] = ev;
    }

    private static (World W, Fighter A, Fighter B) PlainPair()
    {
        var w = Fresh();
        var pair = w.Fighters.Values.Where(f => !f.Designed && f.Status == FighterStatus.Active).OrderBy(f => f.Id.Value).Take(2).ToList();
        foreach (var f in pair) f.Camp = null;
        pair[0].Country = new CountryId(901); pair[1].Country = new CountryId(902);
        Assert.Null(Ties.Strongest(w, pair[0], pair[1], new DateOnly(2026, 6, 1)));
        return (w, pair[0], pair[1]);
    }

    [Fact]
    public void ARematch_IsReadFromRealHistory_AndOnlyWhenThereIsSome()
    {
        var (w, a, b) = PlainPair();
        var on = new DateOnly(2026, 6, 1);
        Assert.Null(Ties.Strongest(w, a, b, on));

        AddBout(w, a, b, a, on.AddMonths(-8));
        var tie = Ties.Strongest(w, a, b, on)!;
        Assert.Equal(TieKind.Rematch, tie.Kind);
        Assert.Equal(SeriesLean.Leads, tie.Series);                                   // a won the only meeting
        Assert.Equal(SeriesLean.Trails, Ties.Strongest(w, b, a, on)!.Series);

        // The bout being talked about (a few days old) is the moment, not history.
        var (w2, c, d) = PlainPair();
        AddBout(w2, c, d, c, on.AddDays(-2));
        Assert.Null(Ties.Strongest(w2, c, d, on));

        // And the line is one of the rematch pool for a speaker who leads.
        var pool = TieLines.Pool(tie, TieLines.IsHot(a.Disposition), won: true).ToHashSet();
        int hits = Days().Count(day => pool.Contains(Strip(Quotes.Say(w, a, Moment.WinFinish, day, b))));
        Assert.InRange(hits, 25, 100);
    }

    [Fact]
    public void ALineNeverNamesATieThatIsNotThere()
    {
        // Two strangers, whatever the date, get only the ordinary pools.
        var (w, a, b) = PlainPair();
        var all = Enum.GetValues<TieKind>()
            .SelectMany(k => new[] { true, false }.SelectMany(won => new[] { true, false }.SelectMany(hot =>
                Enum.GetValues<SeriesLean>().SelectMany(s => TieLines.Pool(new Tie(k, s, null, null), hot, won)))))
            .ToHashSet();
        foreach (var d in Days(200))
            foreach (var m in new[] { Moment.WinFinish, Moment.WinDecision, Moment.Loss, Moment.TitleWin, Moment.TitleLoss })
            {
                var raw = Strip(Quotes.Say(w, a, m, d, b));
                Assert.DoesNotContain(raw, all);
                Assert.Equal(Quotes.Say(a, m, d, b), Quotes.Say(w, a, m, d, b));
            }

        // A rematch pool is only ever used with meetings behind it, and a leading pool only for a leader.
        AddBout(w, a, b, b, new DateOnly(2025, 1, 1));   // b won: a trails
        var trails = Ties.Strongest(w, a, b, new DateOnly(2026, 6, 1))!;
        Assert.Equal(SeriesLean.Trails, trails.Series);
        var leads = TieLines.Pool(new Tie(TieKind.Rematch, SeriesLean.Leads, null, null), TieLines.IsHot(a.Disposition), true).ToHashSet();
        foreach (var d in Days(200))
            Assert.DoesNotContain(Strip(Quotes.Say(w, a, Moment.WinFinish, d, b)), leads);
    }

    [Fact]
    public void AWomanAcrossTheCage_GetsHerPronouns()
    {
        var w = Fresh();
        var volkova = ByLast(w, "Volkova"); var volkov = ByLast(w, "Volkov");
        Assert.Equal(Sex.Female, volkova.Sex);
        foreach (var d in Days())
        {
            // Volkov speaks about his sister: the line is about HER.
            string say = Strip(Quotes.Say(w, volkov, Moment.WinFinish, d, volkova));
            Assert.DoesNotMatch(@"\b(he|him|his)\b", say);
        }
    }

    // --- era safety -------------------------------------------------------------------------------

    private static readonly Regex EraUnsafe = new(
        @"rank|weight|glove|pay-per-view|\bppv\b|stream|internet|online|social media|podcast|\bpound|\blbs?\b|division|catchweight|podium|unified|commission",
        RegexOptions.IgnoreCase);

    [Fact]
    public void NoTieLine_SaysAnythingTheYear1993CouldNotHold()
    {
        var all = Enum.GetValues<TieKind>()
            .SelectMany(k => Enum.GetValues<SeriesLean>().SelectMany(s => new[] { true, false }.SelectMany(hot => new[] { true, false }
                .SelectMany(won => TieLines.Pool(new Tie(k, s, null, null), hot, won)))))
            .ToList();
        Assert.True(all.Count >= 60);
        foreach (var line in all) Assert.DoesNotMatch(EraUnsafe, line);
        foreach (var line in all)   // no counts, no places, no names: the tie is all the line claims
            Assert.DoesNotMatch(@"\d|\b(twice|thrice|three|four|five|ten|hundred|first|only)\b", line);
    }

    [Fact]
    public void EveryTieLine_KeepsToItsRegister_AndIsNotAnEmptyPool()
    {
        foreach (var k in Enum.GetValues<TieKind>())
            foreach (bool hot in new[] { true, false })
                foreach (bool won in new[] { true, false })
                    Assert.NotEmpty(TieLines.Pool(new Tie(k, SeriesLean.Level, null, null), hot, won));
        foreach (var s in new[] { SeriesLean.Leads, SeriesLean.Trails })
            foreach (bool hot in new[] { true, false })
                foreach (bool won in new[] { true, false })
                    Assert.NotEmpty(TieLines.Pool(new Tie(TieKind.Rematch, s, null, null), hot, won));
    }

    // --- the Biographer's people ---------------------------------------------------------------------

    [Fact]
    public void ADayOneProfile_NamesTheAuthoredBondsFromBothSides()
    {
        var w = Fresh();
        var volkov = ByLast(w, "Volkov"); var baranov = ByLast(w, "Baranov");
        string v = Biographer.Fighter(w, volkov), b = Biographer.Fighter(w, baranov);
        Assert.Contains("mentors", v); Assert.Contains(baranov.FullName, v);
        Assert.Contains("mentor is", b); Assert.Contains(volkov.FullName, b);
        Assert.Contains("Blood ties", v); Assert.Contains(ByLast(w, "Volkova").FullName, v);   // his sister

        var people = Biographer.People(w, volkov);
        Assert.Contains(people, l => l.Kind == PeopleKind.Student && l.Other == baranov.Id);
        Assert.Contains(people, l => l.Kind == PeopleKind.Family);
        Assert.StartsWith(Biographer.People(w, volkov)[0].Text.Split(' ')[0], Biographer.PeopleText(w, volkov));

        // Friends, from both sides.
        string fu = Biographer.Fighter(w, ByLast(w, "Fuentes")), qu = Biographer.Fighter(w, ByLast(w, "Quiroga"));
        Assert.Contains("friend", fu); Assert.Contains(ByLast(w, "Quiroga").FullName, fu);
        Assert.Contains("friend", qu); Assert.Contains(ByLast(w, "Fuentes").FullName, qu);
    }

    [Fact]
    public void ARival_IsNamedWithTheAuthoredReason_FromBothSides()
    {
        var w = Fresh();
        var rivas = ByLast(w, "Rivas"); var sales = ByLast(w, "Sales");
        string reason = MmaManager.Core.Sim.Generation.DesignedRoster.FeudReason("Rivas", "Sales")!;
        string r = Biographer.Fighter(w, rivas), s = Biographer.Fighter(w, sales);
        Assert.Contains("rival", r); Assert.Contains(sales.FullName, r); Assert.Contains(reason, r);
        Assert.Contains("rival", s); Assert.Contains(rivas.FullName, s); Assert.Contains(reason, s);
    }

    [Fact]
    public void AuthoredBioLeads_AndThePeopleFollowTheCareerLine()
    {
        var w = Fresh();
        var volkov = ByLast(w, "Volkov");
        string text = Biographer.Fighter(w, volkov);
        Assert.StartsWith(volkov.Bio!.TrimEnd().TrimEnd('.').Substring(0, 20), text);
        Assert.True(text.IndexOf(" mentors ", StringComparison.Ordinal) > 0);
        Assert.Equal(text, Biographer.Fighter(w, volkov));
    }

    [Fact]
    public void ThePassage_UpdatesAfterResults_AndStaysTrue()
    {
        var w = Fresh();
        var rivas = ByLast(w, "Rivas"); var sales = ByLast(w, "Sales");
        string before = Biographer.Fighter(w, rivas);
        int met = Ties.SeriesBetween(w, rivas.Id, sales.Id).Meetings;

        AddBout(w, rivas, sales, rivas, w.CurrentDate.AddDays(1));
        AddBout(w, rivas, sales, rivas, w.CurrentDate.AddDays(30));
        AddBout(w, sales, rivas, sales, w.CurrentDate.AddDays(60));
        var s = Ties.SeriesBetween(w, rivas.Id, sales.Id);
        Assert.Equal(met + 3, s.Meetings);

        string after = Biographer.Fighter(w, rivas);
        Assert.NotEqual(before, after);
        Assert.Matches(@"Rivas and Sales have met \d+ times; (Rivas leads|Sales leads|the series is level at) ", after);
        string her = Biographer.Fighter(w, sales);
        Assert.Matches(@"Sales and Rivas have met \d+ times; ", her);

        // A student who holds the belt his mentor held (derived from the lineage, from both sides).
        var title = w.Titles.Values.First(t => t.CurrentChampion is not null);
        var champ = w.Get(title.CurrentChampion!.Value)!;
        var mentor = world_NonChamp(w, champ);
        w.Relationships.Add(new Relationship { A = mentor.Id, B = champ.Id, Kind = RelationshipKind.Mentor, Strength = 70, Since = w.CurrentDate });
        title.Lineage.Add(new TitleReign { Champion = mentor.Id, WonOn = new DateOnly(2000, 1, 1), LostOn = new DateOnly(2001, 1, 1) });
        string line = $"{champ.FullName} now holds the {title.Name} that {mentor.FullName} once held.";
        Assert.Contains(line, Biographer.Fighter(w, champ));
        Assert.Contains(line, Biographer.Fighter(w, mentor));
    }

    private static Fighter world_NonChamp(World w, Fighter champ) =>
        w.Fighters.Values.Where(f => f.Id != champ.Id && f.Status == FighterStatus.Active && !f.Designed
                                     && !w.Relationships.Any(r => r.Involves(f.Id)))
            .OrderBy(f => f.Id.Value).First();

    [Fact]
    public void ThePeoplePassage_NeverReadsAbility()
    {
        // Every line is built from names, banners, belts, bonds and results: no rating words, no numbers but a series tally.
        var w = Fresh();
        foreach (var f in w.Fighters.Values.Where(f => f.Designed))
            foreach (var l in Biographer.People(w, f))
                Assert.DoesNotMatch(@"\b(ability|potential|overall|rating|OVR|scouted)\b", l.Text);
    }

    // --- Origins ---------------------------------------------------------------------------------------

    [Fact]
    public void AnOriginsWorld_SpeaksAndShowsWithoutAnythingEraUnsafe()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(11));
        int peopleLines = 0;
        foreach (var f in w.Fighters.Values.OrderBy(f => f.Id.Value))
            foreach (var l in Biographer.People(w, f))
            {
                peopleLines++;
                Assert.DoesNotMatch(EraUnsafe, l.Text);
            }
        Assert.True(peopleLines > 10, "the pioneers' bonds and feuds should speak");

        var pioneers = w.Fighters.Values.Where(f => f.Designed).ToList();
        var kawabata = pioneers.Single(f => f.LastName == "Kawabata"); var hagiwara = pioneers.Single(f => f.LastName == "Hagiwara");
        Assert.Contains("mentors", Biographer.Fighter(w, kawabata));
        Assert.Contains(kawabata.FullName, Biographer.Fighter(w, hagiwara));

        var quintela = pioneers.Single(f => f.LastName == "Quintela"); var paranhos = pioneers.Single(f => f.LastName == "Paranhos");
        string reason = MmaManager.Core.Sim.Generation.Origins.OriginsPioneers.FeudReason("Quintela", "Paranhos")!;
        Assert.Contains(reason, Biographer.Fighter(w, quintela));
        Assert.Contains(reason, Biographer.Fighter(w, paranhos));

        // Tie lines in an Origins world: every call, every pioneer pair, nothing era-unsafe.
        foreach (var a in pioneers)
            foreach (var b in pioneers.Take(10))
                foreach (var m in new[] { Moment.WinFinish, Moment.WinDecision, Moment.Loss, Moment.TitleWin, Moment.TitleLoss })
                    Assert.DoesNotMatch(EraUnsafe, Quotes.Say(w, a, m, w.CurrentDate.AddDays(a.Id.Value % 50), b));
        Assert.Equal(Quotes.Say(w, kawabata, Moment.WinFinish, w.CurrentDate, hagiwara), Quotes.Say(w, kawabata, Moment.WinFinish, w.CurrentDate, hagiwara));
    }
}
