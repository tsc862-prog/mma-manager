namespace MmaManager.Core.Sim.Living;

/// <summary>
/// An authored main event: the fight a designed show is MEANT to be headlined
/// by, because the lore already says so. <paramref name="Show"/> is the show's
/// name without the year (a signature name from <see cref="SignatureEvents"/>);
/// <paramref name="Red"/>/<paramref name="Blue"/> are designed fighters' last
/// names; <paramref name="Pitch"/> is the line the sport writes when it's made.
/// </summary>
public sealed record DesignedBooking(string Promotion, string Show, string Red, string Blue, string Pitch);

/// <summary>
/// The designed-bookings table, read when a card is announced (see
/// <see cref="ForwardCalendar"/>). A row is honoured when both fighters exist,
/// are active, are signed to that promotion, share a division, will be fit on
/// the night and aren't committed elsewhere — and only until the fight has
/// actually happened once (since the world began). When a row can't be
/// honoured the show simply falls back to the ordinary choice (a hot feud, then
/// the matchmaker's belt), with no news of the fight that wasn't.
///
/// APPEND-ONLY: the database agent owns the authored content and adds rows at
/// the end. Row order is the order rows are tried for a show.
/// </summary>
public static class DesignedBookings
{
    public static readonly IReadOnlyList<DesignedBooking> All = new DesignedBooking[]
    {
        new("ICF", "ICF Emperor's New Year", "Sato", "Kang",
            "Busan's hardest export has called Japan's beloved son a museum piece in two languages and promised "
            + "to prove it in Tokyo. The New Year show is where the ICF finally lets him try."),
        new("GFC", "GFC Desert Clash", "Dudaev", "Al-Suwaidi",
            "The hired gun who has worn three promotions' belts against the Emirati prodigy who fights for the badge, "
            + "not the bonus. The Gulf's money against the Gulf's son."),
    };

    /// <summary>The rows written for this promotion's show, in table order.</summary>
    public static IEnumerable<DesignedBooking> For(string promotion, string show) =>
        All.Where(b => b.Promotion == promotion && b.Show == show);
}
