namespace MmaManager.Core.Model;

/// <summary>
/// Currency-neutral money, backed by <see cref="decimal"/> so finances never
/// suffer floating-point rounding drift. Amounts are whole-currency units.
/// </summary>
public readonly record struct Money(decimal Amount)
{
    public static Money Zero => new(0m);

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);
    public static Money operator *(Money a, decimal factor) => new(a.Amount * factor);

    public override string ToString() => Amount.ToString("C0");
}
