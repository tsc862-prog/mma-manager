using MmaManager.Core.Model;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// Writes a completed bout back into the world: updates both fighters' records
/// and bout history, moves momentum/morale, advances contender rankings, and —
/// for title fights — swaps the belt and maintains the lineage. The one place a
/// fight's consequences land, so the booking loop just sims then calls this.
/// </summary>
public static class ResultsProcessor
{
    public static void Apply(World world, Event ev, Fight fight)
    {
        if (fight.Outcome is not { } outcome) return;

        var red = world.Get(fight.RedCorner);
        var blue = world.Get(fight.BlueCorner);
        if (red is null || blue is null) return;

        red.BoutHistory.Add(fight.Id);
        blue.BoutHistory.Add(fight.Id);
        DecrementContract(red);
        DecrementContract(blue);

        // Getting booked by your own org builds loyalty; winning and (especially) a
        // title shot build more. Neglect eats it back (see WorldTick's monthly decay).
        GrowLoyalty(red, ev, fight, outcome);
        GrowLoyalty(blue, ev, fight, outcome);

        // Draw or no-contest: no winner, gentle momentum decay toward neutral.
        if (outcome.Winner is null)
        {
            bool noContest = outcome.Method == FightMethod.NoContest;
            if (noContest) { red.Record.NoContests++; blue.Record.NoContests++; }
            else { red.Record.Draws++; blue.Record.Draws++; }
            red.Record.CurrentStreak = 0;
            blue.Record.CurrentStreak = 0;
            red.Momentum = Decay(red.Momentum);
            blue.Momentum = Decay(blue.Momentum);
            // No upset without a winner, so locals just fade back to the scene.
            if (red.LocalTalent) red.Status = FighterStatus.Retired;
            if (blue.LocalTalent) blue.Status = FighterStatus.Retired;
            return;
        }

        var winner = outcome.Winner == red.Id ? red : blue;
        var loser = winner == red ? blue : red;
        bool finish = IsFinish(outcome.Method);

        winner.Record.Wins++;
        ApplyWinMethod(winner.Record, outcome.Method);
        loser.Record.Losses++;
        winner.Record.CurrentStreak = winner.Record.CurrentStreak >= 0 ? winner.Record.CurrentStreak + 1 : 1;
        loser.Record.CurrentStreak = loser.Record.CurrentStreak <= 0 ? loser.Record.CurrentStreak - 1 : -1;

        winner.Momentum = Clamp(winner.Momentum + (finish ? 30 : 18));
        loser.Momentum = Clamp(loser.Momentum - (finish ? 30 : 18));
        winner.Morale = Math.Clamp(winner.Morale + 8, 0, 100);
        loser.Morale = Math.Clamp(loser.Morale - 12, 0, 100);

        // Winning grows a name — twice as fast for a marketable babyface. Squashing
        // a local impresses nobody: half credit, and none of it echoes very far.
        bool localBout = red.LocalTalent || blue.LocalTalent;
        // Winning grows a name, and a WIN STREAK grows it faster — a fighter on a
        // tear becomes appointment viewing (mirror of the losing-skid slide below).
        // A TITLE win pays fame in proportion to the BELT's own name: taking a
        // storied strap makes a star overnight; a club trinket barely registers.
        int winStreak = Math.Max(1, winner.Record.CurrentStreak);
        double winMult = 1 + 0.4 * Math.Min(winStreak - 1, 6);
        double titleFame = fight.IsTitleFight && fight.Title is { } tfid && world.Get(tfid) is { } belt
            ? 0.5 + belt.Prestige.Value / 30.0
            : 0;
        int popGain = (int)Math.Round(((finish ? 2 : 1) + titleFame) * winMult);
        if (winner.PersonaTag == "babyface") popGain *= 2;
        // A win is raw material; what you make of it depends on whether you can
        // talk. The ones who sell it turn a result into a story, the quiet ones
        // let the record speak — deliberately near neutral in aggregate, so this
        // redistributes fame rather than inflating it.
        popGain = winner.Disposition switch
        {
            Disposition.Loudmouth or Disposition.Showman => popGain + Math.Max(1, popGain / 4),
            Disposition.QuietProfessional or Disposition.Student => Math.Max(1, popGain - popGain / 5),
            _ => popGain,
        };
        popGain = Org.Franchise.AmplifyPopGain(winner, popGain);   // the camera loves a star
        if (loser.LocalTalent) popGain = Math.Max(1, popGain / 2);
        // Fame is bounded by the stage, twice over. The SCREEN: a main-card win
        // echoes as far as the broadcast platform carries it, and a prelim —
        // below the window — echoes at half volume. The REACH: popularity only
        // grows up to the promotion's fame ceiling (a Continental card can't
        // mint a global 100-pop superstar), lifted a little by a big media
        // partner. Fame earned at a bigger org is never stripped — the fighter
        // simply can't grow PAST the ceiling until they're on a bigger stage.
        bool prelim = ev.IsPrelim(fight);
        int cap = 100;
        if (world.Get(ev.Promotion) is { } hostOrg)
        {
            var size = Org.OrgSizing.SizeOf(hostOrg);
            if (prelim)
            {
                popGain = Math.Max(1, popGain / 2);
                cap = Org.OrgSizing.FameCeiling(size == OrgSize.City ? OrgSize.City : size - 1);
            }
            else
            {
                popGain = (int)Math.Round(popGain * Finance.MediaService.Exposure(hostOrg));
                cap = Math.Min(100, Org.OrgSizing.FameCeiling(size) + Finance.MediaService.FameCeilingBump(hostOrg));
            }
            cap = Math.Max(winner.Intangibles.Popularity.Value, cap);
        }
        else
        {
            // Nobody's show (the local circuit): a club win keeps a name warm
            // but can't build fame past the smallest stage's ceiling.
            cap = Math.Max(winner.Intangibles.Popularity.Value, Org.OrgSizing.FameCeiling(OrgSize.City));
        }
        winner.Intangibles.Popularity = Math.Min(cap, winner.Intangibles.Popularity.Value + popGain);

        // A defeat costs a fighter some shine. A single loss barely dents a rising
        // name, but it COMPOUNDS on a skid and cuts deeper the higher they stood —
        // so a former star who loses for years slides off the marquee instead of
        // coasting on old fame. Locals just fade out of the scene anyway.
        // A single loss roughly offsets a win (no world-wide fame drift), but it
        // COMPOUNDS on a skid and cuts deeper the higher they stood — so a former
        // star who loses for years slides off the marquee. Locals just fade anyway.
        if (!loser.LocalTalent)
        {
            int skid = Math.Max(1, -loser.Record.CurrentStreak);              // losses in a row, this one included
            double streakMult = 1 + 0.5 * Math.Min(skid - 1, 6);             // the longer the slide, the faster the fall
            double fameScale = 0.5 + loser.Intangibles.Popularity.Value / 100.0;   // the higher they stood, the further to fall
            int popLoss = (int)Math.Round((finish ? 2 : 1) * streakMult * fameScale);
            loser.Intangibles.Popularity = Math.Max(3, loser.Intangibles.Popularity.Value - popLoss);
        }

        // Both fighters get better known where they performed (echoes around the region).
        if (Geography.WorldMap.CityOf(world, ev) is { } host)
        {
            Geography.Reputation.BumpFighter(world, winner, host, localBout ? 2 : finish ? 6 : 4);
            Geography.Reputation.BumpFighter(world, loser, host, localBout ? 1 : 2);
        }

        // The local scene takes its people back. A local who UPSETS a real pro is
        // "discovered" (a prospect is born) — and one who wins an all-local bout
        // by STEALING THE SHOW (4+ stars) can be too, IF the show got noticed:
        // club slugfests between gasless brawlers are "exciting" constantly, so
        // excitement only opens the door — someone who matters has to be in the
        // room (a five-star war doubles the odds). Everyone else goes back to
        // the club scene.
        if (loser.LocalTalent) loser.Status = FighterStatus.Retired;
        if (winner.LocalTalent)
        {
            int rating = Flavor.MatchRating.Of(fight);
            bool noticed = rating >= Flavor.MatchRating.FourStar
                && new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)fight.Id.Value ^ 0x5EE0_57A2UL))
                    .Chance(rating >= Flavor.MatchRating.FiveStar ? 0.20 : 0.10);
            if (!loser.LocalTalent || noticed)
                LocalScene.Discover(world, winner, loser, ev, rating);
            else winner.Status = FighterStatus.Retired;   // ordinary local-on-local filler
        }

        var promo = world.Get(ev.Promotion);
        if (promo is null) return;
        // The division at stake: the TITLE's own division when a belt is up
        // (a champ-vs-champ superfight has corners from DIFFERENT weights, so
        // "the winner's division" would be the wrong room), else the winner's.
        var division = (fight.Title is { } forTitle ? promo.Divisions.FirstOrDefault(d => d.Title == forTitle) : null)
                       ?? RankingService.DivisionFor(promo, winner);

        // Non-title results no longer nudge rankings directly — contender order is
        // rebuilt from full résumés by RankingService.RecomputePromotion after the card.
        if (fight.IsTitleFight && fight.Title is { } titleId && world.Get(titleId) is { } title)
        {
            // The belt's own name follows the CREDIBILITY of the fighters who
            // contest it — records, streaks, standing in the company, fame — all
            // judged against what a belt at this stage demands (see
            // TitleCredibility). Every title fight pulls the belt's prestige
            // toward what the booking says about it: worthy contests build a
            // storied strap, paper matchups wear the shine off. (Slow — a belt's
            // name is its history, not its last booking. Computed before the
            // result lands so champion status/rankings read as of fight night.)
            double contest = TitleCredibility.Contest(world, promo, division, title, winner, loser);
            title.Prestige = (int)Math.Round(title.Prestige.Value + (contest - title.Prestige.Value) * 0.15);

            var prevChamp = title.CurrentChampion;
            ApplyTitleResult(title, division, winner.Id, loser.Id, ev.Date);

            // CHAMP-VS-CHAMP CONQUEST: the winner came UP a class for this belt.
            // They move up for good — the old division's belt passes to its top
            // contender behind them, and the sport has a two-division king.
            if (title.CurrentChampion == winner.Id && winner.WeightClass != title.WeightClass
                && world.Titles.Values.Any(t => t.CurrentChampion == winner.Id && t.Id != title.Id))
            {
                var oldClass = winner.WeightClass;
                winner.WeightClass = title.WeightClass;
                Living.Lifecycle.VacateTitlesExcept(world, winner, title.Id, ev.Date);
                winner.Accolades.Add(new Accolade(ev.Date,
                    $"Conquered a second division — moved up from {oldClass} to take the {title.Name}"));
                Living.News.Post(world, ev.Date, NewsCategory.Milestone,
                    $"TWO-DIVISION CHAMPION: {winner.FullName} conquers {title.WeightClass}",
                    $"Moved up from {oldClass} and took the belt — the {oldClass} title passes on behind them.", ev.Promotion, winner.Id);
                if (ev.Promotion == world.PlayerPromotion)
                    Living.Mail.Post(world, ev.Date, MailCategory.Milestone, Living.Mail.Owner(world),
                        $"History: {winner.FullName} is a two-division champion",
                        "Our champion moved up and took a second belt. Nights like this build a promotion's legend.",
                        aboutFighter: winner.Id);
            }

            if (title.CurrentChampion == winner.Id)
            {
                // A champion's clause tops the winner's deal up so they don't expire mid-reign.
                ContractLifecycle.ApplyChampionsClause(winner);
                if (prevChamp == winner.Id)
                    News.TitleDefended(world, title, winner, ev.Date);
                else if (prevChamp is { } pc && world.Get(pc) is { } old)
                {
                    News.TitleChanged(world, title, winner, old, ev.Date);
                    Mail.TitleWon(world, title, winner, ev.Date);
                }
                else   // the belt was vacant — a first champion is crowned
                {
                    News.TitleClaimed(world, title, winner, ev.Date);
                    Mail.TitleWon(world, title, winner, ev.Date);
                }
            }
        }
    }

    private static void ApplyTitleResult(Title title, Division? division, FighterId winner, FighterId loser, DateOnly date)
    {
        bool championRetained = title.CurrentChampion == winner;
        var currentReign = title.Lineage.LastOrDefault(r => r.LostOn is null);

        if (championRetained)
        {
            if (currentReign is not null) currentReign.Defenses++;
            return;
        }

        // New champion: close out the old reign, open a new one, swap the belt.
        if (currentReign is not null) currentReign.LostOn = date;
        title.CurrentChampion = winner;
        title.Lineage.Add(new TitleReign { Champion = winner, WonOn = date });

        // The new champ leaves the contender list; the dethroned champ enters it on top.
        if (division is not null)
        {
            RankingService.Remove(division, winner);
            RankingService.EnterAtTop(division, loser);
        }
    }

    private static void ApplyWinMethod(FightRecord rec, FightMethod method)
    {
        switch (method)
        {
            case FightMethod.Knockout or FightMethod.TechnicalKnockout:
                rec.WinsByKo++; break;
            case FightMethod.SubmissionTap or FightMethod.TechnicalSubmission:
                rec.WinsBySubmission++; break;
            case FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision:
                rec.WinsByDecision++; break;
            // Disqualification wins count toward the record but no finish bucket.
        }
    }

    private static void DecrementContract(Fighter f)
    {
        if (f.Contract is { } c && c.FightsRemaining > 0) c.FightsRemaining--;
    }

    // A fighter slowly warms to the org that keeps them busy and gives them the big
    // fights — deliberately gradual, so real attachment is the reward of YEARS of
    // service (a career fighter, not a one-contract hire). Neglect eats it back.
    private static void GrowLoyalty(Fighter f, Event ev, Fight fight, FightOutcome outcome)
    {
        if (f.LocalTalent || f.Contract?.Promotion != ev.Promotion) return;
        int gain = 1 + (fight.IsTitleFight ? 2 : 0);   // ~+1/fight; a title shot means more
        f.Loyalty = Math.Clamp(f.Loyalty + gain, 0, 100);
    }

    private static bool IsFinish(FightMethod m) => m is
        FightMethod.Knockout or FightMethod.TechnicalKnockout or
        FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;

    private static int Clamp(int momentum) => Math.Clamp(momentum, -100, 100);

    private static int Decay(int momentum) => (int)(momentum * 0.5);
}
