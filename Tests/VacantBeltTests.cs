using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;

namespace MmaManager.Tests;

/// <summary>
/// Who a VACANT belt is crowned between (today's mode). Before this rule set a vacant belt went to whichever two
/// ranked men happened to be free on the date, in or out of form, and nothing kept the #1 and #2 off the undercard
/// while the belt waited for its card: both men in a crowning were the house's top two only about a quarter of the
/// time (longsim, 100 seeds). Six of these tests fail on main (an out-of-form #2 crowned over a ready #3; the #1 and #2,
/// or the second vacant belt's #1 and #2, booked into undercard bouts with the belt vacant; a hurt or deal-spent #1 with
/// nobody held behind him); the rest guard what must not change (a weak or thin division and a small house crown at
/// once, the player's house and a belt already on a card hold nobody).
/// Every scene is built two years past the generated history, so only the bouts staged here are recent.
/// </summary>
public class VacantBeltTests
{
    private sealed record Scene(World W, Promotion P, Division D, Title T, List<Fighter> Ranked, Fighter Spare);

    // An AI house's titled men's division whose belt is vacant, with nine ranked, ready, in-form men on live deals
    // and a spare man outside the rankings. No rivalries; every other belt's rankings are emptied so no other belt
    // queues ahead or competes.
    private static Scene Build(ulong seed = 7)
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed, UseDesignedRoster = false });
        w.CurrentDate = w.CurrentDate.AddYears(2);
        w.Rivalries.Clear();
        foreach (var promo in w.Promotions.Values.Where(p => !p.Defunct && p.Id != w.PlayerPromotion).OrderBy(p => p.Id.Value))
            foreach (var div in promo.Divisions.Where(d => d.Sex == Sex.Male && d.Title is not null).OrderBy(d => (int)d.WeightClass))
            {
                var title = w.Get(div.Title!.Value)!;
                var men = w.RosterOf(promo.Id).Where(f => div.Matches(f) && f.Id != title.CurrentChampion).OrderBy(f => f.Id.Value).ToList();
                if (men.Count < 10) continue;
                foreach (var f in men)
                {
                    f.Status = FighterStatus.Active;
                    f.CurrentInjury = null;
                    f.ReadyToFightOn = null;
                    f.SuspendedUntil = null;
                    f.Camp = null;
                    f.Record.Wins = 12; f.Record.Losses = 2; f.Record.CurrentStreak = 2;
                    if (f.Contract is { } c) c.FightsRemaining = Math.Max(c.FightsRemaining, 3);
                }
                w.Relationships.Clear();
                if (title.CurrentChampion is { } old && w.Get(old) is { } ex) ex.Status = FighterStatus.Retired;
                title.CurrentChampion = null;
                title.Lineage.Clear();
                var ranked = men.Take(9).ToList();
                div.Rankings.Clear();
                div.Rankings.AddRange(ranked.Select(f => f.Id));
                foreach (var other in promo.Divisions)
                    if (other != div) other.Rankings.Clear();
                return new Scene(w, promo, div, title, ranked, men[9]);
            }
        throw new InvalidOperationException("no AI men's titled division with ten men");
    }

    private static HashSet<FighterId> AllBut(Scene s, IEnumerable<Fighter> keep)
    {
        var k = keep.Select(f => f.Id).ToHashSet();
        return s.W.Fighters.Keys.Where(id => !k.Contains(id)).ToHashSet();
    }

    // The crowning this house would put on top in 35 days, with the given men unavailable (booked elsewhere).
    private static Matchup? Crowning(Scene s, params Fighter[] unavailable)
    {
        var exclude = AllBut(s, s.Ranked.Append(s.Spare).Where(f => !unavailable.Contains(f)));
        var bout = new Matchmaker().BestBout(s.W, s.P, s.W.CurrentDate.AddDays(35), exclude, titleLength: true);
        return bout is { IsTitle: true } b && b.Title == s.T.Id ? b : null;
    }

    // The non-title bouts this house would book in 21 days from the scene's men alone.
    private static IReadOnlyList<Matchup> Undercard(Scene s) =>
        new Matchmaker().BuildUndercard(s.W, s.P.Id, 10, s.W.CurrentDate.AddDays(21), AllBut(s, s.Ranked.Append(s.Spare)));

    private static bool On(IReadOnlyList<Matchup> card, Fighter f) => card.Any(m => m.Red == f.Id || m.Blue == f.Id);
    private static void OutOfForm(Fighter f) { f.Record.Wins = 2; f.Record.Losses = 6; f.Record.CurrentStreak = -2; }

    [Fact]
    public void AVacantBelt_IsCrownedBetweenTheTopTwo_TheBetterRankedInRed()
    {
        var s = Build();
        var bout = Crowning(s);
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[0].Id, bout!.Value.Red);
        Assert.Equal(s.Ranked[1].Id, bout.Value.Blue);
    }

    [Fact]
    public void AnOutOfFormSecond_IsPassedOverForTheNextCredibleMan()
    {
        var s = Build();
        OutOfForm(s.Ranked[1]);
        var bout = Crowning(s);
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[0].Id, bout!.Value.Red);
        Assert.Equal(s.Ranked[2].Id, bout.Value.Blue);
    }

    [Fact]
    public void AWeakDivision_StillCrownsAtOnce_TheOldWay()
    {
        // Nobody inside the window but #1 is in form: no credible pair, so the two best-ranked men free fight for it now.
        var s = Build();
        foreach (var f in s.Ranked.Skip(1).Take(Matchmaker.TitleShotWindow - 1)) OutOfForm(f);
        var bout = Crowning(s);
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[0].Id, bout!.Value.Red);
        Assert.Equal(s.Ranked[1].Id, bout.Value.Blue);
    }

    [Fact]
    public void AThinDivision_StillCrownsAtOnce()
    {
        // Two ranked men, one out of form: the belt is still contested between them.
        var s = Build();
        s.D.Rankings.RemoveRange(2, s.D.Rankings.Count - 2);
        OutOfForm(s.Ranked[1]);
        var bout = Crowning(s);
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[0].Id, bout!.Value.Red);
        Assert.Equal(s.Ranked[1].Id, bout.Value.Blue);
    }

    [Fact]
    public void ASmallHouse_CrownsAtOnce_WhenItsTopTwoAreBusy()
    {
        // A Regional house runs one card a month: with #1 and #2 booked elsewhere the belt is not sat out, #3 and #4 fight for it.
        var s = Build();
        s.P.Prestige = 25;
        Assert.Equal(OrgSize.Regional, MmaManager.Core.Sim.Org.OrgSizing.SizeOf(s.P));
        var bout = Crowning(s, s.Ranked[0], s.Ranked[1]);
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[2].Id, bout!.Value.Red);
        Assert.Equal(s.Ranked[3].Id, bout.Value.Blue);
    }

    [Fact]
    public void TheTopTwo_AreKeptOffTheUndercard_WhileTheBeltIsVacant()
    {
        var s = Build();
        var card = Undercard(s);
        Assert.NotEmpty(card);
        Assert.False(On(card, s.Ranked[0]), "#1 waits for the belt");
        Assert.False(On(card, s.Ranked[1]), "#2 waits for the belt");
        Assert.True(On(card, s.Ranked[2]), "#3 fights on");
    }

    [Fact]
    public void TheMenHeld_AreTheMenCrowned()
    {
        // #2 out of form: the belt goes to #1 and #3, so those two are held and #2 fights.
        var s = Build();
        OutOfForm(s.Ranked[1]);
        var bout = Crowning(s)!.Value;
        var card = Undercard(s);
        Assert.Equal(s.Ranked[0].Id, bout.Red);
        Assert.Equal(s.Ranked[2].Id, bout.Blue);
        Assert.False(On(card, s.Ranked[0]));
        Assert.False(On(card, s.Ranked[2]));
        Assert.True(On(card, s.Ranked[1]), "the out-of-form #2 is not the belt's man: he fights on");
    }

    [Fact]
    public void ASpentDeal_IsNeverHeld()
    {
        var s = Build();
        s.Ranked[0].Contract!.FightsRemaining = 0;
        var card = Undercard(s);
        Assert.True(On(card, s.Ranked[0]), "a man with no fights left on his deal is not held for the belt");
        Assert.False(On(card, s.Ranked[1]));
        Assert.False(On(card, s.Ranked[2]));
    }

    [Fact]
    public void AHurtMan_IsNeverHeld()
    {
        // #1 hurt past the night the belt would be fought: #2 and #3 are held, #1 is not (and can't be booked).
        var s = Build();
        var hurt = s.Ranked[0];
        hurt.Status = FighterStatus.Injured;
        hurt.CurrentInjury = new Injury { Severity = InjurySeverity.Minor, Description = "test", OccurredOn = s.W.CurrentDate, ExpectedReturn = s.W.CurrentDate.AddDays(90) };
        var card = Undercard(s);
        Assert.False(On(card, s.Ranked[1]));
        Assert.False(On(card, s.Ranked[2]));
        Assert.True(On(card, s.Ranked[3]), "#4 fights on");
    }

    [Fact]
    public void AWeakDivision_HoldsNobody()
    {
        var s = Build();
        foreach (var f in s.Ranked.Skip(1).Take(Matchmaker.TitleShotWindow - 1)) OutOfForm(f);
        var card = Undercard(s);
        Assert.True(On(card, s.Ranked[0]), "no credible pair, nobody held");
    }

    [Fact]
    public void ThePlayersHouse_IsNeverHeldOn()
    {
        var s = Build();
        s.W.PlayerPromotion = s.P.Id;
        var card = Undercard(s);
        Assert.True(On(card, s.Ranked[0]));
        Assert.True(On(card, s.Ranked[1]));
    }

    [Fact]
    public void OnTheCrowningCard_TheNextPairDown_IsNotHeld()
    {
        // The card that crowns #1 v #2 books its undercard from the rest: #3 and #4 are not held for a belt already taken.
        var s = Build();
        var card = new Matchmaker().BuildCard(s.W, s.P.Id, 10, s.W.CurrentDate.AddDays(35), AllBut(s, s.Ranked.Append(s.Spare)));
        var main = card[^1];
        Assert.True(main.IsTitle);
        Assert.Equal(s.T.Id, main.Title);
        Assert.True(On(card.Take(card.Count - 1).ToList(), s.Ranked[2]));
        Assert.True(On(card.Take(card.Count - 1).ToList(), s.Ranked[3]));

        var headliners = new Matchmaker().Headliners(s.W, s.P.Id, s.W.CurrentDate.AddDays(35), AllBut(s, s.Ranked.Append(s.Spare)));
        Assert.Equal(2, headliners.Count);
        Assert.False(headliners[0].IsTitle);
        Assert.True(On(new[] { headliners[0] }, s.Ranked[2]) || On(new[] { headliners[0] }, s.Ranked[3]),
            "the co-main may be the #3 v #4 eliminator");
    }

    [Fact]
    public void TwoVacantBelts_TheSecondPairIsHeld_WhileTheFirstCrowningIsOnTheBooks()
    {
        // A monthly house with two vacant belts announces the first one's crowning; until the second is booked its own
        // top two are kept off the undercard. (Before, the booked belt still took the first place in the house's queue,
        // the second was a cadence away, past the undercard's date, and its top two were booked: the original failure,
        // for every vacant belt after the first.)
        var s = Build();
        s.P.Prestige = 25;
        Assert.Equal(OrgSize.Regional, MmaManager.Core.Sim.Org.OrgSizing.SizeOf(s.P));
        var d2 = s.P.Divisions.Where(d => d != s.D && d.Title is not null)
            .Select(d => (D: d, Men: s.W.RosterOf(s.P.Id).Where(f => d.Matches(f) && !s.Ranked.Contains(f) && f != s.Spare).OrderBy(f => f.Id.Value).ToList()))
            .First(x => x.Men.Count >= 6);
        var t2 = s.W.Get(d2.D.Title!.Value)!;
        foreach (var f in d2.Men)
        {
            f.Status = FighterStatus.Active; f.CurrentInjury = null; f.ReadyToFightOn = null; f.SuspendedUntil = null; f.Camp = null;
            f.Record.Wins = 12; f.Record.Losses = 2; f.Record.CurrentStreak = 2;
            if (f.Contract is { } c) c.FightsRemaining = Math.Max(c.FightsRemaining, 3);
        }
        if (t2.CurrentChampion is { } old && s.W.Get(old) is { } ex) ex.Status = FighterStatus.Retired;
        t2.CurrentChampion = null;
        t2.Lineage.Clear();
        d2.D.Rankings.Clear();
        d2.D.Rankings.AddRange(d2.Men.Take(5).Select(f => f.Id));

        // The belts queue vacant-first by weight class, sex, title id: A is the one the house books first.
        static (int, int, int) Order(Division d) => ((int)d.WeightClass, (int)d.Sex, d.Title!.Value.Value);
        bool sceneFirst = Order(s.D).CompareTo(Order(d2.D)) < 0;
        var (aDiv, aTitle, aMen) = sceneFirst ? (s.D, s.T, s.Ranked) : (d2.D, t2, d2.Men);
        var (bMen, _) = sceneFirst ? (d2.Men, 0) : (s.Ranked, 0);

        var ev = new Event { Id = s.W.NewEventId(), Promotion = s.P.Id, Name = "Crowning night", Date = s.W.CurrentDate.AddDays(28), Status = EventStatus.Scheduled };
        ev.Card.Add(new Fight { Id = s.W.NewFightId(), RedCorner = aMen[0].Id, BlueCorner = aMen[1].Id, WeightClass = aDiv.WeightClass, IsTitleFight = true, Title = aTitle.Id });
        s.W.Events[ev.Id] = ev;
        s.W.Relationships.Clear();

        var exclude = AllBut(s, s.Ranked.Concat(d2.Men).Append(s.Spare).Where(f => f != aMen[0] && f != aMen[1]));
        var card = new Matchmaker().BuildUndercard(s.W, s.P.Id, 10, s.W.CurrentDate.AddDays(21), exclude);
        Assert.NotEmpty(card);
        Assert.False(On(card, bMen[0]), "the second belt's #1 waits for it");
        Assert.False(On(card, bMen[1]), "the second belt's #2 waits for it");
    }

    [Fact]
    public void ACrowningAlreadyOnTheBooks_HoldsNobody()
    {
        var s = Build();
        var ev = new Event { Id = s.W.NewEventId(), Promotion = s.P.Id, Name = "Crowning night", Date = s.W.CurrentDate.AddDays(28), Status = EventStatus.Scheduled };
        ev.Card.Add(new Fight { Id = s.W.NewFightId(), RedCorner = s.Ranked[0].Id, BlueCorner = s.Ranked[1].Id, WeightClass = s.D.WeightClass, IsTitleFight = true, Title = s.T.Id });
        s.W.Events[ev.Id] = ev;
        var exclude = AllBut(s, s.Ranked.Skip(2).Append(s.Spare));   // #1 and #2 are committed to that card
        var card = new Matchmaker().BuildUndercard(s.W, s.P.Id, 10, s.W.CurrentDate.AddDays(21), exclude);
        Assert.True(On(card, s.Ranked[2]));
        Assert.True(On(card, s.Ranked[3]));
    }
}
