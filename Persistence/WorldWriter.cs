using Microsoft.Data.Sqlite;
using MmaManager.Core.Model;

namespace MmaManager.Persistence;

/// <summary>Writes a <see cref="World"/> graph into an (empty) save database.</summary>
internal sealed class WorldWriter
{
    private readonly SqliteConnection _c;
    private readonly SqliteTransaction _tx;

    public WorldWriter(SqliteConnection c, SqliteTransaction tx) { _c = c; _tx = tx; }

    public void Write(World world)
    {
        WriteMeta(world);
        WriteIdCounters(world);
        WriteFighters(world);
        WritePromotions(world);
        WriteTitles(world);
        WriteGeography(world);
        WriteNews(world);
        WriteMail(world);
        WriteShortlist(world);
        WriteOrg(world);
        WriteRivalries(world);
        WriteGlobalRankings(world);
        WriteDecisions(world);
        WriteBiddingWars(world);
        WriteRelationships(world);
        WriteEvents(world);
    }

    // The cross-org rankings are derived (GlobalRankings.Recompute), but the sim reads them
    // before it next recomputes (year-end awards), so a world loaded without them behaves
    // differently from one that never was saved. Saved as-is; one row per class so a class
    // with an empty list keeps its key.
    private void WriteGlobalRankings(World w)
    {
        using var gr = _c.Cmd(_tx, "INSERT INTO global_rankings (weight_class, fighters) VALUES ($w,$f)");
        foreach (var kv in w.GlobalRankings)
        {
            gr.Parameters.Clear();
            gr.Bind("$w", kv.Key.ToString());
            gr.Bind("$f", string.Join("|", kv.Value.Select(f => f.Value)));
            gr.ExecuteNonQuery();
        }

        using var p4p = _c.Cmd(_tx, "INSERT INTO pound_for_pound (ordinal, fighter_id) VALUES ($o,$f)");
        for (int i = 0; i < w.PoundForPound.Count; i++)
        {
            p4p.Parameters.Clear();
            p4p.Bind("$o", i);
            p4p.Bind("$f", w.PoundForPound[i].Value);
            p4p.ExecuteNonQuery();
        }
    }

    private void WriteRivalries(World w)
    {
        using var riv = _c.Cmd(_tx,
            "INSERT INTO rivalries (ordinal, fighter_a, fighter_b, intensity, since) VALUES ($o,$a,$b,$i,$s)");
        for (int i = 0; i < w.Rivalries.Count; i++)
        {
            var rv = w.Rivalries[i];
            riv.Parameters.Clear();
            riv.Bind("$o", i);
            riv.Bind("$a", rv.A.Value);
            riv.Bind("$b", rv.B.Value);
            riv.Bind("$i", rv.Intensity);
            riv.Bind("$s", Db.Date(rv.Since));
            riv.ExecuteNonQuery();
        }
    }

    private void WriteDecisions(World w)
    {
        using var dec = _c.Cmd(_tx,
            "INSERT INTO decisions (id, kind, created, deadline, fighter, detail, sponsor, promotion_id, price, state) VALUES ($id,$k,$c,$dl,$f,$d,$sp,$pid,$pr,$st)");
        foreach (var d in w.Decisions)
        {
            dec.Parameters.Clear();
            dec.Bind("$id", d.Id);
            dec.Bind("$k", d.Kind.ToString());
            dec.Bind("$c", Db.Date(d.Created));
            dec.Bind("$dl", Db.Date(d.Deadline));
            dec.Bind("$f", Db.IdN(d.Fighter?.Value));
            dec.Bind("$d", d.Detail);
            dec.Bind("$sp", d.Sponsor);
            dec.Bind("$pid", Db.IdN(d.Promotion?.Value));
            dec.Bind("$pr", Db.Cash(d.Price));
            dec.Bind("$st", d.State.ToString());
            dec.ExecuteNonQuery();
        }
    }

    private void WriteBiddingWars(World w)
    {
        using var war = _c.Cmd(_tx,
            "INSERT INTO bidding_wars (ordinal, fighter_id, round, current_bid, next_round_on, suitors) VALUES ($o,$f,$r,$b,$n,$s)");
        for (int i = 0; i < w.BiddingWars.Count; i++)
        {
            var bw = w.BiddingWars[i];
            war.Parameters.Clear();
            war.Bind("$o", i);
            war.Bind("$f", bw.Fighter.Value);
            war.Bind("$r", bw.Round);
            war.Bind("$b", Db.Cash(bw.CurrentBid));
            war.Bind("$n", Db.Date(bw.NextRoundOn));
            war.Bind("$s", string.Join("|", bw.Suitors.Select(s => s.Value)));
            war.ExecuteNonQuery();
        }
    }

    private void WriteRelationships(World w)
    {
        using var rel = _c.Cmd(_tx,
            "INSERT INTO relationships (ordinal, fighter_a, fighter_b, kind, strength, since, note) VALUES ($o,$a,$b,$k,$st,$s,$n)");
        for (int i = 0; i < w.Relationships.Count; i++)
        {
            var rv = w.Relationships[i];
            rel.Parameters.Clear();
            rel.Bind("$o", i);
            rel.Bind("$a", rv.A.Value);
            rel.Bind("$b", rv.B.Value);
            rel.Bind("$k", (int)rv.Kind);
            rel.Bind("$st", rv.Strength);
            rel.Bind("$s", Db.Date(rv.Since));
            rel.Bind("$n", Db.Text(rv.Note));
            rel.ExecuteNonQuery();
        }
    }

    private void WriteOrg(World w)
    {
        using var market = _c.Cmd(_tx,
            "INSERT INTO staff_market (ordinal, role, name, quality, salary, background) VALUES ($o,$r,$n,$q,$s,$bg)");
        for (int i = 0; i < w.StaffMarket.Count; i++)
        {
            var s = w.StaffMarket[i];
            market.Parameters.Clear();
            market.Bind("$o", i);
            market.Bind("$r", s.Role.ToString());
            market.Bind("$n", s.Name);
            market.Bind("$q", s.Quality);
            market.Bind("$s", Db.Cash(s.MonthlySalary));
            market.Bind("$bg", Db.Text(s.Background));
            market.ExecuteNonQuery();
        }

        using var staff = _c.Cmd(_tx,
            "INSERT INTO promotion_staff (promotion_id, ordinal, role, name, quality, salary, background) VALUES ($p,$o,$r,$n,$q,$s,$bg)");
        using var obj = _c.Cmd(_tx,
            "INSERT INTO objectives (promotion_id, ordinal, kind, description, target, start, deadline, status) VALUES ($p,$o,$k,$d,$t,$st,$dl,$stat)");

        foreach (var p in w.Promotions.Values)
        {
            for (int i = 0; i < p.Staff.Count; i++)
            {
                var s = p.Staff[i];
                staff.Parameters.Clear();
                staff.Bind("$p", p.Id.Value);
                staff.Bind("$o", i);
                staff.Bind("$r", s.Role.ToString());
                staff.Bind("$n", s.Name);
                staff.Bind("$q", s.Quality);
                staff.Bind("$s", Db.Cash(s.MonthlySalary));
                staff.Bind("$bg", Db.Text(s.Background));
                staff.ExecuteNonQuery();
            }

            for (int i = 0; i < p.Objectives.Count; i++)
            {
                var ob = p.Objectives[i];
                obj.Parameters.Clear();
                obj.Bind("$p", p.Id.Value);
                obj.Bind("$o", i);
                obj.Bind("$k", ob.Kind.ToString());
                obj.Bind("$d", ob.Description);
                obj.Bind("$t", ob.Target);
                obj.Bind("$st", Db.Date(ob.Start));
                obj.Bind("$dl", Db.Date(ob.Deadline));
                obj.Bind("$stat", ob.Status.ToString());
                obj.ExecuteNonQuery();
            }
        }
    }

    private void WriteMail(World w)
    {
        using var mail = _c.Cmd(_tx,
            "INSERT INTO mail (ordinal, date, category, sender, subject, body, is_read, about_fighter, about_event) VALUES ($o,$d,$c,$s,$sub,$b,$r,$af,$ae)");
        for (int i = 0; i < w.Mail.Count; i++)
        {
            var m = w.Mail[i];
            mail.Parameters.Clear();
            mail.Bind("$o", i);
            mail.Bind("$d", Db.Date(m.Date));
            mail.Bind("$c", m.Category.ToString());
            mail.Bind("$s", m.From);
            mail.Bind("$sub", m.Subject);
            mail.Bind("$b", m.Body);
            mail.Bind("$r", m.Read ? 1 : 0);
            mail.Bind("$af", m.AboutFighter?.Value);
            mail.Bind("$ae", m.AboutEvent?.Value);
            mail.ExecuteNonQuery();
        }
    }

    private void WriteNews(World w)
    {
        using var news = _c.Cmd(_tx,
            "INSERT INTO news (ordinal, date, category, headline, body, promotion_id, about_fighter, about_event) VALUES ($o,$d,$c,$h,$b,$p,$af,$ae)");
        for (int i = 0; i < w.News.Count; i++)
        {
            var n = w.News[i];
            news.Parameters.Clear();
            news.Bind("$o", i);
            news.Bind("$d", Db.Date(n.Date));
            news.Bind("$c", n.Category.ToString());
            news.Bind("$h", n.Headline);
            news.Bind("$b", n.Body);
            news.Bind("$p", n.Promotion.Value);
            news.Bind("$af", n.AboutFighter?.Value);
            news.Bind("$ae", n.AboutEvent?.Value);
            news.ExecuteNonQuery();
        }
    }

    private void WriteGeography(World w)
    {
        using var country = _c.Cmd(_tx, "INSERT INTO countries (id, ordinal, name, code, continent, areas, mma_interest, mma_interest_base) VALUES ($id,$ord,$n,$c,$ct,$a,$mi,$mib)");
        using var city = _c.Cmd(_tx, "INSERT INTO cities (id, ordinal, name, region, market, country_id, area, mma_interest, mma_interest_base) VALUES ($id,$ord,$n,$r,$m,$c,$a,$mi,$mib)");
        using var venue = _c.Cmd(_tx, "INSERT INTO venues (id, ordinal, name, city_id, capacity, rent) VALUES ($id,$ord,$n,$c,$cap,$rent)");
        using var camp = _c.Cmd(_tx, "INSERT INTO camps (id, ordinal, name, city_id, specialty, reputation, head_coach, coach_quality, capacity, monthly_fee, lore) VALUES ($id,$ord,$n,$c,$sp,$rep,$hc,$q,$cap,$fee,$lore)");

        int countryOrd = 0;
        foreach (var k in w.Countries.Values)
        {
            country.Parameters.Clear();
            country.Bind("$id", k.Id.Value);
            country.Bind("$ord", countryOrd++);
            country.Bind("$n", k.Name);
            country.Bind("$c", k.Code);
            country.Bind("$ct", k.Continent.ToString());
            country.Bind("$a", string.Join("|", k.Areas));
            country.Bind("$mi", k.MmaInterest);
            country.Bind("$mib", k.MmaInterestBase);
            country.ExecuteNonQuery();
        }

        int cityOrd = 0;
        foreach (var c in w.Cities.Values)
        {
            city.Parameters.Clear();
            city.Bind("$id", c.Id.Value);
            city.Bind("$ord", cityOrd++);
            city.Bind("$n", c.Name);
            city.Bind("$r", c.Region.ToString());
            city.Bind("$m", c.Market);
            city.Bind("$c", c.Country.Value);
            city.Bind("$a", Db.IdN(c.Area));
            city.Bind("$mi", c.MmaInterest);
            city.Bind("$mib", c.MmaInterestBase);
            city.ExecuteNonQuery();
        }

        int venueOrd = 0;
        foreach (var v in w.Venues.Values)
        {
            venue.Parameters.Clear();
            venue.Bind("$id", v.Id.Value);
            venue.Bind("$ord", venueOrd++);
            venue.Bind("$n", v.Name);
            venue.Bind("$c", v.City.Value);
            venue.Bind("$cap", v.Capacity);
            venue.Bind("$rent", Db.Cash(v.Rent));
            venue.ExecuteNonQuery();
        }

        int campOrd = 0;
        foreach (var g in w.Camps.Values)
        {
            camp.Parameters.Clear();
            camp.Bind("$id", g.Id.Value);
            camp.Bind("$ord", campOrd++);
            camp.Bind("$n", g.Name);
            camp.Bind("$c", g.City.Value);
            camp.Bind("$sp", g.Specialty.ToString());
            camp.Bind("$rep", g.Reputation);
            camp.Bind("$hc", Db.IdN(g.HeadCoach?.Value));
            camp.Bind("$q", g.CoachQuality);
            camp.Bind("$cap", g.Capacity);
            camp.Bind("$fee", Db.Cash(g.MonthlyFee));
            camp.Bind("$lore", Db.Text(g.Lore));
            camp.ExecuteNonQuery();
        }
    }

    private void WriteIdCounters(World w)
    {
        using var cmd = _c.Cmd(_tx, "INSERT INTO id_counters (name, value) VALUES ($n,$v)");
        foreach (var kv in w.IdCounters)
        {
            cmd.Parameters.Clear();
            cmd.Bind("$n", kv.Key);
            cmd.Bind("$v", kv.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void WriteMeta(World w)
    {
        using var cmd = _c.Cmd(_tx,
            "INSERT INTO meta (save_version, current_date, start_date, rng_seed, player_promotion, owner_notes, last_acquisition) VALUES ($v,$d,$sd,$s,$p,$n,$la)");
        cmd.Bind("$v", World.LatestSaveVersion);
        cmd.Bind("$d", Db.Date(w.CurrentDate));
        cmd.Bind("$sd", Db.Date(w.StartDate));
        cmd.Bind("$s", w.RngSeed.ToString());
        cmd.Bind("$p", w.PlayerPromotion.Value == 0 ? DBNull.Value : w.PlayerPromotion.Value);
        cmd.Bind("$n", w.OwnerNotes);
        cmd.Bind("$la", Db.DateN(w.LastAcquisitionOn));
        cmd.ExecuteNonQuery();
    }

    private void WriteShortlist(World w)
    {
        using var cmd = _c.Cmd(_tx, "INSERT INTO shortlist (ordinal, fighter_id) VALUES ($o,$f)");
        for (int i = 0; i < w.Shortlist.Count; i++)
        {
            cmd.Parameters.Clear();
            cmd.Bind("$o", i);
            cmd.Bind("$f", w.Shortlist[i].Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void WriteFighters(World w)
    {
        var core = new[]
        {
            "id", "ordinal", "first_name", "last_name", "nickname", "persona", "bio", "is_designed", "is_local", "home_city", "country_id", "birth_date", "sex", "nationality", "stance",
            "height_cm", "reach_cm", "natural_weight_lbs", "weight_class", "primary_style", "potential_ability",
            "status", "morale", "momentum", "suspended_until",
            "wins", "losses", "draws", "no_contests", "wins_ko", "wins_sub", "wins_dec", "streak",
            "contract_promotion", "contract_signed_on", "contract_type", "purse", "win_bonus",
            "fights_remaining", "is_exclusive", "has_champions_clause",
            "signing_bonus", "ppv_points", "performance_bonus", "guaranteed", "poach_warned",
            "injury_severity", "injury_desc", "injury_occurred", "injury_return",
            "ready_to_fight_on", "loyalty", "marketing_push", "camp_id", "camp_stipend", "star_power",
            "discovered_by", "disposition", "origin_country",
        };
        var cols = core.Concat(FighterColumns.Ratings.Select(r => r.Column)).ToList();
        string sql = $"INSERT INTO fighters ({string.Join(",", cols)}) VALUES ({string.Join(",", cols.Select(c => "$" + c))})";

        using var cmd = _c.Cmd(_tx, sql);
        using var bouts = _c.Cmd(_tx, "INSERT INTO fighter_bouts (fighter_id, ordinal, fight_id) VALUES ($f,$o,$b)");
        using var rrep = _c.Cmd(_tx, "INSERT INTO fighter_city_rep (fighter_id, city_id, rating) VALUES ($f,$r,$v)");
        using var acc = _c.Cmd(_tx, "INSERT INTO fighter_accolades (fighter_id, ordinal, date, title) VALUES ($f,$o,$d,$t)");

        int fighterOrd = 0;
        foreach (var f in w.Fighters.Values)
        {
            cmd.Parameters.Clear();
            cmd.Bind("$id", f.Id.Value);
            cmd.Bind("$ordinal", fighterOrd++);
            cmd.Bind("$first_name", f.FirstName);
            cmd.Bind("$last_name", f.LastName);
            cmd.Bind("$nickname", Db.Text(f.Nickname));
            cmd.Bind("$persona", Db.Text(f.PersonaTag));
            cmd.Bind("$bio", Db.Text(f.Bio));
            cmd.Bind("$is_designed", f.Designed ? 1 : 0);
            cmd.Bind("$is_local", f.LocalTalent ? 1 : 0);
            cmd.Bind("$home_city", Db.IdN(f.HomeCity?.Value));
            cmd.Bind("$country_id", f.Country.Value);
            cmd.Bind("$birth_date", Db.Date(f.BirthDate));
            cmd.Bind("$sex", f.Sex.ToString());
            cmd.Bind("$nationality", f.Nationality.ToString());
            cmd.Bind("$stance", f.Stance.ToString());
            cmd.Bind("$height_cm", f.HeightCm);
            cmd.Bind("$reach_cm", f.ReachCm);
            cmd.Bind("$natural_weight_lbs", f.NaturalWeightLbs);
            cmd.Bind("$weight_class", f.WeightClass.ToString());
            cmd.Bind("$primary_style", f.PrimaryStyle.ToString());
            cmd.Bind("$potential_ability", f.PotentialAbility.Value);
            cmd.Bind("$status", f.Status.ToString());
            cmd.Bind("$morale", f.Morale.Value);
            cmd.Bind("$momentum", f.Momentum);
            cmd.Bind("$suspended_until", Db.DateN(f.SuspendedUntil));
            cmd.Bind("$wins", f.Record.Wins);
            cmd.Bind("$losses", f.Record.Losses);
            cmd.Bind("$draws", f.Record.Draws);
            cmd.Bind("$no_contests", f.Record.NoContests);
            cmd.Bind("$wins_ko", f.Record.WinsByKo);
            cmd.Bind("$wins_sub", f.Record.WinsBySubmission);
            cmd.Bind("$wins_dec", f.Record.WinsByDecision);
            cmd.Bind("$streak", f.Record.CurrentStreak);

            if (f.Contract is { } k)
            {
                cmd.Bind("$contract_promotion", k.Promotion.Value);
                cmd.Bind("$contract_signed_on", Db.Date(k.SignedOn));
                cmd.Bind("$contract_type", k.Type.ToString());
                cmd.Bind("$purse", Db.Cash(k.PursePerFight));
                cmd.Bind("$win_bonus", Db.Cash(k.WinBonus));
                cmd.Bind("$fights_remaining", k.FightsRemaining);
                cmd.Bind("$is_exclusive", k.IsExclusive ? 1 : 0);
                cmd.Bind("$has_champions_clause", k.HasChampionsClause ? 1 : 0);
                cmd.Bind("$signing_bonus", Db.Cash(k.SigningBonus));
                cmd.Bind("$ppv_points", k.PpvPoints);
                cmd.Bind("$performance_bonus", Db.Cash(k.PerformanceBonus));
                cmd.Bind("$guaranteed", k.Guaranteed ? 1 : 0);
                cmd.Bind("$poach_warned", k.PoachWarned ? 1 : 0);
            }
            else
            {
                foreach (var p in new[] { "$contract_promotion", "$contract_signed_on", "$contract_type", "$purse", "$win_bonus", "$fights_remaining", "$is_exclusive", "$has_champions_clause", "$signing_bonus", "$ppv_points", "$performance_bonus", "$guaranteed", "$poach_warned" })
                    cmd.Bind(p, DBNull.Value);
            }

            if (f.CurrentInjury is { } inj)
            {
                cmd.Bind("$injury_severity", inj.Severity.ToString());
                cmd.Bind("$injury_desc", inj.Description);
                cmd.Bind("$injury_occurred", Db.Date(inj.OccurredOn));
                cmd.Bind("$injury_return", Db.Date(inj.ExpectedReturn));
            }
            else
            {
                foreach (var p in new[] { "$injury_severity", "$injury_desc", "$injury_occurred", "$injury_return" })
                    cmd.Bind(p, DBNull.Value);
            }

            cmd.Bind("$ready_to_fight_on", Db.DateN(f.ReadyToFightOn));
            cmd.Bind("$loyalty", f.Loyalty);
            cmd.Bind("$marketing_push", f.MarketingPush ? 1 : 0);
            cmd.Bind("$camp_id", Db.IdN(f.Camp?.Value));
            cmd.Bind("$camp_stipend", f.CampStipend ? 1 : 0);
            cmd.Bind("$star_power", f.StarPower ? 1 : 0);
            cmd.Bind("$discovered_by", Db.IdN(f.DiscoveredBy?.Value));
            cmd.Bind("$disposition", f.Disposition.ToString());
            cmd.Bind("$origin_country", Db.Text(f.OriginCountryCode));

            foreach (var r in FighterColumns.Ratings)
                cmd.Bind("$" + r.Column, r.Get(f));

            cmd.ExecuteNonQuery();

            for (int i = 0; i < f.BoutHistory.Count; i++)
            {
                bouts.Parameters.Clear();
                bouts.Bind("$f", f.Id.Value);
                bouts.Bind("$o", i);
                bouts.Bind("$b", f.BoutHistory[i].Value);
                bouts.ExecuteNonQuery();
            }

            foreach (var kv in f.CityReputation)
            {
                rrep.Parameters.Clear();
                rrep.Bind("$f", f.Id.Value);
                rrep.Bind("$r", kv.Key.Value);
                rrep.Bind("$v", kv.Value.Value);
                rrep.ExecuteNonQuery();
            }

            for (int i = 0; i < f.Accolades.Count; i++)
            {
                acc.Parameters.Clear();
                acc.Bind("$f", f.Id.Value);
                acc.Bind("$o", i);
                acc.Bind("$d", Db.Date(f.Accolades[i].Date));
                acc.Bind("$t", f.Accolades[i].Title);
                acc.ExecuteNonQuery();
            }
        }
    }

    private void WritePromotions(World w)
    {
        using var promo = _c.Cmd(_tx,
            "INSERT INTO promotions (id, ordinal, name, abbreviation, base_region, is_player, balance, prestige, reputation, defunct, home_city, round_minutes, standard_rounds, title_rounds, fotn_bonus, prestige_hundredths, milestones, marketing_budget, tier_clearance, promotion_offered, eligible_since, sponsored_camp, face_fighter, lore, stream_name, stream_launched, stream_subs, courted_by, last_cleared_tier, last_cleared_on, owner_name, owner_note, founded_year) VALUES ($id,$ord,$n,$a,$r,$p,$b,$pr,$rep,$def,$hc,$rm,$sr,$tr,$fotn,$ph,$ms,$mb,$tc,$po,$es,$sc,$ff,$lore,$stn,$stl,$sts,$cb,$lct,$lco,$own,$ownn,$fy)");
        using var pop = _c.Cmd(_tx,
            "INSERT INTO promotion_city_pop (promotion_id, city_id, rating) VALUES ($p,$r,$v)");
        using var div = _c.Cmd(_tx,
            "INSERT INTO divisions (promotion_id, ordinal, weight_class, title_id, sex) VALUES ($p,$o,$w,$t,$sx)");
        using var rank = _c.Cmd(_tx,
            "INSERT INTO division_rankings (promotion_id, weight_class, sex, position, fighter_id) VALUES ($p,$w,$sx,$pos,$f)");
        using var ledger = _c.Cmd(_tx,
            "INSERT INTO ledger_entries (promotion_id, ordinal, date, category, amount, description) VALUES ($p,$o,$d,$c,$a,$desc)");
        using var sponsor = _c.Cmd(_tx,
            "INSERT INTO sponsor_deals (promotion_id, ordinal, partner, fee_per_event, events_remaining, requirements, strikes, signed_on) VALUES ($p,$o,$pt,$fee,$er,$req,$st,$s)");
        using var media = _c.Cmd(_tx,
            "INSERT INTO media_deals (promotion_id, ordinal, kind, partner, fee_per_event, months_remaining, requirements, strikes, signed_on, last_aired) VALUES ($p,$o,$k,$pt,$fee,$mr,$req,$st,$s,$la)");
        using var offer = _c.Cmd(_tx,
            "INSERT INTO deal_offers (promotion_id, ordinal, media_kind, partner, fee_per_event, term, requirements, offered_on, expires_on) VALUES ($p,$o,$k,$pt,$fee,$t,$req,$on,$ex)");

        int promoOrd = 0;
        foreach (var p in w.Promotions.Values)
        {
            promo.Parameters.Clear();
            promo.Bind("$id", p.Id.Value);
            promo.Bind("$ord", promoOrd++);
            promo.Bind("$n", p.Name);
            promo.Bind("$a", p.Abbreviation);
            promo.Bind("$r", p.BaseRegion.ToString());
            promo.Bind("$p", p.IsPlayerControlled ? 1 : 0);
            promo.Bind("$b", Db.Cash(p.Balance));
            promo.Bind("$pr", p.Prestige.Value);
            promo.Bind("$rep", p.Reputation.Value);
            promo.Bind("$def", p.Defunct ? 1 : 0);
            promo.Bind("$hc", Db.IdN(p.HomeCity?.Value));
            promo.Bind("$rm", p.Rules.RoundMinutes);
            promo.Bind("$sr", p.Rules.StandardRounds);
            promo.Bind("$tr", p.Rules.TitleRounds);
            promo.Bind("$fotn", Db.Cash(p.Rules.FightOfNightBonus));
            promo.Bind("$ph", p.PrestigeHundredths);
            promo.Bind("$ms", string.Join("|", p.Milestones));
            promo.Bind("$mb", Db.Cash(p.MarketingBudget));
            promo.Bind("$tc", p.TierClearance.ToString());
            promo.Bind("$po", p.PromotionOffered ? 1 : 0);
            promo.Bind("$es", Db.DateN(p.EligibleSince));
            promo.Bind("$sc", Db.IdN(p.SponsoredCamp?.Value));
            promo.Bind("$ff", Db.IdN(p.FaceOfCompany?.Value));
            promo.Bind("$lore", Db.Text(p.Lore));
            promo.Bind("$own", Db.Text(p.OwnerName));
            promo.Bind("$ownn", Db.Text(p.OwnerNote));
            promo.Bind("$fy", p.FoundedYear);
            promo.Bind("$stn", Db.Text(p.Stream?.Name));
            promo.Bind("$stl", Db.DateN(p.Stream?.LaunchedOn));
            promo.Bind("$sts", p.Stream?.Subscribers ?? 0);
            promo.Bind("$cb", string.Join("|", p.CourtedBy.OrderBy(x => x)));
            promo.Bind("$lct", Db.Text(p.LastClearedTier?.ToString()));
            promo.Bind("$lco", Db.DateN(p.LastClearedOn));
            promo.ExecuteNonQuery();

            foreach (var kv in p.PopularityByCity)
            {
                pop.Parameters.Clear();
                pop.Bind("$p", p.Id.Value);
                pop.Bind("$r", kv.Key.Value);
                pop.Bind("$v", kv.Value.Value);
                pop.ExecuteNonQuery();
            }

            for (int i = 0; i < p.Divisions.Count; i++)
            {
                var d = p.Divisions[i];
                div.Parameters.Clear();
                div.Bind("$p", p.Id.Value);
                div.Bind("$o", i);
                div.Bind("$w", d.WeightClass.ToString());
                div.Bind("$t", Db.IdN(d.Title?.Value));
                div.Bind("$sx", d.Sex.ToString());
                div.ExecuteNonQuery();

                for (int pos = 0; pos < d.Rankings.Count; pos++)
                {
                    rank.Parameters.Clear();
                    rank.Bind("$p", p.Id.Value);
                    rank.Bind("$w", d.WeightClass.ToString());
                    rank.Bind("$sx", d.Sex.ToString());
                    rank.Bind("$pos", pos);
                    rank.Bind("$f", d.Rankings[pos].Value);
                    rank.ExecuteNonQuery();
                }
            }

            for (int i = 0; i < p.Ledger.Count; i++)
            {
                var le = p.Ledger[i];
                ledger.Parameters.Clear();
                ledger.Bind("$p", p.Id.Value);
                ledger.Bind("$o", i);
                ledger.Bind("$d", Db.Date(le.Date));
                ledger.Bind("$c", le.Category.ToString());
                ledger.Bind("$a", Db.Cash(le.Amount));
                ledger.Bind("$desc", le.Description);
                ledger.ExecuteNonQuery();
            }

            for (int i = 0; i < p.SponsorDeals.Count; i++)
            {
                var dl = p.SponsorDeals[i];
                sponsor.Parameters.Clear();
                sponsor.Bind("$p", p.Id.Value);
                sponsor.Bind("$o", i);
                sponsor.Bind("$pt", dl.Partner);
                sponsor.Bind("$fee", Db.Cash(dl.FeePerEvent));
                sponsor.Bind("$er", dl.EventsRemaining);
                sponsor.Bind("$req", Db.Reqs(dl.Requirements));
                sponsor.Bind("$st", dl.Strikes);
                sponsor.Bind("$s", Db.Date(dl.SignedOn));
                sponsor.ExecuteNonQuery();
            }

            for (int i = 0; i < p.MediaDeals.Count; i++)
            {
                var dl = p.MediaDeals[i];
                media.Parameters.Clear();
                media.Bind("$p", p.Id.Value);
                media.Bind("$o", i);
                media.Bind("$k", dl.Kind.ToString());
                media.Bind("$pt", dl.Partner);
                media.Bind("$fee", Db.Cash(dl.RightsFeePerEvent));
                media.Bind("$mr", dl.MonthsRemaining);
                media.Bind("$req", Db.Reqs(dl.Requirements));
                media.Bind("$st", dl.Strikes);
                media.Bind("$s", Db.Date(dl.SignedOn));
                media.Bind("$la", Db.DateN(dl.LastAiredOn));
                media.ExecuteNonQuery();
            }

            for (int i = 0; i < p.Offers.Count; i++)
            {
                var of = p.Offers[i];
                offer.Parameters.Clear();
                offer.Bind("$p", p.Id.Value);
                offer.Bind("$o", i);
                offer.Bind("$k", of.Media is { } mk ? mk.ToString() : DBNull.Value);
                offer.Bind("$pt", of.Partner);
                offer.Bind("$fee", Db.Cash(of.FeePerEvent));
                offer.Bind("$t", of.Term);
                offer.Bind("$req", Db.Reqs(of.Requirements));
                offer.Bind("$on", Db.Date(of.OfferedOn));
                offer.Bind("$ex", Db.Date(of.ExpiresOn));
                offer.ExecuteNonQuery();
            }
        }
    }

    private void WriteTitles(World w)
    {
        using var title = _c.Cmd(_tx,
            "INSERT INTO titles (id, ordinal, promotion_id, weight_class, name, current_champion, prestige) VALUES ($id,$ord,$p,$w,$n,$c,$pr)");
        using var reign = _c.Cmd(_tx,
            "INSERT INTO title_reigns (title_id, ordinal, champion, won_on, lost_on, defenses) VALUES ($t,$o,$c,$won,$lost,$d)");

        int titleOrd = 0;
        foreach (var t in w.Titles.Values)
        {
            title.Parameters.Clear();
            title.Bind("$id", t.Id.Value);
            title.Bind("$ord", titleOrd++);
            title.Bind("$p", t.Promotion.Value);
            title.Bind("$w", t.WeightClass.ToString());
            title.Bind("$n", t.Name);
            title.Bind("$c", Db.IdN(t.CurrentChampion?.Value));
            title.Bind("$pr", t.Prestige.Value);
            title.ExecuteNonQuery();

            for (int i = 0; i < t.Lineage.Count; i++)
            {
                var reg = t.Lineage[i];
                reign.Parameters.Clear();
                reign.Bind("$t", t.Id.Value);
                reign.Bind("$o", i);
                reign.Bind("$c", reg.Champion.Value);
                reign.Bind("$won", Db.Date(reg.WonOn));
                reign.Bind("$lost", Db.DateN(reg.LostOn));
                reign.Bind("$d", reg.Defenses);
                reign.ExecuteNonQuery();
            }
        }
    }

    private void WriteEvents(World w)
    {
        using var ev = _c.Cmd(_tx,
            """
            INSERT INTO events (id, ordinal, promotion_id, name, date, venue, venue_id, region, tier, status,
                fin_present, attendance, gate, ppv_buys, ppv_rev, production, payroll, main_card, hype_bonus)
            VALUES ($id,$ord,$p,$n,$d,$v,$vid,$r,$tier,$s,$fp,$at,$g,$pb,$pr,$pc,$pay,$mc,$hb)
            """);
        using var fight = _c.Cmd(_tx,
            """
            INSERT INTO fights (id, event_id, ordinal, red, blue, weight_class, scheduled_rounds,
                is_title, title_id, catchweight, weight_miss, overturned, out_present, winner, loser, method, end_round, end_time, round_scores)
            VALUES ($id,$e,$o,$red,$blue,$w,$sr,$it,$t,$cw,$wm,$ot,$op,$win,$lose,$m,$er,$et,$rs)
            """);
        using var stat = _c.Cmd(_tx,
            """
            INSERT INTO fight_stats (fight_id, fighter_id, sig_landed, sig_att, td_landed, td_att, sub_att, kd, control)
            VALUES ($f,$fi,$sl,$sa,$tl,$ta,$su,$kd,$c)
            """);

        int eventOrd = 0;
        foreach (var e in w.Events.Values)
        {
            ev.Parameters.Clear();
            ev.Bind("$id", e.Id.Value);
            ev.Bind("$ord", eventOrd++);
            ev.Bind("$p", e.Promotion.Value);
            ev.Bind("$n", e.Name);
            ev.Bind("$d", Db.Date(e.Date));
            ev.Bind("$v", Db.Text(e.Venue));
            ev.Bind("$vid", Db.IdN(e.VenueId?.Value));
            ev.Bind("$r", e.Region.ToString());
            ev.Bind("$tier", e.Tier.ToString());
            ev.Bind("$s", e.Status.ToString());
            ev.Bind("$mc", e.MainCardFights);
            ev.Bind("$hb", e.HypeBonus);
            if (e.Financials is { } fin)
            {
                ev.Bind("$fp", 1);
                ev.Bind("$at", fin.Attendance);
                ev.Bind("$g", Db.Cash(fin.GateRevenue));
                ev.Bind("$pb", fin.PayPerViewBuys);
                ev.Bind("$pr", Db.Cash(fin.PayPerViewRevenue));
                ev.Bind("$pc", Db.Cash(fin.ProductionCost));
                ev.Bind("$pay", Db.Cash(fin.FighterPayroll));
            }
            else
            {
                ev.Bind("$fp", 0);
                foreach (var p in new[] { "$at", "$g", "$pb", "$pr", "$pc", "$pay" }) ev.Bind(p, DBNull.Value);
            }
            ev.ExecuteNonQuery();

            for (int i = 0; i < e.Card.Count; i++)
            {
                var f = e.Card[i];
                fight.Parameters.Clear();
                fight.Bind("$id", f.Id.Value);
                fight.Bind("$e", e.Id.Value);
                fight.Bind("$o", i);
                fight.Bind("$red", f.RedCorner.Value);
                fight.Bind("$blue", f.BlueCorner.Value);
                fight.Bind("$w", f.WeightClass.ToString());
                fight.Bind("$sr", f.ScheduledRounds);
                fight.Bind("$it", f.IsTitleFight ? 1 : 0);
                fight.Bind("$t", Db.IdN(f.Title?.Value));
                fight.Bind("$cw", f.CatchweightLbs is { } cw ? cw : DBNull.Value);
                fight.Bind("$wm", Db.IdN(f.WeightMissBy?.Value));
                fight.Bind("$ot", f.OverturnedNc ? 1 : 0);
                if (f.Outcome is { } o)
                {
                    fight.Bind("$op", 1);
                    fight.Bind("$win", Db.IdN(o.Winner?.Value));
                    fight.Bind("$lose", Db.IdN(o.Loser?.Value));
                    fight.Bind("$m", o.Method.ToString());
                    fight.Bind("$er", o.EndRound);
                    fight.Bind("$et", o.EndTimeSeconds);
                    fight.Bind("$rs", Db.Scores(o.RoundScores));
                }
                else
                {
                    fight.Bind("$op", 0);
                    foreach (var p in new[] { "$win", "$lose", "$m", "$er", "$et", "$rs" }) fight.Bind(p, DBNull.Value);
                }
                fight.ExecuteNonQuery();

                if (f.Outcome is { } outcome)
                {
                    foreach (var kv in outcome.Stats)
                    {
                        var s = kv.Value;
                        stat.Parameters.Clear();
                        stat.Bind("$f", f.Id.Value);
                        stat.Bind("$fi", kv.Key.Value);
                        stat.Bind("$sl", s.SignificantStrikesLanded);
                        stat.Bind("$sa", s.SignificantStrikesAttempted);
                        stat.Bind("$tl", s.TakedownsLanded);
                        stat.Bind("$ta", s.TakedownsAttempted);
                        stat.Bind("$su", s.SubmissionAttempts);
                        stat.Bind("$kd", s.KnockdownsScored);
                        stat.Bind("$c", s.ControlTimeSeconds);
                        stat.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
