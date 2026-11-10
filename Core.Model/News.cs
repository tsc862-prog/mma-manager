namespace MmaManager.Core.Model;

/// <summary>What a news item is about — lets the feed be filtered and iconified.</summary>
public enum NewsCategory
{
    TitleChange,
    TitleDefense,
    Signing,
    Retirement,
    Result,
    Debut,
    Finance,
    Award,
    Milestone,
    Callout,
    Rivalry,
    Suspension,
    Withdrawal,
    General,
}

/// <summary>
/// A dated headline in the world news feed. The sim posts these as notable things
/// happen — belts change hands, big names sign, champions walk away — so the world
/// tells its own story between advances.
/// </summary>
public sealed record NewsItem(DateOnly Date, NewsCategory Category, string Headline, string Body, PromotionId Promotion,
    FighterId? AboutFighter = null, EventId? AboutEvent = null);
