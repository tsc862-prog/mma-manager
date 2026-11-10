using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The wider combat-sports calendar: the Olympics, world wrestling and
/// jiu-jitsu championships. Active pros whose style fits compete and medal by
/// ability; the result is a news story, a permanent accolade on the medalists'
/// profiles, and a small popularity / home-region reputation boost. Flavor, not
/// a fight card — nothing on the record.
/// </summary>
public static class SpecialCircuit
{
    private sealed record Meet(int Month, int EveryYears, string Name, PrimaryStyle[] Styles, int Boost);

    private static readonly Meet[] Calendar =
    {
        new(8, 1, "World Wrestling Championships",
            new[] { PrimaryStyle.Wrestler }, Boost: 3),
        new(11, 1, "World Jiu-Jitsu Championship",
            new[] { PrimaryStyle.BrazilianJiuJitsu, PrimaryStyle.Grappler }, Boost: 3),
        new(7, 4, "Olympic Games (combat sports)",
            new[] { PrimaryStyle.Wrestler, PrimaryStyle.Grappler, PrimaryStyle.BrazilianJiuJitsu }, Boost: 5),
    };

    private static readonly string[] Medals = { "gold", "silver", "bronze" };

    public static void RunMonthly(World world, DateOnly date, SimRandom rng)
    {
        foreach (var meet in Calendar)
        {
            if (date.Month != meet.Month || date.Year % meet.EveryYears != 0) continue;

            // Style-matched active pros in their athletic window; form on the day decides.
            var podium = world.Fighters.Values
                .Where(f => f.Status == FighterStatus.Active && f.CurrentInjury is null
                            && meet.Styles.Contains(f.PrimaryStyle) && f.AgeOn(date) <= 34)
                .OrderByDescending(f => FighterAbility.CurrentAbility(f) + rng.NextGaussian(0, 6))
                .Take(3)
                .ToList();
            if (podium.Count < 3) continue;

            for (int i = 0; i < 3; i++)
            {
                var f = podium[i];
                int boost = meet.Boost - i;
                f.Accolades.Add(new Accolade(date, $"{date.Year} {meet.Name} — {Medals[i]}"));
                f.Intangibles.Popularity = f.Intangibles.Popularity.Value + boost;
                Geography.Reputation.BumpFighterRegion(world, f, f.Nationality, boost + 1);
            }

            News.Post(world, date, NewsCategory.Milestone,
                $"{podium[0].FullName} takes {meet.Name} gold",
                $"Silver for {podium[1].FullName}, bronze for {podium[2].FullName}. The grappling world was watching.",
                default, podium[0].Id);
        }
    }
}
