using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// A promotion's house rules in plain words, for every screen that shows them (the New Game choice,
/// the Book Event desk, HQ). Reads the rules the engine obeys (<see cref="FightRules"/>) and the
/// house's night format; decides nothing. Today's rules (<see cref="FightRules.IsModern"/>) give no
/// lines at all, so a modern promotion's screens never grow a rules box.
/// </summary>
public static class HouseRules
{
    /// <summary>Is this a house with its own early-era rules (an Origins promotion)?</summary>
    public static bool HasOwn(Promotion? p) => p?.Rules.Fight is { IsModern: false };

    /// <summary>"One 20-minute round" / "3 rounds of 5 minutes".</summary>
    public static string Time(FightRules r, int rounds, int minutes) =>
        rounds <= 1 ? $"One {minutes}-minute round" : $"{rounds} rounds of {minutes} minutes";

    /// <summary>A bout's length in words: "3 rounds", or at a one-round house "one 30-minute round".</summary>
    public static string Rounds(int n, Promotion? p) =>
        n == 1 && p?.Rules.Fight is { IsModern: false } r ? $"one {r.RoundMinutes ?? p.Rules.RoundMinutes}-minute round"
        : n == 1 ? "1 round" : $"{n} rounds";

    /// <summary>The rule lines for a live promotion (empty for today's rules).</summary>
    public static IReadOnlyList<string> For(Promotion? p)
    {
        if (p?.Rules.Fight is not { IsModern: false } r) return Array.Empty<string>();
        var lines = Lines(r, r.Rounds ?? p.Rules.StandardRounds, r.RoundMinutes ?? p.Rules.RoundMinutes, bracket: p.Rules.ShowFormat == ShowFormat.OneNightTournament);
        // After a weight split: the lines the house has drawn, heaviest first, each with the weights it takes.
        var bands = p.Divisions.Where(d => !d.Open && d.Sex == Sex.Male).OrderByDescending(d => d.WeightClass).ToList();
        if (!r.OpenWeight && bands.Count > 0)
            lines.Insert(0, "Weight classes: " + string.Join("; ", bands.Select(d =>
                d.Lightest is { } lo ? $"{FighterRow.Pretty(d.WeightClass.ToString()).ToLowerInvariant()} ({FighterRow.BandRange(lo, d.WeightClass)})"
                : FighterRow.Pretty(d.WeightClass.ToString()).ToLowerInvariant())) + ".");
        // A founding house's own words for its fouls and its finishes (the authored rule set, by abbreviation).
        if (OriginsPromotions.All.FirstOrDefault(s => s.Abbreviation == p.Abbreviation) is { } spec) Authored(lines, spec, r);
        return lines;
    }

    /// <summary>The rule lines for an authored day-one house (the New Game screen, before any world exists).</summary>
    public static IReadOnlyList<string> For(OriginsPromotionSpec spec)
    {
        var lines = Lines(spec.ToFightRules(), spec.Rules.Rounds, spec.Rules.RoundMinutes, bracket: spec.Rules.TournamentSize > 0);
        Authored(lines, spec);
        return lines;
    }

    // The authored "not allowed" list says everything the ground line does, and more: one or the other.
    // (A house that has since adopted judges also ends bouts on their decision: the founding list says so too.)
    private static void Authored(List<string> lines, OriginsPromotionSpec spec, FightRules? now = null)
    {
        lines.RemoveAll(l => l.StartsWith("On the ground", StringComparison.Ordinal));
        lines.Add("Not allowed: " + string.Join(", ", spec.Rules.Banned) + ".");
        var ways = spec.Rules.WaysToWin.ToList();
        if (now is { Judges: true } && !ways.Any(w => w.Contains("judges", StringComparison.OrdinalIgnoreCase))) ways.Add("judges' decision");
        lines.Add("A bout ends by " + JoinOr(ways) + ".");
    }

    private static List<string> Lines(FightRules r, int rounds, int minutes, bool bracket)
    {
        var lines = new List<string>();
        if (r.OpenWeight) lines.Add("No weight classes: any man may meet any man, whatever the size difference.");
        lines.Add(Time(r, rounds, minutes) + (r.Judges
            ? ". Judges score a bout that goes the distance."
            : bracket
                ? ". No judges: a bout that reaches the limit is a draw, and a draw puts both men out of the bracket."
                : ". No judges: a bout that reaches the limit is a draw."));
        lines.Add(r.Gloves switch
        {
            GloveType.Bare => "Bare hands, with the fighter's own tape.",
            GloveType.Optional => "Gloves optional: a light open-fingered glove, or bare hands.",
            GloveType.ThinOpenFinger => "Thin open-fingered gloves for everyone.",
            _ => "Today's small open-fingered gloves.",
        });
        lines.Add(r.Enclosure switch
        {
            Enclosure.Ring => "A roped ring.",
            Enclosure.OpenFloor => "An open floor with the crowd around it: no ropes, no fence.",
            _ => "A fenced ring.",
        });
        if (r.RopeEscapes > 0)
            lines.Add($"{r.RopeEscapes} rope escapes a bout: the rope breaks a hold; past that, it costs points.");
        if (!r.Elbows || !r.GroundedHeadPunches || !r.GroundedKicks)
            lines.Add("On the ground: " + string.Join("; ", new[]
            {
                r.Elbows ? null : "no elbows",
                r.GroundedHeadPunches ? null : "no closed fists to a grounded man's head",
                r.GroundedKicks ? null : "no kicks to a grounded man's head",
            }.Where(s => s is not null)) + ".");
        return lines;
    }

    /// <summary>What a night at this house is, in one line ("An eight-man bracket in one night, the winner takes $30,000").</summary>
    public static string NightLine(Promotion p) => p.Rules.ShowFormat switch
    {
        ShowFormat.OneNightTournament =>
            $"{Article(Number(p.Rules.TournamentSize))} {Number(p.Rules.TournamentSize)}-man bracket in one night: the man left standing wins the tournament"
            + (p.Rules.TournamentPrize.Amount > 0 ? $" and {p.Rules.TournamentPrize}." : "."),
        ShowFormat.GymChallenge => "Challenge matches between rival gyms, a handful a night; a man never fights his own gym-mate, and there is no belt.",
        _ => HasOwn(p) ? "A card of single bouts, the house belt on the line when it can be." : "",
    };

    /// <summary>How often the house puts on a night (the sim's cadence, in words).</summary>
    public static string Cadence(Promotion p) => OriginsShows.CadenceWeeks(p) switch
    {
        >= 12 => "about once a season",
        >= 9 => "about five times a year",
        _ => "about every two months",
    };

    private static string Article(string word) => "aeiou".Contains(char.ToLowerInvariant(word[0])) ? "An" : "A";

    private static string Number(int n) => n switch { 2 => "two", 4 => "four", 8 => "eight", 16 => "sixteen", _ => n.ToString() };

    /// <summary>The bracket's size in words ("eight").</summary>
    public static string Men(Promotion p) => Number(Math.Max(2, p.Rules.TournamentSize));

    /// <summary>How the field is drawn, in the sim's terms (TournamentService.Field): the men fit to fight that
    /// night, promised men first, then the top of your scouts' read.</summary>
    public static string FieldRule(Promotion p) =>
        $"the field is the top {Men(p)} by your scouts' read among the men fit to fight that night, a man you promised a place first";

    private static string JoinOr(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " or " + items[^1],
    };
}

/// <summary>One of the three day-one houses on the New Game screen: who founded it, where, what it thinks the sport is.</summary>
public sealed class OriginsHouseCard
{
    public OriginsHouseCard(OriginsPromotionSpec spec, int index)
    {
        Spec = spec;
        Index = index;
        Rules = HouseRules.For(spec);
    }

    public OriginsPromotionSpec Spec { get; }
    public int Index { get; }

    public string Name => Spec.Name;
    public string Abbreviation => Spec.Abbreviation;
    public string Place => $"{Spec.HomeCity}, {Country}";
    public string Country => Spec.Country switch { "USA" => "United States", "JPN" => "Japan", "BRA" => "Brazil", _ => Spec.Country };
    public string Since => Spec.FirstShowYear < OriginsPioneers.StartDate.Year
        ? $"Nights since {Spec.FirstShowYear}" : $"First night this year";
    public string Niche => Spec.Niche;
    public string Lore => Spec.Lore;
    public string FounderName => Spec.Founder.Name;
    public string FounderLine => $"Founder, {Spec.Founder.Age}";
    public string FounderBio => Spec.Founder.Bio;
    /// <summary>The bio's first sentence, for the card (the whole bio is the tooltip).</summary>
    public string FounderLead => FirstSentence(Spec.Founder.Bio);
    public ImageSource FounderFace => Art.PeopleLibrary.Portrait(Spec.Founder.Name, 160);
    public ImageSource Logo => Art.LogoFactory.ForAbbreviation(Spec.Abbreviation);
    public string Bank => Spec.Balance.ToString();
    public string Night => Spec.Rules.Format + ".";
    public string Room => Spec.HomeVenue;
    public string RoomLore => Spec.HomeVenueLore;
    public IReadOnlyList<string> Rules { get; }

    private static string FirstSentence(string s)
    {
        int dot = s.IndexOf(". ", StringComparison.Ordinal);
        return dot > 0 ? s[..(dot + 1)] : s;
    }
}

/// <summary>The words an Origins start leaves on the status line.</summary>
public static class OriginsStart
{
    public static IReadOnlyList<OriginsHouseCard> Houses { get; } =
        OriginsPromotions.All.Select((s, i) => new OriginsHouseCard(s, i)).ToArray();

    /// <summary>The words a ringside start leaves on the status line.</summary>
    public const string RingsideSummary =
        "1 January 1993. You have no promotion: you are watching from ringside as three small houses invent a sport. "
        + "Found your own whenever you're ready.";

    public static string SummaryFor(OriginsPromotionSpec spec) =>
        $"1 January 1993. You run {spec.Name} out of {spec.HomeCity} with {spec.Balance} in the bank. "
        + "There are no weight classes, no television deals and no rulebook anyone agrees on yet. Put on your first night.";
}
