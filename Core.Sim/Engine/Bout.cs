using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Engine;

/// <summary>
/// One simulated bout. Holds all mutable fight state and runs the exchange loop,
/// so each <see cref="FightEngine.Simulate"/> call is self-contained and the
/// engine itself stays stateless. Everything random flows from the one seeded
/// <see cref="SimRandom"/> it is given, so a seed reproduces the fight exactly: the
/// exchange rolls draw from it directly, and the stoppage rolls (flash knockouts,
/// staying down, the referee, cuts, the doctor, the corner) from <see cref="_ss"/>, a
/// keyed sub-stream seeded by one draw at the opening bell.
///
/// Commentary rule: the play-by-play is built only when it was asked for, and
/// NEVER draws from <see cref="_rng"/> — a wording choice comes from a hash of
/// (text seed, line number) via <see cref="V"/>. The few draws that look like
/// text (the strike and hold names) are kept exactly where they always were
/// and happen whether or not text is on, so results are identical with text on
/// or off (pinned by the golden-master tests).
/// </summary>
internal sealed class Bout
{
    // --- Tuning constants (the dials to turn when balancing) ----------------
    private readonly double RoundSeconds;     // from the promotion's ruleset (300 standard)
    private const double StrikeK = 0.038;     // striking accuracy logistic slope
    private const double TakedownK = 0.045;
    private const double SubmissionK = 0.045;
    private const double PassK = 0.05;
    private const double SweepK = 0.05;
    private const double StandupK = 0.045;

    // --- Stoppages (plan step 3). Rolls marked [ss] use the stoppage stream _ss. ---
    // Knockdown roll on a landed power shot: Logistic(impact*KdImpact + (100-Chin)*KdChin
    // - Health*KdHealth + hurt bonus - KdOffset, KdSlope). Fewer than before, and less tied to
    // accumulated damage, so a knockdown can come early (R1 is the top finishing round).
    private const double KdImpact = 2.2, KdChin = 0.4, KdHealth = 0, KdHurtBonus = 18, KdOffset = 69, KdSlope = 0.06;
    // Flash knockout [ss]: any landed power shot, scaled by power against chin and division.
    private const double FlashBase = 0.0045, FlashSpread = 25;
    // Out cold once dropped [ss]: Logistic(impact*OutImpact + (100-Chin)*OutChin - Health*OutHealth - OutOffset, OutSlope).
    private const double OutImpact = 1.6, OutChin = 0.3, OutHealth = 0.15, OutOffset = 47, OutSlope = 0.09;
    // Referee stoppage [ss] once a fighter is hurt or worn down: the shots he eats without
    // answering (Barrage) plus how worn he is. Logistic(Barrage + (StopHealth-Health)*StopWorn - StopOffset, StopSlope).
    private const double StopHealth = 50, StopWorn = 0.6, StopOffset = 20, StopSlope = 0.16, StopPosition = 5, StopGroundGate = 10;
    // After a knockdown the attacker may pounce [ss]; the referee may stop it right there.
    private const double PounceStopOffset = 43;
    // Cuts [ss]: chance a landed power shot opens one; the doctor looks between rounds.
    private const double CutOpen = 0.04, CutWorsen = 0.5;
    /// <summary>Doctor's chance per cut level over 2, checked between rounds [ss].</summary>
    private const double DoctorStop = 0.3;
    /// <summary>Below this health between rounds, a corner may pull its fighter [ss].</summary>
    private const double CornerHealth = 30;
    // Submissions: the tap roll's offset (was 48), the extra cost from the bottom, the help a hurt opponent gives.
    // From the bottom, Flexibility is most of the threat (SubFlex per point; was 0.05).
    private const double SubOffset = 58, SubBottomPenalty = 16, SubHurtBonus = 12, SubFlex = 0.25;

    // --- Scoring ---
    /// <summary>A judge scores a round 10-10 only when it is this close on his own (noisy) read.</summary>
    private const double EvenRound = 0.05;
    /// <summary>10-8: a judge's dominance read (margin + knockdowns + damage edge) at or over this.</summary>
    private const double TenEightDominance = 24;

    private readonly SimRandom _rng;
    /// <summary>
    /// The stoppage stream: every roll this step added (flash knockouts, staying down, the
    /// referee, cuts, the doctor, the corner) draws from here, never from <see cref="_rng"/>.
    /// Seeded by ONE draw from the bout stream at the opening bell.
    /// </summary>
    private readonly SimRandom _ss;
    private readonly int _rounds;
    private readonly Side[] _s = new Side[2];
    private readonly List<string>? _log;
    private readonly ulong _textSeed;

    private Phase _phase = Phase.Standing;
    private int _top;                 // ground: which side is on top
    private GroundPos _ground;
    private int _round = 1;
    private double _clock;            // seconds elapsed in the current round

    private Finish? _finish;

    public Bout(Fighter red, Fighter blue, FightParams p, SimRandom rng)
    {
        _rng = rng;
        _ss = new SimRandom(SimRandom.Mix(rng.NextULong(), 0x5709_9A6E_0003UL));
        _rounds = Math.Max(1, p.ScheduledRounds);
        RoundSeconds = Math.Clamp(p.RoundSeconds, 60, 600);
        _s[0] = new Side(red, _rounds);
        _s[1] = new Side(blue, _rounds);
        _log = p.GeneratePlayByPlay ? new List<string>(64) : null;
        _textSeed = SimRandom.Mix(p.TextSeed, ((ulong)(uint)red.Id.Value << 32) | (uint)blue.Id.Value);
    }

    // --- Entry point --------------------------------------------------------

    public FightOutcome Run()
    {
        if (Logging) Header($"{_s[0].F.FullName} vs {_s[1].F.FullName} — {_rounds} rounds.");

        for (_round = 1; _round <= _rounds && _finish is null; _round++)
        {
            if (Logging) Header($"--- Round {_round} ---");
            RunRound();
            if (_finish is not null) break;
            if (Logging) BellLines();
            if (_round < _rounds)
            {
                BetweenRounds();
                if (_finish is not null) break;
                RecoverBetweenRounds();
            }
        }

        return _finish is { } f ? FinishOutcome(f) : DecisionOutcome();
    }

    // --- Round loop ---------------------------------------------------------

    private void RunRound()
    {
        _clock = 0;
        ResetPositionForNewRound();
        _s[0].Barrage = _s[1].Barrage = 0;

        while (_clock < RoundSeconds && _finish is null)
        {
            double dt = ExchangeDuration();
            if (_clock + dt > RoundSeconds) dt = RoundSeconds - _clock;
            _clock += dt;

            // Control time + extra bottom drain accrue whenever the fight is grounded.
            if (_phase == Phase.Ground)
            {
                _s[_top].Stats.ControlTimeSeconds += (int)dt;
                AddScore(_top, 0.02 * dt);
                Drain(1 - _top, 0.9 * dt / 60.0);
            }

            if (Logging) RecoveredLines();
            int actor = ChooseActor();
            ResolveExchange(actor);
            PassiveDrain(dt);
            TickHurt();
        }
    }

    /// <summary>Let a hurt fighter clear the woozy state if he weathers a few exchanges.</summary>
    private void TickHurt()
    {
        for (int i = 0; i < 2; i++)
        {
            if (!_s[i].Hurt) continue;
            if (--_s[i].HurtTimer <= 0)
            {
                _s[i].Hurt = false;
                _s[i].Barrage = 0;      // weathered it: the referee's count starts again
                // Reported at the start of the NEXT exchange (when it first counts), not
                // stamped on the same second as the exchange that just happened.
                _s[i].RecoveredPending = true;
            }
        }
    }

    private void RecoveredLines()
    {
        for (int i = 0; i < 2; i++)
        {
            if (!_s[i].RecoveredPending) continue;
            _s[i].RecoveredPending = false;
            switch (V(3))
            {
                case 0: Say($"{Name(i)} has recovered and is fighting back."); break;
                case 1: Say($"{Name(i)} has shaken off the cobwebs."); break;
                default: Say($"{Name(i)}'s legs are back under {Obj(i)}."); break;
            }
        }
    }

    /// <summary>The bell: a fighter still hurt is saved by it (text only; the recovery itself is unchanged).</summary>
    private void BellLines()
    {
        for (int i = 0; i < 2; i++)
        {
            _s[i].RecoveredPending = false;
            if (_s[i].Hurt && _round < _rounds) Say($"Saved by the bell — {Name(i)} goes back to the corner still hurt.");
        }
    }

    private void ResetPositionForNewRound()
    {
        // Fighters are stood up between rounds.
        _phase = Phase.Standing;
        _ground = GroundPos.Guard;
    }

    private double ExchangeDuration()
    {
        double d = _phase switch
        {
            Phase.Standing => _rng.NextGaussian(14, 5),
            Phase.Clinch => _rng.NextGaussian(16, 5),
            _ => _rng.NextGaussian(22, 7),
        };
        return d < 4 ? 4 : d;
    }

    // --- Actor selection ----------------------------------------------------

    private int ChooseActor()
    {
        if (_phase == Phase.Ground)
        {
            // Top fighter drives most of the action; an active guard/scrambler
            // steals initiative from the bottom now and then.
            Side bot = _s[1 - _top];
            double botInit = 0.35 + (bot.F.Grappling.BottomGame + bot.F.Grappling.Scrambling) / 400.0;
            return _rng.Chance(botInit) ? 1 - _top : _top;
        }

        double a = Activity(0), b = Activity(1);
        return _rng.Chance(a / (a + b)) ? 0 : 1;
    }

    private double Activity(int i)
    {
        var f = _s[i].F;
        double stamina = _s[i].Stamina;
        // Aggression and stamina drive output; committed counter-fighters lead less.
        double baseAct = 0.4 + f.Tendencies.Aggression / 100.0;
        baseAct *= 0.5 + 0.5 * stamina / 100.0;
        baseAct *= 1.15 - 0.3 * (f.Tendencies.Counterfighting / 100.0);
        if (_s[i].Hurt) baseAct *= 0.5;      // a hurt fighter covers up
        return Math.Max(0.05, baseAct);
    }

    // --- Exchange dispatch --------------------------------------------------

    private void ResolveExchange(int a)
    {
        switch (_phase)
        {
            case Phase.Standing: StandingAction(a); break;
            case Phase.Clinch: ClinchAction(a); break;
            default: GroundAction(a); break;
        }
    }

    // --- Standing -----------------------------------------------------------

    private void StandingAction(int a)
    {
        var f = _s[a].F;
        // Weight the options by tendency and skill: strike-first fighters throw,
        // grapplers look for the takedown, clinch fighters tie up.
        double strike = 0.5 + f.Tendencies.StrikingPreference / 100.0;
        double takedown = 0.2 + f.Grappling.TakedownOffense / 120.0
                              + (1 - f.Tendencies.StrikingPreference / 100.0) * 0.6;
        double clinch = 0.1 + f.Grappling.ClinchControl / 200.0;
        // Finish the hurt opponent rather than shooting.
        if (_s[1 - a].Hurt) { strike *= 3; takedown *= 0.3; clinch *= 0.3; }
        // NOTE (plan step 3): a just-dropped fighter can still be the actor and shoot or
        // tie up. Changing that changes results, so for now the text says what it is —
        // a hurt fighter grabbing hold to survive.

        switch (_rng.WeightedIndex(stackalloc[] { strike, takedown, clinch }))
        {
            case 0: StrikeExchange(a, Phase.Standing); break;
            case 1: TakedownAttempt(a); break;
            default: ClinchEntry(a); break;
        }
    }

    private void ClinchEntry(int a)
    {
        var atk = _s[a].F; var def = _s[1 - a].F;
        double p = Logistic(Eff(atk.Grappling.ClinchControl, _s[a].Stamina)
                          - Eff(def.Grappling.TakedownDefense, _s[1 - a].Stamina), TakedownK);
        Drain(a, 0.6);
        if (_rng.Chance(p))
        {
            _phase = Phase.Clinch;
            if (!Logging) return;
            if (_s[a].Hurt) { Say($"Hurt, {Name(a)} grabs hold of {Name(1 - a)} to buy time."); return; }
            switch (V(3))
            {
                case 0: Say($"{Name(a)} closes the distance and presses {Name(1 - a)} into the clinch."); break;
                case 1: Say($"{Name(a)} walks {Name(1 - a)} down and ties up against the cage."); break;
                default: Say($"{Name(a)} grabs a collar tie and drags {Name(1 - a)} into the clinch."); break;
            }
        }
        else if (Logging)
        {
            switch (V(3))
            {
                case 0: Say($"{Name(a)} tries to tie up but {Name(1 - a)} circles out."); break;
                case 1: Say($"{Name(1 - a)} frames off and keeps {Name(a)} at range."); break;
                default: Say($"{Name(a)} reaches for the clinch; {Name(1 - a)} pivots away."); break;
            }
        }
    }

    // --- Clinch -------------------------------------------------------------

    private void ClinchAction(int a)
    {
        var f = _s[a].F;
        double strike = 0.4 + f.Striking.ClinchStriking / 120.0;
        double takedown = 0.3 + f.Grappling.ClinchControl / 120.0 + f.Grappling.TakedownOffense / 200.0;
        double separate = 0.25 + (1 - f.Tendencies.Aggression / 100.0) * 0.4;
        if (_s[1 - a].Hurt) { strike *= 2.5; separate *= 0.3; }

        switch (_rng.WeightedIndex(stackalloc[] { strike, takedown, separate }))
        {
            case 0: StrikeExchange(a, Phase.Clinch); break;
            case 1: ClinchTakedown(a); break;
            default:
                _phase = Phase.Standing;
                if (Logging)
                    switch (V(3))
                    {
                        case 0: Say($"{Name(a)} breaks the clinch and the fighters reset."); break;
                        case 1: Say($"{Name(a)} pushes off and they're back in open space."); break;
                        default: Say($"{Name(a)} peels the grip away and steps out of the clinch."); break;
                    }
                break;
        }
    }

    private void ClinchTakedown(int a)
    {
        var atk = _s[a].F; var def = _s[1 - a].F;
        double p = Logistic(Eff(atk.Grappling.ClinchControl, _s[a].Stamina)
                          - Eff(def.Grappling.TakedownDefense, _s[1 - a].Stamina) + 6, TakedownK);
        Drain(a, 1.2); Drain(1 - a, 0.6);
        _s[a].Stats.TakedownsAttempted++;
        if (_rng.Chance(p))
        {
            _s[a].Stats.TakedownsLanded++;
            GoToGround(a);
            AddScore(a, 3);
            if (Logging)
                switch (V(3))
                {
                    case 0: Say($"{Name(a)} lands a trip from the clinch and puts {Name(1 - a)} on the mat."); break;
                    case 1: Say($"{Name(a)} hits a body-lock takedown against the fence."); break;
                    default: Say($"{Name(a)} throws {Name(1 - a)} to the canvas from the clinch!"); break;
                }
        }
        else if (Logging)
        {
            switch (V(3))
            {
                case 0: Say($"{Name(1 - a)} defends the takedown against the cage."); break;
                case 1: Say($"{Name(a)} looks for the trip but {Name(1 - a)} keeps {Pos(1 - a)} balance."); break;
                default: Say($"{Name(1 - a)} stuffs {Name(a)}'s takedown attempt along the fence."); break;
            }
        }
    }

    // --- Takedown from open space ------------------------------------------

    private void TakedownAttempt(int a)
    {
        var atk = _s[a].F; var def = _s[1 - a].F;
        double p = Logistic(Eff(atk.Grappling.TakedownOffense, _s[a].Stamina)
                          - Eff(def.Grappling.TakedownDefense, _s[1 - a].Stamina), TakedownK);
        Drain(a, 1.6); Drain(1 - a, 0.8);
        _s[a].Stats.TakedownsAttempted++;
        bool hurt = _s[a].Hurt;
        if (_rng.Chance(p))
        {
            _s[a].Stats.TakedownsLanded++;
            GoToGround(a);
            AddScore(a, 3);
            if (!Logging) return;
            if (hurt) { Say($"Still hurt, {Name(a)} dives for a desperate takedown — and gets it!"); return; }
            switch (V(4))
            {
                case 0: Say($"{Name(a)} shoots in and completes the takedown."); break;
                case 1: Say($"{Name(a)} changes levels and drives {Name(1 - a)} to the mat."); break;
                case 2: Say($"{Name(a)} snatches a double-leg and puts {Name(1 - a)} down."); break;
                default: Say($"Takedown, {Name(a)} — {Name(1 - a)} is on {Pos(1 - a)} back."); break;
            }
        }
        else
        {
            AddScore(1 - a, 0.5);
            if (!Logging) return;
            if (hurt) { Say($"Still hurt, {Name(a)} shoots a desperate takedown but {Name(1 - a)} sprawls on it."); return; }
            switch (V(4))
            {
                case 0: Say($"{Name(a)} shoots but {Name(1 - a)} sprawls and defends."); break;
                case 1: Say($"{Name(1 - a)} sprawls hard on {Name(a)}'s shot."); break;
                case 2: Say($"{Name(a)} goes for the legs; {Name(1 - a)} stuffs it."); break;
                default: Say($"{Name(1 - a)} reads the shot early and stays upright."); break;
            }
        }
    }

    // --- Striking (standing or clinch) -------------------------------------

    private void StrikeExchange(int a, Phase where)
    {
        var atk = _s[a].F; var def = _s[1 - a].F;
        double staA = _s[a].Stamina, staD = _s[1 - a].Stamina;

        // The standing accuracy blend in real numbers (it used integer division, which made
        // Speed and KickTechnique move it in identical whole-point steps — review bug 13).
        double accuracy = where == Phase.Clinch
            ? Eff(atk.Striking.ClinchStriking, staA)
            : Eff((atk.Striking.PunchTechnique * 2 + atk.Striking.KickTechnique + atk.Striking.Footwork + atk.Physical.Speed) / 5.0, staA);
        double defense = Eff((def.Striking.StrikingDefense * 2 + def.Striking.Footwork + def.Mental.FightIQ) / 4, staD);
        if (_s[1 - a].Hurt) defense *= 0.6;      // hurt fighters get hit more

        _s[a].Stats.SignificantStrikesAttempted++;
        double pLand = Logistic(accuracy - defense, StrikeK);
        if (!_rng.Chance(pLand))
        {
            if (Logging) SayMiss(a, where);
            return;
        }

        _s[a].Stats.SignificantStrikesLanded++;
        _s[a].Barrage = 0;                       // landing back breaks the referee's count on you

        // Is this a committed power shot or a lighter scoring blow? Heavier divisions hit harder.
        bool power = _rng.Chance(0.28 + atk.Tendencies.FinishInstinct / 300.0);
        double powerScale = 0.6 + 0.8 * (Math.Max(atk.Striking.PunchPower, atk.Striking.KickPower) / 100.0);
        double dmg = power
            ? Math.Max(2, _rng.NextGaussian(12, 4)) * powerScale * _s[a].Power
            : Math.Max(1, _rng.NextGaussian(3.5, 1.2));

        Hurt(1 - a, dmg);
        AddScore(a, power ? 2.2 : 1.0);
        _s[a].RoundDmg[_round - 1] += dmg;

        if (power && TryKnockdown(a, dmg, where)) return;   // dropped (and maybe finished)

        // The strike name is a draw from the bout stream, made whether or not text is on.
        string shot = StrikeName(atk, where);
        if (Logging) SayLanded(a, power, shot);
        // A power shot may open a cut (elbows and knees in the clinch most); any shot can
        // worsen an open one. Then the referee looks at a hurt fighter eating shots.
        if (power) MaybeCut(1 - a, where == Phase.Clinch ? 2.5 : 1.0);
        else if (_s[1 - a].Cut > 0) MaybeCut(1 - a, 0);
        RefereeLooks(a, dmg, power, ground: false);
    }

    private void SayLanded(int a, bool power, string shot)
    {
        if (power)
            switch (V(4))
            {
                case 0: Say($"{Name(a)} lands a hard {shot} flush."); break;
                case 1: Say($"{Name(a)} cracks {Name(1 - a)} with a heavy {shot}!"); break;
                case 2: Say($"Big {shot} from {Name(a)} — {Name(1 - a)} felt that one."); break;
                default: Say($"{Name(a)} connects with a thudding {shot}."); break;
            }
        else
            switch (V(4))
            {
                case 0: Say($"{Name(a)} scores with a crisp {shot}."); break;
                case 1: Say($"{Name(a)} lands a clean {shot}."); break;
                case 2: Say($"{Name(a)} touches {Name(1 - a)} with a quick {shot}."); break;
                default: Say($"{Name(a)} finds the target with {Art(shot)} {shot}."); break;
            }
    }

    private void SayMiss(int a, Phase where)
    {
        // The missed strike's name is chosen by hash: no draw.
        string shot = TextStrikeName(_s[a].F, where);
        if (where == Phase.Clinch)
        {
            switch (V(3))
            {
                case 0: Say($"{Name(a)} digs for {Art(shot)} {shot} but {Name(1 - a)} blocks it."); break;
                case 1: Say($"{Name(1 - a)} ties up {Name(a)}'s arms and smothers the {shot}."); break;
                default: Say($"{Name(a)} throws {Art(shot)} {shot} in the clinch; {Name(1 - a)} turns it aside."); break;
            }
            return;
        }
        switch (V(4))
        {
            case 0: Say($"{Name(a)} throws but {Name(1 - a)} slips it."); break;
            case 1: Say($"{Name(a)} swings {Art(shot)} {shot} and {Name(1 - a)} makes {Obj(a)} miss."); break;
            case 2: Say($"{Name(1 - a)} blocks {Name(a)}'s {shot}."); break;
            default: Say($"{Name(a)} misses with the {shot}."); break;
        }
    }

    /// <summary>
    /// The landed strike's name: ONE draw from the bout stream (as it always was —
    /// removing it would reshuffle every bout), used to index the vocabulary that fits
    /// where the fight is.
    /// </summary>
    private string StrikeName(Fighter f, Phase where)
    {
        ulong u = _rng.NextULong();
        var list = where == Phase.Clinch ? ClinchStrikes : PrefersPunches(f) ? Punches : Kicks;
        return list[(int)(u % (ulong)list.Length)];
    }

    private string TextStrikeName(Fighter f, Phase where)
    {
        var list = where == Phase.Clinch ? ClinchStrikes : PrefersPunches(f) ? Punches : Kicks;
        return list[V(list.Length)];
    }

    private static bool PrefersPunches(Fighter f) =>
        f.Tendencies.StrikingPreference > 55 || f.Striking.PunchPower >= f.Striking.KickPower;

    private static readonly string[] Punches = { "right hand", "left hook", "jab-cross", "overhand right", "uppercut", "counter left" };
    private static readonly string[] Kicks = { "head kick", "body kick", "low kick", "front kick", "spinning kick" };
    private static readonly string[] ClinchStrikes = { "knee to the body", "short elbow", "uppercut", "knee", "short hook" };

    /// <summary>
    /// A landed power shot: does it drop him? A flash knockout [ss] can come off any power
    /// shot (power against chin, heavier divisions more); otherwise the knockdown roll, which
    /// leans on the shot and the chin far more than on accumulated damage. Once he is down:
    /// out cold (KO) [ss], or the attacker jumps on him [ss] and the referee may stop it there
    /// (TKO), or the fight goes on — on the ground with the attacker on top, or waved back up.
    /// </summary>
    private bool TryKnockdown(int a, double dmg, Phase where)
    {
        int d = 1 - a;
        var atk = _s[a].F; var def = _s[d].F;
        bool wasHurt = _s[d].Hurt;
        int power = Math.Max(atk.Striking.PunchPower, atk.Striking.KickPower);
        double pFlash = FlashBase * _s[a].Power * _s[a].Power
                      * Math.Exp((power - def.Physical.Chin) / FlashSpread) * (wasHurt ? 2 : 1);
        bool flash = _ss.Chance(pFlash);
        double margin = dmg * KdImpact + (100 - def.Physical.Chin) * KdChin - _s[d].Health * KdHealth
                      + (wasHurt ? KdHurtBonus : 0) - KdOffset;
        bool kd = _rng.Chance(Logistic(margin, KdSlope));    // the bout stream's knockdown roll, as ever
        if (!kd && !flash) return false;

        _s[a].Stats.KnockdownsScored++;        // credited to the fighter who landed it
        _s[d].RoundKD[_round - 1]++;           // the dropped fighter, for 10-8 scoring
        AddScore(a, 4);
        double extra = Math.Max(0, _rng.NextGaussian(10, 3));
        Hurt(d, extra);
        _s[a].RoundDmg[_round - 1] += extra;
        string shot = StrikeName(atk, where);

        double pOut = Logistic(dmg * OutImpact + (100 - def.Physical.Chin) * OutChin - _s[d].Health * OutHealth
                               + (wasHurt ? 10 : 0) - OutOffset, OutSlope);
        bool outCold = flash || _s[d].Health <= 0 || _ss.Chance(pOut);
        if (outCold)
        {
            RecordFinish(a, FightMethod.Knockout, flash ? "KO: flash" : "KO: knockdown");
            if (Logging)
            {
                if (flash)
                    switch (V(3))
                    {
                        case 0: Say($"OH! {Name(a)} lands {Art(shot)} {shot} and {Name(d)} is out before {Subj(d)} hits the canvas!"); break;
                        case 1: Say($"Out of nowhere — {Art(shot)} {shot} from {Name(a)} and {Name(d)} is out cold!"); break;
                        default: Say($"{Name(a)} catches {Name(d)} clean with {Art(shot)} {shot}, and it's over in an instant!"); break;
                    }
                else
                {
                    switch (V(3))
                    {
                        case 0: Say($"BIG SHOT! {Name(a)} drops {Name(d)} with {Art(shot)} {shot}!"); break;
                        case 1: Say($"DOWN GOES {Name(d)}! {Name(a)} lands {Art(shot)} {shot}!"); break;
                        default: Say($"{Name(a)} puts {Name(d)} on the canvas with {Art(shot)} {shot}!"); break;
                    }
                    switch (V(2))
                    {
                        case 0: Say($"{Name(d)} isn't getting up — {Name(a)} wins by knockout!"); break;
                        default: Say($"That's it — {Name(d)} is out, and {Name(a)} wins by knockout!"); break;
                    }
                }
            }
            return true;
        }

        if (Logging)
            switch (V(3))
            {
                case 0: Say($"BIG SHOT! {Name(a)} drops {Name(d)} with {Art(shot)} {shot}!"); break;
                case 1: Say($"DOWN GOES {Name(d)}! {Name(a)} lands {Art(shot)} {shot}!"); break;
                default: Say($"{Name(a)} puts {Name(d)} on the canvas with {Art(shot)} {shot}!"); break;
            }

        // Down but not out, and now hurt (the bout stream's hurt-timer draw, as ever).
        _s[d].Hurt = true;
        _s[d].RecoveredPending = false;
        _s[d].HurtTimer = _rng.Range(2, 4);

        // The follow-up: a finisher jumps on a dropped man; others step back and wave him up.
        double pPounce = 0.25 + atk.Tendencies.FinishInstinct / 250.0 + atk.Tendencies.Aggression / 500.0;
        if (_ss.Chance(pPounce))
        {
            double dazed = dmg * 0.8 + (100 - def.Physical.Chin) * 0.25 + (60 - _s[d].Health) * 0.4
                         + atk.Tendencies.FinishInstinct * 0.15 - PounceStopOffset;
            if (_ss.Chance(Logistic(dazed, 0.12)))
            {
                RecordFinish(a, FightMethod.TechnicalKnockout, "TKO: follow-up after knockdown");
                if (Logging)
                    switch (V(3))
                    {
                        case 0: Say($"{Name(a)} follows {Obj(d)} down with hammerfists and the referee dives in — it's over!"); break;
                        case 1: Say($"{Name(a)} pounces on the dropped {Name(d)}; the referee has seen enough. TKO!"); break;
                        default: Say($"{Name(d)} tries to cover up on the mat but {Name(a)} is all over {Obj(d)} — the referee stops it!"); break;
                    }
                return true;
            }
            GoToGround(a);
            _ground = GroundPos.HalfGuard;
            if (Logging)
                switch (V(2))
                {
                    case 0: Say($"{Name(a)} pounces and lands on top in half guard; {Name(d)} is tying up to survive."); break;
                    default: Say($"{Name(a)} follows {Obj(d)} to the mat — {Name(d)} grabs hold and hangs on."); break;
                }
            return true;
        }

        if (Logging)
            switch (V(3))
            {
                case 0: Say($"{Name(a)} steps back and waves {Obj(d)} up — {Name(d)} rises on wobbly legs."); break;
                case 1: Say($"{Name(d)} beats the follow-up and gets up, badly hurt."); break;
                default: Say($"{Name(d)} survives — but {Subj(d)} is on wobbly legs!"); break;
            }
        return true;
    }

    /// <summary>
    /// The referee watches a fighter who is hurt or worn down. Every shot he eats without
    /// answering builds the count; enough of it, on a man this hurt, and he steps in [ss].
    /// A fighter with nothing left (health gone) is stopped on the next clean shot. Returns
    /// true when it is stopped.
    /// </summary>
    private bool RefereeLooks(int a, double dmg, bool heavy, bool ground)
    {
        int d = 1 - a;
        var s = _s[d];
        if (_finish is not null || !(s.Hurt || s.Health < StopHealth + (ground ? StopGroundGate : 0))) return false;
        // On the ground every landed shot counts in full (he is pinned under it).
        s.Barrage += heavy || ground ? dmg : dmg * 0.6;
        double x = s.Barrage + (StopHealth - s.Health) * StopWorn - StopOffset
                 + (ground ? (int)_ground * StopPosition : 0) + (s.Health <= 0 ? 100 : 0);
        if (!_ss.Chance(Logistic(x, StopSlope))) return false;

        if (ground)
        {
            RecordFinish(a, FightMethod.TechnicalKnockout, "TKO: ground strikes");
            if (Logging)
                switch (V(2))
                {
                    case 0: Say($"The referee has seen enough — {Name(a)} wins by ground-and-pound TKO!"); break;
                    default: Say($"The referee waves it off! {Name(a)} wins by TKO from {GroundName()}!"); break;
                }
        }
        else
        {
            RecordFinish(a, FightMethod.TechnicalKnockout, "TKO: standing strikes");
            if (Logging)
                switch (V(3))
                {
                    case 0: Say($"{Name(a)} unloads on a wobbled {Name(d)} and the referee jumps in — TKO!"); break;
                    case 1: Say($"{Name(d)} is out on {Pos(d)} feet, not defending, and the referee waves it off!"); break;
                    default: Say($"{Name(a)} pins {Name(d)} on the fence with a flurry and the referee steps in. It's over!"); break;
                }
        }
        return true;
    }

    /// <summary>
    /// A landed power shot may open a cut [ss] (<paramref name="factor"/> &gt; 1 for elbows and
    /// knees); any landed shot can make an open one worse. <paramref name="factor"/> 0 = cannot open.
    /// </summary>
    private void MaybeCut(int d, double factor)
    {
        var s = _s[d];
        if (s.Cut == 0)
        {
            if (factor <= 0 || !_ss.Chance(CutOpen * factor)) return;
            s.Cut = 1;
            if (Logging)
                switch (V(2))
                {
                    case 0: Say($"That opened a cut over {Name(d)}'s eye."); break;
                    default: Say($"Blood! {Name(d)} is cut."); break;
                }
            return;
        }
        if (_ss.Chance(CutWorsen))
        {
            s.Cut++;
            if (Logging && s.Cut == 3) Say($"The cut on {Name(d)} is getting worse — it's pouring now.");
            // A gash this bad: the referee calls time for the doctor mid-round.
            if (s.Cut >= 4 && _finish is null && _ss.Chance(0.5))
            {
                RecordFinish(1 - d, FightMethod.TechnicalKnockout, "TKO: doctor");
                if (Logging) Say($"The referee calls time and brings in the doctor to look at {Name(d)}'s cut… it's over! {Name(1 - d)} wins by TKO (doctor's stoppage).");
            }
        }
    }

    /// <summary>
    /// The minute between rounds: the doctor looks at a bad cut, and a corner may pull a
    /// fighter who is badly beaten [ss]. Either is a TKO at the end of the round just fought.
    /// </summary>
    private void BetweenRounds()
    {
        for (int d = 0; d < 2 && _finish is null; d++)
        {
            var s = _s[d];
            if (s.Cut >= 3 && _ss.Chance(Math.Min(0.8, DoctorStop * (s.Cut - 2))))
            {
                RecordFinish(1 - d, FightMethod.TechnicalKnockout, "TKO: doctor, between rounds", betweenRounds: true);
                if (Logging) Say($"The doctor checks the cut over {Name(d)}'s eye between rounds… and waves it off! {Name(1 - d)} wins by TKO (doctor's stoppage).");
                return;
            }
            if (s.Health < CornerHealth)
            {
                double p = 0.03 + 0.15 * s.RoundKD[_round - 1] + (CornerHealth - s.Health) * 0.008;
                if (_ss.Chance(p))
                {
                    RecordFinish(1 - d, FightMethod.TechnicalKnockout, "TKO: corner", betweenRounds: true);
                    if (Logging) Say($"{Name(d)}'s corner has seen enough — they stop it on the stool. {Name(1 - d)} wins by TKO.");
                    return;
                }
            }
        }
    }

    // --- Ground -------------------------------------------------------------

    private void GroundAction(int a)
    {
        if (a == _top) GroundTopAction(a);
        else GroundBottomAction(a);
    }

    private void GroundTopAction(int a)
    {
        var f = _s[a].F;
        double strike = 0.5 + f.Striking.ClinchStriking / 150.0 + (int)_ground * 0.15;
        double pass = _ground < GroundPos.Mount ? 0.45 + f.Grappling.TopControl / 150.0 : 0.12;
        double sub = 0.05 + f.Grappling.SubmissionOffense / 300.0 + (int)_ground * 0.06;
        if (_s[1 - a].Hurt) strike *= 2.5;

        switch (_rng.WeightedIndex(stackalloc[] { strike, pass, sub }))
        {
            case 0: GroundStrike(a); break;
            case 1: PassGuard(a); break;
            default: SubmissionAttempt(a); break;
        }
    }

    private void GroundBottomAction(int a)
    {
        var f = _s[a].F;
        double sweep = 0.35 + f.Grappling.Scrambling / 150.0 + f.Grappling.BottomGame / 200.0;
        double standup = 0.5 + f.Grappling.Scrambling / 200.0 + f.Tendencies.StrikingPreference / 200.0;
        double sub = 0.08 + f.Grappling.SubmissionOffense / 300.0 + f.Physical.Flexibility / 400.0;

        switch (_rng.WeightedIndex(stackalloc[] { sweep, standup, sub }))
        {
            case 0: Sweep(a); break;
            case 1: StandUp(a); break;
            default: SubmissionAttempt(a); break;
        }
    }

    private void GroundStrike(int a)
    {
        var atk = _s[a].F; var def = _s[1 - a].F;
        double defense = Eff(def.Grappling.BottomGame, _s[1 - a].Stamina) * 0.6 + 20;
        double accuracy = Eff(atk.Striking.ClinchStriking, _s[a].Stamina) + (int)_ground * 6;
        _s[a].Stats.SignificantStrikesAttempted++;
        Drain(a, 0.5);
        if (!_rng.Chance(Logistic(accuracy - defense, StrikeK)))
        {
            if (Logging)
                switch (V(3))
                {
                    case 0: Say($"{Name(a)} postures up but {Name(1 - a)} ties {Obj(a)} up from the bottom."); break;
                    case 1: Say($"{Name(1 - a)} controls {Name(a)}'s wrists and stops the ground strikes."); break;
                    default: Say($"{Name(a)} looks for room to strike but {Name(1 - a)} keeps {Obj(a)} close."); break;
                }
            return;
        }
        _s[a].Stats.SignificantStrikesLanded++;
        _s[a].Barrage = 0;
        bool heavy = _ground >= GroundPos.Mount && _rng.Chance(0.4);
        double dmg = heavy ? Math.Max(3, _rng.NextGaussian(11, 3)) * _s[a].Power : Math.Max(1, _rng.NextGaussian(4, 1.5));
        Hurt(1 - a, dmg);
        AddScore(a, heavy ? 2 : 1);
        _s[a].RoundDmg[_round - 1] += dmg;
        if (Logging)
        {
            if (heavy)
                switch (V(3))
                {
                    case 0: Say($"{Name(a)} lands heavy ground-and-pound from {GroundName()}!"); break;
                    case 1: Say($"{Name(a)} rains down big shots from {GroundName()}!"); break;
                    default: Say($"Heavy elbows from {Name(a)} in {GroundName()}!"); break;
                }
            else
                switch (V(3))
                {
                    case 0: Say($"{Name(a)} works short strikes from {GroundName()}."); break;
                    case 1: Say($"{Name(a)} chips away with short punches from {GroundName()}."); break;
                    default: Say($"{Name(a)} lands a few short elbows from {GroundName()}."); break;
                }
        }

        if (heavy) MaybeCut(1 - a, 2.5);
        else if (_s[1 - a].Cut > 0) MaybeCut(1 - a, 0);
        RefereeLooks(a, dmg, heavy, ground: true);
    }

    private void PassGuard(int a)
    {
        var atk = _s[a].F; var def = _s[1 - a].F;
        double p = Logistic(Eff(atk.Grappling.TopControl, _s[a].Stamina)
                          - Eff((def.Grappling.BottomGame + def.Grappling.Scrambling) / 2, _s[1 - a].Stamina), PassK);
        Drain(a, 0.8); Drain(1 - a, 0.6);
        if (_rng.Chance(p) && _ground < GroundPos.BackControl)
        {
            _ground = (GroundPos)((int)_ground + 1);
            AddScore(a, 1.5);
            if (Logging)
                switch (V(3))
                {
                    case 0: Say($"{Name(a)} advances to {GroundName()}."); break;
                    case 1: Say($"{Name(a)} slides through to {GroundName()}."); break;
                    default: Say($"{Name(a)} passes and settles into {GroundName()}."); break;
                }
        }
        else if (Logging)
        {
            if (_ground == GroundPos.BackControl)
            {
                Say($"{Name(a)} tightens the hooks from back control as {Name(1 - a)} hand-fights.");
                return;
            }
            switch (V(3))
            {
                case 0: Say($"{Name(1 - a)} defends and keeps {Name(a)} in {GroundName()}."); break;
                case 1: Say($"{Name(a)} looks to advance from {GroundName()} but {Name(1 - a)} frames well."); break;
                default: Say($"{Name(1 - a)} blocks the pass and holds {Name(a)} in {GroundName()}."); break;
            }
        }
    }

    private void Sweep(int a)
    {
        var atk = _s[a].F; var def = _s[1 - a].F;
        double p = Logistic(Eff((atk.Grappling.Scrambling + atk.Grappling.BottomGame) / 2, _s[a].Stamina)
                          - Eff(def.Grappling.TopControl, _s[1 - a].Stamina) - 6, SweepK);
        Drain(a, 1.0);
        if (_rng.Chance(p))
        {
            _top = a;
            _ground = GroundPos.Guard;
            AddScore(a, 2);
            if (Logging)
                switch (V(3))
                {
                    case 0: Say($"{Name(a)} sweeps and comes up on top!"); break;
                    case 1: Say($"Reversal! {Name(a)} sweeps {Name(1 - a)} and takes top position."); break;
                    default: Say($"{Name(a)} hits a sweep and lands on top."); break;
                }
        }
        else if (Logging)
        {
            switch (V(3))
            {
                case 0: Say($"{Name(a)} looks to sweep but {Name(1 - a)} stays heavy on top."); break;
                case 1: Say($"{Name(1 - a)} bases out and shuts down {Name(a)}'s sweep."); break;
                default: Say($"{Name(a)} bridges hard but can't move {Name(1 - a)}."); break;
            }
        }
    }

    private void StandUp(int a)
    {
        var atk = _s[a].F; var def = _s[1 - a].F;
        double p = Logistic(Eff(atk.Grappling.Scrambling, _s[a].Stamina)
                          - Eff(def.Grappling.TopControl, _s[1 - a].Stamina) + 8, StandupK);
        Drain(a, 0.9);
        if (_rng.Chance(p))
        {
            _phase = Phase.Standing;
            AddScore(a, 1);
            if (Logging)
                switch (V(3))
                {
                    case 0: Say($"{Name(a)} scrambles up and the fight returns to the feet."); break;
                    case 1: Say($"{Name(a)} wall-walks back up."); break;
                    default: Say($"{Name(a)} gets back to {Pos(a)} feet."); break;
                }
        }
        else if (Logging)
        {
            switch (V(3))
            {
                case 0: Say($"{Name(a)} tries to stand but {Name(1 - a)} drags {Obj(a)} back down."); break;
                case 1: Say($"{Name(1 - a)} keeps {Name(a)} pinned as {Subj(a)} tries to get up."); break;
                default: Say($"{Name(a)} looks for a wall-walk but {Name(1 - a)} holds {Obj(a)} down."); break;
            }
        }
    }

    private void SubmissionAttempt(int a)
    {
        var atk = _s[a].F; var def = _s[1 - a].F;
        bool fromTop = a == _top;
        // Submissions are rare and demand a real position/skill edge: dominant
        // ground positions add the most threat, tapping a competent defender is hard.
        // From the bottom it is harder still (most real taps come from the back or on top),
        // and a hurt man is far easier to choke.
        double posBonus = fromTop ? (int)_ground * 4 : atk.Physical.Flexibility * SubFlex - SubBottomPenalty;
        double p = Logistic(Eff(atk.Grappling.SubmissionOffense, _s[a].Stamina) + posBonus
                          + (_s[1 - a].Hurt ? SubHurtBonus : 0)
                          - Eff(def.Grappling.SubmissionDefense, _s[1 - a].Stamina) - SubOffset, 0.055);
        Drain(a, 1.3);
        _s[a].Stats.SubmissionAttempts++;
        AddScore(a, 1.5);
        // ONE draw (exactly as the old pick from a single seven-hold list), now indexing
        // the holds that can actually be applied from this position.
        string hold = HoldFor(fromTop, _ground, _rng.NextULong());
        if (_rng.Chance(p))
        {
            RecordFinish(a, FightMethod.SubmissionTap, "SUB");
            if (Logging)
                switch (V(2))
                {
                    case 0: Say($"{Name(a)} locks in {Art(hold)} {hold}... and {Name(1 - a)} taps! Submission win for {Name(a)}!"); break;
                    default: Say($"{Name(a)} sinks {Art(hold)} {hold} and {Name(1 - a)} has to tap!"); break;
                }
        }
        else if (Logging)
        {
            switch (V(3))
            {
                case 0: Say($"{Name(a)} threatens with {Art(hold)} {hold} but {Name(1 - a)} escapes the hold."); break;
                case 1: Say($"{Name(a)} goes for {Art(hold)} {hold}; {Name(1 - a)} defends calmly."); break;
                default: Say($"{Name(1 - a)} fights off {Name(a)}'s {hold} attempt."); break;
            }
        }
    }

    /// <summary>
    /// The holds that can be attempted from here. <paramref name="fromTop"/> is the attacker's
    /// side; <paramref name="pos"/> is the TOP fighter's position (guard, half guard, …).
    /// </summary>
    internal static string[] HoldsFor(bool fromTop, GroundPos pos) => (fromTop, pos) switch
    {
        (true, GroundPos.Guard) => TopGuardHolds,
        (true, GroundPos.HalfGuard) => TopHalfHolds,
        (true, GroundPos.SideControl) => TopSideHolds,
        (true, GroundPos.Mount) => TopMountHolds,
        (true, _) => TopBackHolds,
        (false, GroundPos.Guard) => BottomGuardHolds,
        (false, GroundPos.HalfGuard) => BottomHalfHolds,
        (false, GroundPos.SideControl or GroundPos.Mount) => BottomPinnedHolds,
        (false, _) => BottomBackHolds,
    };

    internal static string HoldFor(bool fromTop, GroundPos pos, ulong draw)
    {
        var list = HoldsFor(fromTop, pos);
        return list[(int)(draw % (ulong)list.Length)];
    }

    private static readonly string[] TopGuardHolds = { "guillotine", "kimura", "Americana", "Achilles lock" };
    private static readonly string[] TopHalfHolds = { "arm-triangle choke", "kimura", "D'Arce choke", "Americana" };
    private static readonly string[] TopSideHolds = { "arm-triangle choke", "kimura", "Americana", "north-south choke" };
    private static readonly string[] TopMountHolds = { "armbar", "arm-triangle choke", "Americana", "mounted triangle" };
    private static readonly string[] TopBackHolds = { "rear-naked choke", "short choke", "armbar from the back", "body-triangle choke" };
    private static readonly string[] BottomGuardHolds = { "triangle", "armbar", "guillotine", "omoplata", "kimura" };
    private static readonly string[] BottomHalfHolds = { "kimura", "kneebar", "heel hook", "guillotine" };
    private static readonly string[] BottomPinnedHolds = { "kimura", "guillotine" };
    private static readonly string[] BottomBackHolds = { "wrist lock" };

    // --- Shared mechanics ---------------------------------------------------

    private void GoToGround(int topSide)
    {
        _phase = Phase.Ground;
        _top = topSide;
        _ground = GroundPos.Guard;
    }

    private void Hurt(int i, double dmg)
    {
        _s[i].Health = Math.Max(0, _s[i].Health - dmg);
    }

    private void Drain(int i, double amount)
    {
        var f = _s[i].F;
        // Low cardio and a reckless pace burn the tank faster.
        double resist = 0.6 + 0.5 * (f.Physical.Cardio / 100.0);
        double paceMod = 1.2 - 0.4 * (f.Tendencies.Pace / 100.0);
        _s[i].Stamina = Math.Max(0, _s[i].Stamina - amount * paceMod / resist);
    }

    private void PassiveDrain(double dt)
    {
        for (int i = 0; i < 2; i++) Drain(i, 0.35 * dt / 60.0);
    }

    private void RecoverBetweenRounds()
    {
        for (int i = 0; i < 2; i++)
        {
            var f = _s[i].F;
            _s[i].Stamina = Math.Min(100, _s[i].Stamina + 18 + 34 * (f.Physical.Recovery / 100.0));
            _s[i].Health = Math.Min(100, _s[i].Health + 4 + 16 * (f.Physical.Recovery / 100.0));
            _s[i].Hurt = false;
            _s[i].HurtTimer = 0;
        }
    }

    private void AddScore(int i, double pts) => _s[i].RoundPts[_round - 1] += pts;

    /// <summary>"a" / "an" for the following word (good enough for our strike/sub vocab).</summary>
    private static string Art(string word) => word.Length > 0 && "aeiouAEIOU".IndexOf(word[0]) >= 0 ? "an" : "a";

    private static double Eff(double rating, double stamina) => rating * (0.5 + 0.5 * stamina / 100.0);

    private static double Logistic(double diff, double k) => 1.0 / (1.0 + Math.Exp(-k * diff));

    private string GroundName() => _ground switch
    {
        GroundPos.Guard => "guard",
        GroundPos.HalfGuard => "half guard",
        GroundPos.SideControl => "side control",
        GroundPos.Mount => "full mount",
        _ => "back control",
    };

    // --- Commentary ---------------------------------------------------------

    private bool Logging => _log is not null;

    private string Name(int i) => _s[i].F.FullName;
    private bool Female(int i) => _s[i].F.Sex == Sex.Female;
    /// <summary>Object pronoun: him / her.</summary>
    private string Obj(int i) => Female(i) ? "her" : "him";
    /// <summary>Possessive: his / her.</summary>
    private string Pos(int i) => Female(i) ? "her" : "his";
    /// <summary>Subject pronoun: he / she.</summary>
    private string Subj(int i) => Female(i) ? "she" : "he";

    /// <summary>
    /// Which of <paramref name="n"/> equivalent lines to use: a hash of the bout's text seed
    /// and the line number. Pure — it never touches <see cref="_rng"/>. Only call while logging.
    /// </summary>
    private int V(int n) =>
        (int)(SimRandom.Mix(_textSeed, (ulong)_log!.Count) % (ulong)n);

    /// <summary>An action line, stamped with the round and the clock remaining.</summary>
    private void Say(string line)
    {
        if (_log is null) return;
        int remaining = (int)Math.Max(0, RoundSeconds - _clock);
        _log.Add($"R{_round} {remaining / 60}:{remaining % 60:00} — {line}");
    }

    /// <summary>An unstamped line (the introduction, a round header).</summary>
    private void Header(string line) => _log?.Add(line);

    // --- Finish & decision --------------------------------------------------

    private void RecordFinish(int winner, FightMethod method, string detail, bool betweenRounds = false)
    {
        if (_finish is not null) return;
        _finish = new Finish(winner, method, _round, betweenRounds ? (int)RoundSeconds : (int)Math.Min(RoundSeconds, _clock), betweenRounds);
        FinishDetail = detail;
    }

    /// <summary>How the fight was finished, for the lab (not saved): null for a decision.</summary>
    internal string? FinishDetail { get; private set; }

    private FightOutcome FinishOutcome(Finish f)
    {
        var winner = _s[f.Winner].F.Id;
        var loser = _s[1 - f.Winner].F.Id;
        return new FightOutcome
        {
            Winner = winner,
            Loser = loser,
            Method = f.Method,
            EndRound = f.Round,
            EndTimeSeconds = f.TimeSeconds,
            Stats = BuildStats(),
            PlayByPlay = _log,
            // The rounds completed before the stoppage (a stoppage on the stool ends after its round).
            RoundScores = BuildRoundScores(f.BetweenRounds ? f.Round : f.Round - 1),
        };
    }

    /// <summary>The noiseless card for a finish: each completed round scored straight off the
    /// round points with the judges' rules (10-9; 10-8 for a dominant round). Nobody was judging yet.</summary>
    private List<RoundScore> BuildRoundScores(int completedRounds)
    {
        var scores = new List<RoundScore>(completedRounds);
        for (int r = 0; r < completedRounds; r++)
        {
            var (ra, rb) = ScoreRound(r, _s[0].RoundPts[r], _s[1].RoundPts[r]);
            scores.Add(new RoundScore(ra, rb));
        }
        return scores;
    }

    /// <summary>
    /// One judge's score for round <paramref name="r"/> given his read of each fighter's work
    /// (<paramref name="a"/> red, <paramref name="b"/> blue). 10-10 only for a round he truly
    /// cannot split; 10-8 only for dominance — a wide margin backed by knockdowns or a clear
    /// damage edge, not any knockdown at all.
    /// </summary>
    private (int Red, int Blue) ScoreRound(int r, double a, double b)
    {
        double diff = a - b;
        if (Math.Abs(diff) < EvenRound) return (10, 10);
        int w = diff > 0 ? 0 : 1, l = 1 - w;
        double dominance = Math.Abs(diff)
                         + 6 * (_s[l].RoundKD[r] - _s[w].RoundKD[r])
                         + 0.25 * (_s[w].RoundDmg[r] - _s[l].RoundDmg[r]);
        int loser = dominance >= TenEightDominance ? 8 : 9;
        return w == 0 ? (10, loser) : (loser, 10);
    }

    private FightOutcome DecisionOutcome()
    {
        // The loop leaves _round one past the last round; the decision belongs to the last.
        _round = _rounds;

        // Three judges independently score every round (see ScoreRound: 10-9, 10-8 only
        // for dominance, 10-10 almost never), with a little per-judge noise so close
        // fights can split. Each judge's card is kept so the STORED card is one of
        // the cards that actually decided the fight (same draws, same order as ever).
        int cardsA = 0, cardsB = 0;
        var judgeRed = new int[3, _rounds];
        var judgeBlue = new int[3, _rounds];
        var vote = new int[3];          // 0 = red, 1 = blue, -1 = level
        var level = new bool[3];        // the per-round card itself is level (tie broken on raw merit)
        for (int j = 0; j < 3; j++)
        {
            int totA = 0, totB = 0;
            for (int r = 0; r < _rounds; r++)
            {
                double a = _s[0].RoundPts[r] + _rng.NextGaussian(0, 1.2);
                double b = _s[1].RoundPts[r] + _rng.NextGaussian(0, 1.2);
                var (ra, rb) = ScoreRound(r, a, b);
                judgeRed[j, r] = ra; judgeBlue[j, r] = rb;
                totA += ra; totB += rb;
            }
            // A level card is a level card: no tie-break on raw merit, so a judge can score a
            // draw — which is how majority decisions and draws happen.
            level[j] = totA == totB;
            if (totA > totB) { cardsA++; vote[j] = 0; }
            else if (totB > totA) { cardsB++; vote[j] = 1; }
            else vote[j] = -1;
        }

        int endTime = (int)RoundSeconds;

        if (cardsA == cardsB)
        {
            if (Logging) Say("The judges' scorecards: it's a draw.");
            return Decision(null, null, FightMethod.Draw, endTime, StoredCard(judgeRed, judgeBlue, StoredJudge(vote, level, -1)));
        }

        int winSide = cardsA > cardsB ? 0 : 1;
        int winCards = Math.Max(cardsA, cardsB);
        var method = winCards == 3 ? FightMethod.UnanimousDecision
                   : (cardsA + cardsB == 3 ? FightMethod.SplitDecision : FightMethod.MajorityDecision);

        var winner = _s[winSide].F.Id;
        var loser = _s[1 - winSide].F.Id;
        if (Logging) Say($"The judges' scorecards: {_s[winSide].F.FullName} wins by {Pretty(method)}.");
        return Decision(winner, loser, method, endTime, StoredCard(judgeRed, judgeBlue, StoredJudge(vote, level, winSide)));
    }

    /// <summary>
    /// Which judge's card is stored (and shown): the FIRST judge, in judge order, who voted
    /// for the official winner with a card that is not level; failing that, the first judge
    /// who voted for the winner at all (a level card won on the raw-merit tie-break). For a
    /// draw (<paramref name="winSide"/> = -1): the first level card, else judge 1.
    /// </summary>
    private static int StoredJudge(int[] vote, bool[] level, int winSide)
    {
        if (winSide < 0)
        {
            for (int j = 0; j < 3; j++) if (vote[j] == -1 || level[j]) return j;
            return 0;
        }
        for (int j = 0; j < 3; j++) if (vote[j] == winSide && !level[j]) return j;
        for (int j = 0; j < 3; j++) if (vote[j] == winSide) return j;
        return 0;
    }

    private List<RoundScore> StoredCard(int[,] red, int[,] blue, int judge)
    {
        var card = new List<RoundScore>(_rounds);
        for (int r = 0; r < _rounds; r++) card.Add(new RoundScore(red[judge, r], blue[judge, r]));
        return card;
    }

    private FightOutcome Decision(FighterId? w, FighterId? l, FightMethod m, int endTime, List<RoundScore> card) => new()
    {
        Winner = w,
        Loser = l,
        Method = m,
        EndRound = _rounds,
        EndTimeSeconds = endTime,
        Stats = BuildStats(),
        PlayByPlay = _log,
        RoundScores = card,
    };

    private Dictionary<FighterId, FightStatline> BuildStats() => new()
    {
        [_s[0].F.Id] = _s[0].Stats,
        [_s[1].F.Id] = _s[1].Stats,
    };

    private static string Pretty(FightMethod m) => m switch
    {
        FightMethod.UnanimousDecision => "unanimous decision",
        FightMethod.SplitDecision => "split decision",
        FightMethod.MajorityDecision => "majority decision",
        _ => m.ToString(),
    };

    // --- Nested state -------------------------------------------------------

    private sealed class Side
    {
        public Side(Fighter f, int rounds)
        {
            F = f;
            RoundPts = new double[rounds];
            RoundKD = new int[rounds];
            RoundDmg = new double[rounds];
            Power = DivisionPower(f);
        }

        /// <summary>
        /// How hard a fighter of this division and sex hits, relative to a man at lightweight:
        /// heavier fighters finish more with strikes, the lightest and women's divisions less
        /// (the shape of real MMA's finish mix by division; the numbers are approximate).
        /// Public facts only: weight class and sex.
        /// </summary>
        internal static double DivisionPower(Fighter f)
        {
            double w = f.WeightClass switch
            {
                WeightClass.Strawweight => 0.86,
                WeightClass.Flyweight => 0.90,
                WeightClass.Bantamweight => 0.94,
                WeightClass.Featherweight => 0.97,
                WeightClass.Lightweight => 1.00,
                WeightClass.Welterweight => 1.03,
                WeightClass.Middleweight => 1.06,
                WeightClass.LightHeavyweight => 1.10,
                _ => 1.16,
            };
            return f.Sex == Sex.Female ? w * 0.88 : w;
        }

        /// <summary>Strike-damage multiplier (see <see cref="DivisionPower"/>).</summary>
        public readonly double Power;
        /// <summary>Damage taken while hurt or worn without answering back: the referee's count.</summary>
        public double Barrage;
        /// <summary>0 = no cut; 1+ = open, higher is worse (the doctor looks at 3+).</summary>
        public int Cut;
        /// <summary>Damage this fighter DEALT in each round (judges' damage edge for 10-8s).</summary>
        public double[] RoundDmg;

        public Fighter F { get; }
        public double Stamina = 100;
        public double Health = 100;
        public bool Hurt;
        public int HurtTimer;
        /// <summary>Text only: the hurt state cleared; say so before the next exchange.</summary>
        public bool RecoveredPending;
        public FightStatline Stats { get; } = new();
        public double[] RoundPts;
        public int[] RoundKD;
    }

    private readonly record struct Finish(int Winner, FightMethod Method, int Round, int TimeSeconds, bool BetweenRounds);
}
