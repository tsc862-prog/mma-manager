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
}
