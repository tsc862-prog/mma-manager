using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Living;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// OPEN TRYOUT: local men called in to sign on the floor purse. Two kinds, both the sim's
/// (<see cref="PlayerFounding.TryoutOptions"/>): the one FREE tryout of a house the player founded (its first step after
/// founding), and the RECURRING tryout any of his houses in Origins is offered when it is short of men for its next night,
/// at most twice a year. The screen says which is on offer, holds it on his word (<see cref="PlayerFounding.HoldTryout"/>),
/// then shows the men THAT tryout signed (as his scouts read them) and what to do next. With nothing on offer it says the
/// sim's reason. Holds ids only; everything else is read from the sim.
/// </summary>
public sealed class TryoutViewModel : ObservableObject
{
    private World? _w;

    public TryoutViewModel()
    {
        HoldCommand = new RelayCommand(Hold, () => CanHold);
    }

    public string HouseName { get; private set; } = "";
    public ImageSource? Logo { get; private set; }
    public string Kicker { get; private set; } = "";
    public string Headline { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public string Refusal { get; private set; } = "";
    public bool HasRefusal => Refusal.Length > 0;
    public bool CanHold { get; private set; }
    /// <summary>The offer on screen is the free founding tryout (else the recurring one, or none).</summary>
    public bool IsFree { get; private set; }
    public string HoldText { get; private set; } = "Call the open tryout";
    public string HoldTip { get; private set; } = "";
    public string BookText { get; private set; } = "Book your first night ▸";
    public string NextLine { get; private set; } = "";
    public bool HasNextLine => NextLine.Length > 0;
    public ObservableCollection<string> Rules { get; } = new();
    public bool HasRules => Rules.Count > 0;
    public string TargetsLine { get; private set; } = "";

    /// <summary>"Just founded" words for the first visit after founding (empty afterwards).</summary>
    public string Welcome { get; set; } = "";
    public bool HasWelcome => Welcome.Length > 0;

    public ObservableCollection<FighterCardModel> Signed { get; } = new();
    public bool HasSigned => Signed.Count > 0;
    public string SignedLine { get; private set; } = "";

    public ICommand HoldCommand { get; }
    public ICommand? BookCommand { get; set; }
    public ICommand? LaterCommand { get; set; }

    /// <summary>The tryout last held from this screen was the free founding one (for the shell's status line).</summary>
    public bool JustHeldFree { get; private set; }

    /// <summary>Fired after the tryout is held (the shell refreshes every screen).</summary>
    public Action? OnChanged { get; set; }

    public void Attach(World w)
    {
        _w = w;
        var today = w.CurrentDate;
        var promo = w.Get(w.PlayerPromotion);
        HouseName = promo?.Name ?? "";
        Logo = promo is null ? null : LogoFactory.ForPromotion(promo);
        var offer = PlayerFounding.TryoutOptions(w, today);
        var freeOn = promo is null ? null : PlayerFounding.TryoutHeldOn(promo);
        var lastOn = promo is null ? null : PlayerFounding.LastTryoutOn(promo);
        CanHold = offer.Available;
        IsFree = offer.Available && offer.Free;
        Refusal = "";
        NextLine = "";
        // The tryout whose men the screen lists: none while one is on offer, else the last one held (if any).
        DateOnly? shownOn = offer.Available ? null : lastOn;
        bool shownFree = shownOn is not null && shownOn == freeOn;

        if (offer.Available && offer.Free)
        {
            Kicker = "YOUR FIRST FIGHTERS";
            Headline = "Call your free open tryout";
            Detail = offer.Detail + " You can call it now or come back to it from the Open Tryout app; it is yours once.";
            HoldText = "Call the open tryout";
            HoldTip = "Free, and once only: local men who fit a division sign on minimum terms";
            BookText = "Book your first night ▸";
        }
        else if (offer.Available)
        {
            Kicker = "SHORT OF MEN  ·  AN OPEN TRYOUT";
            Headline = "Call in the local hopefuls";
            string house = promo?.Name ?? "the house";
            Detail = $"{house} is short of men for its next night. The local gyms will send their hopefuls, and every one who fits a division "
                     + $"signs on the sport's floor purse, on {house}'s usual terms, three fights. "
                     + (offer.Turnout > 0
                         ? $"It would sign up to {offer.Turnout} {(offer.Turnout == 1 ? "man" : "men")}, enough to bring the roster back to strength. "
                         : "")
                     + "It costs nothing, but the gyms can only do this twice a year, so it is worth calling when you need it.";
            HoldText = "Call the open tryout";
            HoldTip = "Free; at most twice a year, and only while the house is short of men";
            BookText = "Book your next night ▸";
            NextLine = "Not now? The offer stands while the house is short of men. The free-agent market, on Contracts, is the other way to fill the roster.";
        }
        else if (shownOn is { } on)
        {
            Kicker = $"THE OPEN TRYOUT  ·  HELD {on:d MMMM yyyy}".ToUpperInvariant();
            Headline = shownFree ? "The tryout is done: now book a night" : "New men on the books";
            Detail = shownFree
                ? "These are the men who signed on the day. The rest of your roster comes off the free-agent market, at their asking terms."
                : "The local gyms sent their hopefuls. Put them on your next card, and sign the rest you need off the free-agent market.";
            BookText = shownFree ? "Book your first night ▸" : "Book your next night ▸";
            // When the next one could come (the sim's spacing), said plainly.
            if (offer.NextPossibleOn is { } next)
                NextLine = $"The gyms can stage another open tryout from {next:d MMMM yyyy}, if you are short of men then.";
        }
        else
        {
            // Nothing on offer and never held: the sim's reason, plainly.
            Kicker = "THE OPEN TRYOUT";
            Headline = "No open tryout today";
            Detail = "The local gyms stage an open tryout for a house that is short of men for its next night, at most twice a year.";
            Refusal = offer.Refusal ?? "";
            BookText = "Book a night ▸";
        }

        Rules.Clear();
        if (promo is not null) foreach (var r in HouseRules.For(promo)) Rules.Add(r);
        var targets = promo?.Objectives.Where(o => o.Status == ObjectiveStatus.Active).ToList() ?? new();
        TargetsLine = promo is null ? ""
            : targets.Count == 0
                ? $"Nobody will judge {promo.Name} on what is left of {today.Year}: the owner's targets start on 1 January {today.Year + 1}."
                : $"The owner's targets, to {targets.Max(o => o.Deadline):d MMMM yyyy}: " + string.Join("; ", targets.Select(o => o.Description.TrimEnd('.'))) + ".";

        // The men THAT tryout signed: read back off their accolade on its day (both kinds write the same words), so the
        // list survives a refresh and a reload and never mixes two tryouts.
        Signed.Clear();
        if (promo is not null && shownOn is { } day)
        {
            string mark = $"Signed at {promo.Name}'s open tryout";
            foreach (var f in w.Fighters.Values.Where(f => f.Accolades.Any(a => a.Title == mark && a.Date == day)).OrderBy(f => f.Id.Value))
                Signed.Add(new FighterCardModel(f, w));
        }
        SignedLine = shownOn is null ? ""
            : Signed.Count == 0 ? "Nobody the house could use turned up. The free-agent market is where the fighters are."
            : $"{Signed.Count} local {(Signed.Count == 1 ? "man" : "men")} signed on the tryout terms. Green, all of them: your scouts' first read is on each card.";
        OnPropertyChanged(string.Empty);
        CommandManager.InvalidateRequerySuggested();   // the Call button follows CanHold at once
    }

    private void Hold()
    {
        if (_w is null || !CanHold) return;   // (a RelayCommand run directly skips CanExecute)
        JustHeldFree = IsFree;
        PlayerFounding.HoldTryout(_w, _w.CurrentDate, out var refused);
        if (refused is not null) { Refusal = refused; OnPropertyChanged(nameof(Refusal)); OnPropertyChanged(nameof(HasRefusal)); return; }
        Welcome = "";
        OnChanged?.Invoke();   // the shell refreshes every screen, this one included
    }
}
