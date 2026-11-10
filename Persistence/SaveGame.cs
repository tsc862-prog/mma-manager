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

        SweepStaleTemps(full);

        string tmp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool verified = false;
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
            verified = true;

            (SwapFile ?? DefaultSwap)(tmp, full);
            if (!File.Exists(full))
                throw new IOException("The save was swapped into place but the target file does not exist.");
        }
        catch
        {
            // ReplaceFile with no backup can fail after it has already removed the target
            // (error 1176), leaving the finished save only under its temp name. Only a temp
            // file that passed verification may be put in place (an unfinished one must never
            // become the save); the save then succeeded and is not an error.
            if (verified && !File.Exists(full) && File.Exists(tmp))
            {
                File.Move(tmp, full);   // if this fails too, the temp file is kept and the error surfaces
                return;
            }
            DeleteQuietly(tmp);
            throw;
        }
    }

    /// <summary>Test seam: replaces the final swap (set per thread; null = the real one).</summary>
    [ThreadStatic] internal static Action<string, string>? SwapFile;

    private static void DefaultSwap(string tmp, string full)
    {
        if (File.Exists(full)) File.Replace(tmp, full, null);
        else File.Move(tmp, full);
    }

    /// <summary>
    /// A kill or power loss mid-save leaves its temp file behind. Remove leftovers of this
    /// target only: files named exactly "&lt;target&gt;.&lt;32 hex&gt;.tmp" in the same folder.
    /// </summary>
    private static void SweepStaleTemps(string full)
    {
        var dir = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        var pattern = new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(Path.GetFileName(full)) + @"\.[0-9a-f]{32}\.tmp$");
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, Path.GetFileName(full) + ".*.tmp"))
            {
                if (!pattern.IsMatch(Path.GetFileName(f)) || string.Equals(f, full, StringComparison.OrdinalIgnoreCase)) continue;
                DeleteQuietly(f);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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

        if (new FileInfo(path).Length == 0)
            throw new InvalidDataException($"'{path}' is empty, not a save file.");

        // Read-only: opening must never create or change a file.
        using var conn = new SqliteConnection(ConnectionString(path, readOnly: true));
        conn.Open();

        try { return new WorldReader(conn).Read(); }
        catch (SqliteException e) when (e.SqliteErrorCode == 1 || e.SqliteErrorCode == 26)   // SQL error (no such table) / not a database
        {
            throw new InvalidDataException($"'{path}' is not a readable save file ({e.Message}).", e);
        }
    }

    private static string ConnectionString(string path, bool readOnly = false) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            // No pooling: closing the connection must release the file handle
            // immediately so the save can be re-saved, moved, or deleted.
            Pooling = false,
        }.ToString();
}
