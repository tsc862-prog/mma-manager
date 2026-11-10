using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// When a genuine STAR hits free agency, the big orgs don't quietly negotiate —
/// they escalate against each other in public, in rounds, until one remains.
/// The sport's favourite soap opera: every round makes headlines, the number
/// climbs 20% a fortnight, and the winner overpays gloriously (the money sink
/// the summit's hoarded banks deserve). The fighter is off the normal
/// free-agency board while the war runs. The player never bids through this
/// system — they can SWOOP at any moment by signing the fighter through normal
/// negotiation (any signing ends the war on the spot), and the agent keeps
/// them posted on what beating the market currently costs.
/// </summary>
public static class BiddingWars
{
    private const int MaxConcurrentWars = 2;
    private const int PopFloor = 62;   // a genuine draw, not necessarily a superstar — wars at 68+ fired twice a generation
    private const decimal EscalationPerRound = 1.2m;
    private const int RoundDays = 14;
    private const int FinalRound = 3;

    private static readonly FighterValuation Valuation = new();

    // A war is exactly where roster discipline breaks: a room stays in the
    // bidding while the number is within 2.5× its normal purse cap — or when
    // its bank is so deep the cap is an accountant's fiction anyway.
    private static bool CanCarry(World world, Promotion p, decimal bid) =>
        Negotiator.PurseCap(world, p) * 2.5m >= bid || p.Balance.Amount >= bid * 20m;

    public static void RunWeekly(World world, DateOnly date, SimRandom rng)
    {
        Advance(world, date, rng);
        MaybeStart(world, date, rng);
    }

    /// <summary>Any signing ends the fighter's war on the spot (the market lost).</summary>
    public static void OnSigned(World world, Fighter f, PromotionId by, DateOnly date)
    {
        int removed = world.BiddingWars.RemoveAll(w => w.Fighter == f.Id);
        if (removed > 0 && by == world.PlayerPromotion)
            Living.News.Post(world, date, NewsCategory.Signing,
                $"{f.FullName} signs — the bidding war is over",
                "The suitors circled and somebody else closed. The market hates being beaten to a name.", by);
    }

    private static void Advance(World world, DateOnly date, SimRandom rng)
    {
        foreach (var war in world.BiddingWars.ToList())
        {
            var f = world.Get(war.Fighter);
            if (f is null || f.Status != FighterStatus.Active || f.Contract is not null)
            {
                world.BiddingWars.Remove(war);   // retired, suspended, or signed out from under the market
                continue;
            }
            if (date < war.NextRoundOn) continue;

            // The number climbs — and the rooms that can't carry it go quiet.
            war.Round++;
            war.CurrentBid = new Money(Math.Round(war.CurrentBid.Amount * EscalationPerRound / 500m) * 500m);
            war.Suitors.RemoveAll(pid => world.Get(pid) is not { Defunct: false } p
                || Solvency.Distress(p) > 0
                || !CanCarry(world, p, war.CurrentBid.Amount));

            if (war.Suitors.Count == 0)
            {
                world.BiddingWars.Remove(war);
                Living.News.Post(world, date, NewsCategory.Signing,
                    $"The {f.FullName} sweepstakes collapse",
                    "The number got silly and every room went quiet. The star stays on the market.", default);
                continue;
            }

            if (war.Round >= FinalRound || war.Suitors.Count == 1)
            {
                Resolve(world, war, f, date);
                continue;
            }

            Living.News.Post(world, date, NewsCategory.Signing,
                $"Bidding for {f.FullName} reaches {war.CurrentBid}/fight",
                $"{war.Suitors.Count} promotions still in the room, and nobody's blinking yet.", default);
            if (world.Get(world.PlayerPromotion) is { Defunct: false } player
                && OrgSizing.SizeOf(player) >= OrgSize.National)
                Living.Mail.Post(world, date, MailCategory.General, "The Agent",
                    $"{f.FullName} — the number is {war.CurrentBid}",
                    $"The war is live and climbing. Beat {war.CurrentBid}/fight at the table and the star is ours before the market closes it.",
                    aboutFighter: f.Id);
            war.NextRoundOn = date.AddDays(RoundDays);
        }
    }

    private static void Resolve(World world, BiddingWar war, Fighter f, DateOnly date)
    {
        var winner = war.Suitors.Select(world.Get).Where(p => p is not null)
            .OrderByDescending(p => p!.Prestige.Value).ThenByDescending(p => p!.Balance.Amount)
            .FirstOrDefault();
        world.BiddingWars.Remove(war);
        if (winner is null) return;

        // The winner's curse, itemised: war-price purse, half again as a win
        // bonus, and a signing bonus the size of the bid — a real cheque.
        var offer = new ContractOffer
        {
            Fighter = f.Id, Promotion = winner.Id,
            PursePerFight = war.CurrentBid,
            WinBonus = new Money(war.CurrentBid.Amount * 0.5m),
            SigningBonus = war.CurrentBid,
            Fights = 5,
        };
        ContractLifecycle.Sign(world, f, offer, date);
        Living.News.Post(world, date, NewsCategory.Signing,
            $"{f.FullName} signs with {winner.Name} after a bidding war",
            $"The final number: {war.CurrentBid}/fight plus a {offer.SigningBonus} signing bonus. The market sets the price, and the market was feeling rich.",
            winner.Id);
    }

    private static void MaybeStart(World world, DateOnly date, SimRandom rng)
    {
        if (world.BiddingWars.Count >= MaxConcurrentWars) return;
        if (!rng.Chance(0.30)) return;   // stars hit the open market rarely; wars rarer

        var star = world.FreeAgents()
            .Where(f => f.Status == FighterStatus.Active && !f.LocalTalent
                && f.Intangibles.Popularity.Value >= PopFloor
                && f.Record.TotalBouts >= 6
                && world.BiddingWars.All(w => w.Fighter != f.Id))
            .OrderByDescending(f => f.Intangibles.Popularity.Value)
            .FirstOrDefault();
        if (star is null) return;

        var ask = Valuation.Evaluate(world, star).AskingPurse;
        var openingBid = new Money(Math.Round(ask.Amount * 1.1m / 500m) * 500m);
        var suitors = world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != world.PlayerPromotion
                && OrgSizing.SizeOf(p) >= OrgSize.National
                && Solvency.Distress(p) == 0
                && p.Divisions.Any(d => d.Matches(star))
                && CanCarry(world, p, openingBid.Amount))
            .OrderByDescending(p => p.Prestige.Value)
            .Take(4)
            .Select(p => p.Id)
            .ToList();
        if (suitors.Count < 2) return;   // it takes two rooms to make a war

        var war = new BiddingWar
        {
            Fighter = star.Id, CurrentBid = openingBid, Round = 1,
            NextRoundOn = date.AddDays(RoundDays),
        };
        war.Suitors.AddRange(suitors);
        world.BiddingWars.Add(war);

        Living.News.Post(world, date, NewsCategory.Signing,
            $"BIDDING WAR: {suitors.Count} promotions chase {star.FullName}",
            $"The star of the free-agent class (pop {star.Intangibles.Popularity.Value}) has the big rooms outbidding " +
            $"each other in public — the opening number is {openingBid}/fight and climbing.", default);
        if (world.Get(world.PlayerPromotion) is { Defunct: false } player
            && OrgSizing.SizeOf(player) >= OrgSize.National)
            Living.Mail.Post(world, date, MailCategory.General, "The Agent",
                $"Bidding war: {star.FullName}",
                $"The market wants my client and the number starts at {openingBid}/fight. " +
                "Sign them at the table before the war ends and the star is yours — hesitate and pay the winner's price.",
                aboutFighter: star.Id);
    }
}
