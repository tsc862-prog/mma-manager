namespace MmaManager.Core.Model;

// Ratings are grouped into small objects rather than living as ~35 flat
// properties on Fighter. Grouping keeps the model readable, lets the fight
// engine ask for exactly the group it needs, and makes it easy to display a
// "Striking" panel in the UI. All groups are mutable classes because training
// nudges individual ratings over a career.

/// <summary>Everything about standing/clinch offence and defence with strikes.</summary>
public sealed class StrikingRatings
{
    public Rating PunchPower { get; set; }
    public Rating PunchTechnique { get; set; }   // crispness / accuracy / combinations
    public Rating KickPower { get; set; }
    public Rating KickTechnique { get; set; }
    public Rating ClinchStriking { get; set; }   // dirty boxing, elbows, knees
    public Rating StrikingDefense { get; set; }  // head movement, blocking, distance
    public Rating Footwork { get; set; }         // range control, angles, cage cutting
}

/// <summary>Wrestling, ground control, and submissions, both directions.</summary>
public sealed class GrapplingRatings
{
    public Rating TakedownOffense { get; set; }
    public Rating TakedownDefense { get; set; }
    public Rating TopControl { get; set; }       // holding/advancing position on top
    public Rating BottomGame { get; set; }       // guard retention, sweeps, stand-ups
    public Rating SubmissionOffense { get; set; }
    public Rating SubmissionDefense { get; set; }
    public Rating Scrambling { get; set; }       // transitions, winning scrambles
    public Rating ClinchControl { get; set; }    // trips, throws, controlling the tie-up
}

/// <summary>Athletic and durability attributes.</summary>
public sealed class PhysicalRatings
{
    public Rating Strength { get; set; }
    public Rating Cardio { get; set; }           // the gas tank — the sim's secret sauce
    public Rating Speed { get; set; }            // hand/foot speed, explosiveness
    public Rating Chin { get; set; }             // resistance to being hurt/finished
    public Rating Recovery { get; set; }         // between rounds and post-injury
    public Rating Flexibility { get; set; }      // aids guard, escapes, sub defence
}

/// <summary>Mental attributes that shape how a fighter performs under fire.</summary>
public sealed class MentalRatings
{
    public Rating FightIQ { get; set; }          // reads, adjustments, shot selection
    public Rating Composure { get; set; }        // performing when hurt or behind
    public Rating Heart { get; set; }            // comebacks, refusing to quit
    public Rating Discipline { get; set; }       // avoids fouls, sticks to the game plan
}

/// <summary>Business-facing attributes that drive draw and marketability.</summary>
public sealed class Intangibles
{
    public Rating Popularity { get; set; }       // ticket/PPV draw
    public Rating Charisma { get; set; }         // mic skills, star quality
}

/// <summary>
/// WHO THIS PERSON IS outside the cage — the shorthand the sport would use for
/// them. Displayed on the profile and used to pick the words they say; the
/// numeric consequences live in <see cref="Temperament"/>, which is rolled
/// around whichever disposition a fighter has.
///
/// (This replaces the old behavioural read on <see cref="Fighter.PersonaTag"/>,
/// which was stamped from the ART PACK at load — so a fighter's temperament was
/// a function of what their portrait looked like, and headless runs had none at
/// all. PersonaTag survives as what it always really was: a marketable LOOK.)
/// </summary>
public enum Disposition
{
    /// <summary>Says little, trains hard, turns up in shape. The coaches' favourite.</summary>
    QuietProfessional = 0,
    /// <summary>Sells fights with their mouth and believes every word of it.</summary>
    Loudmouth = 1,
    /// <summary>Here to entertain — the walkout, the callout, the crowd.</summary>
    Showman = 2,
    /// <summary>A short fuse. Great television, frequent trouble.</summary>
    Hothead = 3,
    /// <summary>Follows the money, and says so. No hard feelings, no loyalty either.</summary>
    Mercenary = 4,
    /// <summary>Bleeds the house colours. Signs what's put in front of them.</summary>
    CompanyMan = 5,
    /// <summary>Wants the hardest fight available, every time. Ask about money later.</summary>
    Warhorse = 6,
    /// <summary>A student of the game — film, technique, the long project.</summary>
    Student = 7,
}

/// <summary>
/// The behavioural sliders the sim actually reads, 0–100. Rolled around the
/// fighter's <see cref="Disposition"/> at creation and stable for a career —
/// this is character, not form.
/// </summary>
public sealed class Temperament
{
    /// <summary>What they think they're worth. Drives asking price, title-shot demands, callouts.</summary>
    public Rating Ego { get; set; } = 50;

    /// <summary>Camp discipline: making weight, doing the work, staying out of the papers.</summary>
    public Rating Professionalism { get; set; } = 50;

    /// <summary>The short fuse — beefs started, fouls thrown, trouble found.</summary>
    public Rating Temper { get; set; } = 50;

    /// <summary>Hunger for the bigger stage. High = drifts toward whoever is above you.</summary>
    public Rating Ambition { get; set; } = 50;
}

/// <summary>
/// 0–100 behavioural sliders that tell the fight engine HOW this fighter likes
/// to fight. These describe style, not ability, and aren't developed by
/// training the way skills are.
/// </summary>
public sealed class Tendencies
{
    /// <summary>0 = always seeks to grapple, 100 = always seeks to strike.</summary>
    public Rating StrikingPreference { get; set; }

    /// <summary>Output and pressure. High = pushes the pace and volume.</summary>
    public Rating Aggression { get; set; }

    /// <summary>Cardio management. High = paces sensibly; low = empties the tank early.</summary>
    public Rating Pace { get; set; }

    /// <summary>Leading vs waiting. High = a patient counter-fighter.</summary>
    public Rating Counterfighting { get; set; }

    /// <summary>How hard they hunt a finish vs playing it safe on the lead.</summary>
    public Rating FinishInstinct { get; set; }
}
