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
public static class CardFiller
{
    /// <summary>The fewest bouts a professional card should carry.</summary>
    public const int MinCardBouts = 6;

    /// <summary>Bring a card up to the minimum length before it runs.</summary>
    public static void PadToMinimum(World world, Event ev)
    {
        if (ev.Card.Count == 0 || world.Get(ev.Promotion) is not { } promo) return;

        var booked = new HashSet<FighterId>(ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner }));
        // Nobody committed to another scheduled card (the forward calendar holds
        // the org's next announced nights) pads this one, and champions only
        // fight for the belt.
        foreach (var other in world.Events.Values)
            if (other.Id != ev.Id && other.Status == EventStatus.Scheduled)
                foreach (var f in other.Card) { booked.Add(f.RedCorner); booked.Add(f.BlueCorner); }
        foreach (var t in world.Titles.Values)
            if (t.CurrentChampion is { } champ) booked.Add(champ);
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

    // The best same-division pairing among unbooked, available roster fighters.
    private static Fight? RosterBout(World world, Promotion promo, DateOnly date, HashSet<FighterId> booked)
    {
        foreach (var group in world.RosterOf(promo.Id)
                     .Where(f => f.IsMatchReady(date) && !booked.Contains(f.Id))
                     .GroupBy(f => (f.WeightClass, f.Sex)))
        {
            var pool = group.OrderByDescending(FighterAbility.CurrentAbility).ToList();
            if (pool.Count < 2) continue;
            return new Fight
            {
                Id = world.NewFightId(),
                RedCorner = pool[0].Id,
                BlueCorner = pool[1].Id,
                WeightClass = group.Key.WeightClass,
                ScheduledRounds = promo.Rules.StandardRounds,
            };
        }
        return null;
    }

    private static WeightClass FillerWeightClass(Promotion promo) =>
        promo.Divisions.FirstOrDefault(d => d.Sex == Sex.Male)?.WeightClass ?? WeightClass.Lightweight;
}
