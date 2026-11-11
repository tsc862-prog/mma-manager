using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// Materialises <see cref="DesignedLegends"/> into the built world: the retired
/// greats themselves, their reigns prepended to the belts they held, and (in a
/// second pass, once the camps exist) the offices they run today.
///
/// Runs AFTER <see cref="HistoryBuilder"/> on purpose. HistoryBuilder turns a
/// fighter's tally into bouts in the RECENT window — exactly wrong for a man who
/// last fought fifteen years ago — so legends carry their record as a plain
/// tally, the same way the minted opposition does. Their reigns are the part
/// that has to be real, and those go straight onto the title.
/// </summary>
public static class LegendBuilder
{
    /// <summary>
    /// Create the legends and write their reigns into the title lineages. Call
    /// once the promotions, belts and (optionally) history exist.
    /// </summary>
    public static void Build(World world, DateOnly startDate)
    {
        var gen = FighterGenerator.ForWorld(world);

        foreach (var spec in DesignedLegends.All)
        {
            var f = Create(world, gen, spec, startDate);
            world.Fighters[f.Id] = f;

            foreach (var reign in spec.Reigns)
                if (BeltOf(world, reign) is { } title)
                    Enthrone(world, title, f, reign, startDate);
        }
    }

    /// <summary>
    /// The second pass: put the legends who coach into their rooms. Call after
    /// <see cref="Living.Camps.Build"/> — a designed camp only exists by then.
    /// A famous head coach raises a room the way a retiring star does, but never
    /// lowers the authored coaching a designed camp was tuned with.
    /// </summary>
    public static void StaffCamps(World world, DateOnly startDate)
    {
        foreach (var spec in DesignedLegends.All)
        {
            if (spec.HeadCoachOf is not { } roomName) continue;
            var camp = world.Camps.Values.FirstOrDefault(c => c.Name == roomName);
            if (camp is null) continue;
            var legend = world.Fighters.Values.FirstOrDefault(f =>
                f.Designed && f.Status == FighterStatus.Retired
                && f.FirstName == spec.First && f.LastName == spec.Last);
            if (legend is null) continue;

            camp.HeadCoach = legend.Id;
            legend.Camp = camp.Id;                       // the gym is where they live now
            camp.CoachQuality = Math.Max(camp.CoachQuality, CoachQuality(spec));
            camp.Reputation = Math.Max(camp.Reputation, spec.Popularity * 3 / 5);
        }
    }

    /// <summary>What kind of coach a career like that makes — the same curve retirement uses.</summary>
    private static int CoachQuality(DesignedLegend spec) =>
        Math.Clamp(46 + spec.Popularity / 4 + spec.Reigns.Sum(r => r.Defenses), 50, 92);

    private static Fighter Create(World world, FighterGenerator gen, DesignedLegend spec, DateOnly startDate)
    {
        var id = world.NewFighterId();
        // Built at the level they FOUGHT at — a coach's authority (and the room
        // they run) reads off the career, not off what's left of the body.
        int peak = Math.Clamp(62 + spec.Popularity / 4 + spec.Reigns.Length * 4, 60, 90);
        var f = gen.Generate(id, startDate, new GenOptions
        {
            WeightClass = spec.WeightClass,
            Style = spec.Style,
            Nationality = spec.Region,
            CountryCode = spec.Country,
            Age = spec.AgeNow,
            Overall = peak,
            Sex = spec.Sex,
            WithRecord = false,        // the tally is authored, not rolled
        });

        f.FirstName = spec.First;
        f.LastName = spec.Last;
        f.Nickname = spec.Nickname;
        f.Bio = spec.Bio;
        f.Designed = true;
        f.Status = FighterStatus.Retired;
        f.Momentum = 0;
        f.Contract = null;
        f.Intangibles.Popularity = spec.Popularity;
        // Who the bio says they are, not whoever the hash handed them (after fame is
        // set: ego tracks it). Hash-derived like every disposition, so this costs the
        // generation stream nothing.
        if (DesignedLegends.Dispositions.TryGetValue($"{spec.First} {spec.Last}", out var disposition))
            Personality.ApplyTo(f, disposition);
        // Nobody gets that famous by accident — the era's megastars carried the
        // it-factor too, and the profile should still say so.
        if (spec.Popularity >= 70) f.StarPower = true;

        var rec = f.Record;
        rec.Wins = spec.Wins;
        rec.Losses = spec.Losses;
        rec.Draws = rec.NoContests = 0;
        rec.CurrentStreak = 0;
        // A finish split that reads like the style they fought with.
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)id.Value ^ 0x1E6E_0D15UL));
        int ko = spec.Style is PrimaryStyle.Striker or PrimaryStyle.Kickboxer or PrimaryStyle.Brawler
            ? rng.Range(spec.Wins / 2, spec.Wins * 3 / 4) : rng.Range(spec.Wins / 6, spec.Wins / 3);
        int sub = spec.Style is PrimaryStyle.BrazilianJiuJitsu or PrimaryStyle.Grappler
            ? rng.Range(spec.Wins / 3, spec.Wins / 2) : rng.Range(0, spec.Wins / 5);
        rec.WinsByKo = Math.Min(ko, spec.Wins);
        rec.WinsBySubmission = Math.Min(sub, spec.Wins - rec.WinsByKo);
        rec.WinsByDecision = spec.Wins - rec.WinsByKo - rec.WinsBySubmission;

        gen.AssignHometown(world, f, spec.Country);

        // The honours their profile carries forever.
        foreach (var reign in spec.Reigns)
        {
            var won = startDate.AddYears(-reign.WonYearsAgo);
            string belt = BeltOf(world, reign)?.Name ?? $"{reign.Org} {reign.WeightClass} Championship";
            f.Accolades.Add(new Accolade(won, $"{belt} — {reign.Defenses} defense{(reign.Defenses == 1 ? "" : "s")}"));
        }
        return f;
    }

    /// <summary>The belt a reign refers to, if this world's promotion list has that org running that division.</summary>
    private static Title? BeltOf(World world, DesignedReign reign)
    {
        var promo = world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == reign.Org);
        if (promo is null) return null;
        var division = promo.Divisions.FirstOrDefault(d => d.WeightClass == reign.WeightClass && d.Sex == reign.Sex);
        return division?.Title is { } id ? world.Get(id) : null;
    }

    /// <summary>
    /// Slot a legend's reign in at the HEAD of a belt's lineage. The dates shift
    /// back if they'd collide with a reign the history builder already wrote —
    /// a line of succession has to run one way.
    /// </summary>
    private static void Enthrone(World world, Title title, Fighter legend, DesignedReign reign, DateOnly startDate)
    {
        var won = startDate.AddYears(-reign.WonYearsAgo);
        var lost = startDate.AddYears(-reign.LostYearsAgo);
        if (lost <= won) lost = won.AddYears(1);

        // Everything already on this belt has to start after this reign ends.
        var earliest = title.Lineage.Count > 0 ? title.Lineage.Min(r => r.WonOn) : startDate;
        if (lost >= earliest)
        {
            int shift = lost.DayNumber - earliest.DayNumber + 180;
            won = won.AddDays(-shift);
            lost = lost.AddDays(-shift);
        }

        title.Lineage.Insert(0, new TitleReign
        {
            Champion = legend.Id,
            WonOn = won,
            LostOn = lost,
            Defenses = reign.Defenses,
        });
    }
}
