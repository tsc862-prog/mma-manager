using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// Posts world news. The sim calls in when something noteworthy happens — a belt
/// changes hands, a name signs, a champion retires — and the feed builds itself.
/// A cap keeps the backlog bounded over a long career.
/// </summary>
public static class News
{
    private const int MaxItems = 1000;

    public static void Post(World world, DateOnly date, NewsCategory category, string headline, string body, PromotionId promo,
        FighterId? aboutFighter = null, EventId? aboutEvent = null)
    {
        world.News.Add(new NewsItem(date, category, headline, body, promo, aboutFighter, aboutEvent));
        if (world.News.Count > MaxItems) world.News.RemoveRange(0, world.News.Count - MaxItems);
    }

    // The big moments come with the new champion's own words — a belt changing
    // hands should read like somebody was there, not like a database update.
    public static void TitleChanged(World world, Title title, Fighter newChamp, Fighter oldChamp, DateOnly date) =>
        Post(world, date, NewsCategory.TitleChange,
            $"{newChamp.FullName} dethrones {oldChamp.FullName} for the {title.Name}",
            Flavor.Quotes.Say(newChamp, Flavor.Moment.TitleWin, date), title.Promotion, newChamp.Id);

    public static void TitleClaimed(World world, Title title, Fighter champ, DateOnly date) =>
        Post(world, date, NewsCategory.TitleChange,
            $"{champ.FullName} claims the vacant {title.Name}",
            Flavor.Quotes.Say(champ, Flavor.Moment.TitleWin, date), title.Promotion, champ.Id);

    public static void TitleDefended(World world, Title title, Fighter champ, DateOnly date) =>
        Post(world, date, NewsCategory.TitleDefense,
            $"{champ.FullName} retains the {title.Name}", "", title.Promotion, champ.Id);

    public static void Signed(World world, Promotion promo, Fighter f, Money purse, DateOnly date, bool renewal)
    {
        // Only surface signings worth talking about (or anything the player does).
        if (promo.Id != world.PlayerPromotion && FighterAbility.CurrentAbility(f) < 68) return;
        string verb = renewal ? "re-signs with" : "signs with";
        Post(world, date, NewsCategory.Signing,
            $"{f.FullName} {verb} {promo.Abbreviation}",
            $"Reported at {purse}/fight. " + Flavor.Quotes.Say(f, Flavor.Moment.Signed, date), promo.Id, f.Id);
    }

    public static void Retired(World world, Fighter f, DateOnly date, bool champion)
    {
        if (!champion && FighterAbility.CurrentAbility(f) < 68) return;
        Post(world, date, NewsCategory.Retirement,
            $"{f.FullName} announces retirement",
            $"Hangs them up with a {f.Record} record. " + Flavor.Quotes.Say(f, Flavor.Moment.Retiring, date),
            f.Contract?.Promotion ?? default, f.Id);
    }
}
