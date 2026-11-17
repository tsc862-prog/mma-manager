using System.Text.RegularExpressions;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// The authored story arcs and the owners' agendas. Text only: these tests pin that every beat names real authored
/// people, that every beat can actually fire (each condition is arranged in a real world and the weekly pass told),
/// that a beat fires once, that no text leaks a hidden number or an era-unsafe word, and that the owners' roll is a
/// plain hash.
/// </summary>
public class ArcsAndAgendasTests
{
    private static readonly Lazy<World> ModernWorld = new(() => new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 }));
    private static readonly Lazy<World> OriginsWorld = new(() => OriginsWorldTests.AiWorld(5));

    private static readonly string[] Cast_ModernNames =
        DesignedRoster.All.Select(d => $"{d.First} {d.Last}").Concat(DesignedLegends.All.Select(d => $"{d.First} {d.Last}")).ToArray();
    private static readonly string[] Cast_OriginsNames = OriginsPioneers.All.Select(p => p.FullName).ToArray();

    // ---------------------------------------------------------------- the tables

    [Fact]
    public void TheArcTable_IsShapedAsPromised()
    {
        Assert.InRange(Arcs.All.Count, 20, 40);
        Assert.True(ArcsModern.All.Count >= 18);
        Assert.True(ArcsOrigins.All.Count >= 5);
        Assert.Equal(Arcs.All.Count, Arcs.All.Select(a => a.Id).Distinct().Count());
        Assert.Equal(Arcs.All.Count, Arcs.All.Select(a => a.Title).Distinct().Count());
        foreach (var arc in Arcs.All)
        {
            var slots = arc.Beats.Select(b => b.Slot).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(Enumerable.Range(1, slots.Count), slots);          // 1..n with no gap
            Assert.InRange(slots.Count, 3, 5);                              // three to five beats on the road
            Assert.Equal(arc.Beats.Count, arc.Beats.Select(b => b.Tag).Distinct().Count());
            // The beats in a slot are alternatives: each slot's alternatives are different conditions or sides.
            foreach (var g in arc.Beats.GroupBy(b => b.Slot).Where(g => g.Count() > 1))
                Assert.Equal(g.Count(), g.Select(b => (b.When, b.Swap)).Distinct().Count());
        }
    }

    [Fact]
    public void EveryCastName_IsARealAuthoredPersonOfTheRightMode()
    {
        foreach (var arc in ArcsModern.All)
        {
            Assert.Equal(WorldMode.Modern, arc.Mode);
            Assert.Contains(arc.Lead, Cast_ModernNames);
            Assert.Contains(arc.Foil, Cast_ModernNames);
        }
        foreach (var arc in ArcsOrigins.All)
        {
            Assert.Equal(WorldMode.Origins, arc.Mode);
            Assert.Contains(arc.Lead, Cast_OriginsNames);
            Assert.Contains(arc.Foil, Cast_OriginsNames);
            // A 1993 arc cannot be about somebody the world reserved only for today.
            Assert.DoesNotContain(arc.Lead, ArcsModern.All.Select(a => a.Lead));
        }
    }

    [Fact]
    public void TheCast_IsReservedByTheNameRegistry_InItsOwnMode()
    {
        // The arcs add no new person; they lean on people who are already reserved. A generated fighter can therefore never
        // take a cast name: the registry yields to the authored one. (No new authored table to register.)
        var modern = NameRegistry.Build(ModernWorld.Value);
        foreach (var arc in ArcsModern.All)
            foreach (var n in new[] { arc.Lead, arc.Foil })
            {
                var parts = n.Split(' ', 2);
                Assert.True(modern.Taken(parts[0], parts[1]), $"{n} is not reserved");
            }
        var origins = NameRegistry.Build(OriginsWorld.Value);
        foreach (var arc in ArcsOrigins.All)
            foreach (var n in new[] { arc.Lead, arc.Foil })
            {
                var parts = n.Split(' ', 2);
                Assert.True(origins.Taken(parts[0], parts[1]), $"{n} is not reserved");
            }
    }

    [Fact]
    public void EveryCastMember_ResolvesInTheBuiltWorld()
    {
        var m = ModernWorld.Value;
        foreach (var arc in ArcsModern.All)
            foreach (var n in new[] { arc.Lead, arc.Foil })
                Assert.Single(m.Fighters.Values, f => f.Designed && $"{f.FirstName} {f.LastName}" == n);
        var o = OriginsWorld.Value;
        foreach (var arc in ArcsOrigins.All)
            foreach (var n in new[] { arc.Lead, arc.Foil })
                Assert.Single(o.Fighters.Values, f => f.Designed && $"{f.FirstName} {f.LastName}" == n);
    }

    [Fact]
    public void TheBeatOrgs_AreRealHouses_InTheirMode()
    {
        var abbrs = ModernWorld.Value.Promotions.Values.Select(p => p.Abbreviation).ToHashSet();
        foreach (var b in ArcsModern.All.SelectMany(a => a.Beats).Where(b => b.When is Cond.LeftOrg))
            Assert.Contains(b.Org!, abbrs);
        foreach (var b in ArcsModern.All.SelectMany(a => a.Beats).Where(b => b.When is Cond.SignedWith && b.Org is not null))
            Assert.Contains(b.Org!, abbrs);
        Assert.All(ArcsOrigins.All.SelectMany(a => a.Beats), b => Assert.True(b.When != Cond.LeftOrg));
    }

    // ---------------------------------------------------------------- the words

    private static readonly Regex Token = new(@"\{(\w+)\}");
    private static readonly string[] KnownTokens = { "AOrg", "BOrg", "AWhere", "BWhere", "ARec", "BRec", "Belt" };
    private static readonly string[] HiddenWords =
        { "ability", "potential", "overall", "ceiling", "skill", "stat ", "attribute", "talent score", "hidden", "ovr" };
    private static readonly string[] AnachronismsIn1993 =
        { "stream", "ranking", "weight class", "weight-class", "glove", "ppv", "pay per view", "app ", "social media", "internet", "podcast", "online", "contender", "title", "belt" };

    private static IEnumerable<(string Where, string Text)> BeatTexts(IEnumerable<Arc> arcs) =>
        arcs.SelectMany(a => a.Beats.SelectMany(b => new[] { ($"{a.Id}/{b.Tag}/headline", b.Headline), ($"{a.Id}/{b.Tag}/body", b.Body), ($"{a.Id}/{b.Tag}/tag", b.Tag) }));

    [Fact]
    public void NoBeatText_CarriesADigit_AnUnknownToken_OrAHiddenWord()
    {
        foreach (var (where, text) in BeatTexts(Arcs.All))
        {
            Assert.False(text.Any(char.IsDigit), $"digit in {where}");
            foreach (Match m in Token.Matches(text)) Assert.Contains(m.Groups[1].Value, KnownTokens);
            string lower = text.ToLowerInvariant();
            foreach (var w in HiddenWords) Assert.False(lower.Contains(w), $"'{w}' in {where}");
            Assert.False(text.Contains("  "), where);
            Assert.True(text.Trim() == text && text.Length >= 4, where);
        }
    }

    [Fact]
    public void NoBeatText_ClaimsAFirst_AnOnly_OrANever()
    {
        // A beat is told whichever way the world went: it may not assert what the sim can overturn.
        var claim = new Regex(@"\b(first|only|never|always|every|nobody|ever)\b", RegexOptions.IgnoreCase);
        foreach (var (where, text) in BeatTexts(Arcs.All).Where(x => !x.Where.EndsWith("/tag")))
            Assert.False(claim.IsMatch(text), $"{where}: {text}");
    }

    [Fact]
    public void TheOriginsArcs_AreEraSafeForNineteenNinetyThree()
    {
        foreach (var (where, text) in BeatTexts(ArcsOrigins.All))
        {
            string lower = text.ToLowerInvariant();
            foreach (var w in AnachronismsIn1993) Assert.False(lower.Contains(w), $"'{w}' in {where}");
        }
        foreach (var b in ArcsOrigins.All.SelectMany(a => a.Beats))
            Assert.DoesNotContain(b.When, new[] { Cond.TitleWon, Cond.TitleWonFrom, Cond.TitleLost, Cond.Defended, Cond.DefendedVs, Cond.Defences, Cond.LeftOrg });
    }

    // ---------------------------------------------------------------- every beat can fire, once

    private sealed class Undo
    {
        private readonly List<Action> _undo = new();
        public void Add(Action a) => _undo.Add(a);
        public void Run() { for (int i = _undo.Count - 1; i >= 0; i--) _undo[i](); _undo.Clear(); }
    }

    private static Event AddEvent(World w, DateOnly date, EventStatus status, Undo u)
    {
        var promo = w.Promotions.Values.First(p => !p.Defunct);
        var ev = new Event { Id = w.NewEventId(), Promotion = promo.Id, Name = $"Arc Night {date}", Date = date, Status = status };
        w.Events[ev.Id] = ev;
        u.Add(() => w.Events.Remove(ev.Id));
        return ev;
    }

    private static Fight AddBout(World w, Event ev, Fighter red, Fighter blue, Fighter? winner, FightMethod method = FightMethod.UnanimousDecision)
    {
        var fight = new Fight { Id = w.NewFightId(), RedCorner = red.Id, BlueCorner = blue.Id, WeightClass = red.WeightClass };
        if (ev.Status == EventStatus.Completed)
            fight.Outcome = new FightOutcome
            {
                Winner = winner?.Id, Loser = winner is null ? null : (winner.Id == red.Id ? blue.Id : red.Id),
                Method = method, EndRound = 3, EndTimeSeconds = 300,
            };
        ev.Card.Add(fight);
        return fight;
    }

    /// <summary>Make the beat's condition true in the world as of <paramref name="d"/>.</summary>
    private static void Arrange(World w, Arc arc, Beat b, DateOnly d, Undo u)
    {
        var lead = w.Fighters.Values.Single(f => f.Designed && $"{f.FirstName} {f.LastName}" == arc.Lead);
        var foil = w.Fighters.Values.Single(f => f.Designed && $"{f.FirstName} {f.LastName}" == arc.Foil);
        var who = b.Swap ? foil : lead;
        var other = b.Swap ? lead : foil;
        var third = w.Fighters.Values.First(f => f.Designed && f.Id != lead.Id && f.Id != foil.Id);
        var recent = d.AddDays(-1);

        switch (b.When)
        {
            case Cond.Booked:
                AddBout(w, AddEvent(w, d.AddDays(30), EventStatus.Scheduled, u), lead, foil, null);
                break;
            case Cond.Fought:
                AddBout(w, AddEvent(w, recent, EventStatus.Completed, u), lead, foil, lead);
                break;
            case Cond.Rematch:
                AddBout(w, AddEvent(w, d.AddDays(-300), EventStatus.Completed, u), lead, foil, foil);
                AddBout(w, AddEvent(w, recent, EventStatus.Completed, u), lead, foil, lead);
                break;
            case Cond.Won:
                AddBout(w, AddEvent(w, recent, EventStatus.Completed, u), lead, foil, who);
                break;
            case Cond.Finished:
                AddBout(w, AddEvent(w, recent, EventStatus.Completed, u), lead, foil, who, FightMethod.Knockout);
                break;
            case Cond.TitleWon:
            case Cond.TitleWonFrom:
            case Cond.TitleLost:
            case Cond.Defended:
            case Cond.DefendedVs:
            {
                var t = w.Titles.Values.OrderBy(x => x.Id.Value).First();
                var was = t.CurrentChampion;
                var added = new List<TitleReign>();
                var foe = b.When is Cond.TitleWonFrom or Cond.DefendedVs ? other : third;
                Fighter winner, loser;
                if (b.When == Cond.TitleLost) { winner = third; loser = who; }
                else { winner = who; loser = foe; }
                if (b.When is Cond.TitleWon or Cond.TitleWonFrom or Cond.TitleLost)
                {
                    // The loser held it and lost it on the night; the winner's reign opens on the night.
                    var r0 = new TitleReign { Champion = loser.Id, WonOn = d.AddDays(-100), LostOn = recent };
                    var r1 = new TitleReign { Champion = winner.Id, WonOn = recent };
                    t.Lineage.Add(r0); t.Lineage.Add(r1); added.Add(r0); added.Add(r1);
                    t.CurrentChampion = winner.Id;
                }
                else
                {
                    // A defence: the champion's reign began long before the night.
                    var r0 = new TitleReign { Champion = who.Id, WonOn = d.AddDays(-200) };
                    t.Lineage.Add(r0); added.Add(r0);
                    t.CurrentChampion = who.Id;
                }
                var fight = AddBout(w, AddEvent(w, recent, EventStatus.Completed, u), winner, loser, winner);
                fight.IsTitleFight = true; fight.Title = t.Id;
                u.Add(() => { t.CurrentChampion = was; foreach (var r in added) t.Lineage.Remove(r); });
                break;
            }
            case Cond.Defences:
            {
                var t = w.Titles.Values.OrderBy(x => x.Id.Value).First();
                var was = t.CurrentChampion;
                t.CurrentChampion = who.Id;
                var r = new TitleReign { Champion = who.Id, WonOn = d.AddDays(-200), Defenses = b.N };
                t.Lineage.Add(r);
                u.Add(() => { t.CurrentChampion = was; t.Lineage.Remove(r); });
                break;
            }
            case Cond.WonStreak:
            {
                int was = who.Record.CurrentStreak;
                who.Record.CurrentStreak = b.N;
                u.Add(() => who.Record.CurrentStreak = was);
                AddBout(w, AddEvent(w, recent, EventStatus.Completed, u), who, third, who);
                break;
            }
            case Cond.LostStreak:
            {
                int was = who.Record.CurrentStreak;
                who.Record.CurrentStreak = -b.N;
                u.Add(() => who.Record.CurrentStreak = was);
                AddBout(w, AddEvent(w, recent, EventStatus.Completed, u), who, third, third);
                break;
            }
            case Cond.Retired:
            {
                var was = who.Status;
                who.Status = FighterStatus.Retired;
                u.Add(() => who.Status = was);
                break;
            }
            case Cond.LeftOrg:
            {
                var was = who.Contract;
                who.Contract = null;
                u.Add(() => who.Contract = was);
                break;
            }
            case Cond.SignedWith:
            {
                var was = who.Contract;
                var promo = b.Org is null ? w.Promotions.Values.First(p => !p.Defunct) : w.Promotions.Values.First(p => p.Abbreviation == b.Org);
                who.Contract = new Contract { Promotion = promo.Id, SignedOn = recent };
                u.Add(() => who.Contract = was);
                break;
            }
            case Cond.FeudHot:
            {
                var r = new Rivalry { A = lead.Id, B = foil.Id, Intensity = b.N + 5, Since = recent };
                w.Rivalries.Add(r);
                u.Add(() => w.Rivalries.Remove(r));
                break;
            }
        }
    }

    private static void FiresOnceInItsWorld(World w, IEnumerable<Arc> arcs)
    {
        var date = w.CurrentDate.AddDays(400);
        int step = 0;
        foreach (var arc in arcs)
        {
            var lead = w.Fighters.Values.Single(f => f.Designed && $"{f.FirstName} {f.LastName}" == arc.Lead);
            foreach (var beat in arc.Beats)
            {
                var d = date.AddDays(60 * step++);
                var u = new Undo();
                int accoladesBefore = lead.Accolades.Count;
                // Every OTHER arc is already told out, so only this one is live (the day-one calendar has some pairs booked).
                foreach (var other in arcs.Concat(ArcsModern.All).Concat(ArcsOrigins.All).Where(x => x != arc && x.Mode == arc.Mode))
                    if (w.Fighters.Values.SingleOrDefault(f => f.Designed && $"{f.FirstName} {f.LastName}" == other.Lead) is { } ol)
                        foreach (var ob in other.Beats) ol.Accolades.Add(new Accolade(d.AddDays(-100), Arcs.AccoladeTitle(other, ob)));
                // The road so far: every earlier slot already told, a month ago.
                foreach (var prior in arc.Beats.Where(x => x.Slot < beat.Slot).GroupBy(x => x.Slot).Select(g => g.First()))
                    lead.Accolades.Add(new Accolade(d.AddDays(-30), Arcs.AccoladeTitle(arc, prior)));

                Arrange(w, arc, beat, d, u);
                var told = Arcs.RunWeekly(w, d);
                Assert.True(told.Any(t => t.Arc == arc && t.Beat == beat), $"{arc.Id}/{beat.Tag} ({beat.When}) did not fire");

                // Told once: the accolade is on the Lead, the news is in the feed, and a second pass says nothing more.
                Assert.Contains(lead.Accolades, a => a.Title == Arcs.AccoladeTitle(arc, beat));
                var item = w.News.Last(n => n.AboutFighter == lead.Id && n.Category == beat.Category && n.Date == d);
                Assert.DoesNotContain("{", item.Headline + item.Body);
                Assert.DoesNotContain(Arcs.RunWeekly(w, d.AddDays(7)), t => t.Arc == arc && t.Beat.Slot == beat.Slot);
                Assert.DoesNotContain(Arcs.RunWeekly(w, d.AddDays(14)), t => t.Arc == arc && t.Beat.Slot == beat.Slot);
                Assert.Equal(1, lead.Accolades.Count(a => a.Title == Arcs.AccoladeTitle(arc, beat)));

                // Put the world back for the next beat.
                u.Run();
                lead.Accolades.RemoveRange(accoladesBefore, lead.Accolades.Count - accoladesBefore);
                foreach (var f in w.Fighters.Values) f.Accolades.RemoveAll(a => a.Title.StartsWith(Arcs.AccoladePrefix, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void EveryModernBeat_CanFire_AndFiresOnce() => FiresOnceInItsWorld(new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 }), ArcsModern.All);

    [Fact]
    public void EveryOriginsBeat_CanFire_AndFiresOnce() => FiresOnceInItsWorld(OriginsWorldTests.AiWorld(11), ArcsOrigins.All);

    [Fact]
    public void ABeat_WaitsForTheOneBeforeIt_AndForItsOwnMoment()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 12 });
        var arc = ArcsModern.All.Single(a => a.Id == "seo-mercer");
        var d = w.CurrentDate.AddDays(400);
        var u = new Undo();

        // Slot two's condition is true, but slot one has not been told: nothing fires for the arc.
        Arrange(w, arc, arc.Beats.First(b => b.Slot == 2 && !b.Swap), d, u);
        Assert.DoesNotContain(Arcs.RunWeekly(w, d), t => t.Arc == arc);
        // With nothing true, nothing fires.
        u.Run();
        Assert.DoesNotContain(Arcs.RunWeekly(w, d.AddDays(7)), t => t.Arc == arc);
                var lead = w.Fighters.Values.Single(f => $"{f.FirstName} {f.LastName}" == arc.Lead);
        var foil = w.Fighters.Values.Single(f => $"{f.FirstName} {f.LastName}" == arc.Foil);
        AddBout(w, AddEvent(w, d.AddDays(-40), EventStatus.Scheduled, u), lead, foil, null);
        Assert.Single(Arcs.RunWeekly(w, d.AddDays(60)).Where(t => t.Arc == arc));   // booked, still on the card: slot one tells
        // ...and a once-told slot is not told again, nor the next one without its bout.
        Assert.DoesNotContain(Arcs.RunWeekly(w, d.AddDays(67)), t => t.Arc == arc);
    }

    [Fact]
    public void ABeat_IsSafe_WhenItsPeopleHaveRetiredOrLeft()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 13 });
        foreach (var arc in ArcsModern.All)
        {
            foreach (var name in new[] { arc.Lead, arc.Foil })
            {
                var f = w.Fighters.Values.Single(x => x.Designed && $"{x.FirstName} {x.LastName}" == name);
                f.Status = FighterStatus.Retired;
                f.Contract = null;
            }
        }
        // Nobody is left to fight: nothing may throw and nothing may fire on a fight that never happened.
        var told = Arcs.RunWeekly(w, w.CurrentDate.AddDays(7));
        Assert.All(told, t => Assert.Equal(Cond.Retired, t.Beat.When));   // a card that still lists a retired man is not a booking
    }

    [Fact]
    public void TheWeeklyPass_DoesNotFlood_AndTellsOnlyItsOwnMode()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 14 });
        // Make every arc's first beat true at once.
        var u = new Undo();
        var d = w.CurrentDate.AddDays(400);
        foreach (var arc in ArcsModern.All)
        {
            var first = arc.Beats.First(b => b.Slot == 1);
            if (first.When is Cond.Booked or Cond.Fought or Cond.WonStreak or Cond.SignedWith or Cond.Retired or Cond.TitleWon or Cond.Defended)
                Arrange(w, arc, first, d, u);
        }
        var told = Arcs.RunWeekly(w, d);
        Assert.InRange(told.Count, 1, Arcs.MaxPerWeek);   // never more than the cap in a week
        // And the next week, the rest follow, never the same one twice.
        var next = Arcs.RunWeekly(w, d.AddDays(7));
        Assert.Empty(next.Intersect(told));
        // A Modern world never tells an Origins arc.
        Assert.All(told.Concat(next), t => Assert.Equal(WorldMode.Modern, t.Arc.Mode));
    }

    // ---------------------------------------------------------------- the tick

    private static bool IsSaga(Accolade a) => Arcs.IsSagaMark(a.Title);

    private static List<string> SagaMarks(World w) =>
        w.Fighters.Values.OrderBy(f => f.Id.Value).SelectMany(f => f.Accolades.Where(IsSaga).Select(a => $"{f.Id.Value}|{a.Date}|{a.Title}")).ToList();

    private static bool IsOwnerVoice(NewsItem n) =>
        n.Category == NewsCategory.General && OwnerAgendas.All.Any(o => n.Headline.StartsWith(o.Owner + " on ", StringComparison.Ordinal));

    [Theory]
    [InlineData(21UL)]
    [InlineData(22UL)]
    public void TheRealTick_TellsArcs_AndTheOwnersSpeakAboutWhatTheTickItselfPosted(ulong seed)
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        w.PlayerPromotion = default;   // an observer: every owner may speak
        new WorldTick().AdvanceWeeks(w, 78);

        // Arcs: something was told, only ever on a designed lead, and each mark is a real beat.
        Assert.NotEmpty(SagaMarks(w));
        foreach (var f in w.Fighters.Values.Where(f => f.Accolades.Any(IsSaga)))
        {
            Assert.True(f.Designed);
            foreach (var a in f.Accolades.Where(IsSaga))
                Assert.True(Arcs.All.Any(arc => arc.Lead == $"{f.FirstName} {f.LastName}" && arc.Beats.Any(b => Arcs.AccoladeTitle(arc, b) == a.Title)), a.Title);
        }

        // Owners: quotes exist, and each answers a signing / title / retirement item of the same day about the same fighter.
        var voices = w.News.Where(IsOwnerVoice).ToList();
        Assert.NotEmpty(voices);
        foreach (var v in voices)
        {
            Assert.StartsWith("\u201C", v.Body);
            Assert.DoesNotContain("{", v.Body);
            Assert.DoesNotContain("\"", v.Body);   // no nickname quotes inside the quoted line
            if (v.Date > w.CurrentDate.AddDays(-84))   // (an older source may have left the capped feed already)
            Assert.True(w.News.Any(n => n.Date <= v.Date && n.Date > v.Date.AddDays(-7) && n.AboutFighter == v.AboutFighter
                                         && (n.Category == NewsCategory.Signing || n.Category == NewsCategory.TitleChange || n.Category == NewsCategory.Retirement)), $"no source item for the owner line on {v.Date}");
        }
    }

    [Fact]
    public void TheMonthlyBlocksNews_GetsOwnerQuotes_NotOnlyTheWeeklyOnes()
    {
        // The month's retirements, expiries, poachings and free-agent signings post news dated the day the month turns. Over
        // a year or two of a built world some of the quoted items must be of those kinds (they are posted after the old
        // hook position, so a pass that ran before them would never read them).
        int quotedRetirementsOrSignings = 0;   // (retirements and poachings are posted by the MONTHLY block only)
        foreach (ulong seed in new[] { 21UL, 22UL })
        {
            var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
            w.PlayerPromotion = default;
            new WorldTick().AdvanceWeeks(w, 104);
            foreach (var v in w.News.Where(IsOwnerVoice))
                if (w.News.Any(n => n.Date <= v.Date && n.Date > v.Date.AddDays(-7) && n.AboutFighter == v.AboutFighter
                                    && n.Headline.Contains(" away from ")))
                    quotedRetirementsOrSignings++;
        }
        Assert.True(quotedRetirementsOrSignings >= 1, $"only {quotedRetirementsOrSignings}");
    }

    [Theory]
    [InlineData(31UL)]
    [InlineData(32UL)]
    public void TheOriginsTick_TellsSomeBeats_OnTwoSeeds(ulong seed)
    {
        var w = OriginsWorldTests.AiWorld(seed);
        new WorldTick().AdvanceWeeks(w, 52 * 8);
        Assert.NotEmpty(SagaMarks(w));
        foreach (var f in w.Fighters.Values.Where(f => f.Accolades.Any(IsSaga)))
            foreach (var a in f.Accolades.Where(IsSaga))
                Assert.True(ArcsOrigins.All.Any(arc => arc.Lead == $"{f.FirstName} {f.LastName}" && arc.Beats.Any(b => Arcs.AccoladeTitle(arc, b) == a.Title)), a.Title);
    }

    [Fact]
    public void ASeedRunTwice_TellsTheSameStories_WithArcsOn()
    {
        List<string> Run()
        {
            var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 41 });
            new WorldTick().AdvanceWeeks(w, 40);
            var l = SagaMarks(w);
            l.AddRange(w.News.Select(n => $"{n.Date}|{n.Category}|{n.Headline}|{n.Body}"));
            return l;
        }
        var a = Run(); var b = Run();
        Assert.True(a.Count > 100);
        Assert.Equal(a, b);
    }

    // ---------------------------------------------------------------- owners

    private static IEnumerable<string> AllOwnerNames() =>
        DesignedStaff.Owners.Select(o => o.Name)
            .Concat(OriginsPromotions.Owners.Select(o => o.Name))
            .Concat(OriginsArrivals.All.Where(a => a.Kind == OriginsArrivalKind.Promotion).Select(a => a.Lead!));

    [Fact]
    public void EveryDesignedOwner_HasAnAgenda_WithAVoiceForEveryMoment()
    {
        foreach (var name in AllOwnerNames())
        {
            var a = OwnerAgendas.Of(name);
            Assert.NotNull(a);
            Assert.EndsWith(".", a!.PageLine);
            Assert.StartsWith("What ", a.PageLine);
            foreach (OwnerMoment m in Enum.GetValues<OwnerMoment>())
                Assert.NotEmpty(OwnerVoices.Lines(a, m));
        }
        Assert.Equal(OwnerAgendas.All.Count, OwnerAgendas.All.Select(a => a.Owner).Distinct().Count());
        Assert.All(OwnerAgendas.All, a => Assert.Contains(a.Owner, AllOwnerNames()));
    }

    [Fact]
    public void OwnerLines_CarryNoNumbers_NoHiddenWords_AndOnlyKnownTokens()
    {
        var known = new[] { "F", "L", "Org" };
        foreach (var a in OwnerAgendas.All)
        {
            var all = new List<string> { a.PageLine };
            foreach (OwnerMoment m in Enum.GetValues<OwnerMoment>()) all.AddRange(OwnerVoices.Lines(a, m));
            foreach (var s in all)
            {
                Assert.False(s.Any(char.IsDigit), s);
                foreach (Match t in Token.Matches(s)) Assert.Contains(t.Groups[1].Value, known);
                string lower = s.ToLowerInvariant();
                foreach (var w in HiddenWords) Assert.False(lower.Contains(w), $"'{w}' in {s}");
            }
            // Signing lines name the man; outbid lines name the rival house; nothing else must.
            Assert.All(a.Signs, s => Assert.Contains("{F}", s));
            Assert.All(a.Outbid, s => Assert.Contains("{Org}", s));
            Assert.All(a.Crowns, s => Assert.Contains("{F}", s));
            Assert.All(a.Left, s => Assert.Contains("{F}", s));
            Assert.All(a.Retired, s => Assert.Contains("{F}", s));
        }
    }

    [Fact]
    public void TheOriginsOwners_SpeakIn1993()
    {
        foreach (var name in OriginsPromotions.Owners.Select(o => o.Name).Concat(OriginsArrivals.All.Where(a => a.Kind == OriginsArrivalKind.Promotion).Select(a => a.Lead!)))
        {
            var a = OwnerAgendas.Of(name)!;
            var all = new List<string> { a.PageLine };
            foreach (OwnerMoment m in Enum.GetValues<OwnerMoment>()) all.AddRange(OwnerVoices.Lines(a, m));
            foreach (var s in all)
                foreach (var w in new[] { "stream", "ranking", "weight class", "social media", "internet", "podcast", "online", "contender" })
                    Assert.False(s.ToLowerInvariant().Contains(w), $"'{w}' in {s}");
        }
    }

    [Fact]
    public void TheOrgPage_CarriesTheOwnersAgenda()
    {
        var w = ModernWorld.Value;
        var owned = w.Promotions.Values.Where(p => OwnerAgendas.Of(p, w.Mode) is not null).ToList();
        Assert.True(owned.Count >= 15);
        foreach (var p in owned) Assert.Contains(OwnerAgendas.Of(p, w.Mode)!.PageLine, Biographer.Promotion(w, p));
        var o = OriginsWorld.Value;
        var oOwned = o.Promotions.Values.Where(p => OwnerAgendas.Of(p, o.Mode) is not null).ToList();
        Assert.Equal(3, oOwned.Count);
        foreach (var p in oOwned) Assert.Contains(OwnerAgendas.Of(p, o.Mode)!.PageLine, Biographer.Promotion(o, p));
    }

    [Fact]
    public void TheOwnerRoll_IsAPlainHash_StableAndRoughlyAtItsShare()
    {
        var w = ModernWorld.Value;
        var promo = w.Promotions.Values.First();
        int hits = 0, n = 2000;
        for (int i = 0; i < n; i++)
        {
            var item = new NewsItem(new DateOnly(2027, 1, 1).AddDays(i % 300), NewsCategory.Signing, $"Somebody{i} signs with AFC", "", promo.Id, new FighterId(1));
            ulong a = OwnerVoices.Roll(w, item, promo, OwnerMoment.Signs);
            Assert.Equal(a, OwnerVoices.Roll(w, item, promo, OwnerMoment.Signs));   // stable
            if ((int)(a % 100) < OwnerVoices.Share(OwnerMoment.Signs)) hits++;
        }
        Assert.InRange(hits, (int)(n * 0.28), (int)(n * 0.42));
    }

    [Fact]
    public void ReadingTheNews_FindsTheMomentsOfAnOwnersHouse()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 31 });
        var afc = w.Promotions.Values.Single(p => p.Abbreviation == "AFC");
        var ufl = w.Promotions.Values.Single(p => p.Abbreviation == "UFL");
        var fid = w.Fighters.Values.First().Id;
        var d = new DateOnly(2027, 2, 1);
        NewsItem Item(NewsCategory c, string h, Promotion p) => new(d, c, h, "", p.Id, fid);

        Assert.Equal(new[] { (afc, OwnerMoment.Signs) },
            OwnerVoices.Moments(w, Item(NewsCategory.Signing, "Ann Other signs with AFC", afc)).Select(m => (m.Promo, m.Moment)));
        var away = OwnerVoices.Moments(w, Item(NewsCategory.Signing, "AFC signs Ann Other away from UFL", afc)).ToList();
        Assert.Equal(new[] { (afc, OwnerMoment.Signs), (ufl, OwnerMoment.Outbid) }, away.Select(m => (m.Promo, m.Moment)));
        Assert.Equal(afc, away[1].Other);
        Assert.Equal(OwnerMoment.Crowns, OwnerVoices.Moments(w, Item(NewsCategory.TitleChange, "A dethrones B for the Belt", afc)).Single().Moment);
        Assert.Equal(OwnerMoment.Left, OwnerVoices.Moments(w, Item(NewsCategory.TitleChange, "A vacates the Belt", afc)).Single().Moment);
        Assert.Equal(OwnerMoment.Retired, OwnerVoices.Moments(w, Item(NewsCategory.Retirement, "A announces retirement", afc)).Single().Moment);
        Assert.Equal(OwnerMoment.Signs, OwnerVoices.Moments(w, Item(NewsCategory.Signing, "Ann Other signs with Apex Fighting Championship after a bidding war", afc)).Single().Moment);   // BiddingWars now names the fighter
        Assert.Empty(OwnerVoices.Moments(w, Item(NewsCategory.Result, "A beats B", afc)));
        Assert.Empty(OwnerVoices.Moments(w, Item(NewsCategory.General, "Katherine Ross on Other", afc)));   // the owners do not answer themselves
    }

    [Fact]
    public void AnOwnersWords_AreSaidInTheirHouseAboutTheirMan()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 32 });
        w.PlayerPromotion = default;   // an observer world: every owner speaks
        var afc = w.Promotions.Values.Single(p => p.Abbreviation == "AFC");
        var f = w.Fighters.Values.First(x => !x.Designed);
        int said = 0;
        for (int i = 0; i < 40; i++)
        {
            var d = new DateOnly(2027, 1, 1).AddDays(i * 8);
            w.CurrentDate = d;
            News.Post(w, d, NewsCategory.TitleChange, $"{f.FullName} claims the vacant Test Belt {i}", "", afc.Id, f.Id);
            OwnerVoices.RunWeekly(w, d);
        }
        foreach (var n in w.News.Where(n => n.Headline.StartsWith("Katherine Ross on ", StringComparison.Ordinal)))
        {
            said++;
            Assert.Equal(f.Id, n.AboutFighter);
            Assert.Contains(f.FullName, n.Body.Replace(f.LastName, f.FullName).Replace(f.FullName + " " + f.LastName, f.FullName));
        }
        Assert.InRange(said, 15, 38);   // seventy in a hundred, give or take
    }
    // ---------------------------------------------------------------- beats that must NOT fire

    private static void TellEarlierSlots(Fighter lead, Arc arc, int upToSlotExclusive, DateOnly when)
    {
        foreach (var prior in arc.Beats.Where(x => x.Slot < upToSlotExclusive).GroupBy(x => x.Slot).Select(g => g.First()))
            lead.Accolades.Add(new Accolade(when, Arcs.AccoladeTitle(arc, prior)));
    }

    private static void TellEveryOtherArc(World w, Arc keep, DateOnly when)
    {
        foreach (var other in ArcsModern.All.Where(a => a != keep))
            if (w.Fighters.Values.SingleOrDefault(f => f.Designed && $"{f.FirstName} {f.LastName}" == other.Lead) is { } ol)
                foreach (var ob in other.Beats) ol.Accolades.Add(new Accolade(when, Arcs.AccoladeTitle(other, ob)));
    }

    [Fact]
    public void ARetiringChampion_WhoseBeltPassesToTheHeir_IsNotDethroned()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 51 });
        var arc = ArcsModern.All.Single(a => a.Id == "seo-mercer");
        var seo = w.Fighters.Values.Single(f => f.Designed && f.LastName == "Seo");
        var mercer = w.Fighters.Values.Single(f => f.Designed && f.LastName == "Mercer");
        var d = w.CurrentDate.AddDays(500);
        TellEveryOtherArc(w, arc, d.AddDays(-200));
        TellEarlierSlots(seo, arc, 4, d.AddDays(-100));

        var title = w.Titles.Values.Single(t => t.CurrentChampion == mercer.Id);
        // Seo is first in line, so the real lifecycle hands HIM the belt when Mercer retires (a new reign, no fight).
        var division = w.Get(title.Promotion)!.Divisions.First(dv => dv.Title == title.Id);
        division.Rankings.Remove(seo.Id); division.Rankings.Insert(0, seo.Id);
        seo.Status = FighterStatus.Active;
        typeof(Lifecycle).GetMethod("VacateBelt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { w, title, d.AddDays(-1) });
        Assert.Equal(seo.Id, title.CurrentChampion);   // he holds the belt without having beaten anybody for it
        Assert.Contains(title.Lineage, r => r.WonOn == d.AddDays(-1));   // the heir's reign exists

        Assert.DoesNotContain(Arcs.RunWeekly(w, d), t => t.Arc == arc);   // before the fix: "Seo takes the belt from Mercer"
        // ...and if the heir was Seo himself, the headline would have been a lie.
    }

    [Fact]
    public void AChampionWhoLeaves_LosesNothing_AndNobodyBeatsHim()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 52 });
        var arc = ArcsModern.All.Single(a => a.Id == "duarte-pires");
        var duarte = w.Fighters.Values.Single(f => f.Designed && f.LastName == "Duarte");
        var d = w.CurrentDate.AddDays(500);
        TellEveryOtherArc(w, arc, d.AddDays(-200));
        TellEarlierSlots(duarte, arc, 3, d.AddDays(-100));   // slots one and two told; slot three is "Duarte loses the belt"

        var promo = w.Get(duarte.Contract!.Promotion)!;
        MmaManager.Core.Sim.Contracts.ContractLifecycle.LeavePromotion(w, duarte, promo.Id, d.AddDays(-1), "he left");
        Assert.Contains(w.Titles.Values, t => t.Lineage.Any(r => r.Champion == duarte.Id && r.LostOn == d.AddDays(-1)));   // the reign ended, without a defeat

        Assert.DoesNotContain(Arcs.RunWeekly(w, d), t => t.Arc == arc && t.Beat.When == Cond.TitleLost);   // before the fix it fired
    }

    [Fact]
    public void TheNightABeltIsWon_IsNotADefence()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 53 });
        var arc = ArcsModern.All.Single(a => a.Id == "brody-ironside");
        var brody = w.Fighters.Values.Single(f => f.Designed && f.LastName == "Brody");
        var third = w.Fighters.Values.First(f => f.Designed && f.Id != brody.Id);
        var d = w.CurrentDate.AddDays(500);
        TellEveryOtherArc(w, arc, d.AddDays(-200));
        var t = w.Titles.Values.OrderBy(x => x.Id.Value).First();
        var u = new Undo();

        // He wins it tonight: a new reign opens on the night. "Brody retains" would be false.
        var r0 = new TitleReign { Champion = third.Id, WonOn = d.AddDays(-90), LostOn = d.AddDays(-1) };
        var r1 = new TitleReign { Champion = brody.Id, WonOn = d.AddDays(-1) };
        t.Lineage.Add(r0); t.Lineage.Add(r1);
        var was = t.CurrentChampion; t.CurrentChampion = brody.Id;
        var f = AddBout(w, AddEvent(w, d.AddDays(-1), EventStatus.Completed, u), brody, third, brody);
        f.IsTitleFight = true; f.Title = t.Id;
        Assert.DoesNotContain(Arcs.RunWeekly(w, d), x => x.Arc == arc);

        // The same man, defending a belt he won long ago, does tell it.
        r1.LostOn = null;
        t.Lineage.Remove(r1); t.Lineage.Add(new TitleReign { Champion = brody.Id, WonOn = d.AddDays(-300) });
        Assert.True(Arcs.RunWeekly(w, d.AddDays(7)).Any(x => x.Arc == arc && x.Beat.When == Cond.Defended));
        t.CurrentChampion = was;
    }

    [Fact]
    public void TwelveBeatsThatComeTrueTogether_AreAllTold_BeforeTheirNewsGoesStale()
    {
        // The cap (a few a week) and the window (a few weeks) together carry a dozen bouts' worth of beats from one busy night;
        // a bout is told for as long as it is inside the window, so nothing a busy week produces is lost.
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 54 });
        var u = new Undo();
        var d = w.CurrentDate.AddDays(500);
        var arranged = new List<(Arc Arc, Beat Beat)>();
        foreach (var arc in ArcsModern.All)
        {
            var beat = arc.Beats.FirstOrDefault(b => b.Slot == 2 && !b.Swap && b.When is Cond.Won or Cond.TitleWonFrom);
            if (beat is null || arranged.Count >= 12) continue;
            var lead = w.Fighters.Values.Single(f => f.Designed && $"{f.FirstName} {f.LastName}" == arc.Lead);
            TellEarlierSlots(lead, arc, 2, d.AddDays(-100));
            Arrange(w, arc, beat, d, u);
            arranged.Add((arc, beat));
        }
        Assert.Equal(12, arranged.Count);
        TellEveryOtherArc(w, arranged[0].Arc, d.AddDays(-200));   // (marks every other arc told; undo for the arranged ones below)
        foreach (var (arc, _) in arranged)
        {
            var lead = w.Fighters.Values.Single(f => f.Designed && $"{f.FirstName} {f.LastName}" == arc.Lead);
            lead.Accolades.RemoveAll(a => a.Title.StartsWith(Arcs.AccoladePrefix + arc.Title + ": ", StringComparison.Ordinal));
            TellEarlierSlots(lead, arc, 2, d.AddDays(-100));
        }
        var told = new HashSet<string>();
        for (int wk = 0; wk < 4; wk++)
            foreach (var t in Arcs.RunWeekly(w, d.AddDays(7 * wk))) told.Add(t.Arc.Id);
        foreach (var (arc, beat) in arranged) Assert.True(told.Contains(arc.Id), $"{arc.Id}/{beat.Tag} was never told");
    }

    [Fact]
    public void ABoutForAVacantBelt_TakesNothingFromTheLoser()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 55 });
        var arc = ArcsModern.All.Single(a => a.Id == "seo-mercer");
        var seo = w.Fighters.Values.Single(f => f.Designed && f.LastName == "Seo");
        var mercer = w.Fighters.Values.Single(f => f.Designed && f.LastName == "Mercer");
        var d = w.CurrentDate.AddDays(500);
        TellEveryOtherArc(w, arc, d.AddDays(-200));
        TellEarlierSlots(seo, arc, 4, d.AddDays(-100));
        var t = w.Titles.Values.OrderBy(x => x.Id.Value).First();
        var u = new Undo();

        // The belt was vacant going in (Mercer's reign ended long ago); Seo beats Mercer for it and opens a reign on the night.
        t.Lineage.Add(new TitleReign { Champion = mercer.Id, WonOn = d.AddDays(-300), LostOn = d.AddDays(-200) });
        var r1 = new TitleReign { Champion = seo.Id, WonOn = d.AddDays(-1) };
        t.Lineage.Add(r1);
        var fight = AddBout(w, AddEvent(w, d.AddDays(-1), EventStatus.Completed, u), seo, mercer, seo);
        fight.IsTitleFight = true; fight.Title = t.Id;
        Assert.DoesNotContain(Arcs.RunWeekly(w, d), x => x.Arc == arc);   // "Seo takes the belt from Mercer" would be false

        // The same bout, with Mercer champion going in and losing it on the night, is that story.
        t.Lineage.RemoveAt(t.Lineage.Count - 2);   // (the old reign)
        t.Lineage.Insert(t.Lineage.Count - 1, new TitleReign { Champion = mercer.Id, WonOn = d.AddDays(-100), LostOn = d.AddDays(-1) });
        Assert.Contains(Arcs.RunWeekly(w, d.AddDays(7)), x => x.Arc == arc && x.Beat.When == Cond.TitleWonFrom);
    }

    [Fact]
    public void ATitleBeatTold_AfterTheBeltHasMovedOnAgain_StillSaysOnlyWhatHappened()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 56 });
        var arc = ArcsModern.All.Single(a => a.Id == "seo-mercer");
        var seo = w.Fighters.Values.Single(f => f.Designed && f.LastName == "Seo");
        var third = w.Fighters.Values.First(f => f.Designed && f.Id != seo.Id && f.LastName != "Mercer");
        var beat = arc.Beats.Single(b => b.When == Cond.TitleWonFrom && !b.Swap);
        var d = w.CurrentDate.AddDays(500);
        TellEveryOtherArc(w, arc, d.AddDays(-200));
        TellEarlierSlots(seo, arc, 4, d.AddDays(-100));
        var u = new Undo();
        Arrange(w, arc, beat, d, u);
        // ...and somebody else has taken the belt off Seo since (the cap delayed the beat): the beat is still told, in the past tense.
        var t = w.Titles.Values.OrderBy(x => x.Id.Value).First();
        t.CurrentChampion = third.Id;
        Assert.Contains(Arcs.RunWeekly(w, d), x => x.Arc == arc && x.Beat == beat);
        var item = w.News.Last(n => n.AboutFighter == seo.Id && n.Date == d);
        Assert.Matches(@"\bwon\b", item.Body);
        Assert.DoesNotMatch(@"\b(holds|now|belongs|is champion)\b", item.Body + " " + item.Headline);
    }

    [Fact]
    public void ABeatPinnedToAHouse_IsTold_WhenThatHouseHasFolded()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 57 });
        var arc = ArcsModern.All.Single(a => a.Id == "huanca-quispe");
        var huanca = w.Fighters.Values.Single(f => f.Designed && f.LastName == "Huanca");
        var d = w.CurrentDate.AddDays(500);
        TellEveryOtherArc(w, arc, d.AddDays(-200));
        TellEarlierSlots(huanca, arc, 2, d.AddDays(-100));
        var cdc = w.Promotions.Values.Single(p => p.Abbreviation == "CDC");
        Assert.DoesNotContain(Arcs.RunWeekly(w, d), x => x.Arc == arc);          // still with the house: nothing to tell
        cdc.Defunct = true;                                                        // the house folds; its men are released
        huanca.Contract = null;
        Assert.Contains(Arcs.RunWeekly(w, d.AddDays(7)), x => x.Arc == arc && x.Beat.When == Cond.LeftOrg);   // he HAS left it
    }

    private static readonly Regex PresentFacts = new(@"\b(champion|champions|challenger|belt|belts|title|holds|not yet)\b", RegexOptions.IgnoreCase);

    [Fact]
    public void ABeatThatDoesNotCheckABelt_SaysNothingAboutAChampionOrABelt()
    {
        // Booked, streaks, rematches, wins between the two, retirements, signings and departures do not prove who holds what.
        var unchecked_ = new[] { Cond.Booked, Cond.WonStreak, Cond.LostStreak, Cond.Rematch, Cond.Won, Cond.Finished, Cond.Fought, Cond.Retired, Cond.SignedWith, Cond.LeftOrg };
        foreach (var arc in Arcs.All)
            foreach (var b in arc.Beats.Where(b => unchecked_.Contains(b.When)))
                foreach (var (where, text) in new[] { ("headline", b.Headline), ("body", b.Body) })
                    Assert.False(PresentFacts.IsMatch(text), $"{arc.Id}/{b.Tag}/{where}: {text}");
    }

    [Fact]
    public void TitleBeats_AreToldInThePastTense()
    {
        var present = new Regex(@"\b(now holds?|holds the|has the \{Belt\}|belongs|is champion|has a belt)\b", RegexOptions.IgnoreCase);
        foreach (var arc in Arcs.All)
            foreach (var b in arc.Beats.Where(b => b.When is Cond.TitleWon or Cond.TitleWonFrom))
            {
                Assert.False(present.IsMatch(b.Body + " " + b.Headline), $"{arc.Id}/{b.Tag}");
                Assert.Matches(@"\bwon\b|\btakes\b|\bwins\b", b.Headline + " " + b.Body);
            }
    }

    // ---------------------------------------------------------------- the words, the second pass

    private static readonly string[] Divisions = { "strawweight", "flyweight", "bantamweight", "featherweight", "lightweight", "welterweight", "middleweight", "heavyweight" };
    private static readonly Regex AgeWords = new(@"\b(twenty|thirty|forty|teenage|-year-old|year-old)\b", RegexOptions.IgnoreCase);

    [Fact]
    public void BeatText_NamesNoHouse_City_Division_OrAge_ItsConditionDoesNotPin()
    {
        var houses = new List<(string Name, string Abbr, string? City)>();
        foreach (var w in new[] { ModernWorld.Value, OriginsWorld.Value })
            foreach (var p in w.Promotions.Values)
                houses.Add((p.Name, p.Abbreviation, p.HomeCity is { } c ? w.Get(c)?.Name : null));
        foreach (var a in OriginsArrivals.All.Where(a => a.Kind == OriginsArrivalKind.Promotion)) houses.Add((a.Name, a.Abbreviation ?? "", a.City));
        var cities = houses.Select(h => h.City).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();

        foreach (var arc in Arcs.All)
            foreach (var b in arc.Beats)
            {
                var pinned = b.Org is null ? null : houses.Where(h => h.Abbr == b.Org).ToList();
                foreach (var (where, text) in new[] { ("headline", b.Headline), ("body", b.Body) })
                {
                    string at = $"{arc.Id}/{b.Tag}/{where}";
                    foreach (var h in houses)
                    {
                        if (pinned is not null && pinned.Contains(h)) continue;
                        Assert.False(text.Contains(h.Name, StringComparison.OrdinalIgnoreCase), $"{at} names {h.Name}");
                        if (h.Abbr.Length >= 3) Assert.False(Regex.IsMatch(text, $@"\b{Regex.Escape(h.Abbr)}\b"), $"{at} names {h.Abbr}");
                    }
                    foreach (var c in cities!)
                        if (pinned is null || !pinned.Any(h => h.City == c))
                            Assert.False(Regex.IsMatch(text, $@"\b{Regex.Escape(c!)}\b"), $"{at} names {c}");
                    foreach (var dv in Divisions) Assert.False(text.Contains(dv, StringComparison.OrdinalIgnoreCase), $"{at} names {dv}");
                    Assert.False(AgeWords.IsMatch(text), $"{at} states an age");
                }
            }
    }

    [Fact]
    public void ATitleBeat_NamesItsBeltByToken_AndNoBeatClaimsAnythingAboutWhen()
    {
        foreach (var b in Arcs.All.SelectMany(a => a.Beats).Where(b => b.When is Cond.TitleWon or Cond.TitleWonFrom or Cond.TitleLost or Cond.Defended or Cond.DefendedVs))
            Assert.Contains("{Belt}", b.Headline);
        var when = new Regex(@"\b(tonight|today|this week|last night|yesterday|just now|at last|finally)\b", RegexOptions.IgnoreCase);
        foreach (var (where, text) in BeatTexts(Arcs.All).Where(x => !x.Where.EndsWith("/tag")))
            Assert.False(when.IsMatch(text), $"{where}: {text}");
    }

    [Fact]
    public void TheOriginsArcs_NeverOpenOnABookedCard()
    {
        foreach (var arc in ArcsOrigins.All)
        {
            Assert.DoesNotContain(arc.Beats, b => b.When == Cond.Booked);
            // Every opening step can happen without a card being announced ahead: a meeting, a win streak, a signing.
            Assert.All(arc.Beats.Where(b => b.Slot == 1), b => Assert.Contains(b.When, new[] { Cond.Won, Cond.WonStreak, Cond.SignedWith }));
        }
    }

    [Fact]
    public void StoryMarks_AreNotHonours_AndTheScreensCanTellThemApart()
    {
        foreach (var arc in Arcs.All)
            foreach (var b in arc.Beats)
            {
                string t = Arcs.AccoladeTitle(arc, b);
                Assert.True(Arcs.IsSagaMark(t));
                // None of the shapes a screen or a system reads as an honour.
                Assert.False(t.EndsWith("gold") || t.EndsWith("silver") || t.EndsWith("bronze"), t);
                Assert.False(t.Contains("of the Year") || t.StartsWith("Won ") || t.StartsWith("Signed at ") || t.StartsWith("Discovered") || t.StartsWith("Founding class"), t);
            }
        Assert.False(Arcs.IsSagaMark("2027 Fighter of the Year"));
        Assert.False(Arcs.IsSagaMark("Won Crucible Challenge"));
    }

    // ---------------------------------------------------------------- the owners, the second pass

    private static readonly string[] GenderedWords =
        { "he", "him", "his", "himself", "she", "her", "hers", "herself", "man", "men", "woman", "women", "boy", "girl", "guy", "lad", "lady", "gentleman", "sir", "madam" };

    [Fact]
    public void EveryOwnerLine_ReadsRight_ForAManAndForAWoman()
    {
        foreach (var a in OwnerAgendas.All)
            foreach (OwnerMoment m in Enum.GetValues<OwnerMoment>())
                foreach (var line in OwnerVoices.Lines(a, m))
                    foreach (var (first, last) in new[] { ("Alex", "Sample"), ("Alexandra", "Sample") })
                    {
                        string text = line.Replace("{F}", $"{first} {last}").Replace("{L}", last).Replace("{Org}", "AFC");
                        Assert.DoesNotContain("\"", text);   // no nickname quotes inside the quoted line
                        foreach (var word in Regex.Matches(text.ToLowerInvariant(), @"[a-z']+").Select(x => x.Value))
                            Assert.False(GenderedWords.Contains(word), $"'{word}' in {a.Owner}: {text}");
                    }
    }

    [Fact]
    public void AnOwnersQuote_NamesTheFighterByFirstAndLastName_NeverTheNickname()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 61 });
        w.PlayerPromotion = default;
        var afc = w.Promotions.Values.Single(p => p.Abbreviation == "AFC");
        var f = w.Fighters.Values.First(x => x.Designed && x.Nickname is not null);
        Assert.Contains("\"", f.FullName);   // the nickname is in FullName, in straight quotes
        for (int i = 0; i < 60; i++)
        {
            var d = new DateOnly(2027, 1, 1).AddDays(i * 8);
            w.CurrentDate = d;
            News.Post(w, d, NewsCategory.TitleChange, $"{f.FullName} claims the vacant Test Belt {i}", "", afc.Id, f.Id);
            OwnerVoices.RunWeekly(w, d);
        }
        var voices = w.News.Where(IsOwnerVoice).ToList();
        Assert.NotEmpty(voices);
        Assert.All(voices, v => Assert.DoesNotContain("\"", v.Body));
        Assert.All(voices, v => Assert.DoesNotContain(f.Nickname!, v.Body));
    }

    [Fact]
    public void AnAgenda_AttachesOnlyToItsDesignedHouse_ItsOwnerAndItsEra()
    {
        var o = OriginsWorldTests.AiWorld(71);
        var cru = o.Promotions.Values.Single(p => p.Abbreviation == "CRU");
        Assert.NotNull(OwnerAgendas.Of(cru, WorldMode.Origins));
        Assert.Null(OwnerAgendas.Of(cru, WorldMode.Modern));                  // the right house in the wrong era

        // A house founded later whose random owner happens to share a designed owner's name gets nothing.
        var name = cru.OwnerName;
        var abbr = cru.Abbreviation;
        cru.Abbreviation = "ZZZ";
        Assert.Null(OwnerAgendas.Of(cru, WorldMode.Origins));                 // the right name on the wrong house
        cru.Abbreviation = abbr;
        cru.OwnerName = "Somebody Else";
        Assert.Null(OwnerAgendas.Of(cru, WorldMode.Origins));                 // the right house with another owner
        cru.OwnerName = name;
        Assert.NotNull(OwnerAgendas.Of(cru, WorldMode.Origins));

        // And a Modern owner's agenda never attaches in an Origins world, even to a house that carries the abbreviation.
        var afc = ModernWorld.Value.Promotions.Values.Single(p => p.Abbreviation == "AFC");
        Assert.Null(OwnerAgendas.Of(afc, WorldMode.Origins));
    }

    [Fact]
    public void ARenewal_IsNotASigning_ForTheOwnersVoice()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 31 });
        var afc = w.Promotions.Values.Single(p => p.Abbreviation == "AFC");
        var item = new NewsItem(new DateOnly(2027, 2, 1), NewsCategory.Signing, "Ann Other re-signs with AFC", "", afc.Id, w.Fighters.Values.First().Id);
        Assert.Empty(OwnerVoices.Moments(w, item));
    }

    // ---------------------------------------------------------------- the sample sheet

    private static string When(Beat b, Arc arc) => b.When switch
    {
        Cond.Booked => $"when {arc.Lead} and {arc.Foil} are announced on the same card",
        Cond.Fought => $"when {arc.Lead} and {arc.Foil} fight",
        Cond.Rematch => $"when {arc.Lead} and {arc.Foil} fight again",
        Cond.Won => b.Swap ? $"when {arc.Foil} beats {arc.Lead}" : $"when {arc.Lead} beats {arc.Foil}",
        Cond.Finished => b.Swap ? $"when {arc.Foil} finishes {arc.Lead}" : $"when {arc.Lead} finishes {arc.Foil}",
        Cond.TitleWonFrom => b.Swap ? $"when {arc.Foil} takes a belt from {arc.Lead}" : $"when {arc.Lead} takes a belt from {arc.Foil}",
        Cond.TitleWon => $"when {(b.Swap ? arc.Foil : arc.Lead)} becomes champion",
        Cond.TitleLost => $"when {(b.Swap ? arc.Foil : arc.Lead)} loses a belt in the cage",
        Cond.Defended => $"when {(b.Swap ? arc.Foil : arc.Lead)} defends a belt successfully",
        Cond.DefendedVs => b.Swap ? $"when {arc.Foil} defends a belt against {arc.Lead}" : $"when {arc.Lead} defends a belt against {arc.Foil}",
        Cond.Defences => $"when {(b.Swap ? arc.Foil : arc.Lead)}'s reign reaches {b.N} defences",
        Cond.WonStreak => $"when {(b.Swap ? arc.Foil : arc.Lead)} wins again with a run of {b.N} or more",
        Cond.LostStreak => $"when {(b.Swap ? arc.Foil : arc.Lead)} loses again with a skid of {b.N} or more",
        Cond.Retired => $"when {(b.Swap ? arc.Foil : arc.Lead)} retires",
        Cond.LeftOrg => $"when {arc.Lead} is no longer with {b.Org}",
        Cond.SignedWith => b.Org is null ? $"when {arc.Lead} signs with any house" : $"when {arc.Lead} signs with {b.Org}",
        Cond.FeudHot => $"when the feud runs hot",
        _ => "",
    };

    [Fact]
    public void Showcase_WritesTheSampleSheet()
    {
        string? path = Environment.GetEnvironmentVariable("ARCS_SAMPLES_PATH");
        if (string.IsNullOrEmpty(path)) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("STORY ARCS AND OWNER AGENDAS: SAMPLES");
        sb.AppendLine("What the player reads as the world earns each beat. A story's beats come in order; where two beats share a step, the world tells whichever way the result went.");
        sb.AppendLine("Records and house names in angle brackets are filled from the world at the moment the beat is told (the sample uses made-up numbers).");
        sb.AppendLine();
        string[] pick =
        {
            "seo-mercer", "donovan-volkova", "kang-sato", "rivas-sales", "mokoena-ndlovu", "moyo-orourke", "vela-morita",
            "huanca-quispe", "saengkham-temple", "tanner-hale", "suwaidi-falcon", "diallo-gazelle", "brody-ironside",
            "quintela-paranhos", "zandvliet-delacroix", "skaggs-quirke", "thanasarn-yoon",
        };
        string Fill(string t) => t.Replace("{AOrg}", "<house>").Replace("{BOrg}", "<house>").Replace("{ARec}", "<record>").Replace("{BRec}", "<record>").Replace("{Belt}", "<the belt>");
        foreach (var id in pick)
        {
            var arc = Arcs.All.Single(a => a.Id == id);
            sb.AppendLine($"=== {arc.Title}  ({(arc.Mode == WorldMode.Origins ? "Origins" : "Modern")}; {arc.Lead} and {arc.Foil}) ===");
            foreach (var slot in arc.Beats.Select(b => b.Slot).Distinct().OrderBy(x => x))
            {
                var alts = arc.Beats.Where(b => b.Slot == slot).ToList();
                sb.AppendLine(alts.Count > 1 ? $"Step {slot} (one of {alts.Count}, whichever way it goes):" : $"Step {slot}:");
                foreach (var b in alts)
                {
                    sb.AppendLine($"  [{When(b, arc)}]");
                    sb.AppendLine($"  Headline: {Fill(b.Headline)}");
                    sb.AppendLine($"  {Fill(b.Body)}");
                    sb.AppendLine();
                }
            }
        }
        sb.AppendLine("OWNER AGENDAS");
        sb.AppendLine("Each owner has a line on the org page and speaks, now and then, when their house signs a name, crowns a champion, loses a star or is outbid. Sample fighter: Alex Sample.");
        sb.AppendLine();
        string[] owners = { "Katherine Ross", "Baron Aleksander Reuss", "Pavel Ilic", "Hideo Kanemura", "Sheikh Faisal bin Rashid", "Bill Hennessy", "Dona Marisa Bittencourt", "Marty Feldstein", "Ernesto Chambi", "Leonard Pastor" };
        foreach (var name in owners)
        {
            var a = OwnerAgendas.Of(name)!;
            sb.AppendLine($"--- {a.Owner} ({a.Wants}) ---");
            sb.AppendLine($"Org page: {a.PageLine}");
            foreach (OwnerMoment m in Enum.GetValues<OwnerMoment>())
            {
                string line = OwnerVoices.Lines(a, m)[0].Replace("{F}", "Alex Sample").Replace("{L}", "Sample").Replace("{Org}", "AFC");
                string label = m switch { OwnerMoment.Signs => "Signs a name", OwnerMoment.Crowns => "Crowns a champion", OwnerMoment.Left => "Loses a star who leaves", OwnerMoment.Retired => "Loses a star who retires", _ => "Is outbid" };
                sb.AppendLine($"  {label}: “{line}”");
            }
            sb.AppendLine();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
    }
}
