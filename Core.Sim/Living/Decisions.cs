using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// THE OWNER'S DESK: two-way calls only the player can make. Someone wants
/// something — a ranked fighter demands the next title shot, a sponsor wants a
/// hometown name showcased, the board wants a fading contract off the books —
/// and both answers cost something. At most one new call lands per month; a
/// call ignored past its deadline resolves itself (usually as a quiet no).
/// The Home screen renders the desk; Accept/Decline below apply the effects.
/// </summary>
public static class Decisions
{
    public static void RunMonthly(World world, DateOnly date, SimRandom rng)
    {
        if (world.Get(world.PlayerPromotion) is not { } promo) return;

        // The deadline is an answer too.
        for (int i = world.Decisions.Count - 1; i >= 0; i--)
        {
            var d = world.Decisions[i];
            if (date <= d.Deadline) continue;
            if (d.State == DecisionState.Pending && d.Kind == DecisionKind.TitleShotRequest
                && d.Fighter is { } fid && world.Get(fid) is { } snubbed)
            {
                // Silence reads as a no — with a no's consequences.
                snubbed.Loyalty = Math.Max(0, snubbed.Loyalty - 8);
                snubbed.Morale = Math.Clamp(snubbed.Morale - 6, 0, 100);
            }
            world.Decisions.RemoveAt(i);
        }

        if (world.Decisions.Count(d => d.State == DecisionState.Pending) >= 2) return;   // the desk holds two calls, tops
        if (!rng.Chance(0.35)) return;

        int id = world.Decisions.Count == 0 ? 1 : world.Decisions.Max(d => d.Id) + 1;
        var roster = world.RosterOf(promo.Id).Where(f => f.Status == FighterStatus.Active).ToList();

        // Which call lands is context — try the juiciest that applies.
        // 1) A ranked contender wants the belt shot, out of turn or not.
        var contender = roster
            .Where(f => f.Momentum >= 15 && f.Loyalty < 85)
            .Where(f => promo.Divisions.Any(d => d.Matches(f) && d.Rankings.Take(3).Contains(f.Id)
                && d.Title is { } t && world.Get(t)?.CurrentChampion is { } c && c != f.Id))
            .OrderByDescending(f => f.Intangibles.Popularity.Value)
            .FirstOrDefault();
        if (contender is not null && rng.Chance(0.5))
        {
            Add(world, promo, new Decision
            {
                Id = id, Kind = DecisionKind.TitleShotRequest, Created = date, Deadline = date.AddDays(45),
                Fighter = contender.Id,
                Detail = $"{contender.FullName} ({contender.Record}, ranked top 3) wants the NEXT title shot — publicly. " +
                         "Grant it and they're ours for years; tell them to earn it and they'll remember that too.",
            }, date, $"{contender.FullName} wants the next title shot");
            return;
        }

        // 2) A sponsor wants their money's worth: local blood on the main card.
        if (promo.SponsorDeals.Count > 0 && rng.Chance(0.6))
        {
            var deal = promo.SponsorDeals[rng.Range(0, promo.SponsorDeals.Count - 1)];
            Add(world, promo, new Decision
            {
                Id = id, Kind = DecisionKind.SponsorShowcase, Created = date, Deadline = date.AddDays(75),
                Sponsor = deal.Partner,
                Detail = $"{deal.Partner} wants a HOME-REGION fighter featured on our next main card — local blood sells " +
                         "their product. Deliver and they'll pay a full extra fee; agree and fail, and it counts against us.",
            }, date, $"{deal.Partner} wants a hometown showcase");
            return;
        }

        // 3) The board eyes a fading, expensive veteran's deal.
        var fading = roster
            .Where(f => f.AgeOn(date) >= 34 && f.Record.CurrentStreak <= -2
                && f.Contract is { } c && c.PursePerFight.Amount >= 8_000m)
            .OrderByDescending(f => f.Contract!.PursePerFight.Amount)
            .FirstOrDefault();
        if (fading is not null)
        {
            Add(world, promo, new Decision
            {
                Id = id, Kind = DecisionKind.VeteranCut, Created = date, Deadline = date.AddDays(45),
                Fighter = fading.Id,
                Detail = $"The board wants {fading.FullName} ({fading.AgeOn(date)}, {fading.Record}, " +
                         $"{fading.Contract!.PursePerFight}/fight) off the books — the slide looks terminal. " +
                         "Cutting a name the locker room respects has a cost of its own.",
            }, date, $"The board wants {fading.FullName} released");
        }
    }

    private static void Add(World world, Promotion promo, Decision d, DateOnly date, string subject)
    {
        world.Decisions.Add(d);
        Mail.Post(world, date, MailCategory.BoardNote, "The Desk",
            $"Decision: {subject}", d.Detail + " Make the call from the Home screen.", aboutFighter: d.Fighter);
    }

    /// <summary>The player says YES.</summary>
    public static void Accept(World world, Decision d, DateOnly date)
    {
        if (world.Get(world.PlayerPromotion) is not { } promo) return;
        switch (d.Kind)
        {
            case DecisionKind.TitleShotRequest when d.Fighter is { } fid && world.Get(fid) is { } f:
                if (f.Contract?.Promotion != promo.Id) { world.Decisions.Remove(d); break; }   // left since asking
                foreach (var div in promo.Divisions.Where(x => x.Matches(f)))
                {
                    div.Rankings.Remove(f.Id);
                    div.Rankings.Insert(0, f.Id);   // to the front of the line
                }
                f.Loyalty = Math.Min(100, f.Loyalty + 10);
                f.Morale = Math.Clamp(f.Morale + 8, 0, 100);
                Mail.Post(world, date, MailCategory.General, "Matchmaking",
                    $"{f.FullName} moved to #1 contender",
                    "The shot is promised — book the title fight when you're ready. The locker room saw who got rewarded.",
                    aboutFighter: fid);
                world.Decisions.Remove(d);
                break;

            case DecisionKind.SponsorShowcase:
                d.State = DecisionState.Accepted;   // judged at our next event's settlement
                break;

            case DecisionKind.AcquisitionOffer when d.Promotion is { } tid && world.Get(tid) is { Defunct: false } target:
                if (promo.Balance.Amount < d.Price.Amount)
                {
                    Mail.Post(world, date, MailCategory.BoardNote, "The Desk",
                        $"We can't cover the {target.Name} deal",
                        $"The ask is {d.Price} and the books say no. The offer stands until the deadline — find the money or let it go.");
                    return;   // the call stays on the desk
                }
                world.Decisions.Remove(d);
                Acquisitions.Execute(world, promo, target, date, d.Price);
                break;

            case DecisionKind.VeteranCut when d.Fighter is { } vid && world.Get(vid) is { } vet:
                world.Decisions.Remove(d);
                if (vet.Contract?.Promotion != promo.Id) break;   // already gone — nobody left to cut
                // A real release: guaranteed money is paid out, the rankings and
                // any belt are cleared, and the locker room notices how careers
                // end here (the reputation dent).
                var owed = Contracts.ContractLifecycle.Release(world, vet, dentReputation: true);
                Mail.Post(world, date, MailCategory.General, Mail.Owner(world),
                    $"{vet.FullName} released",
                    (owed.Amount > 0 ? $"The guaranteed money still went out the door ({owed}). " : "The books thank you. ") +
                    "The locker room's respect took the hit instead.", aboutFighter: vid);
                break;

            default:
                world.Decisions.Remove(d);
                break;
        }
    }

    /// <summary>The player says NO.</summary>
    public static void Decline(World world, Decision d, DateOnly date)
    {
        if (d.Kind == DecisionKind.TitleShotRequest && d.Fighter is { } fid && world.Get(fid) is { } f)
        {
            f.Loyalty = Math.Max(0, f.Loyalty - 10);
            f.Morale = Math.Clamp(f.Morale - 8, 0, 100);
            Mail.Post(world, date, MailCategory.General, "Matchmaking",
                $"{f.FullName} told to earn it",
                "They didn't take it well — keep an eye on that contract.", aboutFighter: fid);
        }
        world.Decisions.Remove(d);
    }

    /// <summary>
    /// A player card settled: any ACCEPTED sponsor showcase gets judged — did a
    /// home-region fighter feature on the main card? Deliver: a full extra fee.
    /// Fail: the promise counts against us with that sponsor.
    /// </summary>
    public static void OnPlayerEventSettled(World world, Promotion promo, Event ev)
    {
        foreach (var d in world.Decisions
                     .Where(x => x.Kind == DecisionKind.SponsorShowcase && x.State == DecisionState.Accepted)
                     .ToList())
        {
            int mainStart = ev.Card.Count - ev.EffectiveMainCard;
            bool delivered = ev.Card.Skip(mainStart)
                .SelectMany(f => new[] { f.RedCorner, f.BlueCorner })
                .Select(idn => world.Get(idn))
                .Any(f => f is not null && (f.Nationality == promo.BaseRegion
                    || (f.HomeCity is { } hc && world.Get(hc)?.Region == promo.BaseRegion)));

            var deal = promo.SponsorDeals.FirstOrDefault(s => s.Partner == d.Sponsor);
            if (delivered && deal is not null)
            {
                Finance.Ledger.Income(promo, ev.Date, FinanceCategory.Sponsorship,
                    deal.FeePerEvent, $"{ev.Name} — {deal.Partner} showcase bonus");
                Mail.Post(world, ev.Date, MailCategory.General, "Commercial",
                    $"{deal.Partner} loved the show",
                    $"Local blood on the main card, exactly as promised — they paid a full extra fee ({deal.FeePerEvent}).");
            }
            else if (deal is not null)
            {
                deal.Strikes++;
                Mail.Post(world, ev.Date, MailCategory.BoardNote, "Commercial",
                    $"We broke our word to {deal.Partner}",
                    "We promised a hometown showcase and didn't deliver — that's a strike against the deal.");
            }
            world.Decisions.Remove(d);
        }
    }
}
