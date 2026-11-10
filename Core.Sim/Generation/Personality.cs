using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// Who a fighter is outside the cage.
///
/// Before this existed the sim read temperament off <see cref="Fighter.PersonaTag"/>
/// — a tag stamped from the ART PACK when the UI assigned a portrait. That made a
/// fighter's character a function of what their picture looked like, gave headless
/// runs no personalities at all, and left the authored bios unable to cash their
/// own cheques (a man written as "predicts rounds, calls shots" behaved like
/// whatever face he was given).
///
/// Everything here is HASH-DERIVED from the fighter's id, not drawn from the
/// shared generation stream, so adding it doesn't reshuffle a single seeded
/// world — the same trick <c>StarRoll</c> uses for the it-factor.
/// </summary>
public static class Personality
{
    /// <summary>Roll (deterministically) who this fighter is, and the numbers behind it.</summary>
    public static void Apply(Fighter f) => ApplyTo(f, RollDisposition(f));

    /// <summary>
    /// Set a KNOWN disposition and roll the numbers around it — how the designed
    /// roster's authored archetypes enter the world.
    /// </summary>
    public static void ApplyTo(Fighter f, Disposition disposition)
    {
        f.Disposition = disposition;
        var (ego, prof, temper, ambition) = Baseline(disposition);

        // Jitter around the archetype so two loudmouths aren't the same loudmouth,
        // and let the competitive attributes bleed in where they genuinely relate:
        // in-cage discipline tracks out-of-cage professionalism, and a fighter the
        // public already loves has had their ego watered.
        f.Temperament.Ego = Clamp(ego + Jitter(f, 11, 12) + (f.Intangibles.Popularity.Value - 35) / 5);
        f.Temperament.Professionalism = Clamp(prof + Jitter(f, 12, 12) + (f.Mental.Discipline.Value - 50) / 4);
        f.Temperament.Temper = Clamp(temper + Jitter(f, 13, 12) - (f.Mental.Composure.Value - 50) / 4);
        f.Temperament.Ambition = Clamp(ambition + Jitter(f, 14, 12));
    }

    /// <summary>The archetype's centre of gravity — jitter and attributes move it from here.</summary>
    private static (int Ego, int Professionalism, int Temper, int Ambition) Baseline(Disposition d) => d switch
    {
        Disposition.QuietProfessional => (35, 80, 25, 45),
        Disposition.Loudmouth         => (82, 50, 62, 70),
        Disposition.Showman           => (70, 55, 45, 68),
        Disposition.Hothead           => (68, 35, 88, 55),
        Disposition.Mercenary         => (62, 60, 40, 88),
        Disposition.CompanyMan        => (40, 72, 35, 22),
        Disposition.Warhorse          => (45, 68, 50, 40),
        _                             => (35, 85, 25, 50),   // Student
    };

    /// <summary>
    /// Who talks and who doesn't. Charisma is the strongest tell — the sport's
    /// quiet men are quiet partly because the microphone was never their friend —
    /// but every archetype stays reachable from every band.
    /// </summary>
    private static Disposition RollDisposition(Fighter f)
    {
        int charisma = f.Intangibles.Charisma.Value;
        var weights = charisma >= 65 ? LoudBand : charisma >= 40 ? MidBand : QuietBand;

        int total = 0;
        foreach (int w in weights) total += w;
        int roll = (int)(Hash(f.Id.Value, 7) % (uint)total);
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll < 0) return (Disposition)i;
        }
        return Disposition.QuietProfessional;
    }

    // Weights in Disposition order: Quiet, Loudmouth, Showman, Hothead,
    // Mercenary, CompanyMan, Warhorse, Student.
    private static readonly int[] LoudBand  = { 10, 20, 25, 12, 10,  8, 10,  5 };
    private static readonly int[] MidBand   = { 20, 10, 12,  8, 10, 14, 14, 12 };
    private static readonly int[] QuietBand = { 30,  1,  3,  8,  8, 16, 14, 20 };

    private static int Jitter(Fighter f, uint salt, int range) =>
        (int)(Hash(f.Id.Value, salt) % (uint)(2 * range + 1)) - range;

    private static int Clamp(int v) => Math.Clamp(v, 1, 99);

    // Murmur-style finalizer: stable per fighter, independent of generation order.
    private static uint Hash(int id, uint salt)
    {
        unchecked
        {
            uint h = (uint)id * 2654435761u ^ (salt * 0x9E3779B1u);
            h ^= h >> 15; h *= 0x2C1B3C6Du;
            h ^= h >> 12; h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }
    }

    // --- How the sport talks about them ---------------------------------------

    /// <summary>The label shown on the profile.</summary>
    public static string Label(Disposition d) => d switch
    {
        Disposition.QuietProfessional => "Quiet professional",
        Disposition.Loudmouth => "Loudmouth",
        Disposition.Showman => "Showman",
        Disposition.Hothead => "Hothead",
        Disposition.Mercenary => "Mercenary",
        Disposition.CompanyMan => "Company man",
        Disposition.Warhorse => "Warhorse",
        _ => "Student of the game",
    };

    /// <summary>One line of scouting on the person rather than the fighter.</summary>
    public static string Note(Disposition d) => d switch
    {
        Disposition.QuietProfessional => "Says little, trains hard, turns up in shape.",
        Disposition.Loudmouth => "Sells the fight with their mouth and believes every word of it.",
        Disposition.Showman => "Here for the show — the walkout, the callout, the crowd.",
        Disposition.Hothead => "A short fuse. Great television, regular trouble.",
        Disposition.Mercenary => "Follows the money and doesn't pretend otherwise.",
        Disposition.CompanyMan => "Bleeds the house colours; signs whatever's put in front of them.",
        Disposition.Warhorse => "Wants the hardest fight available. Asks about money later.",
        _ => "A student of the game: film, technique, the long project.",
    };
}
