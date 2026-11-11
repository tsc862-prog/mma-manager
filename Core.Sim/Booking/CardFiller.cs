using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// Every card runs at a realistic length — nobody pays to see a two-fight show.
/// A promotion first fleshes out the undercard from its own available roster;
/// a small org that's run out of contracted fighters papers the rest with
/// unnamed local talent (local vs local — pure filler, no roster impact). The
/// big promotions have the depth to never need it. Filler slots in as prelims,
/// ahead of the booked main event.
/// </summary>
/// <summary>One bout the house would add on the night (a local pair has no names yet).</summary>
public sealed record FillerBout(FighterId? Red, FighterId? Blue, WeightClass WeightClass, bool LocalFiller, Money Cost);

/// <summary>What padding would add to a card: the bouts, their cost, and the card length before and after.</summary>
public sealed record FillerPreview(IReadOnlyList<FillerBout> Bouts, Money Cost, int BoutsBefore, int BoutsAfter)
{
    public bool WouldPad => Bouts.Count > 0;
    public int LocalPairs => Bouts.Count(b => b.LocalFiller);
}

public static class CardFiller
{
    /// <summary>The fewest bouts a professional card should carry.</summary>
    public const int MinCardBouts = 6;

    /// <summary>Bring a card up to the minimum length before it runs.</summary>
    public static void PadToMinimum(World world, Event ev)
    {
        if (ev.Card.Count == 0 || world.Get(ev.Promotion) is not { } promo) return;

        var booked = new HashSet<FighterId>(ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner }));
        bool small = OrgSizing.SizeOf(promo) <= OrgSize.Regional;
        int guard = 0;

        while (ev.Card.Count < MinCardBouts && guard++ < 40)
        {
            // Prefer a real bout from unbooked, available roster fighters.
            if (RosterBout(world, promo, ev.Date, booked) is { } bout)
            {
                ev.Card.Insert(0, bout);
                booked.Add(bout.RedCorner);
                booked.Add(bout.BlueCorner);
                continue;
            }

            // Out of contracted talent: a small org fills with locals; a bigger
            // promotion just runs the (already deep) card it has.
            if (!small) break;
            var wc = FillerWeightClass(promo);
            var (red, blue) = LocalScene.CreateLocalPair(world, promo, ev, wc);
            ev.Card.Insert(0, new Fight
            {
                Id = world.NewFightId(),
                RedCorner = red.Id,
                BlueCorner = blue.Id,
                WeightClass = wc,
                ScheduledRounds = promo.Rules.StandardRounds,
            });
        }
    }

    /// <summary>
    /// What <see cref="PadToMinimum"/> would add to this card if it ran now,
    /// without adding anything: the same loop, but no fight ids are taken and no
    /// local is minted. Roster bouts name the pair the house would put on; local
    /// filler is a slot (no names exist until the night). Cost is what the night
    /// would pay for them: purses plus one win bonus on average for a roster bout,
    /// two appearance fees for a local pair. Read-only, draw-free.
    /// </summary>
    public static FillerPreview Preview(World world, Event ev)
    {
        var bouts = new List<FillerBout>();
        if (ev.Card.Count == 0 || world.Get(ev.Promotion) is not { } promo)
            return new FillerPreview(bouts, Money.Zero, ev.Card.Count, ev.Card.Count);

        var booked = new HashSet<FighterId>(ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner }));
        bool small = OrgSizing.SizeOf(promo) <= OrgSize.Regional;
        int count = ev.Card.Count, guard = 0;
        while (count < MinCardBouts && guard++ < 40)
        {
            if (RosterPair(world, promo, ev.Date, booked) is { } pair)
            {
                var cost = Money.Zero;
                foreach (var f in new[] { pair.Red, pair.Blue })
                    if (f.Contract is { } c) cost += c.PursePerFight + new Money(c.WinBonus.Amount * 0.5m);
                bouts.Add(new FillerBout(pair.Red.Id, pair.Blue.Id, pair.WeightClass, false, cost));
                booked.Add(pair.Red.Id);
                booked.Add(pair.Blue.Id);
                count++;
                continue;
            }
            if (!small) break;
            bouts.Add(new FillerBout(null, null, FillerWeightClass(promo), true,
                new Money(2 * Financials.LocalAppearanceFee)));
            count++;
        }
        var total = Money.Zero;
        foreach (var b in bouts) total += b.Cost;
        return new FillerPreview(bouts, total, ev.Card.Count, count);
    }

    // The best same-division pairing among unbooked, available roster fighters.
    private static Fight? RosterBout(World world, Promotion promo, DateOnly date, HashSet<FighterId> booked) =>
        RosterPair(world, promo, date, booked) is { } p
            ? new Fight
            {
                Id = world.NewFightId(),
                RedCorner = p.Red.Id,
                BlueCorner = p.Blue.Id,
                WeightClass = p.WeightClass,
                ScheduledRounds = promo.Rules.StandardRounds,
            }
            : null;

    private static (Fighter Red, Fighter Blue, WeightClass WeightClass)? RosterPair(
        World world, Promotion promo, DateOnly date, HashSet<FighterId> booked)
    {
        foreach (var group in world.RosterOf(promo.Id)
                     .Where(f => f.IsMatchReady(date) && !booked.Contains(f.Id))
                     .GroupBy(f => (f.WeightClass, f.Sex)))
        {
            var pool = group.OrderByDescending(FighterAbility.CurrentAbility).ToList();
            if (pool.Count < 2) continue;
            return (pool[0], pool[1], group.Key.WeightClass);
        }
        return null;
    }

    private static WeightClass FillerWeightClass(Promotion promo) =>
        promo.Divisions.FirstOrDefault(d => d.Sex == Sex.Male)?.WeightClass ?? WeightClass.Lightweight;
}
