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
    public static void Play(MainViewModel vm, int weeks, bool hold, bool bookNight = false, int months = 0, bool reform = false, bool busy = false, bool stopAtBan = false)
    {
        if (bookNight && vm.Tournament.Applies)
        {
            // The way a player does it: the Bracket Desk's default night, "Book the night".
            vm.Tournament.ScheduleCommand.Execute(null);
            Console.WriteLine($"ORIGINS booked a bracket night: {vm.Tournament.Status}{vm.Tournament.Error}");
        }
        // Months first (the long haul: years of the sport's story), then weeks.
        for (int i = 0; i < months; i++)
        {
            if (vm.IsFightNight) vm.SimTonightCommand.Execute(null);
            if (busy) KeepBusy(vm);
            vm.AdvanceMonthCommand.Execute(null);
            if (vm.IsFightNight) vm.SimTonightCommand.Execute(null);
            if (reform) Reform(vm);
            if (i % 12 == 11 || vm.Org.HasBanLine) Console.WriteLine($"ORIGINS {vm.LiveWorld?.CurrentDate:MMM yyyy}: {vm.LiveWorld?.Era.Chapters.Count} chapters, era {Core.Sim.Living.OriginsStory.CurrentEra(vm.LiveWorld!).Name}, bans {vm.LiveWorld!.Era.Bans.Count}, banned at home {vm.Org.HasBanLine}");
            if (stopAtBan && vm.Org.HasBanLine) break;
        }
        for (int i = 0; i < weeks; i++)
        {
            if (vm.IsFightNight) vm.SimTonightCommand.Execute(null);
            if (busy) KeepBusy(vm);
            vm.AdvanceWeekCommand.Execute(null);
            if (reform) Reform(vm);
        }
        if (!hold && vm.IsFightNight) vm.SimTonightCommand.Execute(null);
        Console.WriteLine($"ORIGINS played {months} months + {weeks} weeks -> {vm.LiveWorld?.CurrentDate:d MMM yyyy}; fight night waiting: {vm.IsFightNight}");
    }

    /// <summary>
    /// Keep the house running nights the way a player would, through the screens: a bracket house books the Bracket
    /// Desk's default night; a card house (after its split) books a card five weeks out, one bout per division.
    /// </summary>
    public static void KeepBusy(MainViewModel vm)
    {
        var w = vm.LiveWorld!;
        // Keep a roster: sign free agents at their terms (through Contracts) while the house has fewer than twelve men.
        for (int tries = 0; tries < 12 && w.RosterOf(w.PlayerPromotion).Count(f => f.Status != FighterStatus.Retired) < 12; tries++)
        {
            var row = vm.Contracts.FreeAgents.Skip(tries).FirstOrDefault();
            if (row is null) break;
            vm.Contracts.SelectedFreeAgent = row;
            vm.Contracts.SignAtTheirTerms();
        }
        if (w.Events.Values.Any(e => e.Promotion == w.PlayerPromotion && e.Status == EventStatus.Scheduled)) return;
        if (vm.Tournament.Applies) { vm.Tournament.ScheduleCommand.Execute(null); return; }
        var bk = vm.Booking;
        bk.NewEventDate = w.CurrentDate.AddDays(35).ToDateTime(TimeOnly.MinValue);
        bk.NewEventCommand.Execute(null);
        foreach (var div in bk.Divisions.ToList())
        {
            bk.SelectedDivision = div;
            for (int k = 0; k < 2 && bk.RedOptions.Count > 0; k++)
            {
                bk.SelectedRed = bk.RedOptions[0];
                if (bk.BlueOptions.Count == 0) break;
                bk.SelectedBlue = bk.BlueOptions[0];
                bk.AddBoutCommand.Execute(null);
            }
        }
        vm.Contracts.OnChanged?.Invoke();
    }

    /// <summary>Adopt every reform HQ offers today, through HQ's own command (the harness has no confirm dialog).</summary>
    public static void Reform(MainViewModel vm)
    {
        foreach (var r in vm.Org.Reforms.Where(r => r.CanAdopt).Select(r => (r.Chapter, r.Title)).ToList())
        {
            vm.Org.AdoptReformCommand.Execute(r.Chapter);
            Console.WriteLine($"ORIGINS {vm.LiveWorld?.CurrentDate:d MMM yyyy} adopted '{r.Title}': {vm.Org.Status}");
        }
    }

    /// <summary>Advance week by week until the briefing has a new chapter to tell (at most <paramref name="max"/> weeks).</summary>
    public static void UntilChapter(MainViewModel vm, int max, bool reform, bool busy = false)
    {
        for (int i = 0; i < max; i++)
        {
            if (vm.IsFightNight) vm.SimTonightCommand.Execute(null);
            if (busy) KeepBusy(vm);
            vm.AdvanceWeekCommand.Execute(null);
            if (vm.Briefing.HasChapters || vm.Briefing.Chapters.Count > 0) break;
            vm.Briefing.Attach(vm.LiveWorld!);
            if (vm.Briefing.HasChapters) break;
            if (reform) Reform(vm);
        }
        Console.WriteLine($"ORIGINS until a chapter -> {vm.LiveWorld?.CurrentDate:d MMM yyyy}: {string.Join(" | ", vm.Briefing.Chapters.Select(c => c.Title))}");
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
        // The night as the sim saves it (Event.Bracket): slot by slot, round by round; a fought slot also puts its bout on the card.
        var slotIn = new Dictionary<int, int>();
        void Slot(int round, FighterId? red, FighterId? blue, FighterId? winner, FighterId? through, BracketHow how,
            FighterId? withdrew = null, bool fought = true, int endRound = 1, int secs = 400)
        {
            FightId? fid = null;
            if (fought && red is { } r && blue is { } b)
            {
                var fight = new Fight
                {
                    Id = w.NewFightId(), RedCorner = r, BlueCorner = b, WeightClass = Division.OpenClass, OpenWeight = true, ScheduledRounds = 1,
                    BracketRound = round,
                    Outcome = new FightOutcome
                    {
                        Winner = winner, Loser = winner is null ? null : winner == r ? b : r,
                        Method = winner is null ? FightMethod.Draw : FightMethod.SubmissionTap, EndRound = endRound,
                        EndTimeSeconds = winner is null && endRound == 1 ? 1200 : secs,
                    },
                };
                ev.Card.Add(fight);
                fid = fight.Id;
            }
            int slot = slotIn.GetValueOrDefault(round);
            slotIn[round] = slot + 1;
            ev.Bracket.Add(new BracketSlot { Round = round, Slot = slot, Red = red, Blue = blue, Fight = fid, Advanced = through, How = how, Withdrew = withdrew });
        }
        const BracketHow Won = BracketHow.Won, Nobody = BracketHow.NobodyAdvanced, Walk = BracketHow.Walkover;
        ev.BracketAlternate = kase == "c" ? null : A;
        switch (kase)
        {
            case "a":
                Slot(1, S(1), S(8), S(1), S(1), Won); Slot(1, S(4), S(5), null, A, BracketHow.AlternateAfterDraw);
                Slot(1, S(2), S(7), S(2), S(2), Won); Slot(1, S(3), S(6), S(3), S(3), Won);
                Slot(2, S(1), A, S(1), null, Nobody, withdrew: S(1)); Slot(2, S(2), S(3), S(2), S(2), Won);
                Slot(3, null, S(2), null, S(2), Walk, fought: false); break;
            case "b":
                Slot(1, S(1), S(8), S(1), S(1), Won); Slot(1, S(4), S(5), null, A, BracketHow.AlternateAfterDraw);
                Slot(1, S(2), S(7), S(2), S(2), Won); Slot(1, S(3), S(6), S(3), S(3), Won);
                Slot(2, S(1), A, S(1), S(1), Won); Slot(2, S(2), S(3), S(2), null, Nobody, withdrew: S(2));
                Slot(3, S(1), null, null, S(1), Walk, fought: false); break;
            case "c":
                Slot(1, S(1), S(8), null, null, Nobody); Slot(1, S(4), S(5), null, null, Nobody);
                Slot(1, S(2), S(7), S(2), S(2), Won); Slot(1, S(3), S(6), S(3), S(3), Won);
                Slot(2, null, null, null, null, Nobody, fought: false); Slot(2, S(2), S(3), null, null, Nobody);
                Slot(3, null, null, null, null, Nobody, fought: false); break;
            case "d":
                Slot(1, S(1), S(8), null, A, BracketHow.AlternateAfterDraw); Slot(1, S(4), S(5), null, null, Nobody);
                Slot(1, S(2), S(7), null, null, Nobody); Slot(1, S(3), S(6), S(3), S(3), Won);
                Slot(2, A, null, null, A, Walk, fought: false); Slot(2, null, S(3), null, S(3), Walk, fought: false);
                Slot(3, A, S(3), null, null, Nobody, endRound: 2, secs: 300); break;
            case "e":
                Slot(1, S(1), S(8), null, S(1), BracketHow.JudgedDrawBetterSeed); Slot(1, S(4), S(5), S(4), S(4), Won);
                Slot(1, S(2), S(7), S(2), S(2), Won); Slot(1, S(3), S(6), S(3), S(3), Won);
                Slot(2, S(1), S(4), S(4), S(4), Won); Slot(2, S(2), S(3), S(3), S(3), Won);
                Slot(3, S(4), S(3), S(3), S(3), Won); break;
            default:
                Slot(1, S(1), S(8), S(1), S(1), Won); Slot(1, S(4), S(5), S(4), S(4), Won);
                Slot(1, S(2), S(7), S(2), S(2), Won); Slot(1, S(3), S(6), S(3), S(3), Won);
                Slot(2, S(1), S(4), S(1), S(1), Won); Slot(2, S(2), S(3), S(2), S(2), Won);
                Slot(3, S(1), S(2), S(1), S(1), Won, endRound: 2, secs: 100); break;
        }
        w.Events[ev.Id] = ev;
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
