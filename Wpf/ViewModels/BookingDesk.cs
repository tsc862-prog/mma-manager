using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Scouting;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

// The Card Desk's card models: what the bill, the picker, the room list, the
// night panel and the table draw. Every one is rebuilt on a refresh, so the
// screen holds ids (FightId, FighterId, VenueId), never these objects. Each
// carries the desk (BookingViewModel) so a template's buttons reach its
// commands without walking the visual tree. Nothing here decides anything:
// figures come from the sim (PlayerBooking, MatchmakingAdvisor, the scouts).

/// <summary>A short labelled fact on a card ("Bad blood · heat 60", "Last fight on his deal").</summary>
public sealed record DeskTag(string Text, CardTone Tone = CardTone.Neutral);

/// <summary>Plain words for a scouted read, the same bands the rating badge uses.</summary>
public static class ReadWords
{
    public static string Word(int read) => ScoutReport.Band(Math.Clamp(read, 0, 100)) switch
    {
        StatBand.Elite => "Elite",
        StatBand.Strong => "Strong",
        StatBand.Average => "Solid",
        _ => "Raw",
    };

    /// <summary>"Strong 71": what our scouts make of a fighter (never the truth).</summary>
    public static string Read(int read) => $"{Word(read)} {read}";
}

/// <summary>One corner of a booked bout or a proposal, as the bill draws it.</summary>
public sealed class BillCorner
{
    public BillCorner(Fighter f, World w, MatchmakingAdvisor advisor, bool body = false, CityId? hostCity = null)
    {
        Id = f.Id.Value;
        FighterId = f.Id;
        Name = $"{f.FirstName} {f.LastName}";
        Short = f.LastName;
        Nickname = f.Nickname is { Length: > 0 } n ? $"“{n}”" : "";
        var standing = advisor.StandingOf(w, f.Id);
        IsChampion = standing.Kind == Standing.Champion;
        Rank = f.LocalTalent ? "Local" : standing.Kind == Standing.Unranked ? "" : standing.Label;
        Record = f.Record.ToString();
        Read = FighterRow.Scouted(w, f);
        ReadText = f.LocalTalent ? "Local club fighter" : ReadWords.Read(Read);
        string? home = f.HomeCity is { } hc ? w.Get(hc)?.Name : null;
        AtHome = hostCity is { } host && f.HomeCity == host;
        string style = FighterRow.Pretty(f.PrimaryStyle.ToString());
        var bits = new List<string>();
        if (Rank.Length > 0 && !f.LocalTalent) bits.Add(Rank == "C" ? "Champion" : Rank);
        bits.Add(Record);
        bits.Add(style);
        if (home is not null) bits.Add(home);
        Facts = string.Join(" · ", bits);
        Purse = f.Contract is { } c ? $"{c.PursePerFight} purse" : "";
        _f = f;
        _w = w;
        if (body) BodyImage = Portraits.Body(f, w);
    }

    private readonly Fighter _f;
    private readonly World _w;

    public int Id { get; }
    public FighterId FighterId { get; }
    public string Name { get; }
    public string Short { get; }
    public string Nickname { get; }
    public bool HasNickname => Nickname.Length > 0;
    /// <summary>"C", "#3", "Local" or "" (unranked).</summary>
    public string Rank { get; }
    public bool HasRank => Rank.Length > 0;
    public bool IsChampion { get; }
    public string Record { get; }
    /// <summary>The scouted read (fog of war).</summary>
    public int Read { get; }
    public string ReadText { get; }
    public string Facts { get; }
    public string Purse { get; }
    public bool AtHome { get; }
    /// <summary>Full-body art, drawn for the main event only.</summary>
    public ImageSource? BodyImage { get; }
    /// <summary>The face, decoded off the UI thread on first draw.</summary>
    public LazyFace Face => _face ??= new LazyFace(ready => Portraits.Thumb(_f, _w, ready));
    private LazyFace? _face;
}

/// <summary>One booked bout on the bill, top-billed (the main event first).</summary>
public sealed class BillBout
{
    public required BookingViewModel Desk { get; init; }
    public required FightId Id { get; init; }
    /// <summary>Index in the sim's running order (0 = first prelim, last = main event).</summary>
    public required int CardIndex { get; init; }
    public required int CardCount { get; init; }
    public required string SlotLabel { get; init; }
    public required BillCorner Red { get; init; }
    public required BillCorner Blue { get; init; }
    public required string Kicker { get; init; }
    public required string Division { get; init; }
    public required string Rounds { get; init; }
    public required bool IsTitle { get; init; }
    public required bool IsMainEvent { get; init; }
    public required bool IsCoMain { get; init; }
    public required bool IsPrelim { get; init; }
    public bool IsMainCard => !IsPrelim;
    /// <summary>A plain fight on the main card (not the main event, not the co-main).</summary>
    public bool IsRow => !IsMainEvent;
    public IReadOnlyList<DeskTag> Sells { get; init; } = Array.Empty<DeskTag>();
    public IReadOnlyList<DeskTag> WatchFor { get; init; } = Array.Empty<DeskTag>();
    public bool HasSells => Sells.Count > 0;
    public bool HasWatchFor => WatchFor.Count > 0;
    /// <summary>The matchmaker's read in a sentence (from our scouts' reads).</summary>
    public string Matchmaker { get; init; } = "";
    public bool HasMatchmaker => Matchmaker.Length > 0;
    public bool CanMoveUp => CardIndex < CardCount - 1;
    public bool CanMoveDown => CardIndex > 0;
    /// <summary>Text for screen readers / tooltips: "Keane vs Szymanski".</summary>
    public string Pairing => $"{Red.Short} vs {Blue.Short}";
}

/// <summary>A proposed pairing for an open slot, with the matchmaker's reason.</summary>
public sealed class ProposalCard : ObservableObject
{
    public required BookingViewModel Desk { get; init; }
    public required BoutProposal Proposal { get; init; }
    public required BoutSlot Slot { get; init; }
    public required BillCorner Red { get; init; }
    public required BillCorner Blue { get; init; }
    public required string Division { get; init; }
    public required string Reason { get; init; }
    /// <summary>"Title fight · 5 rds" or "3 rds".</summary>
    public required string Terms { get; init; }
    public required string Read { get; init; }
    public string SlotLabel { get; init; } = "";
    public string Pairing => $"{Red.Short} vs {Blue.Short}";

    private bool _isChecked = true;
    /// <summary>Ticked to book in a bulk proposal.</summary>
    public bool IsChecked { get => _isChecked; set { if (Set(ref _isChecked, value)) Desk.RaiseProposedCount(); } }
}

/// <summary>A fighter in the picker's division ladder, ready or not (with why).</summary>
public sealed class LadderEntry
{
    public LadderEntry(Fighter f, World w, BookingViewModel desk, string rank, string line, bool available, string why)
    {
        Desk = desk;
        Id = f.Id.Value;
        FighterId = f.Id;
        Name = f.FullName;
        Rank = rank;
        Record = f.Record.ToString();
        Read = FighterRow.Scouted(w, f);
        ReadText = ReadWords.Read(Read);
        Line = line;
        Available = available;
        Why = why;
        _f = f;
        _w = w;
    }
    private readonly Fighter _f;
    private readonly World _w;
    public BookingViewModel Desk { get; }
    public int Id { get; }
    public FighterId FighterId { get; }
    public string Name { get; }
    /// <summary>"C", "#3" or "NR".</summary>
    public string Rank { get; }
    public string Record { get; }
    public int Read { get; }
    public string ReadText { get; }
    /// <summary>What the matchmaker says about this pick (or the deal / form line).</summary>
    public string Line { get; }
    public bool HasLine => Line.Length > 0;
    public bool Available { get; }
    /// <summary>Why the fighter can't be booked on this card ("" when they can).</summary>
    public string Why { get; }
    public LazyFace Face => _face ??= new LazyFace(ready => Portraits.Thumb(_f, _w, ready));
    private LazyFace? _face;
    /// <summary>What a click does: pick a corner, or swap this fighter into a bout.</summary>
    public System.Windows.Input.ICommand? Command { get; init; }
}

/// <summary>A room the card could run in (VenueOptions), in words.</summary>
public sealed class VenueRow
{
    public required BookingViewModel Desk { get; init; }
    public required VenueId Id { get; init; }
    public required string Name { get; init; }
    public required string Seats { get; init; }
    public required string Rent { get; init; }
    public required string House { get; init; }
    public required string BoxOffice { get; init; }
    public required bool BoxOfficePositive { get; init; }
    public required bool SimPick { get; init; }
    public required bool Selected { get; init; }
    public string Note { get; init; } = "";
    public bool HasNote => Note.Length > 0;
}

/// <summary>One money line in the night panel ("Tickets (1,212 × $62)", "+$75,144").</summary>
public sealed record MoneyLine(string Label, string Amount, CardTone Tone, string Detail = "")
{
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>One partner clause, checked against the card as it will run.</summary>
public sealed record TermLine(string Text, TermStatus Status, string Gap)
{
    public bool IsMet => Status == TermStatus.Met;
    public bool IsMissed => Status == TermStatus.Missed;
    public bool IsOnTheNight => Status == TermStatus.JudgedOnTheNight;
    public string Glyph => Status switch { TermStatus.Met => "✓", TermStatus.Missed => "✗", _ => "?" };
    public bool HasGap => Gap.Length > 0;
}

/// <summary>A sponsor or rights partner: what this card pays them and how their terms stand.</summary>
public sealed class PartnerLine
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Fee { get; init; }
    public required string Strikes { get; init; }
    public required CardTone Tone { get; init; }
    public required IReadOnlyList<TermLine> Terms { get; init; }
    public bool HasTerms => Terms.Count > 0;
    public string Verdict { get; init; } = "";
    public bool HasVerdict => Verdict.Length > 0;
}

/// <summary>A risk the desk can see coming, in plain words.</summary>
public sealed record RiskLine(string Text, CardTone Tone, string Icon, string Detail = "")
{
    /// <summary>The full list behind a folded line (the tooltip), or the text itself.</summary>
    public string Tip => Detail.Length > 0 ? Detail : Text;
}

/// <summary>A row of the dense roster table (the Table view): one of your fighters for this card's date.</summary>
public sealed class DeskTableRow
{
    public required int Id { get; init; }
    public required string Rank { get; init; }
    public required int RankSort { get; init; }
    public required string Name { get; init; }
    public required string Division { get; init; }
    public required string Record { get; init; }
    public required int RecordSort { get; init; }
    public required int Read { get; init; }
    public required string ReadText { get; init; }
    public required int Popularity { get; init; }
    public required string LastFought { get; init; }
    public required int LastFoughtSort { get; init; }
    public required int FightsLeft { get; init; }
    public required int Morale { get; init; }
    public required string Availability { get; init; }
    public required bool IsAvailable { get; init; }
    public required bool OnThisCard { get; init; }
    public required string Notes { get; init; }
    /// <summary>The face, decoded off the UI thread when the row is first drawn.</summary>
    public required LazyFace Face { get; init; }
}
