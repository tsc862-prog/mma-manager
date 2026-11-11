using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Finance;

/// <summary>
/// The media ladder: who airs the promotion's fights, what they pay per event,
/// and what they demand for it. Three lanes can be occupied at once — one
/// linear TV home, one streaming home (the org's own platform or a licensing
/// giant), and one pay-per-view carrier. Networks approach a promotion that
/// qualifies (offers land in the mail); every deal writes a broadcast window
/// (main-card min/max), quality/star floors, and a content cadence into the
/// contract, and enough strikes gets the promotion dropped mid-term.
///
/// Media is OPTIONAL money but mandatory OXYGEN: the platform sets how loudly a
/// show echoes — fighter fame gains and the brand's own climb both scale with
/// the biggest screen the org is on (<see cref="Exposure"/>, <see cref="BrandFactor"/>).
/// An org with no screen at all can still run great cards; it just can't turn
/// them into stars or standing at full rate.
/// </summary>
public static class MediaService
{
    public const int StrikeLimit = 3;

    /// <summary>
    /// What a card at the top of the sport rates: the median of International
    /// promotions' card averages as <see cref="DealCompliance"/> reads them.
    /// Every card-quality clause is written RELATIVE to this, so when the
    /// fight engine changes how fights rate, this one number is re-measured
    /// (`longsim compare` prints card quality by tier) and the whole term sheet
    /// follows. Measured on the #37 engine (40 seeds x 5 years): International
    /// median 48.6, p10 40.8, p25 44.5; share of cards clearing 42 / 45 = 86% / 72%.
    /// Prime time sits at par-7 (41) and its strict flagship partner at 43, so
    /// the #1 promotion's ordinary cards clear its strictest partner about 80-85%
    /// of the time; cable sports at par-12 (36, flagship 38) clears ~95%.
    /// </summary>
    public const int CardQualityPar = 48;

    // --- The ladder -----------------------------------------------------------

    /// <summary>
    /// A network's PERSONALITY within its rung — the tiers aren't palette
    /// swaps. The flagship pays top dollar and carries you furthest but writes
    /// the strictest contract; the niche channel loves the sport and asks
    /// little but pays and carries less; the budget option is easy money on a
    /// small screen. FeeScale multiplies the rung's going rate (for the PPV
    /// carriers, whose fee is zero, it IS the promotion's share of every buy);
    /// ReachScale scales the exposure/fame/brand carry; Strictness -1/0/+1
    /// loosens or tightens the contract's clauses.
    /// </summary>
    private sealed record PartnerProfile(string Name, string Tag, decimal FeeScale, double ReachScale, int Strictness);

    private sealed record Rung(
        MediaKind Kind,
        PartnerProfile[] Partners,
        int TermMonths,
        Func<World, Promotion, bool> Qualifies,
        Func<Promotion, decimal> Fee,
        Func<Promotion, DealRequirement[]> Terms);

    private static readonly Rung[] Ladder =
    {
        new(MediaKind.SpecialtyCable,
            new PartnerProfile[]
            {
                new("Combat One", "standard", 1.0m, 1.0, 0),
                new("Octagon TV", "flagship", 1.2m, 1.1, +1),
                new("The Fight Channel", "niche", 0.8m, 0.85, -1),
                new("Ring Report", "budget", 0.65m, 0.8, -1),
            },
            18,
            (w, p) => Org.OrgSizing.SizeOf(p) >= OrgSize.Regional && p.Prestige.Value >= 24,
            p => 30_000m + Math.Max(0, p.Prestige.Value - 20) * 3_000m,
            _ => new[]
            {
                new DealRequirement(DealRequirementKind.MinMainCard, 3),
                new DealRequirement(DealRequirementKind.MaxMainCard, 5),
                new DealRequirement(DealRequirementKind.EventCadence, 100),
            }),

        new(MediaKind.CableSports,
            new PartnerProfile[]
            {
                new("PrimeSport", "flagship", 1.25m, 1.1, +1),
                new("Vertex Sports", "standard", 1.0m, 1.0, 0),
                new("Global Combat Network", "niche", 0.85m, 0.9, -1),
                new("AllSport 24", "budget", 0.7m, 0.85, -1),
            },
            24,
            (w, p) => Org.OrgSizing.SizeOf(p) >= OrgSize.National,
            p => 150_000m + Math.Max(0, p.Prestige.Value - 32) * 15_000m,
            _ => new[]
            {
                new DealRequirement(DealRequirementKind.MinMainCard, 4),
                new DealRequirement(DealRequirementKind.MaxMainCard, 6),
                new DealRequirement(DealRequirementKind.MinCardQuality, CardQualityPar - 12),
                new DealRequirement(DealRequirementKind.EventCadence, 60),
            }),

        new(MediaKind.PrimeBroadcast,
            new PartnerProfile[]
            {
                new("NBS", "standard", 1.0m, 1.0, 0),
                new("United Broadcasting", "flagship", 1.2m, 1.1, +1),
                new("Star Network", "niche", 0.85m, 0.95, -1),
                new("Meridian One", "upstart", 0.9m, 0.9, -1),
            },
            24,
            (w, p) => Org.OrgSizing.SizeOf(p) >= OrgSize.Continental
                   || (Org.OrgSizing.SizeOf(p) >= OrgSize.National && TopDraw(w, p) >= 70),
            p => 500_000m + Math.Max(0, p.Prestige.Value - 45) * 30_000m,
            _ => new[]
            {
                new DealRequirement(DealRequirementKind.MinMainCard, 5),
                new DealRequirement(DealRequirementKind.MaxMainCard, 5),
                new DealRequirement(DealRequirementKind.MinStarPower, 60),
                new DealRequirement(DealRequirementKind.MinCardQuality, CardQualityPar - 7),
                new DealRequirement(DealRequirementKind.EventCadence, 75),
            }),

        new(MediaKind.MajorStream,
            new PartnerProfile[]
            {
                new("Streamflix", "flagship", 1.25m, 1.1, +1),
                new("Arena+", "standard", 1.0m, 1.0, 0),
                new("Watchtower", "niche", 0.85m, 0.9, -1),
                new("Nimbus Video", "upstart", 0.75m, 0.85, -1),
            },
            24,
            (w, p) => Org.OrgSizing.SizeOf(p) >= OrgSize.Continental && p.Prestige.Value >= 50,
            p => 900_000m + Math.Max(0, p.Prestige.Value - 55) * 60_000m,
            _ => new[]
            {
                new DealRequirement(DealRequirementKind.MinMainCard, 5),
                new DealRequirement(DealRequirementKind.MaxMainCard, 7),
                new DealRequirement(DealRequirementKind.MinStarPower, 70),
                new DealRequirement(DealRequirementKind.EventCadence, 120),
            }),

        new(MediaKind.PpvCarrier,
            new PartnerProfile[]
            {
                // The carrier's FeeScale IS the promotion's share of every buy.
                new("MainCard", "standard", 0.55m, 1.0, 0),
                new("PrimeView", "premium", 0.60m, 1.1, +1),
                new("BoxSeat", "budget", 0.50m, 0.9, -1),
            },
            36,
            (w, p) => Org.OrgSizing.SizeOf(p) >= OrgSize.Continental || TopDraw(w, p) >= 80,
            _ => 0m,   // the carrier's value is the pipe, not a fee — see Financials' PPV revenue
            _ => new[]
            {
                new DealRequirement(DealRequirementKind.MinMainCard, 5),
                new DealRequirement(DealRequirementKind.MinStarPower, 55),
            }),
    };

    private static int TopDraw(World world, Promotion p) =>
        world.RosterOf(p.Id).Where(f => f.Status == FighterStatus.Active)
            .Select(f => f.Intangibles.Popularity.Value).DefaultIfEmpty(0).Max();

    private static Rung RungOf(MediaKind kind) => Ladder.First(r => r.Kind == kind);

    // Partner names are unique across the whole ladder, so a deal only needs to
    // carry the NAME — everything about the network's personality derives here.
    private static readonly Dictionary<string, PartnerProfile> Profiles =
        Ladder.SelectMany(r => r.Partners).ToDictionary(p => p.Name);

    private static PartnerProfile ProfileOf(string partner) =>
        Profiles.TryGetValue(partner, out var p) ? p : new PartnerProfile(partner, "standard", 1.0m, 1.0, 0);

    /// <summary>The network's personality tag ("flagship", "niche", "budget"…) for display.</summary>
    public static string TagOf(string partner) => ProfileOf(partner).Tag;

    /// <summary>
    /// A network's contract, in its own voice: the flagship tightens the
    /// screws (higher quality/star floors, a faster cadence), the easy-going
    /// channels loosen them (no quality clause, a patient calendar). The
    /// broadcast WINDOW is left alone — it's the shape of a timeslot, not a
    /// temperament.
    /// </summary>
    private static List<DealRequirement> AdjustTerms(DealRequirement[] baseTerms, int strictness)
    {
        if (strictness == 0) return baseTerms.ToList();
        var adjusted = new List<DealRequirement>();
        foreach (var req in baseTerms)
        {
            switch (req.Kind)
            {
                case DealRequirementKind.MinCardQuality:
                    if (strictness < 0) continue;                    // the easy-going don't grade your cards
                    // A couple of points stricter, not five: the old +5 on prime
                    // time's flat 45 wrote a 50 floor the #1 promotion missed on
                    // 56-58% of nights (both engines) — a contract nobody in
                    // the world could keep. Now par-7+2 = 43 (~82% of top cards clear it).
                    adjusted.Add(req with { Threshold = req.Threshold + 2 });
                    break;
                case DealRequirementKind.MinStarPower:
                    adjusted.Add(req with { Threshold = Math.Max(30, req.Threshold + strictness * 5) });
                    break;
                case DealRequirementKind.EventCadence:
                    adjusted.Add(req with { Threshold = strictness > 0 ? (int)(req.Threshold * 0.75) : (int)(req.Threshold * 1.5) });
                    break;
                default:
                    adjusted.Add(req);
                    break;
            }
        }
        return adjusted;
    }

    // --- Lane bookkeeping -----------------------------------------------------

    public static MediaDeal? DealIn(Promotion p, MediaLane lane) =>
        p.MediaDeals.FirstOrDefault(d => MediaLanes.LaneOf(d.Kind) == lane);

    /// <summary>The streaming lane counts the org's own platform as occupied.</summary>
    public static bool LaneOpen(Promotion p, MediaKind kind) =>
        DealIn(p, MediaLanes.LaneOf(kind)) is null
        && !(MediaLanes.LaneOf(kind) == MediaLane.Streaming && p.Stream is not null);

    /// <summary>Can this promotion sell a pay-per-view? Only with a carrier signed on.</summary>
    public static bool HasPpvCarrier(Promotion p) =>
        p.MediaDeals.Any(d => d.Kind == MediaKind.PpvCarrier);

    // --- The partner market ---------------------------------------------------
    // A channel's fight-night slot is EXCLUSIVE: a network carrying one
    // promotion isn't shopping for a second. The PPV carrier is a pipe, not a
    // slot — it can carry everyone's pay-per-views at once.

    private static bool IsExclusive(MediaKind kind) => kind != MediaKind.PpvCarrier;

    /// <summary>Is this partner free to sign someone (not carrying another promotion)?</summary>
    public static bool PartnerFree(World world, string partner) =>
        !world.Promotions.Values.Any(p => !p.Defunct && p.MediaDeals.Any(d => d.Partner == partner && IsExclusive(d.Kind)));

    /// <summary>
    /// The partner re-enters the market: everyone they ever pitched forgets the
    /// old courtship, so they can come knocking again — including on promotions
    /// that passed the first time.
    /// </summary>
    private static void ReleasePartner(World world, string partner)
    {
        foreach (var p in world.Promotions.Values) p.CourtedBy.Remove(partner);
    }

    /// <summary>A partner an offer could come from: free, and never pitched this promotion before.</summary>
    private static PartnerProfile? Suitor(World world, Promotion promo, Rung rung, SimRandom rng)
    {
        var candidates = rung.Partners
            .Where(pp => !promo.CourtedBy.Contains(pp.Name)
                && !promo.Offers.Any(o => o.Partner == pp.Name)
                && (!IsExclusive(rung.Kind) || PartnerFree(world, pp.Name)))
            .ToList();
        return candidates.Count == 0 ? null : candidates[rng.Range(0, candidates.Count - 1)];
    }

    // --- Exposure: what the screen does beyond the check ----------------------

    /// <summary>Fame multiplier for main-card wins, from the biggest screen the org is on — scaled by how far that network actually carries (a niche channel's prime hour isn't the flagship's).</summary>
    public static double Exposure(Promotion p)
    {
        double best = 1.0;
        foreach (var d in p.MediaDeals)
            best = Math.Max(best, 1.0 + (ExposureOf(d.Kind) - 1.0) * ProfileOf(d.Partner).ReachScale);
        if (p.Stream is not null) best = Math.Max(best, ExposureOf(MediaKind.InHouseStream));
        return best;
    }

    /// <summary>How much higher the platform lifts the org's fame ceiling.</summary>
    public static int FameCeilingBump(Promotion p)
    {
        int best = 0;
        foreach (var d in p.MediaDeals)
            best = Math.Max(best, (int)Math.Round(BumpOf(d.Kind) * ProfileOf(d.Partner).ReachScale));
        if (p.Stream is not null) best = Math.Max(best, BumpOf(MediaKind.InHouseStream));
        return best;
    }

    /// <summary>
    /// How fully a show converts into BRAND standing. A promotion nobody can
    /// watch grows slower than its product deserves; a prime-time platform
    /// amplifies every good card. Applied to positive standing swings only.
    /// </summary>
    public static double BrandFactor(Promotion p)
    {
        double best = 0.75;   // no screen at all — word of mouth only
        foreach (var d in p.MediaDeals)
            best = Math.Max(best, 0.75 + (BrandOf(d.Kind) - 0.75) * ProfileOf(d.Partner).ReachScale);
        if (p.Stream is not null) best = Math.Max(best, BrandOf(MediaKind.InHouseStream));
        return best;
    }

    /// <summary>
    /// The promotion's cut of every pay-per-view buy — set by WHICH carrier
    /// signed them (the premium pipe pays 60%, the budget one 50%). The old
    /// flat 55% when no profile is known.
    /// </summary>
    public static decimal PpvShare(Promotion p) =>
        p.MediaDeals.FirstOrDefault(d => d.Kind == MediaKind.PpvCarrier) is { } carrier
            ? ProfileOf(carrier.Partner).FeeScale
            : 0.55m;

    private static double ExposureOf(MediaKind k) => k switch
    {
        MediaKind.InHouseStream => 1.05,
        MediaKind.SpecialtyCable => 1.15,
        MediaKind.CableSports => 1.25,
        MediaKind.MajorStream => 1.4,
        MediaKind.PrimeBroadcast => 1.5,
        MediaKind.PpvCarrier => 1.35,
        _ => 1.0,
    };

    private static int BumpOf(MediaKind k) => k switch
    {
        MediaKind.InHouseStream => 1,
        MediaKind.SpecialtyCable => 2,
        MediaKind.CableSports => 4,
        MediaKind.MajorStream => 6,
        MediaKind.PrimeBroadcast => 6,
        MediaKind.PpvCarrier => 5,
        _ => 0,
    };

    private static double BrandOf(MediaKind k) => k switch
    {
        MediaKind.InHouseStream => 0.9,
        MediaKind.SpecialtyCable => 1.0,
        MediaKind.CableSports => 1.1,
        MediaKind.MajorStream => 1.2,
        MediaKind.PrimeBroadcast => 1.25,
        MediaKind.PpvCarrier => 1.1,
        _ => 0.75,
    };

    // --- The in-house streaming platform --------------------------------------

    public static readonly Money StreamLaunchCost = new(50_000m);
    private const decimal NetPerSubscriber = 6m;      // monthly sub price, after the app stores' cut
    private const decimal StreamOpsBase = 6_000m;     // keeping the lights on
    private const decimal StreamOpsPerSub = 0.6m;     // bandwidth and support scale with the audience

    /// <summary>
    /// Launch the promotion's own service — available to anyone from day one,
    /// for real money. Fails quietly if one exists or the cash isn't there.
    /// </summary>
    public static bool LaunchStream(World world, Promotion promo, DateOnly date)
    {
        if (promo.Stream is not null || !LaneOpen(promo, MediaKind.InHouseStream)) return false;
        if (promo.Balance.Amount < StreamLaunchCost.Amount) return false;

        Ledger.Expense(promo, date, FinanceCategory.StreamingOps, StreamLaunchCost, "Streaming platform launch");
        promo.Stream = new StreamingPlatform { Name = $"{promo.Abbreviation}+", LaunchedOn = date };

        Living.News.Post(world, date, NewsCategory.Finance,
            $"{promo.Abbreviation} launches its own streaming service",
            $"{promo.Stream.Name} goes live — every card, straight to the fans, no network in between.", promo.Id);
        if (promo.Id == world.PlayerPromotion)
            Org.Milestones.Award(world, promo, date, "stream-launch", "Our own platform",
                $"{promo.Stream.Name} is live. Subscribers grow with every card we run — and melt away when we go dark.");
        return true;
    }

    /// <summary>
    /// Where the subscriber count is headed: a following the size of the brand
    /// (prestige) plus the pull of its biggest name, all scaled by how ALIVE
    /// the promotion is — no cards, no reason to subscribe.
    /// </summary>
    private static int SubscriberPotential(World world, Promotion p)
    {
        int prestige = p.Prestige.Value;
        int star = TopDraw(world, p);
        double activity = Math.Min(1.0, EventsInWindow(world, p, 120) / 3.0);
        return (int)((prestige * prestige * 25.0 + star * star * 1.5) * activity);
    }

    private static void TickStream(World world, Promotion promo, DateOnly date)
    {
        if (promo.Stream is not { } s) return;

        // Subscribers chase the platform's potential — up when the product earns
        // it, and away the moment the promotion stops giving people a reason.
        int potential = SubscriberPotential(world, promo);
        double rate = s.Subscribers <= potential ? 0.12 : 0.10;
        s.Subscribers = Math.Max(0, s.Subscribers + (int)Math.Round((potential - s.Subscribers) * rate));

        Ledger.Income(promo, date, FinanceCategory.Streaming,
            new Money(s.Subscribers * NetPerSubscriber), $"{s.Name} — {s.Subscribers:N0} subscribers");
        Ledger.Expense(promo, date, FinanceCategory.StreamingOps,
            new Money(StreamOpsBase + s.Subscribers * StreamOpsPerSub), $"{s.Name} — operations");
    }

    /// <summary>Fight night is the platform's sales pitch: a card closes some of the gap to potential.</summary>
    private static void StreamAirs(World world, Promotion promo, Event ev)
    {
        if (promo.Stream is not { } s) return;
        int potential = SubscriberPotential(world, promo);
        if (potential > s.Subscribers)
            s.Subscribers += (int)((potential - s.Subscribers) * 0.08);
    }

    // --- Seeding (world build) ------------------------------------------------

    /// <summary>
    /// Established orgs open with the media presence their tier has earned:
    /// linear TV at the best rung with a FREE channel (the dial is finite —
    /// the giants claim the flagship networks and the rest work down it), a
    /// PPV pipe and an in-house platform for the giants.
    /// </summary>
    public static void SeedEstablished(World world, Promotion promo, DateOnly on, SimRandom rng)
    {
        foreach (var rung in Ladder
                     .Where(r => MediaLanes.LaneOf(r.Kind) == MediaLane.Linear && r.Qualifies(world, promo))
                     .OrderByDescending(r => r.Fee(promo)))
            if (TrySign(world, promo, rung, on, rng) is not null) break;

        var carrier = RungOf(MediaKind.PpvCarrier);
        if (carrier.Qualifies(world, promo)) TrySign(world, promo, carrier, on, rng);

        // The giants stream themselves; the biggest also license to a giant of
        // the screen — but one home per lane, so the world's #1 gets the license
        // and everyone else at Continental+ runs their own platform.
        var stream = RungOf(MediaKind.MajorStream);
        if (stream.Qualifies(world, promo) && promo.Prestige.Value >= 70
            && TrySign(world, promo, stream, on, rng) is not null)
        { }
        else if (Org.OrgSizing.SizeOf(promo) >= OrgSize.Continental)
            promo.Stream = new StreamingPlatform
            {
                Name = $"{promo.Abbreviation}+",
                LaunchedOn = on,
                Subscribers = SubscriberPotentialSeed(promo),
            };
    }

    // Seeded platforms open at a settled audience (activity assumed healthy).
    private static int SubscriberPotentialSeed(Promotion p) =>
        p.Prestige.Value * p.Prestige.Value * 25;

    /// <summary>Sign this rung with any free partner — null when the whole pool is spoken for.</summary>
    private static MediaDeal? TrySign(World world, Promotion promo, Rung rung, DateOnly on, SimRandom rng)
    {
        var pool = rung.Partners.Where(pp => !IsExclusive(rung.Kind) || PartnerFree(world, pp.Name)).ToList();
        if (pool.Count == 0) return null;
        var profile = pool[rng.Range(0, pool.Count - 1)];
        var deal = new MediaDeal
        {
            Kind = rung.Kind,
            Partner = profile.Name,
            RightsFeePerEvent = new Money(rung.Fee(promo) * profile.FeeScale),
            MonthsRemaining = rung.TermMonths,
            Requirements = AdjustTerms(rung.Terms(promo), profile.Strictness),
            SignedOn = on,
        };
        promo.MediaDeals.Add(deal);
        return deal;
    }

    // --- The monthly market ---------------------------------------------------

    public static void RunMonthly(World world, Promotion promo, DateOnly date, SimRandom rng)
    {
        TickStream(world, promo, date);
        promo.Offers.RemoveAll(o => o.Media is not null && o.ExpiresOn < date);

        // Cadence: a partner that has had nothing to air for too long gets restless.
        foreach (var deal in promo.MediaDeals.ToList())
        {
            var cadence = deal.Requirements.FirstOrDefault(r => r.Kind == DealRequirementKind.EventCadence);
            if (cadence is null) continue;
            var clock = deal.LastAiredOn ?? deal.SignedOn;
            if (date.DayNumber - clock.DayNumber <= cadence.Threshold) continue;

            deal.Strikes++;
            deal.LastAiredOn = date;   // reset the clock so a dark month is one strike, not one per month
            if (promo.Id == world.PlayerPromotion)
                Living.Mail.Post(world, date, MailCategory.BoardNote, deal.Partner,
                    $"{deal.Partner} has nothing to air",
                    $"Our contract promises an event every {cadence.Threshold} days and we've gone quiet. " +
                    (deal.Strikes >= StrikeLimit ? "They've torn the deal up." : $"Strike {deal.Strikes} of {StrikeLimit}."));
            if (deal.Strikes >= StrikeLimit) Drop(world, promo, deal, date, "after the promotion went dark");
        }

        // Terms run down monthly; a clean partner usually renews at today's
        // value — but rights change hands in this business, and once in a
        // while the network goes another direction at expiry through no fault
        // of the promotion's. That churn is what keeps the finite dial ALIVE:
        // without it every channel stays locked to its first signing forever
        // and a rising org could never get on television at all.
        foreach (var deal in promo.MediaDeals.ToList())
        {
            if (--deal.MonthsRemaining > 0) continue;
            var rung = RungOf(deal.Kind);
            if (deal.Strikes == 0 && rung.Qualifies(world, promo) && !rng.Chance(0.25))
            {
                deal.RightsFeePerEvent = new Money(rung.Fee(promo) * ProfileOf(deal.Partner).FeeScale);
                deal.MonthsRemaining = rung.TermMonths;
                deal.SignedOn = date;
                if (promo.Id == world.PlayerPromotion)
                    Living.Mail.Post(world, date, MailCategory.General, deal.Partner,
                        $"{deal.Partner} renews",
                        $"The deal ran clean and they re-upped: {deal.RightsFeePerEvent}/event for {rung.TermMonths} months.");
            }
            else Drop(world, promo, deal, date,
                deal.Strikes > 0 ? "after repeated missed terms"
                : !rung.Qualifies(world, promo) ? "after the promotion slipped below the platform's bar"
                : "after the network went another direction at renewal");
        }

        // The networks come to a promotion worth airing — but each one pitches
        // ONCE. Pass (or let the offer lapse) and they move on for good; they
        // only return to the table when they re-enter the market (their deal
        // with whoever they signed ends). AI orgs answer at once.
        foreach (var rung in Ladder)
        {
            if (!rung.Qualifies(world, promo)) continue;
            // Up to TWO networks of the same rung can be courting at once —
            // with real personalities on the table (the strict flagship vs the
            // easy-going budget channel), the choice is the point.
            if (promo.Offers.Count(o => o.Media == rung.Kind) >= 2) continue;
            // Into an open lane — or over the top of a LESSER deal in the same
            // lane (the bigger network poaches: cable calls the org stuck on
            // specialty). An org running its OWN platform isn't poachable.
            var current = DealIn(promo, MediaLanes.LaneOf(rung.Kind));
            bool upgrade = current is not null && current.Kind != rung.Kind
                && rung.Fee(promo) > current.RightsFeePerEvent.Amount;
            if (!LaneOpen(promo, rung.Kind) && !upgrade) continue;
            if (!rng.Chance(0.5)) continue;   // the phone doesn't ring the instant you qualify
            if (Suitor(world, promo, rung, rng) is not { } profile) continue;   // nobody free who hasn't already pitched

            MakeOffer(world, promo, rung, profile, date);
        }

        if (promo.Id != world.PlayerPromotion) AiAccept(world, promo, date);
    }

    private static DealOffer MakeOffer(World world, Promotion promo, Rung rung, PartnerProfile profile, DateOnly date)
    {
        promo.CourtedBy.Add(profile.Name);
        var offer = new DealOffer
        {
            Media = rung.Kind,
            Partner = profile.Name,
            FeePerEvent = new Money(rung.Fee(promo) * profile.FeeScale),
            Term = rung.TermMonths,
            Requirements = AdjustTerms(rung.Terms(promo), profile.Strictness),
            OfferedOn = date,
            ExpiresOn = date.AddDays(90),
        };
        promo.Offers.Add(offer);
        if (promo.Id == world.PlayerPromotion)
            Living.Mail.Post(world, date, MailCategory.Milestone, "The Network",
                $"Media offer: {offer.Partner} ({Label(rung.Kind)} · {profile.Tag})",
                (rung.Kind == MediaKind.PpvCarrier
                    ? $"{offer.Partner} will carry our pay-per-views — we keep {profile.FeeScale:P0} of every buy. "
                    : $"{offer.Partner} offers {offer.FeePerEvent}/event for {offer.Term} months. ")
                + Terms(offer.Requirements) + " Answer at the Media desk — they only ask once.");
        return offer;
    }

    /// <summary>
    /// A partner freshly back on the market SHOPS AROUND: they pitch the best
    /// promotion that could use them (highest prestige, rung qualified, lane
    /// open or a genuine upgrade) rather than waiting for the monthly lottery —
    /// which, iterated in promotion order, always handed a freed channel to the
    /// same early-created orgs and starved late risers of television for
    /// decades. The org that just lost them is skipped; the monthly loop can
    /// still re-court them later.
    /// </summary>
    private static void ShopAround(World world, MediaKind kind, string partner, DateOnly date, Promotion? except)
    {
        if (!IsExclusive(kind)) return;
        var rung = RungOf(kind);
        var suitor = world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != except?.Id
                && rung.Qualifies(world, p)
                && !p.Offers.Any(o => o.Partner == partner)
                && (LaneOpen(p, kind)
                    || (DealIn(p, MediaLanes.LaneOf(kind)) is { } cur && cur.Kind != kind
                        && rung.Fee(p) > cur.RightsFeePerEvent.Amount)))
            .OrderByDescending(p => p.Prestige.Value)
            .FirstOrDefault();
        if (suitor is not null) MakeOffer(world, suitor, rung, ProfileOf(partner), date);
    }

    /// <summary>Sign a media offer into its lane (replacing a lesser deal in the same lane).</summary>
    public static MediaDeal Accept(World world, Promotion promo, DealOffer offer, DateOnly date)
    {
        promo.Offers.Remove(offer);
        if (offer.Media is not { } kind) throw new InvalidOperationException("not a media offer");
        var existing = DealIn(promo, MediaLanes.LaneOf(kind));
        if (existing is not null)
        {
            promo.MediaDeals.Remove(existing);
            if (IsExclusive(existing.Kind))
            {
                ReleasePartner(world, existing.Partner);      // jilted — back on the market...
                promo.CourtedBy.Add(existing.Partner);        // ...but not knocking on THIS door again
                ShopAround(world, existing.Kind, existing.Partner, date, promo);
            }
        }

        var deal = new MediaDeal
        {
            Kind = kind,
            Partner = offer.Partner,
            RightsFeePerEvent = offer.FeePerEvent,
            MonthsRemaining = offer.Term,
            Requirements = offer.Requirements.ToList(),
            SignedOn = date,
        };
        promo.MediaDeals.Add(deal);

        // A signed partner is off the market: their pitches elsewhere are withdrawn.
        if (IsExclusive(kind))
            foreach (var other in world.Promotions.Values.Where(p => p.Id != promo.Id))
                other.Offers.RemoveAll(o => o.Media is not null && o.Partner == deal.Partner);
        Living.News.Post(world, date, NewsCategory.Finance,
            $"{promo.Abbreviation} signs with {deal.Partner}",
            $"A {Label(kind)} deal puts the promotion's cards on a bigger screen.", promo.Id);
        if (promo.Id == world.PlayerPromotion && kind != MediaKind.InHouseStream)
            Org.Milestones.Award(world, promo, date, $"media-{kind}", $"On the air: {Label(kind)}",
                $"{deal.Partner} carries our fights now. Bigger screens make bigger stars.");
        return deal;
    }

    private static void AiAccept(World world, Promotion promo, DateOnly date)
    {
        foreach (var offer in promo.Offers.Where(o => o.Media is not null)
                     .OrderByDescending(o => o.FeePerEvent.Amount).ToList())
            if (offer.Media is { } k && (LaneOpen(promo, k)
                || DealIn(promo, MediaLanes.LaneOf(k))?.RightsFeePerEvent.Amount < offer.FeePerEvent.Amount))
                Accept(world, promo, offer, date);

        // A National+ org with cash and no streaming home builds its own.
        if (promo.Stream is null && DealIn(promo, MediaLane.Streaming) is null
            && Org.OrgSizing.SizeOf(promo) >= OrgSize.National
            && promo.Balance.Amount > 1_000_000m)
            LaunchStream(world, promo, date);
    }

    private static void Drop(World world, Promotion promo, MediaDeal deal, DateOnly date, string why)
    {
        promo.MediaDeals.Remove(deal);
        if (IsExclusive(deal.Kind))
        {
            // Back on the market and straight onto the phones — but a partner
            // who just walked out doesn't turn around and pitch the same org
            // (the old CourtedBy entry stands until they sign SOMEWHERE ELSE
            // and that deal ends — eventual forgiveness, not instant amnesia).
            ReleasePartner(world, deal.Partner);
            promo.CourtedBy.Add(deal.Partner);
            ShopAround(world, deal.Kind, deal.Partner, date, promo);
        }
        Living.News.Post(world, date, NewsCategory.Finance,
            $"{deal.Partner} drops {promo.Abbreviation}",
            $"The {Label(deal.Kind)} deal is over {why}.", promo.Id);
        if (promo.Id == world.PlayerPromotion)
            Living.Mail.Post(world, date, MailCategory.BoardNote, "The Network",
                $"{deal.Partner} walks",
                $"We've lost our {Label(deal.Kind)} deal {why}. The money and the reach go with it.");
    }

    // --- Fight night ----------------------------------------------------------

    /// <summary>
    /// Air the card: every rights partner pays their per-event fee (checked
    /// against the contract's terms — half pay and a strike on a miss), the
    /// cadence clocks reset, the in-house platform converts the night into
    /// subscribers. PPV nights are additionally audited by the carrier.
    /// </summary>
    public static void SettleEvent(World world, Promotion promo, Event ev)
    {
        decimal premium = Org.OrgPerks.PartnerPremium(promo) * Org.Franchise.PartnerFactor(world, promo);
        // A crowded band thins the rights money too — finite TV hours, split
        // more ways (see Org.MarketShare; the economy shapes the pyramid now).
        premium *= (decimal)Org.MarketShare.Factor(world, promo);

        foreach (var deal in promo.MediaDeals.ToList())
        {
            deal.LastAiredOn = ev.Date;
            bool ppvNight = ev.Tier >= EventTier.Ppv;
            if (deal.Kind == MediaKind.PpvCarrier && !ppvNight) continue;   // the carrier only judges PPV nights

            var audit = DealCompliance.Audit(world, ev, deal.Requirements);
            var missed = audit.Where(c => !c.Met).ToList();
            bool honoured = missed.Count == 0;

            if (deal.RightsFeePerEvent.Amount > 0)
                Ledger.Income(promo, ev.Date, FinanceCategory.Broadcast,
                    new Money(deal.RightsFeePerEvent.Amount * premium * (honoured ? 1m : 0.5m)),
                    $"{ev.Name} — {deal.Partner} rights{(honoured ? "" : " (terms missed)")}");

            if (honoured)
            {
                // A clean card earns a strike back. Strikes used to be forever,
                // which made a strict partner a cliff on a timer: the flagship's
                // ordinary cards miss a 50 quality floor about half the time and a
                // 45 floor one time in five, so every prime-time deal it signed was
                // gone within months however good the rest of its nights were (the
                // AFC lost seven in three years and folded, seed 8, 2029). Now the
                // partner judges the RUN of cards: an org that delivers more often
                // than it misses keeps the deal; one that keeps missing still walks.
                if (deal.Strikes > 0)
                {
                    deal.Strikes--;
                    if (promo.Id == world.PlayerPromotion)
                        Living.Mail.Post(world, ev.Date, MailCategory.General, deal.Partner,
                            $"{deal.Partner} liked {ev.Name}",
                            $"The card met every term, and they've taken a strike off the slate ({deal.Strikes} of {StrikeLimit} now).");
                }
                continue;
            }
            deal.Strikes++;
            if (promo.Id == world.PlayerPromotion)
                Living.Mail.Post(world, ev.Date, MailCategory.BoardNote, deal.Partner,
                    $"{deal.Partner}: {ev.Name} broke our terms",
                    $"{string.Join("; ", missed.Select(m => m.Text))}. " +
                    (deal.Strikes >= StrikeLimit ? "They've torn the deal up."
                        : $"Strike {deal.Strikes} of {StrikeLimit} — they paid reduced rights."));
            if (deal.Strikes >= StrikeLimit)
                Drop(world, promo, deal, ev.Date, "after one broken card too many");
        }

        StreamAirs(world, promo, ev);
    }

    /// <summary>
    /// The main-card size the org's media contracts call for (the strictest
    /// window across its deals), or null when nobody regulates the card. Used
    /// to auto-fit a card that was booked without an explicit split — the AI's
    /// matchmaker and a player who never touched the divider both stay
    /// compliant by default.
    /// </summary>
    public static int? BroadcastWindow(Promotion p)
    {
        int? max = null, min = null;
        foreach (var req in p.MediaDeals.SelectMany(d => d.Requirements))
        {
            if (req.Kind == DealRequirementKind.MaxMainCard)
                max = Math.Min(max ?? int.MaxValue, req.Threshold);
            if (req.Kind == DealRequirementKind.MinMainCard)
                min = Math.Max(min ?? 0, req.Threshold);
        }
        return max ?? min;
    }

    /// <summary>The platform's monthly books at its current audience (what the Media desk quotes).</summary>
    public static (Money Income, Money Ops) StreamMonthly(StreamingPlatform s) =>
        (new Money(s.Subscribers * NetPerSubscriber), new Money(StreamOpsBase + s.Subscribers * StreamOpsPerSub));

    /// <summary>One rung of the ladder as the Media desk shows it.</summary>
    public readonly record struct RungView(MediaKind Kind, string Label, bool Qualified, string Gate, Money Fee, string Terms);

    /// <summary>The whole ladder, judged for this promotion — the Media desk's map of what's out there.</summary>
    public static IReadOnlyList<RungView> LadderView(World world, Promotion promo) =>
        Ladder.Select(r => new RungView(
            r.Kind, Label(r.Kind), r.Qualifies(world, promo), Gate(r.Kind),
            new Money(r.Fee(promo)),
            string.Join("; ", r.Terms(promo).Select(x => DealCompliance.Describe(x)))
            + $" · networks: {string.Join(", ", r.Partners.Select(pp => $"{pp.Name} ({pp.Tag})"))} — terms and money vary by house")).ToList();

    private static string Gate(MediaKind kind) => kind switch
    {
        MediaKind.SpecialtyCable => "Regional reach · prestige 24+",
        MediaKind.CableSports => "National reach",
        MediaKind.PrimeBroadcast => "Continental reach (or National with a 70-pop star)",
        MediaKind.MajorStream => "Continental reach · prestige 50+",
        MediaKind.PpvCarrier => "Continental reach (or an 80-pop superstar)",
        _ => "",
    };

    // --- Helpers --------------------------------------------------------------

    public static int EventsInWindow(World world, Promotion p, int days) =>
        world.Events.Values.Count(e => e.Promotion == p.Id && e.Status == EventStatus.Completed
            && world.CurrentDate.DayNumber - e.Date.DayNumber <= days);

    public static string Label(MediaKind kind) => kind switch
    {
        MediaKind.InHouseStream => "in-house streaming",
        MediaKind.SpecialtyCable => "specialty cable",
        MediaKind.CableSports => "cable sports",
        MediaKind.PrimeBroadcast => "prime-time broadcast",
        MediaKind.MajorStream => "major streaming",
        MediaKind.PpvCarrier => "pay-per-view carrier",
        _ => kind.ToString(),
    };

    private static string Terms(IReadOnlyList<DealRequirement> reqs) =>
        reqs.Count == 0 ? "No strings attached."
            : "Their terms: " + string.Join("; ", reqs.Select(DealCompliance.Describe)) + ".";
}
