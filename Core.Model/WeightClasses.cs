namespace MmaManager.Core.Model;

/// <summary>Static reference data for weight classes (upper limit in pounds).</summary>
public static class WeightClasses
{
    public static int LimitLbs(WeightClass wc) => wc switch
    {
        WeightClass.Strawweight     => 115,
        WeightClass.Flyweight       => 125,
        WeightClass.Bantamweight    => 135,
        WeightClass.Featherweight   => 145,
        WeightClass.Lightweight     => 155,
        WeightClass.Welterweight    => 170,
        WeightClass.Middleweight    => 185,
        WeightClass.LightHeavyweight => 205,
        WeightClass.Heavyweight     => 265,
        _ => throw new ArgumentOutOfRangeException(nameof(wc), wc, null)
    };
}
