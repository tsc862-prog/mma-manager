using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// One bout the house would add on the night (a local pair has no names yet).
/// <see cref="Cost"/> is the expected bill (purses plus one win bonus on average);
/// <see cref="Purses"/> is the floor (a draw pays no bonus) and
/// <see cref="Purses"/> + <see cref="WinBonusMax"/> the ceiling.
/// </summary>
public sealed record FillerBout(FighterId? Red, FighterId? Blue, WeightClass WeightClass, bool LocalFiller, Money Cost,
    Money Purses, Money WinBonusMax);

/// <summary>What padding would add to a card: the bouts, their cost, and the card length before and after.</summary>
public sealed record FillerPreview(IReadOnlyList<FillerBout> Bouts, Money Cost, int BoutsBefore, int BoutsAfter)
{
    public bool WouldPad => Bouts.Count > 0;
    public int LocalPairs => Bouts.Count(b => b.LocalFiller);
    public Money Purses => Bouts.Aggregate(Money.Zero, (s, b) => s + b.Purses);
    public Money WinBonusMax => Bouts.Aggregate(Money.Zero, (s, b) => s + b.WinBonusMax);

    public static FillerPreview None(int bouts) => new(Array.Empty<FillerBout>(), Money.Zero, bouts, bouts);
}

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

        var booked = Unavailable(world, ev);
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
    internal static FillerPreview Preview(World world, Event ev)
    {
        var bouts = new List<FillerBout>();
        if (ev.Card.Count == 0 || world.Get(ev.Promotion) is not { } promo)
            return FillerPreview.None(ev.Card.Count);

        var booked = Unavailable(world, ev);
        bool small = OrgSizing.SizeOf(promo) <= OrgSize.Regional;
        int count = ev.Card.Count, guard = 0;
        while (count < MinCardBouts && guard++ < 40)
        {
            if (RosterPair(world, promo, ev.Date, booked) is { } pair)
            {
                Money purses = Money.Zero, halfBonuses = Money.Zero;
                decimal maxBonus = 0m;
                foreach (var f in new[] { pair.Red, pair.Blue })
                    if (f.Contract is { } c)
                    {
                        purses += c.PursePerFight;
                        halfBonuses += new Money(c.WinBonus.Amount * 0.5m);
                        maxBonus = Math.Max(maxBonus, c.WinBonus.Amount);
                    }
                bouts.Add(new FillerBout(pair.Red.Id, pair.Blue.Id, pair.WeightClass, false,
                    purses + halfBonuses, purses, new Money(maxBonus)));
                booked.Add(pair.Red.Id);
                booked.Add(pair.Blue.Id);
                count++;
                continue;
            }
            if (!small) break;
            var fees = new Money(2 * Financials.LocalAppearanceFee);
            bouts.Add(new FillerBout(null, null, FillerWeightClass(promo), true, fees, fees, Money.Zero));
            count++;
        }
        var total = Money.Zero;
        foreach (var b in bouts) total += b.Cost;
        return new FillerPreview(bouts, total, ev.Card.Count, count);
    }

    // Who can't pad this card: anyone already on it, anyone committed to another
    // scheduled card (the forward calendar holds the org's next announced
    // nights), and champions, who only fight for the belt. Shared by the night
    // and the preview, so the preview shows what the night will do.
    private static HashSet<FighterId> Unavailable(World world, Event ev)
    {
        var booked = new HashSet<FighterId>(ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner }));
        foreach (var other in world.Events.Values)
            if (other.Id != ev.Id && other.Status == EventStatus.Scheduled)
                foreach (var f in other.Card) { booked.Add(f.RedCorner); booked.Add(f.BlueCorner); }
        foreach (var t in world.Titles.Values)
            if (t.CurrentChampion is { } champ) booked.Add(champ);
        return booked;
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
            // The player's own card is padded by the player's scouts' read (id as
            // the last word), so neither the preview nor the night picks from the
            // truth. AI cards keep the order they always had.
            var pool = promo.Id == world.PlayerPromotion
                ? group.OrderByDescending(f => Scouting.ScoutingService.PerceivedAbility(world, promo, f))
                       .ThenBy(f => f.Id.Value).ToList()
                : group.OrderByDescending(FighterAbility.CurrentAbility).ToList();
            if (pool.Count < 2) continue;
            return (pool[0], pool[1], group.Key.WeightClass);
        }
        return null;
    }

    private static WeightClass FillerWeightClass(Promotion promo) =>
        promo.Divisions.FirstOrDefault(d => d.Sex == Sex.Male)?.WeightClass ?? WeightClass.Lightweight;
}
