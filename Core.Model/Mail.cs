namespace MmaManager.Core.Model;

/// <summary>The kind of message in the player's inbox.</summary>
public enum MailCategory
{
    ContractAlert,
    BoardNote,
    FighterRequest,
    Milestone,
    General,
}

/// <summary>
/// A message to the player — a contract about to lapse, a note from the owner, a
/// fighter's request, a milestone. Unlike news (world-wide, immutable), mail is
/// addressed to the player and can be marked read.
/// </summary>
public sealed class MailMessage
{
    public required DateOnly Date { get; init; }
    public required MailCategory Category { get; init; }
    public required string From { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public bool Read { get; set; }

    /// <summary>Who/what the message is about — the UI's deep links (both optional).</summary>
    public FighterId? AboutFighter { get; init; }
    public EventId? AboutEvent { get; init; }
}
