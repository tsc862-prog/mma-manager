using System.Text;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Persistence;

namespace MmaManager.Tests;

public class PersistenceTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsTheWholeWorld()
    {
        // A world with a season already run, so events/fights/outcomes/stats,
        // injuries, retirements and rankings are all populated.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 99 });
        new WorldTick().AdvanceWeeks(world, 30);

        // The owner's desk state (the Notes app) rides in the save too.
        world.OwnerNotes = "sign a heavyweight before summer\nwatch the CK strap";
        world.Shortlist.Add(world.Fighters.Keys.First());
        world.Shortlist.Add(world.Fighters.Keys.Skip(5).First());

        string path = Path.Combine(Path.GetTempPath(), $"mma_roundtrip_{Guid.NewGuid():N}.db");
        try
        {
            SaveGame.Save(world, path);
            var loaded = SaveGame.Load(path);

            // A full canonical dump must match exactly (play-by-play excepted, which
            // isn't persisted). This catches any field that fails to round-trip.
            Assert.Equal(Dump(world), Dump(loaded));
            Assert.Equal(world.OwnerNotes, loaded.OwnerNotes);
            Assert.Equal(world.Shortlist, loaded.Shortlist);

            // Id generation must resume above existing ids after a load.
            var fresh = loaded.NewFighterId();
            Assert.True(fresh.Value > loaded.Fighters.Keys.Max(k => k.Value) - 1);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void NewlyBuiltWorld_RoundTrips()
    {
        var world = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 3, RosterSize = 60 });
        string path = Path.Combine(Path.GetTempPath(), $"mma_new_{Guid.NewGuid():N}.db");
        try
        {
            SaveGame.Save(world, path);
            Assert.Equal(Dump(world), Dump(SaveGame.Load(path)));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // A deterministic, exhaustive text projection of everything we persist.
    private static string Dump(World w)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"meta {w.SaveVersion} {w.CurrentDate:yyyy-MM-dd} {w.RngSeed} player={w.PlayerPromotion.Value}");

        foreach (var f in w.Fighters.Values.OrderBy(f => f.Id.Value))
        {
            sb.Append($"F {f.Id.Value}|{f.FullName}|{f.BirthDate:yyyy-MM-dd}|{f.Nationality}|{f.Stance}|")
              .Append($"{f.HeightCm}|{f.ReachCm}|{f.WeightClass}|{f.PrimaryStyle}|PA{f.PotentialAbility.Value}|")
              .Append($"{f.Status}|mor{f.Morale.Value}|mom{f.Momentum}|loy{f.Loyalty}|susp{f.SuspendedUntil?.ToString("yyyy-MM-dd") ?? "-"}|rdy{f.ReadyToFightOn?.ToString("yyyy-MM-dd") ?? "-"}|disc{f.DiscoveredBy?.Value.ToString() ?? "-"}|")
              .Append($"rec {f.Record.Wins}-{f.Record.Losses}-{f.Record.Draws}-{f.Record.NoContests}/{f.Record.WinsByKo}-{f.Record.WinsBySubmission}-{f.Record.WinsByDecision}/str{f.Record.CurrentStreak}|");
            foreach (var rc in FighterRatingDump(f)) sb.Append(rc).Append(',');
            sb.Append('|');
            sb.Append(f.Contract is { } k
                ? $"K {k.Promotion.Value}/{k.SignedOn:yyyy-MM-dd}/{k.Type}/{k.PursePerFight.Amount}/{k.WinBonus.Amount}/{k.FightsRemaining}/{k.IsExclusive}/{k.HasChampionsClause}"
                  + $"/{k.SigningBonus.Amount}/{k.PpvPoints}/{k.PerformanceBonus.Amount}/{k.Guaranteed}"
                : "K -");
            sb.Append(f.CurrentInjury is { } inj
                ? $"|I {inj.Severity}/{inj.Description}/{inj.OccurredOn:yyyy-MM-dd}/{inj.ExpectedReturn:yyyy-MM-dd}"
                : "|I -");
            sb.Append("|bh ").Append(string.Join(",", f.BoutHistory.Select(b => b.Value)));
            sb.Append("|rr ").Append(string.Join(",", f.CityReputation.OrderBy(k => k.Key.Value).Select(k => $"{k.Key.Value}={k.Value.Value}")));
            sb.Append("|home ").Append(f.HomeCity?.Value ?? 0);
            sb.Append("|nat ").Append(f.Country.Value);
            sb.Append("|loc ").Append(f.LocalTalent ? 1 : 0);
            sb.Append("|bio ").Append(f.Bio ?? "-");
            sb.AppendLine();
        }

        foreach (var p in w.Promotions.Values.OrderBy(p => p.Id.Value))
        {
            sb.AppendLine($"P {p.Id.Value}|{p.Name}|{p.Abbreviation}|{p.BaseRegion}|{p.IsPlayerControlled}|{p.Balance.Amount}|{p.PrestigeHundredths}|rep{p.Reputation.Value}"
                + $"|rules {p.Rules.RoundMinutes}m/{p.Rules.StandardRounds}r/{p.Rules.TitleRounds}t/{p.Rules.FightOfNightBonus.Amount}"
                + $"|ms[{string.Join(",", p.Milestones.OrderBy(m => m))}]|lore {p.Lore ?? "-"}|elig {p.EligibleSince?.ToString("yyyy-MM-dd") ?? "-"}");
            foreach (var pop in p.PopularityByCity.OrderBy(k => k.Key.Value))
                sb.AppendLine($"  pop {pop.Key}={pop.Value.Value}");
            foreach (var d in p.Divisions)
                sb.AppendLine($"  div {d.WeightClass}/{d.Sex} title={d.Title?.Value.ToString() ?? "-"} ranks=[{string.Join(",", d.Rankings.Select(r => r.Value))}]");
            sb.AppendLine($"  titles=[{string.Join(",", p.Titles.Select(t => t.Value))}]");
            foreach (var le in p.Ledger)
                sb.AppendLine($"  L {le.Date:yyyy-MM-dd}/{le.Category}/{le.Amount.Amount}/{le.Description}");
            foreach (var dl in p.SponsorDeals)
                sb.AppendLine($"  D spons/{dl.Partner}/{dl.FeePerEvent.Amount}/{dl.EventsRemaining}/{dl.Strikes}"
                    + $"/[{string.Join(",", dl.Requirements.Select(r => $"{r.Kind}:{r.Threshold}"))}]/{dl.SignedOn:yyyy-MM-dd}");
            foreach (var dl in p.MediaDeals)
                sb.AppendLine($"  D {dl.Kind}/{dl.Partner}/{dl.RightsFeePerEvent.Amount}/{dl.MonthsRemaining}/{dl.Strikes}"
                    + $"/[{string.Join(",", dl.Requirements.Select(r => $"{r.Kind}:{r.Threshold}"))}]/{dl.SignedOn:yyyy-MM-dd}/{dl.LastAiredOn?.ToString("yyyy-MM-dd") ?? "-"}");
            foreach (var of in p.Offers)
                sb.AppendLine($"  OF {of.Media?.ToString() ?? "spons"}/{of.Partner}/{of.FeePerEvent.Amount}/{of.Term}"
                    + $"/[{string.Join(",", of.Requirements.Select(r => $"{r.Kind}:{r.Threshold}"))}]/{of.OfferedOn:yyyy-MM-dd}/{of.ExpiresOn:yyyy-MM-dd}");
            if (p.Stream is { } st)
                sb.AppendLine($"  ST {st.Name}/{st.LaunchedOn:yyyy-MM-dd}/{st.Subscribers}");
            if (p.CourtedBy.Count > 0)
                sb.AppendLine($"  CB [{string.Join(",", p.CourtedBy.OrderBy(x => x))}]");
            foreach (var s in p.Staff)
                sb.AppendLine($"  S {s.Role}/{s.Name}/{s.Quality}/{s.MonthlySalary.Amount}");
            foreach (var ob in p.Objectives)
                sb.AppendLine($"  O {ob.Kind}/{ob.Description}/{ob.Target}/{ob.Start:yyyy-MM-dd}/{ob.Deadline:yyyy-MM-dd}/{ob.Status}");
        }

        foreach (var t in w.Titles.Values.OrderBy(t => t.Id.Value))
        {
            sb.AppendLine($"T {t.Id.Value}|{t.Promotion.Value}|{t.WeightClass}|{t.Name}|champ={t.CurrentChampion?.Value.ToString() ?? "-"}|pr{t.Prestige.Value}");
            foreach (var reign in t.Lineage)
                sb.AppendLine($"  reign {reign.Champion.Value}/{reign.WonOn:yyyy-MM-dd}/{reign.LostOn?.ToString("yyyy-MM-dd") ?? "-"}/{reign.Defenses}");
        }

        foreach (var d in w.Decisions)
            sb.AppendLine($"DEC {d.Id}|{d.Kind}|{d.Created:yyyy-MM-dd}|{d.Deadline:yyyy-MM-dd}|{d.Fighter?.Value.ToString() ?? "-"}|{d.Detail}|{d.Sponsor}|{d.Promotion?.Value.ToString() ?? "-"}|{d.Price.Amount}|{d.State}");
        foreach (var bw in w.BiddingWars)
            sb.AppendLine($"WAR {bw.Fighter.Value}|r{bw.Round}|{bw.CurrentBid.Amount}|{bw.NextRoundOn:yyyy-MM-dd}|{string.Join(",", bw.Suitors.Select(s => s.Value))}");
        sb.AppendLine($"ACQ {w.LastAcquisitionOn?.ToString("yyyy-MM-dd") ?? "-"}");
        foreach (var n in w.News)
            sb.AppendLine($"N {n.Date:yyyy-MM-dd}|{n.Category}|{n.Headline}|{n.Body}|{n.Promotion.Value}");
        foreach (var m in w.Mail)
            sb.AppendLine($"M {m.Date:yyyy-MM-dd}|{m.Category}|{m.From}|{m.Subject}|{m.Body}|{m.Read}");
        foreach (var s in w.StaffMarket)
            sb.AppendLine($"SM {s.Role}|{s.Name}|{s.Quality}|{s.MonthlySalary.Amount}");
        foreach (var rv in w.Rivalries)
            sb.AppendLine($"RV {rv.A.Value}v{rv.B.Value}|{rv.Intensity}|{rv.Since:yyyy-MM-dd}");
        foreach (var k in w.Countries.Values.OrderBy(k => k.Id.Value))
            sb.AppendLine($"K {k.Id.Value}|{k.Name}|{k.Code}|{k.Continent}|[{string.Join("|", k.Areas)}]|mi{k.MmaInterest}/{k.MmaInterestBase}");
        foreach (var c in w.Cities.Values.OrderBy(c => c.Id.Value))
            sb.AppendLine($"C {c.Id.Value}|{c.Name}|{c.Region}|{c.Market}|{c.Country.Value}|{c.Area?.ToString() ?? "-"}|mi{c.MmaInterest}/{c.MmaInterestBase}");
        foreach (var v in w.Venues.Values.OrderBy(v => v.Id.Value))
            sb.AppendLine($"V {v.Id.Value}|{v.Name}|{v.City.Value}|{v.Capacity}|{v.Rent.Amount}");
        foreach (var g in w.Camps.Values.OrderBy(g => g.Id.Value))
            sb.AppendLine($"G {g.Id.Value}|{g.Name}|{g.City.Value}|{g.Specialty}|{g.Reputation}|{g.HeadCoach?.Value.ToString() ?? "-"}|{g.CoachQuality}|{g.Capacity}|{g.MonthlyFee.Amount}|{g.Lore ?? "-"}");

        foreach (var e in w.Events.Values.OrderBy(e => e.Id.Value))
        {
            sb.AppendLine($"E {e.Id.Value}|{e.Promotion.Value}|{e.Name}|{e.Date:yyyy-MM-dd}|{e.Venue ?? "-"}|{e.VenueId?.Value.ToString() ?? "-"}|{e.Region}|{e.Tier}|{e.Status}|mc{e.MainCardFights}|hype{e.HypeBonus:0.###}|"
                + (e.Financials is { } fin ? $"fin {fin.Attendance}/{fin.GateRevenue.Amount}/{fin.PayPerViewBuys}/{fin.PayPerViewRevenue.Amount}/{fin.ProductionCost.Amount}/{fin.FighterPayroll.Amount}" : "fin -"));
            foreach (var fight in e.Card)
            {
                sb.Append($"  fight {fight.Id.Value}|{fight.RedCorner.Value}v{fight.BlueCorner.Value}|{fight.WeightClass}|{fight.ScheduledRounds}|title={fight.IsTitleFight}/{fight.Title?.Value.ToString() ?? "-"}|cw={fight.CatchweightLbs?.ToString() ?? "-"}|wm={fight.WeightMissBy?.Value.ToString() ?? "-"}|ot={fight.OverturnedNc}|");
                sb.Append(fight.Outcome is { } o
                    ? $"O {o.Winner?.Value.ToString() ?? "-"}/{o.Loser?.Value.ToString() ?? "-"}/{o.Method}/R{o.EndRound}/{o.EndTimeSeconds}"
                    : "O -");
                if (fight.Outcome is { } oc)
                    foreach (var s in oc.Stats.OrderBy(k => k.Key.Value))
                        sb.Append($" [{s.Key.Value}:{s.Value.SignificantStrikesLanded}/{s.Value.SignificantStrikesAttempted}/{s.Value.TakedownsLanded}/{s.Value.TakedownsAttempted}/{s.Value.SubmissionAttempts}/{s.Value.KnockdownsScored}/{s.Value.ControlTimeSeconds}]");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static IEnumerable<int> FighterRatingDump(Fighter f) => new[]
    {
        f.Striking.PunchPower.Value, f.Striking.PunchTechnique.Value, f.Striking.KickPower.Value, f.Striking.KickTechnique.Value,
        f.Striking.ClinchStriking.Value, f.Striking.StrikingDefense.Value, f.Striking.Footwork.Value,
        f.Grappling.TakedownOffense.Value, f.Grappling.TakedownDefense.Value, f.Grappling.TopControl.Value, f.Grappling.BottomGame.Value,
        f.Grappling.SubmissionOffense.Value, f.Grappling.SubmissionDefense.Value, f.Grappling.Scrambling.Value, f.Grappling.ClinchControl.Value,
        f.Physical.Strength.Value, f.Physical.Cardio.Value, f.Physical.Speed.Value, f.Physical.Chin.Value, f.Physical.Recovery.Value, f.Physical.Flexibility.Value,
        f.Mental.FightIQ.Value, f.Mental.Composure.Value, f.Mental.Heart.Value, f.Mental.Discipline.Value,
        f.Intangibles.Popularity.Value, f.Intangibles.Charisma.Value,
        f.Tendencies.StrikingPreference.Value, f.Tendencies.Aggression.Value, f.Tendencies.Pace.Value, f.Tendencies.Counterfighting.Value, f.Tendencies.FinishInstinct.Value,
    };
}
