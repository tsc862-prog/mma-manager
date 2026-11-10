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
