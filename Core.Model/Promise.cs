namespace MmaManager.Core.Model;

/// <summary>What a promotion promised a fighter at the table (contracts stage 9: promises with teeth).</summary>
public enum PromiseKind
{
    /// <summary>At least <see cref="Promise.Fights"/> fights on its cards inside the year.</summary>
    Activity,

    /// <summary>A fight on one of its cards in his home country inside the year.</summary>
    HomeShow,

    /// <summary>A title fight on one of its cards (or the belt itself) inside the year.</summary>
    TitleShot,
}

/// <summary>Where a promise stands.</summary>
public enum PromiseState
{
    /// <summary>Still to be kept: the deadline has not passed.</summary>
    Open,

    /// <summary>Kept: he got what he was promised.</summary>
    Kept,

    /// <summary>Broken: the deadline passed, or he left, without it being kept. He remembers.</summary>
    Broken,

    /// <summary>Fell away through nobody's fault: he retired, the promotion closed.</summary>
    Void,
}

/// <summary>
/// A promise a player-rules promotion wrote into a fighter's deal (World.Promises, id
/// order). Made at signing (Core.Sim Contracts.Promises) from the terms on the slip, tested
/// monthly against the live world (its cards, its belts, where it staged them) and resolved
/// by the sim: kept, broken or void. A promise belongs to the fighter and the promotion, not
/// to one contract: re-signing him does not reset it. Resolved promises are kept a few years
/// because a broken one is remembered at the next table.
/// </summary>
public sealed class Promise
{
    public required int Id { get; init; }
    public required FighterId Fighter { get; init; }
    public required PromotionId Promotion { get; init; }
    public required PromiseKind Kind { get; init; }

    /// <summary>For <see cref="PromiseKind.Activity"/>: how many fights he was promised (else 0).</summary>
    public int Fights { get; init; }

    public required DateOnly Made { get; init; }

    /// <summary>Kept by this day (moved on only while he is injured and can't fight).</summary>
    public DateOnly Deadline { get; set; }

    public PromiseState State { get; set; } = PromiseState.Open;

    /// <summary>The day it was kept, broken or fell away; null while open.</summary>
    public DateOnly? Resolved { get; set; }

    /// <summary>The owner has been warned that time is running out on it (sent once).</summary>
    public bool Warned { get; set; }

    /// <summary>How it ended, in plain words ("the deadline passed", "he was released"); empty while open.</summary>
    public string Note { get; set; } = "";

    public bool Live => State == PromiseState.Open;
}
