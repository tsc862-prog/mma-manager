using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Engine;

/// <summary>How a bracket slot was filled for the next round.</summary>
public enum BracketAdvance
{
    /// <summary>Won the bout (in regulation or overtime).</summary>
    Won,
    /// <summary>A judged draw: the better ORIGINAL seed goes through.</summary>
    JudgedDrawBetterSeed,
    /// <summary>No opponent in the slot: walked over.</summary>
    Walkover,
    /// <summary>The winner could not continue; the alternate took his place.</summary>
    AlternateForWithdrawal,
    /// <summary>A no-judges draw put both men out; the alternate took the place.</summary>
    AlternateAfterDraw,
    /// <summary>Nobody goes through from this slot (both out, no alternate left): the next bout is a walkover.</summary>
    NobodyAdvanced,
}

/// <summary>One slot of the bracket: the bout (null for a walkover), who went through and why.</summary>
public sealed record BracketBout(int Round, FighterId? Red, FighterId? Blue, FightId? Fight, FightOutcome? Outcome,
    FighterId? Advanced, BracketAdvance How, FighterId? Withdrew = null);

/// <summary>A whole night. <see cref="Champion"/> is null when the final was drawn even after overtime
/// (<see cref="FinalDrawn"/>), or when nobody was left to fight it.</summary>
public sealed record BracketNight(Fighter? Champion, IReadOnlyList<BracketBout> Bouts, bool FinalDrawn)
{
    public int BoutsFought => Bouts.Count(b => b.Outcome is not null);
    public int Walkovers => Bouts.Count(b => b.Outcome is null && b.How == BracketAdvance.Walkover);
}

/// <summary>What one bout of the night produced: the outcome and both corners' condition at the end.</summary>
public sealed record BracketBoutResult(FightOutcome Outcome, BoutCondition RedEnd, BoutCondition BlueEnd, FightId? Fight = null);

/// <summary>
/// The one-night single-elimination bracket, as ONE function both the booker
/// (<c>TournamentService.RunOneNight</c>) and the lab call, so they cannot drift apart. It owns the
/// night's rules; the caller owns how a bout is fought and recorded (a booked <see cref="Fight"/> in a
/// world, or a bare lab bout).
/// <list type="bullet">
/// <item>Pairings: standard seeding (<see cref="Order"/>: 1 v 8, 4 v 5, 2 v 7, 3 v 6), upper slot red.</item>
/// <item>Condition carries: a winner goes on with what he has after the rest (<see cref="BoutCondition.AfterRest"/>).</item>
/// <item>Before the final, a winner who cannot continue (spent, a cut of 3+, or injured per the caller)
/// withdraws: the alternate (ranked below every seed) takes his place once per night; after that the
/// slot is empty and the next opponent walks over. Nobody withdraws from the final.</item>
/// <item>Draws: with judges, the better ORIGINAL seed advances. With no judges both men are out (the
/// alternate or a walkover fills the slot); in the FINAL a no-judges draw first goes to one overtime
/// period of <see cref="FinalOvertimeSeconds"/>, and if that is level too the night has no champion.</item>
/// </list>
/// Deterministic: it makes no rolls of its own.
/// </summary>
public static class OneNightBracket
{
    /// <summary>The final's overtime period after a no-judges draw.</summary>
    public const int FinalOvertimeSeconds = 300;

    /// <summary>Standard bracket order for seeds 0..size-1: 1 v 8, 4 v 5, 2 v 7, 3 v 6 for eight.</summary>
    public static int[] Order(int size)
    {
        var order = new List<int> { 0 };
        for (int n = 1; n < size; n *= 2)
        {
            var grown = new List<int>(order.Count * 2);
            foreach (int s in order) { grown.Add(s); grown.Add(2 * n - 1 - s); }
            order = grown;
        }
        return order.ToArray();
    }

    /// <param name="seeded">The field, best first (power-of-two count).</param>
    /// <param name="alternate">The reserve, or null.</param>
    /// <param name="roundSeconds">Seconds per round (the rules' minutes, else the house's).</param>
    /// <param name="fight">Fights one bout under the given params and reports it.</param>
    /// <param name="injured">After a bout: is this winner hurt beyond fighting on (the caller's injury model)?</param>
    public static BracketNight Run(IReadOnlyList<Fighter> seeded, Fighter? alternate, FightRules rules, int roundSeconds,
        Func<Fighter, Fighter, FightParams, BracketBoutResult> fight, Func<Fighter, bool>? injured = null)
    {
        int size = seeded.Count;
        if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentException("A bracket needs a power-of-two field.", nameof(seeded));
        int rounds = rules.Rounds ?? 3;
        var rank = new Dictionary<FighterId, int>();
        for (int i = 0; i < size; i++) rank[seeded[i].Id] = i;
        if (alternate is not null) rank[alternate.Id] = size;   // below everyone

        var condition = new Dictionary<FighterId, BoutCondition>();
        var minutes = new Dictionary<FighterId, double>();
        var bouts = new List<BracketBout>();
        bool alternateUsed = false, finalDrawn = false;
        int bracketRound = 1;

        var slots = Order(size).Select(i => (Fighter?)seeded[i]).ToList();
        while (slots.Count > 1)
        {
            bool final = slots.Count == 2;
            var next = new List<Fighter?>();
            for (int i = 0; i + 1 < slots.Count; i += 2)
            {
                var red = slots[i]; var blue = slots[i + 1];
                if (red is null || blue is null)
                {
                    var through = red ?? blue;
                    bouts.Add(new BracketBout(bracketRound, red?.Id, blue?.Id, null, null, through?.Id,
                        through is null ? BracketAdvance.NobodyAdvanced : BracketAdvance.Walkover));
                    next.Add(through);
                    continue;
                }

                var p = new FightParams
                {
                    ScheduledRounds = rounds,
                    RoundSeconds = roundSeconds,
                    GeneratePlayByPlay = false,
                    Rules = rules,
                    RedStart = condition.GetValueOrDefault(red.Id),
                    BlueStart = condition.GetValueOrDefault(blue.Id),
                    OvertimeSeconds = final && !rules.Judges ? FinalOvertimeSeconds : 0,
                };
                var r = fight(red, blue, p);
                var o = r.Outcome;
                double fought = (o.EndRound > rounds ? rounds * roundSeconds : (o.EndRound - 1) * roundSeconds) + o.EndTimeSeconds;
                minutes[red.Id] = minutes.GetValueOrDefault(red.Id) + fought / 60.0;
                minutes[blue.Id] = minutes.GetValueOrDefault(blue.Id) + fought / 60.0;

                Fighter? winner = o.Winner == red.Id ? red : o.Winner == blue.Id ? blue : null;
                var how = BracketAdvance.Won;
                bool drawOut = false;
                if (winner is null)
                {
                    if (rules.Judges) { winner = rank[red.Id] <= rank[blue.Id] ? red : blue; how = BracketAdvance.JudgedDrawBetterSeed; }
                    else drawOut = true;
                }

                FighterId? withdrew = null;
                if (winner is not null)
                {
                    var rested = (winner == red ? r.RedEnd : r.BlueEnd).AfterRest(winner, minutes[winner.Id]);
                    condition[winner.Id] = rested;
                    if (!final && (!rested.CanContinue || injured?.Invoke(winner) == true))
                    {
                        withdrew = winner.Id;
                        winner = null;
                    }
                }

                if (winner is null && !final && alternate is not null && !alternateUsed)
                {
                    winner = alternate;
                    alternateUsed = true;
                    how = drawOut ? BracketAdvance.AlternateAfterDraw : BracketAdvance.AlternateForWithdrawal;
                }
                else if (winner is null)
                {
                    how = BracketAdvance.NobodyAdvanced;
                    if (final) finalDrawn = drawOut;
                }

                bouts.Add(new BracketBout(bracketRound, red.Id, blue.Id, r.Fight, o, winner?.Id, how, withdrew));
                next.Add(winner);
            }
            slots = next;
            bracketRound++;
        }
        return new BracketNight(slots[0], bouts, finalDrawn);
    }
}
