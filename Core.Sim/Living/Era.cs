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
        world.Mode == WorldMode.Modern || world.Era.IsUnlocked(capability)
        || OriginsTimeline.Contested(world, capability);

    /// <summary>
    /// Is this media rung on the air yet? (Also while the chapter that opens it is up for grabs: the first
    /// house to sign one IS the chapter, <see cref="OriginsTimeline.Contested"/>.)
    /// </summary>
    public static bool AllowsMedia(World world, MediaKind kind) =>
        world.Mode == WorldMode.Modern || world.Era.IsUnlocked(Capability.Media(kind))
        || OriginsTimeline.Contested(world, Capability.Media(kind));

    /// <summary>
    /// Can a promotion run a division for this weight class and sex? The class
    /// must have been split out, and a women's division needs women's MMA too.
    /// </summary>
    public static bool AllowsDivision(World world, WeightClass wc, Sex sex) =>
        world.Mode == WorldMode.Modern
        || (world.Era.IsUnlocked(Capability.Class(wc))
            && (sex == Sex.Male || world.Era.IsUnlocked(Capability.WomensDivisions)));

    /// <summary>
    /// Does any house fight at this class: the sport has the division (<see cref="AllowsDivision"/>), or a
    /// live (not defunct) promotion runs a division filed under it, a drawn line counting before the first
    /// champion in that class unlocks it for the sport. A band filed under its top class counts for that
    /// class only; an open division counts for none. Always true in today's mode. Read-only, draw-free.
    /// </summary>
    public static bool ClassFought(World world, WeightClass wc, Sex sex) =>
        AllowsDivision(world, wc, sex)
        || world.Promotions.Values.Any(p => !p.Defunct
            && p.Divisions.Any(d => !d.Open && d.Sex == sex && d.WeightClass == wc));

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

    /// <summary>
    /// Can a card be staged in this city's country (always, in today's mode)? Yes once its scene is awake,
    /// and also while the chapter that wakes it is up for grabs: the first house to take a night there IS
    /// that chapter (<see cref="OriginsTimeline.ContestedScene"/>). Talent waits for the scene itself
    /// (<see cref="Scenes"/> reads only what is unlocked).
    /// </summary>
    public static bool AllowsSceneIn(World world, CityId city) =>
        world.Mode == WorldMode.Modern
        || (world.Get(city) is { } c && world.Get(c.Country) is { } country
            && (world.Era.IsUnlocked(Capability.Scene(country.Code)) || OriginsTimeline.ContestedScene(world, country.Code)));

    /// <summary>
    /// Has the Backlash closed this city's halls to the sport on this date (a live <see cref="SceneBan"/> over
    /// its country, or over its area of the country)? Never, in today's mode.
    /// </summary>
    public static bool Banned(World world, CityId cityId, DateOnly date)
    {
        if (world.Mode == WorldMode.Modern || world.Era.Bans.Count == 0) return false;
        if (world.Get(cityId) is not { } city || world.Get(city.Country) is not { } country) return false;
        // A commission's licence beats any ban over its region (a licensed region is never closed again).
        foreach (var s in world.Era.Sanctions)
            if (s.Country == country.Code && (s.Area is null || s.Area == city.Area) && s.On <= date) return false;
        foreach (var ban in world.Era.Bans)
            if (ban.Country == country.Code && (ban.Area is null || ban.Area == city.Area) && ban.ActiveOn(date))
                return true;
        return false;
    }

    /// <summary>Unlock a capability; false when it already was.</summary>
    public static bool Unlock(World world, Capability capability) => world.Era.Unlocked.Add(capability.Id);

    /// <summary>
    /// Record a chapter of the sport's story as fired (once per key; false when
    /// it already had). The record is the durable signal — never count headlines.
    /// </summary>
    public static bool Fire(World world, string chapterKey, DateOnly on,
        PromotionId? by = null, FighterId? fighter = null, string? note = null,
        ChapterVariant variant = ChapterVariant.World, CityId? city = null, string? partner = null)
    {
        if (world.Era.HasFired(chapterKey)) return false;
        world.Era.Chapters.Add(new ChapterRecord
        {
            Key = chapterKey, FiredOn = on, Promotion = by, Fighter = fighter, Note = note,
            Variant = variant, City = city, Partner = partner,
        });
        return true;
    }
}
