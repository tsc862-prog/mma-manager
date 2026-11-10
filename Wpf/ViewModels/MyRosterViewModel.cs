using System.Collections.ObjectModel;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// One of YOUR fighters on the Roster screen: the front-office view — contract
/// terms, loyalty, camp and availability precomputed, since this screen is about
/// running the gym, not scouting the world.
/// </summary>
public sealed class MyRosterRow
{
    private readonly Fighter _f;
    private readonly World _w;

    public MyRosterRow(Fighter f, World w, Event? bookedOn)
    {
        _f = f;
        _w = w;

        var c = f.Contract;
        ContractLine = c is null ? "—"
            : $"{c.FightsRemaining} bout{(c.FightsRemaining == 1 ? "" : "s")} · {c.PursePerFight}/fight{(c.IsExclusive ? "" : " · non-excl")}";
        ExpiringSoon = c is { FightsRemaining: <= 1 };

        if (f.Status == FighterStatus.Injured)
        {
            Availability = f.ReadyToFightOn is { } back ? $"🤕 injured — back {back:d MMM}" : "🤕 injured";
            IsSidelined = true;
        }
        else if (bookedOn is not null)
            Availability = $"📅 {bookedOn.Name} · {bookedOn.Date:d MMM}";
        else if (f.ReadyToFightOn is { } ready && ready > w.CurrentDate)
            Availability = $"recovering — ready {ready:d MMM}";
        else
        {
            Availability = "✔ ready";
            IsReady = true;
        }
    }

    public int FighterId => _f.Id.Value;
    public ImageSource Portrait => Portraits.For(_f, _w);
    public string Name => _f.FullName;
    public string Badges
    {
        get
        {
            string b = "";
            if (_w.Titles.Values.Any(t => t.CurrentChampion == _f.Id)) b += "👑";
            if (Core.Sim.Org.Franchise.IsFace(_w, _f)) b += "📣";
            return b;
        }
    }
    public string Division => FighterRow.DivisionLabel(_f.WeightClass, _f.Sex);
    public int Age => _f.AgeOn(_w.CurrentDate);
    public string Record => _f.Record.ToString();
    public int RecordSort => FighterRow.RecordKey(_f.Record);
    public int Ovr => FighterRow.Scouted(_w, _f);
    public string Form => _f.Momentum >= 20 ? $"▲ {_f.Momentum}" : _f.Momentum <= -20 ? $"▼ {_f.Momentum}" : _f.Momentum.ToString();
    public bool Hot => _f.Momentum >= 20;
    public bool Cold => _f.Momentum <= -20;
    public int Morale => _f.Morale.Value;
    public int Loyalty => _f.Loyalty;
    public string CampName => _f.Camp is { } c && _w.Get(c) is { } camp ? camp.Name : "local scene";

    public string ContractLine { get; }
    /// <summary>Last bout on the deal — the giants may be circling.</summary>
    public bool ExpiringSoon { get; }
    public string Availability { get; }
    public bool IsReady { get; }
    public bool IsSidelined { get; }

    // Sort key: men's divisions light→heavy, then women's, best first inside each.
    public int SortKey => ((int)_f.Sex * 100 + (int)_f.WeightClass) * 1000 - Ovr;
}

/// <summary>
/// The Roster screen: every fighter under contract to YOUR promotion, with the
/// front-office columns (deal, loyalty, morale, camp, availability) that the
/// world-research Fighters database doesn't owe you.
/// </summary>
public sealed class MyRosterViewModel : ObservableObject
{
    public ObservableCollection<MyRosterRow> Fighters { get; } = new();

    private string _summary = "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }

    public void Attach(World world)
    {
        Fighters.Clear();

        // Who's already booked on an upcoming card (availability at a glance).
        var booked = new Dictionary<FighterId, Event>();
        foreach (var ev in world.Events.Values.Where(e => e.Status == EventStatus.Scheduled))
            foreach (var fight in ev.Card)
            {
                booked[fight.RedCorner] = ev;
                booked[fight.BlueCorner] = ev;
            }

        var mine = world.Fighters.Values
            .Where(f => f.Status != FighterStatus.Retired && f.Contract?.Promotion == world.PlayerPromotion)
            .Select(f => new MyRosterRow(f, world, booked.GetValueOrDefault(f.Id)))
            .OrderBy(r => r.SortKey)
            .ToList();
        foreach (var r in mine) Fighters.Add(r);

        IsEmpty = mine.Count == 0;
        int divisions = world.Get(world.PlayerPromotion)?.Divisions.Count ?? 0;
        Summary = mine.Count == 0
            ? "No fighters under contract."
            : $"{mine.Count} fighter{(mine.Count == 1 ? "" : "s")} under contract  ·  {divisions} divisions  ·  " +
              $"{mine.Count(r => r.IsReady)} ready  ·  {mine.Count(r => !r.IsReady && !r.IsSidelined)} booked or recovering  ·  " +
              $"{mine.Count(r => r.IsSidelined)} injured";
    }
}
