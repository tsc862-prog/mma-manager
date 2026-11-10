namespace MmaManager.Core.Model;

/// <summary>
/// A 0–100 skill/attribute value — the currency the whole sim runs on.
/// Clamps on construction, so an out-of-range rating can never exist anywhere
/// in the model. Implicit conversions to/from int keep sim math and
/// assignment ergonomic (e.g. <c>fighter.Physical.Chin = 82;</c>).
/// </summary>
public readonly struct Rating : IComparable<Rating>, IEquatable<Rating>
{
    public const int Min = 0;
    public const int Max = 100;

    public int Value { get; }

    public Rating(int value) => Value = Math.Clamp(value, Min, Max);

    public static implicit operator Rating(int value) => new(value);
    public static implicit operator int(Rating rating) => rating.Value;

    public int CompareTo(Rating other) => Value.CompareTo(other.Value);
    public bool Equals(Rating other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Rating r && Equals(r);
    public override int GetHashCode() => Value;
    public override string ToString() => Value.ToString();
}
