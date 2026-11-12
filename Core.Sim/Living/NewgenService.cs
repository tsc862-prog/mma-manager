using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// Feeds the future into the world: periodically generates a fresh intake of
/// young prospects as free agents, so as veterans retire the talent pool keeps
/// renewing. Newgens are raw (low current ability) but carry the potential the
/// generator gives the young, so some become tomorrow's champions.
/// </summary>
public sealed class NewgenService
{
    private readonly FighterGenerator _gen;

    public NewgenService(ulong worldSeed, IdentityCatalog? catalog = null) =>
        _gen = new FighterGenerator(worldSeed, catalog);

    // Portrait-first like the initial build: newgens draw a pack identity so
    // their portrait genuinely fits (weight band, culture, style).
    public static NewgenService ForWorld(World world) => new(world.RngSeed, world.ArtCatalog as IdentityCatalog);

    /// <summary>Generate <paramref name="count"/> young prospects and add them as free agents.</summary>
    public IReadOnlyList<Fighter> GenerateIntake(World world, int count, SimRandom rng)
    {
        var intake = new List<Fighter>(count);
        for (int i = 0; i < count; i++)
        {
            var id = world.NewFighterId();

            // Dynasties: now and then a prospect arrives carrying a famous name —
            // the kid brother or the next generation of an established star. They
            // inherit the surname, the homeland, and a slice of the spotlight
            // (with all the pressure that comes with it).
            var elder = rng.Chance(0.07) ? PickElder(world, rng) : null;
            // A prospect turning pro debuts 0-0 — their record is written in the
            // cage from here on (the rolled backstory record is only for the
            // initial build, where HistoryBuilder makes it REAL fights).
            var options = elder is null
                ? new GenOptions { Age = rng.Range(18, 22), WithRecord = false }
                : new GenOptions
                {
                    Age = rng.Range(18, 22),
                    WithRecord = false,
                    Nationality = elder.Nationality,
                    CountryCode = world.Countries.TryGetValue(elder.Country, out var c) ? c.Code : null,
                    Sex = elder.Sex,   // keeps the sibling/junior story simple and the division relevant
                    // A woman fights where women's divisions run: near the elder's
                    // class, picked by a hash of the id (not a draw on this stream).
                    WeightClass = elder.Sex == Sex.Female ? WomensClassNear(world, id, elder.WeightClass) : null,
                };

            var prospect = _gen.Generate(id, world.CurrentDate, options);
            _gen.AssignHometown(world, prospect);
            world.Fighters[id] = prospect;
            intake.Add(prospect);

            if (elder is not null) TieDynasty(world, prospect, elder, rng);
            Camps.JoinLocalCamp(world, prospect, rng);   // find a room near home
        }
        return intake;
    }

    /// <summary>
    /// A women's class for a famous woman's junior: the elder's own class or one
    /// either side of it (clamped to the classes women fight at), chosen by a
    /// hash of the prospect's id — deterministic, and no draw on any stream.
    /// </summary>
    internal static WeightClass WomensClassNear(World world, FighterId id, WeightClass elderClass)
    {
        var classes = WomensDivisions.Classes;
        int at = Array.IndexOf(classes, elderClass);
        if (at < 0) at = elderClass < classes[0] ? 0 : classes.Length - 1;   // nearest end of the women's range
        int step = (int)(SimRandom.Mix(world.RngSeed, (ulong)id.Value ^ 0xD1_4A57_7EUL) % 3) - 1;
        return classes[Math.Clamp(at + step, 0, classes.Length - 1)];
    }

    // A name worth inheriting: a well-known fighter with a real career behind them.
    private static Fighter? PickElder(World world, SimRandom rng)
    {
        var famous = world.Fighters.Values
            .Where(f => f.Intangibles.Popularity.Value >= 55 && f.Record.TotalBouts >= 12)
            .OrderByDescending(f => f.Intangibles.Popularity.Value)
            .Take(30)
            .ToList();
        return famous.Count == 0 ? null : famous[rng.Range(0, famous.Count - 1)];
    }

    private static void TieDynasty(World world, Fighter prospect, Fighter elder, SimRandom rng)
    {
        prospect.LastName = elder.LastName;
        int gap = elder.AgeOn(world.CurrentDate) - prospect.AgeOn(world.CurrentDate);
        string relation = gap >= 16
            ? prospect.Sex == Sex.Female ? "daughter" : "son"
            : prospect.Sex == Sex.Female ? "younger sister" : "younger brother";

        // A famous name opens doors (and sells tickets) before the first bell.
        prospect.Intangibles.Popularity = Math.Min(35, elder.Intangibles.Popularity.Value * 2 / 5);

        world.Relationships.Add(new Relationship
        {
            A = elder.Id, B = prospect.Id, Kind = RelationshipKind.Family,
            Strength = 85, Since = world.CurrentDate,
            Note = $"{relation} of {elder.FullName}",
        });
        News.Post(world, world.CurrentDate, NewsCategory.Debut,
            $"A famous name turns pro: {prospect.FullName}",
            $"The {relation} of {elder.FullName} is stepping into the cage. The name opens doors — and invites comparisons.",
            elder.Contract?.Promotion ?? default, prospect.Id);
    }
}
