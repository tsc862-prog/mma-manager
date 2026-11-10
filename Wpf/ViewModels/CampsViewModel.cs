using System.Collections.ObjectModel;
using System.Windows.Input;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Living;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One camp in the Camps tab: the room, its city, its coaching, its people.</summary>
public sealed class CampRow
{
    private readonly World _w;
    public Camp Model { get; }

    public CampRow(Camp c, World w, bool sponsored)
    {
        _w = w;
        Model = c;
        Sponsored = sponsored;
        Members = w.Fighters.Values
            .Count(f => f.Camp == c.Id && f.Status == FighterStatus.Active);
    }

    public string Name => Model.Name + (Sponsored ? "  ★" : "");
    public string City => _w.Get(Model.City)?.Name ?? "—";
    public string Specialty => FighterRow.Pretty(Model.Specialty.ToString());
    public int Coaching => Model.CoachQuality;
    public int Reputation => Model.Reputation;
    public int Members { get; }
    public string Capacity => Model.Capacity >= 999 ? "open" : $"{Members}/{Model.Capacity}";
    public int CapacitySort => Model.Capacity;
    public string HeadCoach => Model.HeadCoach is { } hc ? _w.Get(hc)?.FullName ?? "—" : "—";
    public string Fee => $"{Model.MonthlyFee}/mo";
    public decimal FeeSort => Model.MonthlyFee.Amount;
    public bool Sponsored { get; }
    public string? Lore => Model.Lore;
}

/// <summary>
/// The Camps tab: every room in the sport — where the talent trains, who runs it,
/// and the player's two levers: SPONSOR a camp (team deal) and buy a CAMP REPORT
/// (a scout's week on their mats, mailed back).
/// </summary>
public sealed class CampsViewModel : ObservableObject
{
    private World? _world;
    public Action? OnChanged { get; set; }

    public CampsViewModel()
    {
        SponsorCommand = new RelayCommand(ToggleSponsor, () => SelectedCamp is not null);
        BuyReportCommand = new RelayCommand(BuyReport, () => SelectedCamp is not null);
    }

    public ObservableCollection<CampRow> Camps { get; } = new();
    public ObservableCollection<string> Members { get; } = new();

    public ICommand SponsorCommand { get; }
    public ICommand BuyReportCommand { get; }

    private CampRow? _selectedCamp;
    public CampRow? SelectedCamp
    {
        get => _selectedCamp;
        set
        {
            if (!Set(ref _selectedCamp, value)) return;
            RefreshMembers();
            OnPropertyChanged(nameof(SponsorButtonText));
            OnPropertyChanged(nameof(ReportButtonText));
        }
    }

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public string SponsorButtonText
    {
        get
        {
            if (_world is null || SelectedCamp is null) return "Sponsor camp";
            var player = _world.Get(_world.PlayerPromotion);
            if (player?.SponsoredCamp == SelectedCamp.Model.Id) return "End the team deal";
            return $"Sponsor camp — {Core.Sim.Living.Camps.SponsorshipCost(SelectedCamp.Model)}/mo (half-price stipends, their fighters warm to us)";
        }
    }

    public string ReportButtonText => SelectedCamp is null
        ? "Buy camp report"
        : $"Buy camp report — {Core.Sim.Living.Camps.ReportCost(SelectedCamp.Model)} (a scout's week on their mats, mailed back)";

    public void Attach(World world)
    {
        _world = world;
        var sponsored = world.Get(world.PlayerPromotion)?.SponsoredCamp;
        var keep = SelectedCamp?.Model.Id;
        Camps.Clear();
        foreach (var c in world.Camps.Values
                     .OrderByDescending(c => c.Reputation).ThenByDescending(c => c.CoachQuality))
            Camps.Add(new CampRow(c, world, sponsored == c.Id));
        _selectedCamp = Camps.FirstOrDefault(r => r.Model.Id == keep) ?? Camps.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedCamp));
        RefreshMembers();
        OnPropertyChanged(nameof(SponsorButtonText));
        OnPropertyChanged(nameof(ReportButtonText));
    }

    private void RefreshMembers()
    {
        Members.Clear();
        if (_world is null || SelectedCamp is null) return;
        var members = _world.Fighters.Values
            .Where(f => f.Camp == SelectedCamp.Model.Id && f.Status == FighterStatus.Active)
            .OrderByDescending(FighterAbility.CurrentAbility);
        foreach (var f in members)
        {
            string org = f.Contract is { } c ? _world.Get(c.Promotion)?.Abbreviation ?? "FA" : "FA";
            string stipend = f.CampStipend ? " · stipend" : "";
            Members.Add($"{f.FullName}   ·   {f.Record}   ·   {FighterRow.Pretty(f.WeightClass.ToString())}   ·   {org}{stipend}");
        }
    }

    private void ToggleSponsor()
    {
        if (_world is null || SelectedCamp is null || _world.Get(_world.PlayerPromotion) is not { } player) return;
        if (player.SponsoredCamp == SelectedCamp.Model.Id)
        {
            player.SponsoredCamp = null;
            Status = $"Team deal with {SelectedCamp.Model.Name} ended.";
        }
        else
        {
            player.SponsoredCamp = SelectedCamp.Model.Id;
            Status = $"Team deal signed: {SelectedCamp.Model.Name} — {Core.Sim.Living.Camps.SponsorshipCost(SelectedCamp.Model)}/mo. "
                   + "Stipends there are half price, and their fighters will hear us out.";
        }
        OnChanged?.Invoke();
    }

    private void BuyReport()
    {
        if (_world is null || SelectedCamp is null || _world.Get(_world.PlayerPromotion) is not { } player) return;
        Status = Core.Sim.Living.Camps.BuyReport(_world, player, SelectedCamp.Model, _world.CurrentDate)
            ? $"Scout dispatched to {SelectedCamp.Model.Name} — the report is in your mail."
            : "Not enough in the bank for that report.";
        OnChanged?.Invoke();
    }
}
