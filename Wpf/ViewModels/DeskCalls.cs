using MmaManager.Core.Model;
using MmaManager.Core.Sim.Contracts;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// The owner's desk calls (Decisions) in words, shared by every surface that shows
/// them — Home's "Needs you", the Inbox letter, the Planner and the Briefing — so a
/// call reads, labels its buttons and confirms the same everywhere.
/// </summary>
public static class DeskCalls
{
    private static string Rival(World w, Decision d) =>
        d.Promotion is { } rp && w.Get(rp) is { } rival ? rival.Abbreviation : "a rival";

    private static string Him(Fighter? f) => f?.Sex == Sex.Female ? "her" : "him";

    /// <summary>The call in one line, no full stop ("Kenji Morita wants a title shot").</summary>
    public static string Title(World w, Decision d)
    {
        var who = d.Fighter is { } fid ? w.Get(fid) : null;
        var target = d.Promotion is { } pid ? w.Get(pid) : null;
        return d.Kind switch
        {
            DecisionKind.TitleShotRequest => who is null ? "A contender wants a title shot" : $"{who.FirstName} {who.LastName} wants a title shot",
            DecisionKind.SponsorShowcase => $"{(d.Sponsor.Length > 0 ? d.Sponsor : "A sponsor")} wants a hometown showcase",
            DecisionKind.VeteranCut => who is null ? "The board wants a release" : $"The board wants {who.FirstName} {who.LastName} released",
            DecisionKind.AcquisitionOffer => target is null ? "A promotion is for sale" : $"{target.Name} is for sale",
            DecisionKind.RivalOffer => $"{(who is null ? "One of ours" : $"{who.FirstName} {who.LastName}")} has an offer from {Rival(w, d)}",
            _ => "A decision for you",
        };
    }

    /// <summary>The call as a calendar marker on its deadline (the Planner).</summary>
    public static string DueLine(World w, Decision d)
    {
        var who = d.Fighter is { } fid ? w.Get(fid) : null;
        return d.Kind switch
        {
            DecisionKind.TitleShotRequest => $"{who?.LastName ?? "A contender"} wants an answer on the title shot",
            DecisionKind.SponsorShowcase => $"{(d.Sponsor.Length > 0 ? d.Sponsor : "A sponsor")} wants a showcase answer",
            DecisionKind.VeteranCut => $"The board wants an answer on {who?.LastName ?? "a veteran"}",
            DecisionKind.AcquisitionOffer => $"{(d.Promotion is { } p && w.Get(p) is { } org ? org.Abbreviation : "A rival")} is for sale: answer due",
            DecisionKind.RivalOffer => $"{(d.Promotion is { } rp && w.Get(rp) is { } rv ? rv.Abbreviation : "A rival")} wants {who?.LastName ?? "one of ours"}: match or let go",
            _ => "A decision is due",
        };
    }

    /// <summary>The line icon for the call (App.xaml icon set).</summary>
    public static string Icon(Decision d) => d.Kind switch
    {
        DecisionKind.TitleShotRequest => "Crown",
        DecisionKind.SponsorShowcase or DecisionKind.RivalOffer => "Money",
        DecisionKind.AcquisitionOffer => "Building",
        _ => "Warning",
    };

    /// <summary>
    /// The yes button. <paramref name="short_"/> is Home's compact row ("Do it"); the
    /// long form is the Briefing's ("Grant the shot"). A rival offer always names the match price.
    /// </summary>
    public static string YesText(World w, Decision d, bool short_ = false)
    {
        var who = d.Fighter is { } fid ? w.Get(fid) : null;
        if (d.Kind == DecisionKind.RivalOffer) return $"Match ({d.Price}/fight)";
        if (short_) return "Do it";
        return d.Kind switch
        {
            DecisionKind.TitleShotRequest => "Grant the shot",
            DecisionKind.SponsorShowcase => "Agree to the showcase",
            DecisionKind.VeteranCut => who is null ? "Release him" : $"Release {who.LastName}",
            DecisionKind.AcquisitionOffer => d.Price.Amount > 0 ? $"Buy it — {d.Price}" : "Buy it",
            _ => "Do it",
        };
    }

    /// <summary>The no button.</summary>
    public static string NoText(World w, Decision d, bool short_ = false)
    {
        var who = d.Fighter is { } fid ? w.Get(fid) : null;
        if (d.Kind == DecisionKind.RivalOffer) return $"Let {Him(who)} go";
        if (short_) return "Pass";
        return d.Kind switch
        {
            DecisionKind.TitleShotRequest => "Tell them to earn it",
            DecisionKind.VeteranCut => $"Keep {Him(who)}",
            DecisionKind.AcquisitionOffer => "Let it go",
            _ => "Pass",
        };
    }

    /// <summary>
    /// What saying yes costs, for the calls that are expensive, destructive or binding
    /// (buying a promotion, releasing a fighter, matching a rival's offer): heading,
    /// body, detail and the confirm button. Null when yes needs no confirmation.
    /// </summary>
    public static (string Heading, string Body, string Detail, string Confirm)? YesCosts(World w, Decision d)
    {
        var player = w.Get(w.PlayerPromotion);
        switch (d.Kind)
        {
            case DecisionKind.AcquisitionOffer:
            {
                var target = d.Promotion is { } pid ? w.Get(pid) : null;
                string name = target?.Name ?? "the promotion";
                return ($"Buy {name}?",
                    $"{player?.Name ?? "You"} pays {d.Price} and takes over {name} — roster, brand and all.",
                    $"That money comes straight out of the bank ({player?.Balance} today). There's no selling it back.",
                    "Buy it");
            }
            case DecisionKind.VeteranCut when d.Fighter is { } fid && w.Get(fid) is { } who:
                return ($"Release {who.FullName}?",
                    $"{who.FullName} comes off your roster and onto the free-agent market, where any promotion can sign {Him(who)}.",
                    "The board gets its way, and the locker room notices how a name they respect was let go.",
                    "Release");
            case DecisionKind.RivalOffer when d.Fighter is { } oid && w.Get(oid) is { } f && OfferMarket.LiveFor(w, oid) is { } offer:
            {
                var m = OfferMarket.MatchTerms(w, offer);
                string rival = w.Get(offer.From)?.Abbreviation ?? "the rival";
                return ($"Match {rival} for {f.FullName}?",
                    $"{f.FullName} re-signs now: {MatchLine(m)}.",
                    $"{rival} offered {Terms(offer)}. The new deal replaces what is left of the current one"
                    + (m.SigningBonus.Amount > 0 ? $", and the signing bonus comes out of the bank today ({player?.Balance} now)." : "."),
                    "Match");
            }
            default:
                return null;
        }
    }

    /// <summary>The match deal in a line ("$9,500 a fight for 4 fights, $4,500 to win").</summary>
    public static string MatchLine(ContractOffer m) =>
        $"{m.PursePerFight} a fight for {m.Fights} fights"
        + (m.WinBonus.Amount > 0 ? $", {m.WinBonus} to win" : "")
        + (m.SigningBonus.Amount > 0 ? $", {m.SigningBonus} to sign" : "");

    /// <summary>A rival's paper in a line.</summary>
    public static string Terms(RivalOffer o) =>
        $"{o.Purse} a fight for {o.Fights} fights"
        + (o.WinBonus.Amount > 0 ? $", {o.WinBonus} to win" : "")
        + (o.SigningBonus.Amount > 0 ? $", {o.SigningBonus} to sign" : "")
        + (o.Exclusive ? "" : ", non-exclusive");

    // =====================================================================================
    // Rival offers on the free agents the player courts (RivalOfferKind.FreeAgent). There
    // is no Desk card and no Match: the owner beats it at the table (sit down with him) or
    // lets him go. The terms come out at the table, so these lines never quote them.
    // =====================================================================================

    /// <summary>Pending rival offers on free agents the player is courting, soonest deadline first.</summary>
    public static List<RivalOffer> Courted(World w) =>
        w.RivalOffers.Where(o => o.Kind == RivalOfferKind.FreeAgent && o.State == RivalOfferState.Pending
                                 && o.Holder == w.PlayerPromotion && w.Get(o.Fighter) is { Contract: null })
            .OrderBy(o => o.Deadline).ThenBy(o => o.Id).ToList();

    /// <summary>The player's pending free-agent offer for this fighter, if any.</summary>
    public static RivalOffer? CourtedOffer(World w, FighterId fighter) =>
        OfferMarket.LiveFor(w, fighter) is { Kind: RivalOfferKind.FreeAgent, State: RivalOfferState.Pending } o
        && o.Holder == w.PlayerPromotion ? o : null;

    private static string RivalOf(World w, RivalOffer o) => w.Get(o.From)?.Abbreviation ?? "A rival";

    /// <summary>The rival raised its offer this week (answering the owner's final offer in writing): its letter is today's.</summary>
    public static bool CameBackToday(World w, RivalOffer o) =>
        o.Improvements > 0 && w.Get(o.Fighter) is { } f
        && w.Mail.Any(m => m.Date == w.CurrentDate && m.AboutFighter == f.Id && m.Subject == $"{RivalOf(w, o)} came back for {f.FullName}");

    /// <summary>"NFC has made Kenji Morita an offer" (no full stop).</summary>
    public static string FreeAgentTitle(World w, RivalOffer o) =>
        $"{RivalOf(w, o)} has made {(w.Get(o.Fighter) is { } f ? $"{f.FirstName} {f.LastName}" : "a free agent")} an offer";

    /// <summary>"NFC has made Kenji Morita an offer — he signs it after 14 March unless you beat it."</summary>
    public static string FreeAgentLine(World w, RivalOffer o) => w.Get(o.Fighter) is { } f
        ? Lifers.Gendered(f, $"{FreeAgentTitle(w, o)} — he signs it after {o.Deadline:d MMMM} unless you beat it.")
        : FreeAgentTitle(w, o) + ".";

    /// <summary>The Planner's marker on the deadline.</summary>
    public static string FreeAgentDueLine(World w, RivalOffer o) =>
        $"{w.Get(o.Fighter)?.LastName ?? "A free agent"} signs with {RivalOf(w, o)} unless you beat it";

    /// <summary>"Sit down with him ▸" / "...her ▸".</summary>
    public static string SitDownText(Fighter? f) => $"Sit down with {Him(f)} ▸";

    /// <summary>"Let him go" / "Let her go".</summary>
    public static string LetGoText(Fighter? f) => $"Let {Him(f)} go";

    /// <summary>What letting a courted free agent go costs: heading, body, detail and the confirm button.</summary>
    public static (string Heading, string Body, string Detail, string Confirm) LetGoCosts(World w, RivalOffer o)
    {
        var f = w.Get(o.Fighter);
        string name = f?.FullName ?? "him";
        string rival = w.Get(o.From)?.Name ?? "The rival";
        string abbr = RivalOf(w, o);
        string body = $"You stop courting {name}. {rival} signs {Him(f)} on its paper when the week turns, if it still has the room and the money.";
        string detail = o.Final is not null
            ? $"Your offer in writing is withdrawn with it. There's no calling {Him(f)} back once {abbr} has {Him(f)}."
            : $"There's no calling {Him(f)} back once {abbr} has {Him(f)}.";
        return ($"Let {name} go to {abbr}?", body, detail, LetGoText(f));
    }
}
