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
    private static readonly string[] KnownTokens = { "AOrg", "BOrg", "ARec", "BRec", "Belt" };
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
            {
                var t = w.Titles.Values.OrderBy(x => x.Id.Value).First();
                var added = new List<TitleReign>();
                if (b.When == Cond.TitleWonFrom)
                {
                    var r0 = new TitleReign { Champion = other.Id, WonOn = d.AddDays(-100), LostOn = recent };
                    t.Lineage.Add(r0); added.Add(r0);
                }
                if (b.When == Cond.TitleLost)
                {
                    var r = new TitleReign { Champion = who.Id, WonOn = d.AddDays(-100), LostOn = recent };
                    t.Lineage.Add(r); added.Add(r);
                }
                else
                {
                    var r = new TitleReign { Champion = who.Id, WonOn = recent };
                    t.Lineage.Add(r); added.Add(r);
                }
                u.Add(() => { foreach (var r in added) t.Lineage.Remove(r); });
                break;
            }
            case Cond.Defended:
            case Cond.DefendedVs:
            {
                var t = w.Titles.Values.OrderBy(x => x.Id.Value).First();
                var was = t.CurrentChampion;
                t.CurrentChampion = who.Id;
                u.Add(() => t.CurrentChampion = was);
                var opponent = b.When == Cond.DefendedVs ? other : third;
                var f = AddBout(w, AddEvent(w, recent, EventStatus.Completed, u), who, opponent, who);
                f.IsTitleFight = true; f.Title = t.Id;
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
        Assert.InRange(told.Count, 1, Arcs.MaxPerWeek);
        // And the next week, the rest follow, never the same one twice.
        var next = Arcs.RunWeekly(w, d.AddDays(7));
        Assert.Empty(next.Intersect(told));
        // A Modern world never tells an Origins arc.
        Assert.All(told.Concat(next), t => Assert.Equal(WorldMode.Modern, t.Arc.Mode));
    }

    // ---------------------------------------------------------------- the tick

    [Fact]
    public void AdvancingTheWorld_TellsArcsAndOwners_WithoutTouchingTheFeedShape()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 21 });
        var tick = new WorldTick();
        tick.AdvanceWeeks(w, 40);
        var told = w.Fighters.Values.Where(f => f.Accolades.Any(a => a.Title.StartsWith(Arcs.AccoladePrefix, StringComparison.Ordinal))).ToList();
        Assert.All(told, f => Assert.True(f.Designed));
        foreach (var f in told)
            foreach (var a in f.Accolades.Where(a => a.Title.StartsWith(Arcs.AccoladePrefix, StringComparison.Ordinal)))
                Assert.Contains(Arcs.All, arc => arc.Lead == $"{f.FirstName} {f.LastName}" && arc.Beats.Any(b => Arcs.AccoladeTitle(arc, b) == a.Title));
        // Owner voices read like a line from a named owner, about a named fighter.
        var voices = w.News.Where(n => n.Category == NewsCategory.General && n.Headline.Contains(" on ")
                                       && OwnerAgendas.All.Any(o => n.Headline.StartsWith(o.Owner + " on ", StringComparison.Ordinal))).ToList();
        foreach (var v in voices)
        {
            Assert.StartsWith("“", v.Body);
            Assert.DoesNotContain("{", v.Body);
            Assert.NotNull(v.AboutFighter);
        }
    }

    [Fact]
    public void TheOriginsWorld_AlsoTicksCleanly()
    {
        var w = OriginsWorldTests.AiWorld(22);
        new WorldTick().AdvanceWeeks(w, 40);
        foreach (var f in w.Fighters.Values)
            foreach (var a in f.Accolades.Where(a => a.Title.StartsWith(Arcs.AccoladePrefix, StringComparison.Ordinal)))
                Assert.Contains(ArcsOrigins.All, arc => arc.Lead == $"{f.FirstName} {f.LastName}" && arc.Beats.Any(b => Arcs.AccoladeTitle(arc, b) == a.Title));
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
            Assert.All(a.LosesStar, s => Assert.Contains("{F}", s));
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
        var owned = w.Promotions.Values.Where(p => OwnerAgendas.Of(p.OwnerName) is not null).ToList();
        Assert.True(owned.Count >= 15);
        foreach (var p in owned) Assert.Contains(OwnerAgendas.Of(p.OwnerName)!.PageLine, Biographer.Promotion(w, p));
        var o = OriginsWorld.Value;
        foreach (var p in o.Promotions.Values.Where(p => OwnerAgendas.Of(p.OwnerName) is not null))
            Assert.Contains(OwnerAgendas.Of(p.OwnerName)!.PageLine, Biographer.Promotion(o, p));
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
        Assert.Equal(OwnerMoment.LosesStar, OwnerVoices.Moments(w, Item(NewsCategory.TitleChange, "A vacates the Belt", afc)).Single().Moment);
        Assert.Equal(OwnerMoment.LosesStar, OwnerVoices.Moments(w, Item(NewsCategory.Retirement, "A announces retirement", afc)).Single().Moment);
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
                string label = m switch { OwnerMoment.Signs => "Signs a name", OwnerMoment.Crowns => "Crowns a champion", OwnerMoment.LosesStar => "Loses a star", _ => "Is outbid" };
                sb.AppendLine($"  {label}: “{line}”");
            }
            sb.AppendLine();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
    }
}
