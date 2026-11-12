using Microsoft.Data.Sqlite;

namespace MmaManager.Persistence;

/// <summary>
/// The save-file schema. Top-level entities and their queryable dimensions
/// (weight class, promotion, status, record) are real columns; the 36 fighter
/// ratings are generated from <see cref="FighterColumns"/> so schema and mapping
/// stay in lock-step. Order-bearing lists (rankings, lineage, cards, bout
/// history) carry an ordinal so they round-trip exactly, and so do the entity
/// tables (fighters, promotions, ...): the sim iterates those dictionaries, so
/// the order they enumerate in is state and is restored as saved.
/// </summary>
internal static class SaveSchema
{
    public static void Create(SqliteConnection c, SqliteTransaction tx)
    {
        string ratingCols = string.Join(",\n            ",
            FighterColumns.Ratings.Select(r => $"{r.Column} INTEGER NOT NULL"));

        c.Exec(tx, $"""
            CREATE TABLE meta (
                save_version INTEGER NOT NULL,
                current_date TEXT NOT NULL,
                start_date TEXT NOT NULL,
                rng_seed TEXT NOT NULL,
                player_promotion INTEGER,
                owner_notes TEXT NOT NULL DEFAULT '',
                last_acquisition TEXT
            );

            CREATE TABLE id_counters (
                name TEXT NOT NULL, value INTEGER NOT NULL
            );

            CREATE TABLE shortlist (
                ordinal INTEGER NOT NULL, fighter_id INTEGER NOT NULL
            );

            CREATE TABLE promotions (
                id INTEGER PRIMARY KEY, ordinal INTEGER NOT NULL,
                name TEXT NOT NULL, abbreviation TEXT NOT NULL, base_region TEXT NOT NULL,
                is_player INTEGER NOT NULL, balance TEXT NOT NULL, prestige INTEGER NOT NULL,
                reputation INTEGER NOT NULL, defunct INTEGER NOT NULL DEFAULT 0, home_city INTEGER,
                round_minutes INTEGER NOT NULL DEFAULT 5, standard_rounds INTEGER NOT NULL DEFAULT 3,
                title_rounds INTEGER NOT NULL DEFAULT 5, fotn_bonus TEXT NOT NULL DEFAULT '0',
                prestige_hundredths INTEGER NOT NULL DEFAULT 0, milestones TEXT NOT NULL DEFAULT '',
                marketing_budget TEXT NOT NULL DEFAULT '0',
                tier_clearance TEXT NOT NULL DEFAULT 'International', promotion_offered INTEGER NOT NULL DEFAULT 0,
                eligible_since TEXT, sponsored_camp INTEGER, face_fighter INTEGER, lore TEXT,
                stream_name TEXT, stream_launched TEXT, stream_subs INTEGER NOT NULL DEFAULT 0,
                courted_by TEXT NOT NULL DEFAULT '',
                last_cleared_tier TEXT, last_cleared_on TEXT,
                owner_name TEXT, owner_note TEXT, founded_year INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE promotion_city_pop (
                promotion_id INTEGER NOT NULL, city_id INTEGER NOT NULL, rating INTEGER NOT NULL
            );

            CREATE TABLE ledger_entries (
                promotion_id INTEGER NOT NULL, ordinal INTEGER NOT NULL,
                date TEXT NOT NULL, category TEXT NOT NULL, amount TEXT NOT NULL, description TEXT NOT NULL
            );

            CREATE TABLE sponsor_deals (
                promotion_id INTEGER NOT NULL, ordinal INTEGER NOT NULL,
                partner TEXT NOT NULL, fee_per_event TEXT NOT NULL, events_remaining INTEGER NOT NULL,
                requirements TEXT NOT NULL DEFAULT '', strikes INTEGER NOT NULL DEFAULT 0, signed_on TEXT NOT NULL
            );

            CREATE TABLE media_deals (
                promotion_id INTEGER NOT NULL, ordinal INTEGER NOT NULL,
                kind TEXT NOT NULL, partner TEXT NOT NULL, fee_per_event TEXT NOT NULL,
                months_remaining INTEGER NOT NULL, requirements TEXT NOT NULL DEFAULT '',
                strikes INTEGER NOT NULL DEFAULT 0, signed_on TEXT NOT NULL, last_aired TEXT
            );

            CREATE TABLE deal_offers (
                promotion_id INTEGER NOT NULL, ordinal INTEGER NOT NULL,
                media_kind TEXT, partner TEXT NOT NULL, fee_per_event TEXT NOT NULL, term INTEGER NOT NULL,
                requirements TEXT NOT NULL DEFAULT '', offered_on TEXT NOT NULL, expires_on TEXT NOT NULL
            );

            CREATE TABLE divisions (
                promotion_id INTEGER NOT NULL, ordinal INTEGER NOT NULL,
                weight_class TEXT NOT NULL, title_id INTEGER, sex TEXT NOT NULL DEFAULT 'Male'
            );

            CREATE TABLE division_rankings (
                promotion_id INTEGER NOT NULL, weight_class TEXT NOT NULL, sex TEXT NOT NULL DEFAULT 'Male',
                position INTEGER NOT NULL, fighter_id INTEGER NOT NULL
            );

            CREATE TABLE titles (
                id INTEGER PRIMARY KEY, ordinal INTEGER NOT NULL, promotion_id INTEGER NOT NULL,
                weight_class TEXT NOT NULL, name TEXT NOT NULL, current_champion INTEGER,
                prestige INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE title_reigns (
                title_id INTEGER NOT NULL, ordinal INTEGER NOT NULL, champion INTEGER NOT NULL,
                won_on TEXT NOT NULL, lost_on TEXT, defenses INTEGER NOT NULL
            );

            CREATE TABLE fighters (
                id INTEGER PRIMARY KEY, ordinal INTEGER NOT NULL,
                first_name TEXT NOT NULL, last_name TEXT NOT NULL, nickname TEXT,
                persona TEXT, bio TEXT, is_designed INTEGER NOT NULL DEFAULT 0, is_local INTEGER NOT NULL DEFAULT 0,
                home_city INTEGER, country_id INTEGER NOT NULL DEFAULT 0,
                birth_date TEXT NOT NULL, sex TEXT NOT NULL, nationality TEXT NOT NULL, stance TEXT NOT NULL,
                height_cm INTEGER NOT NULL, reach_cm INTEGER NOT NULL, natural_weight_lbs INTEGER NOT NULL,
                weight_class TEXT NOT NULL, primary_style TEXT NOT NULL, potential_ability INTEGER NOT NULL,
                status TEXT NOT NULL, morale INTEGER NOT NULL, momentum INTEGER NOT NULL,
                suspended_until TEXT,
                wins INTEGER NOT NULL, losses INTEGER NOT NULL, draws INTEGER NOT NULL, no_contests INTEGER NOT NULL,
                wins_ko INTEGER NOT NULL, wins_sub INTEGER NOT NULL, wins_dec INTEGER NOT NULL, streak INTEGER NOT NULL,
                contract_promotion INTEGER, contract_signed_on TEXT, contract_type TEXT,
                purse TEXT, win_bonus TEXT, fights_remaining INTEGER, is_exclusive INTEGER, has_champions_clause INTEGER,
                signing_bonus TEXT, ppv_points INTEGER, performance_bonus TEXT, guaranteed INTEGER, poach_warned INTEGER, bond_notice_on TEXT,
                window_ends_on TEXT, window_warned INTEGER,
                injury_severity TEXT, injury_desc TEXT, injury_occurred TEXT, injury_return TEXT,
                ready_to_fight_on TEXT, loyalty INTEGER NOT NULL, marketing_push INTEGER NOT NULL, camp_id INTEGER,
                camp_stipend INTEGER NOT NULL DEFAULT 0, star_power INTEGER NOT NULL DEFAULT 0,
                discovered_by INTEGER,
                disposition TEXT NOT NULL DEFAULT 'QuietProfessional',
                origin_country TEXT,
                {ratingCols}
            );

            CREATE TABLE fighter_accolades (
                fighter_id INTEGER NOT NULL, ordinal INTEGER NOT NULL,
                date TEXT NOT NULL, title TEXT NOT NULL
            );

            CREATE TABLE fighter_bouts (
                fighter_id INTEGER NOT NULL, ordinal INTEGER NOT NULL, fight_id INTEGER NOT NULL
            );

            CREATE TABLE fighter_city_rep (
                fighter_id INTEGER NOT NULL, city_id INTEGER NOT NULL, rating INTEGER NOT NULL
            );

            CREATE TABLE global_rankings (
                weight_class TEXT NOT NULL, fighters TEXT NOT NULL
            );

            CREATE TABLE pound_for_pound (
                ordinal INTEGER NOT NULL, fighter_id INTEGER NOT NULL
            );

            CREATE TABLE rivalries (
                ordinal INTEGER NOT NULL, fighter_a INTEGER NOT NULL, fighter_b INTEGER NOT NULL,
                intensity INTEGER NOT NULL, since TEXT NOT NULL
            );

            CREATE TABLE relationships (
                ordinal INTEGER NOT NULL, fighter_a INTEGER NOT NULL, fighter_b INTEGER NOT NULL,
                kind INTEGER NOT NULL, strength INTEGER NOT NULL, since TEXT NOT NULL, note TEXT
            );

            CREATE TABLE news (
                ordinal INTEGER NOT NULL, date TEXT NOT NULL, category TEXT NOT NULL,
                headline TEXT NOT NULL, body TEXT NOT NULL, promotion_id INTEGER NOT NULL,
                about_fighter INTEGER, about_event INTEGER
            );

            CREATE TABLE mail (
                ordinal INTEGER NOT NULL, date TEXT NOT NULL, category TEXT NOT NULL,
                sender TEXT NOT NULL, subject TEXT NOT NULL, body TEXT NOT NULL, is_read INTEGER NOT NULL,
                about_fighter INTEGER, about_event INTEGER
            );

            CREATE TABLE staff_market (
                ordinal INTEGER NOT NULL, role TEXT NOT NULL, name TEXT NOT NULL,
                quality INTEGER NOT NULL, salary TEXT NOT NULL, background TEXT
            );

            CREATE TABLE promotion_staff (
                promotion_id INTEGER NOT NULL, ordinal INTEGER NOT NULL, role TEXT NOT NULL,
                name TEXT NOT NULL, quality INTEGER NOT NULL, salary TEXT NOT NULL, background TEXT
            );

            CREATE TABLE objectives (
                promotion_id INTEGER NOT NULL, ordinal INTEGER NOT NULL, kind TEXT NOT NULL,
                description TEXT NOT NULL, target INTEGER NOT NULL,
                start TEXT NOT NULL, deadline TEXT NOT NULL, status TEXT NOT NULL
            );

            CREATE TABLE countries (
                id INTEGER PRIMARY KEY, ordinal INTEGER NOT NULL, name TEXT NOT NULL, code TEXT NOT NULL,
                continent TEXT NOT NULL, areas TEXT NOT NULL, mma_interest INTEGER NOT NULL, mma_interest_base INTEGER NOT NULL
            );

            CREATE TABLE cities (
                id INTEGER PRIMARY KEY, ordinal INTEGER NOT NULL, name TEXT NOT NULL, region TEXT NOT NULL, market INTEGER NOT NULL,
                country_id INTEGER NOT NULL, area INTEGER, mma_interest INTEGER NOT NULL, mma_interest_base INTEGER NOT NULL
            );

            CREATE TABLE venues (
                id INTEGER PRIMARY KEY, ordinal INTEGER NOT NULL, name TEXT NOT NULL, city_id INTEGER NOT NULL,
                capacity INTEGER NOT NULL, rent TEXT NOT NULL
            );

            CREATE TABLE camps (
                id INTEGER PRIMARY KEY, ordinal INTEGER NOT NULL, name TEXT NOT NULL, city_id INTEGER NOT NULL,
                specialty TEXT NOT NULL, reputation INTEGER NOT NULL,
                head_coach INTEGER, coach_quality INTEGER NOT NULL,
                capacity INTEGER NOT NULL, monthly_fee TEXT NOT NULL, lore TEXT
            );

            CREATE TABLE events (
                id INTEGER PRIMARY KEY, ordinal INTEGER NOT NULL, promotion_id INTEGER NOT NULL, name TEXT NOT NULL,
                date TEXT NOT NULL, venue TEXT, venue_id INTEGER, region TEXT NOT NULL, tier TEXT NOT NULL, status TEXT NOT NULL,
                fin_present INTEGER NOT NULL, attendance INTEGER, gate TEXT, ppv_buys INTEGER,
                ppv_rev TEXT, production TEXT, payroll TEXT, main_card INTEGER NOT NULL DEFAULT 0,
                hype_bonus REAL NOT NULL DEFAULT 0
            );

            CREATE TABLE fights (
                id INTEGER PRIMARY KEY, event_id INTEGER NOT NULL, ordinal INTEGER NOT NULL,
                red INTEGER NOT NULL, blue INTEGER NOT NULL, weight_class TEXT NOT NULL,
                scheduled_rounds INTEGER NOT NULL, is_title INTEGER NOT NULL, title_id INTEGER, catchweight INTEGER,
                weight_miss INTEGER, overturned INTEGER NOT NULL DEFAULT 0,
                out_present INTEGER NOT NULL, winner INTEGER, loser INTEGER,
                method TEXT, end_round INTEGER, end_time INTEGER,
                round_scores TEXT
            );

            CREATE TABLE decisions (
                id INTEGER PRIMARY KEY, kind TEXT NOT NULL, created TEXT NOT NULL, deadline TEXT NOT NULL,
                fighter INTEGER, detail TEXT NOT NULL DEFAULT '', sponsor TEXT NOT NULL DEFAULT '',
                promotion_id INTEGER, price TEXT NOT NULL DEFAULT '0',
                state TEXT NOT NULL
            );

            CREATE TABLE bidding_wars (
                ordinal INTEGER NOT NULL, fighter_id INTEGER NOT NULL, round INTEGER NOT NULL,
                current_bid TEXT NOT NULL, next_round_on TEXT NOT NULL, suitors TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE talks (
                ordinal INTEGER NOT NULL, fighter_id INTEGER NOT NULL, promotion_id INTEGER NOT NULL,
                demand_frac REAL NOT NULL, mood INTEGER NOT NULL, patience INTEGER NOT NULL, round INTEGER NOT NULL,
                soured_until TEXT, last_talked TEXT NOT NULL
            );

            CREATE TABLE fight_stats (
                fight_id INTEGER NOT NULL, fighter_id INTEGER NOT NULL,
                sig_landed INTEGER NOT NULL, sig_att INTEGER NOT NULL,
                td_landed INTEGER NOT NULL, td_att INTEGER NOT NULL,
                sub_att INTEGER NOT NULL, kd INTEGER NOT NULL, control INTEGER NOT NULL
            );
            """);
    }
}
