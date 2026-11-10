namespace MmaManager.Core.Model;

/// <summary>
/// Bad blood between two fighters, built from close fights and rematches. Intensity
/// (0–100) grows each time they clash; a rivalry bout draws better and the feed
/// tells the story. Held at the world level so it survives either fighter changing orgs.
/// </summary>
public sealed class Rivalry
{
    public required FighterId A { get; init; }
    public required FighterId B { get; init; }
    public int Intensity { get; set; }
    public required DateOnly Since { get; set; }

    public bool Involves(FighterId id) => A == id || B == id;
    public bool IsBetween(FighterId x, FighterId y) => (A == x && B == y) || (A == y && B == x);
    public FighterId Other(FighterId id) => A == id ? B : A;
}
