using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace LongSim.Check;

/// <summary>
/// A scripted human. It plays a player promotion through the same calls the WPF view models make
/// (BookingViewModel, ContractsViewModel/NegotiationViewModel, DashboardViewModel desk buttons, FinanceViewModel and
/// MediaViewModel offer buttons, MyRosterViewModel/MainViewModel fighter actions, CampsViewModel, OrgViewModel,
/// MainViewModel.Advance/SimPendingEvent/RunLive) and deliberately takes the awkward orders a human can take.
///
/// It is stateless between weeks except for a short action log: every choice is drawn from a stream keyed on the world
/// seed, the in-game date and the name of the routine (never the sim's own streams), so a run reproduces from the seed
/// and a loaded twin makes the same choices as the world it was saved from.
/// </summary>
public sealed class PlayerScript
{
    private readonly string _run;
    private readonly ViolationLog _log;
    private readonly bool _reckless;           // plays the books into the ground on purpose
    private readonly PlayerBooking _booking = new();
    private readonly FighterValuation _valuation = new();
    private readonly List<string> _actions = new();

    public PlayerScript(string run, ViolationLog log, bool reckless)
    {
        _run = run; _log = log; _reckless = reckless;
        foreach (var (n, d) in PlayCatalog) log.Declare(n, d);
    }

    public static readonly (string Name, string Desc)[] PlayCatalog =
    {
        ("PLAY-LEDGER",         "no UI action or weekly advance moves a promotion's balance without an equal ledger line"),
        ("PLAY-RAN-UNAVAILABLE","a due card never runs a bout with a retired, unrostered or (live path) injured/suspended fighter without the sim pulling the bout"),
        ("PLAY-EVENT-GONE",     "a cancelled card leaves the world; a card the player did not cancel never silently disappears"),
    };

    public IReadOnlyList<string> Actions => _actions;
    public string Recent(int n = 8) => string.Join(" | ", _actions.TakeLast(n));
    public int ActionsTaken { get; private set; }
    public bool ReachedBankruptcy { get; private set; }
    public readonly Dictionary<string, int> Counts = new();

    private void Note(World w, string text)
    {
        _actions.Add($"{w.CurrentDate:yyyy-MM-dd} {text}");
        if (_actions.Count > 60) _actions.RemoveAt(0);
        ActionsTaken++;
    }
    private void Count(string k) => Counts[k] = Counts.GetValueOrDefault(k) + 1;

    private SimRandom Roll(World w, string salt)
    {
        ulong h = 1469598103934665603UL;
        foreach (char c in salt) h = (h ^ c) * 1099511628211UL;
        return new SimRandom(SimRandom.Mix(w.RngSeed ^ 0x51A7_C0DE_5EEDUL, h ^ (ulong)w.CurrentDate.DayNumber * 0x9E3779B97F4A7C15UL));
    }

    private static Promotion? Player(World w) => w.Get(w.PlayerPromotion);

    // ------------------------------------------------------------ ledger watch

    private Dictionary<PromotionId, (decimal Bal, int Count)> _snap = new();

    private void Snapshot(World w)
    {
        _snap = new();
        foreach (var p in w.Promotions.Values) _snap[p.Id] = (p.Balance.Amount, p.Ledger.Count);
    }

    private void VerifyLedger(World w, string what)
    {
        foreach (var p in w.Promotions.Values)
        {
            if (!_snap.TryGetValue(p.Id, out var s)) continue;
            if (p.Ledger.Count < s.Count) continue;
            decimal added = 0m;
            for (int i = s.Count; i < p.Ledger.Count; i++) added += p.Ledger[i].Amount.Amount;
            decimal moved = p.Balance.Amount - s.Bal;
            if (moved != added)
                _log.Report("PLAY-LEDGER", _run, w.CurrentDate, $"{what}:{p.Id.Value}",
                    $"{what} moved {p.Abbreviation} #{p.Id.Value}'s balance by {moved:N2} but its ledger gained {added:N2} ({p.Ledger.Count - s.Count} entries). Recent: {Recent(5)}");
        }
    }

    /// <summary>Run one UI action under a ledger watch; an exception is a finding, not a stop.</summary>
    private void Act(World w, string name, Action a)
    {
        Snapshot(w);
        Note(w, name);
        Count(name.Split(' ')[0]);
        try { a(); }
        catch (Exception e)
        {
            _log.Report("CRASH", _run, w.CurrentDate, $"act:{name.Split(' ')[0]}:{e.GetType().Name}:{FirstFrame(e)}",
                $"{e.GetType().Name} in action '{name}': {e.Message} @ {FirstFrame(e)}. Recent: {Recent(8)}");
        }
        VerifyLedger(w, name.Split(' ')[0]);
    }

    public static string FirstFrame(Exception e)
    {
        var lines = (e.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("at MmaManager") || l.StartsWith("at LongSim")).Take(2);
        return string.Join(" <- ", lines.Select(l => l.Length > 140 ? l[..140] : l));
    }

    // ---------------------------------------------------------------- the week

    /// <summary>One week of play. Plans at the start of each month, then advances along a path chosen from the date.</summary>
    public void Week(World w, WorldTick tick)
    {
        var promo = Player(w);
        if (promo is null || promo.Defunct)
        {
            if (promo is { Defunct: true }) ReachedBankruptcy = true;
            tick.AdvanceWeek(w);
            return;
        }

        if (w.CurrentDate.Day <= 7) MonthlyPlan(w, promo);
        Advance(w, tick, promo);
    }

    private void Advance(World w, WorldTick tick, Promotion promo)
    {
        var r = Roll(w, "advance");
        bool live = r.Chance(0.5);

        // Anything already due is run before time moves (the UI refuses to advance past a due card).
        RunDue(w, tick, live);
        if (Player(w) is not { Defunct: false }) return;

        Snapshot(w);
        if (!live)
        {
            // The tick path: the world runs the player's card itself when its week comes.
            var due = PlayerCardsDueBy(w, w.CurrentDate.AddDays(7)).ToList();
            var before = due.ToDictionary(e => e.Id, e => Problems(w, e, live: false));
            Note(w, "advance-tick");
            try { tick.AdvanceWeek(w); }
            catch (Exception) { throw; }   // sim crashes surface to the runner with this script's recent actions
            VerifyLedger(w, "advance-tick");
            foreach (var e in due) AfterRun(w, e, before[e.Id], "tick");
        }
        else
        {
            Note(w, "advance-stopping");
            tick.AdvanceWeeksStoppingForPlayer(w, 1);
            VerifyLedger(w, "advance-stopping");
            RunDue(w, tick, live: true);
        }
    }

    private static IEnumerable<Event> PlayerCardsDueBy(World w, DateOnly date) =>
        w.Events.Values.Where(e => e.Status == EventStatus.Scheduled && e.Promotion == w.PlayerPromotion && e.Date <= date)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value);

    /// <summary>MainViewModel.SimPendingEvent / RunLive: run every card that has come due, in order.</summary>
    private void RunDue(World w, WorldTick tick, bool live)
    {
        for (int guard = 0; guard < 6; guard++)
        {
            var ev = PlayerCardsDueBy(w, w.CurrentDate).FirstOrDefault();
            if (ev is null) return;
            var problems = Problems(w, ev, live: true);
            bool withPbp = new SimRandom(SimRandom.Mix(w.RngSeed, (ulong)ev.Id.Value ^ 0xAB1EUL)).Chance(0.3);
            Snapshot(w);
            Note(w, $"run-live {ev.Name} pbp={withPbp}");
            Count("run-live");
            tick.RunEventLive(w, ev, playByPlay: withPbp);
            VerifyLedger(w, "run-live");
            AfterRun(w, ev, problems, "live");
        }
    }

    private sealed record Problem(FightId Fight, FighterId Fighter, string Why);

    private static List<Problem> Problems(World w, Event ev, bool live)
    {
        var list = new List<Problem>();
        foreach (var fight in ev.Card)
            foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
            {
                var f = w.Get(id);
                string? why = null;
                if (f is null) why = "missing";
                else if (f.Status == FighterStatus.Retired) why = "retired";
                else if (!f.LocalTalent && f.Contract?.Promotion != ev.Promotion)
                    why = f.Contract is null ? "released (a free agent)" : $"signed away to promotion #{f.Contract.Promotion.Value}";
                else if (live && f.Status == FighterStatus.Injured && f.CurrentInjury is { } inj && inj.ExpectedReturn > ev.Date) why = "injured";
                else if (live && f.Status == FighterStatus.Suspended && f.SuspendedUntil is { } su && su > ev.Date) why = "suspended";
                if (why is not null) list.Add(new Problem(fight.Id, id, why));
            }
        return list;
    }

    private void AfterRun(World w, Event ev, List<Problem> before, string path)
    {
        foreach (var p in before)
        {
            var fight = ev.Card.FirstOrDefault(f => f.Id == p.Fight);
            if (fight?.Outcome is null) continue;   // the sim pulled the bout or left it unfought
            var f = w.Get(p.Fighter);
            _log.Report("PLAY-RAN-UNAVAILABLE", _run, ev.Date, $"{ev.Id.Value}:{p.Fight.Value}:{p.Fighter.Value}",
                $"{ev.Name} #{ev.Id.Value} ({path} path) fought bout {p.Fight.Value} with {f?.FullName ?? "?"} #{p.Fighter.Value} who was {p.Why} when the card ran. Recent: {Recent(6)}");
        }
    }

    // ------------------------------------------------------------- the month

    private void MonthlyPlan(World w, Promotion promo)
    {
        var r = Roll(w, "month");
        bool small = OrgSizing.SizeOf(promo) <= OrgSize.Regional;

        // 1. The desk. Lapse many, answer some, answer one AFTER its subject has left.
        foreach (var d in w.Decisions.ToList())
        {
            if (Player(w) is not { Defunct: false }) return;
            if (d.State != DecisionState.Pending) continue;
            double x = r.NextDouble();
            if (x < 0.40) { Note(w, $"desk-lapse {d.Kind} #{d.Id}"); continue; }
            if (x < 0.55 && d.Fighter is { } gone && w.Get(gone) is { } g && g.Contract?.Promotion == promo.Id
                && d.Kind is DecisionKind.VeteranCut or DecisionKind.TitleShotRequest)
            {
                Act(w, $"release-then-answer {g.FullName} #{g.Id.Value}", () => ContractLifecycle.Release(w, g));
                Act(w, $"desk-accept-after-leaving {d.Kind} #{d.Id}", () => Decisions.Accept(w, d, w.CurrentDate));
                continue;
            }
            if (x < 0.78) Act(w, $"desk-accept {d.Kind} #{d.Id}", () => Decisions.Accept(w, d, w.CurrentDate));
            else Act(w, $"desk-decline {d.Kind} #{d.Id}", () => Decisions.Decline(w, d, w.CurrentDate));
        }

        // 2. Offers on the table: take some, refuse some, let the rest expire.
        foreach (var o in promo.Offers.ToList())
        {
            double x = r.NextDouble();
            if (x < 0.45)
            {
                if (o.Media is null) Act(w, $"sponsor-accept {o.Partner}", () => Sponsorships.Accept(w, promo, o, w.CurrentDate));
                else Act(w, $"media-accept {o.Media} {o.Partner}", () => MediaService.Accept(w, promo, o, w.CurrentDate));
            }
            else if (x < 0.60) Act(w, $"offer-decline {o.Partner}", () => promo.Offers.Remove(o));
        }
        if (promo.Stream is null && r.Chance(_reckless ? 0.3 : 0.08) && (_reckless || promo.Balance.Amount > 400_000m))
            Act(w, "launch-stream", () => MediaService.LaunchStream(w, promo, w.CurrentDate));

        // 3. People: renewals, signings, releases.
        Renewals(w, promo, r);
        Signings(w, promo, r, small);
        Releases(w, promo, r);

        // 4. The front office.
        FrontOffice(w, promo, r);

        // 5. Org screen: belts, house rules, marketing budget, the step up.
        OrgDesk(w, promo, r);

        // 6. Cards: build them, rebuild them, cancel the wrong one.
        Cards(w, promo, r, small);

        // 7. Small stuff the UI writes straight to the model.
        if (r.Chance(0.3)) Act(w, "mail-read", () => { foreach (var m in w.Mail.TakeLast(20)) m.Read = true; });
        if (r.Chance(0.1) && w.Fighters.Count > 0)
        {
            var any = w.RosterOf(promo.Id).FirstOrDefault();
            if (any is not null) Act(w, "shortlist-toggle", () => { if (!w.Shortlist.Remove(any.Id)) w.Shortlist.Add(any.Id); });
        }
        if (r.Chance(0.05)) Act(w, "notes", () => w.OwnerNotes = $"note {w.CurrentDate:yyyy-MM}");
    }

    private void Renewals(World w, Promotion promo, SimRandom r)
    {
        foreach (var f in w.RosterOf(promo.Id).Where(f => f.Status != FighterStatus.Retired && f.Contract is { FightsRemaining: <= 1 }).Take(3).ToList())
        {
            if (!r.Chance(0.6)) { Note(w, $"renewal-ignored {f.FullName}"); continue; }
            if (!_reckless && _valuation.Evaluate(w, f).AskingPurse.Amount > Negotiator.PurseCap(w, promo)) { Note(w, $"renewal-too-dear {f.FullName}"); continue; }
            Act(w, $"renew {f.FullName} #{f.Id.Value}", () => Negotiate(w, promo, f, r, allowLowball: false));
        }
    }

    private void Signings(World w, Promotion promo, SimRandom r, bool small)
    {
        int perMonth = small ? 3 : r.Range(0, 2);
        int depth = 4 + (promo.Balance.Amount > 600_000m ? 1 : 0) + (promo.Balance.Amount > 1_500_000m ? 1 : 0);
        decimal maxAsk = _reckless ? 1_000_000m : Math.Min(Negotiator.PurseCap(w, promo), 3_000m + promo.Prestige.Value * 300m + Math.Max(0m, promo.Balance.Amount / 400m));
        for (int i = 0; i < perMonth; i++)
        {
            if (!_reckless && promo.Balance.Amount < 100_000m) return;
            var roster = w.RosterOf(promo.Id).Where(f => f.Status == FighterStatus.Active).ToList();
            var thin = promo.Divisions.Where(d => roster.Count(f => d.Matches(f)) < (small ? depth : depth + 6)).ToList();
            if (thin.Count == 0 && !_reckless) return;
            var pick = w.FreeAgents()
                .Where(f => f.Status == FighterStatus.Active && (thin.Count == 0 || thin.Any(d => d.Matches(f))))
                .Select(f => (F: f, Exp: _valuation.Evaluate(w, f)))
                .Where(x => x.Exp.AskingPurse.Amount <= maxAsk)
                .OrderByDescending(x => FighterAbility.CurrentAbility(x.F) + Math.Max(0, x.F.PotentialAbility.Value - FighterAbility.CurrentAbility(x.F)) * 0.5)
                .ThenBy(x => x.F.Id.Value)
                .Skip(r.Range(0, 3))
                .FirstOrDefault();
            if (pick.F is null) return;
            var f = pick.F;
            Act(w, $"sign {f.FullName} #{f.Id.Value}", () => Negotiate(w, promo, f, r, allowLowball: true));
        }
    }

    /// <summary>The three ways the UI signs someone: the room (session), "their terms" harness, a raw offer. A lowball may walk them out.</summary>
    private void Negotiate(World w, Promotion promo, Fighter f, SimRandom r, bool allowLowball)
    {
        var exp = _valuation.Evaluate(w, f);
        var session = new NegotiationSession(w, f, promo, exp);
        bool excl = Negotiator.WantsExclusivity(promo);
        double path = r.NextDouble();
        if (path < 0.15)
        {
            // straight to the pen at the asking terms (the harness path)
            var offer = new ContractOffer
            {
                Fighter = f.Id, Promotion = promo.Id, PursePerFight = exp.AskingPurse, WinBonus = exp.AskingWinBonus,
                Fights = exp.DesiredFights, SigningBonus = exp.WantsSigningBonus ? new Money(5_000m) : Money.Zero,
            };
            ContractLifecycle.Sign(w, f, offer, w.CurrentDate);
            return;
        }
        if (allowLowball && path < 0.45)
        {
            var low = session.TheirTermsAsOffer(excl, clause: false);
            low.PursePerFight = new Money(Math.Round(low.PursePerFight.Amount * 0.55m));
            var first = session.Table(low);
            if (first.Verdict == TalkVerdict.WalkOut || session.Outcome == TalkOutcome.TheyWalked) return;   // they walked: nothing signed
            if (r.Chance(0.4)) { session.WalkAway(); return; }
        }
        for (int round = 0; round < 6; round++)
        {
            var offer = session.TheirTermsAsOffer(excl, clause: r.Chance(0.15));
            var resp = session.Table(offer);
            if (resp.Verdict == TalkVerdict.Accept) { ContractLifecycle.Sign(w, f, offer, w.CurrentDate); return; }
            if (resp.Verdict == TalkVerdict.WalkOut) return;
        }
        session.WalkAway();
    }

    private void Releases(World w, Promotion promo, SimRandom r)
    {
        var roster = w.RosterOf(promo.Id).Where(f => f.Status != FighterStatus.Retired).OrderBy(f => f.Id.Value).ToList();
        if (roster.Count == 0) return;

        // a champion
        var champs = w.Titles.Values.Where(t => t.Promotion == promo.Id && t.CurrentChampion is not null).OrderBy(t => t.Id.Value).ToList();
        if (champs.Count > 0 && r.Chance(_reckless ? 0.10 : 0.04))
        {
            var t = r.Pick(champs);
            if (w.Get(t.CurrentChampion!.Value) is { } c)
                Act(w, $"release-champion {c.FullName} #{c.Id.Value} of {t.Name}", () => ContractLifecycle.Release(w, c));
        }

        // someone already booked on a future card
        var booked = w.Events.Values.Where(e => e.Promotion == promo.Id && e.Status == EventStatus.Scheduled)
            .SelectMany(e => e.Card).SelectMany(f => new[] { f.RedCorner, f.BlueCorner })
            .Select(id => w.Get(id)).Where(f => f is { LocalTalent: false } && f.Contract?.Promotion == promo.Id).OrderBy(f => f!.Id.Value).ToList();
        if (booked.Count > 0 && r.Chance(_reckless ? 0.12 : 0.05))
        {
            var f = r.Pick(booked)!;
            Act(w, $"release-booked {f.FullName} #{f.Id.Value}", () => ContractLifecycle.Release(w, f));
        }

        if (r.Chance(0.04))
        {
            var f = r.Pick(roster);
            if (f.Contract is not null) Act(w, $"release {f.FullName} #{f.Id.Value}", () => ContractLifecycle.Release(w, f));
        }
    }

    private void FrontOffice(World w, Promotion promo, SimRandom r)
    {
        var roster = w.RosterOf(promo.Id).Where(f => f.Status != FighterStatus.Retired).OrderBy(f => f.Id.Value).ToList();

        if (r.Chance(_reckless ? 0.3 : 0.10) && w.StaffMarket.Count > 0 && (_reckless || promo.Balance.Amount > 2_000_000m) && promo.Staff.Count < 2)
        {
            var cand = w.StaffMarket.OrderBy(s => s.Role).ThenByDescending(s => s.Quality).ThenBy(s => s.Name).Skip(r.Range(0, 4)).FirstOrDefault();
            if (cand is not null) Act(w, $"staff-hire {cand.Role} {cand.Name}", () => StaffService.Hire(w, promo, cand));
        }
        if (r.Chance(0.05) && promo.Staff.Count > 0)
        {
            var s = r.Pick(promo.Staff);
            Act(w, $"staff-fire {s.Role}", () => StaffService.Fire(w, promo, s.Role));
        }
        if (roster.Count == 0) return;

        if (r.Chance(0.10)) { var f = r.Pick(roster); Act(w, $"retention-bonus {f.FullName}", () => RetentionService.TryPayBonus(w, promo, f, out _)); }
        if (r.Chance(0.10)) { var f = r.Pick(roster); Act(w, $"marketing-push {f.FullName}", () => f.MarketingPush = !f.MarketingPush); }
        if (r.Chance(0.08)) { var f = r.Pick(roster); Act(w, $"set-face {f.FullName}", () => Franchise.SetFace(w, promo, f, w.CurrentDate)); }
        if (r.Chance(0.03)) Act(w, "unset-face", () => promo.FaceOfCompany = null);
        if (r.Chance(0.12) && (_reckless || promo.Balance.Amount > 500_000m))
        {
            var f = r.Pick(roster);
            if (f.CampStipend) Act(w, $"stop-stipend {f.FullName}", () => Camps.StopStipend(w, f));
            else Act(w, $"pay-stipend {f.FullName}", () => Camps.PayStipend(w, promo, f));
        }
        if (r.Chance(0.05) && w.Camps.Count > 0 && (_reckless || promo.Balance.Amount > 1_000_000m))
        {
            var camp = w.Camps.Values.OrderBy(c => c.Id.Value).ElementAt(r.Range(0, w.Camps.Count - 1));
            if (promo.SponsoredCamp == camp.Id) Act(w, $"camp-sponsor-end {camp.Name}", () => promo.SponsoredCamp = null);
            else Act(w, $"camp-sponsor {camp.Name}", () => promo.SponsoredCamp = camp.Id);
        }
        if (r.Chance(0.06) && w.Camps.Count > 0)
        {
            var camp = w.Camps.Values.OrderBy(c => c.Id.Value).ElementAt(r.Range(0, w.Camps.Count - 1));
            Act(w, $"camp-report {camp.Name}", () => Camps.BuyReport(w, promo, camp, w.CurrentDate));
        }
    }

    private void OrgDesk(World w, Promotion promo, SimRandom r)
    {
        // belts for divisions we have fighters in but no belt
        if (r.Chance(0.35))
        {
            var titled = promo.Divisions.Where(d => d.Title is not null).Select(d => (d.WeightClass, d.Sex)).ToHashSet();
            var pick = w.RosterOf(promo.Id).Where(f => f.Status != FighterStatus.Retired).Select(f => (f.WeightClass, f.Sex)).Distinct()
                .Where(p => !titled.Contains(p)).OrderBy(p => p.Sex).ThenBy(p => p.WeightClass).Cast<(WeightClass, Sex)?>().FirstOrDefault();
            if (pick is { } p && (_reckless || promo.Balance.Amount >= PromotionSetup.BeltCost))
                Act(w, $"commission-belt {p.Item1}", () => PromotionSetup.CommissionTitle(w, promo, p.Item1, p.Item2, w.CurrentDate));
        }

        if (r.Chance(0.10))
        {
            int[] mins = { 3, 4, 5 }, stds = { 2, 3, 5 }, tits = { 3, 5 };
            Act(w, "house-rules", () =>
            {
                promo.Rules.RoundMinutes = r.Pick(mins); promo.Rules.StandardRounds = r.Pick(stds); promo.Rules.TitleRounds = r.Pick(tits);
                promo.Rules.FightOfNightBonus = new Money(Math.Round(r.Range(0, 20) * 1_000m / 500m) * 500m);
            });
        }
        if (r.Chance(_reckless ? 0.5 : 0.10) && (_reckless || promo.Balance.Amount > 1_000_000m))
        {
            decimal budget = _reckless ? r.Range(5, 40) * 10_000m : r.Range(0, 5) * 10_000m;
            Act(w, $"marketing-budget {budget:N0}", () => promo.MarketingBudget = new Money(budget));
        }
        if (PromotionLadder.IsEligible(promo))
        {
            if (r.Chance(0.7)) Act(w, "take-promotion", () => PromotionLadder.Promote(w, promo, w.CurrentDate));
            else Note(w, "promotion-lapsed");
        }
    }

    // ------------------------------------------------------------------ cards

    private void Cards(World w, Promotion promo, SimRandom r, bool small)
    {
        var upcoming = _booking.UpcomingEvents(w).ToList();

        // the awkward order: cancel a card that is NOT the newest
        if (upcoming.Count >= 2 && r.Chance(0.20))
        {
            var newest = upcoming.MaxBy(e => e.Id.Value)!;
            var victim = upcoming.Where(e => e.Id != newest.Id).OrderBy(e => e.Id.Value).ElementAt(r.Range(0, upcoming.Count - 2));
            CancelAndVerify(w, victim, "cancel-non-newest");
            upcoming = _booking.UpcomingEvents(w).ToList();
        }
        else if (upcoming.Count >= 1 && r.Chance(0.07))
        {
            CancelAndVerify(w, r.Pick(upcoming), "cancel");
            upcoming = _booking.UpcomingEvents(w).ToList();
        }

        // keep a couple of cards on the calendar
        int want = small ? (r.Chance(0.3) ? 2 : 1) : 2;
        for (int i = upcoming.Count; i < want; i++)
        {
            var date = w.CurrentDate.AddDays(r.Range(14, 60));
            var tier = r.Pick(new[] { EventTier.FightNight, EventTier.FightNight, EventTier.Ppv, EventTier.SuperCard });
            CityId? city = r.Chance(0.4) && w.Cities.Count > 0 ? w.Cities.Values.OrderBy(c => c.Id.Value).ElementAt(r.Range(0, w.Cities.Count - 1)).Id : null;
            Event? created = null;
            Act(w, $"create-event {date:yyyy-MM-dd} {tier}", () => created = _booking.CreateEvent(w, date, null, tier, city));
            if (created is not null) { BuildCard(w, promo, created, r, small); }
        }

        // rework an existing card: fill gaps, remove a bout, move a bout
        foreach (var ev in _booking.UpcomingEvents(w).Take(2).ToList())
        {
            if (w.Get(ev.Id) is null) continue;
            if (ev.Card.Count < 4 || r.Chance(0.3)) BuildCard(w, promo, ev, r, small);
            if (ev.Card.Count > 0 && r.Chance(0.25))
            {
                var bout = r.Pick(ev.Card);
                Act(w, $"remove-bout {bout.Id.Value}", () => _booking.RemoveBout(w, ev, bout.Id));
                BuildCard(w, promo, ev, r, small, max: 1);   // then add another
            }
            if (ev.Card.Count > 1 && r.Chance(0.2))
            {
                int i = r.Range(0, ev.Card.Count - 1); int j = Math.Clamp(i + (r.Chance(0.5) ? 1 : -1), 0, ev.Card.Count - 1);
                Act(w, $"move-bout {i}->{j}", () => (ev.Card[i], ev.Card[j]) = (ev.Card[j], ev.Card[i]));
            }
        }
    }

    private void CancelAndVerify(World w, Event ev, string label)
    {
        var id = ev.Id;
        Act(w, $"{label} {ev.Name} #{id.Value} ({ev.Card.Count} bouts)", () => _booking.CancelEvent(w, ev));
        if (w.Get(id) is not null)
            _log.Report("PLAY-EVENT-GONE", _run, w.CurrentDate, $"cancel:{id.Value}", $"CancelEvent left event #{id.Value} in world.Events");
    }

    private void BuildCard(World w, Promotion promo, Event ev, SimRandom r, bool small, int max = 6)
    {
        int added = 0;
        for (int attempt = 0; attempt < max * 3 && added < max; attempt++)
        {
            if (w.Get(ev.Id) is null || promo.Divisions.Count == 0) return;
            var div = r.Pick(promo.Divisions);

            // sign a fighter mid-card-build, then use him
            if (r.Chance(0.06) && !_reckless && promo.Balance.Amount > 150_000m)
            {
                var fa = w.FreeAgents().Where(f => f.Status == FighterStatus.Active && div.Matches(f) && f.IsMatchReady(ev.Date))
                    .Select(f => (F: f, Exp: _valuation.Evaluate(w, f))).Where(x => x.Exp.AskingPurse.Amount <= 8_000m)
                    .OrderBy(x => x.F.Id.Value).FirstOrDefault();
                if (fa.F is not null) Act(w, $"sign-mid-build {fa.F.FullName} #{fa.F.Id.Value}", () => Negotiate(w, promo, fa.F, r, allowLowball: false));
            }

            var avail = _booking.AvailableRoster(w, ev, div.WeightClass, div.Sex)
                .Where(f => !BookedElsewhere(w, ev, f.Id)).ToList();
            if (avail.Count == 0) continue;
            if (avail.Count == 1)
            {
                if (small && r.Chance(0.6))
                {
                    var style = r.Pick(Enum.GetValues<PrimaryStyle>());
                    bool ok = false;
                    Act(w, $"add-local-bout {avail[0].FullName}", () => ok = _booking.AddLocalBout(w, ev, avail[0].Id, style, r.Pick(new[] { 3, 5 }), out _));
                    if (ok) added++;
                }
                continue;
            }

            bool title = div.Title is not null && r.Chance(0.35);
            Fighter red = avail[r.Range(0, Math.Min(3, avail.Count - 1))];
            if (title && div.Title is { } tid && w.Get(tid)?.CurrentChampion is { } champ && w.Get(champ) is { } champF && avail.Contains(champF))
                red = champF;
            var opp = _booking.EligibleOpponents(w, ev, red.Id).Where(f => !BookedElsewhere(w, ev, f.Id)).ToList();
            if (opp.Count == 0) continue;
            var blue = opp[r.Range(0, Math.Min(4, opp.Count - 1))];
            bool added1 = false;
            int rounds = title ? 5 : r.Pick(new[] { 3, 3, 5 });
            Act(w, $"add-bout {red.FullName} v {blue.FullName}{(title ? " TITLE" : "")}", () => added1 = _booking.AddBout(w, ev, red.Id, blue.Id, rounds, title, out _));
            if (added1) added++;
        }
    }

    private static bool BookedElsewhere(World w, Event ev, FighterId id) =>
        w.Events.Values.Any(e => e.Id != ev.Id && e.Status == EventStatus.Scheduled && e.Card.Any(f => f.RedCorner == id || f.BlueCorner == id));
}
