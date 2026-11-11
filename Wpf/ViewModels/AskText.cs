using MmaManager.Core.Model;
using MmaManager.Core.Sim.Contracts;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// The player-facing words for what a fighter asks of YOUR promotion and how
/// bound he is to it. The numbers come from the sim (<see cref="AskingQuote"/>,
/// <see cref="Lifers.BondOf"/>); this only phrases them, so the contracts
/// screen, the fighter profile and the negotiating room say the same thing.
/// </summary>
public static class AskText
{
    /// <summary>"Asking ~$4,000 / fight (non-exclusive deal; market rate $5,000)".</summary>
    public static string Line(AskingQuote q) =>
        $"Asking ~{q.Purse} / fight" + Because(q);

    /// <summary>" (reason; market rate $X)" when the quote isn't the open-market rate, else "".</summary>
    public static string Because(AskingQuote q) =>
        q.Reasons.Count == 0 || q.Purse == q.MarketRate ? "" : $" ({q.Why}; market rate {q.MarketRate})";

    /// <summary>
    /// One line on his bond with the player's promotion, or "" when there is
    /// nothing worth saying. Only for the player's OWN fighters: the bond is
    /// read off his deal with you, and a rival's dressing room is not yours to see.
    /// </summary>
    public static string Bond(World w, Fighter f)
    {
        if (w.PlayerPromotion == default || f.Contract?.Promotion != w.PlayerPromotion) return "";
        var b = Lifers.BondOf(w, f);
        string text;
        if (b.IsHolding)
            text = "Lifer: one of yours for life. Rivals get a no, and he asks less to stay.";
        else if (b.IsLifer && b.Grievance is { } g)
            text = $"Lifer, but unhappy: {g.Reason}. "
                 + (b.KeepsFirstCall ? "He still gives you first call. " : "He's taking calls and the hometown rate is off. ")
                 + (b.Repair is { } fix ? $"To fix it: {fix}." : "");
        else if (b.Score >= Lifers.Threshold - 15 && b.Repair is { } nearFix)
            text = b.Grievance is { } ng
                ? $"Nearly a lifer, but {ng.Reason}. To fix it: {nearFix}."
                : $"Nearly a lifer: {nearFix}.";
        else
            return "";
        return Lifers.Gendered(f, text.TrimEnd());
    }
}
