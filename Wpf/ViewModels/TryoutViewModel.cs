using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Living;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// OPEN TRYOUT: the first step of a house founded from ringside. Says what the house is (its name, its rules, when the
/// owner's targets start), offers the one free open tryout (<see cref="PlayerFounding.TryoutOptions"/>), holds it on
/// the player's word (<see cref="PlayerFounding.HoldTryout"/>), shows the men it signed as his scouts read them, and
/// then points at booking the first night. Holds ids only; everything else is read from the sim.
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
    public bool CanHold { get; private set; }
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

    /// <summary>Fired after the tryout is held (the shell refreshes every screen).</summary>
    public Action? OnChanged { get; set; }

    public void Attach(World w)
    {
        _w = w;
        var promo = w.Get(w.PlayerPromotion);
        HouseName = promo?.Name ?? "";
        Logo = promo is null ? null : LogoFactory.ForPromotion(promo);
        var offer = PlayerFounding.TryoutOptions(w, w.CurrentDate);
        var heldOn = promo is null ? null : PlayerFounding.TryoutHeldOn(promo);
        CanHold = offer.Available;
        Refusal = offer.Available || heldOn is not null ? "" : offer.Refusal ?? "";
        Kicker = heldOn is { } on ? $"THE OPEN TRYOUT  ·  HELD {on:d MMMM yyyy}".ToUpperInvariant() : "YOUR FIRST FIGHTERS";
        Headline = heldOn is null ? "Call your free open tryout" : "The tryout is done: now book a night";
        Detail = heldOn is null
            ? offer.Detail + " You can call it now or come back to it from the Open Tryout app; it is yours once."
            : "These are the men who signed on the day. The rest of your roster comes off the free-agent market, at their asking terms.";

        Rules.Clear();
        if (promo is not null) foreach (var r in HouseRules.For(promo)) Rules.Add(r);
        var targets = promo?.Objectives.Where(o => o.Status == ObjectiveStatus.Active).ToList() ?? new();
        TargetsLine = promo is null ? ""
            : targets.Count == 0
                ? $"Nobody will judge {promo.Name} on what is left of {w.CurrentDate.Year}: the owner's targets start on 1 January {w.CurrentDate.Year + 1}."
                : $"The owner's targets, to {targets.Max(o => o.Deadline):d MMMM yyyy}: " + string.Join("; ", targets.Select(o => o.Description.TrimEnd('.'))) + ".";

        // The men the tryout signed: read back off their accolade, so the list survives a refresh and a reload.
        Signed.Clear();
        if (promo is not null && heldOn is not null)
        {
            string mark = $"Signed at {promo.Name}'s open tryout";
            foreach (var f in w.Fighters.Values.Where(f => f.Accolades.Any(a => a.Title == mark)).OrderBy(f => f.Id.Value))
                Signed.Add(new FighterCardModel(f, w));
        }
        SignedLine = Signed.Count == 0 ? (heldOn is null ? "" : "Nobody from the tryout is still signed.")
            : $"{Signed.Count} local {(Signed.Count == 1 ? "man" : "men")} signed on the tryout terms. Green, all of them: your scouts' first read is on each card.";
        OnPropertyChanged(string.Empty);
    }

    private void Hold()
    {
        if (_w is null) return;
        PlayerFounding.HoldTryout(_w, _w.CurrentDate, out var refused);
        if (refused is not null) { Refusal = refused; OnPropertyChanged(nameof(Refusal)); return; }
        Welcome = "";
        OnChanged?.Invoke();   // the shell refreshes every screen, this one included
    }
}
