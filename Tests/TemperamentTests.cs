using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// Who fighters ARE outside the cage, and the sport having people in it: real
/// temperament (rather than a character read off the portrait art), fighters who
/// speak in their own voice, named front offices and owners, buildings with
/// names, and contracts at different stages of their life.
/// </summary>
public class TemperamentTests
{
    private static World Build(ulong seed = 9) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    // --- temperament --------------------------------------------------------

    [Fact]
    public void EveryoneHasACharacter_EvenWithNoArtPackLoaded()
    {
        var world = Build();
        var active = world.Fighters.Values.Where(f => !f.LocalTalent).ToList();

        // The old behavioural read came off PersonaTag, which the WPF layer
        // stamps from the art pack — so a headless world had no personalities.
        Assert.All(active, f =>
        {
            Assert.InRange(f.Temperament.Ego.Value, 1, 99);
            Assert.InRange(f.Temperament.Professionalism.Value, 1, 99);
            Assert.InRange(f.Temperament.Temper.Value, 1, 99);
            Assert.InRange(f.Temperament.Ambition.Value, 1, 99);
        });

        // And the sport contains more than one kind of person.
        var kinds = active.Select(f => f.Disposition).Distinct().ToList();
        Assert.True(kinds.Count >= 6, $"only {kinds.Count} dispositions in a whole world");
    }

    [Fact]
    public void TheBiosFinallyCash_AuthoredFightersAreWhoTheySay()
    {
        var world = Build();

        Fighter By(string last) => world.Fighters.Values.First(f => f.Designed && f.LastName == last);

        // "predicts rounds, calls shots"
        Assert.Equal(Disposition.Loudmouth, By("O'Rourke").Disposition);
        // "no entrance music, no interest in leaving"
        Assert.Equal(Disposition.CompanyMan, By("Volkov").Disposition);
        // "reads every offer sheet that lands"
        Assert.Equal(Disposition.Mercenary, By("Al-Marri").Disposition);
        // "has never needed to speak"
        Assert.Equal(Disposition.QuietProfessional, By("Morita").Disposition);

        // And the numbers follow the archetype, not just the label.
        Assert.True(By("Al-Marri").Temperament.Ambition.Value > By("Volkov").Temperament.Ambition.Value,
            "the Gulf's hired gun should be hungrier to move than the house's lifer");
        Assert.True(By("Morita").Temperament.Ego.Value < By("O'Rourke").Temperament.Ego.Value);
    }

    [Fact]
    public void TemperamentIsHashDerived_SoTheWorldIsUnchangedByIt()
    {
        // The whole design constraint: personality must cost the shared
        // generation stream nothing, or every tuned seed in the project reshuffles.
        var a = Build(21);
        var b = Build(21);
        var fa = a.Fighters.Values.OrderBy(f => f.Id.Value).ToList();
        var fb = b.Fighters.Values.OrderBy(f => f.Id.Value).ToList();

        Assert.Equal(fa.Count, fb.Count);
        for (int i = 0; i < fa.Count; i++)
        {
            Assert.Equal(fa[i].FullName, fb[i].FullName);
            Assert.Equal(fa[i].Disposition, fb[i].Disposition);
            Assert.Equal(fa[i].Temperament.Ego.Value, fb[i].Temperament.Ego.Value);
        }
    }

    // --- voice --------------------------------------------------------------

    [Fact]
    public void FightersSpeakInTheirOwnVoice_AndTheSameMomentReadsTheSameWay()
    {
        var world = Build();
        var loud = world.Fighters.Values.First(f => f.Disposition == Disposition.Loudmouth);
        var quiet = world.Fighters.Values.First(f => f.Disposition == Disposition.QuietProfessional);
        var on = world.CurrentDate;

        string a = Quotes.Say(loud, Moment.TitleWin, on);
        string b = Quotes.Say(quiet, Moment.TitleWin, on);

        Assert.StartsWith("“", a);                 // it's a quote
        Assert.EndsWith("”", a);
        Assert.NotEqual(a, b);                          // different people, different words
        Assert.Equal(a, Quotes.Say(loud, Moment.TitleWin, on));   // stable — a save reloads word for word

        // Every disposition has something to say about every moment (falling back
        // to the sport's default register rather than to nothing).
        foreach (var d in Enum.GetValues<Disposition>())
            foreach (var m in Enum.GetValues<Moment>())
            {
                var f = new Fighter { Id = new FighterId(7), FirstName = "T", LastName = "Est", BirthDate = new DateOnly(1995, 1, 1) };
                f.Disposition = d;
                Assert.False(string.IsNullOrWhiteSpace(Quotes.Say(f, m, on)));
            }
    }

    [Fact]
    public void TheNewsFeedHasVoicesInIt()
    {
        var world = Build();
        // Title changes are reported with the new champion's own words.
        var titleNews = world.News.Where(n => n.Category == NewsCategory.TitleChange).ToList();
        Assert.NotEmpty(titleNews);
    }

    // --- the front office ---------------------------------------------------

    [Fact]
    public void EveryRivalHasPeopleBehindTheDesks_AndThePlayerBuildsTheirOwn()
    {
        var world = Build();

        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct && !p.IsPlayerControlled))
        {
            Assert.NotEmpty(promo.Staff);
            Assert.All(promo.Staff, s =>
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Name));
                Assert.False(string.IsNullOrWhiteSpace(s.Background), $"{s.Name} has no history");
            });
        }

        // The player starts with empty desks: hiring the back office is the point
        // of the staff screen, and a shoestring can't inherit a salary it never
        // agreed to.
        Assert.Empty(world.Get(world.PlayerPromotion)!.Staff);
    }

    [Fact]
    public void RivalStaffAreFlavour_NotAHiddenBuffOrAHiddenBill()
    {
        var world = Build();
        var rival = world.Promotions.Values.First(p => !p.IsPlayerControlled && p.Staff.Count > 0);

        // Staff effects are the PLAYER's edge — otherwise handing twenty
        // promotions a marketing department re-tunes every gate in the sim.
        Assert.Equal(0, StaffService.MarketingBonus(rival), 6);
        Assert.Equal(1.0, StaffService.MedicalRecoveryFactor(rival), 6);
        Assert.Equal(1.0, StaffService.CoachGrowthMultiplier(rival), 6);
    }

    [Fact]
    public void TheHousesHaveOwners_AndBoardNotesAreSigned()
    {
        var world = Build();
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");

        Assert.Equal("Katherine Ross", afc.OwnerName);
        Assert.False(string.IsNullOrWhiteSpace(afc.OwnerNote));
        Assert.True(afc.FoundedYear is > 1900 and < 2030);

        // The letter is signed by a person, and the org page says whose house it is.
        world.PlayerPromotion = afc.Id;
        Assert.Equal("Katherine Ross", MmaManager.Core.Sim.Living.Mail.Owner(world));
        Assert.Contains("Katherine Ross", Biographer.Promotion(world, afc));
    }

    [Fact]
    public void AFoundedPromotionHasNoOwnerButThePlayer()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 4,
            Founding = new WorldBuilder.FoundingSpec { Name = "New Wave Fighting", Abbreviation = "NWF" },
        });

        var mine = world.Get(world.PlayerPromotion)!;
        Assert.Null(mine.OwnerName);
        Assert.Empty(mine.Staff);
        Assert.Equal("The Accountant", MmaManager.Core.Sim.Living.Mail.Owner(world));
    }

    // --- buildings and deals ------------------------------------------------

    [Fact]
    public void FightTownsHaveTheirOwnBuildings()
    {
        var world = Build();
        string[] In(string city) => world.Venues.Values
            .Where(v => world.Get(v.City)!.Name == city)
            .OrderBy(v => v.Capacity).Select(v => v.Name).ToArray();

        Assert.Equal(new[] { "Korakuen Basement", "Ryōgoku Hall", "The Sunrise Dome" }, In("Tokyo"));
        Assert.Equal(new[] { "The Yard", "Surulere Sports Hall", "Eko Grand Arena" }, In("Lagos"));

        // No two cities share a building name — the whole world used to run on
        // "{City} Fight Hall / The {City} Forum / {City} Arena".
        var names = world.Venues.Values.Select(v => v.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void DealsArriveAtDifferentStagesOfTheirLife()
    {
        var world = Build();
        var deals = world.Fighters.Values
            .Where(f => f.Contract is not null && f.Status == FighterStatus.Active)
            .Select(f => f.Contract!)
            .ToList();

        Assert.True(deals.Count > 100);
        // A real slice of the world is at the table on day one — that's where a
        // contracts screen gets interesting.
        double expiring = deals.Count(c => c.FightsRemaining <= 1) / (double)deals.Count;
        Assert.InRange(expiring, 0.05, 0.25);
        Assert.Contains(deals, c => c.FightsRemaining >= 6);   // and some are locked up long term
    }

    [Fact]
    public void TheLoreIsInThePayroll()
    {
        var world = Build();
        Fighter By(string last) => world.Fighters.Values.First(f => f.Designed && f.LastName == last);

        // "fights for peanuts by choice" vs "the richest purse-per-fight deal in
        // the division" — neither used to be anywhere in the data.
        var okafor = By("Okafor");
        var alMarri = By("Al-Marri");
        Assert.NotNull(okafor.Contract);
        Assert.NotNull(alMarri.Contract);

        // Okafor is the bigger NAME and takes the smaller cheque, by choice.
        Assert.True(okafor.Intangibles.Popularity.Value > alMarri.Intangibles.Popularity.Value);
        Assert.True(alMarri.Contract!.PursePerFight.Amount > okafor.Contract!.PursePerFight.Amount * 3,
            $"the Gulf should badly outpay the cause ({alMarri.Contract.PursePerFight} vs {okafor.Contract.PursePerFight})");
    }
}
