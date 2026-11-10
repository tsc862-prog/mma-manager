using MmaManager.Core.Model;

namespace MmaManager.Persistence;

/// <summary>
/// Single source of truth for the fighters table's 32 rating columns: the
/// column name paired with a getter/setter into the fighter's grouped ratings.
/// The writer and reader both drive off this list, so the schema, the INSERT,
/// and the SELECT can never drift out of order.
/// </summary>
internal sealed record RatingColumn(string Column, Func<Fighter, int> Get, Action<Fighter, int> Set);

internal static class FighterColumns
{
    public static readonly RatingColumn[] Ratings =
    {
        // Striking
        new("punch_power", f => f.Striking.PunchPower, (f, v) => f.Striking.PunchPower = v),
        new("punch_technique", f => f.Striking.PunchTechnique, (f, v) => f.Striking.PunchTechnique = v),
        new("kick_power", f => f.Striking.KickPower, (f, v) => f.Striking.KickPower = v),
        new("kick_technique", f => f.Striking.KickTechnique, (f, v) => f.Striking.KickTechnique = v),
        new("clinch_striking", f => f.Striking.ClinchStriking, (f, v) => f.Striking.ClinchStriking = v),
        new("striking_defense", f => f.Striking.StrikingDefense, (f, v) => f.Striking.StrikingDefense = v),
        new("footwork", f => f.Striking.Footwork, (f, v) => f.Striking.Footwork = v),
        // Grappling
        new("takedown_offense", f => f.Grappling.TakedownOffense, (f, v) => f.Grappling.TakedownOffense = v),
        new("takedown_defense", f => f.Grappling.TakedownDefense, (f, v) => f.Grappling.TakedownDefense = v),
        new("top_control", f => f.Grappling.TopControl, (f, v) => f.Grappling.TopControl = v),
        new("bottom_game", f => f.Grappling.BottomGame, (f, v) => f.Grappling.BottomGame = v),
        new("submission_offense", f => f.Grappling.SubmissionOffense, (f, v) => f.Grappling.SubmissionOffense = v),
        new("submission_defense", f => f.Grappling.SubmissionDefense, (f, v) => f.Grappling.SubmissionDefense = v),
        new("scrambling", f => f.Grappling.Scrambling, (f, v) => f.Grappling.Scrambling = v),
        new("clinch_control", f => f.Grappling.ClinchControl, (f, v) => f.Grappling.ClinchControl = v),
        // Physical
        new("strength", f => f.Physical.Strength, (f, v) => f.Physical.Strength = v),
        new("cardio", f => f.Physical.Cardio, (f, v) => f.Physical.Cardio = v),
        new("speed", f => f.Physical.Speed, (f, v) => f.Physical.Speed = v),
        new("chin", f => f.Physical.Chin, (f, v) => f.Physical.Chin = v),
        new("recovery", f => f.Physical.Recovery, (f, v) => f.Physical.Recovery = v),
        new("flexibility", f => f.Physical.Flexibility, (f, v) => f.Physical.Flexibility = v),
        // Mental
        new("fight_iq", f => f.Mental.FightIQ, (f, v) => f.Mental.FightIQ = v),
        new("composure", f => f.Mental.Composure, (f, v) => f.Mental.Composure = v),
        new("heart", f => f.Mental.Heart, (f, v) => f.Mental.Heart = v),
        new("discipline", f => f.Mental.Discipline, (f, v) => f.Mental.Discipline = v),
        // Intangibles
        new("popularity", f => f.Intangibles.Popularity, (f, v) => f.Intangibles.Popularity = v),
        new("charisma", f => f.Intangibles.Charisma, (f, v) => f.Intangibles.Charisma = v),
        // Temperament (who they are outside the cage)
        new("ego", f => f.Temperament.Ego, (f, v) => f.Temperament.Ego = v),
        new("professionalism", f => f.Temperament.Professionalism, (f, v) => f.Temperament.Professionalism = v),
        new("temper", f => f.Temperament.Temper, (f, v) => f.Temperament.Temper = v),
        new("ambition", f => f.Temperament.Ambition, (f, v) => f.Temperament.Ambition = v),
        // Tendencies
        new("striking_preference", f => f.Tendencies.StrikingPreference, (f, v) => f.Tendencies.StrikingPreference = v),
        new("aggression", f => f.Tendencies.Aggression, (f, v) => f.Tendencies.Aggression = v),
        new("pace", f => f.Tendencies.Pace, (f, v) => f.Tendencies.Pace = v),
        new("counterfighting", f => f.Tendencies.Counterfighting, (f, v) => f.Tendencies.Counterfighting = v),
        new("finish_instinct", f => f.Tendencies.FinishInstinct, (f, v) => f.Tendencies.FinishInstinct = v),
    };
}
