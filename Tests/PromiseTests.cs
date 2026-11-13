using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using Xunit.Abstractions;

namespace MmaManager.Tests;

/// <summary>
/// Promises with teeth and matching rights (contracts stage 9, Contracts.Promises and
/// OfferMarket.MatchTerms): what each promise is worth at the table, when one is refused,
/// the monthly test (kept, broken, warned, void), what a broken one costs, the lifecycle
/// edges, the matching right against no right, and that an all-AI world never sees any of it.
/// </summary>
public class PromiseTests
{
    private readonly ITestOutputHelper _out;
    public PromiseTests(ITestOutputHelper output) => _out = output;

    // --- Refused at the table ---------------------------------------------------------------

    [Fact]
    public void APromiseThatCantBeKept_IsRefusedAtTheTable_AndCostsNoRound()
    {
        var world = FoundedWorld(48);
        var promo = Player(world);
        var f = HireOne(world);
        var div = promo.Divisions.First(d => d.Matches(f));
        div.Title = null;   // no belt in his division

        Assert.NotNull(Promises.Refusal(world, promo, f, PromiseKind.TitleShot, 0, 4));
        Assert.NotNull(Promises.Refusal(world, promo, f, PromiseKind.Activity, 5, 6));   // outside 2-4
        Assert.NotNull(Promises.Refusal(world, promo, f, PromiseKind.Activity, 3, 2));   // more than the deal holds
        Assert.Null(Promises.Refusal(world, promo, f, PromiseKind.Activity, 2, 4));

        // A belt he already holds can't be promised to him.
        var t = Belt(world, promo, div);
        t.CurrentChampion = f.Id;
        Assert.NotNull(Promises.Refusal(world, promo, f, PromiseKind.TitleShot, 0, 4));
        t.CurrentChampion = null;
        Assert.Null(Promises.Refusal(world, promo, f, PromiseKind.TitleShot, 0, 4));

        // A home show beyond the promotion's reach (a fighter from a country it can't book in).
        var abroad = world.FreeAgents().FirstOrDefault(x => x.Status == FighterStatus.Active && world.Get(x.Country) is not null
            && !world.Venues.Values.Any(v => world.Get(v.City)?.Country == x.Country && Core.Sim.Org.OrgReach.CanBookIn(world, promo, v.City)));
        Assert.NotNull(abroad);
        Assert.NotNull(Promises.Refusal(world, promo, abroad!, PromiseKind.HomeShow, 0, 4));

        // An AI promotion's deal never carries one.
        var ai = world.Promotions.Values.First(p => p.Id != promo.Id && !p.Defunct);
        Assert.NotNull(Promises.Refusal(world, ai, f, PromiseKind.Activity, 2, 4));

        // At the table: the room refuses it before anything else, without using a round.
        var s = NegotiationSession.Resume(world, f, promo, Exp(world, f));
        var offer = s.TheirTermsAsOffer(new ContractOffer { Fighter = f.Id, Promotion = promo.Id, Exclusive = Excl(promo), PromiseFights = 5 });
        var answer = s.Table(offer);
        Assert.Equal(TalkVerdict.Counter, answer.Verdict);
        Assert.Equal(0, s.Round);
        Assert.Contains(answer.Reactions, r => r.Term == "Promise: activity" && r.Feel == TermFeel.Sore);
        Assert.Equal(TalkOutcome.Open, s.Outcome);

        // The view says which can be made and which can't.
        var view = s.View(new ContractOffer { Fighter = f.Id, Promotion = promo.Id, Exclusive = Excl(promo) });
        Assert.Contains(view.PromiseOptions, o => o.Kind == PromiseKind.Activity && o.Fights == 2 && o.Allowed);
        Assert.Contains(view.PromiseOptions, o => o.Kind == PromiseKind.TitleShot && o.Allowed);
    }

    // --- Priced at the table -------------------------------------------------------------------

    [Fact]
    public void ATitleShotPromise_TakesMoneyOffAContendersNumber_AndTheMatchingRightAddsToIt()
    {
        var world = FoundedWorld(48);
        var promo = Player(world);
        // A free agent (no live deal to floor his number), placed as the #1 contender.
        var f = world.FreeAgents().Where(x => x.Status == FighterStatus.Active && !x.LocalTalent && promo.Divisions.Any(d => d.Matches(x)))
            .OrderByDescending(x => new FighterValuation().Evaluate(world, x).Priorities.TitleOpportunity).ThenBy(x => x.Id.Value).First();
        var div = promo.Divisions.First(d => d.Matches(f));
        Belt(world, promo, div);
        div.Rankings.Insert(0, f.Id);   // the #1 contender
        var exp = Exp(world, f);
        Assert.True(exp.Priorities.TitleOpportunity > 0, "precondition: he cares about a belt");

        var s = new NegotiationSession(world, f, promo, exp);
        bool excl = Excl(promo);
        var plain = new ContractOffer { Fighter = f.Id, Promotion = promo.Id, Exclusive = excl };
        var title = new ContractOffer { Fighter = f.Id, Promotion = promo.Id, Exclusive = excl, PromiseTitleShot = true };
        var right = new ContractOffer { Fighter = f.Id, Promotion = promo.Id, Exclusive = excl, MatchingRight = true };
        var p0 = s.DemandPurse(plain).Amount;
        var pt = s.DemandPurse(title).Amount;
        var pr = s.DemandPurse(right).Amount;
        _out.WriteLine($"his number {p0} · with a title-shot promise {pt} · with a matching right {pr}");
        Assert.True(pt < p0, "a title shot is worth money to a #1 contender");
        Assert.True(pt >= p0 * 0.75m, "never more than the quarter appeal can be worth");
        Assert.True(pr > p0, "he charges for a matching right");
        Assert.True(pr <= NegotiationSession.UpTo500(p0 * Negotiator.MatchingRightPremium) + 500m);

        // The AI's own price of an offer reads promises the same way (Appeal).
        Assert.True(Negotiator.Appeal(world, f, title, exp) > Negotiator.Appeal(world, f, plain, exp));

        // The view prices the option the same way.
        var view = s.View(plain);
        var opt = view.PromiseOptions.Single(o => o.Kind == PromiseKind.TitleShot);
        Assert.Equal(p0 - pt, opt.PurseOff.Amount);
        Assert.Equal(pr - p0, view.MatchingRightCost.Amount);

        // Signing on his terms with the promise: it is on the books, and he says so.
        var deal = s.TheirTermsAsOffer(title);
        Assert.True(deal.PromiseTitleShot);
        Assert.Equal(TalkVerdict.Accept, s.Table(deal).Verdict);
        int mail = world.Mail.Count;
        ContractLifecycle.Sign(world, f, deal, world.CurrentDate);
        var p = Assert.Single(world.Promises);
        Assert.Equal(PromiseKind.TitleShot, p.Kind);
        Assert.Equal(world.CurrentDate.AddDays(Promises.DeadlineDays), p.Deadline);
        Assert.Contains(world.Mail.Skip(mail), m => m.Subject == $"{f.FullName} will hold us to it");
        Assert.Single(Promises.For(world, f.Id));
    }

    // --- The monthly test --------------------------------------------------------------------

    [Fact]
    public void ATitleShotPromise_IsKept_ByATitleFight_AndEarnsLoyalty()
    {
        var (world, f, p) = Promised(PromiseKind.TitleShot);
        int loyalty = f.Loyalty;
        Fought(world, f, title: true);
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.Equal(PromiseState.Kept, p.State);
        Assert.Equal(Math.Min(100, loyalty + Promises.KeptLoyalty), f.Loyalty);
        Assert.Contains(world.Mail, m => m.Subject == $"{f.FullName}: you kept your word");
    }

    [Fact]
    public void AnActivityPromise_IsKept_OnlyWhenEnoughFightsAreFought()
    {
        var (world, f, p) = Promised(PromiseKind.Activity, fights: 2);
        Fought(world, f);
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.True(p.Live);
        Fought(world, f);
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.Equal(PromiseState.Kept, p.State);
    }

    [Fact]
    public void AHomeShowPromise_IsKept_OnlyByACardInHisCountry()
    {
        var (world, f, p) = Promised(PromiseKind.HomeShow);
        var home = world.Venues.Values.First(v => world.Get(v.City)?.Country == f.Country);
        var away = world.Venues.Values.FirstOrDefault(v => world.Get(v.City)?.Country != f.Country);
        if (away is not null)
        {
            Fought(world, f, venue: away.Id);
            Promises.RunMonthly(world, world.CurrentDate);
            Assert.True(p.Live);
        }
        Fought(world, f, venue: home.Id);
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.Equal(PromiseState.Kept, p.State);
    }

    [Fact]
    public void APromise_IsWarnedBeforeItBreaks_ThenBroken_WithARealCost()
    {
        var (world, f, p) = Promised(PromiseKind.TitleShot);
        var promo = Player(world);
        f.Loyalty = 50;

        world.CurrentDate = p.Deadline.AddDays(-40);
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.True(p.Live);
        Assert.True(p.Warned);
        Assert.Contains(world.Mail, m => m.Subject.StartsWith($"We promised {f.FullName} a title shot") && m.Body.Contains("left"));
        Assert.True(Promises.For(world, f.Id).Single().AtRisk);

        f.Contract!.PursePerFight = new Money(500m);   // (so his live deal doesn't floor the quote)
        decimal before = AskingQuote.For(world, promo, f, Exp(world, f)).Purse.Amount;
        int patienceBefore = new NegotiationSession(world, f, promo, Exp(world, f)).Patience;

        world.CurrentDate = p.Deadline.AddDays(3);
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.Equal(PromiseState.Broken, p.State);
        Assert.Equal(50 - Promises.BrokenLoyalty, f.Loyalty);
        Assert.Contains(world.Mail, m => m.Subject == $"We broke our word to {f.FullName}");

        // The grievance machinery reads it.
        var g = Lifers.GrievanceOf(world, f, world.CurrentDate);
        Assert.NotNull(g);
        Assert.Equal(Lifers.GrievanceKind.BrokenPromise, g!.Kind);

        // The next table remembers: he asks more, has less patience, and a new promise is worth nothing.
        Assert.True(Promises.Distrust(world, f.Id, promo.Id, world.CurrentDate) > 1m);
        Assert.True(AskingQuote.For(world, promo, f, Exp(world, f)).Purse.Amount > before);
        var s = new NegotiationSession(world, f, promo, Exp(world, f));
        Assert.Equal(patienceBefore - 1, s.Patience);
        var plain = new ContractOffer { Fighter = f.Id, Promotion = promo.Id, Exclusive = Excl(promo) };
        var withPromise = new ContractOffer { Fighter = f.Id, Promotion = promo.Id, Exclusive = Excl(promo), PromiseFights = 2 };
        Assert.Equal(s.DemandPurse(plain), s.DemandPurse(withPromise));

        // A later promise kept mends the grievance.
        var kept = new Promise
        {
            Id = 99, Fighter = f.Id, Promotion = promo.Id, Kind = PromiseKind.Activity, Fights = 2,
            Made = world.CurrentDate, Deadline = world.CurrentDate.AddDays(365),
            State = PromiseState.Kept, Resolved = world.CurrentDate.AddDays(1),
        };
        world.Promises.Add(kept);
        Assert.Null(Promises.Grievance(world, f, promo.Id, world.CurrentDate.AddDays(2)));
    }

    [Fact]
    public void ATitleFightBookedBeforeTheDeadline_KeepsThePromiseOpenThroughTheGrace()
    {
        var (world, f, p) = Promised(PromiseKind.TitleShot);
        world.CurrentDate = p.Deadline.AddDays(-10);
        var card = Book(world, f, p.Deadline.AddDays(20), title: true);
        world.CurrentDate = p.Deadline.AddDays(5);
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.True(p.Live, "booked: being honoured");

        // The card is fought: kept.
        card.Status = EventStatus.Completed;
        foreach (var x in card.Card) x.Outcome = Win(x);
        world.CurrentDate = p.Deadline.AddDays(25);
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.Equal(PromiseState.Kept, p.State);
    }

    [Fact]
    public void AnInjuryAtTheDeadline_StopsTheClock()
    {
        var (world, f, p) = Promised(PromiseKind.TitleShot);
        var deadline = p.Deadline;
        world.CurrentDate = deadline.AddDays(2);
        f.CurrentInjury = new Injury
        {
            Severity = InjurySeverity.Moderate, Description = "broken hand",
            OccurredOn = deadline.AddDays(-30), ExpectedReturn = deadline.AddDays(40),
        };
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.True(p.Live);
        Assert.Equal(deadline.AddDays(40 + Promises.GraceDays), p.Deadline);
    }

    // --- Lifecycle edges -----------------------------------------------------------------------

    [Fact]
    public void HeRetires_ThePromiseFallsAway()
    {
        var (world, f, p) = Promised(PromiseKind.TitleShot);
        Lifecycle.Retire(world, f, world.CurrentDate);
        Assert.Equal(PromiseState.Void, p.State);
        Assert.Equal(0, Promises.BrokenBy(world, f.Id, Player(world).Id, world.CurrentDate));
    }

    [Fact]
    public void HeIsReleased_ThePromiseIsBroken_AndRemembered()
    {
        var (world, f, p) = Promised(PromiseKind.Activity, fights: 2);
        ContractLifecycle.Release(world, f);
        Assert.Equal(PromiseState.Broken, p.State);
        Assert.Contains(world.Mail, m => m.Subject == $"{f.FullName} left with our promise unkept");
        Assert.Equal(1, Promises.BrokenBy(world, f.Id, Player(world).Id, world.CurrentDate));
    }

    [Fact]
    public void ThePromotionFolds_ThePromiseFallsAway()
    {
        var (world, f, p) = Promised(PromiseKind.Activity, fights: 2);
        Player(world).Defunct = true;
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.Equal(PromiseState.Void, p.State);
    }

    [Fact]
    public void TheBeltIsVacated_AFightForTheVacantBeltStillKeepsIt()
    {
        var (world, f, p) = Promised(PromiseKind.TitleShot);
        var promo = Player(world);
        var title = world.Get(promo.Divisions.First(d => d.Matches(f)).Title!.Value)!;
        title.CurrentChampion = null;   // vacated
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.True(p.Live);
        Fought(world, f, title: true);
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.Equal(PromiseState.Kept, p.State);
    }

    [Fact]
    public void TheDealEndsBeforeTheDeadline_ThePromiseStandsIfHeReSigns_AndBreaksIfHeLeaves()
    {
        var (world, f, p) = Promised(PromiseKind.TitleShot);
        f.Contract!.FightsRemaining = 0;
        DealWindow.Open(world, f, world.CurrentDate);
        Assert.Contains(world.Mail, m => m.Subject.StartsWith($"{f.FullName}'s deal is up") && m.Body.Contains("a title shot"));
        Promises.RunMonthly(world, world.CurrentDate);
        Assert.True(p.Live, "in his window the promise still stands");

        // The window closes unsigned: he leaves, and the promise is broken.
        world.CurrentDate = f.Contract.WindowEndsOn!.Value;
        DealWindow.RunWeekly(world, world.CurrentDate);
        Assert.Null(f.Contract);
        Assert.Equal(PromiseState.Broken, p.State);
    }

    // --- Matching right -------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]   // a small, non-exclusive house: matching by his lights may already be cheaper
    [InlineData(true)]    // an exclusive national house: the rival's bigger stage pulls; the right keeps him anyway
    public void WithAMatchingRight_TheMatchIsTheRivalsTermsExactly_WithoutOne_ItMustBeatThemByHisLights(bool exclusiveHolder)
    {
        var world = FoundedWorld(48);
        if (exclusiveHolder) Player(world).Prestige = Core.Sim.Org.OrgSizing.PrestigeFor(OrgSize.National);
        var f = Star(world, loyalty: 20);
        f.Contract!.FightsRemaining = 1;
        var o = OfferMarket.Call(world, f, world.CurrentDate);
        Assert.NotNull(o);
        var exp = Exp(world, f);
        decimal theirs = OfferMarket.Score(world, f, OfferMarket.AsContractOffer(o!), exp);

        var without = OfferMarket.MatchTerms(world, o!);
        Assert.False(OfferMarket.HasMatchingRight(world, o!));
        Assert.True(OfferMarket.Score(world, f, without, exp) >= theirs);

        f.Contract.MatchingRight = true;
        Assert.True(OfferMarket.HasMatchingRight(world, o!));
        var with = OfferMarket.MatchTerms(world, o!);
        decimal live = Negotiator.LivePurseFloor(f, f.Contract.Promotion)?.Amount ?? 0m;
        Assert.Equal(Math.Min(without.PursePerFight.Amount, Math.Max(o!.Purse.Amount, NegotiationSession.UpTo500(live))), with.PursePerFight.Amount);
        Assert.Equal(o.WinBonus, with.WinBonus);
        Assert.Equal(o.SigningBonus, with.SigningBonus);
        Assert.Equal(o.Fights, with.Fights);
        Assert.True(with.PursePerFight.Amount <= without.PursePerFight.Amount);
        if (exclusiveHolder)
        {
            Assert.True(with.PursePerFight.Amount < without.PursePerFight.Amount, "the right is cheaper than beating the bigger stage");
            Assert.True(OfferMarket.Score(world, f, with, exp) < theirs, "he'd prefer the rival's paper, and stays anyway");
        }
        _out.WriteLine($"rival {o.Purse} · match without the right {without.PursePerFight} · with {with.PursePerFight} "
                       + $"(score theirs {theirs:0} · exact match {OfferMarket.Score(world, f, with, exp):0})");

        // The Desk says so, and matching signs the exact terms.
        OfferMarket.RefreshDesk(world);
        var desk = world.Decisions.Single(d => d.Kind == DecisionKind.RivalOffer && d.Fighter == f.Id);
        Assert.Contains("matching right", desk.Detail);
        Assert.Equal(with.PursePerFight, desk.Price);
        Assert.True(OfferMarket.Match(world, o, world.CurrentDate));
        Assert.Equal(with.PursePerFight, f.Contract!.PursePerFight);
        Assert.False(f.Contract.MatchingRight, "spent: the new deal carries one only if its slip did");
    }

    [Fact]
    public void AMatchingRight_IsSignedOnlyIntoThePlayersDeals()
    {
        var world = FoundedWorld(48);
        var ai = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion && !p.Defunct);
        var fa = world.FreeAgents().First(x => x.Status == FighterStatus.Active && !x.LocalTalent);
        var exp = Exp(world, fa);
        ContractLifecycle.Sign(world, fa, new ContractOffer
        {
            Fighter = fa.Id, Promotion = ai.Id, PursePerFight = exp.AskingPurse, Fights = exp.DesiredFights,
            MatchingRight = true, PromiseFights = 2, PromiseTitleShot = true,
        }, world.CurrentDate);
        Assert.False(fa.Contract!.MatchingRight);
        Assert.Empty(world.Promises);
    }

    // --- An all-AI world never sees any of it -----------------------------------------------------

    [Fact]
    public void AnAllAiWorld_NeverMakesAPromise_OrAMatchingRight()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        world.PlayerPromotion = default;
        new WorldTick().AdvanceWeeks(world, 30);
        Assert.Empty(world.Promises);
        Assert.DoesNotContain(world.Fighters.Values, f => f.Contract is { MatchingRight: true });
        Assert.DoesNotContain(world.Mail, m => m.Subject.Contains("promise", StringComparison.OrdinalIgnoreCase));
    }

    // --- Helpers ------------------------------------------------------------------------------

    private static World FoundedWorld(ulong seed) =>
        new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed,
            Founding = new WorldBuilder.FoundingSpec
            {
                Prestige = 20,
                StartingBalance = new Money(2_000_000m),
                Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight },
            },
        });

    private static Promotion Player(World world) => world.Get(world.PlayerPromotion)!;
    private static bool Excl(Promotion p) => Negotiator.WantsExclusivity(p);
    private static ContractExpectations Exp(World world, Fighter f) => new FighterValuation().Evaluate(world, f);

    private static Title Belt(World world, Promotion promo, Division div)
    {
        if (div.Title is { } id && world.Get(id) is { } have) return have;
        var t = new Title { Id = world.NewTitleId(), Promotion = promo.Id, WeightClass = div.WeightClass, Name = "Test Belt" };
        world.Titles[t.Id] = t;
        div.Title = t.Id;
        return t;
    }

    // A fighter of the player's from his home country (so a home show can be kept), signed
    // on a 4-fight deal carrying one promise, made today.
    private static (World World, Fighter F, Promise P) Promised(PromiseKind kind, int fights = 0)
    {
        var world = FoundedWorld(48);
        var promo = Player(world);
        var f = HireOne(world, homeShowPossible: kind == PromiseKind.HomeShow);
        var div = promo.Divisions.First(d => d.Matches(f));
        if (kind == PromiseKind.TitleShot) Belt(world, promo, div);
        var c = f.Contract!;
        ContractLifecycle.Sign(world, f, new ContractOffer
        {
            Fighter = f.Id, Promotion = promo.Id, PursePerFight = c.PursePerFight, WinBonus = c.WinBonus, Fights = 4,
            Exclusive = c.IsExclusive,
            PromiseTitleShot = kind == PromiseKind.TitleShot, PromiseHomeShow = kind == PromiseKind.HomeShow,
            PromiseFights = kind == PromiseKind.Activity ? fights : 0,
        }, world.CurrentDate);
        var p = Assert.Single(world.Promises);
        Assert.Equal(kind, p.Kind);
        return (world, f, p);
    }

    private static Fighter HireOne(World world, bool homeShowPossible = false)
    {
        var promo = Player(world);
        var pick = world.FreeAgents()
            .Where(x => x.Status == FighterStatus.Active && !x.LocalTalent && promo.Divisions.Any(d => d.Matches(x))
                        && (!homeShowPossible || world.Venues.Values.Any(v => world.Get(v.City)?.Country == x.Country
                                                                             && Core.Sim.Org.OrgReach.CanBookIn(world, promo, v.City))))
            .OrderBy(x => x.Id.Value).First();
        var exp = Exp(world, pick);
        ContractLifecycle.Sign(world, pick, new ContractOffer
        {
            Fighter = pick.Id, Promotion = promo.Id, PursePerFight = exp.AskingPurse, WinBonus = exp.AskingWinBonus,
            Fights = 4, Exclusive = Excl(promo),
        }, world.CurrentDate);
        return pick;
    }

    private static Fighter Star(World world, int loyalty)
    {
        var f = HireOne(world);
        foreach (var a in Raise(f)) a(72);
        f.Record.Wins = 14; f.Record.Losses = 2; f.Record.WinsByKo = 7; f.Record.WinsByDecision = 7;
        f.Record.CurrentStreak = 4;
        f.Intangibles.Popularity = 55;
        f.Disposition = Disposition.Warhorse;
        f.Loyalty = loyalty;
        return f;
    }

    // A card of the player's with him on it, a week from today (scheduled).
    private static Event Book(World world, Fighter f, DateOnly date, bool title = false, VenueId? venue = null)
    {
        var promo = Player(world);
        var ev = new Event
        {
            Id = world.NewEventId(), Promotion = promo.Id, Name = "Promise Night", Date = date,
            Status = EventStatus.Scheduled, VenueId = venue,
        };
        var opponent = world.Fighters.Values.First(x => x.Id != f.Id && x.Status == FighterStatus.Active);
        ev.Card.Add(new Fight
        {
            Id = world.NewFightId(), RedCorner = f.Id, BlueCorner = opponent.Id, WeightClass = f.WeightClass,
            IsTitleFight = title, Title = title ? promo.Divisions.First(d => d.Matches(f)).Title : null,
        });
        world.Events[ev.Id] = ev;
        return ev;
    }

    // He fights on a card of the player's, a week on (the clock moves to the card).
    private static void Fought(World world, Fighter f, bool title = false, VenueId? venue = null)
    {
        world.CurrentDate = world.CurrentDate.AddDays(7);
        var ev = Book(world, f, world.CurrentDate, title, venue);
        ev.Status = EventStatus.Completed;
        foreach (var x in ev.Card) x.Outcome = Win(x);
    }

    private static FightOutcome Win(Fight x) => new()
    {
        Winner = x.RedCorner, Loser = x.BlueCorner, Method = FightMethod.UnanimousDecision, EndRound = 3, EndTimeSeconds = 300,
    };

    private static IEnumerable<Action<int>> Raise(Fighter f) => new Action<int>[]
    {
        v => f.Striking.PunchPower = v, v => f.Striking.PunchTechnique = v, v => f.Striking.StrikingDefense = v,
        v => f.Striking.Footwork = v, v => f.Striking.KickTechnique = v, v => f.Grappling.TakedownOffense = v,
        v => f.Grappling.TakedownDefense = v, v => f.Grappling.TopControl = v, v => f.Grappling.Scrambling = v,
        v => f.Grappling.SubmissionOffense = v, v => f.Grappling.SubmissionDefense = v,
        v => f.Physical.Cardio = v, v => f.Physical.Speed = v, v => f.Physical.Chin = v, v => f.Physical.Strength = v,
        v => f.Mental.FightIQ = v, v => f.Mental.Composure = v, v => f.Mental.Heart = v,
    };
}
