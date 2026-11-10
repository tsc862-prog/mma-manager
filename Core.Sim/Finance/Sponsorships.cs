using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Finance;

/// <summary>
/// Sponsorship as a product the promotion SELLS on fight night: brands pay per
/// event staged — no show, no check — and write requirements into the deal
/// (local names, action, orthodox rules, stars, a full house). Deliver and the
/// check clears in full; miss and it's half pay and a strike, two strikes and
/// the brand walks. Offers arrive by archetype: local businesses back a City
/// outfit, national and global brands only return calls once the promotion's
/// reach earns them. Deterministic through SimRandom.
/// </summary>
public static class Sponsorships
{
    /// <summary>How many sponsor logos fit on the canvas at each reach tier.</summary>
    public static int Slots(OrgSize size) => size switch
    {
        OrgSize.International => 6,
        OrgSize.Continental => 5,
        OrgSize.National => 4,
        OrgSize.Regional => 3,
        _ => 2,
    };

    /// <summary>How many strikes a sponsor tolerates before walking.</summary>
    public const int StrikeLimit = 2;

    // --- The market: who sponsors a fight promotion, and what they want -------

    private sealed record Archetype(
        string Label,
        string[] Partners,
        OrgSize MinTier,
        decimal BaseFee,
        decimal PerPrestige,
        Func<Promotion, DealRequirement[]> Terms);

    private static readonly Archetype[] Archetypes =
    {
        new("local business",
            new[] { "Main Street Motors", "Cornerstone Hardware", "Falcon Lager", "Golden Gate Pawn", "Riverside Tire & Auto" },
            OrgSize.City, 10_000m, 800m,
            _ => new[] { new DealRequirement(DealRequirementKind.MinLocalFighters, 2) }),

        new("supplement brand",
            new[] { "Grit Supplements", "Ironclad Nutrition", "Apex Fuel", "Warpath Whey" },
            OrgSize.City, 8_000m, 700m,
            _ => new[] { new DealRequirement(DealRequirementKind.MinCardQuality, 42) }),

        new("equipment house",
            new[] { "Titan Gloves", "Kensei Fightwear", "Vale Tudo Supply Co." },
            OrgSize.Regional, 10_000m, 800m,
            _ => new[] { new DealRequirement(DealRequirementKind.StandardRules, 0) }),

        new("energy drink",
            new[] { "Voltage Energy", "Overdrive", "Static Surge" },
            OrgSize.Regional, 12_000m, 900m,
            p => new[] { new DealRequirement(DealRequirementKind.MinStarPower,
                Org.OrgSizing.DrawFloor(Org.OrgSizing.SizeOf(p))) }),

        new("brewery",
            new[] { "Redline Brewing Co.", "Iron City Ales", "Halftime Lager" },
            OrgSize.Regional, 11_000m, 850m,
            p => new[] { new DealRequirement(DealRequirementKind.MinAttendance,
                Org.OrgSizing.SizeOf(p) >= OrgSize.National ? 4_000 : 1_500) }),

        new("national brand",
            new[] { "Summit Bank", "Nova Telecom", "Redline Autos", "Frontier Insurance" },
            OrgSize.National, 40_000m, 1_500m,
            _ => new[] { new DealRequirement(DealRequirementKind.TitleFight, 0) }),

        new("global brand",
            new[] { "Meridian Airlines", "Vanta Sportswear", "Atlas Watches", "Polar Cola" },
            OrgSize.Continental, 120_000m, 2_500m,
            _ => new[]
            {
                new DealRequirement(DealRequirementKind.TitleFight, 0),
                new DealRequirement(DealRequirementKind.MinStarPower, 65),
            }),
    };

    private static Money FeeFor(Archetype a, Promotion p) =>
        new(a.BaseFee + p.Prestige.Value * a.PerPrestige);

    // --- Seeding (world build) ------------------------------------------------

    /// <summary>
    /// An established promotion opens with its sponsor slots mostly sold — the
    /// brand has been on sale for years. A brand-new founding instead gets ONE
    /// easy local backer plus offers to grow into.
    /// </summary>
    public static void SeedEstablished(World world, Promotion promo, DateOnly on, SimRandom rng)
    {
        var size = Org.OrgSizing.SizeOf(promo);
        int fill = Math.Max(1, Slots(size) - 1);
        var eligible = Archetypes.Where(a => size >= a.MinTier).ToList();
        for (int i = 0; i < fill && eligible.Count > 0; i++)
        {
            var pick = eligible[rng.Range(0, eligible.Count - 1)];
            eligible.Remove(pick);   // one deal per archetype — the canvas stays varied
            promo.SponsorDeals.Add(new SponsorDeal
            {
                Partner = pick.Partners[rng.Range(0, pick.Partners.Length - 1)],
                FeePerEvent = FeeFor(pick, promo),
                EventsRemaining = rng.Range(4, 10),
                Requirements = pick.Terms(promo).ToList(),
                SignedOn = on,
            });
        }
    }

    /// <summary>A founded promotion's day one: one friendly local sponsor signed, a second offer on the table.</summary>
    public static void SeedFounding(World world, Promotion promo, DateOnly on, SimRandom rng)
    {
        var local = Archetypes[0];
        promo.SponsorDeals.Add(new SponsorDeal
        {
            Partner = local.Partners[rng.Range(0, local.Partners.Length - 1)],
            FeePerEvent = FeeFor(local, promo),
            EventsRemaining = 6,
            Requirements = new(),   // the hometown backer asks nothing — they're just glad you exist
            SignedOn = on,
        });
        AddOffer(promo, Archetypes[1], on, rng);
    }

    // --- The monthly market ---------------------------------------------------

    /// <summary>
    /// Keep the offer table alive: stale offers lapse, and (a few times a year)
    /// a brand the promotion qualifies for comes knocking if there's an open
    /// slot. AI promotions answer their own mail — they accept the best offer
    /// on the spot.
    /// </summary>
    public static void RunMonthly(World world, Promotion promo, DateOnly date, SimRandom rng)
    {
        promo.Offers.RemoveAll(o => o.Media is null && o.ExpiresOn < date);

        int open = Slots(Org.OrgSizing.SizeOf(promo)) - promo.SponsorDeals.Count;
        int pending = promo.Offers.Count(o => o.Media is null);
        if (open > 0 && pending < 2 && rng.Chance(0.35))
        {
            var size = Org.OrgSizing.SizeOf(promo);
            var taken = promo.SponsorDeals.Select(d => d.Partner).ToHashSet();
            var eligible = Archetypes.Where(a => size >= a.MinTier
                && a.Partners.Any(n => !taken.Contains(n))).ToList();
            if (eligible.Count > 0)
            {
                var offer = AddOffer(promo, eligible[rng.Range(0, eligible.Count - 1)], date, rng);
                if (promo.Id == world.PlayerPromotion && offer is not null)
                    Living.Mail.Post(world, date, MailCategory.General, "Commercial",
                        $"Sponsorship offer: {offer.Partner}",
                        $"{offer.Partner} wants on the canvas: {offer.FeePerEvent}/event for {offer.Term} events. " +
                        Terms(offer.Requirements) + " Answer on the Finance page before it lapses.");
            }
        }

        if (promo.Id != world.PlayerPromotion)
            AiAccept(world, promo, date);
    }

    private static DealOffer? AddOffer(Promotion promo, Archetype a, DateOnly date, SimRandom rng)
    {
        var name = a.Partners[rng.Range(0, a.Partners.Length - 1)];
        if (promo.SponsorDeals.Any(d => d.Partner == name) || promo.Offers.Any(o => o.Partner == name))
            return null;
        var offer = new DealOffer
        {
            Partner = name,
            FeePerEvent = FeeFor(a, promo),
            Term = rng.Range(4, 8),
            Requirements = a.Terms(promo).ToList(),
            OfferedOn = date,
            ExpiresOn = date.AddDays(60),
        };
        promo.Offers.Add(offer);
        return offer;
    }

    /// <summary>Sign an offer off the table into an active deal.</summary>
    public static SponsorDeal Accept(World world, Promotion promo, DealOffer offer, DateOnly date)
    {
        promo.Offers.Remove(offer);
        var deal = new SponsorDeal
        {
            Partner = offer.Partner,
            FeePerEvent = offer.FeePerEvent,
            EventsRemaining = offer.Term,
            Requirements = offer.Requirements.ToList(),
            SignedOn = date,
        };
        promo.SponsorDeals.Add(deal);
        return deal;
    }

    private static void AiAccept(World world, Promotion promo, DateOnly date)
    {
        while (promo.SponsorDeals.Count < Slots(Org.OrgSizing.SizeOf(promo)))
        {
            var best = promo.Offers.Where(o => o.Media is null)
                .OrderByDescending(o => o.FeePerEvent.Amount).FirstOrDefault();
            if (best is null) return;
            Accept(world, promo, best, date);
        }
    }

    // --- Fight night ----------------------------------------------------------

    /// <summary>
    /// Pay out every sponsor against the settled card: full fee when the terms
    /// were honoured (scaled up on PPV/SuperCard nights, and by the partner
    /// premium a rising org commands), half fee and a strike when they weren't.
    /// Terms count down per event; a clean expiring deal renews at today's
    /// value, a struck-out sponsor walks.
    /// </summary>
    public static void SettleEvent(World world, Promotion promo, Event ev)
    {
        decimal tierMult = ev.Tier switch { EventTier.Ppv => 1.5m, EventTier.SuperCard => 2.0m, _ => 1.0m };
        decimal premium = Org.OrgPerks.PartnerPremium(promo) * Org.Franchise.PartnerFactor(world, promo);

        foreach (var deal in promo.SponsorDeals.ToList())
        {
            var audit = DealCompliance.Audit(world, ev, deal.Requirements);
            var missed = audit.Where(c => !c.Met).ToList();
            bool honoured = missed.Count == 0;

            var fee = new Money(deal.FeePerEvent.Amount * tierMult * premium * (honoured ? 1m : 0.5m));
            Ledger.Income(promo, ev.Date, FinanceCategory.Sponsorship, fee,
                $"{ev.Name} — {deal.Partner}{(honoured ? "" : " (terms missed)")}");

            if (!honoured)
            {
                deal.Strikes++;
                if (promo.Id == world.PlayerPromotion)
                    Living.Mail.Post(world, ev.Date, MailCategory.BoardNote, "Commercial",
                        $"{deal.Partner} is unhappy with {ev.Name}",
                        $"We missed their terms — {string.Join("; ", missed.Select(m => m.Text))} — so they paid half. " +
                        (deal.Strikes >= StrikeLimit ? "That was the last straw: they're walking." :
                         "One more miss and they walk."));
                if (deal.Strikes >= StrikeLimit)
                {
                    promo.SponsorDeals.Remove(deal);
                    Living.News.Post(world, ev.Date, NewsCategory.Finance,
                        $"{deal.Partner} drops {promo.Abbreviation}",
                        "The sponsor pulled out after the promotion repeatedly missed its contract terms.", promo.Id);
                    continue;
                }
            }

            if (--deal.EventsRemaining <= 0)
            {
                if (deal.Strikes == 0)
                {
                    // A happy sponsor re-ups at what the promotion is worth NOW.
                    var arch = Archetypes.FirstOrDefault(a => a.Partners.Contains(deal.Partner));
                    if (arch is not null) deal.FeePerEvent = FeeFor(arch, promo);
                    deal.EventsRemaining = 6;
                    deal.SignedOn = ev.Date;
                    if (promo.Id == world.PlayerPromotion)
                        Living.Mail.Post(world, ev.Date, MailCategory.General, "Commercial",
                            $"{deal.Partner} renews",
                            $"A clean run — they re-signed for 6 more events at {deal.FeePerEvent}/event.");
                }
                else
                {
                    promo.SponsorDeals.Remove(deal);
                    if (promo.Id == world.PlayerPromotion)
                        Living.Mail.Post(world, ev.Date, MailCategory.General, "Commercial",
                            $"{deal.Partner} moves on",
                            "The deal ran its course and the missed terms along the way cost us the renewal.");
                }
            }
        }
    }

    private static string Terms(IReadOnlyList<DealRequirement> reqs) =>
        reqs.Count == 0 ? "No strings attached."
            : "Their terms: " + string.Join("; ", reqs.Select(DealCompliance.Describe)) + ".";
}
