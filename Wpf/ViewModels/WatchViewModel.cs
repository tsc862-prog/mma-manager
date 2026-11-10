using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One line of the live broadcast — a bout intro, a round header, action, or the result.</summary>
public sealed class WatchLine
{
    public required string Text { get; init; }
    public required string Kind { get; init; }   // "Header" | "Round" | "Action" | "Result"

    /// <summary>Which bout this line belongs to (drives the "now in the cage" panel).</summary>
    public int BoutIndex { get; init; } = -1;

    /// <summary>The line split around the fighters' names, so each renders in their corner color.</summary>
    public IReadOnlyList<CornerSeg> Segments { get; init; } = Array.Empty<CornerSeg>();
}

/// <summary>
/// The bout currently in the cage: the walkout card (full-body shots, records,
/// billing), a pre-fight tale of the tape, the judges' card filling in round by
/// round, and the result block the broadcast reveals when it's over.
/// </summary>
public sealed class WatchBoutCard
{
    public required string RedName { get; init; }
    public required string BlueName { get; init; }
    public required string RedRecord { get; init; }
    public required string BlueRecord { get; init; }
    public required ImageSource? RedBody { get; init; }
    public required ImageSource? BlueBody { get; init; }
    public required string Billing { get; init; }
    public required string ResultText { get; init; }
    public required string Stars { get; init; }
    public required IReadOnlyList<TaleRow> Stats { get; init; }

    /// <summary>Pre-fight compare (age, frame, style, form) — shown until the result.</summary>
    public required IReadOnlyList<TaleRow> Tale { get; init; }

    /// <summary>Did the red/blue corner take it (drives the winner emphasis).</summary>
    public required bool RedWon { get; init; }
    public required bool BlueWon { get; init; }

    /// <summary>The full canonical judges' card (every completed round).</summary>
    public required IReadOnlyList<TaleRow> AllScores { get; init; }

    /// <summary>The rounds scored SO FAR — the broadcast adds to this as each round ends.</summary>
    public ObservableCollection<TaleRow> Scorecard { get; } = new();

    public void RevealScoresUpTo(int completedRounds)
    {
        while (Scorecard.Count < Math.Min(completedRounds, AllScores.Count))
            Scorecard.Add(AllScores[Scorecard.Count]);
    }
}

/// <summary>
/// One bout on the running order. The result is known from the start (the card
/// is pre-simulated) but stays hidden until the broadcast reaches it — no
/// spoilers scrolling ahead of the commentary.
/// </summary>
public sealed class WatchCardRow : ObservableObject
{
    public required string Order { get; init; }
    public required string Matchup { get; init; }
    public required string Billing { get; init; }
    public required string Result { get; init; }
    public required string Stars { get; init; }

    private bool _revealed;
    public bool Revealed { get => _revealed; set => Set(ref _revealed, value); }
}

/// <summary>
/// The broadcast booth: reveals a just-run card's play-by-play line by line, like
/// watching it live. The card is already simulated (real results); this just paces
/// the commentary out with a timer, with play/pause and skip.
/// </summary>
public sealed class WatchViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private readonly List<WatchLine> _script = new();
    private int _cursor;

    public WatchViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _timer.Tick += (_, _) => Reveal();
        PlayPauseCommand = new RelayCommand(TogglePlay, () => _script.Count > 0 && _cursor < _script.Count);
        SkipCommand = new RelayCommand(SkipToEnd, () => _script.Count > 0 && _cursor < _script.Count);
        RestartCommand = new RelayCommand(Restart, () => _script.Count > 0);
    }

    public ICommand PlayPauseCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand RestartCommand { get; }

    public string[] Speeds { get; } = { "Slow", "Normal", "Fast" };

    private string _speed = "Normal";
    public string Speed
    {
        get => _speed;
        set
        {
            if (!Set(ref _speed, value)) return;
            _timer.Interval = TimeSpan.FromMilliseconds(value switch { "Slow" => 1100, "Fast" => 280, _ => 650 });
        }
    }

    public ObservableCollection<WatchLine> Lines { get; } = new();

    private string _title = "Run one of your events to watch it live.";
    public string Title { get => _title; private set => Set(ref _title, value); }

    private bool _isPlaying;
    public bool IsPlaying { get => _isPlaying; private set { if (Set(ref _isPlaying, value)) OnPropertyChanged(nameof(PlayLabel)); } }
    public string PlayLabel => IsPlaying ? "Pause" : "Play";

    private bool _hasCard;
    public bool HasCard { get => _hasCard; private set => Set(ref _hasCard, value); }

    private readonly List<WatchBoutCard> _bouts = new();

    private WatchBoutCard? _currentBout;
    public WatchBoutCard? CurrentBout { get => _currentBout; private set => Set(ref _currentBout, value); }

    private bool _showResult;
    public bool ShowResult { get => _showResult; private set => Set(ref _showResult, value); }

    /// <summary>The card in billing order (main event on top) — results fill in as the broadcast reaches them.</summary>
    public ObservableCollection<WatchCardRow> CardRows { get; } = new();

    // Same rows keyed by broadcast order, so reveals can find their row.
    private readonly List<WatchCardRow> _rowByBout = new();

    private bool _cardOver;
    /// <summary>The broadcast has ended: the running order is now the full results sheet.</summary>
    public bool CardOver { get => _cardOver; private set => Set(ref _cardOver, value); }

    private ImageSource? _poster;
    /// <summary>Tonight's poster — the cage-side panel shows it until the first walkouts.</summary>
    public ImageSource? Poster { get => _poster; private set => Set(ref _poster, value); }

    /// <summary>Build the broadcast script from a card that has just been run with play-by-play.</summary>
    public void Load(World world, Event ev)
    {
        _timer.Stop();
        _script.Clear();
        _bouts.Clear();
        Lines.Clear();
        CardRows.Clear();
        _cursor = 0;
        CurrentBout = null;
        ShowResult = false;
        CardOver = false;
        Title = $"{ev.Name} — {ev.Date:ddd d MMM yyyy}";
        Poster = Art.PosterFactory.For(world, ev);

        // The cold open: a beat on the poster before the first walkouts. The
        // tease line tints the main-event names in their corner colors.
        var (mainRed, mainBlue) = ev.Card.Count > 0
            ? (world.Get(ev.Card[^1].RedCorner), world.Get(ev.Card[^1].BlueCorner)) : (null, null);
        WatchLine Intro(string kind, string text) => new()
        {
            Kind = kind, BoutIndex = -1, Text = text,
            Segments = CornerText.Segment(text, mainRed?.FullName ?? "??", mainBlue?.FullName ?? "??"),
        };
        string where = ev.Venue is { Length: > 0 } v ? $"live from {v}" : $"live in {FighterRow.Pretty(ev.Region.ToString())}";
        _script.Add(Intro("Header", $"🎙  Welcome to {ev.Name}, {where}!"));
        if (mainRed is not null && mainBlue is not null)
            _script.Add(Intro("Action", $"{ev.Card.Count} bout{(ev.Card.Count == 1 ? "" : "s")} tonight, and it all builds to the main event — {mainRed.FullName} against {mainBlue.FullName}."));

        _rowByBout.Clear();
        for (int i = 0; i < ev.Card.Count; i++)
        {
            var f = ev.Card[i];
            var (r, b) = (world.Get(f.RedCorner), world.Get(f.BlueCorner));
            string red = r?.FullName ?? "?";
            string blue = b?.FullName ?? "?";
            string slot = CardBilling.Slot(i, ev.Card.Count);
            WatchLine Line(string kind, string text) => new()
            {
                Kind = kind, BoutIndex = i, Text = text,
                Segments = CornerText.Segment(text, red, blue),
            };
            _bouts.Add(BuildCard(world, ev, f, r, b));
            _rowByBout.Add(new WatchCardRow
            {
                Order = $"{CardBilling.Position(i, ev.Card.Count)}",
                Matchup = $"{red} vs {blue}",
                Billing = slot + "  ·  " + FighterRow.Pretty(f.WeightClass.ToString()) + (f.IsTitleFight ? "  ·  ★ TITLE" : ""),
                Result = ShortResult(world, f),
                Stars = MatchRating.StarsFor(f),
            });

            _script.Add(Line("Header", $"{slot.ToUpperInvariant()} — {red} vs {blue}{(f.IsTitleFight ? "  ★ TITLE" : "")}"));

            if (f.Outcome?.PlayByPlay is { } pbp)
                foreach (var line in pbp)
                    _script.Add(Line(line.StartsWith("---") ? "Round" : "Action", line));

            _script.Add(Line("Result", $"{ResultLine(world, f)}  {MatchRating.StarsFor(f)}"));
        }

        // The visible sheet reads top-billed: main event first.
        foreach (var row in _rowByBout.AsEnumerable().Reverse()) CardRows.Add(row);

        HasCard = _script.Count > 0;
    }

    private static WatchBoutCard BuildCard(World world, Event ev, Fight f, Fighter? r, Fighter? b)
    {
        var rs = f.Outcome?.Stats.GetValueOrDefault(f.RedCorner) ?? new FightStatline();
        var bs = f.Outcome?.Stats.GetValueOrDefault(f.BlueCorner) ?? new FightStatline();
        string Time(int s) => $"{s / 60}:{s % 60:00}";

        // The pre-fight tale of the tape (shown until the result comes in).
        var tale = new List<TaleRow>();
        if (r is not null && b is not null)
        {
            int rAge = r.AgeOn(world.CurrentDate), bAge = b.AgeOn(world.CurrentDate);
            tale.Add(new TaleRow { Label = "Age", Red = rAge.ToString(), Blue = bAge.ToString(), Edge = 0 });
            tale.Add(new TaleRow { Label = "Height", Red = $"{r.HeightCm} cm", Blue = $"{b.HeightCm} cm", Edge = Math.Sign(b.HeightCm - r.HeightCm) });
            tale.Add(new TaleRow { Label = "Reach", Red = $"{r.ReachCm} cm", Blue = $"{b.ReachCm} cm", Edge = Math.Sign(b.ReachCm - r.ReachCm) });
            tale.Add(new TaleRow { Label = "Style", Red = FighterRow.Pretty(r.PrimaryStyle.ToString()), Blue = FighterRow.Pretty(b.PrimaryStyle.ToString()), Edge = 0 });
            tale.Add(new TaleRow { Label = "Form", Red = r.Momentum.ToString("+0;-0;0"), Blue = b.Momentum.ToString("+0;-0;0"), Edge = Math.Sign(b.Momentum - r.Momentum) });
        }

        // The canonical judges' card, one TaleRow per completed round.
        var scores = (f.Outcome?.RoundScores ?? new List<RoundScore>())
            .Select((s, i) => new TaleRow
            {
                Label = $"Round {i + 1}", Red = s.Red.ToString(), Blue = s.Blue.ToString(),
                Edge = Math.Sign(s.Blue - s.Red),
            })
            .ToList();

        return new WatchBoutCard
        {
            RedName = r?.FullName ?? "?",
            BlueName = b?.FullName ?? "?",
            RedRecord = r?.Record.ToString() ?? "",
            BlueRecord = b?.Record.ToString() ?? "",
            RedBody = r is null ? null : Art.Portraits.Body(r, world),
            BlueBody = b is null ? null : Art.Portraits.Body(b, world),
            Billing = FighterRow.Pretty(f.WeightClass.ToString())
                      + $"  ·  {f.ScheduledRounds} rounds"
                      + (f.IsTitleFight ? "  ·  TITLE FIGHT" : ""),
            ResultText = ResultLine(world, f),
            Stars = MatchRating.StarsFor(f),
            Tale = tale,
            AllScores = scores,
            RedWon = f.Outcome?.Winner is { } rw && r?.Id == rw,
            BlueWon = f.Outcome?.Winner is { } bw && b?.Id == bw,
            Stats = new[]
            {
                new TaleRow { Label = "Sig. strikes", Red = $"{rs.SignificantStrikesLanded}/{rs.SignificantStrikesAttempted}", Blue = $"{bs.SignificantStrikesLanded}/{bs.SignificantStrikesAttempted}", Edge = bs.SignificantStrikesLanded - rs.SignificantStrikesLanded },
                new TaleRow { Label = "Knockdowns", Red = rs.KnockdownsScored.ToString(), Blue = bs.KnockdownsScored.ToString(), Edge = bs.KnockdownsScored - rs.KnockdownsScored },
                new TaleRow { Label = "Takedowns", Red = $"{rs.TakedownsLanded}/{rs.TakedownsAttempted}", Blue = $"{bs.TakedownsLanded}/{bs.TakedownsAttempted}", Edge = bs.TakedownsLanded - rs.TakedownsLanded },
                new TaleRow { Label = "Sub attempts", Red = rs.SubmissionAttempts.ToString(), Blue = bs.SubmissionAttempts.ToString(), Edge = bs.SubmissionAttempts - rs.SubmissionAttempts },
                new TaleRow { Label = "Control", Red = Time(rs.ControlTimeSeconds), Blue = Time(bs.ControlTimeSeconds), Edge = bs.ControlTimeSeconds - rs.ControlTimeSeconds },
            },
        };
    }

    public void Start()
    {
        if (_script.Count == 0) return;
        IsPlaying = true;
        _timer.Start();
    }

    private void Reveal()
    {
        if (_cursor >= _script.Count) { _timer.Stop(); IsPlaying = false; return; }
        var line = _script[_cursor++];
        Lines.Add(line);

        // Keep the "now in the cage" panel and the running order in step.
        if (line.BoutIndex >= 0 && line.BoutIndex < _bouts.Count)
        {
            if (line.Kind == "Header") { CurrentBout = _bouts[line.BoutIndex]; ShowResult = false; }
            else if (line.Kind == "Round" && CurrentBout is { } cb)
            {
                // "--- Round N ---": rounds 1..N-1 are in the books — post their scores.
                if (TryParseRound(line.Text, out int n)) cb.RevealScoresUpTo(n - 1);
            }
            else if (line.Kind == "Result")
            {
                CurrentBout?.RevealScoresUpTo(int.MaxValue);
                ShowResult = true;
                if (line.BoutIndex < _rowByBout.Count) _rowByBout[line.BoutIndex].Revealed = true;
            }
        }

        if (_cursor >= _script.Count) CardOver = true;   // that's the card — full results sheet
    }

    private void TogglePlay()
    {
        if (IsPlaying) Pause();
        else Start();
    }

    /// <summary>Stop the broadcast clock (the window is closing, or the user hit pause).</summary>
    public void Pause()
    {
        _timer.Stop();
        IsPlaying = false;
    }

    public void SkipToEnd()
    {
        _timer.Stop();
        IsPlaying = false;
        while (_cursor < _script.Count) Lines.Add(_script[_cursor++]);
        if (_bouts.Count > 0) { CurrentBout = _bouts[^1]; ShowResult = true; }
        foreach (var bout in _bouts) bout.RevealScoresUpTo(int.MaxValue);
        foreach (var row in CardRows) row.Revealed = true;
        CardOver = _script.Count > 0;
    }

    private void Restart()
    {
        _timer.Stop();
        Lines.Clear();
        _cursor = 0;
        CurrentBout = null;
        ShowResult = false;
        CardOver = false;
        foreach (var bout in _bouts) bout.Scorecard.Clear();
        foreach (var row in CardRows) row.Revealed = false;
        Start();
    }

    /// <summary>Render-harness helpers: put a chosen bout on the cage-side panel after a skip.</summary>
    public int BoutWithScorecard => _bouts.FindLastIndex(b => b.AllScores.Count > 0);
    public void ShowBout(int index)
    {
        if (index < 0 || index >= _bouts.Count) return;
        CurrentBout = _bouts[index];
        ShowResult = true;
    }

    /// <summary>Pull N out of a "--- Round N ---" play-by-play marker.</summary>
    private static bool TryParseRound(string text, out int round)
    {
        round = 0;
        foreach (char c in text)
            if (char.IsAsciiDigit(c)) round = round * 10 + (c - '0');
        return round > 0;
    }

    /// <summary>Compact result for the running-order sheet ("Duarte — KO · R1 2:33").</summary>
    private static string ShortResult(World world, Fight f)
    {
        if (f.Outcome is not { } o) return "No result";
        if (o.Winner is not { } wid) return o.Method == FightMethod.NoContest ? "No Contest" : "Draw";
        string w = world.Get(wid)?.FullName ?? "?";
        return $"{w} — {Pretty(o.Method)} · R{o.EndRound} {o.EndTimeSeconds / 60}:{o.EndTimeSeconds % 60:00}";
    }

    private static string ResultLine(World world, Fight f)
    {
        if (f.Outcome is not { } o) return "No result.";
        if (o.Winner is not { } wid) return o.Method == FightMethod.NoContest ? "Result: No Contest." : "Result: Draw.";
        string w = world.Get(wid)?.FullName ?? "?";
        return $"Result: {w} wins by {Pretty(o.Method)} — R{o.EndRound} {o.EndTimeSeconds / 60}:{o.EndTimeSeconds % 60:00}";
    }

    private static string Pretty(FightMethod m) => m switch
    {
        FightMethod.Knockout => "KO",
        FightMethod.TechnicalKnockout => "TKO",
        FightMethod.SubmissionTap => "submission",
        FightMethod.TechnicalSubmission => "technical submission",
        FightMethod.UnanimousDecision => "unanimous decision",
        FightMethod.SplitDecision => "split decision",
        FightMethod.MajorityDecision => "majority decision",
        _ => m.ToString(),
    };
}
