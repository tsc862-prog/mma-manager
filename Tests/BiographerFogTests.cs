using MmaManager.Core.Model;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// The profile's generated prose may only say what the player has seen: a hidden strength or flaw must not
/// leak through the Biographer, a revealed one is named from the scouted read, and authored text is untouched.
/// </summary>
public class BiographerFogTests
{
    private const string Power = "one-punch knockout power";
    private const string Flaw = "a shaky submission defense";

    private static (World W, Fighter F) Subject()
    {
        var w = FogKit.Modern();
        var f = w.Fighters.Values.OrderBy(x => x.Id.Value).First(x => x.Status == FighterStatus.Active && x.Bio is null);
        FogKit.SetTrue(f, 60);
        f.PrimaryStyle = PrimaryStyle.WellRounded;   // every channel starts below the Approximate line at 0-0
        FogKit.Unknown(f);
        return (w, f);
    }

    private static void Extremes(Fighter f, int power, int subDef)
    {
        f.Striking.PunchPower = power;
        f.Grappling.SubmissionDefense = subDef;
    }

    private static ScoutReport Seen(Fighter f, bool full) =>
        ScoutingService.Assess(f, ownedByPlayer: full, Array.Empty<BoutObservation>(), scoutingBonus: full ? 9 : 0);

    [Fact]
    public void Unscouted_NamesNeitherTheStrengthNorTheFlaw()
    {
        var (w, f) = Subject();
        Extremes(f, 98, 5);

        var blind = Biographer.Fighter(w, f);                       // no report at all
        var fog = Biographer.Fighter(w, f, Seen(f, full: false));   // a report with nothing revealed on these channels

        foreach (var text in new[] { blind, fog })
        {
            Assert.DoesNotContain(Power, text);
            Assert.DoesNotContain(Flaw, text);
            Assert.DoesNotContain("The knock is", text);
            Assert.DoesNotContain("They shine", text);
        }
    }

    [Fact]
    public void Revealed_NamesBoth_FromTheScoutedRead()
    {
        var (w, f) = Subject();
        Extremes(f, 98, 5);
        f.Record.Wins = 10;   // a real résumé, so the report has something to read

        var text = Biographer.Fighter(w, f, Seen(f, full: true));

        Assert.Contains(Power, text);
        Assert.Contains(Flaw, text);
    }

    [Fact]
    public void ARevealedMiddlingStat_IsNotNamed()
    {
        var (w, f) = Subject();   // everything 60: Strong band, neither elite nor weak
        f.Record.Wins = 10;

        var text = Biographer.Fighter(w, f, Seen(f, full: true));

        Assert.DoesNotContain("They shine", text);
        Assert.DoesNotContain("The knock is", text);
    }

    [Fact]
    public void SameFacts_DifferentHiddenAttributes_SameUnscoutedProse()
    {
        var (w, f) = Subject();
        f.Record.Wins = 6; f.Record.Losses = 1; f.Record.WinsByKo = 4; f.Record.WinsByDecision = 2; f.Record.CurrentStreak = 3;

        Extremes(f, 98, 5);
        var a = Biographer.Fighter(w, f);
        FogKit.SetTrue(f, 35);
        Extremes(f, 20, 95);
        f.Physical.Chin = 99; f.Physical.Cardio = 8;
        var b = Biographer.Fighter(w, f);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Unscouted_ProseStillReadsLikePublicFacts()
    {
        var (w, f) = Subject();
        f.Record.Wins = 8; f.Record.WinsByKo = 6; f.Record.WinsByDecision = 2; f.Record.Losses = 1;

        var text = Biographer.Fighter(w, f);

        Assert.Contains("6", text);                       // how the wins have come is public
        Assert.Contains("look most at home", text);       // style-led, not skill-led
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void DesignedFighters_KeepTheirAuthoredText_WhateverIsScouted()
    {
        var w = FogKit.Modern();
        var mercer = w.Fighters.Values.First(f => f.LastName == "Mercer" && f.Designed);

        var blind = Biographer.Fighter(w, mercer);
        var seen = Biographer.Fighter(w, mercer, Seen(mercer, full: true));

        Assert.StartsWith("The face of the sport", blind);
        Assert.Equal(blind, seen);
    }
}
