using System.Security.Cryptography;
using System.Text;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// Golden fingerprints of the AI founding path (<see cref="OriginsFounding.Found"/>) in observer worlds: a house
/// forced open on fixed dates (day one; after the sport's rules and first weight lines; three years in) and the
/// whole world after three years with founding active. The constants were captured from the code as it was before
/// the player-founding refactor (main at 654adbc, its OriginsFounding/OriginsTimeline checked out into a scratch
/// build; recaptured from 29e733a after Origins phase 3, which changed only the three-year cases; recaptured again
/// for the Origins money tuning, PR #64: the newcomer bank's base fell 150k -> 50k, which changes every house's
/// balance and nothing else on the fixed-date cases (with a 150k base they reproduce the old values exactly), and
/// the young sport's money changes the three-year worlds; and for Origins tuning 2, where a founder's room and purses
/// follow the crowd it has seen, which changes only the three-year worlds and the houses in them; and on
/// world/perceived-potential, where a head coach picks his protégé on his read of the kids' ceilings, not their hidden
/// potential: only seed 2's three-year world and the house founded in it move, and with that one site put back on the
/// truth both come back exactly; and on world/origins-asks, where a man asks a smaller house less (Negotiator.HouseTier):
/// the three three-year worlds and their houses move, the day-one, split and tryout cases do not, and with the tier off
/// every key matches exactly, the market's read of the player's house moving none; and on world/origins-tier-reach,
/// where the founding roster signs at the house's tier price on the 250 step: s2-day1 and s4-split move through that
/// alone, s1-day1 and s3-day1 do not, the tryout cases cannot (tryout men sign on the floor purse), and the three-year
/// worlds and houses move through the founding price, the AI's 250-step opening offer and (seed 2) the stand-in's price;
/// with every reached path off, every key matches exactly), so a reordered or added draw on the AI path turns this red. When founding is retuned ON PURPOSE (bank,
/// prestige, rules, roster), recapture: set FOUNDING_GOLDEN_OUT to a file path and run this class; it writes the
/// new values instead of asserting.
/// </summary>
public class OriginsFoundingGoldenTests
{
    private static readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["s1-day1"] = "C0A6A81BAA1B8B9F71D2",
        ["s1-years3-world"] = "2220E362D0780BD218B5",
        ["s1-years3-house"] = "DBC44A3F507CADF4CE76",
        ["s2-day1"] = "1C4C138D05D3D7258B14",
        ["s2-years3-world"] = "4297DBD2828D15A59383",
        ["s2-years3-house"] = "C34AF59527BB4A2D5CE7",
        ["s3-day1"] = "8B18A7BC8DF1DC87464D",
        ["s3-years3-world"] = "9142C62C2F79D9AFD8B0",
        ["s3-years3-house"] = "646DF75C97B1967C3331",
        ["s4-split"] = "A81C21B23FC29399BFB3",
        ["s5-split"] = "F5E3833ECBD87E286F81",
        ["s6-tryouts"] = "9AB7EF497F834D5F857C",
        ["s7-tryouts"] = "04C06E974B508EC04CD2",
    };

    [Fact]
    public void AiFounding_MatchesTheCapturedFingerprints()
    {
        var actual = Capture();
        string? outPath = Environment.GetEnvironmentVariable("FOUNDING_GOLDEN_OUT");
        if (outPath is not null)
        {
            File.WriteAllLines(outPath, actual.Select(kv => $"        [\"{kv.Key}\"] = \"{Hash(kv.Value)}\","));
            File.WriteAllLines(outPath + ".full.txt", actual.Select(kv => $"{kv.Key}\n{kv.Value}\n"));
            return;
        }
        Assert.Equal(Expected.Keys.OrderBy(k => k), actual.Keys.OrderBy(k => k));
        foreach (var (key, text) in actual)
            Assert.True(Expected[key] == Hash(text), $"{key} differs from the captured AI founding:\n{text}");
    }

    private static Dictionary<string, string> Capture()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            var w = new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(seed));
            var day1 = OriginsFounding.Found(w, w.CurrentDate, null);
            map[$"s{seed}-day1"] = House(w, day1);
            new WorldTick().AdvanceWeeks(w, 52 * 3);
            map[$"s{seed}-years3-world"] = WorldPrint(w);
            var later = OriginsFounding.Found(w, w.CurrentDate, null);
            map[$"s{seed}-years3-house"] = House(w, later);
        }
        foreach (ulong seed in new ulong[] { 4, 5 })
        {
            var w = new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(seed));
            Era.Unlock(w, Capability.Rounds);
            Era.Unlock(w, Capability.Gloves);
            foreach (var wc in OriginsReforms.LinesOf(OriginsReforms.Ladder[0])) Era.Unlock(w, Capability.Class(wc));
            map[$"s{seed}-split"] = House(w, OriginsFounding.Found(w, w.CurrentDate, null));
        }
        // No free agent to sign: the house fills from open tryouts of local men (the tryout draws), open and split.
        foreach (ulong seed in new ulong[] { 6, 7 })
        {
            var w = new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(seed));
            foreach (var f in w.FreeAgents().ToList()) f.Status = FighterStatus.Retired;
            if (seed == 7)
            {
                Era.Unlock(w, Capability.Rounds);
                Era.Unlock(w, Capability.Gloves);
                foreach (var wc in OriginsReforms.LinesOf(OriginsReforms.Ladder[0])) Era.Unlock(w, Capability.Class(wc));
            }
            map[$"s{seed}-tryouts"] = House(w, OriginsFounding.Found(w, w.CurrentDate.AddDays(3), null));
        }
        return map;
    }

    private static string House(World w, Promotion? p)
    {
        if (p is null) return "none";
        var sb = new StringBuilder();
        sb.AppendLine($"{p.Id.Value} {p.Name} [{p.Abbreviation}] city {p.HomeCity?.Value} region {p.BaseRegion} y{p.FoundedYear} owner {p.OwnerName}");
        sb.AppendLine($"bank {p.Balance.Amount} prestige {p.PrestigeHundredths} rep {p.Reputation.Value} tier {p.TierClearance} format {p.Rules.ShowFormat}");
        sb.AppendLine($"rules {p.Rules.Fight} rm {p.Rules.RoundMinutes} sr {p.Rules.StandardRounds} tr {p.Rules.TitleRounds}");
        foreach (var d in p.Divisions)
            sb.AppendLine($"div {d.WeightClass} open {d.Open} lightest {d.Lightest} title {(d.Title is { } t ? w.Get(t)?.Name : "-")} ranks {string.Join(",", d.Rankings.Select(r => r.Value))}");
        foreach (var f in w.RosterOf(p.Id).OrderBy(f => f.Id.Value))
            sb.AppendLine($"man {f.Id.Value} {f.FirstName} {f.LastName} {f.OriginCountryCode} {f.WeightClass} purse {f.Contract!.PursePerFight.Amount} win {f.Contract.WinBonus.Amount} fights {f.Contract.FightsRemaining} disc {f.DiscoveredBy?.Value} camp {f.Camp?.Value}");
        foreach (var s in p.SponsorDeals)
            sb.AppendLine($"sponsor {s.Partner} {s.FeePerEvent.Amount} x{s.EventsRemaining} on {s.SignedOn}");
        foreach (var o in p.Offers)
            sb.AppendLine($"offer {o.Media} {o.Partner} {o.FeePerEvent.Amount} x{o.Term} till {o.ExpiresOn}");
        sb.AppendLine($"popularity {string.Join(",", p.PopularityByCity.OrderBy(kv => kv.Key.Value).Select(kv => $"{kv.Key.Value}:{kv.Value.Value}"))}");
        sb.AppendLine($"fighters {w.Fighters.Count} titles {w.Titles.Count} promos {w.Promotions.Count}");
        return sb.ToString();
    }

    private static string WorldPrint(World w)
    {
        var sb = new StringBuilder();
        foreach (var p in w.Promotions.Values.OrderBy(p => p.Id.Value))
            sb.AppendLine($"{p.Id.Value} {p.Name} [{p.Abbreviation}] city {p.HomeCity?.Value} y{p.FoundedYear} owner {p.OwnerName} bank {p.Balance.Amount} pr {p.PrestigeHundredths} defunct {p.Defunct} rules {p.Rules.Fight} divs {p.Divisions.Count}");
        foreach (var f in w.Fighters.Values.OrderBy(f => f.Id.Value))
            sb.Append($"{f.Id.Value}:{f.Record.Wins}-{f.Record.Losses}:{f.Contract?.Promotion.Value};");
        sb.AppendLine();
        foreach (var e in w.Events.Values.OrderBy(e => e.Id.Value))
            sb.Append($"{e.Id.Value}:{e.Promotion.Value}:{e.Date}:{e.Status}:{string.Join(",", e.Card.Select(c => c.Outcome?.Winner?.Value))};");
        sb.AppendLine();
        foreach (var c in w.Era.Chapters) sb.Append($"{c.Key}@{c.FiredOn}:{c.Variant}:{c.Promotion?.Value};");
        return sb.ToString();
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..20];
}
