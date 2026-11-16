using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf;

/// <summary>
/// Render-harness helpers for an Origins career (headless only): the start summary the New Game
/// window would give, and N weeks of play the way a player who sims every night would play them.
/// </summary>
internal static class OriginsHarness
{
    public static string Summary(int house) =>
        OriginsStart.SummaryFor(OriginsPromotions.All[Math.Clamp(house, 0, OriginsPromotions.All.Count - 1)]);

    /// <summary>Advance week by week; a fight night of ours is simmed when it arrives (the last one is
    /// left on the bar when <paramref name="hold"/>).</summary>
    public static void Play(MainViewModel vm, int weeks, bool hold, bool bookNight = false)
    {
        if (bookNight && vm.Tournament.Applies)
        {
            // The way a player does it: the Bracket Desk's default night, "Book the night".
            vm.Tournament.ScheduleCommand.Execute(null);
            Console.WriteLine($"ORIGINS booked a bracket night: {vm.Tournament.Status}{vm.Tournament.Error}");
        }
        for (int i = 0; i < weeks; i++)
        {
            if (vm.IsFightNight) vm.SimTonightCommand.Execute(null);
            vm.AdvanceWeekCommand.Execute(null);
        }
        if (!hold && vm.IsFightNight) vm.SimTonightCommand.Execute(null);
        Console.WriteLine($"ORIGINS played {weeks} weeks -> {vm.LiveWorld?.CurrentDate:d MMM yyyy}; fight night waiting: {vm.IsFightNight}");
    }

    /// <summary>
    /// Plant a fought bracket night shaped to one hard case (harness only: the world is the harness's own) and
    /// return it. Seeds s1..s8 and the alternate A are the house's roster by id. QF1 s1-s8, QF2 s4-s5, QF3 s2-s7,
    /// QF4 s3-s6; SF1 = QF1/QF2, SF2 = QF3/QF4.
    /// a: alternate in at QF2, SF1 winner can't go on, final walked over.  b: the mirror (SF2 winner can't go on).
    /// c: no alternate; QF1+QF2 drawn (SF1 empty), SF2 drawn: nobody fit for the final.
    /// d: QF1-3 drawn (alternate fills one), two semi walkovers, final drawn after overtime.
    /// e: judges: QF1 drawn, better seed through.  f: a clean night, final won in overtime.
    /// </summary>
    public static Event PlantBracket(World w, string kase)
    {
        var promo = w.Get(w.PlayerPromotion)!;
        var men = w.RosterOf(promo.Id).OrderBy(f => f.Id.Value).Select(f => f.Id).ToList();
        FighterId S(int seed) => men[seed - 1];
        var A = men[8];
        if (kase == "e") promo.Rules.Fight = (promo.Rules.Fight ?? FightRules.Modern) with { Judges = true };
        var ev = new Event
        {
            Id = w.NewEventId(), Promotion = promo.Id, Name = $"{promo.Name} case {kase}", Date = w.CurrentDate,
            Format = EventFormat.Tournament, Status = EventStatus.Completed, Venue = "Pico Sports Pavilion",
        };
        void F(FighterId red, FighterId blue, FighterId? winner, int round = 1, int secs = 400)
        {
            ev.Card.Add(new Fight
            {
                Id = w.NewFightId(), RedCorner = red, BlueCorner = blue, WeightClass = Division.OpenClass, OpenWeight = true, ScheduledRounds = 1,
                Outcome = new FightOutcome
                {
                    Winner = winner, Loser = winner is null ? null : winner == red ? blue : red,
                    Method = winner is null ? FightMethod.Draw : FightMethod.SubmissionTap, EndRound = round, EndTimeSeconds = secs,
                },
            });
        }
        FighterId? champ = null;
        switch (kase)
        {
            case "a":
                F(S(1), S(8), S(1)); F(S(4), S(5), null, 1, 1200); F(S(2), S(7), S(2)); F(S(3), S(6), S(3));
                F(S(1), A, S(1)); F(S(2), S(3), S(2)); champ = S(2); break;
            case "b":
                F(S(1), S(8), S(1)); F(S(4), S(5), null, 1, 1200); F(S(2), S(7), S(2)); F(S(3), S(6), S(3));
                F(S(1), A, S(1)); F(S(2), S(3), S(2)); champ = S(1); break;
            case "c":
                F(S(1), S(8), null, 1, 1200); F(S(4), S(5), null, 1, 1200); F(S(2), S(7), S(2)); F(S(3), S(6), S(3));
                F(S(2), S(3), null, 1, 1200); break;
            case "d":
                F(S(1), S(8), null, 1, 1200); F(S(4), S(5), null, 1, 1200); F(S(2), S(7), null, 1, 1200); F(S(3), S(6), S(3));
                F(A, S(3), null, 2, 300); break;
            case "e":
                F(S(1), S(8), null, 1, 1200); F(S(4), S(5), S(4)); F(S(2), S(7), S(2)); F(S(3), S(6), S(3));
                F(S(1), S(4), S(4)); F(S(2), S(3), S(3)); F(S(4), S(3), S(3)); champ = S(3); break;
            default:
                F(S(1), S(8), S(1)); F(S(4), S(5), S(4)); F(S(2), S(7), S(2)); F(S(3), S(6), S(3));
                F(S(1), S(4), S(1)); F(S(2), S(3), S(2)); F(S(1), S(2), S(1), 2, 100); champ = S(1); break;
        }
        w.Events[ev.Id] = ev;
        if (champ is { } c) w.Get(c)!.Accolades.Add(new Accolade(ev.Date, $"Won {ev.Name}"));
        var night = BracketNightReader.Read(w, ev);
        Console.WriteLine($"BRACKET case {kase}: " + (night is null ? "UNREADABLE" : $"champion {w.Get(night.Champion ?? default)?.LastName ?? "none"}, final drawn {night.FinalDrawn}, alternate {(night.Alternate is { } a ? w.Get(a)?.LastName : "-")}"));
        if (night is not null)
            foreach (var b in night.Bouts)
                Console.WriteLine($"  R{b.Round} {Name(w, b.Red)} v {Name(w, b.Blue)}: {(b.Fight is null ? "no bout" : "fought")}, {b.How} -> {Name(w, b.Advanced)}{(b.Withdrew is { } x ? $" (withdrew {Name(w, x)})" : "")}");
        var board = BracketBoard.From(w, ev);
        Console.WriteLine($"  board: {string.Join(" | ", board?.Rounds.Select(r => $"{r.Name} x{r.Bouts.Count}") ?? Array.Empty<string>())}; champion line '{board?.ChampionLine}'; verdict '{board?.Verdict}'");
        foreach (var r in board?.Rounds ?? Array.Empty<BracketRound>())
            foreach (var bt in r.Bouts) Console.WriteLine($"    {r.Name}: {bt.Red.Name}{(bt.Red.HasTag ? $" [{bt.Red.Tag}]" : "")} v {bt.Blue.Name}{(bt.Blue.HasTag ? $" [{bt.Blue.Tag}]" : "")} | {bt.How} | {bt.Aside}");
        return ev;
    }

    private static string Name(World w, FighterId? id) => id is { } x ? w.Get(x)?.LastName ?? "?" : "nobody";
}
