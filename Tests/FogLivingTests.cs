using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// The living world's choices and headlines read what is seen, never the truth: an AI house's short-notice
/// stand-in, the front office's retention bonus, Rookie of the Year's tie-break, which signings and retirements
/// make the news, and the day-one market headlines. Every test here failed on the code before its fix.
/// </summary>
public class FogLivingTests
{
    [Fact]
    public void AnAiHousesStandIn_IsTheManItRatesHighest_InTodaysMode()
    {
        var (w, promo, _, y, z, up, down) = FogMatchmakerTests.Setup();
        Assert.Equal(WorldMode.Modern, w.Mode);
        var date = w.CurrentDate.AddDays(20);
        var fight = new Fight { Id = w.NewFightId(), RedCorner = y.Id, BlueCorner = z.Id, WeightClass = y.WeightClass };
        var ev = new Event { Id = w.NewEventId(), Promotion = promo.Id, Name = "Fog Night", Date = date, Card = { fight } };
        // Only Up and Down can take the call.
        var booked = w.Fighters.Keys.Where(id => id != up.Id && id != down.Id).ToHashSet();

        Assert.True(Storylines.SaveBout(w, ev, fight, z.Id, booked, w.CurrentDate, "Injured in camp"));

        var replacement = ev.Card.Single();
        Assert.Equal(y.Id, replacement.RedCorner);
        Assert.Equal(up.Id, replacement.BlueCorner);   // the truth would have sent in Down
    }

    [Fact]
    public void TheRetentionBonus_GoesToTheManTheHouseValuesMost()
    {
        var (w, promo, _, _, _, up, down) = FogMatchmakerTests.Setup();
        foreach (var f in new[] { up, down })
        {
            f.Loyalty = 10;
            f.Intangibles.Popularity = 40;
        }
        promo.Balance = new Money(50_000_000m);

        AiFrontOffice.RetainAFlightRisk(w, promo, new List<Fighter> { down, up }, surplus: 50_000_000m);

        Assert.True(up.Loyalty > 10);     // the house's read
        Assert.Equal(10, down.Loyalty);   // the truth would have paid Down
    }

    [Fact]
    public void RookieOfTheYear_TiesGoOnThePublicRecord_NotHiddenAbility()
    {
        var w = FogKit.Modern();
        int year = w.CurrentDate.Year;
        var award = new DateOnly(year + 1, 1, 1);
        foreach (var e in w.Events.Values.Where(e => e.Date.Year == year).ToList()) w.Events.Remove(e.Id);
        var kids = w.Fighters.Values.Where(f => f.Status == FighterStatus.Active && f.AgeOn(award) <= 24)
            .OrderBy(f => f.Id.Value).Take(2).ToList();
        var vets = w.Fighters.Values.Where(f => f.Status == FighterStatus.Active && f.AgeOn(award) >= 30)
            .OrderBy(f => f.Id.Value).Take(2).ToList();
        Assert.Equal(2, kids.Count);
        var promo = w.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value).First();
        var ev = new Event { Id = w.NewEventId(), Promotion = promo.Id, Name = "Rookie Night", Date = new DateOnly(year, 12, 1), Status = EventStatus.Completed };
        for (int i = 0; i < 2; i++)
        {
            var bout = new Fight
            {
                Id = w.NewFightId(), RedCorner = kids[i].Id, BlueCorner = vets[i].Id, WeightClass = kids[i].WeightClass,
                Outcome = new FightOutcome { Winner = kids[i].Id, Loser = vets[i].Id, Method = FightMethod.UnanimousDecision, EndRound = 3 },
            };
            ev.Card.Add(bout);
            kids[i].BoutHistory.Clear();
            kids[i].BoutHistory.Add(bout.Id);
            FogKit.Unknown(kids[i]);
            kids[i].Record.Wins = 1; kids[i].Record.WinsByDecision = 1; kids[i].Record.CurrentStreak = 1;
        }
        w.Events[ev.Id] = ev;
        // Identical in public; the later id is far better in truth.
        FogKit.SetTrue(kids[0], 50);
        FogKit.SetTrue(kids[1], 75);

        Storylines.YearEndAwards(w, award);

        Assert.Contains(kids[0].Accolades, a => a.Title.Contains("Rookie of the Year"));
        Assert.DoesNotContain(kids[1].Accolades, a => a.Title.Contains("Rookie of the Year"));
    }

    // A man with a blank record whose public read sits at least `gap` points above (or below) his truth.
    private static Fighter Unknown(World w, Func<int, bool> offset)
    {
        foreach (var f in w.FreeAgents().Where(f => f.Status == FighterStatus.Active).OrderBy(f => f.Id.Value))
        {
            FogKit.Unknown(f);
            FogKit.SetTrue(f, 60);
            if (offset(ScoutingService.PublicRead(w, f) - 60)) return f;
        }
        throw new InvalidOperationException("no such free agent");
    }

    [Fact]
    public void WhichSigningsMakeTheNews_IsThePressesRead_NotTheTruth()
    {
        var w = FogKit.Modern();
        var promo = w.Promotions.Values.Where(p => !p.Defunct && p.Id != w.PlayerPromotion).OrderBy(p => p.Id.Value).First();
        var hyped = Unknown(w, o => o >= 4);   // read well above the truth
        var quiet = Unknown(w, o => o <= -4);  // read well below it
        FogKit.SetTrue(hyped, News.NewsworthyRead - 2);
        FogKit.SetTrue(quiet, News.NewsworthyRead + 2);
        Assert.True(ScoutingService.PublicRead(w, hyped) >= News.NewsworthyRead);
        Assert.True(ScoutingService.PublicRead(w, quiet) < News.NewsworthyRead);
        w.News.Clear();

        News.Signed(w, promo, hyped, new Money(10_000m), w.CurrentDate, renewal: false);
        News.Signed(w, promo, quiet, new Money(10_000m), w.CurrentDate, renewal: false);
        News.Retired(w, hyped, w.CurrentDate, champion: false);
        News.Retired(w, quiet, w.CurrentDate, champion: false);

        Assert.Contains(w.News, n => n.Headline.StartsWith(hyped.FullName + " signs with"));
        Assert.Contains(w.News, n => n.Headline.StartsWith(hyped.FullName + " announces retirement"));
        Assert.DoesNotContain(w.News, n => n.Headline.StartsWith(quiet.FullName));
    }

    [Fact]
    public void TheDayOneWhisper_NamesTheRecord_NotTheHiddenCeiling()
    {
        var w = FogKit.Modern();
        var start = w.CurrentDate;
        bool Eligible(Fighter f) => f.Contract is null && f.Status == FighterStatus.Active && !f.LocalTalent && f.AgeOn(start) <= 22;
        var kids = w.Fighters.Values.Where(Eligible).OrderBy(f => f.Id.Value).ToList();
        Assert.True(kids.Count >= 2, "need two unsigned kids");
        foreach (var k in kids) k.PotentialAbility = Math.Min(k.PotentialAbility.Value, 60);
        var proven = kids[0];
        var ceiling = kids[1];
        FogKit.Unknown(proven); proven.Record.Wins = 30; proven.Record.WinsByDecision = 30; proven.Record.CurrentStreak = 30;
        FogKit.Unknown(ceiling); ceiling.Record.Wins = 3; ceiling.Record.WinsByDecision = 3; ceiling.Record.CurrentStreak = 3;
        ceiling.PotentialAbility = 99;
        w.News.Clear();

        Prologue.Write(w, start);

        var whisper = w.News.Single(n => n.Headline.StartsWith("The name the scouts keep saying"));
        Assert.EndsWith(proven.FullName, whisper.Headline);   // the hidden ceiling would have named the other kid
    }

    [Fact]
    public void TheDayOneFreeAgentHeadlines_GoOnThePublicRead()
    {
        var w = FogKit.Modern();
        // Only six free agents are names (popularity 35+): five the press reads near their truth and one it reads
        // far below his - who is, in truth, the best of the six.
        foreach (var f in w.FreeAgents()) f.Intangibles.Popularity = 0;
        var underrated = Unknown(w, o => o <= -5);
        var fine = new List<Fighter>();
        foreach (var f in w.FreeAgents().Where(f => f.Status == FighterStatus.Active && !f.LocalTalent && f != underrated)
                     .OrderBy(f => f.Id.Value))
        {
            FogKit.Unknown(f);
            FogKit.SetTrue(f, 60);
            if (ScoutingService.PublicRead(w, f) - 60 >= -2) fine.Add(f);
            if (fine.Count == 5) break;
        }
        Assert.Equal(5, fine.Count);
        foreach (var f in fine.Append(underrated)) f.Intangibles.Popularity = 40;
        FogKit.SetTrue(underrated, 64);
        Assert.True(fine.All(f => ScoutingService.PublicRead(w, f) > ScoutingService.PublicRead(w, underrated)));
        Assert.True(fine.All(f => FighterAbility.CurrentAbility(f) < FighterAbility.CurrentAbility(underrated)));
        w.News.Clear();

        Prologue.Write(w, w.CurrentDate);

        var named = w.News.Where(n => n.Headline.EndsWith(" is a free agent")).Select(n => n.Headline).ToList();
        Assert.Equal(5, named.Count);
        Assert.DoesNotContain(underrated.FullName + " is a free agent", named);   // the truth would have named him
    }
}
