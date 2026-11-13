using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// Careers begin, pause, and end. This handles the ends: healing fighters whose
/// layoff is over, and retiring the old and faded — cleaning them out of every
/// ranking and, if they held a belt, passing it to the top contender so titles
/// never go stale.
/// </summary>
public static class Lifecycle
{
    /// <summary>Return injured or suspended fighters to action once their layoff/ban has elapsed.</summary>
    public static int HealReturning(World world, DateOnly date)
    {
        int returned = 0;
        foreach (var f in world.Fighters.Values)
        {
            if (f.Status == FighterStatus.Injured && f.CurrentInjury is { } inj && date >= inj.ExpectedReturn)
            {
                f.CurrentInjury = null;
                f.Status = FighterStatus.Active;
                returned++;
            }
            else if (f.Status == FighterStatus.Suspended && f.SuspendedUntil is { } until && date >= until)
            {
                f.SuspendedUntil = null;
                f.Status = FighterStatus.Active;
                returned++;
            }
        }
        return returned;
    }

    /// <summary>Roll monthly retirements for the old and declining. Returns the count retired.</summary>
    public static int RollRetirements(World world, DateOnly date, SimRandom rng)
    {
        int retired = 0;
        foreach (var f in world.Fighters.Values)
        {
            if (f.Status == FighterStatus.Retired) continue;
            int age = f.AgeOn(date);

            // A real career window: most are done by their late thirties, and the
            // over-40 holdout is the exception, not a demographic. (Rates were
            // roughly half this; careers overstayed and the world skewed old.)
            double p = age switch
            {
                >= 42 => 1.0,
                >= 40 => 0.22,
                >= 38 => 0.11,
                >= 36 => 0.05,
                >= 34 => 0.02,
                _ => 0.0,
            };
            // A long skid at an advanced age hastens the decision to walk away.
            if (age >= 33 && f.Momentum <= -45) p += 0.07;

            // The CONTRACT shapes the decision. A fighter with fights left on a
            // deal rides it out — that's guaranteed money, and nobody walks away
            // from it mid-term (retirements cluster where deals END — see the
            // sunset roll in ContractLifecycle.ProcessExpiries). An UNSIGNED
            // fighter staring at a silent phone is far quicker to go.
            if (age < 42)
                p *= f.Contract is { FightsRemaining: > 0 } ? 0.35 : 2.0;

            // A veteran free agent the phone stopped ringing for doesn't grind
            // club shows forever — and when the RECORD is the reason nobody
            // calls, they hang them up quickest of all.
            if (f.Contract is null && age >= 30 && f.Record.TotalBouts >= 10)
                p += f.Record.Wins < f.Record.Losses ? 0.09 : 0.04;

            if (p > 0 && rng.Chance(p))
            {
                Retire(world, f, date);
                Camps.OnRetirement(world, f, date, rng);   // notable names stay in the sport as coaches
                retired++;
            }
        }
        return retired;
    }

    /// <summary>
    /// Monthly WASHOUTS — the sport's brutal early filter, and the reason the
    /// world holds no 0-10 journeymen: they quit at 0-4. A fighter whose record
    /// says it isn't working walks away and gets a normal job — sharply likelier
    /// early in a career (a kid with nothing to show has nothing to stay for),
    /// deeper on a skid, and far likelier unsigned (a paycheck keeps hope
    /// alive). Established veterans on a slide get the dignity of the ordinary
    /// retirement roll instead. Quiet by design: nobody writes a story about a
    /// 1-5 fighter going back to work (News.Retired already gates on fame).
    /// </summary>
    public static int RollWashouts(World world, DateOnly date, SimRandom rng)
    {
        int quit = 0;
        foreach (var f in world.Fighters.Values.OrderBy(f => f.Id.Value))
        {
            if (f.Status != FighterStatus.Active || f.LocalTalent) continue;
            var rec = f.Record;
            int bouts = rec.TotalBouts;
            if (bouts < 3) continue;                                     // everyone gets a fair start
            double winRate = rec.Wins / (double)bouts;
            if (winRate >= 0.45) continue;                               // it's working — fight on

            double despair = (0.45 - winRate) * 2.2;                     // how loudly the record says stop
            double green = Math.Clamp(1.5 - bouts / 20.0, 0.4, 1.5);     // the early years cut deepest
            double skid = rec.CurrentStreak <= -4 ? 1.6 : rec.CurrentStreak <= -2 ? 1.2 : 1.0;
            // A deal with fights left is guaranteed money — almost nobody quits
            // mid-contract; the reckoning comes when it runs out and no one calls.
            double unsigned = f.Contract is { FightsRemaining: > 0 } ? 0.15 : 1.0;
            double p = 0.10 * despair * green * skid * unsigned;
            // Winless past the fair start: most simply stop showing up to the
            // gym — and nobody, signed or not, grinds a goose egg into double
            // digits. (This floor is deliberately harsh: with hundreds of
            // strugglers rolling monthly, softer odds leak 0-6 survivors.)
            if (rec.Wins == 0) p = Math.Max(p, f.Contract is null ? 0.6 : 0.3);

            if (rng.Chance(Math.Min(0.9, p)))
            {
                Retire(world, f, date);
                quit++;
            }
        }
        return quit;
    }

    /// <summary>Retire a fighter: release him, strip him from rankings, hand off any belt.</summary>
    public static void Retire(World world, Fighter f, DateOnly date)
    {
        if (f.Contract is { } held) Contracts.OfferMarket.OnLeaving(world, f, held.Promotion, date);   // a rival's offer dies with his career
        bool wasChampion = world.Titles.Values.Any(t => t.CurrentChampion == f.Id);
        News.Retired(world, f, date, wasChampion);   // reads the contract before it's cleared
        bool players = f.Contract is { } deal && deal.Promotion == world.PlayerPromotion;

        f.Status = FighterStatus.Retired;
        f.CurrentInjury = null;
        f.Contract = null;
        f.CampStipend = false;   // nobody pays a retired man's corner (not a grievance: he left)
        if (players) ForwardCalendar.OffPlayerCards(world, f, date, "he retired");   // off the player's cards at once

        foreach (var promo in world.Promotions.Values)
            foreach (var div in promo.Divisions)
                div.Rankings.Remove(f.Id);

        RelinquishTitles(world, f, date);
    }

    /// <summary>Strip any belt a fighter holds, passing it to the top contender (or vacating it).</summary>
    public static void RelinquishTitles(World world, Fighter f, DateOnly date)
    {
        foreach (var title in world.Titles.Values)
            if (title.CurrentChampion == f.Id)
                VacateBelt(world, title, date);
    }

    /// <summary>
    /// A champion moving on (up a division, say) hands back every belt EXCEPT
    /// the one named — each passes to its division's top contender.
    /// </summary>
    public static void VacateTitlesExcept(World world, Fighter f, TitleId keep, DateOnly date)
    {
        foreach (var title in world.Titles.Values)
            if (title.CurrentChampion == f.Id && title.Id != keep)
                VacateBelt(world, title, date);
    }

    // A retiring champion's belt passes to the top available contender (a new
    // reign), or goes vacant if the division has no one left to crown.
    private static void VacateBelt(World world, Title title, DateOnly date)
    {
        var reign = title.Lineage.LastOrDefault(r => r.LostOn is null);
        if (reign is not null) reign.LostOn = date;

        var division = world.Get(title.Promotion)?.Divisions
            .FirstOrDefault(d => d.Title == title.Id);

        FighterId? heir = null;
        if (division is not null)
        {
            foreach (var id in division.Rankings)
            {
                if (world.Get(id) is { Status: FighterStatus.Active } c && c.Contract?.Promotion == title.Promotion)
                { heir = id; break; }
            }
        }

        if (heir is { } newChamp)
        {
            title.CurrentChampion = newChamp;
            title.Lineage.Add(new TitleReign { Champion = newChamp, WonOn = date });
            division!.Rankings.Remove(newChamp);
        }
        else
        {
            title.CurrentChampion = null;
        }
    }
}
