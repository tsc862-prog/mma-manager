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

        // Who they fight for (the world wall shows this instead of the deal).
        if (c is not null && w.Get(c.Promotion) is { } org)
        {
            _org = org;
            Affiliation = org.Id == w.PlayerPromotion ? $"{org.Abbreviation} · your promotion" : $"{org.Abbreviation} · {org.Name}";
            IsMine = org.Id == w.PlayerPromotion;
        }
        else Affiliation = "Free agent";
        IsShortlisted = w.Shortlist.Contains(f.Id);
    }

    private readonly Promotion? _org;
    private ImageSource? _orgLogo;

    public int Id { get; }
    public Fighter Model => _f;
    /// <summary>Asked for on first draw only (virtualised walls never draw off-screen cards),
    /// decoded small on a worker thread (<see cref="Portraits.Thumb(Fighter, World, Action{ImageSource})"/>);
    /// the face appears when it is ready.</summary>
    public ImageSource? Portrait => Face.Image;

    /// <summary>The face as a tiny observable holder — templates bind <c>Face.Image</c>. Only this one
    /// binding listens for the late arrival; the card itself stays a plain object (cheap to bind).</summary>
    public LazyFace Face => _face ??= new LazyFace(ready => Portraits.Thumb(_f, _w, ready));
    private LazyFace? _face;
    /// <summary>"AFC · Apex Fighting Championship", or "Free agent".</summary>
    public string Affiliation { get; } = "";
    /// <summary>The promotion's logo (null for a free agent), drawn on first use.</summary>
    public ImageSource? AffiliationLogo => _org is null ? null : _orgLogo ??= LogoFactory.ForPromotion(_org);
    public bool HasAffiliationLogo => _org is not null;
    public bool IsMine { get; }
    public bool IsShortlisted { get; }
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
    public string CardKey => $"{Id}|{Name}|{Nickname}|{Facts}|{Scout}|{IsChampion}|{IsFace}|{ContractText}|{StatusText}|{StatusTone}|{Why}|{IsSelected}|{Affiliation}|{IsShortlisted}";
}

/// <summary>
/// A picture that may arrive late: asked for on first read (so only drawn cards ask),
/// returned at once when ready, otherwise filled in when the worker thread hands it back.
/// </summary>
public sealed class LazyFace : System.ComponentModel.INotifyPropertyChanged
{
    private readonly Func<Action<ImageSource>, ImageSource?> _load;
    private ImageSource? _image;
    private bool _asked;

    public LazyFace(Func<Action<ImageSource>, ImageSource?> load) => _load = load;

    public ImageSource? Image
    {
        get
        {
            if (!_asked)
            {
                _asked = true;
                _image = _load(img => { _image = img; PropertyChanged?.Invoke(this, new(nameof(Image))); });
            }
            return _image;
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// One thing that needs the player: a line icon, a plain sentence, when it is due,
/// and up to two buttons. Ordered by the screen, most urgent first.
/// </summary>
public sealed class ActionItemModel
{
    /// <summary>A line icon from the App.xaml set (Calendar, Pen, Bell, Money, …).</summary>
    public required string Icon { get; init; }
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

/// <summary>
/// A news story as a card you can follow: a face (or the promotion's logo), the
/// kind and day, the headline, what was said, and links to the people, the
/// promotion and the event it is about. The picture is loaded on first draw only.
/// </summary>
public sealed class StoryCardModel : Controls.ICardKey
{
    /// <param name="picture">Returns the picture now, or null and calls back when it is decoded.</param>
    public StoryCardModel(Func<Action<ImageSource>, ImageSource?> picture) => Face = new LazyFace(picture);

    /// <summary>The face or logo; templates bind <c>Face.Image</c> (it may land after the card is drawn).</summary>
    public LazyFace Face { get; }
    public ImageSource? Picture => Face.Image;
    /// <summary>Nothing to show at all (a wire story about no one): the template draws the paper's icon.</summary>
    public bool HasNoPicture { get; init; }
    /// <summary>True when the picture is a logo (drawn padded on a dark tile, not cropped like a face).</summary>
    public bool PictureIsLogo { get; init; }
    public required string Kicker { get; init; }
    public required string Icon { get; init; }
    public required string Headline { get; init; }
    /// <summary>The story text with any quote taken out (the quote is shown on its own).</summary>
    public string Body { get; init; } = "";
    public bool HasBody => Body.Length > 0;
    public string Quote { get; init; } = "";
    public bool HasQuote => Quote.Length > 0;
    public bool AboutPlayer { get; init; }

    public int FighterId { get; init; }
    public string FighterName { get; init; } = "";
    public bool HasFighter => FighterId != 0;
    public int PromotionId { get; init; }
    public string PromotionLabel { get; init; } = "";
    public bool HasPromotion => PromotionId != 0;
    public int EventId { get; init; }
    public string EventName { get; init; } = "";
    public bool HasEvent => EventId != 0;

    /// <summary>What clicking the card itself opens (the person, else the event, else the promotion).</summary>
    public ICommand? OpenCommand { get; init; }
    public object? OpenParameter { get; init; }

    public string Key { get; init; } = "";
    public string CardKey => Key;
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
