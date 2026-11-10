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
            // The sim has not raised a desk decision in 30 weeks; add one so the table is tested.
            world.Decisions.Add(new Decision
            {
                Id = 1, Kind = DecisionKind.TitleShotRequest,
                Created = world.CurrentDate, Deadline = world.CurrentDate.AddDays(7),
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
    public void DictionaryEnumerationOrder_SurvivesTheSave()
    {
        // The sim iterates these dictionaries. After a removal a new entry reuses the
        // freed slot, so enumeration order stops matching id order; the save must keep it.
        var world = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 5, RosterSize = 40 });
        var first = world.Fighters.Values.First();
        world.Fighters.Remove(first.Id);
        var newcomer = new Fighter
        {
            Id = world.NewFighterId(), FirstName = "New", LastName = "Arrival",
            BirthDate = new DateOnly(2000, 1, 1),
        };
        world.Fighters[newcomer.Id] = newcomer;
        Assert.Equal(newcomer.Id, world.Fighters.Keys.First());   // the premise: slot reuse put it first

        string path = TempPath("order");
        try
        {
            SaveGame.Save(world, path);
            var loaded = SaveGame.Load(path);
            Assert.Equal(world.Fighters.Keys.ToList(), loaded.Fighters.Keys.ToList());
        }
        finally { File.Delete(path); }
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

    // Two seeds, and the second run crosses a year end (year-end awards read the saved rankings).
    [Theory]
    [InlineData(7, 20, 20)]
    [InlineData(13, 45, 25)]
    public void LoadedWorld_AdvancesExactlyLikeAWorldThatWasNeverSaved(int seed, int before, int after)
    {
        var straight = BuildSeasoned(seed: seed, weeks: 0);
        new WorldTick().AdvanceWeeks(straight, before + after);

        var split = BuildSeasoned(seed: seed, weeks: 0);
        new WorldTick().AdvanceWeeks(split, before);
        string path = TempPath("determinism");
        World resumed;
        try
        {
            SaveGame.Save(split, path);
            resumed = SaveGame.Load(path);
        }
        finally { File.Delete(path); }
        new WorldTick().AdvanceWeeks(resumed, after);

        var diffs = PersistenceGraph.Diff(straight, resumed, max: 60);
        Assert.True(diffs.Count == 0,
            "A saved-and-loaded world diverged from the same world run straight through:\n" + PersistenceGraph.Describe(diffs));
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
