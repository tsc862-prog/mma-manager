using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>Which way a fighter with a rival's offer in his hand leans.</summary>
public enum LeaningSide { Theirs, Torn, Yours }

/// <summary>His leaning in words: the side, and why (what pulls him each way).</summary>
public sealed record Leaning(LeaningSide Side, string Words);

/// <summary>What became of a final offer the owner put in writing (<see cref="OfferMarket.LodgeFinal"/>).</summary>
public enum FinalOfferResult
{
    /// <summary>In writing: he decides between it and the rival's paper on the next tick.</summary>
    Lodged,
    /// <summary>No live offer from a rival for this courted free agent: there is nothing to beat in writing.</summary>
    NoOffer,
    /// <summary>Worth less to him than the rival's paper (by <see cref="OfferMarket.Score"/>): he won't take it, so it isn't lodged.</summary>
    NotEnough,
    /// <summary>The promotion can't cover its own signing bonus.</summary>
    CantAfford,
    /// <summary>Not a deal he'd sign whatever the money: the wrong length, no real signing bonus, too little guaranteed.</summary>
    OffTerms,
}

/// <summary>
/// RIVAL OFFERS for the player's fighters (contracts stage 6). When one of the
/// player's fighters is on the last fight of his deal, a bigger promotion that
/// wants him (<see cref="PoachingService.BestSuitor"/>'s gates, on its own read)
/// may put a real offer in front of him: named promotion, purse, length,
/// bonuses, a deadline. It is the deal he takes when he is free, unless the
/// owner keeps him. The owner answers on the Desk:
/// <list type="bullet">
/// <item>MATCH (<see cref="Match"/>): re-sign him now on the rival's structure at a
///   purse that beats it by HIS lights (<see cref="Score"/>: the money, weighed by
///   what he wants — title path, home, his people, loyalty), at least his own floor.</item>
/// <item>OPEN THE TABLE: the negotiating room (<see cref="NegotiationSession.Resume"/>)
///   will not go under the match purse while the offer is live.</item>
/// <item>LET HIM GO (<see cref="LetGo"/>; silence past the deadline is the same answer):
///   he fights out his deal, his exclusive re-sign window still opens and still
///   belongs to the owner (the table stays open), and the day it closes unsigned he
///   signs with the rival on the lodged terms — provided the rival still has the room
///   and the money; otherwise he walks onto the open market as before.</item>
/// </list>
/// A holding lifer refuses the call (<see cref="Lifers.TurnDown"/>) and no offer is lodged.
///
/// FREE AGENTS the owner courts (contracts stage 7, <see cref="RivalOfferKind.FreeAgent"/>):
/// when the AI free-agency pass would sign a free agent who has open talks with a
/// player-rules promotion, the rival lodges that same deal with a 10-14 day deadline
/// instead (<see cref="TryLodgeFreeAgent"/>; <see cref="RivalOffer.Holder"/> is then the
/// courting promotion). The owner beats it at the table (<see cref="NegotiationSession.Resume"/>
/// reads it), or puts a final offer in writing (<see cref="LodgeFinal"/>), which the rival may
/// answer once on the next tick; past the deadline, or if the owner lets him go or he walks
/// out of the owner's talks, the rival signs him. Both kinds share <see cref="MaxPerYear"/>.
///
/// Only promotions under the player's deal rules (<see cref="DealWindow.PlayerRules"/>)
/// see offers, so an all-AI world never reaches any of this. No RNG stream is drawn:
/// the rate gate and the rival's appetite are keyed hash rolls, iteration is in id order.
///
/// Fog: the rival decides WHETHER to bid on its own read (the suitor gates); the price
/// is the public-facts ask (<see cref="FighterValuation"/>) through
/// <see cref="Negotiator.PoachOffer"/> times a hashed appetite, capped by its budget. The
/// player is shown the rival's name and stage, the terms, the deadline and the leaning;
/// never the rival's read or appetite.
/// </summary>
public static class OfferMarket
{
    private static readonly Negotiator Negotiator = new();
    private static readonly FighterValuation Valuation = new();

    /// <summary>Days the owner has to answer an offer (extended to the end of the re-sign window once it opens).</summary>
    public const int AnswerDays = 21;

    /// <summary>The clock stops when a pending offer has this many days or fewer left.</summary>
    public const int FinalWeekDays = 7;

    /// <summary>At most this many offers a year (trailing 365 days) for one promotion's fighters.</summary>
    public const int MaxPerYear = 5;

    /// <summary>
    /// Weekly chance that a rival calls on a fighter on his last fight: a little for
    /// anyone, more the bigger the name (popularity is public), much more for a
    /// champion. Measured (2026-11-12, founded orgs at prestige 12 and 30, script-built
    /// rosters of ~20): almost every decent fighter on a small org's last fight has a
    /// bigger suitor — ~13 such deals a year, each eligible ~7 weeks — so a flat 25%
    /// saturated <see cref="MaxPerYear"/> every year, and 8% gave 3-5.
    /// </summary>
    public static double WeeklyCallChance(World world, Fighter f) =>
        Math.Min(0.35, 0.03 + f.Intangibles.Popularity.Value * 0.002
                       + (world.Titles.Values.Any(t => t.CurrentChampion == f.Id) ? 0.10 : 0.0));

    /// <summary>Resolved offers stay on the books this long (the yearly cap and the record read them), then are dropped.</summary>
    public const int KeepResolvedDays = 365;

    private const ulong CallSalt = 0x0FFE_2CA1_1UL;
    private const ulong AppetiteSalt = 0x0FFE_2A99_E7UL;

    // --- Reading ------------------------------------------------------------------

    /// <summary>The live offer for this fighter (pending, or let go and waiting on his window), if any.</summary>
    public static RivalOffer? LiveFor(World world, FighterId fighter) =>
        world.RivalOffers.FirstOrDefault(o => o.Fighter == fighter && o.Live);

    /// <summary>The offer as the deal it would sign (the rival's paper).</summary>
    public static ContractOffer AsContractOffer(RivalOffer o) => new()
    {
        Fighter = o.Fighter, Promotion = o.From,
        PursePerFight = o.Purse, WinBonus = o.WinBonus, SigningBonus = o.SigningBonus,
        Fights = o.Fights, Exclusive = o.Exclusive, Round = 0,
    };

    /// <summary>
    /// How much an offer is worth to HIM: its per-fight money (a non-exclusive deal
    /// counts for more, since he keeps his outside earnings), lifted by what he wants
    /// from that promotion (<see cref="Negotiator.Appeal"/>, worth up to 25%), and — only
    /// at the promotion he is signed to — by his personal discount (a lifer's hometown
    /// rate, a discovered local's gratitude: a dollar from home is worth more to him).
    /// A rival that once discovered him gets no such lift: he owes the house he is in.
    /// A free agent's "house" is the promotion courting him with a rival's offer standing
    /// (<see cref="RivalOffer.Holder"/>): the same discount his table there quotes (a
    /// discoverer's gratitude), so the rival's paper can't erase it.
    /// </summary>
    public static decimal Score(World world, Fighter f, ContractOffer o, ContractExpectations exp)
    {
        decimal excl = o.Exclusive ? 1m : Negotiator.NonExclusiveMoneyFactor;
        decimal home = HomeFactor(world, f, o.Promotion);
        double appeal = Negotiator.Appeal(world, f, o, exp);
        return Negotiator.MoneyValue(world, o) / excl * (1m + (decimal)appeal * 0.25m) / home;
    }

    // His personal discount for this promotion as Score weighs it: at the house he is
    // signed to, or (a free agent) at the one courting him with a rival's offer live.
    private static decimal HomeFactor(World world, Fighter f, PromotionId promo) =>
        f.Contract?.Promotion == promo
        || (f.Contract is null && world.RivalOffers.Count > 0
            && LiveFor(world, f.Id) is { Kind: RivalOfferKind.FreeAgent } fa && fa.Holder == promo)
            ? Negotiator.TableDiscount(world, f, promo) : 1m;

    /// <summary>
    /// A final offer in writing as he weighs it against the rival's paper: its
    /// <see cref="Score"/>, less the price of the leash terms on it (a champion's clause,
    /// <see cref="Negotiator.ChampionsClausePremium"/>; a matching right,
    /// <see cref="Negotiator.MatchingRightPremium"/>) — what the table charges for them.
    /// </summary>
    public static decimal FinalScore(World world, Fighter f, ContractOffer o, ContractExpectations exp) =>
        Score(world, f, o, exp) / Negotiator.LeashFactor(o);

    /// <summary>
    /// Whether the holder of this end-of-deal offer bought the right to match it (contracts
    /// stage 9): his deal with the holder carries <see cref="Contract.MatchingRight"/>, and the
    /// holder runs under the player's deal rules. A free agent's offer never has one.
    /// </summary>
    public static bool HasMatchingRight(World world, RivalOffer o) =>
        o.Kind == RivalOfferKind.EndOfDeal
        && world.Get(o.Fighter)?.Contract is { MatchingRight: true } c && c.Promotion == o.Holder
        && DealWindow.PlayerRules(world, o.Holder);

    /// <summary>
    /// What it takes to keep him: the rival's structure (length, win bonus, signing
    /// bonus) on the holder's terms (its exclusivity), at the lowest purse on the $500
    /// step whose <see cref="Score"/> beats the rival's — and never under his live
    /// deal's show money or the floor his own table would come down to.
    ///
    /// With a MATCHING RIGHT (<see cref="HasMatchingRight"/>) the holder keeps him on the
    /// rival's terms exactly: the rival's purse, win bonus, signing bonus and length, even
    /// when the rival's stage, home or people pull him harder (his <see cref="Score"/> would
    /// prefer the rival's paper) and even under his own table's floor. Only his live deal's
    /// show money still stands under it (nobody re-signs for a pay cut). That is the whole
    /// difference the right makes: without it the match must beat the rival by his lights.
    /// The right is never a cost: when the match by his lights comes cheaper than the rival's
    /// exact money (a non-exclusive deal is worth more to him a dollar), the cheaper stands.
    /// </summary>
    public static ContractOffer MatchTerms(World world, RivalOffer o)
    {
        var f = world.Get(o.Fighter)!;
        var holder = world.Get(o.Holder)!;
        bool excl = Negotiator.WantsExclusivity(holder);
        var ours = new ContractOffer
        {
            Fighter = f.Id, Promotion = holder.Id, WinBonus = o.WinBonus, SigningBonus = o.SigningBonus,
            Fights = o.Fights, Exclusive = excl, Round = 0,
        };
        decimal exact = HasMatchingRight(world, o)
            ? Math.Max(o.Purse.Amount, Negotiator.LivePurseFloor(f, holder.Id) is { } held ? NegotiationSession.UpTo500(held.Amount) : 0m)
            : decimal.MaxValue;

        var exp = Valuation.Evaluate(world, f);
        decimal theirs = Score(world, f, AsContractOffer(o), exp);
        double appeal = Negotiator.Appeal(world, f, ours, exp);
        decimal exclF = excl ? 1m : Negotiator.NonExclusiveMoneyFactor;
        decimal home = HomeFactor(world, f, holder.Id);
        decimal neededValue = theirs * home * exclF / (1m + (decimal)appeal * 0.25m);
        decimal signingPerFight = o.Fights > 0 ? o.SigningBonus.Amount / o.Fights : o.SigningBonus.Amount;
        decimal purse = NegotiationSession.UpTo500(neededValue - 0.5m * o.WinBonus.Amount - signingPerFight);

        // Never under what he is paid now (unless skidding), nor under where his own
        // table would bottom out: matching is not a back door to a cheap renewal.
        decimal tableFloor = NegotiationSession.UpTo500(
            AskingQuote.For(world, holder, f, exp, excl).Purse.Amount * (decimal)(1.0 - appeal * 0.25));
        decimal liveFloor = Negotiator.LivePurseFloor(f, holder.Id) is { } lf ? NegotiationSession.UpTo500(lf.Amount) : 0m;
        purse = Math.Max(purse, Math.Max(tableFloor, liveFloor));
        // The right never makes keeping him dearer: when matching by his lights already costs
        // less than the rival's exact money (a non-exclusive deal, his discount), that stands.
        ours.PursePerFight = new Money(Math.Min(purse, exact));
        return ours;
    }

    /// <summary>
    /// Which way he leans and why, in words: his number at the owner's table (the
    /// opening ask) against the rival's paper, both weighed by <see cref="Score"/>.
    /// Reads his wants and public facts about the two promotions; nothing about ability.
    /// </summary>
    public static Leaning LeaningOf(World world, RivalOffer o)
    {
        var f = world.Get(o.Fighter)!;
        var holder = world.Get(o.Holder)!;
        var rival = world.Get(o.From);
        var exp = Valuation.Evaluate(world, f);
        bool excl = Negotiator.WantsExclusivity(holder);
        var atAsk = new NegotiationSession(world, f, holder, exp).TheirTermsAsOffer(excl, clause: false);
        decimal mine = Score(world, f, atAsk, exp), theirs = Score(world, f, AsContractOffer(o), exp);
        double r = theirs <= 0 ? 2.0 : (double)(mine / theirs);
        var side = r >= 1.05 ? LeaningSide.Yours : r >= 0.95 ? LeaningSide.Torn : LeaningSide.Theirs;

        // The pulls, each as weighted in Appeal, plus the money as a share of his number.
        var us = Negotiator.AppealOf(world, f, holder.Id, exp);
        var them = Negotiator.AppealOf(world, f, o.From, exp);
        string abbr = rival?.Abbreviation ?? "them";
        var pullsThem = new List<(double W, string Why)>();
        var pullsUs = new List<(double W, string Why)>();
        decimal moneyUs = Negotiator.MoneyValue(world, atAsk) / (excl ? 1m : Negotiator.NonExclusiveMoneyFactor)
                          / Negotiator.TableDiscount(world, f, holder.Id);
        decimal moneyThem = Negotiator.MoneyValue(world, AsContractOffer(o)) / (o.Exclusive ? 1m : Negotiator.NonExclusiveMoneyFactor);
        double moneyGap = moneyUs <= 0 ? 1 : (double)(moneyThem / moneyUs) - 1.0;
        if (moneyGap > 0.05) pullsThem.Add((moneyGap * 0.25, $"the money ({(int)Math.Round(moneyGap * 100)}% over his number with us)"));
        else if (moneyGap < -0.05) pullsUs.Add((-moneyGap * 0.25, "his number with us is more than their money"));
        void Pull(double a, double b, double w, string forThem, string forUs)
        {
            double d = (a - b) * w * 0.25;
            if (d > 0.005) pullsThem.Add((d, forThem));
            else if (d < -0.005) pullsUs.Add((-d, forUs));
        }
        // A free agent has no house: the courting promotion is not his home (the loyalty pull
        // is zero for both sides), and what it can claim is having discovered him. Wording only:
        // the pulls are read for words, never for the score.
        bool free = o.Kind == RivalOfferKind.FreeAgent;
        Pull(them.Title, us.Title, 0.45, $"a title path on {abbr}'s bigger stage", "a title path here");
        Pull(them.Home, us.Home, 0.15, $"{abbr} stages its cards closer to home", free ? "we stage our cards in his part of the world" : "fighting at home with us");
        Pull(them.Bonds, us.Bonds, 0.10, $"his friends or his camp are tied to {abbr}", "his friends or his camp are tied to us");
        if (!free) Pull(them.Loyalty, us.Loyalty, 0.30, "", "loyalty to the house that built him");
        else if (Negotiator.GratitudeFactor(f, holder.Id) < 1m) pullsUs.Add((0.01, "we gave him his first shot"));

        static string Top(List<(double W, string Why)> l) => string.Join(" and ",
            l.Where(x => x.Why.Length > 0).OrderByDescending(x => x.W).ThenBy(x => x.Why, StringComparer.Ordinal).Take(2).Select(x => x.Why));
        string pullThem = Top(pullsThem), pullUs = Top(pullsUs);
        string words = side switch
        {
            LeaningSide.Theirs => $"He's leaning their way{(pullThem.Length > 0 ? $": {pullThem}" : "")}."
                                  + (pullUs.Length > 0 ? $" What keeps him listening: {pullUs}." : ""),
            LeaningSide.Torn => "He's torn."
                                + (pullThem.Length > 0 ? $" For {abbr}: {pullThem}." : "")
                                + (pullUs.Length > 0 ? $" For us: {pullUs}." : ""),
            _ => $"He's leaning our way{(pullUs.Length > 0 ? $": {pullUs}" : "")}. Pay him his number and he {(o.Kind == RivalOfferKind.FreeAgent ? "signs" : "stays")}.",
        };
        return new Leaning(side, Lifers.Gendered(f, words));
    }

    /// <summary>
    /// Top of the bill for the owner: the face of his promotion, one of its champions,
    /// or in the main event of one of its scheduled cards. An offer for him stops the clock.
    /// </summary>
    public static bool TopBilled(World world, Fighter f, PromotionId holder) =>
        world.Get(holder)?.FaceOfCompany == f.Id
        || world.Titles.Values.Any(t => t.Promotion == holder && t.CurrentChampion == f.Id)
        || world.Events.Values.Any(e => e.Status == EventStatus.Scheduled && e.Promotion == holder && e.Card.Count > 0
                                        && (e.Card[^1].RedCorner == f.Id || e.Card[^1].BlueCorner == f.Id));

    // --- The weekly pass ------------------------------------------------------------

    /// <summary>
    /// Weekly, before the re-sign windows are run (<see cref="DealWindow.RunWeekly"/>):
    /// resolve the live offers (answered or not, the window closing on a let-go one),
    /// then let the rivals call on fighters who have reached the last fight of their
    /// deal. Returns an offer that should stop the owner's clock (one just made for a
    /// champion or a top-billed fighter, or a pending one entering its final week), else null.
    /// </summary>
    public static RivalOffer? RunWeekly(World world, DateOnly date)
    {
        if (!AnyPlayerRules(world)) return null;
        RivalOffer? stop = null;

        foreach (var o in world.RivalOffers.Where(o => o.Live).OrderBy(o => o.Id).ToList())
        {
            var hit = Resolve(world, o, date);
            stop ??= hit;
        }
        world.RivalOffers.RemoveAll(o => o.Resolved is { } r && date.DayNumber - r.DayNumber > KeepResolvedDays);

        foreach (var f in world.Fighters.Values.ToList())   // id order (EntityMap)
        {
            if (f.Status != FighterStatus.Active || f.Contract is not { FightsRemaining: 1, PoachWarned: false } c) continue;
            if (!DealWindow.PlayerRules(world, c.Promotion) || LiveFor(world, f.Id) is not null) continue;
            if (OffersThisYear(world, c.Promotion, date) >= MaxPerYear) continue;
            if (!new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)f.Id.Value * 0x9E37_79B9UL ^ (ulong)date.DayNumber ^ CallSalt))
                    .Chance(WeeklyCallChance(world, f))) continue;
            if (Call(world, f, date) is { } made && stop is null
                && made.State == RivalOfferState.Pending && TopBilled(world, f, made.Holder))
                stop = made;
        }
        return stop;
    }

    /// <summary>Offers made for this promotion's fighters in the 365 days to <paramref name="date"/>.</summary>
    public static int OffersThisYear(World world, PromotionId holder, DateOnly date) =>
        world.RivalOffers.Count(o => o.Holder == holder && date.DayNumber - o.Made.DayNumber < 365);

    /// <summary>
    /// A rival calls on this fighter (on the last fight of his deal with a player-rules
    /// promotion), past the rate gate: the best suitor on a bigger stage, if any wants
    /// him; a holding lifer turns it down in public; otherwise, if the rival can reach
    /// his price, the offer is lodged and the owner is told. Returns the offer, or null.
    /// </summary>
    internal static RivalOffer? Call(World world, Fighter f, DateOnly date)
    {
        if (f.Contract is not { FightsRemaining: 1 } c || world.Get(c.Promotion) is not { } holder) return null;
        if (PoachingService.BestSuitor(world, holder, f) is not { } suitor) return null;
        if (Lifers.IsHolding(world, f)) { Lifers.TurnDown(world, f, holder, suitor, date); return null; }
        if (Bid(world, suitor, f) is not { } bid) return null;

        c.PoachWarned = true;   // one call per deal
        var o = new RivalOffer
        {
            Id = world.RivalOffers.Count == 0 ? 1 : world.RivalOffers.Max(x => x.Id) + 1,
            Fighter = f.Id, From = suitor.Id, Holder = holder.Id, Kind = RivalOfferKind.EndOfDeal,
            Purse = bid.PursePerFight, WinBonus = bid.WinBonus, SigningBonus = bid.SigningBonus,
            Fights = bid.Fights, Exclusive = bid.Exclusive,
            Made = date, Deadline = date.AddDays(AnswerDays),
        };
        world.RivalOffers.Add(o);
        if (holder.Id == world.PlayerPromotion) Tell(world, o, f, holder, suitor, date);
        return o;
    }

    /// <summary>
    /// The rival's bid, priced off what the market can SEE: his public-facts ask
    /// (<see cref="FighterValuation"/>) through <see cref="Negotiator.PoachOffer"/> (the
    /// loyalty premium, the bigger stage's pull, the rival's purse cap), times the
    /// rival's appetite for him (0.94-1.08, a hash of the world, rival and fighter),
    /// on the $500 step and never past the cap. Null when the rival can't reach him.
    /// True ability never enters: the rival's read only decided whether it called.
    /// </summary>
    internal static ContractOffer? Bid(World world, Promotion suitor, Fighter f)
    {
        var exp = Valuation.Evaluate(world, f);
        if (Negotiator.PoachOffer(world, suitor, f, exp) is not { } offer) return null;
        double appetite = 0.94 + 0.14 * new SimRandom(SimRandom.Mix(world.RngSeed,
            ((ulong)suitor.Id.Value << 32) ^ (ulong)f.Id.Value ^ AppetiteSalt)).NextDouble();
        decimal cap = Negotiator.PurseCap(world, suitor);
        decimal purse = Math.Min(Math.Round(offer.PursePerFight.Amount * (decimal)appetite / 500m) * 500m, Math.Max(cap, offer.PursePerFight.Amount));
        offer.PursePerFight = new Money(Math.Max(500m, purse));
        offer.WinBonus = new Money(Math.Min(offer.WinBonus.Amount, Math.Round(purse * 0.75m / 500m) * 500m));
        if (offer.SigningBonus.Amount > 0) offer.SigningBonus = new Money(Math.Round(purse * 0.5m / 500m) * 500m);
        return offer;
    }

    // One live offer through the week: answered, timed out, overtaken, or taken.
    private static RivalOffer? Resolve(World world, RivalOffer o, DateOnly date)
    {
        if (o.Kind == RivalOfferKind.FreeAgent) return ResolveFreeAgent(world, o, date);
        var f = world.Get(o.Fighter);
        var rival = world.Get(o.From);
        if (f is null || f.Status == FighterStatus.Retired || f.Contract?.Promotion != o.Holder)
            return Close(world, o, RivalOfferState.Withdrawn, date);
        var c = f.Contract!;
        if (rival is null || rival.Defunct)
        {
            Close(world, o, RivalOfferState.Withdrawn, date);
            c.PoachWarned = false;   // the rival fell away, not the deal: another may call on it
            if (o.Holder == world.PlayerPromotion)
                Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                    $"{rival?.Abbreviation ?? "The rival"}'s offer for {f.FullName} is off",
                    Lifers.Gendered(f, $"{rival?.Name ?? "The promotion that wanted him"} has closed its doors — the offer for {f.FullName} died with it."),
                    aboutFighter: f.Id);
            return null;
        }
        // The deal grew back (a champion's clause): he is not on his last fight any more.
        if (c.FightsRemaining > 1)
        {
            c.PoachWarned = false;   // a new last fight will come: a rival may call on it then
            return Close(world, o, RivalOfferState.Withdrawn, date);
        }

        if (DealWindow.InWindow(world, f) && c.WindowEndsOn is { } end)
        {
            if (date >= end) { WindowCloses(world, o, f, rival, date); return null; }
            if (o.Deadline < end)
            {
                o.Deadline = end;   // his window is the owner's time to keep him (the Desk card follows in RefreshDesk)
            }
        }
        else if (o.State == RivalOfferState.Pending && date > o.Deadline)
        {
            // Silence is an answer: he'll go when his deal is up.
            LetGo(world, o, date, silent: true);
            return null;
        }

        // The last week to answer: the owner hears it, and the clock stops.
        int left = o.Deadline.DayNumber - date.DayNumber;
        if (o.State == RivalOfferState.Pending && left <= FinalWeekDays && left + 7 > FinalWeekDays)
        {
            if (o.Holder == world.PlayerPromotion)
                Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                    $"Last week to answer {rival.Abbreviation} on {f.FullName}",
                    Lifers.Gendered(f, $"{rival.Name}'s offer for {f.FullName} needs an answer by {o.Deadline:d MMMM}. "
                        + $"Match it on the Desk, sit down with him on the Contracts screen, or let him go — "
                        + $"if we say nothing, he signs with {rival.Abbreviation} when his deal is up."),
                    aboutFighter: f.Id);
            return o;
        }
        return null;
    }

    // His window closed with the offer still standing: he signs with the rival on the
    // lodged terms, if it still has the room and the money; else he walks to the market.
    private static void WindowCloses(World world, RivalOffer o, Fighter f, Promotion rival, DateOnly date)
    {
        bool mine = o.Holder == world.PlayerPromotion;
        var holder = world.Get(o.Holder);
        if (Solvency.Distress(rival) > 0 || !PoachingService.HasRoom(world, rival, f) || rival.Balance.Amount < o.SigningBonus.Amount)
        {
            Close(world, o, RivalOfferState.Withdrawn, date);
            f.Contract!.PoachWarned = false;   // the rival fell away
            if (mine)
                Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                    $"{rival.Abbreviation} pulled its offer for {f.FullName}",
                    Lifers.Gendered(f, $"{rival.Name} no longer has the room or the money for {f.FullName}. His window has closed, "
                        + "so he's on the open market — we can still talk to him, but so can everyone else."),
                    aboutFighter: f.Id);
            return;   // DealWindow closes him onto the market this same week
        }

        Close(world, o, RivalOfferState.Signed, date);
        ContractLifecycle.LeavePromotion(world, f, o.Holder, date,
            $"{rival.Name} signed him when his deal ran out", rival.Id, hisChoice: true);
        f.Contract = null;
        ContractLifecycle.Sign(world, f, AsContractOffer(o), date);   // (posts the signing news itself)
        if (mine)
            Living.Mail.Post(world, date, MailCategory.BoardNote, Living.Mail.Owner(world),
                $"We lost {f.FullName} to {rival.Abbreviation}",
                Lifers.Gendered(f, $"His window closed without a new deal, and {f.FullName} signed the paper {rival.Name} put in front of him: "
                    + $"{o.Purse} a fight for {o.Fights} fights. This is the treadmill — we build them up, the giants take them."),
                aboutFighter: f.Id);
    }

    // --- The owner's answers ---------------------------------------------------------------

    /// <summary>
    /// MATCH: re-sign him now on <see cref="MatchTerms"/>. Returns false (and the offer
    /// stays) when it is no longer live or the promotion can't cover the signing bonus.
    /// </summary>
    public static bool Match(World world, RivalOffer o, DateOnly date)
    {
        if (o.State != RivalOfferState.Pending && o.State != RivalOfferState.LetGo) return false;
        if (world.Get(o.Fighter) is not { } f || f.Contract?.Promotion != o.Holder || world.Get(o.Holder) is not { } holder) return false;
        var terms = MatchTerms(world, o);
        if (holder.Balance.Amount < terms.SigningBonus.Amount)
        {
            if (o.Holder == world.PlayerPromotion)
                Living.Mail.Post(world, date, MailCategory.BoardNote, Living.Mail.Owner(world),
                    $"We can't cover {f.FullName}'s signing bonus",
                    $"Matching means {terms.SigningBonus} up front and the books say no. The offer stands until the deadline.",
                    aboutFighter: f.Id);
            return false;
        }
        Close(world, o, RivalOfferState.Matched, date);
        ContractLifecycle.Sign(world, f, terms, date);
        if (o.Holder == world.PlayerPromotion)
            Living.Mail.Post(world, date, MailCategory.FighterRequest, f.FullName,
                $"{f.FullName} is staying",
                Lifers.Gendered(f, $"“You matched it. That's all I needed to see.” {f.FullName} re-signed: {terms.PursePerFight} a fight for {terms.Fights} fights."
                    + $" {world.Get(o.From)?.Abbreviation ?? "The rival"} will have to look elsewhere."),
                aboutFighter: f.Id);
        return true;
    }

    /// <summary>
    /// LET HIM GO: no match. He fights out his deal; his re-sign window still opens and
    /// is still the owner's (the table stays open, at the offer's floor), and the day it
    /// closes unsigned he signs with the rival. <paramref name="silent"/>: the deadline passed unanswered.
    /// </summary>
    public static void LetGo(World world, RivalOffer o, DateOnly date, bool silent = false)
    {
        if (o.State != RivalOfferState.Pending) return;
        o.State = RivalOfferState.LetGo;
        // A courted free agent let go: the owner has stopped courting him, so the rival
        // signs him on the next tick (if it still can). There is no re-sign window to wait on.
        if (o.Kind == RivalOfferKind.FreeAgent)
        {
            o.Final = null;
            if (o.Holder == world.PlayerPromotion && world.Get(o.Fighter) is { } fa)
            {
                string rivalAbbr = world.Get(o.From)?.Abbreviation ?? "the rival";
                Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                    $"We let {fa.FullName} go to {rivalAbbr}",
                    Lifers.Gendered(fa, $"We've stopped courting {fa.FullName}. {rivalAbbr} signs him on its paper when the week turns "
                        + "— if it still has the room and the money; if not, he stays on the open market."),
                    aboutFighter: fa.Id);
            }
            return;
        }
        RemoveDesk(world, o);
        if (o.Holder != world.PlayerPromotion || world.Get(o.Fighter) is not { } f) return;
        string abbr = world.Get(o.From)?.Abbreviation ?? "the rival";
        Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
            silent ? $"No answer on {f.FullName}: he'll sign with {abbr}" : $"We let {f.FullName} go to {abbr}",
            Lifers.Gendered(f, (silent ? $"We didn't answer {abbr}'s offer, and that's an answer. " : "")
                + $"{f.FullName} fights out his deal with us; then his re-sign window is still ours — we can sit down with him "
                + $"until it closes, but he won't take less than {abbr}'s paper is worth to him. If it closes unsigned, he signs with {abbr}."),
            aboutFighter: f.Id);
    }

    /// <summary>A fighter signed a deal (any door): his live offer is settled by it, and the Desk's numbers follow.</summary>
    internal static void OnSigned(World world, Fighter f, PromotionId to, DateOnly date)
    {
        if (world.RivalOffers.Count == 0) return;   // the AI world: nothing to settle
        if (LiveFor(world, f.Id) is { } o)
            Close(world, o, to == o.Holder ? RivalOfferState.Beaten : to == o.From ? RivalOfferState.Signed : RivalOfferState.Withdrawn, date);
        // A signing to either side of a pending offer can move its terms (his people on that roster).
        if (world.RivalOffers.Any(x => x.State == RivalOfferState.Pending && (x.Holder == to || x.From == to)))
            RefreshDesk(world);
    }

    /// <summary>
    /// He is leaving the promotion that holds his offer by some other door (released,
    /// retired, his window run out): the offer dies with the deal, now — the Desk card
    /// and the table's rival floor go at once.
    /// </summary>
    internal static void OnLeaving(World world, Fighter f, PromotionId from, DateOnly date)
    {
        if (world.RivalOffers.Count == 0) return;
        if (LiveFor(world, f.Id) is { } o && o.Holder == from) Close(world, o, RivalOfferState.Withdrawn, date);
        if (world.RivalOffers.Any(x => x.State == RivalOfferState.Pending && (x.Holder == from || x.From == from)))
            RefreshDesk(world);
    }

    /// <summary>A free agent retires: any offer for him dies now, not at the next tick.</summary>
    internal static void OnRetired(World world, Fighter f, DateOnly date)
    {
        if (world.RivalOffers.Count == 0) return;
        if (LiveFor(world, f.Id) is { } o) Close(world, o, RivalOfferState.Withdrawn, date);
    }

    // --- Free agents the player courts (contracts stage 7) -----------------------------------

    /// <summary>Shortest and longest deadline (days) on a courted free agent's offer: a keyed hash picks within it.</summary>
    public const int FreeAgentMinDays = 10, FreeAgentMaxDays = 14;

    private const ulong DeadlineSalt = 0x0FFE_FA0D_EAD1UL;

    /// <summary>Whether a rival's offer for him is live (so the quiet market leaves him alone).</summary>
    public static bool HasLiveOffer(World world, FighterId fighter) =>
        world.RivalOffers.Count > 0 && LiveFor(world, fighter) is not null;

    /// <summary>Seats in <paramref name="div"/> that <paramref name="promo"/> holds for free agents it has lodged offers for.</summary>
    public static int SeatsHeld(World world, PromotionId promo, Division div) =>
        world.RivalOffers.Count == 0 ? 0
            : world.RivalOffers.Count(o => o.Live && o.Kind == RivalOfferKind.FreeAgent && o.From == promo
                                           && world.Get(o.Fighter) is { } x && div.Matches(x));

    /// <summary>
    /// The player-rules promotion courting this free agent, if any: open talks
    /// (<see cref="World.Talks"/>, list order) that he has not walked out of, inside
    /// <see cref="DealWindow.TalksMemoryDays"/>. Null when nobody is courting him.
    /// </summary>
    public static PromotionId? CourtedBy(World world, Fighter f, DateOnly date)
    {
        if (world.Talks.Count == 0 || f.Contract is not null) return null;
        foreach (var t in world.Talks)
            if (t.Fighter == f.Id && t.SouredUntil is null
                && date.DayNumber - t.LastTalked.DayNumber <= DealWindow.TalksMemoryDays
                && DealWindow.PlayerRules(world, t.Promotion) && world.Get(t.Promotion) is { Defunct: false })
                return t.Promotion;
        return null;
    }

    /// <summary>
    /// The AI free-agency pass would sign this free agent on <paramref name="terms"/>
    /// today. If a player-rules promotion is courting him (<see cref="CourtedBy"/>), the
    /// caps have room (<see cref="FreeAgentMaxPerYear"/> of this kind and <see cref="MaxPerYear"/>
    /// of both together in the trailing year, and at most one of this kind per courting
    /// promotion per pass), and the rival can really sign him today (solvent, room, the
    /// signing bonus in the bank: the same gates as at the deadline, so no offer is a bluff), the rival
    /// lodges exactly those terms as a <see cref="RivalOfferKind.FreeAgent"/> offer with a
    /// 10-14 day deadline instead, and the owner hears it from the fighter (the terms come
    /// out at the table). Returns whether it was lodged; false leaves today's instant
    /// signing to the caller. No RNG stream: the deadline is a keyed hash.
    /// </summary>
    internal static bool TryLodgeFreeAgent(World world, Promotion rival, Fighter f, ContractOffer terms, DateOnly date)
    {
        if (CourtedBy(world, f, date) is not { } courting || courting == rival.Id) return false;
        if (HasLiveOffer(world, f.Id) || OffersThisYear(world, courting, date) >= MaxPerYear) return false;
        if (FreeAgentOffersThisYear(world, courting, date) >= FreeAgentMaxPerYear
            || world.RivalOffers.Any(x => x.Kind == RivalOfferKind.FreeAgent && x.Holder == courting && x.Made == date))
            return false;
        if (Solvency.Distress(rival) > 0 || !PoachingService.HasRoom(world, rival, f) || rival.Balance.Amount < terms.SigningBonus.Amount)
            return false;
        int span = FreeAgentMaxDays - FreeAgentMinDays + 1;
        int days = FreeAgentMinDays + (int)(SimRandom.Mix(world.RngSeed,
            (ulong)f.Id.Value * 0x9E37_79B9UL ^ (ulong)date.DayNumber ^ DeadlineSalt) % (ulong)span);
        var o = new RivalOffer
        {
            Id = world.RivalOffers.Count == 0 ? 1 : world.RivalOffers.Max(x => x.Id) + 1,
            Fighter = f.Id, From = rival.Id, Holder = courting, Kind = RivalOfferKind.FreeAgent,
            Purse = terms.PursePerFight, WinBonus = terms.WinBonus, SigningBonus = terms.SigningBonus,
            Fights = terms.Fights, Exclusive = terms.Exclusive,
            Made = date, Deadline = date.AddDays(days),
        };
        world.RivalOffers.Add(o);
        if (courting == world.PlayerPromotion)
            Living.Mail.Post(world, date, MailCategory.FighterRequest, f.FullName,
                $"{f.FullName} has an offer from {rival.Abbreviation}",
                Lifers.Gendered(f, $"“{rival.Name} put paper in front of me. I said I'd hear you out first — but not for long.” "
                    + $"{f.FullName} will give us until {o.Deadline:d MMMM}: sit down with him on the Contracts screen and he'll show us "
                    + $"what they're offering. Beat it at the table, or put our best offer in writing. If we say nothing, he signs with {rival.Abbreviation}."),
                aboutFighter: f.Id);
        return true;
    }

    /// <summary>
    /// Free-agent offers allowed per courting promotion in the trailing 365 days (they also
    /// count toward <see cref="MaxPerYear"/>), so shopping the market can't use up the year's
    /// calls on the owner's own fighters.
    /// </summary>
    public const int FreeAgentMaxPerYear = 2;

    /// <summary>Free-agent offers lodged for this courting promotion in the 365 days to <paramref name="date"/>.</summary>
    public static int FreeAgentOffersThisYear(World world, PromotionId holder, DateOnly date) =>
        world.RivalOffers.Count(o => o.Holder == holder && o.Kind == RivalOfferKind.FreeAgent && date.DayNumber - o.Made.DayNumber < 365);

    /// <summary>
    /// The rival's walk-away purse for a courted free agent: the smaller of its purse cap
    /// and his ask (never past its own budget), never under what it already offered. Public
    /// facts only. Sim-only: never shown.
    /// </summary>
    internal static decimal CeilingPurse(World world, Promotion rival, RivalOffer o, ContractExpectations exp)
    {
        decimal ask = exp.AskingPurse.Amount * (o.Exclusive ? 1m : Negotiator.NonExclusiveMoneyFactor);
        decimal top = Math.Floor(Math.Min(Negotiator.PurseCap(world, rival), ask) / 500m) * 500m;
        return Math.Max(o.Purse.Amount, top);
    }

    /// <summary>Chance the rival answers a final offer at all (a keyed roll per offer; else it folds).</summary>
    public const double ComeBackChance = 0.5;

    private const ulong ComeBackSalt = 0x0FFE_C0BA_C4UL;

    /// <summary>
    /// Whether the rival is minded to come back once if the owner's final offer beats its paper:
    /// its own call, a keyed hash of the world, the rival, the fighter and the day it made the
    /// offer (no RNG stream). Whether it CAN is a separate question (<see cref="Improve"/>).
    /// </summary>
    internal static bool WouldComeBack(World world, RivalOffer o) =>
        new SimRandom(SimRandom.Mix(world.RngSeed,
            ((ulong)o.From.Value << 32) ^ (ulong)o.Fighter.Value ^ ((ulong)o.Made.DayNumber * 0x9E37_79B9UL) ^ ComeBackSalt))
            .Chance(ComeBackChance);

    /// <summary>
    /// "Put it in writing": the owner's final offer to a courted free agent with a rival's
    /// paper in his hand. Lodged only if it is worth at least as much to him as the rival's
    /// (<see cref="FinalScore"/>: a champion's clause is priced in) and the promotion can cover its signing bonus; a new one replaces
    /// an earlier one. On the next weekly tick the rival may come back once
    /// (<see cref="RivalOffer.Improvements"/>) or fold, and he chooses by score. Callers
    /// normally go through <see cref="NegotiationSession.PutInWriting"/>, which also holds
    /// the offer to the table's sanity rules (length, signing bonus, guaranteed money).
    /// </summary>
    internal static FinalOfferResult LodgeFinal(World world, Fighter f, Promotion promo, ContractOffer ours)
    {
        if (LiveFor(world, f.Id) is not { Kind: RivalOfferKind.FreeAgent, State: RivalOfferState.Pending } o
            || o.Holder != promo.Id || f.Contract is not null || world.Get(o.From) is not { Defunct: false })
            return FinalOfferResult.NoOffer;
        if (promo.Balance.Amount < ours.SigningBonus.Amount) return FinalOfferResult.CantAfford;
        var exp = Valuation.Evaluate(world, f);
        if (FinalScore(world, f, ours, exp) < Score(world, f, AsContractOffer(o), exp)) return FinalOfferResult.NotEnough;
        o.Final = new ContractOffer
        {
            Fighter = f.Id, Promotion = promo.Id,
            PursePerFight = ours.PursePerFight, WinBonus = ours.WinBonus, SigningBonus = ours.SigningBonus,
            Fights = ours.Fights, Exclusive = ours.Exclusive, ChampionsClause = ours.ChampionsClause,
            PpvPoints = ours.PpvPoints, PerformanceBonus = ours.PerformanceBonus, Round = 0,
            PromiseFights = ours.PromiseFights, PromiseHomeShow = ours.PromiseHomeShow,
            PromiseTitleShot = ours.PromiseTitleShot, MatchingRight = ours.MatchingRight,
        };
        return FinalOfferResult.Lodged;
    }

    // A courted free agent's offer through the week: settled elsewhere, the rival or the
    // courting side gone, a final offer answered, his talks with the owner over, the
    // deadline passed (the rival signs him), or its last week (the clock stops).
    private static RivalOffer? ResolveFreeAgent(World world, RivalOffer o, DateOnly date)
    {
        var f = world.Get(o.Fighter);
        if (f is null || f.Status == FighterStatus.Retired || f.Contract is not null
            || world.Get(o.Holder) is not { Defunct: false } holder)
            return Close(world, o, RivalOfferState.Withdrawn, date);
        bool mine = o.Holder == world.PlayerPromotion;
        if (world.Get(o.From) is not { Defunct: false } rival)
        {
            Close(world, o, RivalOfferState.Withdrawn, date);
            if (mine)
                Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                    $"{world.Get(o.From)?.Abbreviation ?? "The rival"}'s offer for {f.FullName} is off",
                    Lifers.Gendered(f, $"{world.Get(o.From)?.Name ?? "The promotion that wanted him"} has closed its doors, and its offer for {f.FullName} went with it. "
                        + "He's still a free agent."),
                    aboutFighter: f.Id);
            return null;
        }

        // He walked out of the owner's talks: a final offer in writing goes with them.
        bool walkedOut = world.Talks.Any(t => t.Fighter == f.Id && t.Promotion == o.Holder && t.SouredUntil is not null);
        if (walkedOut) o.Final = null;

        // The owner's final offer, in writing: the rival's one chance, then his choice.
        if (o.Final is not null && o.State == RivalOfferState.Pending)
        {
            var (done, stop) = AnswerFinal(world, o, f, holder, rival, date);
            if (done) return stop;
        }

        // Let go, he walked out of the owner's talks, or the deadline passed: the rival
        // signs him on its paper. (Talks merely going quiet don't end it before the
        // deadline the owner was given.)
        if (o.State == RivalOfferState.LetGo || walkedOut || date > o.Deadline)
        {
            RivalSigns(world, o, f, rival, date);
            return null;
        }

        // The last week to beat it: the owner hears it, and the clock stops.
        int left = o.Deadline.DayNumber - date.DayNumber;
        if (left <= FinalWeekDays && left + 7 > FinalWeekDays)
        {
            if (mine)
                Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                    $"Last week to beat {rival.Abbreviation} for {f.FullName}",
                    Lifers.Gendered(f, $"{f.FullName} has {rival.Name}'s offer in his hand and will sign it after {o.Deadline:d MMMM}. "
                        + "Sit down with him on the Contracts screen to beat it, or put our best offer in writing."),
                    aboutFighter: f.Id);
            return o;
        }
        return null;
    }

    // The rival signs him on the lodged terms, if it still has the room and the money;
    // otherwise the offer falls away and he stays on the market.
    private static void RivalSigns(World world, RivalOffer o, Fighter f, Promotion rival, DateOnly date)
    {
        bool mine = o.Holder == world.PlayerPromotion;
        o.Final = null;
        if (Solvency.Distress(rival) > 0 || !PoachingService.HasRoom(world, rival, f) || rival.Balance.Amount < o.SigningBonus.Amount)
        {
            Close(world, o, RivalOfferState.Withdrawn, date);
            if (mine)
                Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                    $"{rival.Abbreviation} pulled its offer for {f.FullName}",
                    Lifers.Gendered(f, $"{rival.Name} no longer has the room or the money for {f.FullName}. He's still a free agent — we can still talk to him."),
                    aboutFighter: f.Id);
            return;
        }
        Close(world, o, RivalOfferState.Signed, date);
        ContractLifecycle.Sign(world, f, AsContractOffer(o), date);   // (posts the signing news itself)
        if (mine)
            Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                $"{f.FullName} signed with {rival.Abbreviation}",
                Lifers.Gendered(f, $"We didn't beat {rival.Name}'s paper in time, and {f.FullName} signed it: {TermsText(o)}."),
                aboutFighter: f.Id);
    }

    // He weighs the owner's final offer against the rival's paper. Done = the offer is
    // settled for this week (he signed with us, or the rival came back and the owner must
    // answer: Stop is then the offer); not done = he turned ours down and the offer runs on.
    private static (bool Done, RivalOffer? Stop) AnswerFinal(World world, RivalOffer o, Fighter f, Promotion holder, Promotion rival, DateOnly date)
    {
        var ours = o.Final!;
        o.Final = null;
        bool mine = o.Holder == world.PlayerPromotion;
        var exp = Valuation.Evaluate(world, f);
        decimal sOurs = FinalScore(world, f, ours, exp), sTheirs = Score(world, f, AsContractOffer(o), exp);
        bool broke = holder.Balance.Amount < ours.SigningBonus.Amount;

        if (broke || sOurs < sTheirs)
        {
            if (mine)
                Living.Mail.Post(world, date, MailCategory.FighterRequest, f.FullName,
                    $"{f.FullName} turned down our offer",
                    Lifers.Gendered(f, broke
                        ? $"“You can't cover the signing bonus you wrote down.” {rival.Abbreviation}'s paper still stands until {o.Deadline:d MMMM}."
                        : $"“{rival.Abbreviation}'s paper is worth more to me than yours.” It still stands until {o.Deadline:d MMMM}."),
                    aboutFighter: f.Id);
            return (false, null);
        }

        // The rival's one chance, if it chooses to take it (a keyed roll: no automatic
        // escalation): come back to beat ours by 5% (by his lights), within its budget, if
        // it is solvent, has the room and can pay — or fold.
        if (o.Improvements == 0 && WouldComeBack(world, o) && Solvency.Distress(rival) == 0
            && PoachingService.HasRoom(world, rival, f) && Improve(world, o, f, rival, sOurs * 1.05m, exp))
        {
            if (o.Deadline < date.AddDays(FinalWeekDays)) o.Deadline = date.AddDays(FinalWeekDays);
            if (mine)
                Living.Mail.Post(world, date, MailCategory.FighterRequest, f.FullName,
                    $"{rival.Abbreviation} came back for {f.FullName}",
                    Lifers.Gendered(f, $"“They came back at {o.Purse} a fight. Your move.” {rival.Name} raised its offer to {TermsText(o)}. "
                        + $"Beat it by {o.Deadline:d MMMM} or he signs there — they won't go again."),
                    aboutFighter: f.Id);
            return (true, o);   // the owner must answer now: the clock stops
        }

        // He takes ours (the signing settles the offer as Beaten).
        ContractLifecycle.Sign(world, f, ours, date);
        if (mine)
            Living.Mail.Post(world, date, MailCategory.FighterRequest, f.FullName,
                $"{f.FullName} is ours",
                Lifers.Gendered(f, $"“I'm yours.” {f.FullName} took our offer over {rival.Name}'s: {ours.PursePerFight} a fight for {ours.Fights} fights."),
                aboutFighter: f.Id);
        return (true, null);
    }

    // Raise the rival's paper until it scores at least <paramref name="target"/>: the purse on
    // the $500 step up to its walk-away purse (CeilingPurse: never past its own purse cap),
    // then a signing bonus up to half that purse (how AI offers price one). False when that
    // isn't enough, or the bank can't cover the bonus.
    internal static bool Improve(World world, RivalOffer o, Fighter f, Promotion rival, decimal target, ContractExpectations exp)
    {
        decimal ceiling = CeilingPurse(world, rival, o, exp);
        decimal maxSigning = Math.Max(o.SigningBonus.Amount, Math.Floor(ceiling * 0.5m / 500m) * 500m);
        ContractOffer At(decimal p, decimal sb) => new()
        {
            Fighter = o.Fighter, Promotion = o.From, PursePerFight = new Money(p), WinBonus = o.WinBonus,
            SigningBonus = new Money(sb), Fights = o.Fights, Exclusive = o.Exclusive,
        };
        decimal purse = o.Purse.Amount, signing = o.SigningBonus.Amount;
        while (purse + 500m <= ceiling && Score(world, f, At(purse, signing), exp) < target) purse += 500m;
        while (signing + 500m <= maxSigning && Score(world, f, At(purse, signing), exp) < target) signing += 500m;
        if (Score(world, f, At(purse, signing), exp) < target || rival.Balance.Amount < signing) return false;
        o.Purse = new Money(purse);
        o.SigningBonus = new Money(signing);
        o.Improvements++;
        return true;
    }

    // --- Plumbing --------------------------------------------------------------------------

    private static bool AnyPlayerRules(World world) =>
        world.Promotions.Values.Any(p => DealWindow.PlayerRules(world, p.Id));

    private static RivalOffer? Close(World world, RivalOffer o, RivalOfferState state, DateOnly date)
    {
        o.State = state;
        o.Resolved = date;
        RemoveDesk(world, o);
        return null;
    }

    // The owner hears it from the fighter himself, and the call goes on the Desk.
    private static void Tell(World world, RivalOffer o, Fighter f, Promotion holder, Promotion rival, DateOnly date)
    {
        var lean = LeaningOf(world, o);
        string terms = TermsText(o);
        string voice = lean.Side switch
        {
            LeaningSide.Yours => "I'd rather stay. Show me you want me.",
            LeaningSide.Torn => "I could go either way. Make it easy for me.",
            _ => "That's hard to say no to.",
        };
        int id = world.Decisions.Count == 0 ? 1 : world.Decisions.Max(d => d.Id) + 1;
        world.Decisions.Add(DeskCall(world, o, id, date));
        Living.Mail.Post(world, date, MailCategory.FighterRequest, f.FullName,
            $"{f.FullName} has an offer from {rival.Abbreviation}",
            Lifers.Gendered(f, $"“{rival.Name} put paper in front of me: {terms}. {voice}” "
                + $"{f.FullName}'s deal with {holder.Abbreviation} is down to its last fight. {lean.Words} "
                + (HasMatchingRight(world, o) ? $"But we hold a matching right: match {rival.Abbreviation}'s terms exactly and he has to stay. " : "")
                + $"Answer by {o.Deadline:d MMMM}: match it on the Desk (Home), sit down with him on the Contracts screen, "
                + $"or let him go — he signs with {rival.Abbreviation} when his deal is up."),
            aboutFighter: f.Id);
    }

    private static void RemoveDesk(World world, RivalOffer o) =>
        world.Decisions.RemoveAll(d => d.Kind == DecisionKind.RivalOffer && d.Fighter == o.Fighter);

    // The offer's terms in a line ("$9,000 a fight for 4 fights, $4,500 to win").
    private static string TermsText(RivalOffer o) =>
        $"{o.Purse} a fight for {o.Fights} fights"
        + (o.WinBonus.Amount > 0 ? $", {o.WinBonus} to win" : "")
        + (o.SigningBonus.Amount > 0 ? $", {o.SigningBonus} to sign" : "")
        + (o.Exclusive ? "" : ", non-exclusive");

    // The Desk call for a pending offer, built from the LIVE match terms: the price on the
    // card is the purse Match signs (a Decision is immutable, so it is rebuilt, not edited).
    private static Decision DeskCall(World world, RivalOffer o, int id, DateOnly created)
    {
        var f = world.Get(o.Fighter)!;
        var rival = world.Get(o.From)!;
        var lean = LeaningOf(world, o);
        var match = MatchTerms(world, o);
        string tier = OrgSizing.Label(OrgSizing.SizeOf(rival));
        return new Decision
        {
            Id = id, Kind = DecisionKind.RivalOffer, Created = created, Deadline = o.Deadline,
            Fighter = f.Id, Promotion = rival.Id, Price = match.PursePerFight,
            Detail = Lifers.Gendered(f, $"{rival.Name} ({tier}) has offered {f.FullName} {TermsText(o)}. {lean.Words} "
                + (HasMatchingRight(world, o) ? "We hold a matching right on his deal: match their terms exactly and he stays, whatever pulls him there. " : "")
                + $"Match: {match.PursePerFight} a fight for {match.Fights} fights"
                + (match.SigningBonus.Amount > 0 ? $" and {match.SigningBonus} to sign" : "")
                + $" keeps him. Let him go and he signs with {rival.Abbreviation} when his deal is up — "
                + "his re-sign window is still ours until then."),
        };
    }

    /// <summary>
    /// Rebuild every pending offer's Desk call from the live world — its deadline, the
    /// match price (which moves when his last fight is fought, his record changes, or a
    /// friend joins the roster) and his leaning — so the player signs at the number shown.
    /// Run at the end of each week's tick and after any signing or departure. No RNG.
    /// </summary>
    public static void RefreshDesk(World world)
    {
        for (int i = 0; i < world.Decisions.Count; i++)
        {
            var d = world.Decisions[i];
            if (d.Kind != DecisionKind.RivalOffer || d.Fighter is not { } fid) continue;
            if (LiveFor(world, fid) is not { State: RivalOfferState.Pending } o
                || world.Get(o.From) is null || world.Get(fid)?.Contract?.Promotion != o.Holder) continue;
            world.Decisions[i] = DeskCall(world, o, d.Id, d.Created);
        }
    }
}
