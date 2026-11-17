using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// The matchmaker pairs and promotes on its house's read, never the truth. Before the fix it ordered the undercard
/// pool, scored each pairing and promoted the main event on <see cref="FighterAbility.CurrentAbility"/>; these
/// tests failed there (the pairing followed the truth).
/// </summary>
public class FogMatchmakerTests
{
    // An AI house, one of its men's divisions, and four unbooked men in it: the house misreads Up high and Down
    // low, and Up sits a point below Down in truth. Y and Z are clearly better than both, equal in truth AND in the
    // house's read.
    internal static (World W, Promotion P, Division D, Fighter Y, Fighter Z, Fighter Up, Fighter Down) Setup(ulong seed = 5)
    {
        var w = FogKit.Modern(seed);
        foreach (var promo in w.Promotions.Values.Where(p => !p.Defunct && p.Id != w.PlayerPromotion).OrderBy(p => p.Id.Value))
        {
            int prestige = promo.Prestige.Value;
            promo.Prestige = 20;   // no scouting competence: the read carries its full error
            int Read(Fighter f) => ScoutingService.PerceivedAbility(w, promo, f);
            foreach (var div in promo.Divisions.Where(d => d.Sex == Sex.Male)
                         .OrderByDescending(d => w.RosterOf(promo.Id).Count(d.Matches)).ThenBy(d => (int)d.WeightClass))
            {
                var pool = w.RosterOf(promo.Id).Where(f => div.Matches(f) && f.Status == FighterStatus.Active && FogKit.Unbooked(w, f))
                    .OrderBy(f => f.Id.Value).ToList();
                if (pool.Count < 4) continue;
                foreach (var f in pool) FogKit.Unknown(f);
                if (FogKit.Misread(pool, Read) is not { } pair || !FogKit.Neutral(w, pair.Up, pair.Down)) continue;
                var rest = pool.Where(f => f != pair.Up && f != pair.Down
                                           && FogKit.Neutral(w, f, pair.Up) && FogKit.Neutral(w, f, pair.Down)).ToList();
                foreach (var f in rest) FogKit.SetTrue(f, 80);
                var twin = rest.GroupBy(f => Read(f)).OrderBy(g => g.Key)
                    .Select(g => g.ToList()).FirstOrDefault(g => g.Count >= 2 && FogKit.Neutral(w, g[0], g[1]));
                if (twin is null) continue;
                FogKit.Invert(pair.Up, pair.Down, Read);
                div.Rankings.Clear();   // no eliminator tier: the pairing is on ability alone
                return (w, promo, div, twin[0], twin[1], pair.Up, pair.Down);
            }
            promo.Prestige = prestige;
        }
        throw new InvalidOperationException("no AI division with a misread pair");
    }

    private static HashSet<FighterId> AllBut(World w, params Fighter[] keep) =>
        w.Fighters.Keys.Where(id => keep.All(k => k.Id != id)).ToHashSet();

    [Fact]
    public void TheBestPairing_FollowsTheHousesRead_NotTheTruth()
    {
        var (w, promo, _, y, _, up, down) = Setup();
        var bout = new Matchmaker().BestBout(w, promo, w.CurrentDate.AddDays(30), AllBut(w, y, up, down), titleLength: false);

        Assert.NotNull(bout);
        var pair = new[] { bout!.Value.Red, bout.Value.Blue };
        Assert.Contains(y.Id, pair);
        Assert.Contains(up.Id, pair);      // the man the house rates higher
        Assert.DoesNotContain(down.Id, pair);   // not the one who truly is
    }

    [Fact]
    public void TheMainEventPromotedFromTheUndercard_IsTheStrongestByTheHousesRead()
    {
        // Y and Z share a room, so do Up and Down: the two bouts are Y or Z against Up or Down. Y and Z are equal
        // by every measure, so whichever way the undercard pairs them, the bout with Up is the stronger by the
        // house's read and the bout with Down the stronger in truth. The read must headline.
        var (w, promo, _, y, z, up, down) = Setup();
        var rooms = w.Camps.Values.Where(c => c.Reputation < 60).OrderBy(c => c.Id.Value).Take(2).ToList();
        Assert.Equal(2, rooms.Count);
        y.Camp = rooms[0].Id; z.Camp = rooms[0].Id;
        up.Camp = rooms[1].Id; down.Camp = rooms[1].Id;
        int Read(Fighter f) => ScoutingService.PerceivedAbility(w, promo, f);
        Assert.Equal(Read(y), Read(z));
        Assert.Equal(FighterAbility.CurrentAbility(y), FighterAbility.CurrentAbility(z));
        Assert.True(Read(up) > Read(down));
        Assert.True(FighterAbility.CurrentAbility(down) > FighterAbility.CurrentAbility(up));

        var card = new Matchmaker().BuildCard(w, promo.Id, targetFights: 2, w.CurrentDate.AddDays(30), AllBut(w, y, z, up, down));

        Assert.Equal(2, card.Count);
        var main = card[^1];
        Assert.False(main.IsTitle);
        Assert.Equal(promo.Rules.TitleRounds, main.Rounds);
        Assert.Contains(up.Id, new[] { main.Red, main.Blue });
        Assert.DoesNotContain(down.Id, new[] { main.Red, main.Blue });
    }
}
