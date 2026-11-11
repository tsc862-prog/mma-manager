using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Engine;

/// <summary>
/// The engine benchmark: a deterministic, fixed-seed bulk report over generated
/// fighters (every style, both sexes, every weight class, 3- and 5-round bouts).
/// Everything except the timing block is a pure function of the options, so two
/// runs on the same code print the same numbers. Run it headless with
/// <c>tools/longsim</c>: <c>dotnet run --no-build -- fightlab [scale] [seed]</c>.
/// The engine tests pin today's numbers in bands (see <c>FightBenchmarkTests</c>).
/// </summary>
public static partial class FightLab
{
    // --- Options --------------------------------------------------------------

    public sealed record ReportOptions
    {
        public ulong Seed { get; init; } = 2026;
        /// <summary>Realistically matched pairs (blue overall = red ± N(0,4)), each fought at 3 and at 5 rounds.</summary>
        public int MatchedPairs { get; init; } = 3000;
        /// <summary>Matched pairs per weight class and sex (9 men's + 4 women's classes), 3 rounds: the by-division mix.</summary>
        public int ClassPairs { get; init; } = 300;
        /// <summary>Pairs with ability gaps up to ±25, 3 rounds: the upset curve.</summary>
        public int WidePairs { get; init; } = 6000;
        /// <summary>Fighters per style at exactly the same CurrentAbility (70) for the style matrix.</summary>
        public int StylePool { get; init; } = 8;
        public int StyleSeeds { get; init; } = 2;
        /// <summary>Opponents in the CA-65 field each attribute test fights.</summary>
        public int AttributeField { get; init; } = 12;
        /// <summary>Bouts per (base, opponent) pair in the attribute and tendency tests.</summary>
        public int AttributeSeeds { get; init; } = 100;
        public int TextFights { get; init; } = 400;
        public int TimingBouts { get; init; } = 20_000;
        public bool IncludeStyles { get; init; } = true;
        public bool IncludeAttributes { get; init; } = true;
        public bool IncludeText { get; init; } = true;
        public bool IncludeTiming { get; init; } = true;

        /// <summary>The CI configuration the tests use (a few seconds).</summary>
        public static ReportOptions Small { get; } = new();

        /// <summary>Scale every sample by <paramref name="k"/> (k = 1 is <see cref="Small"/>).</summary>
        public ReportOptions Scaled(int k) => this with
        {
            MatchedPairs = MatchedPairs * k, WidePairs = WidePairs * k, ClassPairs = ClassPairs * k,
            StyleSeeds = StyleSeeds * k, AttributeSeeds = AttributeSeeds * k,
            TextFights = TextFights * k, TimingBouts = TimingBouts * k,
        };
    }

    // --- Results --------------------------------------------------------------

    public sealed record MethodMix(int Fights, int Ko, int Tko, int Sub, int Ud, int Sd, int Md, int Draw, int Other, double AvgMinutes)
    {
        private double P(int n) => Fights == 0 ? 0 : 100.0 * n / Fights;
        public double FinishPct => P(Ko + Tko + Sub);
        public double KoPct => P(Ko);
        public double TkoPct => P(Tko);
        public double SubPct => P(Sub);
        public double UdPct => P(Ud);
        public double SdPct => P(Sd);
        public double MdPct => P(Md);
        public double DrawPct => P(Draw);
        public double OtherPct => P(Other);
    }

    /// <summary>One bin of the upset curve: |CurrentAbility gap| in [Lo, Hi].</summary>
    public sealed record UpsetBin(int Lo, int Hi, int Bouts, double FavouriteWinPct, double FavouriteFinishPct);

    /// <summary>
    /// +10 to one attribute on a CA-65 base, everything else equal, against a CA-65 field
    /// with common random numbers. <see cref="MeanPp"/> is the win-probability change in
    /// percentage points averaged over the bases; <see cref="PpPerCaPoint"/> divides it by
    /// the CurrentAbility the +10 costs (10 × weight / total weight). An attribute the cage
    /// never reads gives exactly 0 (<see cref="ExactlyZero"/>: every bout bit-identical).
    /// </summary>
    public sealed record AttributeEffect(string Name, double Weight, double CaPoints, double MeanPp, double PpPerCaPoint, double[] PerBasePp, bool ExactlyZero);

    /// <summary>One tendency at 20 versus 80 on each base (win %), and the largest swing.</summary>
    public sealed record TendencyEffect(string Name, double[] WinAt20, double[] WinAt80, bool ExactlyZero)
    {
        public double MaxAbsSwingPp => WinAt20.Zip(WinAt80, (a, b) => Math.Abs(b - a)).Max();
    }

    public sealed record ReportResult
    {
        public required ReportOptions Options { get; init; }
        public required MethodMix ThreeRound { get; init; }
        public required MethodMix FiveRound { get; init; }
        public required MethodMix Men3 { get; init; }
        public required MethodMix Women3 { get; init; }
        /// <summary>3-round matched pairs per division ("M Heavyweight", "W Strawweight", …).</summary>
        public required IReadOnlyList<(string Division, MethodMix Mix)> ByDivision { get; init; }
        /// <summary>How the matched 3-round fights ended, by finish detail (share % of ALL those fights).</summary>
        public required IReadOnlyList<(string Detail, double Pct)> FinishDetails { get; init; }
        /// <summary>Same, 5 rounds.</summary>
        public required IReadOnlyList<(string Detail, double Pct)> FinishDetails5 { get; init; }
        /// <summary>Share (%) of 3-round round-one finishes inside each minute (index 0 = 0:00-1:00).</summary>
        public required double[] RoundOneFinishByMinute { get; init; }
        /// <summary>Share (%) of ALL 3-round fights finished in round r (index 0 = R1).</summary>
        public required double[] FinishByRound3 { get; init; }
        public required double[] FinishByRound5 { get; init; }
        public double KnockdownsPerFight { get; init; }
        /// <summary>Share (%) of knockdowns that ended the fight.</summary>
        public double KnockdownFinishPct { get; init; }
        public double TakedownsPerFight { get; init; }
        public double SubAttemptsPerFight { get; init; }
        /// <summary>Share (%) of completed rounds scored 10-8 (or worse) on the stored card.</summary>
        public double TenEightPct { get; init; }
        /// <summary>3-round judged decisions (UD/SD/MD).</summary>
        public int Decisions { get; init; }
        /// <summary>Share (%) of decisions whose stored card does NOT favour the official winner (level or reversed).</summary>
        public double CardMismatchPct { get; init; }
        /// <summary>Share (%) of decisions whose stored card favours the official LOSER.</summary>
        public double CardReversedPct { get; init; }
        /// <summary>Stored card totals (e.g. "29-28") as share (%) of 3-round decisions, most common first.</summary>
        public required IReadOnlyList<(string Card, double Pct)> CardTotals { get; init; }
        public double MatchRatingMean3 { get; init; }
        /// <summary>MatchRating of matched 3- / 5-round bouts: mean, p10, p25, median, p75, p90.</summary>
        public double[] RatingDist3 { get; init; } = Array.Empty<double>();
        public double[] RatingDist5 { get; init; } = Array.Empty<double>();
        /// <summary>
        /// Card quality as DealCompliance's MinCardQuality reads it (average MatchRating of the card),
        /// over synthetic 12-bout cards of matched pairs: eleven 3-rounders and a 5-round main event.
        /// Same six numbers as <see cref="RatingDist3"/>.
        /// </summary>
        public double[] CardQualityDist { get; init; } = Array.Empty<double>();
        /// <summary>Share (%) of those synthetic cards at or over 40 / 42 / 45 / 50 (the deal thresholds in use).</summary>
        public double[] CardQualityClears { get; init; } = Array.Empty<double>();
        /// <summary>Star distribution (%) of 3- and 5-round bouts, index 0 = one star.</summary>
        public required double[] Stars3 { get; init; }
        public required double[] Stars5 { get; init; }
        public required IReadOnlyList<UpsetBin> Upset { get; init; }
        public required MethodMix Wide { get; init; }

        public double[,]? StyleMatrix { get; init; }
        public double[]? StyleVsField { get; init; }
        /// <summary>Best minus worst style's win % against the field at equal CurrentAbility.</summary>
        public double StyleSpreadPp { get; init; }
        public double BrawlerMinusWellRoundedPp { get; init; }

        public IReadOnlyList<AttributeEffect>? Attributes { get; init; }
        public IReadOnlyList<AttributeEffect>? Frame { get; init; }
        public IReadOnlyList<TendencyEffect>? Tendencies { get; init; }
        public double AllSkillsPlus10Pp { get; init; }
        public PrimaryStyle[] AttributeBases { get; init; } = Array.Empty<PrimaryStyle>();

        public double LinesPerFight { get; init; }
        public int DistinctLines { get; init; }
        /// <summary>Within one fight, the share (%) of lines that repeat an earlier line word for word (names stripped).</summary>
        public double RepeatPct { get; init; }

        public double MicrosPerBoutTextOff { get; init; }
        public double MicrosPerBoutTextOn { get; init; }
        public double KbPerBoutTextOff { get; init; }
        public double KbPerBoutTextOn { get; init; }
        public double BoutsPerSecondTextOff => MicrosPerBoutTextOff <= 0 ? 0 : 1_000_000 / MicrosPerBoutTextOff;

        public string ToText() => FormatReport(this);
    }

    // --- Attribute table -------------------------------------------------------

    /// <summary>One priced attribute: its <see cref="FighterAbility"/> weight and accessors.</summary>
    public sealed record AttributeAccessor(string Name, double Weight, Func<Fighter, int> Get, Action<Fighter, int> Set);

    /// <summary>
    /// The 25 attributes <see cref="FighterAbility.CurrentAbility"/> prices, with the same
    /// weights in the same order (a test pins that they agree).
    /// </summary>
    public static readonly IReadOnlyList<AttributeAccessor> Attributes = new AttributeAccessor[]
    {
        new("PunchPower", 1.0, f => f.Striking.PunchPower, (f, v) => f.Striking.PunchPower = Clamp(v)),
        new("PunchTechnique", 1.1, f => f.Striking.PunchTechnique, (f, v) => f.Striking.PunchTechnique = Clamp(v)),
        new("KickPower", 0.8, f => f.Striking.KickPower, (f, v) => f.Striking.KickPower = Clamp(v)),
        new("KickTechnique", 0.8, f => f.Striking.KickTechnique, (f, v) => f.Striking.KickTechnique = Clamp(v)),
        new("ClinchStriking", 0.7, f => f.Striking.ClinchStriking, (f, v) => f.Striking.ClinchStriking = Clamp(v)),
        new("StrikingDefense", 1.2, f => f.Striking.StrikingDefense, (f, v) => f.Striking.StrikingDefense = Clamp(v)),
        new("Footwork", 1.0, f => f.Striking.Footwork, (f, v) => f.Striking.Footwork = Clamp(v)),
        new("TakedownOffense", 1.0, f => f.Grappling.TakedownOffense, (f, v) => f.Grappling.TakedownOffense = Clamp(v)),
        new("TakedownDefense", 1.2, f => f.Grappling.TakedownDefense, (f, v) => f.Grappling.TakedownDefense = Clamp(v)),
        new("TopControl", 1.0, f => f.Grappling.TopControl, (f, v) => f.Grappling.TopControl = Clamp(v)),
        new("BottomGame", 0.8, f => f.Grappling.BottomGame, (f, v) => f.Grappling.BottomGame = Clamp(v)),
        new("SubmissionOffense", 0.9, f => f.Grappling.SubmissionOffense, (f, v) => f.Grappling.SubmissionOffense = Clamp(v)),
        new("SubmissionDefense", 1.0, f => f.Grappling.SubmissionDefense, (f, v) => f.Grappling.SubmissionDefense = Clamp(v)),
        new("Scrambling", 0.9, f => f.Grappling.Scrambling, (f, v) => f.Grappling.Scrambling = Clamp(v)),
        new("ClinchControl", 0.7, f => f.Grappling.ClinchControl, (f, v) => f.Grappling.ClinchControl = Clamp(v)),
        new("Strength", 0.9, f => f.Physical.Strength, (f, v) => f.Physical.Strength = Clamp(v)),
        new("Cardio", 1.3, f => f.Physical.Cardio, (f, v) => f.Physical.Cardio = Clamp(v)),
        new("Speed", 1.1, f => f.Physical.Speed, (f, v) => f.Physical.Speed = Clamp(v)),
        new("Chin", 1.2, f => f.Physical.Chin, (f, v) => f.Physical.Chin = Clamp(v)),
        new("Recovery", 0.8, f => f.Physical.Recovery, (f, v) => f.Physical.Recovery = Clamp(v)),
        new("Flexibility", 0.5, f => f.Physical.Flexibility, (f, v) => f.Physical.Flexibility = Clamp(v)),
        new("FightIQ", 1.2, f => f.Mental.FightIQ, (f, v) => f.Mental.FightIQ = Clamp(v)),
        new("Composure", 1.0, f => f.Mental.Composure, (f, v) => f.Mental.Composure = Clamp(v)),
        new("Heart", 0.9, f => f.Mental.Heart, (f, v) => f.Mental.Heart = Clamp(v)),
        new("Discipline", 0.7, f => f.Mental.Discipline, (f, v) => f.Mental.Discipline = Clamp(v)),
    };

    public static readonly IReadOnlyList<string> TendencyNames =
        new[] { "StrikingPreference", "Aggression", "Pace", "Counterfighting", "FinishInstinct" };

    public static int GetTendency(Fighter f, string name) => name switch
    {
        "StrikingPreference" => f.Tendencies.StrikingPreference,
        "Aggression" => f.Tendencies.Aggression,
        "Pace" => f.Tendencies.Pace,
        "Counterfighting" => f.Tendencies.Counterfighting,
        "FinishInstinct" => f.Tendencies.FinishInstinct,
        _ => throw new ArgumentException(name),
    };

    public static void SetTendency(Fighter f, string name, int v)
    {
        switch (name)
        {
            case "StrikingPreference": f.Tendencies.StrikingPreference = Clamp(v); break;
            case "Aggression": f.Tendencies.Aggression = Clamp(v); break;
            case "Pace": f.Tendencies.Pace = Clamp(v); break;
            case "Counterfighting": f.Tendencies.Counterfighting = Clamp(v); break;
            case "FinishInstinct": f.Tendencies.FinishInstinct = Clamp(v); break;
            default: throw new ArgumentException(name);
        }
    }

    private static int Clamp(int v) => Math.Clamp(v, 1, 99);

    /// <summary>A copy of <paramref name="s"/> with its own id: every rating, tendency and frame value.</summary>
    public static Fighter CloneForLab(Fighter s, FighterId id)
    {
        var d = new Fighter
        {
            Id = id, FirstName = s.FirstName, LastName = s.LastName, Nickname = s.Nickname,
            BirthDate = s.BirthDate, Sex = s.Sex, Stance = s.Stance, HeightCm = s.HeightCm, ReachCm = s.ReachCm,
            WeightClass = s.WeightClass, PrimaryStyle = s.PrimaryStyle,
        };
        foreach (var a in Attributes) a.Set(d, a.Get(s));
        foreach (var t in TendencyNames) SetTendency(d, t, GetTendency(s, t));
        d.Intangibles.Popularity = s.Intangibles.Popularity;
        d.Intangibles.Charisma = s.Intangibles.Charisma;
        return d;
    }

    // --- The report ------------------------------------------------------------

    private static readonly PrimaryStyle[] Styles = Enum.GetValues<PrimaryStyle>();
    private static readonly WeightClass[] MenClasses = Enum.GetValues<WeightClass>();
    private static readonly WeightClass[] WomenClasses =
        { WeightClass.Strawweight, WeightClass.Flyweight, WeightClass.Bantamweight, WeightClass.Featherweight };

    public static readonly PrimaryStyle[] AttributeBaseStyles =
    {
        PrimaryStyle.WellRounded, PrimaryStyle.Striker, PrimaryStyle.Wrestler,
        PrimaryStyle.BrazilianJiuJitsu, PrimaryStyle.Brawler, PrimaryStyle.Kickboxer,
    };

    /// <summary>Run the benchmark. See <see cref="ReportOptions.Small"/> for the CI configuration.</summary>
    public static ReportResult Report(ReportOptions? options = null)
    {
        var o = options ?? ReportOptions.Small;
        var lab = new Lab(o.Seed);
        var engine = new FightEngine();

        // ---- Population A: matched pairs at 3 and 5 rounds
        var pick = new SimRandom(SimRandom.Mix(o.Seed, 0xA));
        var pairs = new (Fighter R, Fighter B)[o.MatchedPairs];
        for (int i = 0; i < pairs.Length; i++)
        {
            bool woman = pick.Chance(0.15);
            var wc = woman ? WomenClasses[pick.Next(0, WomenClasses.Length)] : MenClasses[pick.Next(0, MenClasses.Length)];
            var sex = woman ? Sex.Female : Sex.Male;
            int ov = Math.Clamp((int)Math.Round(pick.NextGaussian(60, 10)), 35, 90);
            int ob = Math.Clamp(ov + (int)Math.Round(pick.NextGaussian(0, 4)), 30, 95);
            pairs[i] = (lab.Make(ov, Styles[pick.Next(0, 8)], wc, sex), lab.Make(ob, Styles[pick.Next(0, 8)], wc, sex));
        }
        var a3 = new FightOutcome[pairs.Length];
        var a5 = new FightOutcome[pairs.Length];
        var d3 = new string?[pairs.Length];
        var d5 = new string?[pairs.Length];
        Parallel.For(0, pairs.Length, i =>
        {
            a3[i] = engine.Simulate(pairs[i].R, pairs[i].B, Fast(3), SimRandom.Mix(o.Seed ^ 0x3A, (ulong)i), out d3[i]);
            a5[i] = engine.Simulate(pairs[i].R, pairs[i].B, Fast(5), SimRandom.Mix(o.Seed ^ 0x5A, (ulong)i), out d5[i]);
        });

        // ---- Population C: matched pairs inside each division (3 rounds)
        var slots = MenClasses.Select(c => (Sex: Sex.Male, Wc: c)).Concat(WomenClasses.Select(c => (Sex: Sex.Female, Wc: c))).ToArray();
        var pickC = new SimRandom(SimRandom.Mix(o.Seed, 0xC1A55));
        var cpairs = new (Fighter R, Fighter B)[slots.Length * o.ClassPairs];
        for (int i = 0; i < cpairs.Length; i++)
        {
            var (sx, wc) = slots[i / Math.Max(1, o.ClassPairs)];
            int ov = Math.Clamp((int)Math.Round(pickC.NextGaussian(60, 10)), 35, 90);
            int ob = Math.Clamp(ov + (int)Math.Round(pickC.NextGaussian(0, 4)), 30, 95);
            cpairs[i] = (lab.Make(ov, Styles[pickC.Next(0, 8)], wc, sx), lab.Make(ob, Styles[pickC.Next(0, 8)], wc, sx));
        }
        var c3 = new FightOutcome[cpairs.Length];
        Parallel.For(0, cpairs.Length, i => c3[i] = engine.Simulate(cpairs[i].R, cpairs[i].B, Fast(3), SimRandom.Mix(o.Seed ^ 0xC3, (ulong)i)));
        var byDivision = slots.Select((s, k) => (
            $"{(s.Sex == Sex.Male ? "M" : "W")} {s.Wc}",
            Mix(c3.Skip(k * o.ClassPairs).Take(o.ClassPairs).ToList()))).ToList();

        static List<(string, double)> Details(string?[] ds, int n) => ds.Where(d => d is not null)
            .GroupBy(d => d!).Select(g => (g.Key, 100.0 * g.Count() / Math.Max(1, n)))
            .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
        var r1 = a3.Where(x => IsFinish(x.Method) && x.EndRound == 1).ToList();
        var r1Min = new double[5];
        foreach (var x in r1) r1Min[Math.Clamp(x.EndTimeSeconds / 60, 0, 4)]++;
        for (int k = 0; k < 5; k++) r1Min[k] = r1.Count == 0 ? 0 : 100 * r1Min[k] / r1.Count;

        var idx = Enumerable.Range(0, pairs.Length).ToList();
        var men = idx.Where(i => pairs[i].R.Sex == Sex.Male).Select(i => a3[i]).ToList();
        var women = idx.Where(i => pairs[i].R.Sex == Sex.Female).Select(i => a3[i]).ToList();

        int totalKd = a3.Sum(x => x.Stats.Values.Sum(s => s.KnockdownsScored));
        int koFinishes = a3.Count(x => x.Method == FightMethod.Knockout);

        int rounds = 0, tenEight = 0;
        foreach (var x in a3)
            foreach (var s in x.RoundScores ?? new List<RoundScore>())
            {
                rounds++;
                if (Math.Min(s.Red, s.Blue) <= 8) tenEight++;
            }

        var decisions = idx.Where(i => IsDecision(a3[i].Method)).ToList();
        int mismatch = 0, reversed = 0;
        var totals = new Dictionary<string, int>();
        foreach (int i in decisions)
        {
            var x = a3[i];
            int cr = x.RoundScores!.Sum(s => s.Red), cb = x.RoundScores!.Sum(s => s.Blue);
            int winSign = x.Winner == pairs[i].R.Id ? 1 : -1;
            if (Math.Sign(cr - cb) != winSign) mismatch++;
            if (Math.Sign(cr - cb) == -winSign) reversed++;
            string key = $"{Math.Max(cr, cb)}-{Math.Min(cr, cb)}";
            totals[key] = totals.GetValueOrDefault(key) + 1;
        }

        var ratings3 = idx.Select(i => RatingOf(pairs[i], a3[i])).ToList();
        var ratings5 = idx.Select(i => RatingOf(pairs[i], a5[i])).ToList();
        var cardQ = new List<double>();
        for (int c = 0; c + 12 <= pairs.Length; c += 12)
            cardQ.Add((Enumerable.Range(c, 11).Sum(i => ratings3[i]) + ratings5[c + 11]) / 12.0);
        double[] clears = { 40, 42, 45, 50 };

        // ---- Population B: wide gaps, the upset curve
        var pickB = new SimRandom(SimRandom.Mix(o.Seed, 0xB));
        var wide = new (Fighter R, Fighter B)[o.WidePairs];
        for (int i = 0; i < wide.Length; i++)
        {
            int ov = pickB.Range(40, 85);
            int ob = Math.Clamp(ov + pickB.Range(-25, 25), 25, 95);
            wide[i] = (lab.Make(ov, Styles[pickB.Next(0, 8)]), lab.Make(ob, Styles[pickB.Next(0, 8)]));
        }
        var b3 = new FightOutcome[wide.Length];
        var caR = wide.Select(p => FighterAbility.CurrentAbility(p.R)).ToArray();
        var caB = wide.Select(p => FighterAbility.CurrentAbility(p.B)).ToArray();
        Parallel.For(0, wide.Length, i => b3[i] = engine.Simulate(wide[i].R, wide[i].B, Fast(3), SimRandom.Mix(o.Seed ^ 0xB3, (ulong)i)));
        var bins = new List<UpsetBin>();
        foreach (var (lo, hi) in new[] { (0, 0), (1, 2), (3, 4), (5, 6), (7, 9), (10, 12), (13, 17), (18, 22), (23, 99) })
        {
            var inBin = Enumerable.Range(0, wide.Length).Where(i => Math.Abs(caR[i] - caB[i]) >= lo && Math.Abs(caR[i] - caB[i]) <= hi).ToList();
            if (inBin.Count == 0) continue;
            double favWin = inBin.Average(i =>
            {
                var w = b3[i].Winner;
                if (w is null) return 0.5;
                bool redWon = w == wide[i].R.Id;
                return caR[i] == caB[i] ? (redWon ? 1.0 : 0.0) : (redWon == (caR[i] > caB[i]) ? 1.0 : 0.0);
            });
            var favWins = inBin.Where(i => caR[i] != caB[i] && b3[i].Winner is { } w && (w == wide[i].R.Id) == (caR[i] > caB[i])).ToList();
            double favFin = favWins.Count == 0 ? 0 : 100.0 * favWins.Count(i => IsFinish(b3[i].Method)) / favWins.Count;
            bins.Add(new UpsetBin(lo, hi, inBin.Count, 100 * favWin, favFin));
        }

        var result = new ReportResult
        {
            Options = o,
            ThreeRound = Mix(a3), FiveRound = Mix(a5), Men3 = Mix(men), Women3 = Mix(women),
            ByDivision = byDivision, FinishDetails = Details(d3, a3.Length), FinishDetails5 = Details(d5, a5.Length),
            RoundOneFinishByMinute = r1Min,
            FinishByRound3 = ByRound(a3, 3), FinishByRound5 = ByRound(a5, 5),
            KnockdownsPerFight = a3.Average(x => x.Stats.Values.Sum(s => s.KnockdownsScored)),
            KnockdownFinishPct = totalKd == 0 ? 0 : 100.0 * koFinishes / totalKd,
            TakedownsPerFight = a3.Average(x => x.Stats.Values.Sum(s => s.TakedownsLanded)),
            SubAttemptsPerFight = a3.Average(x => x.Stats.Values.Sum(s => s.SubmissionAttempts)),
            TenEightPct = rounds == 0 ? 0 : 100.0 * tenEight / rounds,
            Decisions = decisions.Count,
            CardMismatchPct = decisions.Count == 0 ? 0 : 100.0 * mismatch / decisions.Count,
            CardReversedPct = decisions.Count == 0 ? 0 : 100.0 * reversed / decisions.Count,
            CardTotals = totals.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => (kv.Key, 100.0 * kv.Value / decisions.Count)).ToList(),
            MatchRatingMean3 = ratings3.Count == 0 ? 0 : ratings3.Average(),
            RatingDist3 = Dist(ratings3.Select(x => (double)x)), RatingDist5 = Dist(ratings5.Select(x => (double)x)),
            CardQualityDist = Dist(cardQ),
            CardQualityClears = clears.Select(t => cardQ.Count == 0 ? 0 : 100.0 * cardQ.Count(q => q >= t) / cardQ.Count).ToArray(),
            Stars3 = StarShares(ratings3), Stars5 = StarShares(ratings5),
            Upset = bins, Wide = Mix(b3),
        };

        if (o.IncludeStyles) result = Styles_(result, lab, engine, o);
        if (o.IncludeAttributes) result = Attributes_(result, lab, engine, o);
        if (o.IncludeText) result = Text_(result, lab, engine, o);
        if (o.IncludeTiming) result = Timing_(result, lab, engine, o);
        return result;
    }

    // ---- Style matrix: every fighter at exactly CA 70

    private static ReportResult Styles_(ReportResult r, Lab lab, FightEngine engine, ReportOptions o)
    {
        var pool = Styles.Select(s => Enumerable.Range(0, o.StylePool).Select(_ => lab.MakeAtCa(70, s, 0)).ToArray()).ToArray();
        int n = Styles.Length, per = o.StylePool, seeds = o.StyleSeeds;
        var m = new double[n, n];
        Parallel.For(0, n * n, ci =>
        {
            int a = ci / n, b = ci % n;
            double won = 0; int bouts = 0;
            for (int i = 0; i < per; i++)
                for (int j = 0; j < per; j++)
                    for (int k = 0; k < seeds; k++)
                    {
                        var x = engine.Simulate(pool[a][i], pool[b][j], Fast(3),
                            SimRandom.Mix(o.Seed ^ 0x57, (ulong)(ci * 1_000_000 + i * 10_000 + j * 100 + k)));
                        won += WinScore(x, pool[a][i].Id); bouts++;
                    }
            m[a, b] = 100 * won / bouts;
        });
        var vs = Enumerable.Range(0, n).Select(a => Enumerable.Range(0, n).Where(b => b != a).Average(b => m[a, b])).ToArray();
        int brawl = Array.IndexOf(Styles, PrimaryStyle.Brawler), wr = Array.IndexOf(Styles, PrimaryStyle.WellRounded);
        return r with
        {
            StyleMatrix = m, StyleVsField = vs,
            StyleSpreadPp = vs.Max() - vs.Min(),
            BrawlerMinusWellRoundedPp = vs[brawl] - vs[wr],
        };
    }

    // ---- Attributes and tendencies: +10 / 20-vs-80 on CA-65 bases vs a CA-65 field

    private static ReportResult Attributes_(ReportResult r, Lab lab, FightEngine engine, ReportOptions o)
    {
        var bases = AttributeBaseStyles.Select(s => lab.MakeAtCa(65, s, 0)).ToArray();
        var fieldPick = new SimRandom(SimRandom.Mix(o.Seed, 0xF1E1D));
        var field = Enumerable.Range(0, o.AttributeField).Select(_ => lab.MakeAtCa(65, Styles[fieldPick.Next(0, 8)], 1)).ToArray();
        int seeds = o.AttributeSeeds;
        int nextId = 2_000_000;
        Fighter Clone(Fighter f) => CloneForLab(f, new FighterId(Interlocked.Increment(ref nextId)));

        // Run one variant against the whole field with the SAME seeds as every other
        // variant (common random numbers): returns win share and a fingerprint of every
        // outcome, so an attribute the engine never reads is detectably bit-identical.
        (double Win, ulong Print) VsField(Fighter me)
        {
            var wins = new double[field.Length * seeds];
            var prints = new ulong[field.Length * seeds];
            Parallel.For(0, wins.Length, i =>
            {
                int j = i / seeds, k = i % seeds;
                var x = engine.Simulate(me, field[j], Fast(3), SimRandom.Mix(o.Seed ^ 0xA77, (ulong)(j * 100_000 + k)));
                wins[i] = WinScore(x, me.Id);
                var h = new Fnv();
                HashResult(h, x, me.Id, field[j].Id);
                HashCard(h, x);
                prints[i] = ulong.Parse(h.Hex, NumberStyles.HexNumber);
            });
            ulong print = 0;
            for (int i = 0; i < prints.Length; i++) print = print * 31 + prints[i];
            return (100 * wins.Average(), print);
        }

        var baseRes = bases.Select(VsField).ToArray();
        double wSum = Attributes.Sum(a => a.Weight);

        AttributeEffect Effect(string name, double weight, Action<Fighter> mutate)
        {
            var per = new double[bases.Length];
            bool zero = true;
            for (int bi = 0; bi < bases.Length; bi++)
            {
                var c = Clone(bases[bi]);
                mutate(c);
                var v = VsField(c);
                per[bi] = v.Win - baseRes[bi].Win;
                zero &= v.Print == baseRes[bi].Print;
            }
            double ca = weight > 0 ? 10 * weight / wSum : double.NaN;
            double mean = per.Average();
            return new AttributeEffect(name, weight, ca, mean, weight > 0 ? mean / ca : double.NaN, per, zero);
        }

        var attrs = Attributes.Select(a => Effect(a.Name, a.Weight, f => a.Set(f, a.Get(f) + 10))).ToList();
        var frame = new List<AttributeEffect>
        {
            Effect("Height +10cm", 0, f => f.HeightCm += 10),
            Effect("Reach +10cm", 0, f => f.ReachCm += 10),
            Effect("Stance flip", 0, f => f.Stance = f.Stance == Stance.Southpaw ? Stance.Orthodox : Stance.Southpaw),
            Effect("Popularity 99", 0, f => f.Intangibles.Popularity = 99),
        };
        var all = Effect("ALL skills +10", 0, f => { foreach (var a in Attributes) a.Set(f, a.Get(f) + 10); });

        var tends = new List<TendencyEffect>();
        foreach (var t in TendencyNames)
        {
            var lo = new double[bases.Length]; var hi = new double[bases.Length];
            bool zero = true;
            for (int bi = 0; bi < bases.Length; bi++)
            {
                var c20 = Clone(bases[bi]); SetTendency(c20, t, 20);
                var c80 = Clone(bases[bi]); SetTendency(c80, t, 80);
                var v20 = VsField(c20); var v80 = VsField(c80);
                lo[bi] = v20.Win; hi[bi] = v80.Win;
                zero &= v20.Print == v80.Print;
            }
            tends.Add(new TendencyEffect(t, lo, hi, zero));
        }

        return r with
        {
            Attributes = attrs, Frame = frame, Tendencies = tends,
            AllSkillsPlus10Pp = all.MeanPp, AttributeBases = AttributeBaseStyles,
        };
    }

    // ---- Relevance probe: does the cage read this attribute at all, and how much?

    /// <summary>
    /// One attribute (or frame value, or tendency) swung from <c>Low</c> to <c>High</c> on
    /// every base. <see cref="Dead"/> is exact: every bout of the high variant is
    /// bit-identical to the low variant, so the engine never reads the value.
    /// <see cref="PpPerCaPoint"/> is the mean win change divided by the CurrentAbility the
    /// swing costs (NaN for unpriced values — use <see cref="MeanPp"/>).
    /// </summary>
    public sealed record Relevance(string Name, double Weight, double MeanPp, double PpPerCaPoint, double[] PerBasePp, bool Dead);

    /// <summary>
    /// The relevance probe the attribute test pins. Every priced attribute is set to
    /// <paramref name="low"/> and then <paramref name="high"/> on CA-65 bases of six styles,
    /// each variant fighting the same CA-65 field on the same seeds (common random
    /// numbers). Height and reach swing 20 cm, stance flips, tendencies run 20 → 80.
    /// A wide swing keeps the signal well clear of the noise; the +10 marginals in
    /// <see cref="Report"/> are the price-comparable numbers.
    /// </summary>
    public static IReadOnlyList<Relevance> AttributeRelevance(ulong seed = 2026, int low = 30, int high = 90, int fieldSize = 8, int seedsPerPair = 25)
    {
        var lab = new Lab(seed);
        var engine = new FightEngine();
        var bases = AttributeBaseStyles.Select(s => lab.MakeAtCa(65, s, 0)).ToArray();
        var fieldPick = new SimRandom(SimRandom.Mix(seed, 0xF1E1D));
        var field = Enumerable.Range(0, fieldSize).Select(_ => lab.MakeAtCa(65, Styles[fieldPick.Next(0, 8)], 1)).ToArray();
        int nextId = 3_000_000;
        double wSum = Attributes.Sum(a => a.Weight);

        (double Win, ulong Print) VsField(Fighter me)
        {
            var wins = new double[field.Length * seedsPerPair];
            var prints = new ulong[wins.Length];
            Parallel.For(0, wins.Length, i =>
            {
                int j = i / seedsPerPair, k = i % seedsPerPair;
                var x = engine.Simulate(me, field[j], Fast(3), SimRandom.Mix(seed ^ 0x8E1, (ulong)(j * 100_000 + k)));
                wins[i] = WinScore(x, me.Id);
                var h = new Fnv();
                HashResult(h, x, me.Id, field[j].Id);
                HashCard(h, x);
                prints[i] = ulong.Parse(h.Hex, NumberStyles.HexNumber);
            });
            ulong print = 0;
            for (int i = 0; i < prints.Length; i++) print = print * 31 + prints[i];
            return (100 * wins.Average(), print);
        }

        Relevance Probe(string name, double weight, double caSwing, Action<Fighter> toLow, Action<Fighter> toHigh)
        {
            var per = new double[bases.Length];
            bool dead = true;
            for (int bi = 0; bi < bases.Length; bi++)
            {
                var lo = CloneForLab(bases[bi], new FighterId(++nextId)); toLow(lo);
                var hi = CloneForLab(bases[bi], new FighterId(++nextId)); toHigh(hi);
                var l = VsField(lo); var h = VsField(hi);
                per[bi] = h.Win - l.Win;
                dead &= l.Print == h.Print;
            }
            double mean = per.Average();
            return new Relevance(name, weight, mean, caSwing > 0 ? mean / caSwing : double.NaN, per, dead);
        }

        var list = new List<Relevance>();
        foreach (var a in Attributes)
            list.Add(Probe(a.Name, a.Weight, (high - low) * a.Weight / wSum, f => a.Set(f, low), f => a.Set(f, high)));
        list.Add(Probe("Height", 0, 0, f => f.HeightCm -= 10, f => f.HeightCm += 10));
        list.Add(Probe("Reach", 0, 0, f => f.ReachCm -= 10, f => f.ReachCm += 10));
        list.Add(Probe("Stance", 0, 0, f => f.Stance = Stance.Orthodox, f => f.Stance = Stance.Southpaw));
        foreach (var t in TendencyNames)
            list.Add(Probe(t, 0, 0, f => SetTendency(f, t, 20), f => SetTendency(f, t, 80)));
        return list;
    }

    // ---- Play-by-play repetition

    private static readonly Regex Stamp = new(@"^R\d+ \d+:\d\d — ", RegexOptions.Compiled);

    private static ReportResult Text_(ReportResult r, Lab lab, FightEngine engine, ReportOptions o)
    {
        var pick = new SimRandom(SimRandom.Mix(o.Seed, 0x7E47));
        long lines = 0; double repeatShare = 0;
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < o.TextFights; i++)
        {
            bool woman = i % 7 == 0;
            var wc = woman ? WeightClass.Flyweight : MenClasses[pick.Next(0, MenClasses.Length)];
            var sx = woman ? Sex.Female : Sex.Male;
            int ov = pick.Range(50, 80); int ob = Math.Clamp(ov + pick.Range(-15, 15), 35, 90);
            var a = lab.Make(ov, Styles[pick.Next(0, 8)], wc, sx);
            var b = lab.Make(ob, Styles[pick.Next(0, 8)], wc, sx);
            int rounds = i % 9 == 0 ? 5 : 3;
            var x = engine.Simulate(a, b, new FightParams { ScheduledRounds = rounds }, SimRandom.Mix(o.Seed ^ 0x7E, (ulong)i));
            var pbp = x.PlayByPlay ?? new List<string>();
            lines += pbp.Count;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int dups = 0;
            foreach (var line in pbp)
            {
                string t = Stamp.Replace(line, "").Replace(a.FullName, "A").Replace(b.FullName, "B");
                if (t.StartsWith("---", StringComparison.Ordinal) || t.Contains(" vs ", StringComparison.Ordinal)) continue;
                if (!seen.Add(t)) dups++;
                distinct.Add(t);
            }
            repeatShare += (double)dups / Math.Max(1, pbp.Count);
        }
        return r with
        {
            LinesPerFight = o.TextFights == 0 ? 0 : (double)lines / o.TextFights,
            DistinctLines = distinct.Count,
            RepeatPct = o.TextFights == 0 ? 0 : 100 * repeatShare / o.TextFights,
        };
    }

    // ---- Cost per bout (single thread; the only non-deterministic block)

    private static ReportResult Timing_(ReportResult r, Lab lab, FightEngine engine, ReportOptions o)
    {
        var a = lab.MakeAtCa(65, PrimaryStyle.WellRounded, 1);
        var b = lab.MakeAtCa(65, PrimaryStyle.Wrestler, 1);
        (double Us, double Kb) Measure(bool text)
        {
            var p = new FightParams { ScheduledRounds = 3, GeneratePlayByPlay = text };
            int n = Math.Max(1, text ? o.TimingBouts / 4 : o.TimingBouts);
            for (int i = 0; i < Math.Min(2000, n); i++) engine.Simulate(a, b, p, (ulong)i);   // warm up
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) engine.Simulate(a, b, p, SimRandom.Mix(99, (ulong)i));
            sw.Stop();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
            return (sw.Elapsed.TotalMilliseconds * 1000 / n, alloc / 1024.0 / n);
        }
        var off = Measure(false);
        var on = Measure(true);
        return r with { MicrosPerBoutTextOff = off.Us, KbPerBoutTextOff = off.Kb, MicrosPerBoutTextOn = on.Us, KbPerBoutTextOn = on.Kb };
    }

    // --- Helpers ---------------------------------------------------------------

    private static FightParams Fast(int rounds) => new() { ScheduledRounds = rounds, GeneratePlayByPlay = false };

    private static bool IsFinish(FightMethod m) =>
        m is FightMethod.Knockout or FightMethod.TechnicalKnockout or FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;

    private static bool IsDecision(FightMethod m) =>
        m is FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision;

    private static double WinScore(FightOutcome x, FighterId me) => x.Winner == me ? 1 : x.Winner is null ? 0.5 : 0;

    private static int RatingOf((Fighter R, Fighter B) p, FightOutcome x) =>
        MatchRating.Of(new Fight { Id = new FightId(1), RedCorner = p.R.Id, BlueCorner = p.B.Id, Outcome = x });

    /// <summary>mean, p10, p25, median, p75, p90 (nearest rank).</summary>
    private static double[] Dist(IEnumerable<double> xs)
    {
        var v = xs.OrderBy(x => x).ToList();
        if (v.Count == 0) return new double[6];
        double Q(double q) => v[Math.Clamp((int)Math.Round(q * (v.Count - 1)), 0, v.Count - 1)];
        return new[] { v.Average(), Q(0.1), Q(0.25), Q(0.5), Q(0.75), Q(0.9) };
    }

    private static double[] StarShares(List<int> ratings)
    {
        var s = new double[5];
        foreach (int v in ratings) s[MatchRating.Stars(v).Length - 1]++;
        for (int i = 0; i < 5; i++) s[i] = ratings.Count == 0 ? 0 : 100 * s[i] / ratings.Count;
        return s;
    }

    private static MethodMix Mix(IReadOnlyCollection<FightOutcome> xs)
    {
        int ko = 0, tko = 0, sub = 0, ud = 0, sd = 0, md = 0, dr = 0, other = 0;
        double secs = 0;
        foreach (var x in xs)
        {
            switch (x.Method)
            {
                case FightMethod.Knockout: ko++; break;
                case FightMethod.TechnicalKnockout: tko++; break;
                case FightMethod.SubmissionTap or FightMethod.TechnicalSubmission: sub++; break;
                case FightMethod.UnanimousDecision: ud++; break;
                case FightMethod.SplitDecision: sd++; break;
                case FightMethod.MajorityDecision: md++; break;
                case FightMethod.Draw: dr++; break;
                default: other++; break;
            }
            // Report fights use 300-second rounds throughout.
            secs += (x.EndRound - 1) * 300 + x.EndTimeSeconds;
        }
        return new MethodMix(xs.Count, ko, tko, sub, ud, sd, md, dr, other, xs.Count == 0 ? 0 : secs / xs.Count / 60);
    }

    private static double[] ByRound(FightOutcome[] xs, int rounds)
    {
        var r = new double[rounds];
        foreach (var x in xs) if (IsFinish(x.Method)) r[x.EndRound - 1]++;
        for (int i = 0; i < rounds; i++) r[i] = xs.Length == 0 ? 0 : 100 * r[i] / xs.Length;
        return r;
    }

    /// <summary>Fighter factory over the real generator, with its own id space.</summary>
    private sealed class Lab
    {
        private static readonly DateOnly Today = new(2026, 1, 1);
        private readonly FighterGenerator _gen;
        private int _id = 1_000_000;
        public Lab(ulong seed) => _gen = new FighterGenerator(seed);

        public Fighter Make(int overall, PrimaryStyle style, WeightClass wc = WeightClass.Lightweight, Sex sex = Sex.Male) =>
            _gen.Generate(new FighterId(++_id), Today, new GenOptions
            { Overall = overall, Style = style, Age = 28, WeightClass = wc, Sex = sex, WithRecord = false });

        /// <summary>Generate until the fighter's real CurrentAbility is within <paramref name="tol"/> of <paramref name="ca"/>.</summary>
        public Fighter MakeAtCa(int ca, PrimaryStyle style, int tol)
        {
            int ov = ca;
            for (int tries = 0; ; tries++)
            {
                var f = Make(ov, style);
                int c = FighterAbility.CurrentAbility(f);
                if (Math.Abs(c - ca) <= tol) return f;
                if (tries % 3 == 2) ov += Math.Sign(ca - c);
            }
        }
    }

    // --- Text -------------------------------------------------------------------

    private static string FormatReport(ReportResult r)
    {
        var sb = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        void L(string s = "") => sb.AppendLine(s);
        string P(double v) => v.ToString("0.0", ci).PadLeft(5) + "%";
        string Line(MethodMix m) =>
            $"n={m.Fights,6}  finish {P(m.FinishPct)}  KO {P(m.KoPct)} TKO {P(m.TkoPct)} SUB {P(m.SubPct)} | UD {P(m.UdPct)} SD {P(m.SdPct)} MD {P(m.MdPct)} Draw {P(m.DrawPct)} other {P(m.OtherPct)} | avg {m.AvgMinutes.ToString("0.0", ci)} min";

        var o = r.Options;
        L($"=== FIGHTLAB REPORT  seed {o.Seed}  matched {o.MatchedPairs}  wide {o.WidePairs} ===");
        L("Method mix (matched pairs)");
        L("  3 rounds  " + Line(r.ThreeRound));
        L("  5 rounds  " + Line(r.FiveRound));
        L("  men  (3)  " + Line(r.Men3));
        L("  women(3)  " + Line(r.Women3));
        L("By division (3 rounds, matched pairs inside the division)");
        foreach (var (div, dm) in r.ByDivision) L($"  {div,-19} " + Line(dm));
        L("How the finishes happened (share of ALL fights)");
        L("  3 rounds  " + string.Join("  ", r.FinishDetails.Select(d => $"{d.Detail} {P(d.Pct)}")));
        L("  5 rounds  " + string.Join("  ", r.FinishDetails5.Select(d => $"{d.Detail} {P(d.Pct)}")));
        L("Finishes by round (share of ALL fights)");
        L("  3 rounds  " + string.Join("  ", r.FinishByRound3.Select((v, i) => $"R{i + 1} {P(v)}")));
        L("  5 rounds  " + string.Join("  ", r.FinishByRound5.Select((v, i) => $"R{i + 1} {P(v)}")));
        L("  round-one finishes by minute (3 rounds): " + string.Join("  ", r.RoundOneFinishByMinute.Select((v, i) => $"{i}-{i + 1} {P(v)}")));
        L("Action (3 rounds)");
        L($"  knockdowns per fight {r.KnockdownsPerFight.ToString("0.00", ci)}  (share that ended the fight {P(r.KnockdownFinishPct)})");
        L($"  takedowns landed per fight {r.TakedownsPerFight.ToString("0.00", ci)}  submission attempts per fight {r.SubAttemptsPerFight.ToString("0.00", ci)}");
        L("Scorecards (3-round decisions)");
        L($"  decisions {r.Decisions}; 10-8 share of completed rounds {P(r.TenEightPct)}");
        L($"  stored card does not favour the official winner: {P(r.CardMismatchPct)}  (favours the loser: {P(r.CardReversedPct)})");
        L("  card totals: " + string.Join("  ", r.CardTotals.Take(10).Select(c => $"{c.Card} {P(c.Pct)}")));
        L("Match rating");
        L($"  mean (3 rds) {r.MatchRatingMean3.ToString("0.0", ci)}");
        L("  stars 3 rds " + string.Join("  ", r.Stars3.Select((v, i) => $"{i + 1}* {P(v)}")));
        L("  stars 5 rds " + string.Join("  ", r.Stars5.Select((v, i) => $"{i + 1}* {P(v)}")));
        string D6(double[] d) => d.Length < 6 ? "" :
            $"mean {d[0].ToString("0.0", ci)}  p10 {d[1].ToString("0.0", ci)} p25 {d[2].ToString("0.0", ci)} median {d[3].ToString("0.0", ci)} p75 {d[4].ToString("0.0", ci)} p90 {d[5].ToString("0.0", ci)}";
        L("  rating 3 rds: " + D6(r.RatingDist3));
        L("  rating 5 rds: " + D6(r.RatingDist5));
        L("  card quality (MinCardQuality's average, synthetic 12-bout cards: 11 x 3 rds + a 5-round main): " + D6(r.CardQualityDist));
        if (r.CardQualityClears.Length == 4)
            L($"  cards clearing 40 / 42 / 45 / 50: {P(r.CardQualityClears[0])} {P(r.CardQualityClears[1])} {P(r.CardQualityClears[2])} {P(r.CardQualityClears[3])}");
        L("Upset curve (wide pairs, 3 rounds)");
        L("  |CA gap|      n   favourite wins   favourite's wins that are finishes");
        foreach (var b in r.Upset) L($"  {b.Lo,2}-{(b.Hi >= 99 ? "+" : b.Hi.ToString(ci)),-3}  {b.Bouts,6}   {P(b.FavouriteWinPct)}          {P(b.FavouriteFinishPct)}");
        L("  overall   " + Line(r.Wide));

        if (r.StyleMatrix is { } m && r.StyleVsField is { } vs)
        {
            L("Style matrix (everyone at CurrentAbility 70; row's win %)");
            L("            " + string.Join(" ", Styles.Select(s => Short(s).PadLeft(6))) + "   vs field");
            for (int a = 0; a < Styles.Length; a++)
                L($"  {Short(Styles[a]),-9} " + string.Join(" ", Enumerable.Range(0, Styles.Length).Select(b => P(m[a, b]).PadLeft(6))) + $"   {P(vs[a])}");
            L($"  spread (best - worst) {r.StyleSpreadPp.ToString("0.0", ci)} pp; Brawler - WellRounded {r.BrawlerMinusWellRoundedPp.ToString("0.0", ci)} pp");
        }

        if (r.Attributes is { } attrs)
        {
            L($"Attribute marginals (+10, CA-65 bases {string.Join("/", r.AttributeBases.Select(Short))} vs a {o.AttributeField}-fighter CA-65 field, {o.AttributeSeeds} bouts per pair)");
            L("  attribute           weight  CA pts   mean pp   pp per CA point   (0 = bit-identical: the cage never reads it)");
            foreach (var a in attrs.OrderByDescending(a => a.PpPerCaPoint))
                L($"  {a.Name,-18} {a.Weight.ToString("0.0", ci),6} {a.CaPoints.ToString("0.00", ci),7} {a.MeanPp.ToString("+0.00;-0.00;0.00", ci),9} {a.PpPerCaPoint.ToString("+0.00;-0.00;0.00", ci),12}{(a.ExactlyZero ? "   ZERO" : "")}");
            foreach (var a in r.Frame!)
                L($"  {a.Name,-18} {"",6} {"",7} {a.MeanPp.ToString("+0.00;-0.00;0.00", ci),9} {"",12}{(a.ExactlyZero ? "   ZERO" : "")}");
            L($"  ALL skills +10 -> {r.AllSkillsPlus10Pp.ToString("+0.0", ci)} pp (10 CA points)");
            L("Tendencies (win % at 20 -> 80, per base)");
            foreach (var t in r.Tendencies!)
                L($"  {t.Name,-19} " + string.Join("  ", t.WinAt20.Select((v, i) => $"{Short(r.AttributeBases[i])} {v.ToString("0.0", ci)}->{t.WinAt80[i].ToString("0.0", ci)}")) +
                  $"   max swing {t.MaxAbsSwingPp.ToString("0.0", ci)} pp{(t.ExactlyZero ? "  ZERO" : "")}");
        }

        if (o.IncludeText)
            L($"Play-by-play: {r.LinesPerFight.ToString("0.0", ci)} lines per fight, {r.DistinctLines} distinct name-stripped lines in {o.TextFights} fights, {P(r.RepeatPct)} of a fight's lines repeat an earlier line");
        if (o.IncludeTiming)
            L($"Cost per 3-round bout (single thread): text off {r.MicrosPerBoutTextOff.ToString("0.0", ci)} us / {r.KbPerBoutTextOff.ToString("0.0", ci)} KB;  text on {r.MicrosPerBoutTextOn.ToString("0.0", ci)} us / {r.KbPerBoutTextOn.ToString("0.0", ci)} KB");
        return sb.ToString();
    }

    private static string Short(PrimaryStyle s) => s switch
    {
        PrimaryStyle.Striker => "Strk", PrimaryStyle.Kickboxer => "KickB", PrimaryStyle.Wrestler => "Wrest",
        PrimaryStyle.BrazilianJiuJitsu => "BJJ", PrimaryStyle.Grappler => "Grapp", PrimaryStyle.Brawler => "Brawl",
        PrimaryStyle.ClinchFighter => "Clinch", _ => "WellR",
    };
}
