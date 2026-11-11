using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Wpf.Art;

namespace MmaManager.Wpf.ViewModels;

// Shared card models behind the building blocks in App.xaml
// (FighterCardTemplate, ActionRowTemplate, HeadlineCardTemplate). Every screen
// that shows people as cards builds these; the templates do the rest.

/// <summary>How loudly a card's status line speaks.</summary>
public enum CardTone { Neutral, Good, Warn, Bad, Info }

/// <summary>
/// One fighter as a contact card: face, name, what the scout thinks, where the
/// deal stands, and one line on what is going on with them. Built fresh on every
/// refresh (hold <see cref="Id"/>, not the card). The portrait is loaded only
/// when a card is actually drawn, so a wall of 300 cards stays cheap.
/// </summary>
public sealed class FighterCardModel : Controls.ICardKey
{
    private readonly Fighter _f;
    private readonly World _w;
    private ImageSource? _portrait;

    public FighterCardModel(Fighter f, World w, Event? bookedOn = null, string? why = null)
    {
        _f = f;
        _w = w;
        Id = f.Id.Value;
        Name = $"{f.FirstName} {f.LastName}";
        Nickname = f.Nickname is { Length: > 0 } nick ? $"“{nick}”" : "";
        Division = FighterRow.DivisionLabel(f.WeightClass, f.Sex);
        Age = f.AgeOn(w.CurrentDate);
        Record = f.Record.ToString();
        Scout = FighterRow.Scouted(w, f);
        Why = why ?? "";

        var title = w.Titles.Values.FirstOrDefault(t => t.CurrentChampion == f.Id);
        IsChampion = title is not null;
        ChampionText = title is null ? "" : $"{FighterRow.DivisionLabel(title.WeightClass, f.Sex)} champion";
        IsFace = Core.Sim.Org.Franchise.IsFace(w, f);

        var c = f.Contract;
        if (c is null) { ContractText = "Free agent"; ContractTone = CardTone.Info; }
        else if (c.FightsRemaining <= 0) { ContractText = "Deal is up"; ContractTone = CardTone.Bad; }
        else if (c.FightsRemaining == 1) { ContractText = "Final fight on the deal"; ContractTone = CardTone.Warn; }
        else { ContractText = $"{c.FightsRemaining} fights left"; ContractTone = CardTone.Neutral; }

        int streak = f.Record.CurrentStreak;
        if (f.Status == FighterStatus.Injured)
        {
            StatusText = f.ReadyToFightOn is { } back ? $"Injured until {back:d MMM}" : "Injured";
            StatusTone = CardTone.Bad;
        }
        else if (bookedOn is not null)
        {
            StatusText = $"Booked: {bookedOn.Name} · {bookedOn.Date:d MMM}";
            StatusTone = CardTone.Info;
        }
        else if (f.ReadyToFightOn is { } ready && ready > w.CurrentDate)
        {
            StatusText = $"Recovering — ready {ready:d MMM}";
            StatusTone = CardTone.Warn;
        }
        else if (streak >= 3)
        {
            StatusText = $"Ready · on a {streak}-fight win streak";
            StatusTone = CardTone.Good;
        }
        else if (streak <= -3)
        {
            StatusText = $"Ready · lost {-streak} in a row";
            StatusTone = CardTone.Warn;
        }
        else
        {
            StatusText = "Ready to fight";
            StatusTone = CardTone.Good;
        }
        IsReady = f.Status != FighterStatus.Injured && bookedOn is null && !(f.ReadyToFightOn is { } r2 && r2 > w.CurrentDate);
        IsInjured = f.Status == FighterStatus.Injured;
    }

    public int Id { get; }
    public Fighter Model => _f;
    /// <summary>Loaded on first draw only (virtualised walls never draw off-screen cards).</summary>
    public ImageSource Portrait => _portrait ??= Portraits.For(_f, _w);
    public string Name { get; }
    public string Nickname { get; }
    public string Division { get; }
    public int Age { get; }
    public string Record { get; }
    /// <summary>The player's scouted read (fog of war) — never the truth.</summary>
    public int Scout { get; }
    /// <summary>Set on walls already grouped by division, so each card doesn't repeat it.</summary>
    public bool HideDivision { get; init; }
    public string Facts => HideDivision ? $"Age {Age}  ·  {Record}" : $"{Division}  ·  {Age}  ·  {Record}";
    public bool IsChampion { get; }
    public string ChampionText { get; }
    public bool IsFace { get; }
    public string ContractText { get; }
    public CardTone ContractTone { get; }
    public string StatusText { get; }
    public CardTone StatusTone { get; }
    public bool IsReady { get; }
    public bool IsInjured { get; }
    /// <summary>Why this fighter is on this screen right now (home page picks); empty on the roster wall.</summary>
    public string Why { get; }
    public bool HasWhy => Why.Length > 0;
    public bool IsSelected { get; init; }

    /// <summary>Everything the card draws, as one string: equal keys draw identically.</summary>
    public string CardKey => $"{Id}|{Name}|{Nickname}|{Facts}|{Scout}|{IsChampion}|{IsFace}|{ContractText}|{StatusText}|{StatusTone}|{Why}|{IsSelected}";
}

/// <summary>
/// One thing that needs the player: a glyph, a plain sentence, when it is due,
/// and up to two buttons. Ordered by the screen, most urgent first.
/// </summary>
public sealed class ActionItemModel
{
    public required string Glyph { get; init; }
    public required string Title { get; init; }
    public string Detail { get; init; } = "";
    public string Due { get; init; } = "";
    public CardTone Tone { get; init; } = CardTone.Neutral;
    public ImageSource? Picture { get; init; }
    public bool HasPicture => Picture is not null;

    public string PrimaryText { get; init; } = "";
    public ICommand? PrimaryCommand { get; init; }
    public object? PrimaryParameter { get; init; }
    public string SecondaryText { get; init; } = "";
    public ICommand? SecondaryCommand { get; init; }
    public object? SecondaryParameter { get; init; }
    public bool HasSecondary => SecondaryText.Length > 0;
}

/// <summary>A news story as a picture card: who it is about, the headline, and what was said.</summary>
public sealed class HeadlineCardModel
{
    public ImageSource? Picture { get; init; }
    public required string Kicker { get; init; }
    public required string Headline { get; init; }
    public string Quote { get; init; } = "";
    public bool HasQuote => Quote.Length > 0;
    public bool AboutPlayer { get; init; }
    public ICommand? OpenCommand { get; init; }
    public object? OpenParameter { get; init; }
}

/// <summary>A stop on a journey strip (the tier climb, the first-steps path).</summary>
public sealed class JourneyStop
{
    public required string Label { get; init; }
    public string Detail { get; init; } = "";
    public string Number { get; init; } = "";
    public bool Done { get; init; }
    public bool Current { get; init; }
    public bool IsFirst { get; init; }
    public bool IsLast { get; init; }
    public string ActionText { get; init; } = "";
    public ICommand? ActionCommand { get; init; }
    public bool HasAction => ActionText.Length > 0 && Current;
}
