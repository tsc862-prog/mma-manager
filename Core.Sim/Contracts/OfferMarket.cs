using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>Which way a fighter with a rival's offer in his hand leans.</summary>
public enum LeaningSide { Theirs, Torn, Yours }

/// <summary>His leaning in words: the side, and why (what pulls him each way).</summary>
public sealed record Leaning(LeaningSide Side, string Words);

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
    /// </summary>
    public static decimal Score(World world, Fighter f, ContractOffer o, ContractExpectations exp)
    {
        decimal excl = o.Exclusive ? 1m : Negotiator.NonExclusiveMoneyFactor;
        decimal home = f.Contract?.Promotion == o.Promotion ? Negotiator.TableDiscount(world, f, o.Promotion) : 1m;
        double appeal = Negotiator.Appeal(world, f, o, exp);
        return Negotiator.MoneyValue(world, o) / excl * (1m + (decimal)appeal * 0.25m) / home;
    }

    /// <summary>
    /// What it takes to keep him: the rival's structure (length, win bonus, signing
    /// bonus) on the holder's terms (its exclusivity), at the lowest purse on the $500
    /// step whose <see cref="Score"/> beats the rival's — and never under his live
    /// deal's show money or the floor his own table would come down to.
    /// </summary>
    public static ContractOffer MatchTerms(World world, RivalOffer o)
    {
        var f = world.Get(o.Fighter)!;
        var holder = world.Get(o.Holder)!;
        var exp = Valuation.Evaluate(world, f);
        decimal theirs = Score(world, f, AsContractOffer(o), exp);

        bool excl = Negotiator.WantsExclusivity(holder);
        var ours = new ContractOffer
        {
            Fighter = f.Id, Promotion = holder.Id, WinBonus = o.WinBonus, SigningBonus = o.SigningBonus,
            Fights = o.Fights, Exclusive = excl, Round = 0,
        };
        double appeal = Negotiator.Appeal(world, f, ours, exp);
        decimal exclF = excl ? 1m : Negotiator.NonExclusiveMoneyFactor;
        decimal home = f.Contract?.Promotion == holder.Id ? Negotiator.TableDiscount(world, f, holder.Id) : 1m;
        decimal neededValue = theirs * home * exclF / (1m + (decimal)appeal * 0.25m);
        decimal signingPerFight = o.Fights > 0 ? o.SigningBonus.Amount / o.Fights : o.SigningBonus.Amount;
        decimal purse = NegotiationSession.UpTo500(neededValue - 0.5m * o.WinBonus.Amount - signingPerFight);

        // Never under what he is paid now (unless skidding), nor under where his own
        // table would bottom out: matching is not a back door to a cheap renewal.
        decimal tableFloor = NegotiationSession.UpTo500(
            AskingQuote.For(world, holder, f, exp, excl).Purse.Amount * (decimal)(1.0 - appeal * 0.25));
        decimal liveFloor = Negotiator.LivePurseFloor(f, holder.Id) is { } lf ? NegotiationSession.UpTo500(lf.Amount) : 0m;
        purse = Math.Max(purse, Math.Max(tableFloor, liveFloor));
        ours.PursePerFight = new Money(purse);
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
        Pull(them.Title, us.Title, 0.45, $"a title path on {abbr}'s bigger stage", "a title path here");
        Pull(them.Home, us.Home, 0.15, $"{abbr} fights closer to home", "fighting at home with us");
        Pull(them.Bonds, us.Bonds, 0.10, $"his people are at {abbr}", "his people are here");
        Pull(them.Loyalty, us.Loyalty, 0.30, "", "loyalty to the house that built him");

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
            _ => $"He's leaning our way{(pullUs.Length > 0 ? $": {pullUs}" : "")}. Pay him his number and he stays.",
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
