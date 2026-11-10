using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Engine;

/// <summary>
/// One simulated bout. Holds all mutable fight state and runs the exchange loop,
/// so each <see cref="FightEngine.Simulate"/> call is self-contained and the
/// engine itself stays stateless. Everything random flows through a single
/// seeded <see cref="SimRandom"/>, so a seed reproduces the fight exactly.
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

    private readonly SimRandom _rng;
    private readonly bool _pbp;
    private readonly int _rounds;
    private readonly Side[] _s = new Side[2];
    private readonly List<string>? _log;

    private Phase _phase = Phase.Standing;
    private int _top;                 // ground: which side is on top
    private GroundPos _ground;
    private int _round = 1;
    private double _clock;            // seconds elapsed in the current round

    private Finish? _finish;

    public Bout(Fighter red, Fighter blue, FightParams p, SimRandom rng)
    {
        _rng = rng;
        _pbp = p.GeneratePlayByPlay;
        _rounds = Math.Max(1, p.ScheduledRounds);
        RoundSeconds = Math.Clamp(p.RoundSeconds, 60, 600);
        _s[0] = new Side(red, _rounds);
        _s[1] = new Side(blue, _rounds);
        _log = _pbp ? new List<string>() : null;
    }

    // --- Entry point --------------------------------------------------------

    public FightOutcome Run()
    {
        Say($"{_s[0].F.FullName} vs {_s[1].F.FullName} — {_rounds} rounds.");

        for (_round = 1; _round <= _rounds && _finish is null; _round++)
        {
            Say($"--- Round {_round} ---");
            RunRound();
            if (_finish is not null) break;
            if (_round < _rounds) RecoverBetweenRounds();
        }

        return _finish is { } f ? FinishOutcome(f) : DecisionOutcome();
    }

    // --- Round loop ---------------------------------------------------------

    private void RunRound()
    {
        _clock = 0;
        ResetPositionForNewRound();

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
                Say($"{Name(i)} has recovered and is fighting back.");
            }
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
            Say($"{Name(a)} closes the distance and presses {Name(1 - a)} into the clinch.");
        }
        else
        {
            Say($"{Name(a)} tries to tie up but {Name(1 - a)} circles out.");
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
                Say($"{Name(a)} breaks the clinch and the fighters reset.");
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
            Say($"{Name(a)} lands a trip from the clinch and puts {Name(1 - a)} on the mat.");
        }
        else
        {
            Say($"{Name(1 - a)} defends the takedown against the cage.");
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
        if (_rng.Chance(p))
        {
            _s[a].Stats.TakedownsLanded++;
            GoToGround(a);
            AddScore(a, 3);
            Say($"{Name(a)} shoots in and completes the takedown.");
        }
        else
        {
            AddScore(1 - a, 0.5);
            Say($"{Name(a)} shoots but {Name(1 - a)} sprawls and defends.");
        }
    }

    // --- Striking (standing or clinch) -------------------------------------

    private void StrikeExchange(int a, Phase where)
    {
        var atk = _s[a].F; var def = _s[1 - a].F;
        double staA = _s[a].Stamina, staD = _s[1 - a].Stamina;

        double accuracy = where == Phase.Clinch
            ? Eff(atk.Striking.ClinchStriking, staA)
            : Eff((atk.Striking.PunchTechnique * 2 + atk.Striking.KickTechnique + atk.Striking.Footwork + atk.Physical.Speed) / 5, staA);
        double defense = Eff((def.Striking.StrikingDefense * 2 + def.Striking.Footwork + def.Mental.FightIQ) / 4, staD);
        if (_s[1 - a].Hurt) defense *= 0.6;      // hurt fighters get hit more

        _s[a].Stats.SignificantStrikesAttempted++;
        double pLand = Logistic(accuracy - defense, StrikeK);
        if (!_rng.Chance(pLand))
        {
            Say($"{Name(a)} throws but {Name(1 - a)} slips it.");
            return;
        }

        _s[a].Stats.SignificantStrikesLanded++;

        // Is this a committed power shot or a lighter scoring blow?
        bool power = _rng.Chance(0.28 + atk.Tendencies.FinishInstinct / 300.0);
        double powerScale = 0.6 + 0.8 * (Math.Max(atk.Striking.PunchPower, atk.Striking.KickPower) / 100.0);
        double dmg = power
            ? Math.Max(2, _rng.NextGaussian(12, 4)) * powerScale
            : Math.Max(1, _rng.NextGaussian(3.5, 1.2));

        Hurt(1 - a, dmg);
        AddScore(a, power ? 2.2 : 1.0);

        if (power && TryKnockdown(a, dmg)) return;   // knockdown may end it

        if (_finish is null)
            Say(power
                ? $"{Name(a)} lands a hard {StrikeName(atk)} that lands flush."
                : $"{Name(a)} scores with a crisp {StrikeName(atk)}.");
    }

    private string StrikeName(Fighter f) =>
        f.Tendencies.StrikingPreference > 55 || f.Striking.PunchPower >= f.Striking.KickPower
            ? _rng.Pick(Punches) : _rng.Pick(Kicks);

    private static readonly string[] Punches = { "right hand", "left hook", "jab-cross", "overhand right", "uppercut", "counter left" };
    private static readonly string[] Kicks = { "head kick", "body kick", "low kick", "front kick", "spinning kick" };

    private bool TryKnockdown(int a, double dmg)
    {
        var def = _s[1 - a].F;
        double healthCushion = _s[1 - a].Health * 0.25;   // fresh fighters are harder to drop
        double margin = dmg * 2.2 + (100 - def.Physical.Chin) * 0.4 - healthCushion
                      + (_s[1 - a].Hurt ? 25 : 0);
        double pKd = Logistic(margin - 30, 0.06);
        if (!_rng.Chance(pKd)) return false;

        _s[a].Stats.KnockdownsScored++;        // credited to the fighter who landed it
        _s[1 - a].RoundKD[_round - 1]++;       // the dropped fighter, for 10-8 scoring
        AddScore(a, 4);
        Hurt(1 - a, _rng.NextGaussian(14, 4));
        string shot = StrikeName(_s[a].F);
        Say($"BIG SHOT! {Name(a)} drops {Name(1 - a)} with {Art(shot)} {shot}!");

        if (_s[1 - a].Health <= 0)
        {
            RecordFinish(a, FightMethod.Knockout);
            Say($"It's all over! {Name(a)} scores the knockout!");
            return true;
        }

        // Survived the knockdown but is now hurt and vulnerable.
        _s[1 - a].Hurt = true;
        _s[1 - a].HurtTimer = _rng.Range(2, 4);
        Say($"{Name(1 - a)} is hurt but stays in it!");
        return true;
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
            Say($"{Name(a)} postures up but {Name(1 - a)} ties him up from the bottom.");
            return;
        }
        _s[a].Stats.SignificantStrikesLanded++;
        bool heavy = _ground >= GroundPos.Mount && _rng.Chance(0.4);
        double dmg = heavy ? Math.Max(3, _rng.NextGaussian(11, 3)) : Math.Max(1, _rng.NextGaussian(4, 1.5));
        Hurt(1 - a, dmg);
        AddScore(a, heavy ? 2 : 1);
        Say(heavy
            ? $"{Name(a)} lands heavy ground-and-pound from {GroundName()}!"
            : $"{Name(a)} works short strikes from {GroundName()}.");

        if (_s[1 - a].Health <= 0 && _finish is null)
        {
            RecordFinish(a, FightMethod.TechnicalKnockout);
            Say($"The referee has seen enough — {Name(a)} wins by ground-and-pound TKO!");
        }
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
            Say($"{Name(a)} advances to {GroundName()}.");
        }
        else
        {
            Say($"{Name(1 - a)} defends and keeps {Name(a)} in {GroundName()}.");
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
            Say($"{Name(a)} sweeps and comes up on top!");
        }
        else
        {
            Say($"{Name(a)} looks to sweep but {Name(1 - a)} stays heavy on top.");
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
            Say($"{Name(a)} scrambles up and the fight returns to the feet.");
        }
        else
        {
            Say($"{Name(a)} tries to stand but {Name(1 - a)} drags him back down.");
        }
    }

    private void SubmissionAttempt(int a)
    {
        var atk = _s[a].F; var def = _s[1 - a].F;
        bool fromTop = a == _top;
        // Submissions are rare and demand a real position/skill edge: dominant
        // ground positions add the most threat, tapping a competent defender is hard.
        double posBonus = fromTop ? (int)_ground * 4 : atk.Physical.Flexibility * 0.05;
        double p = Logistic(Eff(atk.Grappling.SubmissionOffense, _s[a].Stamina) + posBonus
                          - Eff(def.Grappling.SubmissionDefense, _s[1 - a].Stamina) - 48, 0.055);
        Drain(a, 1.3);
        _s[a].Stats.SubmissionAttempts++;
        AddScore(a, 1.5);
        string hold = _rng.Pick(Subs);
        if (_rng.Chance(p))
        {
            RecordFinish(a, FightMethod.SubmissionTap);
            Say($"{Name(a)} locks in {Art(hold)} {hold}... and {Name(1 - a)} taps! Submission win for {Name(a)}!");
        }
        else
        {
            Say($"{Name(a)} threatens with {Art(hold)} {hold} but {Name(1 - a)} escapes the hold.");
        }
    }

    private static readonly string[] Subs = { "rear-naked choke", "armbar", "guillotine", "triangle", "kimura", "D'Arce choke", "heel hook" };

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
    private static string Art(string word) => word.Length > 0 && "aeiou".IndexOf(char.ToLowerInvariant(word[0])) >= 0 ? "an" : "a";

    private static double Eff(int rating, double stamina) => rating * (0.5 + 0.5 * stamina / 100.0);

    private static double Logistic(double diff, double k) => 1.0 / (1.0 + Math.Exp(-k * diff));

    private string GroundName() => _ground switch
    {
        GroundPos.Guard => "guard",
        GroundPos.HalfGuard => "half guard",
        GroundPos.SideControl => "side control",
        GroundPos.Mount => "full mount",
        _ => "back control",
    };

    private string Name(int i) => _s[i].F.FullName;

    private void Say(string line)
    {
        if (_log is null) return;
        int remaining = (int)Math.Max(0, RoundSeconds - _clock);
        // Round header / intro lines carry no clock.
        _log.Add(line.StartsWith("---") || line.Contains(" vs ")
            ? line
            : $"R{_round} {remaining / 60}:{remaining % 60:00} — {line}");
    }

    // --- Finish & decision --------------------------------------------------

    private void RecordFinish(int winner, FightMethod method) =>
        _finish ??= new Finish(winner, method, _round, (int)Math.Min(RoundSeconds, _clock));

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
            RoundScores = BuildRoundScores(f.Round - 1),   // the rounds before the stoppage
        };
    }

    /// <summary>The canonical card: each completed round scored 10-9 (10-8 on a
    /// knockdown or a rout) straight off the round points, no judge noise.</summary>
    private List<RoundScore> BuildRoundScores(int completedRounds)
    {
        var scores = new List<RoundScore>(completedRounds);
        for (int r = 0; r < completedRounds; r++)
        {
            double a = _s[0].RoundPts[r], b = _s[1].RoundPts[r];
            scores.Add(a > b ? new RoundScore(10, LoserRoundScore(a - b, _s[1].RoundKD[r]))
                     : b > a ? new RoundScore(LoserRoundScore(b - a, _s[0].RoundKD[r]), 10)
                     : new RoundScore(10, 10));
        }
        return scores;
    }

    private FightOutcome DecisionOutcome()
    {
        // Three judges independently score every round 10-9 (or 10-8 on a
        // knockdown / lopsided round), with a little per-judge noise so close
        // fights can split.
        int cardsA = 0, cardsB = 0;
        for (int j = 0; j < 3; j++)
        {
            int totA = 0, totB = 0;
            double rawA = 0, rawB = 0;
            for (int r = 0; r < _rounds; r++)
            {
                double a = _s[0].RoundPts[r] + _rng.NextGaussian(0, 1.2);
                double b = _s[1].RoundPts[r] + _rng.NextGaussian(0, 1.2);
                rawA += a; rawB += b;
                if (a > b) { totA += 10; totB += LoserRoundScore(a - b, _s[1].RoundKD[r]); }
                else if (b > a) { totB += 10; totA += LoserRoundScore(b - a, _s[0].RoundKD[r]); }
                else { totA += 10; totB += 10; }
            }
            // Break an even card on total merit; a genuine dead heat is a draw card.
            if (totA == totB) { totA = rawA > rawB ? totA + 1 : totA; totB = rawB > rawA ? totB + 1 : totB; }
            if (totA > totB) cardsA++;
            else if (totB > totA) cardsB++;
        }

        int endTime = (int)RoundSeconds;

        if (cardsA == cardsB)
            return Decision(null, null, FightMethod.Draw, endTime);

        int winSide = cardsA > cardsB ? 0 : 1;
        int winCards = Math.Max(cardsA, cardsB);
        var method = winCards == 3 ? FightMethod.UnanimousDecision
                   : (cardsA + cardsB == 3 ? FightMethod.SplitDecision : FightMethod.MajorityDecision);

        var winner = _s[winSide].F.Id;
        var loser = _s[1 - winSide].F.Id;
        Say($"The judges' scorecards: {_s[winSide].F.FullName} wins by {Pretty(method)}.");
        return Decision(winner, loser, method, endTime);
    }

    /// <summary>A lost round is 9, or 8 if the loser was knocked down or badly outscored.</summary>
    private static int LoserRoundScore(double margin, int knockdownsAgainst) =>
        knockdownsAgainst > 0 || margin > 12 ? 8 : 9;

    private FightOutcome Decision(FighterId? w, FighterId? l, FightMethod m, int endTime) => new()
    {
        Winner = w,
        Loser = l,
        Method = m,
        EndRound = _rounds,
        EndTimeSeconds = endTime,
        Stats = BuildStats(),
        PlayByPlay = _log,
        RoundScores = BuildRoundScores(_rounds),
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
        }

        public Fighter F { get; }
        public double Stamina = 100;
        public double Health = 100;
        public bool Hurt;
        public int HurtTimer;
        public FightStatline Stats { get; } = new();
        public double[] RoundPts;
        public int[] RoundKD;
    }

    private readonly record struct Finish(int Winner, FightMethod Method, int Round, int TimeSeconds);
}
