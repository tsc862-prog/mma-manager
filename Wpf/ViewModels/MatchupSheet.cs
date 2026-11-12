using System.Windows;
using System.Windows.Media;
using MmaManager.Core.Model;

namespace MmaManager.Wpf.ViewModels;

// The fight sheet: everything the desk can say about one pairing before it is
// booked, grouped as a story (the fighters, between them, what's at stake, the
// night, the contracts) with the dense tale of the tape one toggle away. Built
// by BookingViewModel.Sheet from public facts, your own books and the scouts'
// reads; nothing here decides anything.

/// <summary>One past result as a chip: "W" / "L" / "D", with the method and opponent on hover.</summary>
public sealed record FormChip(string Text, CardTone Tone, string Tip);

/// <summary>A labelled line on the sheet ("Record", "21-3-0 · 12 KO, 4 SUB, 5 DEC").</summary>
public sealed record SheetLine(string Label, string Text, CardTone Tone = CardTone.Neutral)
{
    public bool HasLabel => Label.Length > 0;
}

/// <summary>A before/after figure for the night ("House", "3,100", "3,840", "+740").</summary>
public sealed record NightDelta(string Label, string Before, string After, string Change, CardTone Tone);

/// <summary>One corner of the fight sheet.</summary>
public sealed class CornerSheet
{
    public required int Id { get; init; }
    public required bool IsRed { get; init; }
    public required string Name { get; init; }
    public string Nickname { get; init; } = "";
    public bool HasNickname => Nickname.Length > 0;
    public ImageSource? Body { get; init; }
    /// <summary>"Champion", "#3 Lightweight", "Unranked at Lightweight".</summary>
    public required string Standing { get; init; }
    public bool IsChampion { get; init; }
    public required string Record { get; init; }
    /// <summary>Wins split by method, as star widths for a bar (KO, SUB, DEC).</summary>
    public GridLength KoWidth { get; init; }
    public GridLength SubWidth { get; init; }
    public GridLength DecWidth { get; init; }
    public bool HasWins { get; init; }
    public string WinsText { get; init; } = "";
    public string LossesText { get; init; } = "";
    public IReadOnlyList<FormChip> Form { get; init; } = Array.Empty<FormChip>();
    public bool HasForm => Form.Count > 0;
    public string StreakText { get; init; } = "";
    /// <summary>The person, line by line: age and home, style and frame, camp and temperament, draw.</summary>
    public IReadOnlyList<SheetLine> Facts { get; init; } = Array.Empty<SheetLine>();
    /// <summary>"Ready" / "Recovering until 3 Feb" and when he last fought.</summary>
    public string Readiness { get; init; } = "";
    public CardTone ReadinessTone { get; init; }
    /// <summary>"Strong 71" (the scouts' read, never the truth).</summary>
    public string ReadText { get; init; } = "";
    /// <summary>"Well scouted · 60% exact · 3 bouts on tape".</summary>
    public string ScoutText { get; init; } = "";
    /// <summary>What a win does for him, what a loss risks.</summary>
    public string WinDoes { get; init; } = "";
    public string LossRisks { get; init; } = "";
    /// <summary>Your fighter: what booking him costs and what it does to his deal and his mood.</summary>
    public bool IsYours { get; init; }
    public IReadOnlyList<SheetLine> Contract { get; init; } = Array.Empty<SheetLine>();
    public bool HasContract => Contract.Count > 0;
}

/// <summary>The whole fight sheet for one pairing.</summary>
public sealed class MatchupSheet
{
    public required CornerSheet Red { get; init; }
    public required CornerSheet Blue { get; init; }
    /// <summary>"MAIN CARD · LIGHTWEIGHT · 3 ROUNDS".</summary>
    public required string Kicker { get; init; }
    /// <summary>The one line on why this fight matters.</summary>
    public required string Headline { get; init; }
    public IReadOnlyList<DeskTag> Tags { get; init; } = Array.Empty<DeskTag>();
    /// <summary>The matchmaker, in a sentence or two (scouted reads only).</summary>
    public string Matchmaker { get; init; } = "";
    public int Competitiveness { get; init; }
    public int Appeal { get; init; }
    /// <summary>History, feud, camp and friendship, style clash, how it may end.</summary>
    public IReadOnlyList<SheetLine> Between { get; init; } = Array.Empty<SheetLine>();
    /// <summary>Stakes in the division: belt, eliminator, rankings.</summary>
    public IReadOnlyList<SheetLine> Stakes { get; init; } = Array.Empty<SheetLine>();
    /// <summary>The night before and after this bout.</summary>
    public IReadOnlyList<NightDelta> Night { get; init; } = Array.Empty<NightDelta>();
    public bool HasNight => Night.Count > 0;
    public string NightNote { get; init; } = "";
    /// <summary>Partner terms this bout meets, still misses, or breaks.</summary>
    public IReadOnlyList<SheetLine> Terms { get; init; } = Array.Empty<SheetLine>();
    public bool HasTerms => Terms.Count > 0;
    /// <summary>Risks this bout adds to the night.</summary>
    public IReadOnlyList<SheetLine> Risks { get; init; } = Array.Empty<SheetLine>();
    public bool HasRisks => Risks.Count > 0;
    /// <summary>The dense version (the Table toggle): every comparable line, red | label | blue.</summary>
    public IReadOnlyList<TaleRow> Tape { get; init; } = Array.Empty<TaleRow>();
    /// <summary>"Book this fight", "Swap Reyes in", or "" (a look only).</summary>
    public string ActionText { get; init; } = "";
    public bool HasAction => ActionText.Length > 0;
    /// <summary>The picker's own pairing: rounds and the title box are editable.</summary>
    public bool CanEditTerms { get; init; }
}
