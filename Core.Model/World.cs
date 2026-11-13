namespace MmaManager.Core.Model;

/// <summary>
/// The entire game world and the root of a save file. Everything the sim
/// touches lives here in memory; persistence just serialises this graph.
/// Because entities reference each other by id, this is the single place ids
/// are resolved back to objects.
/// </summary>
public sealed class World
{
    /// <summary>
    /// The save shape this build reads AND writes. Bumped when the model changes
    /// shape. Development rule: saves are never migrated — the reader refuses any
    /// other version and the player starts a new world (see PROJECT.md).
    /// </summary>
    public const int LatestSaveVersion = 47;   // v47: free-agent rival offers (RivalOffer.Improvements + the owner's final offer, RivalOffer.Final; Purse/WinBonus/SigningBonus settable); v46: rival offers (World.RivalOffers, DecisionKind.RivalOffer); v45: deals end (Contract.WindowEndsOn/WindowWarned) + World.Talks (talks remembered); v44: Contract.BondNoticeOn (lifer stories told once); v43: safe save (entity ordinals keep dictionary order, round scores + poach warning + cross-org rankings persisted, loyalty/marketing_push NOT NULL); v42: temperament & voice (disposition + ego/professionalism/temper/ambition, named staff, org owners); v41: fog-of-war pricing (discovered_by); v40: big-money era

    /// <summary>The version a loaded save was written at (always <see cref="LatestSaveVersion"/> — older saves are refused on load).</summary>
    public int SaveVersion { get; init; } = LatestSaveVersion;

    public DateOnly CurrentDate { get; set; }

    /// <summary>
    /// The day the save began. Everything in <see cref="Events"/> dated before
    /// this is generated pre-history — real cards with real results, but nothing
    /// the player was around to watch (scouting counts it as record, not tape).
    /// </summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// Master seed. The fight engine and world tick derive their RNG from this,
    /// so a save + seed reproduces every result exactly — which is what makes
    /// balance testing and bug reproduction possible.
    /// </summary>
    public ulong RngSeed { get; set; }

    public PromotionId PlayerPromotion { get; set; }

    /// <summary>
    /// Portrait-first generation catalog (a Core.Sim <c>IdentityCatalog</c>),
    /// held opaquely so the model stays free of the sim layer. Set by the world
    /// builder and re-attached after a load; not persisted. Null => procedural
    /// generation (headless runs, tests).
    /// </summary>
    public object? ArtCatalog { get; set; }

    // The entity collections enumerate in ascending id order, always (EntityMap): the sim iterates
    // them while it rolls, so their order must not depend on removals or on a save/load.
    public EntityMap<FighterId, Fighter> Fighters { get; } = new();
    public EntityMap<PromotionId, Promotion> Promotions { get; } = new();
    public EntityMap<TitleId, Title> Titles { get; } = new();
    public EntityMap<EventId, Event> Events { get; } = new();
    public EntityMap<CountryId, Country> Countries { get; } = new();
    public EntityMap<CityId, City> Cities { get; } = new();
    public EntityMap<VenueId, Venue> Venues { get; } = new();
    public EntityMap<CampId, Camp> Camps { get; } = new();

    /// <summary>World news feed, oldest first; the sim appends notable happenings.</summary>
    public List<NewsItem> News { get; init; } = new();

    /// <summary>The player's inbox, oldest first.</summary>
    public List<MailMessage> Mail { get; init; } = new();

    /// <summary>The owner's notepad (the Notes app) — free text, part of the career.</summary>
    public string OwnerNotes { get; set; } = "";

    /// <summary>Fighters the owner has pinned to watch (the Notes app's shortlist).</summary>
    public List<FighterId> Shortlist { get; init; } = new();

    /// <summary>Active fighter rivalries across the world.</summary>
    public List<Rivalry> Rivalries { get; init; } = new();

    /// <summary>The owner's desk: two-way calls awaiting the player's answer (see Living.Decisions).</summary>
    public List<Decision> Decisions { get; init; } = new();

    /// <summary>Public bidding wars over marquee free agents (see Contracts.BiddingWars).</summary>
    public List<BiddingWar> BiddingWars { get; init; } = new();

    /// <summary>Contract talks a fighter remembers, one per fighter and promotion (see Contracts.NegotiationSession.Resume).</summary>
    public List<TalksMemory> Talks { get; init; } = new();

    /// <summary>Rival promotions' offers for the player's fighters, id order; resolved ones are kept a year (see Contracts.OfferMarket).</summary>
    public List<RivalOffer> RivalOffers { get; init; } = new();

    /// <summary>When the last org acquisition closed — buyouts are rare, seismic events (see Living.Acquisitions).</summary>
    public DateOnly? LastAcquisitionOn { get; set; }

    /// <summary>The warm bonds — family, friends, mentors — across the world.</summary>
    public List<Relationship> Relationships { get; init; } = new();

    /// <summary>Staff available to hire.</summary>
    public List<StaffMember> StaffMarket { get; init; } = new();

    // --- Cross-org rankings (derived by GlobalRankings.Recompute, but saved: the sim reads them before it next recomputes) ---
    /// <summary>Best fighters in each weight class across all promotions.</summary>
    public Dictionary<WeightClass, List<FighterId>> GlobalRankings { get; init; } = new();

    /// <summary>The pound-for-pound top fighters, across weight classes.</summary>
    public List<FighterId> PoundForPound { get; init; } = new();

    // --- Id generation ------------------------------------------------------
    // Kept private so ids can only come from here. After loading a save, call
    // SyncIdCounters() so freshly generated ids never collide with saved ones.
    private int _nextFighter, _nextPromotion, _nextTitle, _nextEvent, _nextFight, _nextCountry, _nextCity, _nextVenue, _nextCamp;

    public FighterId NewFighterId()     => new(++_nextFighter);
    public PromotionId NewPromotionId() => new(++_nextPromotion);
    public TitleId NewTitleId()         => new(++_nextTitle);
    public EventId NewEventId()         => new(++_nextEvent);
    public FightId NewFightId()         => new(++_nextFight);
    public CountryId NewCountryId()     => new(++_nextCountry);
    public CityId NewCityId()           => new(++_nextCity);
    public VenueId NewVenueId()         => new(++_nextVenue);
    public CampId NewCampId()           => new(++_nextCamp);

    /// <summary>
    /// Reset id counters to the highest id currently in use. The save reader calls
    /// this and then <see cref="RestoreIdCounters"/> with the saved counters.
    /// </summary>
    public void SyncIdCounters()
    {
        _nextFighter   = Fighters.Keys.Select(k => k.Value).DefaultIfEmpty(0).Max();
        _nextPromotion = Promotions.Keys.Select(k => k.Value).DefaultIfEmpty(0).Max();
        _nextTitle     = Titles.Keys.Select(k => k.Value).DefaultIfEmpty(0).Max();
        _nextEvent     = Events.Keys.Select(k => k.Value).DefaultIfEmpty(0).Max();
        _nextCountry   = Countries.Keys.Select(k => k.Value).DefaultIfEmpty(0).Max();
        _nextCity      = Cities.Keys.Select(k => k.Value).DefaultIfEmpty(0).Max();
        _nextVenue     = Venues.Keys.Select(k => k.Value).DefaultIfEmpty(0).Max();
        _nextCamp      = Camps.Keys.Select(k => k.Value).DefaultIfEmpty(0).Max();

        int maxFight = Events.Values
            .SelectMany(e => e.Card)
            .Select(f => f.Id.Value)
            .DefaultIfEmpty(0)
            .Max();
        _nextFight = maxFight;
    }

    /// <summary>
    /// The id counters by name, for the save. A counter is not derivable from the entities
    /// that survive (cancelling the newest event frees its id, yet the next one must not
    /// reuse it, because fight seeds and tick rolls key on ids).
    /// </summary>
    public Dictionary<string, int> IdCounters => new()
    {
        ["fighter"] = _nextFighter, ["promotion"] = _nextPromotion, ["title"] = _nextTitle,
        ["event"] = _nextEvent, ["fight"] = _nextFight, ["country"] = _nextCountry,
        ["city"] = _nextCity, ["venue"] = _nextVenue, ["camp"] = _nextCamp,
    };

    /// <summary>Restore saved counters after <see cref="SyncIdCounters"/>; never lowers one below what is in use.</summary>
    public void RestoreIdCounters(IReadOnlyDictionary<string, int> saved)
    {
        int Pick(string key, int derived) => Math.Max(derived, saved.GetValueOrDefault(key));
        _nextFighter = Pick("fighter", _nextFighter);
        _nextPromotion = Pick("promotion", _nextPromotion);
        _nextTitle = Pick("title", _nextTitle);
        _nextEvent = Pick("event", _nextEvent);
        _nextFight = Pick("fight", _nextFight);
        _nextCountry = Pick("country", _nextCountry);
        _nextCity = Pick("city", _nextCity);
        _nextVenue = Pick("venue", _nextVenue);
        _nextCamp = Pick("camp", _nextCamp);
    }

    // --- Convenience lookups ------------------------------------------------
    public Fighter? Get(FighterId id)   => Fighters.GetValueOrDefault(id);
    public Promotion? Get(PromotionId id) => Promotions.GetValueOrDefault(id);
    public Title? Get(TitleId id)       => Titles.GetValueOrDefault(id);
    public Event? Get(EventId id)       => Events.GetValueOrDefault(id);
    public Country? Get(CountryId id)   => Countries.GetValueOrDefault(id);
    public City? Get(CityId id)         => Cities.GetValueOrDefault(id);
    public Venue? Get(VenueId id)       => Venues.GetValueOrDefault(id);
    public Camp? Get(CampId id)         => Camps.GetValueOrDefault(id);

    public IEnumerable<Fighter> FreeAgents() =>
        Fighters.Values.Where(f => f.Contract is null && f.Status != FighterStatus.Retired && !f.LocalTalent);

    public IEnumerable<Fighter> RosterOf(PromotionId promotion) =>
        Fighters.Values.Where(f => f.Contract?.Promotion == promotion);
}
