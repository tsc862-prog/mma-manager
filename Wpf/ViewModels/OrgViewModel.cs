using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One of the house's belts: the strap, who holds it, and its lineage.</summary>
public sealed class BeltRow
{
    public BeltRow(Title t, Division d, World w)
    {
        Division = FighterRow.DivisionLabel(d.WeightClass, d.Sex);
        BeltName = t.Name;
        Prestige = t.Prestige.Value;

        if (t.CurrentChampion is { } cid && w.Get(cid) is { } champ)
        {
            HasChampion = true;
            ChampName = champ.FullName;
            ChampRecord = champ.Record.ToString();
            ChampPortrait = Portraits.For(champ, w);
            var reign = t.Lineage.LastOrDefault(r => r.LostOn is null);
            ReignLine = reign is null ? "" : $"since {reign.WonOn:MMM yyyy}  ·  {reign.Defenses} defense{(reign.Defenses == 1 ? "" : "s")}";
        }

        var past = t.Lineage.Where(r => r.LostOn is not null).Reverse().Take(2)
            .Select(r => $"{w.Get(r.Champion)?.FullName ?? "?"} ({r.WonOn:yyyy}–{r.LostOn:yyyy}, {r.Defenses} def)")
            .ToList();
        LineageLine = past.Count > 0 ? "Previously: " + string.Join("  ·  ", past) : "";
    }

    public string Division { get; }
    public string BeltName { get; }
    public int Prestige { get; }
    public bool HasChampion { get; }
    public string ChampName { get; } = "";
    public string ChampRecord { get; } = "";
    public ImageSource? ChampPortrait { get; }
    public string ReignLine { get; } = "";
    public string LineageLine { get; }
}

/// <summary>A past show on the HQ poster wall.</summary>
public sealed class PosterThumb
{
    public PosterThumb(ImageSource poster, string caption) { Poster = poster; Caption = caption; }
    public ImageSource Poster { get; }
    public string Caption { get; }
}

/// <summary>A staff member (hired or on the market).</summary>
public sealed class StaffRow
{
    public StaffRow(StaffMember s)
    {
        Model = s;
        Role = FighterRow.Pretty(s.Role.ToString());
        Name = s.Name;
        Quality = s.Quality;
        Salary = s.MonthlySalary.ToString();
        SalarySort = s.MonthlySalary.Amount;
        Background = s.Background ?? "";
    }
    public StaffMember Model { get; }
    public string Role { get; }
    public string Name { get; }
    public int Quality { get; }
    public string Salary { get; }
    public decimal SalarySort { get; }

    /// <summary>Where they came from — a hire you can picture rather than a number with a salary.</summary>
    public string Background { get; }
}

/// <summary>One owner objective.</summary>
public sealed class ObjectiveRow
{
    public ObjectiveRow(Objective o)
    {
        Description = o.Description;
        Deadline = o.Deadline.ToString("d MMM yyyy");
        Status = o.Status.ToString();
    }
    public string Description { get; }
    public string Deadline { get; }
    public string Status { get; }
}

/// <summary>The org screen: hire/fire back-office staff and track the owner's objectives.</summary>
public sealed class OrgViewModel : ObservableObject
{
    private World? _world;
    public Action? OnChanged { get; set; }

    public ObservableCollection<StaffRow> MyStaff { get; } = new();
    public ObservableCollection<StaffRow> Market { get; } = new();
    public ObservableCollection<ObjectiveRow> Objectives { get; } = new();

    private StaffRow? _selectedMarket;
    public StaffRow? SelectedMarket { get => _selectedMarket; set => Set(ref _selectedMarket, value); }

    private StaffRow? _selectedStaff;
    public StaffRow? SelectedStaff { get => _selectedStaff; set => Set(ref _selectedStaff, value); }

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private string _identity = "";
    public string Identity { get => _identity; private set => Set(ref _identity, value); }

    private int _prestige, _reputation;
    public int Prestige { get => _prestige; private set => Set(ref _prestige, value); }
    public int Reputation { get => _reputation; private set => Set(ref _reputation, value); }

    private int _tierProgress;
    public int TierProgress { get => _tierProgress; private set => Set(ref _tierProgress, value); }

    private string _tierLine = "";
    public string TierLine { get => _tierLine; private set => Set(ref _tierLine, value); }

    // --- The house: who we are (story, history, belts, the poster wall) ------
    private string _story = "";
    /// <summary>The house identity: authored lore (when designed) + the live career
    /// line, straight from <see cref="Core.Sim.Flavor.Biographer.Promotion"/>.</summary>
    public string Story { get => _story; private set => Set(ref _story, value); }

    private string _historyLine = "";
    /// <summary>The run so far ("14 events since Jan 2026 · biggest house 5,204 · 3 milestones").</summary>
    public string HistoryLine { get => _historyLine; private set => Set(ref _historyLine, value); }

    public ObservableCollection<BeltRow> Belts { get; } = new();
    public ObservableCollection<PosterThumb> PosterWall { get; } = new();

    private bool _hasPosters;
    public bool HasPosters { get => _hasPosters; private set => Set(ref _hasPosters, value); }

    public ICommand HireCommand { get; }
    public ICommand FireCommand { get; }
    public ICommand AcceptPromotionCommand { get; }
    public ICommand CreateTitleCommand { get; }

    public OrgViewModel()
    {
        HireCommand = new RelayCommand(Hire, () => _world is not null && SelectedMarket is not null);
        FireCommand = new RelayCommand(Fire, () => _world is not null && SelectedStaff is not null);
        AcceptPromotionCommand = new RelayCommand(AcceptPromotion, () => CanPromote);
        CreateTitleCommand = new RelayCommand(CreateTitle, () => _world is not null && SelectedBeltless is not null);
    }

    // --- Commissioning belts: weight classes we staff but haven't sanctioned --
    public ObservableCollection<DivisionPick> BeltlessDivisions { get; } = new();

    private DivisionPick? _selectedBeltless;
    public DivisionPick? SelectedBeltless { get => _selectedBeltless; set => Set(ref _selectedBeltless, value); }

    private bool _hasBeltless;
    public bool HasBeltless { get => _hasBeltless; private set => Set(ref _hasBeltless, value); }

    public string BeltCostLine => $"Commission belt ({new Money(Core.Sim.Booking.PromotionSetup.BeltCost)})";

    private void CreateTitle()
    {
        if (_world is null || Promo is not { } promo || SelectedBeltless is not { } pick) return;
        if (promo.Balance.Amount < Core.Sim.Booking.PromotionSetup.BeltCost)
        {
            Status = "The books can't cover a new belt right now.";
            return;
        }
        var title = Core.Sim.Booking.PromotionSetup.CommissionTitle(_world, promo, pick.WeightClass, pick.Sex, _world.CurrentDate);
        Status = $"The {title.Name} is sanctioned — vacant until somebody takes it. Book the title fight.";
        Refresh();
        OnChanged?.Invoke();
    }

    /// <summary>Has the promotion maxed its tier and earned the right to step up?</summary>
    public bool CanPromote => Promo is { } p && PromotionLadder.IsEligible(p);

    /// <summary>The pitch shown when a step up is on the table (empty otherwise).</summary>
    public string PromotionLine => Promo is { } p && PromotionLadder.IsEligible(p)
        ? $"We've maxed out the {OrgSizing.Label(p.TierClearance)} scene. Step up to {OrgSizing.Label(PromotionLadder.NextTier(p))}? "
          + "Bigger gates and better talent — but the running costs jump too, so only take the leap when the bank can carry it."
        : "";

    private void AcceptPromotion()
    {
        if (_world is null || Promo is not { } promo || !PromotionLadder.IsEligible(promo)) return;
        var to = PromotionLadder.NextTier(promo);
        PromotionLadder.Promote(_world, promo, _world.CurrentDate);
        Status = $"We're going {OrgSizing.Label(to)}. Make the shows count.";
        Refresh();
        OnChanged?.Invoke();
    }

    // --- House rules (the owner's ruleset, written straight to the model) ----
    public int[] RoundMinuteOptions { get; } = { 3, 4, 5 };
    public int[] StandardRoundOptions { get; } = { 2, 3, 5 };
    public int[] TitleRoundOptions { get; } = { 3, 5 };

    private OrgRules? Rules => _world?.Get(_world.PlayerPromotion)?.Rules;

    public int RoundMinutes
    {
        get => Rules?.RoundMinutes ?? 5;
        set { if (Rules is { } r && r.RoundMinutes != value) { r.RoundMinutes = value; OnPropertyChanged(); } }
    }

    public int StandardRounds
    {
        get => Rules?.StandardRounds ?? 3;
        set { if (Rules is { } r && r.StandardRounds != value) { r.StandardRounds = value; OnPropertyChanged(); OnChanged?.Invoke(); } }
    }

    public int TitleRounds
    {
        get => Rules?.TitleRounds ?? 5;
        set { if (Rules is { } r && r.TitleRounds != value) { r.TitleRounds = value; OnPropertyChanged(); OnChanged?.Invoke(); } }
    }

    /// <summary>Fight of the Night bonus in dollars (0 = none), edited as plain text.</summary>
    public string FightOfNightBonus
    {
        get => Rules is { } r ? r.FightOfNightBonus.Amount.ToString("0") : "0";
        set
        {
            if (Rules is not { } r) return;
            if (!decimal.TryParse(value, out var amount) || amount < 0) return;
            r.FightOfNightBonus = new Money(Math.Round(amount / 500m) * 500m);   // paid in round numbers
            OnPropertyChanged();
        }
    }

    private Promotion? Promo => _world?.Get(_world.PlayerPromotion);

    /// <summary>Monthly brand-marketing budget (dollars/month), edited as plain text — pour it in to climb.</summary>
    public string MarketingBudget
    {
        get => Promo is { } p ? p.MarketingBudget.Amount.ToString("0") : "0";
        set
        {
            if (Promo is not { } p) return;
            if (!decimal.TryParse(value, out var amount) || amount < 0) return;
            p.MarketingBudget = new Money(Math.Round(amount / 10_000m) * 10_000m);   // in round chunks
            OnPropertyChanged();
            OnPropertyChanged(nameof(MarketingEffect));
            OnChanged?.Invoke();   // overhead/next-tier line moves
        }
    }

    /// <summary>Plain-language read on what the current marketing spend is buying.</summary>
    public string MarketingEffect
    {
        get
        {
            if (Promo is not { } p) return "";
            if (p.MarketingBudget.Amount <= 0) return "No campaign running. Pour money in to fill seats and climb faster.";
            double intensity = OrgMarketing.Intensity(p);
            return $"Campaign at {intensity:P0} intensity — about +{OrgMarketing.AttendanceBonus(p):P0} attendance and +{OrgMarketing.StandingQualityBonus(p):F0} to every show's standing. "
                 + $"Near-saturation for your size is ~{OrgMarketing.SuggestedMax(p)}/mo.";
        }
    }

    public void Attach(World world)
    {
        _world = world;
        Refresh();
    }

    private void Refresh()
    {
        MyStaff.Clear(); Market.Clear(); Objectives.Clear();
        if (_world?.Get(_world.PlayerPromotion) is not { } promo) return;

        var size = OrgSizing.Label(OrgSizing.SizeOf(promo));
        Identity = $"{promo.Name} — {size} promotion";
        var standing = OrgSizing.Progress(promo);
        TierProgress = standing.Percent;
        TierLine = standing.Next is { } next
            ? $"{standing.Percent}% to {OrgSizing.Label(next)}"
            : "Top of the sport";
        Prestige = promo.Prestige.Value;
        Reputation = promo.Reputation.Value;

        // The house: the press-kit story (authored lore leads) + the run so far.
        Story = Core.Sim.Flavor.Biographer.Promotion(_world, promo);
        var done = _world.Events.Values
            .Where(e => e.Promotion == promo.Id && e.Status == EventStatus.Completed)
            .OrderBy(e => e.Date).ToList();
        HistoryLine = done.Count == 0
            ? "No events yet — the story starts with your first card."
            : $"{done.Count} event{(done.Count == 1 ? "" : "s")} since {done[0].Date:MMM yyyy}"
              + $"  ·  biggest house {done.Max(e => e.Financials?.Attendance ?? 0):N0}"
              + (promo.Milestones.Count > 0 ? $"  ·  {promo.Milestones.Count} milestone{(promo.Milestones.Count == 1 ? "" : "s")}" : "");

        // The belts, heaviest first — every strap with its holder and lineage.
        Belts.Clear();
        foreach (var d in promo.Divisions.OrderBy(d => d.Sex).ThenByDescending(d => d.WeightClass))
            if (d.Title is { } tid && _world.Get(tid) is { } title)
                Belts.Add(new BeltRow(title, d, _world));

        // Weight classes we have fighters in but no sanctioned belt for — the
        // owner can commission one (creating the division alongside if needed).
        BeltlessDivisions.Clear();
        var titled = promo.Divisions.Where(d => d.Title is not null)
            .Select(d => new DivisionPick(d.WeightClass, d.Sex)).ToHashSet();
        foreach (var pick in _world.RosterOf(promo.Id)
                     .Where(f => f.Status != FighterStatus.Retired)
                     .Select(f => new DivisionPick(f.WeightClass, f.Sex))
                     .Distinct()
                     .Where(p => !titled.Contains(p))
                     .OrderBy(p => p.Sex).ThenBy(p => p.WeightClass))
            BeltlessDivisions.Add(pick);
        HasBeltless = BeltlessDivisions.Count > 0;
        if (SelectedBeltless is not { } sel || !BeltlessDivisions.Contains(sel))
            SelectedBeltless = BeltlessDivisions.FirstOrDefault();

        // The poster wall: the last six nights out.
        PosterWall.Clear();
        foreach (var e in done.AsEnumerable().Reverse().Take(6))
            PosterWall.Add(new PosterThumb(PosterFactory.For(_world, e), $"{e.Name}  ·  {e.Date:MMM yyyy}"));
        HasPosters = PosterWall.Count > 0;

        foreach (var s in promo.Staff.OrderBy(s => s.Role)) MyStaff.Add(new StaffRow(s));
        foreach (var s in _world.StaffMarket.OrderBy(s => s.Role).ThenByDescending(s => s.Quality))
            Market.Add(new StaffRow(s));
        foreach (var o in promo.Objectives.Where(o => o.Status == ObjectiveStatus.Active || o.Deadline >= _world.CurrentDate.AddMonths(-3)))
            Objectives.Add(new ObjectiveRow(o));

        // Rules read straight off the promotion — re-announce them for a new world.
        OnPropertyChanged(nameof(RoundMinutes));
        OnPropertyChanged(nameof(StandardRounds));
        OnPropertyChanged(nameof(TitleRounds));
        OnPropertyChanged(nameof(FightOfNightBonus));
        OnPropertyChanged(nameof(MarketingBudget));
        OnPropertyChanged(nameof(MarketingEffect));
        OnPropertyChanged(nameof(CanPromote));
        OnPropertyChanged(nameof(PromotionLine));
    }

    private void Hire()
    {
        if (_world?.Get(_world.PlayerPromotion) is not { } promo || SelectedMarket is null) return;
        StaffService.Hire(_world, promo, SelectedMarket.Model);
        Status = $"Hired {SelectedMarket.Name} ({SelectedMarket.Role}).";
        Refresh();
        OnChanged?.Invoke();
    }

    private void Fire()
    {
        if (_world?.Get(_world.PlayerPromotion) is not { } promo || SelectedStaff is null) return;
        StaffService.Fire(_world, promo, SelectedStaff.Model.Role);
        Status = $"Let go of {SelectedStaff.Name}.";
        Refresh();
        OnChanged?.Invoke();
    }
}
