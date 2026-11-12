using MmaManager.Core.Model;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// The owner's desk calls (Decisions) in words, shared by every surface that shows
/// them — Home's "Needs you", the Inbox letter and the Briefing — so a call reads
/// and confirms the same everywhere.
/// </summary>
public static class DeskCalls
{
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
            _ => "A decision for you",
        };
    }

    /// <summary>The line icon for the call (App.xaml icon set).</summary>
    public static string Icon(Decision d) => d.Kind switch
    {
        DecisionKind.TitleShotRequest => "Crown",
        DecisionKind.SponsorShowcase => "Money",
        DecisionKind.AcquisitionOffer => "Building",
        _ => "Warning",
    };

    /// <summary>
    /// What saying yes costs, for the calls that are expensive or destructive (buying a
    /// promotion, releasing a fighter): heading, body, detail and the confirm button.
    /// Null when yes needs no confirmation.
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
                    $"{who.FullName} comes off your roster and onto the free-agent market, where any promotion can sign {(who.Sex == Sex.Female ? "her" : "him")}.",
                    "The board gets its way, and the locker room notices how a name they respect was let go.",
                    "Release");
            default:
                return null;
        }
    }
}
