using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// The back office. Generates a pool of hireable staff, handles hire/fire (one per
/// role), bills salaries as overhead, and exposes each role's effect so the rest of
/// the sim can ask "how good is this promotion's marketing / medical / coaching?".
/// Only the player hires staff, so the effects are the player's edge.
/// </summary>
public static class StaffService
{
    private static readonly string[] FirstNames =
    {
        "Marcus", "Elena", "Dmitri", "Sofia", "Andre", "Priya", "Hakim", "Nora",
        "Tomas", "Grace", "Ivan", "Lena", "Owen", "Mei", "Diego", "Ruth",
    };
    private static readonly string[] LastNames =
    {
        "Vance", "Okoro", "Petrov", "Marsh", "Cole", "Nakamura", "Rahman", "Diaz",
        "Fischer", "Bauer", "Sung", "Almeida", "Novak", "Reeves", "Adeyemi", "Cross",
    };

    /// <summary>Salary for a given standard of professional.</summary>
    public static Money SalaryFor(int quality) => new(6_000m + quality * 900m);

    /// <summary>Stand up a market of candidates — a few per role at mixed quality.</summary>
    public static void SeedMarket(World world, SimRandom rng)
    {
        foreach (var role in Enum.GetValues<StaffRole>())
            for (int i = 0; i < 4; i++)
            {
                int quality = rng.Range(35, 90);
                world.StaffMarket.Add(new StaffMember
                {
                    Role = role,
                    Name = $"{rng.Pick(FirstNames)} {rng.Pick(LastNames)}",
                    Quality = quality,
                    MonthlySalary = SalaryFor(quality),
                    Background = BackgroundFor(role, quality, rng),
                });
            }
    }

    /// <summary>
    /// Give every promotion in the world a front office. Nobody used to employ
    /// anybody — the player started with empty desks and rival orgs had nothing
    /// to poach, which quietly made the whole staff layer inert. The designed
    /// names take their posts first; the rest of each org's desks are filled to a
    /// standard its prestige can plausibly afford.
    /// </summary>
    public static void SeedFrontOffices(World world, SimRandom rng, bool useDesigned = true)
    {
        if (useDesigned)
            foreach (var spec in DesignedStaff.Staff.Where(s => s.Org is not null))
                if (world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == spec.Org) is { IsPlayerControlled: false } employer)
                    employer.Staff.Add(ToMember(spec));

        foreach (var promo in world.Promotions.Values)
        {
            // The PLAYER starts with empty desks — hiring your own back office is
            // the whole point of the staff screen, and a founded shoestring cannot
            // survive inheriting a salary it never agreed to.
            if (promo.Defunct || promo.IsPlayerControlled) continue;
            int prestige = promo.Prestige.Value;
            // How deep a bench the house can carry: the summit staffs every desk
            // with real professionals, a city hall has a mate who does the posters.
            int desks = prestige >= 45 ? 4 : prestige >= 30 ? 3 : prestige >= 18 ? 2 : 1;

            foreach (var role in Enum.GetValues<StaffRole>())
            {
                if (promo.Staff.Count >= desks) break;
                if (promo.Staff.Any(s => s.Role == role)) continue;
                int quality = Math.Clamp(28 + prestige / 2 + rng.Range(-8, 10), 25, 88);
                promo.Staff.Add(new StaffMember
                {
                    Role = role,
                    Name = $"{rng.Pick(FirstNames)} {rng.Pick(LastNames)}",
                    Quality = quality,
                    MonthlySalary = SalaryFor(quality),
                    Background = BackgroundFor(role, quality, rng),
                });
            }
        }

        // The notable free agents go on the market alongside the generated pool.
        if (!useDesigned) return;
        foreach (var spec in DesignedStaff.Staff.Where(s => s.Org is null))
            world.StaffMarket.Add(ToMember(spec));
    }

    /// <summary>Stamp the authored owners onto the designed houses.</summary>
    public static void SeedOwners(World world)
    {
        foreach (var owner in DesignedStaff.Owners)
        {
            if (world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == owner.Org) is not { } promo) continue;
            promo.OwnerName = owner.Name;
            promo.OwnerNote = owner.Note;
            promo.FoundedYear = owner.FoundedYear;
        }
    }

    private static StaffMember ToMember(DesignedStaffMember s) => new()
    {
        Role = s.Role,
        Name = s.Name,
        Quality = s.Quality,
        MonthlySalary = SalaryFor(s.Quality),
        Background = s.Background,
    };

    /// <summary>Where a generated professional came from — a line, so a hire is a person.</summary>
    private static string BackgroundFor(StaffRole role, int quality, SimRandom rng)
    {
        var pool = quality >= 70 ? Distinguished[role] : quality >= 50 ? Solid[role] : Journeyman[role];
        return rng.Pick(pool);
    }

    private static readonly Dictionary<StaffRole, string[]> Distinguished = new()
    {
        [StaffRole.Marketing] = new[]
        {
            "Built a national campaign out of one grainy knockout clip and a good instinct.",
            "Came from live music promotion and treats a fight card like a headline tour.",
            "Has never missed a sell-out target and mentions it in every meeting.",
        },
        [StaffRole.Medical] = new[]
        {
            "Emergency medicine consultant who works fight nights because somebody competent should.",
            "Published on weight-cutting; regarded as the reason three careers weren't shorter.",
            "Runs the strictest pre-fight screening in the business and won't be argued with.",
        },
        [StaffRole.HeadCoach] = new[]
        {
            "Cornered two world champions and can still hold pads for six rounds.",
            "Reads a fight three exchanges ahead and says exactly one useful sentence per round.",
            "Turned a whole regional gym into contenders on borrowed equipment.",
        },
        [StaffRole.Scouting] = new[]
        {
            "Found a champion on a phone recording from a car park show.",
            "Sits through amateur cards nobody else attends and remembers every name.",
            "Keeps a private list of a hundred prospects and shares it with nobody.",
        },
    };

    private static readonly Dictionary<StaffRole, string[]> Solid = new()
    {
        [StaffRole.Marketing] = new[]
        {
            "Local radio background; knows every regional sponsor by their first name.",
            "Does the posters, the socials and the ticket calls, usually all at once.",
            "Steady, unglamorous, and has never once blown a deadline.",
        },
        [StaffRole.Medical] = new[]
        {
            "A&E nurse by day, cageside by night, unflappable in both.",
            "Sports physio who moved across when the fight money got serious.",
            "Ten years of fight nights and a very organised kit bag.",
        },
        [StaffRole.HeadCoach] = new[]
        {
            "Journeyman fighter turned coach — knows exactly what it feels like to be losing.",
            "Wrestling background, patient with beginners, brutal about conditioning.",
            "Runs a tidy room and gets the basics right every single time.",
        },
        [StaffRole.Scouting] = new[]
        {
            "Works the regional circuit weekend after weekend on their own petrol money.",
            "Ex-fighter with a good eye and a better phone book.",
            "Reliable reads, no flair, almost never badly wrong.",
        },
    };

    private static readonly Dictionary<StaffRole, string[]> Journeyman = new()
    {
        [StaffRole.Marketing] = new[]
        {
            "Learning on the job, cheerfully, in public.",
            "Did club nights before this and it shows, for better and worse.",
        },
        [StaffRole.Medical] = new[]
        {
            "Newly qualified and keen; hasn't seen a truly bad night yet.",
            "Covers the shows nobody else will, for the money nobody else would take.",
        },
        [StaffRole.HeadCoach] = new[]
        {
            "More enthusiasm than method, but the fighters like him.",
            "Fought a bit, coaches a bit, means well.",
        },
        [StaffRole.Scouting] = new[]
        {
            "Watches a lot of tape and hasn't yet learned what to ignore.",
            "A fan with a spreadsheet, which is further than most get.",
        },
    };

    /// <summary>Hire a candidate — replaces any current holder of that role — and pulls them off the market.</summary>
    public static void Hire(World world, Promotion promo, StaffMember candidate)
    {
        promo.Staff.RemoveAll(s => s.Role == candidate.Role);
        world.StaffMarket.Remove(candidate);
        promo.Staff.Add(candidate);
    }

    /// <summary>Fire the holder of a role, returning them to the market.</summary>
    public static void Fire(World world, Promotion promo, StaffRole role)
    {
        var held = promo.Staff.FirstOrDefault(s => s.Role == role);
        if (held is null) return;
        promo.Staff.Remove(held);
        world.StaffMarket.Add(held);
    }

    public static Money MonthlySalaries(Promotion promo) =>
        new(promo.Staff.Sum(s => s.MonthlySalary.Amount));

    // --- Effects ------------------------------------------------------------
    // Staff effects are the PLAYER'S EDGE, as they always have been. Every
    // promotion in the world now has a named front office — so a rival is people
    // rather than a logo, and there's somebody to poach — but the AI's back
    // office is modelled inside its base overhead and its general competence,
    // not applied a second time here. Without this guard, handing the world's
    // twenty promotions a marketing department quietly lifts every gate in the
    // sim and re-tunes the entire economy.
    private static int Quality(Promotion promo, StaffRole role) =>
        promo.IsPlayerControlled
            ? promo.Staff.FirstOrDefault(s => s.Role == role)?.Quality ?? 0
            : 0;

    /// <summary>Gate/attendance uplift from marketing (0 .. ~0.25).</summary>
    public static double MarketingBonus(Promotion promo) => Quality(promo, StaffRole.Marketing) / 400.0;

    /// <summary>Injury-layoff multiplier from medical staff (1.0 down to ~0.7).</summary>
    public static double MedicalRecoveryFactor(Promotion promo) => 1.0 - Quality(promo, StaffRole.Medical) / 300.0;

    /// <summary>Development-rate multiplier from the head coach (1.0 up to ~1.4).</summary>
    public static double CoachGrowthMultiplier(Promotion promo) => 1.0 + Quality(promo, StaffRole.HeadCoach) / 250.0;

    /// <summary>
    /// Fog-of-War reveal bonus from the scouting department (0 up to ~0.55): a
    /// well-scouted promotion sees exact stats on prospects and rivals far sooner.
    /// </summary>
    public static double ScoutingBonus(Promotion? promo) =>
        promo is null ? 0 : Quality(promo, StaffRole.Scouting) / 165.0;
}
