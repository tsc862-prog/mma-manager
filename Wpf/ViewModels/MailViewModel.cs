using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One button in a letter: what the player can do about it, wired to a command the app already has.</summary>
public sealed class LetterAction
{
    public required string Text { get; init; }
    public required ICommand Command { get; init; }
    public object? Parameter { get; init; }
    public bool IsPrimary { get; init; }
    /// <summary>Puts the letter in "Needs a decision" (a contract renewal acts, but lives under Fighters).</summary>
    public bool Decides { get; init; } = true;
}

/// <summary>The inbox folders (plain filters; a letter can sit in more than one).</summary>
public enum MailFolder { Decisions, Board, Offers, Fighters, Digest, Everything }

/// <summary>A folder choice with how many letters are in it.</summary>
public sealed class FolderOption
{
    public required MailFolder Folder { get; init; }
    public required string Label { get; init; }
    public int Count { get; init; }
    public int Unread { get; init; }
    public string CountText => Count == 0 ? "" : Unread > 0 ? $"{Unread} new" : Count.ToString();
    public bool HasCount => Count > 0;
    public bool HasUnread => Unread > 0;
}

/// <summary>
/// One letter on the promoter's desk: who it is from (a face, or a desk's icon, and
/// a name), the subject and the opening line, and — when it is about something the
/// player can act on right now — the buttons that do it, wired to the same commands
/// the rest of the app uses. Rebuilt on every refresh; the inbox holds the
/// <see cref="MailMessage"/> itself (not this row) to keep the selection.
/// </summary>
public sealed class LetterModel : ObservableObject
{
    private readonly Func<ImageSource?>? _picture;
    private ImageSource? _pic;
    private bool _picLoaded;

    public LetterModel(MailMessage m, Func<ImageSource?>? picture)
    {
        Model = m;
        _picture = picture;
        _unread = !m.Read;
    }

    public MailMessage Model { get; }
    public required string Sender { get; init; }
    public required string SenderRole { get; init; }
    /// <summary>The desk's icon when there is no face to show.</summary>
    public required string Icon { get; init; }
    public ImageSource? Picture { get { if (!_picLoaded) { _pic = _picture?.Invoke(); _picLoaded = true; } return _pic; } }
    public bool HasPicture => _picture is not null;
    public required string When { get; init; }
    public required string FullDate { get; init; }
    public string Subject => Model.Subject;
    public required string Snippet { get; init; }
    public string Body => Model.Body;

    public required IReadOnlySet<MailFolder> Folders { get; init; }
    public bool NeedsDecision => Folders.Contains(MailFolder.Decisions);

    /// <summary>What can be done about it, right here (empty = nothing to do, or it's been done).</summary>
    public IReadOnlyList<LetterAction> Actions { get; init; } = Array.Empty<LetterAction>();
    public bool HasActions => Actions.Count > 0;
    /// <summary>One plain line over the buttons ("The offer lapses on 12 May.") or the outcome ("Decided.").</summary>
    public string ActionNote { get; init; } = "";
    public bool HasActionNote => ActionNote.Length > 0;
    public string DueText { get; init; } = "";
    /// <summary>The list tag: "Needs you · 12 days left".</summary>
    public string NeedsTag => DueText.Length > 0 ? $"Needs you  ·  {DueText}" : "Needs you";

    // Follow the letter to who/what it's about.
    public int FighterId { get; init; }
    public string FighterName { get; init; } = "";
    public bool HasFighter => FighterId != 0;
    /// <summary>The face on the "about" chip, decoded when the letter is opened.</summary>
    public ImageSource? FighterFace => _face ??= FaceLoader?.Invoke();
    private ImageSource? _face;
    public Func<ImageSource?>? FaceLoader { get; init; }
    public int EventId { get; init; }
    public string EventName { get; init; } = "";
    public bool HasEvent => EventId != 0;

    private bool _unread;
    public bool Unread { get => _unread; set => Set(ref _unread, value); }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>
/// The Inbox: the promoter's correspondence. Letters with the sender's face or
/// desk, a reading pane, plain folder filters (Needs a decision / From the board /
/// Offers / Fighters / Digest / Everything), mark-all and
/// digests-read in bulk, and — where a letter is about something live — the
/// action inside the letter, wired to commands that already exist (desk
/// decisions, sponsor and media offers, contract talks, the tier step-up).
/// No game rule lives here: actions only call those commands.
/// </summary>
public sealed class MailViewModel : ObservableObject
{
    private World? _world;
    private MailMessage? _selectedMessage;

    // --- Wiring (set by the shell) ---------------------------------------------
    public ICommand? AcceptDecisionCommand { get; set; }
    public ICommand? DeclineDecisionCommand { get; set; }
    public ICommand? AcceptSponsorCommand { get; set; }
    public ICommand? DeclineSponsorCommand { get; set; }
    public ICommand? AcceptMediaCommand { get; set; }
    public ICommand? DeclineMediaCommand { get; set; }
    public ICommand? NegotiateCommand { get; set; }
    public ICommand? PromoteCommand { get; set; }
    public Action<int>? Navigate { get; set; }
    /// <summary>Run after an action that does not refresh the app by itself.</summary>
    public Action? AfterAction { get; set; }
    /// <summary>Ask before something expensive (heading, body, detail, confirm) — null = go ahead (harness).</summary>
    public Func<string, string, string?, string, bool>? Confirm { get; set; }

    public MailViewModel()
    {
        MarkAllReadCommand = new RelayCommand(MarkAllRead, () => UnreadCount > 0);
        MarkShownReadCommand = new RelayCommand(() => MarkRead(Letters.Select(l => l.Model)), () => Letters.Any(l => l.Unread));
        MarkDigestsReadCommand = new RelayCommand(() => MarkRead(_all.Where(l => l.Folders.Contains(MailFolder.Digest)).Select(l => l.Model)),
            () => DigestUnread > 0);
        OpenTabCommand = new RelayCommand<int>(t => Navigate?.Invoke(t));
    }

    public ICommand MarkAllReadCommand { get; }
    public ICommand MarkShownReadCommand { get; }
    public ICommand MarkDigestsReadCommand { get; }
    public ICommand OpenTabCommand { get; }

    public ObservableCollection<LetterModel> Letters { get; } = new();
    /// <summary>Letters waiting on a decision (up to 5), shown in the reading pane while no letter is open.</summary>
    public ObservableCollection<LetterModel> Waiting { get; } = new();
    public bool HasWaiting => Waiting.Count > 0;
    private bool _noMail;
    /// <summary>Not a single letter yet (a founder's first day): one friendly empty desk instead of empty panes.</summary>
    public bool NoMail { get => _noMail; private set { if (Set(ref _noMail, value)) OnPropertyChanged(nameof(HasMail)); } }
    public bool HasMail => !NoMail;
    public ObservableCollection<FolderOption> Folders { get; } = new();
    private List<LetterModel> _all = new();

    private FolderOption? _folder;
    public FolderOption? Folder
    {
        get => _folder;
        set
        {
            if (value is null || ReferenceEquals(_folder, value)) return;
            _folderKey = value.Folder;
            _folder = value;
            OnPropertyChanged();
            Refilter();
        }
    }
    private MailFolder _folderKey = MailFolder.Everything;
    public MailFolder FolderKey => _folderKey;
    public void ShowFolder(MailFolder f) => Folder = Folders.FirstOrDefault(o => o.Folder == f);

    private int _unreadCount;
    public int UnreadCount { get => _unreadCount; private set { if (Set(ref _unreadCount, value)) OnPropertyChanged(nameof(Header)); } }
    public string Header => UnreadCount > 0 ? $"Inbox ({UnreadCount} unread)" : "Inbox";

    private int _digestUnread;
    public int DigestUnread { get => _digestUnread; private set { if (Set(ref _digestUnread, value)) OnPropertyChanged(nameof(HasDigestUnread)); } }
    public bool HasDigestUnread => DigestUnread > 0;

    private string _summary = "";
    /// <summary>"3 letters need a decision · 41 unread" (shown when no letter is open).</summary>
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    private LetterModel? _selected;
    public LetterModel? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            if (_selected is not null) _selected.IsSelected = false;
            _selected = value;
            if (value is not null) { value.IsSelected = true; _selectedMessage = value.Model; MarkRead(new[] { value.Model }); }
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
        }
    }
    public bool HasSelection => _selected is not null;
    private bool _listEmpty;
    public bool ListEmpty { get => _listEmpty; private set => Set(ref _listEmpty, value); }

    public void Attach(World world)
    {
        _world = world;
        var player = world.Get(world.PlayerPromotion);
        _all = Enumerable.Reverse(world.Mail).Select(m => Build(world, player, m)).ToList();
        UnreadCount = world.Mail.Count(m => !m.Read);
        NoMail = _all.Count == 0;
        RefreshFolders();
        Refilter();
    }

    private void RefreshFolders()
    {
        var folders = new (MailFolder F, string Label)[]
        {
            (MailFolder.Decisions, "Needs a decision"), (MailFolder.Board, "From the board"), (MailFolder.Offers, "Offers"),
            (MailFolder.Fighters, "Fighters"), (MailFolder.Digest, "Digest"), (MailFolder.Everything, "Everything"),
        };
        Folders.Clear();
        foreach (var (f, label) in folders)
        {
            var inIt = f == MailFolder.Everything ? _all : _all.Where(l => l.Folders.Contains(f)).ToList();
            Folders.Add(new FolderOption { Folder = f, Label = label, Count = inIt.Count, Unread = inIt.Count(l => l.Unread) });
        }
        _folder = Folders.First(o => o.Folder == _folderKey);
        OnPropertyChanged(nameof(Folder));
        DigestUnread = _all.Count(l => l.Unread && l.Folders.Contains(MailFolder.Digest));
        Waiting.Clear();
        foreach (var l in _all.Where(l => l.NeedsDecision).Take(5)) Waiting.Add(l);
        OnPropertyChanged(nameof(HasWaiting));
        int decisions = _all.Count(l => l.NeedsDecision);
        Summary = (decisions == 0 ? "Nothing in the post needs a decision." : decisions == 1 ? "1 letter needs a decision." : $"{decisions} letters need a decision.")
                  + (UnreadCount > 0 ? $"  ·  {UnreadCount} unread" : "  ·  all read");
    }

    private void Refilter()
    {
        Letters.Clear();
        foreach (var l in _all.Where(l => _folderKey == MailFolder.Everything || l.Folders.Contains(_folderKey)))
            Letters.Add(l);
        ListEmpty = Letters.Count == 0;
        // Keep the open letter open across a refresh (held by its message, not its row).
        var keep = _selectedMessage is null ? null : _all.FirstOrDefault(l => ReferenceEquals(l.Model, _selectedMessage));
        _selected = null;
        if (keep is not null) { keep.IsSelected = true; _selected = keep; }
        else _selectedMessage = null;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void MarkAllRead()
    {
        if (_world is null) return;
        MarkRead(_world.Mail);
    }

    private void MarkRead(IEnumerable<MailMessage> messages)
    {
        if (_world is null) return;
        bool any = false;
        foreach (var m in messages.ToList()) if (!m.Read) { m.Read = true; any = true; }
        if (!any) return;
        foreach (var l in _all) l.Unread = !l.Model.Read;
        UnreadCount = _world.Mail.Count(m => !m.Read);
        var keep = _folderKey;
        RefreshFolders();
        _folderKey = keep;
    }

    // --- Building a letter ------------------------------------------------------------
    private LetterModel Build(World w, Promotion? player, MailMessage m)
    {
        var today = w.CurrentDate;
        var about = m.AboutFighter is { } fid ? w.Get(fid) : null;
        var ev = m.AboutEvent is { } eid && w.Events.TryGetValue(eid, out var e) ? e : null;
        var folders = new HashSet<MailFolder>();
        var actions = new List<LetterAction>();
        string note = "", due = "";

        // --- who it's from ---
        string from = m.From;
        string role;
        string icon;
        Func<ImageSource?>? pic = null;
        string ownerName = player?.OwnerName ?? "";
        if (about is not null && about.FullName == from)
        {
            role = $"Fighter  ·  {FighterRow.DivisionLabel(about.WeightClass, about.Sex)}";
            icon = "People";
            pic = () => Portraits.Thumb(about, w);
        }
        else
        {
            (role, icon) = from switch
            {
                "The Accountant" => ("Your accountant", "Money"),
                "The Board" => ($"The board of {player?.Abbreviation ?? "the promotion"}", "Building"),
                "The Owner" => (ownerName.Length > 0 ? $"{ownerName}, owner" : "The owner", "Building"),
                "The Desk" => ("The front desk  ·  calls only you can make", "Warning"),
                "Matchmaking" => ("Matchmaking desk", "Calendar"),
                "Commercial" => ("Commercial desk  ·  sponsors", "Money"),
                "PR" => (StaffLine(player, StaffRole.Marketing, "Press and marketing"), "Broadcast"),
                "Scouting" => (StaffLine(player, StaffRole.Scouting, "Scouting desk"), "Search"),
                "The Agent" => ("About a free agent on the market", "People"),
                "The Network" => ("Broadcast partners", "Broadcast"),
                "MMA Weekly" => ("The weekly digest", "Newspaper"),
                "FightWire" => ("The sport's news wire", "Newspaper"),
                _ when ownerName.Length > 0 && from == ownerName => ($"Owner of {player!.Name}", "Building"),
                _ when m.Category == MailCategory.FighterRequest => ("Fighter", "People"),
                _ => ("Partner", "Money"),
            };
        }

        // --- folders by who sent it and what it is ---
        bool digest = from is "MMA Weekly" or "FightWire";
        if (digest) folders.Add(MailFolder.Digest);
        if (m.Category == MailCategory.BoardNote || from is "The Board" or "The Owner" or "The Accountant" or "The Desk"
            || (ownerName.Length > 0 && from == ownerName))
            if (!digest) folders.Add(MailFolder.Board);
        if (m.Subject.StartsWith("Sponsorship offer:", StringComparison.Ordinal) || m.Subject.StartsWith("Media offer:", StringComparison.Ordinal)
            || from == "The Agent")
            folders.Add(MailFolder.Offers);
        if (m.Category is MailCategory.FighterRequest or MailCategory.ContractAlert || from == "The Agent"
            || (about is not null && about.FullName == from))
            folders.Add(MailFolder.Fighters);

        // --- what can be done about it, now ---
        if (m.Subject.StartsWith("Decision:", StringComparison.Ordinal))
        {
            var d = w.Decisions.FirstOrDefault(x => x.Created == m.Date && m.Body.StartsWith(x.Detail, StringComparison.Ordinal));
            if (d is { State: DecisionState.Pending } && AcceptDecisionCommand is not null && DeclineDecisionCommand is not null)
            {
                int days = d.Deadline.DayNumber - today.DayNumber;
                due = days <= 0 ? "Due today" : days == 1 ? "Due tomorrow" : $"{days} days left";
                note = d.Price.Amount > 0 ? $"Saying yes costs {d.Price}. Ignoring it past {d.Deadline:d MMM} counts as an answer."
                                          : $"Ignoring it past {d.Deadline:d MMM} counts as an answer.";
                actions.Add(new LetterAction { Text = DeskCalls.YesText(w, d, short_: true), Command = AcceptDecisionCommand, Parameter = d, IsPrimary = true });
                actions.Add(new LetterAction { Text = DeskCalls.NoText(w, d, short_: true), Command = DeclineDecisionCommand, Parameter = d });
            }
            else if (d is { State: DecisionState.Accepted }) note = "You said yes. It's settled at the next event.";
            else note = "This call has been made.";
        }
        else if (about is not null && m.Subject.StartsWith($"{about.FullName} has an offer from ", StringComparison.Ordinal)
                 && w.Decisions.FirstOrDefault(x => x.Kind == DecisionKind.RivalOffer && x.Fighter == about.Id && x.State == DecisionState.Pending) is { } rd
                 && AcceptDecisionCommand is not null && DeclineDecisionCommand is not null)
        {
            // A rival's offer: the same three ways forward as the Desk (match, the table, let him go).
            int days = rd.Deadline.DayNumber - today.DayNumber;
            due = days <= 0 ? "Due today" : days == 1 ? "Due tomorrow" : $"{days} days left";
            note = $"Ignoring it past {rd.Deadline:d MMM} lets him go.";
            actions.Add(new LetterAction { Text = DeskCalls.YesText(w, rd), Command = AcceptDecisionCommand, Parameter = rd, IsPrimary = true });
            if (NegotiateCommand is not null)
                actions.Add(new LetterAction { Text = "Sit down with him ▸", Command = NegotiateCommand, Parameter = about.Id.Value, Decides = false });
            actions.Add(new LetterAction { Text = DeskCalls.NoText(w, rd), Command = DeclineDecisionCommand, Parameter = rd });
        }
        else if (m.Subject.StartsWith("Sponsorship offer:", StringComparison.Ordinal) || m.Subject.StartsWith("Media offer:", StringComparison.Ordinal))
        {
            bool media = m.Subject.StartsWith("Media", StringComparison.Ordinal);
            var o = player?.Offers.FirstOrDefault(x => (x.Media is not null) == media && x.ExpiresOn >= today
                                                      && m.Subject.Contains(x.Partner, StringComparison.Ordinal));
            var accept = media ? AcceptMediaCommand : AcceptSponsorCommand;
            var decline = media ? DeclineMediaCommand : DeclineSponsorCommand;
            if (o is not null && accept is not null && decline is not null)
            {
                int days = o.ExpiresOn.DayNumber - today.DayNumber;
                due = days <= 1 ? "Lapses tomorrow" : $"{days} days left";
                note = $"{o.FeePerEvent} an event. The offer lapses on {o.ExpiresOn:d MMM}.";
                var row = new OfferRow(o);
                actions.Add(new LetterAction { Text = "Sign the deal", Command = Then(accept), Parameter = row, IsPrimary = true });
                actions.Add(new LetterAction { Text = "Turn it down", Command = Then(decline), Parameter = row });
                actions.Add(new LetterAction { Text = media ? "Media desk ▸" : "Finance ▸", Command = OpenTabCommand, Parameter = media ? Tab.Media : Tab.Finance });
            }
            else note = player?.SponsorDeals.Any(s => m.Subject.Contains(s.Partner, StringComparison.Ordinal)) == true
                        || player?.MediaDeals.Any(s => m.Subject.Contains(s.Partner, StringComparison.Ordinal)) == true
                ? "Signed." : "This offer is off the table.";
        }
        else if (m.Subject.StartsWith("Eligible to promote to", StringComparison.Ordinal))
        {
            if (player is not null && PromotionLadder.IsEligible(player) && PromoteCommand is not null)
            {
                var to = PromotionLadder.NextTier(player);
                note = $"Bigger gates and better talent at {OrgSizing.Label(to)} — but the running costs jump too.";
                actions.Add(new LetterAction { Text = $"Step up to {OrgSizing.Label(to)}", Command = AskThen(PromoteCommand,
                    $"Step up to {OrgSizing.Label(to)}?",
                    $"{player.Name} moves up to {OrgSizing.Label(to)}. Gates and the talent you can sign get bigger, and so do the monthly running costs.",
                    "Only take the leap when the bank can carry it. There's no stepping back down by choice."), IsPrimary = true });
                actions.Add(new LetterAction { Text = "HQ ▸", Command = OpenTabCommand, Parameter = Tab.Org });
            }
            else note = player is not null && OrgSizing.SizeOf(player) > OrgSize.City ? "Done — or no longer on the table." : "";
        }
        else if (about is not null && NegotiateCommand is not null)
        {
            bool ours = about.Contract?.Promotion == w.PlayerPromotion;
            bool free = about.Contract is null && about.Status != FighterStatus.Retired;
            bool war = w.BiddingWars.Any(b => b.Fighter == about.Id);
            if (m.Category == MailCategory.ContractAlert && ours && about.Contract is { FightsRemaining: <= 1 })
            {
                note = about.Contract.FightsRemaining > 0 ? "One fight left on the deal."
                     : about.Contract.WindowEndsOn is { } end ? $"The deal is up — yours alone to re-sign until {end:MMM d}, then a free agent."
                     : "The deal is up.";
                actions.Add(new LetterAction { Text = "Talk terms ▸", Command = NegotiateCommand, Parameter = about.Id.Value, IsPrimary = true, Decides = false });
            }
            else if ((from == "The Agent" || m.Category == MailCategory.ContractAlert) && free)
            {
                note = war ? "The bidding war is still live." : "Still a free agent.";
                actions.Add(new LetterAction { Text = "Open talks ▸", Command = NegotiateCommand, Parameter = about.Id.Value, IsPrimary = true });
            }
            else if (from == "The Agent" && !free)
                note = about.Contract is { } c && w.Get(c.Promotion) is { } signed ? $"Signed with {signed.Abbreviation}." : "";
            else if (m.Category == MailCategory.FighterRequest && m.Subject.StartsWith("Callout:", StringComparison.Ordinal) && ours)
                actions.Add(new LetterAction { Text = "Book Event ▸", Command = OpenTabCommand, Parameter = Tab.BookEvent });
            else if (m.Category == MailCategory.FighterRequest && m.Subject.Contains("bigger camp", StringComparison.Ordinal) && ours)
                actions.Add(new LetterAction { Text = "Camps ▸", Command = OpenTabCommand, Parameter = Tab.Camps });
        }

        if (actions.Any(a => a.IsPrimary && a.Decides)) folders.Add(MailFolder.Decisions);

        string snippet = m.Body.Replace("\r", " ").Replace("\n", " ");
        if (snippet.Length > 140) snippet = snippet[..140].TrimEnd() + "…";
        return new LetterModel(m, pic)
        {
            Sender = from,
            SenderRole = role,
            Icon = icon,
            When = m.Date == today ? "Today" : m.Date == today.AddDays(-1) ? "Yesterday" : m.Date.ToString("d MMM"),
            FullDate = m.Date.ToString("dddd d MMMM yyyy"),
            Snippet = snippet,
            Folders = folders,
            Actions = actions,
            ActionNote = note,
            DueText = due,
            FighterId = about?.Id.Value ?? 0,
            FighterName = about is null ? "" : $"{about.FirstName} {about.LastName}",
            FaceLoader = about is null ? null : () => Portraits.Thumb(about, w),
            EventId = ev?.Id.Value ?? 0,
            EventName = ev?.Name ?? "",
        };
    }

    // The desk's line, with today's holder named when the player has one ("Press and marketing · Rosa Diaz").
    private static string StaffLine(Promotion? p, StaffRole role, string desk) =>
        p?.Staff.FirstOrDefault(s => s.Role == role)?.Name is { Length: > 0 } name ? $"{desk}  ·  {name}" : desk;

    // An existing command, then the app-wide refresh (for commands that only refresh their own screen).
    private ICommand Then(ICommand inner) => new RelayCommand<object?>(p =>
    {
        if (!inner.CanExecute(p)) return;
        inner.Execute(p);
        AfterAction?.Invoke();
    });

    // An expensive existing command: ask first (interactive only), then run it.
    private ICommand AskThen(ICommand inner, string heading, string body, string detail) => new RelayCommand(() =>
    {
        if (Confirm is { } ask && !ask(heading, body, detail, heading.TrimEnd('?'))) return;
        if (inner.CanExecute(null)) inner.Execute(null);
    });
}
