using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One of your belts on the home page.</summary>
public sealed class ChampionRow
{
    public int FighterId { get; init; }   // 0 = vacant belt
    public required ImageSource? Portrait { get; init; }
    public required string Division { get; init; }
    public required string Name { get; init; }
    public required string Record { get; init; }
}

/// <summary>A deal about to run out, surfaced on the home page.</summary>
public sealed class ExpiringRow
{
    public int FighterId { get; init; }
    public required string Name { get; init; }
    public required string Division { get; init; }
    public required string Deal { get; init; }
}

/// <summary>A career milestone, earned or still to come, for the achievements strip.</summary>
public sealed class MilestoneRow
{
    public required string Title { get; init; }
    public required bool Earned { get; init; }
    public string Mark => Earned ? "🏆" : "○";
}

/// <summary>A call sitting on the owner's desk, awaiting yes or no.</summary>
public sealed class DecisionRow
{
    public required Decision Model { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string Due { get; init; }
}

/// <summary>
/// The home page: your promotion at a glance — who you are on the ladder, your
/// next card, the belts, deals about to run out, headlines, inbox, and the
/// owner's objectives. Everything links off to the tab that acts on it.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    /// <summary>Set by the shell; jumps the main tab control to another screen.</summary>
    public Action<int>? Navigate { get; set; }

    public ICommand BookCommand { get; }
    public ICommand ContractsCommand { get; }
    public ICommand InboxCommand { get; }
    public ICommand AcceptDecisionCommand { get; }
    public ICommand DeclineDecisionCommand { get; }

    /// <summary>Set by the shell: the world reference for desk actions + a refresh hook.</summary>
    public Func<World?>? GetWorld { get; set; }
    public Action? OnDecisionMade { get; set; }

    public DashboardViewModel()
    {
        BookCommand = new RelayCommand(() => Navigate?.Invoke(Tab.BookEvent));
        ContractsCommand = new RelayCommand(() => Navigate?.Invoke(Tab.Contracts));
        InboxCommand = new RelayCommand(() => Navigate?.Invoke(Tab.Inbox));
        AcceptDecisionCommand = new RelayCommand<DecisionRow>(row =>
        {
            if (GetWorld?.Invoke() is not { } w) return;
            MmaManager.Core.Sim.Living.Decisions.Accept(w, row.Model, w.CurrentDate);
            OnDecisionMade?.Invoke();
        });
        DeclineDecisionCommand = new RelayCommand<DecisionRow>(row =>
        {
            if (GetWorld?.Invoke() is not { } w) return;
            MmaManager.Core.Sim.Living.Decisions.Decline(w, row.Model, w.CurrentDate);
            OnDecisionMade?.Invoke();
        });
    }

    // --- Your promotion -------------------------------------------------------
    private ImageSource? _logo;
    public ImageSource? Logo { get => _logo; private set => Set(ref _logo, value); }

    private string _orgName = "";
    public string OrgName { get => _orgName; private set => Set(ref _orgName, value); }

    private string _orgTagline = "";
    public string OrgTagline { get => _orgTagline; private set => Set(ref _orgTagline, value); }

    private int _prestige;
    public int Prestige { get => _prestige; private set => Set(ref _prestige, value); }

    private int _reputation;
    public int Reputation { get => _reputation; private set => Set(ref _reputation, value); }

    private string _balanceText = "";
    public string BalanceText { get => _balanceText; private set => Set(ref _balanceText, value); }

    private string _rosterLine = "";
    public string RosterLine { get => _rosterLine; private set => Set(ref _rosterLine, value); }

    private string _tierLine = "";
    /// <summary>The climb, quantified: what the next size tier is and how far away.</summary>
    public string TierLine { get => _tierLine; private set => Set(ref _tierLine, value); }

    private int _tierProgress;
    /// <summary>0–100 fill toward the next reach tier (the climb bar).</summary>
    public int TierProgress { get => _tierProgress; private set => Set(ref _tierProgress, value); }

    // --- Next event -----------------------------------------------------------
    private bool _hasNextEvent;
    public bool HasNextEvent { get => _hasNextEvent; private set => Set(ref _hasNextEvent, value); }

    private string _nextEventTitle = "";
    public string NextEventTitle { get => _nextEventTitle; private set => Set(ref _nextEventTitle, value); }

    private string _nextEventLine = "";
    public string NextEventLine { get => _nextEventLine; private set => Set(ref _nextEventLine, value); }

    private string _nextEventMain = "";
    public string NextEventMain { get => _nextEventMain; private set => Set(ref _nextEventMain, value); }

    // --- Inbox ----------------------------------------------------------------
    private string _inboxLine = "";
    public string InboxLine { get => _inboxLine; private set => Set(ref _inboxLine, value); }

    // --- Lists ----------------------------------------------------------------
    public ObservableCollection<NewsRow> Headlines { get; } = new();
    public ObservableCollection<MailRow> RecentMail { get; } = new();
    public ObservableCollection<ChampionRow> Champions { get; } = new();
    public ObservableCollection<ExpiringRow> Expiring { get; } = new();
    public ObservableCollection<ObjectiveRow> Objectives { get; } = new();
    public ObservableCollection<DecisionRow> Desk { get; } = new();
    public bool DeskHasCalls => Desk.Count > 0;
    public ObservableCollection<PromotionRow> Ladder { get; } = new();
    public ObservableCollection<MilestoneRow> Milestones { get; } = new();

    private string _milestoneLine = "";
    /// <summary>"3 of 9 milestones" — the achievements header.</summary>
    public string MilestoneLine { get => _milestoneLine; private set => Set(ref _milestoneLine, value); }

    public void Attach(World world)
    {
        var player = world.Get(world.PlayerPromotion);
        if (player is null) return;

        Logo = LogoFactory.ForPromotion(player);
        OrgName = player.Name;
        Prestige = player.Prestige.Value;
        Reputation = player.Reputation.Value;
        BalanceText = player.Balance.ToString();

        var byPrestige = world.Promotions.Values.Where(p => !p.Defunct).OrderByDescending(p => p.Prestige.Value).ToList();
        int rank = byPrestige.FindIndex(p => p.Id == player.Id) + 1;
        string hq = player.HomeCity is { } hc && world.Get(hc) is { } hqCity ? hqCity.Name : FighterRow.Pretty(player.BaseRegion.ToString());
        OrgTagline = $"{OrgSizing.Label(OrgSizing.SizeOf(player))} promotion  ·  HQ {hq}  ·  #{rank} of {byPrestige.Count} in the world";

        var roster = world.RosterOf(player.Id).Where(f => f.Status != FighterStatus.Retired).ToList();
        RosterLine = $"{roster.Count} fighters under contract  ·  {player.Divisions.Count} divisions";

        // The climb: what your reach covers now, how far into the tier you are,
        // and what the next tier opens.
        var standing = OrgSizing.Progress(player);
        TierProgress = standing.Percent;
        TierLine = standing.Next is { } nextTier
            ? $"▲ {OrgSizing.Label(standing.Tier)}  ·  {standing.Percent}% to {OrgSizing.Label(nextTier)}"
            : $"★ International {standing.Percent}% — the whole map is yours";

        // Next card.
        var next = world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == player.Id)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
            .FirstOrDefault();
        HasNextEvent = next is not null;
        if (next is not null)
        {
            int days = next.Date.DayNumber - world.CurrentDate.DayNumber;
            var main = next.Card.LastOrDefault();
            NextEventTitle = next.Name;
            NextEventLine = $"{next.Date:ddd d MMM yyyy}  ·  {(days <= 0 ? "tonight" : $"in {days} days")}  ·  {next.Card.Count} bout{(next.Card.Count == 1 ? "" : "s")}";
            NextEventMain = main is null
                ? "No bouts on the card yet."
                : $"Main event: {world.Get(main.RedCorner)?.FullName} vs {world.Get(main.BlueCorner)?.FullName}";
        }
        else
        {
            NextEventTitle = "No card booked";
            NextEventLine = "Your fighters are waiting. Book a show to keep the momentum.";
            NextEventMain = "";
        }

        // Headlines + inbox.
        Headlines.Clear();
        foreach (var n in Enumerable.Reverse(world.News).Take(7))
            Headlines.Add(new NewsRow(n, n.Promotion == player.Id));

        RecentMail.Clear();
        foreach (var m in Enumerable.Reverse(world.Mail).Take(3))
            RecentMail.Add(new MailRow(m, world));
        int unread = world.Mail.Count(m => !m.Read);
        InboxLine = unread == 0 ? "Inbox is clear." : $"{unread} unread message{(unread == 1 ? "" : "s")}.";

        // Belts.
        Champions.Clear();
        foreach (var division in player.Divisions)
        {
            if (division.Title is not { } tid || world.Get(tid) is not { } title) continue;
            var champ = title.CurrentChampion is { } cid ? world.Get(cid) : null;
            Champions.Add(new ChampionRow
            {
                FighterId = champ?.Id.Value ?? 0,
                Portrait = champ is null ? null : Portraits.For(champ, world),
                Division = FighterRow.Pretty(division.WeightClass.ToString()),
                Name = champ?.FullName ?? "Vacant",
                Record = champ?.Record.ToString() ?? "",
            });
        }

        // The owner's desk: calls awaiting an answer.
        Desk.Clear();
        foreach (var d in world.Decisions.Where(d => d.State == DecisionState.Pending).OrderBy(d => d.Deadline))
            Desk.Add(new DecisionRow
            {
                Model = d,
                Title = d.Kind switch
                {
                    DecisionKind.TitleShotRequest => "⚡ Title shot demanded",
                    DecisionKind.SponsorShowcase => $"🤝 {d.Sponsor} wants a showcase",
                    DecisionKind.VeteranCut => "✂ The board wants a release",
                    DecisionKind.AcquisitionOffer => "🏦 A promotion is for sale",
                    _ => "Decision",
                },
                Detail = d.Detail,
                Due = $"answer by {d.Deadline:d MMM}",
            });
        OnPropertyChanged(nameof(DeskHasCalls));

        // Deals about to run out.
        Expiring.Clear();
        foreach (var f in roster
                     .Where(f => f.Contract is { FightsRemaining: <= 1 })
                     .OrderBy(f => f.Contract!.FightsRemaining)
                     .Take(6))
            Expiring.Add(new ExpiringRow
            {
                FighterId = f.Id.Value,
                Name = f.FullName,
                Division = FighterRow.Pretty(f.WeightClass.ToString()),
                Deal = $"{f.Contract!.PursePerFight} / fight  ·  {f.Contract.FightsRemaining} left",
            });

        // Owner objectives.
        Objectives.Clear();
        foreach (var o in player.Objectives)
            Objectives.Add(new ObjectiveRow(o));

        // The ladder.
        Ladder.Clear();
        foreach (var p in byPrestige)
            Ladder.Add(new PromotionRow(p, world));

        // Career milestones — earned first, then what's still ahead.
        Milestones.Clear();
        int earned = 0;
        foreach (var (key, title) in MmaManager.Core.Sim.Org.Milestones.Catalog
                     .OrderByDescending(m => player.Milestones.Contains(m.Key)))
        {
            bool got = player.Milestones.Contains(key);
            if (got) earned++;
            Milestones.Add(new MilestoneRow { Title = title, Earned = got });
        }
        MilestoneLine = $"{earned} of {MmaManager.Core.Sim.Org.Milestones.Catalog.Count} milestones";
    }
}
