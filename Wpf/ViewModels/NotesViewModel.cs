using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>A fighter pinned to the owner's shortlist, with a live line on where they stand.</summary>
public sealed class ShortlistRow
{
    public ShortlistRow(Fighter f, World w)
    {
        FighterId = f.Id.Value;
        Portrait = Portraits.For(f, w);
        Name = f.FullName;
        Detail = $"{FighterRow.DivisionLabel(f.WeightClass, f.Sex)}  ·  {f.AgeOn(w.CurrentDate)}y  ·  {f.Record}  ·  Sct {FighterRow.Scouted(w, f)}";
        Status = f.Contract is { } c
            ? (c.Promotion == w.PlayerPromotion ? "on OUR roster" : $"signed to {w.Get(c.Promotion)?.Abbreviation ?? "?"}")
            : f.Status == FighterStatus.Retired ? "retired" : "FREE AGENT";
        IsFreeAgent = f.Contract is null && f.Status != FighterStatus.Retired;
    }

    public int FighterId { get; }
    public ImageSource Portrait { get; }
    public string Name { get; }
    public string Detail { get; }
    public string Status { get; }
    public bool IsFreeAgent { get; }
}

/// <summary>
/// The Notes app: the owner's free-text notepad (persisted with the career) and
/// the fighter shortlist — pinned from any screen, managed here.
/// </summary>
public sealed class NotesViewModel : ObservableObject
{
    private World? _world;

    public NotesViewModel()
    {
        RemoveCommand = new RelayCommand<ShortlistRow>(r =>
        {
            if (r is null || _world is null) return;
            _world.Shortlist.Remove(new FighterId(r.FighterId));
            Rebuild();
        });
    }

    public ICommand RemoveCommand { get; }

    /// <summary>The notepad — written straight onto the world, so it saves with the career.</summary>
    public string Text
    {
        get => _world?.OwnerNotes ?? "";
        set
        {
            if (_world is null || _world.OwnerNotes == value) return;
            _world.OwnerNotes = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ShortlistRow> Shortlist { get; } = new();

    private bool _hasShortlist;
    public bool HasShortlist { get => _hasShortlist; private set => Set(ref _hasShortlist, value); }

    public void Attach(World world)
    {
        _world = world;
        OnPropertyChanged(nameof(Text));
        Rebuild();
    }

    private void Rebuild()
    {
        Shortlist.Clear();
        if (_world is null) { HasShortlist = false; return; }
        foreach (var id in _world.Shortlist)
            if (_world.Get(id) is { } f)
                Shortlist.Add(new ShortlistRow(f, _world));
        HasShortlist = Shortlist.Count > 0;
    }
}
