using MmaManager.Core.Model;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The fight business consolidates the way it always has: a giant buys a
/// struggling rival — roster, brand and all. This is where the market's fat
/// middle slims itself (MarketShare dilution bleeds the weakest of a crowded
/// band into the red, and the red is what gets bought), and where the summit's
/// hoarded banks finally get SPENT. Rare and seismic by design: at most one
/// deal every couple of years world-wide, and every one is front-page news.
/// The player gets first refusal when they could plausibly write the cheque —
/// the broker's call lands on THE DESK before the market moves.
/// </summary>
public static class Acquisitions
{
    private const int CooldownMonths = 12;

    /// <summary>The share of the price the selling owners keep however deep the guarantees run.</summary>
    public const decimal SellerKeeps = 0.10m;

    /// <summary>
    /// Is this org quietly FOR SALE? In the red outright — or thin-banked while
    /// its band is diluted (a crowded market grinding a marginal player down is
    /// exactly who takes the phone call). National-grade and up only: nobody
    /// brokers the sale of a club hall.
    /// </summary>
    public static bool IsForSale(World world, Promotion p)
    {
        if (OrgSizing.SizeOf(p) < OrgSize.National) return false;

        // THE SPORT'S #1 BRAND IS NEVER QUIETLY FOR SALE. The flagship is the
        // ladder the whole game climbs; it doesn't change hands because of one
        // bad year, and a rival buying the crown jewel out of a cash dip reads
        // as a bug, not a story. If it genuinely dies it still FOLDS on the
        // debt ceiling — a far harder line — like any other company.
        if (world.Promotions.Values.Where(x => !x.Defunct)
                .OrderByDescending(x => x.Prestige.Value).ThenBy(x => x.Id.Value)
                .FirstOrDefault() is { } flagship && flagship.Id == p.Id)
            return false;

        // Thin-banked (or outright in the red) AND STILL BLEEDING over the
        // trailing quarter. Both halves matter: the balance alone is a snapshot
        // — a giant's eight-figure cash flow is lumpy (a wave of contract
        // buyouts can swing it $14M in six months and recover) — while the
        // LEDGER says whether the business is actually dying. Set at "dying",
        // not "a rough quarter": the designed world must not get eaten for
        // ordinary volatility (an early cut of this sold IFA in 10/10 sweep
        // seeds; a later one sold AFC, the flagship, on a buyout wave).
        decimal thin = OrgSizing.SizeOf(p) switch
        {
            OrgSize.International => 3_000_000m,
            OrgSize.Continental => 1_000_000m,
            _ => 250_000m,
        };
        return p.Balance.Amount < thin && TrailingNet(p, world.CurrentDate, 120) < -thin / 2m;
    }

    // Net cash flow over the trailing window, read newest-first off the ledger
    // (appended chronologically, so the scan stops at the cutoff).
    private static decimal TrailingNet(Promotion p, DateOnly asOf, int days)
    {
        decimal net = 0m;
        for (int i = p.Ledger.Count - 1; i >= 0; i--)
        {
            var e = p.Ledger[i];
            if (asOf.DayNumber - e.Date.DayNumber > days) break;
            net += e.Amount.Amount;
        }
        return net;
    }

    /// <summary>
    /// What the whole company costs: the brand, the contracts, the cash in the
    /// drawer — DISCOUNTED by how badly the seller needs out (a fire sale is
    /// how the deal actually closes).
    /// </summary>
    public static Money AskingPrice(World world, Promotion target)
    {
        decimal brand = target.Prestige.Value * target.Prestige.Value * 4_000m;
        decimal roster = 0m;
        foreach (var f in world.RosterOf(target.Id))
            if (f.Contract is { } c) roster += c.PursePerFight.Amount * c.FightsRemaining * 0.25m;
        decimal cash = Math.Max(0m, target.Balance.Amount);
        decimal distressDiscount = Solvency.Distress(target) switch { >= 2 => 0.55m, 1 => 0.75m, _ => 0.9m };
        return new Money((brand + roster) * distressDiscount + cash);
    }

    public static void RunMonthly(World world, DateOnly date, SimRandom rng)
    {
        if (world.LastAcquisitionOn is { } last && date.DayNumber - last.DayNumber < CooldownMonths * 30) return;
        if (!rng.Chance(0.5)) return;   // brokers work slowly — but a live seller finds a phone

        // A target is a NAME in genuine trouble — the wreckage MarketShare
        // dilution and bad years produce (see IsForSale).
        var target = world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != world.PlayerPromotion && IsForSale(world, p))
            .OrderBy(p => p.Balance.Amount)
            .FirstOrDefault();
        if (target is null) return;
        // The broker only shops a target once a call is already on the desk.
        if (world.Decisions.Any(d => d.Kind == DecisionKind.AcquisitionOffer)) return;

        var price = AskingPrice(world, target);

        // First refusal: a player big enough to swallow the target hears about
        // it before the market does.
        if (world.Get(world.PlayerPromotion) is { Defunct: false } player
            && OrgSizing.SizeOf(player) >= OrgSize.National
            && player.Balance.Amount >= price.Amount * 0.9m)
        {
            int id = world.Decisions.Count == 0 ? 1 : world.Decisions.Max(d => d.Id) + 1;
            var call = new Decision
            {
                Id = id, Kind = DecisionKind.AcquisitionOffer, Created = date, Deadline = date.AddDays(35),
                Promotion = target.Id, Price = price,
                Detail = $"{target.Name} is quietly for sale — the books are bleeding and ownership wants out. " +
                         $"Asking {price}: the roster, the brand, everything. Say yes and we absorb them whole; " +
                         "pass and someone else may write the cheque.",
            };
            world.Decisions.Add(call);
            Mail.Post(world, date, MailCategory.BoardNote, "The Desk",
                $"Decision: {target.Name} is for sale", call.Detail + " Make the call from the Home screen.");
            return;   // the market waits for the player's answer (the deadline is an answer)
        }

        // Otherwise the market moves: the richest org that can carry the deal buys.
        // MONEY buys up the ladder as happily as down it: a cash-rich Continental
        // swallowing a bleeding International is the sport's biggest possible
        // story, not a rules violation. A buyer needs stature (National-grade or
        // better) and the cheque — nothing else.
        var buyer = FindBuyer(world, target, price);
        if (buyer is null) return;

        Execute(world, buyer, target, date, price);
    }

    /// <summary>
    /// The richest AI org that can carry the deal: National-grade or better, out
    /// of distress, with 1.25x the price in the bank. Ties go to the lower id.
    /// </summary>
    private static Promotion? FindBuyer(World world, Promotion target, Money price) =>
        world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != world.PlayerPromotion && p.Id != target.Id
                && OrgSizing.SizeOf(p) >= OrgSize.National
                && Solvency.Distress(p) == 0
                && p.Balance.Amount >= price.Amount * 1.25m)
            .OrderByDescending(p => p.Balance.Amount).ThenBy(p => p.Id.Value)
            .FirstOrDefault();

    /// <summary>
    /// The last-minute sale. Called by <see cref="Solvency"/> in the tick a
    /// promotion would close: if anyone can carry it, it is SOLD at the fire-sale
    /// price (deep distress takes 45% off) instead of folding. The monthly broker
    /// is slow on purpose (a coin-flip a month, a year's cooldown after any deal,
    /// and a seller must look like it is dying over a trailing quarter), so a fast
    /// collapse used to beat it to the ceiling: VMMA on seed 40 went from $6M to
    /// -$3M in nine months with a buyer able to pay and folded outright, 270
    /// fighters released and its belts dead, where on main it would have been
    /// bought. Regional-grade and up (a club hall just closes); never the
    /// sport's #1 brand (see <see cref="IsForSale"/>), never the player (their
    /// bankruptcy is the game over). No first refusal for the player here: there
    /// is no time for a desk call when the doors close this tick. No dice.
    /// </summary>
    public static bool TryRescue(World world, Promotion target, DateOnly date)
    {
        if (target.Defunct || target.Id == world.PlayerPromotion) return false;
        if (OrgSizing.SizeOf(target) < OrgSize.Regional) return false;
        if (world.Promotions.Values.Where(x => !x.Defunct)
                .OrderByDescending(x => x.Prestige.Value).ThenBy(x => x.Id.Value)
                .FirstOrDefault() is { } flagship && flagship.Id == target.Id)
            return false;
        var price = AskingPrice(world, target);
        if (FindBuyer(world, target, price) is not { } buyer) return false;
        Execute(world, buyer, target, date, price);
        return true;
    }

    /// <summary>
    /// Close the deal: the buyer pays, absorbs the best of the roster (champions
    /// first), and the target's name goes on the wall. Everyone the buyer can't
    /// carry hits free agency in the shake-out.
    /// </summary>
    public static void Execute(World world, Promotion buyer, Promotion target, DateOnly date, Money price)
    {
        if (target.Balance.Amount > 0)
            Finance.Ledger.Income(buyer, date, FinanceCategory.Acquisition,
                new Money(target.Balance.Amount), $"{target.Abbreviation} cash reserves acquired");

        // The roster: champions and the best of each division come over on
        // their existing terms; the rest are casualties of the merger. "Best"
        // is the BUYER's read of them (fog of war: it decides on what its
        // scouts see, not on true ability), ties on id.
        var champions = new HashSet<FighterId>(
            target.Titles.Select(t => world.Get(t)?.CurrentChampion).Where(c => c is not null).Select(c => c!.Value));
        int carried = 0, released = 0;
        var cutLoose = new List<(Fighter Fighter, decimal Owed)>();
        foreach (var f in world.RosterOf(target.Id).OrderByDescending(f => champions.Contains(f.Id) ? 1 : 0)
                     .ThenByDescending(f => Scouting.ScoutingService.PerceivedAbility(world, buyer, f))
                     .ThenBy(f => f.Id.Value).ToList())
        {
            var div = buyer.Divisions.FirstOrDefault(d => d.Matches(f));
            int have = div is null ? int.MaxValue
                : world.RosterOf(buyer.Id).Count(x => div.Matches(x) && x.Status != FighterStatus.Retired);
            // Champions ALWAYS come over — absorbing the belts is the point of
            // the purchase; everyone else needs room in the buyer's division.
            if (f.Status == FighterStatus.Active && div is not null && f.Contract is { } c
                && (champions.Contains(f.Id)
                    || have < OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(buyer)) + 2))
            {
                Contracts.Lifers.OnLeaving(world, f, target, $"{target.Name} was sold to {buyer.Name}", date, buyer.Id);
                f.Contract = new Contract
                {
                    Promotion = buyer.Id, SignedOn = date, Type = c.Type,
                    PursePerFight = c.PursePerFight, WinBonus = c.WinBonus,
                    FightsRemaining = c.FightsRemaining, IsExclusive = c.IsExclusive,
                    HasChampionsClause = c.HasChampionsClause, SigningBonus = c.SigningBonus,
                    PpvPoints = c.PpvPoints, PerformanceBonus = c.PerformanceBonus, Guaranteed = c.Guaranteed,
                };
                f.Loyalty = 25;   // new landlord, old locker room
                if (div is not null && !div.Rankings.Contains(f.Id) && !champions.Contains(f.Id))
                    div.Rankings.Add(f.Id);
                carried++;
            }
            else
            {
                // A fighter cut loose in the merger is owed what is left on his
                // guaranteed deal, as any release pays (ContractLifecycle.Release).
                if (f.Contract is { } dropped && f.Status != FighterStatus.Retired
                    && ContractLifecycle.GuaranteedOwed(dropped).Amount > 0)
                    cutLoose.Add((f, ContractLifecycle.GuaranteedOwed(dropped).Amount));
                Contracts.Lifers.OnLeaving(world, f, target, $"he was cut loose when {buyer.Name} bought {target.Name}", date);
                f.Contract = null;
                f.CampStipend = false;   // the old org's promise goes with it
                released++;
            }
        }

        // The money. The guarantees are the SELLER's debts, so they are settled
        // out of the purchase price before the old owners are paid: the buyer
        // writes one cheque for the asking price, and the cut fighters are paid
        // from it first. A company whose guarantees exceed what the deal can
        // spare is underwater — its cut fighters share that pro rata, as
        // creditors of a sinking company do — and the owners still walk away
        // with SellerKeeps of the price: no seller signs a deal that leaves
        // them nothing, and the books always show the purchase. Paying them on top of the price
        // instead bankrupted the flagship in 5 of 40 seeds (it bought a
        // Continental and owed $14-45M for the roster it had no room for).
        decimal owedAll = cutLoose.Sum(x => x.Owed);
        decimal settle = Math.Min(owedAll, price.Amount * (1m - SellerKeeps));
        decimal paidOut = 0m;
        foreach (var (f, owed) in cutLoose)
        {
            // Rounded DOWN per head, so the shares never add up past the pot.
            decimal share = owedAll <= 0 ? 0m : Math.Floor(owed * settle / owedAll);
            paidOut += share;
            Finance.Ledger.Expense(buyer, date, FinanceCategory.ContractBuyout, new Money(share),
                $"Contract buyout — {f.FullName} ({target.Abbreviation} merger)");
        }
        Finance.Ledger.Expense(buyer, date, FinanceCategory.Acquisition,
            new Money(price.Amount - paidOut), $"Acquisition of {target.Name}");   // the guarantees paid from the price are the "(… merger)" buyout lines

        // Nobody fights under the old banner now: its contender lists go with it.
        foreach (var d in target.Divisions) d.Rankings.Clear();

        // The belts retire with the banner — their lineages simply end.
        foreach (var tid in target.Titles)
        {
            if (world.Get(tid) is not { } title) continue;
            var reign = title.Lineage.LastOrDefault(r => r.LostOn is null);
            if (reign is not null) reign.LostOn = date;
            title.CurrentChampion = null;
        }

        var cancelled = world.Events.Values
            .Where(e => e.Promotion == target.Id && e.Status == EventStatus.Scheduled)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).ToList();
        foreach (var ev in cancelled) ev.Status = EventStatus.Cancelled;

        target.SponsorDeals.Clear();
        target.MediaDeals.Clear();
        target.Offers.Clear();
        target.Stream = null;
        PromotionLadder.OnSeatFreed(world, OrgSizing.SizeOf(target));   // consolidation makes room at its level
        target.Defunct = true;
        world.LastAcquisitionOn = date;

        News.Post(world, date, NewsCategory.Milestone,
            $"{buyer.Name} acquires {target.Name}",
            $"The sport consolidates: {buyer.Abbreviation} buys the whole company for {price} — " +
            $"{carried} fighters come over, {released} hit the open market, and the {target.Abbreviation} belts retire with the banner."
            + Org.Solvency.CalendarFallout(cancelled),
            buyer.Id);

        Mail.Post(world, date, MailCategory.General, "MMA Weekly",
            buyer.Id == world.PlayerPromotion
                ? $"We own {target.Name}"
                : $"{buyer.Name} just bought {target.Name}",
            buyer.Id == world.PlayerPromotion
                ? $"The deal is closed: {carried} of their fighters now answer to us, and their belts are museum pieces. The market noticed."
                : $"The whole company — {price}. {released} fighters hit free agency in the shake-out; move fast if any fit our plans.");
    }
}
