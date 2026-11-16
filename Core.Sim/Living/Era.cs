using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The one question every era-gated system asks: has the sport got this yet?
/// In a <see cref="WorldMode.Modern"/> world the answer is always yes, decided
/// before anything else is looked at — so the gates threaded through media,
/// sponsors, divisions, startups, crossovers, superfights and acquisitions
/// never touch an RNG stream and today's mode plays exactly as it did. In an
/// <see cref="WorldMode.Origins"/> world it is yes only once a chapter has
/// unlocked the capability (<see cref="Unlock"/>).
///
/// Gate rule for callers: ask BEFORE the system's first draw, so a blocked
/// system draws nothing; in Modern the ask is free and the draws are untouched.
/// </summary>
public static class Era
{
    public static bool Allows(World world, Capability capability) =>
        world.Mode == WorldMode.Modern || world.Era.IsUnlocked(capability);

    /// <summary>Is this media rung on the air yet?</summary>
    public static bool AllowsMedia(World world, MediaKind kind) =>
        world.Mode == WorldMode.Modern || world.Era.IsUnlocked(Capability.Media(kind));

    /// <summary>
    /// Can a promotion run a division for this weight class and sex? The class
    /// must have been split out, and a women's division needs women's MMA too.
    /// </summary>
    public static bool AllowsDivision(World world, WeightClass wc, Sex sex) =>
        world.Mode == WorldMode.Modern
        || (world.Era.IsUnlocked(Capability.Class(wc))
            && (sex == Sex.Male || world.Era.IsUnlocked(Capability.WomensDivisions)));

    /// <summary>Does this country have a live MMA scene (keyed by atlas code)?</summary>
    public static bool AllowsScene(World world, string countryCode) =>
        world.Mode == WorldMode.Modern || world.Era.IsUnlocked(Capability.Scene(countryCode));

    /// <summary>
    /// Read one of the chapter table's unlock strings (<c>Generation.Origins.OriginsChapters</c>:
    /// "Media:PpvCarrier", "Sponsor:EnergyDrink", "WeightClass:Lightweight", "CountryScene:BRA",
    /// "Rounds") as the <see cref="Capability"/> the gates read. The table's vocabulary prefixes the
    /// media and sponsor families ("Media:", "Sponsor:") where the enum spells each one out
    /// (<see cref="CapabilityKind.PpvCarrier"/>, <see cref="CapabilityKind.SponsorEnergyDrink"/>); the
    /// keyed kinds and the plain kinds read as they are. False for anything else.
    /// </summary>
    public static bool TryFromChapterUnlock(string unlock, out Capability capability)
    {
        capability = default;
        int colon = unlock.IndexOf(':');
        if (colon > 0)
        {
            string family = unlock[..colon], key = unlock[(colon + 1)..];
            if (key.Length == 0) return false;
            switch (family)
            {
                case "Media":
                    if (!Enum.TryParse<MediaKind>(key, ignoreCase: false, out var media) || !Enum.IsDefined(media)
                        || media.ToString() != key) return false;
                    capability = Capability.Media(media);
                    return true;
                case "Sponsor":
                    return Capability.TryParse("Sponsor" + key, out capability);
                case "CountryScene":
                    if (Geography.WorldAtlas.ByCode(key.ToUpperInvariant()) is null) return false;
                    return Capability.TryParse(unlock, out capability);
            }
        }
        return Capability.TryParse(unlock, out capability);
    }

    /// <summary>The country codes with a live scene in this world, in id order (every atlas country in today's mode).</summary>
    public static IEnumerable<string> Scenes(World world) =>
        world.Mode == WorldMode.Modern
            ? world.Countries.Values.Select(c => c.Code)
            : world.Era.Unlocked
                .Where(id => id.StartsWith(nameof(CapabilityKind.CountryScene) + ":", StringComparison.Ordinal))
                .Select(id => id[(nameof(CapabilityKind.CountryScene).Length + 1)..]);

    /// <summary>Does this city's country have a live scene (always, in today's mode)?</summary>
    public static bool AllowsSceneIn(World world, CityId city) =>
        world.Mode == WorldMode.Modern
        || (world.Get(city) is { } c && world.Get(c.Country) is { } country && world.Era.IsUnlocked(Capability.Scene(country.Code)));

    /// <summary>Unlock a capability; false when it already was.</summary>
    public static bool Unlock(World world, Capability capability) => world.Era.Unlocked.Add(capability.Id);

    /// <summary>
    /// Record a chapter of the sport's story as fired (once per key; false when
    /// it already had). The record is the durable signal — never count headlines.
    /// </summary>
    public static bool Fire(World world, string chapterKey, DateOnly on,
        PromotionId? by = null, FighterId? fighter = null, string? note = null)
    {
        if (world.Era.HasFired(chapterKey)) return false;
        world.Era.Chapters.Add(new ChapterRecord
        {
            Key = chapterKey, FiredOn = on, Promotion = by, Fighter = fighter, Note = note,
        });
        return true;
    }
}
