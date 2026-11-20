using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Geography;

/// <summary>
/// Fame that travels without the fighter: a card on television makes the men at the top of it heard of wherever
/// the signal goes, and a belt makes its champion known wherever the promotion that hangs it is known. Both are
/// REMOTE fame and both stop well short of what a city learns by watching a man fight in front of it
/// (<see cref="Reputation.BumpFighter"/>, which has no ceiling below 100): remote fame never lifts a city past
/// <see cref="RemoteCeiling"/> ("a few fans"), so a star's map still shows a home and the places he has fought,
/// with a thin glow across the broadcast footprint.
/// <para>
/// Everything is derived from what the world already models: the screen is the strongest deal the house held
/// when the card aired (its kind and the partner's reach), the footprint is where the PROMOTION is known
/// (<see cref="Promotion.PopularityByCity"/>), the depth is the fighter's own public popularity and his place on the
/// card, and a belt's lift is the belt's <see cref="Title.Prestige"/>. No randomness at all, no new model field.
/// Each city's number is computed on its own, so the order cities are walked in never matters.
/// </para>
/// <para>
/// Bounded writes: a city is written only when the target there is at least <see cref="MinWrite"/> and above what
/// the fighter already has, and only for the main card (prelims get nothing). The dictionary can never hold more
/// entries than the world has cities.
/// </para>
/// </summary>
public static class BroadcastFame
{
    /// <summary>The most any city can learn of a man it has never seen fight: just under "known" (25) on the map's scale.</summary>
    public const int RemoteCeiling = 24;

    /// <summary>Below this a remote target is not worth a dictionary entry.</summary>
    public const int MinWrite = 3;

    /// <summary>Share of the gap to the target closed by one ordinary televised main event (PPV and super-card nights more).</summary>
    public const double TvRate = 0.25, BigNightRate = 0.4;

    /// <summary>Share of the gap closed by winning a belt, and by defending one.</summary>
    public const double NewChampRate = 0.5, DefenseRate = 0.25;

    /// <summary>A screen: how high it can lift a stranger city (before the partner's reach) and where it is watched --
    /// home-region cities where the brand is at least <paramref name="HomeFloor"/>, cities abroad at least
    /// <paramref name="AbroadFloor"/> (int.MaxValue = not carried abroad).</summary>
    public readonly record struct Screen(MediaKind Kind, double Cap, int HomeFloor, int AbroadFloor);

    private static (double Cap, int HomeFloor, int AbroadFloor) Shape(MediaKind kind) => kind switch
    {
        MediaKind.InHouseStream => (8, 10, 40),               // the house's own app: its faithful, mostly at home
        MediaKind.SpecialtyCable => (10, 10, int.MaxValue),   // a niche channel: the home region only
        MediaKind.CableSports => (14, 8, 30),                 // national cable: home, and abroad only where the brand is big
        MediaKind.PpvCarrier => (18, 5, 15),                  // the buys pipe: wherever fans already pay for the brand
        MediaKind.PrimeBroadcast => (22, 5, 15),              // over the air in prime time: the biggest single audience
        MediaKind.MajorStream => (20, 5, 5),                  // a worldwide streamer: everywhere the brand is known at all, thinly
        _ => (0, int.MaxValue, int.MaxValue),
    };

    /// <summary>
    /// The screen this card aired on: the strongest of the house's deals (the PPV carrier only on a PPV night) and its
    /// own platform. Null when nobody aired it.
    /// </summary>
    public static Screen? ScreenFor(Promotion promo, Event ev)
    {
        Screen? best = null;
        void Consider(MediaKind kind, double reach)
        {
            var (cap, home, abroad) = Shape(kind);
            var s = new Screen(kind, Math.Min(RemoteCeiling, cap * reach), home, abroad);
            if (s.Cap > 0 && (best is null || s.Cap > best.Value.Cap)) best = s;
        }
        foreach (var d in promo.MediaDeals)
        {
            if (d.Kind == MediaKind.PpvCarrier && ev.Tier < EventTier.Ppv) continue;
            Consider(d.Kind, Finance.MediaService.ReachScaleOf(d.Partner));
        }
        if (promo.Stream is not null) Consider(MediaKind.InHouseStream, 1.0);
        return best;
    }

    /// <summary>How much of the screen a card slot gets: the main event all of it, the co-main most, the rest of the main card some, prelims none.</summary>
    public static double PositionShare(Event ev, int index)
    {
        int n = ev.Card.Count;
        if (index == n - 1) return 1.0;
        if (ev.IsPrelim(ev.Card[index])) return 0;
        return index == n - 2 ? 0.6 : 0.4;
    }

    /// <summary>
    /// After a card has run and its gate is settled (tonight's broadcast does not sell tonight's tickets): the men on
    /// its main card become heard of across the screen's footprint, and every champion crowned or confirmed tonight
    /// becomes known wherever the house is.
    /// </summary>
    public static void AfterCard(World world, Promotion promo, Event ev)
    {
        if (ev.Card.Count == 0) return;
        var screen = ScreenFor(promo, ev);
        List<(CityId City, int Brand)>? tv = screen is { } s ? Footprint(world, promo, s.HomeFloor, s.AbroadFloor) : null;
        List<(CityId City, int Brand)>? known = null;

        for (int i = 0; i < ev.Card.Count; i++)
        {
            var fight = ev.Card[i];
            if (fight.Outcome is not { } outcome) continue;

            if (tv is { Count: > 0 } && screen is { } sc)
            {
                double share = PositionShare(ev, i);
                if (share > 0)
                {
                    double rate = i == ev.Card.Count - 1 && ev.Tier >= EventTier.Ppv ? BigNightRate : TvRate;
                    foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                        if (world.Get(id) is { LocalTalent: false } f)
                            Televise(f, tv, sc.Cap * share, rate);
                }
            }

            // A belt won or kept tonight (a draw keeps it too, but makes nobody's name).
            if (fight.IsTitleFight && fight.Title is { } tid && world.Get(tid) is { } title
                && outcome.Winner is { } wid && title.CurrentChampion == wid && world.Get(wid) is { } champ)
            {
                known ??= Footprint(world, promo, BeltFloor, BeltFloor);
                bool crowned = title.Lineage.LastOrDefault(r => r.LostOn is null) is { } reign
                               && reign.Champion == wid && reign.WonOn == ev.Date;
                LiftForBelt(champ, known, title.Prestige.Value, crowned ? NewChampRate : DefenseRate);
            }
        }
    }

    /// <summary>A belt is news wherever the brand is at least this well known.</summary>
    public const int BeltFloor = 10;

    /// <summary>The cities a screen (or a belt) reaches, with the brand's standing in each.</summary>
    internal static List<(CityId City, int Brand)> Footprint(World world, Promotion promo, int homeFloor, int abroadFloor)
    {
        var list = new List<(CityId, int)>();
        foreach (var kv in promo.PopularityByCity)
        {
            if (world.Get(kv.Key) is not { } c) continue;
            int floor = c.Region == promo.BaseRegion ? homeFloor : abroadFloor;
            if (kv.Value.Value >= floor) list.Add((kv.Key, kv.Value.Value));
        }
        return list;
    }

    /// <summary>
    /// One televised appearance: in each footprint city the target is the screen's cap at this slot, held down by how
    /// famous the man is and how well the city knows the brand (popularity x brand / 250: a 90-pop star where the brand
    /// is at 60 is worth ~22); the city closes <paramref name="rate"/> of the gap.
    /// </summary>
    internal static void Televise(Fighter f, List<(CityId City, int Brand)> footprint, double slotCap, double rate)
    {
        int pop = f.Intangibles.Popularity.Value;
        foreach (var (city, brand) in footprint)
            Approach(f, city, Math.Min(slotCap, pop * brand / 250.0), rate);
    }

    /// <summary>
    /// A belt won or defended: in each city where the house is known the target is the brand's standing there times the
    /// belt's prestige / 200 (a prestige-80 belt where the brand is at 60 is worth the ceiling; a club strap barely
    /// registers), never past <see cref="RemoteCeiling"/>.
    /// </summary>
    internal static void LiftForBelt(Fighter f, List<(CityId City, int Brand)> footprint, int prestige, double rate)
    {
        foreach (var (city, brand) in footprint)
            Approach(f, city, Math.Min(RemoteCeiling, brand * prestige / 200.0), rate);
    }

    private static void Approach(Fighter f, CityId city, double target, double rate)
    {
        int t = (int)Math.Min(RemoteCeiling, target);
        if (t < MinWrite) return;
        int cur = f.CityReputation.GetValueOrDefault(city).Value;
        if (cur >= t) return;   // remote fame never lowers what a city already knows
        int gain = Math.Max(1, (int)Math.Round((t - cur) * rate));
        f.CityReputation[city] = Math.Min(t, cur + gain);
    }
}
