using System.Text;
using System.Text.RegularExpressions;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// The Origins timeline (chapters, era broadcasters and sponsors, scheduled arrivals). Authored ahead of the
/// engine that will fire it, so these tests check the data on its own, check it is consistent with the
/// day-one content, and guard that none of it leaked into today's world.
/// </summary>
public class OriginsChapterTests
{
    private static readonly string[] KnownSlots = { "Org", "Founder", "City", "Country", "Fighter", "Year", "Partner" };
    private static string Key(string s) => s.Trim().ToLowerInvariant();

    // Names already used by today's media ladder and sponsor market (MediaService / Sponsorships keep them private).
    private static readonly string[] ExistingMedia =
    {
        "Combat One", "Octagon TV", "The Fight Channel", "Ring Report", "PrimeSport", "Vertex Sports", "Global Combat Network",
        "AllSport 24", "NBS", "United Broadcasting", "Star Network", "Meridian One", "Streamflix", "Arena+", "Watchtower",
        "Nimbus Video", "MainCard", "PrimeView", "BoxSeat",
    };
    private static readonly string[] ExistingSponsors =
    {
        "Main Street Motors", "Cornerstone Hardware", "Falcon Lager", "Golden Gate Pawn", "Riverside Tire & Auto", "Grit Supplements",
        "Ironclad Nutrition", "Apex Fuel", "Warpath Whey", "Titan Gloves", "Kensei Fightwear", "Vale Tudo Supply Co.", "Voltage Energy",
        "Overdrive", "Static Surge", "Redline Brewing Co.", "Iron City Ales", "Halftime Lager", "Summit Bank", "Nova Telecom",
        "Redline Autos", "Frontier Insurance", "Meridian Airlines", "Vanta Sportswear", "Atlas Watches", "Polar Cola",
    };

    private static IEnumerable<string> AllText(OriginsChapter c)
    {
        yield return c.Title; yield return c.Trigger; yield return c.Digest;
        foreach (var e in c.Effects) yield return e;
        foreach (var n in new[] { c.ByPlayer, c.ByRival, c.ByWorld })
            if (n is not null) { yield return n.Headline; yield return n.Body; }
    }

    private static int Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    // ------------------------------------------------------------------ keys and graph
    [Fact]
    public void Keys_AreUniqueStableFormat_AndTheTimelineIsAboutFortyToSixtyChapters()
    {
        var all = OriginsChapters.All;
        Assert.InRange(all.Count, 40, 60);
        Assert.Equal(all.Count, all.Select(c => c.Key).Distinct().Count());
        Assert.All(all, c => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", c.Key));
        Assert.All(all, c => Assert.InRange(c.Key.Length, 6, 60));
        Assert.All(all, c => Assert.InRange(c.Era, 1, 6));
        for (int era = 1; era <= 6; era++) Assert.True(all.Count(c => c.Era == era) >= 4, $"era {era} needs real content");
        Assert.All(all, c => Assert.False(string.IsNullOrWhiteSpace(c.Title)));
    }

    [Fact]
    public void Requires_AllExist_AndTheGraphHasNoCycles()
    {
        var byKey = OriginsChapters.All.ToDictionary(c => c.Key);
        foreach (var c in OriginsChapters.All)
        {
            Assert.DoesNotContain(c.Key, c.Requires);
            Assert.Equal(c.Requires.Length, c.Requires.Distinct().Count());
            foreach (var r in c.Requires) Assert.True(byKey.ContainsKey(r), $"{c.Key} requires unknown '{r}'");
        }
        var state = new Dictionary<string, int>();   // 1 = visiting, 2 = done
        void Visit(string k)
        {
            if (state.GetValueOrDefault(k) == 2) return;
            Assert.NotEqual(1, state.GetValueOrDefault(k));   // back-edge = cycle
            state[k] = 1;
            foreach (var r in byKey[k].Requires) Visit(r);
            state[k] = 2;
        }
        foreach (var c in OriginsChapters.All) Visit(c.Key);
    }

    [Fact]
    public void Windows_AreOrdered_AndConsistentWithWhatTheyRequire()
    {
        var byKey = OriginsChapters.All.ToDictionary(c => c.Key);
        foreach (var c in OriginsChapters.All)
        {
            Assert.InRange(c.EarliestMonth, 0, 372);
            Assert.InRange(c.LatestMonth, c.EarliestMonth + 1, 372);   // the game is about thirty years long
            foreach (var r in c.Requires.Select(k => byKey[k]))
            {
                Assert.True(c.EarliestMonth >= r.EarliestMonth, $"{c.Key} can be due before {r.Key} can fire");
                Assert.True(c.LatestMonth >= r.LatestMonth, $"{c.Key} has a deadline before the deadline of {r.Key}");
                Assert.True(c.Era >= r.Era, $"{c.Key} is in an earlier era than {r.Key}");
            }
        }
        // The arc is roughly chronological by era.
        for (int era = 1; era < 6; era++)
            Assert.True(OriginsChapters.All.Where(c => c.Era == era).Min(c => c.EarliestMonth)
                        <= OriginsChapters.All.Where(c => c.Era == era + 1).Min(c => c.EarliestMonth));
        Assert.Equal(0, OriginsChapters.All.Min(c => c.EarliestMonth));
        Assert.True(OriginsChapters.All.Max(c => c.LatestMonth) >= 330, "the timeline reaches the modern world");
    }

    [Fact]
    public void ClosingChapter_RequiresTheWholeArc_AndEverythingElseLeadsToIt()
    {
        var closing = OriginsChapters.ByKey("the-sport-you-built");
        Assert.Equal(6, closing.Era);
        Assert.Equal(OriginsChapters.All.Max(c => c.LatestMonth), closing.LatestMonth);
        // Every chapter is an ancestor of the closing one, so none is orphaned.
        var byKey = OriginsChapters.All.ToDictionary(c => c.Key);
        var seen = new HashSet<string>();
        void Walk(string k) { if (seen.Add(k)) foreach (var r in byKey[k].Requires) Walk(r); }
        Walk(closing.Key);
        var orphans = OriginsChapters.All.Select(c => c.Key).Where(k => !seen.Contains(k)).ToList();
        Assert.True(orphans.Count == 0, "chapters not reachable from the closing chapter: " + string.Join(", ", orphans));
    }

    // ------------------------------------------------------------------ triggers and unlocks
    [Fact]
    public void Triggers_UseKnownKindsAndConditions()
    {
        foreach (var c in OriginsChapters.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Trigger), c.Key);
            Assert.True(Enum.IsDefined(c.Structured.Kind), c.Key);
            var parts = c.Structured.Condition.Split(':');
            Assert.Contains(parts[0], OriginsChapters.KnownConditions);
            switch (parts[0])
            {
                case "SignMedia":
                    Assert.Equal(2, parts.Length);
                    Assert.True(OriginsCapabilities.MediaKeys.Contains(parts[1]) || parts[1] == "ForeignNetwork", c.Key);
                    break;
                case "SignSponsor":
                    Assert.Contains(parts[1], OriginsCapabilities.SponsorKeys);
                    break;
                case "CrownChampionInClass":
                    Assert.Contains(parts[1], Enum.GetNames<WeightClass>());
                    break;
                case "PromotionReachesTier":
                    Assert.Contains(parts[1], Enum.GetNames<OrgSize>());
                    break;
                case "AdoptRules":
                    Assert.Contains(parts[1], new[] { "rounds", "judges", "gloves" });
                    break;
            }
            // The kind and the condition belong together.
            var firstOnly = new[] { "SignMedia", "SignSponsor", "HoldEventInNewCountry", "CrownChampionInClass", "RunWomensBout", "CoPromote", "AdoptRules", "SanctionedByCommission", "AcquireRival", "HoldEventAbove", "ChampionVsChampion" };
            if (c.Structured.Kind == OriginsTriggerKind.FirstPromotionTo) Assert.Contains(parts[0], firstOnly);
            if (c.Structured.Kind == OriginsTriggerKind.WorldCondition) Assert.DoesNotContain(parts[0], firstOnly);
            if (c.Structured.Kind == OriginsTriggerKind.Dated) Assert.Equal("Date", parts[0]);
            // A media unlock the player can win is fired by signing that media kind.
            foreach (var u in c.Unlocks.Where(u => u.StartsWith("Media:") && u != "Media:PpvCarrier"))
                Assert.Equal("SignMedia:" + u.Substring(6), c.Structured.Condition);
        }
    }

    [Fact]
    public void EveryUnlock_ParsesToKindOrKindKey_WithKnownKindsClassesAndAtlasCountries()
    {
        foreach (var c in OriginsChapters.All)
            foreach (var u in c.Unlocks)
            {
                Assert.True(OriginsCapabilities.IsValid(u), $"{c.Key}: bad unlock '{u}'");
                var parts = u.Split(':');
                bool keyed = OriginsCapabilities.KeyedKinds.Contains(parts[0]);
                Assert.Equal(keyed ? 2 : 1, parts.Length);
            }
        Assert.False(OriginsCapabilities.IsValid("WeightClass:Cruiserweight"));
        Assert.False(OriginsCapabilities.IsValid("CountryScene:ZZZ"));
        Assert.False(OriginsCapabilities.IsValid("Media"));
        Assert.False(OriginsCapabilities.IsValid("Sponsor:Tobacco"));
    }

    [Fact]
    public void EveryCapability_IsUnlockedExactlyOnce_SoTheWorldCanReachModern()
    {
        var unlocked = OriginsChapters.All.SelectMany(c => c.Unlocks).ToList();
        foreach (var cap in OriginsCapabilities.All)
            Assert.True(unlocked.Count(u => u == cap) == 1, $"'{cap}' is unlocked {unlocked.Count(u => u == cap)} times");
        // Media: six kinds. Sponsors: seven types. Classes: all nine.
        Assert.Equal(6, OriginsCapabilities.MediaKeys.Length);
        Assert.Equal(7, OriginsCapabilities.SponsorKeys.Length);
        Assert.Equal(9, Enum.GetNames<WeightClass>().Length);
        foreach (var wc in Enum.GetNames<WeightClass>()) Assert.Contains("WeightClass:" + wc, unlocked);
        Assert.Contains("WomensDivisions", unlocked);
    }

    [Fact]
    public void WeightClasses_ArriveInABelievableOrder_AndTheLightestMenComeLate()
    {
        int M(WeightClass wc) => OriginsChapters.All.Single(c => c.Unlocks.Contains("WeightClass:" + wc)).EarliestMonth;
        Assert.True(M(WeightClass.Heavyweight) <= M(WeightClass.LightHeavyweight));
        Assert.True(M(WeightClass.LightHeavyweight) <= M(WeightClass.Lightweight));
        Assert.True(M(WeightClass.Lightweight) <= M(WeightClass.Bantamweight));
        Assert.True(M(WeightClass.Bantamweight) <= M(WeightClass.Flyweight));
        Assert.True(M(WeightClass.Heavyweight) <= M(WeightClass.Middleweight) + 1);
        Assert.True(M(WeightClass.Middleweight) <= M(WeightClass.Welterweight));
        Assert.True(M(WeightClass.Welterweight) <= M(WeightClass.Featherweight));
        // The first split is early; the last class is at least ten years later.
        Assert.True(M(WeightClass.Flyweight) - M(WeightClass.Heavyweight) >= 120);
        // Women's divisions arrive after the first bout and the resistance, and the star after the division.
        var womens = new[] { "womens-first-bout", "womens-resistance", "womens-first-division", "womens-first-star", "womens-headline-card" }
            .Select(OriginsChapters.ByKey).ToList();
        for (int i = 1; i < womens.Count; i++) Assert.Contains(womens[i - 1].Key, womens[i].Requires);
        Assert.Contains("WomensDivisions", womens[2].Unlocks);
        Assert.True(womens[0].EarliestMonth >= 96);
    }

    [Fact]
    public void CountryScenes_WakeInAnOrder_ForRoughlyTwentyFiveToThirtyFiveCountries()
    {
        var order = OriginsChapters.SceneOrder;
        Assert.InRange(order.Count, 25, 40);
        Assert.Equal(order.Count, order.Select(o => o.Country).Distinct().Count());
        var dayOne = OriginsPromotions.All.Select(p => p.Country).ToHashSet();
        foreach (var (chapter, code) in order)
        {
            Assert.DoesNotContain(code, dayOne);   // the founding countries already have scenes
            var country = WorldAtlas.ByCode(code);
            Assert.NotNull(country);
            Assert.True(WorldAtlas.Cities.Any(c => c.CountryCode == code), $"{code} has no atlas city to host shows");
            Assert.Equal(OriginsTriggerKind.FirstPromotionTo, OriginsChapters.ByKey(chapter).Structured.Kind);
        }
        // The first two are the countries with day-one pioneers the founders could reach; the last are the farthest.
        Assert.Contains(order.Take(3), o => o.Country == "NED");
        Assert.Contains(order.Take(3), o => o.Country == "CAN");
        Assert.True(order.Select(o => OriginsChapters.ByKey(o.Chapter).EarliestMonth).SequenceEqual(
            order.Select(o => OriginsChapters.ByKey(o.Chapter).EarliestMonth).OrderBy(m => m)));
        Assert.True(order.Count(o => WorldAtlas.ByCode(o.Country)!.Continent == Region.Africa) >= 4);
        Assert.True(order.Select(o => WorldAtlas.ByCode(o.Country)!.Continent).Distinct().Count() >= 6);
    }

    // ------------------------------------------------------------------ narration
    [Fact]
    public void EveryChapter_HasNarration_WithKnownSlotsOnly_AndHonestNotes()
    {
        foreach (var c in OriginsChapters.All)
        {
            Assert.NotNull(c.ByWorld);
            Assert.False(string.IsNullOrWhiteSpace(c.Digest));
            Assert.InRange(Words(c.Digest), 4, 25);
            Assert.DoesNotContain("\n", c.Digest);
            foreach (var n in new[] { c.ByPlayer, c.ByRival, c.ByWorld })
            {
                if (n is null) continue;
                Assert.False(string.IsNullOrWhiteSpace(n.Headline), c.Key);
                Assert.InRange(Words(n.Body), 60, 120);
            }
            foreach (var text in AllText(c))
                foreach (Match m in Regex.Matches(text, @"\{([^}]*)\}"))
                    Assert.Contains(m.Groups[1].Value, KnownSlots);
            foreach (var text in AllText(c)) Assert.DoesNotContain("}", Regex.Replace(text, @"\{[A-Za-z]+\}", ""));
            // A missing variant must be explained.
            if (c.ByPlayer is null || c.ByRival is null) Assert.False(string.IsNullOrWhiteSpace(c.Note), $"{c.Key} omits a variant without saying why");
            // Player and rival variants come as a pair: the player-did-it text has a rival counterpart.
            Assert.Equal(c.ByPlayer is null, c.ByRival is null);
        }
        // Most chapters are contestable: they have all three variants.
        Assert.True(OriginsChapters.All.Count(c => c.ByPlayer is not null) >= 30);
    }

    [Fact]
    public void Narration_ReadsInThePeriod_NoAnachronismsByWindowYear()
    {
        var before1995 = new Regex(@"\b(internet|online|website|web ?site|e-?mail|browser)\b", RegexOptions.IgnoreCase);
        var before2005 = new Regex(@"\b(social media|viral|apps?|hashtags?|podcasts?|blogs?|smartphones?|tweets?|selfies?)\b", RegexOptions.IgnoreCase);
        var before2008 = new Regex(@"\b(streams?|streaming|streamers?)\b", RegexOptions.IgnoreCase);
        foreach (var c in OriginsChapters.All)
            foreach (var text in AllText(c))
            {
                if (c.EarliestYear < 1995) Assert.False(before1995.IsMatch(text), $"{c.Key}: '{text}'");
                if (c.EarliestYear < 2005) Assert.False(before2005.IsMatch(text), $"{c.Key}: '{text}'");
                if (c.EarliestYear < 2008) Assert.False(before2008.IsMatch(text), $"{c.Key}: '{text}'");
            }
        // The streaming chapters really are late.
        Assert.True(OriginsChapters.ByKey("inhouse-streaming").EarliestYear >= 2010);
        Assert.True(OriginsChapters.ByKey("major-streamer").EarliestYear >= 2010);
    }

    [Fact]
    public void Everything_IsFictional_NoRealOrganisationsOrPeopleNamed()
    {
        var banned = new Regex(@"\b(UFC|Pancrase|Zuffa|Strikeforce|Bellator|Invicta|Netflix|ESPN|Gracie|Rousey|Carano|Dana White|Octagon|Fedor|Pride FC|K-1|WWE|Ultimate Fighting)\b");
        var text = OriginsChapters.All.SelectMany(AllText)
            .Concat(OriginsBroadcasters.Media.SelectMany(m => new[] { m.Name, m.Tag, m.Lore }))
            .Concat(OriginsBroadcasters.Sponsors.SelectMany(s => new[] { s.Name, s.Tag, s.Lore }))
            .Concat(OriginsArrivals.All.SelectMany(a => new[] { a.Name, a.Lore, a.Niche ?? "", a.Lead ?? "" }));
        foreach (var t in text) Assert.False(banned.IsMatch(t), t);
    }

    [Fact]
    public void ChaptersAgreeWithTheDayOneCanon()
    {
        var world = OriginsChapters.ByKey("crucible-first-night").ByWorld.Body;
        Assert.Contains("Len Pastor", world);
        Assert.Contains("Pico Sports Pavilion", world);
        var two = OriginsChapters.ByKey("shinken-and-duelo-open").ByWorld.Body;
        Assert.Contains("Kosaku Hiyama", two);
        Assert.Contains("Eitoku Hall", two);
        Assert.Contains("Anibal Sampaio", two);
        Assert.Contains("Tijuca", two);
        Assert.Contains(OriginsPromotions.All.Single(p => p.Abbreviation == "CRU").Founder.Name.Split(' ')[1], world);
        // The first chapters sit on day one.
        Assert.Equal(0, OriginsChapters.ByKey("crucible-first-night").EarliestMonth);
        Assert.Equal(0, OriginsChapters.ByKey("shinken-and-duelo-open").EarliestMonth);
        // The closing chapter quotes the founder's question, from the same legal pad.
        Assert.Contains("Len Pastor", OriginsChapters.ByKey("the-sport-you-built").ByWorld.Body);
    }

    // ------------------------------------------------------------------ broadcasters and sponsors
    [Fact]
    public void Broadcasters_CoverEveryEra_WithLore_AndMatchTheirUnlockChapters()
    {
        var media = OriginsBroadcasters.Media;
        Assert.InRange(media.Count, 14, 20);
        Assert.InRange(OriginsBroadcasters.Sponsors.Count, 11, 16);
        foreach (var kind in Enum.GetValues<OriginsPartnerKind>()) Assert.Contains(media, m => m.Kind == kind);
        Assert.True(media.Count(m => m.Kind == OriginsPartnerKind.PpvCarrier) >= 2);
        Assert.Contains(media, m => m.Kind == OriginsPartnerKind.ForeignNetwork && m.HomeCountry == "JPN");
        Assert.Contains(media, m => m.Kind == OriginsPartnerKind.ForeignNetwork && m.HomeCountry == "BRA");
        for (int era = 1; era <= 6; era++)
        {
            Assert.Contains(media, m => m.Era == era);
        }

        foreach (var m in media)
        {
            Assert.InRange(m.Era, 1, 6);
            Assert.NotNull(WorldAtlas.ByCode(m.HomeCountry));
            Assert.False(string.IsNullOrWhiteSpace(m.Tag));
            Assert.True(m.Lore.Length >= 200, m.Name);
            Assert.InRange(m.EarliestMonth, 0, 360);
            if (m.Media is { } mk)
            {
                var unlock = OriginsChapters.All.Single(c => c.Unlocks.Contains("Media:" + mk));
                Assert.True(m.EarliestMonth >= unlock.EarliestMonth, $"{m.Name} appears before {unlock.Key} unlocks {mk}");
            }
        }
        foreach (var s in OriginsBroadcasters.Sponsors)
        {
            Assert.Contains(s.Type, OriginsCapabilities.SponsorKeys);
            Assert.InRange(s.Era, 1, 6);
            Assert.NotNull(WorldAtlas.ByCode(s.HomeCountry));
            Assert.True(s.Lore.Length >= 200, s.Name);
            var unlock = OriginsChapters.All.Single(c => c.Unlocks.Contains("Sponsor:" + s.Type));
            Assert.True(s.EarliestMonth >= unlock.EarliestMonth, $"{s.Name} appears before {unlock.Key} unlocks {s.Type}");
        }
        // Every sponsor type has at least one named sponsor.
        foreach (var type in OriginsCapabilities.SponsorKeys) Assert.Contains(OriginsBroadcasters.Sponsors, s => s.Type == type);
        // Every world-level media kind has at least one named partner.
        foreach (var key in OriginsCapabilities.MediaKeys) Assert.Contains(media, m => m.Media?.ToString() == key);
    }

    [Fact]
    public void NoNameCollisions_AmongTheTimelineContent_OrWithExistingDesignedContent()
    {
        var existingPromos = WorldBuilder.Options.DefaultPromotions.Select(s => Key(s.Name)).ToHashSet();
        var existingAbbrs = WorldBuilder.Options.DefaultPromotions.Select(s => s.Abbreviation.ToUpperInvariant()).ToHashSet();
        var existingFighters = DesignedRoster.All.Select(f => Key(f.First + " " + f.Last)).ToHashSet();
        var existingNicks = DesignedRoster.All.Where(f => f.Nickname is not null).Select(f => Key(f.Nickname!)).ToHashSet();
        var existingLast = DesignedRoster.All.Select(f => Key(f.Last)).ToHashSet();
        var pioneerNames = OriginsPioneers.All.Select(p => Key(p.FullName)).ToHashSet();
        var pioneerNicks = OriginsPioneers.All.Where(p => p.Fighter.Nickname is not null).Select(p => Key(p.Fighter.Nickname!)).ToHashSet();
        var pioneerLast = OriginsPioneers.All.Select(p => Key(p.Fighter.Last)).ToHashSet();

        // Media and sponsors: unique among themselves and against today's.
        var all = OriginsBroadcasters.Media.Select(m => m.Name).Concat(OriginsBroadcasters.Sponsors.Select(s => s.Name)).ToList();
        Assert.Equal(all.Count, all.Select(Key).Distinct().Count());
        foreach (var n in all)
        {
            Assert.DoesNotContain(Key(n), ExistingMedia.Select(Key));
            Assert.DoesNotContain(Key(n), ExistingSponsors.Select(Key));
            Assert.DoesNotContain(Key(n), existingPromos);
            Assert.DoesNotContain(Key(n), OriginsPromotions.All.Select(p => Key(p.Name)));
        }

        // Arrivals: unique keys and names, none colliding with the designed roster, the pioneers or any promotion.
        Assert.Equal(OriginsArrivals.All.Count, OriginsArrivals.All.Select(a => a.Key).Distinct().Count());
        Assert.Equal(OriginsArrivals.All.Count, OriginsArrivals.All.Select(a => Key(a.Name)).Distinct().Count());
        foreach (var a in OriginsArrivals.All)
        {
            Assert.DoesNotContain(Key(a.Name), existingPromos);
            Assert.DoesNotContain(Key(a.Name), all.Select(Key));
            Assert.DoesNotContain(Key(a.Name), existingFighters);
            Assert.DoesNotContain(Key(a.Name), pioneerNames);
            Assert.DoesNotContain(Key(a.Name), OriginsPromotions.All.Select(p => Key(p.Name)));
            if (a.Abbreviation is { } ab && a.Kind == OriginsArrivalKind.Promotion)
            {
                Assert.DoesNotContain(ab.ToUpperInvariant(), existingAbbrs);
                Assert.DoesNotContain(ab.ToUpperInvariant(), OriginsPromotions.All.Select(p => p.Abbreviation));
            }
            if (a.Pioneer is { } p)
            {
                // Surnames are keys of the side tables: no collision with the designed roster or the day-one pioneers.
                Assert.DoesNotContain(Key(p.Fighter.Last), existingLast);
                Assert.DoesNotContain(Key(p.Fighter.Last), pioneerLast);
                Assert.DoesNotContain(Key(p.Fighter.Nickname!), existingNicks);
                Assert.DoesNotContain(Key(p.Fighter.Nickname!), pioneerNicks);
            }
        }
        // Arrival nicknames are unique among themselves too.
        var nicks = OriginsArrivals.All.Where(a => a.Pioneer is not null).Select(a => Key(a.Pioneer!.Fighter.Nickname!)).ToList();
        Assert.Equal(nicks.Count, nicks.Distinct().Count());
        var lasts = OriginsArrivals.All.Where(a => a.Pioneer is not null).Select(a => Key(a.Pioneer!.Fighter.Last)).ToList();
        Assert.Equal(lasts.Count, lasts.Distinct().Count());
        // Founders and head coaches do not share a name with any designed owner, staffer or day-one founder.
        var people = DesignedStaff.Owners.Select(o => Key(o.Name)).Concat(DesignedStaff.Staff.Select(s => Key(s.Name)))
            .Concat(OriginsPromotions.Owners.Select(o => Key(o.Name))).ToHashSet();
        foreach (var a in OriginsArrivals.All.Where(a => a.Lead is not null)) Assert.DoesNotContain(Key(a.Lead!), people);
    }

    // ------------------------------------------------------------------ arrivals
    [Fact]
    public void Arrivals_AreAHandful_TiedToChapters_WithLore_AndRealPlaces()
    {
        var all = OriginsArrivals.All;
        Assert.InRange(all.Count, 6, 12);
        Assert.Contains(all, a => a.Kind == OriginsArrivalKind.Promotion && a.Chapter == "first-sanctioned-state");           // the giant of the Boom
        Assert.Contains(all, a => a.Kind == OriginsArrivalKind.Promotion && a.Chapter == "second-centre-arenas");             // the big-arena promotion
        Assert.Contains(all, a => a.Kind == OriginsArrivalKind.Promotion && a.Name.Contains("Bellona"));                      // the first all-women's promotion
        Assert.Contains(all, a => a.Kind == OriginsArrivalKind.Camp);
        Assert.InRange(all.Count(a => a.Kind == OriginsArrivalKind.Fighter), 3, 8);
        foreach (var a in all)
        {
            var chapter = OriginsChapters.ByKey(a.Chapter);
            Assert.InRange(a.EarliestMonth, chapter.EarliestMonth, chapter.LatestMonth);
            Assert.True(a.LatestMonth >= a.EarliestMonth, a.Key);
            Assert.True(a.LatestMonth >= chapter.LatestMonth - 12 || a.LatestMonth >= a.EarliestMonth + 24, a.Key);
            Assert.True(a.Lore.Length >= 300, a.Key);
            Assert.Matches("^arrival-[a-z0-9]+(-[a-z0-9]+)*$", a.Key);
            var city = WorldAtlas.Cities.SingleOrDefault(c => c.Name == a.City);
            Assert.NotNull(city);
            Assert.Equal(a.Country, city!.CountryCode);
            if (a.Kind != OriginsArrivalKind.Fighter) Assert.False(string.IsNullOrWhiteSpace(a.Lead), a.Key);
            if (a.Kind == OriginsArrivalKind.Promotion) { Assert.False(string.IsNullOrWhiteSpace(a.Abbreviation)); Assert.False(string.IsNullOrWhiteSpace(a.Niche)); }
            if (a.Kind == OriginsArrivalKind.Fighter) Assert.NotNull(a.Pioneer);
        }
        // Country scenes are awake when the arrival arrives (the home countries of the founders aside).
        var awake = OriginsChapters.SceneOrder.ToDictionary(o => o.Country, o => OriginsChapters.ByKey(o.Chapter).EarliestMonth);
        foreach (var a in all.Where(a => awake.ContainsKey(a.Country)))
            Assert.True(a.EarliestMonth >= awake[a.Country] - 12, $"{a.Name} arrives in {a.Country} long before its scene wakes");
    }

    [Fact]
    public void ArrivingFighters_UseThePioneerRowShape_AndFitTheirEra()
    {
        var fighters = OriginsArrivals.All.Where(a => a.Pioneer is not null).Select(a => (A: a, P: a.Pioneer!)).ToList();
        Assert.Contains(fighters, f => f.P.Fighter.Sex == Sex.Female);
        Assert.True(fighters.Count(f => f.P.Fighter.Sex == Sex.Female) >= 2);
        foreach (var (a, p) in fighters)
        {
            var f = p.Fighter;
            Assert.Equal(f.First + " " + f.Last, a.Name);
            Assert.Equal(a.Country, f.Country);
            Assert.Equal(WorldAtlas.ByCode(a.Country)!.Continent, f.Region);
            Assert.False(string.IsNullOrWhiteSpace(f.Bio));
            Assert.False(string.IsNullOrWhiteSpace(p.Hook));
            Assert.Equal(f.Bio, a.Lore);
            Assert.InRange(f.Age, 19, 35);
            Assert.InRange(f.Overall, 55, 80);
            Assert.InRange(f.Wins + f.Losses, 0, 8);
            Assert.Equal(OriginsPioneers.ClassFor(p.WeightKg), f.WeightClass);
            Assert.False(f.Champion);
            Assert.True(p.Skills.Count > 10);
            Assert.All(p.Skills.Values, v => Assert.InRange(v, 1, 95));
            int sentences = f.Bio!.Count(ch => ch == '.' || ch == '!' || ch == '?');
            Assert.InRange(sentences, 3, 7);
        }
        // The first women's star debuts after the first women's division, in a class the women's world actually runs.
        var star = fighters.Single(f => f.A.Chapter == "womens-first-star");
        Assert.Equal(Sex.Female, star.P.Fighter.Sex);
        Assert.True(star.A.EarliestMonth >= OriginsChapters.ByKey("womens-first-division").EarliestMonth);
        foreach (var f in fighters.Where(f => f.P.Fighter.Sex == Sex.Female))
            Assert.Contains(f.P.Fighter.WeightClass, new[] { WeightClass.Strawweight, WeightClass.Flyweight, WeightClass.Bantamweight });
        // Day-one orgs named in a row exist; later arrivals may name the women's promotion.
        var orgs = OriginsPromotions.All.Select(p => p.Abbreviation).Concat(OriginsArrivals.All.Where(a => a.Kind == OriginsArrivalKind.Promotion).Select(a => a.Abbreviation!)).ToHashSet();
        Assert.All(fighters.Where(f => f.P.Fighter.Org is not null), f => Assert.Contains(f.P.Fighter.Org!, orgs));
    }

    // ------------------------------------------------------------------ the guard
    [Fact]
    public void TodaysWorld_ReferencesNothingFromTheTimeline()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2026 });
        var arrivals = OriginsArrivals.All;

        var promoNames = world.Promotions.Values.Select(p => Key(p.Name)).ToHashSet();
        var promoAbbrs = world.Promotions.Values.Select(p => p.Abbreviation.ToUpperInvariant()).ToHashSet();
        foreach (var a in arrivals.Where(a => a.Kind == OriginsArrivalKind.Promotion))
        {
            Assert.DoesNotContain(Key(a.Name), promoNames);
            Assert.DoesNotContain(a.Abbreviation!.ToUpperInvariant(), promoAbbrs);
        }
        Assert.DoesNotContain(WorldBuilder.Options.DefaultPromotions, s => arrivals.Any(a => a.Kind == OriginsArrivalKind.Promotion && a.Abbreviation == s.Abbreviation));

        var fighterNames = world.Fighters.Values.Select(f => Key(f.FirstName + " " + f.LastName)).ToHashSet();
        foreach (var a in arrivals.Where(a => a.Pioneer is not null)) Assert.DoesNotContain(Key(a.Name), fighterNames);
        Assert.DoesNotContain(DesignedRoster.All, d => arrivals.Any(a => a.Pioneer?.Fighter == d));

        var camps = world.Camps.Values.Select(c => Key(c.Name)).ToHashSet();
        foreach (var a in arrivals.Where(a => a.Kind == OriginsArrivalKind.Camp)) Assert.DoesNotContain(Key(a.Name), camps);

        // No sponsor deal in today's world names an era sponsor, and no media partner is one of the era partners.
        var sponsorNames = OriginsBroadcasters.Sponsors.Select(s => Key(s.Name)).ToHashSet();
        foreach (var p in world.Promotions.Values) foreach (var d in p.SponsorDeals) Assert.DoesNotContain(Key(d.Partner), sponsorNames);
        var mediaNames = OriginsBroadcasters.Media.Select(m => Key(m.Name)).ToHashSet();
        Assert.DoesNotContain(world.Promotions.Values, p => p.Stream is not null && mediaNames.Contains(Key(p.Stream.Name)));

        // The chapter table has no way in: today's world starts in 2026, not 1993, and carries no chapter state.
        Assert.Equal(2026, world.StartDate.Year);
    }

    // ------------------------------------------------------------------ the owner's showcase
    /// <summary>Writes the showcase markdown only when ORIGINS_TIMELINE_PATH names a file; otherwise just checks it renders.</summary>
    [Fact]
    public void Showcase_RendersTheWholeTimeline()
    {
        string md = RenderShowcase();
        foreach (var c in OriginsChapters.All) Assert.Contains(c.Title, md);
        foreach (var m in OriginsBroadcasters.Media) Assert.Contains(m.Name, md);
        foreach (var s in OriginsBroadcasters.Sponsors) Assert.Contains(s.Name, md);
        foreach (var a in OriginsArrivals.All) Assert.Contains(a.Name, md);
        if (Environment.GetEnvironmentVariable("ORIGINS_TIMELINE_PATH") is { Length: > 0 } path)
            File.WriteAllText(path, md, new UTF8Encoding(false));
    }

    private static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
    private static string When(int month) => $"{Months[month % 12]} {1993 + month / 12}";

    private static string Describe(string cap) => cap;

    private static string RenderShowcase()
    {
        var eraNames = new[] { "", "The Spectacle", "The Backlash", "Two Worlds", "The Rules", "The Boom", "The Global Game" };
        var eraBlurbs = new[]
        {
            "",
            "Open weight, one-night tournaments, a single long round, no judges, style against style. Pay-per-view and tape sales are the only media.",
            "The press and the politicians turn on the sport. Shows are banned in places, carriers drop it and the money dries up. Promotions that adapt survive.",
            "A second centre of the sport grows abroad with big arenas. Cross-training camps appear and the specialists start losing to all-rounders.",
            "Unified rules and regulation spread. The sport becomes a sport: the full ladder of divisions, cable television, sponsors beyond the fringe.",
            "A breakout television moment makes it mainstream. Money floods in, new promotions and takeovers follow, and women's MMA finds its feet.",
            "Streaming, worldwide scenes, many promotions and a pyramid. The world converges on today's game.",
        };
        var sb = new StringBuilder();
        sb.AppendLine("# Origins: the timeline, 1993 to the modern world");
        sb.AppendLine();
        sb.AppendLine($"{OriginsChapters.All.Count} narrated chapters across six eras, plus {OriginsBroadcasters.Media.Count} era media partners, {OriginsBroadcasters.Sponsors.Count} era sponsors and {OriginsArrivals.All.Count} scheduled arrivals. Everyone and every company is invented; the geography is real. Nothing here is wired into any world yet.");
        sb.AppendLine();
        sb.AppendLine("How to read a chapter: the window is the earliest and latest the chapter can fire (every chapter fires by its latest date whether or not its condition was met, so the order is mostly fixed while the timing and the doer depend on the world and the player). \"Needs\" lists the chapters that must have happened first. \"By the player / By a rival / By the world\" are the three narrations: the player's promotion did it, another promotion did it, or it simply happened. The world version is shown in full, with {Year} filled in; the other two show their headlines (their slots are filled in by the engine: {Org}, {Founder}, {City}, {Country}, {Fighter}, {Partner}).");
        sb.AppendLine();
        sb.AppendLine("## Contents");
        sb.AppendLine();
        for (int era = 1; era <= 6; era++)
        {
            var ch = OriginsChapters.All.Where(c => c.Era == era).ToList();
            sb.AppendLine($"- Era {era}, {eraNames[era]} ({ch.Min(c => 1993 + c.EarliestMonth / 12)} to {ch.Max(c => 1993 + c.LatestMonth / 12)}): {ch.Count} chapters");
        }
        sb.AppendLine("- Country wake-up order");
        sb.AppendLine("- Era media partners and sponsors");
        sb.AppendLine("- Scheduled arrivals");
        sb.AppendLine();

        var byKey = OriginsChapters.All.ToDictionary(c => c.Key);
        for (int era = 1; era <= 6; era++)
        {
            var chapters = OriginsChapters.All.Where(c => c.Era == era).OrderBy(c => c.EarliestMonth).ThenBy(c => c.LatestMonth).ToList();
            sb.AppendLine($"## Era {era}: {eraNames[era]}");
            sb.AppendLine();
            sb.AppendLine(eraBlurbs[era]);
            sb.AppendLine();
            foreach (var c in chapters)
            {
                sb.AppendLine($"### {c.Title}");
                sb.AppendLine();
                sb.AppendLine($"`{c.Key}` | window {When(c.EarliestMonth)} to {When(c.LatestMonth)} | needs: {(c.Requires.Length == 0 ? "nothing (day one)" : string.Join(", ", c.Requires.Select(r => byKey[r].Title)))}");
                sb.AppendLine();
                sb.AppendLine($"- **Trigger:** {c.Trigger}  _[{c.Structured.Kind}: {c.Structured.Condition}{(c.Structured.Value > 0 ? " " + c.Structured.Value : "")}]_");
                sb.AppendLine($"- **Unlocks:** {(c.Unlocks.Length == 0 ? "nothing new" : string.Join(", ", c.Unlocks.Select(Describe)))}");
                foreach (var e in c.Effects) sb.AppendLine($"- **Effect:** {e}");
                sb.AppendLine($"- **Weekly paper:** {c.Digest}");
                if (c.ByPlayer is not null) sb.AppendLine($"- **If the player does it:** {c.ByPlayer.Headline}");
                if (c.ByRival is not null) sb.AppendLine($"- **If a rival does it:** {c.ByRival.Headline}");
                if (c.Note.Length > 0) sb.AppendLine($"- _Note: {c.Note}_");
                sb.AppendLine();
                sb.AppendLine($"> **{c.ByWorld.Headline}**");
                sb.AppendLine(">");
                sb.AppendLine($"> {c.ByWorld.Body.Replace("{Year}", (1993 + (c.EarliestMonth + c.LatestMonth) / 24).ToString())}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Country wake-up order");
        sb.AppendLine();
        sb.AppendLine($"{OriginsChapters.SceneOrder.Count} countries wake after the three founding ones (the United States, Japan and Brazil). Dormant scenes host no shows and produce no local talent until their chapter fires.");
        sb.AppendLine();
        sb.AppendLine("| # | Country | Wakes with | Window starts |");
        sb.AppendLine("| --- | --- | --- | --- |");
        int n = 1;
        foreach (var (chapter, code) in OriginsChapters.SceneOrder)
            sb.AppendLine($"| {n++} | {WorldAtlas.ByCode(code)!.Name} ({code}) | {byKey[chapter].Title} | {When(byKey[chapter].EarliestMonth)} |");
        sb.AppendLine();

        sb.AppendLine("## Era media partners");
        sb.AppendLine();
        foreach (var m in OriginsBroadcasters.Media.OrderBy(m => m.EarliestMonth))
        {
            sb.AppendLine($"### {m.Name}");
            sb.AppendLine();
            sb.AppendLine($"_{m.Tag}_ | era {m.Era}, from {When(m.EarliestMonth)} | {WorldAtlas.ByCode(m.HomeCountry)!.Name} | {m.Kind}{(m.Media is { } k ? " (media: " + k + ")" : "")}");
            sb.AppendLine();
            sb.AppendLine(m.Lore);
            sb.AppendLine();
        }
        sb.AppendLine("## Era sponsors");
        sb.AppendLine();
        foreach (var s in OriginsBroadcasters.Sponsors.OrderBy(s => s.EarliestMonth))
        {
            sb.AppendLine($"### {s.Name}");
            sb.AppendLine();
            sb.AppendLine($"_{s.Tag}_ | era {s.Era}, from {When(s.EarliestMonth)} | {WorldAtlas.ByCode(s.HomeCountry)!.Name} | sponsor type: {s.Type}");
            sb.AppendLine();
            sb.AppendLine(s.Lore);
            sb.AppendLine();
        }

        sb.AppendLine("## Scheduled arrivals");
        sb.AppendLine();
        foreach (var a in OriginsArrivals.All.OrderBy(a => a.EarliestMonth))
        {
            sb.AppendLine($"### {a.Name}");
            sb.AppendLine();
            sb.AppendLine($"{a.Kind} | from {When(a.EarliestMonth)} | {a.City}, {WorldAtlas.ByCode(a.Country)!.Name} | tied to: {byKey[a.Chapter].Title}");
            sb.AppendLine();
            if (a.Pioneer is { } p)
            {
                var f = p.Fighter;
                string nickText = f.Nickname is null ? "" : "(\"" + f.Nickname + "\") ";
                string orgText = f.Org is null ? "" : ", " + f.Org;
                sb.AppendLine($"_{p.Hook}_ {nickText}| {f.Sex}, {f.Age}, {p.Discipline}, {p.HeightCm} cm, {p.WeightKg} kg ({f.WeightClass}), record {f.Wins}-{f.Losses}{orgText}");
            }
            else
            {
                sb.AppendLine($"_{a.Niche ?? ""}_{(a.Lead is { } l ? $" | {(a.Kind == OriginsArrivalKind.Camp ? "head coach" : "founder")}: {l}" : "")}{(a.Abbreviation is { } ab ? $" | {ab}" : "")}");
            }
            sb.AppendLine();
            sb.AppendLine(a.Lore);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
