using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>What happened while time moved forward — a running tally for the caller/UI.</summary>
public sealed class WorldTickReport
{
    public DateOnly Date { get; set; }
    public int EventsRun { get; set; }
    public int Fights { get; set; }
    public int Finishes { get; set; }
    public int Injuries { get; set; }
    public int Healed { get; set; }
    public int Retired { get; set; }
    public int Washouts { get; set; }
    public int Newgens { get; set; }
    public int FreeAgentSignings { get; set; }
    public int Cuts { get; set; }
    public int LocalFights { get; set; }

    /// <summary>Set when an advance stopped because one of the player's cards came due.</summary>
    public Event? PendingPlayerEvent { get; set; }

    /// <summary>
    /// Set when an advance stopped because one of the player's cards lost its
    /// main event (a withdrawal, a retirement, a fighter released or signed
    /// away) — the mail says who and why; the card needs a new headliner.
    /// </summary>
    public Event? MainEventLost { get; set; }

    /// <summary>
    /// Set when a rival's offer for one of the player's fighters needs him now: one
    /// just made for a champion or a top-billed fighter, or a pending one entering
    /// its final week (Contracts.OfferMarket). Offers for anyone else go to the Desk
    /// and the inbox without stopping the clock.
    /// </summary>
    public RivalOffer? OfferNeedsYou { get; set; }

    public void Add(WorldTickReport o)
    {
        EventsRun += o.EventsRun; Fights += o.Fights; Finishes += o.Finishes;
        Injuries += o.Injuries; Healed += o.Healed; Retired += o.Retired; Washouts += o.Washouts;
        Newgens += o.Newgens; FreeAgentSignings += o.FreeAgentSignings; Cuts += o.Cuts; LocalFights += o.LocalFights;
        OfferNeedsYou ??= o.OfferNeedsYou;
        Date = o.Date;
    }
}

/// <summary>
/// Advances the world in weekly steps and makes it live: promotions run events
/// on a staggered cadence, fighters develop and age month to month, the hurt get
/// injured and heal, the old retire, and a fresh intake of prospects keeps the
/// talent pool renewing. The whole thing is deterministic from the world seed
/// and current date, so a run reproduces exactly.
/// </summary>
public sealed class WorldTick
{
    private const int NewgenBaseline = 4;      // young intake beyond replacing outflow — the base of the pyramid stays wide

    private readonly Matchmaker _matchmaker;
    private readonly EventService _events;

    public WorldTick(FightEngine? engine = null)
    {
        _matchmaker = new Matchmaker();
        _events = new EventService(engine);
    }

    public WorldTickReport AdvanceWeeks(World world, int weeks)
    {
        var total = new WorldTickReport { Date = world.CurrentDate };
        for (int i = 0; i < weeks; i++) total.Add(AdvanceWeek(world));
        return total;
    }

    /// <summary>
    /// Advance up to <paramref name="weeks"/> weeks but stop the moment one of the
    /// player's booked cards comes due — leaving it unrun so the player can watch it.
    /// The stopped-on card is reported in <see cref="WorldTickReport.PendingPlayerEvent"/>.
    /// </summary>
    public WorldTickReport AdvanceWeeksStoppingForPlayer(World world, int weeks)
    {
        var total = new WorldTickReport { Date = world.CurrentDate };
        bool players = world.Get(world.PlayerPromotion) is not null;
        for (int i = 0; i < weeks; i++)
        {
            // The main event each of the player's cards is headed by going into the week.
            var mains = !players ? new List<(Event Card, FightId Main)>() : world.Events.Values
                .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == world.PlayerPromotion && e.Card.Count > 0)
                .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
                .Select(e => (Card: e, Main: e.Card[^1].Id))
                .ToList();

            var week = AdvanceWeek(world, runPlayerEvents: false);
            total.Add(week);
            var pending = !players ? null : world.Events.Values
                .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == world.PlayerPromotion && e.Date <= world.CurrentDate)
                .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
                .FirstOrDefault();
            if (pending is not null) { total.PendingPlayerEvent = pending; break; }

            // Time stops when one of the player's MAIN EVENTS falls out (a
            // withdrawal, a retirement, a fighter lost): the card needs a decision.
            var lost = mains.FirstOrDefault(m => m.Card.Status == EventStatus.Scheduled && m.Card.Card.All(f => f.Id != m.Main)).Card;
            if (lost is not null) { total.MainEventLost = lost; break; }

            // ...and when a rival's offer for one of his fighters needs an answer now.
            if (week.OfferNeedsYou is not null) break;
        }
        return total;
    }

    public WorldTickReport AdvanceWeek(World world, bool runPlayerEvents = true)
    {
        var before = world.CurrentDate;
        var after = before.AddDays(7);
        world.CurrentDate = after;

        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)after.DayNumber));
        var report = new WorldTickReport { Date = after };

        report.Healed = Lifecycle.HealReturning(world, after);
        // The club shows the local circuit staged for this week's dates (no RNG:
        // their rolls were made when they were staged; the bouts seed off fight ids).
        report.LocalFights += LocalCircuit.RunDue(world, after);
        // The world's clock: AI promotions announce their shows weeks ahead,
        // fill the undercards as the dates close in, and take anyone who has
        // left, retired or been suspended off the cards they were booked on.
        ForwardCalendar.RunWeekly(world, after, _matchmaker, _events);

        // The weeks before a card have their own stories: camp injuries force
        // withdrawals (and short-notice saves), the press tours sell the big
        // fights, and any public bidding war over a star escalates.
        Storylines.RollCampWithdrawals(world, after, rng);
        PressTour.RunWeekly(world, after, rng);
        BiddingWars.RunWeekly(world, after, rng);

        RunDueEvents(world, after, rng, report, runPlayerEvents);

        // Rival offers for the player's fighters on their last fight: answered,
        // timed out, or taken the day a window closes on a let-go one; and new
        // calls (no RNG stream: keyed hash rolls). Before the windows run, so a
        // closing window with an offer standing goes to the rival, not the market.
        report.OfferNeedsYou = OfferMarket.RunWeekly(world, after);

        // The player's deals that have been fought out: windows opened, the last
        // week warned, the run-out ones closed onto the open market (no RNG).
        DealWindow.RunWeekly(world, after);

        // The week's paper: one digest mail with the stories, the calendar,
        // and a prospect worth circling.
        Digest.Weekly(world, after);

        // Monthly: development, retirements, newgen intake, roster restocking.
        if (after.Month != before.Month)
        {
            // Development runs through the ROOM a fighter trains in (their camp),
            // with the player org's coaching staff stacking on top for their own
            // roster — capped so the two levers can't compound into inflation.
            double coach = world.Get(world.PlayerPromotion) is { } pp ? Org.StaffService.CoachGrowthMultiplier(pp) : 1.0;
            foreach (var f in world.Fighters.Values)
            {
                double mult = Camps.GrowthMultiplier(world, f)
                            * (f.Contract?.Promotion == world.PlayerPromotion ? coach : 1.0);
                FighterDevelopment.Advance(f, after, rng, Math.Min(mult, 1.35));
            }

            report.Retired = Lifecycle.RollRetirements(world, after, rng);
            report.Washouts = Lifecycle.RollWashouts(world, after, rng);
            // Intake tracks outflow so the population self-balances instead of
            // ballooning — and because washouts prune the failures EARLY while
            // the intake arrives green, the whole pool tilts young: a wide base
            // of low-fight-count kids, filtered hard by their records. The
            // growth baseline follows OPPORTUNITY: when a quarter of the sport
            // is already unsigned, fewer people turn pro (the unchecked
            // baseline grew the free-agent pool without bound).
            int actives = 0, unsigned = 0;
            foreach (var f in world.Fighters.Values)
            {
                if (f.Status != FighterStatus.Active || f.LocalTalent) continue;
                actives++;
                if (f.Contract is null) unsigned++;
            }
            int baseline = unsigned > actives / 4 ? 1 : NewgenBaseline;
            int intake = report.Retired + report.Washouts + baseline;
            report.Newgens = NewgenService.ForWorld(world).GenerateIntake(world, intake, rng).Count;

            // Expiring deals renew / get poached / hit the market (the player's own
            // are courted weekly by OfferMarket instead), orgs trim the fighters
            // they no longer want, then everyone fills their gaps.
            Lifers.RunMonthly(world, after);   // standing grievances wear lifers down (no RNG)
            ContractLifecycle.ProcessExpiries(world, after);
            report.Cuts = RosterManagement.RunMonthly(world, after);
            report.FreeAgentSignings = FreeAgencyService.RunMonthly(world, after);

            // The scenes beyond the promotions: idle fighters take local fights,
            // and the wider combat-sports calendar (Olympics, world championships).
            LocalCircuit.RunMonthly(world, after, rng);   // staged now, fought on their dates (report.LocalFights counts them then)
            SpecialCircuit.RunMonthly(world, after, rng);

            // Off-cage drama: feuds flare on social media, fresh beefs start, and
            // bad blood that can't be settled cools off instead of simmering forever.
            Storylines.MonthlyDrama(world, after, rng);
            Storylines.MaybeSuperfight(world, after, rng);   // a dominant champ moves up: champ vs champ
            Acquisitions.RunMonthly(world, after, rng);      // the market consolidates: a giant buys a bleeding rival
            Decisions.RunMonthly(world, after, rng);         // the owner's desk: calls awaiting the player

            // And the warm side: friendships form (camp mates hardest) and deepen,
            // and head coaches take the room's best prospect under their wing.
            Bonds.RunMonthly(world, after, rng);
            Camps.RunMonthly(world, after, rng);

            // A promotion that goes dark slips out of the conversation — its fame
            // fades a touch each month it doesn't put on a card. And a fighter left
            // on the shelf sours on the org that isn't using them.
            DecayIdleOrgReputation(world, after);
            DecayNeglectedLoyalty(world, after);

            // MMA interest decays toward each place's baseline, and a homegrown star
            // props up interest in their home region.
            Geography.MmaInterest.Tick(world);

            // Recurring income (sponsors, broadcast, merch), then keeping the lights on.
            Finance.RevenueService.CollectMonthly(world, after);
            Org.AiFrontOffice.RunMonthly(world, after);      // rivals set marketing budgets, retain & build talent
            Org.MarketingService.RunMonthly(world, after);   // active fighter promo pushes grow draws + bill
            Org.OrgMarketing.ChargeMonthly(world, after);    // bill every promotion's brand campaign
            Camps.ChargeMonthly(world, after);               // bill camp stipends + team deals (loyalty back)
            Finance.Ledger.ChargeMonthlyOverhead(world, after);

            // With the month's money settled, anyone past the debt ceiling folds.
            Org.Solvency.RunMonthly(world, after);

            // A brand that hasn't run in months fades — prestige slips until it books again.
            Org.Standing.MonthlyDrift(world, after);

            // Offer eligible orgs a step up the ladder (player decides; AI promotes only
            // when it can afford the bigger stage, so it doesn't overextend and fold).
            Org.PromotionLadder.RunMonthly(world, after);

            // Faces of the company: catch a lost face (crisis) and let AI orgs crown one.
            Org.Franchise.RunMonthly(world, after);

            // The macro living world: crossover stars arrive, new promotions launch.
            WorldEvents.RunMonthly(world, after, rng);

            // Nudge the player about expiring deals and the state of the books.
            Mail.MonthlyReview(world, after);

            // Owner objectives + year-end honours when the calendar rolls over.
            if (after.Year != before.Year)
            {
                if (world.Get(world.PlayerPromotion) is { } py)
                    Org.Objectives.Seed(py, new DateOnly(after.Year, 1, 1), new DateOnly(after.Year, 12, 31));
                Storylines.YearEndAwards(world, after);
            }
            Org.Objectives.Evaluate(world, after);

            // Refresh the cross-org and pound-for-pound rankings.
            GlobalRankings.Recompute(world);
        }

        // Whatever changed tonight comes off the calendar tonight, not next
        // week: the month's exits (expiries, cuts, retirements, sales, closures)
        // and the week's own (a belt passed on when a champion is suspended
        // after a card, a signing out of a bidding war).
        ForwardCalendar.Audit(world, after);
        LocalCircuit.Audit(world);

        // The week has moved the world: a rival offer's Desk call shows today's match
        // price (his last fight fought, his record, his people) — the number Match signs.
        OfferMarket.RefreshDesk(world);

        return report;
    }

    // A promotion that hasn't run a card in this long starts to fade from the
    // public eye; each idle month past it costs a little standing.
    private const int IdleReputationDays = 50;

    private static void DecayIdleOrgReputation(World world, DateOnly date)
    {
        // When each promotion last staged a card.
        var lastEvent = new Dictionary<PromotionId, DateOnly>();
        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Completed) continue;
            if (!lastEvent.TryGetValue(ev.Promotion, out var d) || ev.Date > d)
                lastEvent[ev.Promotion] = ev.Date;
        }

        foreach (var promo in world.Promotions.Values)
        {
            if (promo.Defunct) continue;
            // A promotion that has never run yet hasn't built anything to lose.
            if (!lastEvent.TryGetValue(promo.Id, out var last)) continue;
            if (date.DayNumber - last.DayNumber >= IdleReputationDays)
                Geography.Reputation.DecayOrg(promo, 1);
        }
    }

    // A contracted fighter who hasn't been booked in this long starts to sour on
    // the org that's leaving them idle — set beyond a normal booking rotation, so
    // only genuine neglect (benched for months) erodes loyalty, not routine gaps.
    private const int LoyaltyNeglectDays = 120;

    internal static void DecayNeglectedLoyalty(World world, DateOnly date)
    {
        var lastFought = new Dictionary<FighterId, DateOnly>();
        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Completed) continue;
            foreach (var fight in ev.Card)
                foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                    if (!lastFought.TryGetValue(id, out var d) || ev.Date > d) lastFought[id] = ev.Date;
        }

        foreach (var f in world.Fighters.Values)
        {
            if (f.Contract is not { } c || f.Status != FighterStatus.Active) continue;
            // Neglect is measured from when they were last AVAILABLE and unused —
            // not from their last bout. A star healing from injury or in age-scaled
            // post-fight recovery isn't being benched, so the clock only starts once
            // they're cleared to fight (ReadyToFightOn) or from the day they signed.
            int availableSince = lastFought.TryGetValue(f.Id, out var last) ? last.DayNumber : c.SignedOn.DayNumber;
            if (f.ReadyToFightOn is { } ready) availableSince = Math.Max(availableSince, ready.DayNumber);
            availableSince = Math.Max(availableSince, c.SignedOn.DayNumber);
            if (date.DayNumber - availableSince < LoyaltyNeglectDays) continue;
            // A lifer left on the shelf long enough to hold it against the org
            // (Contracts.Lifers' Benched grievance, from 180 days) is worn down
            // by that grievance instead — one bite a month, never two.
            if (Lifers.IsLifer(world, f)
                && Lifers.GrievanceOf(world, f, date)?.Kind == Lifers.GrievanceKind.Benched) continue;
            // How hard being benched bites is a matter of character: the restless
            // are already taking calls, the company man assumes there's a reason.
            // (A modifier on the existing decay, not a second drift — an
            // unconditional monthly nudge would be ±12 loyalty a year and would
            // swamp everything else the loyalty system does.)
            int bite = f.Temperament.Ambition.Value >= 70 ? 3
                     : f.Temperament.Ambition.Value <= 30 ? 1
                     : 2;
            f.Loyalty = Math.Max(0, f.Loyalty - bite);
        }
    }

    // Run every scheduled event whose date has arrived — AI cards booked this
    // tick and any the player has scheduled for now or earlier.
    private void RunDueEvents(World world, DateOnly asOf, SimRandom rng, WorldTickReport report, bool runPlayerEvents)
    {
        var due = world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Date <= asOf
                        && !LocalCircuit.IsLocalCard(e)                                  // the club shows run themselves (LocalCircuit.RunDue)
                        && (runPlayerEvents || e.Promotion != world.PlayerPromotion))   // player watches their own
            // The player's cards run first on their night, as they always did
            // when AI cards were created on the day (and so always had the
            // higher ids): an AI card announced weeks ago must not move the
            // player's card along the week's shared stream.
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Promotion == world.PlayerPromotion ? 0 : 1)
            .ThenBy(e => e.Id.Value)
            .ToList();

        foreach (var ev in due)
            RunEventCore(world, ev, rng, playByPlay: false, report);
    }

    /// <summary>
    /// Run one due event with the full match-day pipeline: fight-week pullouts,
    /// the card itself, injuries, and the storyline beats. Shared by the tick and
    /// the player's "run it live" path. Fight outcomes are seeded per-fight, so a
    /// live run produces exactly the same results the auto-advance would have.
    /// </summary>
    private void RunEventCore(World world, Event ev, SimRandom rng, bool playByPlay, WorldTickReport? report)
    {
        bool aiCard = ev.Promotion != world.PlayerPromotion && world.Get(ev.Promotion) is not null;
        if (ev.Card.Count == 0)
        {
            // An announced AI card that lost everything is a collapse (standing
            // hit and a headline); an empty card nobody booked is a non-event.
            if (aiCard) ForwardCalendar.Collapse(world, ev, ev.Date);
            else ev.Status = EventStatus.Cancelled;
            return;
        }
        // Fight night's last word on an AI card: only fighters signed to the
        // promotion (or its local filler), none retired, suspended or booked
        // elsewhere, and no belt on a bout it can't be on.
        var announcedMain = ev.Card[^1];
        ForwardCalendar.GuardAtRun(world, ev);
        Storylines.RollPullouts(world, ev, ev.Date, rng);
        // AI cards: only a main event that actually came off the card is replaced
        // at the top of the bill (an announced feud headliner stays above a belt).
        if (aiCard && ev.Card.Count > 0 && !ev.Card.Contains(announcedMain)) ForwardCalendar.RestoreHeadliner(world, ev);
        Storylines.RollWeightMisses(world, ev, ev.Date, rng);   // weigh-in day: fines, catchweights, stripped champs
        if (ev.Card.Count == 0)
        {
            // A promoted card that collapsed to nothing is a real black eye.
            if (aiCard) { ForwardCalendar.Collapse(world, ev, ev.Date); return; }
            ev.Status = EventStatus.Cancelled;
            if (world.Get(ev.Promotion) is { } collapsed) Org.Standing.OnCancelled(world, collapsed, ev);
            return;
        }

        _events.RunEvent(world, ev, playByPlay);
        int injuries = InjuryModel.RollForEvent(world, ev, ev.Date, rng);
        Storylines.AfterEvent(world, ev, ev.Date, rng);
        Bonds.AfterEvent(world, ev, ev.Date);   // a bout between friends/family leaves a mark
        Camps.AfterEvent(world, ev, ev.Date);   // teammates colliding + a protégé's golden night
        // A belt that changed hands tonight may sit on a later card of the player's
        // without its champion: put his other cards right now, not at the next tick.
        if (ev.Promotion == world.PlayerPromotion) ForwardCalendar.AuditPlayerCards(world, ev.Date);

        if (report is not null)
        {
            report.EventsRun++;
            report.Fights += ev.Card.Count;
            report.Finishes += ev.Card.Count(f => f.Outcome is { } o && IsFinish(o.Method));
            report.Injuries += injuries;
        }
    }

    /// <summary>Run a scheduled event now (optionally with play-by-play), so the player can watch it or run it on the spot.</summary>
    public void RunEventLive(World world, Event ev, bool playByPlay = true)
    {
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, 0xE0E0_0000UL ^ (ulong)ev.Id.Value));
        RunEventCore(world, ev, rng, playByPlay, report: null);
    }

    private static bool IsFinish(FightMethod m) => m is
        FightMethod.Knockout or FightMethod.TechnicalKnockout or
        FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;
}
