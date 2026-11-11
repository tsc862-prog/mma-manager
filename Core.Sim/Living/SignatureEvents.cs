using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The calendar's traditions: each designed org has ONE flagship show a year in
/// a signature month, named after its identity (the lore made mechanical — the
/// ICF really does run its dome show every New Year). The world tick stages it
/// at the org's biggest allowed tier, and the date becomes something the whole
/// sport anticipates.
/// </summary>
public static class SignatureEvents
{
    private static readonly Dictionary<string, (int Month, string Name)> Signatures = new()
    {
        ["AFC"] = (7, "AFC International"),          // the summer spectacular
        ["VMMA"] = (11, "VMMA Crown of Europe"),
        ["ICF"] = (1, "ICF Emperor's New Year"),     // the dome show from the lore
        ["IFA"] = (5, "IFA Iron Gala"),
        ["GFC"] = (3, "GFC Desert Clash"),
        ["UFL"] = (7, "UFL Independence Brawl"),
        ["RCS"] = (2, "RCS Carnaval de Combate"),
        ["CK"] = (9, "CK Reinado"),
        ["SCM"] = (12, "SCM Southern Summer"),
        ["MFC"] = (11, "MFC Temple Fair Night"),     // the temple-fair circuit's night in the big house on the river
        ["CPF"] = (12, "CPF Mountain Night"),        // the township heavyweights at the foot of Table Mountain
    };

    /// <summary>The signature name if this org's flagship month is NOW (else null).</summary>
    public static string? For(Promotion promo, DateOnly date) =>
        Signatures.TryGetValue(promo.Abbreviation, out var sig) && sig.Month == date.Month ? sig.Name : null;
}
