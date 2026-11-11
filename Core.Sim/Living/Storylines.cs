using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The drama layer — the stuff that makes the world feel like it's telling a story.
/// Around each card it hands out fight-night awards, notices streaks and hot
/// prospects, brews rivalries out of close fights, lets winners call people out,
/// pulls the occasional fighter with a fight-week injury, and busts the rare cheat.
/// Deterministic (the random beats come off the tick's seeded RNG).
/// </summary>
public static class Storylines
{
    // --- Weeks out: camp injuries, and the scramble to save the bout --------

    /// <summary>
    /// The slow-motion version of fight-week disaster: every week, each booked
    /// fighter on a card inside five weeks has a small chance of a camp injury.
    /// With TIME on the clock the story isn't a scrapped bout — it's the
    /// short-notice replacement: AI matchmakers save the fight with a roster
    /// body or a free agent on a one-fight deal (a career-making phone call);
    /// the player gets the bad news, the open slot, and the names worth calling.
    /// A champion's withdrawal takes the belt off the card; a challenger's
    /// keeps it on the line only if the stand-in is a ranked name.
    /// </summary>
    public static void RollCampWithdrawals(World world, DateOnly date, SimRandom rng)
    {
        var upcoming = world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled
                && e.Date.DayNumber - date.DayNumber is > 6 and <= 35
                && world.Get(e.Promotion) is not null)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
            .ToList();
        if (upcoming.Count == 0) return;

        // Who's already spoken for — a replacement can't be double-booked.
        var booked = ForwardCalendar.Committed(world);

        foreach (var ev in upcoming)
        {
            // The player's cards roll on the week's shared stream as they always
            // have. Every AI card (the forward calendar keeps dozens in this
            // window) rolls on its own keyed stream, so they add no draw to it.
            var r = ev.Promotion == world.PlayerPromotion ? rng
                : new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)ev.Id.Value * 0x2545F491UL ^ (ulong)date.DayNumber ^ CampWithdrawalSalt));
            foreach (var fight in ev.Card.ToList())
            {
                FighterId? outId = null;
                foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                {
                    if (world.Get(id) is not { Status: FighterStatus.Active } f) continue;
                    if (!r.Chance(0.008)) continue;
                    int weeks = r.Range(2, 8);
                    f.CurrentInjury = new Injury
                    {
                        Severity = InjurySeverity.Minor,
                        Description = "a training-camp injury",
                        OccurredOn = date,
                        ExpectedReturn = date.AddDays(weeks * 7),
                    };
                    f.Status = FighterStatus.Injured;
                    outId = id;
                    break;
                }
                if (outId is not { } hurt) continue;

                var hurtF = world.Get(hurt)!;
                var stayId = fight.RedCorner == hurt ? fight.BlueCorner : fight.RedCorner;

                // The player fills their own holes — they get the news and the shortlist.
                if (ev.Promotion == world.PlayerPromotion)
                {
                    ev.Card.Remove(fight);
                    var stay = world.Get(stayId);
                    var options = ReplacementPool(world, ev, fight, booked, stayId).Take(3)
                        .Select(c => $"{c.FullName} ({c.Record}, {(c.Contract is null ? "free agent" : "our roster")})")
                        .ToList();
                    Mail.Post(world, date, MailCategory.General, "Matchmaking",
                        $"Pullout: {hurtF.FullName} is OUT of {ev.Name}",
                        $"Camp injury, {(ev.Date.DayNumber - date.DayNumber) / 7} weeks out — the bout with " +
                        $"{stay?.FullName ?? "their opponent"} is off the card. " +
                        (options.Count > 0
                            ? "Names worth a short-notice call: " + string.Join(" · ", options) + ". Rebook from the event screen."
                            : "No obvious replacement is free — rebook or run short."),
                        aboutFighter: hurt);
                    News.Post(world, date, NewsCategory.Withdrawal,
                        $"{hurtF.FullName} withdraws from {ev.Name}", "Injured in camp; the search for a replacement is on.", ev.Promotion);
                    continue;
                }

                // The AI saves the bout if anyone plausible will take the call.
                bool wasMain = ev.Card.Count > 0 && ReferenceEquals(ev.Card[^1], fight);
                if (!SaveBout(world, ev, fight, hurt, booked, date, "Injured in camp"))
                {
                    // (An emptied card is rebuilt by the calendar's fill pass.)
                    if (ev.Card.Count > 0 && wasMain) ForwardCalendar.RestoreHeadliner(world, ev);
                }
            }
        }
    }

    private const ulong CampWithdrawalSalt = 0xCA4F_3A11UL;

    /// <summary>The name prefix of a champ-vs-champ super-card (a one-off, outside the regular calendar).</summary>
    public const string SuperfightPrefix = "CHAMP VS CHAMP";

    public static bool IsSuperfight(Event ev) => ev.Name.StartsWith(SuperfightPrefix, StringComparison.Ordinal);

    /// <summary>
    /// A corner is OUT of an AI card with time still on the clock: the
    /// matchmaker saves the bout with a roster body or a free agent on a
    /// one-fight deal if anyone plausible will take the call (true), otherwise
    /// the bout is off (false). A champion out takes the belt off the card; a
    /// challenger's stand-in keeps it on the line only if he's a ranked name.
    /// <paramref name="cause"/> is the news's reason ("Injured in camp", "has
    /// left for AFC"...). The feed only hears about it when it matters.
    /// </summary>
    internal static bool SaveBout(World world, Event ev, Fight fight, FighterId outId,
        HashSet<FighterId> booked, DateOnly date, string cause)
    {
        var outF = world.Get(outId);
        string outName = outF?.FullName ?? "A fighter";
        var stayId = fight.RedCorner == outId ? fight.BlueCorner : fight.RedCorner;
        bool loud = Newsworthy(world, ev, fight);
        string reason = Reason(outF, cause);

        var sub = ReplacementPool(world, ev, fight, booked, stayId).FirstOrDefault();
        if (sub is null)
        {
            ev.Card.Remove(fight);
            if (loud)
                News.Post(world, date, NewsCategory.Withdrawal,
                    $"{outName} {(cause.StartsWith("Injured") ? "withdraws from" : "is off")} {ev.Name}",
                    $"{reason}; no replacement was found and the bout is off.", ev.Promotion);
            return false;
        }

        if (sub.Contract is null)
        {
            // A free agent takes the short-notice call at a premium — the
            // phone call that makes careers.
            var ask = new FighterValuation().Evaluate(world, sub).AskingPurse;
            ContractLifecycle.Sign(world, sub, new ContractOffer
            {
                Fighter = sub.Id, Promotion = ev.Promotion,
                PursePerFight = new Money(Math.Round(ask.Amount * 1.25m / 100m) * 100m),
                Fights = 1, Exclusive = false,
            }, date);
        }

        // The belt is the champion's to defend: if he's the one out — or has
        // already walked away from it — it comes off the card.
        var belt = fight.IsTitleFight && fight.Title is { } tid ? world.Get(tid) : null;
        bool champOut = belt is not null && (belt.CurrentChampion == outId || belt.CurrentChampion is null && belt.Lineage.LastOrDefault()?.Champion == outId);
        bool subRanked = world.Get(ev.Promotion) is { } promo
            && promo.Divisions.Any(d => d.Matches(sub) && d.Rankings.Take(8).Contains(sub.Id));
        bool beltStaysOn = fight.IsTitleFight && !champOut && subRanked;

        var replacement = new Fight
        {
            Id = world.NewFightId(),
            RedCorner = fight.RedCorner == outId ? sub.Id : fight.RedCorner,
            BlueCorner = fight.BlueCorner == outId ? sub.Id : fight.BlueCorner,
            WeightClass = fight.WeightClass,
            ScheduledRounds = fight.ScheduledRounds,
            IsTitleFight = beltStaysOn,
            Title = beltStaysOn ? fight.Title : null,
        };
        ev.Card[ev.Card.IndexOf(fight)] = replacement;
        booked.Add(sub.Id);

        if (loud || Newsworthy(world, ev, replacement))
            News.Post(world, date, NewsCategory.Withdrawal,
                $"{sub.FullName} steps in for {outName} at {ev.Name}",
                (cause.StartsWith("Injured") ? "" : reason + ". ")
                + (champOut ? "The champion is out and the belt is off the card — the show goes on. "
                    : fight.IsTitleFight && !beltStaysOn ? "The title leaves the line with the challenger — the stand-in fights for pride and position. "
                    : "") + "Short notice, full danger.", ev.Promotion);
        return true;
    }

    /// <summary>
    /// A bout comes off a card with no time to save it (fight week, or both
    /// corners gone): scrapped, the bill reshuffled by the caller.
    /// </summary>
    internal static void ScrapBout(World world, Event ev, Fight fight, FighterId outId, DateOnly date, string cause)
    {
        bool loud = Newsworthy(world, ev, fight);
        ev.Card.Remove(fight);
        if (!loud) return;
        var outF = world.Get(outId);
        News.Post(world, date, NewsCategory.Withdrawal,
            $"{outF?.FullName ?? "A fighter"} is off {ev.Name}",
            $"{Reason(outF, cause)}; there's no time to find a replacement and the bout is scrapped.", ev.Promotion, outId);
    }

    // "Injured in camp" / "Jae-won Seo has left for VMMA".
    private static string Reason(Fighter? f, string cause) =>
        cause.StartsWith("Injured") || f is null ? cause : $"{f.FullName} {cause}";

    // The feed hears about a lost bout when it matters: always on the player's
    // card, otherwise when it's a title fight or one of the announced top two
    // bouts of a card the sport is watching, or involves a known name.
    private static bool Newsworthy(World world, Event ev, Fight fight)
    {
        if (ev.Promotion == world.PlayerPromotion) return true;
        int at = ev.Card.IndexOf(fight);
        bool top = at >= 0 && at >= ev.Card.Count - 2;
        if ((fight.IsTitleFight || top) && ForwardCalendar.MeritsHeadline(world, ev)) return true;
        return (world.Get(fight.RedCorner)?.Intangibles.Popularity.Value ?? 0) >= 55
            || (world.Get(fight.BlueCorner)?.Intangibles.Popularity.Value ?? 0) >= 55;
    }

    // Who could plausibly save the bout: roster bodies first (free, familiar),
    // then free agents hungry for the call — same division, healthy, unbooked.
    // (On an AI card, nobody on a spent deal: his contract ends before the bout.)
    private static IEnumerable<Fighter> ReplacementPool(World world, Event ev, Fight fight,
        HashSet<FighterId> booked, FighterId opponent)
    {
        if (world.Get(ev.Promotion) is not { } promo) return Enumerable.Empty<Fighter>();
        var div = promo.Divisions.FirstOrDefault(d =>
            d.WeightClass == fight.WeightClass && world.Get(opponent) is { } o && d.Matches(o));
        bool ai = ev.Promotion != world.PlayerPromotion;

        bool Fits(Fighter c) => c.Status == FighterStatus.Active && !c.LocalTalent
            && c.Id != opponent && !booked.Contains(c.Id)
            && c.IsMatchReady(ev.Date)
            // With no division to match (a superfight is fought a class up from the
            // challenger's own), still never cross the sexes: a women's flyweight
            // champion was once sent in to replace a man on an AFC super-card.
            && (div?.Matches(c) ?? (c.WeightClass == fight.WeightClass
                                    && world.Get(opponent) is { } foe && foe.Sex == c.Sex))
            && !(ai && c.Contract is { FightsRemaining: <= 0 })
            && world.BiddingWars.All(w => w.Fighter != c.Id);

        var roster = world.RosterOf(promo.Id).Where(Fits).ToList();
        // An AI house only phones the free agents it can pay: the stand-in signs at
        // 1.25x his asking price, and a club show that rings a star's agent for a
        // one-night fill-in bankrupts itself (it did, in the shoestring run).
        // (A bleeding house tightens as its free agency does: DistressedPurseFactor.)
        int distress = Org.Solvency.Distress(promo);
        decimal cap = !ai ? decimal.MaxValue
            : Contracts.Negotiator.PurseCap(world, promo) * (distress > 0 ? Org.Solvency.DistressedPurseFactor(distress) : 1m);
        var valuation = new FighterValuation();
        var market = world.FreeAgents().Where(c => Fits(c) && c.Record.TotalBouts >= 3).ToList()
            .Where(c => !ai || valuation.Evaluate(world, c).AskingPurse.Amount * 1.25m <= cap);
        return roster.Concat(market)
            .OrderByDescending(c => Ability.FighterAbility.CurrentAbility(c));
    }

    // --- Before the card: fight-week withdrawals ----------------------------
    public static void RollPullouts(World world, Event ev, DateOnly date, SimRandom rng)
    {
        foreach (var fight in ev.Card.ToList())
        {
            foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
            {
                if (world.Get(id) is not { Status: FighterStatus.Active } f) continue;
                if (!rng.Chance(0.03)) continue;

                int weeks = rng.Range(3, 8);
                f.CurrentInjury = new Injury
                {
                    Severity = InjurySeverity.Minor,
                    Description = "a training-camp injury",
                    OccurredOn = date,
                    ExpectedReturn = date.AddDays(weeks * 7),
                };
                f.Status = FighterStatus.Injured;
                ev.Card.Remove(fight);

                News.Post(world, date, NewsCategory.Withdrawal,
                    $"{f.FullName} withdraws from {ev.Name}", "Pulled with a camp injury; the bout is off.", ev.Promotion);
                if (ev.Promotion == world.PlayerPromotion)
                    Mail.Post(world, date, MailCategory.General, "Matchmaking",
                        $"Pullout: {f.FullName}", $"{f.FullName} is out of {ev.Name} injured — that bout has been scrapped.");
                break;   // one corner out ends the bout
            }
        }
    }

    // --- Fight week: the scale takes its victims ----------------------------

    /// <summary>
    /// Weigh-in day. A small chance per bout that a corner blows the limit:
    /// the bout goes ahead at a catchweight, the misser is fined a fifth of
    /// their purse (paid to the house) and takes the reputational bruise —
    /// and a belt NEVER survives a bad scale: a challenger who misses fights
    /// with the title off the line; a CHAMPION who misses is stripped on the
    /// spot, the belt vacant behind them.
    /// </summary>
    public static void RollWeightMisses(World world, Event ev, DateOnly date, SimRandom rng)
    {
        var promo = world.Get(ev.Promotion);
        foreach (var fight in ev.Card)
        {
            if (fight.WeightMissBy is not null) continue;
            // WHO blows the limit is a matter of character, not a coin toss: the
            // pro who lives in the gym makes weight, the one who doesn't, doesn't.
            // Aggregate stays near the old flat 4% per bout — it's redistributed
            // onto the fighters who deserve it.
            var misserId = rng.Chance(0.5) ? fight.RedCorner : fight.BlueCorner;
            var otherId = misserId.Equals(fight.RedCorner) ? fight.BlueCorner : fight.RedCorner;
            if (!rng.Chance(MissChance(world.Get(misserId))))
            {
                misserId = otherId;
                if (!rng.Chance(MissChance(world.Get(misserId)))) continue;
            }
            if (world.Get(misserId) is not { Status: FighterStatus.Active } misser || misser.LocalTalent) continue;

            fight.WeightMissBy = misserId;
            fight.CatchweightLbs = WeightClasses.LimitLbs(fight.WeightClass) + rng.Range(2, 5);
            misser.Morale = Math.Clamp(misser.Morale - 8, 0, 100);
            misser.Intangibles.Popularity = Math.Max(3, misser.Intangibles.Popularity.Value - 2);

            if (promo is not null && misser.Contract is { } c && c.PursePerFight.Amount > 0)
                Finance.Ledger.Income(promo, date, FinanceCategory.Other,
                    new Money(Math.Round(c.PursePerFight.Amount * 0.2m)), $"Weight-miss fine — {misser.LastName}");

            string drama;
            if (fight is { IsTitleFight: true, Title: { } tid } && world.Get(tid) is { } belt)
            {
                if (belt.CurrentChampion == misserId)
                {
                    // Stripped on the scale — the reign ends in a bathrobe.
                    var reign = belt.Lineage.LastOrDefault(r => r.LostOn is null);
                    if (reign is not null) reign.LostOn = date;
                    belt.CurrentChampion = null;
                    drama = $"stripped of the {belt.Name} on the scale — the belt is vacant";
                }
                else drama = "the title is OFF the line after the challenger blew the limit";
                fight.IsTitleFight = false;
                fight.Title = null;
            }
            else drama = $"the bout goes ahead at a {fight.CatchweightLbs}lb catchweight";

            if (Notable(world, ev) || misser.Intangibles.Popularity.Value >= 40)
                News.Post(world, date, NewsCategory.Withdrawal,
                    $"{misser.FullName} misses weight at {ev.Name}",
                    $"{char.ToUpper(drama[0])}{drama[1..]}. A fifth of the purse goes to the house. "
                    + Flavor.Quotes.Say(misser, Flavor.Moment.MissedWeight, date), ev.Promotion, misserId);
            if (ev.Promotion == world.PlayerPromotion || misser.Contract?.Promotion == world.PlayerPromotion)
                Mail.Post(world, date, MailCategory.BoardNote, "Matchmaking",
                    $"Scale drama: {misser.FullName} missed weight",
                    $"{misser.FullName} came in heavy for {ev.Name} — {drama}.", aboutFighter: misserId);
        }
    }

    /// <summary>How likely this fighter is to blow the limit — professionalism, inverted.</summary>
    private static double MissChance(Fighter? f) =>
        f is null ? 0 : 0.02 * (1.9 - f.Temperament.Professionalism.Value / 100.0 * 1.6);

    // --- After the card -----------------------------------------------------
    public static void AfterEvent(World world, Event ev, DateOnly date, SimRandom rng)
    {
        Awards(world, ev, date);
        Milestones(world, ev, date);
        Rivalries(world, ev, date, rng);
        Callouts(world, ev, date, rng);
        Suspensions(world, ev, date, rng);
        Controversies(world, ev, date, rng);
    }

    private static bool Notable(World world, Event ev) =>
        ev.Promotion == world.PlayerPromotion || ev.Tier >= EventTier.Ppv || ev.Card.Any(f => f.IsTitleFight);

    private static void Awards(World world, Event ev, DateOnly date)
    {
        var scored = ev.Card.Where(f => f.Outcome is not null).ToList();
        if (scored.Count == 0 || world.Get(ev.Promotion) is not { } promo) return;

        var fotn = scored.OrderByDescending(Flavor.MatchRating.Of).ThenBy(f => f.Id.Value).First();
        Pay(world, promo, fotn.RedCorner, date, "Fight of the Night");
        Pay(world, promo, fotn.BlueCorner, date, "Fight of the Night");

        var potn = scored.Where(f => IsFinish(f.Outcome!.Method))
            .OrderBy(f => f.Outcome!.EndRound).ThenBy(f => f.Outcome!.EndTimeSeconds).FirstOrDefault();
        if (potn?.Outcome?.Winner is { } pid)
            Pay(world, promo, pid, date, "Performance of the Night");

        if (!Notable(world, ev)) return;
        var (r, b) = (world.Get(fotn.RedCorner), world.Get(fotn.BlueCorner));
        if (r is not null && b is not null)
            News.Post(world, date, NewsCategory.Award,
                $"Fight of the Night: {r.FullName} vs {b.FullName}", $"Stole the show at {ev.Name}.", ev.Promotion);
        if (potn is not null && world.Get(potn.Outcome!.Winner!.Value) is { } pw)
            News.Post(world, date, NewsCategory.Award,
                $"Performance of the Night: {pw.FullName}", $"A statement finish at {ev.Name}.", ev.Promotion);
    }

    private static void Pay(World world, Promotion promo, FighterId id, DateOnly date, string award)
    {
        if (world.Get(id) is not { } f) return;
        // The house bonus everyone on the card is playing for — and, on top of
        // it, the performance bonus written into his deal, if his deal is with
        // this promotion (the contract lever is extra money, never a pay cut).
        var house = new Money(Math.Max(2_000m, promo.Prestige.Value * 200m));
        Finance.Ledger.Expense(promo, date, FinanceCategory.Other, house, $"{award} — {f.FullName}");
        if (f.Contract is { PerformanceBonus.Amount: > 0 } c && c.Promotion == promo.Id)
            Finance.Ledger.Expense(promo, date, FinanceCategory.FighterPurse, c.PerformanceBonus,
                $"{award} — {f.FullName}'s contract performance bonus");
        f.Morale = Math.Clamp(f.Morale + 6, 0, 100);
    }

    private static void Milestones(World world, Event ev, DateOnly date)
    {
        foreach (var fight in ev.Card)
        {
            if (fight.Outcome?.Winner is not { } wid || world.Get(wid) is not { } w) continue;
            int streak = w.Record.CurrentStreak;
            bool mine = w.Contract?.Promotion == world.PlayerPromotion;

            if (streak is 5 or 8 or 10 or 15 && (mine || streak >= 8))
                News.Post(world, date, NewsCategory.Milestone,
                    $"{w.FullName} extends the streak to {streak}", "Riding a hot run.", ev.Promotion);

            if (w.Record.Losses == 0 && w.Record.Wins is 5 or 8 or 10 && w.AgeOn(date) <= 25)
                News.Post(world, date, NewsCategory.Debut,
                    $"Prospect watch: {w.FullName} to {w.Record.Wins}-0", "Undefeated and climbing.", ev.Promotion);
        }
    }

    private static void Rivalries(World world, Event ev, DateOnly date, SimRandom rng)
    {
        foreach (var fight in ev.Card)
        {
            if (fight.Outcome is not { } o) continue;
            var (a, b) = (fight.RedCorner, fight.BlueCorner);
            var existing = Between(world, a, b);
            bool closeDecision = o.Method is FightMethod.SplitDecision or FightMethod.MajorityDecision;

            if (existing is not null)
            {
                existing.Intensity = Math.Min(100, existing.Intensity + 20);   // they met again — it grows
                if (existing.Intensity >= 60 && Notable(world, ev) &&
                    world.Get(a) is { } fa && world.Get(b) is { } fb)
                    News.Post(world, date, NewsCategory.Rivalry,
                        $"Bad blood: {fa.FullName} and {fb.FullName} go at it again", "This one's personal now.", ev.Promotion);
            }
            else if ((closeDecision || (IsFinish(o.Method) && rng.Chance(0.25))) && rng.Chance(0.5))
            {
                world.Rivalries.Add(new Rivalry { A = a, B = b, Intensity = 30, Since = date });
                if (Notable(world, ev) && world.Get(a) is { } fa && world.Get(b) is { } fb)
                    News.Post(world, date, NewsCategory.Rivalry,
                        $"A rivalry is born: {fa.FullName} vs {fb.FullName}", "Sparks flew — a rematch would sell.", ev.Promotion);
            }
        }
    }

    private static void Callouts(World world, Event ev, DateOnly date, SimRandom rng)
    {
        foreach (var fight in ev.Card)
        {
            if (fight.Outcome?.Winner is not { } wid || world.Get(wid) is not { } w || w.LocalTalent) continue;
            // A statement finish earns the microphone; a decision win with a
            // grudge to air sometimes grabs it anyway.
            bool finish = IsFinish(fight.Outcome.Method);
            if (!rng.Chance(finish ? 0.35 : 0.10)) continue;

            var promo = world.Get(ev.Promotion);
            var div = promo?.Divisions.FirstOrDefault(d => d.Matches(w));

            // Who do they want? The belt, the man who beat them, or the name
            // one rung up the ladder — in that order of ambition.
            var champId = div?.Title is { } tid ? world.Get(tid)?.CurrentChampion : null;
            FighterId? lastBeatMe = LastConqueror(world, w);
            FighterId? rungAbove = null;
            if (div is not null)
            {
                int myRank = div.Rankings.IndexOf(w.Id);
                if (myRank > 0 && world.Get(div.Rankings[myRank - 1]) is { Status: FighterStatus.Active }) rungAbove = div.Rankings[myRank - 1];
            }

            (FighterId Target, string Line, string MailLine)? pick =
                champId is { } cid && cid != w.Id && world.Get(cid) is { Status: FighterStatus.Active } champ
                    ? (cid, $"{w.FullName} calls out champion {champ.FullName}",
                       $"After that win, {w.FullName} is calling for a shot at {champ.FullName}. Book it on the Book Event screen.")
                : lastBeatMe is { } rid && world.Get(rid) is { Status: FighterStatus.Active } rival && rng.Chance(0.7)
                    ? (rid, $"{w.FullName} wants {rival.FullName} — \"run it back\"",
                       $"{w.FullName} used the mic to demand the rematch with {rival.FullName} — the loss still stings. The crowd would eat it up.")
                : rungAbove is { } aid && world.Get(aid) is { } above
                    ? (aid, $"{w.FullName} calls out {above.FullName}: \"you're next\"",
                       $"{w.FullName} wants {above.FullName}'s spot in the rankings — and said so on the mic. That fight books itself now.")
                : null;
            if (pick is not { } callout) continue;

            News.Post(world, date, NewsCategory.Callout, callout.Line,
                (finish ? "The statement finish came with a statement. " : "The mic work was sharper than the fight. ")
                + Flavor.Quotes.Say(w, finish ? Flavor.Moment.WinFinish : Flavor.Moment.WinDecision, date,
                    fight.Outcome.Loser is { } lid ? world.Get(lid) : null),
                ev.Promotion, w.Id);
            if (w.Contract?.Promotion == world.PlayerPromotion)
                Mail.Post(world, date, MailCategory.FighterRequest, w.FullName,
                    $"Callout: {w.FullName}", callout.MailLine, aboutFighter: w.Id);

            // Words have weight: a public callout starts (or stokes) bad blood —
            // which the matchmaker and the box office both feed on when the
            // fight actually gets made.
            Heat(world, w.Id, callout.Target, date, 12);
        }
    }

    // The last active fighter to hand them a loss (the revenge storyline).
    // Scans recent events newest-first and only their small cards — a fighter's
    // last loss is nearly always close by, and a grudge older than two years is
    // cold anyway.
    private static FighterId? LastConqueror(World world, Fighter f)
    {
        var cutoff = world.CurrentDate.AddYears(-2);
        foreach (var e in world.Events.Values
                     .Where(e => e.Status == EventStatus.Completed && e.Date >= cutoff)
                     .OrderByDescending(e => e.Date))
        {
            foreach (var bout in e.Card)
            {
                if (bout.Outcome is not { } o || o.Loser != f.Id || o.Winner is not { } victor) continue;
                return world.Get(victor) is { Status: FighterStatus.Active, LocalTalent: false } ? victor : null;
            }
        }
        return null;
    }

    // --- Champ vs champ: the superfight roll ---------------------------------

    /// <summary>
    /// Now and then a DOMINANT champion (a real reign, a real name) moves up a
    /// division to challenge the champion above — an AI org stages it as a
    /// dedicated champ-vs-champ super-card a month out. Win and they conquer
    /// the higher division for good (the old belt passes on behind them — see
    /// ResultsProcessor); lose and they return home with the mystique dented.
    /// The player's org is never auto-booked — their superfights are their own
    /// call on the Book Event screen.
    /// </summary>
    public static void MaybeSuperfight(World world, DateOnly date, SimRandom rng)
    {
        if (!rng.Chance(0.05)) return;   // a couple of attempts a year, world-wide

        foreach (var promo in world.Promotions.Values
                     .Where(p => !p.Defunct && p.Id != world.PlayerPromotion)
                     .OrderByDescending(p => p.Prestige.Value))
        {
            foreach (var lower in promo.Divisions)
            {
                if (lower.Title is not { } lowTid || world.Get(lowTid) is not { } lowBelt) continue;
                if (lowBelt.CurrentChampion is not { } challengerId
                    || world.Get(challengerId) is not { Status: FighterStatus.Active } challenger) continue;
                var reign = lowBelt.Lineage.LastOrDefault(r => r.LostOn is null);
                if (reign is null || reign.Defenses < 2 || challenger.Intangibles.Popularity.Value < 60) continue;

                if (lower.WeightClass >= WeightClass.Heavyweight) continue;
                var upperClass = (WeightClass)((int)lower.WeightClass + 1);
                var upper = promo.Divisions.FirstOrDefault(d => d.WeightClass == upperClass && d.Sex == lower.Sex);
                if (upper?.Title is not { } upTid || world.Get(upTid) is not { } upBelt) continue;
                if (upBelt.CurrentChampion is not { } champId
                    || world.Get(champId) is not { Status: FighterStatus.Active } champ) continue;

                var when = date.AddDays(28);
                if (!challenger.IsMatchReady(when) || !champ.IsMatchReady(when)) continue;
                // A champion already announced for a card is spoken for.
                if (ForwardCalendar.IsCommitted(world, challengerId) || ForwardCalendar.IsCommitted(world, champId)) continue;

                var ev = new Booking.EventService().CreateEvent(world, promo.Id,
                    $"{SuperfightPrefix}: {champ.LastName} vs {challenger.LastName}", when,
                    new[] { new Booking.Matchup(champId, challengerId, upperClass, 5, true, upTid) });
                ev.Tier = Org.OrgPerks.MaxEventTier(promo);

                News.Post(world, date, NewsCategory.Milestone,
                    $"SUPERFIGHT: {challenger.FullName} moves up to challenge {champ.FullName}",
                    $"The {lower.WeightClass} king chases a second belt at {upperClass} — champ versus champ, {when:d MMMM}.",
                    promo.Id, challengerId);
                Mail.Post(world, date, MailCategory.General, "FightWire",
                    $"Champ vs champ announced at {promo.Abbreviation}",
                    $"{challenger.FullName} ({lower.WeightClass} champion) moves up to challenge {champ.FullName} for the {upBelt.Name}. " +
                    "The whole sport will be watching — a two-division king changes what a star even means.",
                    aboutFighter: challengerId);
                return;   // one announcement at a time — let it breathe
            }
        }
    }

    // --- Between the cards: the drama never sleeps ---------------------------

    /// <summary>
    /// Monthly off-cage drama — the feud fuel that doesn't need a fight night.
    /// Social-media shots and altercations FLARE existing rivalries; big
    /// personalities pick fresh fights with division neighbours out of nowhere;
    /// and feuds that can no longer be settled (retirements, different orgs)
    /// slowly cool off and die instead of simmering forever.
    /// </summary>
    public static void MonthlyDrama(World world, DateOnly date, SimRandom rng)
    {
        // Cool-down and housekeeping first: a feud needs two active fighters to
        // stay alive, and cross-org bad blood fades when the fight can't be made.
        for (int i = world.Rivalries.Count - 1; i >= 0; i--)
        {
            var rv = world.Rivalries[i];
            var a = world.Get(rv.A);
            var b = world.Get(rv.B);
            if (a is null || b is null || a.Status == FighterStatus.Retired || b.Status == FighterStatus.Retired)
            {
                world.Rivalries.RemoveAt(i);   // somebody hung up the gloves — it's over
                continue;
            }
            if (a.Contract?.Promotion != b.Contract?.Promotion)
            {
                rv.Intensity -= 4;             // different rosters — the story starves
                if (rv.Intensity < 10) world.Rivalries.RemoveAt(i);
            }
        }

        // Flare-ups: live feuds throw sparks between fights.
        foreach (var rv in world.Rivalries.ToList())
        {
            if (!rng.Chance(0.06)) continue;
            var a = world.Get(rv.A);
            var b = world.Get(rv.B);
            if (a is null || b is null) continue;

            rv.Intensity = Math.Min(100, rv.Intensity + 8);
            string beef = FlareLines[rng.Range(0, FlareLines.Length - 1)];
            PostDrama(world, date, a, b, string.Format(beef, a.FullName, b.FullName));
        }

        // Fresh beefs: a couple of rolls a month, world-wide — big personalities
        // pick fights with a division neighbour without a bell ever ringing.
        var promos = world.Promotions.Values.Where(p => !p.Defunct).ToList();
        for (int attempt = 0; attempt < 2 && promos.Count > 0; attempt++)
        {
            var promo = promos[rng.Range(0, promos.Count - 1)];
            if (promo.Divisions.Count == 0) continue;
            var division = promo.Divisions[rng.Range(0, promo.Divisions.Count - 1)];
            var named = division.Rankings.Take(8).Select(world.Get)
                .Where(f => f is { Status: FighterStatus.Active }).Cast<Fighter>().ToList();
            if (named.Count < 2) continue;

            var x = named[rng.Range(0, named.Count - 1)];
            var y = named[rng.Range(0, named.Count - 1)];
            if (x.Id == y.Id || Between(world, x.Id, y.Id) is not null) continue;

            // Loudmouths start trouble; quiet professionals mostly don't.
            double chance = 0.18 + Spice(x) + Spice(y);
            if (!rng.Chance(chance)) continue;

            world.Rivalries.Add(new Rivalry { A = x.Id, B = y.Id, Intensity = 20, Since = date });
            string beef = BeefLines[rng.Range(0, BeefLines.Length - 1)];
            PostDrama(world, date, x, y, string.Format(beef, x.FullName, y.FullName));
        }
    }

    /// <summary>
    /// How likely a fighter is to start something out of nothing. Reads the
    /// TEMPERAMENT — ego picks the fight, temper throws the first word — with the
    /// marketable look worth a little on top. (This used to be the portrait tag
    /// alone, which meant a fighter's willingness to start a beef was decided by
    /// the art pack, and headless worlds had no beefs at all.)
    /// </summary>
    internal static double Spice(Fighter f)
    {
        double spice = (f.Temperament.Ego.Value - 50) / 250.0        // ±0.20
                     + (f.Temperament.Temper.Value - 50) / 330.0;    // ±0.15
        return spice + f.PersonaTag switch
        {
            "menace" => 0.06,
            "glam" or "adonis" => 0.03,
            "babyface" => -0.03,
            _ => 0.0,
        };
    }

    private static readonly string[] FlareLines =
    {
        "{0} torches {1} on social media",
        "{0} and {1} have to be separated at a fan event",
        "{0} mocks {1}'s last performance in an interview",
        "{0} posts training footage aimed squarely at {1}",
        "{0} says {1} is \"ducking\" the rematch",
    };

    private static readonly string[] BeefLines =
    {
        "{0} and {1} trade shots online — out of nowhere",
        "{0} calls {1} overrated: \"that ranking is a joke\"",
        "{0} and {1} clash in the comments — the division notices",
        "Backstage run-in: {0} and {1} exchange words",
    };

    /// <summary>Raise (or start) bad blood between two fighters — callouts, drama, dirty fights.</summary>
    internal static void Heat(World world, FighterId a, FighterId b, DateOnly date, int amount)
    {
        var rv = Between(world, a, b);
        if (rv is not null) rv.Intensity = Math.Min(100, rv.Intensity + amount);
        else world.Rivalries.Add(new Rivalry { A = a, B = b, Intensity = 20, Since = date });
    }

    // Drama makes headlines when it involves the player's roster or a real name.
    private static void PostDrama(World world, DateOnly date, Fighter a, Fighter b, string headline)
    {
        bool notable = a.Contract?.Promotion == world.PlayerPromotion
                    || b.Contract?.Promotion == world.PlayerPromotion
                    || a.Intangibles.Popularity.Value >= 55 || b.Intangibles.Popularity.Value >= 55;
        if (notable)
            News.Post(world, date, NewsCategory.Rivalry, headline,
                "The feud is doing numbers.", a.Contract?.Promotion ?? default);
    }

    private static void Suspensions(World world, Event ev, DateOnly date, SimRandom rng)
    {
        foreach (var fight in ev.Card)
        {
            foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
            {
                if (world.Get(id) is not { Status: FighterStatus.Active } f) continue;
                // 0.4% per corner: a failed test somewhere in the world about
                // once a month — a SCANDAL, not wallpaper. (The old 1.5% quietly
                // produced hundreds of suspensions a year; nobody noticed until
                // overturned results started stamping NCs on records for good.)
                if (!rng.Chance(0.004)) continue;

                int weeks = rng.Range(20, 40);
                f.Status = FighterStatus.Suspended;
                f.SuspendedUntil = date.AddDays(weeks * 7);
                Lifecycle.RelinquishTitles(world, f, date);   // a champ who fails a test loses the belt
                f.Intangibles.Popularity = Math.Max(3, f.Intangibles.Popularity.Value - 6);

                // The commission takes its cut of a dirty night's pay.
                if (world.Get(ev.Promotion) is { } promo && f.Contract is { } c && c.PursePerFight.Amount > 0)
                    Finance.Ledger.Income(promo, date, FinanceCategory.Other,
                        new Money(Math.Round(c.PursePerFight.Amount * 0.3m)), $"Anti-doping fine — {f.LastName}");

                // And if the cheat WON, the win never happened: the result is
                // overturned to a No Contest for both — records, streaks and all.
                bool overturned = fight.Outcome is { } o && o.Winner == f.Id && !fight.OverturnedNc;
                if (overturned) Overturn(world, fight, date);

                News.Post(world, date, NewsCategory.Suspension,
                    $"{f.FullName} suspended for a failed test",
                    (overturned ? $"The win is GONE — the result is overturned to a No Contest. " : "") +
                    $"Out until {f.SuspendedUntil:MMM yyyy}.", ev.Promotion);
            }
        }
    }

    // Strike the result from the books: a No Contest for both corners. The
    // original outcome is kept on the fight (it happened as it happened) — only
    // the LEDGER of wins and losses changes, and the streaks reset to the NC.
    internal static void Overturn(World world, Fight fight, DateOnly date)
    {
        if (fight.Outcome is not { Winner: { } winId, Loser: { } loseId } o) return;
        fight.OverturnedNc = true;

        if (world.Get(winId) is { } winner)
        {
            var rec = winner.Record;
            rec.Wins = Math.Max(0, rec.Wins - 1);
            switch (o.Method)
            {
                case FightMethod.Knockout or FightMethod.TechnicalKnockout:
                    rec.WinsByKo = Math.Max(0, rec.WinsByKo - 1); break;
                case FightMethod.SubmissionTap or FightMethod.TechnicalSubmission:
                    rec.WinsBySubmission = Math.Max(0, rec.WinsBySubmission - 1); break;
                case FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision:
                    rec.WinsByDecision = Math.Max(0, rec.WinsByDecision - 1); break;
            }
            rec.NoContests++;
            rec.CurrentStreak = 0;   // the freshest line on the record now reads NC
            winner.Momentum = Math.Clamp(winner.Momentum - 15, -40, 40);
        }
        if (world.Get(loseId) is { } loser)
        {
            loser.Record.Losses = Math.Max(0, loser.Record.Losses - 1);
            loser.Record.NoContests++;
            loser.Record.CurrentStreak = 0;
        }
    }

    /// <summary>
    /// The judges get one wrong — or close enough that the crowd decides they
    /// did. A tight decision has a real chance of going down as a ROBBERY:
    /// instant bad blood (the grudge machinery sells the rematch), sympathy
    /// swings to the loser, and the division argues about it for weeks.
    /// </summary>
    private static void Controversies(World world, Event ev, DateOnly date, SimRandom rng)
    {
        foreach (var fight in ev.Card)
        {
            if (fight.Outcome is not { Winner: { } winId, Loser: { } loseId } o) continue;
            bool close = o.Method is FightMethod.SplitDecision or FightMethod.MajorityDecision
                || (o.Method == FightMethod.UnanimousDecision && o.RoundScores is { } rs
                    && Math.Abs(rs.Sum(r => Math.Sign(r.Red - r.Blue))) <= 1);
            if (!close || !rng.Chance(0.12)) continue;
            if (world.Get(winId) is not { } winner || world.Get(loseId) is not { } loser) continue;

            Heat(world, winId, loseId, date, 25);   // "everyone knows I won that fight"
            loser.Intangibles.Popularity = Math.Min(100, loser.Intangibles.Popularity.Value + 3);
            loser.Morale = Math.Clamp(loser.Morale - 4, 0, 100);
            winner.Intangibles.Popularity = Math.Max(3, winner.Intangibles.Popularity.Value - 1);

            News.Post(world, date, NewsCategory.Result,
                $"ROBBERY? {winner.FullName}'s decision over {loser.FullName} disputed",
                "The scorecards say one thing, the room said another. The loser's stock just went UP — and the rematch sells itself.",
                ev.Promotion);
        }
    }

    /// <summary>
    /// End-of-year honours, posted when the calendar rolls over: Fight, Knockout,
    /// Submission, Event, Fighter and Rookie of the Year. Winners carry the award
    /// on their profile forever (<see cref="Fighter.Accolades"/>) plus a small
    /// popularity bump; Event of the Year lifts its promotion's prestige.
    /// </summary>
    public static void YearEndAwards(World world, DateOnly date)
    {
        int year = date.Year - 1;
        // Promotion cards only: the sport's honours don't go to club shows
        // (the promotion-less local circuit — where two gasless brawlers can
        // rate "exciting" every single month).
        var completed = world.Events.Values
            .Where(e => e.Status == EventStatus.Completed && e.Date.Year == year
                        && world.Get(e.Promotion) is not null)
            .ToList();
        var fights = completed
            .SelectMany(e => e.Card.Select(f => (Event: e, Fight: f)))
            .Where(x => x.Fight.Outcome is not null)
            .ToList();

        // Fight of the Year — both fighters share the honour.
        var foty = fights.OrderByDescending(x => Flavor.MatchRating.Of(x.Fight))
            .ThenBy(x => x.Fight.Id.Value).FirstOrDefault();
        if (foty.Fight is not null &&
            world.Get(foty.Fight.RedCorner) is { } fr && world.Get(foty.Fight.BlueCorner) is { } fb)
        {
            Honour(fr, date, $"{year} Fight of the Year (vs {fb.LastName})", pop: 3);
            Honour(fb, date, $"{year} Fight of the Year (vs {fr.LastName})", pop: 3);
            News.Post(world, date, NewsCategory.Award,
                $"Fight of the Year: {fr.FullName} vs {fb.FullName}",
                $"{Flavor.MatchRating.StarsFor(foty.Fight)} at {foty.Event.Name}.", foty.Event.Promotion, aboutEvent: foty.Event.Id);
        }

        BestFinish(world, date, year, fights,
            new[] { FightMethod.Knockout, FightMethod.TechnicalKnockout }, "Knockout of the Year");
        BestFinish(world, date, year, fights,
            new[] { FightMethod.SubmissionTap, FightMethod.TechnicalSubmission }, "Submission of the Year");

        // Event of the Year — the card of the season; the host's stock rises.
        var eoty = completed.Where(e => e.Card.Count(f => f.Outcome is not null) >= 3)
            .OrderByDescending(e => e.Card.Where(f => f.Outcome is not null).Average(f => (double)Flavor.MatchRating.Of(f)))
            .ThenBy(e => e.Id.Value).FirstOrDefault();
        if (eoty is not null && world.Get(eoty.Promotion) is { } host)
        {
            // Route the bump through Standing so a tier crossing is announced too.
            Org.Standing.Apply(world, host, +1, date, $"after Event of the Year ({eoty.Name})");
            News.Post(world, date, NewsCategory.Award,
                $"Event of the Year: {eoty.Name}", $"The card of {year}.", eoty.Promotion, aboutEvent: eoty.Id);
            if (eoty.Promotion == world.PlayerPromotion)
                Mail.Post(world, date, MailCategory.Milestone, Mail.Owner(world),
                    $"Event of the Year: {eoty.Name}", "The whole sport is talking about our show. Great for business.",
                    aboutEvent: eoty.Id);
        }

        // Fighter of the Year — the pound-for-pound best.
        if (world.PoundForPound.Count > 0 && world.Get(world.PoundForPound[0]) is { } best)
        {
            Honour(best, date, $"{year} Fighter of the Year", pop: 4);
            News.Post(world, date, NewsCategory.Award,
                $"Fighter of the Year: {best.FullName}", $"The pound-for-pound best of {year}.",
                best.Contract?.Promotion ?? default, best.Id);
            if (best.Contract?.Promotion == world.PlayerPromotion)
                Mail.Post(world, date, MailCategory.Milestone, Mail.Owner(world),
                    $"{best.FullName} is Fighter of the Year", "One of ours on top of the sport.",
                    aboutFighter: best.Id);
        }

        // Rookie of the Year — the young debutant with the biggest first season.
        var eventDateByFight = world.Events.Values
            .SelectMany(e => e.Card.Select(f => (f.Id, e.Date)))
            .ToDictionary(x => x.Id, x => x.Date);
        var rookie = world.Fighters.Values
            .Where(f => f.Status != FighterStatus.Retired && f.AgeOn(date) <= 24 && f.BoutHistory.Count > 0)
            .Where(f => eventDateByFight.TryGetValue(f.BoutHistory[0], out var d0) && d0.Year == year)
            .OrderByDescending(f => f.BoutHistory.Count(b => eventDateByFight.TryGetValue(b, out var d) && d.Year == year))
            .ThenByDescending(FighterAbility.CurrentAbility).ThenBy(f => f.Id.Value)
            .FirstOrDefault();
        if (rookie is not null)
        {
            Honour(rookie, date, $"{year} Rookie of the Year", pop: 3);
            News.Post(world, date, NewsCategory.Award,
                $"Rookie of the Year: {rookie.FullName}", $"The breakout debutant of {year}.",
                rookie.Contract?.Promotion ?? default);
        }
    }

    private static void BestFinish(World world, DateOnly date, int year,
        List<(Event Event, Fight Fight)> fights, FightMethod[] methods, string award)
    {
        var pick = fights.Where(x => methods.Contains(x.Fight.Outcome!.Method))
            .OrderByDescending(x => Flavor.MatchRating.Of(x.Fight)).ThenBy(x => x.Fight.Id.Value)
            .FirstOrDefault();
        if (pick.Fight?.Outcome?.Winner is { } wid && world.Get(wid) is { } w)
        {
            Honour(w, date, $"{year} {award}", pop: 2);
            News.Post(world, date, NewsCategory.Award,
                $"{award}: {w.FullName}", $"At {pick.Event.Name}.", pick.Event.Promotion);
        }
    }

    /// <summary>An award lands on the profile forever and lifts the name a little.</summary>
    private static void Honour(Fighter f, DateOnly date, string title, int pop)
    {
        f.Accolades.Add(new Accolade(date, title));
        f.Intangibles.Popularity = f.Intangibles.Popularity.Value + pop;
    }

    private static Rivalry? Between(World world, FighterId a, FighterId b) =>
        world.Rivalries.FirstOrDefault(r => r.IsBetween(a, b));

    /// <summary>Whether two fighters have live bad blood (for gate/hype boosts).</summary>
    public static Rivalry? RivalryBetween(World world, FighterId a, FighterId b) => Between(world, a, b);

    private static bool IsFinish(FightMethod m) => m is
        FightMethod.Knockout or FightMethod.TechnicalKnockout or
        FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;
}
