using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The pre-fight media cycle: two to three weeks out, a notable card gets its
/// press conference — and what happens at the dais moves the box office. An
/// electric back-and-forth sells the fight (gate and PPV up); a flat one sells
/// nothing; and when two spicy personas share a stage with real bad blood, the
/// face-off can go WRONG — a brawl the commission fines and the fans reward
/// (nothing sells like chaos, and the promotion wears the embarrassment).
/// One tour per card: the [14, 20)-days-out window is exactly one weekly tick
/// wide, so a card is rolled once. The dividend lands in Event.HypeBonus and
/// is spent by Financials at settlement.
/// </summary>
public static class PressTour
{
    private const ulong PressSalt = 0x9_3E55_70E7UL;

    public static void RunWeekly(World world, DateOnly date, SimRandom shared)
    {
        foreach (var ev in world.Events.Values
                     .Where(e => e.Status == EventStatus.Scheduled && e.Card.Count > 0)
                     .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
                     .ToList())
        {
            int daysOut = ev.Date.DayNumber - date.DayNumber;
            if (daysOut is < 14 or >= 21) continue;
            if (world.Get(ev.Promotion) is not { } promo || promo.Prestige.Value < 15) continue;
            // An AI house tours its FLAGSHIP nights and its superfights — the
            // shows it sells for two months. Its ordinary fight nights, now on
            // the calendar weeks ahead too, don't get a media cycle: the AI's
            // gate has never carried a press-tour dividend on them, and handing
            // every AI title card one would rewrite the business's finances.
            if (ev.Promotion != world.PlayerPromotion && !SignatureEvents.IsSignature(world, ev) && !Storylines.IsSuperfight(ev))
                continue;
            // The player's pressers roll on the week's shared stream as they always
            // have; an AI card (now on the calendar weeks ahead) rolls its own.
            var rng = ev.Promotion == world.PlayerPromotion ? shared
                : new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)ev.Id.Value * 0x2545F491UL ^ (ulong)date.DayNumber ^ PressSalt));

            var main = ev.Card[^1];
            if (world.Get(main.RedCorner) is not { } red || world.Get(main.BlueCorner) is not { } blue) continue;
            int marquee = Math.Max(red.Intangibles.Popularity.Value, blue.Intangibles.Popularity.Value);
            if (!main.IsTitleFight && marquee < 55) continue;   // nobody stages a presser for an anonymous bout

            double spice = Storylines.Spice(red) + Storylines.Spice(blue);
            bool badBlood = Storylines.RivalryBetween(world, main.RedCorner, main.BlueCorner) is { Intensity: >= 30 };

            double brawl = 0.05 + (badBlood ? 0.05 : 0) + Math.Max(0, spice) * 0.3;
            double electric = Math.Max(0.08, 0.20 + spice * 0.5);

            if (rng.Chance(brawl))
            {
                ev.HypeBonus = 0.12 + rng.Range(0, 8) / 100.0;
                Storylines.Heat(world, main.RedCorner, main.BlueCorner, date, 30);
                promo.Reputation = Math.Max(0, promo.Reputation.Value - 2);
                var fine = new Money(2_000m * Math.Max(5, promo.Prestige.Value));
                Finance.Ledger.Expense(promo, date, FinanceCategory.Fines, fine, $"Commission fine — {ev.Name} press brawl");
                News.Post(world, date, NewsCategory.Rivalry,
                    $"CHAOS at the {ev.Name} press conference",
                    $"{red.FullName} and {blue.FullName} had to be pulled apart at the face-off. The commission fined the " +
                    "promotion, the clip is everywhere, and the fight just got a lot bigger.", ev.Promotion);
                if (ev.Promotion == world.PlayerPromotion)
                    Mail.Post(world, date, MailCategory.BoardNote, "PR",
                        $"The {ev.Name} presser turned into a brawl",
                        $"They went through the tables. The commission fined us {fine} and the board is furious — but the " +
                        "phones haven't stopped ringing. This card is going to do numbers.");
            }
            else if (rng.Chance(electric))
            {
                ev.HypeBonus = 0.08 + rng.Range(0, 7) / 100.0;
                Storylines.Heat(world, main.RedCorner, main.BlueCorner, date, 10);
                News.Post(world, date, NewsCategory.Rivalry,
                    $"{red.FullName} and {blue.FullName} light up the {ev.Name} presser",
                    "Real needle, real quotes — the kind of press conference that sells a fight on its own.", ev.Promotion);
                if (ev.Promotion == world.PlayerPromotion)
                    Mail.Post(world, date, MailCategory.General, "PR",
                        $"Electric presser for {ev.Name}",
                        "The main event sold itself at the dais today. Expect a bump at the gate and on screens.");
            }
            else if (rng.Chance(0.35))
            {
                ev.HypeBonus = 0.04 + rng.Range(0, 3) / 100.0;
                if (ev.Promotion == world.PlayerPromotion)
                    Mail.Post(world, date, MailCategory.General, "PR",
                        $"Presser for {ev.Name} went fine",
                        "A few sparks, a few quotes worth clipping. A modest bump, nothing more.");
            }
            // else: two professionals answered questions politely. Nobody cares.
        }
    }
}
