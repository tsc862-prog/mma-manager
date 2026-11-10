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
    /// <summary>
    /// Write the world to <paramref name="path"/>, replacing any existing file.
    /// The save goes to a temporary file beside the target, is committed, closed
    /// and verified, and only then swapped into place. Any failure leaves the
    /// previous file untouched and removes the temporary file.
    /// </summary>
    public static void Save(World world, string path)
    {
        string full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string tmp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var conn = new SqliteConnection(ConnectionString(tmp)))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    SaveSchema.Create(conn, tx);
                    new WorldWriter(conn, tx).Write(world);
                    tx.Commit();
                }
                // Disposing the connection (no pooling) releases the file handle
                // before the move below.
            }

            Verify(tmp);

            if (File.Exists(full)) File.Replace(tmp, full, null);
            else File.Move(tmp, full);
        }
        catch
        {
            DeleteQuietly(tmp);
            throw;
        }
    }

    /// <summary>Reopen the finished temp file and check it is a whole, readable database of the current version.</summary>
    private static void Verify(string tmp)
    {
        using var conn = new SqliteConnection(ConnectionString(tmp));
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA integrity_check";
            if (!string.Equals(cmd.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The save failed its integrity check; the previous save was kept.");
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT save_version FROM meta LIMIT 1";
            if (Convert.ToInt32(cmd.ExecuteScalar()) != World.LatestSaveVersion)
                throw new InvalidDataException("The save was written with the wrong version; the previous save was kept.");
        }
    }

    private static void DeleteQuietly(string tmp)
    {
        foreach (var f in new[] { tmp, tmp + "-journal", tmp + "-wal", tmp + "-shm" })
        {
            try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
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
