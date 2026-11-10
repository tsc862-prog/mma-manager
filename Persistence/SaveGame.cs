using Microsoft.Data.Sqlite;
using MmaManager.Core.Model;

namespace MmaManager.Persistence;

/// <summary>
/// Saves and loads a <see cref="World"/> to a SQLite file. The whole graph is
/// the save; on load the id counters are resynced so freshly minted ids don't
/// collide with saved ones. <c>FightOutcome.PlayByPlay</c> is intentionally not
/// stored — it regenerates from the seed by re-simulating the bout.
/// </summary>
public static class SaveGame
{
    /// <summary>Write the world to <paramref name="path"/>, replacing any existing file.</summary>
    public static void Save(World world, string path)
    {
        if (File.Exists(path)) File.Delete(path);

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var conn = new SqliteConnection(ConnectionString(path));
        conn.Open();
        using var tx = conn.BeginTransaction();

        SaveSchema.Create(conn, tx);
        new WorldWriter(conn, tx).Write(world);

        tx.Commit();
    }

    /// <summary>Load a world from <paramref name="path"/>, resynced and ready to run.</summary>
    public static World Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Save file not found.", path);

        using var conn = new SqliteConnection(ConnectionString(path));
        conn.Open();

        var world = new WorldReader(conn).Read();
        world.SyncIdCounters();
        return world;
    }

    private static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            // No pooling: closing the connection must release the file handle
            // immediately so the save can be re-saved, moved, or deleted.
            Pooling = false,
        }.ToString();
}
