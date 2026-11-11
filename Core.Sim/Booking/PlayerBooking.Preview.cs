using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Booking;

public sealed partial class PlayerBooking
{
    /// <summary>
    /// The night as <see cref="Project"/> would read it if this bout were on the
    /// card: added in <paramref name="slot"/> (where the Card Desk places a new
    /// bout), or, with <paramref name="replacing"/>, put in that booked bout's
    /// place the way <see cref="ReplaceFighter(World, Event, FightId, FighterId, FighterId, out string?)"/>
    /// would (same place, same rounds; the belt stays only while the champion is
    /// in or the title is vacant). Nothing is validated, booked or minted: the
    /// projection runs on a detached copy of the event, takes no fight id and
    /// touches no RNG stream, so the world is exactly as it was. Fogged like
    /// <see cref="Project"/> (every figure is public or the player's own books).
    /// </summary>
    public CardProjection ProjectWith(World world, Event ev, FighterId red, FighterId blue, int rounds, bool titleFight,
        BoutSlot slot, FightId? replacing = null)
    {
        if (!IsMine(world, ev)) return new CardProjection();
        var card = new List<Fight>(ev.Card);
        var copy = new Event
        {
            Id = ev.Id, Promotion = ev.Promotion, Name = ev.Name, Date = ev.Date,
            Venue = ev.Venue, VenueId = ev.VenueId, Region = ev.Region, Tier = ev.Tier, Status = ev.Status,
            MainCardFights = ev.MainCardFights, HypeBonus = ev.HypeBonus, Card = card,
        };
        var r = world.Get(red);
        var division = r is null ? null : world.Get(ev.Promotion)?.Divisions.FirstOrDefault(d => d.Matches(r));
        var hypothetical = new FightId(-1);   // never a real id: nothing is taken from the world's counter

        int at = replacing is { } fid ? card.FindIndex(f => f.Id == fid) : -1;
        if (at >= 0)
        {
            var bout = card[at];
            bool keep = bout.IsTitleFight && bout.Title is { } tid && world.Get(tid) is { } t
                        && (t.CurrentChampion is not { } champ || champ == red || champ == blue);
            card[at] = new Fight
            {
                Id = bout.Id, RedCorner = red, BlueCorner = blue, WeightClass = bout.WeightClass,
                ScheduledRounds = bout.ScheduledRounds, CatchweightLbs = bout.CatchweightLbs,
                IsTitleFight = keep, Title = keep ? bout.Title : null,
            };
            return Project(world, copy);
        }

        // As AddBout books it (at the bottom of the bill), then where the desk moves it.
        card.Insert(0, new Fight
        {
            Id = hypothetical, RedCorner = red, BlueCorner = blue, WeightClass = r?.WeightClass ?? default,
            ScheduledRounds = rounds, IsTitleFight = titleFight, Title = titleFight ? division?.Title : null,
        });
        int n = card.Count;
        int to = slot switch
        {
            BoutSlot.MainEvent => n - 1,
            BoutSlot.CoMain => Math.Max(0, n - 2),
            BoutSlot.MainCard => Math.Max(0, n - Math.Min(n, MainCardOnTheNight(world, copy))),
            _ => 0,
        };
        if (to > 0)
        {
            var added = card[0];
            card.RemoveAt(0);
            card.Insert(to, added);
        }
        return Project(world, copy);
    }
}
