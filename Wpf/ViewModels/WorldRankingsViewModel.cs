using System.Collections.ObjectModel;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One line of a cross-org ranking.</summary>
public sealed class GlobalRankRow
{
    public GlobalRankRow(int rank, Fighter f, World w)
    {
        FighterId = f.Id.Value;
        Rank = rank == 0 ? "" : $"#{rank}";
        RankSort = rank == 0 ? int.MaxValue : rank;
        Name = f.FullName;
        Org = f.Contract is { } c ? (w.Get(c.Promotion)?.Abbreviation ?? "—") : "Free agent";
        Division = FighterRow.DivisionLabel(f);
        Ovr = FighterRow.Scouted(w, f);
        Record = f.Record.ToString();
        RecordSort = FighterRow.RecordKey(f.Record);
        Fame = MmaManager.Core.Sim.Geography.Reputation.FighterBest(w, f);
        IsPlayer = f.Contract is { } pc && pc.Promotion == w.PlayerPromotion;
    }
    public int FighterId { get; }
    public string Rank { get; }
    public int RankSort { get; }
    public string Name { get; }
    public string Org { get; }
    public string Division { get; }
    public int Ovr { get; }
    public string Record { get; }
    public int RecordSort { get; }
    public int Fame { get; }
    public bool IsPlayer { get; }
}

/// <summary>The cross-org world rankings: pound-for-pound plus a per-division top list.</summary>
public sealed class WorldRankingsViewModel : ObservableObject
{
    private World? _world;

    public ObservableCollection<GlobalRankRow> PoundForPound { get; } = new();
    public ObservableCollection<GlobalRankRow> Division { get; } = new();
    public ObservableCollection<WeightClass> Divisions { get; } =
        new(Enum.GetValues<WeightClass>());

    private WeightClass _selectedDivision = WeightClass.Lightweight;
    public WeightClass SelectedDivision
    {
        get => _selectedDivision;
        set { if (Set(ref _selectedDivision, value)) RefreshDivision(); }
    }

    /// <summary>The sport has weight classes to rank (always, today; not in 1993).</summary>
    public bool HasDivisions { get; private set; } = true;
    public string NoDivisionsLine => "Every man in the sport fights at open weight, so the pound-for-pound list is the only ranking there is. "
        + "When the sport splits into weight classes, their rankings appear here.";

    public void Attach(World world)
    {
        _world = world;
        // Only the classes the sport has split out (every one, today).
        var classes = Enum.GetValues<WeightClass>().Where(w => FighterRow.ClassExists(w, Sex.Male)).ToList();
        if (!classes.SequenceEqual(Divisions))
        {
            Divisions.Clear();
            foreach (var c in classes) Divisions.Add(c);
            if (!classes.Contains(_selectedDivision) && classes.Count > 0) { _selectedDivision = classes[0]; OnPropertyChanged(nameof(SelectedDivision)); }
        }
        HasDivisions = classes.Count > 0;
        OnPropertyChanged(nameof(HasDivisions));
        PoundForPound.Clear();
        int rank = 1;
        foreach (var id in world.PoundForPound)
            if (world.Get(id) is { } f) PoundForPound.Add(new GlobalRankRow(rank++, f, world));
        RefreshDivision();
    }

    private void RefreshDivision()
    {
        Division.Clear();
        if (_world is null || !HasDivisions || !_world.GlobalRankings.TryGetValue(SelectedDivision, out var ids)) return;
        int rank = 1;
        foreach (var id in ids)
            if (_world.Get(id) is { } f) Division.Add(new GlobalRankRow(rank++, f, _world));
    }
}
