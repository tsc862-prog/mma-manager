using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Geography;

/// <summary>
/// A country's name as it reads mid-sentence: "the United States", "the Netherlands",
/// "the Philippines", "the United Arab Emirates", "the Czech Republic" — and plain "Brazil".
/// For player-facing text that says "in {country}"; never used to build ids or keys.
/// </summary>
public static class CountryNames
{
    // Whole names that take "the" (plural or island-group names).
    private static readonly HashSet<string> TheNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Netherlands", "Philippines", "Bahamas", "Gambia", "Maldives", "Seychelles", "Comoros",
        "Vatican", "Vatican City", "Congo", "Democratic Republic of the Congo",
    };

    /// <summary>The name with its article when it needs one ("the United States"; "Brazil").</summary>
    public static string InSentence(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        string n = name.Trim();
        if (n.StartsWith("the ", StringComparison.OrdinalIgnoreCase)) return "the " + n[4..];
        bool needs = TheNames.Contains(n)
                     || n.StartsWith("United ", StringComparison.Ordinal)    // the United States / Kingdom / Arab Emirates
                     || n.EndsWith(" Republic", StringComparison.Ordinal)    // the Czech / Dominican / Central African Republic
                     || n.EndsWith(" Islands", StringComparison.Ordinal)     // the Solomon / Cayman Islands
                     || n.EndsWith(" Emirates", StringComparison.Ordinal)
                     || n.StartsWith("Republic of ", StringComparison.Ordinal);
        return needs ? "the " + n : n;
    }

    /// <summary><see cref="InSentence(string)"/> for a country.</summary>
    public static string InSentence(Country c) => InSentence(c.Name);
}
