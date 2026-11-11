using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// An authored booking: a bout the lore says a particular show is built around.
/// <list type="bullet">
/// <item><paramref name="Org"/>: the promotion's abbreviation.</item>
/// <item><paramref name="Day"/>: roughly when, in days from the world's start date;
/// the calendar resolves the exact night (the org's show week nearest that day, or
/// for a flagship the flagship's own night in that month).</item>
/// <item><paramref name="AnnounceDaysBefore"/>: how far ahead the card goes up (if
/// that is earlier than the org's normal lead, the card is announced early).</item>
/// <item><paramref name="EventName"/>: null = that night's numbered card; a
/// signature name (<see cref="SignatureEvents"/>) = that flagship.</item>
/// <item><paramref name="Red"/>/<paramref name="Blue"/>: designed fighters' FULL
/// names ("Daichi Sato"). A null <paramref name="Blue"/> means the matchmaker's
/// pick for the shot: the champion's challenger when <paramref name="Red"/> holds
/// the belt, the champion when he doesn't.</item>
/// <item><paramref name="Wc"/>/<paramref name="Sex"/>: the division it's fought in.</item>
/// <item><paramref name="Title"/>: the division's belt is on the line (while one of
/// the two holds it, or it's vacant).</item>
/// <item><paramref name="Order"/>: 0 = main event, 1 = co-main.</item>
/// <item><paramref name="Pitch"/>: optional line the announcement carries.</item>
/// </list>
/// </summary>
public sealed record DesignedBooking(string Org, int Day, int AnnounceDaysBefore, string? EventName,
    string Red, string? Blue, WeightClass Wc, Sex Sex, bool Title, int Order, string? Pitch = null);

/// <summary>
/// The designed-bookings table, read when a card is announced (see
/// <see cref="ForwardCalendar"/>). Each row is a one-time booking for its night.
/// A row is honoured when its fighters exist, are active, signed to that
/// promotion, in that division, fit on the night and not committed elsewhere.
/// When a row can't be honoured the bout is dropped with an "off" headline and
/// nobody is substituted INTO it; the card itself goes ahead with the ordinary
/// choice at the top. Until its card is announced, a row's fighters are held
/// back from the promotion's other nights (inside
/// <see cref="ForwardCalendar.ReserveWeeks"/> of the date). A row for the
/// player's own promotion never fires: the player books their own cards.
///
/// APPEND-ONLY: the database agent owns this content and adds rows at the end
/// (the story authors' table T4). Rows whose show doesn't exist yet (a flagship
/// that isn't in <see cref="SignatureEvents"/>) simply never resolve to a night.
/// </summary>
public static class DesignedBookings
{
    public static readonly IReadOnlyList<DesignedBooking> All = new DesignedBooking[]
    {
        // 4.1 — the dome show the ICF's lore always promised.
        new("ICF", 14, 42, "ICF Emperor's New Year", "Daichi Sato", "Ji-ho Kang", WeightClass.Lightweight, Sex.Male, true, 0,
            Pitch: "Busan's hardest export has called Japan's beloved son a museum piece in two languages and promised "
                 + "to prove it in Tokyo. The New Year show is where the ICF finally lets him try."),
        // 14.1 — the Gulf's money against the Gulf's son.
        new("GFC", 65, 42, "GFC Desert Clash", "Movsar Dudaev", "Khalid Al-Suwaidi", WeightClass.Middleweight, Sex.Male, true, 0,
            Pitch: "The hired gun who has worn three promotions' belts against the Emirati prodigy who fights for the badge, "
                 + "not the bonus. The Gulf's money against the Gulf's son."),
        // 1.5 and 2.9 — the AFC's summer spectacular (an AI-run AFC only).
        new("AFC", 189, 56, "AFC International", "Dane Mercer", "Jae-won Seo", WeightClass.Lightweight, Sex.Male, true, 0),
        new("AFC", 189, 56, "AFC International", "Katya Volkova", "Sarah Donovan", WeightClass.Bantamweight, Sex.Female, true, 1),
        // 3.8 — master and student.
        new("IFA", 130, 49, "IFA Iron Gala", "Dmitri Volkov", "Kirill Baranov", WeightClass.Middleweight, Sex.Male, true, 0),
        // 5.7 — the house's future gets her shot.
        new("KAC", 40, 28, null, "Ayaka Kondo", null, WeightClass.Bantamweight, Sex.Female, true, 0),
        // 7.9 — the two Brazilian houses' champions on their big nights.
        new("RCS", 45, 49, "RCS Carnaval de Combate", "Rafael Souza", null, WeightClass.Lightweight, Sex.Male, true, 0),
        new("CK", 255, 56, "CK Reinado", "Wallace Bandeira", null, WeightClass.Heavyweight, Sex.Male, true, 0),
        // 9.5 — the condor and the hummingbird.
        new("CDC", 21, 21, null, "Mateo Quispe", "Alonso Huanca", WeightClass.Flyweight, Sex.Male, true, 0),
        // 10.6 — the Lion of Lagos at home.
        new("SCL", 60, 35, null, "Chidi Okafor", null, WeightClass.Middleweight, Sex.Male, true, 0),
        // 11.8 — Table Mountain against the Rhino.
        new("CPF", 75, 35, null, "Sipho Ndlovu", "Thabo Mokoena", WeightClass.Heavyweight, Sex.Male, true, 0),
        // 13.7 — the Tide against the Reef.
        new("SCM", 90, 42, null, "Manaia Rahiri", "Sione Tuilagi", WeightClass.Heavyweight, Sex.Male, true, 0),
        // 6.8 — the golden boy's title shot on the temple-fair circuit's big night.
        new("MFC", 320, 56, "MFC Temple Fair Night", "Arthit Saengkham", null, WeightClass.Featherweight, Sex.Male, true, 0),
    };
}
