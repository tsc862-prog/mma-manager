using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;

namespace MmaManager.Tests;

/// <summary>
/// The world's PAST is real: every fighter's record is backed by actual
/// back-dated events with opponents, methods and dates; title lineages predate
/// the save; the authored lore bouts exist as data; and the résumé rankings
/// judge the pre-history like any other body of work.
/// </summary>
public class HistoryBuilderTests
{
    private static World Build(ulong seed = 7) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    // Records and histories describe the same facts — for everyone with a career.
    [Fact]
    public void EveryRecord_IsBackedByRealBouts()
    {
        var world = Build();
        var fights = world.Events.Values
            .SelectMany(e => e.Card.Select(f => (Fight: f, Event: e)))
            .ToDictionary(x => x.Fight.Id, x => x);

        var withHistory = world.Fighters.Values
            .Where(f => !f.LocalTalent && f.BoutHistory.Count > 0 && f.Status == FighterStatus.Active)
            .ToList();
        Assert.NotEmpty(withHistory);

        foreach (var f in withHistory)
        {
            int w = 0, l = 0, d = 0, nc = 0;
            DateOnly prev = DateOnly.MinValue;
            foreach (var id in f.BoutHistory)
            {
                Assert.True(fights.ContainsKey(id), $"{f.FullName} has a bout that resolves to no event");
                var (fight, ev) = fights[id];
                Assert.NotNull(fight.Outcome);
                Assert.Equal(EventStatus.Completed, ev.Status);
                Assert.True(ev.Date < world.StartDate, "pre-history lives before day one");
                Assert.True(ev.Date >= prev, $"{f.FullName}'s history is out of order");
                prev = ev.Date;

                var o = fight.Outcome!;
                if (o.Winner == f.Id) w++;
                else if (o.Loser == f.Id) l++;
                else if (o.Method == FightMethod.NoContest) nc++;
                else d++;
            }
            Assert.Equal(f.Record.Wins, w);
            Assert.Equal(f.Record.Losses, l);
            Assert.Equal(f.Record.Draws, d);
            Assert.Equal(f.Record.NoContests, nc);
        }
    }

    [Fact]
    public void DesignedTallies_SurviveTheHistoryPass()
    {
        var world = Build();
        foreach (var spec in DesignedRoster.All)
        {
            var f = world.Fighters.Values.First(x => x.Designed && x.FirstName == spec.First && x.LastName == spec.Last);
            Assert.Equal(spec.Wins, f.Record.Wins);
            Assert.Equal(spec.Losses, f.Record.Losses);
            if (spec.Streak is { } s) Assert.Equal(s, f.Record.CurrentStreak);
        }
    }

    [Theory]
    [InlineData(1ul)]
    [InlineData(7ul)]
    [InlineData(21ul)]
    public void TheLoreBouts_ExistAsData(ulong seed)
    {
        var world = Build(seed);
        Fighter F(string last) => world.Fighters.Values.First(x => x.Designed && x.LastName == last);
        var results = world.Events.Values
            .SelectMany(e => e.Card.Select(f => (Fight: f, e.Date)))
            .Where(x => x.Fight.Outcome is not null)
            .ToList();
        List<(Fight Fight, DateOnly Date)> Between(Fighter a, Fighter b) => results
            .Where(x => (x.Fight.RedCorner == a.Id && x.Fight.BlueCorner == b.Id)
                     || (x.Fight.RedCorner == b.Id && x.Fight.BlueCorner == a.Id))
            .ToList();

        // Mercer took a decision off Seo — the grudge has a bout behind it.
        var grudge = Between(F("Mercer"), F("Seo"));
        Assert.Single(grudge);
        Assert.Equal(F("Mercer").Id, grudge[0].Fight.Outcome!.Winner);

        // The Baltic wars stand at one apiece, and the second was for the belt.
        var wars = Between(F("Ozols"), F("Urbonas"));
        Assert.Equal(2, wars.Count);
        Assert.Contains(wars, x => x.Fight.Outcome!.Winner == F("Ozols").Id);
        Assert.Contains(wars, x => x.Fight.Outcome!.Winner == F("Urbonas").Id);
        Assert.Contains(wars, x => x.Fight.IsTitleFight);

        // Hale dethroned Tanner — the AFC light-heavy lineage remembers the reign.
        var dethrone = Between(F("Hale"), F("Tanner"));
        Assert.Single(dethrone);
        Assert.True(dethrone[0].Fight.IsTitleFight);
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        var lhw = world.Titles.Values.First(t => t.Promotion == afc.Id
            && t.WeightClass == WeightClass.LightHeavyweight
            && world.Fighters[t.CurrentChampion!.Value].Sex == Sex.Male);
        Assert.Contains(lhw.Lineage, r => r.Champion == F("Tanner").Id && r.LostOn is not null);
        Assert.Equal(F("Hale").Id, lhw.Lineage[^1].Champion);
    }

    [Fact]
    public void ChampionsReigns_PredateTheSave()
    {
        var world = Build();
        foreach (var title in world.Titles.Values.Where(t => t.CurrentChampion is not null))
        {
            var reign = title.Lineage[^1];
            Assert.Equal(title.CurrentChampion, reign.Champion);
            Assert.True(reign.WonOn < world.StartDate,
                $"{title.Name}: the champion should have won the belt before day one");

            // The belt-winning night is a real title fight on a real card.
            var winBout = world.Events.Values
                .Where(e => e.Date == reign.WonOn && e.Promotion == title.Promotion)
                .SelectMany(e => e.Card)
                .FirstOrDefault(f => f.IsTitleFight && f.Title == title.Id
                                     && f.Outcome?.Winner == reign.Champion);
            Assert.True(winBout is not null, $"{title.Name}: no belt-winning bout on {reign.WonOn}");
        }
    }

    [Fact]
    public void DayOneRankings_AreEarnedInTheCage()
    {
        var world = Build();
        // The résumé recompute ran over the pre-history: the AFC's divisions rank
        // fighters who actually have recent results behind them.
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        var recent = world.Events.Values.Where(e => e.Date >= world.StartDate.AddYears(-4)).ToList();
        Assert.NotEmpty(recent);
        foreach (var div in afc.Divisions.Where(d => d.Rankings.Count > 0))
        {
            var top = world.Fighters[div.Rankings[0]];
            Assert.True(top.BoutHistory.Count > 0, $"{top.FullName} is ranked #1 with no career behind the record");
        }
    }

    [Fact]
    public void EventNumbering_ContinuesThePast()
    {
        var world = Build();
        // Every established promotion has a past to continue; a fresh AI card
        // would number itself events-so-far + 1.
        foreach (var promo in world.Promotions.Values)
        {
            int past = world.Events.Values.Count(e => e.Promotion == promo.Id && e.Date < world.CurrentDate);
            Assert.True(past > 0, $"{promo.Abbreviation} should have a history of cards");
            Assert.All(world.Events.Values.Where(e => e.Promotion == promo.Id && e.Date < world.CurrentDate),
                e => Assert.Equal(EventStatus.Completed, e.Status));
            // The forward calendar's announced cards (day one is mid-story) are
            // all in the future, and number on from the past.
            var ahead = world.Events.Values.Where(e => e.Promotion == promo.Id && e.Date >= world.CurrentDate).ToList();
            Assert.All(ahead, e => Assert.Equal(EventStatus.Scheduled, e.Status));
            Assert.All(ahead.Where(e => e.Name.Contains("Fight Night ")), e =>
                Assert.True(int.Parse(e.Name[(e.Name.LastIndexOf(' ') + 1)..]) > past));
        }
    }

    [Fact]
    public void MintedOpposition_StaysOutOfTheLivingWorld()
    {
        var world = Build();
        var minted = world.Fighters.Values
            .Where(f => f.Status == FighterStatus.Retired && f.BoutHistory.Count > 0)
            .ToList();
        Assert.NotEmpty(minted);
        // Retired opposition never shows up as signable talent or in rankings.
        Assert.DoesNotContain(world.FreeAgents(), f => f.Status == FighterStatus.Retired);
        foreach (var ranked in world.GlobalRankings.Values.SelectMany(r => r))
            Assert.NotEqual(FighterStatus.Retired, world.Fighters[ranked].Status);
    }

    [Fact]
    public void FoundedStart_HasNoHistoryOfItsOwn()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 11,
            Founding = new WorldBuilder.FoundingSpec(),
        });
        // The player's brand-new promotion never staged a card; the rest of the world did.
        Assert.DoesNotContain(world.Events.Values, e => e.Promotion == world.PlayerPromotion);
        Assert.Contains(world.Events.Values, e => e.Status == EventStatus.Completed);
    }

    [Fact]
    public void History_IsDeterministicFromTheSeed()
    {
        var a = Build(21);
        var b = Build(21);
        Assert.Equal(a.Events.Count, b.Events.Count);
        Assert.Equal(a.Fighters.Count, b.Fighters.Count);
        string Fingerprint(World w) => string.Join("|",
            w.Events.Values.OrderBy(e => e.Id.Value).Select(e =>
                $"{e.Name}@{e.Date:yyyyMMdd}:{string.Join(",", e.Card.Select(f => $"{f.RedCorner.Value}v{f.BlueCorner.Value}={f.Outcome?.Winner?.Value ?? 0}"))}"));
        Assert.Equal(Fingerprint(a), Fingerprint(b));
    }

    [Fact]
    public void NoHistory_OptsTheWholeSystemOff()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, GenerateHistory = false });
        Assert.Empty(world.Events);
        Assert.All(world.Fighters.Values, f => Assert.Empty(f.BoutHistory));
    }
}
