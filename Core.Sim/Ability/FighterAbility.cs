using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Ability;

/// <summary>
/// Collapses a fighter's ~25 skills into a single 0–100 "current ability"
/// number — the Football-Manager CA idea. PROJECT.md deliberately keeps this
/// weighting in the sim layer (not the model) so it can be tuned without
/// touching the domain; the fighter generator uses it to guarantee potential
/// never sits below current ability, and the UI uses it for scouting overalls.
///
/// The weighting is a first pass: durability, cardio, and ring generalship
/// count for a little more than any single specialist skill, and business-facing
/// intangibles (popularity, charisma) don't count toward fighting ability at all.
/// Expect to retune once the fight engine tells us what actually wins fights.
/// </summary>
public static class FighterAbility
{
    public static int CurrentAbility(Fighter f)
    {
        double sum = 0, wSum = 0;
        void Add(int rating, double weight) { sum += rating * weight; wSum += weight; }

        // Striking
        Add(f.Striking.PunchPower, 1.0);
        Add(f.Striking.PunchTechnique, 1.1);
        Add(f.Striking.KickPower, 0.8);
        Add(f.Striking.KickTechnique, 0.8);
        Add(f.Striking.ClinchStriking, 0.7);
        Add(f.Striking.StrikingDefense, 1.2);
        Add(f.Striking.Footwork, 1.0);

        // Grappling
        Add(f.Grappling.TakedownOffense, 1.0);
        Add(f.Grappling.TakedownDefense, 1.2);
        Add(f.Grappling.TopControl, 1.0);
        Add(f.Grappling.BottomGame, 0.8);
        Add(f.Grappling.SubmissionOffense, 0.9);
        Add(f.Grappling.SubmissionDefense, 1.0);
        Add(f.Grappling.Scrambling, 0.9);
        Add(f.Grappling.ClinchControl, 0.7);

        // Physical — the durability/gas-tank attributes carry extra weight.
        Add(f.Physical.Strength, 0.9);
        Add(f.Physical.Cardio, 1.3);
        Add(f.Physical.Speed, 1.1);
        Add(f.Physical.Chin, 1.2);
        Add(f.Physical.Recovery, 0.8);
        Add(f.Physical.Flexibility, 0.5);

        // Mental
        Add(f.Mental.FightIQ, 1.2);
        Add(f.Mental.Composure, 1.0);
        Add(f.Mental.Heart, 0.9);
        Add(f.Mental.Discipline, 0.7);

        return (int)Math.Round(sum / wSum);
    }
}
