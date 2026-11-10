using MmaManager.Core.Model;
using MmaManager.Core.Sim.Flavor;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// The player's "firsts" — the one-time career markers that turn the prestige
/// climb into a story: first sellout, first champion, first pay-per-view, first
/// show abroad. Each fires ONCE (recorded on <see cref="Promotion.Milestones"/>)
/// as celebratory news + a board letter. Player-promotion only; the AI's climb
/// is told through prestige and news, not personal milestones.
/// </summary>
public static class Milestones
{
    /// <summary>Check every event-driven milestone after a card settles.</summary>
    public static void CheckAfterEvent(World world, Promotion promo, Event ev)
    {
        if (promo.Id != world.PlayerPromotion) return;

        Award(world, promo, ev.Date, "first-show", "First show in the books",
            $"{ev.Name} is our first completed card. Every empire starts with one night.");

        if (ev.Financials is { } fin)
        {
            var venue = ev.VenueId is { } vid ? world.Get(vid) : null;
            if (venue is { Capacity: > 0 } && fin.Attendance >= venue.Capacity)
                Award(world, promo, ev.Date, "first-sellout", "First sellout",
                    $"{ev.Name} packed {venue.Name} to the rafters — {fin.Attendance:N0} paid. Not an empty seat in the house.");

            if (fin.Attendance >= 10_000)
                Award(world, promo, ev.Date, "house-10k", "Five figures in the building",
                    $"{fin.Attendance:N0} through the doors for {ev.Name}. That's arena money.");

            if (fin.PayPerViewBuys > 0)
                Award(world, promo, ev.Date, "first-ppv", "First pay-per-view",
                    $"{ev.Name} moved {fin.PayPerViewBuys:N0} PPV buys — we're a television product now.");
        }

        if (ev.Card.Any(f => MatchRating.Of(f) >= 85))
            Award(world, promo, ev.Date, "five-star", "A five-star fight",
                $"The crowd got a classic at {ev.Name}. People will be talking about that one for years.");

        if (promo.Titles.Any(tid => world.Get(tid)?.CurrentChampion is not null))
            Award(world, promo, ev.Date, "first-champion", "First champion crowned",
                "We have our first titleholder. The belt means something now.");

        if (promo.Titles.Select(world.Get).Any(t => t?.Lineage.Any(r => r.Defenses >= 1) == true))
            Award(world, promo, ev.Date, "first-defense", "First title defended",
                "A champion turned back a challenger — the belt has a lineage now.");

        if (ev.Region != promo.BaseRegion)
            Award(world, promo, ev.Date, "first-international-show", "First show abroad",
                $"We ran {ev.Name} outside our home region. The brand is going international.");
    }

    /// <summary>Record a milestone once — celebratory news for the world, a board letter for the owner.</summary>
    public static void Award(World world, Promotion promo, DateOnly date, string key, string title, string body)
    {
        if (promo.Id != world.PlayerPromotion) return;
        if (!promo.Milestones.Add(key)) return;   // already earned — fire once

        Living.News.Post(world, date, NewsCategory.Milestone, $"{promo.Abbreviation}: {title}", body, promo.Id);
        Living.Mail.Post(world, date, MailCategory.Milestone, "The Board", $"Milestone — {title}", body);
    }

    /// <summary>The full milestone catalog with display titles, for the achievements panel.</summary>
    public static IReadOnlyList<(string Key, string Title)> Catalog { get; } = new[]
    {
        ("first-show", "First show"),
        ("first-sellout", "First sellout"),
        ("house-10k", "10,000 in the house"),
        ("first-champion", "First champion"),
        ("first-defense", "First title defense"),
        ("first-tv", "First TV deal"),
        ("first-ppv", "First pay-per-view"),
        ("five-star", "Five-star fight"),
        ("first-international-show", "First show abroad"),
    };
}
