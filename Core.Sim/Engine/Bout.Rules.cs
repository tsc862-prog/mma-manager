using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Engine;

/// <summary>
/// The Origins half of the bout: early-era rule sets (one long round, no judges, gloves, ground
/// restrictions, rope escapes), the size term for open-weight fights, and condition carried in
/// from an earlier bout on the same night.
///
/// THE RULE: every line here runs only when <see cref="_custom"/> is set (a non-modern
/// <see cref="FightRules"/> or a carried <see cref="BoutCondition"/>), with one exception: the size
/// term's relative edge also runs on today's rules when the booker flags the bout
/// (<see cref="FightParams.BandedSizeTerm"/>; Origins only). On today's rules without that flag each hook
/// in Bout.cs is skipped or adds exactly 0.0, so a modern bout is byte-identical to before
/// (pinned by the golden master and the text golden). New rolls draw from <see cref="_rs"/>,
/// a keyed stream derived from the SAME opening draw that seeds the stoppage stream: no rule here
/// ever adds a draw to the bout stream.
/// </summary>
internal sealed partial class Bout
{
    /// <summary>Longest round the custom path accepts (a single 30-minute round is the longest authored).</summary>
    private const double MaxRoundSeconds = 3600;
    /// <summary>In a round longer than ten minutes, the doctor and the corners get a look every this many seconds [rs].</summary>
    private const double MidRoundCheckEvery = 300;
    /// <summary>No closed fists to a grounded man's head: palms and body shots land less and hurt less.</summary>
    private const double RestrictedGroundAccuracy = 5, RestrictedGroundDamage = 0.65;
    /// <summary>Chance a submission that would have been a tap happens within reach of the rope [rs].</summary>
    private const double RopeReach = 0.65;

    private readonly FightRules _rules;
    /// <summary>A non-modern rule set or carried condition: the Origins paths are live.</summary>
    private readonly bool _custom;
    /// <summary>The size term is on: open-weight rules, or the booker's <see cref="FightParams.BandedSizeTerm"/>.
    /// Independent of <see cref="_custom"/>: on today's rules only the relative edge runs (<see cref="ApplyRelativeSize"/>).</summary>
    private readonly bool _size;
    /// <summary>The rules stream: every new roll on the custom path (hand injuries, rope reach, mid-round checks).</summary>
    private readonly SimRandom? _rs;
    private double _nextCheck;
    /// <summary>One overtime period's length (a no-judges bout the booker wants decided); 0 = none.</summary>
    private readonly int _overtimeSeconds;
    private bool _overtimeRan;
    /// <summary>No closed fists on the ground: the follow-up after a knockdown rarely finishes it.</summary>
    private const double RestrictedPounceOffset = 18;

    // --- Set-up -------------------------------------------------------------

    private void ApplyRules(FightParams p)
    {
        for (int i = 0; i < 2; i++)
        {
            var s = _s[i];
            s.Deduct = new int[s.RoundPts.Length];
            s.Glove = Gloves.For(_rules.Gloves, s.F);
            s.WeightKg = SizeTerm.WeightKg(s.F);
            // Open weight: size comes from natural weight ONLY - the weight-class power term is
            // replaced (not stacked) by a power curve on the man's own kg.
            if (_size && !_rules.IsModern) s.Power = SizeTerm.OpenPower(s.F, s.WeightKg);
            s.Power *= Gloves.DamageMul(s.Glove);
        }
        // Today's rules on the custom path only because condition is carried (a later bout of a bracket night at a
        // house under the code): the size term is the one today's rules get, the relative edge, as the night's first
        // bout had it on the modern path. Never both: this replaces the full term below, and the constructor calls
        // ApplyRelativeSize only when the bout is NOT on the custom path.
        if (_size && _rules.IsModern) ApplyRelativeSize();
        else if (_size)
        {
            for (int i = 0; i < 2; i++)
            {
                var me = _s[i]; var him = _s[1 - i];
                me.SizeEdge = SizeTerm.Edge(me.WeightKg, him.WeightKg);
                me.Power *= Math.Exp(SizeTerm.PowerRelK * me.SizeEdge);
                me.DrainMul = Math.Exp(SizeTerm.GasRelK * me.SizeEdge) * SizeTerm.HeavyGas(me.WeightKg);
            }
        }
        Carry(0, p.RedStart);
        Carry(1, p.BlueStart);
    }

    /// <summary>
    /// The size term on TODAY's rules (a banded division, or an open-weight bout kept after the house took
    /// the unified code): the bout stays on the modern path - modern round lengths, no overtime, no
    /// gloves, no _rs, no rules text - and only the RELATIVE edge between the two men is added. The
    /// absolute part stays today's: power from the division table (not <see cref="SizeTerm.OpenPower"/>)
    /// and no <see cref="SizeTerm.HeavyGas"/>. So between two men of the same natural weight every term is
    /// exactly neutral (x1.0, +0.0) and the bout is byte-identical to the unflagged one. No roll here.
    /// </summary>
    private void ApplyRelativeSize()
    {
        for (int i = 0; i < 2; i++) _s[i].WeightKg = SizeTerm.WeightKg(_s[i].F);
        for (int i = 0; i < 2; i++)
        {
            var me = _s[i];
            me.SizeEdge = SizeTerm.Edge(me.WeightKg, _s[1 - i].WeightKg);
            me.Power *= Math.Exp(SizeTerm.PowerRelK * me.SizeEdge);
            me.DrainMul = Math.Exp(SizeTerm.GasRelK * me.SizeEdge);
        }
    }

    private void Carry(int i, BoutCondition? c)
    {
        if (c is null) return;
        var s = _s[i];
        s.Health = Math.Clamp(c.Health, 1, 100);
        s.Stamina = Math.Clamp(c.Stamina, 0, 100);
        s.Cut = Math.Max(0, c.Cut);
        s.HandPower = Math.Clamp(c.HandPower, 0.3, 1);
        s.Power *= s.HandPower;
    }

    /// <summary>Rope escapes fighter <paramref name="i"/> used (lab and tests; not saved).</summary>
    internal int RopeEscapesUsed(int i) => _s[i].RopeEscapesUsed;

    /// <summary>The fighter's condition at the end of the bout, to carry into the next one on the night.</summary>
    internal BoutCondition EndCondition(int i) => new(_s[i].Health, _s[i].Stamina, _s[i].Cut, _s[i].HandPower);

    // --- Size term hooks (exactly 0 / 1 when off) ----------------------------

    /// <summary>The bigger man is harder to move, take down and hold down: rating points to <paramref name="a"/>'s grappling roll.</summary>
    private double GrappleEdge(int a) => _size ? SizeTerm.GrapplePts * _s[a].SizeEdge : 0.0;
    /// <summary>The smaller man is quicker: rating points to <paramref name="a"/>'s strike accuracy.</summary>
    private double SpeedEdge(int a) => _size ? -SizeTerm.SpeedPts * _s[a].SizeEdge : 0.0;
    /// <summary>The bigger man absorbs more: chin points for defender <paramref name="d"/>.</summary>
    private double ChinEdge(int d) => _size ? SizeTerm.ChinPts * _s[d].SizeEdge : 0.0;
    /// <summary>Strength in the hold: submission-defence points for defender <paramref name="d"/>.</summary>
    private double SubSizeEdge(int d) => _size ? SizeTerm.SubPts * _s[d].SizeEdge : 0.0;
    /// <summary>Carrying a bigger man's weight on the bottom drains more.</summary>
    private double PinnedDrain() => Math.Exp(SizeTerm.PinnedK * _s[_top].SizeEdge);

    private double RestrictedPounce() => _custom && !_rules.GroundedHeadPunches ? RestrictedPounceOffset : 0.0;

    /// <summary>A landed shot thrown with the hand (not a kick, knee or elbow): only these hurt the hand.</summary>
    internal static bool IsPunch(string shot) => Array.IndexOf(Punches, shot) >= 0 || shot is "uppercut" or "short hook";

    private double ClinchCutFactor => _custom && !_rules.Elbows ? 1.5 : 2.5;
    private double GroundCutFactor => !_custom ? 2.5
        : (_rules.Elbows ? 2.5 : 1.2) * (_rules.GroundedHeadPunches ? 1.0 : 0.5);

    // --- Rolls on the rules stream -------------------------------------------

    /// <summary>A landed power punch may hurt an unprotected hand [rs]; the hand then hits softer for the rest of the night.</summary>
    private void MaybeHurtHand(int a, bool punch)
    {
        var s = _s[a];
        if (!punch || s.HandPower < 1) return;      // one bad hand per night is enough
        double p = Gloves.HandInjury(s.Glove);
        if (p <= 0 || !_rs!.Chance(p)) return;
        s.HandPower = Gloves.HurtHand;
        s.Power *= Gloves.HurtHand;
        if (Logging)
            switch (V(2))
            {
                case 0: Say($"{Name(a)} shakes out {Pos(a)} right hand after that one — it looks like it hurt {Obj(a)} too."); break;
                default: Say($"{Name(a)} winces and flexes {Pos(a)} fist. Bare knuckles on a hard head."); break;
            }
    }

    /// <summary>
    /// A submission that would have been a tap, under rope-escape rules [rs]: if the rope is in reach,
    /// the defender grabs it, the hold is broken and they restart standing. Within the allowance it
    /// is free; past it, it costs a point on every card (with no judges, the rope no longer saves him).
    /// Returns true when the rope saved him.
    /// </summary>
    private bool TryRopeEscape(int a, string hold)
    {
        int d = 1 - a;
        var s = _s[d];
        if (_rules.RopeEscapes <= 0) return false;
        bool past = s.RopeEscapesUsed >= _rules.RopeEscapes;
        if (past && !_rules.Judges) return false;
        if (!_rs!.Chance(RopeReach)) return false;
        s.RopeEscapesUsed++;
        _phase = Phase.Standing;
        if (past) s.Deduct[_round - 1]++;
        if (Logging)
        {
            Say($"{Name(a)} has the {hold} locked — but {Name(d)} reaches the rope! The referee breaks it and stands them up.");
            if (past) Say($"That is beyond {Name(d)}'s allowance: a point is taken from {Obj(d)} on the cards.");
            else
            {
                int left = _rules.RopeEscapes - s.RopeEscapesUsed;
                Say(left == 0 ? $"{Name(d)} has no rope escapes left." : $"{Name(d)} has {left} rope escape{(left == 1 ? "" : "s")} left.");
            }
        }
        return true;
    }

    /// <summary>After every exchange on the custom path: gassing lines, and in a long round the doctor and the corners.</summary>
    private void AfterExchangeRules()
    {
        if (Logging)
            for (int i = 0; i < 2; i++)
            {
                if (_s[i].GassedSaid || _s[i].Stamina > 18 || _finish is not null) continue;
                _s[i].GassedSaid = true;
                Say(_size && _s[i].WeightKg >= 110
                    ? $"{Name(i)} is breathing hard, hands on {Pos(i)} hips between exchanges. All that size is a heavy load to carry."
                    : $"{Name(i)} is running on empty — the pace has caught up with {Obj(i)}.");
            }

        if (RoundSeconds <= 600 || _finish is not null || _clock < _nextCheck || _clock >= RoundSeconds) return;
        _nextCheck += MidRoundCheckEvery;
        for (int d = 0; d < 2 && _finish is null; d++)
        {
            var s = _s[d];
            if (s.Cut >= 3 && _rs!.Chance(Math.Min(0.8, DoctorStop * (s.Cut - 2))))
            {
                RecordFinish(1 - d, FightMethod.TechnicalKnockout, "TKO: doctor, mid-round check");
                if (Logging) Say($"The referee calls a pause for the doctor to look at {Name(d)}'s cut… and the doctor stops it. {Name(1 - d)} wins.");
                return;
            }
            if (s.Health < CornerHealth && _rs!.Chance(0.05 + (CornerHealth - s.Health) * 0.012))
            {
                RecordFinish(1 - d, FightMethod.TechnicalKnockout, "TKO: corner towel");
                if (Logging) Say($"In comes the towel from {Name(d)}'s corner — they have seen enough. {Name(1 - d)} wins.");
                return;
            }
        }
    }

    // --- Outcomes -------------------------------------------------------------

    /// <summary>The limit, unfinished, with nobody scoring: a draw. No judges' draws are made.</summary>
    private FightOutcome TimeLimitDraw()
    {
        _round = _overtimeRan ? _rounds + 1 : _rounds;
        FinishDetail = _overtimeRan ? "Draw: time limit after overtime, no judges" : "Draw: time limit, no judges";
        if (Logging)
        {
            _clock = RoundSeconds;
            Say(_overtimeRan ? "Time again! Overtime could not separate them either: the bout is a draw."
                : _rounds == 1 ? $"Time! {Minutes(RoundSeconds)} and nobody could finish it. There are no judges here: the bout is a draw."
                : "The final bell. There are no judges here, so the bout is declared a draw.");
        }
        return new FightOutcome
        {
            Winner = null, Loser = null, Method = FightMethod.Draw,
            EndRound = _round, EndTimeSeconds = (int)RoundSeconds,
            Stats = BuildStats(), PlayByPlay = _log, RoundScores = new List<RoundScore>(),
        };
    }

    /// <summary>
    /// One overtime period after a no-judges draw at the limit (the booker's call: a tournament
    /// final). No rest: the fighters go straight on, same rules, as round <c>rounds + 1</c>.
    /// </summary>
    private void RunOvertime()
    {
        _overtimeRan = true;
        if (Logging)
        {
            _clock = RoundSeconds;
            Say("Time! Nobody could finish it, and a final needs a winner: they will fight on.");
        }
        _round = _rounds + 1;
        RoundSeconds = _overtimeSeconds;
        if (Logging) Header($"--- Round {_round}: overtime, {Words((int)Math.Round(RoundSeconds / 60))} minutes ---");
        RunRound();
    }

    /// <summary>The single long round's header. Only the round number is a digit: the broadcast
    /// window reads every digit in a "---" line as the round number.</summary>
    private string SingleRoundHeader() => $"--- Round 1: a single {Words((int)Math.Round(RoundSeconds / 60))}-minute round ---";

    /// <summary>The cage words in the stock lines, put into this room (a roped ring, or an open floor).</summary>
    private string InTheRoom(string line)
    {
        bool ring = _rules.Enclosure == Enclosure.Ring;
        return line
            .Replace("ties up against the cage", ring ? "ties up against the ropes" : "ties up in close")
            .Replace("takedown against the fence", ring ? "takedown against the ropes" : "takedown from the clinch")
            .Replace("defends the takedown against the cage", ring ? "defends the takedown against the ropes" : "defends the takedown in the clinch")
            .Replace("takedown attempt along the fence", ring ? "takedown attempt along the ropes" : "takedown attempt in the clinch")
            .Replace(" on the fence with a flurry", ring ? " on the ropes with a flurry" : " with a flurry")
            .Replace("wall-walks back up", ring ? "climbs back up using the ropes" : "scrambles back to the feet")
            .Replace("looks for a wall-walk", ring ? "reaches for the ropes to climb up" : "looks for a way back up");
    }

    private static readonly string[] Ones =
        { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
          "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
    private static readonly string[] Tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty" };

    /// <summary>A number in words, 0-60 (larger falls back to "many").</summary>
    internal static string Words(int n) =>
        n < 0 || n > 60 ? "many" : n < 20 ? Ones[n] : n % 10 == 0 ? Tens[n / 10] : $"{Tens[n / 10]}-{Ones[n % 10]}";

    // --- Text -----------------------------------------------------------------

    private string CustomIntro()
    {
        var parts = new List<string>(4)
        {
            _rounds == 1 ? $"one round of {Minutes(RoundSeconds)}" : $"{_rounds} rounds of {Minutes(RoundSeconds)}",
            _rules.Judges ? "judges at ringside" : "no judges",
        };
        if (_rules.Gloves != GloveType.Modern)
            parts.Add(_rules.Gloves switch
            {
                GloveType.Bare => "bare hands",
                GloveType.ThinOpenFinger => "thin open-fingered gloves",
                _ => "gloves optional",
            });
        if (_rules.Enclosure == Enclosure.Ring) parts.Add("a roped ring");
        else if (_rules.Enclosure == Enclosure.OpenFloor) parts.Add("an open floor");
        if (_rules.RopeEscapes > 0) parts.Add($"{_rules.RopeEscapes} rope escapes each");
        if (_rules.OpenWeight) parts.Add("no weight limit");
        return $"{_s[0].F.FullName} vs {_s[1].F.FullName} — {string.Join(", ", parts)}.";
    }

    private void TaleOfTheTape()
    {
        if (_size)
        {
            int big = _s[0].WeightKg >= _s[1].WeightKg ? 0 : 1;
            double gap = _s[big].WeightKg - _s[1 - big].WeightKg;
            if (gap >= 8)
                Header($"Tale of the tape: {Name(big)} has {Math.Round(gap)} kg ({Math.Round(gap * 2.20462)} lb) on {Name(1 - big)}.");
        }
        if (_rules.Gloves == GloveType.Optional)
            for (int i = 0; i < 2; i++)
                Header(_s[i].Glove == GloveType.Bare
                    ? $"{Name(i)} comes out bare-handed."
                    : $"{Name(i)} has pulled on a pair of thin gloves.");
        for (int i = 0; i < 2; i++)
            if (_s[i].Health < 99 || _s[i].Stamina < 99)
                Header($"{Name(i)} is not fresh: {Pos(i)} last fight tonight is still in {Pos(i)} legs.");
    }

    private void SayRestrictedGroundStrike(int a, bool heavy, bool restricted)
    {
        string what = restricted
            ? (heavy ? "heavy palm strikes" : "short punches to the body")
            : (heavy ? "heavy punches" : "short punches");
        switch (V(2))
        {
            case 0: Say($"{Name(a)} lands {what} from {GroundName()}{(heavy ? "!" : ".")}"); break;
            default: Say($"{Name(a)} works {what} from {GroundName()}{(heavy ? "!" : ".")}"); break;
        }
    }

    private static string Minutes(double seconds)
    {
        int m = (int)Math.Round(seconds / 60);
        return m == 1 ? "1 minute" : $"{m} minutes";
    }
}

/// <summary>
/// The size term for open-weight fights: every tunable in one place. Off (and exactly neutral) unless
/// the rule set says <see cref="FightRules.OpenWeight"/> or the bout carries <see cref="FightParams.BandedSizeTerm"/>
/// (on today's rules, only the relative terms: Bout.ApplyRelativeSize). The input is each man's natural weight
/// (<see cref="Fighter.NaturalWeightLbs"/>), falling back to the middle of his weight class.
///
/// The relative edge is s = ln(my weight / his weight), capped at +-<see cref="MaxEdge"/>; at equal
/// weight s = 0 and every relative effect is exactly neutral. Reference points: 110 kg vs 62 kg is
/// s = 0.57; 102 vs 83 is 0.21; 168 vs 83 is 0.70.
/// </summary>
public static class SizeTerm
{
    /// <summary>
    /// Strike power in open weight comes from natural weight only (the weight-class table is not used):
    /// <list type="bullet">
    /// <item>Absolute, from the man's own kg: (kg / <see cref="PowerRefKg"/>) ^ PowerAbsK (x the women's
    /// factor). 0.4 is the smooth version of today's division table (54 kg 0.89, 73 kg 1.00, 108 kg 1.17),
    /// so two big men finish each other as heavyweights do and two small men as flyweights do.</item>
    /// <item>Relative, the mismatch: x exp(PowerRelK x s). 110 v 62: x1.77 / x0.56.</item>
    /// </list>
    /// Flash-KO odds use Power squared.
    /// </summary>
    public const double PowerAbsK = 0.4, PowerRelK = 0.7;
    public const double PowerRefKg = 73, PowerMinKg = 45, PowerMaxKg = 200;
    public const double WomensPower = 0.88;
    /// <summary>Chin points per unit of s: the bigger man absorbs more. 110 v 62: +-8.</summary>
    public const double ChinPts = 30;
    /// <summary>Grappling points per unit of s on takedowns, clinch, passes, sweeps and getting up. 110 v 62: +-16.</summary>
    public const double GrapplePts = 60;
    /// <summary>Strike-accuracy points per unit of s, to the SMALLER man (he is quicker). 110 v 62: +-5.7.</summary>
    public const double SpeedPts = 8;
    /// <summary>Activity (who leads the exchanges): x exp(-ActivityK x s): the smaller man is busier.</summary>
    public const double ActivityK = 0.25;
    /// <summary>Submission-defence points per unit of s (strength in the hold; technique still decides most).</summary>
    public const double SubPts = 8;
    /// <summary>Relative gas: drain x exp(GasRelK x s): moving the bigger body costs more.</summary>
    public const double GasRelK = 0.15;
    /// <summary>Absolute gas for very big men: drain x (1 + HeavyGasSlope x (kg / HeavyGasFromKg - 1)) over HeavyGasFromKg.
    /// 120 kg: x1.3; 168 kg: x2.0. A 168 kg man in a 20-minute round is spent by the middle of it.</summary>
    public const double HeavyGasFromKg = 100, HeavyGasSlope = 1.5;
    /// <summary>The man underneath drains x exp(PinnedK x s_top) carrying a bigger man.</summary>
    public const double PinnedK = 0.8;
    /// <summary>Cap on |s| (a weight ratio of about 2.7).</summary>
    public const double MaxEdge = 1.0;

    public static double Edge(double myKg, double hisKg) =>
        myKg == hisKg ? 0.0 : Math.Clamp(Math.Log(myKg / hisKg), -MaxEdge, MaxEdge);

    public static double OpenPower(Fighter f, double kg) =>
        (f.Sex == Sex.Female ? WomensPower : 1.0) * Math.Pow(Math.Clamp(kg, PowerMinKg, PowerMaxKg) / PowerRefKg, PowerAbsK);

    /// <summary>The nearest weight class to a natural weight (how a builder labels an open-weight man).</summary>
    public static WeightClass NearestClass(double kg) =>
        Enum.GetValues<WeightClass>().OrderBy(w => Math.Abs(ClassMidKg(w) - kg)).First();

    public static double HeavyGas(double kg) => kg <= HeavyGasFromKg ? 1.0 : 1 + HeavyGasSlope * (kg / HeavyGasFromKg - 1);

    /// <summary>Natural weight in kg: <see cref="Fighter.NaturalWeightLbs"/>, else the middle of the weight class.</summary>
    public static double WeightKg(Fighter f) =>
        f.NaturalWeightLbs > 0 ? f.NaturalWeightLbs / 2.20462 : ClassMidKg(f.WeightClass);

    public static double ClassMidKg(WeightClass wc) => wc switch
    {
        WeightClass.Strawweight => 54,
        WeightClass.Flyweight => 58,
        WeightClass.Bantamweight => 63,
        WeightClass.Featherweight => 68,
        WeightClass.Lightweight => 73,
        WeightClass.Welterweight => 80,
        WeightClass.Middleweight => 87,
        WeightClass.LightHeavyweight => 95,
        _ => 108,
    };
}

/// <summary>What the hands wear, and what it does in the cage. All tunables here.</summary>
public static class Gloves
{
    /// <summary>A hurt hand hits this hard for the rest of the night.</summary>
    public const double HurtHand = 0.75;

    /// <summary>Strike damage: bare-handed men hold back their power shots to save their hands.</summary>
    public static double DamageMul(GloveType g) => g switch { GloveType.Bare => 0.92, GloveType.ThinOpenFinger => 1.0, _ => 1.0 };

    /// <summary>Cut chance multiplier for shots thrown in these gloves: bare knuckles open the skin.</summary>
    public static double CutMul(GloveType g) => g switch { GloveType.Bare => 1.8, GloveType.ThinOpenFinger => 1.3, _ => 1.0 };

    /// <summary>Chance per landed power punch that the puncher hurts his own hand.</summary>
    public static double HandInjury(GloveType g) => g switch { GloveType.Bare => 0.012, GloveType.ThinOpenFinger => 0.004, _ => 0.0 };

    /// <summary>The glove a fighter wears under a rule: with gloves optional, strikers pull on thin
    /// gloves to save their hands and grapplers fight bare (no roll: a public read of his game).</summary>
    public static GloveType For(GloveType rule, Fighter f) => rule switch
    {
        GloveType.Optional => f.Tendencies.StrikingPreference >= 50 ? GloveType.ThinOpenFinger : GloveType.Bare,
        _ => rule,
    };
}
