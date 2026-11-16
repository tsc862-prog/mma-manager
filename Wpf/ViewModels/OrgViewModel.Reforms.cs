using System.Collections.ObjectModel;
using System.Windows.Input;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Living;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// One reform of the house rules, as HQ shows it (Origins): what it does, what it buys, and either an Adopt
/// button (it is one of <see cref="OriginsReforms.Options"/> today) or the sim's reason it isn't
/// (<see cref="OriginsReforms.Refusal"/>). Built from ids every refresh; holds the chapter key only.
/// </summary>
public sealed class ReformCard
{
    public required string Chapter { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public IReadOnlyList<string> Buys { get; init; } = Array.Empty<string>();
    public bool HasBuys => Buys.Count > 0;
    public bool CanAdopt { get; init; }
    public string Refusal { get; init; } = "";
    public bool HasRefusal => Refusal.Length > 0;
    public string Kicker => CanAdopt ? "YOURS TO ADOPT  ·  COSTS NO MONEY" : "NOT YET";
    public CardTone Tone => CanAdopt ? CardTone.Good : CardTone.Neutral;
}

public sealed partial class OrgViewModel
{
    /// <summary>Asks before something that can't be undone (heading, body, detail, confirm text); null = go ahead (harness).</summary>
    public Func<string, string, string?, string, bool>? Confirm { get; set; }

    /// <summary>The house's reforms, open ones first (an Origins house; empty today).</summary>
    public ObservableCollection<ReformCard> Reforms { get; } = new();
    public bool HasReforms => Reforms.Count > 0;

    /// <summary>A ban has closed the house's own city: what a licence would take, in the sim's honest words.</summary>
    public string BanLine { get; private set; } = "";
    public bool HasBanLine => BanLine.Length > 0;

    /// <summary>An Origins house that has made every reform there is: said once, plainly.</summary>
    public string ReformsDoneLine { get; private set; } = "";
    public bool HasReformsDoneLine => ReformsDoneLine.Length > 0;

    /// <summary>Is there anything to show in the reforms box at all (an Origins house)?</summary>
    public bool ShowReforms => HasReforms || HasBanLine || HasReformsDoneLine;

    private ICommand? _adoptReformCommand;
    /// <summary>Adopt a reform (parameter = its chapter key); asks first, since it can't be undone.</summary>
    public ICommand AdoptReformCommand => _adoptReformCommand ??= new RelayCommand<string>(AdoptReform);

    private void RefreshReforms(Promotion promo)
    {
        Reforms.Clear();
        BanLine = ""; ReformsDoneLine = "";
        var w = _world;
        if (w is null || w.Mode != WorldMode.Origins || promo.Defunct)
        {
            RaiseReforms();
            return;
        }
        var today = w.CurrentDate;
        var open = OriginsReforms.Options(w, promo, today);
        foreach (var o in open)
            Reforms.Add(new ReformCard { Chapter = o.Chapter, Title = o.Label, Detail = o.Detail, Buys = Buys(w, promo, o.Chapter), CanAdopt = true });

        // The ones not open today, each with the sim's reason (rounds, gloves, then the next weight line).
        var shut = new List<string>();
        if (!OriginsReforms.HasRounds(promo)) shut.Add(OriginsReforms.Rounds);
        if (!OriginsReforms.HasGloves(promo)) shut.Add(OriginsReforms.Gloves);
        if (OriginsReforms.Ladder.FirstOrDefault(c => !OriginsReforms.HasLines(promo, c)) is { } line) shut.Add(line);
        foreach (var c in shut.Where(c => open.All(o => o.Chapter != c)))
            if (OriginsReforms.Refusal(w, promo, c, today) is { } why)
                Reforms.Add(new ReformCard { Chapter = c, Title = Label(c), Detail = What(promo, c), Buys = Buys(w, promo, c), Refusal = why });

        if (promo.HomeCity is { } homeId && w.Get(homeId) is { } home && Era.Banned(w, home.Id, today))
            BanLine = $"{home.Name} has banned the sport: your own hall is closed to you. " + OriginsBacklash.LicenceAdvice(w, promo, today);
        if (Reforms.Count == 0)
            ReformsDoneLine = "Rounds, judges, gloves and every weight line the sport has drawn: there is nothing left to reform.";
        RaiseReforms();
    }

    private void RaiseReforms()
    {
        foreach (var p in new[] { nameof(HasReforms), nameof(BanLine), nameof(HasBanLine), nameof(ReformsDoneLine), nameof(HasReformsDoneLine), nameof(ShowReforms) })
            OnPropertyChanged(p);
    }

    // A reform not open today has no label from the sim: name it plainly from its lines.
    private static string Label(string chapter) =>
        chapter == OriginsReforms.Rounds ? "Rounds and judges"
        : chapter == OriginsReforms.Gloves ? "Gloves for everyone"
        : chapter == OriginsReforms.Ladder[0] ? "Split heavy and light"
        : "Add " + string.Join(" and ", OriginsReforms.LinesOf(chapter).Select(OriginsReforms.Name)) + " divisions";

    // (An open reform shows the sim's own Detail; this is only for one that isn't open yet. The weight lines'
    // bands come from the sim's BandsAfter, so they can't disagree with what adopting would draw.)
    private static string What(Promotion promo, string chapter)
    {
        if (chapter == OriginsReforms.Rounds) return "Timed rounds with a minute's rest; three judges score a fight that goes the distance.";
        if (chapter == OriginsReforms.Gloves) return "Every man wears the same thin, open-fingered glove.";
        var bands = OriginsReforms.BandsAfter(promo, chapter);
        string Band(int i) => OriginsReforms.Name(bands[i].Class).ToLowerInvariant() + " "
            + (i == 0 ? $"up to {bands[i].Top} lb" : i == bands.Count - 1 ? $"over {bands[i - 1].Top} lb" : $"{bands[i - 1].Top + 1} to {bands[i].Top} lb");
        string lines = "The divisions would be " + string.Join(", ", Enumerable.Range(0, bands.Count).Reverse().Select(Band)) + "; each new one gets a vacant belt.";
        string bracket = promo.Rules.ShowFormat == ShowFormat.OneNightTournament ? " The house would give up its one-night brackets for cards of single bouts." : "";
        return (chapter == OriginsReforms.Ladder[0] ? "The open-weight belt is retired. " : "") + lines + bracket;
    }

    /// <summary>What a reform buys, from public facts: the licence it counts toward, and whose chapter it is.</summary>
    private static IReadOnlyList<string> Buys(World w, Promotion promo, string chapter)
    {
        var lines = new List<string>();
        bool weight = OriginsReforms.Ladder.Contains(chapter);
        if (!weight)
            lines.Add(OriginsReforms.HasRounds(promo) || OriginsReforms.HasGloves(promo)
                ? "The last rule a commission asks for: with it, a commission can license you (and a licence lifts a ban)."
                : "One of the rules a commission asks for before it will license a house (rounds, judges and gloves).");
        else
            // (What the split does to the house's nights and weights is the card's own text: the sim's Detail.)
            lines.Add("More belts: more title fights, and more champions to sell.");
        if (w.Era.Chapter(chapter) is { } done)
            lines.Add(done.Promotion == promo.Id
                ? $"You were first, in {done.FiredOn.Year}: it is your chapter of the sport's story."
                : $"{(done.Promotion is { } by ? w.Get(by)?.Name : null) ?? "Another house"} got there first, in {done.FiredOn.Year}.");
        else
            lines.Add(weight
                ? "Nobody has crowned a champion in it yet: the first house to do so writes this chapter of the sport."
                : "Nobody has done it yet: the first house to adopt it writes this chapter of the sport.");
        return lines;
    }

    private void AdoptReform(string? chapter)
    {
        if (_world is null || Promo is not { } promo || chapter is null) return;
        var card = Reforms.FirstOrDefault(r => r.Chapter == chapter);
        if (card is not { CanAdopt: true }) { Status = card?.Refusal ?? "That reform isn't open to you."; return; }
        if (Confirm is { } ask && !ask($"{card.Title}?",
                $"{card.Detail} It costs no money, but it can't be undone: every card you book from now on is fought this way.",
                string.Join("\n", card.Buys), "Adopt it"))
        {
            Status = $"The house rules stay as they are.";
            return;
        }
        var today = _world.CurrentDate;
        if (!OriginsReforms.TryAdopt(_world, promo, chapter, today, out var refused))
        {
            Status = refused ?? "That reform isn't open to you.";
            Refresh();
            return;
        }
        bool first = _world.Era.Chapter(chapter) is { } rec && rec.Promotion == promo.Id && rec.FiredOn == today;
        Status = $"{card.Title}: adopted. The house rules have changed for every card from now on."
                 + (first ? " You were first: it is a chapter of the sport's story now." : "");
        Refresh();
        OnChanged?.Invoke();
    }
}
