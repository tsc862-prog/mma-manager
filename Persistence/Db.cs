using System.Globalization;
using Microsoft.Data.Sqlite;
using MmaManager.Core.Model;

namespace MmaManager.Persistence;

/// <summary>
/// Small typed helpers over ADO.NET so the reader/writer stay readable: dates as
/// ISO text, money as invariant decimal text, enums by name, ids as integers,
/// and null handled uniformly. Kept deliberately thin — this is the only place
/// that knows how each model type maps onto a SQLite cell.
/// </summary>
internal static class Db
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static SqliteCommand Cmd(this SqliteConnection c, SqliteTransaction? tx, string sql)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    public static void Exec(this SqliteConnection c, SqliteTransaction? tx, string sql)
    {
        using var cmd = c.Cmd(tx, sql);
        cmd.ExecuteNonQuery();
    }

    // --- value encoders (model -> cell) ------------------------------------
    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
    public static object DateN(DateOnly? d) => d is null ? DBNull.Value : Date(d.Value);
    public static string Cash(Money m) => m.Amount.ToString(Inv);
    public static object IdN(int? id) => id is null ? (object)DBNull.Value : id.Value;
    public static object Text(string? s) => s is null ? DBNull.Value : s;

    public static void Bind(this SqliteCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    // --- value decoders (cell -> model) ------------------------------------
    public static int Int(this SqliteDataReader r, string col) => r.GetInt32(r.GetOrdinal(col));
    public static int? IntN(this SqliteDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetInt32(i);
    }
    public static long Long(this SqliteDataReader r, string col) => r.GetInt64(r.GetOrdinal(col));
    public static double Dbl(this SqliteDataReader r, string col) => r.GetDouble(r.GetOrdinal(col));
    public static bool Bool(this SqliteDataReader r, string col) => r.Int(col) != 0;

    public static string Str(this SqliteDataReader r, string col) => r.GetString(r.GetOrdinal(col));
    public static string? StrN(this SqliteDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    public static DateOnly Date(this SqliteDataReader r, string col) =>
        DateOnly.ParseExact(r.Str(col), "yyyy-MM-dd", Inv);
    public static DateOnly? DateN(this SqliteDataReader r, string col)
    {
        var s = r.StrN(col);
        return s is null ? null : DateOnly.ParseExact(s, "yyyy-MM-dd", Inv);
    }

    public static Money Cash(this SqliteDataReader r, string col) =>
        new(decimal.Parse(r.Str(col), Inv));

    public static T Enum<T>(this SqliteDataReader r, string col) where T : struct, Enum =>
        System.Enum.Parse<T>(r.Str(col));

    public static T? EnumN<T>(this SqliteDataReader r, string col) where T : struct, Enum
    {
        var s = r.StrN(col);
        return s is null ? null : System.Enum.Parse<T>(s);
    }

    // --- round scores: null = not recorded, "" = recorded with no rounds, else "10-9,9-10" ----
    public static object Scores(List<RoundScore>? scores) =>
        scores is null ? DBNull.Value : string.Join(",", scores.Select(x => $"{x.Red}-{x.Blue}"));

    public static List<RoundScore>? Scores(this SqliteDataReader r, string col) =>
        r.StrN(col) is not { } s ? null
        : s.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('-'))
            .Select(p => new RoundScore(int.Parse(p[0], Inv), int.Parse(p[1], Inv)))
            .ToList();

    // --- deal requirements: "Kind:Threshold|Kind:Threshold" ------------------
    public static string Reqs(IEnumerable<DealRequirement> reqs) =>
        string.Join("|", reqs.Select(x => $"{x.Kind}:{x.Threshold}"));

    public static List<DealRequirement> Reqs(this SqliteDataReader r, string col) =>
        r.Str(col).Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Split(':'))
            .Select(p => new DealRequirement(System.Enum.Parse<DealRequirementKind>(p[0]), int.Parse(p[1], Inv)))
            .ToList();
}
