using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// Somebody, or something, that made a chapter happen: the house, the man, the place and the partner the
/// story names. <see cref="Nobody"/> is "it just happened".
/// </summary>
public sealed record ChapterDoer(
    PromotionId? Promotion = null, FighterId? Fighter = null, CityId? City = null, string? Partner = null, string? Note = null)
{
    public static readonly ChapterDoer Nobody = new();
}

/// <summary>
/// The Origins timeline engine: the authored chapter table (<see cref="OriginsChapters"/>) fired over the
/// years of an Origins world, run from the world tick (<see cref="RunWeekly"/>; never in today's mode).
///
/// Shaped history, as the owner decided: the ORDER is mostly fixed (a chapter fires only after every
/// chapter it requires has fired, on an earlier week), the TIMING is a window (a chapter is live from its
/// earliest month once its requirements are met, and fires by its latest month whatever happens), and WHO
/// does it depends on the world. A "first promotion to..." chapter fires for whichever house gets there
/// first, the player's or an AI house; a world condition fires when the world reaches it; a dated chapter
/// lands a keyed number of months after what it follows. At the deadline, a chapter nobody made happen
/// "just happens" (its world narration). Every firing is an <see cref="Era.Fire"/> record carrying the
/// narration variant and who and where it was about, so the story reads the same whenever it is told
/// (<see cref="OriginsStory"/>).
///
/// While a contestable chapter is live, the capability it names is open to whoever reaches for it first
/// (<see cref="Contested"/>, <see cref="ContestedScene"/>): the first house to sign a late-night cable deal,
/// or to take a night to Amsterdam, IS the chapter. Nothing else is unlocked early.
///
/// Draw-free on every shared stream: timings and choices are keyed hash rolls off the seed, the chapter key
/// and the date, and every ordering ends on an id.
/// </summary>
public static class OriginsTimeline
{
    private const ulong DueSalt = 0x0219_7111_D0E5UL;

    /// <summary>The table in the order the engine walks it: by window, then authoring order.</summary>
    private static readonly OriginsChapter[] Walk = OriginsChapters.All
        .Select((c, i) => (c, i)).OrderBy(x => x.c.EarliestMonth).ThenBy(x => x.i).Select(x => x.c).ToArray();

    /// <summary>The capability a "first to sign/sponsor" chapter puts up for grabs (by capability id).</summary>
    private static readonly Dictionary<string, OriginsChapter> ContestedBy = BuildContest();

    /// <summary>The chapter that wakes each country's scene (by country code), for the nights that can wake it.</summary>
    private static readonly Dictionary<string, OriginsChapter> SceneChapterOf = OriginsChapters.All
        .Where(c => c.Structured.Condition == "HoldEventInNewCountry")
        .SelectMany(c => c.Unlocks.Where(u => u.StartsWith("CountryScene:", StringComparison.Ordinal))
            .Select(u => (Code: u["CountryScene:".Length..].ToUpperInvariant(), Chapter: c)))
        .ToDictionary(x => x.Code, x => x.Chapter, StringComparer.Ordinal);

    private static Dictionary<string, OriginsChapter> BuildContest()
    {
        var map = new Dictionary<string, OriginsChapter>(StringComparer.Ordinal);
        foreach (var c in OriginsChapters.All.Where(c => c.Structured.Kind == OriginsTriggerKind.FirstPromotionTo))
        {
            var parts = c.Structured.Condition.Split(':');
            if (parts.Length != 2) continue;
            string? unlock = parts[0] switch
            {
                "SignMedia" => "Media:" + parts[1],
                "SignSponsor" => "Sponsor:" + parts[1],
                _ => null,
            };
            if (unlock is null || !c.Unlocks.Contains(unlock) || !Era.TryFromChapterUnlock(unlock, out var cap)) continue;
            map[cap.Id] = c;
        }
        return map;
    }

    // ------------------------------------------------------------------ reading the clock

    /// <summary>Months since the world began (the chapter table's own clock).</summary>
    public static int Month(World world, DateOnly date) => WorldEvents.Epoch(world, date);

    /// <summary>
    /// Is this chapter up for grabs on this date: not yet fired, its window open, everything it needs done
    /// (on an earlier day: a chapter never follows another on the day it was told)?
    /// </summary>
    public static bool IsLive(World world, OriginsChapter c, DateOnly date) =>
        world.Mode == WorldMode.Origins && !world.Era.HasFired(c.Key) && Month(world, date) >= c.EarliestMonth
        && c.Requires.All(r => world.Era.Chapter(r) is { } rec && rec.FiredOn < date);

    /// <summary>Has this chapter fired, or is it live (the moment it describes can happen now)?</summary>
    public static bool IsOpen(World world, string chapterKey, DateOnly date) =>
        world.Era.HasFired(chapterKey) || IsLive(world, OriginsChapters.ByKey(chapterKey), date);

    /// <summary>
    /// A capability the first house to use it will win for the sport (a media rung or sponsor type whose
    /// chapter is live). Always false in today's mode, and for anything a chapter does not put up for grabs.
    /// </summary>
    public static bool Contested(World world, Capability capability) =>
        world.Mode == WorldMode.Origins && ContestedBy.TryGetValue(capability.Id, out var c)
        && IsLive(world, c, world.CurrentDate);

    /// <summary>A country whose scene the first house to take a night there will wake (its chapter is live).</summary>
    public static bool ContestedScene(World world, string countryCode) => ContestedScene(world, countryCode, world.CurrentDate);

    /// <summary>As above, on a given date.</summary>
    public static bool ContestedScene(World world, string countryCode, DateOnly date) =>
        world.Mode == WorldMode.Origins && SceneChapterOf.TryGetValue(countryCode.ToUpperInvariant(), out var c)
        && IsLive(world, c, date);

    /// <summary>The countries whose scenes are up for grabs today (code order).</summary>
    public static IReadOnlyList<string> ContestedScenes(World world) =>
        world.Mode != WorldMode.Origins ? Array.Empty<string>()
        : SceneChapterOf.Keys.Where(code => !world.Era.IsUnlocked(Capability.Scene(code)) && ContestedScene(world, code))
            .OrderBy(code => code, StringComparer.Ordinal).ToList();

    /// <summary>The era the sport is in: the latest era any fired chapter belongs to (1 before anything fires).</summary>
    public static int CurrentEra(World world)
    {
        int era = 1;
        foreach (var r in world.Era.Chapters)
            if (OriginsChapters.All.FirstOrDefault(c => c.Key == r.Key) is { } c) era = Math.Max(era, c.Era);
        return era;
    }

    /// <summary>
    /// The month a chapter is due. A dated chapter lands a keyed two to twelve months after the last thing
    /// it follows (never before its window opens, never after it closes); every other chapter's deadline is
    /// its window's end.
    /// </summary>
    public static int DueMonth(World world, OriginsChapter c)
    {
        if (c.Structured.Kind != OriginsTriggerKind.Dated) return c.LatestMonth;
        int due = c.EarliestMonth;
        int gap = 2 + (int)(SimRandom.Mix(world.RngSeed, KeyHash(c.Key) ^ DueSalt) % 11);
        foreach (var r in c.Requires)
            if (world.Era.Chapter(r) is { } rec) due = Math.Max(due, Month(world, rec.FiredOn) + gap);
        return Math.Clamp(due, c.EarliestMonth, c.LatestMonth);
    }

    /// <summary>A stable 64-bit hash of a key (FNV-1a; string.GetHashCode is salted per process).</summary>
    internal static ulong KeyHash(string key)
    {
        ulong h = 14695981039346656037UL;
        foreach (char ch in key) { h ^= ch; h *= 1099511628211UL; }
        return h;
    }

    /// <summary>A keyed roll in [0, 1): the seed, then any number of stable parts.</summary>
    internal static double Roll(World world, params ulong[] parts)
    {
        ulong h = world.RngSeed;
        foreach (var p in parts) h = SimRandom.Mix(h, p);
        return (h >> 11) * (1.0 / (1UL << 53));
    }

    // ------------------------------------------------------------------ the weekly pass

    /// <summary>
    /// The week's turn of the story (Origins only; today's mode returns at once). On the first week of a
    /// month the world's Origins machinery moves first (the Backlash's bans and licences, rule reforms,
    /// new houses, scheduled arrivals); then every live chapter is checked in window order and fired when
    /// somebody did it, or when its time is up.
    /// </summary>
    public static void RunWeekly(World world, DateOnly date)
    {
        if (world.Mode != WorldMode.Origins) return;
        if (date.AddDays(-7).Month != date.Month)
        {
            OriginsBacklash.RunMonthly(world, date);
            OriginsReforms.RunMonthly(world, date);
            OriginsFounding.RunMonthly(world, date);
            OriginsFounding.RunArrivals(world, date);
            OriginsScenes.RunMonthly(world, date);   // gyms open in the scenes that have woken
            OriginsCamps.RunMonthly(world, date);    // rooms earn names and learn the whole game
        }
        // A licensed house the code binds takes it the week it is clear of any bracket on its books.
        if (world.Era.HasFired(OriginsReforms.Unified)) OriginsReforms.UnifiedRulesBegin(world, date);

        int month = Month(world, date);
        foreach (var c in Walk)
        {
            if (!IsLive(world, c, date)) continue;
            var doer = Detect(world, c, date);
            if (doer is null && month >= DueMonth(world, c)) doer = AtDeadline(world, c, date);
            if (doer is not null) Fire(world, c, doer, date);
        }
    }

    /// <summary>The date a live chapter's window opened (its earliest month, or the day its last requirement fired).</summary>
    public static DateOnly WindowOpened(World world, OriginsChapter c)
    {
        var anchor = world.StartDate == default ? new DateOnly(2026, 1, 1) : world.StartDate;
        var open = anchor.AddMonths(c.EarliestMonth);
        foreach (var r in c.Requires)
            if (world.Era.Chapter(r) is { } rec && rec.FiredOn > open) open = rec.FiredOn;
        return open;
    }

    // ------------------------------------------------------------------ who did it

    /// <summary>Did somebody make this chapter happen? Null when nobody has (yet).</summary>
    internal static ChapterDoer? Detect(World world, OriginsChapter c, DateOnly date)
    {
        switch (c.Key)
        {
            case "crucible-first-night":
            {
                // The Crucible's first night on the carrier.
                var cru = world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == "CRU");
                var night = cru is null ? null : FirstNight(world, cru.Id);
                return night is null ? null : new ChapterDoer(cru!.Id, City: CityOf(world, night));
            }
            case "shinken-and-duelo-open":
            {
                // Both rooms have opened their doors.
                var sfa = world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == "SFA");
                var dlb = world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == "DLB");
                return sfa is not null && dlb is not null && FirstNight(world, sfa.Id) is not null && FirstNight(world, dlb.Id) is not null
                    ? ChapterDoer.Nobody : null;
            }
        }

        var parts = c.Structured.Condition.Split(':');
        string cond = parts[0];
        string? arg = parts.Length > 1 ? parts[1] : null;
        int value = c.Structured.Value;
        var since = WindowOpened(world, c);
        switch (cond)
        {
            case "SignSponsor":
            {
                if (!Era.TryFromChapterUnlock("Sponsor:" + arg, out var cap)) return null;
                (DateOnly On, Promotion P, string Partner)? best = null;
                foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
                    foreach (var d in p.SponsorDeals)
                        if (Finance.Sponsorships.ArchetypeOf(d.Partner) == cap && (best is null || d.SignedOn < best.Value.On))
                            best = (d.SignedOn, p, d.Partner);
                return best is { } b ? new ChapterDoer(b.P.Id, City: b.P.HomeCity, Partner: b.Partner) : null;
            }
            case "SignMedia":
            {
                if (!Enum.TryParse<MediaKind>(arg, out var kind)) return null;   // a foreign network: no such screen yet
                (DateOnly On, Promotion P, string Partner)? best = null;
                foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
                {
                    if (kind == MediaKind.InHouseStream && p.Stream is { } s && (best is null || s.LaunchedOn < best.Value.On))
                        best = (s.LaunchedOn, p, s.Name);
                    foreach (var d in p.MediaDeals)
                        if (d.Kind == kind && (best is null || d.SignedOn < best.Value.On)) best = (d.SignedOn, p, d.Partner);
                }
                return best is { } b ? new ChapterDoer(b.P.Id, City: b.P.HomeCity, Partner: b.Partner) : null;
            }
            case "HoldEventInNewCountry":
            {
                var countries = c.Unlocks.Where(u => u.StartsWith("CountryScene:", StringComparison.Ordinal))
                    .Select(u => u["CountryScene:".Length..].ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
                var ev = CompletedSince(world, since).FirstOrDefault(e =>
                    CityOf(world, e) is { } city && world.Get(city) is { } ct && world.Get(ct.Country) is { } k && countries.Contains(k.Code));
                return ev is null ? null : new ChapterDoer(ev.Promotion, City: CityOf(world, ev));
            }
            case "CrownChampionInClass":
            {
                // The first champion crowned in any class the chapter draws (a house draws its lines together).
                var classes = c.Unlocks.Where(u => u.StartsWith("WeightClass:", StringComparison.Ordinal))
                    .Select(u => Enum.Parse<WeightClass>(u["WeightClass:".Length..])).ToHashSet();
                (DateOnly On, Promotion P, TitleReign R, Title T)? best = null;
                foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
                    foreach (var d in p.Divisions)
                    {
                        if (d.Open || !classes.Contains(d.WeightClass) || d.Title is not { } tid || world.Get(tid) is not { } t) continue;
                        if (d.Sex == Sex.Female) continue;   // women's MMA is a later phase
                        if (t.Lineage.Count > 0 && (best is null || t.Lineage[0].WonOn < best.Value.On))
                            best = (t.Lineage[0].WonOn, p, t.Lineage[0], t);
                    }
                if (best is not { } bb) return null;
                var night = world.Events.Values.Where(e => e.Status == EventStatus.Completed && e.Date == bb.On && e.Promotion == bb.P.Id)
                    .OrderBy(e => e.Id.Value).FirstOrDefault();
                return new ChapterDoer(bb.P.Id, bb.R.Champion, night is null ? bb.P.HomeCity : CityOf(world, night));
            }
            case "HoldEventAbove":
            {
                // A night that FILLS a room that size: the crowd, not the building. (The arena chapter is the second
                // centre's: its words say outside North America, and that is where the second centre is.)
                var ev = CompletedSince(world, since).FirstOrDefault(e => e.Financials is { } fin && fin.Attendance >= value
                    && (c.Key != OriginsScenes.SecondCentreChapter
                        || (CityOf(world, e) is { } at && world.Get(at) is { } city && city.Region != Region.NorthAmerica)));
                return ev is null ? null : new ChapterDoer(ev.Promotion, City: CityOf(world, ev));
            }
            case "PromotionsAtLeast":
            case "PromotionsOnTheMapAtLeast":
                return CountDoer(world, value);
            case "PromotionReachesTier":
            {
                if (!Enum.TryParse<OrgSize>(arg, out var size)) return null;
                var p = world.Promotions.Values.Where(p => !p.Defunct && Org.OrgSizing.SizeOf(p) >= size)
                    .OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value).FirstOrDefault();
                return p is null ? null : new ChapterDoer(p.Id, City: p.HomeCity);
            }
            case "FighterFameAtLeast":
            {
                var f = world.Fighters.Values.Where(f => f.Status == FighterStatus.Active && f.Sex == Sex.Male
                        && f.Intangibles.Popularity.Value >= value)
                    .OrderByDescending(f => f.Intangibles.Popularity.Value).ThenBy(f => f.Id.Value).FirstOrDefault();
                return f is null ? null : new ChapterDoer(f.Contract?.Promotion, f.Id, f.Contract is { } k && world.Get(k.Promotion) is { } home ? home.HomeCity : f.HomeCity);
            }
            case "UpsetOfStar":
            {
                foreach (var ev in CompletedSince(world, since))
                    foreach (var fight in ev.Card)
                    {
                        if (fight.Outcome?.Winner is not { } w) continue;
                        var winner = world.Get(w);
                        var loser = world.Get(w == fight.RedCorner ? fight.BlueCorner : fight.RedCorner);
                        if (winner is null || loser is null || winner.PrimaryStyle == loser.PrimaryStyle) continue;
                        int lp = loser.Intangibles.Popularity.Value, wp = winner.Intangibles.Popularity.Value;
                        if (lp >= value && wp + 15 <= lp) return new ChapterDoer(ev.Promotion, winner.Id, CityOf(world, ev));
                    }
                return null;
            }
            case "BothFightersFameAtLeast":
            {
                foreach (var ev in CompletedSince(world, since))
                    foreach (var fight in ev.Card)
                        if (world.Get(fight.RedCorner) is { } a && world.Get(fight.BlueCorner) is { } b
                            && a.Intangibles.Popularity.Value >= value && b.Intangibles.Popularity.Value >= value)
                            return new ChapterDoer(ev.Promotion, fight.Outcome?.Winner, CityOf(world, ev));
                return null;
            }
            case "CampReputationAtLeast":
                // A famous room that teaches the whole game (a one-art room's fame is not this chapter), credited to
                // the house with the most of its men.
                return OriginsCamps.FamousRoom(world, value);
            case "AllRoundersBeatSpecialists":
                return AllRoundersWin(world, since, date, value) ? ChapterDoer.Nobody : null;
            case "SanctionedRegionsAtLeast":
                return world.Era.Sanctions.Count >= value ? ChapterDoer.Nobody : null;
            case "ContinentsWithPromotionsAtLeast":
                return world.Promotions.Values.Where(p => !p.Defunct).Select(p => p.BaseRegion).Distinct().Count() >= value
                    ? ChapterDoer.Nobody : null;
            case "ChaptersFired":
                return world.Era.Chapters.Count >= value ? ChapterDoer.Nobody : null;
        }
        // Dated chapters wait for their date; rule adoptions and licences fire where they happen
        // (OriginsReforms, OriginsBacklash); the rest wait for systems later phases build (see OriginsStory.Gaps).
        return null;
    }

    /// <summary>
    /// Who made a "promotions at least <paramref name="value"/>" count: null while fewer houses are alive; else the
    /// newest live house, unless the player's house is one of the houses making the count (its place in id order
    /// is at or past the count's last place: it opened before the chapter could fire, a newcomer may have opened
    /// since), which the story credits. With no player, or a player house among the first three, it is the newest.
    /// The weekly detection and the founding-day credit (<see cref="PlayerFounding"/>) both ask this.
    /// </summary>
    internal static ChapterDoer? CountDoer(World world, int value)
    {
        var alive = world.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value).ToList();
        if (alive.Count < value) return null;
        var doer = alive[^1];
        int mine = world.HasPlayer ? alive.FindIndex(p => p.Id == world.PlayerPromotion) : -1;
        if (mine >= value - 1) doer = alive[mine];
        return new ChapterDoer(doer.Id, City: doer.HomeCity);
    }

    /// <summary>A chapter's time is up and nobody did it: whatever the world does about it, then "it just happened".</summary>
    private static ChapterDoer AtDeadline(World world, OriginsChapter c, DateOnly date)
    {
        switch (c.Key)
        {
            case "rivals-appear":
                // The fourth house opens: the sport is never three rooms for ever.
                if (OriginsFounding.Found(world, date, null) is { } fresh) return new ChapterDoer(fresh.Id, City: fresh.HomeCity);
                break;
            case "first-sanctioned-state":
                if (OriginsBacklash.CommissionMoves(world, date) is { } city) return new ChapterDoer(City: city);
                break;
        }
        return ChapterDoer.Nobody;
    }

    // ------------------------------------------------------------------ firing

    /// <summary>Fire a chapter now if it is live (a rule adoption or a licence that IS the chapter). False when it was not live.</summary>
    internal static bool FireNow(World world, string chapterKey, ChapterDoer doer, DateOnly date)
    {
        var c = OriginsChapters.ByKey(chapterKey);
        if (!IsLive(world, c, date)) return false;
        Fire(world, c, doer, date);
        return true;
    }

    private static void Fire(World world, OriginsChapter c, ChapterDoer doer, DateOnly date)
    {
        var player = world.PlayerPromotion;
        bool hasPlayer = player.Value != 0 && world.Get(player) is not null;
        var variant = doer.Promotion is { } by && hasPlayer && by == player && c.ByPlayer is not null ? ChapterVariant.Player
            : doer.Promotion is { } r && (!hasPlayer || r != player) && c.ByRival is not null ? ChapterVariant.Rival
            : ChapterVariant.World;
        var n = NarrationOf(c, variant);
        string text = n.Headline + " " + n.Body;

        // What the words need, fixed now so the story reads the same whenever it is told.
        var fighter = doer.Fighter;
        if (fighter is null && text.Contains("{Fighter}")) fighter = FallbackFighter(world, doer.Promotion);
        var partner = doer.Partner;
        if (partner is null && text.Contains("{Partner}")) partner = FallbackPartner(world, c, date);
        var city = doer.City;
        if (city is null && (text.Contains("{City}") || text.Contains("{Country}")) && doer.Promotion is { } hp)
            city = world.Get(hp)?.HomeCity;

        Era.Fire(world, c.Key, date, doer.Promotion, fighter, doer.Note, variant, city, partner);
        foreach (var unlock in c.Unlocks)
        {
            if (!Era.TryFromChapterUnlock(unlock, out var cap))
                throw new InvalidOperationException($"Chapter {c.Key} unlocks '{unlock}', which names no capability.");
            Era.Unlock(world, cap);
        }

        ApplyEffects(world, c, fighter, date);

        var record = world.Era.Chapter(c.Key)!;
        string headline = OriginsStory.Fill(n.Headline, world, record);
        string body = OriginsStory.Fill(n.Body, world, record);
        News.Post(world, date, NewsCategory.General, headline, body, doer.Promotion ?? default, fighter);
        if (variant == ChapterVariant.Player)
            Mail.Post(world, date, MailCategory.Milestone, "The Story So Far", headline,
                body + " The sport will remember who did it first.");
    }

    internal static OriginsNarration NarrationOf(OriginsChapter c, ChapterVariant v) => v switch
    {
        ChapterVariant.Player => c.ByPlayer ?? c.ByWorld,
        ChapterVariant.Rival => c.ByRival ?? c.ByWorld,
        _ => c.ByWorld,
    };

    /// <summary>The effects a chapter has beyond its unlocks (the ones today's systems can carry).</summary>
    private static void ApplyEffects(World world, OriginsChapter c, FighterId? fighter, DateOnly date)
    {
        switch (c.Key)
        {
            case "first-star":
                // The first face on a poster: the camera has found someone to love.
                if (fighter is { } id && world.Get(id) is { } star) star.StarPower = true;
                break;
            case "press-turns":
                OriginsBacklash.PressTurns(world, date);
                break;
            case "bans-spread":
                OriginsBacklash.BanWave(world, date, first: true);
                break;
            case "carriers-drop":
                OriginsBacklash.BanWave(world, date, first: false);
                break;
            case "lean-years":
                OriginsBacklash.LeanYearsBegin(world, date);
                break;
            case OriginsScenes.SecondCentreChapter:
                OriginsScenes.SecondCentreBegins(world, date);
                break;
            case OriginsReforms.Unified:
                OriginsReforms.UnifiedRulesBegin(world, date);
                break;
        }
    }

    /// <summary>The fewest bouts between a specialist and an all-rounder that settle the argument.</summary>
    public const int SpecialistBoutsNeeded = 30;

    /// <summary>The look-back for the specialists' fall: the last two years of bouts (and none before its window opened).</summary>
    public const int SpecialistLookbackMonths = 24;

    /// <summary>
    /// Have the all-rounders beaten the specialists: over the bouts since the window opened (the last two years
    /// at most) between a man who knows more than one art and a man who knows one (<see cref="OriginsTalent.IsAllRounder"/>,
    /// read off their skills today), with a winner, at least <see cref="SpecialistBoutsNeeded"/> of them and the
    /// all-rounder winning at least <paramref name="percent"/>%?
    /// </summary>
    internal static bool AllRoundersWin(World world, DateOnly since, DateOnly date, int percent) =>
        AllRounderRecord(world, since > date.AddMonths(-SpecialistLookbackMonths) ? since : date.AddMonths(-SpecialistLookbackMonths)) is var (won, bouts)
        && bouts >= SpecialistBoutsNeeded && won * 100 >= percent * bouts;

    /// <summary>
    /// All-rounders against specialists since a date: the all-rounders' wins and the bouts with a winner. Read-only.
    /// WORLD-LEVEL DETECTOR: it classifies men by their true skills (<see cref="OriginsTalent.IsAllRounder"/>), so a
    /// screen may show the sport-wide totals but never which man counted as which.
    /// </summary>
    public static (int AllRounderWins, int Bouts) AllRounderRecord(World world, DateOnly since)
    {
        int won = 0, bouts = 0;
        foreach (var ev in CompletedSince(world, since))
            foreach (var fight in ev.Card)
            {
                if (fight.Outcome?.Winner is not { } w) continue;
                if (world.Get(fight.RedCorner) is not { } a || world.Get(fight.BlueCorner) is not { } b) continue;
                bool ra = OriginsTalent.IsAllRounder(a), rb = OriginsTalent.IsAllRounder(b);
                if (ra == rb) continue;
                bouts++;
                if ((ra && w == a.Id) || (rb && w == b.Id)) won++;
            }
        return (won, bouts);
    }

    /// <summary>The best-known man of the house the chapter is about (or of the sport), when its words name one.</summary>
    private static FighterId? FallbackFighter(World world, PromotionId? promo) =>
        world.Fighters.Values
            .Where(f => f.Status == FighterStatus.Active && (promo is null || f.Contract?.Promotion == promo))
            .OrderByDescending(f => f.Intangibles.Popularity.Value).ThenBy(f => f.Id.Value)
            .Select(f => (FighterId?)f.Id).FirstOrDefault();

    /// <summary>An era partner of the right kind for a chapter whose words name one (the authored broadcasters and sponsors).</summary>
    private static string? FallbackPartner(World world, OriginsChapter c, DateOnly date)
    {
        int month = Month(world, date);
        var parts = c.Structured.Condition.Split(':');
        if (parts[0] == "SignMedia" && parts.Length > 1)
        {
            var pool = OriginsBroadcasters.Media.Where(m => m.Media?.ToString() == parts[1]
                || (parts[1] == "ForeignNetwork" && m.Kind == OriginsPartnerKind.ForeignNetwork)).ToList();
            return (pool.Where(m => m.EarliestMonth <= month).OrderByDescending(m => m.EarliestMonth).FirstOrDefault()
                    ?? pool.OrderBy(m => m.EarliestMonth).FirstOrDefault())?.Name;
        }
        if (parts[0] == "SignSponsor" && parts.Length > 1)
        {
            var pool = OriginsBroadcasters.Sponsors.Where(s => s.Type == parts[1]).ToList();
            return (pool.Where(s => s.EarliestMonth <= month).OrderByDescending(s => s.EarliestMonth).FirstOrDefault()
                    ?? pool.OrderBy(s => s.EarliestMonth).FirstOrDefault())?.Name;
        }
        // A media unlock named in a chapter that is not itself a signing (the breakout night, say).
        var media = c.Unlocks.FirstOrDefault(u => u.StartsWith("Media:", StringComparison.Ordinal));
        return media is null ? null
            : OriginsBroadcasters.Media.Where(m => "Media:" + m.Media == media).OrderBy(m => m.EarliestMonth).FirstOrDefault()?.Name;
    }

    // ------------------------------------------------------------------ small readers

    private static Event? FirstNight(World world, PromotionId promo) =>
        world.Events.Values.Where(e => e.Promotion == promo && e.Status == EventStatus.Completed)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).FirstOrDefault();

    /// <summary>Completed nights on or after a date, oldest first (ties on id).</summary>
    private static IEnumerable<Event> CompletedSince(World world, DateOnly since) =>
        world.Events.Values.Where(e => e.Status == EventStatus.Completed && e.Date >= since && e.Promotion.Value != 0)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value);

    internal static CityId? CityOf(World world, Event ev) =>
        ev.VenueId is { } v && world.Get(v) is { } room ? room.City : world.Get(ev.Promotion)?.HomeCity;

    /// <summary>The lean years' grip on a house (its home in a Backlash country, inside the two years after they begin).</summary>
    public static bool LeanFor(World world, Promotion promo, DateOnly date) =>
        world.Mode == WorldMode.Origins && OriginsBacklash.LeanOn(world, date)
        && promo.HomeCity is { } hc && world.Get(hc) is { } city && world.Get(city.Country) is { } k
        && OriginsBacklash.Countries.Contains(k.Code);
}
