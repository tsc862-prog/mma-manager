using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
namespace LongSim;
public static class OriginsDebug
{
    public static void Run(string[] args)
    {
        ulong seed = ulong.Parse(args[1]); int weeks = int.Parse(args[2]); string abbr = args[3];
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(seed, 0));
        w.Get(w.PlayerPromotion)!.IsPlayerControlled = false; w.PlayerPromotion = default;
        var t = new WorldTick();
        for (int i = 0; i < weeks; i++) t.AdvanceWeek(w);
        var p = w.Promotions.Values.First(x => x.Abbreviation == abbr);
        var neg = new Negotiator(); var val = new FighterValuation();
        Console.WriteLine($"{w.CurrentDate} {abbr} roster {w.RosterOf(p.Id).Count()} cap {Negotiator.PurseCap(w, p)} bal {p.Balance}");
        foreach (var f in w.RosterOf(p.Id)) Console.WriteLine($"  on roster: {f.FullName} {f.Record} left {f.Contract!.FightsRemaining} purse {f.Contract.PursePerFight} status {f.Status}");
        foreach (var f in w.FreeAgents().Where(f => f.Status == FighterStatus.Active).Take(15))
        {
            var exp = val.Evaluate(w, f);
            var offer = neg.AiOpeningOffer(w, p, f, exp);
            var v = neg.Evaluate(w, f, offer, exp);
            Console.WriteLine($"  FA {f.FullName} {f.Record} age {f.AgeOn(w.CurrentDate)} ask {exp.AskingPurse} offer {offer.PursePerFight} -> {v.Verdict} {v.Summary}  wants={MmaManager.Core.Sim.Org.RosterPolicy.Wants(w, p, f)}");
        }
        foreach (var n in w.News.Where(n => n.Promotion == p.Id).TakeLast(25)) Console.WriteLine($"  news {n.Date} {n.Headline}");
    }
}
