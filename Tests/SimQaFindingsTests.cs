using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// Findings from the sim-QA sweeps (tools/longsim check / playcheck), each pinned as a test that FAILS today and is
/// skipped with its finding id so the suite stays green. The owning agent un-skips the test with the fix. A test that
/// is not skipped here encodes something that already holds and must keep holding.
/// Reproduce a finding in the harness with the seed noted on each test: <c>dotnet run -c Release -- check N Y</c>
/// or <c>playcheck N Y</c> from tools/longsim.
/// </summary>
public class SimQaFindingsTests
{
    private static World Build(ulong seed) => new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    // A card's fight-week pullout roll is a 3% chance per active corner drawn from a stream keyed on the world seed and event
    // id, so any single seed can empty a small card by luck and hide a bug. Every scenario below runs on several seeds.
    private static readonly ulong[] Seeds = { 11, 12, 13, 14, 15, 16 };

    /// <summary>The player's AFC with one card on the calendar and four bouts booked (several, so a chance pullout cannot empty the card); returns the first bout's corners.</summary>
    private static (World World, Event Card, Fighter Red, Fighter Blue) BookedCard(ulong seed = 7)
    {
        var world = Build(seed);
        var promo = world.Get(world.PlayerPromotion)!;
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(30));
        Fighter? red = null, blue = null;
        foreach (var div in promo.Divisions.Where(d => d.Sex == Sex.Male))
        {
            var pair = booking.AvailableRoster(world, ev, div.WeightClass).Skip(red is null ? 0 : 2).Take(2).ToList();
            if (pair.Count < 2) continue;
            if (!booking.AddBout(world, ev, pair[0].Id, pair[1].Id, 3, false, out _)) continue;
            red ??= pair[0]; blue ??= pair[1];
            if (ev.Card.Count >= 4) break;
        }
        Assert.True(red is not null && ev.Card.Count >= 4, "could not book a four-bout card");
        return (world, ev, red!, blue!);
    }

    private static bool Fought(Event ev, Fighter f) =>
        ev.Card.Any(b => b.Outcome is not null && (b.RedCorner == f.Id || b.BlueCorner == f.Id));

    // playcheck seed 6 (UFL takeover), 2026-05-13: PLAY-RAN-UNAVAILABLE. Retirement is rolled monthly and Lifecycle.Retire
    // strips rankings and belts but not scheduled bouts, so the card runs a retired man.
    [Fact]
    public void ARetiredFighterIsNotFoughtOnAScheduledCard()
    {
        var bad = new List<string>();
        foreach (var seed in Seeds)
        {
            var (world, ev, red, _) = BookedCard(seed);
            Lifecycle.Retire(world, red, world.CurrentDate);
            new WorldTick().RunEventLive(world, ev, playByPlay: false);
            if (Fought(ev, red)) bad.Add($"seed {seed}: {red.FullName} retired before fight night and still fought");
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    // playcheck seed 5 (founded), 2030-05-02, and seed 2 (ICF takeover), 2031-03-06: CHAMP-CONTRACT and LINEAGE. A champion who
    // retires while booked to defend has the belt handed to the next contender at once; the stale title bout then still runs, the
    // retired man can win it back, and the lineage closes a reign on the card's date before the reign opened on the retirement date.
    [Fact]
    public void ARetiredChampionsStaleTitleBoutDoesNotReclaimTheBelt()
    {
        var bad = new List<string>();
        foreach (var seed in Seeds)
        {
            var world = Build(seed);
            var promo = world.Get(world.PlayerPromotion)!;
            var booking = new PlayerBooking();
            var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(30));
            var div = promo.Divisions.First(d => d.Sex == Sex.Male && d.Title is not null
                && world.Get(d.Title!.Value)?.CurrentChampion is not null && d.Rankings.Count >= 2);
            var title = world.Get(div.Title!.Value)!;
            var champ = world.Get(title.CurrentChampion!.Value)!;
            var challenger = world.Get(div.Rankings[0])!;
            Assert.True(booking.AddBout(world, ev, champ.Id, challenger.Id, 5, titleFight: true, out var err), err);
            // a couple of undercard bouts so a chance pullout cannot cancel the whole night
            foreach (var d in promo.Divisions.Where(d => d.Sex == Sex.Male && d != div).Take(3))
            {
                var pair = booking.AvailableRoster(world, ev, d.WeightClass).Take(2).ToList();
                if (pair.Count == 2) booking.AddBout(world, ev, pair[0].Id, pair[1].Id, 3, false, out _);
            }

            Lifecycle.Retire(world, champ, world.CurrentDate.AddDays(34));   // retires in the week the UI holds the card (card date +4d); the belt passes down the rankings
            new WorldTick().RunEventLive(world, ev, playByPlay: false);

            if (world.Get(title.CurrentChampion!.Value)!.Status == FighterStatus.Retired) bad.Add($"seed {seed}: the retired {champ.FullName} holds the {title.Name} again");
            foreach (var reign in title.Lineage.Where(r => r.LostOn is not null && r.LostOn < r.WonOn))
                bad.Add($"seed {seed}: reign of {world.Get(reign.Champion)!.FullName} ends {reign.LostOn} before it begins {reign.WonOn}");
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    // playcheck seed 5 (founded), 2026-06-06: releasing a booked fighter leaves the bout on the card; he fights as a free
    // agent for the org that just let him go (and is paid from its books).
    [Fact]
    public void AReleasedFighterIsNotFoughtOnAScheduledCard()
    {
        var bad = new List<string>();
        foreach (var seed in Seeds)
        {
            var (world, ev, red, _) = BookedCard(seed);
            ContractLifecycle.Release(world, red);
            new WorldTick().RunEventLive(world, ev, playByPlay: false);
            if (Fought(ev, red)) bad.Add($"seed {seed}: {red.FullName} was released before fight night and still fought for the org that cut him");
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    // check seed 3, 2026-03-05 (and seed 1, 2026-10-29): MaybeSuperfight books a champ-vs-champ card 28 days out, then the
    // AI matchmaker builds that night's ordinary card with no knowledge of cards already scheduled, so the champions fight twice in a day.
    [Fact(Skip = "SQA-03: Matchmaker.BuildCard ignores fighters already booked on another scheduled card (same-date double booking)")]
    public void TheMatchmakerDoesNotBookSomeoneAlreadyOnAScheduledCard()
    {
        var world = Build(3);
        world.PlayerPromotion = default;
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        var mm = new Matchmaker();
        var first = mm.BuildCard(world, afc.Id, 13);
        Assert.NotEmpty(first);

        var date = world.CurrentDate.AddDays(7);
        new EventService().CreateEvent(world, afc.Id, "Superfight night", date, new[] { first[0] });
        var again = mm.BuildCard(world, afc.Id, 13);

        var booked = new HashSet<FighterId> { first[0].Red, first[0].Blue };
        Assert.DoesNotContain(again, m => booked.Contains(m.Red) || booked.Contains(m.Blue));
    }

    // playcheck seed 1 (founded), 2026-06-18: nothing stops the player scheduling two cards on one date, and CardFiller.PadToMinimum
    // fills a thin card from the roster without looking at the other card, so the same fighters fight on both.
    [Fact]
    public void PaddingACardDoesNotUseFightersBookedOnASecondCardTheSameNight()
    {
        var bad = new List<string>();
        foreach (var seed in Seeds)
        {
            var world = Build(seed);
            var promo = world.Get(world.PlayerPromotion)!;
            var booking = new PlayerBooking();
            var date = world.CurrentDate.AddDays(30);
            var first = booking.CreateEvent(world, date, "First");
            var second = booking.CreateEvent(world, date, "Second");

            // A few bouts on the first card (so a chance pullout cannot empty it); every other bookable pair goes on the second,
            // so any padding the first card gets is a double booking.
            var mine = new HashSet<FighterId>();
            foreach (var d in promo.Divisions.Where(d => d.Sex == Sex.Male).Take(2))
            {
                var pair = booking.AvailableRoster(world, first, d.WeightClass).Take(2).ToList();
                if (pair.Count == 2 && booking.AddBout(world, first, pair[0].Id, pair[1].Id, 3, false, out _)) { mine.Add(pair[0].Id); mine.Add(pair[1].Id); }
            }
            foreach (var d in promo.Divisions)
                for (int guard = 0; guard < 200; guard++)
                {
                    var avail = booking.AvailableRoster(world, second, d.WeightClass, d.Sex).Where(f => !mine.Contains(f.Id)).Take(2).ToList();
                    if (avail.Count < 2 || !booking.AddBout(world, second, avail[0].Id, avail[1].Id, 3, false, out _)) break;
                }

            new WorldTick().RunEventLive(world, first, playByPlay: false);

            var onSecond = second.Card.SelectMany(b => new[] { b.RedCorner, b.BlueCorner }).ToHashSet();
            int twice = first.Card.SelectMany(b => new[] { b.RedCorner, b.BlueCorner }).Count(onSecond.Contains);
            if (twice > 0) bad.Add($"seed {seed}: {twice} fighters are on both cards on {date:yyyy-MM-dd}");
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    // check seed 1, 2026-02-05: LocalCircuit.StageCard dates a card on day 6..25 of the month and runs it at once, so results
    // exist up to three weeks before the world clock reaches the card. Fighters on it are not held for recovery, so the
    // same fighter can be on a promotion card dated the same day (check seed 2, 2026-11-19) or earlier (BOUT-ORDER).
    [Fact]
    public void NoCardIsCompletedBeforeItsOwnDate()
    {
        var world = Build(1);
        world.PlayerPromotion = default;
        new WorldTick().AdvanceWeeks(world, 5);   // 2026-02-05: the first month boundary
        var early = world.Events.Values.Where(e => e.Status == EventStatus.Completed && e.Date > world.CurrentDate)
            .Select(e => $"{e.Name} {e.Date:yyyy-MM-dd}").Take(3).ToList();
        Assert.True(early.Count == 0, $"completed before their date on {world.CurrentDate:yyyy-MM-dd}: {string.Join("; ", early)}");
    }

    // check seeds 1-8, first month onward: a prospect minted as a famous woman's junior inherits her sex but gets a random
    // weight class, so there are women at heavyweight, in classes no women's division runs.
    [Fact]
    public void EveryWomanIsInAWeightClassWomenFight()
    {
        var world = Build(2);
        world.PlayerPromotion = default;
        new WorldTick().AdvanceWeeks(world, 9);
        var wrong = world.Fighters.Values
            .Where(f => f.Sex == Sex.Female && f.Status != FighterStatus.Retired && !WomensDivisions.Allows(f.WeightClass))
            .Select(f => $"{f.FullName} ({f.WeightClass})").Take(3).ToList();
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    // check seed 1 (1 year, 4 seeds had 14 of these): a camp stipend is the paying org's promise, but nothing clears
    // Fighter.CampStipend when he leaves, so a free agent still reads "stipend paid" and the NEXT org that signs him is billed.
    [Fact]
    public void ACampStipendDoesNotFollowAFighterToAnotherOrg()
    {
        var world = Build(4);
        world.PlayerPromotion = default;
        var orgs = world.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value).ToList();
        var from = orgs[0]; var to = orgs[1];
        var f = world.RosterOf(from.Id).First(x => x.Status == FighterStatus.Active);
        f.CampStipend = true;
        f.Camp ??= world.Camps.Values.First().Id;

        ContractLifecycle.Release(world, f);
        Assert.False(f.CampStipend, "released fighter still carries the old org's stipend");

        ContractLifecycle.Sign(world, f, new ContractOffer { Fighter = f.Id, Promotion = to.Id, PursePerFight = new Money(5_000m), Fights = 3 }, world.CurrentDate);
        int before = to.Ledger.Count;
        Camps.ChargeMonthly(world, world.CurrentDate);
        Assert.DoesNotContain(to.Ledger.Skip(before), l => l.Category == FinanceCategory.CampFees && l.Description.Contains(f.FullName));
    }
}
