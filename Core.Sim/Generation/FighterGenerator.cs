using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// Produces internally coherent fictional fighters: a wrestler looks like a
/// wrestler, ratings correlate with an overall level, and age/record/potential
/// distribute realistically. Every fighter is a pure function of the world seed
/// and the fighter's id, so generation is fully reproducible and independent of
/// order — regenerating fighter #42 from the same seed yields the same fighter.
/// </summary>
public sealed class FighterGenerator
{
    private readonly ulong _worldSeed;
    private readonly IdentityCatalog? _catalog;

    public FighterGenerator(ulong worldSeed, IdentityCatalog? catalog = null)
    {
        _worldSeed = worldSeed;
        _catalog = catalog;
    }

    public static FighterGenerator ForWorld(World world) => new(world.RngSeed, world.ArtCatalog as IdentityCatalog);

    /// <summary>
    /// Mint <paramref name="count"/> fighters, add them to the world as free
    /// agents, and return them. Weight classes are spread across the roster.
    /// </summary>
    public IReadOnlyList<Fighter> Populate(World world, int count, GenOptions? template = null)
    {
        var made = new List<Fighter>(count);
        for (int i = 0; i < count; i++)
        {
            var id = world.NewFighterId();
            var fighter = Generate(id, world.CurrentDate, template);
            AssignHometown(world, fighter);
            EnsureUniqueName(world, fighter);
            world.Fighters[id] = fighter;
            made.Add(fighter);
        }
        return made;
    }

    /// <summary>Mint a pool of women spread evenly across the given weight classes (the women's-division talent).</summary>
    public IReadOnlyList<Fighter> PopulateWomen(World world, int count, IReadOnlyList<WeightClass> classes)
    {
        var made = new List<Fighter>(count);
        for (int i = 0; i < count; i++)
        {
            var id = world.NewFighterId();
            var f = Generate(id, world.CurrentDate, new GenOptions
            {
                Sex = Sex.Female,
                WeightClass = classes[i % classes.Count],
            });
            AssignHometown(world, f);
            EnsureUniqueName(world, f);
            world.Fighters[id] = f;
            made.Add(f);
        }
        return made;
    }

    /// <summary>
    /// Materialize a hand-designed fighter: identity, archetype, level and record
    /// come from the design; the generator fills in everything beneath (the full
    /// skill spread, tendencies, measurables, potential) deterministically. This
    /// is how the designed initial database enters the world — newgens keep using
    /// <see cref="Generate"/> untouched.
    /// </summary>
    public Fighter CreateDesigned(World world, DesignedFighter d)
    {
        var id = world.NewFighterId();
        var f = Generate(id, world.CurrentDate, new GenOptions
        {
            WeightClass = d.WeightClass,
            Style = d.Style,
            Nationality = d.Region,
            CountryCode = d.Country,
            Age = d.Age,
            Overall = d.Overall,
            Sex = d.Sex,
        });

        f.FirstName = d.First;
        f.LastName = d.Last;
        f.Nickname = d.Nickname;
        f.Bio = d.Bio;
        f.Designed = true;
        // The bio says who they are; make the sim able to hear it. Re-derives the
        // numbers around the authored archetype so an authored loudmouth actually
        // behaves like one.
        if (DesignedRoster.Dispositions.TryGetValue(d.Last, out var disposition))
        {
            f.Disposition = disposition;
            Personality.ApplyTo(f, disposition);
        }

        // The designed record replaces the fabricated one; finishes re-split to
        // match how this archetype wins.
        var rng = new SimRandom(SimRandom.Mix(_worldSeed, (ulong)id.Value ^ 0xDE51_67EDUL));
        var rec = f.Record;
        rec.Wins = d.Wins;
        rec.Losses = d.Losses;
        rec.Draws = 0;
        rec.NoContests = 0;
        rec.WinsByKo = rec.WinsBySubmission = rec.WinsByDecision = 0;
        SplitFinishes(rng, f, StyleTemplate.For(d.Style));

        AssignHometown(world, f, d.Country);
        if (d.Popularity is { } pop) f.Intangibles.Popularity = pop;
        if (f.HomeCity is { } dh) f.CityReputation[dh] = Math.Min(70, f.Intangibles.Popularity.Value + 10);
        // A designed headliner that famous didn't get there by accident — the
        // world's authored megastars carry the it-factor from day one.
        if (f.Intangibles.Popularity.Value >= 70) f.StarPower = true;
        double winRate = rec.TotalBouts > 0 ? (double)rec.Wins / rec.TotalBouts : 0.5;
        f.Momentum = Math.Clamp((int)Math.Round((winRate - 0.5) * 90), -70, 70);

        // How attached they already are to their org and their current form. Loyalty
        // defaults from tenure (an established veteran has more history with the org
        // than a fresh prospect) unless the design pins it; an unbeaten fighter is
        // riding their whole record, others carry a designed or modest streak.
        f.Loyalty = d.Loyalty ?? (d.Org is null ? 20 : Math.Clamp(30 + (d.Age - 24) * 2, 22, 58));
        rec.CurrentStreak = d.Streak ?? (d.Losses == 0 ? d.Wins : d.Champion ? 3 : 1);

        world.Fighters[id] = f;
        return f;
    }

    /// <summary>Build one fighter with the given id, as of <paramref name="asOf"/>.</summary>
    public Fighter Generate(FighterId id, DateOnly asOf, GenOptions? options = null)
    {
        var o = options ?? GenOptions.Default;
        var rng = new SimRandom(SimRandom.Mix(_worldSeed, (ulong)id.Value));

        var weightClass = o.WeightClass ?? RollWeightClass(rng);
        var style = o.Style ?? RollStyle(rng);
        var region = o.Nationality ?? RollRegion(rng);
        var template = StyleTemplate.For(style);

        int age = o.Age ?? rng.GaussianInt(28.5, 4.5, 19, 41);
        // Exactly `age` years old as of today (the extra days stay within the year).
        var birthDate = asOf.AddYears(-age).AddDays(-rng.Next(0, 364));

        // Overall level: bell curve skewed toward regional/mid talent; elites rare.
        int overall = o.Overall ?? Math.Clamp(rng.GaussianInt(52, 12, 22, 92), 22, 92);

        var sex = o.Sex ?? Sex.Male;
        string? culture = o.Culture;
        var originRng = OriginRng(id);
        string countryCode;

        // Portrait-first: draw a real pack identity that fits this fighter's sex
        // and weight, and adopt its culture + nationality + style so they have an
        // exact portrait. Stars (high overall) prefer three-stage identities so
        // the world's headliners visibly age.
        IdentitySpec? identity = null;
        if (_catalog is not null && o.CountryCode is null && o.Culture is null)
        {
            var pickRng = new SimRandom(SimRandom.Mix(_worldSeed, (ulong)id.Value ^ 0x1DEA_7175UL));
            identity = _catalog.Pick(pickRng, sex, weightClass, preferTriptych: overall >= 70);
        }

        if (identity is not null)
        {
            culture = identity.Culture;
            if (identity.Style is { } s) { style = s; template = StyleTemplate.For(style); }
            var country = CultureCountry(originRng, identity.Culture);
            if (country is not null) { countryCode = country.Code; region = country.Continent; }
            else countryCode = RollCountryCode(originRng, region);
        }
        else
        {
            // Country comes off a dedicated per-fighter stream so AssignHometown
            // re-derives the exact same country when it resolves the CountryId.
            countryCode = o.CountryCode ?? RollCountryCode(originRng, region);
        }

        // The name culture drives the FIGHT name too, so a Brazilian gets called
        // something a Brazilian crowd would shout.
        string? nameCulture = culture ?? WorldAtlas.ByCode(countryCode)?.Culture;
        var (first, last) = RollName(rng, countryCode, region, sex, nameCulture);

        var fighter = new Fighter
        {
            Id = id,
            FirstName = first,
            LastName = last,
            Nickname = rng.Chance(0.55) ? NameData.RollNickname(rng.NextULong(), nameCulture, region, sex) : null,
            BirthDate = birthDate,
            Sex = sex,
            Nationality = region,
            OriginCountryCode = countryCode,
            Stance = RollStance(rng),
            WeightClass = weightClass,
            PrimaryStyle = style,
        };

        RollMeasurables(rng, fighter, weightClass, sex);
        if (identity is not null)
        {
            // Keep the natural weight inside the portrait's manifest band (Pick
            // guarantees the bands overlap) so the art layer's weight match
            // always agrees with the identity this fighter was built around.
            var (loLb, hiLb) = NaturalWeightBand(weightClass);
            fighter.NaturalWeightLbs = Math.Clamp(fighter.NaturalWeightLbs,
                Math.Max(identity.WeightLo, loLb), Math.Min(identity.WeightHi, hiLb));
        }
        ApplySkills(rng, fighter, template, overall);
        ApplyTendencies(rng, fighter, template);
        ApplyPotential(rng, fighter, age);
        if (o.WithRecord) ApplyRecord(rng, fighter, template, overall, age, o.MaxBouts);
        ApplyIntangiblesAndState(rng, fighter, overall);

        return fighter;
    }

    /// <summary>
    /// Resolve the fighter's country in-world (re-deriving the same roll
    /// <see cref="Generate"/> made off the origin stream) and roll their
    /// hometown — a city in their own country when it has a fight scene,
    /// otherwise anywhere on their home continent (the Georgian who came up
    /// in Moscow). Bigger markets produce more fighters, and everybody is
    /// somebody in their own gym.
    /// </summary>
    public void AssignHometown(World world, Fighter f, string? countryCode = null)
    {
        var rng = OriginRng(f.Id);
        // Prefer the country the generator already chose (portrait-first or roll);
        // fall back to a fresh roll for fighters made outside Generate.
        string code = countryCode ?? f.OriginCountryCode ?? RollCountryCode(rng, f.Nationality);
        if (WorldMap.CountryByCode(world, code) is { } country) f.Country = country.Id;

        if (f.HomeCity is not null) return;
        var cities = world.Cities.Values
            .Where(c => c.Country == f.Country)
            .OrderBy(c => c.Id.Value)
            .ToList();
        if (cities.Count == 0)
            cities = world.Cities.Values
                .Where(c => c.Region == f.Nationality)
                .OrderBy(c => c.Id.Value)
                .ToList();
        if (cities.Count == 0) return;

        var weights = cities.Select(c => (double)c.Market).ToArray();
        f.HomeCity = cities[rng.WeightedIndex(weights)].Id;
        f.CityReputation[f.HomeCity.Value] = Math.Min(60, f.Intangibles.Popularity.Value);
    }

    private SimRandom OriginRng(FighterId id) =>
        new(SimRandom.Mix(_worldSeed, (ulong)id.Value ^ 0x40ED_7014UL));

    /// <summary>Roll a nationality within the continent, weighted by how much fight talent each country produces.</summary>
    private static string RollCountryCode(SimRandom rng, Region region)
    {
        var pool = WorldAtlas.Countries.Where(c => c.Continent == region && c.Weight > 0).ToList();
        if (pool.Count == 0) return WorldAtlas.FlagshipCode(region);
        var weights = pool.Select(c => (double)c.Weight).ToArray();
        return pool[rng.WeightedIndex(weights)].Code;
    }

    /// <summary>Pick a country that uses a name culture (weighted by talent) — how a portrait identity sets nationality.</summary>
    private static Geography.CountrySpec? CultureCountry(SimRandom rng, string culture)
    {
        var pool = WorldAtlas.CountriesWithCulture(culture);
        if (pool.Count == 0) return null;
        var weights = pool.Select(c => (double)c.Weight).ToArray();
        return pool[rng.WeightedIndex(weights)];
    }

    // --- Skills -------------------------------------------------------------

    private static void ApplySkills(SimRandom rng, Fighter f, StyleTemplate t, int overall)
    {
        // Each skill = overall + style bias + noise. Bias pushes a style's
        // signature skills up and its weaknesses down around the overall level.
        int Roll(SkillId s)
        {
            double bias = (t.Weight(s) - 0.5) * t.Spread;
            double val = overall + bias + rng.NextGaussian(0, 6);
            return Math.Clamp((int)Math.Round(val), 1, 99);
        }

        var st = f.Striking;
        st.PunchPower = Roll(SkillId.PunchPower);
        st.PunchTechnique = Roll(SkillId.PunchTechnique);
        st.KickPower = Roll(SkillId.KickPower);
        st.KickTechnique = Roll(SkillId.KickTechnique);
        st.ClinchStriking = Roll(SkillId.ClinchStriking);
        st.StrikingDefense = Roll(SkillId.StrikingDefense);
        st.Footwork = Roll(SkillId.Footwork);

        var g = f.Grappling;
        g.TakedownOffense = Roll(SkillId.TakedownOffense);
        g.TakedownDefense = Roll(SkillId.TakedownDefense);
        g.TopControl = Roll(SkillId.TopControl);
        g.BottomGame = Roll(SkillId.BottomGame);
        g.SubmissionOffense = Roll(SkillId.SubmissionOffense);
        g.SubmissionDefense = Roll(SkillId.SubmissionDefense);
        g.Scrambling = Roll(SkillId.Scrambling);
        g.ClinchControl = Roll(SkillId.ClinchControl);

        var p = f.Physical;
        p.Strength = Roll(SkillId.Strength);
        p.Cardio = Roll(SkillId.Cardio);
        p.Speed = Roll(SkillId.Speed);
        p.Chin = Roll(SkillId.Chin);
        p.Recovery = Roll(SkillId.Recovery);
        p.Flexibility = Roll(SkillId.Flexibility);

        var m = f.Mental;
        m.FightIQ = Roll(SkillId.FightIQ);
        m.Composure = Roll(SkillId.Composure);
        m.Heart = Roll(SkillId.Heart);
        m.Discipline = Roll(SkillId.Discipline);
    }

    private static void ApplyTendencies(SimRandom rng, Fighter f, StyleTemplate t)
    {
        int Around(double centre, double sd = 10) =>
            Math.Clamp((int)Math.Round(rng.NextGaussian(centre, sd)), 1, 99);

        var td = f.Tendencies;
        td.StrikingPreference = Around(t.StrikingPreference, 8);
        td.Aggression = Around(t.Aggression);
        td.Pace = Around(t.Pace);
        td.Counterfighting = Around(t.Counterfighting, 14);
        td.FinishInstinct = Around(t.FinishInstinct);
    }

    // --- Potential ----------------------------------------------------------

    private static void ApplyPotential(SimRandom rng, Fighter f, int age)
    {
        // Anchor to the fighter's realized current ability (the sim's CA
        // weighting) so potential is always a ceiling above where they are now.
        // Younger fighters carry more untapped headroom.
        int current = FighterAbility.CurrentAbility(f);
        double gapMean = age <= 23 ? 14 : age <= 27 ? 8 : age <= 31 ? 3 : 1;
        int gap = Math.Max(0, (int)Math.Round(rng.NextGaussian(gapMean, 6)));
        f.PotentialAbility = Math.Clamp(current + gap, current, 99);
    }

    // --- Backstory record ---------------------------------------------------

    private static void ApplyRecord(SimRandom rng, Fighter f, StyleTemplate t, int overall, int age, int? maxBouts)
    {
        double perYear = Math.Max(1.2, rng.NextGaussian(2.4, 0.5));
        double yearsPro = Math.Max(0, age - 21 + rng.NextGaussian(0, 1.5));
        int bouts = (int)Math.Round(Math.Min(yearsPro * perYear, (age - 18) * 3.0));
        if (bouts < 0) bouts = 0;
        if (maxBouts is { } cap) bouts = Math.Min(bouts, cap);

        // Careers are fought against MATCHED opposition — a regional fighter's
        // record is built on regional opponents — so ability tilts the win rate
        // around .500 rather than setting it against the world average.
        double winRate = Math.Clamp(0.5 + (overall - 48) * 0.004, 0.42, 0.9);

        var rec = f.Record;
        for (int i = 0; i < bouts; i++)
        {
            if (rng.Chance(0.01)) rec.NoContests++;
            else if (rng.Chance(0.035)) rec.Draws++;
            else if (rng.Chance(winRate)) rec.Wins++;
            else
            {
                rec.Losses++;
                // Survivorship: anyone still active is someone the washout
                // filter (Lifecycle.RollWashouts) never claimed. A record that
                // says "it isn't working" ends the backstory here — the live
                // world holds no 5-15 grinders, so day one must not mint them.
                int total = rec.Wins + rec.Losses;
                if (total >= 3 && rec.Wins < total * 0.45)
                {
                    double quit = (0.45 - rec.Wins / (double)total) * 2.5;
                    if (rec.Wins == 0) quit = Math.Max(quit, 0.55);
                    if (rng.Chance(Math.Min(0.85, quit))) break;
                }
            }
        }

        SplitFinishes(rng, f, t);
    }

    /// <summary>Split a record's wins into KO/sub/decision by how this fighter tends to win.</summary>
    private static void SplitFinishes(SimRandom rng, Fighter f, StyleTemplate t)
    {
        var rec = f.Record;
        double koW = 0.20 + (f.Striking.PunchPower + f.Striking.KickPower) / 200.0 * 0.6 + t.KoBias;
        double subW = 0.10 + f.Grappling.SubmissionOffense / 100.0 * 0.6 + t.SubBias;
        double decW = 0.45;
        Span<double> weights = stackalloc double[] { koW, subW, decW };

        for (int i = 0; i < rec.Wins; i++)
        {
            switch (rng.WeightedIndex(weights))
            {
                case 0: rec.WinsByKo++; break;
                case 1: rec.WinsBySubmission++; break;
                default: rec.WinsByDecision++; break;
            }
        }
    }

    // --- Intangibles & dynamic state ---------------------------------------

    private static void ApplyIntangiblesAndState(SimRandom rng, Fighter f, int overall)
    {
        int wins = f.Record.Wins;
        int popularity = Math.Clamp(
            (int)Math.Round(rng.NextGaussian(28, 14) + (overall - 50) * 0.3 + wins * 0.8), 1, 96);
        int charisma = Math.Clamp(
            (int)Math.Round(rng.NextGaussian(45, 18) + (popularity - 40) * 0.2), 1, 96);
        f.Intangibles.Popularity = popularity;
        f.Intangibles.Charisma = charisma;

        // The rare it-factor: the camera just loves some fighters. High charisma
        // makes it a little likelier, but it can land on anyone (~2%). Hash-derived
        // rather than rolled so it doesn't perturb the shared generation stream.
        f.StarPower = StarRoll(f.Id.Value, charisma);

        // Who they are outside the cage. Hash-derived like the it-factor above,
        // so it costs the shared generation stream nothing.
        Personality.Apply(f);

        f.Morale = Math.Clamp((int)Math.Round(rng.NextGaussian(58, 12)), 20, 90);

        double winRate = f.Record.TotalBouts > 0 ? (double)wins / f.Record.TotalBouts : 0.5;
        f.Momentum = Math.Clamp((int)Math.Round((winRate - 0.5) * 90 + rng.NextGaussian(0, 18)), -70, 70);

        f.Status = FighterStatus.Active;
    }

    // Deterministic per fighter (murmur-style finalizer) — the it-factor is baked
    // into who they are, not drawn from the world's dice.
    private static bool StarRoll(int id, int charisma)
    {
        unchecked
        {
            uint h = (uint)id * 2654435761u ^ 0x5F17A11u;
            h ^= h >> 15; h *= 2246822519u;
            h ^= h >> 13; h *= 3266489917u;
            h ^= h >> 16;
            return h / (double)uint.MaxValue < 0.014 + charisma * 0.0002;
        }
    }

    // --- Physical measurables ----------------------------------------------

    private static void RollMeasurables(SimRandom rng, Fighter f, WeightClass wc, Sex sex)
    {
        int meanHeight = wc switch
        {
            WeightClass.Strawweight => 168,
            WeightClass.Flyweight => 170,
            WeightClass.Bantamweight => 172,
            WeightClass.Featherweight => 175,
            WeightClass.Lightweight => 178,
            WeightClass.Welterweight => 181,
            WeightClass.Middleweight => 185,
            WeightClass.LightHeavyweight => 188,
            WeightClass.Heavyweight => 191,
            _ => 180,
        };
        if (sex == Sex.Female) meanHeight -= 8;   // women stand a touch shorter at the same limit
        int height = rng.GaussianInt(meanHeight, 4, meanHeight - 12, meanHeight + 12);
        int reach = Math.Clamp(height + (int)Math.Round(rng.NextGaussian(3, 5)), height - 8, height + 16);
        f.HeightCm = height;
        f.ReachCm = reach;

        var (loLb, hiLb) = NaturalWeightBand(wc);
        f.NaturalWeightLbs = rng.GaussianInt((loLb + hiLb) / 2.0, (hiLb - loLb) / 4.0, loLb, hiLb);
    }

    // Realistic natural / walk-around weight range (lbs) per division.
    private static (int Lo, int Hi) NaturalWeightBand(WeightClass wc) => wc switch
    {
        WeightClass.Strawweight => (106, 115),
        WeightClass.Flyweight => (116, 125),
        WeightClass.Bantamweight => (126, 135),
        WeightClass.Featherweight => (136, 145),
        WeightClass.Lightweight => (146, 155),
        WeightClass.Welterweight => (156, 170),
        WeightClass.Middleweight => (171, 185),
        WeightClass.LightHeavyweight => (186, 205),
        WeightClass.Heavyweight => (206, 265),
        _ => (146, 205),
    };

    // --- Weighted categorical rolls ----------------------------------------

    private static WeightClass RollWeightClass(SimRandom rng)
    {
        // Middle divisions are the deepest; the extremes are thinner.
        Span<double> w = stackalloc double[]
        {
            0.7,  // Strawweight
            0.9,  // Flyweight
            1.1,  // Bantamweight
            1.2,  // Featherweight
            1.3,  // Lightweight
            1.3,  // Welterweight
            1.1,  // Middleweight
            0.9,  // LightHeavyweight
            0.8,  // Heavyweight
        };
        return (WeightClass)rng.WeightedIndex(w);
    }

    private static PrimaryStyle RollStyle(SimRandom rng)
    {
        Span<double> w = stackalloc double[]
        {
            1.2,  // Striker
            0.9,  // Kickboxer
            1.2,  // Wrestler
            1.0,  // BrazilianJiuJitsu
            0.9,  // Grappler
            0.8,  // Brawler
            0.6,  // ClinchFighter
            1.3,  // WellRounded
        };
        return (PrimaryStyle)rng.WeightedIndex(w);
    }

    private static Region RollRegion(SimRandom rng)
    {
        Span<double> w = stackalloc double[]
        {
            1.4,  // NorthAmerica
            1.2,  // SouthAmerica
            1.3,  // Europe
            1.1,  // Asia
            0.6,  // Oceania
            0.7,  // Africa
            0.6,  // MiddleEast
        };
        return (Region)rng.WeightedIndex(w);
    }

    private static Stance RollStance(SimRandom rng)
    {
        Span<double> w = stackalloc double[] { 0.66, 0.28, 0.06 };
        return (Stance)rng.WeightedIndex(w);
    }

    private static (string First, string Last) RollName(SimRandom rng, string countryCode, Region region, Sex sex, string? culture)
    {
        culture ??= WorldAtlas.ByCode(countryCode)?.Culture;
        var (first, last) = culture is not null
            ? NameData.Pool(culture, region, sex)
            : NameData.Pool(region);
        return (rng.Pick(first), rng.Pick(last));
    }

    /// <summary>
    /// Guarantee a fighter's first+last name AND their fight name are unique in
    /// the world, re-rolling from their own culture's pools if either collides
    /// with someone already there. Because the hand-designed database enters the
    /// world first, this is also what RESERVES the authored nicknames — nobody
    /// else ends up called "The Hammer" once Volkov has it. Deterministic off the
    /// world seed + fighter id, so a given seed still yields a stable world. Call
    /// on newgens and discoveries right before they enter the world — never on
    /// hand-authored designed names, and never on the intentional "Local X"
    /// filler (which is meant to repeat).
    /// </summary>
    public void EnsureUniqueName(World world, Fighter f)
    {
        if (f.LocalTalent) return;
        static string Key(string first, string last) => $"{first}|{last}";

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var takenNicknames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in world.Fighters.Values)
        {
            if (o.Id == f.Id || o.LocalTalent) continue;
            taken.Add(Key(o.FirstName, o.LastName));
            if (o.Nickname is { } n) takenNicknames.Add(n);
        }

        var culture = f.OriginCountryCode is { } code ? WorldAtlas.ByCode(code)?.Culture : null;
        var rng = new SimRandom(SimRandom.Mix(_worldSeed, (ulong)f.Id.Value ^ 0x4E11_D3D0UL));

        if (taken.Contains(Key(f.FirstName, f.LastName)))
        {
            var (firsts, lasts) = culture is not null
                ? NameData.Pool(culture, f.Nationality, f.Sex)
                : NameData.Pool(f.Nationality);
            for (int attempt = 0; attempt < 16 && taken.Contains(Key(f.FirstName, f.LastName)); attempt++)
            {
                f.LastName = rng.Pick(lasts);
                if (attempt >= 8) f.FirstName = rng.Pick(firsts);   // deep collision: vary the first name too
            }
        }

        // The fight name: the first tries stay in the authored pools, then the
        // roll escalates into the combinatorial tail, which never runs dry.
        for (int attempt = 0; f.Nickname is { } nick && takenNicknames.Contains(nick) && attempt < 16; attempt++)
            f.Nickname = NameData.RollNickname(rng, culture, f.Nationality, f.Sex, attempt);
    }
}
