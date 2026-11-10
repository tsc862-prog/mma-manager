using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>A free agent the player can pursue.</summary>
public sealed class FreeAgentRow
{
    public FreeAgentRow(Fighter f, World w, ContractExpectations exp, Money effectiveAsk)
    {
        Id = f.Id;
        Name = f.FullName;
        Division = FighterRow.DivisionLabel(f.WeightClass, f.Sex);
        // Men's divisions light→heavy, then women's — so the Division column
        // sorts into the two blocks instead of interleaving them alphabetically.
        DivisionSort = (int)f.Sex * 100 + (int)f.WeightClass;
        Age = f.AgeOn(w.CurrentDate);
        Ovr = FighterRow.Scouted(w, f);
        Record = f.Record.ToString();
        RecordSort = FighterRow.RecordKey(f.Record);
        Fights = f.Record.TotalBouts;
        Hometown = HometownOf(f, w);
        Value = exp.MarketValue;
        // The ask AT YOUR TERMS (a small hall's non-exclusive deal runs ~20%
        // under the exclusive rate) — so this column, the negotiating table
        // and the purse on the signed deal all quote the same number.
        Ask = effectiveAsk.ToString();
        AskSort = effectiveAsk.Amount;
        Portrait = Portraits.For(f, w);
    }
    public FighterId Id { get; }
    public string Name { get; }
    public string Division { get; }
    public int DivisionSort { get; }
    public int Age { get; }
    public int Ovr { get; }
    public string Record { get; }
    public int RecordSort { get; }
    public int Fights { get; }
    public string Hometown { get; }
    public int Value { get; }
    public string Ask { get; }
    public decimal AskSort { get; }
    public ImageSource Portrait { get; }

    /// <summary>"City, CCC" for the fighter's hometown ("Rio de Janeiro, BRA"), or "—".</summary>
    public static string HometownOf(Fighter f, World w)
    {
        if (f.HomeCity is not { } hc || w.Get(hc) is not { } city) return "—";
        return w.Get(city.Country) is { } country ? $"{city.Name}, {country.Code}" : city.Name;
    }
}

/// <summary>One of the player's fighters and the state of their deal.</summary>
public sealed class MyContractRow
{
    public MyContractRow(Fighter f, World w)
    {
        Id = f.Id;
        Name = f.FullName;
        Division = FighterRow.DivisionLabel(f.WeightClass, f.Sex);
        Purse = f.Contract?.PursePerFight.ToString() ?? "—";
        PurseSort = f.Contract?.PursePerFight.Amount ?? 0m;
        FightsLeft = f.Contract?.FightsRemaining ?? 0;
        Status = FightsLeft == 0 ? "Expired" : FightsLeft <= 1 ? "Expiring" : "Signed";
        Terms = f.Contract is { } c
            ? (c.IsExclusive ? "exclusive" : "non-excl") + (c.HasChampionsClause ? " · clause" : "")
            : "—";
        Portrait = Portraits.For(f, w);
    }
    public FighterId Id { get; }
    public string Name { get; }
    public string Division { get; }
    public string Purse { get; }
    public decimal PurseSort { get; }
    public int FightsLeft { get; }
    public string Status { get; }
    /// <summary>The deal's shape at a glance ("exclusive · clause").</summary>
    public string Terms { get; }
    public bool Expiring => FightsLeft <= 1;
    public ImageSource Portrait { get; }
}

/// <summary>
/// The Contracts screen: pursue free agents, renew your own fighters, and release
/// them. "Negotiate contract" opens the negotiating room — multi-round talks with
/// the fighter's agent (<see cref="NegotiationSession"/>) built on the same math
/// the AI's <see cref="Negotiator"/> uses, so both sides play by the same rules.
/// </summary>
public sealed class ContractsViewModel : ObservableObject
{
    private readonly FighterValuation _valuation = new();
    private World? _world;

    /// <summary>Raised after a signing/release so the shell can refresh balance and rosters.</summary>
    public Action? OnChanged { get; set; }

    private FighterId? _targetId;
    private bool _targetIsOwn;
    private ContractExpectations? _expectations;

    // Camps whose agents walked out on you: no fresh talks until the date passes.
    // In-memory only — a reload clears the grudge, which is harmless.
    private readonly Dictionary<FighterId, DateOnly> _souredUntil = new();

    public ContractsViewModel()
    {
        NegotiateCommand = new RelayCommand(Negotiate, () => _world is not null && _targetId is not null);
        ReleaseCommand = new RelayCommand(Release, () => _world is not null && _targetIsOwn && _targetId is not null);
        SuggestSigningCommand = new RelayCommand(SuggestSigning, () => _world is not null);
    }

    public ICommand NegotiateCommand { get; }
    public ICommand ReleaseCommand { get; }
    public ICommand SuggestSigningCommand { get; }

    public ObservableCollection<FreeAgentRow> FreeAgents { get; } = new();
    public ObservableCollection<MyContractRow> MyContracts { get; } = new();

    // --- Free-agent market filters ------------------------------------------
    // Men's divisions, then the women's ones — each its own filter entry, so the
    // two sides of the sport browse separately.
    public ObservableCollection<string> DivisionFilters { get; } = new(
        new[] { "All divisions" }
            .Concat(Enum.GetValues<WeightClass>().Select(w => FighterRow.DivisionLabel(w, Sex.Male)))
            .Concat(WomensDivisions.Classes.Select(w => FighterRow.DivisionLabel(w, Sex.Female))));

    private string _faDivision = "All divisions";
    public string FaDivision { get => _faDivision; set { if (Set(ref _faDivision, value)) RefreshFreeAgents(); } }

    private string _faSearch = "";
    public string FaSearch { get => _faSearch; set { if (Set(ref _faSearch, value)) RefreshFreeAgents(); } }

    // Prospect-hunting bounds: cap the résumé (fights/wins), the age, or the
    // price — the shoestring questions ("who's green?", "who can I afford?").
    private static readonly (string Label, int? Cap)[] FightCapChoices =
        { ("Any fights", null), ("Debut (0)", 0), ("≤ 3 fights", 3), ("≤ 5 fights", 5), ("≤ 10 fights", 10), ("≤ 15 fights", 15) };
    private static readonly (string Label, int? Cap)[] WinCapChoices =
        { ("Any wins", null), ("0 wins", 0), ("≤ 2 wins", 2), ("≤ 5 wins", 5), ("≤ 10 wins", 10) };
    private static readonly (string Label, int? Cap)[] AgeCapChoices =
        { ("Any age", null), ("≤ 21", 21), ("≤ 23", 23), ("≤ 25", 25), ("≤ 28", 28), ("≤ 32", 32) };
    private static readonly (string Label, decimal? Cap)[] AskCapChoices =
        { ("Any ask", null), ("≤ $2.5k", 2_500m), ("≤ $5k", 5_000m), ("≤ $10k", 10_000m), ("≤ $25k", 25_000m), ("≤ $100k", 100_000m) };

    public ObservableCollection<string> FightCapFilters { get; } = new(FightCapChoices.Select(c => c.Label));
    public ObservableCollection<string> WinCapFilters { get; } = new(WinCapChoices.Select(c => c.Label));
    public ObservableCollection<string> AgeCapFilters { get; } = new(AgeCapChoices.Select(c => c.Label));
    public ObservableCollection<string> AskCapFilters { get; } = new(AskCapChoices.Select(c => c.Label));

    private string _faMaxFights = FightCapChoices[0].Label;
    public string FaMaxFights { get => _faMaxFights; set { if (Set(ref _faMaxFights, value)) RefreshFreeAgents(); } }

    private string _faMaxWins = WinCapChoices[0].Label;
    public string FaMaxWins { get => _faMaxWins; set { if (Set(ref _faMaxWins, value)) RefreshFreeAgents(); } }

    private string _faMaxAge = AgeCapChoices[0].Label;
    public string FaMaxAge { get => _faMaxAge; set { if (Set(ref _faMaxAge, value)) RefreshFreeAgents(); } }

    private string _faMaxAsk = AskCapChoices[0].Label;
    public string FaMaxAsk { get => _faMaxAsk; set { if (Set(ref _faMaxAsk, value)) RefreshFreeAgents(); } }

    private string _marketLine = "";
    /// <summary>How deep the market runs ("38 of 214 free agents shown").</summary>
    public string MarketLine { get => _marketLine; private set => Set(ref _marketLine, value); }

    private FreeAgentRow? _selectedFreeAgent;
    public FreeAgentRow? SelectedFreeAgent
    {
        get => _selectedFreeAgent;
        set { if (Set(ref _selectedFreeAgent, value) && value is not null) Target(value.Id, own: false); }
    }

    private MyContractRow? _selectedMyContract;
    public MyContractRow? SelectedMyContract
    {
        get => _selectedMyContract;
        set { if (Set(ref _selectedMyContract, value) && value is not null) Target(value.Id, own: true); }
    }

    // --- Scouted expectations (shown for the current target) ----------------
    private bool _hasTarget;
    public bool HasTarget { get => _hasTarget; private set => Set(ref _hasTarget, value); }

    private string _targetName = "Select a fighter to open talks.";
    public string TargetName { get => _targetName; private set => Set(ref _targetName, value); }

    private string _targetSub = "";
    public string TargetSub { get => _targetSub; private set => Set(ref _targetSub, value); }

    private ImageSource? _targetPortrait;
    /// <summary>Head shot of who's across the negotiating table.</summary>
    public ImageSource? TargetPortrait { get => _targetPortrait; private set => Set(ref _targetPortrait, value); }

    private string _asking = "";
    public string Asking { get => _asking; private set => Set(ref _asking, value); }

    private int _priMoney, _priTitle, _priLoyalty, _priHome;
    public int PriMoney { get => _priMoney; private set => Set(ref _priMoney, value); }
    public int PriTitle { get => _priTitle; private set => Set(ref _priTitle, value); }
    public int PriLoyalty { get => _priLoyalty; private set => Set(ref _priLoyalty, value); }
    public int PriHome { get => _priHome; private set => Set(ref _priHome, value); }

    public bool IsOwnFighter => _targetIsOwn;

    // --- Response -----------------------------------------------------------
    private string _response = "";
    public string Response { get => _response; private set => Set(ref _response, value); }

    private string _responseTone = "Good";
    /// <summary>How the response should read: "Good" (green), "Warn" (gold), "Bad" (red).</summary>
    public string ResponseTone { get => _responseTone; private set => Set(ref _responseTone, value); }

    // --- Wiring -------------------------------------------------------------
    public void Attach(World world)
    {
        _world = world;
        ClearTarget();
        RefreshLists();
    }

    /// <summary>
    /// Open talks with a specific fighter from anywhere in the app (the
    /// fighter profile's "Contract talks" button lands here): a free agent or
    /// one of our own — anyone else is under someone else's paper.
    /// </summary>
    public bool OpenTalksWith(FighterId id)
    {
        if (_world?.Get(id) is not { Status: not FighterStatus.Retired } f) return false;
        bool own = f.Contract?.Promotion == _world.PlayerPromotion;
        if (!own && f.Contract is not null) return false;   // under contract elsewhere
        Target(id, own);
        return true;
    }

    private string _payrollLine = "";
    /// <summary>What the roster is owed if every remaining contracted bout happens.</summary>
    public string PayrollLine { get => _payrollLine; private set => Set(ref _payrollLine, value); }

    private void RefreshLists()
    {
        FreeAgents.Clear();
        MyContracts.Clear();
        if (_world is null) return;

        var deals = _world.RosterOf(_world.PlayerPromotion)
            .Where(f => f.Status != FighterStatus.Retired && f.Contract is not null)
            .Select(f => f.Contract!)
            .ToList();
        var committed = new Money(deals.Sum(c => c.PursePerFight.Amount * c.FightsRemaining));
        PayrollLine = deals.Count == 0
            ? "No fighters under contract."
            : $"{deals.Count} deals  ·  committed purses {committed} across remaining bouts";

        RefreshFreeAgents();

        foreach (var f in _world.RosterOf(_world.PlayerPromotion)
                     .Where(f => f.Status != FighterStatus.Retired)
                     .OrderBy(f => f.Contract?.FightsRemaining ?? 99)
                     .ThenByDescending(FighterAbility.CurrentAbility))
            MyContracts.Add(new MyContractRow(f, _world));
    }

    private void RefreshFreeAgents()
    {
        FreeAgents.Clear();
        if (_world is null) return;

        var market = _world.FreeAgents().Where(f => f.Status == FighterStatus.Active).ToList();
        IEnumerable<Fighter> shown = market;
        if (FaDivision != "All divisions")
            shown = shown.Where(f => FighterRow.DivisionLabel(f.WeightClass, f.Sex) == FaDivision);
        if (FightCapChoices.First(c => c.Label == FaMaxFights).Cap is { } fightCap)
            shown = shown.Where(f => f.Record.TotalBouts <= fightCap);
        if (WinCapChoices.First(c => c.Label == FaMaxWins).Cap is { } winCap)
            shown = shown.Where(f => f.Record.Wins <= winCap);
        if (AgeCapChoices.First(c => c.Label == FaMaxAge).Cap is { } ageCap)
            shown = shown.Where(f => f.AgeOn(_world.CurrentDate) <= ageCap);
        if (FaSearch.Trim() is { Length: > 0 } needle)
            shown = shown.Where(f => f.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                                     || FreeAgentRow.HometownOf(f, _world).Contains(needle, StringComparison.OrdinalIgnoreCase));

        // Price every survivor at YOUR terms (the ask a non-exclusive hall
        // actually pays), so the ask filter and the ask column agree.
        var mine = _world.Get(_world.PlayerPromotion);
        IEnumerable<(Fighter F, ContractExpectations Exp, Money Ask)> priced = shown
            .Select(f =>
            {
                var exp = _valuation.Evaluate(_world, f);
                return (f, exp, mine is null ? exp.AskingPurse : Negotiator.EffectiveAskingPurse(mine, f, exp));
            });
        if (AskCapChoices.First(c => c.Label == FaMaxAsk).Cap is { } askCap)
            priced = priced.Where(x => x.Ask.Amount <= askCap);

        // The men's market first, then the women's — best available on top of each.
        int count = 0;
        foreach (var x in priced.OrderBy(x => x.F.Sex).ThenByDescending(x => FighterAbility.CurrentAbility(x.F)).Take(200))
        {
            FreeAgents.Add(new FreeAgentRow(x.F, _world, x.Exp, x.Ask));
            count++;
        }
        MarketLine = count == market.Count
            ? $"{market.Count} on the market"
            : $"{count} shown of {market.Count} on the market";
    }

    private void Target(FighterId id, bool own)
    {
        if (_world?.Get(id) is not { } f) return;
        _targetId = id;
        _targetIsOwn = own;
        Response = "";

        var exp = _valuation.Evaluate(_world, f);
        _expectations = exp;
        HasTarget = true;
        TargetName = f.FullName;
        TargetPortrait = Portraits.For(f, _world);
        TargetSub = $"Scouted {FighterRow.Scouted(_world, f)} · {FighterRow.DivisionLabel(f.WeightClass, f.Sex)} · {f.Record}"
                    + $" · {FreeAgentRow.HometownOf(f, _world)}"
                    + (own ? $" · {f.Contract?.FightsRemaining ?? 0} fight(s) left" : " · free agent");
        // Quote the ask at YOUR terms — a small hall deals non-exclusive and the
        // fighter takes ~20% less, so the raw exclusive rate would read as a lie
        // next to the market list and the deal that gets signed.
        var mine = _world.Get(_world.PlayerPromotion);
        var effAsk = mine is null ? exp.AskingPurse : Negotiator.EffectiveAskingPurse(mine, f, exp);
        Asking = $"Asking ~{effAsk} / fight, {exp.DesiredFights} fights"
                 + (effAsk.Amount < exp.AskingPurse.Amount ? $" (non-exclusive — exclusive rate {exp.AskingPurse})" : "")
                 + (exp.WantsSigningBonus ? ", wants a signing bonus" : "");
        PriMoney = exp.Priorities.Money;
        PriTitle = exp.Priorities.TitleOpportunity;
        PriLoyalty = exp.Priorities.Loyalty;
        PriHome = exp.Priorities.HomeRegion;
        OnPropertyChanged(nameof(IsOwnFighter));
    }

    /// <summary>
    /// Headless-harness path: sit down with the selected target and sign at their
    /// terms without opening the window — the same session the room drives.
    /// </summary>
    public bool SignAtTheirTerms()
    {
        if (_world is null || _targetId is null || _world.Get(_targetId.Value) is not { } f
            || _expectations is null || _world.Get(_world.PlayerPromotion) is not { } promo) return false;

        var session = new NegotiationSession(_world, f, promo, _expectations);
        var offer = session.TheirTermsAsOffer(Negotiator.WantsExclusivity(promo), clause: false);
        if (session.Table(offer).Verdict != TalkVerdict.Accept) return false;
        Sign(offer);
        return true;
    }

    /// <summary>Open the negotiating room over the shell and act on how it ends.</summary>
    private void Negotiate()
    {
        if (_world is null || _targetId is null || _world.Get(_targetId.Value) is not { } f
            || _expectations is null || _world.Get(_world.PlayerPromotion) is not { } promo) return;

        if (_souredUntil.TryGetValue(f.Id, out var until) && _world.CurrentDate < until)
        {
            ResponseTone = "Bad";
            Response = $"Their camp hasn't cooled off — no talks until {until:MMM d}.";
            return;
        }

        var session = new NegotiationSession(_world, f, promo, _expectations);
        var vm = new NegotiationViewModel(_world, f, promo, _expectations, session, _targetIsOwn);
        var win = new NegotiationWindow { DataContext = vm };
        if (System.Windows.Application.Current?.MainWindow is { IsLoaded: true } owner)
            win.Owner = owner;
        win.ShowDialog();

        if (vm.SignedOffer is { } deal)
        {
            Sign(deal);
        }
        else if (session.Outcome == TalkOutcome.TheyWalked)
        {
            var cooloff = _world.CurrentDate.AddDays(21);
            _souredUntil[f.Id] = cooloff;
            ResponseTone = "Bad";
            Response = $"{f.FullName}'s camp walked out — talks are dead until {cooloff:MMM d}.";
        }
        else
        {
            ResponseTone = "Warn";
            Response = "You left the table — the door stays open.";
        }
    }

    private void Sign(ContractOffer offer)
    {
        if (_world is null || _targetId is null || _world.Get(_targetId.Value) is not { } f) return;
        bool renew = _targetIsOwn;
        ContractLifecycle.Sign(_world, f, offer, _world.CurrentDate);
        var name = f.FullName;
        ClearTarget();
        RefreshLists();
        ResponseTone = "Good";
        Response = renew ? $"Renewed {name}." : $"Signed {name}!";
        OnChanged?.Invoke();
    }

    // The scout picks the smartest next signing (thin divisions, affordability,
    // ability + upside) and opens talks with them.
    private void SuggestSigning()
    {
        if (_world is null || _world.Get(_world.PlayerPromotion) is not { } promo) return;
        if (SigningAdvisor.Suggest(_world, promo) is not { } pick)
        {
            ResponseTone = "Bad";
            Response = "The scout has nobody to recommend — the market is bare.";
            return;
        }
        SelectedFreeAgent = FreeAgents.FirstOrDefault(r => r.Id == pick.Fighter.Id);
        ResponseTone = "Warn";
        Response = pick.Reason;
    }

    private void Release()
    {
        if (_world is null || _targetId is null || _world.Get(_targetId.Value) is not { } f) return;
        var owed = ContractLifecycle.Release(_world, f);
        var name = f.FullName;
        ClearTarget();
        RefreshLists();
        ResponseTone = "Warn";
        Response = owed.Amount > 0 ? $"Released {name} — paid out {owed} in guarantees." : $"Released {name}.";
        OnChanged?.Invoke();
    }

    private void ClearTarget()
    {
        _targetId = null;
        _targetIsOwn = false;
        _expectations = null;
        HasTarget = false;
        TargetName = "Select a fighter to open talks.";
        TargetPortrait = null;
        TargetSub = "";
        Asking = "";
        PriMoney = PriTitle = PriLoyalty = PriHome = 0;
        OnPropertyChanged(nameof(IsOwnFighter));
    }
}
