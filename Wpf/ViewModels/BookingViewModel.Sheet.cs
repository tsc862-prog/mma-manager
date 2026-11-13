using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

// The fight sheet: the comparison for one pairing, filling the bill's side of
// the desk while the picker (or a proposal list) stays in the rail. Sources:
// the record and the cards (public), your own contracts, the rankings, the
// scouts' reads (MatchmakingAdvisor / FighterRow, never the truth), the
// matchmaker's reasons (PlayerBooking.Suggest) and the night with and without
// the bout (PlayerBooking.Project / ProjectWith). Read-only.
public sealed partial class BookingViewModel
{
    private enum SheetSource { Pick, Replace, Proposal, Bulk }

    /// <summary>What the sheet is showing, by id (rebuilt from the world on every refresh).</summary>
    private sealed record SheetAsk(SheetSource Source, FighterId Red, FighterId Blue, BoutSlot Slot, int Rounds, bool Title,
        FightId? Replacing = null, FighterId? Incoming = null, ProposalCard? Card = null);

    private SheetAsk? _ask;

    private void InitSheet()
    {
        LookCloserCommand = new RelayCommand<ProposalCard>(LookCloser);
        PreviewReplaceCommand = new RelayCommand<LadderEntry>(PreviewReplace);
        CloseSheetCommand = new RelayCommand(CloseSheet);
        SheetActionCommand = new RelayCommand(SheetAction, () => Sheet?.HasAction == true);
    }

    public ICommand LookCloserCommand { get; private set; } = null!;
    public ICommand PreviewReplaceCommand { get; private set; } = null!;
    public ICommand CloseSheetCommand { get; private set; } = null!;
    public ICommand SheetActionCommand { get; private set; } = null!;

    private MatchupSheet? _sheet;
    /// <summary>The fight sheet on the bill's side of the desk (null: the bill shows).</summary>
    public MatchupSheet? Sheet
    {
        get => _sheet;
        private set
        {
            if (!Set(ref _sheet, value)) return;
            OnPropertyChanged(nameof(HasSheet));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool HasSheet => _sheet is not null;

    private bool _sheetTable;
    /// <summary>The sheet's Story / Table toggle (the dense tale of the tape).</summary>
    public bool ShowSheetTable { get => _sheetTable; set { if (Set(ref _sheetTable, value)) OnPropertyChanged(nameof(ShowSheetStory)); } }
    public bool ShowSheetStory { get => !_sheetTable; set => ShowSheetTable = !value; }

    private void CloseSheet()
    {
        if (_ask?.Source == SheetSource.Pick) { SelectedBlue = null; RebuildLadder(); }
        _ask = null;
        Sheet = null;
    }

    /// <summary>A suggestion's "Look closer": the sheet for that pairing, its Book button the suggestion's.</summary>
    private void LookCloser(ProposalCard? c)
    {
        if (c is null) return;
        var s = c.Proposal;
        _ask = new SheetAsk(_rail == DeskRail.Proposals ? SheetSource.Bulk : SheetSource.Proposal,
            s.Red, s.Blue, c.Slot, s.Rounds, s.TitleFight, Card: c);
        RefreshSheet();
    }

    /// <summary>Change a corner: preview the bout with this fighter in it before swapping.</summary>
    private void PreviewReplace(LadderEntry? e)
    {
        if (_world is null || SelectedEvent is null || e is null || _replaceFight is not { } fid) return;
        var fight = SelectedEvent.Model.Card.FirstOrDefault(f => f.Id == fid);
        if (fight is null) return;
        var red = _replaceBlue ? fight.RedCorner : e.FighterId;
        var blue = _replaceBlue ? e.FighterId : fight.BlueCorner;
        _ask = new SheetAsk(SheetSource.Replace, red, blue, BoutSlot.MainCard, fight.ScheduledRounds, fight.IsTitleFight,
            Replacing: fid, Incoming: e.FighterId);
        RefreshSheet();
        RebuildLadder();
    }

    private void SheetAction()
    {
        var a = _ask;
        if (a is null) return;
        switch (a.Source)
        {
            case SheetSource.Pick: AddBout(); break;
            case SheetSource.Proposal: BookProposal(a.Card); break;
            case SheetSource.Replace when a.Incoming is { } inc:
                ReplaceWith(inc);
                break;
        }
        if (_rail != DeskRail.Picker && _rail != DeskRail.Proposals) { _ask = null; Sheet = null; }
    }

    /// <summary>The picker's pairing changed (both corners chosen, or the rounds or the belt).</summary>
    private void SyncPickSheet()
    {
        if (_world is null || SelectedEvent is null) return;
        if (SelectedRed is { } r && SelectedBlue is { } b && IsPickingSlot)
            _ask = new SheetAsk(SheetSource.Pick, r.Id, b.Id, _pickSlot ?? _nextSlot, SelectedRounds, IsTitleBout);
        else if (_ask?.Source == SheetSource.Pick) _ask = null;
        RefreshSheet();
    }

    /// <summary>Rebuild the sheet from the world (after any refresh), or drop it if its pairing is gone.</summary>
    private void RefreshSheet()
    {
        if (_ask is null || _world is null || SelectedEvent is null) { Sheet = null; return; }
        bool live = _ask.Source switch
        {
            SheetSource.Pick or SheetSource.Replace => _rail == DeskRail.Picker,
            SheetSource.Proposal => true,
            _ => _rail == DeskRail.Proposals,
        };
        // A proposal that got booked (or whose fighter did) is no longer a proposal.
        var card = SelectedEvent.Model.Card;
        if (_ask.Source is SheetSource.Proposal or SheetSource.Bulk
            && card.Any(f => f.RedCorner == _ask.Red || f.BlueCorner == _ask.Red || f.RedCorner == _ask.Blue || f.BlueCorner == _ask.Blue))
            live = false;
        if (!live || _world.Get(_ask.Red) is null || _world.Get(_ask.Blue) is null) { _ask = null; Sheet = null; return; }
        Sheet = BuildSheet(_ask);
    }

    // === The fight index: every completed bout, once per world state ==============

    private Dictionary<FightId, (Fight, Event)>? _fightIndex;
    private (World?, DateOnly, int) _fightIndexKey;

    private Dictionary<FightId, (Fight, Event)> FightIndex()
    {
        var w = _world!;
        var key = (w, w.CurrentDate, w.Events.Count);
        if (_fightIndex is not null && _fightIndexKey == key) return _fightIndex;
        var map = new Dictionary<FightId, (Fight, Event)>();
        foreach (var e in w.Events.Values)
        {
            if (e.Status != EventStatus.Completed) continue;
            foreach (var f in e.Card) if (f.Outcome is not null) map[f.Id] = (f, e);
        }
        _fightIndexKey = key;
        return _fightIndex = map;
    }

    /// <summary>His bouts on file, oldest first (the record the public can read).</summary>
    private List<(Fight Fight, Event Event)> HistoryOf(Fighter f)
    {
        var index = FightIndex();
        var list = new List<(Fight, Event)>();
        foreach (var id in f.BoutHistory)
            if (index.TryGetValue(id, out var x)) list.Add(x);
        list.Sort((a, b) => a.Item2.Date.CompareTo(b.Item2.Date));
        return list;
    }

    private static bool IsKo(FightMethod m) => m is FightMethod.Knockout or FightMethod.TechnicalKnockout;
    private static bool IsSub(FightMethod m) => m is FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;
    private static string MethodWord(FightMethod m) => IsKo(m) ? "KO/TKO" : IsSub(m) ? "submission"
        : m is FightMethod.Disqualification ? "DQ" : m is FightMethod.Draw ? "draw" : m is FightMethod.NoContest ? "no contest" : "decision";

    /// <summary>The last five results as chips, oldest first.</summary>
    private List<FormChip> FormOf(Fighter f, List<(Fight Fight, Event Event)> history)
    {
        var w = _world!;
        var chips = new List<FormChip>();
        foreach (var (fight, ev) in history.TakeLast(5))
        {
            var o = fight.Outcome!;
            var opp = fight.RedCorner == f.Id ? fight.BlueCorner : fight.RedCorner;
            string oppName = w.Get(opp) is { } of ? $"{of.FirstName} {of.LastName}" : "?";
            string res = fight.OverturnedNc || o.Method == FightMethod.NoContest ? "NC" : o.Winner == f.Id ? "W" : o.Winner is null ? "D" : "L";
            var tone = res == "W" ? CardTone.Good : res == "L" ? CardTone.Bad : CardTone.Neutral;
            string verb = res switch { "W" => "Beat", "L" => "Lost to", "D" => "Drew with", _ => "No contest with" };
            string how = res is "W" or "L" ? $" by {MethodWord(o.Method)}{(IsKo(o.Method) || IsSub(o.Method) ? $" in round {o.EndRound}" : "")}" : "";
            chips.Add(new FormChip(res, tone, $"{verb} {oppName}{how}\n{ev.Name}, {ev.Date:d MMM yyyy}{(fight.IsTitleFight ? " · title fight" : "")}"));
        }
        return chips;
    }

    // === Building the sheet ===========================================================

    private MatchupSheet BuildSheet(SheetAsk a)
    {
        var w = _world!;
        var ev = SelectedEvent!.Model;
        var r = w.Get(a.Red)!;
        var b = w.Get(a.Blue)!;
        var appraisal = _advisor.Appraise(w, r.Id, b.Id);
        var rh = HistoryOf(r);
        var bh = HistoryOf(b);
        var host = Core.Sim.Geography.WorldMap.CityOf(w, ev);
        var division = w.Get(w.PlayerPromotion)?.Divisions.FirstOrDefault(d => d.Matches(r));
        var title = division?.Title is { } tid ? w.Get(tid) : null;
        bool titleFight = a.Source == SheetSource.Replace
            ? a.Title && title is not null && (title.CurrentChampion is not { } champ || champ == r.Id || champ == b.Id)
            : a.Title;
        int rounds = a.Rounds;
        var proposal = MatchmakerPick(ev, a.Slot, r.Id, b.Id);

        var red = CornerOf(r, b, true, rh, bh, host, titleFight, title);
        var blue = CornerOf(b, r, false, bh, rh, host, titleFight, title);

        // The kicker and the one line on why it matters.
        string divLabel = FighterRow.DivisionLabel(r.WeightClass, r.Sex);
        string slotWord = a.Source == SheetSource.Replace ? SlotLabelOf(ev, a.Replacing) : a.Slot switch
        {
            BoutSlot.MainEvent => "MAIN EVENT", BoutSlot.CoMain => "CO-MAIN", BoutSlot.MainCard => "MAIN CARD", _ => "PRELIM",
        };
        string kicker = $"{slotWord} · {(titleFight ? $"{divLabel} title" : divLabel).ToUpperInvariant()} · {rounds} ROUNDS";
        string headline = proposal?.Reason
            ?? (appraisal.Stakes == Stakes.Mismatch ? "A mismatch on our reads: the favourite should run through this."
                : $"{char.ToUpperInvariant(appraisal.Summary[0])}{appraisal.Summary[1..]}.");

        var tags = new List<DeskTag>();
        if (titleFight) tags.Add(new DeskTag(title?.CurrentChampion is null ? "For the vacant belt" : "Title fight", CardTone.Warn));
        else if (appraisal.Stakes == Stakes.TitleEliminator) tags.Add(new DeskTag("Eliminator", CardTone.Info));
        if (Core.Sim.Living.Storylines.RivalryBetween(w, r.Id, b.Id) is { Intensity: >= 20 } feud)
            tags.Add(new DeskTag($"Bad blood · heat {feud.Intensity}", CardTone.Bad));
        int met = r.BoutHistory.Intersect(b.BoutHistory).Count();
        if (met == 1) tags.Add(new DeskTag("Rematch", CardTone.Warn));
        else if (met >= 2) tags.Add(new DeskTag(met == 2 ? "Trilogy" : $"Meeting no. {met + 1}", CardTone.Warn));
        foreach (var f in new[] { r, b }.Where(f => host is { } h && f.HomeCity == h)) tags.Add(new DeskTag($"{f.LastName} at home", CardTone.Good));
        tags.Add(new DeskTag(appraisal.Competitiveness >= 70 ? "Toss-up on our reads" : appraisal.Competitiveness >= 45 ? "Competitive"
            : appraisal.Competitiveness >= 25 ? "Clear favourite" : "Lopsided", appraisal.Stakes == Stakes.Mismatch ? CardTone.Bad : CardTone.Neutral));

        string matchmaker = $"On what we've seen, {r.LastName} reads {ReadWords.Read(FighterRow.Scouted(w, r))} and {b.LastName} {ReadWords.Read(FighterRow.Scouted(w, b))}: "
                            + (appraisal.Competitiveness >= 70 ? "a toss-up." : appraisal.Competitiveness >= 45 ? "competitive, with an edge." : appraisal.Competitiveness >= 25 ? "one is a clear favourite." : "lopsided.")
                            + $" Appeal {appraisal.Appeal} of 100: "
                            + (appraisal.Appeal >= 70 ? "a fight people will pay for." : appraisal.Appeal >= 50 ? "a solid sell." : appraisal.Appeal >= 35 ? "fine for the undercard." : "hard to sell on its own.")
                            + (proposal is null ? MatchmakerWouldnt(r, b, title) : "");

        // The night with this bout on the card.
        var (night, nightNote, terms, risks) = NightOf(ev, a, r, b, rounds, titleFight);

        return new MatchupSheet
        {
            Red = red, Blue = blue, Kicker = kicker, Headline = headline, Tags = tags,
            Matchmaker = matchmaker, Competitiveness = appraisal.Competitiveness, Appeal = appraisal.Appeal,
            Between = Between(r, b, rh, bh),
            Stakes = StakesOf(ev, a, r, b, appraisal, titleFight, title, rounds),
            Night = night, NightNote = nightNote, Terms = terms, Risks = risks,
            Tape = TapeRows(r, b, rh, bh),
            ActionText = a.Source switch
            {
                SheetSource.Pick or SheetSource.Proposal => "Book this fight",
                SheetSource.Replace => $"Swap {w.Get(a.Incoming!.Value)?.LastName} in",
                _ => "",
            },
            CanEditTerms = a.Source == SheetSource.Pick,
        };
    }

    private string SlotLabelOf(Event ev, FightId? fid)
    {
        int i = fid is { } id ? ev.Card.FindIndex(f => f.Id == id) : -1;
        int n = ev.Card.Count;
        return i == n - 1 ? "MAIN EVENT" : i == n - 2 ? "CO-MAIN" : n - i > (_projection?.MainCardOnTheNight ?? n) ? "PRELIM" : "MAIN CARD";
    }

    // The matchmaker's own read of this pairing (PlayerBooking.Suggest), once per card state and slot.
    private Dictionary<(FighterId, FighterId), BoutProposal>? _pairs;
    private (object?, BoutSlot) _pairsKey;

    private BoutProposal? MatchmakerPick(Event ev, BoutSlot slot, FighterId a, FighterId b)
    {
        var facts = Facts();
        if (_pairs is null || !ReferenceEquals(_pairsKey.Item1, facts) || _pairsKey.Item2 != slot)
        {
            _pairs = new();
            foreach (var p in _booking.Suggest(_world!, ev, slot, 100_000)) _pairs[(p.Red, p.Blue)] = p;
            _pairsKey = (facts, slot);
        }
        return _pairs.TryGetValue((a, b), out var x) ? x : _pairs.TryGetValue((b, a), out var y) ? y : null;
    }

    // Why the matchmaker wouldn't offer this pairing himself (the same rules Suggest keeps).
    private string MatchmakerWouldnt(Fighter r, Fighter b, Title? title)
    {
        var w = _world!;
        bool champ = title?.CurrentChampion is { } c && (c == r.Id || c == b.Id);
        if (champ) return " He'd only put a champion in a title fight.";
        if (r.Camp is { } room && room == b.Camp) return " Teammates: he'd only put them in the same cage for a belt.";
        if (Core.Sim.Living.Bonds.WontFight(w, r.Id, b.Id)) return " They're close: he'd only make them fight for a belt.";
        return "";
    }

    private CornerSheet CornerOf(Fighter f, Fighter opp, bool isRed, List<(Fight Fight, Event Event)> history,
        List<(Fight Fight, Event Event)> oppHistory, CityId? host, bool titleFight, Title? title)
    {
        var w = _world!;
        var ev = SelectedEvent!.Model;
        var x = FactsFor(f.Id);
        var st = _advisor.StandingOf(w, f.Id);
        string divLabel = FighterRow.DivisionLabel(f.WeightClass, f.Sex);
        string standing = f.LocalTalent ? "Local club fighter"
            : st.Kind == Standing.Champion ? $"{divLabel} champion"
            : st.Kind == Standing.Ranked ? $"#{st.Rank} at {divLabel}" : $"Unranked at {divLabel}";

        // How he wins, how he loses.
        var rec = f.Record;
        int wins = Math.Max(1, rec.WinsByKo + rec.WinsBySubmission + rec.WinsByDecision);
        int lossKo = 0, lossSub = 0, lossDec = 0, lossOther = 0;
        foreach (var (fight, _) in history)
        {
            var o = fight.Outcome!;
            if (fight.OverturnedNc || o.Loser != f.Id) continue;
            if (IsKo(o.Method)) lossKo++; else if (IsSub(o.Method)) lossSub++;
            else if (o.Method is FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision) lossDec++;
            else lossOther++;
        }
        string winsText = rec.Wins == 0 ? "No pro wins yet"
            : $"Wins: {rec.WinsByKo} KO/TKO · {rec.WinsBySubmission} sub · {rec.WinsByDecision} decision";
        int stopped = lossKo + lossSub;
        string lossesText = rec.Losses == 0 ? "Never beaten"
            : stopped == 0 ? $"Losses: {rec.Losses}, never stopped on file"
            : $"Losses: {Join(new[] { lossKo > 0 ? $"{lossKo} KO/TKO" : null, lossSub > 0 ? $"{lossSub} sub" : null, lossDec > 0 ? $"{lossDec} decision" : null, lossOther > 0 ? $"{lossOther} other" : null })}"
              + (lossKo + lossSub + lossDec + lossOther < rec.Losses ? " on file" : "");

        // Readiness: today, on the card's date.
        string ready; CardTone readyTone;
        if (f.LocalTalent) { ready = "A local from the club scene"; readyTone = CardTone.Neutral; }
        else if (x is { Ready: true }) { ready = $"Ready for {ev.Date:d MMM}"; readyTone = CardTone.Good; }
        else if (x is { OnCard: true } && f.IsMatchReady(ev.Date) && _ask?.Source == SheetSource.Replace)
        { ready = $"Stays in this bout, ready for {ev.Date:d MMM}"; readyTone = CardTone.Good; }
        else { ready = x?.Why is { Length: > 0 } why ? why : "Not cleared for this date"; readyTone = CardTone.Bad; }
        string lastFought = x?.LastFought is { } lf ? $"Last fought {lf:d MMM yyyy} ({AgoText(x.IdleDays)})" : "No pro fight on file yet";

        var facts = new List<SheetLine>();
        string from = f.HomeCity is { } hc && w.Get(hc) is { } city ? $"{city.Name}, {w.Get(city.Country)?.Name ?? FighterRow.Pretty(f.Nationality.ToString())}"
            : w.Get(f.Country)?.Name ?? FighterRow.Pretty(f.Nationality.ToString());
        bool atHome = host is { } h && f.HomeCity == h;
        facts.Add(new SheetLine("Age", $"{f.AgeOn(w.CurrentDate)} · from {from}" + (atHome ? " · fighting at home" : x?.Home == true ? " · a local draw in this region" : ""),
            atHome ? CardTone.Good : CardTone.Neutral));
        facts.Add(new SheetLine("Style", $"{FighterRow.Pretty(f.PrimaryStyle.ToString())} · {FighterRow.Pretty(f.Stance.ToString()).ToLowerInvariant()} · {f.HeightCm} cm, {f.ReachCm} cm reach"));
        if (!f.LocalTalent)
        {
            string camp = f.Camp is { } cid && w.Get(cid) is { } cmp ? $"Trains at {cmp.Name}" + (f.CampStipend ? " (your stipend)" : "") : "No camp on file";
            facts.Add(new SheetLine("Camp", $"{camp} · {Core.Sim.Generation.Personality.Label(f.Disposition).ToLowerInvariant()}"));
            var draw = new List<string> { $"{Cap(DrawWord(f.Intangibles.Popularity.Value))} (popularity {f.Intangibles.Popularity.Value})" };
            if (f.StarPower) draw.Add("star power");
            if (Core.Sim.Org.Franchise.IsFace(w, f)) draw.Add("the face of the company");
            facts.Add(new SheetLine("Draw", string.Join(" · ", draw), f.Intangibles.Popularity.Value >= 60 ? CardTone.Good : CardTone.Neutral));
            facts.Add(new SheetLine("Form", f.Momentum >= 20 ? $"Rising (momentum {f.Momentum})" : f.Momentum <= -20 ? $"Sliding (momentum {f.Momentum})" : $"Steady (momentum {f.Momentum})",
                f.Momentum >= 20 ? CardTone.Good : f.Momentum <= -20 ? CardTone.Bad : CardTone.Neutral));
        }

        int read = FighterRow.Scouted(w, f);
        string scout = f.LocalTalent ? "Unknown: the scouts have only the club record" : new FighterRow(f, w, FightIndex()).ScoutingLine;

        var (winDoes, lossRisks) = StakesFor(f, opp, st, _advisor.StandingOf(w, opp.Id), titleFight, title);

        return new CornerSheet
        {
            Id = f.Id.Value, IsRed = isRed, Name = $"{f.FirstName} {f.LastName}",
            Nickname = f.Nickname is { Length: > 0 } n ? $"“{n}”" : "",
            Body = Portraits.Body(f, w),
            Standing = standing, IsChampion = st.Kind == Standing.Champion, Record = rec.ToString(),
            HasWins = rec.Wins > 0,
            KoWidth = new GridLength(rec.WinsByKo, GridUnitType.Star),
            SubWidth = new GridLength(rec.WinsBySubmission, GridUnitType.Star),
            DecWidth = new GridLength(rec.WinsByDecision, GridUnitType.Star),
            WinsText = winsText, LossesText = lossesText,
            Form = FormOf(f, history), StreakText = StreakText(rec.CurrentStreak) is { Length: > 0 } s ? s : "",
            Facts = facts,
            Readiness = $"{ready} · {lastFought}", ReadinessTone = readyTone,
            ReadText = f.LocalTalent ? "Unscouted" : ReadWords.Read(read), ScoutText = scout,
            WinDoes = winDoes, LossRisks = lossRisks,
            IsYours = f.Contract?.Promotion == w.PlayerPromotion && !f.LocalTalent,
            Contract = ContractLines(f, x, titleFight),
        };
    }

    private static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    private static string Join(IEnumerable<string?> parts) => string.Join(", ", parts.Where(p => p is not null));

    // What a win does for him, what a loss risks: from the belt, the rankings, the streak.
    private (string Win, string Loss) StakesFor(Fighter f, Fighter opp, FighterStanding me, FighterStanding them, bool titleFight, Title? title)
    {
        if (f.LocalTalent) return ("A scalp off the club scene would make his name locally.", "Nothing much: he's the underdog.");
        var w = new List<string>();
        var l = new List<string>();
        int streak = f.Record.CurrentStreak;
        if (titleFight && title is not null)
        {
            if (me.Kind == Standing.Champion)
            {
                int defenses = title.Lineage.LastOrDefault(r => r.Champion == f.Id && r.LostOn is null)?.Defenses ?? 0;
                w.Add($"keeps the belt (defence no. {defenses + 1})");
                l.Add("loses the belt");
            }
            else
            {
                w.Add(title.CurrentChampion is null ? "wins the vacant belt" : "takes the belt");
                l.Add("goes back to the queue");
            }
        }
        else if (me is { Kind: Standing.Ranked, Rank: <= 5 } && them is { Kind: Standing.Ranked, Rank: <= 5 })
        {
            w.Add("should be next in line for the title");
            l.Add("drops out of the title picture");
        }
        else if (me.Kind == Standing.Ranked && them.Kind == Standing.Unranked)
        {
            w.Add($"holds his place at #{me.Rank}");
            l.Add($"a loss to an unranked fighter could cost the #{me.Rank} spot");
        }
        else if (them.Kind == Standing.Ranked && (me.Kind == Standing.Unranked || (me.Rank ?? 99) > (them.Rank ?? 0)))
        {
            w.Add($"beating the #{them.Rank} should lift him up the rankings");
            l.Add("little lost: he was the lower-ranked man");
        }
        if (streak >= 2) { w.Add($"makes it {streak + 1} straight"); l.Add($"ends a {streak}-fight win streak"); }
        else if (streak <= -2) { w.Add($"snaps a {-streak}-fight skid"); l.Add($"a {Ordinal(1 - streak)} straight loss"); }
        if (f.Record.TotalBouts == 0) w.Add("a winning debut");
        if (f.Contract is { FightsRemaining: <= 1 } && f.Contract.Promotion == _world!.PlayerPromotion)
            w.Add("his price goes up at the table");
        string Say(List<string> xs, string none) => xs.Count == 0 ? none : Cap(string.Join("; ", xs)) + ".";
        return (Lifers.Gendered(f, Say(w, "A win keeps him moving.")), Lifers.Gendered(f, Say(l, "A setback, nothing worse.")));
    }

    // Your fighter: the money, the deal, and what this booking does to his mood.
    private List<SheetLine> ContractLines(Fighter f, FighterFacts? x, bool titleFight)
    {
        var w = _world!;
        var lines = new List<SheetLine>();
        if (f.Contract is not { } c || c.Promotion != w.PlayerPromotion || f.LocalTalent) return lines;
        var ev = SelectedEvent!.Model;
        lines.Add(new SheetLine("This fight", $"{c.PursePerFight} to show, {c.WinBonus} more to win"));
        lines.Add(c.FightsRemaining <= 1
            ? new SheetLine("Deal", "Last fight on his deal: after this bout the contract is up and rivals can bid", CardTone.Warn)
            : new SheetLine("Deal", $"{c.FightsRemaining} fights left (this is one of them)"));
        if (c.PpvPoints > 0)
            lines.Add(new SheetLine("PPV points", ev.Tier >= EventTier.Ppv ? $"{c.PpvPoints} point{(c.PpvPoints == 1 ? "" : "s")}: a share of this card's pay-per-view take"
                : $"{c.PpvPoints} point{(c.PpvPoints == 1 ? "" : "s")}, but this card isn't a pay-per-view"));
        if (AskText.Bond(w, f) is { Length: > 0 } bond) lines.Add(new SheetLine("Bond", bond, x?.Grievance is null ? CardTone.Good : CardTone.Warn));
        if (x?.Grievance is { } g)
        {
            var fix = g.Kind switch
            {
                Lifers.GrievanceKind.Benched => new SheetLine("This booking", "Fixes it: it gets him off the shelf.", CardTone.Good),
                Lifers.GrievanceKind.PassedOver => titleFight
                    ? new SheetLine("This booking", "Fixes it: this is the title shot he's owed.", CardTone.Good)
                    : new SheetLine("This booking", "Doesn't fix it: he wants the title shot, not this.", CardTone.Warn),
                Lifers.GrievanceKind.Skid => new SheetLine("This booking", "A win would fix it; another loss makes it worse.", CardTone.Info),
                Lifers.GrievanceKind.Underpaid => new SheetLine("This booking", "Doesn't fix it: he wants a raise.", CardTone.Warn),
                _ => null,
            };
            if (fix is not null) lines.Add(fix with { Text = Lifers.Gendered(f, fix.Text) });
        }
        else if (x is { NeedsFight: true })
            lines.Add(new SheetLine("This booking", $"Gets him back in there after {x.ShelfDays / 30} months unused.", CardTone.Good));
        // PoachWarned now means a rival lodged a real offer on this deal (OfferMarket).
        if (MmaManager.Core.Sim.Contracts.OfferMarket.LiveFor(_world!, f.Id) is { } live)
        {
            string rival = _world!.Get(live.From)?.Name ?? "A rival";
            lines.Add(new SheetLine("Rivals", live.State == RivalOfferState.LetGo
                ? $"You let him go: he signs with {rival} ({live.Purse} a fight) when this deal is up."
                : $"{rival} has offered him {live.Purse} a fight. Match it or let him go on the Desk by {live.Deadline:d MMM}.",
                CardTone.Warn));
        }
        else if (c.PoachWarned)
            lines.Add(new SheetLine("Rivals", "A rival already made an offer on this deal; it's off the table now.", CardTone.Info));
        return lines.Select(l => l with { Text = Lifers.Gendered(f, l.Text) }).ToList();
    }

    private List<SheetLine> Between(Fighter r, Fighter b, List<(Fight Fight, Event Event)> rh, List<(Fight Fight, Event Event)> bh)
    {
        var w = _world!;
        var lines = new List<SheetLine>();

        // Their history.
        var shared = r.BoutHistory.Intersect(b.BoutHistory).ToHashSet();
        var meetings = rh.Where(x => shared.Contains(x.Fight.Id)).ToList();
        if (meetings.Count == 0) lines.Add(new SheetLine("History", "They've never met."));
        else
        {
            int rw = meetings.Count(m => m.Fight.Outcome!.Winner == r.Id && !m.Fight.OverturnedNc);
            int bw = meetings.Count(m => m.Fight.Outcome!.Winner == b.Id && !m.Fight.OverturnedNc);
            foreach (var (fight, ev) in meetings.TakeLast(3))
            {
                var o = fight.Outcome!;
                string who = fight.OverturnedNc ? "No contest (overturned)"
                    : o.Winner is { } win ? $"{w.Get(win)?.LastName} won by {MethodWord(o.Method)}{(IsKo(o.Method) || IsSub(o.Method) ? $" in round {o.EndRound}" : "")}"
                    : "A draw";
                lines.Add(new SheetLine("Met before", $"{ev.Date:MMM yyyy}: {who} ({ev.Name}{(fight.IsTitleFight ? ", for a title" : "")})", CardTone.Warn));
            }
            if (meetings.Count >= 2) lines.Add(new SheetLine("Series", $"{r.LastName} {rw}, {b.LastName} {bw}", CardTone.Warn));
        }

        // Feud, bonds, camps.
        if (Core.Sim.Living.Storylines.RivalryBetween(w, r.Id, b.Id) is { } feud)
            lines.Add(new SheetLine("Bad blood", $"Heat {feud.Intensity} of 100 ({(feud.Intensity >= 80 ? "a war" : feud.Intensity >= 60 ? "personal" : feud.Intensity >= 40 ? "heated" : "simmering")}), since {feud.Since:MMM yyyy}. A grudge sells the house.", CardTone.Bad));
        foreach (var rel in w.Relationships.Where(x => x.IsBetween(r.Id, b.Id)))
            lines.Add(new SheetLine(rel.Kind == RelationshipKind.Family ? "Family" : rel.Kind == RelationshipKind.Mentor ? "Mentor" : "Friends",
                (rel.Note is { Length: > 0 } note ? Cap(note) + ". " : "") + "Making them fight cuts the bond.", CardTone.Warn));
        if (r.Camp is { } ca && ca == b.Camp && w.Get(ca) is { } camp)
            lines.Add(new SheetLine("Teammates", $"Both train at {camp.Name}: only a belt usually puts teammates in the same cage.", CardTone.Warn));
        else if (r.Camp is { } c1 && b.Camp is { } c2 && Core.Sim.Living.Camps.AtWar(w, c1, c2))
            lines.Add(new SheetLine("Gym war", $"{w.Get(c1)?.Name} and {w.Get(c2)?.Name} are at war: both rooms' fans come.", CardTone.Info));

        // The styles, and how it may end (public records only).
        lines.Add(new SheetLine("Styles", StyleClash(r, b)));
        lines.Add(new SheetLine("How it may end", FinishHint(r, b, rh, bh)));
        if (Math.Abs(r.ReachCm - b.ReachCm) >= 8)
        {
            var longer = r.ReachCm > b.ReachCm ? r : b;
            lines.Add(new SheetLine("Frame", $"{longer.LastName} has {Math.Abs(r.ReachCm - b.ReachCm)} cm more reach."));
        }
        return lines;
    }

    private static string Lean(PrimaryStyle s) => s switch
    {
        PrimaryStyle.Striker or PrimaryStyle.Kickboxer or PrimaryStyle.Brawler => "striker",
        PrimaryStyle.Wrestler or PrimaryStyle.BrazilianJiuJitsu or PrimaryStyle.Grappler => "grappler",
        PrimaryStyle.ClinchFighter => "clinch",
        _ => "all-rounder",
    };

    private static string StyleClash(Fighter r, Fighter b)
    {
        string rs = FighterRow.Pretty(r.PrimaryStyle.ToString()).ToLowerInvariant(), bs = FighterRow.Pretty(b.PrimaryStyle.ToString()).ToLowerInvariant();
        string rl = Lean(r.PrimaryStyle), bl = Lean(b.PrimaryStyle);
        if (rl == "striker" && bl == "grappler") return $"{r.LastName} ({rs}) wants it standing; {b.LastName} ({bs}) wants it on the mat. Who decides where it goes?";
        if (rl == "grappler" && bl == "striker") return $"{b.LastName} ({bs}) wants it standing; {r.LastName} ({rs}) wants it on the mat. Who decides where it goes?";
        if (rl == bl && rl == "striker") return $"Two strikers ({rs} vs {bs}): it should stay on the feet.";
        if (rl == bl && rl == "grappler") return $"Two grapplers ({rs} vs {bs}): a fight for position.";
        return $"{FighterRow.Pretty(r.PrimaryStyle.ToString())} vs {bs}.";
    }

    // "7 of his 9 wins by knockout; she has never been stopped": the record, read aloud.
    private string FinishHint(Fighter r, Fighter b, List<(Fight Fight, Event Event)> rh, List<(Fight Fight, Event Event)> bh)
    {
        string One(Fighter f, Fighter opp, List<(Fight Fight, Event Event)> oh)
        {
            var rec = f.Record;
            if (rec.Wins == 0) return $"{f.LastName} has no pro wins yet";
            int ko = rec.WinsByKo, sub = rec.WinsBySubmission;
            int oppStopped = oh.Count(x => !x.Fight.OverturnedNc && x.Fight.Outcome!.Loser == opp.Id && (IsKo(x.Fight.Outcome.Method) || IsSub(x.Fight.Outcome.Method)));
            string how = ko * 2 >= rec.Wins ? $"{ko} of {f.LastName}'s {rec.Wins} wins came by knockout"
                : sub * 2 >= rec.Wins ? $"{sub} of {f.LastName}'s {rec.Wins} wins came by submission"
                : rec.WinsByDecision * 2 >= rec.Wins ? $"{f.LastName} goes the distance ({rec.WinsByDecision} of {rec.Wins} wins on the cards)"
                : $"{f.LastName} finishes and decisions alike";
            return how + (opp.Record.Losses > 0 && oppStopped == 0 ? $", and {opp.LastName} has never been stopped" : "");
        }
        return Lifers.Gendered(r, One(r, b, bh)) + "; " + Lifers.Gendered(b, One(b, r, rh)) + ".";
    }

    private List<SheetLine> StakesOf(Event ev, SheetAsk a, Fighter r, Fighter b, MatchupAppraisal appraisal, bool titleFight, Title? title, int rounds)
    {
        var lines = new List<SheetLine>();
        if (titleFight && title is not null)
            lines.Add(new SheetLine("The belt", $"{title.Name} on the line{(title.CurrentChampion is null ? " (vacant)" : "")}: {(rounds >= 5 ? "five" : rounds.ToString(CultureInfo.InvariantCulture))} rounds"
                + $"{(title.Prestige.Value >= 60 ? ", a belt with a name" : "")}.", CardTone.Warn));
        else if (a.Title && !titleFight)
            lines.Add(new SheetLine("The belt", "The belt comes off: the champion is no longer in the bout.", CardTone.Bad));
        else if (title?.CurrentChampion is { } champ && (champ == r.Id || champ == b.Id))
            lines.Add(new SheetLine("The belt", "A champion in a non-title fight: he'd risk his aura for nothing.", CardTone.Warn));
        else if (appraisal.Stakes == Stakes.TitleEliminator)
            lines.Add(new SheetLine("Title picture", "A title eliminator: the winner should be next.", CardTone.Info));
        lines.Add(new SheetLine("Weight", $"{FighterRow.DivisionLabel(r.WeightClass, r.Sex)}, {rounds} rounds" + (r.WeightClass == b.WeightClass ? "" : " (a class mismatch)")));
        if (a.Source != SheetSource.Replace && a.Slot == BoutSlot.Prelim && appraisal.Appeal >= 60)
            lines.Add(new SheetLine("Slot", "A big fight for the prelims: the main card would sell it better.", CardTone.Info));
        if (appraisal.Stakes == Stakes.Mismatch)
            lines.Add(new SheetLine("Risk", "A mismatch on our reads: the crowd may not buy it, and the loser's stock drops.", CardTone.Bad));
        return lines;
    }

    // The night with this bout, against the night as it is.
    private (List<NightDelta>, string, List<SheetLine>, List<SheetLine>) NightOf(Event ev, SheetAsk a, Fighter r, Fighter b, int rounds, bool title)
    {
        var w = _world!;
        var before = _projection ?? _booking.Project(w, ev);
        var after = _booking.ProjectWith(w, ev, r.Id, b.Id, rounds, title, a.Slot, a.Replacing);
        bool empty = ev.Card.Count == 0;
        var rows = new List<NightDelta>();
        void Row(string label, decimal x, decimal y, Func<decimal, string> fmt, bool higherIsGood = true)
        {
            decimal d = y - x;
            var tone = d == 0 ? CardTone.Neutral : (d > 0) == higherIsGood ? CardTone.Good : CardTone.Bad;
            string sign = d > 0 ? "+" : d < 0 ? "−" : "±";
            rows.Add(new NightDelta(label, empty ? "—" : fmt(x), fmt(y), d == 0 ? "no change" : sign + fmt(Math.Abs(d)), tone));
        }
        string Num(decimal v) => v.ToString("N0", CultureInfo.InvariantCulture);
        string Cash(decimal v) => Compact(new Money(v)).TrimStart('+');
        Row("House", before.Attendance, after.Attendance, Num);
        if (after.PpvBuys > 0 || before.PpvBuys > 0) Row("PPV buys", before.PpvBuys, after.PpvBuys, Num);
        Row("Money in", before.Income.Amount, after.Income.Amount, Cash);
        Row("Fighter pay", (before.Purses + before.WinBonusExpected + before.PpvPoints).Amount, (after.Purses + after.WinBonusExpected + after.PpvPoints).Amount, Cash, higherIsGood: false);
        if (before.Filler.Amount != 0 || after.Filler.Amount != 0) Row("House padding", before.Filler.Amount, after.Filler.Amount, Cash, higherIsGood: false);
        Row("Expected net", before.Net.Amount, after.Net.Amount, v => Compact(new Money(v)));

        string note = (a.Source == SheetSource.Bulk ? "This bout alone added to the card as it stands (not the rest of the proposal). " : "")
                      + (a.Source == SheetSource.Replace ? "The swap, everything else as booked. " : "")
                      + $"With it the night lands between {Compact(after.NetLow)} and {Compact(after.NetHigh)}, depending on who wins and the terms judged on the night.";

        // Partner terms: what this bout settles, breaks, or still leaves short.
        var terms = new List<SheetLine>();
        var was = before.Partners.SelectMany(p => p.Terms.Select(t => (p.Partner, t.Text, t.Status))).ToList();
        foreach (var p in after.Partners)
            foreach (var t in p.Terms)
            {
                var old = was.FirstOrDefault(x => x.Partner == p.Partner && x.Text == t.Text);
                bool hadIt = !empty && old.Partner is not null && old.Status == TermStatus.Met;
                if (t.Status == TermStatus.Met && !hadIt && (empty || old.Status != TermStatus.Met))
                    terms.Add(new SheetLine(p.Partner, $"Meets it: {t.Text}.", CardTone.Good));
                else if (t.Status == TermStatus.Missed && hadIt)
                    terms.Add(new SheetLine(p.Partner, $"Breaks it: {t.Text} ({t.Gap}).", CardTone.Bad));
                else if (t.Status == TermStatus.Missed)
                    terms.Add(new SheetLine(p.Partner, $"Still short: {t.Text} ({t.Gap}).", CardTone.Warn));
                else if (t.Status == TermStatus.JudgedOnTheNight && old.Status != TermStatus.JudgedOnTheNight)
                    terms.Add(new SheetLine(p.Partner, $"Judged on the night: {t.Text}.", CardTone.Info));
            }

        // Risks this bout brings with it.
        var risks = new List<SheetLine>();
        var had = before.Risks.Select(x => x.Text).ToHashSet();
        foreach (var x in after.Risks.Where(x => !had.Contains(x.Text)
                     && x.Kind is not (BookingRiskKind.PartnerTermMissed or BookingRiskKind.EmptyCard or BookingRiskKind.RivalCardClash)))
            risks.Add(new SheetLine("", x.Text, x.Kind is BookingRiskKind.NotMatchReady or BookingRiskKind.NotUnderContract ? CardTone.Bad : CardTone.Warn));
        foreach (var x in before.Risks.Where(x => x.Kind is BookingRiskKind.RoomTooBig or BookingRiskKind.RoomTooSmall or BookingRiskKind.CardPadded)
                     .Where(x => after.Risks.All(y => y.Text != x.Text)))
            risks.Add(new SheetLine("", $"Settles: {x.Text}", CardTone.Good));
        return (rows, note, terms, risks);
    }

    // The dense version: every comparable line, red | label | blue (the Table toggle).
    private List<TaleRow> TapeRows(Fighter r, Fighter b, List<(Fight Fight, Event Event)> rh, List<(Fight Fight, Event Event)> bh)
    {
        var w = _world!;
        var rows = new List<TaleRow>();
        var rx = FactsFor(r.Id);
        var bx = FactsFor(b.Id);
        void Num(string label, int rv, int bv, bool higherIsBetter = true)
        {
            int edge = rv == bv ? 0 : (rv > bv ? -1 : 1);
            if (!higherIsBetter) edge = -edge;
            rows.Add(new TaleRow { Label = label, Red = rv.ToString(CultureInfo.InvariantCulture), Blue = bv.ToString(CultureInfo.InvariantCulture), Edge = edge });
        }
        void Txt(string label, string rv, string bv) => rows.Add(new TaleRow { Label = label, Red = rv, Blue = bv });

        Txt("Rank", _advisor.StandingOf(w, r.Id).Label, _advisor.StandingOf(w, b.Id).Label);
        Txt("Record", r.Record.ToString(), b.Record.ToString());
        Txt("Wins KO / sub / dec", $"{r.Record.WinsByKo} / {r.Record.WinsBySubmission} / {r.Record.WinsByDecision}", $"{b.Record.WinsByKo} / {b.Record.WinsBySubmission} / {b.Record.WinsByDecision}");
        Txt("Streak", StreakText(r.Record.CurrentStreak) is { Length: > 0 } s1 ? s1 : "—", StreakText(b.Record.CurrentStreak) is { Length: > 0 } s2 ? s2 : "—");
        Txt("Last fought", AgoShort(rx?.IdleDays), AgoShort(bx?.IdleDays));
        Num("Age", r.AgeOn(w.CurrentDate), b.AgeOn(w.CurrentDate), higherIsBetter: false);
        Num("Height (cm)", r.HeightCm, b.HeightCm);
        Num("Reach (cm)", r.ReachCm, b.ReachCm);
        Txt("Stance", FighterRow.Pretty(r.Stance.ToString()), FighterRow.Pretty(b.Stance.ToString()));
        Txt("Style", FighterRow.Pretty(r.PrimaryStyle.ToString()), FighterRow.Pretty(b.PrimaryStyle.ToString()));
        Num("Popularity", r.Intangibles.Popularity.Value, b.Intangibles.Popularity.Value);
        Num("Momentum", r.Momentum, b.Momentum);
        Num("Scouted", FighterRow.Scouted(w, r), FighterRow.Scouted(w, b));
        Txt("Purse", r.Contract?.PursePerFight.ToString() ?? "—", b.Contract?.PursePerFight.ToString() ?? "—");
        Txt("Fights left", r.Contract?.FightsRemaining.ToString(CultureInfo.InvariantCulture) ?? "—", b.Contract?.FightsRemaining.ToString(CultureInfo.InvariantCulture) ?? "—");

        // The skills: the same fogged bars the profile draws.
        var index = FightIndex();
        var rr = new FighterRow(r, w, index);
        var br = new FighterRow(b, w, index);
        foreach (var (label, bar) in new[] { ("Punch power", "Punch Pow"), ("Striking def", "Str Defense"), ("Takedowns", "TD Offense"), ("Takedown def", "TD Defense"),
                     ("Submissions", "Sub Offense"), ("Cardio", "Cardio"), ("Chin", "Chin"), ("Fight IQ", "Fight IQ") })
        {
            var x = rr.ScoutedRating(bar);
            var y = br.ScoutedRating(bar);
            rows.Add(new TaleRow { Label = label, Red = x.ValueText, Blue = y.ValueText, Edge = RatingBar.ShownEdge(x, y) });
        }
        return rows;
    }
}
