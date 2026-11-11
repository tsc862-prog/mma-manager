using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// LIFERS — the fighters who stay. The talent treadmill is the game's core
/// tension (the giants take what you build), and it should stay that way for
/// the ordinary prospect. But a few fighters are bound to their promotion by
/// years of service, by temperament, by being the face on the posters — and
/// for them the owner's rule is: <b>a lifer holds, and breaks for a reason.</b>
///
/// Nothing here is a flag. Who counts is DERIVED from state the sim already
/// keeps — loyalty (authored or earned fight by fight, renewal by renewal,
/// bonus by bonus), a company-man disposition, the face of the company — so a
/// player's long-kept fighter becomes one, and one who is let down stops being
/// one. While he HOLDS:
/// <list type="bullet">
/// <item>his promotion fights to keep him: it renews him first at expiry, and
///   may stretch past its usual purse budget to do it (<see cref="RenewalStretch"/>);</item>
/// <item>he asks less of his own promotion (<see cref="HomeDiscount"/>) — the
///   same factor the AI's renewal and the player's negotiating room both read;</item>
/// <item>a rival's approach fails regardless of money — but the approach is
///   public (<see cref="TurnDown"/>).</item>
/// </list>
/// He BREAKS for a reason the player can read (<see cref="GrievanceOf"/>): his
/// promotion is in the red, he is passed over for the title shot, left on the
/// shelf, paid far below what the market has seen him be worth, or stuck on a
/// skid — and two player decisions break him outright (taking the posters off
/// him, stopping his camp stipend). A standing grievance opens the door at once
/// (he stops giving the hometown discount, he takes calls) and wears his
/// loyalty down month by month; fix it in time and he holds again, let it run
/// and the bond is gone. A break never teleports him: the normal market paths
/// — the poach, the expiry, free agency — are what then move him. No chance is
/// involved anywhere, so nothing here draws on any RNG stream.
/// </summary>
public static class Lifers
{
    /// <summary>The lifer bar on <see cref="Score"/>.</summary>
    public const int Threshold = 80;
    private const int CompanyManBonus = 10;
    private const int FaceBonus = 5;

    /// <summary>How far past its usual purse cap a solvent promotion will stretch to renew a lifer.</summary>
    public const decimal RenewalStretch = 2.5m;   // the same reach a room keeps in a bidding war

    // The hometown discount: what fraction of his ask a holding lifer wants from his own promotion.
    private const decimal CompanyManDiscount = 0.50m;
    private const decimal LiferDiscount = 0.60m;

    private const int RefusalStoryGapDays = 180;

    // Grievance thresholds.
    private const int BenchedDays = 180;            // the neglect decay starts at 120; this is the open door
    private const int PassedOverWindowDays = 365;
    private const int PassedOverTitleFights = 2;
    private const int UnderpaidAfterDays = 270;     // "sustained": most of a year on the deal
    private const decimal UnderpaidFraction = 0.35m;
    private const int SkidLength = 3;

    // --- Who is a lifer --------------------------------------------------------

    /// <summary>
    /// How bound a fighter is to the promotion he is signed to: his loyalty, plus
    /// a company man's devotion and the face of the company's billing. A mercenary
    /// never scores — he follows the money however long he has stayed.
    /// </summary>
    public static int Score(World world, Fighter f)
    {
        if (f.Disposition == Disposition.Mercenary || f.Contract is not { } c) return 0;
        return f.Loyalty
             + (f.Disposition == Disposition.CompanyMan ? CompanyManBonus : 0)
             + (world.Get(c.Promotion)?.FaceOfCompany == f.Id ? FaceBonus : 0);
    }

    /// <summary>The rule, in one place: an active, signed, non-mercenary fighter at or over the bar.</summary>
    public static bool IsLifer(World world, Fighter f) =>
        f.Status == FighterStatus.Active && f.Contract is not null && Score(world, f) >= Threshold;

    /// <summary>A lifer with nothing standing between him and his promotion — the protected state.</summary>
    public static bool IsHolding(World world, Fighter f) =>
        IsLifer(world, f) && GrievanceOf(world, f, world.CurrentDate) is null;

    /// <summary>
    /// Whether his own promotion gets the first call when his deal runs out — and
    /// his hometown terms. A holding lifer does; so does one whose only complaint
    /// is his PAY, because the renewal is exactly the raise that repairs it (an
    /// underpaid lifer takes rivals' calls, but his own house gets to fix it
    /// first). The other grievances — the red ink, the skipped title shot, the
    /// shelf, the skid — aren't solved by a new piece of paper.
    /// </summary>
    public static bool KeepsFirstCall(World world, Fighter f) =>
        IsLifer(world, f) && GrievanceOf(world, f, world.CurrentDate) is not { Kind: not GrievanceKind.Underpaid };

    /// <summary>
    /// What a fighter asks OF THIS PROMOTION, as a fraction of his market ask:
    /// a holding lifer takes meaningfully less to stay (a company man the most).
    /// 1 for everyone else — and for him too at any other promotion's table.
    /// </summary>
    public static decimal HomeDiscount(World world, Fighter f, PromotionId promo)
    {
        if (f.Contract?.Promotion != promo || !KeepsFirstCall(world, f)) return 1m;
        return f.Disposition == Disposition.CompanyMan ? CompanyManDiscount : LiferDiscount;
    }

    // --- Why a lifer breaks ------------------------------------------------------

    public enum GrievanceKind { OrgInTrouble, PassedOver, Benched, Underpaid, Skid }

    /// <summary>A standing grievance and its plain-language reason.</summary>
    public sealed record Grievance(GrievanceKind Kind, string Reason);

    /// <summary>
    /// The first standing reason this fighter has to doubt his promotion, or null.
    /// Every condition is read off the live world (the books, the rankings, the
    /// cards, the contract, the record), so it reproduces across save and load and
    /// clears itself the moment the promotion fixes it: back in the black, a title
    /// fight booked, a card, a raise, a win.
    /// </summary>
    public static Grievance? GrievanceOf(World world, Fighter f, DateOnly date) =>
        ReadGrievance(world, f, date) is { } g ? g with { Reason = Gendered(f, g.Reason) } : null;

    private static Grievance? ReadGrievance(World world, Fighter f, DateOnly date)
    {
        if (f.Contract is not { } c || world.Get(c.Promotion) is not { } org) return null;

        int distress = Solvency.Distress(org);
        if (distress > 0)
            return new(GrievanceKind.OrgInTrouble, distress >= 2
                ? $"{org.Abbreviation} is fighting for its life and can't promise him a future"
                : $"{org.Abbreviation} is in the red and can't promise him a future");

        // One pass over this promotion's cards: when he last fought here, whether
        // he is booked, and who has been getting title shots in his division.
        var div = org.Divisions.FirstOrDefault(d => d.Matches(f));
        var title = div?.Title is { } tid ? world.Get(tid) : null;
        DateOnly? lastFought = null, lastTitleFight = null;
        bool booked = false, bookedForTitle = false;
        int titleFightsWithoutHim = 0;
        var windowStart = date.AddDays(-PassedOverWindowDays);
        foreach (var ev in world.Events.Values)
        {
            if (ev.Promotion != org.Id) continue;
            bool done = ev.Status == EventStatus.Completed;
            bool upcoming = ev.Status == EventStatus.Scheduled;
            if (!done && !upcoming) continue;
            foreach (var fight in ev.Card)
            {
                bool his = fight.RedCorner == f.Id || fight.BlueCorner == f.Id;
                if (upcoming)
                {
                    if (his) { booked = true; bookedForTitle |= fight.IsTitleFight; }
                    continue;
                }
                if (his)
                {
                    if (lastFought is null || ev.Date > lastFought) lastFought = ev.Date;
                    if (fight.IsTitleFight && (lastTitleFight is null || ev.Date > lastTitleFight)) lastTitleFight = ev.Date;
                }
                else if (title is not null && fight.Title == title.Id && ev.Date > windowStart && ev.Date <= date)
                    titleFightsWithoutHim++;
            }
        }

        // Ranked #1-#2, the belt keeps changing hands or being defended without
        // him, and nobody has booked him a shot (the #2 has to watch one more go
        // by — somebody is always #2 while the #1 gets his turn).
        int rank = div?.Rankings.IndexOf(f.Id) ?? -1;
        if (div is not null && title?.CurrentChampion is { } champ && champ != f.Id && !bookedForTitle
            && rank is >= 0 and <= 1
            && titleFightsWithoutHim >= PassedOverTitleFights + rank
            && (lastTitleFight is null || lastTitleFight <= windowStart))
        {
            bool formerChamp = title.Lineage.Any(r => r.Champion == f.Id);
            return new(GrievanceKind.PassedOver, formerChamp
                ? $"the former champion, passed over {Times(titleFightsWithoutHim)} for a shot at the {title.Name}"
                : $"passed over {Times(titleFightsWithoutHim)} for the {title.Name} shot");
        }

        // On the shelf: healthy, unbooked, and unused for half a year.
        if (!booked && f.IsMatchReady(date))
        {
            int since = Math.Max(lastFought?.DayNumber ?? 0, c.SignedOn.DayNumber);
            if (f.ReadyToFightOn is { } ready) since = Math.Max(since, ready.DayNumber);
            int idle = date.DayNumber - since;
            if (idle >= BenchedDays)
            {
                int months = idle / 30;
                bool isChamp = world.Titles.Values.Any(t => t.Promotion == org.Id && t.CurrentChampion == f.Id);
                return new(GrievanceKind.Benched, isChamp
                    ? $"the champion hasn't been booked in {months} months"
                    : $"left on the shelf for {months} months");
            }
        }

        // Paid a fraction of what the market has SEEN him be worth, for most of a
        // year — the fog-of-war price, the one every promotion's table quotes.
        if (date.DayNumber - c.SignedOn.DayNumber >= UnderpaidAfterDays)
        {
            var ask = Negotiator.EffectiveAskingPurse(org, f, Valuation.Evaluate(world, f));
            if (c.PursePerFight.Amount < ask.Amount * UnderpaidFraction)
                return new(GrievanceKind.Underpaid,
                    $"paid {c.PursePerFight}/fight, barely a third of what the market now says he's worth");
        }

        if (f.Record.CurrentStreak <= -SkidLength)
            return new(GrievanceKind.Skid,
                $"{-f.Record.CurrentStreak} straight losses have him wondering whether he needs a fresh start");

        return null;
    }

    private static string Times(int n) => n == 2 ? "twice" : $"{n} times";

    /// <summary>
    /// The sim's sentences about a lifer are written "he/him/his"; a woman's
    /// story gets her own pronouns. (Applied to narration only — a fighter's
    /// quoted words are already in her own voice.)
    /// </summary>
    public static string Gendered(Fighter f, string text) =>
        f.Sex != Sex.Female ? text
        : System.Text.RegularExpressions.Regex.Replace(text, @"\b(he|He|him|his|His|himself)\b", m => m.Value switch
        {
            "he" => "she", "He" => "She", "him" => "her", "his" => "her", "His" => "Her", _ => "herself",
        });

    private static readonly FighterValuation Valuation = new();
    private static readonly Negotiator Negotiator = new();

    // --- Monthly: grievances wear the bond down -----------------------------------

    /// <summary>
    /// Monthly, before the expiry pass: every lifer with a standing grievance
    /// loses loyalty (a restless fighter faster, a company man slowest). The month
    /// the bond drops below the bar is the BREAK — told in the news and, for the
    /// player's fighter, on the owner's desk. The player also hears (at most every
    /// few months) when one of their lifers starts to doubt them, with the reason,
    /// while there is still time to fix it.
    /// </summary>
    public static void RunMonthly(World world, DateOnly date)
    {
        foreach (var f in world.Fighters.Values.OrderBy(f => f.Id.Value))
        {
            if (!IsLifer(world, f) || GrievanceOf(world, f, date) is not { } g) continue;
            var org = world.Get(f.Contract!.Promotion)!;
            bool mine = org.Id == world.PlayerPromotion;

            // An AI promotion fixes an underpaid lifer's money the way the player
            // can at the table: tear up the deal and re-sign him on a raise (with
            // the same stretch it would use at expiry). The player gets the mail
            // below and makes the call themselves.
            if (!mine && g.Kind == GrievanceKind.Underpaid && Solvency.Distress(org) == 0)
            {
                var exp = Valuation.Evaluate(world, f);
                var offer = Negotiator.AiOpeningOffer(world, org, f, exp, RenewalStretch);
                if (offer.PursePerFight.Amount > f.Contract.PursePerFight.Amount
                    && Negotiator.Evaluate(world, f, offer, exp).Verdict == NegotiationVerdict.Accept)
                {
                    ContractLifecycle.Sign(world, f, offer, date);
                    continue;
                }
            }

            int bite = f.Temperament.Ambition.Value >= 70 ? 3 : f.Temperament.Ambition.Value <= 30 ? 1 : 2;
            f.Loyalty = Math.Max(0, f.Loyalty - bite);

            if (Score(world, f) < Threshold)
            {
                // A lifer whose house can't fix it can heal and re-break (fights
                // rebuild loyalty, the grievance wears it back down): the headline
                // runs once a year at most.
                bool toldLately = world.News.Any(n => n.AboutFighter == f.Id && n.Headline.EndsWith(" is broken")
                                                      && date.DayNumber - n.Date.DayNumber < 365);
                if ((Notable(world, f) || mine) && !toldLately)
                    Living.News.Post(world, date, NewsCategory.Milestone,
                        $"{f.FullName}'s bond with {org.Abbreviation} is broken",
                        $"The {org.Abbreviation} lifer is listening to offers: {g.Reason}." + Voice(f, g.Kind, date),
                        org.Id, f.Id);
                if (mine)
                    Living.Mail.Post(world, date, MailCategory.BoardNote, Living.Mail.Owner(world),
                        $"We've lost {f.FullName}'s loyalty",
                        Gendered(f, $"{f.FullName} was one of ours for life. Not any more: {g.Reason}. He's a free agent in waiting — "
                        + "the next bigger promotion that calls will get a hearing. A retention bonus can still buy some of it back."),
                        aboutFighter: f.Id);
            }
            else if (mine && !RecentlyMailed(world, f, date, "is unhappy"))
            {
                Living.Mail.Post(world, date, MailCategory.FighterRequest, f.FullName,
                    $"{f.FullName} is unhappy",
                    Gendered(f, $"One of our lifers has a grievance: {g.Reason}. Until it's fixed he's taking calls, and he won't give us "
                    + "the hometown discount. Every month it stands wears his loyalty down.") + Voice(f, g.Kind, date),
                    aboutFighter: f.Id);
            }
        }
    }

    // --- The approach he turns down ----------------------------------------------

    /// <summary>
    /// A bigger promotion came calling and a holding lifer said no. A notable
    /// lifer's refusal makes the news; the player always hears when it's theirs —
    /// it's good news. Rate-limited off persisted state alone: told at most once
    /// per contract (the contract's poach-warning flag doubles as the "already
    /// told" mark, so a player's unsigned lifer isn't re-reported every month),
    /// and only on a deal at least <see cref="RefusalStoryGapDays"/> old — every
    /// approach comes at an expiry, so the last story can be no later than this
    /// deal's signing day. The refusal itself holds every time.
    /// </summary>
    public static void TurnDown(World world, Fighter f, Promotion from, Promotion suitor, DateOnly date)
    {
        if (f.Contract is not { } c || c.PoachWarned) return;
        c.PoachWarned = true;
        if (date.DayNumber - c.SignedOn.DayNumber < RefusalStoryGapDays) return;
        bool mine = from.Id == world.PlayerPromotion;
        string quote = f.Disposition == Disposition.CompanyMan ? " " + Flavor.Quotes.Attributed(f, Flavor.Moment.Signed, date) : "";
        if (Notable(world, f) || mine)
            Living.News.Post(world, date, NewsCategory.Signing,
                $"{f.FullName} turns down {suitor.Abbreviation}",
                $"{suitor.Name} came calling with the bigger stage; {f.FullName} is staying at {from.Abbreviation}.{quote}",
                from.Id, f.Id);
        if (mine)
            Living.Mail.Post(world, date, MailCategory.General, "Matchmaking",
                $"{f.FullName} turned down {suitor.Abbreviation}",
                Gendered(f, $"{suitor.Name} made a run at {f.FullName} and he told them no — he's ours. Keep him happy and the giants "
                + "can call all they like.") + quote,
                aboutFighter: f.Id);
    }

    // --- When a lifer does leave ---------------------------------------------------

    /// <summary>
    /// A fighter is leaving <paramref name="from"/> through <paramref name="door"/>
    /// (a plain-language account of how: "his deal ran out and VMMA couldn't pay
    /// him"). If he was a lifer — or a near-lifer with a standing grievance — it's
    /// one of the stories of the year: a prominent headline that says why, and a
    /// note to the player when it's their fighter or their signing. Call BEFORE
    /// the contract is cleared or replaced (the reading is of the bond he's
    /// leaving).
    /// </summary>
    public static void OnLeaving(World world, Fighter f, Promotion from, string door, DateOnly date, PromotionId? to = null,
        bool hisChoice = false)
    {
        if (f.Contract?.Promotion != from.Id) return;
        door = Gendered(f, door);
        int score = Score(world, f);
        if (score < Threshold - 10) return;   // the ordinary departure — cheap exit, no reading of the cards
        var g = GrievanceOf(world, f, date);
        if (score < Threshold && g is null) return;

        // Whose decision it was decides the headline: when HE walked (turned the
        // offer down, took the rival's), the grievance is the why; when the
        // promotion let him go (cut, let the deal run out, folded, sold), the
        // door is the story and the grievance is the background.
        string reason = hisChoice && g is not null ? g.Reason : door;
        string background = g is null ? "" : hisChoice ? $"How it ended: {door}." : Gendered(f, $"He'd already been unhappy: {g.Reason}.");
        string tenure = Tenure(world, f, from, date);
        var dest = to is { } t ? world.Get(t) : null;
        Living.News.Post(world, date, NewsCategory.Milestone,
            $"{f.FullName} leaves {from.Name}{tenure} — {reason}",
            (dest is not null ? $"The {from.Abbreviation} lifer signs with {dest.Name}. " : $"The {from.Abbreviation} lifer is gone. ")
            + background + Voice(f, hisChoice ? g?.Kind : null, date, leaving: true),
            from.Id, f.Id);

        if (from.Id == world.PlayerPromotion)
            Living.Mail.Post(world, date, MailCategory.BoardNote, Living.Mail.Owner(world),
                $"{f.FullName} has left us",
                $"A lifer walked out the door{tenure}: {reason}." + (background.Length > 0 ? " " + background : "")
                + " The fans will remember who let it happen.",
                aboutFighter: f.Id);
        else if (to == world.PlayerPromotion && world.PlayerPromotion != default)
            Living.Mail.Post(world, date, MailCategory.General, "Matchmaking",
                $"We signed {from.Abbreviation}'s lifer",
                $"{f.FullName} leaves {from.Name}{tenure} to join us — {reason}. That's a statement signing.",
                aboutFighter: f.Id);
    }

    /// <summary>
    /// A decision that breaks a lifer outright — the posters came off him, his
    /// camp stipend was cut. His loyalty drops below the bar (so fixing it means
    /// rebuilding it: a retention bonus, the posters back), and he says so.
    /// </summary>
    public static void BreakByDecision(World world, Fighter f, string reason, DateOnly date)
    {
        if (!IsLifer(world, f)) return;
        reason = Gendered(f, reason);
        var org = world.Get(f.Contract!.Promotion)!;
        int bonus = Score(world, f) - f.Loyalty;
        f.Loyalty = Math.Max(0, Math.Min(f.Loyalty - 10, Threshold - 1 - bonus));
        bool mine = org.Id == world.PlayerPromotion;
        if (Notable(world, f) || mine)
            Living.News.Post(world, date, NewsCategory.Milestone,
                $"{f.FullName}'s bond with {org.Abbreviation} is broken",
                $"The {org.Abbreviation} lifer is listening to offers: {reason}.", org.Id, f.Id);
        if (mine)
            Living.Mail.Post(world, date, MailCategory.BoardNote, Living.Mail.Owner(world),
                $"We've lost {f.FullName}'s loyalty",
                Gendered(f, $"{f.FullName} took it personally: {reason}. He was ours for life; now the next bigger promotion that calls "
                + "gets a hearing. A retention bonus can buy some of it back."),
                aboutFighter: f.Id);
    }

    // --- helpers --------------------------------------------------------------------

    // Worth a headline: a name, a belt, the face, or one of the authored careers.
    private static bool Notable(World world, Fighter f) =>
        f.Designed || f.Intangibles.Popularity.Value >= 45
        || (f.Contract is { } c && world.Get(c.Promotion)?.FaceOfCompany == f.Id)
        || world.Titles.Values.Any(t => t.CurrentChampion == f.Id);

    private static string Voice(Fighter f, GrievanceKind? kind, DateOnly date, bool leaving = false)
    {
        var moment = kind switch
        {
            GrievanceKind.PassedOver => Flavor.Moment.PassedOver,
            GrievanceKind.Underpaid => Flavor.Moment.Lowball,
            _ when leaving && f.Disposition == Disposition.CompanyMan => Flavor.Moment.Cut,
            _ => (Flavor.Moment?)null,
        };
        return moment is { } m ? " " + Flavor.Quotes.Attributed(f, m, date) : "";
    }

    private static bool RecentlyMailed(World world, Fighter f, DateOnly date, string subjectPart) =>
        world.Mail.Any(m => m.AboutFighter == f.Id && m.Subject.Contains(subjectPart)
                            && date.DayNumber - m.Date.DayNumber < 120);

    // " after 9 years" — the unbroken run of his fights for this promotion, read
    // off the cards (designed careers are back-dated into real events, so the
    // authored lifers' tenure is real too). Empty when it's too short to boast of.
    private static string Tenure(World world, Fighter f, Promotion from, DateOnly date)
    {
        var bouts = new List<(DateOnly Date, PromotionId Promo)>();
        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Completed) continue;
            foreach (var fight in ev.Card)
                if (fight.RedCorner == f.Id || fight.BlueCorner == f.Id) { bouts.Add((ev.Date, ev.Promotion)); break; }
        }
        bouts.Sort((a, b) => a.Date.CompareTo(b.Date));
        DateOnly? start = null;
        int fights = 0;
        for (int i = bouts.Count - 1; i >= 0 && bouts[i].Promo == from.Id; i--) { start = bouts[i].Date; fights++; }
        if (start is null) return "";
        int years = (date.DayNumber - start.Value.DayNumber) / 365;
        return years >= 2 ? $" after {years} years" : fights >= 4 ? $" after {fights} fights" : "";
    }
}
