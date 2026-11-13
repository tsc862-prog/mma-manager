using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using Xunit.Abstractions;

namespace MmaManager.Tests;

/// <summary>
/// Rival offers on the free agents the player courts (contracts stage 7): when an AI
/// promotion would sign a free agent the player has open talks with, it lodges the same
/// deal as an offer with a 10-14 day deadline instead; the table reads it (floor, opening
/// line, leaning, the one-model view); the owner can beat it at the table or put a final
/// offer in writing, which the rival may answer once; at the deadline the rival signs him.
/// </summary>
public class FreeAgentOfferTests
{
    private readonly ITestOutputHelper _out;
    public FreeAgentOfferTests(ITestOutputHelper output) => _out = output;

    // --- When an offer comes ----------------------------------------------------------

    [Fact]
    public void ACourtedFreeAgent_GetsAnOffer_InsteadOfBeingSignedOnTheSpot()
    {
        var (world, offers) = CourtedMarket(48);
        _out.WriteLine($"lodged {offers.Count}");
        Assert.Single(offers);   // at most one per courting promotion per monthly pass, however many it courts
        foreach (var o in offers)
        {
            var f = world.Get(o.Fighter)!;
            Assert.Equal(RivalOfferKind.FreeAgent, o.Kind);
            Assert.Equal(world.PlayerPromotion, o.Holder);   // the courting side
            Assert.NotEqual(world.PlayerPromotion, o.From);
            Assert.Null(f.Contract);                          // not signed on the spot
            Assert.Equal(RivalOfferState.Pending, o.State);
            int days = o.Deadline.DayNumber - o.Made.DayNumber;
            Assert.InRange(days, OfferMarket.FreeAgentMinDays, OfferMarket.FreeAgentMaxDays);
            Assert.True(o.Purse.Amount > 0 && o.Fights > 0);
            Assert.Contains(world.Mail, m => m.Category == MailCategory.FighterRequest && m.AboutFighter == f.Id
                                             && m.Subject == $"{f.FullName} has an offer from {world.Get(o.From)!.Abbreviation}");
        }
        // No Desk card for a free agent: the table is where it is answered.
        Assert.DoesNotContain(world.Decisions, d => d.Kind == DecisionKind.RivalOffer);
        // The rival holds his seat while the offer is live.
        var o0 = offers[0];
        var rival = world.Get(o0.From)!;
        var div = rival.Divisions.First(d => d.Matches(world.Get(o0.Fighter)!));
        Assert.Equal(offers.Count(o => o.From == rival.Id && div.Matches(world.Get(o.Fighter)!)), OfferMarket.SeatsHeld(world, rival.Id, div));
    }

    [Fact]
    public void AFreeAgentNobodyCourts_IsSignedOnTheSpot_AsBefore()
    {
        // The same world, the same pass, no talks: every signing happens and no offer is made.
        var world = FoundedWorld(48);
        int signed = FreeAgencyService.RunMonthly(world, world.CurrentDate);
        Assert.True(signed > 0);
        Assert.Empty(world.RivalOffers);

        // Talks with a promotion that is NOT under the player's rules change nothing.
        var w2 = FoundedWorld(48);
        var ai = w2.Promotions.Values.Where(p => p.Id != w2.PlayerPromotion && !p.Defunct).OrderBy(p => p.Id.Value).First();
        foreach (var fa in w2.FreeAgents().Where(x => x.Status == FighterStatus.Active).OrderBy(x => x.Id.Value))
            w2.Talks.Add(new TalksMemory { Fighter = fa.Id, Promotion = ai.Id, Round = 1, Mood = 60, Patience = 2, LastTalked = w2.CurrentDate });
        Assert.Equal(signed, FreeAgencyService.RunMonthly(w2, w2.CurrentDate));
        Assert.Empty(w2.RivalOffers);
    }

    [Fact]
    public void AWalkOut_IsNotCourting_AndColdTalksAreNotCourting()
    {
        var world = FoundedWorld(48);
        var promo = world.Get(world.PlayerPromotion)!;
        var fa = world.FreeAgents().First(x => x.Status == FighterStatus.Active);
        world.Talks.Add(new TalksMemory
        {
            Fighter = fa.Id, Promotion = promo.Id, Round = 2, Mood = 10, Patience = 0,
            SouredUntil = world.CurrentDate.AddDays(21), LastTalked = world.CurrentDate,
        });
        Assert.Null(OfferMarket.CourtedBy(world, fa, world.CurrentDate));
        world.Talks.Clear();
        world.Talks.Add(new TalksMemory { Fighter = fa.Id, Promotion = promo.Id, Round = 1, LastTalked = world.CurrentDate.AddDays(-61) });
        Assert.Null(OfferMarket.CourtedBy(world, fa, world.CurrentDate));
        world.Talks.Clear();
        world.Talks.Add(new TalksMemory { Fighter = fa.Id, Promotion = promo.Id, Round = 1, LastTalked = world.CurrentDate.AddDays(-30) });
        Assert.Equal(promo.Id, OfferMarket.CourtedBy(world, fa, world.CurrentDate));
    }

    [Fact]
    public void TheYearlyCap_CoversBothKindsTogether()
    {
        var world = FoundedWorld(48);
        // Five end-of-deal offers already this year: no free-agent offer, the AI signs as before.
        for (int i = 0; i < OfferMarket.MaxPerYear; i++) world.RivalOffers.Add(PastOffer(world, i + 1, RivalOfferKind.EndOfDeal, 30));
        CourtEveryone(world);
        FreeAgencyService.RunMonthly(world, world.CurrentDate);
        Assert.DoesNotContain(world.RivalOffers, o => o.Kind == RivalOfferKind.FreeAgent);
    }

    [Fact]
    public void FreeAgentOffers_HaveTheirOwnCapOfTwoAYear_AndOnePerPass()
    {
        var world = FoundedWorld(48);
        var tick = new WorldTick();
        for (int m = 0; m < 13; m++)
        {
            CourtEveryone(world);
            int before = world.RivalOffers.Count(o => o.Kind == RivalOfferKind.FreeAgent);
            for (int w = 0; w < 4; w++) tick.AdvanceWeek(world);
            Assert.True(world.RivalOffers.Count(o => o.Kind == RivalOfferKind.FreeAgent) - before <= 1, "one per monthly pass");
        }
        var fa = world.RivalOffers.Where(o => o.Kind == RivalOfferKind.FreeAgent).Select(o => o.Made).OrderBy(d => d).ToList();
        _out.WriteLine($"free-agent offers in a year of courting: {fa.Count}");
        Assert.NotEmpty(fa);
        Assert.True(fa.All(d => fa.Count(x => x >= d && x.DayNumber - d.DayNumber < 365) <= OfferMarket.FreeAgentMaxPerYear));
    }

    [Fact]
    public void WithTwoFreeAgentOffersUsed_EndOfDealOffersStillCome_ThreeMore()
    {
        var world = FoundedWorld(48);
        world.RivalOffers.Add(PastOffer(world, 1, RivalOfferKind.FreeAgent, 40));
        world.RivalOffers.Add(PastOffer(world, 2, RivalOfferKind.FreeAgent, 20));
        CourtEveryone(world);
        FreeAgencyService.RunMonthly(world, world.CurrentDate);
        Assert.Equal(2, world.RivalOffers.Count(o => o.Kind == RivalOfferKind.FreeAgent));   // the free-agent cap is spent

        // Stars on their last fight: end-of-deal calls keep coming until the total of five.
        var promo = world.Get(world.PlayerPromotion)!;
        int made = 0;
        foreach (var f in world.FreeAgents().Where(x => x.Status == FighterStatus.Active && promo.Divisions.Any(d => d.Matches(x)))
                     .OrderBy(x => x.Id.Value).Take(40).ToList())
        {
            ContractLifecycle.Sign(world, f, new ContractOffer
            {
                Fighter = f.Id, Promotion = promo.Id, PursePerFight = new Money(5_000m), WinBonus = new Money(5_000m), Fights = 1,
            }, world.CurrentDate);
            foreach (var a in Raise(f)) a(72);
            f.Record.Wins = 14; f.Record.Losses = 2; f.Record.CurrentStreak = 4; f.Intangibles.Popularity = 55; f.Loyalty = 10;
            f.Disposition = Disposition.Warhorse;
            if (OfferMarket.OffersThisYear(world, promo.Id, world.CurrentDate) < OfferMarket.MaxPerYear
                && OfferMarket.Call(world, f, world.CurrentDate) is not null) made++;
        }
        Assert.Equal(OfferMarket.MaxPerYear - 2, made);
        Assert.Equal(OfferMarket.MaxPerYear, OfferMarket.OffersThisYear(world, promo.Id, world.CurrentDate));
    }

    [Fact]
    public void ARivalThatCouldNotReallySignHim_DoesNotLodgeAPhantomOffer()
    {
        // The would-be rival and its terms from a real pass; then, in a fresh copy of that
        // world, the same rival in the red, without room, or without the signing bonus:
        // no offer (the pass signs on the spot exactly as main does).
        var (seen, offers) = CourtedMarket(48);
        var o = offers.Single();
        var terms = OfferMarket.AsContractOffer(o);
        terms.SigningBonus = new Money(10_000m);

        World Fresh(out Promotion rival, out Fighter f)
        {
            var w = FoundedWorld(48);
            CourtEveryone(w);
            rival = w.Get(o.From)!;
            f = w.Get(o.Fighter)!;
            rival.Balance = new Money(1_000_000m);
            return w;
        }

        var ok = Fresh(out var r0, out var f0);
        Assert.True(OfferMarket.TryLodgeFreeAgent(ok, r0, f0, terms, ok.CurrentDate), "precondition: a solvent rival with room lodges");

        var red = Fresh(out var r1, out var f1);
        r1.Balance = new Money(-1m);
        Assert.True(MmaManager.Core.Sim.Org.Solvency.Distress(r1) > 0);
        Assert.False(OfferMarket.TryLodgeFreeAgent(red, r1, f1, terms, red.CurrentDate));

        var broke = Fresh(out var r2, out var f2);
        r2.Balance = new Money(5_000m);   // solvent, but not the $10,000 bonus
        Assert.False(OfferMarket.TryLodgeFreeAgent(broke, r2, f2, terms, broke.CurrentDate));

        var full = Fresh(out var r3, out var f3);
        f3.Intangibles.Popularity = 0;   // no draw to squeeze in
        var div = r3.Divisions.First(d => d.Matches(f3));
        foreach (var x in full.FreeAgents().Where(x => x.Status == FighterStatus.Active && div.Matches(x) && x.Id != f3.Id).Take(60).ToList())
            ContractLifecycle.Sign(full, x, new ContractOffer { Fighter = x.Id, Promotion = r3.Id, PursePerFight = new Money(1_000m), Fights = 2 }, full.CurrentDate);
        Assert.False(PoachingService.HasRoom(full, r3, f3), "precondition: the division is full");
        Assert.False(OfferMarket.TryLodgeFreeAgent(full, r3, f3, terms, full.CurrentDate));

        Assert.Empty(red.RivalOffers);
        Assert.Empty(broke.RivalOffers);
        Assert.Empty(full.RivalOffers);
    }

    private static RivalOffer PastOffer(World world, int id, RivalOfferKind kind, int daysAgo) => new()
    {
        Id = id, Fighter = new FighterId(900_000 + id), From = world.Promotions.Keys.First(),
        Holder = world.PlayerPromotion, Made = world.CurrentDate.AddDays(-daysAgo), Kind = kind,
        State = RivalOfferState.Withdrawn, Resolved = world.CurrentDate.AddDays(-daysAgo + 10),
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

    // --- The deadline: the rival signs him -----------------------------------------------

    [Fact]
    public void PastTheDeadline_TheRivalSignsHim_OnTheLodgedTerms_ChargedOnce_WithOneHeadline()
    {
        var (world, o, f) = OneOffer(48);
        var rival = world.Get(o.From)!;
        rival.Balance = new Money(Math.Max(rival.Balance.Amount, 2_000_000m));
        foreach (var a in Raise(f)) a(72);   // a name the press covers (signings under that go unreported)
        int bonusLines = rival.Ledger.Count(l => l.Category == FinanceCategory.SigningBonus && l.Description.Contains(f.FullName));
        int news = world.News.Count(n => n.Category == NewsCategory.Signing && n.Headline.Contains(f.FullName));
        int playerLines = world.Get(world.PlayerPromotion)!.Ledger.Count;

        // A week in: the final week of a 10-14 day deadline — the clock stops, once.
        var stop = OfferMarket.RunWeekly(world, o.Made.AddDays(7));
        Assert.Same(o, stop);
        Assert.Contains(world.Mail, m => m.Subject == $"Last week to beat {rival.Abbreviation} for {f.FullName}");
        Assert.Null(f.Contract);

        // Past the deadline he signs the rival's paper.
        Assert.Null(OfferMarket.RunWeekly(world, o.Made.AddDays(14 + 1)));
        Assert.Equal(RivalOfferState.Signed, o.State);
        Assert.Equal(rival.Id, f.Contract!.Promotion);
        Assert.Equal(o.Purse, f.Contract.PursePerFight);
        Assert.Equal(o.Fights, f.Contract.FightsRemaining);
        Assert.Equal(o.SigningBonus.Amount > 0 ? 1 : 0,
            rival.Ledger.Count(l => l.Category == FinanceCategory.SigningBonus && l.Description.Contains(f.FullName)) - bonusLines);
        Assert.Equal(1, world.News.Count(n => n.Category == NewsCategory.Signing && n.Headline.Contains(f.FullName)) - news);
        Assert.Equal(playerLines, world.Get(world.PlayerPromotion)!.Ledger.Count);
        Assert.Contains(world.Mail, m => m.Subject == $"{f.FullName} signed with {rival.Abbreviation}");

        // It never resolves twice.
        var resolved = o.Resolved;
        for (int w = 3; w < 8; w++) OfferMarket.RunWeekly(world, o.Made.AddDays(7 * w));
        Assert.Equal(RivalOfferState.Signed, o.State);
        Assert.Equal(resolved, o.Resolved);
        Assert.Equal(rival.Id, f.Contract!.Promotion);
    }

    [Fact]
    public void IfTheRivalCanNoLongerPay_ItsOfferFallsAway_AndHeStaysOnTheMarket()
    {
        var (world, o, f) = OneOffer(48);
        world.Get(o.From)!.Balance = new Money(-1m);   // in the red
        OfferMarket.RunWeekly(world, o.Deadline.AddDays(1));
        Assert.Equal(RivalOfferState.Withdrawn, o.State);
        Assert.Null(f.Contract);
        Assert.Contains(world.Mail, m => m.Subject == $"{world.Get(o.From)!.Abbreviation} pulled its offer for {f.FullName}");
    }

    // --- The table reads it -----------------------------------------------------------------

    [Fact]
    public void TheTable_OpensWithTheRivalsPaper_AndWontSignUnderIt()
    {
        var (world, o, f) = OneOffer(48);
        var promo = world.Get(world.PlayerPromotion)!;
        var rival = world.Get(o.From)!;
        bool excl = Negotiator.WantsExclusivity(promo);
        var exp = new FighterValuation().Evaluate(world, f);
        decimal theirs = OfferMarket.Score(world, f, OfferMarket.AsContractOffer(o), exp);

        var room = NegotiationSession.Resume(world, f, promo, exp);
        Assert.Contains(rival.Abbreviation, room.OpeningLine);
        Assert.Contains(o.Purse.ToString(), room.OpeningLine);
        Assert.Same(o, room.RivalOffer);
        Assert.True(OfferMarket.Score(world, f, room.TheirTermsAsOffer(excl, false), exp) >= theirs, "his number beats the rival's paper");

        // Patience: with paper in his pocket he hears you out one round longer (a fresh
        // sit-down; resumed talks keep the patience they had left).
        var plain = new NegotiationSession(world, f, promo, exp);
        var memory = world.Talks.Single(t => t.Fighter == f.Id && t.Promotion == promo.Id);
        Assert.Equal(memory.Patience, room.Patience);
        world.Talks.Remove(memory);
        Assert.Equal(plain.Patience + 1, NegotiationSession.Resume(world, f, promo, exp).Patience);
        world.Talks.Add(memory);


        // An offer worth less to him than the rival's is refused, whatever else it does.
        var under = OfferMarket.MatchTerms(world, o);
        while (under.PursePerFight.Amount > 500m && OfferMarket.Score(world, f, under, exp) >= theirs)
            under.PursePerFight = new Money(under.PursePerFight.Amount - 500m);
        Assert.True(OfferMarket.Score(world, f, under, exp) < theirs);
        Assert.NotEqual(TalkVerdict.Accept, room.Table(under).Verdict);

        // The one read model: his position, reasons, the disclosed slip, his leaning — nothing hidden.
        var view = room.View(excl, false);
        Assert.NotNull(view.Rival);
        Assert.Equal(rival.Abbreviation, view.Rival!.Abbreviation);
        Assert.Equal(o.Purse, view.Rival.Purse);
        Assert.Equal(o.Deadline, view.Rival.Deadline);
        Assert.Equal(RivalOfferKind.FreeAgent, view.Rival.Kind);
        Assert.NotNull(view.Leaning);
        Assert.False(string.IsNullOrWhiteSpace(view.Leaning!.Words));
        Assert.Contains(view.Reasons, r => r.Contains(rival.Abbreviation));
        Assert.NotNull(view.Last);
        Assert.Equal(room.TheirTermsAsOffer(excl, false).PursePerFight, view.Position.PursePerFight);

        // Meeting his terms signs him: the offer is beaten, and settled once.
        var again = NegotiationSession.Resume(world, f, promo, exp);
        var full = again.TheirTermsAsOffer(excl, false);
        Assert.Equal(TalkVerdict.Accept, again.Table(full).Verdict);
        again.Remember(world.CurrentDate);
        ContractLifecycle.Sign(world, f, full, world.CurrentDate);
        Assert.Equal(RivalOfferState.Beaten, o.State);
        Assert.Null(OfferMarket.LiveFor(world, f.Id));
        OfferMarket.RunWeekly(world, o.Deadline.AddDays(1));
        Assert.Equal(promo.Id, f.Contract!.Promotion);
        Assert.Equal(RivalOfferState.Beaten, o.State);
    }

    [Fact]
    public void TheTablesNumbers_DoNotMoveWithTrueAbility()
    {
        var (world, o, f) = OneOffer(48);
        var promo = world.Get(world.PlayerPromotion)!;
        bool excl = Negotiator.WantsExclusivity(promo);
        var before = NegotiationSession.Resume(world, f, promo, new FighterValuation().Evaluate(world, f)).View(excl, false);
        var lean = OfferMarket.LeaningOf(world, o);
        f.PotentialAbility = 99;
        f.Mental.FightIQ = 99; f.Physical.Cardio = 99; f.Striking.PunchPower = 99;
        var after = NegotiationSession.Resume(world, f, promo, new FighterValuation().Evaluate(world, f)).View(excl, false);
        Assert.Equal(lean, OfferMarket.LeaningOf(world, o));
        Assert.Equal(before.Rival, after.Rival);
        Assert.Equal(before.Leaning, after.Leaning);
    }

    // --- Put it in writing --------------------------------------------------------------------

    // Seed 40's rival is minded to come back (its keyed roll), seed 48's is not: both could afford to.
    private const ulong ComesBackSeed = 40, FoldsSeed = 48;

    [Fact]
    public void AFinalOffer_ARivalMindedToComeBack_RaisesOnce_WithinItsBudget_ThenHeChoosesOurs()
    {
        var (world, o, f) = OneOffer(ComesBackSeed);
        Assert.True(OfferMarket.WouldComeBack(world, o), "precondition: this rival chooses to come back");
        var promo = world.Get(world.PlayerPromotion)!;
        promo.Balance = new Money(5_000_000m);
        var rival = world.Get(o.From)!;
        rival.Balance = new Money(Math.Max(rival.Balance.Amount, 2_000_000m));
        var exp = new FighterValuation().Evaluate(world, f);
        decimal before = OfferMarket.Score(world, f, OfferMarket.AsContractOffer(o), exp);

        var ours = JustBeating(world, o);
        Assert.Equal(FinalOfferResult.Lodged, OfferMarket.LodgeFinal(world, f, promo, ours));
        var stop = OfferMarket.RunWeekly(world, o.Made.AddDays(7));

        Assert.Equal(1, o.Improvements);
        Assert.Same(o, stop);   // "They came back": the owner must answer now
        Assert.Null(o.Final);
        Assert.Null(f.Contract);
        decimal after = OfferMarket.Score(world, f, OfferMarket.AsContractOffer(o), exp);
        Assert.True(after > before);
        Assert.True(after >= OfferMarket.FinalScore(world, f, ours, exp) * 1.05m);
        Assert.True(o.Purse.Amount <= Math.Max(OfferMarket.CeilingPurse(world, rival, o, exp), Negotiator.PurseCap(world, rival)),
            "never past its own purse cap");
        Assert.Contains(world.Mail, m => m.Subject == $"{rival.Abbreviation} came back for {f.FullName}");
        Assert.True(o.Deadline >= o.Made.AddDays(7 + OfferMarket.FinalWeekDays));

        // Our old offer no longer beats it; beat the new paper and he signs with us: no second raise.
        Assert.Equal(FinalOfferResult.NotEnough, OfferMarket.LodgeFinal(world, f, promo, ours));
        var better = JustBeating(world, o);
        Assert.Equal(FinalOfferResult.Lodged, OfferMarket.LodgeFinal(world, f, promo, better));
        OfferMarket.RunWeekly(world, o.Made.AddDays(14));
        Assert.Equal(1, o.Improvements);
        Assert.Equal(RivalOfferState.Beaten, o.State);
        Assert.Equal(promo.Id, f.Contract!.Promotion);
        Assert.Equal(better.PursePerFight, f.Contract.PursePerFight);
        Assert.Contains(world.Mail, m => m.Subject == $"{f.FullName} is ours");
    }

    [Fact]
    public void AFinalOffer_ARivalNotMindedToComeBack_Folds_EvenThoughItCouldAffordTo()
    {
        var (world, o, f) = OneOffer(FoldsSeed);
        Assert.False(OfferMarket.WouldComeBack(world, o), "precondition: this rival does not choose to come back");
        var promo = world.Get(world.PlayerPromotion)!;
        promo.Balance = new Money(5_000_000m);
        var rival = world.Get(o.From)!;
        rival.Balance = new Money(Math.Max(rival.Balance.Amount, 2_000_000m));
        var exp = new FighterValuation().Evaluate(world, f);
        var ours = JustBeating(world, o);
        var probe = new RivalOffer
        {
            Id = 0, Fighter = o.Fighter, From = o.From, Holder = o.Holder, Made = o.Made,
            Purse = o.Purse, WinBonus = o.WinBonus, SigningBonus = o.SigningBonus, Fights = o.Fights, Exclusive = o.Exclusive,
        };
        Assert.True(OfferMarket.Improve(world, probe, f, rival, OfferMarket.FinalScore(world, f, ours, exp) * 1.05m, exp),
            "precondition: it could have come back");

        Assert.Equal(FinalOfferResult.Lodged, OfferMarket.LodgeFinal(world, f, promo, ours));
        Assert.Null(OfferMarket.RunWeekly(world, o.Made.AddDays(7)));
        Assert.Equal(0, o.Improvements);
        Assert.Equal(RivalOfferState.Beaten, o.State);
        Assert.Equal(promo.Id, f.Contract!.Promotion);
        Assert.Equal(ours.PursePerFight, f.Contract.PursePerFight);
    }

    [Fact]
    public void AFinalOffer_WithAChampionsClause_MustBeatThePaperByTheClausesPrice()
    {
        var (world, o, f) = OneOffer(48);
        var promo = world.Get(world.PlayerPromotion)!;
        promo.Balance = new Money(5_000_000m);
        var exp = new FighterValuation().Evaluate(world, f);
        decimal theirs = OfferMarket.Score(world, f, OfferMarket.AsContractOffer(o), exp);
        var ours = JustBeating(world, o);
        Assert.True(OfferMarket.Score(world, f, ours, exp) < theirs * Negotiator.ChampionsClausePremium, "precondition: only just beats it");
        ours.ChampionsClause = true;
        Assert.Equal(FinalOfferResult.NotEnough, OfferMarket.LodgeFinal(world, f, promo, ours));
        ours.ChampionsClause = false;
        Assert.Equal(FinalOfferResult.Lodged, OfferMarket.LodgeFinal(world, f, promo, ours));
    }

    [Fact]
    public void AWalkOut_CancelsAFinalOfferInWriting_AndTheRivalSignsHim()
    {
        var (world, o, f) = OneOffer(ComesBackSeed);
        var promo = world.Get(world.PlayerPromotion)!;
        promo.Balance = new Money(5_000_000m);
        world.Get(o.From)!.Balance = new Money(2_000_000m);
        Assert.Equal(FinalOfferResult.Lodged, OfferMarket.LodgeFinal(world, f, promo, JustBeating(world, o)));
        world.Talks.Single(x => x.Fighter == f.Id && x.Promotion == promo.Id).SouredUntil = world.CurrentDate.AddDays(21);

        OfferMarket.RunWeekly(world, o.Made.AddDays(7));
        Assert.Null(o.Final);
        Assert.Equal(0, o.Improvements);
        Assert.Equal(RivalOfferState.Signed, o.State);
        Assert.Equal(o.From, f.Contract!.Promotion);
    }

    [Fact]
    public void AFinalOfferPastTheRivalsWalkAway_MakesItFold()
    {
        var (world, o, f) = OneOffer(ComesBackSeed);   // minded to come back, but it can't reach this
        var promo = world.Get(world.PlayerPromotion)!;
        promo.Balance = new Money(5_000_000m);
        var exp = new FighterValuation().Evaluate(world, f);
        var ours = OfferMarket.MatchTerms(world, o);
        ours.PursePerFight = new Money(OfferMarket.CeilingPurse(world, world.Get(o.From)!, o, exp) * 2m + 10_000m);
        Assert.Equal(FinalOfferResult.Lodged, OfferMarket.LodgeFinal(world, f, promo, ours));
        OfferMarket.RunWeekly(world, o.Made.AddDays(7));
        Assert.Equal(0, o.Improvements);
        Assert.Equal(RivalOfferState.Beaten, o.State);
        Assert.Equal(promo.Id, f.Contract!.Promotion);
        Assert.Equal(ours.PursePerFight, f.Contract.PursePerFight);
        // The signing bonus is the player's, charged once.
        Assert.Equal(ours.SigningBonus.Amount > 0 ? 1 : 0,
            promo.Ledger.Count(l => l.Category == FinanceCategory.SigningBonus && l.Description.Contains(f.FullName)));
    }

    [Fact]
    public void AFinalOffer_ThatDoesNotBeatTheirPaper_OrIsNoRealDeal_IsNotLodged()
    {
        var (world, o, f) = OneOffer(48);
        var promo = world.Get(world.PlayerPromotion)!;
        var exp = new FighterValuation().Evaluate(world, f);
        var low = OfferMarket.MatchTerms(world, o);
        decimal theirs = OfferMarket.Score(world, f, OfferMarket.AsContractOffer(o), exp);
        while (low.PursePerFight.Amount > 500m && OfferMarket.Score(world, f, low, exp) >= theirs)
            low.PursePerFight = new Money(low.PursePerFight.Amount - 500m);
        Assert.Equal(FinalOfferResult.NotEnough, OfferMarket.LodgeFinal(world, f, promo, low));
        Assert.Null(o.Final);

        var room = NegotiationSession.Resume(world, f, promo, exp);
        var tooShort = OfferMarket.MatchTerms(world, o);
        tooShort.Fights = exp.DesiredFights + 3;
        Assert.Equal(FinalOfferResult.OffTerms, room.PutInWriting(tooShort));

        var good = room.TheirTermsAsOffer(Negotiator.WantsExclusivity(promo), false);
        promo.Balance = new Money(good.SigningBonus.Amount - 1m);
        if (good.SigningBonus.Amount > 0) Assert.Equal(FinalOfferResult.CantAfford, room.PutInWriting(good));
        promo.Balance = new Money(5_000_000m);
        Assert.Equal(FinalOfferResult.Lodged, room.PutInWriting(good));
        Assert.NotNull(room.View(Negotiator.WantsExclusivity(promo), false).Final);
    }

    [Fact]
    public void TheCourtingDiscoverer_KeepsHisGratitude_WhenARivalsPaperIsOnTheTable()
    {
        var (world, o, f) = OneOffer(48);
        var promo = world.Get(world.PlayerPromotion)!;
        bool excl = Negotiator.WantsExclusivity(promo);
        f.Record.Wins = 3; f.Record.Losses = 1;   // young career: the discoverer's discount applies
        var exp = new FighterValuation().Evaluate(world, f);
        var room = NegotiationSession.Resume(world, f, promo, exp);
        var deal = room.TheirTermsAsOffer(excl, false);
        decimal plain = OfferMarket.Score(world, f, deal, exp);

        f.DiscoveredBy = promo.Id;
        decimal grateful = OfferMarket.Score(world, f, deal, exp);
        Assert.Equal(plain / Negotiator.TableDiscount(world, f, promo.Id), grateful);   // a dollar from the house that found him counts for more
        Assert.True(Negotiator.TableDiscount(world, f, promo.Id) < 1m);
        // ...and his number at our table is the lower for it, not pushed back up to the rival's.
        var thankful = NegotiationSession.Resume(world, f, promo, exp).TheirTermsAsOffer(excl, false);
        Assert.True(thankful.PursePerFight.Amount < deal.PursePerFight.Amount,
            $"{thankful.PursePerFight} vs {deal.PursePerFight}");
    }

    // --- Lifecycle edges --------------------------------------------------------------------------

    [Fact]
    public void WhenTheRivalFolds_OrIsBought_TheOfferDies_AndTheTableStopsReadingIt()
    {
        var (world, o, f) = OneOffer(48);
        var promo = world.Get(world.PlayerPromotion)!;
        world.Get(o.From)!.Defunct = true;   // folded, or bought (an acquired org is defunct)
        var exp = new FighterValuation().Evaluate(world, f);

        // Before the tick: a dead rival's paper is not on the table.
        var room = NegotiationSession.Resume(world, f, promo, exp);
        Assert.Null(room.RivalOffer);
        Assert.Null(room.View(true, false).Rival);
        Assert.Equal(FinalOfferResult.NoOffer, OfferMarket.LodgeFinal(world, f, promo, OfferMarket.MatchTerms(world, o)));

        OfferMarket.RunWeekly(world, o.Made.AddDays(7));
        Assert.Equal(RivalOfferState.Withdrawn, o.State);
        Assert.Null(f.Contract);
        Assert.Contains(world.Mail, m => m.Subject == $"{world.Get(o.From)!.Abbreviation}'s offer for {f.FullName} is off");
    }

    [Fact]
    public void HeRetires_TheOfferDiesAtOnce()
    {
        var (world, o, f) = OneOffer(48);
        Lifecycle.Retire(world, f, world.CurrentDate);
        Assert.Equal(RivalOfferState.Withdrawn, o.State);
        Assert.Equal(world.CurrentDate, o.Resolved);
        Assert.Null(OfferMarket.LiveFor(world, f.Id));
    }

    [Fact]
    public void HeSignsElsewhere_ByAnotherDoor_TheOfferIsWithdrawn_AndNeverActs()
    {
        var (world, o, f) = OneOffer(48);
        var third = world.Promotions.Values.Where(p => !p.Defunct && p.Id != o.From && p.Id != world.PlayerPromotion)
            .OrderBy(p => p.Id.Value).First();
        var exp = new FighterValuation().Evaluate(world, f);
        // (a bidding war, a startup's founding class, the local circuit, a short-notice save: all sign through ContractLifecycle.Sign)
        ContractLifecycle.Sign(world, f, new Negotiator().AiOpeningOffer(world, third, f, exp), world.CurrentDate);
        Assert.Equal(RivalOfferState.Withdrawn, o.State);
        OfferMarket.RunWeekly(world, o.Deadline.AddDays(1));
        Assert.Equal(third.Id, f.Contract!.Promotion);
        Assert.Equal(RivalOfferState.Withdrawn, o.State);
    }

    [Fact]
    public void TheRivalSigningHimByAnotherDoor_CountsAsSigned()
    {
        var (world, o, f) = OneOffer(48);
        var rival = world.Get(o.From)!;
        ContractLifecycle.Sign(world, f, OfferMarket.AsContractOffer(o), world.CurrentDate);
        Assert.Equal(RivalOfferState.Signed, o.State);
    }

    [Fact]
    public void HeWalksOutOfOurTalks_TheRivalSignsHimOnTheNextTick()
    {
        var (world, o, f) = OneOffer(48);
        world.Get(o.From)!.Balance = new Money(2_000_000m);
        var t = world.Talks.Single(x => x.Fighter == f.Id && x.Promotion == world.PlayerPromotion);
        t.SouredUntil = world.CurrentDate.AddDays(21);
        OfferMarket.RunWeekly(world, o.Made.AddDays(7));
        Assert.Equal(RivalOfferState.Signed, o.State);
        Assert.Equal(o.From, f.Contract!.Promotion);
    }

    [Fact]
    public void LettingHimGo_TheRivalSignsHimOnTheNextTick()
    {
        var (world, o, f) = OneOffer(48);
        world.Get(o.From)!.Balance = new Money(2_000_000m);
        OfferMarket.LetGo(world, o, world.CurrentDate);
        Assert.Equal(RivalOfferState.LetGo, o.State);
        OfferMarket.RunWeekly(world, o.Made.AddDays(7));
        Assert.Equal(RivalOfferState.Signed, o.State);
        Assert.Equal(o.From, f.Contract!.Promotion);
    }

    [Fact]
    public void TheQuietMarket_LeavesAFreeAgentWithALiveOfferAlone()
    {
        var (world, o, f) = OneOffer(48);
        // Next month's pass: nobody signs him over the offer, and no second offer is lodged.
        FreeAgencyService.RunMonthly(world, world.CurrentDate.AddDays(3));
        Assert.Null(f.Contract);
        Assert.Single(world.RivalOffers, x => x.Fighter == f.Id);
    }

    [Fact]
    public void TheWorldTick_LodgesAndResolvesFreeAgentOffers_EndToEnd()
    {
        var world = FoundedWorld(48);
        var tick = new WorldTick();
        // Court the market each month, as an owner shopping would; advance; offers come and go.
        int stops = 0;
        for (int m = 0; m < 6; m++)
        {
            CourtEveryone(world);
            for (int w = 0; w < 4; w++) if (tick.AdvanceWeek(world).OfferNeedsYou is { Kind: RivalOfferKind.FreeAgent }) stops++;
        }
        var fa = world.RivalOffers.Where(o => o.Kind == RivalOfferKind.FreeAgent).ToList();
        _out.WriteLine($"free-agent offers {fa.Count}: " + string.Join(", ", fa.Select(o => o.State)) + $" · stops {stops}");
        Assert.NotEmpty(fa);
        Assert.True(world.RivalOffers.Count(o => o.Made > world.CurrentDate.AddDays(-365)) <= OfferMarket.MaxPerYear);
        Assert.All(fa.Where(o => !o.Live), o => Assert.NotNull(o.Resolved));
        Assert.Contains(fa, o => o.State == RivalOfferState.Signed);
        foreach (var o in fa.Where(o => o.State == RivalOfferState.Signed))
            Assert.True(world.Get(o.Fighter)!.Contract is null || world.Get(o.Fighter)!.Status == FighterStatus.Retired
                        || world.Get(o.Fighter)!.Contract!.SignedOn >= o.Made);
    }

    // The owner's offer on the rival's structure at the lowest purse whose final score beats its paper.
    internal static ContractOffer JustBeating(World world, RivalOffer o)
    {
        var f = world.Get(o.Fighter)!;
        var exp = new FighterValuation().Evaluate(world, f);
        decimal theirs = OfferMarket.Score(world, f, OfferMarket.AsContractOffer(o), exp);
        var ours = OfferMarket.MatchTerms(world, o);
        while (ours.PursePerFight.Amount > 500m && OfferMarket.FinalScore(world, f, ours, exp) >= theirs)
            ours.PursePerFight = new Money(ours.PursePerFight.Amount - 500m);
        while (OfferMarket.FinalScore(world, f, ours, exp) < theirs)
            ours.PursePerFight = new Money(ours.PursePerFight.Amount + 500m);
        return ours;
    }

    // --- Letting him go, and how his leaning reads (lead review, 2026-11-13) -----------------------

    [Fact]
    public void LettingACourtedFreeAgentGo_TellsTheOwner()
    {
        var (world, o, f) = OneOffer(48);
        int mail = world.Mail.Count;
        OfferMarket.LetGo(world, o, world.CurrentDate);
        Assert.Equal(RivalOfferState.LetGo, o.State);
        var abbr = world.Get(o.From)!.Abbreviation;
        var letter = Assert.Single(world.Mail.Skip(mail));
        Assert.Equal($"We let {f.FullName} go to {abbr}", letter.Subject);
        Assert.Equal(f.Id, letter.AboutFighter);
        Assert.Contains("when the week turns", letter.Body);
    }

    [Fact]
    public void AFreeAgentsLeaning_NeverCallsTheCourtingPromotionHisHouse()
    {
        var (world, all) = CourtedMarket(48);
        Assert.NotEmpty(all);
        foreach (var o in all)
        {
            string words = OfferMarket.LeaningOf(world, o).Words;
            Assert.DoesNotContain("house that built", words);
            Assert.DoesNotContain("his people", words);
            Assert.DoesNotContain("her people", words);
            Assert.DoesNotContain("fighting at home with us", words);
        }
    }

    // --- He speaks for himself (lead review, 2026-11-13: talks are promoter and fighter only) ---------

    [Fact]
    public void EveryLineAtTheTable_IsHisOwn_FirstPerson_NoAgentNoWe()
    {
        var bad = new System.Text.RegularExpressions.Regex(@"\b(we|We|we're|We're|our|us)\b|his people|her people|agent|representation");
        int lines = 0;
        foreach (ulong seed in new ulong[] { 48, 7 })
        {
            var (world, offers) = CourtedMarket(seed);
            var promo = world.Get(world.PlayerPromotion)!;
            bool excl = Negotiator.WantsExclusivity(promo);
            var valuation = new FighterValuation();
            var people = offers.Select(o => world.Get(o.Fighter)!)
                .Concat(world.RosterOf(promo.Id).Take(4))
                .Concat(world.FreeAgents().Where(x => x.Status == FighterStatus.Active).Take(6));
            foreach (var f in people)
                foreach (double frac in new[] { 0.5, 0.85, 0.97, 1.0 })
                {
                    var s = NegotiationSession.Resume(world, f, promo, valuation.Evaluate(world, f));
                    var said = new List<string> { s.OpeningLine, s.Stance };
                    var o = s.TheirTermsAsOffer(new ContractOffer
                    {
                        Fighter = f.Id, Promotion = promo.Id, Exclusive = excl, MatchingRight = frac > 0.9,
                        PromiseTitleShot = true, PromiseHomeShow = frac < 0.9, PromiseFights = 2,
                    });
                    o.PursePerFight = new Money(Math.Round(o.PursePerFight.Amount * (decimal)frac / 500m) * 500m);
                    o.Fights += frac == 0.97 ? 2 : 0;
                    for (int r = 0; r < 3 && s.Outcome == TalkOutcome.Open; r++)
                    {
                        var answer = s.Table(o);
                        said.Add(answer.Line);
                        said.AddRange(answer.Reactions.Select(x => x.Note));
                    }
                    foreach (var line in said)
                    {
                        lines++;
                        Assert.False(line.Contains(f.FirstName), $"he names himself: {line}");
                        Assert.False(bad.IsMatch(line), $"not his own voice: {line}");
                    }
                }
        }
        Assert.True(lines > 100, $"only {lines} lines heard");
    }

    [Fact]
    public void HisNumberUnderTheRivalsPurse_IsExplainedInHisReasons()
    {
        int explained = 0;
        foreach (ulong seed in new ulong[] { 48, 7, 11 })
        {
            var (world, offers) = CourtedMarket(seed);
            var promo = world.Get(world.PlayerPromotion)!;
            foreach (var o in offers)
            {
                var f = world.Get(o.Fighter)!;
                var s = NegotiationSession.Resume(world, f, promo, new FighterValuation().Evaluate(world, f));
                var view = s.View(Negotiator.WantsExclusivity(promo), clause: false);
                if (view.Position.PursePerFight.Amount >= o.Purse.Amount) continue;
                // Still worth at least the rival's paper to him (the table's rule) ...
                var exp = new FighterValuation().Evaluate(world, f);
                Assert.True(OfferMarket.Score(world, f, view.Position, exp) >= OfferMarket.Score(world, f, OfferMarket.AsContractOffer(o), exp));
                // ... and the reasons say why it can be lower.
                Assert.Contains(view.Reasons, r => r.Contains($"under {world.Get(o.From)!.Abbreviation}'s"));
                explained++;
            }
        }
        _out.WriteLine($"numbers under the rival's purse, explained: {explained}");
        Assert.True(explained > 0, "no number under a rival's purse in three seeds (precondition)");
    }

    // --- Helpers ----------------------------------------------------------------------------------

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

    // The owner sits down with every active free agent, offers a little under his number,
    // and leaves the table: open talks with all of them (no walk-outs).
    internal static void CourtEveryone(World world)
    {
        var promo = world.Get(world.PlayerPromotion)!;
        bool excl = Negotiator.WantsExclusivity(promo);
        var valuation = new FighterValuation();
        foreach (var fa in world.FreeAgents().Where(x => x.Status == FighterStatus.Active && !x.LocalTalent).OrderBy(x => x.Id.Value).ToList())
        {
            if (NegotiationSession.SouredUntil(world, fa.Id, promo.Id) is not null) continue;
            var s = NegotiationSession.Resume(world, fa, promo, valuation.Evaluate(world, fa));
            if (s.RivalOffer is not null) continue;
            var offer = s.TheirTermsAsOffer(excl, false);
            offer.PursePerFight = new Money(Math.Max(500m, Math.Round(offer.PursePerFight.Amount * 0.9m / 500m) * 500m));
            s.Table(offer);
            if (s.Outcome == TalkOutcome.Signed) { s.WalkAway(); continue; }   // (never: 0.9 is under his number)
            s.WalkAway();
            s.Remember(world.CurrentDate);
        }
    }

    private static (World World, List<RivalOffer> Offers) CourtedMarket(ulong seed)
    {
        var world = FoundedWorld(seed);
        CourtEveryone(world);
        FreeAgencyService.RunMonthly(world, world.CurrentDate);
        return (world, world.RivalOffers.Where(o => o.Kind == RivalOfferKind.FreeAgent).OrderBy(o => o.Id).ToList());
    }

    private static (World World, RivalOffer O, Fighter F) OneOffer(ulong seed)
    {
        var (world, offers) = CourtedMarket(seed);
        Assert.NotEmpty(offers);
        var o = offers[0];
        return (world, o, world.Get(o.Fighter)!);
    }
}
