using Microsoft.Data.Sqlite;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Persistence;

namespace MmaManager.Tests;

public class PersistenceTests
{
    private static string TempPath(string tag) =>
        Path.Combine(Path.GetTempPath(), $"mma_{tag}_{Guid.NewGuid():N}.db");

    private static World BuildSeasoned(int seed = 99, int weeks = 30, bool exerciseEveryField = false)
    {
        // A world with a season already run, so events/fights/outcomes/stats,
        // injuries, retirements and rankings are all populated.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = (ulong)seed });
        new WorldTick().AdvanceWeeks(world, weeks);

        // The owner's desk state (the Notes app) rides in the save too.
        world.OwnerNotes = "sign a heavyweight before summer\nwatch the CK strap";
        world.Shortlist.Add(world.Fighters.Keys.First());
        world.Shortlist.Add(world.Fighters.Keys.Skip(5).First());

        if (exerciseEveryField)
        {
            // Make sure the desk table is exercised even if the sim raised no decision (Id must be unique).
            world.Decisions.Add(new Decision
            {
                Id = world.Decisions.Select(d => d.Id).DefaultIfEmpty(0).Max() + 1, Kind = DecisionKind.TitleShotRequest,
                Created = world.CurrentDate, Deadline = world.CurrentDate.AddDays(7),
            });
            // Likewise a pending media offer: whether one happens to be on the table
            // on the save date is seed noise (none was, at week 30 of seed 99, once
            // the small promotions' rosters went local).
            if (!world.Promotions.Values.Any(p => p.Offers.Any(o => o.Media is not null && o.Requirements.Count > 0)))
                world.Promotions.Values.OrderBy(p => p.Id.Value).First().Offers.Add(new DealOffer
                {
                    Media = MediaKind.SpecialtyCable, Partner = "Sample Network", FeePerEvent = new Money(25_000m), Term = 12,
                    Requirements = { new DealRequirement(DealRequirementKind.MinAttendance, 2_000) },
                    OfferedOn = world.CurrentDate, ExpiresOn = world.CurrentDate.AddDays(30),
                });
            // Talks the player left mid-way (one per fighter and promotion).
            world.Talks.Add(new TalksMemory
            {
                Fighter = world.Fighters.Keys.Skip(3).First(), Promotion = world.Promotions.Keys.First(),
                DemandFrac = 0.913, Mood = 51, Patience = 2, Round = 3,
                SouredUntil = world.CurrentDate.AddDays(9), LastTalked = world.CurrentDate.AddDays(-12),
            });
            // A rival's offer on the books (the sample fill gives every field a value).
            world.RivalOffers.Add(new RivalOffer
            {
                Id = 1, Fighter = world.Fighters.Keys.Skip(4).First(), From = world.Promotions.Keys.First(),
                Holder = world.Promotions.Keys.Skip(1).First(), Made = world.CurrentDate.AddDays(-3),
                Kind = RivalOfferKind.FreeAgent,
                // The owner's final offer in writing (a courted free agent's offer): every lever set.
                Final = new ContractOffer
                {
                    Fighter = world.Fighters.Keys.Skip(4).First(), Promotion = world.Promotions.Keys.Skip(1).First(),
                    PursePerFight = new Money(12_500m), WinBonus = new Money(6_000m), SigningBonus = new Money(4_000m),
                    Fights = 3, PpvPoints = 2, PerformanceBonus = new Money(2_500m), Exclusive = false, ChampionsClause = true,
                },
            });

            PersistenceGraph.FillUnpopulated(world);
        }
        return world;
    }

    private static void AssertSame(World expected, World actual)
    {
        var diffs = PersistenceGraph.Diff(expected, actual);
        Assert.True(diffs.Count == 0, "World changed across the save:\n" + PersistenceGraph.Describe(diffs));
    }

    // ---- round trip -------------------------------------------------------

    [Fact]
    public void SaveThenLoad_RoundTripsTheWholeWorld()
    {
        var world = BuildSeasoned(exerciseEveryField: true);
        string path = TempPath("roundtrip");
        try
        {
            SaveGame.Save(world, path);
            var loaded = SaveGame.Load(path);

            // Reflection-driven: every public property of every model type is compared,
            // except the ones listed (with reasons) in PersistenceGraph.NotSaved.
            AssertSame(world, loaded);

            // Id generation must resume above existing ids after a load.
            var fresh = loaded.NewFighterId();
            Assert.True(fresh.Value > loaded.Fighters.Keys.Max(k => k.Value));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NewlyBuiltWorld_RoundTrips()
    {
        var world = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 3, RosterSize = 60 });
        string path = TempPath("new");
        try
        {
            SaveGame.Save(world, path);
            AssertSame(world, SaveGame.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NotSavedList_OnlyNamesRealProperties()
    {
        foreach (var key in PersistenceGraph.NotSaved.Keys)
            Assert.True(PersistenceGraph.DeclaresProperty(key), $"NotSaved lists '{key}' but no such property exists; remove it.");
    }

    // ---- coverage: the comparison is only worth something if the data is non-default ----

    /// <summary>
    /// Properties the seasoned test world never populates, and why that is acceptable.
    /// Anything else that stays at its default means a save bug could hide there, so the
    /// test fails and the test world (or this list) must be fixed.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NeverPopulated = new Dictionary<string, string>
    {
        // Filled in as the suite finds them; every entry needs a reason.
    };

    [Fact]
    public void TestWorld_PopulatesEveryModelProperty()
    {
        var world = BuildSeasoned(exerciseEveryField: true);
        var populated = PersistenceGraph.Populated(world);
        var missing = PersistenceGraph.Declared(typeof(World))
            .Where(k => !populated.GetValueOrDefault(k))
            .Where(k => !PersistenceGraph.NotSaved.ContainsKey(k) && !NeverPopulated.ContainsKey(k))
            .OrderBy(k => k)
            .ToList();
        Assert.True(missing.Count == 0,
            "These properties are default everywhere in the test world, so the round trip proves nothing about them:\n"
            + PersistenceGraph.Describe(missing));
    }

    [Fact]
    public void EverySchemaColumn_IsExercisedBySomeRow()
    {
        var world = BuildSeasoned(exerciseEveryField: true);
        string path = TempPath("columns");
        try
        {
            SaveGame.Save(world, path);
            using var conn = Open(path);
            var unused = new List<string>();
            foreach (var table in Tables(conn))
            foreach (var col in Columns(conn, table))
            {
                if (col == "ordinal" || ColumnsNeverExercised.Contains($"{table}.{col}")) continue;   // ordinals start at 0
                using var cmd = conn.CreateCommand();
                // "Exercised" = at least one value that is not NULL, 0, 0.0, '' or the money zero '0'.
                cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE \"{col}\" IS NOT NULL AND \"{col}\" <> 0 AND \"{col}\" <> '' AND \"{col}\" <> '0'";
                if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) unused.Add($"{table}.{col}");
            }
            Assert.True(unused.Count == 0,
                "Schema columns that hold nothing but defaults in the test world (the round trip cannot see a bug there):\n"
                + PersistenceGraph.Describe(unused));
        }
        finally { File.Delete(path); }
    }

    /// <summary>Columns the seasoned world legitimately leaves empty. Needs a reason per entry.</summary>
    private static readonly HashSet<string> ColumnsNeverExercised = new()
    {
        // Filled in as the suite finds them.
    };

    [Fact]
    public void EveryWorldDictionary_KeepsItsEnumerationOrderAcrossTheSave()
    {
        // The sim iterates these dictionaries, so the save must keep their enumeration order. Try to
        // scramble EVERY dictionary on World (remove the first and last, add them back newest-first).
        // A plain Dictionary (GlobalRankings) reuses the freed slots and really is scrambled, so reading
        // that table in key order would fail here. An EntityMap (the entity collections) refuses to be
        // scrambled: it stays in id order, before and after the load.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var dicts = typeof(World).GetProperties()
            .Where(p => typeof(System.Collections.IDictionary).IsAssignableFrom(p.PropertyType) && p.Name != nameof(World.IdCounters))
            .ToList();
        Assert.True(dicts.Count >= 9, "expected the entity dictionaries plus global rankings");

        foreach (var p in dicts)
        {
            var d = (System.Collections.IDictionary)p.GetValue(world)!;
            Assert.True(d.Count >= 2, $"{p.Name} needs at least two entries for this test");
            var keys = d.Keys.Cast<object>().ToList();
            object firstKey = keys[0], firstVal = d[firstKey]!, lastKey = keys[^1], lastVal = d[lastKey]!;
            d.Remove(lastKey);
            d.Remove(firstKey);          // freed last, so reused first
            d.Add(lastKey, lastVal);
            d.Add(firstKey, firstVal);
            if (p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(EntityMap<,>))
                Assert.True(Equals(d.Keys.Cast<object>().First(), firstKey), $"{p.Name} must stay in id order after removals");
            else
                Assert.True(Equals(d.Keys.Cast<object>().First(), lastKey), $"premise: {p.Name} should now enumerate its newest key first");
        }

        var loaded = RoundTrip(world);
        foreach (var p in dicts)
        {
            var before = ((System.Collections.IDictionary)p.GetValue(world)!).Keys.Cast<object>().ToList();
            var after = ((System.Collections.IDictionary)p.GetValue(loaded)!).Keys.Cast<object>().ToList();
            Assert.True(before.SequenceEqual(after), $"{p.Name} enumerates in a different order after load");
        }
    }

    [Fact]
    public void FailedFirstSave_LeavesNoFileAtAll_AndTheErrorIsTheRealOne()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mma_nofile_{Guid.NewGuid():N}");
        string path = Path.Combine(dir, "career.db");
        try
        {
            var bad = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 12, RosterSize = 40 });
            bad.Fighters.Values.Last().FirstName = null!;
            Assert.Throws<SqliteException>(() => SaveGame.Save(bad, path));   // the writer's own error, not swallowed
            Assert.Empty(Directory.GetFiles(dir));                              // no empty target, no temp
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_OfAMissingEmptyOrForeignFile_FailsClearlyAndCreatesNothing()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mma_badload_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string missing = Path.Combine(dir, "missing.db");
            Assert.Throws<FileNotFoundException>(() => SaveGame.Load(missing));
            Assert.False(File.Exists(missing));

            string empty = Path.Combine(dir, "empty.db");
            File.WriteAllBytes(empty, Array.Empty<byte>());
            Assert.Throws<InvalidDataException>(() => SaveGame.Load(empty));

            string foreign = Path.Combine(dir, "foreign.db");
            File.WriteAllText(foreign, "this is not a database, just text that is long enough to look like a file");
            Assert.Throws<InvalidDataException>(() => SaveGame.Load(foreign));

            string noMeta = Path.Combine(dir, "nometa.db");
            using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = noMeta, Pooling = false }.ToString()))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE TABLE other (x INTEGER)";
                cmd.ExecuteNonQuery();
            }
            Assert.Throws<InvalidDataException>(() => SaveGame.Load(noMeta));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---- swap hardening and stale temp files ------------------------------

    [Fact]
    public void WhenTheSwapLosesTheTargetAfterDeletingIt_TheNewSaveIsPutInPlace()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mma_swap_{Guid.NewGuid():N}");
        string path = Path.Combine(dir, "career.db");
        try
        {
            var a = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 31, RosterSize = 40 });
            var b = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 32, RosterSize = 40 });
            SaveGame.Save(a, path);

            // Simulate ReplaceFile error 1176: the target is already gone and the swap throws.
            SaveGame.SwapFile = (tmp, full) => { File.Delete(full); throw new IOException("unable to move the replacement file"); };
            try { SaveGame.Save(b, path); }
            finally { SaveGame.SwapFile = null; }

            AssertSame(b, SaveGame.Load(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Save_SweepsStaleTempFilesOfItsOwnTargetOnly()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mma_sweep_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "career.db");
        string stale = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string otherTarget = Path.Combine(dir, "other.db") + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string lookalike = path + ".notaguid.tmp";
        string unrelated = Path.Combine(dir, "career.db.bak");
        try
        {
            foreach (var f in new[] { stale, otherTarget, lookalike, unrelated }) File.WriteAllText(f, "x");
            SaveGame.Save(new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 41, RosterSize = 40 }), path);

            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(otherTarget) && File.Exists(lookalike) && File.Exists(unrelated));
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---- save -> load -> save -------------------------------------------

    [Fact]
    public void SaveLoadSave_ProducesTheSameDatabase()
    {
        var world = BuildSeasoned(exerciseEveryField: true);
        string p1 = TempPath("a"), p2 = TempPath("b");
        try
        {
            SaveGame.Save(world, p1);
            SaveGame.Save(SaveGame.Load(p1), p2);
            Assert.Equal(DumpDatabase(p1), DumpDatabase(p2));
        }
        finally { File.Delete(p1); File.Delete(p2); }
    }

    // ---- loading must not change behaviour --------------------------------

    // Two seeds. The second saves in December, after the month-end recompute that entered
    // December and before the year rolls over, so the year-end awards (which read
    // PoundForPound before the next recompute) read the SAVED rankings after the load.
    [Theory]
    [InlineData(7, 20, 20)]
    [InlineData(13, 50, 25)]
    public void LoadedWorld_AdvancesExactlyLikeAWorldThatWasNeverSaved(int seed, int before, int after)
    {
        var straight = BuildSeasoned(seed: seed, weeks: 0);
        new WorldTick().AdvanceWeeks(straight, before + after);

        var split = BuildSeasoned(seed: seed, weeks: 0);
        new WorldTick().AdvanceWeeks(split, before);
        if (seed == 13) Assert.Equal(12, split.CurrentDate.Month);   // the premise of this case
        var resumed = RoundTrip(split);
        new WorldTick().AdvanceWeeks(resumed, after);
        if (seed == 13) Assert.True(resumed.CurrentDate.Year > split.CurrentDate.Year, "the run must cross the year end");

        var diffs = PersistenceGraph.Diff(straight, resumed, max: 60);
        Assert.True(diffs.Count == 0,
            "A saved-and-loaded world diverged from the same world run straight through:\n" + PersistenceGraph.Describe(diffs));
    }

    /// <summary>
    /// The player's deals end (Contracts.DealWindow): a world saved two weeks into a
    /// fighter's re-sign window, with contract talks left mid-way, loads into the same
    /// window and the same remembered talks, and runs on through the window's close
    /// exactly like the world that was never saved.
    /// </summary>
    [Fact]
    public void ASaveMidWindow_LoadsIntoTheSameWindow_AndRunsOnLikeANeverSavedWorld()
    {
        (World W, FighterId Out) Setup()
        {
            var w = PlayerWorld(21);
            var promo = w.Get(w.PlayerPromotion)!;
            var f = w.RosterOf(promo.Id).OrderBy(x => x.Id.Value).First();
            f.Contract!.FightsRemaining = 0;
            MmaManager.Core.Sim.Contracts.DealWindow.Open(w, f, w.CurrentDate);

            var fa = w.FreeAgents().Where(x => x.Status == FighterStatus.Active).OrderBy(x => x.Id.Value).First();
            var exp = new MmaManager.Core.Sim.Contracts.FighterValuation().Evaluate(w, fa);
            var talks = MmaManager.Core.Sim.Contracts.NegotiationSession.Resume(w, fa, promo, exp);
            var offer = talks.TheirTermsAsOffer(true, false);
            offer.PursePerFight = new Money(Math.Round(offer.PursePerFight.Amount * 0.85m));
            talks.Table(offer);
            talks.WalkAway();
            talks.Remember(w.CurrentDate);

            new WorldTick().AdvanceWeeks(w, 2);
            Assert.True(MmaManager.Core.Sim.Contracts.DealWindow.InWindow(w, f), "precondition: saved mid-window");
            return (w, f.Id);
        }

        var (straight, outId) = Setup();
        var (split, _) = Setup();
        var resumed = RoundTrip(split);
        AssertSame(split, resumed);
        Assert.NotNull(resumed.Get(outId)!.Contract!.WindowEndsOn);
        Assert.NotEmpty(resumed.Talks);

        new WorldTick().AdvanceWeeks(straight, 4);
        new WorldTick().AdvanceWeeks(resumed, 4);
        Assert.NotEqual(straight.PlayerPromotion, straight.Get(outId)!.Contract?.Promotion);   // the window closed on him
        var diffs = PersistenceGraph.Diff(straight, resumed, max: 60);
        Assert.True(diffs.Count == 0,
            "A world saved mid-window diverged from the same world run straight through:\n" + PersistenceGraph.Describe(diffs));
    }

    /// <summary>
    /// Rival offers (Contracts.OfferMarket): a world saved a week into a live offer for one
    /// of the player's fighters — on the Desk, its deadline still ahead — loads with the same
    /// offer and the same call on the Desk, and runs on past the deadline (silence lets him
    /// go) exactly like the world that was never saved.
    /// </summary>
    [Fact]
    public void ASaveWithALiveRivalOffer_MidDeadline_LoadsTheSameOffer_AndRunsOnLikeANeverSavedWorld()
    {
        (World W, FighterId Courted) Setup()
        {
            var w = PlayerWorld(21);
            // The first of the roster a bigger promotion would make an offer for, once he is a proven name.
            Fighter? f = null;
            foreach (var x in w.RosterOf(w.PlayerPromotion).Where(x => x.Status == FighterStatus.Active).OrderBy(x => x.Id.Value).ToList())
            {
                x.Striking.PunchPower = x.Striking.PunchTechnique = x.Striking.StrikingDefense = x.Striking.Footwork = x.Striking.KickTechnique = 72;
                x.Grappling.TakedownOffense = x.Grappling.TakedownDefense = x.Grappling.TopControl = x.Grappling.Scrambling = 72;
                x.Grappling.SubmissionOffense = x.Grappling.SubmissionDefense = 72;
                x.Physical.Cardio = x.Physical.Speed = x.Physical.Chin = x.Physical.Strength = 72;
                x.Mental.FightIQ = x.Mental.Composure = x.Mental.Heart = 72;
                x.Record.Wins = 14; x.Record.Losses = 2; x.Record.WinsByKo = 7; x.Record.WinsByDecision = 7;
                x.Record.CurrentStreak = 4;
                x.Intangibles.Popularity = 60;
                x.Loyalty = 15;
                x.Disposition = Disposition.Warhorse;
                x.Contract!.FightsRemaining = 1;
                if (MmaManager.Core.Sim.Contracts.OfferMarket.Call(w, x, w.CurrentDate) is not null) { f = x; break; }
                x.Contract.FightsRemaining = 3;
            }
            Assert.NotNull(f);
            new WorldTick().AdvanceWeeks(w, 1);
            Assert.Equal(RivalOfferState.Pending, w.RivalOffers.Single(o => o.Fighter == f.Id).State);   // mid-deadline
            return (w, f.Id);
        }

        var (straight, courted) = Setup();
        var (split, _) = Setup();
        var resumed = RoundTrip(split);
        AssertSame(split, resumed);
        Assert.Contains(resumed.RivalOffers, o => o.Fighter == courted && o.Live);
        Assert.Contains(resumed.Decisions, d => d.Kind == DecisionKind.RivalOffer && d.Fighter == courted);

        new WorldTick().AdvanceWeeks(straight, 4);
        new WorldTick().AdvanceWeeks(resumed, 4);
        Assert.NotEqual(RivalOfferState.Pending, straight.RivalOffers.Single(o => o.Fighter == courted).State);   // the deadline passed
        var diffs = PersistenceGraph.Diff(straight, resumed, max: 60);
        Assert.True(diffs.Count == 0,
            "A world saved mid-offer diverged from the same world run straight through:\n" + PersistenceGraph.Describe(diffs));
    }

    /// <summary>
    /// Free-agent offers (contracts stage 7): a world saved with a rival's offer standing for a
    /// free agent the player courts, and the owner's final offer in writing on it, loads with the
    /// same offer and final offer and runs on (the rival's answer, the deadline) exactly like the
    /// world that was never saved.
    /// </summary>
    [Fact]
    public void ASaveWithAFreeAgentOffer_AndAFinalOfferInWriting_RunsOnLikeANeverSavedWorld()
    {
        (World W, FighterId Courted) Setup()
        {
            var w = PlayerWorld(21);
            var promo = w.Get(w.PlayerPromotion)!;
            promo.Balance = new Money(5_000_000m);
            RivalOffer? o = null;
            for (int week = 0; week < 10 && o is null; week++)
            {
                FreeAgentOfferTests.CourtEveryone(w);
                new WorldTick().AdvanceWeek(w);
                o = w.RivalOffers.FirstOrDefault(x => x.Kind == RivalOfferKind.FreeAgent && x.State == RivalOfferState.Pending);
            }
            Assert.NotNull(o);
            var f = w.Get(o.Fighter)!;
            Assert.Equal(MmaManager.Core.Sim.Contracts.FinalOfferResult.Lodged,
                MmaManager.Core.Sim.Contracts.OfferMarket.LodgeFinal(w, f, promo, MmaManager.Core.Sim.Contracts.OfferMarket.MatchTerms(w, o)));
            return (w, f.Id);
        }

        var (straight, courted) = Setup();
        var (split, _) = Setup();
        var resumed = RoundTrip(split);
        AssertSame(split, resumed);
        var live = resumed.RivalOffers.Single(o => o.Fighter == courted && o.Live);
        Assert.Equal(RivalOfferKind.FreeAgent, live.Kind);
        Assert.NotNull(live.Final);

        new WorldTick().AdvanceWeeks(straight, 4);
        new WorldTick().AdvanceWeeks(resumed, 4);
        Assert.False(straight.RivalOffers.Single(o => o.Fighter == courted).Live);   // answered and settled
        var diffs = PersistenceGraph.Diff(straight, resumed, max: 60);
        Assert.True(diffs.Count == 0,
            "A world saved mid free-agent offer diverged from the same world run straight through:\n" + PersistenceGraph.Describe(diffs));
    }

    private static World RoundTrip(World w)
    {
        string path = TempPath("rt");
        try { SaveGame.Save(w, path); return SaveGame.Load(path); }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// The player's own paths mint ids and then free them: book an event with local bouts,
    /// cancel it, drop a single bout. Fight seeds, tick rolls and local fighters all key on
    /// ids, so a loaded world must keep handing out fresh ids, not reuse the freed ones.
    /// </summary>
    private static World PlayerWorld(ulong seed, bool holeInTheMiddle = false)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed,
            Founding = new WorldBuilder.FoundingSpec
            {
                Prestige = 15,
                Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight },
            },
        });
        new WorldTick().AdvanceWeeks(world, 6);

        var promo = world.Get(world.PlayerPromotion)!;
        Fighter Hire()
        {
            var pick = MmaManager.Core.Sim.Contracts.SigningAdvisor.Suggest(world, promo)!;
            var exp = new MmaManager.Core.Sim.Contracts.FighterValuation().Evaluate(world, pick.Fighter);
            MmaManager.Core.Sim.Contracts.ContractLifecycle.Sign(world, pick.Fighter, new ContractOffer
            {
                Fighter = pick.Fighter.Id, Promotion = promo.Id,
                PursePerFight = exp.AskingPurse, WinBonus = exp.AskingWinBonus, Fights = exp.DesiredFights,
            }, world.CurrentDate);
            return pick.Fighter;
        }
        var first = Hire();
        var second = Hire();
        var third = Hire();

        var booking = new PlayerBooking();
        // Optionally an OLD event that is cancelled at the end: its dictionary slot is then free in a
        // never-saved world but absent from a loaded one.
        var early = holeInTheMiddle ? booking.CreateEvent(world, world.CurrentDate.AddDays(14)) : null;
        if (early is not null) Assert.True(booking.AddLocalBout(world, early, third.Id, PrimaryStyle.Grappler, 3, out _));
        // An event that stays, with a local bout added, removed again and added anew.
        var kept = booking.CreateEvent(world, world.CurrentDate.AddDays(28));
        Assert.True(booking.AddLocalBout(world, kept, first.Id, PrimaryStyle.Wrestler, 3, out var err), err);
        booking.RemoveBout(world, kept, kept.Card[0].Id);
        Assert.True(booking.AddLocalBout(world, kept, first.Id, PrimaryStyle.Brawler, 3, out err), err);
        // Then the newest event, with a local bout, cancelled LAST: frees the highest event,
        // fight and fighter ids, which a counter derived from what survives would hand out again.
        var doomed = booking.CreateEvent(world, world.CurrentDate.AddDays(21));
        Assert.True(booking.AddLocalBout(world, doomed, second.Id, PrimaryStyle.Striker, 3, out err), err);
        booking.CancelEvent(world, doomed);
        if (early is not null) booking.CancelEvent(world, early);
        return world;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlayerPaths_CancelledEventsAndReleasedLocals_DoNotChangeWhatTheLoadedWorldDoes(bool holeInTheMiddle)
    {
        var straight = PlayerWorld(21, holeInTheMiddle);
        var split = PlayerWorld(21, holeInTheMiddle);
        var resumed = RoundTrip(split);

        // The next ids minted must be the ones a never-saved world would mint.
        Assert.Equal(split.NewEventId(), resumed.NewEventId());
        Assert.Equal(split.NewFightId(), resumed.NewFightId());
        Assert.Equal(split.NewFighterId(), resumed.NewFighterId());
        Assert.Equal(split.NewTitleId(), resumed.NewTitleId());
        // (the straight world was only built to prove the setup is itself deterministic)
        Assert.Equal(straight.IdCounters, PlayerWorld(21, holeInTheMiddle).IdCounters);

        // Mint the same ids in the straight world so the compare below is like for like.
        straight.NewEventId(); straight.NewFightId(); straight.NewFighterId(); straight.NewTitleId();

        new WorldTick().AdvanceWeeks(straight, 12);
        new WorldTick().AdvanceWeeks(resumed, 12);
        // A slot freed in the MIDDLE of the world's collections must not change their enumeration order
        // (World's EntityMap keeps id order), so the compare is strict, key order included.
        var diffs = PersistenceGraph.Diff(straight, resumed, max: 60);
        Assert.True(diffs.Count == 0,
            "After the player's cancel/remove paths a loaded world diverged from a never-saved one:\n" + PersistenceGraph.Describe(diffs));
    }

    // After PlayerBooking.CancelEvent / ReleaseLocals remove a non-newest entry, a plain Dictionary reused
    // the freed slot while a loaded one was compact, so the two enumerated differently. World's entity
    // collections are EntityMaps now: always id order, so strict key-order equality holds.
    [Fact]
    public void PlayerPaths_WithAHoleInTheMiddle_EnumerateInTheSameOrderAsANeverSavedWorld()
    {
        var straight = PlayerWorld(21, holeInTheMiddle: true);
        var resumed = RoundTrip(PlayerWorld(21, holeInTheMiddle: true));
        straight.NewEventId(); straight.NewFightId(); straight.NewFighterId(); straight.NewTitleId();
        resumed.NewEventId(); resumed.NewFightId(); resumed.NewFighterId(); resumed.NewTitleId();
        new WorldTick().AdvanceWeeks(straight, 12);
        new WorldTick().AdvanceWeeks(resumed, 12);
        Assert.Empty(PersistenceGraph.Diff(straight, resumed, max: 20));
    }

    // ---- saving is safe ---------------------------------------------------

    [Fact]
    public void FailedSave_LeavesThePreviousFileIntactAndNoTempFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mma_safe_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "career.db");
        try
        {
            var good = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 11, RosterSize = 40 });
            SaveGame.Save(good, path);
            byte[] before = File.ReadAllBytes(path);

            // A second world that cannot be written: a NOT NULL column gets a null.
            var bad = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 12, RosterSize = 40 });
            bad.Fighters.Values.Last().FirstName = null!;
            Assert.ThrowsAny<Exception>(() => SaveGame.Save(bad, path));

            Assert.Equal(before, File.ReadAllBytes(path));
            AssertSame(good, SaveGame.Load(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Save_ReplacesAnExistingFile_AndReleasesTheHandle()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mma_replace_{Guid.NewGuid():N}");
        string path = Path.Combine(dir, "career.db");
        try
        {
            var a = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 21, RosterSize = 40 });
            var b = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 22, RosterSize = 40 });
            SaveGame.Save(a, path);
            SaveGame.Save(b, path);
            AssertSame(b, SaveGame.Load(path));
            Assert.Single(Directory.GetFiles(dir));
            File.Delete(path);                       // would throw if a connection still held the file
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    // ---- helpers ----------------------------------------------------------

    private static SqliteConnection Open(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        return c;
    }

    private static List<string> Tables(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    private static List<string> Columns(SqliteConnection c, string table)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    /// <summary>Every row of every table, in physical (insertion) order, as text.</summary>
    private static List<string> DumpDatabase(string path)
    {
        using var conn = Open(path);
        var lines = new List<string>();
        foreach (var table in Tables(conn))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{table}\" ORDER BY rowid";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var cells = new string[r.FieldCount];
                for (int i = 0; i < r.FieldCount; i++) cells[i] = r.IsDBNull(i) ? "NULL" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!;
                lines.Add(table + ": " + string.Join("|", cells));
            }
        }
        return lines;
    }
}
