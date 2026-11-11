using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The headlines the sport already ran before the player showed up.
///
/// A new game used to open on an EMPTY news feed — a world with decades of
/// back-dated fights, live feuds and reigning champions, and nothing to read
/// about any of it. Everything here is DERIVED from what the build actually
/// produced (real title reigns off the lineages, real streaks off the records,
/// the seeded rivalries, the genuinely unsigned): no invented facts, just the
/// sport reporting on itself for the eighteen months before day one.
///
/// Items are appended oldest-first, because the feed renders the list reversed.
/// </summary>
public static class Prologue
{
    /// <summary>Write the backlog. Call last, once every fighter, belt and bond exists.</summary>
    public static void Write(World world, DateOnly startDate)
    {
        var items = new List<(DateOnly Date, NewsCategory Cat, string Head, string Body, PromotionId Promo, FighterId? About)>();

        TitleStories(world, startDate, items);
        LongReigns(world, startDate, items);
        HotStreaks(world, startDate, items);
        Feuds(world, startDate, items);
        OpenMarket(world, startDate, items);
        TurfWars(world, startDate, items);
        TheOldGuard(world, startDate, items);
        items.AddRange(ForwardCalendar.PrologueItems(world, startDate));   // the shows already announced

        foreach (var i in items.OrderBy(i => i.Date).ThenBy(i => i.Head, StringComparer.Ordinal))
            News.Post(world, i.Date, i.Cat, i.Head, i.Body, i.Promo, i.About);
    }

    // --- the belts ----------------------------------------------------------

    /// <summary>
    /// Every belt that changed hands in the last two years, reported on the day
    /// it actually happened — the reign dates come straight off the lineage the
    /// history builder wrote, so the feed and the title history agree.
    /// </summary>
    private static void TitleStories(World world, DateOnly startDate, List<(DateOnly, NewsCategory, string, string, PromotionId, FighterId?)> items)
    {
        var cutoff = startDate.AddYears(-2);
        foreach (var title in world.Titles.Values.OrderByDescending(t => t.Prestige.Value))
        {
            var reign = title.Lineage.LastOrDefault(r => r.LostOn is null);
            if (reign is null || reign.WonOn < cutoff) continue;
            if (world.Get(reign.Champion) is not { } champ) continue;
            var promo = world.Get(title.Promotion);
            if (promo is null) continue;

            var beaten = title.Lineage.LastOrDefault(r => r.LostOn == reign.WonOn);
            var loser = beaten is not null ? world.Get(beaten.Champion) : null;

            items.Add((reign.WonOn, NewsCategory.TitleChange,
                loser is not null
                    ? $"{champ.FullName} takes the {title.Name} from {loser.FullName}"
                    : $"{champ.FullName} claims the {title.Name}",
                loser is not null
                    ? $"{loser.FullName}'s reign ends at {promo.Name}. The {title.WeightClass} division has a new name at the top of it."
                    : $"{promo.Name} crowns a new {title.WeightClass} champion.",
                promo.Id, champ.Id));
        }
    }

    /// <summary>
    /// The handful of reigns that are a FACT about the sport rather than a line
    /// in a database. Deliberately stingy: every belt in the world clearing a
    /// low bar buried the feed under forty identical milestones.
    /// </summary>
    private static void LongReigns(World world, DateOnly startDate, List<(DateOnly, NewsCategory, string, string, PromotionId, FighterId?)> items)
    {
        var notable = new List<(Title Title, TitleReign Reign, int Years, Promotion Promo, Fighter Champ)>();
        foreach (var title in world.Titles.Values)
        {
            var reign = title.Lineage.LastOrDefault(r => r.LostOn is null);
            if (reign is null || reign.Defenses < 4) continue;
            int years = (startDate.DayNumber - reign.WonOn.DayNumber) / 365;
            if (years < 3) continue;
            if (world.Get(reign.Champion) is not { } champ || world.Get(title.Promotion) is not { } promo) continue;
            notable.Add((title, reign, years, promo, champ));
        }

        var lines = new[]
        {
            "The division has run out of obvious answers — which is usually when somebody unobvious turns up.",
            "Long enough that the contenders who were chasing the belt when it changed hands have mostly retired.",
            "Nobody has come close in two years. The promotion would quite like somebody to.",
            "At this point the belt is less a prize than an address.",
        };

        foreach (var (t, r, years, promo, champ) in notable
                     .OrderByDescending(x => x.Promo.Prestige.Value + x.Reign.Defenses * 3)
                     .Take(5))
            items.Add((startDate.AddDays(-14 - t.Id.Value % 90), NewsCategory.Milestone,
                $"{years} years a champion: {champ.FullName} and the {t.Name}",
                $"{r.Defenses} defenses and counting. {lines[t.Id.Value % lines.Length]}",
                promo.Id, champ.Id));
    }

    // --- the people ---------------------------------------------------------

    /// <summary>Who is red hot going into the season — straight off the records.</summary>
    private static void HotStreaks(World world, DateOnly startDate, List<(DateOnly, NewsCategory, string, string, PromotionId, FighterId?)> items)
    {
        var hot = world.Fighters.Values
            .Where(f => f.Status == FighterStatus.Active && !f.LocalTalent && f.Record.CurrentStreak >= 6)
            .OrderByDescending(f => f.Record.CurrentStreak * 4 + f.Intangibles.Popularity.Value)
            .Take(6)
            .ToList();

        foreach (var f in hot)
        {
            var promo = f.Contract is { } c ? world.Get(c.Promotion) : null;
            bool unbeaten = f.Record.Losses == 0;
            items.Add((startDate.AddDays(-20 - f.Id.Value % 120), NewsCategory.Result,
                unbeaten
                    ? $"Still perfect: {f.FullName} moves to {f.Record}"
                    : $"{f.Record.CurrentStreak} straight for {f.FullName}",
                promo is not null
                    ? $"{promo.Abbreviation} has a problem worth having — the hottest name in the "
                      + $"{DivisionLabel(f)} division is running out of people to beat."
                    : $"Unsigned, unbeaten in {f.Record.CurrentStreak}, and taking calls. Somebody's matchmaker is going to look clever.",
                promo?.Id ?? default, f.Id));
        }
    }

    /// <summary>"women's Strawweight" / "Strawweight" — the division as it's billed.</summary>
    private static string DivisionLabel(Fighter f) =>
        f.Sex == Sex.Female ? $"women's {f.WeightClass}" : f.WeightClass.ToString();

    /// <summary>The bad blood that was already burning when the player walked in.</summary>
    private static void Feuds(World world, DateOnly startDate, List<(DateOnly, NewsCategory, string, string, PromotionId, FighterId?)> items)
    {
        // Four ways to say "these two hate each other", so a world with a dozen
        // live feuds doesn't read like one headline stuck on repeat.
        var openers = new[]
        {
            "Still no love lost", "The one nobody has to sell", "Unfinished business", "It has not cooled off",
        };

        foreach (var r in world.Rivalries.Where(r => r.Intensity >= 45))
        {
            if (world.Get(r.A) is not { } a || world.Get(r.B) is not { } b) continue;
            var promo = a.Contract is { } c ? world.Get(c.Promotion) : null;
            items.Add((startDate.AddDays(-8 - (a.Id.Value + b.Id.Value) % 100), NewsCategory.Rivalry,
                $"{openers[(a.Id.Value + b.Id.Value) % openers.Length]}: {a.FullName} and {b.FullName}",
                $"Two names, one {DivisionLabel(a)} division, and a grudge nobody has to sell. "
                + (promo is not null
                    ? $"{promo.Abbreviation}'s matchmaker can book this whenever the mood takes them."
                    : "Whoever can get both signatures on one contract has a main event."),
                promo?.Id ?? default, a.Id));
        }
    }

    /// <summary>Who is genuinely available — the shelf the player is about to shop from.</summary>
    private static void OpenMarket(World world, DateOnly startDate, List<(DateOnly, NewsCategory, string, string, PromotionId, FighterId?)> items)
    {
        var free = world.Fighters.Values
            .Where(f => f.Contract is null && f.Status == FighterStatus.Active && !f.LocalTalent
                        && f.Intangibles.Popularity.Value >= 35)
            .OrderByDescending(f => FighterAbility.CurrentAbility(f) + f.Intangibles.Popularity.Value)
            .Take(5)
            .ToList();

        foreach (var f in free)
            items.Add((startDate.AddDays(-30 - f.Id.Value % 150), NewsCategory.General,
                $"{f.FullName} is a free agent",
                $"{f.Record} and no name on the contract line. "
                + (f.AgeOn(startDate) <= 24
                    ? "Every scout in the sport has an opinion; nobody has a signature yet."
                    : "The phone works both ways, and it has been ringing."),
                default, f.Id));

        // The prospect everyone is whispering about: young, unbeaten, unsigned.
        var whisper = world.Fighters.Values
            .Where(f => f.Contract is null && f.Status == FighterStatus.Active && !f.LocalTalent
                        && f.Record.Losses == 0 && f.Record.Wins >= 3 && f.AgeOn(startDate) <= 22)
            .OrderByDescending(f => f.PotentialAbility.Value)
            .FirstOrDefault();
        if (whisper is not null)
            items.Add((startDate.AddDays(-5), NewsCategory.Debut,
                $"The name the scouts keep saying: {whisper.FullName}",
                $"{whisper.AgeOn(startDate)} years old, {whisper.Record}, and unsigned. "
                + "Every org in the sport has a file; the one that opens it first gets the career.",
                default, whisper.Id));
    }

    // --- the business -------------------------------------------------------

    /// <summary>Two houses, one region, not quite enough oxygen — derived from the map, not asserted.</summary>
    private static void TurfWars(World world, DateOnly startDate, List<(DateOnly, NewsCategory, string, string, PromotionId, FighterId?)> items)
    {
        var live = world.Promotions.Values.Where(p => !p.Defunct).ToList();
        foreach (var a in live)
            foreach (var b in live)
            {
                if (a.Id.Value >= b.Id.Value || a.BaseRegion != b.BaseRegion) continue;
                if (Math.Abs(a.Prestige.Value - b.Prestige.Value) > 14) continue;
                if (a.Prestige.Value < 30 || b.Prestige.Value < 30) continue;

                items.Add((startDate.AddDays(-45 - a.Id.Value * 7), NewsCategory.Finance,
                    $"{a.Abbreviation} and {b.Abbreviation}: the fight outside the cage",
                    $"Two promotions the same size, chasing the same fighters in front of the same crowds. "
                    + "One of them is going to spend money it doesn't have to stay level — that is usually how these end.",
                    a.Id, null));
            }
    }

    /// <summary>The sport remembers: the retired greats, and the rooms they run now.</summary>
    private static void TheOldGuard(World world, DateOnly startDate, List<(DateOnly, NewsCategory, string, string, PromotionId, FighterId?)> items)
    {
        var legends = world.Fighters.Values
            .Where(f => f.Designed && f.Status == FighterStatus.Retired && f.Accolades.Count > 0)
            .OrderByDescending(f => f.Intangibles.Popularity.Value)
            .ToList();
        if (legends.Count == 0) return;

        var classOf = legends.Take(3).Select(f => f.FullName).ToList();
        items.Add((startDate.AddDays(-60), NewsCategory.Award,
            "Hall of Fame: this year's class announced",
            string.Join(", ", classOf) + " go in together — "
            + "three careers that between them explain most of what the sport looks like now.",
            default, legends[0].Id));

        // A master and a protégé, where the bond exists: the sport's continuity,
        // stated once so the player knows the corner matters.
        foreach (var r in world.Relationships.Where(r => r.Kind == RelationshipKind.Mentor).Take(3))
        {
            if (world.Get(r.A) is not { } coach || world.Get(r.B) is not { } pupil) continue;
            var camp = coach.Camp is { } id ? world.Get(id) : null;
            items.Add((startDate.AddDays(-25 - pupil.Id.Value % 60), NewsCategory.Milestone,
                $"{coach.FullName} is in {pupil.FullName}'s corner",
                camp is not null
                    ? $"The {camp.Name} head coach has taken the {pupil.WeightClass}'s development personally. "
                      + "That corner has produced champions before."
                    : "A career's worth of craft, handed over in a training room. It tends to show up on fight night.",
                pupil.Contract is { } c ? c.Promotion : default, pupil.Id));
        }
    }
}
