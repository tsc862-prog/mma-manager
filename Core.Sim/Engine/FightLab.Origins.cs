using System.Text;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Engine;

/// <summary>
/// The Origins lab: open-weight fights and early rule sets in bulk.
/// <list type="bullet">
/// <item><see cref="SizeTable"/> — the smaller man's win rate by size gap x skill gap.</item>
/// <item><see cref="RuleSetMix"/> — finish rate, method mix, draws and length under each founding
/// promotion's rules, with the day-one pioneers (and modern rules on the same pairs for contrast).</item>
/// <item><see cref="Bracket"/> — an eight-man one-night open-weight bracket, condition carried
/// between bouts, run many times: who wins it.</item>
/// </list>
/// Lab only: nothing here touches a world.
/// </summary>
public static partial class FightLab
{
    /// <summary>The Crucible's rules: one 20-minute round, no judges, gloves optional, open weight.</summary>
    public static FightRules CrucibleRules => OriginsPromotions.All[0].ToFightRules();

    public sealed record SizeCell(double SmallKg, double BigKg, int SkillGap, int Fights, double SmallWinPct, double DrawPct, double FinishPct, double AvgMinutes)
    {
        public double BigWinPct => 100 - SmallWinPct - DrawPct;
    }

    /// <summary>
    /// The smaller man (fixed at <paramref name="smallKg"/>) against bigger men, at skill gaps
    /// (small man's overall minus the big man's). Every cell uses the same base fighters (several
    /// generated pairs, corners alternated), so only size and skill move between cells.
    /// </summary>
    public static List<SizeCell> SizeTable(FightRules rules, int fightsPerCell = 400, ulong seed = 2026,
        double smallKg = 77, double[]? bigKg = null, int[]? skillGaps = null, PrimaryStyle style = PrimaryStyle.WellRounded)
    {
        bigKg ??= new double[] { 77, 84, 93, 102, 113, 125, 168 };
        skillGaps ??= new[] { -10, 0, 10, 20 };
        const int basePairs = 10;
        var gen = new FighterGenerator(seed);
        var bases = new List<(Fighter A, Fighter B)>();
        for (int k = 0; k < basePairs; k++)
        {
            var a = gen.Generate(new FighterId(800_000 + 2 * k), OriginsPioneers.StartDate, new GenOptions { Overall = 60, Style = style, Age = 28, Sex = Sex.Male });
            var b = gen.Generate(new FighterId(800_001 + 2 * k), OriginsPioneers.StartDate, new GenOptions { Overall = 60, Style = style, Age = 28, Sex = Sex.Male });
            bases.Add((a, b));
        }

        var engine = new FightEngine();
        var p = new FightParams { Rules = rules, GeneratePlayByPlay = false };
        var cells = new List<SizeCell>();
        foreach (int gap in skillGaps)
            foreach (double big in bigKg)
            {
                int smallWins = 0, draws = 0, finishes = 0; double secs = 0;
                for (int i = 0; i < fightsPerCell; i++)
                {
                    var (ba, bb) = bases[i % basePairs];
                    var small = CloneForLab(ba, new FighterId(1));
                    var large = CloneForLab(bb, new FighterId(2));
                    foreach (var at in Attributes) at.Set(small, at.Get(small) + gap);
                    small.NaturalWeightLbs = (int)Math.Round(smallKg * 2.20462);
                    large.NaturalWeightLbs = (int)Math.Round(big * 2.20462);
                    // Labelled as a builder would label them: the nearest class to their kg.
                    small.WeightClass = SizeTerm.NearestClass(smallKg);
                    large.WeightClass = SizeTerm.NearestClass(big);
                    bool smallRed = (i & 1) == 0;
                    var o = smallRed
                        ? engine.Simulate(small, large, p, SimRandom.Mix(seed ^ 0x517E, (ulong)i))
                        : engine.Simulate(large, small, p, SimRandom.Mix(seed ^ 0x517E, (ulong)i));
                    if (o.Winner == small.Id) smallWins++;
                    else if (o.Winner is null) draws++;
                    if (IsFinish(o.Method)) finishes++;
                    secs += Seconds(o, p);
                }
                cells.Add(new SizeCell(smallKg, big, gap, fightsPerCell, 100.0 * smallWins / fightsPerCell,
                    100.0 * draws / fightsPerCell, 100.0 * finishes / fightsPerCell, secs / fightsPerCell / 60));
            }
        return cells;
    }

    public sealed record MixRow(string Rules, int Fights, double FinishPct, double KoPct, double TkoPct, double SubPct,
        double DecisionPct, double DrawPct, double AvgMinutes, double FavouriteWinPct);

    /// <summary>
    /// Random pairs of day-one pioneers fought under <paramref name="rules"/>: the method mix, draws,
    /// average length and how often the better man (higher overall) wins.
    /// </summary>
    public static MixRow RuleSetMix(string label, FightRules? rules, int fights = 2000, ulong seed = 2026,
        int rounds = 3, int roundSeconds = 300)
    {
        var pool = PioneersForLab(seed);
        var rng = new SimRandom(SimRandom.Mix(seed, 0x0121_6175UL));
        var engine = new FightEngine();
        var p = new FightParams { Rules = rules, ScheduledRounds = rounds, RoundSeconds = roundSeconds, GeneratePlayByPlay = false };
        int fin = 0, ko = 0, tko = 0, sub = 0, dec = 0, dr = 0, favWins = 0, favBouts = 0; double secs = 0;
        for (int i = 0; i < fights; i++)
        {
            int a = rng.Next(0, pool.Count), b = rng.Next(0, pool.Count - 1);
            if (b >= a) b++;
            var (fa, oa) = pool[a]; var (fb, ob) = pool[b];
            var o = engine.Simulate(fa, fb, p, SimRandom.Mix(seed, (ulong)i));
            switch (o.Method)
            {
                case FightMethod.Knockout: ko++; fin++; break;
                case FightMethod.TechnicalKnockout: tko++; fin++; break;
                case FightMethod.SubmissionTap or FightMethod.TechnicalSubmission: sub++; fin++; break;
                case FightMethod.Draw: dr++; break;
                default: dec++; break;
            }
            if (oa != ob)
            {
                favBouts++;
                if (o.Winner == (oa > ob ? fa.Id : fb.Id)) favWins++;
            }
            secs += Seconds(o, p);
        }
        double Pct(int n) => 100.0 * n / fights;
        return new MixRow(label, fights, Pct(fin), Pct(ko), Pct(tko), Pct(sub), Pct(dec), Pct(dr), secs / fights / 60,
            favBouts == 0 ? 0 : 100.0 * favWins / favBouts);
    }

    public sealed record BracketRow(string Name, int Kg, string Discipline, int Overall, int Titles, double TitlePct);

    /// <summary>
    /// An eight-man one-night bracket of the given pioneers (seeded by overall), run
    /// <paramref name="nights"/> times through <see cref="OneNightBracket.Run"/> - the same function the
    /// booker (<c>TournamentService.RunOneNight</c>) runs, so the lab cannot drift from it. The ninth man
    /// is the alternate.
    /// </summary>
    public static List<BracketRow> Bracket(FightRules rules, IReadOnlyList<OriginsPioneer>? field = null, int nights = 1000, ulong seed = 2026)
    {
        var pool = PioneersForLab(seed);
        // PioneersForLab keeps the table's order, so a pioneer's index is his lab fighter's index.
        var chosen = field ?? OriginsPioneers.All.Where(x => x.Fighter.Org == "CRU").ToList();
        var meta = new Dictionary<FighterId, OriginsPioneer>();
        var entrants = new List<Fighter>();
        foreach (var x in chosen)
        {
            var f = pool[IndexOf(x)].F;
            meta[f.Id] = x;
            entrants.Add(f);
        }
        var seeded = entrants.OrderByDescending(f => OverallOf(f, pool)).ToList();
        var eight = seeded.Take(8).ToList();
        var alternate = seeded.Count > 8 ? seeded[8] : null;
        var titles = new Dictionary<FighterId, int>();
        var engine = new FightEngine();

        for (int n = 0; n < nights; n++)
        {
            var champ = OneNight(engine, eight, alternate, rules, SimRandom.Mix(seed ^ 0xB4AC_E7UL, (ulong)n));
            if (champ is not null) titles[champ.Id] = titles.GetValueOrDefault(champ.Id) + 1;
        }

        return seeded.Select(f => new BracketRow(f.FullName, meta[f.Id].WeightKg, meta[f.Id].Discipline.ToString(),
                OverallOf(f, pool), titles.GetValueOrDefault(f.Id), 100.0 * titles.GetValueOrDefault(f.Id) / nights))
            .OrderByDescending(r => r.Titles).ToList();
    }

    /// <summary>One bracket night in the lab: the booker's bracket, with bare lab bouts.</summary>
    private static Fighter? OneNight(FightEngine engine, List<Fighter> eight, Fighter? alternate, FightRules rules, ulong seed)
    {
        int bout = 0;
        var night = OneNightBracket.Run(eight, alternate, rules, (rules.RoundMinutes ?? 5) * 60, (r, b, p) =>
        {
            var o = engine.Simulate(r, b, p, new SimRandom(SimRandom.Mix(seed, (ulong)bout++)), out var re, out var be);
            return new BracketBoutResult(o, re, be);
        });
        return night.Champion;
    }

    private static double Seconds(FightOutcome o, FightParams p)
    {
        int roundSecs = p.Rules?.RoundMinutes is int m ? m * 60 : p.RoundSeconds;
        return (o.EndRound - 1) * roundSecs + o.EndTimeSeconds;
    }

    private static int IndexOf(OriginsPioneer p)
    {
        for (int i = 0; i < OriginsPioneers.All.Count; i++) if (ReferenceEquals(OriginsPioneers.All[i], p)) return i;
        throw new ArgumentException($"{p.FullName} is not in the pioneer table.");
    }

    private static int OverallOf(Fighter f, List<(Fighter F, int Overall)> pool) => pool.First(x => x.F.Id == f.Id).Overall;

    /// <summary>
    /// The day-one pioneers as lab fighters: generated around their authored style and level (for
    /// tendencies and measurables), then every skill set from the authored discipline profile, with
    /// natural weight and height from the table. The real builder does its own mapping; this is a
    /// faithful stand-in for the lab.
    /// </summary>
    internal static List<(Fighter F, int Overall)> PioneersForLab(ulong seed)
    {
        var gen = new FighterGenerator(seed);
        var list = new List<(Fighter, int)>();
        int id = 700_000;
        foreach (var p in OriginsPioneers.All)
        {
            var d = p.Fighter;
            var f = gen.Generate(new FighterId(id++), OriginsPioneers.StartDate, new GenOptions
            {
                WeightClass = d.WeightClass, Style = d.Style, Age = d.Age, Overall = d.Overall, Sex = Sex.Male,
            });
            f.FirstName = d.First; f.LastName = d.Last; f.Nickname = d.Nickname;
            foreach (var (skill, v) in p.Skills)
                Attributes.First(a => a.Name == skill.ToString()).Set(f, v);
            f.NaturalWeightLbs = p.WeightLbs;
            f.HeightCm = p.HeightCm;
            list.Add((f, d.Overall));
        }
        return list;
    }

    /// <summary>One pioneer bout with text under founding promotion <paramref name="promo"/>'s rules (pair <paramref name="k"/> of a fixed shuffle).</summary>
    public static List<string> OriginsSample(int promo, int k, ulong seed = 2026)
    {
        var pool = PioneersForLab(seed);
        var rng = new SimRandom(SimRandom.Mix(seed, 0x5A3B_1E00UL + (ulong)k));
        int a = rng.Next(0, pool.Count), b = rng.Next(0, pool.Count - 1);
        if (b >= a) b++;
        var rules = OriginsPromotions.All[Math.Clamp(promo, 0, OriginsPromotions.All.Count - 1)].ToFightRules();
        var o = new FightEngine().Simulate(pool[a].F, pool[b].F, new FightParams { Rules = rules, TextSeed = (ulong)k },
            SimRandom.Mix(seed, 0x0050_0000UL + (ulong)k));
        var lines = new List<string>(o.PlayByPlay!);
        lines.Add($"[{o.Method}, round {o.EndRound} at {o.EndTimeSeconds / 60}:{o.EndTimeSeconds % 60:00}]");
        return lines;
    }

    /// <summary>The whole Origins lab as text (the <c>fightlab origins</c> mode).</summary>
    public static string OriginsReport(int fightsPerCell = 400, ulong seed = 2026)
    {
        var sb = new StringBuilder();
        var cru = CrucibleRules;
        foreach (double smallKg in new double[] { 77, 62 })
        {
            sb.AppendLine($"== Size x skill, Crucible rules (1x20, no judges, gloves optional): {smallKg:0} kg man vs the column ==");
            sb.AppendLine("   cells: small man's wins / draws / big man's wins (%); skill gap = small man's level minus the big man's");
            var bigKg = smallKg == 77 ? new double[] { 77, 84, 93, 102, 113, 125, 168 } : new double[] { 62, 70, 80, 93, 110, 125 };
            var cells = SizeTable(cru, fightsPerCell, seed, smallKg, bigKg);
            sb.Append("skill gap ");
            foreach (var b in bigKg) sb.Append($"{b,11:0}kg");
            sb.AppendLine();
            foreach (var g in cells.Select(c => c.SkillGap).Distinct())
            {
                sb.Append($"{g,5:+0;-0;0}     ");
                foreach (var b in bigKg)
                {
                    var c = cells.First(c => c.SkillGap == g && c.BigKg == b);
                    sb.Append($"  {c.SmallWinPct,3:0}/{c.DrawPct,2:0}/{c.BigWinPct,3:0}");
                }
                sb.AppendLine();
            }
            sb.Append("finish%,gap0 ");
            foreach (var c in cells.Where(c => c.SkillGap == 0)) sb.Append($"{c.FinishPct,10:0}  ");
            sb.AppendLine();
            sb.Append("min, gap 0  ");
            foreach (var c in cells.Where(c => c.SkillGap == 0)) sb.Append($"{c.AvgMinutes,11:0.0} ");
            sb.AppendLine();
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("== Pioneer pairs under each house's rules ==");
        sb.AppendLine("rules                         finish   KO  TKO  SUB  dec draw  min  better-man wins");
        var rows = new List<MixRow> { RuleSetMix("modern 3x5 (open weight off)", null, 2000, seed) };
        foreach (var promo in OriginsPromotions.All)
            rows.Add(RuleSetMix($"{promo.Abbreviation} {promo.Rules.Rounds}x{promo.Rules.RoundMinutes}", promo.ToFightRules(), 2000, seed));
        foreach (var r in rows)
            sb.AppendLine($"{r.Rules,-29}{r.FinishPct,6:0.0}{r.KoPct,5:0}{r.TkoPct,5:0}{r.SubPct,5:0}{r.DecisionPct,5:0}{r.DrawPct,5:0}{r.AvgMinutes,5:0.0}  {r.FavouriteWinPct,5:0.0}");

        sb.AppendLine();
        sb.AppendLine("== The Crucible's eight-man night, 1000 times (condition carried) ==");
        foreach (var r in Bracket(cru, nights: 1000, seed: seed))
            sb.AppendLine($"  {r.Name,-40} {r.Kg,4} kg  {r.Discipline,-19} ovr {r.Overall,2}  {r.TitlePct,5:0.0}%");
        return sb.ToString();
    }
}
