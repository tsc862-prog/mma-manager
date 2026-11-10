using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

// Placeholder console host (UI framework will be WPF later — see PROJECT.md).
// It now drives the living world: build a multi-promotion world, then advance
// time and watch it evolve — events, development, injuries, retirements, and a
// fresh intake of prospects.

var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2026 });
var start = world.CurrentDate;
var player = world.Get(world.PlayerPromotion)!;

Console.WriteLine("=== The world, day one ===");
foreach (var p in world.Promotions.Values.OrderByDescending(p => p.Prestige.Value))
    Console.WriteLine($"  {p.Abbreviation,-5} {p.Name,-28} prestige {p.Prestige.Value,2}  roster {world.RosterOf(p.Id).Count(),3}  {p.Balance}");
Console.WriteLine($"  Population: {Active().Count()} active fighters, avg age {Active().Average(f => f.AgeOn(world.CurrentDate)):0.0}\n");

// Remember everyone's starting ability so we can spotlight who rose the most.
var caAtStart = world.Fighters.Values.ToDictionary(f => f.Id, FighterAbility.CurrentAbility);

// Advance three years, reporting each year.
var tick = new WorldTick();
for (int year = 1; year <= 3; year++)
{
    var r = tick.AdvanceWeeks(world, 52);
    Console.WriteLine($"--- Year {year} ({world.CurrentDate:yyyy}) ---");
    Console.WriteLine($"  {r.EventsRun} events, {r.Fights} fights ({100.0 * r.Finishes / Math.Max(1, r.Fights):0.0}% finishes) | "
        + $"{r.Injuries} injuries, {r.Retired} retired, {r.Newgens} prospects in, {r.FreeAgentSignings} signed | "
        + $"now {Active().Count()} active, avg age {Active().Average(f => f.AgeOn(world.CurrentDate)):0.0}");
}

Console.WriteLine($"\n=== {player.Abbreviation} champions, {world.CurrentDate:yyyy} ===");
foreach (var div in player.Divisions)
{
    var title = world.Get(div.Title!.Value)!;
    var champ = title.CurrentChampion is { } id ? world.Get(id) : null;
    string holder = champ is null ? "(vacant)" : $"{champ.FullName,-28} {champ.Record,-10} age {champ.AgeOn(world.CurrentDate)}";
    Console.WriteLine($"  {div.WeightClass,-16} {holder}  [changed {title.Lineage.Count - 1}x]");
}

Console.WriteLine("\n=== Biggest risers (prospects who developed) ===");
var risers = world.Fighters.Values
    .Where(f => f.Status != FighterStatus.Retired && caAtStart.ContainsKey(f.Id))
    .Select(f => (f, gain: FighterAbility.CurrentAbility(f) - caAtStart[f.Id]))
    .OrderByDescending(x => x.gain)
    .Take(6);
foreach (var (f, gain) in risers)
    Console.WriteLine($"  {f.FullName,-28} {f.WeightClass,-14} age {f.AgeOn(world.CurrentDate),2}  CA {caAtStart[f.Id]}→{FighterAbility.CurrentAbility(f)} (+{gain}), PA {f.PotentialAbility.Value}");

Console.WriteLine("\n=== Pound-for-pound (top active fighters, world-wide) ===");
var p4p = world.Fighters.Values
    .Where(f => f.Status == FighterStatus.Active)
    .OrderByDescending(FighterAbility.CurrentAbility)
    .Take(8);
int rank = 1;
foreach (var f in p4p)
{
    var org = f.Contract is { } c ? world.Get(c.Promotion)?.Abbreviation ?? "FA" : "FA";
    Console.WriteLine($"  {rank++,2}. {f.FullName,-28} {org,-5} {f.WeightClass,-14} OVR {FighterAbility.CurrentAbility(f)}  {f.Record}");
}

IEnumerable<Fighter> Active() => world.Fighters.Values.Where(f => f.Status != FighterStatus.Retired);
