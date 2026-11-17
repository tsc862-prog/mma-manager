using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// Which of a house's belts are still contested. A belt is current while a division of its house carries it
/// (<see cref="Division.Title"/>). A belt no division carries is RETIRED: an Origins house that draws weight lines
/// retires its open-weight belt (OriginsReforms.DrawLines drops the open division and closes the
/// reign). A retired belt stays in <see cref="World.Titles"/> and <see cref="Promotion.Titles"/> because it is history
/// (lineages, the biographer, career honours and title moments read it); anything that lists a house's CURRENT belts
/// reads them through here, or through the divisions, never straight off <see cref="Promotion.Titles"/>.
/// </summary>
public static class Belts
{
    /// <summary>True when no division of the belt's house carries it any more (and the house still exists).</summary>
    public static bool IsRetired(World world, Title title) =>
        world.Get(title.Promotion) is { } promo && !promo.Divisions.Any(d => d.Title == title.Id);

    /// <summary>The house's belts that are still contested, in the house's own title order.</summary>
    public static IEnumerable<Title> Current(World world, Promotion promo) =>
        promo.Titles.Select(world.Get).OfType<Title>().Where(t => promo.Divisions.Any(d => d.Title == t.Id));
}
