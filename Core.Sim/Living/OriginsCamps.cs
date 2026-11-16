using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation.Origins;

namespace MmaManager.Core.Sim.Living;

/// <summary>A room the player's house can pay to cross-train (Origins): where, what it costs, and how many of his men train there.</summary>
public sealed record CrossTrainingOption(CampId Camp, string Name, string City, PrimaryStyle Teaches, Money Cost, int YourMen);

/// <summary>
/// The rooms of an Origins world learning to teach everything (phase 3). In 1993 every gym teaches one art:
/// a man who trains there grows in his own half of the game and nowhere else
/// (<see cref="FighterDevelopment.Advance"/>'s one-art list; <see cref="TrainsOneArt"/>). The first famous
/// cross-training camp changes that (<see cref="FirstCampChapter"/>; Fenwick Street is the authored one): a
/// room that teaches striking, wrestling and the ground on one timetable (<see cref="PrimaryStyle.WellRounded"/>
/// once the chapter is open) grows a man across the whole game, the way today's camps do.
/// <list type="bullet">
/// <item>Once the first camp is famous, other rooms copy its timetable: a keyed monthly roll per room, likelier for
/// a room with a name, much likelier once the specialists have fallen (<see cref="RunMonthly"/>).</item>
/// <item>Rooms earn their names: from the chapter's window on, a room's reputation climbs a point a month toward
/// what its men have done (their fame, their belts) and a point more for teaching everything; a room is never
/// marked down (<see cref="ReputationTarget"/>).</item>
/// <item>The player's path: once the chapter is open his house can pay to bring a second and third art into a room
/// where his men train (<see cref="Options"/>, <see cref="TryFund"/>, <see cref="Refusal"/>); a room that reaches
/// the chapter's reputation that way can BE the first famous camp, credited to the house with the most men there.</item>
/// </list>
/// Today's mode: every room teaches everything (<see cref="CrossTrains"/> is always true) and nothing here runs.
/// Keyed rolls only.
/// </summary>
public static class OriginsCamps
{
    public const string FirstCampChapter = "first-cross-training-camp";
    public const string SpecialistsFallChapter = "specialists-fall";

    /// <summary>A one-art room's monthly chance of taking up cross-training once the first camp is famous (plus a point per 30 of reputation).</summary>
    public const double ConvertChance = 0.01;

    /// <summary>The extra monthly chance once the specialists have fallen (every room that wants to win follows).</summary>
    public const double ConvertAfterFall = 0.04;

    /// <summary>What a house's money buys a room when it pays for cross-training: coaches for the other arts, and a name.</summary>
    public const int FundedCoaching = 6, FundedReputation = 6;

    private const ulong ConvertSalt = 0x0219_C0A7_0009UL;

    /// <summary>
    /// Does this room teach the whole game? Always in today's mode. In an Origins world: a room that teaches
    /// everything (<see cref="PrimaryStyle.WellRounded"/>) once the first famous camp's chapter is open (live
    /// or fired); before that nobody teaches that way.
    /// </summary>
    public static bool CrossTrains(World world, Camp camp) =>
        world.Mode != WorldMode.Origins
        || (camp.Specialty == PrimaryStyle.WellRounded && OriginsTimeline.IsOpen(world, FirstCampChapter, world.CurrentDate));

    /// <summary>
    /// Does this man train one art only (Origins)? Only in the one-art era, and only a man who learned one art:
    /// <list type="bullet">
    /// <item>once the specialists have fallen (<see cref="SpecialistsFallChapter"/>) nobody does: every room, ring
    /// and garage teaches the whole game, and a man who trains alone has seen enough of it to copy;</item>
    /// <item>a man who arrived knowing two arts (his listed style is <see cref="PrimaryStyle.WellRounded"/>, which
    /// never changes) keeps working both wherever he trains;</item>
    /// <item>anyone else trains one art when he trains alone or in a room that teaches one.</item>
    /// </list>
    /// False in today's mode, always. His growth then goes into his stronger half of the game (<see cref="FighterDevelopment"/>).
    /// </summary>
    public static bool TrainsOneArt(World world, Fighter f) =>
        world.Mode == WorldMode.Origins
        && f.PrimaryStyle != PrimaryStyle.WellRounded
        && !world.Era.HasFired(SpecialistsFallChapter)
        && (f.Camp is not { } id || world.Get(id) is not { } camp || !CrossTrains(world, camp));

    // ------------------------------------------------------------------ the month

    /// <summary>The month's rooms (Origins; from the timeline's monthly block): names earned, timetables copied, the player told.</summary>
    public static void RunMonthly(World world, DateOnly date)
    {
        if (world.Mode != WorldMode.Origins) return;
        bool open = OriginsTimeline.IsOpen(world, FirstCampChapter, date);
        if (!open) return;
        TellThePlayer(world, date);

        // Names are earned (never marked down).
        foreach (var camp in world.Camps.Values.OrderBy(c => c.Id.Value))
            if (camp.Reputation < Math.Min(90, ReputationTarget(world, camp))) camp.Reputation++;

        if (!world.Era.HasFired(FirstCampChapter)) return;
        int month = OriginsTimeline.Month(world, date);
        bool fallen = world.Era.HasFired(SpecialistsFallChapter);
        foreach (var camp in world.Camps.Values.Where(c => c.Specialty != PrimaryStyle.WellRounded).OrderBy(c => c.Id.Value).ToList())
        {
            double chance = ConvertChance + camp.Reputation / 3000.0 + (fallen ? ConvertAfterFall : 0);
            if (OriginsTimeline.Roll(world, ConvertSalt, (ulong)camp.Id.Value, (ulong)month) >= chance) continue;
            CrossTrain(world, camp, date, funder: null);
        }
    }

    /// <summary>
    /// What a room's name should be on its record: 15, plus 0.6 of the average fame of its three best-known
    /// active men, plus 8 for every reigning champion among them, plus 10 for teaching the whole game.
    /// </summary>
    public static int ReputationTarget(World world, Camp camp)
    {
        var men = world.Fighters.Values.Where(f => f.Camp == camp.Id && f.Status != FighterStatus.Retired)
            .OrderByDescending(f => f.Intangibles.Popularity.Value).ThenBy(f => f.Id.Value).ToList();
        double fame = men.Take(3).Select(f => (double)f.Intangibles.Popularity.Value).DefaultIfEmpty(0).Average();
        var champs = world.Titles.Values.Where(t => t.CurrentChampion is not null).Select(t => t.CurrentChampion!.Value).ToHashSet();
        int belts = men.Count(f => champs.Contains(f.Id));
        return (int)(15 + 0.6 * fame + 8 * belts + (CrossTrains(world, camp) ? 10 : 0));
    }

    /// <summary>A room takes up the whole game: it now teaches everything, and the coaches it hired lift it.</summary>
    private static void CrossTrain(World world, Camp camp, DateOnly date, Promotion? funder)
    {
        var was = camp.Specialty;
        camp.Specialty = PrimaryStyle.WellRounded;
        camp.CoachQuality = Math.Min(90, camp.CoachQuality + (funder is null ? 4 : FundedCoaching));
        if (funder is not null) camp.Reputation = Math.Min(90, camp.Reputation + FundedReputation);
        string city = world.Get(camp.City)?.Name ?? "its city";
        News.Post(world, date, NewsCategory.General,
            funder is null ? $"{camp.Name} copies the new timetable" : $"{funder.Name} pays for {camp.Name} to teach the whole game",
            $"The {Half(was)} room in {city} now teaches {OtherHalf(was)} as well: striking, takedowns and the ground on one timetable.",
            funder?.Id ?? default);
    }

    private static string Half(PrimaryStyle s) => s switch
    {
        PrimaryStyle.Striker or PrimaryStyle.Kickboxer or PrimaryStyle.Brawler => "striking",
        PrimaryStyle.Wrestler or PrimaryStyle.ClinchFighter => "wrestling",
        _ => "grappling",
    };

    private static string OtherHalf(PrimaryStyle s) => s switch
    {
        PrimaryStyle.Striker or PrimaryStyle.Kickboxer or PrimaryStyle.Brawler => "wrestling and the ground",
        _ => "striking",
    };

    /// <summary>The player hears, once, the month cross-training becomes something his money can buy.</summary>
    private static void TellThePlayer(World world, DateOnly date)
    {
        if (world.Get(world.PlayerPromotion) is not { Defunct: false } me) return;
        // Told the month the chapter opened: open now (the caller checked), not open at the last monthly pass.
        if (OriginsReforms.OpenOn(world, FirstCampChapter, OriginsBacklash.PreviousMonthlyTick(date))) return;
        var options = Options(world, me, date);
        Mail.Post(world, date, MailCategory.BoardNote, Mail.Owner(world), "Our men could learn the whole game",
            "Gyms that teach striking, wrestling and the ground in one room are turning out fighters who can do all three. "
            + (options.Count == 0
                ? "None of the rooms our men train in teaches that way, and none we could pay for yet: when one of ours trains in a room we can fund, it's our call."
                : $"We could pay for {options[0].Name} (where {options[0].YourMen} of ours train) to hire the coaches: {options[0].Cost.Amount:N0}. It's our call; nobody will make it for us."));
    }

    // ------------------------------------------------------------------ the player's path

    /// <summary>What it costs a house to bring the whole game into a room: 15,000 and 400 a point of its reputation.</summary>
    public static Money Cost(Camp camp) => new(15_000m + camp.Reputation * 400m);

    /// <summary>
    /// The rooms this house can pay to cross-train today (Origins, the chapter open): rooms that teach one art
    /// where at least one of its active men trains, most of its men first (ties on id). Empty in today's mode.
    /// </summary>
    public static IReadOnlyList<CrossTrainingOption> Options(World world, Promotion promo, DateOnly date)
    {
        if (world.Mode != WorldMode.Origins || promo.Defunct || !OriginsTimeline.IsOpen(world, FirstCampChapter, date))
            return Array.Empty<CrossTrainingOption>();
        var counts = world.RosterOf(promo.Id).Where(f => f.Status != FighterStatus.Retired && f.Camp is not null)
            .GroupBy(f => f.Camp!.Value).ToDictionary(g => g.Key, g => g.Count());
        return world.Camps.Values
            .Where(c => counts.ContainsKey(c.Id) && c.Specialty != PrimaryStyle.WellRounded)
            .OrderByDescending(c => counts[c.Id]).ThenBy(c => c.Id.Value)
            .Select(c => new CrossTrainingOption(c.Id, c.Name, world.Get(c.City)?.Name ?? "", c.Specialty, Cost(c), counts[c.Id]))
            .ToList();
    }

    /// <summary>Why this house can't pay for this room to cross-train today, in words for the screen; null when it can.</summary>
    public static string? Refusal(World world, Promotion promo, CampId campId, DateOnly date)
    {
        if (world.Mode != WorldMode.Origins) return "Today's gyms already teach the whole game.";
        if (promo.Defunct) return "The promotion has closed.";
        if (world.Get(campId) is not { } camp) return "There is no such gym.";
        if (camp.Specialty == PrimaryStyle.WellRounded && OriginsTimeline.IsOpen(world, FirstCampChapter, date))
            return $"{camp.Name} already teaches striking, wrestling and the ground.";
        if (!OriginsTimeline.IsOpen(world, FirstCampChapter, date))
            return "Nobody teaches that way yet: every gym in the sport teaches its own art, and nobody has shown it can be done.";
        if (!world.RosterOf(promo.Id).Any(f => f.Status != FighterStatus.Retired && f.Camp == campId))
            return $"None of your men train at {camp.Name}: a gym takes a promoter's money for its own people.";
        var cost = Cost(camp);
        if (promo.Balance.Amount < cost.Amount) return $"It costs {cost.Amount:N0} to hire the coaches, and the bank can't cover it.";
        return null;
    }

    /// <summary>
    /// Pay for a room to teach the whole game (the player's path; the AI's rooms convert on their own). False,
    /// with the reason, when <see cref="Refusal"/> says no. The room teaches everything from now on, its coaching
    /// and name rise, and the house pays the coaches' fee.
    /// </summary>
    public static bool TryFund(World world, Promotion promo, CampId campId, DateOnly date, out string? refused)
    {
        refused = Refusal(world, promo, campId, date);
        if (refused is not null) return false;
        var camp = world.Get(campId)!;
        Finance.Ledger.Expense(promo, date, FinanceCategory.CampFees, Cost(camp), $"Cross-training coaches — {camp.Name}");
        CrossTrain(world, camp, date, promo);
        return true;
    }

    // ------------------------------------------------------------------ the chapter's doer

    /// <summary>
    /// The famous cross-training room the chapter is about: the best-known room that teaches everything at or
    /// over the reputation, with the house that has the most of its men (ties on id) and its best-known man.
    /// </summary>
    internal static ChapterDoer? FamousRoom(World world, int reputation)
    {
        var camp = world.Camps.Values.Where(k => k.Specialty == PrimaryStyle.WellRounded && k.Reputation >= reputation)
            .OrderByDescending(k => k.Reputation).ThenBy(k => k.Id.Value).FirstOrDefault();
        if (camp is null) return null;
        var men = world.Fighters.Values.Where(f => f.Camp == camp.Id && f.Status == FighterStatus.Active).ToList();
        var house = men.Where(f => f.Contract is not null).GroupBy(f => f.Contract!.Promotion)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Value).Select(g => (PromotionId?)g.Key).FirstOrDefault();
        var star = men.Where(f => house is null || f.Contract?.Promotion == house)
            .OrderByDescending(f => f.Intangibles.Popularity.Value).ThenBy(f => f.Id.Value).Select(f => (FighterId?)f.Id).FirstOrDefault();
        return new ChapterDoer(house, star, camp.City, Note: camp.Name);
    }
}
