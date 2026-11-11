using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;
using Voice = MmaManager.Core.Sim.Flavor;

namespace MmaManager.Wpf.ViewModels;

// =============================================================================
// THE MOMENT: when something big happens to the player's promotion, one card
// takes the desktop. The sim is not asked anything new: every moment is read
// off a COMPLETED event (and the lineage the results processor already wrote).
//
// Adding a kind of moment:
//   1. a class deriving from MomentViewModel with a static Find(World, Event)
//      that returns the moments that night produced (usually none);
//   2. one line in MomentsViewModel.Sources;
//   3. nothing else if the shared poster layout fits (fill Kicker, Headline,
//      Name, Facts, ...); otherwise a DataTemplate for the new type in
//      Views/MomentView.xaml.
// Weight decides which one shows when a night produces several: only the
// biggest takes the screen, the rest become its "Also tonight" line.
// =============================================================================

/// <summary>One fact on a moment card: a small label, the fact, and a line of detail.</summary>
public sealed record MomentFact(string Label, string Value, string Detail)
{
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>A link in a strip of people (a belt's lineage): who, when, and whether it is tonight's.</summary>
public sealed record MomentStripItem(int FighterId, string Name, string Detail, ImageSource? Picture, bool IsCurrent)
{
    public bool HasPicture => Picture is not null;
    public string Icon => "People";
}

/// <summary>Somebody else's word on the moment (the beaten champion), with their face.</summary>
public sealed record MomentVoice(int FighterId, string Text, string By, ImageSource? Picture)
{
    public bool HasPicture => Picture is not null;
    public string Icon => "People";
}

/// <summary>
/// A full-desktop moment. Everything on it is a public fact or the person's own
/// words; nothing reads true ability or potential.
/// </summary>
public abstract class MomentViewModel
{
    /// <summary>How big this is against other moments from the same night (the biggest shows).</summary>
    public int Weight { get; init; }

    /// <summary>The short name of what happened, for the "Also tonight" line of a bigger moment.</summary>
    public string Short { get; init; } = "";

    public string Kicker { get; init; } = "";      // "AFC 162 · ROUND 5 · 3:12 · TKO"
    public string Headline { get; init; } = "";    // "AND NEW."
    public string Name { get; init; } = "";        // the person, as the announcer says it
    public string Subline { get; init; } = "";     // "Women's Bantamweight Championship"
    public string Org { get; init; } = "";
    public ImageSource? OrgLogo { get; init; }
    public ImageSource? Art { get; init; }         // full-body art of the person
    public bool HasArt => Art is not null;

    public string Quote { get; init; } = "";
    public string QuoteBy { get; init; } = "";
    public bool HasQuote => Quote.Length > 0;

    public string StripLabel { get; init; } = "";
    public IReadOnlyList<MomentStripItem> Strip { get; init; } = Array.Empty<MomentStripItem>();
    public bool HasStrip => Strip.Count > 0;

    public IReadOnlyList<MomentFact> Facts { get; init; } = Array.Empty<MomentFact>();

    public MomentVoice? Answer { get; init; }
    public bool HasAnswer => Answer is not null;

    /// <summary>Smaller moments from the same night, in a line (set by the host).</summary>
    public string AlsoLine { get; internal set; } = "";
    public bool HasAlso => AlsoLine.Length > 0;

    /// <summary>The person the moment is about (their profile is one click away), if any.</summary>
    public int? SubjectId { get; init; }
    public string SubjectButton { get; init; } = "";
    public int? EventId { get; init; }
}

/// <summary>
/// A belt on the player's promotion changed hands on the player's card, or was
/// won for the first time. Read entirely from the completed event and the
/// title's lineage (the results processor has already opened the new reign).
/// </summary>
public sealed class TitleMoment : MomentViewModel
{
    public enum Variant { Dethroned, FirstChampion, VacantClaimed }

    public Variant How { get; init; }

    public static IEnumerable<MomentViewModel> Find(World world, Event ev)
    {
        // Your belts only, and only from your card.
        if (ev.Promotion != world.PlayerPromotion || ev.Status != EventStatus.Completed) yield break;
        var promo = world.Get(ev.Promotion);
        int mainIndex = ev.Card.Count - 1;

        for (int i = 0; i < ev.Card.Count; i++)
        {
            var fight = ev.Card[i];
            if (!fight.IsTitleFight || fight.OverturnedNc || fight.Title is not { } tid) continue;
            if (world.Get(tid) is not { } title || title.Promotion != world.PlayerPromotion) continue;
            if (fight.Outcome is not { Winner: { } wid } outcome) continue;

            // A new reign opened tonight for tonight's winner: the belt changed hands.
            int at = title.Lineage.FindLastIndex(r => r.Champion == wid && r.WonOn == ev.Date);
            if (at < 0) continue;
            if (world.Get(wid) is not { } winner) continue;
            var loser = outcome.Loser is { } lid ? world.Get(lid) : null;
            var previous = at > 0 ? title.Lineage[at - 1] : null;

            var how = previous is null ? Variant.FirstChampion
                : loser is not null && previous.Champion == loser.Id && previous.LostOn == ev.Date ? Variant.Dethroned
                : Variant.VacantClaimed;

            yield return Build(world, ev, promo, fight, outcome, title, at, winner, loser, previous, how, isMain: i == mainIndex);
        }
    }

    private static TitleMoment Build(World world, Event ev, Promotion? promo, Fight fight, FightOutcome outcome,
        Title title, int reignIndex, Fighter winner, Fighter? loser, TitleReign? previous, Variant how, bool isMain)
    {
        string division = FighterRow.DivisionLabel(title.WeightClass, winner.Sex);

        // How it was won, the way the arena announcer reads it.
        string clock = $"{outcome.EndTimeSeconds / 60}:{outcome.EndTimeSeconds % 60:00}";
        string kicker = IsDecision(outcome.Method)
            ? $"{ev.Name} · {Words(outcome.EndRound)} rounds · {MethodText(outcome.Method)}"
            : $"{ev.Name} · Round {outcome.EndRound} · {clock} · {MethodText(outcome.Method)}";

        // Distinct champions in the belt's history, and whether tonight's has held it before.
        int champions = title.Lineage.Take(reignIndex + 1).Select(r => r.Champion).Distinct().Count();
        int reignsHeld = title.Lineage.Take(reignIndex + 1).Count(r => r.Champion == winner.Id);
        string stripLabel = reignsHeld > 1
            ? $"The lineage · {Ordinal(reignsHeld)} time {winner.LastName} has held it"
            : $"The lineage · {Ordinal(champions)} champion in the belt's history";

        // The strip: the last few reigns up to tonight's.
        var strip = new List<MomentStripItem>();
        foreach (var r in title.Lineage.Take(reignIndex + 1).Skip(Math.Max(0, reignIndex + 1 - 4)))
        {
            if (world.Get(r.Champion) is not { } c) continue;
            bool current = r.WonOn == ev.Date && r.Champion == winner.Id;
            string detail = current
                ? ev.Date.ToString("d MMM yyyy")
                : $"{r.WonOn.Year} · {r.Defenses} def";
            strip.Add(new MomentStripItem(c.Id.Value, $"{c.FirstName} {c.LastName}", detail, Portraits.Thumb(c, world), current));
        }

        var facts = new List<MomentFact>();
        switch (how)
        {
            case Variant.Dethroned when loser is not null && previous is not null:
                facts.Add(new MomentFact("The reign that ended", $"{loser.FirstName} {loser.LastName}",
                    $"Champion since {previous.WonOn:MMM yyyy} · {Plural(previous.Defenses, "defence")} · {Span(previous.WonOn, ev.Date)}"));
                break;
            case Variant.FirstChampion:
                facts.Add(new MomentFact("The belt", "Its first champion",
                    $"The {division} belt had never been won until tonight."));
                break;
            default:
                facts.Add(new MomentFact("The belt", "Vacant until tonight",
                    previous is not null && world.Get(previous.Champion) is { } last
                        ? $"Last held by {last.FirstName} {last.LastName}" + (previous.LostOn is { } off ? $", until {off:MMM yyyy}" : "")
                        : ""));
                break;
        }

        var rec = winner.Record;
        string streak = rec.CurrentStreak >= 2 ? $"{rec.CurrentStreak} wins in a row" : "";
        string deal = winner.Contract is { Promotion: var p } c2 && p == ev.Promotion
            ? $"{Plural(c2.FightsRemaining, "fight")} left on the deal" + (c2.HasChampionsClause ? " (champion's clause)" : "")
            : "";
        facts.Add(new MomentFact("The champion", rec.ToString(),
            string.Join(" · ", new[] { streak, deal }.Where(s => s.Length > 0))));

        if (ev.Financials is { } fin)
        {
            string ppv = fin.PayPerViewBuys > 0 ? $" · {fin.PayPerViewBuys:N0} PPV buys" : "";
            facts.Add(new MomentFact("The night", $"{fin.Attendance:N0} in the house",
                $"Gate {fin.GateRevenue}{ppv}" + (ev.Venue is { Length: > 0 } v ? $" · {v}" : "")));
        }

        MomentVoice? answer = null;
        if (how == Variant.Dethroned && loser is not null)
            answer = new MomentVoice(loser.Id.Value,
                Voice.Quotes.Say(loser, Voice.Moment.TitleLoss, ev.Date, winner),
                $"{loser.FirstName} {loser.LastName}, after the bout", Portraits.Thumb(loser, world));

        return new TitleMoment
        {
            How = how,
            Weight = (how switch { Variant.Dethroned => 300, Variant.FirstChampion => 260, _ => 220 })
                     + title.Prestige.Value + (isMain ? 50 : 0),
            Short = $"{winner.FirstName} {winner.LastName} took the {division} belt",
            Kicker = kicker.ToUpperInvariant(),
            Headline = how switch
            {
                Variant.Dethroned => "AND NEW.",
                Variant.FirstChampion => "THE FIRST.",
                _ => "CROWNED.",
            },
            Name = winner.FullName.ToUpperInvariant(),
            Subline = how == Variant.FirstChampion ? $"The first {division} Champion" : $"{division} Champion",
            Org = promo?.Name ?? "",
            OrgLogo = promo is null ? null : LogoFactory.ForPromotion(promo),
            Art = Portraits.Body(winner, world),
            Quote = Voice.Quotes.Say(winner, Voice.Moment.TitleWin, ev.Date, loser),
            QuoteBy = $"{winner.LastName}, in the cage",
            StripLabel = stripLabel.ToUpperInvariant(),
            Strip = strip.Count >= 2 ? strip : Array.Empty<MomentStripItem>(),   // a lineage of one is the headline
            Facts = facts,
            Answer = answer,
            SubjectId = winner.Id.Value,
            SubjectButton = $"{winner.LastName}'s profile",
            EventId = ev.Id.Value,
        };
    }

    private static bool IsDecision(FightMethod m) =>
        m is FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision;

    private static string MethodText(FightMethod m) => m switch
    {
        FightMethod.Knockout => "KO",
        FightMethod.TechnicalKnockout => "TKO",
        FightMethod.SubmissionTap => "Submission",
        FightMethod.TechnicalSubmission => "Technical submission",
        FightMethod.UnanimousDecision => "Unanimous decision",
        FightMethod.SplitDecision => "Split decision",
        FightMethod.MajorityDecision => "Majority decision",
        FightMethod.Disqualification => "Disqualification",
        _ => m.ToString(),
    };

    private static string Words(int n) => n switch { 1 => "One", 2 => "Two", 3 => "Three", 4 => "Four", 5 => "Five", _ => n.ToString() };

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    internal static string Ordinal(int n) => (n % 100) is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch
    {
        1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th",
    };

    /// <summary>How long a reign lasted, in the words a commentator would use.</summary>
    internal static string Span(DateOnly from, DateOnly to)
    {
        int days = Math.Max(0, to.DayNumber - from.DayNumber);
        int months = (to.Year - from.Year) * 12 + to.Month - from.Month - (to.Day < from.Day ? 1 : 0);
        if (months < 1) return days == 1 ? "1 day" : $"{days} days";
        int y = months / 12, m = months % 12;
        if (y == 0) return Plural(m, "month");
        return m == 0 ? Plural(y, "year") : $"{Plural(y, "year")}, {Plural(m, "month")}";
    }
}

/// <summary>
/// The shell's side of the moment: after the player's card resolves it asks every
/// source what the night produced, puts the biggest on screen, and takes it down
/// on one click or key. It never holds the clock: advancing time dismisses it.
/// </summary>
public sealed class MomentsViewModel : ObservableObject
{
    public delegate IEnumerable<MomentViewModel> MomentSource(World world, Event ev);

    /// <summary>Every kind of moment the game knows. Add a kind here.</summary>
    public static IReadOnlyList<MomentSource> Sources { get; } = new MomentSource[]
    {
        TitleMoment.Find,
    };

    public MomentsViewModel()
    {
        DismissCommand = new RelayCommand(Dismiss);
        OpenSubjectCommand = new RelayCommand(() =>
        {
            var id = Current?.SubjectId;
            Dismiss();
            if (id is { } fid) OpenFighter?.Invoke(fid);
        }, () => Current?.SubjectId is not null);
        OpenEventCommand = new RelayCommand(() =>
        {
            var id = Current?.EventId;
            Dismiss();
            if (id is { } eid) OpenEvent?.Invoke(eid);
        }, () => Current?.EventId is not null);
        OpenFighterCommand = new RelayCommand<int>(id => { Dismiss(); OpenFighter?.Invoke(id); });
    }

    public ICommand DismissCommand { get; }
    public ICommand OpenFighterCommand { get; }
    public ICommand OpenSubjectCommand { get; }
    public ICommand OpenEventCommand { get; }
    public Action<int>? OpenFighter { get; set; }
    public Action<int>? OpenEvent { get; set; }

    private MomentViewModel? _current;
    public MomentViewModel? Current
    {
        get => _current;
        private set { if (Set(ref _current, value)) OnPropertyChanged(nameof(IsShowing)); }
    }

    public bool IsShowing => _current is not null;

    /// <summary>The biggest moment a completed player card produced, or null (most nights).</summary>
    public static MomentViewModel? Pick(World world, Event ev)
    {
        if (ev.Promotion != world.PlayerPromotion || ev.Status != EventStatus.Completed) return null;
        var all = Sources.SelectMany(s => s(world, ev)).ToList();
        if (all.Count == 0) return null;
        // Stable: equal weights keep source order, then card order.
        var best = all.Select((m, i) => (m, i)).OrderByDescending(x => x.m.Weight).ThenBy(x => x.i).First().m;
        var rest = all.Where(m => !ReferenceEquals(m, best) && m.Short.Length > 0).Select(m => m.Short).ToList();
        if (rest.Count > 0) best.AlsoLine = "Also tonight: " + string.Join(" · ", rest);
        return best;
    }

    /// <summary>Show the night's moment, if it had one. Returns whether anything showed.</summary>
    public bool Offer(World world, Event ev)
    {
        Current = Pick(world, ev);
        return Current is not null;
    }

    public void Dismiss() => Current = null;
}
