using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Living;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One man as the field shows him: seed, face, what he is, why he's in (or out).</summary>
public sealed class FieldEntry
{
    public FieldEntry(Fighter f, World w, string seed, string note, CardTone tone)
    {
        Id = f.Id.Value;
        Seed = seed;
        Name = f.FullName;
        Nickname = f.Nickname is { Length: > 0 } n ? $"“{n}”" : "";
        Craft = FighterRow.Pretty(f.PrimaryStyle.ToString());
        Size = $"{FighterRow.Kilos(f)} kg · {f.HeightCm} cm";
        Record = f.Record.ToString();
        Read = ReadWords.Read(FighterRow.Scouted(w, f));   // the scouts' read, never the truth
        Note = note;
        Tone = tone;
        _f = f; _w = w;
    }

    private readonly Fighter _f;
    private readonly World _w;
    public int Id { get; }
    public string Seed { get; }
    public string Name { get; }
    public string Nickname { get; }
    public string Craft { get; }
    public string Size { get; }
    public string Record { get; }
    public string Read { get; }
    public string Note { get; }
    public bool HasNote => Note.Length > 0;
    public CardTone Tone { get; }
    public LazyFace Face => _face ??= new LazyFace(ready => Portraits.Thumb(_f, _w, ready));
    private LazyFace? _face;
}

/// <summary>One corner of a bracket bout.</summary>
public sealed class BracketCorner
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string FullName { get; init; } = "";
    public required string Seed { get; init; }
    public required bool Won { get; init; }
    public required bool Out { get; init; }
    public string Tag { get; init; } = "";
    public bool HasTag => Tag.Length > 0;
    public required LazyFace Face { get; init; }
}

/// <summary>One bout of the night as the bracket draws it.</summary>
public sealed class BracketBoutView
{
    public required BracketCorner Red { get; init; }
    public required BracketCorner Blue { get; init; }
    public required string How { get; init; }
    public string Aside { get; init; } = "";
    public bool HasAside => Aside.Length > 0;
    public required int EventId { get; init; }
}

/// <summary>A round of the night ("Quarter-finals").</summary>
public sealed record BracketRound(string Name, IReadOnlyList<BracketBoutView> Bouts);

/// <summary>
/// A tournament night that has been fought, drawn as a bracket from the bouts it put on the card (the card
/// holds them in the order the night fought them, round by round, slot by slot). Read-only.
/// </summary>
public sealed class BracketBoard
{
    public required string Title { get; init; }
    public required string When { get; init; }
    public required IReadOnlyList<BracketRound> Rounds { get; init; }
    public string ChampionName { get; init; } = "";
    public string ChampionLine { get; init; } = "";
    public int ChampionId { get; init; }
    public ImageSource? ChampionBody { get; init; }
    public bool HasChampion => ChampionId != 0;
    public string Verdict { get; init; } = "";

    public static BracketBoard? From(World w, Event ev)
    {
        if (ev.Format != EventFormat.Tournament || ev.Status != EventStatus.Completed || ev.Card.Count == 0) return null;
        var promo = w.Get(ev.Promotion);
        int size = promo?.Rules.TournamentSize is > 1 and var s ? s : 8;
        int total = 0;
        for (int n = size; n > 1; n /= 2) total++;
        var order = OneNightBracket.Order(size);
        bool judges = promo?.Rules.Fight?.Judges ?? true;

        // Who the night crowned (the durable record: an accolade dated that night).
        var champion = w.Fighters.Values.FirstOrDefault(f => f.Accolades.Any(a => a.Date == ev.Date && a.Title == $"Won {ev.Name}"));

        // Round of each bout: one past the later of its corners' last rounds, never before the bout fought before it.
        var last = new Dictionary<FighterId, int>();
        var firstRound = new Dictionary<FighterId, int>();
        var rounds = new List<(Fight Fight, int Round)>();
        int prev = 1;
        for (int i = 0; i < ev.Card.Count; i++)
        {
            var f = ev.Card[i];
            int r = Math.Max(prev, Math.Max(last.GetValueOrDefault(f.RedCorner), last.GetValueOrDefault(f.BlueCorner)) + 1);
            if (i < size / 2) r = 1;
            if (i == ev.Card.Count - 1 && champion is not null && f.Outcome?.Winner == champion.Id) r = total;
            r = Math.Min(r, total);
            rounds.Add((f, r));
            prev = r;
            foreach (var id in new[] { f.RedCorner, f.BlueCorner }) { last[id] = r; firstRound.TryAdd(id, r); }
        }

        // Seeds from round one (the bracket's standard order: 1 v 8, 4 v 5, 2 v 7, 3 v 6).
        var seed = new Dictionary<FighterId, int>();
        int k = 0;
        foreach (var (f, r) in rounds.Where(x => x.Round == 1))
        {
            if (2 * k + 1 < order.Length) { seed[f.RedCorner] = order[2 * k] + 1; seed[f.BlueCorner] = order[2 * k + 1] + 1; }
            k++;
        }

        BracketCorner Corner(FighterId id, Fight f, int r)
        {
            var who = w.Get(id);
            bool won = f.Outcome?.Winner == id;
            bool goesOn = rounds.Any(x => x.Round > r && (x.Fight.RedCorner == id || x.Fight.BlueCorner == id));
            string tag = firstRound.GetValueOrDefault(id) > 1 && !seed.ContainsKey(id) ? "Alternate"
                       : won && !goesOn && r < total && rounds.Any(x => x.Round > r) ? "Could not go on" : "";
            return new BracketCorner
            {
                Id = id.Value,
                Name = who is null ? "?" : $"{who.FirstName} {who.LastName}",
                FullName = who?.FullName ?? "?",
                Seed = seed.TryGetValue(id, out var sd) ? sd.ToString() : "A",
                Won = won,
                Out = !won,
                Tag = tag,
                Face = new LazyFace(ready => who is null ? null : Portraits.Thumb(who, w, ready)),
            };
        }

        string RoundName(int r) => (total - r) switch { 0 => "Final", 1 => "Semi-finals", 2 => "Quarter-finals", _ => $"Round {r}" };

        var board = rounds.GroupBy(x => x.Round).OrderBy(g => g.Key)
            .Select(g => new BracketRound(RoundName(g.Key), g.Select(x => new BracketBoutView
            {
                Red = Corner(x.Fight.RedCorner, x.Fight, x.Round),
                Blue = Corner(x.Fight.BlueCorner, x.Fight, x.Round),
                How = How(x.Fight, judges),
                EventId = ev.Id.Value,
            }).ToList())).ToList();

        string verdict = champion is not null ? ""
            : ev.Card[^1].Outcome is { Winner: null } ? "The final was fought to a standstill, overtime and all: no champion tonight, and the purse is split."
            : "Nobody was left fit to fight the final: no champion tonight.";
        return new BracketBoard
        {
            Title = ev.Name,
            When = $"{ev.Date:dddd d MMMM yyyy}" + (ev.Venue is { } v ? $"  ·  {v}" : ""),
            Rounds = board,
            ChampionId = champion?.Id.Value ?? 0,
            ChampionName = champion?.FullName ?? "",
            ChampionLine = champion is null ? "" : $"Ran the table: {rounds.Count(x => x.Fight.Outcome?.Winner == champion.Id)} wins in one night"
                + (promo?.Rules.TournamentPrize is { Amount: > 0 } prize ? $", and {prize}." : "."),
            ChampionBody = champion is null ? null : Portraits.Body(champion, w),
            Verdict = verdict,
        };
    }

    private static string How(Fight f, bool judges)
    {
        if (f.Outcome is not { } o) return "";
        string clock = $"R{o.EndRound} {o.EndTimeSeconds / 60}:{o.EndTimeSeconds % 60:00}";
        if (o.Winner is null)
            return judges ? $"Draw ({clock}): the better seed goes through" : "Draw at the limit: both men out";
        return $"{FighterRow.Pretty(o.Method.ToString())}  ·  {clock}";
    }
}

/// <summary>
/// The Book Event app for a house whose nights are one-night brackets (the Crucible, Origins): there is no
/// card to build. The player puts a night on the books (<see cref="OriginsShows.ScheduleTournament"/>, with the
/// sim's refusal reason when the roster can't fill it), sees who is fit to fight that night and how the field
/// is drawn, and after the night sees the bracket as a bracket. Holds ids, never rows.
/// </summary>
public sealed class TournamentDeskViewModel : ObservableObject
{
    private World? _world;
    private EventId? _shownNight;
    private DateOnly _statusOn;

    public TournamentDeskViewModel()
    {
        ScheduleCommand = new RelayCommand(Schedule, () => _world is not null && !HasNext);
        CancelCommand = new RelayCommand(Cancel, () => HasNext);
        ShowNightCommand = new RelayCommand<int>(id => ShowNight(new EventId(id)));
    }

    public ICommand ScheduleCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ShowNightCommand { get; }

    /// <summary>Asks before something costly (heading, body, detail, confirm text); null = go ahead (harness).</summary>
    public Func<string, string, string?, string, bool>? Confirm { get; set; }

    /// <summary>Called after the world changed here (a night booked or cancelled).</summary>
    public Action? OnChanged { get; set; }

    private bool _applies;
    /// <summary>The player's house runs one-night brackets (an Origins house): this desk replaces the Card Desk.</summary>
    public bool Applies { get => _applies; private set => Set(ref _applies, value); }

    public string HouseName { get; private set; } = "";
    public ImageSource? Logo { get; private set; }
    public string NightLine { get; private set; } = "";
    public string CadenceLine { get; private set; } = "";
    public IReadOnlyList<string> Rules { get; private set; } = Array.Empty<string>();

    // --- the next night ---------------------------------------------------------
    public bool HasNext => _next is not null;
    public bool NoNext => _world is not null && _next is null;
    private EventId? _next;
    public string NextName { get; private set; } = "";
    public string NextWhen { get; private set; } = "";
    public string NextWhere { get; private set; } = "";

    private DateTime _date = DateTime.Today;
    /// <summary>The date the player is looking at (the form's, until a night is on the books).</summary>
    public DateTime Date { get => _date; set { if (Set(ref _date, value)) { Error = ""; RefreshField(); } } }

    private string _error = "";
    public string Error { get => _error; private set { if (Set(ref _error, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => _error.Length > 0;

    private string _status = "";
    public string Status { get => _status; private set { if (Set(ref _status, value)) OnPropertyChanged(nameof(HasStatus)); } }
    public bool HasStatus => _status.Length > 0;

    // --- the field --------------------------------------------------------------
    public ObservableCollection<FieldEntry> Field { get; } = new();
    public ObservableCollection<FieldEntry> LeftOut { get; } = new();
    public bool HasLeftOut => LeftOut.Count > 0;
    public string FieldHeadline { get; private set; } = "";
    public string FieldLine { get; private set; } = "";
    public bool FieldShort { get; private set; }

    // --- the belt and the last night --------------------------------------------
    public string BeltName { get; private set; } = "";
    public string BeltHolder { get; private set; } = "";
    public string BeltLine { get; private set; } = "";
    public int BeltHolderId { get; private set; }
    public LazyFace? BeltFace { get; private set; }
    public bool HasBeltHolder => BeltHolderId != 0;

    private BracketBoard? _board;
    public BracketBoard? Board { get => _board; private set { if (Set(ref _board, value)) OnPropertyChanged(nameof(HasBoard)); } }
    public bool HasBoard => _board is not null;
    /// <summary>The house's past nights, newest first (pick one to see its bracket).</summary>
    public ObservableCollection<PastNight> Past { get; } = new();
    public bool HasPast => Past.Count > 0;

    public sealed record PastNight(int Id, string Name, string When, string Winner);

    /// <summary>Show this night's bracket (after the night, or from the Events app).</summary>
    public void ShowNight(EventId id) { _shownNight = id; RefreshBoard(); ShowBracket = HasBoard; }

    private bool _showBracket;
    /// <summary>The desk's two faces: the next night (field and booking) or a fought night's bracket.</summary>
    public bool ShowBracket { get => _showBracket; set { if (Set(ref _showBracket, value)) OnPropertyChanged(nameof(ShowNextNight)); } }
    public bool ShowNextNight { get => !_showBracket; set => ShowBracket = !value; }

    public void Attach(World world)
    {
        bool sameWorld = ReferenceEquals(world, _world);
        _world = world;
        var promo = world.Get(world.PlayerPromotion);
        Applies = promo?.Rules.ShowFormat == ShowFormat.OneNightTournament;
        if (!sameWorld) { _shownNight = null; Error = ""; Status = ""; ShowBracket = false; }
        else if (world.CurrentDate != _statusOn) { Status = ""; Error = ""; }   // last week's result is old news
        _statusOn = world.CurrentDate;
        if (!Applies || promo is null) return;

        HouseName = promo.Name;
        Logo = LogoFactory.ForPromotion(promo);
        NightLine = HouseRules.NightLine(promo);
        CadenceLine = $"Houses like this run a bracket {HouseRules.Cadence(promo)}: eight men need the months between to heal.";
        Rules = HouseRules.For(promo);

        var next = world.Events.Values
            .Where(e => e.Promotion == promo.Id && e.Status == EventStatus.Scheduled && e.Format == EventFormat.Tournament)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).FirstOrDefault();
        _next = next?.Id;
        if (next is not null)
        {
            NextName = next.Name;
            int days = next.Date.DayNumber - world.CurrentDate.DayNumber;
            NextWhen = $"{next.Date:dddd d MMMM yyyy}  ·  " + (days <= 0 ? "tonight" : days == 1 ? "tomorrow" : $"in {days} days");
            NextWhere = next.Venue ?? "";
            _date = next.Date.ToDateTime(TimeOnly.MinValue);
        }
        else if (!sameWorld || DateOnly.FromDateTime(_date) <= world.CurrentDate)
        {
            var due = OriginsShows.NextNightDue(world, promo);
            var earliest = world.CurrentDate.AddDays(7 * OriginsShows.LeadWeeks);
            _date = (due > earliest ? due : earliest).ToDateTime(TimeOnly.MinValue);
        }
        OnPropertyChanged(nameof(Date));

        // The belt this house's brackets crown.
        BeltName = ""; BeltHolder = ""; BeltLine = ""; BeltHolderId = 0; BeltFace = null;
        if (promo.Divisions.FirstOrDefault(d => d.Open && d.Title is not null)?.Title is { } tid && world.Get(tid) is { } title)
        {
            BeltName = title.Name;
            var reign = title.Lineage.LastOrDefault(r => r.LostOn is null);
            if (title.CurrentChampion is { } cid && world.Get(cid) is { } champ)
            {
                BeltHolderId = champ.Id.Value;
                BeltHolder = champ.FullName;
                BeltFace = new LazyFace(ready => Portraits.Thumb(champ, world, ready));
                BeltLine = reign is null ? "" : $"Won it {reign.WonOn:d MMM yyyy}" + (reign.Defenses > 0 ? $", and again {reign.Defenses}×" : "")
                    + ". The next bracket's winner takes it.";
            }
            else if (title.Lineage.LastOrDefault() is { LostOn: { } gone } lastReign && world.Get(lastReign.Champion) is { } former)
                BeltLine = $"Vacant since {gone:d MMM yyyy}: {former.FullName} " + (lastReign.Vacated ? "left it without losing it." : "lost it.") + " The next bracket's winner takes it.";
            else
                BeltLine = "Nobody has won it yet. The first bracket's winner takes it.";
        }

        Past.Clear();
        foreach (var e in world.Events.Values.Where(e => e.Promotion == promo.Id && e.Format == EventFormat.Tournament && e.Status == EventStatus.Completed)
                     .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id.Value))
        {
            var winner = world.Fighters.Values.FirstOrDefault(f => f.Accolades.Any(a => a.Date == e.Date && a.Title == $"Won {e.Name}"));
            Past.Add(new PastNight(e.Id.Value, e.Name, e.Date.ToString("d MMM yyyy"), winner?.FullName ?? "no champion"));
        }

        Raise();
        RefreshField();
        RefreshBoard();
    }

    private void Raise()
    {
        foreach (var p in new[] { nameof(HouseName), nameof(Logo), nameof(NightLine), nameof(CadenceLine), nameof(Rules), nameof(HasNext), nameof(NoNext),
                     nameof(NextName), nameof(NextWhen), nameof(NextWhere), nameof(BeltName), nameof(BeltHolder), nameof(BeltLine),
                     nameof(BeltHolderId), nameof(BeltFace), nameof(HasBeltHolder), nameof(HasPast) })
            OnPropertyChanged(p);
    }

    /// <summary>
    /// Who is fit to fight on the night and how the field would be drawn. Bookability is the sim's
    /// (<see cref="OriginsShows.CanBook"/>, <see cref="ForwardCalendar.IsCommitted"/>); the order is the sim's
    /// stated rule (promised men first, then the scouts' read; the next man is the alternate). The night
    /// draws the field itself; this is the preview.
    /// </summary>
    private void RefreshField()
    {
        Field.Clear(); LeftOut.Clear();
        if (_world?.Get(_world.PlayerPromotion) is not { } promo || !Applies) return;
        var w = _world;
        var date = DateOnly.FromDateTime(_date);
        int size = Math.Max(2, promo.Rules.TournamentSize);
        var fit = new List<(Fighter F, bool Promised, int Read)>();
        foreach (var f in w.RosterOf(promo.Id).Where(f => f.Status != FighterStatus.Retired && f.Sex == Sex.Male).OrderBy(f => f.Id.Value))
        {
            string? why = OriginsShows.CanBook(w, promo, f, date)
                ? ForwardCalendar.CommittedTo(w, f.Id, _next) is { } other ? $"Booked on {other.Name}" : null
                : WhyNot(w, f, date);
            bool promised = w.Promises.Any(p => p.Live && p.Fighter == f.Id && p.Promotion == promo.Id && p.Kind == PromiseKind.TitleShot);
            if (why is null) fit.Add((f, promised, FighterRow.Scouted(w, f)));
            else LeftOut.Add(new FieldEntry(f, w, "", why, CardTone.Bad));
        }
        var chosen = fit.OrderByDescending(x => x.Promised).ThenByDescending(x => x.Read).ThenBy(x => x.F.Id.Value).ToList();
        var seeds = chosen.Take(size).OrderByDescending(x => x.Read).ThenBy(x => x.F.Id.Value).ToList();
        for (int i = 0; i < seeds.Count; i++)
            Field.Add(new FieldEntry(seeds[i].F, w, $"{i + 1}", seeds[i].Promised ? "Promised a place: in ahead of the order" : "", seeds[i].Promised ? CardTone.Good : CardTone.Neutral));
        if (chosen.Count > size)
            Field.Add(new FieldEntry(chosen[size].F, w, "Alt", "Alternate: in if a winner can't go on", CardTone.Info));
        foreach (var spare in chosen.Skip(size + 1))
            LeftOut.Add(new FieldEntry(spare.F, w, "", "Fit; outside the top eight", CardTone.Neutral));

        FieldShort = seeds.Count < size;
        FieldHeadline = FieldShort ? $"{seeds.Count} of the {size} men a bracket needs" : (HasNext ? "The field as it stands" : "The field you'd draw");
        FieldLine = FieldShort
            ? $"On {date:d MMMM} you can field {seeds.Count} fit men. Sign more fighters, or pick a later night."
            : $"Seeded by your scouts' read of each man (never his true ability); a man you promised a place gets in first. "
              + "Seed 1 meets 8, 4 meets 5, 2 meets 7, 3 meets 6. The field is drawn again on the night: an injury before then changes it.";
        foreach (var p in new[] { nameof(FieldHeadline), nameof(FieldLine), nameof(FieldShort), nameof(HasLeftOut) }) OnPropertyChanged(p);
    }

    /// <summary>The plain reason a man can't be booked that night (the facts behind <see cref="OriginsShows.CanBook"/>).</summary>
    private static string WhyNot(World w, Fighter f, DateOnly date)
    {
        if (f.Status == FighterStatus.Injured || f.CurrentInjury is not null)
            return f.ReadyToFightOn is { } back ? $"Hurt: cleared to fight {back:d MMM}" : "Hurt";
        if (!f.IsMatchReady(date)) return f.ReadyToFightOn is { } ready ? $"Resting: ready {ready:d MMM}" : "Not ready that night";
        if (f.Contract is { FightsRemaining: <= 0 }) return "His deal is fought out: re-sign him first";
        if (Core.Sim.Contracts.DealWindow.OutOfDeal(w, f)) return "Out of deal: re-sign him first";
        return "Can't be booked that night";
    }

    private void RefreshBoard()
    {
        if (_world is null) { Board = null; return; }
        var id = _shownNight ?? (Past.Count > 0 ? new EventId(Past[0].Id) : (EventId?)null);
        Board = id is { } eid && _world.Get(eid) is { } ev ? BracketBoard.From(_world, ev) : null;
    }

    private void Schedule()
    {
        if (_world is null) return;
        var date = DateOnly.FromDateTime(_date);
        var ev = OriginsShows.ScheduleTournament(_world, _world.PlayerPromotion, date, out var reason);
        if (ev is null) { Error = reason ?? "That night can't go on the books."; return; }
        Error = "";
        Status = $"{ev.Name} is on the books for {ev.Date:dddd d MMMM}. The field is drawn from your roster on the night.";
        OnChanged?.Invoke();
    }

    private void Cancel()
    {
        if (_world is null || _next is not { } id || _world.Get(id) is not { } ev) return;
        if (Confirm is { } ask && !ask($"Cancel {ev.Name}?",
                $"{ev.Name} on {ev.Date:ddd d MMM yyyy} comes off the books. Nobody has been drawn yet, so no man loses a booking, "
                + "but the night earns nothing: no gate and no tournament. This can't be undone.",
                null, "Cancel the night"))
        { Status = $"{ev.Name} stays on the books."; return; }
        string name = ev.Name;
        new Core.Sim.Booking.PlayerBooking().CancelEvent(_world, ev);
        Error = "";
        Status = $"{name} is off the books.";
        OnChanged?.Invoke();
    }
}
