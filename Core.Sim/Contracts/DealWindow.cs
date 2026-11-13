using System.Runtime.CompilerServices;
using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// The player's deals really end. When one of the player's fighters fights the
/// last bout on his deal, the deal is over: for <see cref="WindowDays"/> days he
/// talks to nobody but his own promotion (the exclusive re-sign window — the AI
/// market, poaching and bidding wars keep their hands off, and nobody can book
/// him, the player included, until he re-signs), and the day the window closes
/// he is a free agent like any other. The player is told when the window opens
/// and warned in its last week. A champion keeps his belt through the window; if
/// it closes on him, he walks and the belt is vacant.
///
/// AI promotions' deals are untouched: their expiries resolve in the monthly
/// <see cref="ContractLifecycle.ProcessExpiries"/> pass as they always did.
/// Draws no RNG; everything is read off the contract and the date, so a save
/// mid-window reloads into the same window.
/// </summary>
public static class DealWindow
{
    /// <summary>How long the player has him to himself once the deal is fought out.</summary>
    public const int WindowDays = 30;

    /// <summary>The owner is warned when the window has this many days or fewer left.</summary>
    public const int WarnDays = 7;

    /// <summary>How long a fighter remembers where contract talks left off (see <see cref="NegotiationSession.Resume"/>).</summary>
    public const int TalksMemoryDays = 60;

    // tools/longsim runs its founded promotion as an AI org (PlayerPromotion is
    // unset so the world books its cards) but needs it to live under the
    // player's deal rules: a per-world opt-in, so parallel harness worlds never
    // see each other's setting. Never set by the game.
    private static readonly ConditionalWeakTable<World, StrongBox<PromotionId>> HarnessOrgs = new();

    /// <summary>Harness only: run <paramref name="promo"/>'s deals under the player's rules in this world.</summary>
    public static void HarnessPlayerRules(World world, PromotionId promo) =>
        HarnessOrgs.AddOrUpdate(world, new StrongBox<PromotionId>(promo));

    /// <summary>Whether this promotion's deals end into a re-sign window (the player's promotion; a harness stand-in).</summary>
    public static bool PlayerRules(World world, PromotionId promo) =>
        promo != default
        && (promo == world.PlayerPromotion || (HarnessOrgs.TryGetValue(world, out var box) && box.Value == promo));

    /// <summary>
    /// His deal with a player-rules promotion is fought out: no fights left. Until
    /// he re-signs (or the window closes and he walks) nobody books him and only
    /// his promotion talks to him.
    /// </summary>
    public static bool OutOfDeal(World world, Fighter f) =>
        f.Contract is { FightsRemaining: <= 0 } c && PlayerRules(world, c.Promotion);

    /// <summary>In his exclusive re-sign window: out of his deal and not retired.</summary>
    public static bool InWindow(World world, Fighter f) =>
        f.Status != FighterStatus.Retired && OutOfDeal(world, f);

    /// <summary>
    /// In his window, he walked out of talks and the door stays shut past the
    /// window's end: there will be no re-signing — he tests the market.
    /// </summary>
    public static bool TalksClosed(World world, Fighter f) =>
        InWindow(world, f) && f.Contract!.WindowEndsOn is { } end
        && NegotiationSession.SouredUntil(world, f.Id, f.Contract.Promotion) is { } until && until >= end;

    /// <summary>Days left to re-sign him (0 on the day it closes); null when no window is open.</summary>
    public static int? DaysLeft(World world, Fighter f) =>
        InWindow(world, f) && f.Contract!.WindowEndsOn is { } end
            ? Math.Max(0, end.DayNumber - world.CurrentDate.DayNumber) : null;

    /// <summary>
    /// After a card of a player-rules promotion is fought: everyone whose deal it
    /// finished has his window opened tonight (after any champion's clause has
    /// topped a winner's deal back up — he is not out of contract then).
    /// </summary>
    public static void AfterCard(World world, Event ev)
    {
        if (!PlayerRules(world, ev.Promotion)) return;
        foreach (var fight in ev.Card)
            foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                if (world.Get(id) is { } f && f.Contract is { WindowEndsOn: null } c
                    && c.Promotion == ev.Promotion && InWindow(world, f))
                    Open(world, f, ev.Date, tonight: ev.Id);
    }

    /// <summary>
    /// Weekly: open a window for any out-of-deal fighter who has none yet (a deal
    /// that reached zero some other way — an acquisition, a new game), warn in
    /// the last week, close the windows that have run out, and let talks that
    /// went cold be forgotten.
    /// </summary>
    public static void RunWeekly(World world, DateOnly date)
    {
        foreach (var f in world.Fighters.Values.ToList())   // id order (EntityMap)
        {
            if (!InWindow(world, f)) continue;
            var c = f.Contract!;
            if (c.WindowEndsOn is not { } end) { Open(world, f, date); continue; }
            if (date >= end) { Close(world, f, date); continue; }
            if (!c.WindowWarned && end.DayNumber - date.DayNumber <= WarnDays)
            {
                c.WindowWarned = true;
                if (c.Promotion == world.PlayerPromotion)
                {
                    // A rival's offer still standing decides where he goes at the close.
                    string? rival = OfferMarket.LiveFor(world, f.Id) is { } offer && offer.Holder == c.Promotion
                        ? world.Get(offer.From)?.Abbreviation : null;
                    string after = rival is not null
                        ? $"he signs with {rival} on the paper they put in front of him."
                        : "he's a free agent and anyone can have him.";
                    Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                        !TalksClosed(world, f) ? $"Last week to re-sign {f.FullName}"
                        : rival is not null ? $"{f.FullName} will sign with {rival}" : $"{f.FullName} will test the market",
                        Lifers.Gendered(f, TalksClosed(world, f)
                            ? $"{f.FullName} walked out of our talks and won't sit down again before his window closes on {end:d MMMM}. "
                              + (rival is not null ? $"Then {after}" : "Then he's a free agent and anyone can have him — us included.")
                            : $"{f.FullName}'s window closes on {end:d MMMM}. Re-sign him before then, or {after}"),
                        aboutFighter: f.Id);
                }
            }
        }
        ForgetColdTalks(world, date);
    }

    /// <summary>
    /// Open his window as of <paramref name="date"/>: off the player's cards (but
    /// for <paramref name="tonight"/>, the card that ended the deal and is being
    /// settled), and the owner is told.
    /// </summary>
    public static void Open(World world, Fighter f, DateOnly date, EventId? tonight = null)
    {
        if (f.Contract is not { } c || !InWindow(world, f) || c.WindowEndsOn is not null) return;
        var end = date.AddDays(WindowDays);
        c.WindowEndsOn = end;
        c.WindowWarned = false;
        if (c.Promotion != world.PlayerPromotion) return;

        // Nothing already booked hangs on a deal that has ended (the forward
        // calendar's single-booking rule means this is normally a no-op: the
        // bout that ended the deal was his only one). Same handling as any
        // fighter who becomes unavailable: the bout comes off, with a mail.
        foreach (var ev in world.Events.Values
                     .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == c.Promotion && e.Id != tonight
                                 && e.Card.Any(x => x.RedCorner == f.Id || x.BlueCorner == f.Id))
                     .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
                     .ToList())
            foreach (var fight in ev.Card.Where(x => x.RedCorner == f.Id || x.BlueCorner == f.Id).ToList())
                Living.ForwardCalendar.PullPlayerBout(world, ev, fight, f.Id,
                    Lifers.Gendered(f, "his deal is up — re-sign him to book him again"), date);

        string body = $"{f.FullName} has fought out his deal. For the next {WindowDays} days he talks to nobody but us — "
                    + $"no other promotion can sign him, and he can't be booked until he re-signs. On {end:d MMMM} he becomes "
                    + "a free agent and anyone can have him.";
        if (world.Titles.Values.Any(t => t.Promotion == c.Promotion && t.CurrentChampion == f.Id))
            body += " He keeps the belt until then; if he walks, it's vacant.";
        var bond = Lifers.BondOf(world, f);
        // A rival's offer already lodged on his last fight is the concrete version of
        // "rivals are circling": say whose paper it is and what happens at the close.
        var offer = OfferMarket.LiveFor(world, f.Id) is { } o && o.Holder == c.Promotion ? o : null;
        bool suitor = offer is null && PoachingService.HasSuitor(world, f);
        // A lifer who has soured (a standing grievance, or a bond already told as
        // broken / a rival already turned down on this deal) is said to be one plainly.
        bool soured = !bond.IsHolding && (bond.IsLifer || c.BondNoticeOn is not null);
        if (bond.IsHolding)
            body += " He wants to stay, and he'll do it at his hometown rate.";
        else if (soured)
            body += " He is no longer holding for us"
                  + (bond.Grievance is { } g ? $" — {g.Reason}" : "") + ": he'll listen to anyone."
                  + (suitor ? " Rivals are circling — they'll come for him the day he's free." : "");
        else if (suitor)
            body += " Bigger promotions are watching — they'll come for him the day he's free.";
        if (offer is not null && world.Get(offer.From) is { } rival)
            body += $" {rival.Name} has {offer.Purse} a fight for {offer.Fights} fights on the table for him: "
                  + $"if the window closes unsigned, that's where he goes. He won't re-sign for less than what beats it.";
        Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
            $"{f.FullName}'s deal is up — re-sign him by {end:MMM d}",
            Lifers.Gendered(f, body), aboutFighter: f.Id);
    }

    // The window ran out without a new deal: he walks to the open market.
    private static void Close(World world, Fighter f, DateOnly date)
    {
        var c = f.Contract!;
        var promo = world.Get(c.Promotion);
        string abbr = promo?.Abbreviation ?? "his promotion";
        ContractLifecycle.LeavePromotion(world, f, c.Promotion, date, $"{abbr} let his deal run out");
        f.Contract = null;
        if (c.Promotion == world.PlayerPromotion)
            Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                $"{f.FullName} is a free agent",
                Lifers.Gendered(f, $"His window closed without a new deal, so {f.FullName} is on the open market now. "
                    + "We can still talk to him — but so can everyone else."),
                aboutFighter: f.Id);
    }

    // Talks forgotten: two months cold and no walk-out still cooling, or the
    // fighter has retired. (No RNG; list order is kept.)
    private static void ForgetColdTalks(World world, DateOnly date) =>
        world.Talks.RemoveAll(t =>
            world.Get(t.Fighter) is not { Status: not FighterStatus.Retired }
            || (date.DayNumber - t.LastTalked.DayNumber > TalksMemoryDays && (t.SouredUntil is not { } s || s <= date)));
}
