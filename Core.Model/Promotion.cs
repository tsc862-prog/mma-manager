namespace MmaManager.Core.Model;

/// <summary>
/// A fight company — the player's own plus AI-run competitors. Its roster is
/// derived (fighters whose contract points here), so there's no separate list
/// to keep in sync; ask the World for <c>RosterOf(id)</c>.
/// </summary>
public sealed class Promotion
{
    public required PromotionId Id { get; init; }
    public required string Name { get; set; }
    public required string Abbreviation { get; set; }
    public Region BaseRegion { get; set; }
    public bool IsPlayerControlled { get; set; }

    /// <summary>
    /// Authored identity prose — who this house is, its founding story and its
    /// grudges (the designed initial database). Null for procedural startups —
    /// the Biographer derives a description instead. Persisted.
    /// </summary>
    public string? Lore { get; set; }

    /// <summary>
    /// The person who signs the cheques and whose patience the player is spending.
    /// Board notes and objectives come from them BY NAME — "The Owner" was an
    /// abstraction sending letters, which is a strange thing to work for.
    /// Null on a promotion the player founded: they're the owner.
    /// </summary>
    public string? OwnerName { get; set; }

    /// <summary>One line on what the owner is like to work for — what they reward and what they won't forgive.</summary>
    public string? OwnerNote { get; set; }

    /// <summary>The year this house opened its doors. 0 = unknown (a procedural startup stamps its own).</summary>
    public int FoundedYear { get; set; }

    /// <summary>Closed its doors (insolvency). Kept in the world so history resolves, but out of every running system.</summary>
    public bool Defunct { get; set; }

    public Money Balance { get; set; }

    /// <summary>
    /// Discretionary MONTHLY marketing spend on the brand itself — pour money in to
    /// fill more seats and build the promotion's standing faster (helping it climb to
    /// the next tier). Saturating: the bigger you already are, the more it costs to
    /// move the needle. 0 = no campaign. See <see cref="Org.OrgMarketing"/>.
    /// </summary>
    public Money MarketingBudget { get; set; }

    /// <summary>
    /// The fight camp this promotion sponsors (a team deal): stipends there come at
    /// half price, its members warm to the org in negotiations, and your coaches see
    /// its mats every day (fog). Null = no deal. Billed monthly while set.
    /// </summary>
    public CampId? SponsoredCamp { get; set; }

    /// <summary>
    /// The FACE OF THE COMPANY — the fighter the brand is built around. Their fame
    /// feeds the promotion's standing and what partners pay, they demand face money
    /// in return, and losing them (poached, retired) is a company crisis. Null = no
    /// face. See <see cref="Org.Franchise"/>.
    /// </summary>
    public FighterId? FaceOfCompany { get; set; }

    /// <summary>
    /// Dated, categorised money movements since the promotion's starting cash.
    /// The running <see cref="Balance"/> equals that starting cash plus the sum of
    /// every entry here; the finance screen builds its P&amp;L from this list.
    /// </summary>
    public List<LedgerEntry> Ledger { get; init; } = new();

    /// <summary>Active sponsorships — brands that pay per event staged (see Finance.Sponsorships).</summary>
    public List<SponsorDeal> SponsorDeals { get; init; } = new();

    /// <summary>Active media rights deals, at most one per lane (see Finance.MediaService).</summary>
    public List<MediaDeal> MediaDeals { get; init; } = new();

    /// <summary>Sponsor and media offers on the table, waiting for an answer.</summary>
    public List<DealOffer> Offers { get; init; } = new();

    /// <summary>
    /// Media partners who have already made this promotion their pitch. A network
    /// approaches ONCE — pass on them and they move on — and only comes back when
    /// they re-enter the market (their deal with whoever they signed ends).
    /// Cleared per partner world-wide when that partner is released.
    /// </summary>
    public HashSet<string> CourtedBy { get; init; } = new();

    /// <summary>The promotion's own streaming service, once launched. Null = never launched.</summary>
    public StreamingPlatform? Stream { get; set; }

    /// <summary>
    /// The most recent tier this promotion stepped UP to, and when — the market's
    /// memory. The promotion ladder prices an org's entry by how many others
    /// ENTERED that tier recently (a flood), not by how many exist (the sport
    /// maturing), so these stamps are what the door reads. Null = never stepped
    /// up (day-one orgs start settled). Persisted.
    /// </summary>
    public OrgSize? LastClearedTier { get; set; }
    public DateOnly? LastClearedOn { get; set; }

    /// <summary>Hired back-office staff (player promotion). Salaries bill through overhead.</summary>
    public List<StaffMember> Staff { get; init; } = new();

    /// <summary>Owner-set goals for the player promotion.</summary>
    public List<Objective> Objectives { get; init; } = new();

    private int _prestigeHundredths;

    /// <summary>
    /// Fine-grained prestige accumulator in HUNDREDTHS (0–10000). The climb
    /// engine moves this in sub-point steps so small good/bad shows still
    /// register over time; <see cref="Prestige"/> is its whole-number view.
    /// </summary>
    public int PrestigeHundredths
    {
        get => _prestigeHundredths;
        set => _prestigeHundredths = Math.Clamp(value, 0, 10_000);
    }

    /// <summary>
    /// Overall brand strength (0–100) — the number every system reads (talent
    /// appeal, gate/PPV draw, reach tier). Backed by <see cref="PrestigeHundredths"/>,
    /// floored to whole points; setting it snaps the accumulator to that value.
    /// </summary>
    public Rating Prestige
    {
        get => _prestigeHundredths / 100;
        set => _prestigeHundredths = value.Value * 100;
    }

    /// <summary>
    /// The highest tier the promotion has CHOSEN to operate at. Growth is opt-in:
    /// prestige is held at the ceiling of this tier until the org accepts promotion
    /// to the next one, so a promotion can deliberately stay small and cheap —
    /// building a roster, stars and a war chest — instead of being forced up into a
    /// tier its gate can't yet pay for. Set to the starting tier when the world is
    /// built; the AI raises it when it can afford to, the player when they choose.
    /// </summary>
    public OrgSize TierClearance { get; set; } = OrgSize.International;

    /// <summary>True once the org has maxed its tier and been offered promotion (so the
    /// player isn't pestered every month); cleared when they promote or drop back.</summary>
    public bool PromotionOffered { get; set; }

    /// <summary>
    /// When the org first maxed its tier and joined the promotion queue (null =
    /// not currently eligible). Queue SENIORITY is the pressure valve on
    /// crowd-priced promotion: the longer an org waits at the ceiling, the more
    /// the crowd markup decays — so a maxed org is never priced out forever,
    /// and the queue drains in seniority order. Persisted.
    /// </summary>
    public DateOnly? EligibleSince { get; set; }

    /// <summary>
    /// How the promotion is regarded by fighters and agents. Honouring deals and
    /// running well raises it; cutting fighters mid-contract dents it. Starts neutral.
    /// </summary>
    public Rating Reputation { get; set; } = 50;

    /// <summary>Headquarters city — the promotion's home turf (the anchor metro of its base region).</summary>
    public CityId? HomeCity { get; set; }

    /// <summary>
    /// Popularity per CITY: a promotion can own its home town, be known around
    /// the region, and be nobody abroad. Region-level popularity is derived
    /// (market-weighted average), never stored — see the sim's Reputation helper.
    /// </summary>
    public Dictionary<CityId, Rating> PopularityByCity { get; init; } = new();

    /// <summary>The weight classes this promotion actually runs.</summary>
    public List<Division> Divisions { get; init; } = new();

    /// <summary>Belts this promotion sanctions (resolved via the World).</summary>
    public List<TitleId> Titles { get; init; } = new();

    /// <summary>The house ruleset — every promotion runs fights its own way.</summary>
    public OrgRules Rules { get; init; } = new();

    /// <summary>
    /// Career milestones already achieved (keys, e.g. "first-sellout"), so each
    /// fires once. Player-facing progression markers; see the sim's Milestones.
    /// </summary>
    public HashSet<string> Milestones { get; init; } = new();
}

/// <summary>
/// A promotion's configurable ruleset: how long its rounds run, how many a
/// bout goes, what a title fight gets, and whether it pays a Fight of the
/// Night bonus. The owner sets these; the engine, booking defaults, and the
/// event settlement all read them.
/// </summary>
public sealed class OrgRules
{
    /// <summary>Minutes per round (3–5; 5 is the sport's standard).</summary>
    public int RoundMinutes { get; set; } = 5;

    /// <summary>Rounds for an ordinary bout (2–5; 3 is standard).</summary>
    public int StandardRounds { get; set; } = 3;

    /// <summary>Rounds when a belt is on the line (3–5; 5 is standard).</summary>
    public int TitleRounds { get; set; } = 5;

    /// <summary>
    /// Discretionary Fight of the Night bonus, paid to BOTH fighters of each
    /// card's best-rated fight. Zero = the org doesn't pay one. Costs real
    /// money; buys morale and a reputation as a place fighters want to work.
    /// </summary>
    public Money FightOfNightBonus { get; set; }
}

/// <summary>
/// A promotion's instance of a weight class: its ranked contenders and the
/// belt contested in it. The champion is tracked on the Title, not here, so
/// there's a single source of truth for "who holds the belt".
/// </summary>
public sealed class Division
{
    public required WeightClass WeightClass { get; init; }

    /// <summary>Men's or women's division — a promotion can run both at the same weight.</summary>
    public Sex Sex { get; init; } = Sex.Male;

    /// <summary>Contenders in ranking order (index 0 = #1 contender).</summary>
    public List<FighterId> Rankings { get; init; } = new();

    public TitleId? Title { get; set; }                  // null => no belt yet

    /// <summary>This fighter belongs in this division (same weight AND same sex).</summary>
    public bool Matches(Fighter f) => f.WeightClass == WeightClass && f.Sex == Sex;
}

/// <summary>The weight classes contested in women's MMA (the lighter divisions).</summary>
public static class WomensDivisions
{
    public static readonly WeightClass[] Classes =
        { WeightClass.Strawweight, WeightClass.Flyweight, WeightClass.Bantamweight, WeightClass.Featherweight };

    public static bool Allows(WeightClass wc) => Array.IndexOf(Classes, wc) >= 0;
}

public sealed class Title
{
    public required TitleId Id { get; init; }
    public required PromotionId Promotion { get; init; }
    public required WeightClass WeightClass { get; init; }
    public required string Name { get; set; }

    public FighterId? CurrentChampion { get; set; }      // null => vacant

    /// <summary>
    /// The belt's own name in the sport (0–100), built by the QUALITY of the
    /// fighters who contest it: every title fight pulls it toward the level of
    /// the two in the cage, so a lineage of killers makes a storied belt while
    /// a hot-potato club strap stays a trinket. A storied belt sells its fights
    /// harder and mints more fame for whoever wins it. Seeded from the org's
    /// prestige when the belt is created; persisted.
    /// </summary>
    public Rating Prestige { get; set; }

    public List<TitleReign> Lineage { get; init; } = new();
}

public sealed class TitleReign
{
    public required FighterId Champion { get; init; }
    public required DateOnly WonOn { get; init; }
    public DateOnly? LostOn { get; set; }                // null => current reign
    public int Defenses { get; set; }
}
