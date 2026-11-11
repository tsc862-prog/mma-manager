using MmaManager.Core.Model;
using System.Text.RegularExpressions;
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
        AuthoredStories(world, startDate, items);
        items.AddRange(ForwardCalendar.PrologueItems(world, startDate));   // the shows already announced

        foreach (var i in items.OrderBy(i => i.Date).ThenBy(i => i.Head, StringComparer.Ordinal))
            News.Post(world, i.Date, i.Cat, i.Head, i.Body, i.Promo, i.About);
    }

    /// <summary>
    /// The hand-written lines (<see cref="Generation.DesignedHeadlines"/>): spin and
    /// ambition the sim cannot derive. A line whose subject or house is not in this
    /// build (a world made without the designed roster) is dropped whole.
    /// </summary>
    private static void AuthoredStories(World world, DateOnly startDate, List<(DateOnly, NewsCategory, string, string, PromotionId, FighterId?)> items)
    {
        if (!world.Fighters.Values.Any(f => f.Designed)) return;   // no designed roster, no authored feed
        foreach (var h in Generation.DesignedHeadlines.All)
        {
            FighterId? about = null;
            if (h.About is not null)
            {
                var hit = world.Fighters.Values.Where(f => f.Designed && $"{f.FirstName} {f.LastName}" == h.About).ToList();
                if (hit.Count != 1) continue;
                about = hit[0].Id;
            }
            PromotionId promo = default;
            if (h.Org is not null)
            {
                if (world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == h.Org) is not { } p) continue;
                promo = p.Id;
            }
            items.Add((startDate.AddDays(-h.DaysBeforeStart), h.Cat, h.Headline, h.Body, promo, about));
        }
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
            var division = DivisionLabel(champ.Sex, title.WeightClass);

            // What the books say about this reign, and only that: a predecessor
            // beaten for the belt, a belt left empty for under a year (a longer gap is
            // just where the written history starts, not a vacancy), or - for most
            // current reigns, whose predecessor the history never wrote - the
            // title-winning bout itself (who was across the cage, and how it ended).
            var vacated = beaten is null
                ? title.Lineage.LastOrDefault(r => r.LostOn is { } l && l < reign.WonOn && reign.WonOn.DayNumber - l.DayNumber <= 365)
                : null;
            var vacatedBy = vacated is not null ? world.Get(vacated.Champion) : null;
            var winning = TitleWin(world, title, champ, reign.WonOn);

            string since = reign.Defenses switch
            {
                0 => $"{champ.LastName} has yet to defend it.",
                1 => $"{champ.LastName} has defended it once since.",
                var n => $"{champ.LastName} has defended it {n} times since.",
            };

            string head, body;
            if (loser is not null)
            {
                head = $"{champ.FullName} takes the {title.Name} from {loser.FullName}";
                body = $"{loser.FullName}'s reign ends at {promo.Name}. The {division} division has a new name at the top of it.";
            }
            else if (vacatedBy is not null && vacated?.LostOn is { } emptied)
            {
                head = $"{champ.FullName} wins the vacant {title.Name}";
                body = $"The {division} belt had sat empty since {vacatedBy.FullName}'s reign ended in {emptied:MMMM yyyy}. {since}";
            }
            else if (winning is { } win)
            {
                head = win.Method switch
                {
                    FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision
                        => $"{champ.FullName} outpoints {win.Opponent.FullName} for the {title.Name}",
                    FightMethod.Knockout or FightMethod.TechnicalKnockout
                        => $"{champ.FullName} stops {win.Opponent.FullName} to win the {title.Name}",
                    FightMethod.SubmissionTap or FightMethod.TechnicalSubmission
                        => $"{champ.FullName} submits {win.Opponent.FullName} to win the {title.Name}",
                    _ => $"{champ.FullName} beats {win.Opponent.FullName} for the {title.Name}",
                };
                body = $"{promo.Name} has a new {division} champion. {since}";
            }
            else
            {
                head = $"{champ.FullName} claims the {title.Name}";
                body = $"{promo.Name} crowns a new {division} champion. {since}";
            }
            items.Add((reign.WonOn, NewsCategory.TitleChange, head, body, promo.Id, champ.Id));
        }
    }

    /// <summary>
    /// The bout on the day a reign began, if the history wrote one: the champion
    /// winning a title fight at the title's own promotion, and who they beat.
    /// </summary>
    private static (Fighter Opponent, FightMethod Method)? TitleWin(World world, Title title, Fighter champ, DateOnly on)
    {
        foreach (var ev in world.Events.Values)
        {
            if (ev.Date != on || ev.Promotion != title.Promotion) continue;
            foreach (var fight in ev.Card)
            {
                if (!fight.IsTitleFight || fight.Outcome is not { } o || o.Winner != champ.Id) continue;
                if (o.Loser is { } lid && world.Get(lid) is { } loser) return (loser, o.Method);
            }
        }
        return null;
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

        string[] Lines(TitleReign r, Fighter c) => new[]
        {
            "The division has run out of obvious answers — which is usually when somebody unobvious turns up.",
            $"Nobody has taken it off {c.LastName} since {r.WonOn:MMMM yyyy}.",
            $"Every challenger since {r.WonOn:MMMM yyyy} has gone home without it.",
            "At this point the belt is less a prize than an address.",
        };

        foreach (var (t, r, years, promo, champ) in notable
                     .OrderByDescending(x => x.Promo.Prestige.Value + x.Reign.Defenses * 3)
                     .Take(5))
            items.Add((startDate.AddDays(-14 - t.Id.Value % 90), NewsCategory.Milestone,
                $"{years} years a champion: {champ.FullName} and the {t.Name}",
                $"{r.Defenses} defenses and counting. {Lines(r, champ)[t.Id.Value % 4]}",
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

    /// <summary>"women's strawweight" / "light heavyweight" — the division as it's billed.</summary>
    private static string DivisionLabel(Fighter f) => DivisionLabel(f.Sex, f.WeightClass);

    private static string DivisionLabel(Sex sex, WeightClass wc)
    {
        string phrase = Regex.Replace(wc.ToString(), "(?<=.)([A-Z])", " $1").ToLowerInvariant();
        return sex == Sex.Female ? $"women's {phrase}" : phrase;
    }

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
                FeudBody(world, a, b),
                promo?.Id ?? default, a.Id));
        }
    }

    /// <summary>
    /// What is actually on the record between two people who can't stand each
    /// other: whether and how they have met, where they sit on the ladder, and
    /// what they share (a room). Nothing in it is asserted that the
    /// world doesn't hold.
    /// </summary>
    private static string FeudBody(World world, Fighter a, Fighter b)
    {
        var facts = new List<string>();

        // The meetings, newest last.
        var met = world.Events.Values
            .Where(e => e.Status == EventStatus.Completed)
            .OrderBy(e => e.Date)
            .SelectMany(e => e.Card
                .Where(f => f.Outcome?.Winner is not null
                            && ((f.RedCorner == a.Id && f.BlueCorner == b.Id) || (f.RedCorner == b.Id && f.BlueCorner == a.Id)))
                .Select(f => (Event: e, Fight: f)))
            .ToList();
        if (met.Count == 0)
            facts.Add("They have never shared a cage.");
        else
        {
            var last = met[^1];
            var winner = last.Fight.Outcome!.Winner == a.Id ? a : b;
            var loser = winner == a ? b : a;
            string how = MethodPhrase(last.Fight.Outcome!.Method);
            string belt = last.Fight.IsTitleFight ? ", with a belt on the line" : "";
            if (met.Count == 1)
                facts.Add($"{winner.LastName} beat {loser.LastName} {how} in {last.Event.Date:MMMM yyyy}{belt}.");
            else
            {
                int wa = met.Count(m => m.Fight.Outcome!.Winner == a.Id), wb = met.Count - wa;
                facts.Add($"They have met {met.Count} times — {a.LastName} {wa}, {b.LastName} {wb} — and {winner.LastName} took the latest {how} in {last.Event.Date:MMMM yyyy}{belt}.");
            }
        }

        // Where they stand.
        Promotion? pa = a.Contract is { } ca ? world.Get(ca.Promotion) : null;
        Promotion? pb = b.Contract is { } cb ? world.Get(cb.Promotion) : null;
        bool sameHouse = pa is not null && pa == pb;
        if (sameHouse && pa!.Divisions.FirstOrDefault(d => d.Matches(a)) is { } div && div.Matches(b))
        {
            var champId = div.Title is { } tid ? world.Get(tid)?.CurrentChampion : null;
            int ra = div.Rankings.IndexOf(a.Id) + 1, rb = div.Rankings.IndexOf(b.Id) + 1;
            if (champId == a.Id || champId == b.Id)
            {
                var champ = champId == a.Id ? a : b;
                var other = champ == a ? b : a;
                int ro = other == a ? ra : rb;
                var beltTitle = world.Get(div.Title!.Value)!;
                facts.Add(ro > 0
                    ? $"{champ.LastName} holds the {beltTitle.Name}; {other.LastName} is ranked No. {ro}."
                    : $"{champ.LastName} holds the {beltTitle.Name}; {other.LastName} is in the same division but outside the rankings.");
            }
            else if (ra > 0 && rb > 0)
                facts.Add($"{a.LastName} is ranked No. {ra} and {b.LastName} No. {rb} in the {DivisionLabel(a)} division.");
        }
        else if (pa is not null && pb is not null)
            facts.Add($"{a.LastName} fights for {pa.Abbreviation}; {b.LastName} for {pb.Abbreviation}.");

        // What they share.
        if (a.Camp is { } camp && a.Camp == b.Camp && world.Get(camp) is { } room)
            facts.Add($"Both train out of {room.Name}.");

        if (sameHouse) facts.Add($"{pa!.Abbreviation} can book it whenever it likes.");
        if (a.Designed && b.Designed && Generation.DesignedRoster.FeudReason(a.LastName, b.LastName) is { } why)
            facts.Add(why);
        return string.Join(" ", facts);
    }

    private static string MethodPhrase(FightMethod m) => m switch
    {
        FightMethod.Knockout => "by knockout",
        FightMethod.TechnicalKnockout => "by technical knockout",
        FightMethod.SubmissionTap => "by submission",
        FightMethod.TechnicalSubmission => "by technical submission",
        FightMethod.UnanimousDecision => "on a unanimous decision",
        FightMethod.SplitDecision => "on a split decision",
        FightMethod.MajorityDecision => "on a majority decision",
        FightMethod.Disqualification => "by disqualification",
        _ => "on the night",
    };

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
        // Every mentorship on the books gets its line, and the coach is only "runs
        // the room" where the data says he does.
        foreach (var r in world.Relationships.Where(r => r.Kind == RelationshipKind.Mentor))
        {
            if (world.Get(r.A) is not { } coach || world.Get(r.B) is not { } pupil) continue;
            var camp = coach.Camp is { } id ? world.Get(id) : null;
            string note = r.Note is { Length: > 0 } n ? char.ToUpperInvariant(n[0]) + n[1..].TrimEnd('.') + "." : "";
            string intro = camp is null ? ""
                : camp.HeadCoach == coach.Id ? $"{coach.LastName} runs {camp.Name}. "
                : $"{coach.LastName} works out of {camp.Name}. ";
            items.Add((startDate.AddDays(-25 - pupil.Id.Value % 60), NewsCategory.Milestone,
                $"{coach.FullName} is in {pupil.FullName}'s corner",
                (intro + note).Trim() is { Length: > 0 } text
                    ? text
                    : "A career's worth of craft, handed over in a training room. It tends to show up on fight night.",
                pupil.Contract is { } c ? c.Promotion : default, pupil.Id));
        }
    }
}
