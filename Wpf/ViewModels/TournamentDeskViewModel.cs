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
/// How a fought one-night tournament went, slot by slot, in the sim's own shape (<see cref="BracketBout"/>).
/// THE ONE PLACE the screen learns the bracket: it reads the bracket the sim saved with the night.
/// </summary>
public static class BracketNightReader
{
    /// <param name="Seeds">The field in seed order (index 0 = seed 1).</param>
    /// <param name="Alternate">The alternate, when he was used (an unused alternate leaves no trace on the card).</param>
    public sealed record Night(int Size, IReadOnlyList<FighterId> Seeds, FighterId? Alternate,
        IReadOnlyList<BracketBout> Bouts, FighterId? Champion, bool FinalDrawn);

    /// <summary>
    /// The night as the sim saved it (<see cref="Event.Bracket"/>), or null for a night that isn't a fought
    /// bracket. Seeds come off round one's slots (slot k holds seeds Order[2k] and Order[2k+1]); the champion
    /// is whoever the final sent through.
    /// </summary>
    public static Night? Read(World w, Event ev)
    {
        if (ev.Format != EventFormat.Tournament || ev.Status != EventStatus.Completed || ev.Bracket.Count == 0) return null;
        var first = ev.Bracket.Where(s => s.Round == 1).OrderBy(s => s.Slot).ToList();
        int size = first.Count * 2;
        if (size < 2 || first.Any(s => s.Red is null || s.Blue is null)) return null;
        var order = OneNightBracket.Order(size);
        var seeds = new FighterId[size];
        for (int k = 0; k < first.Count; k++) { seeds[order[2 * k]] = first[k].Red!.Value; seeds[order[2 * k + 1]] = first[k].Blue!.Value; }

        var outcomes = ev.Card.ToDictionary(f => f.Id, f => f.Outcome);
        var bouts = ev.Bracket.OrderBy(s => s.Round).ThenBy(s => s.Slot)
            .Select(s => new BracketBout(s.Round, s.Red, s.Blue, s.Fight,
                s.Fight is { } fid ? outcomes.GetValueOrDefault(fid) : null,
                s.Advanced, Enum.Parse<BracketAdvance>(s.How.ToString()), s.Withdrew))
            .ToList();
        var final = bouts[^1];
        bool altUsed = bouts.Any(b => b.How is BracketAdvance.AlternateForWithdrawal or BracketAdvance.AlternateAfterDraw);
        bool finalDrawn = final.Fight is not null && final.Advanced is null && final.Withdrew is null && final.Outcome?.Winner is null;
        return new Night(size, seeds, altUsed ? ev.BracketAlternate : null, bouts, final.Advanced, finalDrawn);
    }
}

/// <summary>
/// A fought tournament night drawn as a bracket (Origins): every slot of every round, walkovers and empty
/// slots included, read from <see cref="BracketNightReader"/>. Read-only.
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
        if (BracketNightReader.Read(w, ev) is not { } night) return null;
        var promo = w.Get(ev.Promotion);
        int rulesRounds = promo?.Rules.Fight?.Rounds ?? promo?.Rules.StandardRounds ?? 3;
        int total = 0;
        for (int n = night.Size; n > 1; n /= 2) total++;
        var seedOf = new Dictionary<FighterId, int>();
        for (int i = 0; i < night.Seeds.Count; i++) seedOf[night.Seeds[i]] = i + 1;

        string Short(FighterId? id) => id is { } x && w.Get(x) is { } f ? f.LastName : "?";

        BracketCorner Corner(FighterId? id, BracketBout b)
        {
            if (id is not { } fid || w.Get(fid) is not { } who)
                return new BracketCorner { Id = 0, Name = "Nobody came through", Seed = "–", Won = false, Out = true, Face = new LazyFace(_ => null) };
            bool won = b.Fight is not null && (b.Outcome?.Winner == fid || (b.How == BracketAdvance.JudgedDrawBetterSeed && b.Advanced == fid))
                       || b.Fight is null && b.Advanced == fid;
            string tag = b.Withdrew == fid ? "Could not go on" : fid == night.Alternate ? "Alternate" : "";
            return new BracketCorner
            {
                Id = fid.Value,
                Name = $"{who.FirstName} {who.LastName}",
                FullName = who.FullName,
                Seed = seedOf.TryGetValue(fid, out var sd) ? sd.ToString() : "A",
                Won = won,
                Out = !won,
                Tag = tag,
                Face = new LazyFace(ready => Portraits.Thumb(who, w, ready)),
            };
        }

        string How(BracketBout b)
        {
            if (b.Fight is null)
                return b.Advanced is null ? "Empty: nobody came through on either side" : $"Walkover: nobody was left to meet {Short(b.Advanced)}";
            if (b.Outcome is not { } o) return "";
            string clock = o.EndRound > rulesRounds ? $"OT {o.EndTimeSeconds / 60}:{o.EndTimeSeconds % 60:00}"
                : $"R{o.EndRound} {o.EndTimeSeconds / 60}:{o.EndTimeSeconds % 60:00}";
            return o.Winner is null ? $"Draw  ·  {clock}" : $"{FighterRow.Pretty(o.Method.ToString())}  ·  {clock}";
        }

        string Aside(BracketBout b, bool final) => b.How switch
        {
            BracketAdvance.JudgedDrawBetterSeed => $"{Short(b.Advanced)} goes through as the better seed",
            BracketAdvance.AlternateAfterDraw => $"Both men out at the limit; the alternate {Short(b.Advanced)} steps in",
            BracketAdvance.AlternateForWithdrawal => $"{Short(b.Withdrew)} could not go on; the alternate {Short(b.Advanced)} steps in",
            BracketAdvance.NobodyAdvanced when b.Fight is not null && final => "Drawn after overtime: no champion",
            BracketAdvance.NobodyAdvanced when b.Fight is not null && b.Withdrew is not null => $"{Short(b.Withdrew)} could not go on, and nobody was left to step in",
            BracketAdvance.NobodyAdvanced when b.Fight is not null => "Both men out at the limit, and nobody was left to step in",
            _ => "",
        };

        string RoundName(int r) => (total - r) switch { 0 => "Final", 1 => "Semi-finals", 2 => "Quarter-finals", _ => $"Round {r}" };

        var rounds = night.Bouts.GroupBy(b => b.Round).OrderBy(g => g.Key)
            .Select(g => new BracketRound(RoundName(g.Key), g.Select(b => new BracketBoutView
            {
                Red = Corner(b.Red, b),
                Blue = Corner(b.Blue, b),
                How = How(b),
                Aside = Aside(b, g.Key == total),
                EventId = ev.Id.Value,
            }).ToList())).ToList();

        var champ = night.Champion is { } cid ? w.Get(cid) : null;
        var finalBout = night.Bouts.LastOrDefault(b => b.Round == total);
        bool walkedOver = champ is not null && finalBout is { Fight: null } && finalBout.Advanced == champ.Id;
        int wins = champ is null ? 0 : night.Bouts.Count(b => b.Fight is not null && b.Outcome?.Winner == champ.Id);
        return new BracketBoard
        {
            Title = ev.Name,
            When = $"{ev.Date:dddd d MMMM yyyy}" + (ev.Venue is { } v ? $"  ·  {v}" : ""),
            Rounds = rounds,
            ChampionId = champ?.Id.Value ?? 0,
            ChampionName = champ?.FullName ?? "",
            ChampionLine = champ is null ? ""
                : $"{wins} win{(wins == 1 ? "" : "s")} in one night"
                  + (promo?.Rules.TournamentPrize is { Amount: > 0 } prize ? $", and {prize}." : ".")
                  + (walkedOver ? " Walked over in the final: nobody was left fit to meet him." : ""),
            ChampionBody = champ is null ? null : Portraits.Body(champ, w),
            // The sim's two ways a night ends without a champion (TournamentService.Run).
            Verdict = champ is not null ? ""
                : night.FinalDrawn ? "The final was fought to a standstill, overtime and all: no champion tonight, and the purse is split."
                : "Nobody was left fit to fight the final: no champion tonight, and no purse paid.",
        };
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
        ScheduleCommand = new RelayCommand(Schedule, () => _world is not null && !HasNext && !FieldShort);
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
    public string PickNightLine { get; private set; } = "";
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
    /// <summary>Why "Book the night" is off (the sim's refusal stays the backstop).</summary>
    public string ShortLine { get; private set; } = "";
    public bool HasShortLine => ShortLine.Length > 0;

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
        CadenceLine = $"Houses like this run a bracket {HouseRules.Cadence(promo)}: {HouseRules.Men(promo)} men need the months between to heal.";
        PickNightLine = $"Pick the night. Nobody is drawn until then: {HouseRules.FieldRule(promo)}.";
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
        else
        {
            BeltName = "No belt";
            BeltLine = "This house crowns nobody: a night's winner takes the purse and the headlines.";
        }

        Past.Clear();
        foreach (var e in world.Events.Values.Where(e => e.Promotion == promo.Id && e.Format == EventFormat.Tournament && e.Status == EventStatus.Completed)
                     .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id.Value))
        {
            var winner = BracketNightReader.Read(world, e)?.Champion is { } cid ? world.Get(cid) : null;
            Past.Add(new PastNight(e.Id.Value, e.Name, e.Date.ToString("d MMM yyyy"), winner is null ? "no champion" : $"won by {winner.FullName}"));
        }

        Raise();
        RefreshField();
        RefreshBoard();
    }

    private void Raise()
    {
        foreach (var p in new[] { nameof(HouseName), nameof(Logo), nameof(NightLine), nameof(CadenceLine), nameof(PickNightLine), nameof(Rules), nameof(HasNext), nameof(NoNext),
                     nameof(NextName), nameof(NextWhen), nameof(NextWhere), nameof(BeltName), nameof(BeltHolder), nameof(BeltLine),
                     nameof(BeltHolderId), nameof(BeltFace), nameof(HasBeltHolder), nameof(HasPast) })
            OnPropertyChanged(p);
    }

    /// <summary>
    /// Who is fit to fight on the night and how the field would be drawn: the sim's own preview
    /// (<see cref="Core.Sim.Booking.TournamentService.PreviewField"/>, the rule the night draws by). Everyone
    /// else on the roster is listed with the plain reason he is out. The night draws the field itself.
    /// </summary>
    private void RefreshField()
    {
        Field.Clear(); LeftOut.Clear();
        if (_world?.Get(_world.PlayerPromotion) is not { } promo || !Applies) return;
        var w = _world;
        var date = DateOnly.FromDateTime(_date);
        var preview = Core.Sim.Booking.TournamentService.PreviewField(w, promo, date, _next);
        int size = preview.Size;
        var seeds = preview.Seeds;
        for (int i = 0; i < seeds.Count; i++)
        {
            bool promised = preview.Promised.Contains(seeds[i].Id);
            Field.Add(new FieldEntry(seeds[i], w, $"{i + 1}", promised ? "Promised a place: in ahead of the order" : "", promised ? CardTone.Good : CardTone.Neutral));
        }
        if (preview.Alternate is { } alternate)
            Field.Add(new FieldEntry(alternate, w, "Alt", "Alternate: in if a winner can't go on", CardTone.Info));
        var drawn = seeds.Select(f => f.Id).ToHashSet();
        if (preview.Alternate is { } a2) drawn.Add(a2.Id);
        var spares = new List<FieldEntry>();
        foreach (var f in w.RosterOf(promo.Id).Where(f => f.Status != FighterStatus.Retired && f.Sex == Sex.Male && !drawn.Contains(f.Id)).OrderBy(f => f.Id.Value))
        {
            if (!OriginsShows.CanBook(w, promo, f, date)) LeftOut.Add(new FieldEntry(f, w, "", WhyNot(w, f, date), CardTone.Bad));
            else if (ForwardCalendar.CommittedTo(w, f.Id, _next) is { } other) LeftOut.Add(new FieldEntry(f, w, "", $"Booked on {other.Name}", CardTone.Bad));
            else spares.Add(new FieldEntry(f, w, "", $"Fit; outside the top {HouseRules.Men(promo)}", CardTone.Neutral));
        }
        foreach (var s in spares) LeftOut.Add(s);

        FieldShort = !preview.Full;
        FieldHeadline = FieldShort ? $"{seeds.Count} of the {size} men a bracket needs" : (HasNext ? "The field as it stands" : "The field you'd draw");
        FieldLine = FieldShort
            ? $"On {date:d MMMM} you can field {seeds.Count} fit men. Sign more fighters, or pick a later night."
            : $"Seeded by your scouts' read of each man (never his true ability); a man you promised a place gets in first. "
              + "Seed 1 meets 8, 4 meets 5, 2 meets 7, 3 meets 6. The field is drawn again on the night: an injury before then changes it.";
        ShortLine = FieldShort && !HasNext ? $"Can't book {date:d MMMM} yet: {seeds.Count} of {size} men are fit to fight that night." : "";
        foreach (var p in new[] { nameof(FieldHeadline), nameof(FieldLine), nameof(FieldShort), nameof(HasLeftOut), nameof(ShortLine), nameof(HasShortLine) }) OnPropertyChanged(p);
        CommandManager.InvalidateRequerySuggested();
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
        BracketBoard? Of(EventId? id) => id is { } eid && _world.Get(eid) is { } ev ? BracketBoard.From(_world, ev) : null;
        Board = Of(_shownNight) ?? Of(Past.Count > 0 ? new EventId(Past[0].Id) : null);
        if (Board is null) ShowBracket = false;
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
