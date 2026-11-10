namespace MmaManager.Core.Model;

public enum WeightClass
{
    Strawweight, Flyweight, Bantamweight, Featherweight, Lightweight,
    Welterweight, Middleweight, LightHeavyweight, Heavyweight
}

public enum Sex { Male, Female }

public enum Stance { Orthodox, Southpaw, Switch }

/// <summary>
/// A fighter's headline archetype. Purely descriptive/scouting flavour and a
/// hint for AI fighter generation — the actual sim behaviour comes from the
/// individual ratings and tendencies, not from this label.
/// </summary>
public enum PrimaryStyle
{
    Striker, Kickboxer, Wrestler, BrazilianJiuJitsu, Grappler,
    Brawler, ClinchFighter, WellRounded
}

public enum Region
{
    NorthAmerica, SouthAmerica, Europe, Asia, Oceania, Africa, MiddleEast
}

public enum FighterStatus { Active, Injured, Suspended, Retired }

public enum ContractType { PerFight, Salaried }

public enum EventStatus { Scheduled, Completed, Cancelled }

/// <summary>The scale of a card: an ordinary Fight Night, a numbered PPV, or a marquee super-card.</summary>
public enum EventTier { FightNight, Ppv, SuperCard }

/// <summary>How far a promotion's reach extends — grows with prestige.</summary>
public enum OrgSize { City, Regional, National, Continental, International }

public enum FightMethod
{
    Knockout, TechnicalKnockout, SubmissionTap, TechnicalSubmission,
    UnanimousDecision, SplitDecision, MajorityDecision,
    Draw, NoContest, Disqualification
}

public enum InjurySeverity { Minor, Moderate, Serious, Career }
