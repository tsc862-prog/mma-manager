using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation.Origins;

namespace MmaManager.Core.Sim.Engine;

/// <summary>
/// The bridge from the founding promotions' authored house rules (<see cref="OriginsRuleSet"/>) to
/// the rules the engine obeys (<see cref="FightRules"/>).
///
/// Supported: rounds and round length (a single 20- or 30-minute round), judges on/off (off = a draw
/// at the limit), gloves (bare / optional / thin open-fingered), open weight (the size term), rope
/// escapes, and three of the "banned" phrases: elbows, closed-fist strikes to a grounded man's head,
/// kicks to a grounded man (carried; the engine has no ground kicks to remove).
///
/// Not modelled (no change in the cage): <see cref="OriginsRuleSet.TimeLimitMinutes"/> beyond
/// rounds x minutes (they agree for all three), fouls (biting, gouging, groin, headbutts, the back of
/// the head), "main events carry more rounds" (a booking choice), rope-escape points as a way to win
/// outright (escapes past the allowance cost a point on the cards instead), and
/// <see cref="OriginsRuleSet.TournamentSize"/> (the booker's: <c>TournamentService.RunOneNight</c>).
/// </summary>
public static class OriginsRules
{
    public static FightRules ToFightRules(this OriginsRuleSet r)
    {
        bool Bans(params string[] words) =>
            r.Banned.Any(b => words.All(w => b.Contains(w, StringComparison.OrdinalIgnoreCase)));

        return new FightRules
        {
            Rounds = Math.Max(1, r.Rounds),
            RoundMinutes = Math.Max(1, r.RoundMinutes),
            Judges = r.Judges && !r.DrawAtLimit,
            Gloves = r.Gloves switch
            {
                GloveRule.Bare => GloveType.Bare,
                GloveRule.Optional => GloveType.Optional,
                _ => GloveType.ThinOpenFinger,
            },
            Elbows = !Bans("elbow"),
            GroundedHeadPunches = !Bans("closed-fist", "grounded"),
            GroundedKicks = !Bans("kick", "grounded"),
            RopeEscapes = Math.Max(0, r.RopeEscapesAllowed),
            OpenWeight = r.OpenWeight,
            // The rule set alone knows a ring only by its ropes; the promotion overload reads the venue too.
            Enclosure = r.RopeEscapesAllowed > 0 || Says(r.Format, "ring") ? Enclosure.Ring : Enclosure.Cage,
        };
    }

    /// <summary>
    /// A founding promotion's rules, with the enclosure read from its authored format and home venue:
    /// a roped ring (format or rope escapes), a fence (the venue lore says so: a cage), else an open
    /// floor (the Duelo's parking garage).
    /// </summary>
    public static FightRules ToFightRules(this OriginsPromotionSpec spec)
    {
        var rules = spec.Rules.ToFightRules();
        var room = rules.Enclosure == Enclosure.Ring ? Enclosure.Ring
                 : Says(spec.HomeVenueLore, "fence") || Says(spec.HomeVenueLore, "cage") ? Enclosure.Cage
                 : Enclosure.OpenFloor;
        return rules with { Enclosure = room };
    }

    private static bool Says(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
