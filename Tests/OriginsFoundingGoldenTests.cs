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
/// build; recaptured from 29e733a after Origins phase 3, which changed only the three-year cases), so a reordered or added draw on the AI path turns this red. When founding is retuned ON PURPOSE (bank,
/// prestige, rules, roster), recapture: set FOUNDING_GOLDEN_OUT to a file path and run this class; it writes the
/// new values instead of asserting.
/// </summary>
public class OriginsFoundingGoldenTests
{
    private static readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["s1-day1"] = "2BD93F9B310891D5F64E",
        ["s1-years3-world"] = "31E7D4C581B7F25D65B3",
        ["s1-years3-house"] = "CE75E9DAD272678E4763",
        ["s2-day1"] = "476CA951BB60EA22993E",
        ["s2-years3-world"] = "046851F746B786EB7DBC",
        ["s2-years3-house"] = "BD1671698340A0FF504F",
        ["s3-day1"] = "97764D76DA032EF0D813",
        ["s3-years3-world"] = "F623ACF38A79EB6210E9",
        ["s3-years3-house"] = "F0A746BD3CC4A8CD54A2",
        ["s4-split"] = "8FB70477327FDD45C4E4",
        ["s5-split"] = "9EAA4A00161FB78D8BCE",
        ["s6-tryouts"] = "01B70CF1CA83175302CE",
        ["s7-tryouts"] = "289EBD790E3A2CF5881C",
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
