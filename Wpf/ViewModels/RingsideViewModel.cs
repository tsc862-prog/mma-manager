using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Living;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One house of the young sport as a spectator sees it: who owns it, where, what its nights are, how it is doing in public.</summary>
public sealed class RingsideHouse
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required ImageSource Logo { get; init; }
    public string OwnerName { get; init; } = "";
    public bool HasOwner => OwnerName.Length > 0;
    public string Place { get; init; } = "";
    public string Since { get; init; } = "";
    /// <summary>"Owner · City, Country · since 1993".</summary>
    public string Byline => string.Join("  ·  ", new[] { OwnerName, Place, Since }.Where(s => s.Length > 0));
    /// <summary>What a night there is ("An eight-man bracket in one night…").</summary>
    public string Night { get; init; } = "";
    /// <summary>The public read: nights held, the last crowd and its direction, the roster.</summary>
    public string Standing { get; init; } = "";
    public string Champion { get; init; } = "";
    public bool HasChampion => Champion.Length > 0;
    /// <summary>"Crowds growing" / "Crowds thinning" / "": the one word on the house's direction.</summary>
    public string Trend { get; init; } = "";
    public CardTone TrendTone { get; init; }
    public bool HasTrend => Trend.Length > 0;
}

/// <summary>A night, past or coming, as one line: the house's logo, the name, when and where, and what happened.</summary>
public sealed class RingsideNight
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required ImageSource Logo { get; init; }
    public string When { get; init; } = "";
    public string Line { get; init; } = "";
}

/// <summary>One national scene of the sport: when it woke and what it holds.</summary>
public sealed record RingsideScene(string Name, string Since, string Detail, bool SecondCentre)
{
    public string Tag => SecondCentre ? "SECOND CENTRE" : "";
}

/// <summary>Why the clock stopped while watching (session only: what the player was not told yet).</summary>
public sealed record RingsideStop(string Kicker, string Headline, string Detail, bool Big);

/// <summary>
/// RINGSIDE: the home of a player who has no promotion yet (an Origins world built with
/// <c>WorldBuilder.Options.OriginsObserver</c>, <see cref="World.HasPlayer"/> false). The young sport as it reads
/// from the seats: the chapter the story is in and what just happened, the houses that exist, the names people
/// talk about (by fame, a public fact; the read on each is the public read, never the truth), the last and next
/// nights, the scenes around the world, and one standing call to found his own promotion. Read-only: it decides
/// nothing and writes nothing; it holds ids.
/// </summary>
public sealed class RingsideViewModel : ObservableObject
{
    public string DateLine { get; private set; } = "";
    public string EraLine { get; private set; } = "";
    public string EraName { get; private set; } = "";

    // --- the chapter the story is in
    public string ChapterKicker { get; private set; } = "";
    public string ChapterHeadline { get; private set; } = "";
    public string ChapterBody { get; private set; } = "";
    public ImageSource? ChapterPicture { get; private set; }
    public bool HasChapterPicture => ChapterPicture is not null;
    public bool ChapterPictureIsLogo { get; private set; }
    public string ChapterTally { get; private set; } = "";

    // --- why the clock stopped (set by the shell after an advance; cleared when read again later)
    private RingsideStop? _stop;
    public RingsideStop? Stop { get => _stop; set { if (Set(ref _stop, value)) OnPropertyChanged(nameof(HasStop)); } }
    public bool HasStop => _stop is not null;

    // --- the call to action
    public string FoundLine { get; private set; } = "";
    public string FoundDetail { get; private set; } = "";
    public string FoundRefusal { get; private set; } = "";
    public bool CanFound { get; private set; }
    public ICommand? FoundCommand { get; set; }
    public ICommand? OpenStoryCommand { get; set; }

    public ObservableCollection<RingsideHouse> Houses { get; } = new();
    public string HousesLine { get; private set; } = "";
    public string GoneLine { get; private set; } = "";
    public bool HasGone => GoneLine.Length > 0;

    public ObservableCollection<FighterCardModel> Names { get; } = new();
    public bool HasNames => Names.Count > 0;

    public ObservableCollection<RingsideNight> LastNights { get; } = new();
    public ObservableCollection<RingsideNight> NextNights { get; } = new();
    public bool HasLastNights => LastNights.Count > 0;
    public bool HasNextNights => NextNights.Count > 0;

    public ObservableCollection<RingsideScene> Scenes { get; } = new();
    public ObservableCollection<ComingCard> Coming { get; } = new();
    public bool HasComing => Coming.Count > 0;

    public void Attach(World w)
    {
        var today = w.CurrentDate;
        DateLine = $"RINGSIDE  ·  {today:dddd d MMMM yyyy}".ToUpperInvariant();
        var (era, name) = OriginsStory.CurrentEra(w);
        int year = today.Year - w.StartDate.Year + 1;
        EraLine = $"ERA {era}  ·  YEAR {year} OF THE SPORT";
        EraName = name;

        // The chapter the story is in: the newest one told, or the opening before any.
        var told = OriginsStory.SoFar(w);
        if (told.LastOrDefault() is { } last)
        {
            ChapterKicker = $"THE LATEST CHAPTER  ·  {last.Date:MMMM yyyy}  ·  {last.Title}".ToUpperInvariant();
            ChapterHeadline = last.Headline;
            ChapterBody = last.Body;
            ChapterPicture = null; ChapterPictureIsLogo = false;
            if (last.Fighter is { } fid && w.Get(fid) is { } f) ChapterPicture = Portraits.Thumb(f, w);
            else if (last.Promotion is { } pid && w.Get(pid) is { } p) { ChapterPicture = LogoFactory.ForPromotion(p); ChapterPictureIsLogo = true; }
            ChapterTally = $"{told.Count} chapter{(told.Count == 1 ? "" : "s")} of the story told so far.";
        }
        else
        {
            ChapterKicker = "BEFORE THE FIRST CHAPTER";
            ChapterHeadline = "The sport has no name yet.";
            ChapterBody = "Three small promotions on three continents put on fights with almost no rules: no weight classes, no rounds anyone agrees on, "
                        + "no television. Nobody knows which style wins. The first nights will start to tell.";
            ChapterPicture = null; ChapterPictureIsLogo = false;
            ChapterTally = "Nothing has happened yet that the history books will keep.";
        }

        // The call to action: what founding today would mean, in the sim's own numbers.
        var choices = PlayerFounding.Choices(w, today);
        CanFound = choices.CanFound;
        FoundRefusal = choices.Refusal ?? "";
        int open = choices.Cities.Count(c => c.Open);
        FoundLine = $"{choices.Bank} in the bank  ·  prestige {choices.Prestige}  ·  one free open tryout";
        FoundDetail = open == 0
            ? "No city can take a new house today: every scene with a crowd already has one, or the halls are closed. That changes as the sport spreads."
            : $"No deadline. Pick a name and a home: {open} {(open == 1 ? "city has" : "cities have")} room for a new house today"
              + (choices.OpenWeight ? ", and the sport still fights at open weight." : ".");

        BuildHouses(w);
        BuildNames(w);
        BuildNights(w);

        Scenes.Clear();
        foreach (var s in OriginsScenes.Scenes(w))
        {
            string houses = s.Houses == 1 ? "1 house" : $"{s.Houses} houses";
            string gyms = s.Gyms == 1 ? "1 gym" : $"{s.Gyms} gyms";
            Scenes.Add(new RingsideScene(s.Name, s.WokeOn <= w.StartDate ? "from the start" : $"since {s.WokeOn:MMM yyyy}",
                $"{houses}  ·  {gyms}  ·  {s.Men} fighters", s.SecondCentre));
        }

        Coming.Clear();
        foreach (var c in OriginsStory.Coming(w, 3)) Coming.Add(new ComingCard(c.EraName, c.Title, c.When));

        OnPropertyChanged(string.Empty);
    }

    private void BuildHouses(World w)
    {
        Houses.Clear();
        var completed = w.Events.Values.Where(e => e.Status == EventStatus.Completed).ToList();
        foreach (var p in w.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.FoundedYear).ThenBy(p => p.Id.Value))
        {
            var nights = completed.Where(e => e.Promotion == p.Id).OrderBy(e => e.Date).ThenBy(e => e.Id.Value).ToList();
            var crowds = nights.Where(e => e.Financials is { Attendance: > 0 }).Select(e => e.Financials!.Attendance).ToList();
            int roster = w.RosterOf(p.Id).Count(f => f.Status != FighterStatus.Retired);
            var bits = new List<string>
            {
                nights.Count == 0 ? "no night held yet" : nights.Count == 1 ? "1 night held" : $"{nights.Count} nights held",
            };
            if (crowds.Count > 0) bits.Add($"last crowd {crowds[^1]:N0}");
            bits.Add(roster == 1 ? "1 fighter" : $"{roster} fighters");

            string trend = ""; var tone = CardTone.Neutral;
            if (crowds.Count >= 3)
            {
                double before = crowds.Take(crowds.Count - 1).TakeLast(3).Average(), now = crowds[^1];
                if (now >= before * 1.15) { trend = "Crowds growing"; tone = CardTone.Good; }
                else if (now <= before * 0.85) { trend = "Crowds thinning"; tone = CardTone.Warn; }
            }

            var champs = p.Divisions.Where(d => d.Title is not null)
                .Select(d => w.Get(d.Title!.Value)?.CurrentChampion is { } cid ? w.Get(cid) : null)
                .OfType<Fighter>().Select(f => f.FullName).Distinct().ToList();
            string night = HouseRules.NightLine(p);
            if (night.Length == 0) night = "A card of single bouts, the house belt on the line when it can be.";

            Houses.Add(new RingsideHouse
            {
                Id = p.Id.Value,
                Name = p.Name,
                Logo = LogoFactory.ForPromotion(p),
                OwnerName = p.OwnerName ?? "",
                Place = Place(w, p),
                Since = p.FoundedYear > 0 ? $"since {p.FoundedYear}" : "",
                Night = $"{night} Nights {HouseRules.Cadence(p)}.",
                Standing = string.Join("  ·  ", bits),
                Champion = champs.Count == 0 ? "" : (champs.Count == 1 ? "Champion: " : "Champions: ") + string.Join(", ", champs),
                Trend = trend,
                TrendTone = tone,
            });
        }
        int live = Houses.Count;
        HousesLine = live == 1 ? "One house puts on nights today." : $"{Words(live)} houses put on nights today.";
        var gone = w.Promotions.Values.Where(p => p.Defunct).OrderBy(p => p.Id.Value).Select(p => p.Name).ToList();
        GoneLine = gone.Count == 0 ? "" : $"Gone: {string.Join(", ", gone)}.";
    }

    private void BuildNames(World w)
    {
        Names.Clear();
        // Fame is a public fact; the badge on each card is the public read (no house of his = no scouts).
        var talked = w.Fighters.Values
            .Where(f => f.Status != FighterStatus.Retired && f.Record.Wins + f.Record.Losses + f.Record.Draws > 0)
            .OrderByDescending(f => f.Intangibles.Popularity.Value).ThenBy(f => f.Id.Value)
            .Take(6);
        foreach (var f in talked) Names.Add(new FighterCardModel(f, w, why: Why(w, f)));
    }

    /// <summary>Why people say his name: a belt, the night he won, a streak, or the way he fights.</summary>
    private static string Why(World w, Fighter f)
    {
        if (w.Titles.Values.FirstOrDefault(t => t.CurrentChampion == f.Id) is { } title)
            return $"{w.Get(title.Promotion)?.Abbreviation} CHAMPION".Trim();
        if (f.Accolades.LastOrDefault(a => a.Title.StartsWith("Won ", StringComparison.Ordinal)) is { } won)
            return won.Title.ToUpperInvariant();
        if (f.Record.CurrentStreak >= 3) return $"{f.Record.CurrentStreak} WINS IN A ROW";
        if (OriginsTalent.ListedAsAllRounder(f)) return "AN ALL-ROUNDER";
        return w.Get(f.Country)?.Name.ToUpperInvariant() ?? "";
    }

    private void BuildNights(World w)
    {
        LastNights.Clear();
        NextNights.Clear();
        var today = w.CurrentDate;
        foreach (var e in w.Events.Values.Where(e => e.Status == EventStatus.Completed)
                     .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id.Value).Take(4))
        {
            if (w.Get(e.Promotion) is not { } p) continue;
            LastNights.Add(new RingsideNight
            {
                Id = e.Id.Value, Name = e.Name, Logo = LogoFactory.ForPromotion(p),
                When = $"{e.Date:d MMM yyyy}  ·  {Where(w, e, p)}",
                Line = Result(w, e),
            });
        }
        foreach (var e in w.Events.Values.Where(e => e.Status == EventStatus.Scheduled && e.Date >= today)
                     .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).Take(4))
        {
            if (w.Get(e.Promotion) is not { } p) continue;
            int days = e.Date.DayNumber - today.DayNumber;
            NextNights.Add(new RingsideNight
            {
                Id = e.Id.Value, Name = e.Name, Logo = LogoFactory.ForPromotion(p),
                When = $"{e.Date:d MMM yyyy}  ·  {(days == 0 ? "tonight" : days == 1 ? "tomorrow" : $"in {days} days")}",
                Line = $"{p.Name}  ·  {Where(w, e, p)}",
            });
        }
    }

    /// <summary>What a finished night produced: the bracket's winner, or the main event's result.</summary>
    private static string Result(World w, Event e)
    {
        if (e.Format == EventFormat.Tournament)
        {
            var night = BracketNightReader.Read(w, e);
            return night?.Champion is { } c && w.Get(c) is { } champ
                ? $"{champ.FullName} won the bracket"
                : "The bracket ended without a winner";
        }
        if (e.Card.Count == 0 || e.Card[^1].Outcome is not { } o) return e.Card.Count == 0 ? "" : "The main event did not finish";
        var red = w.Get(e.Card[^1].RedCorner); var blue = w.Get(e.Card[^1].BlueCorner);
        if (o.Winner is not { } win || w.Get(win) is not { } winner)
            return $"{red?.FullName} and {blue?.FullName}: {(o.Method == FightMethod.NoContest ? "no contest" : "a draw")}";
        var loser = winner.Id == red?.Id ? blue : red;
        return $"{winner.FullName} beat {loser?.FullName} by {Method(o.Method)}";
    }

    private static string Method(FightMethod m) => m switch
    {
        FightMethod.Knockout => "knockout",
        FightMethod.TechnicalKnockout => "TKO",
        FightMethod.SubmissionTap or FightMethod.TechnicalSubmission => "submission",
        FightMethod.UnanimousDecision => "unanimous decision",
        FightMethod.SplitDecision => "split decision",
        FightMethod.MajorityDecision => "majority decision",
        FightMethod.Disqualification => "disqualification",
        _ => TitleMoment.MethodText(m).ToLowerInvariant(),
    };

    private static string Where(World w, Event e, Promotion p)
    {
        if (e.VenueId is { } vid && w.Get(vid) is { } venue && w.Get(venue.City) is { } vc) return vc.Name;
        return p.HomeCity is { } hc && w.Get(hc) is { } city ? city.Name : e.Venue ?? "";
    }

    private static string Place(World w, Promotion p)
    {
        if (p.HomeCity is not { } hc || w.Get(hc) is not { } city) return FighterRow.Pretty(p.BaseRegion.ToString());
        return w.Get(city.Country) is { } k ? $"{city.Name}, {k.Name}" : city.Name;
    }

    private static string Words(int n) => n switch
    {
        2 => "Two", 3 => "Three", 4 => "Four", 5 => "Five", 6 => "Six", 7 => "Seven", 8 => "Eight", 9 => "Nine", 10 => "Ten", _ => n.ToString(),
    };
}
