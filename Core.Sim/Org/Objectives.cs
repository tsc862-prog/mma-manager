using MmaManager.Core.Model;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// The owner's goals for the player. Seeded at the start of each year and resolved
/// by their deadline; meeting or missing one drops a note in the inbox. Kept
/// deliberately evaluable from what the sim already tracks (the books, the cards).
/// </summary>
public static class Objectives
{
    /// <summary>Give the player a fresh set of goals for the year ending <paramref name="deadline"/>.</summary>
    public static void Seed(Promotion promo, DateOnly start, DateOnly deadline)
    {
        promo.Objectives.Add(new Objective
        {
            Kind = ObjectiveKind.StayProfitable,
            Description = "Finish the year in the black.",
            Start = start, Deadline = deadline,
        });
        promo.Objectives.Add(new Objective
        {
            Kind = ObjectiveKind.RunEvents,
            Description = "Promote at least 6 shows this year.",
            Target = 6, Start = start, Deadline = deadline,
        });
    }

    /// <summary>Resolve any objective that has been met or has hit its deadline.</summary>
    public static void Evaluate(World world, DateOnly date)
    {
        if (world.Get(world.PlayerPromotion) is not { } promo) return;

        foreach (var obj in promo.Objectives.Where(o => o.Status == ObjectiveStatus.Active))
        {
            switch (obj.Kind)
            {
                case ObjectiveKind.StayProfitable:
                    if (date >= obj.Deadline)
                        Resolve(world, obj, promo.Balance.Amount >= 0, date);
                    break;

                case ObjectiveKind.RunEvents:
                    int done = world.Events.Values.Count(e =>
                        e.Promotion == promo.Id && e.Status == EventStatus.Completed &&
                        e.Date >= obj.Start && e.Date <= obj.Deadline);
                    if (done >= obj.Target) Resolve(world, obj, true, date);
                    else if (date >= obj.Deadline) Resolve(world, obj, false, date);
                    break;
            }
        }
    }

    private static void Resolve(World world, Objective obj, bool met, DateOnly date)
    {
        obj.Status = met ? ObjectiveStatus.Met : ObjectiveStatus.Failed;
        Mail.Post(world, date, MailCategory.BoardNote, "The Owner",
            met ? $"Objective met: {obj.Description}" : $"Objective missed: {obj.Description}",
            met ? "Good work. Keep it up." : "Not good enough. We'll be watching.");
    }
}
