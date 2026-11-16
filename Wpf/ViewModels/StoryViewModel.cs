using System.Collections.ObjectModel;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Living;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// One fired chapter of an Origins world's history as a card: the narration the sim told when it fired,
/// who it was about (a face, or the house's logo), and what it opened. Read from
/// <see cref="OriginsStory.View"/>; holds ids only.
/// </summary>
public sealed class ChapterCard
{
    public ChapterCard(World w, ChapterView c)
    {
        Key = c.Key;
        EraLine = $"ERA {c.Era}  ·  {c.EraName}".ToUpperInvariant();
        Title = c.Title;
        Headline = c.Headline;
        Body = c.Body;
        When = c.Date.ToString("MMMM yyyy");
        IsYours = c.Promotion is { } p && p == w.PlayerPromotion;
        FighterId = c.Fighter?.Value ?? 0;
        PromotionId = c.Promotion?.Value ?? 0;
        Who = string.Join("  ·  ", new[] { c.FighterName, c.PromotionName }.Where(s => !string.IsNullOrEmpty(s)));
        Opened = c.Unlocked.Count == 0 ? "" : "Opened: " + string.Join(", ", c.Unlocked) + ".";
        if (c.Fighter is { } fid && w.Get(fid) is { } f) Picture = Portraits.Thumb(f, w);
        else if (c.Promotion is { } pid && w.Get(pid) is { } promo) { Picture = LogoFactory.ForPromotion(promo); PictureIsLogo = true; }
    }

    public string Key { get; }
    public string EraLine { get; }
    public string Title { get; }
    public string Headline { get; }
    public string Body { get; }
    public string When { get; }
    public string Who { get; }
    public bool HasWho => Who.Length > 0;
    public string Opened { get; }
    public bool HasOpened => Opened.Length > 0;
    public bool IsYours { get; }
    public string YoursText => IsYours ? "YOUR CHAPTER" : "";
    public int FighterId { get; }
    public int PromotionId { get; }
    public ImageSource? Picture { get; }
    public bool HasPicture => Picture is not null;
    public bool PictureIsLogo { get; }
}

/// <summary>One era of the story: its name, its years, and its chapters in the order they happened.</summary>
public sealed record StoryEra(int Number, string Name, string Span, bool IsCurrent, IReadOnlyList<ChapterCard> Chapters)
{
    public string Heading => $"Era {Number}: {Name}";
    public string NowText => IsCurrent ? "NOW" : "";
}

/// <summary>A chapter still to come, kept vague (no dates): its era, its title, and roughly when.</summary>
public sealed record ComingCard(string EraName, string Title, string When);

/// <summary>
/// The Story So Far (Origins only): the sport's history as the player's world has written it, era by era,
/// newest era first, with a spoiler-free look at what is coming. Read-only; reads
/// <see cref="OriginsStory"/> and nothing else. Empty in today's mode (the app is not on the desktop there).
/// </summary>
public sealed class StoryViewModel : ObservableObject
{
    public bool Applies { get; private set; }
    public string EraNumberText { get; private set; } = "";
    public string EraName { get; private set; } = "";
    public string YearLine { get; private set; } = "";
    public string TallyLine { get; private set; } = "";
    public ObservableCollection<StoryEra> Eras { get; } = new();
    public ObservableCollection<ComingCard> Coming { get; } = new();
    public bool HasComing => Coming.Count > 0;
    public bool NoChapters { get; private set; }

    public void Attach(World w)
    {
        Eras.Clear(); Coming.Clear();
        Applies = w.Mode == WorldMode.Origins;
        if (!Applies) { NoChapters = false; OnPropertyChanged(string.Empty); return; }

        var (era, name) = OriginsStory.CurrentEra(w);
        EraNumberText = $"ERA {era} OF {OriginsStory.EraNames.Length - 1}";
        EraName = name;
        int year = w.CurrentDate.Year - w.StartDate.Year + 1;
        YearLine = $"{w.CurrentDate:MMMM yyyy}  ·  year {year} of the sport you are living through";

        var all = OriginsStory.SoFar(w);
        int yours = all.Count(c => c.Promotion == w.PlayerPromotion);
        TallyLine = all.Count == 0 ? "Nothing has happened yet that the history books will keep. It will."
            : $"{all.Count} chapter{(all.Count == 1 ? "" : "s")} written so far" + (yours > 0 ? $", {yours} of them by your house." : ". None by your house yet.");
        NoChapters = all.Count == 0;

        foreach (var g in all.GroupBy(c => c.Era).OrderByDescending(g => g.Key))
        {
            var cards = g.OrderBy(c => c.Date).Select(c => new ChapterCard(w, c)).ToList();
            int from = g.Min(c => c.Date.Year), to = g.Max(c => c.Date.Year);
            string span = g.Key == era ? $"{from} to now" : from == to ? $"{from}" : $"{from} to {to}";
            Eras.Add(new StoryEra(g.Key, g.First().EraName, span, g.Key == era, cards));
        }
        if (Eras.All(e => e.Number != era))
            Eras.Insert(0, new StoryEra(era, name, "now", true, Array.Empty<ChapterCard>()));

        foreach (var c in OriginsStory.Coming(w)) Coming.Add(new ComingCard(c.EraName, c.Title, c.When));
        OnPropertyChanged(string.Empty);
    }
}
