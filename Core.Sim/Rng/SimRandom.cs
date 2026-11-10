namespace MmaManager.Core.Sim.Rng;

/// <summary>
/// The single deterministic random source the whole sim runs on. A fixed seed
/// reproduces an identical stream on every platform and .NET version (unlike
/// <see cref="System.Random"/>, whose algorithm is not contractually stable),
/// which is what makes balance testing and bug reproduction possible.
///
/// Core generator is splitmix64 — tiny, fast, and statistically solid for game
/// use. Derive independent sub-streams with <see cref="Mix"/> so, e.g., a given
/// fighter id always maps to the same fighter for a given world seed regardless
/// of generation order.
/// </summary>
public sealed class SimRandom
{
    private ulong _state;

    public SimRandom(ulong seed) => _state = seed;

    /// <summary>Deterministically fold a stream id into a seed to spawn an independent sub-stream.</summary>
    public static ulong Mix(ulong seed, ulong stream)
    {
        unchecked
        {
            ulong z = seed + stream * 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Next raw 64-bit value (splitmix64).</summary>
    public ulong NextULong()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        ulong range = (ulong)(maxExclusive - minInclusive);
        return minInclusive + (int)(NextULong() % range);
    }

    /// <summary>Uniform int in [min, max], both ends inclusive.</summary>
    public int Range(int min, int max) => Next(min, max + 1);

    /// <summary>True with probability <paramref name="p"/> (clamped to [0,1]).</summary>
    public bool Chance(double p) => NextDouble() < p;

    /// <summary>Normally distributed value (Box–Muller).</summary>
    public double NextGaussian(double mean = 0.0, double stdDev = 1.0)
    {
        // u1 must be > 0 for Log.
        double u1 = 1.0 - NextDouble();
        double u2 = NextDouble();
        double standard = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + stdDev * standard;
    }

    /// <summary>Gaussian, rounded and clamped into an inclusive integer range.</summary>
    public int GaussianInt(double mean, double stdDev, int min, int max) =>
        Math.Clamp((int)Math.Round(NextGaussian(mean, stdDev)), min, max);

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(0, items.Count)];

    /// <summary>Index into <paramref name="weights"/> chosen proportionally to weight.</summary>
    public int WeightedIndex(ReadOnlySpan<double> weights)
    {
        double total = 0;
        foreach (double w in weights) total += w > 0 ? w : 0;
        if (total <= 0) return 0;

        double roll = NextDouble() * total;
        for (int i = 0; i < weights.Length; i++)
        {
            double w = weights[i] > 0 ? weights[i] : 0;
            if (roll < w) return i;
            roll -= w;
        }
        return weights.Length - 1;
    }
}
