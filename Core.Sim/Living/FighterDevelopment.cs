using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The monthly growth/decline model — the Football-Manager CA→PA arc. Young
/// fighters climb toward their hidden <see cref="Fighter.PotentialAbility"/> by
/// patching weaknesses (which raises overall ability efficiently); past their
/// early thirties the athletic tools erode first, so a fighter can stay
/// technically sharp while losing the speed and gas that once carried them.
/// Call once per in-world month. Deterministic given the RNG.
/// </summary>
public static class FighterDevelopment
{
    /// <summary>Advance a fighter one month of physical/technical development.</summary>
    /// <param name="growthMultiplier">Scales the growth rate (a head coach lifts this above 1.0).</param>
    public static void Advance(Fighter f, DateOnly date, SimRandom rng, double growthMultiplier = 1.0)
    {
        if (f.Status == FighterStatus.Retired) return;

        int age = f.AgeOn(date);
        int pa = f.PotentialAbility;

        // --- Growth: climb toward potential while young -----------------------
        if (age <= 30 && FighterAbility.CurrentAbility(f) < pa)
        {
            double rate = age switch
            {
                <= 20 => 0.9,
                <= 23 => 0.7,
                <= 26 => 0.45,
                <= 28 => 0.25,
                _ => 0.12,
            };
            rate *= 0.7 + 0.6 * (f.Mental.Discipline / 100.0);   // diligent fighters develop faster
            // …and the work happens outside the gym too: the pro who lives clean
            // gets more out of the same camp than the one the coaches chase.
            rate *= 0.85 + 0.3 * (f.Temperament.Professionalism / 100.0);
            rate *= growthMultiplier;                            // and a good head coach accelerates it
            double gap = pa - FighterAbility.CurrentAbility(f);
            // Many small skill gains per month: a single +1 barely moves the
            // ~24-skill overall, so a developing prospect needs a real training
            // budget to climb a few overall points a year.
            int bumps = Poissonish(rng, rate * Math.Min(gap, 30) * 0.55);

            var skills = AllSkills(f);
            for (int i = 0; i < bumps; i++)
            {
                if (FighterAbility.CurrentAbility(f) >= pa) break;
                Nudge(rng, skills, up: true);
            }
        }

        // --- Decline: lose athleticism with age ------------------------------
        if (age >= 32)
        {
            double rate = age switch
            {
                <= 34 => 0.25,
                <= 37 => 0.55,
                _ => 0.85,
            };
            int drops = Poissonish(rng, rate * 3.0);
            var physical = PhysicalSkills(f);
            for (int i = 0; i < drops; i++)
                Nudge(rng, physical, up: false);
        }
    }

    // Pick a skill and move it by one point. Growth favours current weaknesses
    // (rounding a fighter out raises overall ability); decline bites hardest at
    // the tools that are still high (there's more to lose).
    private static void Nudge(SimRandom rng, IReadOnlyList<SkillRef> skills, bool up)
    {
        Span<double> weights = stackalloc double[skills.Count];
        for (int i = 0; i < skills.Count; i++)
        {
            int v = skills[i].Get();
            weights[i] = up ? Math.Max(1, 100 - v) : Math.Max(1, v);
        }
        int idx = rng.WeightedIndex(weights);
        int cur = skills[idx].Get();
        skills[idx].Set(Math.Clamp(cur + (up ? 1 : -1), 1, 99));
    }

    private static int Poissonish(SimRandom rng, double expected)
    {
        int count = 0;
        while (expected > 0)
        {
            if (rng.Chance(Math.Min(1.0, expected))) count++;
            expected -= 1.0;
        }
        return count;
    }

    private readonly record struct SkillRef(Func<int> Get, Action<int> Set);

    private static IReadOnlyList<SkillRef> AllSkills(Fighter f) => new SkillRef[]
    {
        new(() => f.Striking.PunchPower, v => f.Striking.PunchPower = v),
        new(() => f.Striking.PunchTechnique, v => f.Striking.PunchTechnique = v),
        new(() => f.Striking.KickPower, v => f.Striking.KickPower = v),
        new(() => f.Striking.KickTechnique, v => f.Striking.KickTechnique = v),
        new(() => f.Striking.ClinchStriking, v => f.Striking.ClinchStriking = v),
        new(() => f.Striking.StrikingDefense, v => f.Striking.StrikingDefense = v),
        new(() => f.Striking.Footwork, v => f.Striking.Footwork = v),
        new(() => f.Grappling.TakedownOffense, v => f.Grappling.TakedownOffense = v),
        new(() => f.Grappling.TakedownDefense, v => f.Grappling.TakedownDefense = v),
        new(() => f.Grappling.TopControl, v => f.Grappling.TopControl = v),
        new(() => f.Grappling.BottomGame, v => f.Grappling.BottomGame = v),
        new(() => f.Grappling.SubmissionOffense, v => f.Grappling.SubmissionOffense = v),
        new(() => f.Grappling.SubmissionDefense, v => f.Grappling.SubmissionDefense = v),
        new(() => f.Grappling.Scrambling, v => f.Grappling.Scrambling = v),
        new(() => f.Grappling.ClinchControl, v => f.Grappling.ClinchControl = v),
        new(() => f.Mental.FightIQ, v => f.Mental.FightIQ = v),
        new(() => f.Mental.Composure, v => f.Mental.Composure = v),
        new(() => f.Mental.Discipline, v => f.Mental.Discipline = v),
        // Athletic tools are included but grow slowly and are the first to decline.
        new(() => f.Physical.Strength, v => f.Physical.Strength = v),
        new(() => f.Physical.Cardio, v => f.Physical.Cardio = v),
        new(() => f.Physical.Speed, v => f.Physical.Speed = v),
    };

    private static IReadOnlyList<SkillRef> PhysicalSkills(Fighter f) => new SkillRef[]
    {
        new(() => f.Physical.Speed, v => f.Physical.Speed = v),
        new(() => f.Physical.Cardio, v => f.Physical.Cardio = v),
        new(() => f.Physical.Recovery, v => f.Physical.Recovery = v),
        new(() => f.Physical.Strength, v => f.Physical.Strength = v),
        new(() => f.Physical.Chin, v => f.Physical.Chin = v),
        new(() => f.Physical.Flexibility, v => f.Physical.Flexibility = v),
    };
}
