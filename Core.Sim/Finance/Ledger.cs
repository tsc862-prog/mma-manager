using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Finance;

/// <summary>
/// The one doorway every dollar passes through. Recording a movement both adjusts
/// a promotion's running balance and appends a categorised, dated entry to its
/// ledger, so nothing ever moves the balance without leaving a trace the finance
/// screen can total. Deterministic; the world tick charges overhead each month.
/// </summary>
public static class Ledger
{
    /// <summary>Record a signed movement (positive = income, negative = expense) and move the balance.</summary>
    public static void Record(Promotion promo, DateOnly date, FinanceCategory category, Money amount, string description)
    {
        promo.Balance += amount;
        promo.Ledger.Add(new LedgerEntry(date, category, amount, description));
    }

    /// <summary>Convenience for a positive income entry.</summary>
    public static void Income(Promotion promo, DateOnly date, FinanceCategory category, Money amount, string description)
    {
        if (amount.Amount == 0) return;
        Record(promo, date, category, new Money(Math.Abs(amount.Amount)), description);
    }

    /// <summary>Convenience for a negative expense entry (pass a positive amount).</summary>
    public static void Expense(Promotion promo, DateOnly date, FinanceCategory category, Money amount, string description)
    {
        if (amount.Amount == 0) return;
        Record(promo, date, category, new Money(-Math.Abs(amount.Amount)), description);
    }

    /// <summary>Monthly retainer per exclusive fighter inside the org's tier roster target — nominal by design.
    /// (Was 500 at 4-8/division; at the doubled division depth that "pittance" became a mid-tier org killer.)</summary>
    public const decimal RetainerPerSpot = 200m;

    /// <summary>Monthly retainer per exclusive fighter ABOVE the target — the anti-hoarding tax.</summary>
    public const decimal HoardingRetainerPerSpot = 3_000m;

    /// <summary>
    /// Monthly running cost of keeping the lights on — a base outlay that grows
    /// with the brand plus a retainer on the roster you keep exclusive. The
    /// retainer is deliberately NOT a bankruptcy lever: fighters earn their real
    /// money per fight (purses at settle), and a roster inside the org's tier
    /// target costs a pittance to carry. Only spots BEYOND the target get taxed
    /// hard — its one job is to make hoarding fighters expensive. Only EXCLUSIVE
    /// fighters count: a non-exclusive fighter earns on other cards and only
    /// costs their per-fight purse.
    /// </summary>
    public static void ChargeMonthlyOverhead(World world, DateOnly date)
    {
        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct))
        {
            int retained = world.RosterOf(promo.Id)
                .Count(f => f.Status != FighterStatus.Retired && (f.Contract?.IsExclusive ?? true));
            int target = Org.OrgSizing.RosterTargetPerDivision(Org.OrgSizing.SizeOf(promo))
                       * Math.Max(1, promo.Divisions.Count);
            decimal retainer = Math.Min(retained, target) * RetainerPerSpot
                             + Math.Max(0, retained - target) * HoardingRetainerPerSpot;
            // The base outlay scales with the OPERATION, not just the brand: a
            // club-show outfit is a rented office and a van, not a headquarters.
            // (Income is per-event now — a City org's fixed costs must be small
            // enough that its runway is a runway, not a countdown.)
            decimal baseCost = Org.OrgSizing.SizeOf(promo) switch
            {
                OrgSize.International => 45_000m,
                OrgSize.Continental => 32_000m,
                OrgSize.National => 22_000m,
                OrgSize.Regional => 14_000m,
                _ => 8_000m,
            };
            // Staff salaries are a PLAYER cost. Every org in the world now carries
            // a named front office — that's what makes a rival's scout worth
            // poaching and a board note worth signing — but the AI's books model
            // its back office inside the base outlay above rather than line by
            // line, so billing them again would double-charge every promotion in
            // the world and fold the small halls inside a year.
            decimal cost = baseCost + promo.Prestige.Value * 2_000m + retainer
                         + (promo.IsPlayerControlled ? Org.StaffService.MonthlySalaries(promo).Amount : 0m);
            Expense(promo, date, FinanceCategory.Overhead, new Money(cost), "Monthly operating costs");
        }
    }
}
