using System.Text;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// The Origins day-one content (1993): three founding promotions, about forty pioneers and the period
/// voices. It is authored ahead of its wiring, so these tests check the data on its own and guard that
/// none of it has leaked into today's world.
/// </summary>
public class OriginsContentTests
{
    private static string Key(string s) => s.Trim().ToLowerInvariant();

    [Fact]
    public void ThreeFoundingPromotions_OnThreeContinents_WithLore()
    {
        var all = OriginsPromotions.All;
        Assert.Equal(3, all.Count);
        Assert.Equal(3, all.Select(p => p.Region).Distinct().Count());
        Assert.Equal(3, all.Select(p => p.Country).Distinct().Count());
        foreach (var p in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Lore), p.Name);
            Assert.False(string.IsNullOrWhiteSpace(p.Niche), p.Name);
            Assert.False(string.IsNullOrWhiteSpace(p.Founder.Bio), p.Name);
            Assert.False(string.IsNullOrWhiteSpace(p.HomeVenueLore), p.Name);
            Assert.InRange(p.Prestige, 1, 12);               // tiny
            Assert.InRange(p.FoundedYear, 1985, 1993);
            Assert.InRange(p.FirstShowYear, p.FoundedYear, 1993);
            Assert.True(p.Rules.OpenWeight);                 // no weight classes yet
            Assert.InRange(p.Founder.Age, 30, 80);
            // The home city exists in the atlas, in the promotion's own country.
            var city = WorldAtlas.Cities.SingleOrDefault(c => c.Name == p.HomeCity);
            Assert.NotNull(city);
            Assert.Equal(p.Country, city!.CountryCode);
            Assert.Equal(p.Region, WorldAtlas.ByCode(p.Country)!.Continent);
        }
    }

    [Fact]
    public void Promotions_HaveDistinctNamesAndAbbreviations_AgainstTheWholeDesignedWorld()
    {
        var existingNames = WorldBuilder.Options.DefaultPromotions.Select(s => Key(s.Name)).ToHashSet();
        var existingAbbrs = WorldBuilder.Options.DefaultPromotions.Select(s => s.Abbreviation.ToUpperInvariant()).ToHashSet();
        Assert.Equal(OriginsPromotions.All.Count, OriginsPromotions.All.Select(p => Key(p.Name)).Distinct().Count());
        Assert.Equal(OriginsPromotions.All.Count, OriginsPromotions.All.Select(p => p.Abbreviation.ToUpperInvariant()).Distinct().Count());
        foreach (var p in OriginsPromotions.All)
        {
            Assert.DoesNotContain(Key(p.Name), existingNames);
            Assert.DoesNotContain(p.Abbreviation.ToUpperInvariant(), existingAbbrs);
            Assert.InRange(p.Abbreviation.Length, 3, 4);
        }
        // Founders do not share a name with any designed owner or staffer.
        var people = DesignedStaff.Owners.Select(o => Key(o.Name)).Concat(DesignedStaff.Staff.Select(s => Key(s.Name))).ToHashSet();
        foreach (var o in OriginsPromotions.Owners) Assert.DoesNotContain(Key(o.Name), people);
    }

    [Fact]
    public void RuleSets_AreInternallyConsistent_AndEachPromotionIsADifferentIdea()
    {
        foreach (var p in OriginsPromotions.All)
        {
            var r = p.Rules;
            Assert.InRange(r.Rounds, 1, 5);
            Assert.InRange(r.RoundMinutes, 3, 60);
            if (r.TimeLimitMinutes is { } limit) Assert.True(limit >= r.RoundMinutes && limit <= r.Rounds * r.RoundMinutes + 1, p.Name);
            Assert.False(r.Judges && r.DrawAtLimit, $"{p.Name}: judges and a draw at the limit contradict each other");
            Assert.NotEmpty(r.Banned);
            Assert.NotEmpty(r.WaysToWin);
            Assert.False(string.IsNullOrWhiteSpace(r.Format));
        }
        Assert.Equal(3, OriginsPromotions.All.Select(p => (p.Rules.TournamentSize, p.Rules.Judges, p.Rules.RopeEscapesAllowed)).Distinct().Count());
        Assert.Equal(8, OriginsPromotions.All.Single(p => p.Abbreviation == "CRU").Rules.TournamentSize);
        Assert.True(OriginsPromotions.All.Single(p => p.Abbreviation == "SFA").Rules.RopeEscapesAllowed > 0);
    }

    [Fact]
    public void Bridges_ProduceTheExistingShapes()
    {
        foreach (var p in OriginsPromotions.All)
        {
            var spec = p.ToPromotionSpec();
            Assert.Equal(p.Abbreviation, spec.Abbreviation);
            Assert.Equal(p.HomeCity, spec.HomeCity);
            Assert.Null(spec.Divisions);
        }
        Assert.Equal(OriginsPromotions.All.Count, OriginsPromotions.Owners.Count);
    }

    [Fact]
    public void Pioneers_NumberAboutForty_AllMen_AcrossTheThreePromotionsAndTheMarket()
    {
        var all = OriginsPioneers.All;
        Assert.InRange(all.Count, 38, 48);
        Assert.All(all, p => Assert.Equal(Sex.Male, p.Fighter.Sex));
        var abbrs = OriginsPromotions.All.Select(p => p.Abbreviation).ToHashSet();
        foreach (var a in abbrs)
            Assert.True(all.Count(p => p.Fighter.Org == a) >= 8, $"{a} needs a real roster");
        Assert.Contains(all, p => p.Fighter.Org is null);
        Assert.True(all.Count(p => p.Fighter.Org is null) >= 8, "unattached free agents from elsewhere");
        Assert.All(all.Where(p => p.Fighter.Org is not null), p => Assert.Contains(p.Fighter.Org!, abbrs));
        Assert.All(all, p => Assert.False(p.Fighter.Champion));
        // A spread of origins: no more than a handful of countries repeated heavily.
        Assert.True(all.Select(p => p.Fighter.Country).Distinct().Count() >= 12);
        Assert.True(all.Select(p => p.Discipline).Distinct().Count() >= 14, "the era is a menu of single disciplines");
    }

    [Fact]
    public void EveryPioneer_HasBioHookAndNames()
    {
        foreach (var p in OriginsPioneers.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Fighter.First), p.FullName);
            Assert.False(string.IsNullOrWhiteSpace(p.Fighter.Last), p.FullName);
            Assert.False(string.IsNullOrWhiteSpace(p.Hook), p.FullName);
            Assert.False(string.IsNullOrWhiteSpace(p.Fighter.Bio), p.FullName);
            int sentences = p.Fighter.Bio!.Count(c => c == '.' || c == '!' || c == '?');
            Assert.InRange(sentences, 2, 6);
            Assert.True(p.Fighter.Bio!.Length >= 120, p.FullName);
        }
    }

    [Fact]
    public void Pioneers_AreSaneForA1993Start()
    {
        foreach (var p in OriginsPioneers.All)
        {
            var f = p.Fighter;
            Assert.InRange(f.Age, 20, 40);                   // born 1953..1972
            Assert.InRange(f.Overall, 35, 80);               // modest, a few greats
            Assert.InRange(f.Wins, 0, 5);                    // near-zero pro MMA records
            Assert.InRange(f.Losses, 0, 3);
            Assert.InRange(f.Wins + f.Losses, 0, 6);
            Assert.InRange(f.Popularity ?? 10, 5, 45);       // nobody is famous in a new sport
            Assert.InRange(p.HeightCm, 160, 205);
            Assert.InRange(p.WeightKg, 55, 175);
            Assert.NotNull(WorldAtlas.ByCode(f.Country!));
            Assert.Equal(WorldAtlas.ByCode(f.Country!)!.Continent, f.Region);
            Assert.Equal(OriginsPioneers.ClassFor(p.WeightKg), f.WeightClass);
            Assert.Equal(OriginsPioneers.StyleOf(p.Discipline), f.Style);
            // The nearest class really is near: the pioneer is under that class's limit and over the one below.
            Assert.True(p.WeightLbs <= WeightClasses.LimitLbs(f.WeightClass) || f.WeightClass == WeightClass.Heavyweight, p.FullName);
        }
        // Mostly modest, a few real greats, and the size range is wide.
        Assert.InRange(OriginsPioneers.All.Count(p => p.Fighter.Overall >= 70), 3, 8);
        Assert.True(OriginsPioneers.All.Count(p => p.Fighter.Overall <= 58) >= 20);
        Assert.True(OriginsPioneers.All.Max(p => p.WeightKg) - OriginsPioneers.All.Min(p => p.WeightKg) >= 90);
        Assert.True(OriginsPioneers.All.Select(p => p.Fighter.WeightClass).Distinct().Count() >= 6);
    }

    [Fact]
    public void Skills_AreInRange_AndLopsidedToTheDiscipline()
    {
        foreach (var p in OriginsPioneers.All)
        {
            var skills = p.Skills;
            Assert.Equal(Enum.GetValues<SkillId>().Length, skills.Count);
            Assert.All(skills.Values, v => Assert.InRange(v, 1, 95));
            int hi = skills.Values.Max(), lo = skills.Values.Min();
            Assert.True(hi - lo >= 35, $"{p.FullName}: skills too even ({lo}..{hi})");
        }
        SkillId Best(OriginsPioneer p) => p.Skills.OrderByDescending(kv => kv.Value).First().Key;
        // A few anchors: the craft stands out.
        var boxer = OriginsPioneers.All.First(p => p.Discipline == OriginsDiscipline.Boxing);
        Assert.True(boxer.Skills[SkillId.PunchTechnique] > boxer.Skills[SkillId.SubmissionOffense] + 40);
        var judoka = OriginsPioneers.All.First(p => p.Discipline == OriginsDiscipline.Judo);
        Assert.True(judoka.Skills[SkillId.ClinchControl] > judoka.Skills[SkillId.PunchTechnique] + 40);
        var jj = OriginsPioneers.All.First(p => p.Discipline == OriginsDiscipline.JiuJitsu);
        Assert.Contains(Best(jj), new[] { SkillId.SubmissionOffense, SkillId.BottomGame });
        var sumo = OriginsPioneers.All.First(p => p.Discipline == OriginsDiscipline.Sumo);
        Assert.True(sumo.Skills[SkillId.Strength] > sumo.Skills[SkillId.Cardio] + 30);
    }

    [Fact]
    public void Names_AreUnique_AgainstEverythingAlreadyDesigned()
    {
        var existingFull = DesignedRoster.All.Select(d => Key($"{d.First} {d.Last}"))
            .Concat(DesignedLegends.All.Select(d => Key($"{d.First} {d.Last}"))).ToHashSet();
        var existingNick = DesignedRoster.All.Where(d => d.Nickname is not null).Select(d => Key(d.Nickname!))
            .Concat(DesignedLegends.All.Where(d => d.Nickname is not null).Select(d => Key(d.Nickname!))).ToHashSet();
        var existingLast = DesignedRoster.All.Select(d => Key(d.Last))
            .Concat(DesignedLegends.All.Select(d => Key(d.Last))).ToHashSet();
        var people = DesignedStaff.Owners.Select(o => Key(o.Name)).Concat(DesignedStaff.Staff.Select(s => Key(s.Name))).ToHashSet();

        foreach (var p in OriginsPioneers.All)
        {
            Assert.DoesNotContain(Key(p.FullName), existingFull);
            Assert.DoesNotContain(Key(p.FullName), people);
            // Last names feed the keyed side tables, so a shared surname with today's world would mis-resolve.
            Assert.DoesNotContain(Key(p.Fighter.Last), existingLast);
            if (p.Fighter.Nickname is { } n) Assert.DoesNotContain(Key(n), existingNick);
        }

        var full = OriginsPioneers.All.Select(p => Key(p.FullName)).ToList();
        Assert.Equal(full.Count, full.Distinct().Count());
        var nicks = OriginsPioneers.All.Where(p => p.Fighter.Nickname is not null).Select(p => Key(p.Fighter.Nickname!)).ToList();
        Assert.Equal(nicks.Count, nicks.Distinct().Count());
        Assert.True(nicks.Count >= 30, "a nickname where it is natural, which is most of them");

        // Surnames are unique except the one deliberate pair (the Vilela brothers).
        var lasts = OriginsPioneers.All.GroupBy(p => Key(p.Fighter.Last)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Equal(new[] { "vilela" }, lasts);

        // No pioneer reuses a designed org's name either.
        Assert.DoesNotContain(OriginsPioneers.All, p => p.Fighter.Nickname is not null &&
            WorldBuilder.Options.DefaultPromotions.Any(s => Key(s.Name) == Key(p.Fighter.Nickname)));
    }

    [Fact]
    public void SideTables_PointAtRealPioneers()
    {
        var lasts = OriginsPioneers.All.Select(p => p.Fighter.Last).ToHashSet(StringComparer.Ordinal);
        var fulls = OriginsPioneers.All.Select(p => p.FullName).ToHashSet(StringComparer.Ordinal);
        bool Resolves(string k) => lasts.Contains(k) || fulls.Any(f => f.EndsWith(" " + k) || f == k) || fulls.Contains(k);

        // A key is a last name, or "First Last" where it is shared (the Vilelas): the shared bare surname must NOT be used.
        bool KeyOk(string k)
        {
            var hits = OriginsPioneers.All.Where(p => p.Fighter.Last == k || p.FullName == k).ToList();
            return hits.Count == 1;
        }

        foreach (var k in OriginsPioneers.Dispositions.Keys) Assert.True(KeyOk(k), $"disposition key {k}");
        foreach (var k in OriginsPioneers.CampHomes.Keys) Assert.True(KeyOk(k), $"camp key {k}");
        foreach (var b in OriginsPioneers.Bonds) { Assert.True(KeyOk(b.A), b.A); Assert.True(KeyOk(b.B), b.B); Assert.False(string.IsNullOrWhiteSpace(b.Note)); }
        foreach (var k in OriginsPioneers.RivalHeat.Keys)
        {
            var parts = k.Split('|');
            Assert.Equal(2, parts.Length);
            Assert.True(KeyOk(parts[0]) && KeyOk(parts[1]), k);
            Assert.NotNull(OriginsPioneers.FeudReason(parts[0], parts[1]));
        }
        Assert.Equal(OriginsPioneers.RivalHeat.Count, OriginsPioneers.FeudReasons.Count);
        foreach (var p in OriginsPioneers.All.Where(p => p.Fighter.RivalOf is not null))
            Assert.Contains(p.Fighter.RivalOf!, lasts);

        // Every authored gym has lore and a real city; every camp home names a gym that exists.
        var gyms = OriginsPioneers.Gyms.Select(g => g.Name).ToHashSet();
        foreach (var g in OriginsPioneers.Gyms)
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Lore), g.Name);
            Assert.Contains(WorldAtlas.Cities, c => c.Name == g.City);
        }
        Assert.All(OriginsPioneers.CampHomes.Values, g => Assert.Contains(g, gyms));

        // The bios already say these things out loud; the tables must not contradict them.
        var vilela = OriginsPioneers.Bonds.Single(b => b.Kind == RelationshipKind.Family);
        Assert.Equal("Cassio Vilela", vilela.A);
        Assert.Equal(4, OriginsPioneers.RivalHeat.Count);
        Assert.True(Resolves("Kawabata"));
        Assert.Contains(OriginsPioneers.Bonds, b => b.Kind == RelationshipKind.Mentor && b.A == "Kawabata" && b.B == "Hagiwara");
    }

    [Fact]
    public void AuthoredFacts_AgreeWithTheNumbers()
    {
        // Bio-level assertions: the record and the prose must not disagree.
        var by = OriginsPioneers.All.ToDictionary(p => p.Fighter.Last + (p.Fighter.Last == "Vilela" ? p.Fighter.First : ""));
        Assert.Equal((4, 0), (by["Aguiar"].Fighter.Wins, by["Aguiar"].Fighter.Losses));       // "four and oh"
        Assert.Equal((4, 1), (by["Quintela"].Fighter.Wins, by["Quintela"].Fighter.Losses));   // "four wins and one loss"
        Assert.Equal((3, 2), (by["Paranhos"].Fighter.Wins, by["Paranhos"].Fighter.Losses));   // "three wins and two losses"
        Assert.Equal((3, 0), (by["VilelaCassio"].Fighter.Wins, by["VilelaCassio"].Fighter.Losses));
        Assert.Equal((1, 1), (by["VilelaMarcelo"].Fighter.Wins, by["VilelaMarcelo"].Fighter.Losses));
        Assert.Equal((2, 0), (by["Bastos"].Fighter.Wins, by["Bastos"].Fighter.Losses));       // "two and oh"
        Assert.Equal(40, by["Salles"].Fighter.Age);                                           // "a master of forty"
        Assert.Equal(21, by["Amaral"].Fighter.Age);                                           // "twenty-one"
        Assert.Equal(38, by["Kawabata"].Fighter.Age);                                         // "at thirty-eight"
        Assert.Equal(34, by["Khlebnikov"].Fighter.Age);                                       // "he is thirty-four"
        Assert.Equal(36, by["Brannock"].Fighter.Age);                                         // "thirty-six"
        Assert.Equal(168, by["Tofaeono"].WeightKg);                                           // "168 kilos"
        Assert.Equal(155, by["Aizawa"].WeightKg);                                             // "155 kilos"
        Assert.Equal(34, by["Corbin"].Fighter.Age);
        Assert.Equal(WeightClass.Heavyweight, by["Bastos"].Fighter.WeightClass);

        // Temperament matches the bio's voice.
        Assert.Equal(Disposition.Loudmouth, OriginsPioneers.Dispositions["Ishikari"]);
        Assert.Equal(Disposition.Hothead, OriginsPioneers.Dispositions["Skaggs"]);
        Assert.Equal(Disposition.Student, OriginsPioneers.Dispositions["Aguiar"]);
        Assert.Equal(Disposition.Loudmouth, OriginsPioneers.Dispositions["Delacroix"]);

        // Teachers are older than their students.
        foreach (var b in OriginsPioneers.Bonds.Where(b => b.Kind == RelationshipKind.Mentor))
        {
            var a = OriginsPioneers.All.Single(p => p.Fighter.Last == b.A).Fighter;
            var s = OriginsPioneers.All.Single(p => p.Fighter.Last == b.B).Fighter;
            Assert.True(a.Age > s.Age, $"{b.A} should be older than {b.B}");
        }
    }

    [Fact]
    public void Voices_AreEnoughAndPeriodTrue()
    {
        Assert.True(OriginsVoices.LineCount >= 60, $"only {OriginsVoices.LineCount} lines");
        foreach (var kind in Enum.GetValues<OriginsVoiceKind>())
            Assert.True(OriginsVoices.Pool[kind].Count >= 8, kind.ToString());

        var all = OriginsVoices.Pool.Values.SelectMany(p => p).Concat(OriginsVoices.Reactions.Values.SelectMany(p => p)).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        string[] anachronisms =
        {
            "internet", "twitter", "tweet", "facebook", "instagram", "youtube", "podcast", "stream", "online", "social media",
            "hashtag", "viral", "smartphone", "email", "website", "dvd", "blu-ray", "app ", "ufc", "pride fc", "bellator",
            "ratings", "highlight reel", "gif",
        };
        foreach (var line in all)
            foreach (var bad in anachronisms)
                Assert.False(line.Contains(bad, StringComparison.OrdinalIgnoreCase), $"anachronism '{bad}' in: {line}");

        // Every reaction key is a real Disposition/Moment pair.
        foreach (var (d, m) in OriginsVoices.Reactions.Keys) { Assert.True(Enum.IsDefined(d)); Assert.True(Enum.IsDefined(m)); }
    }

    [Fact]
    public void Voices_PickDeterministically_AndFillTheirSlots()
    {
        var a = OriginsVoices.Pick(OriginsVoiceKind.Headline, 17, "Corbin", "Hollister", "Los Angeles", "Crucible");
        var b = OriginsVoices.Pick(OriginsVoiceKind.Headline, 17, "Corbin", "Hollister", "Los Angeles", "Crucible");
        Assert.Equal(a, b);
        foreach (var kind in Enum.GetValues<OriginsVoiceKind>())
            for (int key = 0; key < 40; key++)
            {
                var line = OriginsVoices.Pick(kind, key, "Corbin", "Hollister", "Los Angeles", "Crucible", "boxing");
                Assert.DoesNotContain("{", line);
                Assert.DoesNotContain("}", line);
            }
        // The same key picks different lines across kinds / keys: not a constant.
        Assert.True(Enumerable.Range(0, 60).Select(k => OriginsVoices.Pick(OriginsVoiceKind.StyleTrashTalk, k)).Distinct().Count() > 5);
        Assert.NotEmpty(OriginsVoices.Say(Disposition.Loudmouth, Moment.WinFinish, 3));
        Assert.NotEmpty(OriginsVoices.Say(Disposition.Student, Moment.Loss, 3));
    }

    [Fact]
    public void TodaysWorld_ContainsNoOriginsContent()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2026 });

        // Not the default promotion list, not the built world.
        var abbrs = OriginsPromotions.All.Select(p => p.Abbreviation).ToHashSet();
        Assert.DoesNotContain(WorldBuilder.Options.DefaultPromotions, s => abbrs.Contains(s.Abbreviation));
        Assert.DoesNotContain(world.Promotions.Values, p => abbrs.Contains(p.Abbreviation));
        Assert.DoesNotContain(world.Promotions.Values, p => OriginsPromotions.All.Any(o => o.Name == p.Name));

        // Not the designed roster, not any fighter in the world.
        Assert.DoesNotContain(DesignedRoster.All, d => OriginsPioneers.All.Any(p => p.Fighter == d));
        var names = OriginsPioneers.All.Select(p => (p.Fighter.First, p.Fighter.Last)).ToHashSet();
        Assert.DoesNotContain(world.Fighters.Values, f => names.Contains((f.FirstName, f.LastName)));
        Assert.DoesNotContain(DesignedStaff.Owners, o => OriginsPromotions.Owners.Any(x => x.Name == o.Name || x.Org == o.Org));

        // The opening date of today's mode is still 2026.
        Assert.Equal(2026, world.StartDate.Year);
    }

    // ------------------------------------------------------------------ the owner's showcase
    /// <summary>Writes the showcase markdown only when ORIGINS_SHOWCASE_PATH names a file; otherwise just checks it renders.</summary>
    [Fact]
    public void Showcase_RendersEverybody()
    {
        string md = RenderShowcase();
        foreach (var p in OriginsPromotions.All) Assert.Contains(p.Name, md);
        foreach (var p in OriginsPioneers.All) Assert.Contains(p.FullName, md);
        if (Environment.GetEnvironmentVariable("ORIGINS_SHOWCASE_PATH") is { Length: > 0 } path)
            File.WriteAllText(path, md, new UTF8Encoding(false));
    }

    private static string RenderShowcase()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Origins: day one, 1 January 1993");
        sb.AppendLine();
        sb.AppendLine("Three tiny promotions on three continents, forty-odd pioneers and a first set of period voices. All of it is invented; nothing here is wired into today's world yet.");
        sb.AppendLine();
        sb.AppendLine("A weight in kilograms is the man's natural size. The class beside it is only the nearest class in today's game; in 1993 there are none.");
        sb.AppendLine();

        string Where(string code) => WorldAtlas.ByCode(code)?.Name ?? code;
        var sections = OriginsPromotions.All.Select(p => (Title: p.Name, Org: (string?)p.Abbreviation, Spec: p))
            .Append(("Unattached: free agents from elsewhere", null, null!)).ToList();

        sb.AppendLine("## The three promotions");
        sb.AppendLine();
        foreach (var p in OriginsPromotions.All)
        {
            var r = p.Rules;
            sb.AppendLine($"### {p.Name} ({p.Abbreviation}) - {p.HomeCity}, {Where(p.Country)}");
            sb.AppendLine();
            sb.AppendLine($"*{p.Niche}*");
            sb.AppendLine();
            sb.AppendLine(p.Lore);
            sb.AppendLine();
            sb.AppendLine($"- **Founder:** {p.Founder.Name}, {p.Founder.Age}. {p.Founder.Bio}");
            sb.AppendLine($"- **Founded:** {p.FoundedYear}; first show {p.FirstShowYear}. **Prestige:** {p.Prestige}. **Opening balance:** {p.Balance.Amount:N0}.");
            sb.AppendLine($"- **Home venue:** {p.HomeVenue}. {p.HomeVenueLore}");
            sb.AppendLine($"- **House rules:** {r.Format}. {(r.Rounds == 1 ? $"One round, {r.RoundMinutes} minutes" : $"{r.Rounds} rounds of {r.RoundMinutes} minutes")}"
                + (r.TimeLimitMinutes is { } tl ? $", {tl}-minute limit" : "") + $"; "
                + (r.Judges ? "judges score a bout that goes the distance" : "no judges, a draw at the limit") + $"; gloves: {r.Gloves}; "
                + (r.TournamentSize > 0 ? $"a {r.TournamentSize}-man bracket in one night; " : "")
                + (r.RopeEscapesAllowed > 0 ? $"{r.RopeEscapesAllowed} rope escapes a bout; " : "")
                + $"open weight. Not allowed: {string.Join(", ", r.Banned)}. A bout ends by {string.Join(", ", r.WaysToWin)}.");
            sb.AppendLine();
        }

        sb.AppendLine("## The pioneers");
        sb.AppendLine();
        foreach (var (title, org, _) in sections)
        {
            var group = OriginsPioneers.All.Where(p => p.Fighter.Org == org).ToList();
            sb.AppendLine($"### {title} ({group.Count})");
            sb.AppendLine();
            foreach (var p in group)
            {
                var f = p.Fighter;
                string nick = f.Nickname is null ? "" : $" (\"{f.Nickname}\")";
                string rec = f.Wins + f.Losses == 0 ? "no pro record" : $"{f.Wins}-{f.Losses} in challenge bouts";
                sb.AppendLine($"**{f.First} {f.Last}**{nick} - {p.Discipline}, {Where(f.Country!)}, age {f.Age}, {p.HeightCm} cm / {p.WeightKg} kg (nearest class: {f.WeightClass}), {rec}.");
                sb.AppendLine();
                sb.AppendLine($"*{p.Hook}*");
                sb.AppendLine();
                sb.AppendLine(f.Bio);
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Day-one stories");
        sb.AppendLine();
        sb.AppendLine("Rivalries:");
        foreach (var kv in OriginsPioneers.FeudReasons) sb.AppendLine($"- {kv.Key.Replace("|", " vs ")} (heat {OriginsPioneers.RivalHeat[kv.Key]}): {kv.Value}");
        sb.AppendLine();
        sb.AppendLine("Bonds:");
        foreach (var b in OriginsPioneers.Bonds) sb.AppendLine($"- {b.A} and {b.B} ({b.Kind}, {b.Strength}): {b.Note}");
        sb.AppendLine();
        sb.AppendLine("Gyms:");
        foreach (var g in OriginsPioneers.Gyms) sb.AppendLine($"- **{g.Name}** ({g.City}): {g.Lore}");
        sb.AppendLine();

        sb.AppendLine("## Period voices (a sample)");
        sb.AppendLine();
        foreach (var kind in Enum.GetValues<OriginsVoiceKind>())
        {
            sb.AppendLine($"**{kind}**");
            foreach (var line in OriginsVoices.Pool[kind].Take(3)) sb.AppendLine($"- {line}");
            sb.AppendLine();
        }
        sb.AppendLine($"{OriginsVoices.LineCount} lines in all.");
        return sb.ToString();
    }
}
