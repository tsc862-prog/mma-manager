using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation.Origins;

namespace MmaManager.Core.Sim.Living;

/// <summary>A chapter of the story so far, ready to show: the narration that was told, who it was about, and what it opened.</summary>
public sealed record ChapterView(
    string Key, int Era, string EraName, string Title, DateOnly Date, ChapterVariant Variant,
    string Headline, string Body, string Digest,
    PromotionId? Promotion, string? PromotionName, FighterId? Fighter, string? FighterName,
    IReadOnlyList<string> Unlocked);

/// <summary>A chapter still to come, kept vague: its era, its title, and roughly when ("any time now", "in the next few years").</summary>
public sealed record ComingView(string Key, int Era, string EraName, string Title, string When);

/// <summary>
/// The read model of an Origins world's history, for the Monday Briefing's chapter card and the Story So Far
/// app: the chapters fired so far (oldest first) with the narration chosen when each fired, and a vague
/// look at what is coming (no exact dates: no spoilers). Read-only; empty in today's mode.
/// </summary>
public static class OriginsStory
{
    public static readonly string[] EraNames =
        { "", "The Spectacle", "The Backlash", "Two Worlds", "The Rules", "The Boom", "The Global Game" };

    /// <summary>The chapters that have fired, oldest first.</summary>
    public static IReadOnlyList<ChapterView> SoFar(World world) =>
        world.Era.Chapters.Select(r => View(world, r)).OfType<ChapterView>().ToList();

    /// <summary>The chapters that fired on or after a date (the week's chapter cards for the Briefing).</summary>
    public static IReadOnlyList<ChapterView> Since(World world, DateOnly since) =>
        world.Era.Chapters.Where(r => r.FiredOn >= since).Select(r => View(world, r)).OfType<ChapterView>().ToList();

    /// <summary>The era the sport is in now, by number and name.</summary>
    public static (int Era, string Name) CurrentEra(World world)
    {
        int era = OriginsTimeline.CurrentEra(world);
        return (era, EraNames[era]);
    }

    /// <summary>One fired chapter, told (null for a key this build's table no longer has).</summary>
    public static ChapterView? View(World world, ChapterRecord r)
    {
        var c = OriginsChapters.All.FirstOrDefault(x => x.Key == r.Key);
        if (c is null) return null;
        var n = OriginsTimeline.NarrationOf(c, r.Variant);
        return new ChapterView(
            c.Key, c.Era, EraNames[c.Era], c.Title, r.FiredOn, r.Variant,
            Fill(n.Headline, world, r), Fill(n.Body, world, r), Fill(c.Digest, world, r),
            r.Promotion, r.Promotion is { } p ? world.Get(p)?.Name : null,
            r.Fighter, r.Fighter is { } f ? world.Get(f)?.FullName : null,
            c.Unlocks.Select(Describe).ToList());
    }

    /// <summary>
    /// What is coming, kept vague: the next few chapters whose requirements have happened (or are about to),
    /// in window order, each with a rough "when" and never a date.
    /// </summary>
    public static IReadOnlyList<ComingView> Coming(World world, int max = 4)
    {
        if (world.Mode != WorldMode.Origins) return Array.Empty<ComingView>();
        int month = OriginsTimeline.Month(world, world.CurrentDate);
        return OriginsChapters.All
            .Where(c => !world.Era.HasFired(c.Key) && c.Requires.All(r => world.Era.HasFired(r) || IsNext(world, r)))
            .OrderBy(c => c.EarliestMonth).ThenBy(c => c.LatestMonth).ThenBy(c => c.Key, StringComparer.Ordinal)
            .Take(max)
            .Select(c => new ComingView(c.Key, c.Era, EraNames[c.Era], c.Title,
                OriginsTimeline.IsLive(world, c, world.CurrentDate) ? "any time now"
                : c.EarliestMonth - month <= 24 ? "in the next year or two"
                : "further off"))
            .ToList();
    }

    /// <summary>Has every requirement of this chapter happened (so it is next in line itself)?</summary>
    private static bool IsNext(World world, string key) => OriginsChapters.ByKey(key).Requires.All(world.Era.HasFired);

    /// <summary>A capability in plain words.</summary>
    public static string Describe(string unlock)
    {
        var parts = unlock.Split(':');
        string key = parts.Length > 1 ? parts[1] : "";
        return parts[0] switch
        {
            "Media" => key switch
            {
                "PpvCarrier" => "pay-per-view carriers",
                "SpecialtyCable" => "late-night and specialty cable",
                "CableSports" => "sports cable channels",
                "PrimeBroadcast" => "prime-time network television",
                "InHouseStream" => "a promotion's own streaming channel",
                "MajorStream" => "the streaming giants",
                _ => key,
            },
            "Sponsor" => key switch
            {
                "Local" => "local sponsors",
                "Supplement" => "supplement sponsors",
                "Equipment" => "equipment sponsors",
                "EnergyDrink" => "energy-drink sponsors",
                "Brewery" => "brewery sponsors",
                "National" => "national brands",
                "Global" => "global brands",
                _ => key,
            },
            "WeightClass" => $"the {OriginsReforms.Name(Enum.Parse<WeightClass>(key)).ToLowerInvariant()} division",
            "CountryScene" => $"a scene in {Geography.WorldAtlas.ByCode(key)?.Name ?? key}",
            "Rounds" => "timed rounds",
            "Judges" => "judges' decisions",
            "Gloves" => "gloves",
            "NewPromotions" => "new promotions",
            "Crossovers" => "fighters crossing between promotions",
            "Superfights" => "champion-versus-champion superfights",
            "Acquisitions" => "takeovers",
            "CoPromotion" => "co-promoted cards",
            "WomensDivisions" => "women's divisions",
            _ => unlock,
        };
    }

    /// <summary>Fill a chapter's slots from its record (the house, the man, the place, the partner, the year).</summary>
    public static string Fill(string text, World world, ChapterRecord r)
    {
        var promo = r.Promotion is { } p ? world.Get(p) : null;
        var cityId = r.City ?? promo?.HomeCity;
        var city = cityId is { } cid ? world.Get(cid) : null;
        var country = city is not null ? world.Get(city.Country) : null;
        var fighter = r.Fighter is { } f ? world.Get(f) : null;
        return text
            .Replace("{Org}", promo?.Name ?? "a promotion")
            .Replace("{Founder}", string.IsNullOrWhiteSpace(promo?.OwnerName) ? "The owner" : promo!.OwnerName)
            .Replace("{City}", city?.Name ?? "the city")
            .Replace("{Country}", country?.Name ?? "abroad")
            .Replace("{Fighter}", fighter?.FullName ?? "a fighter")
            .Replace("{Partner}", r.Partner ?? "a backer")
            .Replace("{Year}", r.FiredOn.Year.ToString());
    }

    /// <summary>
    /// The chapters whose effects wait for systems a later phase builds: they fire and unlock on schedule,
    /// and the gap is named here so the UI and the next phases can say so honestly.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Gaps { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["crucible-first-night"] = "Tape sales are the existing merchandise line; no pay-per-view night is sold yet (era media ladder).",
        ["carriers-drop"] = "No house holds a pay-per-view carrier deal before the era media ladder; the drop is recorded (OriginsBacklash.CarriersDark) for it.",
        ["style-upset"] = "Valuation does not price a second art: asks and AI signings read perceived ability only (cross-training itself is phase 3, built).",
        ["second-centre-arenas"] = "Built: a fight-mad market (interest +15 for good), houses that keep their own rules and a ring. Not built: its stars known abroad. Origins crowds rarely reach 15,000, so it mostly fires at its deadline.",
        ["japan-network-weekly"] = "Foreign network television (phase 4); fires at its deadline.",
        ["first-supercard"] = "Co-promoted cards (phase 4); fires at its deadline and unlocks.",
        ["champion-vs-champion"] = "Superfights between promotions (phase 4); fires at its deadline and unlocks today's superfight system.",
        ["unified-rules"] = "Built: one code binds licensed houses, the rest may adopt it, rankings across promotions are published. Not built: title shots don't follow those rankings (each house picks challengers from its own ranking, as before).",
        ["womens-first-bout"] = "Women's MMA (phase 5); fires at its deadline.",
        ["womens-resistance"] = "Women's MMA (phase 5); fires at its deadline.",
        ["breakout-tv-moment"] = "The mainstream surge in interest and media money (phase 4); fires when two famous men meet, or at its deadline.",
        ["first-acquisition"] = "Takeovers between Origins houses (phase 4); fires at its deadline and unlocks today's acquisitions.",
        ["womens-first-division"] = "Women's MMA (phase 5); fires at its deadline.",
        ["womens-first-star"] = "Women's MMA (phase 5); fires at its deadline.",
        ["consolidation-wave"] = "AI takeovers at scale (phase 4); fires at its deadline.",
        ["womens-headline-card"] = "Women's MMA (phase 5); fires at its deadline.",
        ["global-pyramid-forms"] = "The ladder of promotions as today's mode runs it (phase 6).",
        ["the-sport-you-built"] = "The closing retrospective and convergence on today's mode (phase 6).",
    };

    /// <summary>
    /// What a chapter in <see cref="Gaps"/> doesn't do yet, in the player's words for the Story screen (null
    /// for a chapter that does everything its story says). Honest about what is missing, silent about why.
    /// </summary>
    public static string? PlayerNote(string chapterKey) => PlayerNotes.GetValueOrDefault(chapterKey);

    /// <summary>The player-facing wording of every gap (same keys as <see cref="Gaps"/>).</summary>
    public static IReadOnlyDictionary<string, string> PlayerNotes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["crucible-first-night"] = "The tapes of the night sell as merchandise, but no house can sell a night on pay-per-view yet.",
        ["carriers-drop"] = "The carriers going dark is on the record, but no house holds a pay-per-view deal yet, so nobody loses one.",
        ["style-upset"] = "The upset happens, but fans and promoters don't yet pay more for a fighter who knows a second art.",
        ["second-centre-arenas"] = "The new centre's crowds, its own rules and its ring are real; its stars don't yet become famous abroad.",
        ["japan-network-weekly"] = "No house can sign a foreign network deal yet. It happens when its window closes, whatever the houses do.",
        ["first-supercard"] = "No two houses can co-promote a card yet. It happens when its window closes, whatever the houses do.",
        ["champion-vs-champion"] = "No house can make a superfight against another promotion's champion yet. It happens when its window closes, and from then on superfights between promotions' champions can happen.",
        ["unified-rules"] = "The sport's rankings are published now, but a title shot still goes by each promotion's own ranking, not the sport's.",
        ["womens-first-bout"] = "Women's MMA is told in the story but not yet played: there are no women to sign. It happens when its window closes.",
        ["womens-resistance"] = "Women's MMA is told in the story but not yet played: there are no women to sign. It happens when its window closes.",
        ["breakout-tv-moment"] = "The flood of new fans and media money it describes doesn't follow yet. It happens when two famous fighters meet, or when its window closes.",
        ["first-acquisition"] = "One house can't buy another yet. It happens when its window closes, and from then on promotions can be bought and sold.",
        ["womens-first-division"] = "Women's MMA is told in the story but not yet played: there are no women to sign. It happens when its window closes.",
        ["womens-first-star"] = "Women's MMA is told in the story but not yet played: there are no women to sign. It happens when its window closes.",
        ["consolidation-wave"] = "The wave of takeovers it describes doesn't happen yet. It happens when its window closes, whatever the houses do.",
        ["womens-headline-card"] = "Women's MMA is told in the story but not yet played: there are no women to sign. It happens when its window closes.",
        ["global-pyramid-forms"] = "The ladder of promotions, from club shows to the global stage, doesn't take shape yet.",
        ["the-sport-you-built"] = "The look back over the whole story, and the world settling into today's sport, don't follow yet.",
    };
}
