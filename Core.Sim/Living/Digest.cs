using System.Text;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// MMA WEEKLY — the sport, narrated. Once a week the player gets a single
/// compact digest mail: the stories that mattered, the cards coming up, and a
/// prospect worth watching. Everything in it already exists in the news feed
/// and the calendar; the digest is the editor that turns a log into a world.
/// </summary>
public static class Digest
{
    // The categories worth column inches, best first.
    private static readonly NewsCategory[] Priority =
    {
        NewsCategory.TitleChange, NewsCategory.Milestone, NewsCategory.Signing,
        NewsCategory.Callout, NewsCategory.Rivalry, NewsCategory.TitleDefense,
        NewsCategory.Retirement, NewsCategory.Debut, NewsCategory.Finance,
    };

    public static void Weekly(World world, DateOnly date)
    {
        if (world.Get(world.PlayerPromotion) is null) return;

        var weekNews = world.News
            .Where(n => n.Date > date.AddDays(-7) && n.Date <= date)
            .ToList();
        var stories = Priority
            .SelectMany(cat => weekNews.Where(n => n.Category == cat))
            .Take(4)
            .ToList();
        if (stories.Count < 2) return;   // a quiet week doesn't print

        var sb = new StringBuilder();
        sb.AppendLine("THIS WEEK IN THE SPORT");
        foreach (var s in stories) sb.AppendLine($"  • {s.Headline}");

        // The notable cards of the next three weeks: the flagship nights first,
        // then title fights and feuds, then the bigger houses — with what's on top.
        var upcoming = ForwardCalendar.Upcoming(world, date.AddDays(1), date.AddDays(21))
            .Where(c => c.MainEvent is not null)
            .OrderByDescending(c => c.IsSignature)
            .ThenByDescending(c => c.IsPlayer || ForwardCalendar.MeritsHeadline(world, c.Event))
            .ThenByDescending(c => c.Promotion.Prestige.Value)
            .ThenBy(c => c.Date).ThenBy(c => c.Event.Id.Value)
            .Take(4)
            .OrderBy(c => c.Date).ThenBy(c => c.Event.Id.Value)
            .ToList();
        if (upcoming.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ON THE CALENDAR");
            foreach (var c in upcoming)
            {
                string top = c.MainEventLabel(world) is { Length: > 0 } label
                    ? $": {label}{(c.MainEvent!.IsTitleFight ? " (title)" : "")}" : "";
                sb.AppendLine($"  • {c.Date:d MMM} — {c.Event.Name} ({c.Promotion.Abbreviation}){top}");
            }
        }

        // One to watch: a young riser on a streak, rotated deterministically so
        // the column doesn't fixate on a single name.
        var prospect = world.Fighters.Values
            .Where(f => f.Status == FighterStatus.Active && !f.LocalTalent
                && f.AgeOn(date) <= 24 && f.PotentialAbility.Value >= 68 && f.Record.CurrentStreak >= 3)
            .OrderBy(f => (f.Id.Value * 31 + date.DayOfYear / 7) % 97)
            .FirstOrDefault();
        if (prospect is not null)
        {
            string org = prospect.Contract is { } c ? world.Get(c.Promotion)?.Abbreviation ?? "the open market" : "the open market";
            sb.AppendLine();
            sb.AppendLine($"ONE TO WATCH: {prospect.FullName} ({prospect.Record}, age {prospect.AgeOn(date)}) — " +
                          $"{prospect.Record.CurrentStreak} straight at {org}. Scouts keep saying the ceiling is real.");
        }

        Mail.Post(world, date, MailCategory.General, "MMA Weekly",
            $"MMA Weekly — {date:d MMMM yyyy}", sb.ToString().TrimEnd(),
            aboutFighter: prospect?.Id);
    }
}
