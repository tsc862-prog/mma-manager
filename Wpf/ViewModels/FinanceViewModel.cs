using System.Collections.ObjectModel;
using MmaManager.Core.Model;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One category's total on the profit-and-loss.</summary>
public sealed class CategoryRow
{
    public CategoryRow(string category, decimal amount)
    {
        Category = category;
        Amount = new Money(Math.Abs(amount)).ToString();
        AmountSort = Math.Abs(amount);
    }
    public string Category { get; }
    public string Amount { get; }
    public decimal AmountSort { get; }
}

/// <summary>A month's income / expense / net line, with bar widths for the inline chart.</summary>
public sealed class MonthRow
{
    public MonthRow(int year, int month, decimal income, decimal expense, decimal net, decimal scaleMax)
    {
        Month = new DateOnly(year, month, 1).ToString("MMM yyyy");
        MonthSort = new DateOnly(year, month, 1);
        Income = new Money(income).ToString();
        IncomeSort = income;
        Expense = new Money(expense).ToString();
        ExpenseSort = Math.Abs(expense);
        Net = new Money(net).ToString();
        Positive = net >= 0;

        const double barMax = 140;
        double scale = scaleMax <= 0 ? 0 : barMax / (double)scaleMax;
        IncomeBar = (double)income * scale;
        ExpenseBar = (double)Math.Abs(expense) * scale;
    }
    public string Month { get; }
    public DateOnly MonthSort { get; }
    public string Income { get; }
    public decimal IncomeSort { get; }
    public string Expense { get; }
    public decimal ExpenseSort { get; }
    public string Net { get; }
    public bool Positive { get; }
    public double IncomeBar { get; }
    public double ExpenseBar { get; }
}

/// <summary>An active sponsorship: who pays, what they pay per event, and what they demand.</summary>
public sealed class SponsorRow
{
    public SponsorRow(SponsorDeal d)
    {
        Partner = d.Partner;
        Fee = d.FeePerEvent.ToString();
        FeeSort = d.FeePerEvent.Amount;
        Left = $"{d.EventsRemaining} ev";
        Terms = d.Requirements.Count == 0 ? "no strings"
            : string.Join("; ", d.Requirements.Select(MmaManager.Core.Sim.Finance.DealCompliance.Describe));
        Strikes = d.Strikes == 0 ? "" : $"⚠ {d.Strikes}/{MmaManager.Core.Sim.Finance.Sponsorships.StrikeLimit}";
    }
    public string Partner { get; }
    public string Fee { get; }
    public decimal FeeSort { get; }
    public string Left { get; }
    public string Terms { get; }
    public string Strikes { get; }
}

/// <summary>An active media rights deal (or the in-house streaming platform's summary row).</summary>
public sealed class MediaRow
{
    public MediaRow(MediaDeal d, Promotion promo)
    {
        Kind = MmaManager.Core.Sim.Finance.MediaService.Label(d.Kind);
        Partner = $"{d.Partner} · {MmaManager.Core.Sim.Finance.MediaService.TagOf(d.Partner)}";
        Fee = d.Kind == MediaKind.PpvCarrier
            ? $"{MmaManager.Core.Sim.Finance.MediaService.PpvShare(promo):P0} of buys"
            : $"{d.RightsFeePerEvent}/ev";
        FeeSort = d.RightsFeePerEvent.Amount;
        Left = $"{d.MonthsRemaining} mo";
        Terms = string.Join("; ", d.Requirements.Select(MmaManager.Core.Sim.Finance.DealCompliance.Describe));
        Strikes = d.Strikes == 0 ? "" : $"⚠ {d.Strikes}/{MmaManager.Core.Sim.Finance.MediaService.StrikeLimit}";
    }
    public MediaRow(StreamingPlatform s)
    {
        Kind = "in-house streaming";
        Partner = s.Name;
        Fee = $"{s.Subscribers:N0} subs";
        FeeSort = s.Subscribers;
        Left = "ours";
        Terms = "airs every card; subscribers follow the product";
        Strikes = "";
    }
    public string Kind { get; }
    public string Partner { get; }
    public string Fee { get; }
    public decimal FeeSort { get; }
    public string Left { get; }
    public string Terms { get; }
    public string Strikes { get; }
}

/// <summary>A sponsor or network offer waiting for an answer.</summary>
public sealed class OfferRow
{
    public OfferRow(DealOffer o)
    {
        Offer = o;
        Kind = o.Media is { } mk
            ? $"{MmaManager.Core.Sim.Finance.MediaService.Label(mk)} · {MmaManager.Core.Sim.Finance.MediaService.TagOf(o.Partner)}"
            : "sponsorship";
        Partner = o.Partner;
        Fee = o.Media == MediaKind.PpvCarrier ? "share of buys" : $"{o.FeePerEvent}/ev";
        Term = o.Media is null ? $"{o.Term} events" : $"{o.Term} months";
        Terms = o.Requirements.Count == 0 ? "no strings"
            : string.Join("; ", o.Requirements.Select(MmaManager.Core.Sim.Finance.DealCompliance.Describe));
        Expires = o.ExpiresOn.ToString("d MMM");
    }
    public DealOffer Offer { get; }
    public string Kind { get; }
    public string Partner { get; }
    public string Fee { get; }
    public string Term { get; }
    public string Terms { get; }
    public string Expires { get; }
}

/// <summary>A recent ledger transaction.</summary>
public sealed class TxnRow
{
    public TxnRow(LedgerEntry e)
    {
        When = e.Date.ToString("d MMM");
        WhenSort = e.Date;
        Category = FighterRow.Pretty(e.Category.ToString());
        Amount = e.Amount.ToString();
        AmountSort = e.Amount.Amount;
        Description = e.Description;
        Positive = e.IsIncome;
    }
    public string When { get; }
    public DateOnly WhenSort { get; }
    public string Category { get; }
    public string Amount { get; }
    public decimal AmountSort { get; }
    public string Description { get; }
    public bool Positive { get; }
}

/// <summary>
/// The Finance screen: your cash position, this month's profit-and-loss by
/// category, a monthly history, your active sponsorship/broadcast deals, and the
/// most recent transactions — all read from the player promotion's ledger.
/// </summary>
public sealed class FinanceViewModel : ObservableObject
{
    private World? _world;

    private string _balance = "—";
    public string Balance { get => _balance; private set => Set(ref _balance, value); }

    private string _monthLabel = "";
    public string MonthLabel { get => _monthLabel; private set => Set(ref _monthLabel, value); }

    private string _monthIncome = "", _monthExpense = "", _monthNet = "";
    public string MonthIncome { get => _monthIncome; private set => Set(ref _monthIncome, value); }
    public string MonthExpense { get => _monthExpense; private set => Set(ref _monthExpense, value); }
    public string MonthNet { get => _monthNet; private set => Set(ref _monthNet, value); }

    public ObservableCollection<CategoryRow> Income { get; } = new();
    public ObservableCollection<CategoryRow> Expenses { get; } = new();
    public ObservableCollection<MonthRow> History { get; } = new();
    public ObservableCollection<SponsorRow> Sponsors { get; } = new();
    public ObservableCollection<OfferRow> Offers { get; } = new();
    public ObservableCollection<TxnRow> Recent { get; } = new();

    public System.Windows.Input.ICommand AcceptOfferCommand { get; }
    public System.Windows.Input.ICommand DeclineOfferCommand { get; }

    public FinanceViewModel()
    {
        // Sponsor offers only — media deals are the Media desk's business.
        AcceptOfferCommand = new RelayCommand<OfferRow>(row =>
        {
            if (_world?.Get(_world.PlayerPromotion) is not { } promo || row.Offer.Media is not null) return;
            MmaManager.Core.Sim.Finance.Sponsorships.Accept(_world, promo, row.Offer, _world.CurrentDate);
            Refresh();
        });
        DeclineOfferCommand = new RelayCommand<OfferRow>(row =>
        {
            if (_world?.Get(_world.PlayerPromotion) is not { } promo) return;
            promo.Offers.Remove(row.Offer);
            Refresh();
        });
    }

    public void Attach(World world)
    {
        _world = world;
        Refresh();
    }

    private void Refresh()
    {
        Income.Clear(); Expenses.Clear(); History.Clear();
        Sponsors.Clear(); Offers.Clear(); Recent.Clear();
        if (_world?.Get(_world.PlayerPromotion) is not { } promo) return;

        Balance = promo.Balance.ToString();

        var now = _world.CurrentDate;
        MonthLabel = new DateOnly(now.Year, now.Month, 1).ToString("MMMM yyyy");
        var monthEntries = promo.Ledger.Where(e => e.Date.Year == now.Year && e.Date.Month == now.Month).ToList();

        foreach (var g in monthEntries.Where(e => e.Amount.Amount > 0)
                     .GroupBy(e => e.Category).OrderByDescending(g => g.Sum(e => e.Amount.Amount)))
            Income.Add(new CategoryRow(FighterRow.Pretty(g.Key.ToString()), g.Sum(e => e.Amount.Amount)));

        foreach (var g in monthEntries.Where(e => e.Amount.Amount < 0)
                     .GroupBy(e => e.Category).OrderBy(g => g.Sum(e => e.Amount.Amount)))
            Expenses.Add(new CategoryRow(FighterRow.Pretty(g.Key.ToString()), g.Sum(e => e.Amount.Amount)));

        decimal inc = monthEntries.Where(e => e.Amount.Amount > 0).Sum(e => e.Amount.Amount);
        decimal exp = monthEntries.Where(e => e.Amount.Amount < 0).Sum(e => e.Amount.Amount);
        MonthIncome = new Money(inc).ToString();
        MonthExpense = new Money(exp).ToString();
        MonthNet = new Money(inc + exp).ToString();

        var months = promo.Ledger
            .GroupBy(e => (e.Date.Year, e.Date.Month))
            .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
            .Take(12)
            .Select(g => (g.Key.Year, g.Key.Month,
                Income: g.Where(e => e.Amount.Amount > 0).Sum(e => e.Amount.Amount),
                Expense: g.Where(e => e.Amount.Amount < 0).Sum(e => e.Amount.Amount)))
            .ToList();
        decimal scaleMax = months.Count == 0 ? 0 : months.Max(m => Math.Max(m.Income, Math.Abs(m.Expense)));
        foreach (var m in months)
            History.Add(new MonthRow(m.Year, m.Month, m.Income, m.Expense, m.Income + m.Expense, scaleMax));

        foreach (var d in promo.SponsorDeals.OrderByDescending(d => d.FeePerEvent.Amount))
            Sponsors.Add(new SponsorRow(d));
        foreach (var o in promo.Offers.Where(o => o.Media is null).OrderBy(o => o.ExpiresOn))
            Offers.Add(new OfferRow(o));

        foreach (var e in Enumerable.Reverse(promo.Ledger).Take(25)) Recent.Add(new TxnRow(e));
    }
}
