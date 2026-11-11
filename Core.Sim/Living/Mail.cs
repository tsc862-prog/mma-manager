using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The player's inbox. The sim drops in alerts the player should act on — a deal
/// about to lapse, a warning from the owner about the money, a milestone — with a
/// light de-dupe so the same open issue isn't mailed every month.
/// </summary>
public static class Mail
{
    private const int MaxItems = 500;
    private const decimal LowBalanceFloor = 250_000m;

    /// <summary>
    /// Who a board note is actually FROM. Notes used to be signed "The Owner" and
    /// "The Board" — abstractions, which is a strange thing to work for. Where the
    /// player's promotion has a named owner, they sign their own letters; a
    /// promotion the player FOUNDED has no owner but the player, so those notes
    /// come from the accountant instead of from nobody.
    /// </summary>
    public static string Owner(World world) =>
        world.Get(world.PlayerPromotion)?.OwnerName is { Length: > 0 } name ? name : "The Accountant";

    /// <summary>The player's own staff member in a role, by name — or the desk itself when nobody holds it.</summary>
    public static string Desk(World world, StaffRole role, string fallback) =>
        world.Get(world.PlayerPromotion)?.Staff.FirstOrDefault(s => s.Role == role)?.Name ?? fallback;

    public static void Post(World world, DateOnly date, MailCategory category, string from, string subject, string body,
        FighterId? aboutFighter = null, EventId? aboutEvent = null)
    {
        // Don't re-send an identical still-unread message.
        if (world.Mail.Any(m => !m.Read && m.Subject == subject)) return;
        world.Mail.Add(new MailMessage
        {
            Date = date, Category = category, From = from, Subject = subject, Body = body,
            AboutFighter = aboutFighter, AboutEvent = aboutEvent,
        });
        if (world.Mail.Count > MaxItems) world.Mail.RemoveRange(0, world.Mail.Count - MaxItems);
    }

    /// <summary>Monthly: warn about the player's deals that are running out, and the state of the books.</summary>
    public static void MonthlyReview(World world, DateOnly date)
    {
        if (world.Get(world.PlayerPromotion) is not { } promo) return;

        foreach (var f in world.RosterOf(world.PlayerPromotion)
                     .Where(f => f.Status == FighterStatus.Active && f.Contract is { FightsRemaining: <= 1 }))
        {
            int left = f.Contract!.FightsRemaining;
            string state = left == 0 ? "has expired" : "is down to its final fight";
            // Rivals sniff around your good fighters when their deals run low —
            // said only when a real suitor exists (the poaching market's own
            // check, on the rival's read), never off his true ability.
            // (A lifer who turned a rival down on this deal hears nothing here,
            // as WarnExpiring stays silent: his notice comes at expiry.)
            string poaching = Contracts.PoachingService.HasSuitor(world, f)
                              && f.Contract.BondNoticeOn != Contract.RefusalTold
                ? " Rival promotions are already circling — lock them up before they walk."
                : "";
            Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                $"Contract: {f.FullName}",
                $"{f.FullName}'s deal {state}. Re-sign them on the Contracts screen or risk losing them to free agency.{poaching}",
                aboutFighter: f.Id);
        }

        if (promo.Balance.Amount < LowBalanceFloor)
            Post(world, date, MailCategory.BoardNote, Owner(world),
                "We need to talk about the money",
                $"Cash is down to {promo.Balance}. Tighten up the books before we're in the red.");
    }

    /// <summary>A belt landing on the player's roster is worth a note.</summary>
    public static void TitleWon(World world, Title title, Fighter champ, DateOnly date)
    {
        if (title.Promotion != world.PlayerPromotion) return;
        Post(world, date, MailCategory.Milestone, Owner(world),
            $"New champion: {champ.FullName}",
            $"{champ.FullName} has captured the {title.Name}. Great for business.",
            aboutFighter: champ.Id);
    }
}
