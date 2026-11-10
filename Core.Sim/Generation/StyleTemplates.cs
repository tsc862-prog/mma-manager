using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Generation;

/// <summary>Every trainable skill, so a style can weight each one independently.</summary>
public enum SkillId
{
    // Striking
    PunchPower, PunchTechnique, KickPower, KickTechnique, ClinchStriking, StrikingDefense, Footwork,
    // Grappling
    TakedownOffense, TakedownDefense, TopControl, BottomGame, SubmissionOffense, SubmissionDefense, Scrambling, ClinchControl,
    // Physical
    Strength, Cardio, Speed, Chin, Recovery, Flexibility,
    // Mental
    FightIQ, Composure, Heart, Discipline
}

/// <summary>
/// How a fighting style shapes a fighter. Weights are 0..1 where 0.5 is neutral;
/// a skill left out of a template defaults to 0.5. The generator turns each
/// weight into a bias around the fighter's overall level, so a wrestler ends up
/// strong where wrestlers are strong and thin where they aren't — without any
/// two wrestlers being identical.
/// </summary>
public sealed class StyleTemplate
{
    public required IReadOnlyDictionary<SkillId, double> Skills { get; init; }

    /// <summary>How far skills spread from the fighter's overall level. Well-rounded fighters spread less.</summary>
    public double Spread { get; init; } = 32;

    // Tendency centres (0..100); the generator adds noise around these.
    public double StrikingPreference { get; init; } = 50;
    public double Aggression { get; init; } = 55;
    public double Pace { get; init; } = 55;
    public double Counterfighting { get; init; } = 50;
    public double FinishInstinct { get; init; } = 55;

    // Nudges to finish distribution when building a fighter's backstory record.
    public double KoBias { get; init; }
    public double SubBias { get; init; }

    public double Weight(SkillId s) => Skills.GetValueOrDefault(s, 0.5);

    public static StyleTemplate For(PrimaryStyle style) => Templates[style];

    private static readonly IReadOnlyDictionary<PrimaryStyle, StyleTemplate> Templates =
        new Dictionary<PrimaryStyle, StyleTemplate>
        {
            [PrimaryStyle.Striker] = new()
            {
                StrikingPreference = 82, Aggression = 60, Counterfighting = 55, FinishInstinct = 66, KoBias = 0.14,
                Skills = new Dictionary<SkillId, double>
                {
                    [SkillId.PunchPower] = 0.82, [SkillId.PunchTechnique] = 0.86, [SkillId.KickTechnique] = 0.6,
                    [SkillId.Footwork] = 0.8, [SkillId.StrikingDefense] = 0.8, [SkillId.Speed] = 0.7,
                    [SkillId.TakedownDefense] = 0.62,
                    [SkillId.TakedownOffense] = 0.3, [SkillId.TopControl] = 0.3, [SkillId.BottomGame] = 0.32,
                    [SkillId.SubmissionOffense] = 0.28, [SkillId.Scrambling] = 0.4,
                },
            },
            [PrimaryStyle.Kickboxer] = new()
            {
                StrikingPreference = 85, Aggression = 62, Pace = 60, FinishInstinct = 64, KoBias = 0.13,
                Skills = new Dictionary<SkillId, double>
                {
                    [SkillId.KickPower] = 0.85, [SkillId.KickTechnique] = 0.9, [SkillId.PunchTechnique] = 0.75,
                    [SkillId.Footwork] = 0.8, [SkillId.ClinchStriking] = 0.7, [SkillId.StrikingDefense] = 0.75,
                    [SkillId.Cardio] = 0.65, [SkillId.TakedownDefense] = 0.55,
                    [SkillId.TakedownOffense] = 0.3, [SkillId.TopControl] = 0.3, [SkillId.BottomGame] = 0.32,
                    [SkillId.SubmissionOffense] = 0.28,
                },
            },
            [PrimaryStyle.Wrestler] = new()
            {
                StrikingPreference = 30, Aggression = 58, Pace = 62, Counterfighting = 45, FinishInstinct = 45,
                Skills = new Dictionary<SkillId, double>
                {
                    [SkillId.TakedownOffense] = 0.9, [SkillId.TakedownDefense] = 0.86, [SkillId.TopControl] = 0.9,
                    [SkillId.Scrambling] = 0.75, [SkillId.ClinchControl] = 0.8, [SkillId.ClinchStriking] = 0.6,
                    [SkillId.Strength] = 0.8, [SkillId.Cardio] = 0.7,
                    [SkillId.SubmissionOffense] = 0.45, [SkillId.BottomGame] = 0.4,
                    [SkillId.PunchTechnique] = 0.45, [SkillId.KickTechnique] = 0.3, [SkillId.Footwork] = 0.45,
                    [SkillId.StrikingDefense] = 0.45,
                },
            },
            [PrimaryStyle.BrazilianJiuJitsu] = new()
            {
                StrikingPreference = 28, Aggression = 48, Counterfighting = 55, FinishInstinct = 72, SubBias = 0.26,
                Skills = new Dictionary<SkillId, double>
                {
                    [SkillId.SubmissionOffense] = 0.92, [SkillId.SubmissionDefense] = 0.85, [SkillId.BottomGame] = 0.9,
                    [SkillId.Scrambling] = 0.8, [SkillId.Flexibility] = 0.8, [SkillId.TopControl] = 0.7,
                    [SkillId.TakedownDefense] = 0.55,
                    [SkillId.PunchTechnique] = 0.4, [SkillId.KickTechnique] = 0.35, [SkillId.Footwork] = 0.45,
                    [SkillId.StrikingDefense] = 0.4,
                },
            },
            [PrimaryStyle.Grappler] = new()
            {
                StrikingPreference = 33, Aggression = 55, Pace = 62, FinishInstinct = 58, SubBias = 0.14,
                Skills = new Dictionary<SkillId, double>
                {
                    [SkillId.TakedownOffense] = 0.8, [SkillId.TakedownDefense] = 0.76, [SkillId.TopControl] = 0.8,
                    [SkillId.SubmissionOffense] = 0.72, [SkillId.SubmissionDefense] = 0.7, [SkillId.Scrambling] = 0.78,
                    [SkillId.ClinchControl] = 0.7, [SkillId.Strength] = 0.7,
                    [SkillId.PunchTechnique] = 0.45, [SkillId.KickTechnique] = 0.4, [SkillId.Footwork] = 0.45,
                },
            },
            [PrimaryStyle.Brawler] = new()
            {
                StrikingPreference = 78, Aggression = 88, Pace = 32, Counterfighting = 22, FinishInstinct = 84, KoBias = 0.2,
                Skills = new Dictionary<SkillId, double>
                {
                    [SkillId.PunchPower] = 0.9, [SkillId.KickPower] = 0.6, [SkillId.Chin] = 0.85, [SkillId.Heart] = 0.85,
                    [SkillId.Strength] = 0.72,
                    [SkillId.PunchTechnique] = 0.4, [SkillId.StrikingDefense] = 0.3, [SkillId.Footwork] = 0.35,
                    [SkillId.FightIQ] = 0.38, [SkillId.Discipline] = 0.32, [SkillId.Composure] = 0.42, [SkillId.Cardio] = 0.42,
                    [SkillId.TakedownDefense] = 0.45, [SkillId.TakedownOffense] = 0.4, [SkillId.SubmissionDefense] = 0.38,
                },
            },
            [PrimaryStyle.ClinchFighter] = new()
            {
                StrikingPreference = 48, Aggression = 66, Pace = 64, FinishInstinct = 58,
                Skills = new Dictionary<SkillId, double>
                {
                    [SkillId.ClinchStriking] = 0.9, [SkillId.ClinchControl] = 0.9, [SkillId.Strength] = 0.8,
                    [SkillId.TakedownOffense] = 0.7, [SkillId.TopControl] = 0.7, [SkillId.Cardio] = 0.7,
                    [SkillId.TakedownDefense] = 0.65,
                    [SkillId.PunchTechnique] = 0.5, [SkillId.Footwork] = 0.45, [SkillId.KickTechnique] = 0.4,
                },
            },
            [PrimaryStyle.WellRounded] = new()
            {
                Spread = 16, StrikingPreference = 52, Aggression = 55, Pace = 60, FinishInstinct = 56,
                Skills = new Dictionary<SkillId, double>
                {
                    [SkillId.PunchTechnique] = 0.62, [SkillId.StrikingDefense] = 0.62, [SkillId.Footwork] = 0.6,
                    [SkillId.TakedownDefense] = 0.64, [SkillId.TakedownOffense] = 0.6, [SkillId.Scrambling] = 0.62,
                    [SkillId.SubmissionDefense] = 0.62, [SkillId.FightIQ] = 0.65, [SkillId.Cardio] = 0.65,
                    [SkillId.Composure] = 0.62,
                },
            },
        };
}
