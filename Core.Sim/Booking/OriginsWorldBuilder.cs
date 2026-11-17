using MmaManager.Core.Model;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// The world of 1 January 1993 (Origins mode): the sport has no name yet. Three tiny promotions on three
/// continents (<see cref="OriginsPromotions"/>), each with its own house rules and its own idea of a
/// night; the forty-odd authored pioneers (<see cref="OriginsPioneers"/>) signed where their stories put
/// them or loose on the market; the nine authored gyms; the day-one feuds and bonds; and a small
/// generated pool of single-discipline specialists around them. No designed modern roster, no legends,
/// no back-dated history, no prologue, no weight classes: every promotion runs one OPEN division.
///
/// Day-one capabilities (<see cref="DayOneUnlocks"/>): the three home scenes (USA, Japan, Brazil), the
/// pay-per-view carrier rung (the Crucible sells to a carrier from night one) and local sponsors.
/// Nothing else: no weight classes, no women's MMA, no rounds/judges/gloves as sport-wide rules (each
/// house's rules are its own), no startups, crossovers, superfights or acquisitions.
///
/// Its own random stream (<c>Mix(seed, OriginsBuildSalt)</c>) and its own id space: nothing here is
/// shared with a today's-mode build, which never reaches this class.
/// </summary>
public static class OriginsWorldBuilder
{
    public static readonly DateOnly StartDate = new(1993, 1, 1);

    private const ulong OriginsBuildSalt = 0x0219_1993_B11DUL;

    /// <summary>Specialists generated around the pioneers on day one (all 0-0, mostly unsigned).</summary>
    public const int SupportingPool = 64;

    /// <summary>What the sport has on 1 January 1993.</summary>
    public static IReadOnlyList<Capability> DayOneUnlocks { get; } = new[]
    {
        Capability.Scene("USA"), Capability.Scene("JPN"), Capability.Scene("BRA"),
        Capability.PpvCarrier,
        Capability.SponsorLocal,
    };

    /// <summary>The house's nights and its bracket purse, by abbreviation (authored rule sets say the size).</summary>
    private static readonly Dictionary<string, (ShowFormat Format, decimal Prize)> Formats = new(StringComparer.Ordinal)
    {
        ["CRU"] = (ShowFormat.OneNightTournament, 20_000m),   // the winner's cheque is the whole pitch: about a quarter of a good night's gate (no pay-per-view yet)
        ["SFA"] = (ShowFormat.Card, 0m),
        ["DLB"] = (ShowFormat.GymChallenge, 0m),
    };

    /// <summary>Authored home-venue sizes (the lore's numbers).</summary>
    private static readonly Dictionary<string, int> VenueSeats = new(StringComparer.Ordinal)
    {
        ["CRU"] = 4_000,   // "holds four thousand on the night"
        ["SFA"] = 2_800,   // "a 2,800-seat municipal hall"
        ["DLB"] = 1_200,   // "six hundred standing and twice that number on the ramps"
    };

    public static World Build(WorldBuilder.Options o)
    {
        var start = o.StartDate;
        var world = new World
        {
            CurrentDate = start, StartDate = start, RngSeed = o.Seed, ArtCatalog = o.IdentityCatalog,
            Mode = WorldMode.Origins,
        };
        foreach (var cap in DayOneUnlocks) Era.Unlock(world, cap);

        var rng = new SimRandom(SimRandom.Mix(o.Seed, OriginsBuildSalt));
        Geography.WorldMap.Build(world, rng);
        var gen = new FighterGenerator(o.Seed, o.IdentityCatalog);

        // --- The three houses ---------------------------------------------------------
        var promos = new List<Promotion>();
        for (int p = 0; p < OriginsPromotions.All.Count; p++)
        {
            var spec = OriginsPromotions.All[p];
            var home = world.Cities.Values
                .Where(c => string.Equals(c.Name, spec.HomeCity, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Id.Value).First();
            var owner = spec.ToOwner();
            var rules = spec.ToFightRules();
            var (format, prize) = Formats.TryGetValue(spec.Abbreviation, out var fm) ? fm : (ShowFormat.Card, 0m);
            var promo = new Promotion
            {
                Id = world.NewPromotionId(),
                Name = spec.Name,
                Abbreviation = spec.Abbreviation,
                BaseRegion = spec.Region,
                // An observer start flags nobody (all three are the AI's: staffed, booked, no objectives).
                IsPlayerControlled = !o.Observer && o.Founding is null && p == o.PlayerPromotionIndex,
                Balance = spec.Balance,
                Prestige = spec.Prestige,
                Lore = spec.Lore,
                OwnerName = owner.Name,
                OwnerNote = owner.Note,
                FoundedYear = spec.FoundedYear,
                HomeCity = home.Id,
                Rules =
                {
                    RoundMinutes = spec.Rules.RoundMinutes,
                    StandardRounds = spec.Rules.Rounds,
                    TitleRounds = spec.Rules.Rounds,
                    Fight = rules,
                    ShowFormat = format,
                    TournamentSize = format == ShowFormat.OneNightTournament ? spec.Rules.TournamentSize : 0,
                    TournamentPrize = new Money(prize),
                },
            };
            promo.TierClearance = Org.OrgSizing.SizeOf(promo);
            world.Promotions[promo.Id] = promo;
            if (promo.IsPlayerControlled) world.PlayerPromotion = promo.Id;
            promos.Add(promo);

            // The authored home room, in the home city.
            int seats = VenueSeats.GetValueOrDefault(spec.Abbreviation, 1_000);
            var venue = new Venue
            {
                Id = world.NewVenueId(), Name = spec.HomeVenue, City = home.Id,
                Capacity = seats, Rent = new Money(seats * 4m),
            };
            world.Venues[venue.Id] = venue;

            SeedPopularity(world, promo, home);
            OpenDivision(world, promo);
        }

        // --- The pioneers -------------------------------------------------------------
        var pioneers = new List<(OriginsPioneer Row, Fighter Fighter)>();
        foreach (var row in OriginsPioneers.All)
            pioneers.Add((row, CreatePioneer(world, gen, row, start)));

        foreach (var (row, f) in pioneers)
        {
            if (row.Fighter.Org is not { } org || promos.FirstOrDefault(p => p.Abbreviation == org) is not { } house) continue;
            f.Contract = PromotionSetup.SignContract(world, house, start, f, rng);
            // (Today's signing rounds to $500 steps; a 1993 purse is never nothing.)
            if (f.Contract.PursePerFight.Amount < Contracts.Negotiator.OriginsPurseFloor)
            {
                f.Contract.PursePerFight = new Money(Contracts.Negotiator.OriginsPurseFloor);
                f.Contract.WinBonus = new Money(Contracts.Negotiator.OriginsPurseFloor / 2);
            }
            // (A bracket house pays no win bonuses: the cheque is the bonus. The signing's draws are kept.)
            f.Contract.WinBonus = Contracts.Negotiator.WinBonusAt(world, house.Id, f.Contract.WinBonus);
            f.Loyalty = Math.Clamp(30 + (row.Fighter.Age - 24) * 2, 22, 58);
        }

        // Day-one bad blood and the bonds the bios claim.
        foreach (var (row, f) in pioneers.Where(x => x.Row.Fighter.RivalOf is not null))
            if (pioneers.FirstOrDefault(x => x.Row.Fighter.Last == row.Fighter.RivalOf).Fighter is { } enemy
                && !world.Rivalries.Any(r => (r.A == f.Id && r.B == enemy.Id) || (r.A == enemy.Id && r.B == f.Id)))
                world.Rivalries.Add(new Rivalry
                {
                    A = f.Id, B = enemy.Id, Since = start,
                    Intensity = OriginsPioneers.RivalHeatOf(row.Fighter.Last, row.Fighter.RivalOf!),
                });
        Fighter? Resolve(string key)
        {
            var full = pioneers.Where(x => x.Row.FullName == key).ToList();
            if (full.Count == 1) return full[0].Fighter;
            var last = pioneers.Where(x => x.Row.Fighter.Last == key).ToList();
            return last.Count == 1 ? last[0].Fighter : null;
        }
        foreach (var bond in OriginsPioneers.Bonds)
        {
            if (Resolve(bond.A) is not { } a || Resolve(bond.B) is not { } b || a.Id == b.Id) continue;
            world.Relationships.Add(new Relationship
            {
                A = a.Id, B = b.Id, Kind = bond.Kind, Strength = bond.Strength, Since = start, Note = bond.Note,
            });
        }

        // --- The specialists around them (0-0: the sport starts today) -----------------
        OriginsTalent.SeedPool(world, gen, start, rng, SupportingPool);

        // --- The rooms ---------------------------------------------------------------
        OpenGyms(world, pioneers);
        foreach (var f in world.Fighters.Values.OrderBy(f => f.Id.Value)) OriginsTalent.FindGym(world, f);   // the generated men only

        // --- The people behind the desks ---------------------------------------------
        Org.StaffService.SeedMarket(world, rng);
        Org.StaffService.SeedFrontOffices(world, rng, useDesigned: false);
        if (world.Get(world.PlayerPromotion) is { } player)
            Org.Objectives.Seed(player, start, new DateOnly(start.Year, 12, 31), world);

        RankingService.RecomputeAll(world, start, o.RankingsDepth);
        GlobalRankings.Recompute(world);
        OriginsShows.SeedOpening(world);
        Personas.StampAll(world);   // the portraits' looks (no catalog: nothing)
        return world;
    }

    /// <summary>One open division per house; the Crucible's bracket and the Association's ring have a belt (vacant), the Garage has none.</summary>
    private static void OpenDivision(World world, Promotion promo)
    {
        var division = new Division { WeightClass = Division.OpenClass, Sex = Sex.Male, Open = true };
        promo.Divisions.Add(division);
        string? name = promo.Rules.ShowFormat switch
        {
            ShowFormat.OneNightTournament => $"{promo.Name} Tournament Championship",
            ShowFormat.Card => $"{promo.Abbreviation} Open-weight Championship",
            _ => null,   // challenge nights settle arguments between gyms, not belts
        };
        if (name is null) return;
        var title = new Title
        {
            Id = world.NewTitleId(), Promotion = promo.Id, WeightClass = Division.OpenClass, Name = name,
            CurrentChampion = null, Prestige = promo.Prestige.Value,
        };
        world.Titles[title.Id] = title;
        promo.Titles.Add(title.Id);
        division.Title = title.Id;
    }

    /// <summary>Known at home, a little around the home country, nobody abroad (no television yet).</summary>
    private static void SeedPopularity(World world, Promotion promo, City home)
    {
        int prestige = promo.Prestige.Value;
        foreach (var c in world.Cities.Values)
        {
            int value = c.Id == home.Id ? prestige + 10 : c.Country == home.Country ? prestige * 3 / 5 : 0;
            if (value > 0) promo.PopularityByCity[c.Id] = Math.Clamp(value, 0, 100);
        }
    }

    /// <summary>
    /// A pioneer from his authored row: generated around his style and level (tendencies, measurables,
    /// potential, the name rolls are then overwritten), then every skill from his discipline profile, his
    /// real size, his disposition, his challenge-bout record if he has one, and his fame.
    /// </summary>
    internal static Fighter CreatePioneer(World world, FighterGenerator gen, OriginsPioneer row, DateOnly start)
    {
        var d = row.Fighter;
        var id = world.NewFighterId();
        var f = gen.Generate(id, start, new GenOptions
        {
            WeightClass = d.WeightClass, Style = d.Style, Nationality = d.Region, CountryCode = d.Country,
            Age = d.Age, Overall = d.Overall, Sex = Sex.Male, WithRecord = false,
        });
        f.FirstName = d.First;
        f.LastName = d.Last;
        f.Nickname = d.Nickname;
        f.Bio = d.Bio;
        f.Designed = true;
        f.HeightCm = row.HeightCm;
        f.ReachCm = row.HeightCm + (int)(SimRandom.Mix(world.RngSeed, (ulong)id.Value ^ 0x2EAC_4UL) % 9) - 3;
        f.NaturalWeightLbs = row.WeightLbs;
        foreach (var (skill, value) in row.Skills) OriginsTalent.SetSkill(f, skill, value);

        var key = OriginsPioneers.Dispositions.ContainsKey(row.FullName) ? row.FullName : d.Last;
        if (OriginsPioneers.Dispositions.TryGetValue(key, out var disposition)) Personality.ApplyTo(f, disposition);

        // The garage's challenge matches are on the record (no events: they happened before the sport had
        // a calendar); how they were won follows the art.
        var rec = f.Record;
        rec.Wins = d.Wins; rec.Losses = d.Losses; rec.Draws = 0; rec.NoContests = 0;
        bool grappler = row.Discipline is OriginsDiscipline.JiuJitsu or OriginsDiscipline.LutaLivre or OriginsDiscipline.Judo
            or OriginsDiscipline.Sambo or OriginsDiscipline.ProWrestling;
        rec.WinsBySubmission = grappler ? d.Wins : 0;
        rec.WinsByKo = grappler ? 0 : d.Wins;
        rec.WinsByDecision = 0;
        rec.CurrentStreak = d.Losses == 0 ? d.Wins : 1;
        double winRate = rec.TotalBouts > 0 ? (double)rec.Wins / rec.TotalBouts : 0.5;
        f.Momentum = Math.Clamp((int)Math.Round((winRate - 0.5) * 90), -70, 70);

        gen.AssignHometown(world, f, d.Country);
        if (d.Popularity is { } pop) f.Intangibles.Popularity = pop;
        if (f.HomeCity is { } hc) f.CityReputation[hc] = Math.Min(70, f.Intangibles.Popularity.Value + 10);
        f.StarPower = false;   // nobody is a star yet: the first star is a chapter of the story
        world.Fighters[id] = f;
        return f;
    }

    /// <summary>The nine authored training rooms, in their cities, with the pioneers the camp-home table puts there.</summary>
    private static void OpenGyms(World world, List<(OriginsPioneer Row, Fighter Fighter)> pioneers)
    {
        foreach (var gym in OriginsPioneers.Gyms)
        {
            var city = world.Cities.Values.Where(c => c.Name == gym.City).OrderBy(c => c.Id.Value).FirstOrDefault();
            if (city is null) continue;
            // The room teaches what its people do.
            var members = pioneers.Where(x => CampOf(x.Row) == gym.Name).ToList();
            var style = members.Count > 0
                ? members.GroupBy(x => x.Row.Fighter.Style).OrderByDescending(g => g.Count()).ThenBy(g => (int)g.Key).First().Key
                : PrimaryStyle.WellRounded;
            ulong h = SimRandom.Mix(world.RngSeed, (ulong)gym.Name.Length * 977UL + (ulong)gym.Name[0]);
            var camp = new Camp
            {
                Id = world.NewCampId(),
                Name = gym.Name,
                City = city.Id,
                Specialty = style,
                CoachQuality = 52 + (int)(h % 14),
                Reputation = 18 + (int)((h >> 8) % 16),
                Capacity = 30,
                MonthlyFee = new Money(300m + (h >> 16) % 5 * 100m),
                Lore = gym.Lore,
            };
            world.Camps[camp.Id] = camp;
            foreach (var (_, fighter) in members) fighter.Camp = camp.Id;
            // The teacher who is also on the roster runs the room.
            if (members.FirstOrDefault(x => x.Row.Fighter.Age >= 36) is { Fighter: { } elder }) camp.HeadCoach = elder.Id;
        }
    }

    private static string? CampOf(OriginsPioneer row) =>
        OriginsPioneers.CampHomes.TryGetValue(row.FullName, out var room) || OriginsPioneers.CampHomes.TryGetValue(row.Fighter.Last, out room)
            ? room : null;
}
