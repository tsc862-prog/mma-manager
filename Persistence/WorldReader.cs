using Microsoft.Data.Sqlite;
using MmaManager.Core.Model;

namespace MmaManager.Persistence;

/// <summary>
/// Rebuilds a <see cref="World"/> graph from a save database. Reads exactly the
/// current schema (<see cref="World.LatestSaveVersion"/>) — there is no save
/// migration during development; a save from any other version is refused and
/// the player starts a new world.
/// </summary>
internal sealed class WorldReader
{
    private readonly SqliteConnection _c;

    public WorldReader(SqliteConnection c) => _c = c;

    public World Read()
    {
        var world = ReadMeta();
        ReadGeography(world);
        ReadFighters(world);
        ReadBoutHistory(world);
        ReadTitles(world);
        ReadPromotions(world);
        ReadNews(world);
        ReadMail(world);
        ReadShortlist(world);
        ReadOrg(world);
        ReadRivalries(world);
        ReadGlobalRankings(world);
        ReadDecisions(world);
        ReadBiddingWars(world);
        ReadRelationships(world);
        ReadEvents(world);
        return world;
    }

    private World ReadMeta()
    {
        using var cmd = _c.Cmd(null, "SELECT * FROM meta LIMIT 1");
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidDataException("Save file has no meta row.");
        int version = r.Int("save_version");
        if (version != World.LatestSaveVersion)
            throw new InvalidDataException(
                $"This save is version {version}; this build reads only version {World.LatestSaveVersion}. " +
                "Saves are not migrated during development — start a new world.");
        int? player = r.IntN("player_promotion");
        return new World
        {
            SaveVersion = version,
            CurrentDate = r.Date("current_date"),
            StartDate = r.Date("start_date"),
            RngSeed = ulong.Parse(r.Str("rng_seed")),
            PlayerPromotion = new PromotionId(player ?? 0),
            OwnerNotes = r.Str("owner_notes"),
            LastAcquisitionOn = r.DateN("last_acquisition"),
        };
    }

    private void ReadFighters(World world)
    {
        using var cmd = _c.Cmd(null, "SELECT * FROM fighters ORDER BY ordinal");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var f = new Fighter
            {
                Id = new FighterId(r.Int("id")),
                FirstName = r.Str("first_name"),
                LastName = r.Str("last_name"),
                Nickname = r.StrN("nickname"),
                BirthDate = r.Date("birth_date"),
                Sex = r.Enum<Sex>("sex"),
                Nationality = r.Enum<Region>("nationality"),
                Stance = r.Enum<Stance>("stance"),
                HeightCm = r.Int("height_cm"),
                ReachCm = r.Int("reach_cm"),
                NaturalWeightLbs = r.Int("natural_weight_lbs"),
                WeightClass = r.Enum<WeightClass>("weight_class"),
                PrimaryStyle = r.Enum<PrimaryStyle>("primary_style"),
                PotentialAbility = r.Int("potential_ability"),
                Status = r.Enum<FighterStatus>("status"),
                Morale = r.Int("morale"),
                Momentum = r.Int("momentum"),
                SuspendedUntil = r.DateN("suspended_until"),
            };

            f.PersonaTag = r.StrN("persona");
            f.Bio = r.StrN("bio");
            f.Designed = r.Bool("is_designed");
            if (r.IntN("home_city") is { } hc)
                f.HomeCity = new CityId(hc);
            f.Country = new CountryId(r.Int("country_id"));
            f.LocalTalent = r.Bool("is_local");

            f.Record.Wins = r.Int("wins");
            f.Record.Losses = r.Int("losses");
            f.Record.Draws = r.Int("draws");
            f.Record.NoContests = r.Int("no_contests");
            f.Record.WinsByKo = r.Int("wins_ko");
            f.Record.WinsBySubmission = r.Int("wins_sub");
            f.Record.WinsByDecision = r.Int("wins_dec");
            f.Record.CurrentStreak = r.Int("streak");

            if (r.IntN("contract_promotion") is { } cp)
            {
                f.Contract = new Contract
                {
                    Promotion = new PromotionId(cp),
                    SignedOn = r.Date("contract_signed_on"),
                    Type = r.Enum<ContractType>("contract_type"),
                    PursePerFight = r.Cash("purse"),
                    WinBonus = r.Cash("win_bonus"),
                    FightsRemaining = r.Int("fights_remaining"),
                    IsExclusive = r.Bool("is_exclusive"),
                    HasChampionsClause = r.Bool("has_champions_clause"),
                    SigningBonus = r.Cash("signing_bonus"),
                    PpvPoints = r.Int("ppv_points"),
                    PerformanceBonus = r.Cash("performance_bonus"),
                    Guaranteed = r.Bool("guaranteed"),
                    PoachWarned = r.Bool("poach_warned"),
                };
            }

            if (r.StrN("injury_severity") is not null)
            {
                f.CurrentInjury = new Injury
                {
                    Severity = r.Enum<InjurySeverity>("injury_severity"),
                    Description = r.Str("injury_desc"),
                    OccurredOn = r.Date("injury_occurred"),
                    ExpectedReturn = r.Date("injury_return"),
                };
            }

            f.ReadyToFightOn = r.DateN("ready_to_fight_on");
            f.Loyalty = r.Int("loyalty");
            f.MarketingPush = r.Int("marketing_push") != 0;
            f.Camp = r.IntN("camp_id") is { } camp ? new CampId(camp) : null;
            f.CampStipend = r.Int("camp_stipend") != 0;
            f.StarPower = r.Int("star_power") != 0;
            f.DiscoveredBy = r.IntN("discovered_by") is { } disc ? new PromotionId(disc) : null;
            f.Disposition = r.Enum<Disposition>("disposition");

            foreach (var rc in FighterColumns.Ratings)
                rc.Set(f, r.Int(rc.Column));

            world.Fighters[f.Id] = f;
        }
    }

    private void ReadBoutHistory(World world)
    {
        using (var cmd = _c.Cmd(null, "SELECT fighter_id, fight_id FROM fighter_bouts ORDER BY fighter_id, ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                if (world.Get(new FighterId(r.Int("fighter_id"))) is { } f)
                    f.BoutHistory.Add(new FightId(r.Int("fight_id")));
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM fighter_city_rep ORDER BY rowid"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                if (world.Get(new FighterId(r.Int("fighter_id"))) is { } f)
                    f.CityReputation[new CityId(r.Int("city_id"))] = r.Int("rating");
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM fighter_accolades ORDER BY fighter_id, ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                if (world.Get(new FighterId(r.Int("fighter_id"))) is { } f)
                    f.Accolades.Add(new Accolade(r.Date("date"), r.Str("title")));
        }
    }

    private void ReadTitles(World world)
    {
        using (var cmd = _c.Cmd(null, "SELECT * FROM titles ORDER BY ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                int? champ = r.IntN("current_champion");
                var title = new Title
                {
                    Id = new TitleId(r.Int("id")),
                    Promotion = new PromotionId(r.Int("promotion_id")),
                    WeightClass = r.Enum<WeightClass>("weight_class"),
                    Name = r.Str("name"),
                    CurrentChampion = champ is null ? null : new FighterId(champ.Value),
                    Prestige = r.Int("prestige"),
                };
                world.Titles[title.Id] = title;
            }
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM title_reigns ORDER BY title_id, ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (world.Get(new TitleId(r.Int("title_id"))) is not { } title) continue;
                title.Lineage.Add(new TitleReign
                {
                    Champion = new FighterId(r.Int("champion")),
                    WonOn = r.Date("won_on"),
                    LostOn = r.DateN("lost_on"),
                    Defenses = r.Int("defenses"),
                });
            }
        }
    }

    private void ReadPromotions(World world)
    {
        using (var cmd = _c.Cmd(null, "SELECT * FROM promotions ORDER BY ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var promo = new Promotion
                {
                    Id = new PromotionId(r.Int("id")),
                    Name = r.Str("name"),
                    Abbreviation = r.Str("abbreviation"),
                    BaseRegion = r.Enum<Region>("base_region"),
                    IsPlayerControlled = r.Bool("is_player"),
                    Balance = r.Cash("balance"),
                    Prestige = r.Int("prestige"),
                    Reputation = r.Int("reputation"),
                    Defunct = r.Bool("defunct"),
                };
                if (r.IntN("home_city") is { } hc)
                    promo.HomeCity = new CityId(hc);
                promo.Rules.RoundMinutes = r.Int("round_minutes");
                promo.Rules.StandardRounds = r.Int("standard_rounds");
                promo.Rules.TitleRounds = r.Int("title_rounds");
                promo.Rules.FightOfNightBonus = r.Cash("fotn_bonus");
                promo.PrestigeHundredths = r.Int("prestige_hundredths");
                if (r.Str("milestones") is { Length: > 0 } ms)
                    foreach (var key in ms.Split('|')) promo.Milestones.Add(key);
                promo.MarketingBudget = r.Cash("marketing_budget");
                promo.TierClearance = r.Enum<OrgSize>("tier_clearance");
                promo.PromotionOffered = r.Bool("promotion_offered");
                promo.EligibleSince = r.DateN("eligible_since");
                promo.SponsoredCamp = r.IntN("sponsored_camp") is { } sc ? new CampId(sc) : null;
                promo.FaceOfCompany = r.IntN("face_fighter") is { } ff ? new FighterId(ff) : null;
                promo.Lore = r.StrN("lore");
                promo.OwnerName = r.StrN("owner_name");
                promo.OwnerNote = r.StrN("owner_note");
                promo.FoundedYear = r.Int("founded_year");
                foreach (var partner in r.Str("courted_by").Split('|', StringSplitOptions.RemoveEmptyEntries))
                    promo.CourtedBy.Add(partner);
                if (r.StrN("stream_name") is { } streamName)
                    promo.Stream = new StreamingPlatform
                    {
                        Name = streamName,
                        LaunchedOn = r.Date("stream_launched"),
                        Subscribers = r.Int("stream_subs"),
                    };
                promo.LastClearedTier = r.StrN("last_cleared_tier") is { } lct ? Enum.Parse<OrgSize>(lct) : null;
                promo.LastClearedOn = r.DateN("last_cleared_on");
                world.Promotions[promo.Id] = promo;
            }
        }

        // Titles a promotion sanctions (ordered by id for stability).
        foreach (var promo in world.Promotions.Values)
            promo.Titles.AddRange(world.Titles.Values
                .Where(t => t.Promotion == promo.Id)
                .OrderBy(t => t.Id.Value)
                .Select(t => t.Id));

        using (var cmd = _c.Cmd(null, "SELECT * FROM promotion_city_pop ORDER BY rowid"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                if (world.Get(new PromotionId(r.Int("promotion_id"))) is { } promo)
                    promo.PopularityByCity[new CityId(r.Int("city_id"))] = r.Int("rating");
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM divisions ORDER BY promotion_id, ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (world.Get(new PromotionId(r.Int("promotion_id"))) is not { } promo) continue;
                int? title = r.IntN("title_id");
                promo.Divisions.Add(new Division
                {
                    WeightClass = r.Enum<WeightClass>("weight_class"),
                    Sex = r.Enum<Sex>("sex"),
                    Title = title is null ? null : new TitleId(title.Value),
                });
            }
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM division_rankings ORDER BY promotion_id, weight_class, position"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (world.Get(new PromotionId(r.Int("promotion_id"))) is not { } promo) continue;
                var wc = r.Enum<WeightClass>("weight_class");
                var sex = r.Enum<Sex>("sex");
                var div = promo.Divisions.FirstOrDefault(d => d.WeightClass == wc && d.Sex == sex);
                div?.Rankings.Add(new FighterId(r.Int("fighter_id")));
            }
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM ledger_entries ORDER BY promotion_id, ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (world.Get(new PromotionId(r.Int("promotion_id"))) is not { } promo) continue;
                promo.Ledger.Add(new LedgerEntry(
                    r.Date("date"), r.Enum<FinanceCategory>("category"), r.Cash("amount"), r.Str("description")));
            }
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM sponsor_deals ORDER BY promotion_id, ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (world.Get(new PromotionId(r.Int("promotion_id"))) is not { } promo) continue;
                promo.SponsorDeals.Add(new SponsorDeal
                {
                    Partner = r.Str("partner"),
                    FeePerEvent = r.Cash("fee_per_event"),
                    EventsRemaining = r.Int("events_remaining"),
                    Requirements = r.Reqs("requirements"),
                    Strikes = r.Int("strikes"),
                    SignedOn = r.Date("signed_on"),
                });
            }
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM media_deals ORDER BY promotion_id, ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (world.Get(new PromotionId(r.Int("promotion_id"))) is not { } promo) continue;
                promo.MediaDeals.Add(new MediaDeal
                {
                    Kind = r.Enum<MediaKind>("kind"),
                    Partner = r.Str("partner"),
                    RightsFeePerEvent = r.Cash("fee_per_event"),
                    MonthsRemaining = r.Int("months_remaining"),
                    Requirements = r.Reqs("requirements"),
                    Strikes = r.Int("strikes"),
                    SignedOn = r.Date("signed_on"),
                    LastAiredOn = r.DateN("last_aired"),
                });
            }
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM deal_offers ORDER BY promotion_id, ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (world.Get(new PromotionId(r.Int("promotion_id"))) is not { } promo) continue;
                promo.Offers.Add(new DealOffer
                {
                    Media = r.EnumN<MediaKind>("media_kind"),
                    Partner = r.Str("partner"),
                    FeePerEvent = r.Cash("fee_per_event"),
                    Term = r.Int("term"),
                    Requirements = r.Reqs("requirements"),
                    OfferedOn = r.Date("offered_on"),
                    ExpiresOn = r.Date("expires_on"),
                });
            }
        }
    }

    private void ReadGlobalRankings(World world)
    {
        using (var cmd = _c.Cmd(null, "SELECT * FROM global_rankings ORDER BY rowid"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                world.GlobalRankings[r.Enum<WeightClass>("weight_class")] = r.Str("fighters")
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => new FighterId(int.Parse(x)))
                    .ToList();
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM pound_for_pound ORDER BY ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                world.PoundForPound.Add(new FighterId(r.Int("fighter_id")));
        }
    }

    private void ReadRivalries(World world)
    {
        using var cmd = _c.Cmd(null, "SELECT * FROM rivalries ORDER BY ordinal");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            world.Rivalries.Add(new Rivalry
            {
                A = new FighterId(r.Int("fighter_a")),
                B = new FighterId(r.Int("fighter_b")),
                Intensity = r.Int("intensity"),
                Since = r.Date("since"),
            });
    }

    private void ReadDecisions(World world)
    {
        using var cmd = _c.Cmd(null, "SELECT * FROM decisions ORDER BY rowid");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            world.Decisions.Add(new Decision
            {
                Id = r.Int("id"),
                Kind = r.Enum<DecisionKind>("kind"),
                Created = r.Date("created"),
                Deadline = r.Date("deadline"),
                Fighter = r.IntN("fighter") is { } f ? new FighterId(f) : null,
                Detail = r.Str("detail"),
                Sponsor = r.Str("sponsor"),
                Promotion = r.IntN("promotion_id") is { } pid ? new PromotionId(pid) : null,
                Price = r.Cash("price"),
                State = r.Enum<DecisionState>("state"),
            });
    }

    private void ReadBiddingWars(World world)
    {
        using var cmd = _c.Cmd(null, "SELECT * FROM bidding_wars ORDER BY ordinal");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var war = new BiddingWar
            {
                Fighter = new FighterId(r.Int("fighter_id")),
                Round = r.Int("round"),
                CurrentBid = r.Cash("current_bid"),
                NextRoundOn = r.Date("next_round_on"),
            };
            foreach (var s in r.Str("suitors").Split('|', StringSplitOptions.RemoveEmptyEntries))
                war.Suitors.Add(new PromotionId(int.Parse(s)));
            world.BiddingWars.Add(war);
        }
    }

    private void ReadRelationships(World world)
    {
        using var cmd = _c.Cmd(null, "SELECT * FROM relationships ORDER BY ordinal");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            world.Relationships.Add(new Relationship
            {
                A = new FighterId(r.Int("fighter_a")),
                B = new FighterId(r.Int("fighter_b")),
                Kind = (RelationshipKind)r.Int("kind"),
                Strength = r.Int("strength"),
                Since = r.Date("since"),
                Note = r.StrN("note"),
            });
    }

    private void ReadOrg(World world)
    {
        using (var cmd = _c.Cmd(null, "SELECT * FROM staff_market ORDER BY ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                world.StaffMarket.Add(new StaffMember
                {
                    Role = r.Enum<StaffRole>("role"),
                    Name = r.Str("name"),
                    Quality = r.Int("quality"),
                    MonthlySalary = r.Cash("salary"),
                    Background = r.StrN("background"),
                });
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM promotion_staff ORDER BY promotion_id, ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (world.Get(new PromotionId(r.Int("promotion_id"))) is not { } promo) continue;
                promo.Staff.Add(new StaffMember
                {
                    Role = r.Enum<StaffRole>("role"),
                    Name = r.Str("name"),
                    Quality = r.Int("quality"),
                    MonthlySalary = r.Cash("salary"),
                    Background = r.StrN("background"),
                });
            }
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM objectives ORDER BY promotion_id, ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (world.Get(new PromotionId(r.Int("promotion_id"))) is not { } promo) continue;
                promo.Objectives.Add(new Objective
                {
                    Kind = r.Enum<ObjectiveKind>("kind"),
                    Description = r.Str("description"),
                    Target = r.Int("target"),
                    Start = r.Date("start"),
                    Deadline = r.Date("deadline"),
                    Status = r.Enum<ObjectiveStatus>("status"),
                });
            }
        }
    }

    private void ReadMail(World world)
    {
        using var cmd = _c.Cmd(null, "SELECT * FROM mail ORDER BY ordinal");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            world.Mail.Add(new MailMessage
            {
                Date = r.Date("date"),
                Category = r.Enum<MailCategory>("category"),
                From = r.Str("sender"),
                Subject = r.Str("subject"),
                Body = r.Str("body"),
                Read = r.Bool("is_read"),
                AboutFighter = r.IntN("about_fighter") is { } af ? new FighterId(af) : null,
                AboutEvent = r.IntN("about_event") is { } ae ? new EventId(ae) : null,
            });
        }
    }

    private void ReadNews(World world)
    {
        using var cmd = _c.Cmd(null, "SELECT * FROM news ORDER BY ordinal");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            world.News.Add(new NewsItem(
                r.Date("date"), r.Enum<NewsCategory>("category"),
                r.Str("headline"), r.Str("body"), new PromotionId(r.Int("promotion_id")),
                r.IntN("about_fighter") is { } af ? new FighterId(af) : null,
                r.IntN("about_event") is { } ae ? new EventId(ae) : null));
        }
    }

    private void ReadShortlist(World world)
    {
        using var cmd = _c.Cmd(null, "SELECT * FROM shortlist ORDER BY ordinal");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            world.Shortlist.Add(new FighterId(r.Int("fighter_id")));
    }

    private void ReadGeography(World world)
    {
        using (var cmd = _c.Cmd(null, "SELECT * FROM countries ORDER BY ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var country = new Country
                {
                    Id = new CountryId(r.Int("id")),
                    Name = r.Str("name"),
                    Code = r.Str("code"),
                    Continent = r.Enum<Region>("continent"),
                };
                string areas = r.Str("areas");
                if (areas.Length > 0) country.Areas.AddRange(areas.Split('|'));
                country.MmaInterest = r.Int("mma_interest");
                country.MmaInterestBase = r.Int("mma_interest_base");
                world.Countries[country.Id] = country;
            }
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM cities ORDER BY ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var city = new City
                {
                    Id = new CityId(r.Int("id")),
                    Name = r.Str("name"),
                    Region = r.Enum<Region>("region"),
                    Market = r.Int("market"),
                    Country = new CountryId(r.Int("country_id")),
                    Area = r.IntN("area"),
                };
                city.MmaInterest = r.Int("mma_interest");
                city.MmaInterestBase = r.Int("mma_interest_base");
                world.Cities[city.Id] = city;
            }
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM venues ORDER BY ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var venue = new Venue
                {
                    Id = new VenueId(r.Int("id")),
                    Name = r.Str("name"),
                    City = new CityId(r.Int("city_id")),
                    Capacity = r.Int("capacity"),
                    Rent = r.Cash("rent"),
                };
                world.Venues[venue.Id] = venue;
            }
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM camps ORDER BY ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var camp = new Camp
                {
                    Id = new CampId(r.Int("id")),
                    Name = r.Str("name"),
                    City = new CityId(r.Int("city_id")),
                    Specialty = r.Enum<PrimaryStyle>("specialty"),
                    Reputation = r.Int("reputation"),
                    HeadCoach = r.IntN("head_coach") is { } hc ? new FighterId(hc) : null,
                    CoachQuality = r.Int("coach_quality"),
                    Capacity = r.Int("capacity"),
                    MonthlyFee = r.Cash("monthly_fee"),
                    Lore = r.StrN("lore"),
                };
                world.Camps[camp.Id] = camp;
            }
        }
    }

    private void ReadEvents(World world)
    {
        using (var cmd = _c.Cmd(null, "SELECT * FROM events ORDER BY ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var ev = new Event
                {
                    Id = new EventId(r.Int("id")),
                    Promotion = new PromotionId(r.Int("promotion_id")),
                    Name = r.Str("name"),
                    Date = r.Date("date"),
                    Venue = r.StrN("venue"),
                    VenueId = r.IntN("venue_id") is { } vid ? new VenueId(vid) : null,
                    Region = r.Enum<Region>("region"),
                    Tier = r.Enum<EventTier>("tier"),
                    Status = r.Enum<EventStatus>("status"),
                    MainCardFights = r.Int("main_card"),
                    HypeBonus = r.Dbl("hype_bonus"),
                };
                if (r.Bool("fin_present"))
                {
                    ev.Financials = new EventFinancials
                    {
                        Attendance = r.Int("attendance"),
                        GateRevenue = r.Cash("gate"),
                        PayPerViewBuys = r.Long("ppv_buys"),
                        PayPerViewRevenue = r.Cash("ppv_rev"),
                        ProductionCost = r.Cash("production"),
                        FighterPayroll = r.Cash("payroll"),
                    };
                }
                world.Events[ev.Id] = ev;
            }
        }

        using (var cmd = _c.Cmd(null, "SELECT * FROM fights ORDER BY event_id, ordinal"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (world.Get(new EventId(r.Int("event_id"))) is not { } ev) continue;
                int? title = r.IntN("title_id");
                int? catchweight = r.IntN("catchweight");
                var fight = new Fight
                {
                    Id = new FightId(r.Int("id")),
                    RedCorner = new FighterId(r.Int("red")),
                    BlueCorner = new FighterId(r.Int("blue")),
                    WeightClass = r.Enum<WeightClass>("weight_class"),
                    ScheduledRounds = r.Int("scheduled_rounds"),
                    IsTitleFight = r.Bool("is_title"),
                    Title = title is null ? null : new TitleId(title.Value),
                    CatchweightLbs = catchweight,
                    WeightMissBy = r.IntN("weight_miss") is { } wm ? new FighterId(wm) : null,
                    OverturnedNc = r.Bool("overturned"),
                };
                if (r.Bool("out_present"))
                {
                    int? win = r.IntN("winner");
                    int? lose = r.IntN("loser");
                    fight.Outcome = new FightOutcome
                    {
                        Winner = win is null ? null : new FighterId(win.Value),
                        Loser = lose is null ? null : new FighterId(lose.Value),
                        Method = r.Enum<FightMethod>("method"),
                        EndRound = r.Int("end_round"),
                        EndTimeSeconds = r.Int("end_time"),
                        RoundScores = r.Scores("round_scores"),
                    };
                }
                ev.Card.Add(fight);
            }
        }

        // Statlines onto the fights they belong to (index fights by id first).
        var fightsById = world.Events.Values
            .SelectMany(e => e.Card)
            .Where(f => f.Outcome is not null)
            .ToDictionary(f => f.Id.Value);

        using (var cmd = _c.Cmd(null, "SELECT * FROM fight_stats ORDER BY rowid"))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (!fightsById.TryGetValue(r.Int("fight_id"), out var fight)) continue;
                fight.Outcome!.Stats[new FighterId(r.Int("fighter_id"))] = new FightStatline
                {
                    SignificantStrikesLanded = r.Int("sig_landed"),
                    SignificantStrikesAttempted = r.Int("sig_att"),
                    TakedownsLanded = r.Int("td_landed"),
                    TakedownsAttempted = r.Int("td_att"),
                    SubmissionAttempts = r.Int("sub_att"),
                    KnockdownsScored = r.Int("kd"),
                    ControlTimeSeconds = r.Int("control"),
                };
            }
        }
    }
}
