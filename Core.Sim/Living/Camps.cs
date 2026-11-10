using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// Fight camps — the rooms fighters actually develop in (see CAMPS-DESIGN.md).
/// The world starts with DESIGNED camps matching regional fight culture (the
/// same authored-day-one philosophy as the designed roster); fighters get
/// assigned at build by geography, style and tier; newgens join a room near
/// home; and every fighter's monthly development runs through their camp's
/// growth multiplier. Coaches, switching, and player money land in later phases.
/// </summary>
public static class Camps
{
    // How much the room matters: training solo stunts you, an average gym is
    // neutral-ish, an elite room with your style on the wall is a real edge.
    public const double UnaffiliatedMultiplier = 0.85;
    public const double StyleMatchBonus = 1.05;

    /// <summary>
    /// A designed day-one camp: where, what it teaches, how good the room is — and
    /// its story. <paramref name="CityName"/> is the TOWN the lore names (a camp
    /// anchored only to its country piled every US room into whichever city rolled
    /// the biggest market — three "Las Vegas" gyms, one of them the Miami one).
    /// </summary>
    private sealed record CampSpec(string Name, string CountryCode, string CityName, PrimaryStyle Specialty,
        int CoachQuality, int Reputation, int Capacity, decimal Fee, string? Lore = null);

    // The founding rooms of the sport, matching regional fight culture. Elite
    // rooms are selective (small capacity, big fee); local gyms take anyone.
    private static readonly CampSpec[] Designed =
    {
        // North America
        new("Apex Combat Club",        "USA", "Las Vegas", PrimaryStyle.WellRounded,       88, 85, 18, 9_000m,
            Lore: "The sport's finishing school: a Las Vegas warehouse where champions spar champions and the whiteboard lists more title fights than most promotions. Getting a mat here is a career achievement in itself."),
        new("Iron Range Wrestling",    "USA", "Chicago", PrimaryStyle.Wrestler,            82, 74, 20, 6_000m,
            Lore: "A converted Midwest field house where Olympic-calibre wrestling is the only language spoken. They don't teach you to strike — they teach you to make striking irrelevant."),
        new("Sunset Striking Lab",     "USA", "Los Angeles", PrimaryStyle.Striker,         76, 66, 22, 5_000m,
            Lore: "Los Angeles pad-work science under one obsessive head trainer — angles, timing, and film study until midnight. Fighters leave slicker, tanner, and allergic to getting hit."),
        new("Northern Lights MMA",     "CAN", "Montreal", PrimaryStyle.WellRounded,        66, 52, 26, 3_000m,
            Lore: "A friendly, freezing Montreal super-gym that quietly turns out complete fighters. The sauna is the only warm place in the building."),
        // South America
        new("Bandeira Jiu-Jitsu",      "BRA", "Rio de Janeiro", PrimaryStyle.BrazilianJiuJitsu, 86, 82, 18, 6_000m,
            Lore: "One of Rio's founding family academies of jiu-jitsu, run under portraits of three generations. Some of the belts on the wall are older than the sport itself."),
        new("Aço Paulista",            "BRA", "Sao Paulo", PrimaryStyle.BrazilianJiuJitsu, 78, 70, 20, 4_000m,
            Lore: "São Paulo steel: a hard-sparring room where the jiu-jitsu is old-school and the pace is not negotiable."),
        new("Furia Curitiba",          "BRA", "Curitiba", PrimaryStyle.Brawler,            74, 68, 22, 3_500m,
            Lore: "Heir to the infamous Curitiba garage rooms, where the sparring is notorious enough that surviving a Tuesday counts as a credential. Subtlety is not on the curriculum."),
        new("Monte Andino MMA",        "ARG", "Buenos Aires", PrimaryStyle.Grappler,       60, 44, 26, 2_000m,
            Lore: "A Buenos Aires club that buses its fighters up to the Andean foothills for camp — altitude and asado, modest equipment, immodest gas tanks."),
        // Europe
        new("Low Country Kickboxing",  "NED", "Amsterdam", PrimaryStyle.Kickboxer,         84, 76, 18, 6_500m,
            Lore: "The Dutch kickboxing conveyor belt — low kicks, high output, and a century of collective ring craft packed into one Amsterdam gym."),
        new("Ironside MMA",            "GBR", "Manchester", PrimaryStyle.WellRounded,      72, 60, 24, 4_000m,
            Lore: "A no-frills Manchester room above an old boxing gym, all cracked mirrors and good habits. It keeps sending polite, dangerous people abroad."),
        new("Volk Sambo Club",         "RUS", "Moscow", PrimaryStyle.Grappler,             83, 75, 18, 5_000m,
            Lore: "A Soviet-era sambo hall on the Moscow ring road that never changed the curtains or the curriculum. Cold showers, colder top pressure."),
        // Asia & Oceania
        new("Emerald Tiger",           "THA", "Bangkok", PrimaryStyle.Kickboxer,           80, 72, 22, 4_000m,
            Lore: "A Bangkok fight camp that took its muay thai lineage into MMA without softening it: dawn runs, clinch rounds in the heat, and eight-year-olds on the next bag over who will out-kick you."),
        new("Kobudo Dojo",             "JPN", "Tokyo", PrimaryStyle.Grappler,              75, 68, 20, 4_500m,
            Lore: "A Tokyo dojo where catch wrestling and judo blend into something quietly vicious. The etiquette is strict; the leg locks are not."),
        new("Harbour City Fight Team", "AUS", "Sydney", PrimaryStyle.WellRounded,          68, 55, 24, 3_000m,
            Lore: "Sydney's big shed by the water — surfers, bouncers and title contenders on the same mats, taking turns holding pads."),
        // Africa
        new("Highveld Fight Club",     "RSA", "Johannesburg", PrimaryStyle.Striker,        64, 48, 26, 2_000m,
            Lore: "Johannesburg striking with thin-air conditioning built in — sparring at altitude makes fight night feel short."),
        new("Sahel Warriors",          "NGA", "Lagos", PrimaryStyle.Brawler,               58, 40, 28, 1_500m,
            Lore: "Lagos heart in a concrete room: heavy bags patched with tape, a roof that leaks in the rainy season, and more raw power per square metre than anywhere in the sport."),
    };

    /// <summary>
    /// Open the rooms and sort the sport into them. Call after every fighter
    /// exists (rosters, free agents, women, founding pool). Kept as a pair of
    /// steps so the world build can claim the AUTHORED rooms in between — the
    /// gyms have to exist before a fighter's story can put them in one.
    /// </summary>
    public static void Build(World world, SimRandom rng)
    {
        Open(world, rng);
        Assign(world, rng);
    }

    /// <summary>Step one: the designed camps and the local gyms beneath them.</summary>
    public static void Open(World world, SimRandom rng)
    {
        foreach (var spec in Designed)
        {
            // Camps open in the town their story names; if that city isn't on the
            // map this build, they fall back to the country's biggest market.
            var country = world.Countries.Values.FirstOrDefault(c => c.Code == spec.CountryCode);
            if (country is null) continue;
            var inCountry = world.Cities.Values.Where(c => c.Country == country.Id).ToList();
            var city = inCountry.FirstOrDefault(c => c.Name == spec.CityName)
                       ?? inCountry.OrderByDescending(c => c.Market).FirstOrDefault();
            if (city is null) continue;

            var camp = new Camp
            {
                Id = world.NewCampId(),
                Name = spec.Name,
                City = city.Id,
                Specialty = spec.Specialty,
                CoachQuality = spec.CoachQuality,
                Reputation = spec.Reputation,
                Capacity = spec.Capacity,
                MonthlyFee = new Money(spec.Fee),
                Lore = spec.Lore,
            };
            world.Camps[camp.Id] = camp;
        }
        // Beneath the famous rooms, every fight country has an open local gym —
        // no name, no waiting list, honest coaching. That's where most of the
        // sport actually trains (the designed rooms only hold a few hundred mats).
        foreach (var country in world.Countries.Values)
        {
            var cities = world.Cities.Values.Where(c => c.Country == country.Id)
                .OrderByDescending(c => c.Market).ToList();
            if (cities.Count == 0) continue;
            // The biggest town that ISN'T already a famous room's address, so the
            // local gym and a designed camp don't share a street.
            var taken = world.Camps.Values.Select(c => c.City).ToHashSet();
            var home = cities.FirstOrDefault(c => !taken.Contains(c.Id)) ?? cities[0];
            var gym = new Camp
            {
                Id = world.NewCampId(),
                Name = $"{home.Name} {LocalGymNames[rng.Range(0, LocalGymNames.Length - 1)]}",
                City = home.Id,
                Specialty = PrimaryStyle.WellRounded,
                CoachQuality = rng.Range(40, 58),
                Reputation = rng.Range(15, 30),
                Capacity = 999,
                MonthlyFee = new Money(800m),
            };
            world.Camps[gym.Id] = gym;
        }
    }

    /// <summary>
    /// Step two: everyone without a room finds one. Fighters whose story already
    /// put them somewhere keep that mat — the rest sort in around them.
    /// </summary>
    public static void Assign(World world, SimRandom rng)
    {
        if (world.Camps.Count == 0) return;

        // Better fighters get first pick of the rooms — that's how elite camps
        // end up star-studded — but the queue is NOISY on purpose: gyms take
        // people, not rankings, so a journeyman training partner or a coach's
        // pet project lands a mat at a famous room alongside the names. ~70%
        // affiliate; the rest train solo (and grow slower).
        var counts = CountByCamp(world);
        var everyone = world.Fighters.Values
            .Where(f => f.Status != FighterStatus.Retired && !f.LocalTalent)
            .OrderByDescending(f => Ability.FighterAbility.CurrentAbility(f) + rng.Range(-14, 14))
            .ToList();
        foreach (var f in everyone)
        {
            if (f.Camp is not null) continue;   // an authored room was already claimed
            if (!rng.Chance(0.70)) continue;
            f.Camp = PickCamp(world, f, counts);
            if (f.Camp is { } id) counts[id] = counts.GetValueOrDefault(id) + 1;
        }
    }

    /// <summary>A newgen finds a room near home (elite ones won't have space for a nobody yet).</summary>
    public static void JoinLocalCamp(World world, Fighter prospect, SimRandom rng)
    {
        if (world.Camps.Count == 0 || !rng.Chance(0.75)) return;
        prospect.Camp = PickCamp(world, prospect, CountByCamp(world));
    }

    // The best room that fits: home country first, then home continent, style
    // match breaking ties — and only where there's space on the mat.
    private static CampId? PickCamp(World world, Fighter f, Dictionary<CampId, int> counts)
    {
        var best = world.Camps.Values
            .Where(c => counts.GetValueOrDefault(c.Id) < c.Capacity)
            .Select(c => (Camp: c, Score: Score(world, c, f)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Camp.Id.Value)
            .FirstOrDefault();
        return best.Camp?.Id;
    }

    private static Dictionary<CampId, int> CountByCamp(World world)
    {
        var counts = new Dictionary<CampId, int>();
        foreach (var f in world.Fighters.Values)
            if (f.Camp is { } id && f.Status != FighterStatus.Retired)
                counts[id] = counts.GetValueOrDefault(id) + 1;
        return counts;
    }

    private static double Score(World world, Camp c, Fighter f)
    {
        var city = world.Get(c.City);
        if (city is null) return 0;
        double geo = city.Country == f.Country ? 3.0
                   : city.Region == f.Nationality ? 1.5
                   : 0;                              // fighters train on their own continent
        if (geo == 0) return 0;
        double style = c.Specialty == f.PrimaryStyle || c.Specialty == PrimaryStyle.WellRounded ? 1.0 : 0;
        return geo + style + c.Reputation / 50.0;
    }

    private static readonly string[] LocalGymNames = { "Fight Team", "MMA", "Combat Academy", "Fight Club" };

    /// <summary>
    /// The development lever: how fast this fighter grows given where they train.
    /// Solo training stunts (0.85); a room scales with coaching quality (0.95–1.20),
    /// a specialty that matches the fighter's game adds a touch more, and a head
    /// coach's personal attention (a mentor) is worth a real edge on top.
    /// </summary>
    public static double GrowthMultiplier(World world, Fighter f)
    {
        if (f.Camp is not { } id || world.Get(id) is not { } camp) return UnaffiliatedMultiplier;
        double mult = 0.95 + camp.CoachQuality / 100.0 * 0.25;
        if (camp.Specialty == f.PrimaryStyle) mult *= StyleMatchBonus;
        if (world.Relationships.Any(r => r.Kind == RelationshipKind.Mentor && r.B == f.Id)) mult += MentorBonus;
        return mult;
    }

    public const double MentorBonus = 0.08;

    // --- Phase 2: the people --------------------------------------------------

    /// <summary>
    /// A notable retiree stays in the sport. A star whose homeland has no elite
    /// room OPENS ONE ("X opens his own gym" — the camp layer's startup engine);
    /// otherwise a good name joins their old room and raises its coaching.
    /// </summary>
    public static void OnRetirement(World world, Fighter f, DateOnly date, SimRandom rng)
    {
        int pop = f.Intangibles.Popularity.Value;
        if (pop < 35) return;   // journeymen just go home

        // What kind of coach they'd make: their peak game plus the pull of a name.
        int quality = Math.Clamp(30 + f.PotentialAbility.Value / 2 + pop / 5, 45, 90);

        // A star founds a camp where their homeland lacks an elite room.
        bool homelandHasEliteRoom = world.Camps.Values.Any(c =>
            c.CoachQuality >= 70 && world.Get(c.City)?.Country == f.Country);
        if (pop >= 60 && !homelandHasEliteRoom && rng.Chance(0.6))
        {
            var city = (f.HomeCity is { } hc ? world.Get(hc) : null)
                ?? world.Cities.Values.Where(c => c.Country == f.Country)
                    .OrderByDescending(c => c.Market).FirstOrDefault();
            if (city is null) return;

            var camp = new Camp
            {
                Id = world.NewCampId(),
                Name = $"Team {f.LastName}",
                City = city.Id,
                Specialty = f.PrimaryStyle,
                CoachQuality = quality,
                Reputation = pop * 3 / 5,
                Capacity = 20,
                MonthlyFee = new Money(Math.Round(quality * 60m / 100m) * 100m),
            };
            world.Camps[camp.Id] = camp;
            camp.HeadCoach = f.Id;
            f.Camp = camp.Id;   // their gym is where they live now

            News.Post(world, date, NewsCategory.Milestone,
                $"{f.FullName} opens Team {f.LastName} in {city.Name}",
                "Retired, but not gone — the next generation gets that experience first-hand.", default, f.Id);
            return;
        }

        // Otherwise a respected name coaches where they trained — the room levels up.
        if (f.Camp is { } id && world.Get(id) is { } room && rng.Chance(0.5))
        {
            if (quality > room.CoachQuality)
                room.CoachQuality = Math.Min(90, room.CoachQuality + (quality - room.CoachQuality + 1) / 2);
            if (room.HeadCoach is null && pop >= 55) room.HeadCoach = f.Id;
            if (pop >= 55)
                News.Post(world, date, NewsCategory.Milestone,
                    $"{f.FullName} joins {room.Name} as a coach",
                    "The room just got smarter — a career's worth of craft stays on the mats.", default, f.Id);
        }
    }

    /// <summary>
    /// Monthly: every head coach who doesn't have one takes a protégé — the room's
    /// best young prospect gets the master's personal attention (a Mentor bond:
    /// extra development, and a storyline that pays off when they win gold).
    /// </summary>
    public static void RunMonthly(World world, DateOnly date, SimRandom rng)
    {
        // Now and then one of the player's prospects asks for a better room — the
        // FM-style decision lands in the inbox; the button lives on their profile.
        if (rng.Chance(0.05) && world.Get(world.PlayerPromotion) is { } player)
        {
            var hopeful = world.RosterOf(player.Id)
                .Where(f => f.Status == FighterStatus.Active && !f.CampStipend
                            && f.PotentialAbility.Value >= 62 && f.AgeOn(date) <= 27
                            && (f.Camp is null || world.Get(f.Camp.Value) is { CoachQuality: < 70 }))
                .OrderByDescending(f => f.PotentialAbility.Value)
                .FirstOrDefault();
            if (hopeful is not null)
                Mail.Post(world, date, MailCategory.FighterRequest, hopeful.FullName,
                    $"{hopeful.FullName} wants a bigger camp",
                    $"\"The room I'm in has taken me as far as it can. Put me somewhere serious and I'll show you what I've got.\" "
                    + "Pay their camp stipend from the fighter profile — better coaching, and they'll remember who sent them.",
                    aboutFighter: hopeful.Id);
        }

        foreach (var camp in world.Camps.Values)
        {
            if (camp.HeadCoach is not { } coachId || world.Get(coachId) is not { } coach) continue;
            bool hasProtege = world.Relationships.Any(r => r.Kind == RelationshipKind.Mentor
                && r.A == coachId && world.Get(r.B) is { Status: FighterStatus.Active } p && p.Camp == camp.Id);
            if (hasProtege || !rng.Chance(0.5)) continue;

            var prospect = world.Fighters.Values
                .Where(f => f.Camp == camp.Id && f.Status == FighterStatus.Active
                            && f.AgeOn(date) <= 25 && f.PotentialAbility.Value >= 62)
                .OrderByDescending(f => f.PotentialAbility.Value)
                .FirstOrDefault();
            if (prospect is null) continue;

            world.Relationships.Add(new Relationship
            {
                A = coachId, B = prospect.Id, Kind = RelationshipKind.Mentor,
                Strength = 70, Since = date, Note = $"trains under {coach.FullName}",
            });
            News.Post(world, date, NewsCategory.Debut,
                $"{coach.FullName} takes {prospect.FullName} under his wing",
                $"The head of {camp.Name} sees something — the prospect gets the master's corner.", default, prospect.Id);
        }
    }

    // --- Phase 3: the player's money -------------------------------------------

    /// <summary>
    /// Pay a fighter's way into a better room: they move to the best elite camp
    /// that will have them, the org picks up the monthly fee, and the fighter
    /// remembers who paid for their corner (loyalty). Returns the new room, or
    /// null when no better one has space.
    /// </summary>
    public static Camp? PayStipend(World world, Promotion promo, Fighter f)
    {
        double currentQuality = f.Camp is { } cur ? world.Get(cur)?.CoachQuality ?? 0 : 0;
        var counts = CountByCamp(world);
        var target = world.Camps.Values
            .Where(c => c.Id != f.Camp && c.CoachQuality > currentQuality + 5
                        && counts.GetValueOrDefault(c.Id) < c.Capacity
                        && world.Get(c.City)?.Region == f.Nationality)
            .OrderByDescending(c => c.CoachQuality)
            .FirstOrDefault();
        if (target is null) return null;

        f.Camp = target.Id;
        f.CampStipend = true;
        f.Loyalty = Math.Min(100, f.Loyalty + 8);   // you paid for their corner
        return target;
    }

    /// <summary>Stop paying: the fighter drops back to whatever room takes them, and remembers that too.</summary>
    public static void StopStipend(World world, Fighter f)
    {
        if (!f.CampStipend) return;
        f.CampStipend = false;
        f.Loyalty = Math.Max(0, f.Loyalty - 5);
        f.Camp = PickCamp(world, f, CountByCamp(world));   // back to the local scene
    }

    /// <summary>What a camp report costs — the famous rooms charge for access.</summary>
    public static Money ReportCost(Camp camp) => new(15_000m + camp.Reputation * 600m);

    /// <summary>
    /// Send a scout to spend a week at a camp: a one-shot intelligence dump on
    /// everyone on its mats, mailed to the player. The fog stays (this is a
    /// snapshot, not surveillance) — but it's a real look behind the curtain.
    /// </summary>
    public static bool BuyReport(World world, Promotion promo, Camp camp, DateOnly date)
    {
        var cost = ReportCost(camp);
        if (promo.Balance.Amount < cost.Amount) return false;
        Finance.Ledger.Expense(promo, date, FinanceCategory.CampFees, cost, $"Camp report — {camp.Name}");

        var members = world.Fighters.Values
            .Where(f => f.Camp == camp.Id && f.Status == FighterStatus.Active)
            .OrderByDescending(Ability.FighterAbility.CurrentAbility)
            .ToList();
        var lines = members.Select(f =>
            $"• {f.FullName} ({f.Record}, age {f.AgeOn(date)}) — looks a {Ability.FighterAbility.CurrentAbility(f)}, " +
            (f.PotentialAbility.Value - Ability.FighterAbility.CurrentAbility(f) >= 8 ? "clear room to grow" : "close to his ceiling"));
        Mail.Post(world, date, MailCategory.General, "Scouting",
            $"Camp report: {camp.Name}",
            $"A week on the mats at {camp.Name} ({world.Get(camp.City)?.Name}). {members.Count} pros in the room:\n"
            + string.Join("\n", lines));
        return true;
    }

    /// <summary>
    /// Monthly camp money: stipends get billed (half price at a sponsored camp) and
    /// buy loyalty; the team-deal retainer gets billed while a sponsorship stands.
    /// Runs for EVERY org — the AI uses the same sinks the player does.
    /// </summary>
    public static void ChargeMonthly(World world, DateOnly date)
    {
        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct))
        {
            foreach (var f in world.RosterOf(promo.Id))
            {
                if (!f.CampStipend || f.Status == FighterStatus.Retired) continue;
                if (f.Camp is not { } id || world.Get(id) is not { } camp) { f.CampStipend = false; continue; }
                decimal fee = camp.MonthlyFee.Amount * (promo.SponsoredCamp == camp.Id ? 0.5m : 1m);
                Finance.Ledger.Expense(promo, date, FinanceCategory.CampFees, new Money(fee), $"Camp stipend — {f.FullName} at {camp.Name}");
                f.Loyalty = Math.Min(100, f.Loyalty + 1);   // the org invests in them, and they know it
            }

            if (promo.SponsoredCamp is { } sc && world.Get(sc) is { } sponsored)
                Finance.Ledger.Expense(promo, date, FinanceCategory.CampFees,
                    SponsorshipCost(sponsored), $"Team deal — {sponsored.Name}");
        }
    }

    /// <summary>The monthly retainer for a team deal — famous rooms cost famous money.</summary>
    public static Money SponsorshipCost(Camp camp) => new(20_000m + camp.Reputation * 400m);

    /// <summary>
    /// Fog: how much extra the player sees of this fighter through camp channels —
    /// your fighters' campmates get seen daily by people you talk to, and a
    /// sponsored camp's whole room is effectively yours to watch.
    /// </summary>
    public static double FamiliarityBonus(World world, Fighter f)
    {
        if (f.Camp is not { } id) return 0;
        if (world.Get(world.PlayerPromotion) is { } player && player.SponsoredCamp == id) return 0.5;
        bool sharesRoom = world.Fighters.Values.Any(x =>
            x.Camp == id && x.Id != f.Id && x.Status != FighterStatus.Retired
            && x.Contract?.Promotion == world.PlayerPromotion);
        return sharesRoom ? 0.5 : 0;
    }

    /// <summary>
    /// A famous room puts its members on everyone's tape — rival orgs misjudge
    /// them less (0 = no help, 1 = fully known). Feeds the AI fog.
    /// </summary>
    public static double PublicExposure(World world, Fighter f) =>
        f.Camp is { } id && world.Get(id) is { Reputation: >= 60 } camp
            ? Math.Min(0.6, camp.Reputation / 150.0)
            : 0;

    // --- Phase 4: gym wars -------------------------------------------------------

    /// <summary>
    /// Two rooms are AT WAR when their fighters keep feuding — two or more live
    /// rivalries spanning the same pair of camps. Emergent, nothing stored.
    /// </summary>
    public static bool AtWar(World world, CampId a, CampId b)
    {
        if (a == b) return false;
        int feuds = 0;
        foreach (var rv in world.Rivalries)
        {
            if (rv.Intensity < 40) continue;
            var fa = world.Get(rv.A);
            var fb = world.Get(rv.B);
            if (fa?.Camp is null || fb?.Camp is null) continue;
            if ((fa.Camp == a && fb.Camp == b) || (fa.Camp == b && fb.Camp == a)) feuds++;
        }
        return feuds >= 2;
    }

    /// <summary>
    /// After a card: camp storylines. Teammates colliding for a belt is an event
    /// in itself, and a protégé taking gold passes the torch to their mentor's legacy.
    /// </summary>
    public static void AfterEvent(World world, Event ev, DateOnly date)
    {
        foreach (var fight in ev.Card)
        {
            if (fight.Outcome is not { } o) continue;
            var a = world.Get(fight.RedCorner);
            var b = world.Get(fight.BlueCorner);
            if (a is null || b is null) continue;

            if (fight.IsTitleFight && a.Camp is { } room && a.Camp == b.Camp && world.Get(room) is { } camp)
                News.Post(world, date, NewsCategory.Rivalry,
                    $"Teammates collide: {a.FullName} vs {b.FullName}",
                    $"Only a belt puts two {camp.Name} fighters in the same cage — the gym watched in silence.", ev.Promotion, a.Id);

            // A battle in a gym war is bigger than the two names on the marquee.
            if (fight.IsTitleFight && a.Camp is { } ra && b.Camp is { } rb && AtWar(world, ra, rb)
                && world.Get(ra) is { } campA && world.Get(rb) is { } campB)
                News.Post(world, date, NewsCategory.Rivalry,
                    $"Gym war: {campA.Name} vs {campB.Name}",
                    $"{a.FullName} against {b.FullName} is the latest battle between two rooms that can't stand each other.", ev.Promotion);

            // The torch passes: a protégé WINS a belt (new reign dated today).
            if (fight.IsTitleFight && o.Winner is { } wid && fight.Title is { } tid
                && world.Get(tid) is { } title && title.CurrentChampion == wid
                && title.Lineage.LastOrDefault() is { } reign && reign.WonOn == ev.Date && reign.Champion == wid
                && world.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Mentor && r.B == wid) is { } bond
                && world.Get(bond.A) is { } mentor && world.Get(wid) is { } champ)
            {
                News.Post(world, date, NewsCategory.Milestone,
                    $"The torch passes: {champ.FullName} wins gold for {mentor.LastName}'s legacy",
                    $"{mentor.FullName}'s protégé is a champion — the master's craft lives on.", ev.Promotion, champ.Id);
            }
        }
    }
}
