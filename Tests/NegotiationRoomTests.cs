using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;

namespace MmaManager.Tests;

/// <summary>
/// The negotiating room: multi-round talks with an agent who opens at the ask,
/// concedes under good-faith bargaining, charges for the leash terms, and walks
/// out on lowballs — all deterministic, all on the AI's own pricing math.
/// </summary>
public class NegotiationRoomTests
{
    private static World Build(ulong seed = 5) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    private static (World World, Fighter Fighter, Promotion Promo, ContractExpectations Exp, NegotiationSession Session)
        OpenTalks(ulong seed = 5)
    {
        var world = Build(seed);
        var promo = world.Get(world.PlayerPromotion)!;
        var f = world.FreeAgents().First(x => x.Status == FighterStatus.Active);
        var exp = new FighterValuation().Evaluate(world, f);
        return (world, f, promo, exp, new NegotiationSession(world, f, promo, exp));
    }

    [Fact]
    public void MeetingTheirTerms_ClosesTheDealOnTheSpot()
    {
        var (_, _, _, _, session) = OpenTalks();
        var offer = session.TheirTermsAsOffer(exclusive: true, clause: false);

        var resp = session.Table(offer);

        Assert.Equal(TalkVerdict.Accept, resp.Verdict);
        Assert.Equal(TalkOutcome.Signed, session.Outcome);
    }

    [Fact]
    public void RepeatedLowballs_BurnTheRoom_AndTheAgentWalks()
    {
        var (_, f, _, _, session) = OpenTalks();
        var insult = session.TheirTermsAsOffer(exclusive: true, clause: false);
        insult.PursePerFight = new Money(Math.Max(500m, insult.PursePerFight.Amount * 0.2m));
        insult.WinBonus = Money.Zero;
        insult.SigningBonus = Money.Zero;

        TalkResponse resp;
        int rounds = 0;
        do { resp = session.Table(insult); rounds++; }
        while (resp.Verdict == TalkVerdict.Counter && rounds < 10);

        Assert.Equal(TalkVerdict.WalkOut, resp.Verdict);
        Assert.Equal(TalkOutcome.TheyWalked, session.Outcome);
        Assert.True(rounds <= 3, $"two insults should end the sit-down (took {rounds})");
        // A dead room stays dead.
        Assert.Equal(TalkVerdict.WalkOut, session.Table(insult).Verdict);
    }

    [Fact]
    public void GoodFaithBargaining_NeverMovesTheirNumberUp()
    {
        var (_, _, _, _, session) = OpenTalks();
        decimal before = session.DemandPurse(true, false).Amount;

        var bid = session.TheirTermsAsOffer(exclusive: true, clause: false);
        bid.PursePerFight = new Money(Math.Round(bid.PursePerFight.Amount * 0.85m / 500m) * 500m);
        var resp = session.Table(bid);

        decimal after = session.DemandPurse(true, false).Amount;
        Assert.True(after <= before, "a credible bid must never inflate their ask");
        Assert.NotEqual(TalkVerdict.WalkOut, resp.Verdict);   // one fair offer never ends the room
    }

    [Fact]
    public void TheLeashTerms_HaveAPrice()
    {
        var (_, _, _, _, session) = OpenTalks();

        // A champion's clause costs a premium; a non-exclusive deal runs cheaper.
        Assert.True(session.DemandPurse(true, true).Amount > session.DemandPurse(true, false).Amount);
        Assert.True(session.DemandPurse(false, false).Amount < session.DemandPurse(true, false).Amount);

        // So their exact money WITHOUT the clause priced in falls short WITH it.
        var offer = session.TheirTermsAsOffer(exclusive: true, clause: false);
        offer.ChampionsClause = true;
        Assert.NotEqual(TalkVerdict.Accept, session.Table(offer).Verdict);
    }

    [Fact]
    public void WalkingAwayYourself_LeavesTheDoorOpen()
    {
        var (_, _, _, _, session) = OpenTalks();
        session.WalkAway();
        Assert.Equal(TalkOutcome.YouWalked, session.Outcome);
    }

    [Fact]
    public void TheSameSitDown_ReplaysIdentically()
    {
        var a = OpenTalks(seed: 7);
        var b = OpenTalks(seed: 7);
        Assert.Equal(a.Session.OpeningLine, b.Session.OpeningLine);
        Assert.Equal(a.Session.AgentStance, b.Session.AgentStance);

        var bidA = a.Session.TheirTermsAsOffer(true, false);
        bidA.PursePerFight = new Money(bidA.PursePerFight.Amount * 0.8m);
        var bidB = b.Session.TheirTermsAsOffer(true, false);
        bidB.PursePerFight = new Money(bidB.PursePerFight.Amount * 0.8m);

        var ra = a.Session.Table(bidA);
        var rb = b.Session.Table(bidB);
        Assert.Equal(ra.Verdict, rb.Verdict);
        Assert.Equal(ra.Line, rb.Line);
        Assert.Equal(a.Session.DemandPurse(true, false).Amount, b.Session.DemandPurse(true, false).Amount);
    }
}
