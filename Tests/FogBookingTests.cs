using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Rng;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// Booking decisions an AI house makes once the world is running read the house's scouted view, never the truth:
/// padding a short card, seeding a mid-game startup's rankings and first belt, and pricing a mid-game signing.
/// The card-padding and seeding tests failed on the code before the fix.
/// </summary>
public class FogBookingTests
{
    [Fact]
    public void PaddingAnAiCard_PicksTheHousesTopTwo_NotTheTruesTopTwo()
    {
        var (w, promo, _, y, z, up, down) = FogMatchmakerTests.Setup();
        var date = w.CurrentDate.AddDays(30);
        // Only Y, Up and Down can fight that night; Z is already on the card.
        foreach (var f in w.RosterOf(promo.Id))
            if (f != y && f != up && f != down) f.ReadyToFightOn = date.AddDays(60);
        var other = w.Fighters.Values.First(f => f.Contract?.Promotion != promo.Id && f.Status == FighterStatus.Active);
        var ev = new Event
        {
            Id = w.NewEventId(), Promotion = promo.Id, Name = "Fog Night", Date = date,
            Card = { new Fight { Id = w.NewFightId(), RedCorner = z.Id, BlueCorner = other.Id, WeightClass = z.WeightClass } },
        };

        CardFiller.PadToMinimum(w, ev);

        var bout = ev.Card.Single(b => b.RedCorner == y.Id || b.BlueCorner == y.Id);
        var opponent = bout.RedCorner == y.Id ? bout.BlueCorner : bout.RedCorner;
        Assert.Equal(up.Id, opponent);   // the house's read; the truth would have sent in Down
    }

    [Fact]
    public void AMidGameStartup_CrownsAndRanksOnWhatItCanSee()
    {
        // Two brand-new houses with the same two men as their whole roster in one class. The one seeded the
        // day-one way (on the truth) crowns Lo; the one seeded on its own read (what a mid-game startup now
        // passes) crowns Hi, the man it rates higher though he is a point worse.
        var (w, promo, div, _, _, _, _) = FogMatchmakerTests.Setup();
        Promotion Startup(string abbr)
        {
            var p = new Promotion { Id = w.NewPromotionId(), Name = abbr + " Startup", Abbreviation = abbr, Prestige = 20 };
            w.Promotions[p.Id] = p;
            return p;
        }
        var truthful = Startup("TRU");
        var fogged = Startup("FOG");
        // The old house's men in that class, signed to the new one (the founding class), blank records.
        var pool = w.RosterOf(promo.Id).Where(f => div.Matches(f) && f.Status == FighterStatus.Active).OrderBy(f => f.Id.Value).ToList();
        foreach (var f in pool)
        {
            FogKit.Unknown(f);
            f.Contract = new Contract { Promotion = fogged.Id, SignedOn = w.CurrentDate, FightsRemaining = 3 };
        }
        int Seen(Fighter f) => ScoutingService.PerceivedAbility(w, fogged, f);
        var pair = FogKit.Misread(pool, Seen);
        Assert.NotNull(pair);
        var (hi, lo) = pair!.Value;
        FogKit.Invert(hi, lo, Seen);
        var roster = new[] { hi, lo };

        PromotionSetup.ConfigureDivisions(w, truthful, roster, new[] { div.WeightClass }, w.CurrentDate, 15, null, Sex.Male);
        PromotionSetup.ConfigureDivisions(w, fogged, roster, new[] { div.WeightClass }, w.CurrentDate, 15, null, Sex.Male,
            seed: PromotionSetup.SeedScoreAsSeenBy(w, fogged));

        Assert.Equal(lo.Id, w.Titles.Values.Single(t => t.Promotion == truthful.Id).CurrentChampion);
        Assert.Equal(hi.Id, w.Titles.Values.Single(t => t.Promotion == fogged.Id).CurrentChampion);
        Assert.Equal(new[] { lo.Id }, fogged.Divisions.Single().Rankings);
    }

    [Fact]
    public void AMidGameSigning_IsPricedOnTheRead_AndIgnoresTheTruth()
    {
        var w = FogKit.Modern();
        var promo = w.Promotions.Values.Where(p => !p.Defunct && p.Id != w.PlayerPromotion).OrderBy(p => p.Id.Value).First();
        var f = w.FreeAgents().Where(x => x.Status == FighterStatus.Active).OrderBy(x => x.Id.Value).First();

        FogKit.SetTrue(f, 55);
        var a = PromotionSetup.SignContract(w, promo, w.CurrentDate, f, new SimRandom(9), seenAbility: 62);
        FogKit.SetTrue(f, 75);
        var b = PromotionSetup.SignContract(w, promo, w.CurrentDate, f, new SimRandom(9), seenAbility: 62);

        Assert.Equal(a.PursePerFight, b.PursePerFight);       // only the hidden truth moved: the price did not
        Assert.Equal(a.FightsRemaining, b.FightsRemaining);
        // ...and without a read it still prices the truth (the day-one world's history).
        var truth = PromotionSetup.SignContract(w, promo, w.CurrentDate, f, new SimRandom(9));
        var asRead = PromotionSetup.SignContract(w, promo, w.CurrentDate, f, new SimRandom(9), seenAbility: FighterAbility.CurrentAbility(f));
        Assert.Equal(truth.PursePerFight, asRead.PursePerFight);
    }
}
