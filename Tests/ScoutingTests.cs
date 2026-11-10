using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// Fog of War: the player earns the right to see a fighter's stats through fights.
/// These pin the reveal curve — a known style hints, KOs show power, subs show
/// grappling, the distance shows cardio, and your own roster clears quicker.
/// </summary>
public class ScoutingTests
{
    // career = the fighter's public pro-record length (drives general familiarity).
    private static Fighter Fighter(PrimaryStyle style, int career = 0)
    {
        var f = new Fighter
        {
            Id = new FighterId(1),
            FirstName = "Test",
            LastName = "Fighter",
            BirthDate = new DateOnly(1996, 1, 1),
            PrimaryStyle = style,
        };
        f.Record.Wins = career;
        return f;
    }

    private static BoutObservation Decision(int rounds = 3) => new(
        Win: true, Loss: false, Method: FightMethod.UnanimousDecision, EndRound: rounds, ScheduledRounds: rounds,
        SignificantStrikesLanded: 40, KnockdownsScored: 0, KnockdownsAgainst: 0,
        TakedownsLanded: 0, TakedownsAgainst: 0, SubmissionAttempts: 0, ControlTimeSeconds: 0);

    private static BoutObservation KoWin() => new(
        Win: true, Loss: false, Method: FightMethod.Knockout, EndRound: 1, ScheduledRounds: 3,
        SignificantStrikesLanded: 15, KnockdownsScored: 1, KnockdownsAgainst: 0,
        TakedownsLanded: 0, TakedownsAgainst: 0, SubmissionAttempts: 0, ControlTimeSeconds: 0);

    private static BoutObservation SubLoss() => new(
        Win: false, Loss: true, Method: FightMethod.SubmissionTap, EndRound: 2, ScheduledRounds: 3,
        SignificantStrikesLanded: 5, KnockdownsScored: 0, KnockdownsAgainst: 0,
        TakedownsLanded: 0, TakedownsAgainst: 1, SubmissionAttempts: 0, ControlTimeSeconds: 30);

    private static Promotion Org(int prestige, int id = 1) =>
        new() { Id = new PromotionId(id), Name = "Org", Abbreviation = "ORG", Prestige = prestige };

    // === AI-side Fog of War ==================================================

    [Fact]
    public void PerceivedAbility_IsExactForAProvenVeteran()
    {
        var vet = Fighter(PrimaryStyle.WellRounded, career: 20);   // ≥17 bouts ⇒ no guesswork
        int truth = FighterAbility.CurrentAbility(vet);
        Assert.Equal(truth, ScoutingService.PerceivedAbility(Org(30), vet));
        Assert.Equal(truth, ScoutingService.PerceivedAbility(Org(78), vet));
    }

    [Fact]
    public void PerceivedAbility_MisjudgesAProspect_ButStaysInBounds_AndIsStable()
    {
        var prospect = Fighter(PrimaryStyle.WellRounded, career: 0);
        int truth = FighterAbility.CurrentAbility(prospect);
        int seen = ScoutingService.PerceivedAbility(Org(24), prospect);

        Assert.InRange(seen, Math.Max(1, truth - 15), Math.Min(100, truth + 15));
        Assert.Equal(seen, ScoutingService.PerceivedAbility(Org(24), prospect));   // deterministic
    }

    [Fact]
    public void BiggerOrgs_ScoutMoreAccurately_ThanSmallOnes()
    {
        var prospect = Fighter(PrimaryStyle.WellRounded, career: 2);
        int truth = FighterAbility.CurrentAbility(prospect);
        int smallErr = Math.Abs(ScoutingService.PerceivedAbility(Org(22, id: 1), prospect) - truth);
        int giantErr = Math.Abs(ScoutingService.PerceivedAbility(Org(78, id: 1), prospect) - truth);
        Assert.True(giantErr <= smallErr, $"a giant ({giantErr}) shouldn't misjudge more than a minnow ({smallErr})");
    }

    [Fact]
    public void AnOrgJudgesItsOwnFighters_MoreAccuratelyThanOutsiders()
    {
        var f = Fighter(PrimaryStyle.WellRounded, career: 2);
        int truth = FighterAbility.CurrentAbility(f);
        var org = Org(24, id: 5);

        int outsiderErr = Math.Abs(ScoutingService.PerceivedAbility(org, f) - truth);
        f.Contract = new Contract { Promotion = org.Id, SignedOn = new DateOnly(2025, 1, 1), FightsRemaining = 3 };
        int ownerErr = Math.Abs(ScoutingService.PerceivedAbility(org, f) - truth);

        Assert.True(ownerErr <= outsiderErr, $"you should know your own fighter ({ownerErr}) better than an outsider does ({outsiderErr})");
    }

    [Fact]
    public void AScoutingDepartment_ClearsTheFogFaster()
    {
        // Same fighter, same tape — but a promotion with a real scouting department
        // reads him more clearly. Here it turns a rough band into an exact number.
        var f = Fighter(PrimaryStyle.WellRounded, career: 15);
        var noScouts = ScoutingService.Assess(f, ownedByPlayer: false, new List<BoutObservation>(), scoutingBonus: 0);
        var withScouts = ScoutingService.Assess(f, ownedByPlayer: false, new List<BoutObservation>(), scoutingBonus: 0.5);

        Assert.True(withScouts.Reveal(StatChannel.Cardio) > noScouts.Reveal(StatChannel.Cardio));
        Assert.Equal(StatReveal.Approximate, noScouts.Precision(StatChannel.Cardio));
        Assert.Equal(StatReveal.Exact, withScouts.Precision(StatChannel.Cardio));
    }

    [Fact]
    public void Bands_MapToTheRightWords()
    {
        Assert.Equal(StatBand.Elite, ScoutReport.Band(85));
        Assert.Equal(StatBand.Strong, ScoutReport.Band(70));
        Assert.Equal(StatBand.Average, ScoutReport.Band(50));
        Assert.Equal(StatBand.Weak, ScoutReport.Band(30));
    }

    [Fact]
    public void BrandNewFighter_IsMostlyAMystery_ButHisStyleHints()
    {
        var report = ScoutingService.Assess(Fighter(PrimaryStyle.Wrestler), ownedByPlayer: false, new List<BoutObservation>());

        // No tape at all: cardio, chin, striking are unknown.
        Assert.Equal(StatReveal.Hidden, report.Precision(StatChannel.Cardio));
        Assert.Equal(StatReveal.Hidden, report.Precision(StatChannel.Chin));
        Assert.Equal(StatReveal.Hidden, report.Precision(StatChannel.Striking));
        // But a wrestler's wrestling is at least roughly known from reputation.
        Assert.Equal(StatReveal.Approximate, report.Precision(StatChannel.Wrestling));
    }

    [Fact]
    public void AProvenRecord_LiftsTheFog_EvenWithoutPersonalTape()
    {
        // A 14-0 champion is public knowledge: you know roughly what he's got all
        // over, even if you've never studied a single one of his fights yourself.
        var champ = Fighter(PrimaryStyle.Kickboxer, career: 14);
        var report = ScoutingService.Assess(champ, ownedByPlayer: false, new List<BoutObservation>());

        Assert.Equal(0, report.ObservedFights);
        Assert.NotEqual(StatReveal.Hidden, report.Precision(StatChannel.Cardio));   // record alone ⇒ at least a band
        Assert.NotEqual(StatReveal.Hidden, report.Precision(StatChannel.Chin));
        Assert.NotEqual(StatReveal.Exact, report.Precision(StatChannel.Cardio));    // but not exact without tape
    }

    [Fact]
    public void ARivalYouveNeverStudied_StaysBands_NoMatterHisRecord()
    {
        // A 40-fight outsider with zero tape watched: you know his reputation cold
        // (bands everywhere) but you don't have exact numbers on his gas tank until
        // you actually watch him fight — record alone can't fully solve him.
        var vet = Fighter(PrimaryStyle.Wrestler, career: 40);
        var report = ScoutingService.Assess(vet, ownedByPlayer: false, new List<BoutObservation>());
        Assert.Equal(StatReveal.Approximate, report.Precision(StatChannel.Cardio));
        Assert.Equal(StatReveal.Approximate, report.Precision(StatChannel.Chin));
    }

    [Fact]
    public void PublicStats_AreNeverHidden()
    {
        var report = ScoutingService.Assess(Fighter(PrimaryStyle.WellRounded), ownedByPlayer: false, new List<BoutObservation>());
        Assert.Equal(StatReveal.Exact, report.Precision(StatChannel.Public));
    }

    [Fact]
    public void AVeteran_WithPlentyOfTape_IsFullyScouted()
    {
        var bouts = Enumerable.Range(0, 10).Select(_ => Decision()).ToList();
        var report = ScoutingService.Assess(Fighter(PrimaryStyle.WellRounded, career: 10), ownedByPlayer: false, bouts);

        Assert.Equal(StatReveal.Exact, report.Precision(StatChannel.Striking));
        Assert.Equal(StatReveal.Exact, report.Precision(StatChannel.Cardio));
        Assert.Equal(StatReveal.Exact, report.Precision(StatChannel.Mind));
    }

    [Fact]
    public void HowTheyFought_RevealsTheMatchingStats()
    {
        // A striker who only ever lands KOs: power comes clear fast, but nobody has
        // tested his ground game, so it stays hidden.
        var bouts = Enumerable.Range(0, 3).Select(_ => KoWin()).ToList();
        var report = ScoutingService.Assess(Fighter(PrimaryStyle.Striker, career: 3), ownedByPlayer: false, bouts);

        Assert.Equal(StatReveal.Exact, report.Precision(StatChannel.Power));
        Assert.Equal(StatReveal.Hidden, report.Precision(StatChannel.Ground));
        Assert.Equal(StatReveal.Hidden, report.Precision(StatChannel.Submission));
    }

    [Fact]
    public void GettingFinished_ExposesTheWeakness()
    {
        // Tapping to a rear-naked choke tells everyone about your submission defence.
        var report = ScoutingService.Assess(Fighter(PrimaryStyle.Striker, career: 1), ownedByPlayer: false, new[] { SubLoss() });
        Assert.NotEqual(StatReveal.Hidden, report.Precision(StatChannel.SubmissionDef));
    }

    [Fact]
    public void YourOwnFighters_ClearFasterThanOutsiders()
    {
        var bouts = Enumerable.Range(0, 2).Select(_ => Decision()).ToList();
        var mine = ScoutingService.Assess(Fighter(PrimaryStyle.WellRounded, career: 2), ownedByPlayer: true, bouts);
        var theirs = ScoutingService.Assess(Fighter(PrimaryStyle.WellRounded, career: 2), ownedByPlayer: false, bouts);

        Assert.True(mine.Reveal(StatChannel.Cardio) > theirs.Reveal(StatChannel.Cardio));
        // Two fights in, your own guy's gas tank is already a known number; an
        // outsider you've only watched twice is still just roughly known.
        Assert.Equal(StatReveal.Exact, mine.Precision(StatChannel.Cardio));
        Assert.Equal(StatReveal.Approximate, theirs.Precision(StatChannel.Cardio));
    }
}
